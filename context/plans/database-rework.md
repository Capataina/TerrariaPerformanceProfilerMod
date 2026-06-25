# Plan — The History & Memory Layer (cross-session intelligence, unified with the Insights engine)

> Goal: do for **memory** what v0.10–v0.12 did for **data** and v0.19 did for
> **interpretation** — but this time the two halves are explicitly one system. The
> database is the profiler's **cross-session memory**; the insights engine is the
> **reasoning over that memory**. Today they are separate (the engine reasons only
> over the live session; the DB writes per-session and never reads back across
> sessions). This plan joins them: persisted history becomes the substrate the
> engine reasons over, so a finding can say "across your last 5 sessions…" and
> "in both modlist A and B…", and the `LifetimeData` badge — wired but never earned
> — finally lights up in the Insights kanban.
>
> The surprise this plan rests on: the persistence **write** side is already rich
> — ~24 LiteDB collections, a write-ahead journal, backup rotation, crash
> detection, the modlist fingerprint, per-session per-mod / per-hook aggregates.
> What is missing is the **read** side (no query aggregates across sessions), a
> **stable identity** (cross-session data hangs off a fingerprint so brittle that
> adding one mod resets it), and a **self-cleaning lifecycle**. The rework builds
> the consuming half, fixes identity, and adds the lifecycle — it does not rebuild
> the store.
>
> Status: **DRAFT — refined after discussion, ready to execute the core.** The core
> waves (0–5) are agreed; the "next level" capabilities are a marked follow-on
> roadmap. Date opened: 2026-06-25. Last refined: 2026-06-25. Mod version: `0.26.0`.

---

## Why this exists

Three forces, verified against source, not assumed.

**1. The data is captured; it is just never read back across sessions.** Every
per-session artefact is written richly and then queried only by a single
`SessionId` (or not at all). There is exactly one cross-session web read, and it is
a most-recent lookup, not an aggregation.

| Cross-session capability | Reality today | Evidence |
|---|---|---|
| Aggregate over the last N sessions | **does not exist** — no `GROUP BY mod` across sessions | every `.Find` outside `CrossSessionStore` filters `SessionId == oneId` (`SessionSummaryLogger.cs:42-59`, `QueryChatCommands.cs:41-183`, `InteractionInsightDetectors.cs:51-194`) |
| "Reading from db" idle view | most-recent **ended** session, **no modlist filter** | `DbReadModel.GetLastSession()` (`DbReadModel.cs:41`) scans `Sessions.Find(EndedUtc != null)` for max `StartedUtc` (`:55`); no fingerprint clause |
| Genuinely persisted across sessions | only per-(context,mod) **cost distributions** (Welford count/mean/M2) | `CrossSessionStore.Load/Save` (`CrossSessionStore.cs:36,65`), `ContextBaselineRow` |
| Per-mod / per-hook session aggregates | **written, never read** | `PerSessionModAggregate` / `PerSessionHookAggregate` built at session end, zero cross-session readers |
| The `insights` collection | **writer exists, zero producer** | `InsightStream` applies/reconstructs `DbWriteOp.Insight`, nothing enqueues it → `LifetimeData` can never be earned |

The three motivating questions are all answerable from data we *already store*, and
all impossible today purely for want of a query layer: *"unused in your last 3
sessions"* → `PerSessionModAggregate` active-use across 3; *"most lag spikes over
5 sessions"* → `SpikeWindowRow.TopContributors` grouped across 5; *"costly despite
low usage over 4"* → cost-rank vs usage-rank join. The columns all exist.

**2. The cross-session identity is brittle, so per-mod history can't survive a
modlist edit.** The only cross-session key is the **modlist fingerprint** —
`SHA256` of `index:name@version` for *every* mod in load order
(`ModlistFingerprint.Compute`). Add one mod, remove one, reorder, or update any →
a new fingerprint, and every `ContextBaselineRow` for the old one is orphaned
(`CrossSessionStore.Load` finds nothing, baseline restarts at zero). **This is the
exact wound the user named: adding a mod mid-playthrough resets the history of a
playthrough that should have continued.** The stable key the system needs — the
mod's internal name — is already stored in `PerSessionModAggregate.ModInternalName`
(`:29`) and `ModRow.InternalName`, but the cross-session table keys on
`Fingerprint + Dim + Key + ModId` with **no internal name** (`ContextBaselineRow.cs:27`).
`ModId` itself is a session-local load-order index, not a stable identity.

**3. Nothing is self-cleaning, and nothing is bounded.**

| Lifecycle concern | Current handling | Evidence |
|---|---|---|
| Modlist change | **none** — removed mods linger and leak into the idle UI | `GetLastSession` has no fingerprint filter; no change detection on world load |
| Mod update (new version) | recorded, **never acted on** — pre/post-update costs pool into one baseline | `ModRow.VersionHistory` dedupes versions; `ContextBaselineRow` has no version field |
| Retention / growth | only the 24 h warm tier expires; everything else grows forever | `TickAggregatesWarm.DeleteMany(ExpireAtUtc < now)` (`ProfilerDatabase.cs:453`); `Compact()` is a manual chat command |
| Corruption | backup ring + crash-flag + journal replay, but **no integrity check / no backup validation** | `RecoverIfNeeded` probes by read-only open; `RotateBackups` copies without verifying |

What the rework **keeps** (the write side is good engineering): the non-blocking
writer thread + unbounded channel, journal-before-db durability order, the tiered
warm/cold/archive downsampling, the per-session aggregates, the row pools, the
fingerprint (repurposed, not removed), the crash-quarantine recovery.

---

## The spine

> **History is the mod's own past. Every cross-session number is a query over
> persisted per-session aggregates, keyed on stable mod identity (`InternalName`),
> never recomputed from raw events on read. The modlist is a *dimension of
> analysis*, never the identity and never a partition: a mod's lifetime is one
> continuous series across modlist edits, and the differences *between* modlists are
> themselves signal (cross-modpack analysis). The profiler never silently destroys
> a player's data — resets are the player's choice, not the mod's.**

This extends the insights engine's relativity law ("no insight is absolute; every
insight is a deviation from a comparable baseline") with the baseline frame being
*the mod's own history*, and adds the identity/scope/ownership rules the cross-
session world needs. The two halves — memory and reasoning — share this spine.

---

## The unification: the history layer and the Insights engine are one system

This plan and the v0.19 insights engine are not two projects; they are the
producer and consumer of the same cross-session memory.

```
   measurement            MEMORY (this plan)                 REASONING (Insights engine)
   per-tick collectors  →  per-session aggregates  ─┐
                           per-mod lifetime rollup   ├─→  HistoryStore  ─→  cross-session detectors
                           per-modlist breakdown    ─┘    (read layer)      cross-modpack detectors
                                                                                  │
                                                                                  ▼
                                                          Insight records (EvidenceScope=LifetimeData)
                                                                                  │
                                                                                  ▼
                                                          the Insights kanban (the surface we just built)
                                                          + persisted back via the insight producer
```

The consequences of treating them as one:
- The engine's confidence ladder (Preliminary → High), today meaningless because it
  resets every session, becomes real: confirmation accrues across sessions, so
  "High" means "confirmed across N sessions" and is *persisted* via the insight
  producer that closes the orphaned `insights` collection.
- The kanban families (`cost deviation` / `temporal drift` / …) gain two new
  columns — `cross-session` and `cross-modpack` — populated by detectors that read
  the HistoryStore. The `LifetimeData` / `needs persistence` scope badges, wired but
  unearned, finally carry weight.
- The Observatory mod-context drawer grows into the per-mod **profile / character
  sheet** (see the roadmap) — the lifetime view of a mod, reasoned by the engine,
  stored by the history layer.

---

## Locked decisions (from discussion)

| # | Decision | Choice | Why |
|---|---|---|---|
| A | Scope unit for "current view" | **mod-membership overlap**, not fingerprint-equality | a session is relevant if it *contains* the mod(s) in question; adding a mod mid-playthrough never hides prior sessions, removed mods simply drop from the current set |
| B | Rollup shape | **two-level**: global per-mod lifetime **+** per-modlist breakdown | global answers "unused in 3 sessions" / overall trend; the per-modlist level answers "bottom 10% in both A and B" and powers cross-modpack analysis |
| C | Cross-modpack grouping | by **fingerprint**, gated on **≥2 distinct stacks, each with ≥N well-sampled sessions** | the fingerprint is the modpack key; the gate stops weekly tweak-variants from fragmenting into noise |
| D | Retention vs no-forced-reset | **compaction, never deletion of meaning** | raw per-tick/per-event detail of old sessions ages out (existing tiering); the per-mod lifetime rollups are **permanent** (tiny); the mod never auto-deletes a player's history |
| E | Reset control | **discreet top-bar button + confirm dialog**, two scopes: "reset everything" / "forget this modlist" | resets are intentional and the player's; never forced. Corruption recovery stays quarantine-not-delete (recovering already-lost data ≠ resetting good data) |

---

## The architecture — the core history layer

Five pieces, foundation-first; each is the precondition for the next.

### 1. Stable mod identity (the foundation)

- Promote **`InternalName` (+ version)** to the cross-session key everywhere history
  is keyed. `ModId` stays the per-session hot-path array index (Invariant 2,
  zero-alloc); it is resolved to `InternalName` once, at the session-end rollup,
  never on the hot path.
- **Demote the fingerprint as identity, promote it as a dimension.** It no longer
  keys per-mod history; instead it tags each session and groups the per-modlist
  breakdown (B) and the cross-modpack detectors (C). Same value, better job.
- Record the **mod version** on the per-mod history so a version boundary is a
  first-class, detectable event (feeds regression tracking on the roadmap).
- Invariant 5 holds by construction: `InternalName` is `Mod.Name`, a generic loader
  surface, never a hard-coded mod string.
- **Rework `ContextBaseline` to key on `(InternalName, Dim, Key)` with fingerprint
  as a dimension**, so the per-context cost distributions also carry forward across
  modlist edits and become cross-modpack-comparable (today they reset).

### 2. Two-level per-mod rollup (the queryable substrate)

The cross-session analog of the per-session aggregate, and the thing that makes
"last 5 sessions" cheap and "bottom 10% in both A and B" possible.

- **Global level** — one row per `InternalName`: Welford running stats (the pattern
  `ContextBaseline` already round-trips) for cost / active-use / spike-attribution /
  stall-attribution / alloc, plus a **bounded ring of the last N per-session
  summaries** (cost, usage, spike count, version, fingerprint, ended-at).
- **Per-modlist level** — one row per `(InternalName, fingerprint)`: the same stats
  scoped to that stack, so a mod's rank/percentile *within each modlist it has been
  in* is recoverable. This is the cross-modpack substrate.
- Maintained **at session end** by folding that session's `PerSessionModAggregate`
  into both levels — O(mods) once per session, off the hot path. Backfilled from
  existing rows on first open (bounded, off-thread, resumable).
- Version-stratified: the ring carries the version per session, so a baseline that
  spans a version change is flagged, not silently pooled.

### 3. The HistoryStore (the read layer — the missing half)

A read-model parallel to `DbReadModel`, cross-session and mod-membership-scoped.
The single home every cross-session consumer reads through (dashboard, insight
detectors, chat commands stop hand-rolling per-session `.Find` loops).

- `ModHistory(internalName, lastN)` → the per-mod ring + rollup: cost/usage trend,
  spike attribution, version timeline, per-modlist breakdown.
- `RecentSessions(n, scope)` → last N sessions, `scope ∈ {currentModlist,
  containingMod(name), all}`. Replaces `GetLastSession` (= `RecentSessions(1,
  currentModlist)`).
- `RankAcrossSessions(metric, lastN, scope)` → the aggregation primitive behind
  "most spikes over 5 sessions" / "costly despite low usage": group per-session
  aggregates / spike contributors by `InternalName` across the scoped session set.
- **Scope is mod-membership overlap (decision A)**, not exact fingerprint: a per-mod
  query pulls every session that contained that mod; the current-stack overview
  shows each mod's history from whenever it appeared. This is the self-cleaning made
  structural — removed mods are absent from the current scope, present mods continue
  uninterrupted across modlist edits.
- Read-only, fully guarded (Invariants 1 & 4): any query/shape failure returns empty
  and the surface stays in its honest empty state, as `DbReadModel` already does.

### 4. Self-cleaning lifecycle (scoping, retention, version, corruption, reset)

- **Modlist-change detection** on world load: diff the current mod-set against the
  last session's; surface "modlist changed: +3 / −2" on both surfaces (a dashboard
  badge; a `client.log` line). The mod-membership scope (piece 3) then does the
  cleaning automatically — no reset.
- **One DB, not per-modlist DBs** (Decisions): removed-mod data is *excluded from
  current-modlist views*, never deleted, so per-mod cross-modlist history survives.
- **Retention as compaction (decision D)**: a session tier extending warm/cold/
  archive up one level — recent sessions keep full detail; older sessions keep their
  archive + their fold into the rollup; ancient raw events compact on a generous cap.
  The per-mod lifetime rollups are **permanent**. Auto-`Compact()` on a cadence to
  reclaim space (never to delete meaning).
- **Mod-version boundary**: a version change marks the rollup, so a problematic→fixed
  improvement reads as a *step in the series at the boundary*, and the UI — reading
  the series — never breaks when a mod stops being a problem.
- **Corruption hardening** (extends today's recovery): integrity check on open,
  validate a backup before promoting it, an honest "store reset" state surfaced to
  the player; the broken file is quarantined, never deleted.
- **The reset control (decision E)**: a discreet top-bar button (an icon / tucked
  menu, not a fat-fingerable button) with a confirm dialog, offering "reset
  everything" and "forget this modlist". The only path that deletes player data, and
  it is always the player's choice.

### 5. Cross-session + cross-modpack insights (the consumer — the unification made concrete)

Where the memory pays off on the surface we built, and where the dormant badges
light up.

- **Wire the `insights` producer**: at session end, persist the session's top
  insights via `DbWriteOp.Insight` (writer already exists). Closes the
  writer-without-producer gap and lets confidence accrue across sessions.
- **A cross-session detector family** fed by the HistoryStore, each finding badged
  `EvidenceScope.LifetimeData`:
  - "unused in your last 3 sessions" (ring active-use all zero),
  - "top spike contributor across your last 5 sessions" (`RankAcrossSessions`),
  - "costly relative to its usage rank over 4 sessions".
- **A cross-modpack detector family** (decision C) reading the per-modlist level:
  - "ranks bottom-10% in every modlist it has been in" (stack-independent signal),
  - "cheap alone but costly in modpack B" (an interaction finding only visible
    across modpacks).
- These slot into new `cross-session` and `cross-modpack` columns in the kanban and
  populate the lifetime / needs-persistence scope badges.

---

## The next level — follow-on capability roadmap (post-core)

These are what the substrate *unlocks*, layered **after** the core works and the
build-reload-play-restart-play test passes — never speculative cleverness on an
unproven base. Ranked within each axis; the starred ✪ three are the highest
novelty-per-effort and ride the substrate most directly.

### Axis 1 — Time (a mod / stack against its own past)

- **✪ Mod-update regression tracking** — "ThoriumMod ran 1.8× slower after its
  update 1.6.2 → 1.6.3". Each update is a natural before/after experiment; the
  version-stratified rollup makes it almost free. We would be the only tool that
  catches a perf regression in a mod update. Stack-independent, genuinely
  actionable. *Honesty: states the measured delta at the version boundary, never
  "downgrade this mod".*
- **Stack drift / forecast** — "your modlist's frame time has crept up 15% over your
  last 10 sessions". Catches slow creep no single session shows. Backed by the
  session archive series.
- **Playthrough arc (per world)** — "across your 12 sessions on this world, cost grew
  as you reached hardmode; the biggest new cost is X, which appeared at hardmode".
  A playthrough (sessions chained by `SessionRow.WorldId`) is a more meaningful unit
  than a session for narrative insights.

### Axis 2 — Comparison (a mod / session against others)

- **Cross-modpack interaction detection** — "mod X is cheap in every modlist except
  the one that also has mod Y". The cross-session sibling of the within-session
  mod-pair chord. Correlation not causation → badged suggestive, gated by decision C.
- **Session anomaly / today-vs-typical** — "today was unusually laggy: 20% more
  spikes, and CalamityMod ran 2× its usual cost for this stack". Cheap on top of the
  rollup (z-score / Welch of the session against the lifetime distribution).

### Axis 3 — Causation (a mod's cost explained, not just measured)

- **✪ Per-mod conditional profile across sessions** — "across your sessions,
  ThoriumMod's cost concentrates in hardmode jungle; it's near-free elsewhere". The
  descriptive→actionable jump: "expensive" is useless, "expensive *during
  invasions*" tells you when it bites. Backed by the reworked `ContextBaseline`
  (per-context per-mod, now identity-keyed) + segments accumulated cross-session.

### The unifier — per-mod profile / "character sheet" ✪

Every facet above is one view of one entity: each mod accumulates a durable,
cross-modlist **behavioural profile** — typical cost and cost-shape, the contexts
that drive it, its version timeline with regressions flagged, its rank across the
modpacks it has appeared in. The Observatory mod-context drawer we just built is the
*seed*; it grows into a full lifetime character sheet reachable from anywhere (click
a mod → its whole story, not just this session). This gives the expansions one
coherent home instead of seven scattered features, and it is the natural surface for
the data-strength the badges promise.

### Meta — making the data trustworthy

- **Confidence that matures across sessions.** Persisting confirmation counts via the
  insight producer makes "confirmed across 8 sessions" a real basis for High
  confidence — the cross-session substrate is what finally gives the ladder meaning.
- **A data-health view.** Sessions / modlists / mods tracked, evidence backing each
  mod, when retention last compacted. Makes the `this session` vs `lifetime data`
  badges legible, and is the natural home for the reset control.

---

## Decisions & alternatives

- **One DB with mod-membership-scoped views, over a separate DB file per modlist
  (the user's first instinct).** Per-modlist files cleanly isolate clutter but
  *fragment per-mod history* — add a mod and "unused in 3 sessions" resets, the
  opposite of the goal. One DB + identity-keyed history + scoped reads gives the
  isolation without the fragmentation, and it is the precondition for cross-modpack
  analysis (you cannot compare across modlists you have partitioned apart).
  *Recommended; this is the spine.*
- **Identity = `InternalName`, over `ModId` or the fingerprint.** `ModId` is
  load-order-local; the fingerprint is whole-stack-brittle; `InternalName` is the
  generic, stable, already-stored key. A name reused by a different author is an
  accepted rare edge, made visible by the version timeline.
- **Rollup substrate, over re-aggregating raw rows on read.** Re-scanning all
  per-session rows per query is O(history) and unbounded; the Welford rollup +
  bounded ring bounds both read cost and storage (the pattern `ContextBaseline`
  proves).
- **Keep the write layer; build the read layer.** The evidence says the write side
  is sound and the gap is entirely consume-side + lifecycle. Rewriting what works
  would be motion, not progress.

---

## Phases (waves)

Core, foundation-first; each leaves the mod shippable and is independently
verifiable (compile gate + the pure-logic suite — the query / rollup / ranking math
is all testable off the game thread against synthetic session rows).

0. **Contracts + identity.** Freeze the two-level rollup rows + the HistoryStore
   interface; add `InternalName`/version to the cross-session keys; rework
   `ContextBaseline` to identity-key with fingerprint as a dimension; demote the
   fingerprint to a scope/analysis tag. No behaviour change yet.
1. **Rollup substrate.** Build + maintain the global + per-modlist rollup at session
   end; backfill from existing `PerSessionModAggregate` rows on first open.
2. **HistoryStore read layer.** Implement the cross-session queries; reroute
   `GetLastSession` + the chat commands through it; make every read mod-membership-
   scoped.
3. **Self-cleaning lifecycle.** Modlist-change detection + scope badge; the session
   retention/compaction tier + auto-compact; version-boundary marking; corruption
   hardening; the reset control (top-bar button + confirm, two scopes).
4. **Cross-session + cross-modpack insights.** Wire the `insights` producer; add both
   detector families; light up the `LifetimeData` badge and the new kanban columns.
5. **Dashboard cross-session views.** Per-mod history trend in the Observatory
   drawer; a "last N sessions" lens; the modlist-changed banner; the data-health view.

Follow-on (the roadmap above), built after the core ships and the play-test passes,
ranked: **(F1) mod-update regression tracking → (F2) per-mod conditional profile →
(F3) the per-mod character sheet** (the ✪ three), then session anomaly, playthrough
arc, cross-modpack interaction, stack drift, and the confidence-maturation polish.

---

## Risks & invariants

- **Invariant 1 (read-only):** the layer is read + persistence only; no game-state
  mutation. The only deletion of player data is the user-initiated reset control.
- **Invariant 2 (overhead budget):** identity resolution + rollup folding happen at
  session end, never per tick; the hot path keeps `ModId` array indices; the rollup
  write is one batch at teardown.
- **Invariant 3 (honesty):** cross-session findings carry effect-size + data-strength
  badging; `LifetimeData` is only claimed when the ring genuinely spans sessions; a
  version-spanning baseline is flagged, not pooled. **The regression / what-if / cost
  -budget capabilities state measured deltas and distributions — never "remove this
  mod".** The data informs; the player decides.
- **Invariant 4 (abort-clean):** every HistoryStore read is guarded to empty;
  corruption recovery prefers an honest reset over a partial answer, quarantining the
  broken file.
- **Invariant 5 (no mod-specific code):** identity is `Mod.Name`; the cross-session
  and cross-modpack detectors read generic per-mod series, never a named mod.
- **Risk — name instability:** a mod that renames itself splits its own history;
  surfaced by the version timeline, accepted as a rare edge.
- **Risk — backfill cost:** computing the rollup from a large existing store on first
  open could be slow; mitigated by an off-thread, bounded, resumable backfill.
- **Risk — scope creep:** the roadmap is large; the discipline is core-first, the
  ✪ three next, the rest only when a concrete consumer needs them.

---

## What this deliberately does NOT add (closed decisions, not oversights)

- **No community / "typical machine" baselines.** Powerful ("this mod costs 3× more
  on your rig than typical"), but it requires uploading data to a server — a
  fundamentally different trust posture from the local-only, read-only, zero-telemetry
  promise the mod is built on. If it ever happens it is a separate, explicit, opt-in
  product decision, never a silent history-layer feature.
- **Nothing prescriptive.** Several capabilities edge toward "remove these mods";
  Invariant 3 holds the line at descriptive.
- **No raw-event cross-session queries.** The rollup + archive answer the planned
  questions; aggregating raw spike/death/damage rows across sessions waits for a
  concrete second consumer.
- **No JSON-lines revival here.** The agent-readable cross-session export is additive
  and separate; the current agent surface is the `client.log` session-summary block.
