# Plan — The History Layer (cross-session memory + a self-cleaning database)

> Goal: do for **history** what v0.10–v0.12 did for **data** and the v0.19 rework
> did for **interpretation**. The data pipeline gave every number one home
> (`DataRegistry.Shared`); the insights engine gave every interpretation one home
> (`Insights/`). This plan gives **cross-session memory** one home: a *history
> layer* that answers questions across sessions, scoped to the modlist you are
> actually running, keyed on stable mod identity, and self-maintaining.
>
> The surprise this plan is built on: the persistence layer is **already rich on
> the write side** — ~24 LiteDB collections, a write-ahead journal, backup
> rotation, crash detection, a modlist fingerprint, and per-session per-mod /
> per-hook aggregates all land today. What is missing is the **read side**: there
> is no query that aggregates across sessions, the one cross-session read ignores
> the modlist, and the cross-session data is keyed on a fingerprint so brittle that
> adding a single mod resets it. The rework is to build the consuming half, fix the
> identity model, and add the lifecycle discipline — not to rebuild the store.
>
> Status: **DRAFT — not executed.** This is the directional design record for
> discussion, the same role `insights-engine.md` played before its eight waves.
> Date opened: 2026-06-25. Mod version at open: `0.26.0`.

---

## Why this exists

Three forces, verified against the source, not assumed.

**1. The data is captured; it is just never read back across sessions.** Every
per-session artefact is written richly and then queried only by a single
`SessionId` (or not at all). There is exactly one cross-session web read, and it
is a most-recent lookup, not an aggregation.

| Cross-session capability | Reality today | Evidence |
|---|---|---|
| Aggregate over the last N sessions | **does not exist** — no `GROUP BY mod` across sessions anywhere | every `.Find` outside `CrossSessionStore` filters `x.SessionId == oneId` (`SessionSummaryLogger.cs:42-59`, `QueryChatCommands.cs:41-183`, `InteractionInsightDetectors.cs:51-194`) |
| "Reading from db" idle view | most-recent **ended** session only, **no modlist filter** | `DbReadModel.GetLastSession()` (`DbReadModel.cs:41`) scans `Sessions.Find(x => x.EndedUtc != null)` for the max `StartedUtc` (`:55`) — no `ModlistFingerprint` clause |
| Genuinely persisted across sessions | only per-(context,mod) **cost distributions** (Welford count/mean/M2) | `CrossSessionStore.Load/Save` (`CrossSessionStore.cs:36,65`), `ContextBaselineRow` |
| Per-mod / per-hook session aggregates | **written, never read** | `PerSessionModAggregate` / `PerSessionHookAggregate` produced at session end (`SessionRecorder.End()`), zero cross-session readers |
| Spike / stall / segment / event rows | **written, never aggregated** across sessions | all event collections queried per-`SessionId` only |
| The `insights` collection | **writer exists, zero producer** | `InsightStream` applies/reconstructs `DbWriteOp.Insight`, but nothing enqueues it (confirmed by grep; the LifetimeData scope badge can never be earned) |

So the user's three motivating questions are all answerable from data we *already
store* — and all impossible today purely for want of a query layer:

- *"the last 3 sessions you haven't used this mod once"* → `PerSessionModAggregate`
  active-use across the last 3 sessions containing the mod. Data exists; no query.
- *"over the last 5 sessions, mod X caused the most lag spikes"* →
  `SpikeWindowRow.TopContributors` grouped across the last 5 sessions. Data exists;
  no query.
- *"over 4 sessions, mod Y was costly despite being in the least-used 25%"* → join
  per-mod cost rank against usage rank across 4 sessions. Both columns exist; no query.

**2. The cross-session identity is brittle, so per-mod history can't survive a
modlist edit.** The only cross-session key is the **modlist fingerprint** —
`SHA256` of `index:name@version` for *every* mod in load order
(`ModlistFingerprint.Compute`, the algorithm tag is `sha256-of-sorted-id-name-version-v1`).
Add one mod, remove one mod, reorder, or update any mod → a brand-new fingerprint,
and every `ContextBaselineRow` for the old fingerprint is orphaned (a fresh
baseline starts from zero, `CrossSessionStore.Load` finds no rows). The stable
key the system *needs* — the mod's internal name — is already stored in
`PerSessionModAggregate.ModInternalName` (`:29`) and `ModRow.InternalName`, but the
cross-session table (`ContextBaselineRow`) keys on `Fingerprint + Dim + Key + ModId`
with **no internal name** (`ContextBaselineRow.cs:27`), so "this mod across my
modlists" is structurally impossible. `ModId` itself is a session-local load-order
index, not a stable identity.

**3. Nothing is self-cleaning, and nothing is bounded.** There is no modlist-change
detection, no modlist-scoped read, no session retention, and unbounded growth.

| Lifecycle concern | Current handling | Evidence |
|---|---|---|
| Modlist change (mods added/removed) | **none** — removed mods linger and leak into the idle UI | `GetLastSession` has no fingerprint filter; no change-detection log on world load |
| Mod update (same mod, new version) | recorded, **never acted on** — pre/post-update costs pool into one baseline | `ModRow.VersionHistory` dedupes versions; `ContextBaselineRow` has no version field |
| Retention / growth | only the 24 h warm tier expires; everything else grows forever | `TickAggregatesWarm.DeleteMany(ExpireAtUtc < now)` (`ProfilerDatabase.cs:453`) is the only prune; `Compact()` is a manual chat command (`ProfilerCompactCommand.cs:52`) |
| Corruption | backup ring + crash-flag + journal replay, but **no integrity check / no backup validation** | `RecoverIfNeeded` probes by opening read-only; `RotateBackups` copies without verifying; no `integrity_check` |

What the rework must **keep** (the write side is good engineering): the
non-blocking writer thread + unbounded channel, the journal-before-db durability
order, the tiered warm/cold/archive downsampling, the per-session aggregates, the
row pools, the fingerprint (repurposed, not removed).

---

## The spine: the relativity law, extended through time

> **Every cross-session number is a query over persisted per-session aggregates,
> keyed on stable mod identity, scoped to the modlist you are running now. History
> is never recomputed from raw events on read, and never polluted by mods you no
> longer run. A mod's lifetime is a continuous series across modlist edits; the
> modlist fingerprint is a context tag on that series, not its identity.**

This mirrors the insights engine's relativity law ("no insight is absolute; every
insight is a deviation from a comparable baseline"). Here the baseline frame is
*the mod's own past*, and the law that keeps it honest is: identity is the mod, not
the modlist; scope is the modlist, not the identity. The two are decoupled so that
"CalamityMod over my last 5 sessions" survives you adding a sixth mod, while "what
does my current stack cost" excludes the mod you uninstalled.

---

## The architecture

Five pieces, foundation-first. Each is the precondition for the next.

### 1. Stable mod identity (the foundation)

- Promote **`InternalName` (+ version)** to the cross-session key everywhere
  history is keyed. `ModId` stays the per-session hot-path index (zero-alloc array
  index, Invariant 2); it is resolved to `InternalName` once, at the session-end
  rollup, never on the hot path.
- The **fingerprint is demoted** from "the cross-session identity" to "a context
  tag + a membership set". It still answers "is this the same exact stack" and
  scopes the *current-modlist view*, but per-mod history no longer hangs off it.
- Record the **mod version** on the per-mod history so a version boundary is a
  first-class, detectable event (feeds the problematic→fixed signal in piece 5).
- Invariant 5 holds by construction: `InternalName` is a generic tModLoader surface
  (`Mod.Name`), never a hard-coded mod string; the history layer keys on whatever
  name the loader reports.

### 2. The per-mod lifetime rollup (the queryable substrate)

The cross-session analog of the per-session aggregate, and the thing that makes
"last 5 sessions" cheap as history grows.

- A new collection, keyed by `InternalName`, holding **running stats** (the same
  Welford count/mean/M2 `ContextBaseline` already round-trips) for cost / active-use
  / spike-attribution / stall-attribution / alloc, plus a **bounded ring of the
  last N per-session summaries** for that mod (cost, usage, spike count, version,
  fingerprint, ended-at).
- Maintained **at session end** by folding that session's `PerSessionModAggregate`
  into the rollup — O(mods) once per session, off the hot path, the same place the
  per-session aggregates are already built.
- This bounds growth (the rollup is fixed-size per mod; the raw per-session rows
  become prunable, see piece 4) and makes the user's queries O(N sessions in the
  ring), not O(all rows ever).
- Version-stratified: the ring carries the version per session, so a baseline that
  spans a version change can be flagged or split rather than silently pooled.

### 3. The HistoryStore (the read layer — the missing half)

A read-model parallel to `DbReadModel`, but cross-session and modlist-aware. The
single home every cross-session consumer reads through (dashboard + insights +
chat commands stop hand-rolling per-session `.Find` loops).

- `ModHistory(internalName, lastN)` → the per-mod ring + rollup: cost/usage trend,
  spike attribution count, version timeline.
- `RecentSessions(n, scope)` → last N sessions, `scope` ∈ {currentModlist,
  containingMod(name), all}. Replaces `GetLastSession` (which becomes
  `RecentSessions(1, currentModlist)`).
- `RankAcrossSessions(metric, lastN, scope)` → the aggregation primitive behind
  "most spikes over 5 sessions" / "costliest despite low usage": group the
  per-session aggregates / spike contributors by `InternalName` across the scoped
  session set.
- Every method is **modlist-scoped by default** (current stack's members), with an
  explicit opt-out for "across all my modlists" — this is the self-cleaning made
  structural: removed mods are simply not in the current scope, so they never
  clutter, without deleting their history.
- Read-only, fully guarded (Invariant 1, Invariant 4): any query/shape failure
  returns empty and the surface stays in its honest empty state, exactly as
  `DbReadModel` already does.

### 4. Self-cleaning lifecycle (scoping, retention, version, corruption)

- **Modlist-change detection** on world load: diff the current mod-set against the
  last session's; surface "modlist changed: +3 / −2" to both surfaces (a dashboard
  badge; a `client.log` line). The current-modlist scope (piece 3) then does the
  cleaning automatically.
- **One DB, not per-modlist DBs** (see Decisions): removed-mod data is *excluded
  from current-modlist views*, not deleted, so per-mod cross-modlist history
  survives. Optional explicit "forget modlist X" prune for the user who wants it.
- **Retention as a session tier**, extending the existing warm/cold/archive idea up
  one level: recent sessions keep full detail; older sessions keep only their
  archive + are folded into the per-mod rollup; ancient raw events prune on a cap
  (last N sessions or older-than-X). Auto-`Compact()` on a cadence instead of only
  by hand.
- **Mod-version boundary**: a version change marks the rollup so a problematic→fixed
  improvement reads as a *step in the series at the version boundary*, not as
  variance pooled into one average — and the UI, reading the series, never breaks
  when a mod stops being a problem.
- **Corruption hardening** (extends, not replaces, today's recovery): an integrity
  check on open, validate a backup before promoting it, and an honest "store reset"
  state surfaced to the player rather than a silent partial read.

### 5. Cross-session insights (the consumer — closes the orphaned collection)

This is where the history layer pays off on the surface we just built (the Insights
kanban) and where the dormant `LifetimeData` badge finally lights up.

- **Wire the `insights` producer**: at session end, persist the session's top
  insights via `DbWriteOp.Insight` (the writer already exists). Closes the
  writer-without-producer gap.
- **A cross-session detector family** in `Insights/` fed by the HistoryStore,
  emitting exactly the user's examples, each badged `EvidenceScope.LifetimeData`:
  - "unused in your last 3 sessions" (ring active-use all zero),
  - "top spike contributor across your last 5 sessions" (RankAcrossSessions on
    spike attribution),
  - "costly relative to its usage rank over 4 sessions" (cost rank vs usage rank),
  - "improved at version X→Y" (the version-boundary step).
- These slot straight into the kanban under a `cross-session` (or `lifetime`)
  family/column and finally populate the lifetime/needs-persistence scope badges
  that are wired but unearned today.

---

## Decisions & alternatives

- **One DB with modlist-scoped views, over a separate DB file per modlist (the
  user's "separate db" idea).** Per-modlist DB files would cleanly isolate clutter,
  but they *fragment per-mod history* — the moment you add a mod you get a new file
  and "haven't used this mod in 3 sessions" resets, which is the opposite of the
  goal. One DB + identity-keyed history + scoped reads gives the isolation (removed
  mods drop out of the current view) without the fragmentation. The fingerprint
  becomes the scope tag that does the isolation. *Recommended.*
- **Stable identity = `InternalName`, over `ModId` or the fingerprint.** `ModId` is
  load-order-local; the fingerprint is whole-stack-brittle. `InternalName` is the
  generic, stable, already-stored key. Collisions across truly different mods are a
  non-issue (tModLoader names are unique); a name reused by a different author is an
  accepted, rare edge.
- **Rollup substrate, over re-aggregating raw rows on read.** Re-scanning all
  per-session rows per query is simple but O(history) and unbounded; the Welford
  rollup + bounded ring is the pattern `ContextBaseline` already proves and bounds
  both read cost and storage.
- **Keep the write layer; build the read layer.** Tempting to "rewrite the DB"; the
  evidence says the write side is sound and the gap is entirely consume-side +
  lifecycle. Rewriting what works would be motion, not progress.

---

## Phases (waves)

Ordered foundation-first; each leaves the mod shippable and is independently
verifiable (compile gate + the pure-logic suite for the query/rollup math, which is
all testable off the game thread against synthetic session rows).

0. **Contracts + identity.** Freeze the rollup row + HistoryStore interface; add
   `InternalName`/version to the cross-session keys; demote the fingerprint to a
   scope tag. No behaviour change yet.
1. **Rollup substrate.** Build + maintain the per-mod lifetime rollup at session
   end; backfill from existing `PerSessionModAggregate` rows on first open.
2. **HistoryStore read layer.** Implement the cross-session queries; reroute
   `GetLastSession` + the chat commands through it; make every read modlist-scoped.
3. **Self-cleaning lifecycle.** Modlist-change detection + scope badges; session
   retention tier + auto-compact; version-boundary marking; corruption hardening.
4. **Cross-session insights.** Wire the `insights` producer; add the cross-session
   detector family; light up the `LifetimeData` badge in the kanban.
5. **Dashboard cross-session views.** Per-mod history sparkline/trend in the
   Observatory drawer; a "last N sessions" lens; the modlist-changed banner.

---

## Risks & invariants

- **Invariant 1 (read-only):** the history layer is read + persistence only; no
  game-state mutation. Retention deletes only the profiler's own rows.
- **Invariant 2 (overhead budget):** identity resolution + rollup folding happen at
  session end, never per tick; the hot path keeps using `ModId` array indices. The
  rollup write is one batch at teardown.
- **Invariant 3 (honesty):** cross-session findings carry the same effect-size +
  data-strength badging; `LifetimeData` is only claimed when the ring genuinely
  spans sessions; a version-spanning baseline is flagged, not silently averaged.
- **Invariant 4 (abort-clean):** every HistoryStore read is guarded to empty;
  corruption recovery prefers an honest reset over a partial answer.
- **Invariant 5 (no mod-specific code):** identity is `Mod.Name`, never a hard-coded
  mod string; the cross-session detectors read generic per-mod series.
- **Risk — name instability:** a mod that renames itself between versions splits its
  own history. Mitigation: the version timeline makes the rename visible; accepted
  as a rare edge, surfaced rather than hidden.
- **Risk — backfill cost:** computing the rollup from a large existing store on
  first open could be slow. Mitigation: backfill off-thread, bounded, resumable.

---

## What this defers (closed decisions, not oversights)

- **No raw-event cross-session queries.** The rollup + archive answer the planned
  questions; aggregating raw spike/death/damage rows across sessions is out of scope
  until a concrete consumer needs it (build the seam at the second consumer).
- **No multi-machine / cloud sync.** History is per-install; the fingerprint already
  carries a machine component for when that changes.
- **No JSON-lines session export.** CLAUDE.md references an agent-readable JSONL
  surface; the current agent surface is the `client.log` session-summary block.
  Reviving JSONL is a separate, additive piece, not part of the history layer.
