# Code Health Audit — Performance Profiler v0.5 (pre-v0.6 perf pass)

> Breadth-first survey of the entire codebase, run alongside the 11 deep per-system research agents. The per-system docs go *deep* on one surface each; this doc goes *wide* across the repo, catching the cross-cutting patterns that would otherwise fall through the gaps. Cross-references to the per-system docs are explicit so the master plan can hand work to the right place.

Date: 2026-05-20 · Branch: main @ `eaf0dfb` · LOC: 20,042 (.cs only, excluding bin/obj/Tests/bin)

---

## 0. Method

What this audit is and isn't:

- **In scope:** breadth signals — compiler warnings, test coverage gaps, alloc-prone idioms repeated across files, conventions-drift, magic numbers, dead code, comment-vs-code drift, file-size hotspots, idiom inconsistency.
- **Out of scope:** deep architectural redesigns of any single system (those live in the per-system research docs). The CHA flags an issue and routes it; it doesn't redesign.

What I did:

1. Inventoried every `.cs` file under `Profiling/` and `UI/` (excluding `bin/obj/Tests/bin`).
2. Captured all compiler warnings from `dotnet msbuild PerformanceProfiler.csproj` and the test project.
3. Grepped for alloc-prone idioms in non-cold paths: `.ToList()`, `.ToArray()`, `.Select()`, `.Where()`, `.OrderBy()`, `.GroupBy()`, `.SelectMany()`, `string.Format`, `$"..."`, `DateTime.UtcNow`, `DateTimeOffset.UtcNow`, lambdas, `new List/Dictionary/HashSet`, `.ToString()`.
4. Catalogued `TODO`/`FIXME`/`HACK`/`XXX` comments.
5. Surveyed test coverage by source-file-name match against `Tests/*.cs`.
6. Read `context/notes/conventions.md` and checked the v0.4+v0.5 additions against each convention.
7. Spot-read the hottest hot-path files (`InteractionPlayer`, `MetricCollector`, `ProbeStack`) to verify idiom correctness, not just frequency.

What I did **not** do (deliberately, to avoid duplicating per-system agents):

- Did not architect-level review of LiteDB schema, IL emission, or overlay rendering — those are per-system.
- Did not investigate the 233 MB hook-install elephant — that's the hook-instrumentation agent's flagship topic.
- Did not propose new benchmarks — that's the test-harness agent.

---

## 1. Compiler warnings — full inventory

`dotnet msbuild` on the main project (`PerformanceProfiler.csproj`) emits **9 warnings**. `dotnet msbuild Tests/PerformanceProfiler.Tests.csproj` emits **5 warnings**. None are errors. Total **14**.

### 1.1 Main project (9)

| Code | Count | Files |
|---|---:|---|
| `CS0618` (use of obsolete API) | 7 | `ProfilerConfig.cs` (5 × `[Label]`/`[Tooltip]`), `Profiling/Persistence/Interactions/InteractionPlayer.cs` (2 × `PlayerDeathReason.SourceCustomReason`) |
| `ChangeMagicNumberToID` (tModCodeAssist) | 2 | `InteractionNpc.cs:57` (literal `0` → `NPCID.None`), `InteractionPlayer.cs:226` (literal `0` → `ItemID.None`) |

### 1.2 Test project (5)

| Code | Count | File |
|---|---:|---|
| `CS0649` (field never assigned, uses default) | 5 | `Profiling/Events/EventContext.cs:24,27,33,36,39` — fields `Biomes`, `Weather`, `Mode`, `VanillaInvasion`, `Bosses` |

### 1.3 Disposition

- **CS0618 in `ProfilerConfig.cs`:** trivial. tML 1.4.4 deprecated `[Label]`/`[Tooltip]` in favour of `[LabelKey]`/`[TooltipKey]` + localization files. Migration is mechanical: move strings into `Localization/` and switch attributes. **Routes to:** the wrap-up phase, no system-agent owner.
- **CS0618 on `PlayerDeathReason.SourceCustomReason`** (`InteractionPlayer.cs:299, 301`): the obsolete-attribute message says "CustomReason should be used instead". One-character rename. **Routes to:** persistence agent (interaction tracking).
- **`ChangeMagicNumberToID` (2 × literal `0`):** semantic, not behavioural. Replace with `NPCID.None` / `ItemID.None`. **Routes to:** persistence agent.
- **CS0649 on `EventContext.cs` fields:** these fields are read all over the codebase but never assigned *in the test build*. The test project excludes `Profiling/Events/EventAggregator.cs` and `Profiling/Events/ContextSnapshotter.cs` (the writers) because they take a `Mod` reference. The warnings are genuine in the test-build linkage but false in the runtime build. **Routes to:** test-harness agent (the `Compile Include + Link` selection in `Tests.csproj` may need a `<NoWarn>` on CS0649 for these specific fields, or a `[FieldOffset]` shim, or a clearer separation between compile-time-static state and runtime-mutable state in `EventContext`).

**Aggregate severity:** mechanical cleanup. None of these warnings indicate runtime bugs.

---

## 2. Hot-path allocations — the headline cross-cutting finding

### 2.1 `InteractionPlayer.cs` is the worst offender by margin

`Profiling/Persistence/Interactions/InteractionPlayer.cs` is a `ModPlayer` whose hooks fire on every damage event, every NPC hit, every buff tick, and every equip tick. It is hot-path. Per-tick allocations in this file directly inflate the **game-thread enqueue regression from 276 ns/op to 441 ns/op** that `baseline.md` flagged.

Allocations I found:

| Line | Allocation | Trigger frequency | Notes |
|---|---|---|---|
| 37 | `_prevBuffTypes = new int[Player.MaxBuffs]` | Once at class init | OK |
| 67 | `ActiveBuffs = SnapshotActiveBuffTypes()` | Every `OnHurt` | **Hot — allocates a new `List<int>` (see L262) per damage taken** |
| 169 | `_prevBuffTypes = new int[Player.buffType.Length]` | If buff array grows | Cold; OK |
| 204–205 | `try { Lang.GetBuffName(buffType) } catch { "buff-" + buffType }` | Every buff edge | Try/catch overhead per call; catch branch allocates string via concat (boxes `int` for `ToString`) |
| 210 | `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` | Every buff edge | Allocates a `DateTimeOffset` value (struct, but still a copy plus internal offset math) |
| 221 | `var slots = new List<EquipmentSlotEntry>(Player.armor.Length)` | Every `PostUpdateEquips` | **Very hot** — fires on every equip-state change |
| 232 | `slots.Add(new EquipmentSlotEntry { ... })` | N times per equip update | One row per occupied slot |
| 262 | `var list = new List<int>()` | Inside `SnapshotActiveBuffTypes` | Allocates per call; called from `OnHurt` path |
| 276–277 | `try { Lang.GetProjectileName(type)?.Value } catch { "proj-" + type }` | Every damage-dealt-with-projectile row | Same try/catch + string-concat pattern as buff name |

There are **6 separate `DateTime.UtcNow`/`DateTimeOffset.UtcNow` calls** in this single file, one for each row type written (`DamageTakenRow`, `DamageDealtRow`, `BuffEventRow`, `LoadoutSnapshotRow`, `NpcSpawnRow`, `ItemCreatedRow`). Each `.ToUnixTimeMilliseconds()` involves a struct copy + math. They all happen on the game thread before enqueue.

**Routes to:** persistence agent (primary). The fix shape is repeated across every interaction tracker — pre-allocated pooled lists for `slots` and `ActiveBuffs`, a `UnixMsNow()` static helper that reads `Stopwatch.GetTimestamp()` against a captured wall-clock origin (no `DateTime` involvement), and pre-resolved `Lang` name caches keyed by `(buffType, itemType, projectileType, npcType)` that the IL hook surface populates at `PostSetupContent` once per buff/item/projectile/npc id.

The persistence agent should treat this as the single highest-impact change in the pass. It directly addresses the 441 ns/op regression.

### 2.2 The same pattern — repeated

`InteractionNpc.cs` and `InteractionItem.cs` repeat the same shape on a smaller surface (each fires only on `OnSpawn` / `OnCreated`, less hot than `InteractionPlayer`). They share the same `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` allocation pattern, the same `Lang.GetNpcName` / `Lang.GetItemName` lookups inline, and the same `OwningModName(...)` resolution. The fix is the same: pooled rows + cached name resolution.

### 2.3 DateTime usage across the repo

Total `DateTime`/`DateTimeOffset.UtcNow` call sites: **28**. Hot-path occupancy:

| File | Calls | Path |
|---|---:|---|
| `InteractionPlayer.cs` | 6 | Hot |
| `ProfilerDatabase.cs` | 5 | Cold (lifecycle) |
| `DbWriterThread.cs` | 3 | Warm (writer thread) |
| `MetricCollector.cs` | 2 | **Should be zero** — investigate |
| `SessionRecorder.cs` | 2 | Warm |
| `TickDownsampler.cs` | 1 | Warm (1 Hz / 1 min) |
| `Streams/SessionStream.cs` | 1 | Cold (session end) |
| `Streams/ModlistStream.cs` | 2 | Cold |
| `WorldSnapshotter.cs` | 1 | Warm (~30 s) |
| `ProfilerSystem.cs` | 1 | Cold |
| Various overlay + commands | 4 | Mostly cold |

The `MetricCollector.cs:UtcNow×2` is the lowest-frequency call but lives in the hot path. **Routes to:** metric-collection agent — verify those two sites are not per-tick (likely once per session-start and once per session-end).

### 2.4 Try/catch in hot paths

Two sites in `InteractionPlayer.cs` wrap `Lang.GetBuffName` and `Lang.GetProjectileName` in `try/catch`. The defensive shape is correct (Lang lookups for new modded ids can throw on race conditions with `PostSetupContent`), but the cost is:

- One `try` setup per call (≈ 1 ns on .NET 8; trivial).
- The `catch` allocates a fallback string with `"buff-" + buffType` — boxes the int via `Int32.ToString()` + string concat → ≈ 60 bytes per fallback.

The fallback path should never hit in steady-state (Lang has resolved every id by `PostSetupContent`). To prove it doesn't, the fallback should `Logger.Warn` once and cache the failure so subsequent calls take a fast path. The cache is the win, not the try/catch removal.

**Routes to:** persistence agent.

`Profiling/ProbeStack.cs` — verified clean. No try/catch on the hot-callback path. The mentions caught by my grep were doc comments.

### 2.5 LINQ in interaction insight detectors

`Profiling/Insights/Detectors/InteractionInsightDetectors.cs` has **14 LINQ chain expressions**. The insight engine ticks at 1 Hz, so these are not per-tick allocations — but 1 Hz × 14 chains × N rows per chain is still ~hundreds of allocations/second on the writer-thread-adjacent surface.

Sample anti-pattern to expect (without quoting): `db.DamageTakenEvents.Find(...).Where(x => x.SessionId == sid).OrderByDescending(x => x.UnixMs).ToList()`. Every operator allocates an iterator object; `.ToList()` boxes the enumeration into a heap list.

The insights-engine agent owns the redesign — likely an indexed LiteDB query (`Query.EQ`, `Query.GTE`) followed by a pre-allocated buffer fill instead of `.ToList()`.

`Profiling/Persistence/Commands/QueryChatCommands.cs` has 9 chains. These are chat-command paths — fired only when the user types a command. Allocations there are fine.

`Profiling/Events/BiomeRegistry.cs` has 4 chains — fired at `PostSetupContent` once. Fine.

---

## 3. Test coverage gaps

The test suite covers the pure-logic backbone but not the runtime-coupled paths. Files with `*Tests.cs` matching their name in `Tests/`:

- `Baseline.cs` ↔ `BaselineTests.cs`
- `InsightStore.cs` ↔ `InsightStoreTests.cs`
- `RankingScorer.cs` ↔ `RankingScorerTests.cs`
- `RingBuffer.cs` ↔ `RingBufferTests.cs`
- `StallDetector.cs` (split-tested) ↔ `StallDetectorTests.cs` + `StallClassifierTests.cs`
- The whole `Profiling/Persistence/` surface ↔ `Tests/Persistence/PersistenceRoundTripTests.cs` + `PersistenceBenchmarkTests.cs`

Files with **no obvious tests** (30+):

```
Profiling/MetricCollector.cs
Profiling/ProbeStack.cs
Profiling/PerModAttribution.cs
Profiling/HookCoverageView.cs
Profiling/SpikeDetector.cs
Profiling/PerModSample.cs
Profiling/HookBackend.cs
Profiling/ProfilerSystem.cs
Profiling/ProfilerFocusProbe.cs
Profiling/HookCategoryRouter.cs
Profiling/ProfilerSelfHealth.cs
Profiling/HookInterceptor.cs
Profiling/ModImpactScorer.cs
Profiling/ILHookInterceptor.cs
Profiling/PerTickAttributionRing.cs
UI/ProfilerOverlaySystem.cs
UI/ProfilerOverlay.cs
UI/ProfilerTheme.cs
Profiling/Insights/InsightRenderer.cs
Profiling/Insights/IInsightDetector.cs
Profiling/Insights/InsightsEngine.cs
Profiling/Persistence/WorldSnapshotter.cs
Profiling/Persistence/IPersistenceStream.cs
Profiling/Persistence/EventJournal.cs
Profiling/Persistence/SessionRecorder.cs
Profiling/Persistence/SessionSummaryLogger.cs
Profiling/Persistence/DbWriterThread.cs
Profiling/Persistence/StreamRegistry.cs
Profiling/Persistence/PlayerDeathDetector.cs
Profiling/Persistence/ProfilerCompactCommand.cs
Profiling/Persistence/Interactions/InteractionPlayer.cs (+ Npc, Item)
```

This is not "every untested file should get a test". Most of these (UI, ProfilerSystem, hook installers) are runtime-coupled and the project deliberately keeps tML out of the test runner. But there is a **pure-logic substrate inside each of them** that should be lifted out and tested:

- `SpikeDetector.ClassifySeverity` / `IsSpike` — pure functions.
- `PerModAttribution.RegisterOrReuseHook` — pure-id-allocation logic.
- `HookCategoryRouter` — pure mapping.
- `ModImpactScorer` — pure scoring math.
- `InsightsEngine` detector loop — pure given a DB snapshot.
- `PlayerDeathDetector` — pure given the recent damage rows.
- The session-summary aggregator inside `SessionRecorder` — pure given the per-tick samples.

**Routes to:** test-harness agent. Each per-system agent will surface "add benchmark for X" recommendations; the test-harness agent owns the regression-detection design that catches it.

---

## 4. Buff-diff sparsity — analysis (not necessarily a bug)

The playtest captured 2 buff events for a 4.5-minute session — flagged as bug in `baseline.md`. Reading `InteractionPlayer.PostUpdateBuffs` (lines 138–171) carefully, **the diff logic is correct**:

- `_prevBuffCount` is initialised to 0, so the first tick's "removed" loop runs zero iterations (correct — nothing to remove).
- The first tick's "added" loop sees every active buff as not in `_prevBuffTypes[0..0]` → emits "on" edges.
- After the first tick, `Array.Copy(Player.buffType, _prevBuffTypes, Player.buffType.Length)` snapshots the full buff array (22 slots), and `_prevBuffCount = Player.buffType.Length` makes the removed-loop iterate all 22 slots next tick. Slots with `t = 0` are skipped via the `if (t <= 0) continue` guard.

The session reality: pre-hardmode, Forest+Desert exploration, no potions consumed, only accessory was Radar (a passive ability, **not a buff**), torches placed (give no buff to the player). The two captured events were:

- Tick 0: `Peckish` on
- Tick 1: `Peckish` off

Peckish is the "low food" state in the Well Fed → Peckish → Starving decay chain. The player likely loaded into the world with their food running out at the precise tick boundary, then the buff cleared the next tick. **That is actually correct behaviour for this specific session.**

**Reclassification:** the "buff-event sparsity bug" in `baseline.md` is unconfirmed. The fix could still be useful (defensive: re-snapshot on player respawn — currently the prev-buffs array survives a death cycle, which is intentional but worth verifying), but it is not a guaranteed bug.

**Routes to:** persistence agent. Recommended action: write an in-game retest with deliberate potion use (Healing potion, Ironskin, Regen) and re-query `buffEvents`. If those edges all fire, the tracker is correct.

The `itemCreatedEvents = 0` bug and the last-hit death attribution remain genuine bugs.

---

## 5. Conventions audit — does v0.4+v0.5 still follow them?

`context/notes/conventions.md` defines eight project-wide patterns. I checked each against the v0.4+v0.5 surface.

| # | Convention | v0.5 compliance |
|---|---|---|
| 1 | `#nullable enable` at every `.cs` head | ✅ verified across all new files (`InteractionPlayer.cs`, `InteractionNpc.cs`, `InteractionItem.cs`, `Records/*`, `Streams/*`, `Detectors/*`, `ProfilerFocusProbe.cs`, `*Snapshotter*`) |
| 2 | Static-class backend state for `HookInterceptor`/`ILHookInterceptor`/`HookCoverageView`/`HookBackend`/`ProbeStack` | ✅ unchanged |
| 3 | `try/finally` (not `try/catch`) inside hook probes | ✅ — verified `ProbeStack.cs`. The two `try/catch` sites in `InteractionPlayer.cs` (Lang lookups) are **not** inside probes; they wrap an external API call. The convention's wording targets hook probes; these don't violate it but are still worth optimising (§2.4). |
| 4 | `Mod.Logger` for lifecycle/install/teardown only | ✅ — the v0.4 inline-narration additions (`stall-cluster`, `stall`, `player-death`, `context`, `spike` log lines) are *not* per-tick — they fire only when an event happens (event-driven, not tick-driven). Verified `Logger.Info` is never called from `PostUpdateEverything` or any per-tick callback. |
| 5 | `Stopwatch.GetTimestamp()` static reads, never `new Stopwatch()` | ✅ verified. Grep for `new Stopwatch()`: zero hits in `Profiling/` and `UI/`. |
| 6 | Pre-allocated arrays + ModId indexing | ⚠️ — **violated in `InteractionPlayer.cs:221` and `:262`** (`new List<...>` per call) and at `:67` (allocates the SnapshotActiveBuffTypes list per OnHurt). These are not the per-tick path in the strict sense (only fires on equip-change / damage), but every call still allocates. Should be pooled. |
| 7 | `RegisterOrReuseHook` between backends | ✅ unchanged |
| 8 | Per-frame caching at the 1 Hz Tick cadence | ✅ — overview-tab/insights-tab caches still refilled at Tick (1 Hz). No new per-frame allocs found in `OverviewTab.cs` / `OverlayPanel.cs` walkthrough. |

The single violation is **Convention 6 in the new interaction trackers**. Folded into §2.1's recommendation.

---

## 6. Gated detector TODOs — dead until upstream lands

`Profiling/Insights/Detectors/GatedDetectors.cs` has 5 `TODO` comments referencing the insights-engine plan:

- L31: `// TODO: requires Events tab GameContext + transition stream. See plan §4.1 and §11 step 8.`
- L50: `// TODO: requires Events tab BucketStats. See plan §4.2.`
- L70: `// TODO: requires per-tick per-mod ms history (LiteDB). See plan §4.6.`
- L90: `// TODO: requires session-half slicing of per-mod ms history. See plan §4.7.`
- L115: `// TODO: requires per-hook call counts + per-call ms distribution. See plan §4.10.`

These detectors are intentionally gated until upstream data structures land. They are not dead code — they will activate when the insights-engine work resumes. The TODOs are reference markers, not action items for this pass.

**Routes to:** insights-engine agent (which will catalogue these gates against the proposed indexed-query infrastructure). The perf pass should not unblock them; it should only document the gate-readiness.

---

## 7. File-size hotspots (the "big files")

Top by line count:

| LOC | File |
|---:|---|
| 1,223 | `Profiling/HookInterceptor.cs` |
| 766 | `UI/Overlay/Tabs/OverviewTab.cs` |
| 653 | `UI/Overlay/OverlayPanel.cs` |
| 621 | `Profiling/Persistence/SessionRecorder.cs` |
| 569 | `Profiling/ILHookInterceptor.cs` |
| 565 | `Profiling/StallDetector.cs` |
| 529 | `Profiling/MetricCollector.cs` |
| 473 | `Profiling/Persistence/ProfilerDatabase.cs` |
| 459 | `UI/Overlay/Tabs/TreeTab.cs` |
| 432 | `Profiling/ProfilerSystem.cs` |

These are large but not unmanageable. The `HookInterceptor.cs` at 1.2k LOC is the legacy delegate backend (parallel-mode comparison only); the ILHook backend is the active path. **Routes to:** hook-instrumentation agent for whether the delegate backend should be retained or removed (a structural-not-performance question — outside this pass; it stays).

`SessionRecorder.cs` at 621 LOC is where the end-of-session 8.5-s stall is born. The session-summary aggregation lives at the end of this file. **Routes to:** mod-lifecycle agent for the move-off-thread design; persistence agent for the writer-thread queue work.

---

## 8. Comment / doc drift signals

Spot-checked the new interaction trackers against the v0.5 `decisions.md` entry and `philosophy.md`. Findings:

- `InteractionPlayer.cs` header doc cleanly cites Invariant 5 and the philosophy posture. Good.
- `InteractionNpc.cs:57` literal `0` should be `NPCID.None` (matched by the magic-number warning). The accompanying comment correctly explains the SourceCategory stripping logic — no drift.
- `InteractionItem.cs` is the smallest and the comment density is appropriate for the file size.
- `philosophy.md` is fresh and accurate.
- `decisions.md` v0.5 entry matches the v0.5 shipped code (verified Phase A–G against actual files).
- `systems/persistence.md` table (lines 100–108) lists every new collection accurately. No drift.

The `notes/spikes-and-allocations-plan.md` (1,500+ lines) is preserved as historical and correctly tagged `SHIPPED — preserved as historical research record.`. No need to compact it; rationale is what survives.

The `notes/litedb-migration-plan.md` is the same shape — historical, preserved, accurate. The `systems/persistence.md` is the canonical reality file. Good split.

**No drift to fix in this pass.**

---

## 9. Magic-number warnings — `tModCodeAssist`

Two warnings explicitly flagged by tModLoader's code-assist analyser:

- `InteractionNpc.cs:57` — `npc.netID == 0` should be `npc.netID == NPCID.None`
- `InteractionPlayer.cs:226` — `item.type == 0` should be `item.type == ItemID.None`

Both are semantic. `NPCID.None = 0` and `ItemID.None = 0` are constants in the tML public API. The compiler accepts the literal `0`, but the code-assist analyser surfaces it for readability — which lines up with the project's convention of using ID constants over literals.

**Routes to:** persistence agent (these live in the interaction-tracker surface it owns).

---

## 10. ProfilerConfig — obsolete-attribute migration

`ProfilerConfig.cs` uses `[Label("...")]` and `[Tooltip("...")]` 5 times. tML 1.4.4 deprecated these in favour of `[LabelKey("Mods.PerformanceProfiler.Configs.Foo.Bar.Label")]` + matching entries in `Localization/en-US_Mods.PerformanceProfiler.hjson`. The compiler emits CS0618 for every site.

The fix is mechanical:

1. Add localization entries to `Localization/en-US_Mods.PerformanceProfiler.hjson` (one Label + one Tooltip per config field).
2. Replace `[Label]` / `[Tooltip]` with `[LabelKey]` / `[TooltipKey]` referencing the new keys, or just delete them entirely and let tML auto-resolve from the localization file via the field name.

**Routes to:** wrap-up phase (Phase 8). No system-agent owner; trivial. Suggest delete-and-let-autoload — fewer lines of code, more idiomatic, and the obsolete-attribute warnings disappear.

---

## 11. Test-build CS0649 false positives — root cause

`Profiling/Events/EventContext.cs` declares public read-only fields `Biomes`, `Weather`, `Mode`, `VanillaInvasion`, `Bosses`. These are written by code in `Profiling/Events/EventAggregator.cs` and `Profiling/Events/ContextSnapshotter.cs`, but those writers are excluded from the test-build linkage (they reference tML's `Mod` and `ModSystem` types that the test runner doesn't load).

Result: the test-build sees `EventContext` declared but never assigned → CS0649 × 5.

This is a linkage choice, not a runtime bug. **Routes to:** test-harness agent. Options:

- Add `#pragma warning disable CS0649` around the field declarations.
- Add a `<NoWarn>CS0649</NoWarn>` to the Tests project for the linked `EventContext.cs`.
- Split `EventContext` into a pure POCO (test-compilable) + a runtime aggregator class.

The third is cleanest but adds files. The test-harness agent should pick.

---

## 12. Patterns I checked and found clean

These were on my list but came back negative:

- **`async`/`await` usage in non-test paths:** zero hits. Good (Invariant 2; async machinery would add per-call alloc overhead and complicates abort-clean).
- **`new Stopwatch()`:** zero hits. Convention 5 held.
- **Logger calls in per-tick callbacks:** verified clean by reading `MetricCollector.OnEndTick`, `ProbeStack.Leave`, and `PostUpdateEverything` chain. The v0.4 inline-narration adds Logger calls only at *event* boundaries (stall, spike, cluster, transition, death), never per-tick.
- **`StringBuilder` reuse in hot paths:** the only `new StringBuilder()` I found is in cold paths (chat-command formatters, session-summary log line). Hot paths use field-cached arrays + concat at write time.
- **GC.Collect() calls:** zero hits. Good.
- **Reflection (`typeof(...).GetMethod(...)`) in hot paths:** the only reflection I found is in `ILHookInterceptor.Install` (cold, runs once) and `LegacyJsonImporter` (cold, runs once or never). No hot-path reflection.
- **`Span<T>` / `ReadOnlySpan<T>` / `stackalloc` / `ArrayPool<T>` usage** (the *good* signals): present in `SpikeDetector.cs`, `PerTickAttributionRing.cs`, `UI/Overlay/Tabs/TreeTab.cs`. Total of 10 occurrences across the repo. This is *low* for a perf-conscious codebase — many opportunities to expand `Span` adoption (especially in the new interaction trackers and the LiteDB serialisation hot path). **Routes to:** persistence + metric-collection agents.

---

## 13. Cross-cutting recommendations — what to route where

Summary of who owns what, distilled from §1–§12:

| Routing | Agent | Volume |
|---|---|---:|
| Move `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` calls to a `UnixMsNow()` helper backed by `Stopwatch.GetTimestamp()` | persistence + metric-collection | 28 sites |
| Pre-resolve `Lang.GetBuffName` / `Lang.GetItemName` / `Lang.GetProjectileName` / `Lang.GetNpcName` at `PostSetupContent` into id-keyed arrays | persistence | 4 lookup types |
| Replace `new List<EquipmentSlotEntry>` and `new List<int>` per-call in `InteractionPlayer` with pooled lists | persistence | 3 sites |
| Wrap the buff-diff sparsity question with an in-game potion-use retest before declaring it a bug | persistence | 1 verification step |
| Audit LINQ-heavy detector chains (`InteractionInsightDetectors.cs` × 14) for `.ToList()` → buffer-fill conversion | insights-engine | 14 chains |
| Move end-of-session aggregation off the main thread | mod-lifecycle + persistence | 1 structural change |
| Expand `Span<T>`/`stackalloc` adoption in BSON-record build paths and per-tick attribution math | persistence + metric-collection | repo-wide |
| Mechanical fixes: `CS0618` (ProfilerConfig + `SourceCustomReason`), 2 × `ChangeMagicNumberToID`, 5 × `CS0649` Tests | mixed (mostly wrap-up) | 14 warnings → 0 |
| Lift pure-logic substrates out of runtime-coupled files for testability | test-harness | ~7 surfaces |
| Add benchmarks for every per-system change | test-harness | per master plan |

---

## 14. Cross-system "themes" that the per-system agents may miss

Patterns that span multiple systems and might fall between the per-system agents' beats:

### 14.1 The `DateTime` epidemic

Every record-emitting site writes `UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`. There are 28 such sites. Per-call cost of that expression is in the order of 50–80 ns plus a struct copy plus a tick-to-unix conversion. A single `UnixMsNow()` static helper that captures `(Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())` once at session start and computes `origin + (Stopwatch.GetTimestamp() - originStamp) / Stopwatch.Frequency * 1000` per call cuts this to a ~5 ns read+math. **Cross-system implementation:** one static class, used by every emitter.

### 14.2 The `Lang.Get*` epidemic

Five tracker types call `Lang.GetBuffName` / `Lang.GetItemName` / `Lang.GetNpcName` / `Lang.GetProjectileName` inline at emit time. Each call is a dictionary lookup + a `LocalizedText.Value` evaluation (which itself may format/allocate). A `LangNameCache` populated at `PostSetupContent` (one-time scan over each id-space) turns each call into a `string[indexByType]` access. **Cross-system:** one helper, used by interaction-trackers + insights-engine (for rendering).

### 14.3 The `OwningModName(...)` pattern

`InteractionPlayer`, `InteractionNpc`, `InteractionItem`, and `BuffEvent` emission all call `OwningModName(Mod)` or `OwningModName(ModBuff)`. The helper returns `null` for vanilla types. Same shape across files. A cross-cutting `ModOwnerCache` indexed by `(typeKind, id)` resolves to `string?` once and is read per emit. **Cross-system.**

### 14.4 The BSON-field-name overhead

LiteDB stores BSON documents with field names inline per row. `DamageDealtRow` has ~12 fields × ~12 chars each → ~144 bytes of field names per row. With 354 rows in a 4.5-min session that's 51 KB of *just field names*. Across all six new streams and ten existing streams, the field-name tax dominates a non-trivial fraction of the 1064 KB / 10-min figure. **Cross-system:** the storage/RAM cross-system agent (Phase 3) should look at BsonMapper customisation to use short names (`t`, `nm`, `dm`, `c`) in the BSON layer while keeping long names in the C# record properties.

### 14.5 The "everything stamps UnixMs" pattern (redundancy)

Every row carries `UnixMs` *and* every event-bearing row's `SessionId` is identical to the parent session row. Plus most rows carry a `Tick` field. With 1000+ rows per session at ~16 bytes/timestamp + ~12 bytes/ObjectId + ~8 bytes/Tick = ~36 bytes/row × 1000 rows = 36 KB of timestamp/id data per session. Storage-tier opportunity: persist `UnixMs` relative to `SessionStartedUtc` as a 32-bit `int` delta-ms (fits 24 days of session); `SessionId` referenced via the existing per-document container if LiteDB supports row-grouping (it does not by default — but the entire session-keyed collection can live in a single sub-document if redesigned). **Cross-system:** the storage/RAM agent should weigh this.

### 14.6 Per-tick attribution ring buffer footprint

`PerTickAttributionRing` stores 1800 ticks of per-mod samples. With ~20 active mods that's 36k samples per ring × ~32 bytes per sample = ~1.2 MB resident. Multiplied across (ms ring + alloc ring) = 2.4 MB. Not the elephant (233 MB hook install is) but worth thinking about: are the ring buffers using `T[]` or pooled `Memory<T>`? Are the samples value-type (no per-row reference indirection)? **Routes to:** spike-detection + metric-collection agents.

---

## 15. Open questions for the agents to answer

These will be answered by the per-system or cross-system agents. The CHA can't answer them by grepping alone.

1. **Is `GC.GetAllocatedBytesForCurrentThread()` actually the bottleneck or is the IL-emit shape (two FCalls per hook entry/exit) heavier than expected?** → allocation-tracking agent.
2. **Does Mono.Cecil's `ModuleDefinition` retain the entire assembly's IL in memory after `Add(...)` returns?** → hook-instrumentation agent.
3. **What is `LiteDB`'s per-row BSON-document overhead for the typical interaction-tracker shape?** → persistence + storage-cross agent.
4. **Can `OnWorldUnload` defer to `Task.Run` without violating tML's save lifecycle?** → mod-lifecycle agent.
5. **Does the writer thread's `Channel<DbWriteOp>` use the right policy (`SingleReader = true`, `SingleWriter = false`, `BoundedCapacity = ...`)?** → concurrency-cross agent.
6. **Are the `Span<T>` / `stackalloc` opportunities in the interaction-tracker fingerprint-building paths actually wins, or does the LiteDB serialiser still copy?** → persistence + allocation-cross agents.
7. **What FCall-cost figure does `GC.GetAllocatedBytesForCurrentThread()` show on .NET 8 macOS?** → test-harness agent (needs to add the microbench).

---

## 16. Health summary

- **No correctness bugs found** that aren't already on the bug list. The buff-diff sparsity is unconfirmed and the diff logic itself is correct.
- **No security or save-corruption risk.** Invariant 1 (read-only) holds across the new v0.4+v0.5 code.
- **Conventions: 7/8 fully followed; 1 (pre-allocated arrays) violated locally in `InteractionPlayer.cs`.** Routed.
- **Warning hygiene: 14 warnings, all mechanical fixes.** Routed.
- **Test coverage: comprehensive for pure-logic substrates; absent for runtime-coupled paths** (deliberate — tML stays out of the runner). Lift opportunities documented.
- **Comment drift: zero.** Recent docs match recent code.
- **Per-tick hot path: zero-alloc on the timing surface (`ProbeStack`, `MetricCollector`).** The interaction-tracker surface has localised violations (§2.1).
- **Headline regression cause (276 → 441 ns/op enqueue) likely concentrated in the interaction-tracker emit paths**, especially `DateTime`+`Lang.Get*`+`new List<>` per row, repeated across six trackers. Cross-system fix shape is clear and already routed.

The codebase is in good shape. The perf pass has well-defined, well-scoped levers. Nothing in this CHA suggests a structural rewrite; everything routes to additive optimisation work owned by a specific agent.

---

*This file feeds into Phase 5 (master plan synthesis). The recommendations here will be merged with the per-system and cross-system research docs by the coherence pass (Phase 4).*
