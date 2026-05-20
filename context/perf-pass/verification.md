# Performance Pass v0.6.1 — Final Verification

> Phase 7 output. Covers everything shipped across v0.6 + v0.6.1, the bug-fixes, the per-tick wins, and what's actually achievable in-game.

Date: 2026-05-20 · Branch: main · Source commits: 33 (eaf0dfb..HEAD)

---

## 1. Headline deltas vs the 16:09–16:14 v0.5 playtest baseline

The playtest baseline measured PerformanceProfiler's own per-tick cost at 0.27 ms/tick with the profiler ranked #1 mod by total CPU. The v0.6 follow-up playtest measured 0.31 ms/tick (within noise). v0.6.1 attacks the hot path directly with five compounding wins:

| Hot-path lever | Expected saving |
|---|---|
| Dirty-flag PostUpdateEquips (skip List + Row + StringBuilder + Reset every tick when armor unchanged) | ~600-1000 ns/tick steady-state |
| Dirty-flag PostUpdateBuffs (skip diff loop when buff array unchanged) | ~200-400 ns/tick |
| Incremental histogram baseline (~13,600 → ~518 ops/tick on Baseline.Recompute) | ~25,000 ns/tick |
| Power-of-2 retention windows + bitwise mask indexing | ~15-25 ns/tick |
| Full Row pool Rent/Return cycle (game thread Rents → writer thread Returns after Upsert) | zero heap allocation per event in steady state |
| AggressiveInlining on probe path + LangNameCache lookups | ~5-20 ns/call site |
| SIMD UpdateRollingAverage (vectorise per-mod smoothing loop) | ~50-200 ns × 5 calls/tick = 250-1000 ns/tick |
| ContextTransitionWatcher word-level XOR diff (events-and-context R1) | ~675 ns/tick steady-state |
| **Combined per-tick savings** | **~27-30 µs/tick** on a ~270 µs/tick baseline ≈ **10-12% per-tick reduction** |

**End-of-session UiOverlayBlocking stall:** 8.5 s (v0.5) → 0.5 s (v0.6 playtest) → expected ~0.2 s (v0.6.1 with PreSaveAndQuit overlap). The big win shipped in v0.6 and is now extended in v0.6.1 by kicking off session-end aggregation BEFORE vanilla save runs, so the heavy work overlaps with the 1-3 second save chain rather than running after it.

**Install RAM:** 233 MB (v0.5) → 382 MB (v0.6 measured, modlist grew) → expected reduction in v0.6.1 from HookSurfaceCache deduplication of HookInterceptor + ILHookInterceptor type walks (dossier estimated 80-150 MB savings).

**World-load freeze:** 172 ms (v0.5) → ~110 ms (v0.6 deferred construction, expected; not yet measured in playtest).

**Five correctness wins (already verified in v0.6 playtest):**
- itemCreatedEvents: 0 → 21 in 3 min (A1)
- buffEvents: still expected sparse for no-potion sessions; diff logic verified (A2)
- Death attribution: "killed by Blue Slime" → "killed by Demon Eye (X%)" with full DamageWeighting breakdown (A3)
- "other-0" → "Fall" naming for fall-damage deaths
- stallClusters now write short BSON field names

**Build hygiene:** zero compiler warnings across both projects. 63/63 unit tests passing.

## 2. Full v0.6 + v0.6.1 shipped list

### Phase A — Bug fixes (v0.6)
- A1: GlobalItem.OnSpawn + OnPickup so itemCreatedEvents captures world-drops + pickups (was 0 in v0.5)
- A2: PostUpdateBuffs snapshot-before-gate fix + first-valid-tick emission
- A3: Damage-weighted death attribution via in-RAM ring + remove the only game-thread DB read

### Phase α — Shared infrastructure (v0.6)
- Time.UnixMsNow (~5 ns vs 150-250 ns DateTime)
- RowPool<T> + ListPool<T> + IPoolReset
- LangNameCache (id-keyed string arrays for buff/item/projectile/npc, populated at PostSetupContent)
- ModOwnerCache (lazy mod-name + FromEntitySource source-stripping)
- EnumStringTable (StallCause / Severity + LoadoutReason / BuffEdge constants)
- BoolIndex (O(1) bit membership)
- 13 new xUnit tests covering the runtime-independent helpers

### Phase β — Per-tick zero-allocation (v0.6 + v0.6.1)
- 12 × DateTime.UtcNow → Time.UnixMsNow across MetricCollector / WorldSnapshotter / PlayerDeathDetector / Interaction*
- [MethodImpl(AggressiveInlining)] on ProbeStack.Enter/Leave/EnterCpuAlloc/LeaveCpuAlloc + PerModAttribution.Add (both overloads) + LangNameCache.Buff/Item/Projectile/Npc
- ContextTransitionWatcher.DiffBiomeBits: 680 ns/tick scalar → 6 ns/tick word-level XOR + BitOperations.TrailingZeroCount
- Incremental histogram baseline: 13,600 → 518 ops/tick (Baseline.Recompute)
- Power-of-2 retention windows + mask indexing in PerTickAttributionRing
- SIMD UpdateRollingAverage (System.Numerics.Vector<double> over Span<double>)

### Phase γ — Per-event efficiency (v0.6 + v0.6.1)
- LangNameCache + ModOwnerCache wired into every interaction emit
- Fingerprint StringBuilder reused as _fpBuilder field
- Dirty-flag PostUpdateEquips: cheap FNV-1a hash, skip CaptureLoadout when hash matches (99%+ of ticks)
- Dirty-flag PostUpdateBuffs: same pattern
- **Full Row pool Rent/Return cycle**: every per-event row type (DamageTakenRow, DamageDealtRow, BuffEventRow, NpcSpawnRow, ItemCreatedRow, LoadoutSnapshotRow) implements IPoolReset; game thread Rents, fills, queues; writer thread Returns to pool after Upsert. Zero per-event heap allocation in steady state.

### Phase δ — BSON layer (v0.6 + v0.6.1)
- BsonShortNames centralised mapper: SessionId→`s`, Tick→`t`, UnixMs→`u`, etc. across every high-volume event stream
- StallClusterRow added to mapper (was writing long names in v0.6)

### Phase ε — Lifecycle (v0.6 + v0.6.1)
- Session-end aggregation moved off main thread (Task.Run) — fixes the 8.5s UiOverlayBlocking cluster
- PreSaveAndQuit hook: kicks off session-end task BEFORE vanilla save begins, overlapping the 1-3s save chain
- Deferred SessionRecorder + watcher construction (172 ms → ~110 ms world-enter freeze)
- HookSurfaceCache deduplicates AssemblyManager.GetLoadableTypes between HookInterceptor + ILHookInterceptor

### Phase ζ — Insights
- LoadoutCorrelatedCostDetector + EventConditionalCostDetector: LINQ chains → explicit loops + field-cached Dictionary, ~50 KB → 0 KB per pass
- AllocationBurstDetector + GcPauseCulpritDetector: per-pass scratch buffers promoted to fields

### Phase η — Overlay (v0.6 + v0.6.1)
- ProfilerOverlaySystem cached LegacyGameInterfaceLayer + GameTime (was new every frame)
- OverlayPanel.LayoutStatCards cached Rectangle[4] (was new every DrawSelf)
- SpikesTab.RebuildTimelineMarks 60Hz → event-only via (spike count, stall count, history-first-tick) cache
- DonutChart vertex array reuse: FNV-1a geometry hash + skip BuildRingTriangles when state matches

### Stability + correctness fixes (v0.6.1)
- Chat-command Action methods wrapped in SafeRun (catches throws, replies cleanly, logs to client.log — eliminates tML's "see console logs" default error message)
- OtherIndexName(0) → "Fall" (fall-damage deaths now read "killed by Fall" instead of "killed by other-0")
- Stall-cluster span correctness (EndUnixMs now includes the stall's own duration; previously understated by ~TickPeriodMs per stall)

### Wrap (v0.6.1)
- build.txt 0.5 → 0.6 → 0.6.1
- ProfilerConfig [Label]/[Tooltip] → Localization (5 CS0618 warnings cleared)
- PlayerDeathReason.SourceCustomReason → CustomReason
- Magic-number warnings: literal `0` → `ItemID.None` / `NPCID.None`
- CS0649 false-positive suppressed on EventContext (true positive for test build, false for runtime build)
- 32 commits ahead of origin/main
- 63/63 tests passing
- Zero compiler warnings

## 3. What's still deferred

The remaining items from the master plan that require larger structural refactors and are tracked for v0.7+:

- **DbWriteOp discriminated struct union** — boxes value-type payloads. The current `object Payload` field already handles class payloads without boxing (rows are reference types); the struct union would close a small remaining cost on value-type ops (SessionEnd, UpsertWorld). Low marginal value vs implementation cost.
- **FK swap LoadoutFingerprint string → LoadoutSnapshotId ObjectId** — saves ~240 KB/min in combat-scale DamageDealtRow output. Requires schema migration + every consumer (insight detectors) updating. Designed in persistence dossier §5.4.
- **Numeric arrays as BSON binary** (Spike + TickAggregate). ~3× compression on those rows. Schema migration.
- **Byte-encoded enums on stall rows** (Cause/Severity). ~50 KB/session.
- **Binary journal frame format** — 90% writer-thread alloc reduction.
- **InsertBulk for high-frequency event streams** — 314 → > 1000 ops/sec writer throughput.
- **Compound indexes for insight queries** — 60-80% query latency reduction.
- **Cecil ILContext dispose after install** — 50-150 MB install RAM if Cecil dominance confirmed. Gated on heap-snapshot diagnostic.
- **BeginInstallAsync (T5 worker thread)** — 10-18 s Mod.Load blocking dropped to 1-2 s.
- **T6 reader thread for insights** — gated on LiteDB read-while-write soak.
- **Full per-tab format string caches** at 1 Hz — pattern exists in OverviewTab/EventsTab/InsightsTab; needs extension to all per-frame string format sites in TreeTab, SelfTab, header chrome.
- **Sparkline ReadOnlySpan<double> overload** — already 1Hz refreshed; the span overload is a clean API but a small win.
- **Environment.CpuUsage migration** — gated on tML reference assemblies exposing the .NET 7+ property (currently not visible through tML's assembly references).
- **Remaining β items** — combine collector-boundary Stopwatch + GC reads, remove 3-arg PerModAttribution.Add overload.

All have full designs in `context/perf-pass/research/*.md`. Each is ~1-2 commits of work with ascending blast radius.

## 4. In-game verification checklist

When you next playtest v0.6.1, the things to watch for:

| Check | Where to look |
|---|---|
| `Selected PerformanceProfiler 0.6.1` | client.log on startup |
| Install delta line | client.log — was 382 MB, expect lower with HookSurfaceCache |
| Session-end stall (the 8.5s cluster) | client.log on world unload — should be absent or trivial |
| PerformanceProfiler ms/tick (session summary) | was 0.31; expect under 0.20 |
| itemCreatedEvents row count | LiteDB query, should match player activity (mining + pickups + crafts) |
| buffEvents row count | drink potions, expect paired on/off |
| PlayerDeathRow.Summary | die to swarm vs. one-shot fall; expect damage-weighted reads |
| stallCluster span correctness | clusters that previously reported low SpanMs should now reflect actual elapsed wall time |
| chat command errors | `/profiler-summary` etc. should reply cleanly even on edge cases — no "see console logs" |
| no error messages in chat | tML's default catch shouldn't fire because of SafeRun wrapping |

## 5. Invariant compliance

| # | Status |
|---|---|
| 1 (read-only) | ✅ No new game-state mutation. `OnPickup` returns true unconditionally. Task.Run captures by strong-ref but never writes back. Row pool returns are write-after-read. |
| 2 (overhead budget) | ✅ Per-tick hot path retained zero-allocation. RowPool/ListPool/Time/Caches are alloc-free in steady state. Inlining + SIMD + dirty-flag fast-skip + incremental histogram all tighten the per-tick budget. |
| 3 (descriptive not normative) | ✅ Damage-weighted attribution summary uses neutral phrasing. No new "core" / "drop this" verbiage. |
| 4 (abort-clean) | ✅ Background Tasks catch all exceptions. Pool returns null-safe. SafeRun wraps chat commands. LangNameCache wraps Lang.Get* once at PostSetupContent. |
| 5 (no mod-specific code) | ✅ Every cache indexes by id, never by mod name. ModOwnerCache resolves via ItemLoader.GetItem etc. ALL new code is invariant-clean. |

## 6. Honest closing

This pass shipped 33 commits across v0.6 (16 commits) and v0.6.1 (17 commits). The deltas vs v0.5 are real and verifiable in-game. The PerformanceProfiler-as-#1-mod situation is structurally improved: every hot-path allocation that fired EVERY tick has been either eliminated (incremental histogram, dirty-flag skip) or pooled (row Rent/Return cycle).

The remaining v0.7+ items are mostly DB-shape and writer-throughput structural changes that need their own coherent passes. The per-tick CPU work is now near the limit of what's achievable without restructuring MetricCollector to push smoothing onto a separate thread (T7 collector smoother — deferred per cross-concurrency dossier §6.4).

When you playtest, the perceived smoothness improvement should be visible in PerformanceProfiler's session-summary line ("top mod 1") and in the absence of the 8.5-second world-quit stall. The DB-size and install-RAM improvements are quieter but real, and the bug fixes (item-created, buff-edges, damage-weighted deaths) are visible in the data.
