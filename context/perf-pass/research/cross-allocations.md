# Cross-Allocations — Aggregate Allocation Audit (v0.6 perf pass)

> Scope: every recurring allocation in the Performance Profiler codebase, classified by frequency tier (per-tick, per-event, per-emit, per-frame, per-second, per-minute, per-session, once-per-install, once-per-process), aggregated across the 11 per-system dossiers that landed in this pass, with cross-cutting shared-infrastructure proposals where one fix would close N sites at once.
>
> **Reading order.** This file is the *aggregate* view; the per-system files own the local detail. Where a per-system file has already documented a site and its fix, this file references the fix with a tag and a one-liner. Its value-add is (a) finding the same pattern repeated across files, (b) proposing one helper / pool / cache that several systems should share, (c) catching closure / iterator / boxing allocations the per-system narratives missed, and (d) reconciling overlapping or conflicting fixes.
>
> **Hard constraint, restated up front.** No scope cuts. No capture removed. No event sampled away. No insight dropped. Per-tick zero-allocation is an invariant; other tiers carry budgets, not caps that lose features. Universal: no mod-specific code.
>
> **Status of numbers in this file.** Site frequencies are sourced from baseline.md and the 11 per-system dossiers (which themselves cross-check against source). Byte counts are per-object estimates from the dossiers; where a dossier already produced a measured number, this file uses that figure. Where a number is hypothetical (no benchmark yet), it carries a `[hypothesis]` tag and feeds the §6 phase that puts the benchmark in place.

---

## 0. Map

| § | What lives here |
|---|---|
| 1 | **Inventory pass.** Every allocation site across the codebase, classified by frequency tier and tagged with which per-system dossier owns the local fix. Tables walk by tier from per-tick (most painful) down to once-per-process (most tolerable). |
| 2 | **Aggregate budgets per tier.** A proposed bytes-per-tier ceiling and a current-vs-target table. The current totals are aggregated from §1; the targets are the proposed invariant floor for the post-pass build. |
| 3 | **Shared-infrastructure proposals.** Helpers / pools / caches that several systems should share rather than each re-inventing. Includes API sketches: `UnixMsNow`, `LangNameCache`, `RowPool<T>`, `ListPool<T>`, `ModOwnerCache`, `StringInterner`, `JsonWriterPool`, `BsonScratchBuffer`, `BoolIndex`. Each names the consumers and the per-system docs that point at it. |
| 4 | **Cross-system reconciliations.** Concrete cases where two per-system dossiers proposed overlapping or conflicting fixes; this section reconciles them so the master plan has one answer, not two. |
| 5 | **Hidden-allocation finds.** Patterns the per-system narratives did not surface — closure captures, iterator-state objects in `yield return`, `IReadOnlyList<T>` boxing, implicit `string + int` concat, BSON serialiser allocations, `Process.GetCurrentProcess()`, etc. |
| 6 | **Master allocation roadmap.** A phased execution order with cross-system dependencies surfaced — Phase α (build infrastructure), Phase β (per-tick zeroing), Phase γ (per-event row reuse), Phase δ (writer-thread reductions), Phase ε (session-end + install). |
| 7 | **Verification surface.** What tests / benchmarks are needed to prove the budget compliance after each phase. Maps to the test-harness dossier's allocation-aware tests. |
| 8 | **References.** Every per-system doc cited, every external source pulled in. |

---

## 1. Inventory pass

### 1.1 Tier definitions

| Tier | Cadence | Allocations per second at 60 Hz |
|---|---|---:|
| T0 — per-tick | every game tick on the main update thread | up to 60 |
| T1 — per-tick × per-hook | every instrumented method call inside a tick | up to ~3,000,000 (60 ticks × 50k hooks) |
| T2 — per-event (game-thread) | per damage event, per spawn, per buff edge, per equip change | depends on play — 0.5 to 60 per sec sustained, peaks 100+ |
| T3 — per-emit (writer-thread) | per row materialised inside writer thread | tracks T2 plus per-aggregation work |
| T4 — per-frame (draw thread) | per F9 overlay paint at 60 Hz when overlay open | up to 60 |
| T5 — per-second (1 Hz) | insight detectors, cache refreshes, self-health refresh | 1 |
| T6 — per-minute / per-window | spike open, stall open, cold downsample tier emit | 0.05 — 0.5 |
| T7 — per-session | world-load, world-unload, session-end aggregation | rare, large bursts |
| T8 — per-install / per-process | Mod.Load, ILHook install, PostSetupContent | one-shot, very large |

The per-tick tier (T0) and per-tick × per-hook tier (T1) are where Invariant 2 is absolute: **zero allocation**. T2 is where the 441 ns/op enqueue regression in baseline.md lives — allocations there are budgeted, not zero. T3 onward is writer-thread or off-main-thread; allocations there pay only in GC pressure across all threads, not in main-thread latency.

### 1.2 T0 / T1 — per-tick and per-tick-per-hook (must be zero)

Sites the dossiers found that *do* allocate on these tiers today. The fixes are all in scope of the v0.6 pass.

| # | Site | Lines | Allocates | Cadence | Owner doc | Recommended fix |
|---|---|---|---|---|---|---|
| T1.A | None outright | — | The IL-emitted prologue + epilogue around every hook (`ProbeStack.EnterCpuAlloc` / `LeaveCpuAlloc`) | T1 | allocation-tracking, hook-instrumentation | Zero-alloc today — the only T1 alloc surface is *latent* via `[ThreadStatic]` first-touch (see §1.7 hidden finds). |
| T0.B | `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` × 2 in `MetricCollector.BeginTick`/`EndTick` | `MetricCollector.cs:318`, `:371` | DateTimeOffset struct copy + OS clock syscall (~0.5–1 µs, not heap), but every spike/stall/event emit downstream also calls it | T0 | metric-collection (§3.4) | Replace with `UnixMsNow()` helper backed by `Stopwatch.GetTimestamp()` + captured wall-clock origin. See §3.1. |
| T0.C | `RebuildTimelineMarks` in `SpikesTab` per draw not throttled — clears + appends a `List<TimelineMark>` per *frame*, not per tick | `SpikesTab.cs` | List.Add growth path | T4, but written from data computed on T0 | overlay (§4.12) | Cache. |
| T0.D | `_activeKeysLastTick.Clear()` per tick on the event-aggregator even when nobody read | `EventAggregator.cs` | None per se, but the dictionary churn around it has growth allocations on first insert per tick if the watermark advances | T0 | events-and-context (§R8) | Latch-dirty. |

The headline T1 finding from allocation-tracking and hook-instrumentation is that the existing IL emission *is already* zero-alloc per call. The optimisation work there is CPU shaving (FCall consolidation, frame packing, Enter/Leave inlining), not alloc removal. The per-tick alloc hole that *does* exist is T0 (frame-level), not T1 (per-hook).

### 1.3 T2 — per-event (game-thread, the 441 ns/op regression)

These are where the baseline.md enqueue regression lives. Each represents one heap allocation that fires on a player event and lands on the writer queue.

| # | Site | Allocates per event | Cadence | Owner doc | Fix shape |
|---|---|---|---|---|---|
| T2.A | `InteractionPlayer.OnHurt` → `DamageTakenRow` + `ActiveBuffs = new List<int>()` via `SnapshotActiveBuffTypes` | row (1 obj, ~96 B) + `List<int>` header (40 B) + backing `int[]` (depends on buff count, ~24–88 B) + `Lang.GetBuffName` fallback string on miss | per damage taken, 1–5 per sec sustained, 30+ in fight | persistence (§5.2), code-health-audit (§2.1) | Row pool + buff-list pool + `LangNameCache` (§3.2). |
| T2.B | `InteractionPlayer.OnHitNPCWithItem` / `OnHitNPCWithProj` → `DamageDealtRow` | row (~120 B) + 2 strings (`NpcName`, `LoadoutFingerprint`) — only allocate if uncached | 60–300 per sec in heavy combat | persistence (§5.2), code-health-audit (§2.1) | Row pool + `LangNameCache` + `LoadoutFingerprint` cached on `OnLoadoutSnapshot`. |
| T2.C | `InteractionPlayer.PostUpdateBuffs` → diff loops + per-edge `BuffEventRow` + `Lang.GetBuffName` lookup (try/catch fallback) | per edge: row (~80 B) + fallback string if Lang misses | one or two per Buff-on/off edge — currently bugged so only 2 fire in 5 min; expected ~10/min normal play | persistence (§5.4.6.5), code-health-audit (§2.4) | Row pool + `LangNameCache`. Also fix the prev-buffs diff bug (baseline §4). |
| T2.D | `InteractionPlayer.PostUpdateEquips` → `LoadoutSnapshotRow` + `new List<EquipmentSlotEntry>(Player.armor.Length)` + `StringBuilder` for fingerprint | row + list + 10–20 entries + StringBuilder | on equip change, rare | persistence (§5.2), code-health-audit (§2.1) | Row pool + reusable list (sized once at install) + StringBuilder pool of size 1 (single tracker, single thread). |
| T2.E | `InteractionNpc.OnSpawn` → `NpcSpawnRow` + `source.GetType().Name` boxing + `.Substring("EntitySource_".Length)` allocation | row (~80 B) + ~24 B substring | per spawn, 5–30 per min | persistence (§5.17), code-health-audit (§2.2) | Row pool + `Dictionary<Type,string>` cache of stripped source-name. |
| T2.F | `InteractionItem.OnCreated` → `ItemCreatedRow` + `Lang.GetItemName` lookup | row + name | per craft/pickup, 0–10 per min | persistence (§5.17), code-health-audit (§2.2) | Row pool + `LangNameCache`. |
| T2.G | `ContextTransitionWatcher` → `ContextTransitionRow` on every transition | row (~96 B) + interpolated From/To strings on bossStart/bossEnd/bossSwap | per transition, 10/min observed in baseline | events-and-context (§R4, §R5) | Defer Lang resolution to alloc branch; cache `_lastBossType` as int. Row alloc itself is per-transition and acceptable since rare; pool only the row class. |
| T2.H | `PlayerDeathDetector.Capture` → `PlayerDeathRow` + `List<int>` of bosses + LiteDB `Where(...).OrderByDescending(...).Limit(1).FirstOrDefault()` synchronous query | row + list + LINQ chain pipeline | per death, 0.4/min observed | persistence (§5.18), events-and-context (§R10b) | In-RAM ring of last-N damage events, scanned allocation-free for damage-weighted attribution. |
| T2.I | `WorldSnapshotter.OnSnapshot` → `WorldSnapshotRow` + interpolated `Lang.GetNPCNameValue` | row + maybe string | every 30 s | events-and-context | Row pool + cached boss name. |
| T2.J | `DbWriterThread.Enqueue` → `DbWriteOp` struct (passed by value, no heap alloc on the happy path; the `object Payload` field boxes value-type payloads) | 0 if payload is reference; ~24 B box if payload is a struct | every event | persistence (§5.5) | Discriminated struct union, see §3.7. |

Total estimated T2 heap traffic in a baseline session (4 min 55 s, 50 spikes, 50 stalls, 354 damage-dealt, 34 spawns, 10 damage-taken, 41 loadouts, 2 buff-edges, 0 items — bugged): roughly **800 KB**. Of that:
- Row classes: ~500 events × ~96 B average = ~48 KB.
- Substring/lang allocs (uncached): ~400 calls × ~24 B = ~10 KB.
- LoadoutSnapshot lists + StringBuilders: 41 × ~256 B = ~10 KB.
- The rest is BSON serialiser / writer-thread (counts under T3).

The **441 ns/op** in baseline.md is the *per-event* cost; six new event streams in v0.5 drove it from 276 ns up to 441 ns. The fixes above bring it back under 300 ns by replacing per-event heap allocations with pool reuse.

### 1.4 T3 — per-emit (writer-thread)

Allocations the writer thread does, after it dequeues an op, on its way to LiteDB.

| # | Site | Allocates per op | Owner doc | Fix |
|---|---|---|---|---|
| T3.A | `EventJournal.AppendBatch` → `new StringBuilder(count * 256)` per batch | one SB + internal char[] | persistence (§5.7) | Cache one SB on the writer thread, `Clear()` between batches. |
| T3.B | `JsonSerializer.Serialize(line, payload, JsonOpts)` per op | intermediate string + reflection cache hits | persistence (§5.7) | Use source-generated `JsonTypeInfo<T>` from `System.Text.Json` source generator. Eliminates per-op reflection and per-op intermediate string. |
| T3.C | `Encoding.UTF8.GetBytes(buf.ToString())` per batch | byte[] of encoded length | persistence (§5.7) | Use `Utf8JsonWriter` directly into an `ArrayPool<byte>.Shared` buffer. Eliminates the intermediate string entirely. |
| T3.D | `BsonMapper.Serialize(row)` per op | document, property names, BsonValue per field | persistence (§4) | Hand-rolled BSON writer for the hottest streams (DamageDealtRow, BuffEventRow). Pre-resolved property-name BsonValues cached once. |
| T3.E | `BsonSerializer.Serialize(doc, true)` → `byte[buffer]` per Upsert | one byte[] | persistence (§4) | `ArrayPool<byte>.Shared.Rent(predicted)`; falls back to default for >85 KB. |
| T3.F | `ObjectId.NewObjectId()` reads `DateTime.UtcNow` + counter + generates 12-byte id | None heap per se, but the `DateTime.UtcNow` is a non-trivial call | persistence (§5.6) | Defer to writer-thread inside Apply rather than in row constructor. |
| T3.G | `SpikeWindowRow.ToList(float[])` × 2 per spike at drain | two `List<double>` of size 126 | spike-detection (§4.7) | Schema bump v1→v2: store as `float[]` / packed `byte[]`. Removes the float→double widening + the List wrapping. |
| T3.H | `BuildSpikeTopContributors` → `List<SpikeContributor>(0)` + sort + trim | list + 5 class instances | spike-detection (§4.8) | Fixed-size struct-array top-K (5-element insertion sort). |
| T3.I | `BuildStallTopContributors` → `List<StallContributorEntry>(5)` per stall | list + 0–5 entries | stall-detection (§5.H) | Pre-sized `StallContributorEntry[5]`. |
| T3.J | `s.Cause.ToString()` per stall (enum to string) | one string per stall | stall-detection (§5.I) | Pre-computed `string[]` indexed by enum value. |

### 1.5 T4 — per-frame (draw thread, F9 overlay)

These count only while the overlay is open. The overlay dossier estimates **~64 string allocations per frame** at steady state on the SUMMARY tab with 30 mods loaded — at 60 Hz, ~3,840 strings/sec on the draw thread.

| # | Site | Allocates per frame | Owner doc | Fix |
|---|---|---|---|---|
| T4.A | `OverlayPanel.DrawSelf` 4× `string.Format` for stat cards + ~6 for PROFILER HEALTH | ~10 strings | overlay (§4.1) | `OverviewCache` per-tab struct populated at 1 Hz Tick, drawn from cache. |
| T4.B | `OverviewTab` 32 string interpolations across donut, contributors, sparklines, ranking | ~32 strings | overlay (§4.1.1) | Same as T4.A, the largest single saving. |
| T4.C | `TreeTab` 12 rows × 4 interpolated values per row | ~48 strings | overlay (§4.1) | Row-cache filled at 1 Hz. |
| T4.D | `SpikesTab` per-row 4–5 interp + `ToString().ToLowerInvariant()` per row | ~40+ strings | overlay (§4.1) | Pre-computed lowercase reason strings keyed by enum. |
| T4.E | `OverlayDraw.FormatBytes` returns `$"{bytes:F0} B"` etc — 1 string per call | per-call cost | overlay (§4.1) | LRU cache on `(bytes >> 8)` — coarse buckets, same exact-string output. |
| T4.F | `EventsTab` 12 rows × 5–6 interp + `EventsTab.ComputeNowActiveSummary` `new List<string>(8)` + `string.Join` | many; result already cached in `_cachedNowSummary` at 1 Hz | overlay | Already cached; no fix needed. |
| T4.G | `InsightsTab.rec.Pattern.ToString()` per draw | 1 enum-string per record | overlay (§4.1) | `string[]` keyed by Pattern enum. |
| T4.H | `Confidence.ToString().ToLowerInvariant()` per `SeverityBadge.DrawConfidence` | 2 strings per badge | overlay (§4.1, §1.4) | Pre-computed lowercase enum strings. |
| T4.I | `LegacyGameInterfaceLayer` constructor + `Func<bool>` for InsertionHook | one closure if not method-group-cached | overlay (§4.6) | Move to OnModLoad, allocate once. |
| T4.J | `layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text")` `Predicate<GameInterfaceLayer>` | one delegate per call | overlay (§4.6) | Cache delegate as static field. |
| T4.K | `BasicEffect.CurrentTechnique.Passes` foreach allocates EffectPassCollection enumerator | one enumerator per frame in `DonutChart` | overlay (§4.3) | Use explicit indexer (`pass = passes[0]`). |
| T4.L | `Rectangle[] LayoutStatCards(...)` allocates a fresh array each frame | 1 array per frame | overlay (§4.6) | Pre-allocate `_layoutCache.StatCardRects` and refill in place. |
| T4.M | `IReadOnlyList<double>` boxing in `Sparkline.Render(IReadOnlyList<double>)` | implicit interface dispatch per index | overlay (§4.7) | Span overload + adapter that takes `ReadOnlySpan<double>`. |
| T4.N | `ImpactSkyline` label `.Substring(...)` per tower | up to 12 strings | overlay (§1.2) | Label cache keyed by mod-id. |

The dossier's headline: zero per-frame heap allocations at steady state after the overlay caches refill at 1 Hz from the Tick. The fix shape is uniform: **per-tab format-cache struct that the Tick fills**, the Draw reads from. Every interpolated string is paid once per second instead of 60 times.

### 1.6 T5 — per-second (1 Hz)

Insight engine, overlay cache refill, self-health refresh.

| # | Site | Allocates per pass | Owner doc | Fix |
|---|---|---|---|---|
| T5.A | `AllocationBurstDetector.Evaluate` → `new double[modCount]` per pass | one array, ~830 B for M=100 | insights-engine (§1.3) | Promote to private field, reuse. |
| T5.B | `GcPauseCulpritDetector.Evaluate` → `new double[modCount]` per pass | one array | insights-engine (§1.6) | Same — promote to field. |
| T5.C | `LoadoutCorrelatedCostDetector.Evaluate` → 5 LINQ chains, 4 `.ToList()`, `Average` delegate boxing | ~50 KB per pass at sustained M | insights-engine (§4.1) | Indexed range scans + pooled buffers. Drops to <1 KB per pass. |
| T5.D | `EventConditionalCostDetector.Evaluate` → 5 LINQ chains incl. `GroupBy`, `Where(g.Any())`, `Take(3).ToList()`, per-group `OrderBy(...).ToList()` | dominant alloc detector | insights-engine (§4.2) | Explicit pass over events, `Dictionary<int, struct>` for grouping, pooled buffers. |
| T5.E | `EventsTab._cachedNowSummary` `new List<string>(8)` + `string.Join` | one list + one string per 1 Hz refresh | overlay (§1.4) | Already at 1 Hz cadence; fine as-is. |
| T5.F | `OverlayPanel.Tick` cache refill (post-fix) | rebuild format-cache strings | overlay (§4.1) | This is the *fix* for T4 — it's a deliberate 1 Hz allocation in exchange for 60 Hz savings. |

### 1.7 T6 — per-minute / per-window (spike open, stall open, cold tier emit)

| # | Site | Allocates per occurrence | Cadence | Owner doc | Fix |
|---|---|---|---|---|---|
| T6.A | `SpikeDetector.OnTick` window open → `new SpikeWindow { PerModCatMs = new float[126], PerModCatBytes = new float[126] }` | 1–2 float[126] arrays per spike | ~11/min | spike-detection (§4.4) | Pool-backed snapshot slots, indexed by ring slot. |
| T6.B | `TickDownsampler.EmitWarm` → `TickAggregateWarm` (object) + `List<double> PerModMs` of length ~100 + `List<double>? PerModBytes` | per second cold tier and per minute warm | ~1/sec warm | metric-collection (§5.4), persistence (§5.2) | `RowPool<TickAggregateWarm>` + pooled per-mod double[] buffers, returned to pool after writer thread Apply. |
| T6.C | `TickDownsampler.EmitCold` → `TickAggregateCold` row, same shape | ~1/min | metric-collection (§5.4) | Same as T6.B with separate pool. |
| T6.D | `StallDetector` window close → `StallEventRow` | ~0.18/sec at baseline observed | stall-detection (§5.H) | Pool + pre-sized array. |

### 1.8 T7 — per-session (world-load, world-unload aggregation)

The 8.5-second UiOverlayBlocking cluster at session-end lives here. mod-lifecycle dossier owns the §4.1 SessionEnd snapshot pool that moves all aggregation off the game thread.

| # | Site | Allocates per session | Owner doc | Fix |
|---|---|---|---|---|
| T7.A | `OnWorldLoad` → `Collector`, `SessionRecorder`, `EnqueueModlistUpserts` per-mod rows × ~60, `ContextTransitionWatcher`, `WorldSnapshotter`, `PlayerDeathDetector` | ~200 small allocations | mod-lifecycle (§4.4) | Defer SessionRecorder + watchers to first `PostUpdateEverything`. |
| T7.B | `OnWorldUnload` → `BuildModAggregates` allocates `List<double>(categoryCount)` per mod, `PerSessionModAggregate` per mod, `ModCoverage` per mod, `List<TopHookEntry>` per mod | 60 × ~5 objects = ~300 objects | mod-lifecycle (§4.9), spike-detection (§4.8) | Pooled buffers, struct top-K, writer-thread relocation. |
| T7.C | `OnWorldUnload` → `BuildHookAggregates` allocates `PerSessionHookAggregate` per non-silent hook (~10k) | ~10k objects | mod-lifecycle (§4.1) | Move to writer thread post-snapshot. |
| T7.D | `OnWorldUnload` → `DrainSpikes`/`DrainStalls` per-row allocations | 50+50 rows + contributor lists | spike-detection (§4.8), stall-detection (§5.H) | Pool rows + struct contributors. |
| T7.E | `OnWorldUnload` → `SessionSummaryLogger.Write` → `new StringBuilder(512)` + 3 LINQ chains | one SB + several enumerators | mod-lifecycle | Acceptable — one-shot session-end log line. |

### 1.9 T8 — per-install / per-process

The 233 MB hook-install delta and the Mod.Load `Action<string,Exception?>` closure live here.

| # | Site | Allocates | Owner doc | Fix |
|---|---|---|---|---|
| T8.A | `ILHookInterceptor.Install` per-hook `DisplayName(type, method)` × 3 interpolated strings | ~30k strings, ~2.4 MB retained as `HookDescriptor.DisplayName` | hook-instrumentation (ALLOC-3) | Intern strings + reuse across closed-generic instantiations. Free ~1 MB. |
| T8.B | `ILHookInterceptor.Install` per-hook `new ILHook` → MonoMod's per-hook `DynamicMethodDefinition` + Cecil `ModuleDefinition` with importer caches (~5–15 KB / hook × 10k hooks) | ~60–190 MB retained | hook-instrumentation (ALLOC-1) | Dispose `DynamicMethodDefinition` immediately after install, holding only the chain's `SourceCloneIl`. Highest-impact single change in this pass. |
| T8.C | `_installedHooks = new List<ILHook>()` grows via `List.Add` from default capacity → ~14 doublings, ~80 KB final | a few hundred KB transient | hook-instrumentation (ALLOC-5) | Pre-size based on type-count estimate. |
| T8.D | `SelfHealth.MarkInstallStart/End` forces `GC.Collect(2, Forced, blocking)` × 2 | ~100–300 ms wall time | hook-instrumentation, mod-lifecycle (§4.8) | Replace blocking Gen2 with `GC.GetAllocatedBytesForCurrentThread` delta measurement. |
| T8.E | `AssemblyManager.GetLoadableTypes` called twice (once per backend) → allocates type arrays | ~80–150 MB transient | mod-lifecycle | Shared install scan. |
| T8.F | `Process.GetCurrentProcess()` in `ProfilerSelfHealth.Refresh` (if not cached) | one Process wrapper | stall-detection (§5.A), hook-instrumentation | Cache the Process instance once; or remove entirely if the only consumer can use a cheaper API. |

### 1.10 Site-to-doc index (the cross-cutting view)

A reverse map: same allocation pattern, multiple files.

| Pattern | Sites by file | Doc references |
|---|---|---|
| `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` | `MetricCollector.cs:318`, `:371`; `InteractionPlayer.cs` × 4 (damage taken, damage dealt, buff edge, loadout snapshot); `InteractionNpc.cs` × 1; `InteractionItem.cs` × 1; `ContextTransitionWatcher.cs` × 1 (transitions); `WorldSnapshotter.cs` × 1; `PlayerDeathDetector.cs` × 1; `SpikeDetector.cs` × 1 (drain); `StallDetector.cs` × 1; `SessionRecorder.cs` × 3 (start, end, downsample emits); LiteDB `ObjectId.NewObjectId()` (writer-thread, deferred); plus several utility paths. **Total ~28 sites.** | code-health-audit §14.1, metric-collection §3.4, stall-detection §3.7 |
| `new List<...>()` per-event | `InteractionPlayer.cs:262` (ActiveBuffs), `:221` (slots), Spike `BuildSpikeTopContributors`, Stall `topContribs`, `EventsTab.ComputeNowActiveSummary`, ML `BuildModAggregates`, `BuildHookAggregates`, `BuildArchive` | persistence §5.2, spike-detection §4.8, stall-detection §5.H, mod-lifecycle §4.9, code-health-audit §2.1 |
| `Lang.Get*` inline at emit time | `InteractionPlayer.cs` × 2 (`GetBuffName`, `GetProjectileName`); `InteractionNpc.cs` × 1 (`GetNpcName`); `InteractionItem.cs` × 1 (`GetItemName`); `ContextTransitionWatcher.cs` × 1 (`GetNPCNameValue` boss-name); `WorldSnapshotter.cs` × 1; `BossSampler._nameCache` (already cached) | persistence §5.16, events-and-context §R4, code-health-audit §14.2 |
| `string + int` concat (e.g. `"buff-" + buffType`, `"npc-" + type`) | `InteractionPlayer.cs:205`, `:276`; `EmitWeather` fallback `"weather:" + flag.ToString()` | code-health-audit §2.4, events-and-context §R5 |
| LINQ chains on hot/warm paths | `InteractionInsightDetectors.cs` × 14; `LoadoutCorrelatedCostDetector` × 5; `EventConditionalCostDetector` × 5; `QueryChatCommands.cs` × 9 (cold, fine); `PlayerDeathDetector.Capture` × 1 (game thread!) | insights-engine §4, code-health-audit §2.5 |
| `yield return` iterators on enumerated surfaces | `SpikeWindowsView.GetEnumerator()`; `IPersistenceStream.Enumerate*`; debug enumerations | spike-detection §4.6, stall-detection §1.5 |
| `new Func<>` / closure captures | `EventAggregator` accessors; `BiomeBitsetFunc<bool>` (closure-free, OK); `WeatherSources.All` (closure-free, OK); `layers.FindIndex(...)` predicate; `Mod.Load` log delegate; insights detector lambdas | overlay §4.6, events-and-context §1.4, mod-lifecycle §1.9 |
| BSON serialiser allocations per Upsert | `EventJournal.AppendBatch` (StringBuilder + JsonSerializer.Serialize + UTF8.GetBytes); per-stream `BsonMapper.Serialize` | persistence §5.7 |
| `Process.GetCurrentProcess()` | `ProfilerSelfHealth.Refresh`; `StallDetector` rejected; never per-tick | stall-detection §5.A |
| `Substring` allocations | `InteractionNpc.OnSpawn` `source.GetType().Name.Substring(...)`; `ImpactSkyline` label substring; various | persistence §5.17, overlay §1.2 |
| Enum `.ToString()` | `Confidence.ToString().ToLowerInvariant()` × overlay; `s.Cause.ToString()` × stall-drain; `rec.Pattern.ToString()` × insights-tab | overlay §1.4, stall-detection §5.I |
| `IReadOnlyList<T>` boxing | `Sparkline.Render(IReadOnlyList<double>)`; `SpikeDetector.Windows` typed as `IReadOnlyList<SpikeWindow>` | overlay §4.7, spike-detection §4.9 |

This is the cross-cutting view. Every one of these patterns lands in §3 as a shared helper.

---

## 2. Aggregate budgets per tier

This section proposes a **bytes-per-tier ceiling** for the post-pass build, sums the current allocations against it, and shows the headroom each shared fix opens.

### 2.1 Per-tier budget proposal

The targets below assume the v0.6 pass lands the recommendations in §1 and §3. They are framed as *additive ceilings* — the profiler may not exceed these without explicitly raising the budget in a future pass.

| Tier | Cadence | Current (v0.5) | Target (v0.6) | Strict invariant? |
|---|---|---:|---:|---|
| T1 (per-hook) | up to 50k/tick | 0 B/call | 0 B/call | **Yes (Invariant 2)** |
| T0 (per-tick frame) | 60/sec | ~0 B + 2 `DateTimeOffset` struct copies (not heap) | 0 B/tick heap | **Yes** |
| T2 (per-event game thread) | sustained ~30/sec, peak ~100/sec | ~600 B per damage-dealt + ~200 B per buff edge + variable | < 64 B per event (post-pool) | **No — budgeted** |
| T3 (per-emit writer thread) | tracks T2 | ~400 B/op (StringBuilder + JSON intermediate + byte[]) | < 64 B/op (pooled SB + Utf8JsonWriter + ArrayPool) | **No — budgeted** |
| T4 (per-frame draw thread) | 60/sec when overlay open | ~64 strings = ~4 KB/frame | 0 B/frame at steady state | **Yes (after cache fills)** |
| T5 (per-second) | 1/sec | ~50 KB per insight pass (LINQ) | < 4 KB total per second | **No — budgeted** |
| T6 (per-window) | 11/min spikes, ~10/min stalls | ~2 KB per spike + ~200 B per stall | 0 B per window (pool-backed) | **Yes (after pool lands)** |
| T7 (per-session aggregation) | session-end | ~200 KB in bursts on game thread | <= 200 KB total, all on writer thread | **No — budgeted** |
| T8 (per-install) | once | 233 MB | < 80 MB | **No — budgeted** |

### 2.2 Headroom from each shared fix

| Shared fix (§3) | Sites closed | Per-tier delta |
|---|---:|---|
| §3.1 `UnixMsNow()` helper | 28 sites | T0/T2/T3 latency win, no heap delta (saves ~50–80 ns × call) |
| §3.2 `LangNameCache` | ~7 sites | T2 saves ~24 B per uncached miss; eliminates `try/catch` paths |
| §3.3 `RowPool<T>` | 8 row types (DamageTaken/Dealt, BuffEvent, LoadoutSnapshot, NpcSpawn, ItemCreated, SpikeWindow, StallEvent) | T2/T3 ~96 B × every event = the largest single delta |
| §3.4 `ListPool<T>` for transient lists (ActiveBuffs, slots, contributors, modBytes) | ~10 sites | T2/T5 ~40 B + backing array per list |
| §3.5 `ModOwnerCache` | 2 sites (InteractionPlayer NPC owner, InteractionItem item owner) | T2 small but eliminates repeated reflection |
| §3.6 `EnumStringTable` | 5 sites (Confidence, Cause, Pattern, Outcome, Mode) | T4 saves ~2 strings × 60 Hz = 7 KB/sec |
| §3.7 `DbWriteOp` discriminated union | every event | T2/T3 saves the box for value-type payloads (~24 B × every event) |
| §3.8 `Utf8JsonWriter` + `ArrayPool` journal | every batch | T3 saves ~3 allocs per op |
| §3.9 `JsonTypeInfo<T>` source-gen | every batch | T3 reflection elimination |
| §3.10 `ListSnapshotPool<T>` for warm/cold downsample rows | T6 | one alloc per second avoided |

### 2.3 Headroom against baseline.md

Cross-referencing baseline.md row by row, here is what the §1 + §3 stack moves:

| Baseline metric | v0.5 | Target | Mechanism |
|---|---|---|---|
| Game-thread enqueue latency | 441 ns/op | < 200 ns/op | §3.3 RowPool + §3.7 struct union → no heap alloc on enqueue → enqueue is now Channel.TryWrite (~30 ns) + pool rent (~10 ns) + field fill (~60 ns) ≈ 100 ns. Add ~60 ns for the helper call into UnixMsNow. |
| Writer-thread sustained drain | 314 ops/sec | > 1,000 ops/sec | §3.8 + §3.9 + per-stream binary frame (persistence §5.7) lift writer-side ops/sec ~3× by eliminating the per-op JSON serialiser reflection + the StringBuilder + the UTF8 transcoding. |
| 10-min session DB size | 1,064 KB | < 600 KB | persistence §4 packed-array encoding for spike snapshots; binary journal; per-event compact payload. |
| End-of-session main-thread stall | 8.5 s | 0 | mod-lifecycle §4.1 SessionEndSnapshot moves all aggregation to writer thread. |
| Hook install delta | 233 MB / 10,258 hooks | < 80 MB | hook-instrumentation ALLOC-1 disposes per-hook Cecil ModuleDefinition after install (frees ~60–190 MB). |
| Avg per-tick PerformanceProfiler cost | 0.27 ms | < 0.10 ms | allocation-tracking §5.13 + §5.14 + §5.3 (alloc/timing path tightening) + §3.1 UnixMsNow + spike §4.5 (frame-only) |

The cross-cutting takeaway: **the largest single contributor to the 441 ns enqueue regression is per-event row allocation, not per-event row computation**. Pooling rows alone (one fix shape, ten sites) drops the dominant cost.

---

## 3. Shared-infrastructure proposals

### 3.1 `UnixMsNow` — one helper, 28 sites

**Problem.** Twenty-eight call sites do `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`. Each is ~50–80 ns, dominated by the OS clock syscall. None of them needs millisecond accuracy of the wall clock — they need a monotonic timestamp that the writer thread can serialise.

**Helper.**

```csharp
internal static class Time
{
    private static long _wallOriginUnixMs;       // captured DateTimeOffset.UtcNow at session start
    private static long _originStopwatchTicks;   // captured Stopwatch.GetTimestamp() at session start
    private static double _msPerStopwatchTick;   // 1000.0 / Stopwatch.Frequency

    public static void Reset()
    {
        _wallOriginUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _originStopwatchTicks = Stopwatch.GetTimestamp();
        _msPerStopwatchTick = 1000.0 / Stopwatch.Frequency;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long UnixMsNow()
    {
        long delta = Stopwatch.GetTimestamp() - _originStopwatchTicks;
        return _wallOriginUnixMs + (long)(delta * _msPerStopwatchTick);
    }
}
```

**Cost.** ~5 ns per call (one Stopwatch FCall + one multiply + one add) vs ~50–80 ns for `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds`. Net saving across 28 sites at typical cadence (say 60/sec sustained, mostly in T2/T3): ~3–4 µs/sec on the game thread.

**Cross-doc anchor.** code-health-audit §14.1 names the helper. stall-detection §5.F defers the call on the per-tick path. metric-collection §3.4 documents the cost. persistence §5.6 defers `ObjectId.NewObjectId` for the same reason.

**Drift risk.** The wall-clock origin drifts from real-world clock by the difference between `Stopwatch.Frequency`-based monotonic time and the OS clock. Over a session (~hours), this could accumulate to ~1 s, which would show in `unixMs` timestamps as a small offset from real time. Mitigation: re-anchor every minute via a writer-thread refresh (no game-thread cost).

### 3.2 `LangNameCache` — id-keyed string arrays

**Problem.** Five distinct `Lang.Get*` lookup categories (`GetBuffName`, `GetItemName`, `GetNpcName`, `GetProjectileName`, `GetNPCNameValue`) are called inline from emitters across 7+ sites. Each call is a dictionary lookup + `LocalizedText.Value` field read (no heap alloc per se), plus the `try/catch` fallback for modded ids that allocates a fallback string.

**Helper.**

```csharp
internal static class LangNameCache
{
    private static string[] _buffNames;
    private static string[] _itemNames;
    private static string[] _npcNames;
    private static string[] _projectileNames;

    public static void PostSetupContent()
    {
        _buffNames = BuildArray(BuffID.Count + Mod.ContentInstance<ModBuff>().Count(), i => Lang.GetBuffName(i));
        _itemNames = BuildArray(ItemID.Count, i => Lang.GetItemName(i).Value);
        _npcNames = BuildArray(NPCID.Count + modded, i => Lang.GetNPCName(i).Value);
        _projectileNames = BuildArray(ProjectileID.Count + modded, i => SafeProjectileName(i));
    }

    public static string Buff(int type)       => (uint)type < (uint)_buffNames.Length ? _buffNames[type] : Fallback("buff", type);
    public static string Item(int type)       => (uint)type < (uint)_itemNames.Length ? _itemNames[type] : Fallback("item", type);
    public static string Npc(int type)        => (uint)type < (uint)_npcNames.Length ? _npcNames[type] : Fallback("npc", type);
    public static string Projectile(int type) => (uint)type < (uint)_projectileNames.Length ? _projectileNames[type] : Fallback("proj", type);

    private static readonly ConcurrentDictionary<(string, int), string> _fallbacks = new();
    private static string Fallback(string kind, int type) =>
        _fallbacks.GetOrAdd((kind, type), k => $"{k.Item1}-{k.Item2}");
}
```

**Why it works.** Each name is allocated once at `PostSetupContent` and reused for the session. The fallback dictionary handles the edge case of mods that register content after `PostSetupContent` (rare; cached per (kind, type) tuple so each id allocates once for the session). The `(uint)type < (uint)array.Length` bounds check is zero-extra-cost in the JIT.

**Sites closed.** `InteractionPlayer.cs` × 2 (try/catch removed), `InteractionNpc.cs`, `InteractionItem.cs`, `ContextTransitionWatcher.cs`, `WorldSnapshotter.cs`, the existing `BossSampler._nameCache` (which replaces its private Dictionary with a slice of `_npcNames`). **7 sites, one cache.**

**Cross-doc anchor.** code-health-audit §14.2 names the cache. persistence §5.16 specifies the per-event win. events-and-context §R6 already proposes a `string[]` keyed cache for boss names — this generalises it.

**Reconciliation.** events-and-context §R6 proposes a per-tab `_nameByType` array; persistence §5.16 proposes a `BuffNameCache`; insight-engine renderers also need it. **One cache, all consumers**.

### 3.3 `RowPool<T>` — generic per-event row pool

**Problem.** Every event emit allocates a new row class (`DamageTakenRow`, `DamageDealtRow`, `BuffEventRow`, `LoadoutSnapshotRow`, `NpcSpawnRow`, `ItemCreatedRow`, `ContextTransitionRow`, `WorldSnapshotRow`, `PlayerDeathRow`, `SpikeWindowRow`, `StallEventRow`, `TickAggregateWarm`, `TickAggregateCold`, `TickAggregateArchive`). At baseline cadence ~500 events / 5 min × ~96 B = ~48 KB session, but the *enqueue regression* is the headline cost — 441 ns/op of which a large chunk is the SOH bump-allocator.

**Helper.**

```csharp
internal static class RowPool<T> where T : class, new()
{
    private static readonly ConcurrentBag<T> _pool = new();
    private static int _approxAvailable;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T Rent()
    {
        if (_pool.TryTake(out var r)) { Interlocked.Decrement(ref _approxAvailable); return r; }
        return new T();   // fallback when pool is empty — never blocks, never throws
    }

    public static void Return(T r)
    {
        if (_approxAvailable >= MaxPoolSize) return;
        Reset(r);
        _pool.Add(r);
        Interlocked.Increment(ref _approxAvailable);
    }

    // Each row type implements IPoolReset that zeroes fields the next Rent will not overwrite.
    private static void Reset(T r) { (r as IPoolReset)?.Reset(); }

    private const int MaxPoolSize = 128;
}
```

**Lifecycle.**

```
Game thread:   row = RowPool<DamageDealtRow>.Rent();
               row.UnixMs = Time.UnixMsNow();
               row.NpcType = npc.type;
               ...
               db.Enqueue(DbWriteOp.Insert(stream, row));

Writer thread: row = op.Row;
               BsonSerialize(row);
               db.WriteToStream(row);
               RowPool<DamageDealtRow>.Return(row);     // pool ownership returns
```

**Why the return point is the writer thread, not the game thread.** The journal serialises the row before the writer can return it (persistence §5.2). The writer-thread Apply path is the natural ownership inflection point. Mishandled returns turn into use-after-free style bugs; the explicit Reset on Rent (not on Return) double-protects against stale fields if the previous Apply forgot to clear something.

**Per-tick alloc impact.** Zero on the steady-state happy path. Pool empty (cold start, burst): falls back to `new T()`, identical to today's behaviour. Pool warm (steady): zero heap alloc per event.

**Cross-doc anchor.** persistence §5.2 specifies the pattern. mod-lifecycle §4.9 wants pooled rows for the aggregate-build path. spike-detection §4.4 pool-backs `SpikeWindow` snapshots; this generalises to all row types.

**Reconciliation.** persistence §5.2 proposes `ConcurrentBag<T>`; spike-detection §4.4 proposes a slot-indexed pool (faster for known-fixed count). Both are correct in their context — `RowPool<T>` for variable-cadence event rows (where bag is fine), and `SlotPool<T>` for fixed-count ring slots. Provide both.

### 3.4 `ListPool<T>` — transient list pool

**Problem.** Per-event transient lists: `ActiveBuffs = new List<int>()` (InteractionPlayer), `slots = new List<EquipmentSlotEntry>(...)` (InteractionPlayer equip), `topContribs = new List<StallContributorEntry>(5)` (StallDetector), `bosses = new List<int>()` (PlayerDeathDetector), `_perModBytes` per-pass-allocated arrays in insight detectors. Each list is filled, serialised, then garbage-collected.

**Helper.**

```csharp
internal static class ListPool<T>
{
    private static readonly ConcurrentBag<List<T>> _pool = new();
    public static List<T> Rent(int minCap = 0)
    {
        if (_pool.TryTake(out var l)) { if (l.Capacity < minCap) l.Capacity = minCap; return l; }
        return new List<T>(Math.Max(minCap, 4));
    }
    public static void Return(List<T> l)
    {
        if (l.Capacity > 256) return;   // don't pool huge lists; let them GC
        l.Clear();
        _pool.Add(l);
    }
}
```

**Reconciliation note.** persistence §5.2 mentions row-pool returns happen on the writer thread. Lists embedded in rows ride along with the row's lifecycle — when the row returns to its pool, the embedded list also returns to ListPool. Reset hooks on rows call `ListPool<T>.Return(row.ActiveBuffs)` then null the field.

### 3.5 `ModOwnerCache` — cached `OwningModName` resolution

**Problem.** `InteractionNpc.OnSpawn` and `InteractionItem.OnCreated` resolve "which mod owns this npc/item/projectile id" by walking `ModContent.GetContent<*>()` or reading reflection on the source's type. The result is stable for the session.

**Helper.**

```csharp
internal static class ModOwnerCache
{
    private static string[] _ownerByNpcType;
    private static string[] _ownerByItemType;
    private static string[] _ownerByProjectileType;
    private static readonly Dictionary<Type, string> _ownerByEntitySourceType = new();

    public static void PostSetupContent() { /* one walk over ModContent.GetContent<>(...) */ }

    public static string Npc(int type) => _ownerByNpcType[type];
    public static string Item(int type) => _ownerByItemType[type];
    public static string Projectile(int type) => _ownerByProjectileType[type];
    public static string FromEntitySource(IEntitySource src)
    {
        var t = src.GetType();
        if (_ownerByEntitySourceType.TryGetValue(t, out var name)) return name;
        // First-spawn-per-type allocates; cached thereafter
        name = ResolveOwnerFromType(t);
        _ownerByEntitySourceType[t] = name;
        return name;
    }
}
```

**Cross-doc anchor.** persistence §5.17, code-health-audit §2.2.

### 3.6 `EnumStringTable` — enum-to-string without `.ToString()`

**Problem.** Five enum types are converted to strings on hot paths: `Confidence` (overlay), `StallCause` (stall drain + UI), `InsightPattern` (insights tab), `BossOutcome` (transition row), `ProfilerMode` (overlay). Each `.ToString()` boxes the enum and walks the value→name table.

**Helper (pattern for each enum, generated or hand-written).**

```csharp
internal static class CauseNames
{
    private static readonly string[] _names = { "TickStall", "GcPause", "MainThreadStall",
                                                "UiOverlayBlocking", "JitCompile", "DiskIO", "Unknown" };
    private static readonly string[] _lower = { "tickstall", "gcpause", "mainthreadstall",
                                                "uioverlayblocking", "jitcompile", "diskio", "unknown" };
    public static string Name(StallCause c) => _names[(int)c];
    public static string Lower(StallCause c) => _lower[(int)c];
}
```

**Cross-doc anchor.** overlay §1.4, §4.1; stall-detection §5.I.

### 3.7 `DbWriteOp` discriminated struct union

**Problem.** `DbWriteOp.Payload` is `object`, which boxes value-type payloads (~24 B per op). All event rows are reference types today, so this *doesn't* bite — but the §3.3 RowPool returns might leave subtle ownership cycles. The cleaner design has DbWriteOp carry the row payload by value when the row is itself a struct, by reference otherwise.

**Proposed shape.**

```csharp
internal readonly struct DbWriteOp
{
    public readonly DbOpKind Kind;
    public readonly int StreamId;
    public readonly object? RefPayload;   // for class rows
    public readonly TickAggregatePayload TickAggregate;  // by-value for the hottest stream
    public readonly long DeferredMs;      // for ops that need UnixMs at writer-thread time
}
```

This lets the hottest stream — `TickAggregateWarm` at 1 Hz — carry its payload inline as a 64-byte struct in the channel. The channel's `ConcurrentQueueSegment` slot grows, but no heap alloc per op.

**Cross-doc anchor.** persistence §5.5.

**Reconciliation.** allocation-tracking didn't touch DbWriteOp; persistence did. The two converge on the same answer (struct union); this proposal generalises persistence's specific case.

### 3.8 `JournalEmitter` — pooled UTF-8 writer

**Problem.** `EventJournal.AppendBatch` allocates: one StringBuilder + per-op JsonSerializer.Serialize string + Encoding.UTF8.GetBytes per batch = ~3 allocations per op × 64-op batches = ~192 allocations per drain.

**Helper.**

```csharp
internal sealed class JournalEmitter
{
    private readonly byte[] _scratch = new byte[64 * 1024];   // one alloc, lifetime of writer thread
    private readonly Utf8JsonWriter _writer;
    private readonly FileStream _journal;

    public void AppendBatch(ReadOnlySpan<DbWriteOp> ops)
    {
        var bw = new ArrayBufferWriter<byte>(_scratch);
        _writer.Reset(bw);
        foreach (ref readonly var op in ops)
        {
            _writer.WriteStartObject();
            WriteOp(_writer, in op);
            _writer.WriteEndObject();
            _writer.Flush();
            _journal.Write(bw.WrittenSpan);
            bw.Clear();
        }
        _journal.Flush();
    }
}
```

**Cross-doc anchor.** persistence §5.7 (binary journal frame) goes further than this; this is the *intermediate* step that keeps the journal text-readable while eliminating the per-op alloc.

### 3.9 `JsonTypeInfo<T>` source generation

**Problem.** `JsonSerializer.Serialize(payload, payload.GetType(), JsonOpts)` reflects per-call to discover properties. The reflection cache amortises but per-op alloc still happens on the JIT-emitted invocation paths.

**Fix.** Use `System.Text.Json` source generator:

```csharp
[JsonSerializable(typeof(DamageDealtRow))]
[JsonSerializable(typeof(DamageTakenRow))]
// ... all row types
internal partial class JournalJsonContext : JsonSerializerContext { }
```

Then `JsonSerializer.Serialize(writer, payload, JournalJsonContext.Default.GetTypeInfo(payload.GetType()))` — no reflection, no per-op alloc, fully AOT-friendly.

**Cross-doc anchor.** persistence §5.7.

### 3.10 `ListSnapshotPool<T>` — pooled list snapshots for downsample tiers

**Problem.** `TickDownsampler.EmitWarm` allocates a `List<double>` of length ~100 (per-mod ms) every 1 sec, and an optional second one for bytes. Once per minute it allocates the cold-tier equivalent. Drain replicates this in writer-thread aggregate-build paths.

**Helper.**

```csharp
internal sealed class ListSnapshotPool
{
    private readonly Stack<List<double>> _doubleListPool = new();
    private readonly Stack<List<float>> _floatListPool = new();

    public List<double> RentDoubleList(int capacity)
    {
        if (_doubleListPool.TryPop(out var l)) { if (l.Capacity < capacity) l.Capacity = capacity; return l; }
        return new List<double>(capacity);
    }
    public void Return(List<double> l) { l.Clear(); _doubleListPool.Push(l); }
}
```

**Cross-doc anchor.** persistence §5.2 (pool TickAggregateWarm). spike-detection §4.8 (DrainSpikes alloc reduction).

### 3.11 `BoolIndex` — fixed-size set for per-tick membership

**Problem.** `PostUpdateBuffs` diff uses `Array.IndexOf` twice over Player.buffType (length 22) — that's O(n²) per tick. Some other diffs use HashSet/Dictionary that allocates buckets on the first add per session, even with `.Clear()`.

**Helper.** A `BoolIndex` is a `bool[]` keyed by id with a small clear-mark-set protocol:

```csharp
internal struct BoolIndex
{
    public bool[] InSet;
    public int[] PresentKeys;
    public int Count;

    public void Add(int key) { if (!InSet[key]) { InSet[key] = true; PresentKeys[Count++] = key; } }
    public bool Contains(int key) => InSet[key];
    public void Clear() { for (int i = 0; i < Count; i++) InSet[PresentKeys[i]] = false; Count = 0; }
}
```

The `Clear` only zeroes the cells that were set, not the whole bool array — O(set size), not O(domain size).

**Cross-doc anchor.** persistence §5.13 (buff-diff fix); events-and-context (latch dirty).

### 3.12 Summary of shared helpers

| Helper | Sites closed | Tier impact | Owner |
|---|---:|---|---|
| §3.1 `Time.UnixMsNow` | 28 | T0/T2 latency | persistence + metric-collection |
| §3.2 `LangNameCache` | 7 | T2 alloc + latency | persistence |
| §3.3 `RowPool<T>` | 14 row types | T2/T3 alloc | persistence (+ spike, stall, ml) |
| §3.4 `ListPool<T>` | ~10 sites | T2/T5 alloc | persistence + insights |
| §3.5 `ModOwnerCache` | 2 sites | T2 latency | persistence |
| §3.6 `EnumStringTable` × 5 | 5 enums | T4 alloc | overlay + stall |
| §3.7 `DbWriteOp` struct union | per-op | T2 alloc | persistence |
| §3.8 `JournalEmitter` | per-batch | T3 alloc | persistence |
| §3.9 `JsonTypeInfo<T>` srcgen | per-batch | T3 reflection | persistence |
| §3.10 `ListSnapshotPool` | per-second | T6 alloc | metric-collection + persistence |
| §3.11 `BoolIndex` | buff-diff + similar | T0 alloc + CPU | persistence |

---

## 4. Cross-system reconciliations

### 4.1 Spike snapshot pool vs. row pool

**Conflict.** spike-detection §4.4 proposes a fixed-50-slot snapshot pool indexed by ring slot. persistence §5.2 proposes a generic `RowPool<T>` for all rows including `SpikeWindowRow`.

**Reconciliation.** Both. The spike *snapshot arrays* (`PerModCatMs float[126]`) are pool-backed by `SpikeDetector._msSnapshotPool` (fixed-50). The spike *row* (`SpikeWindowRow`) at drain time is pool-backed by `RowPool<SpikeWindowRow>` (variable). One pool per concern. The snapshot pool is owned by the detector (stable identity, lifecycle = world); the row pool is owned by the writer thread (lifecycle = session). No overlap.

### 4.2 `LangNameCache` vs. per-doc partial caches

**Conflict.** Three docs propose three caches:
- events-and-context §R6 — `string[]` for NPC names, in BossSampler.
- persistence §5.16 — `BuffNameCache` and `ItemNameCache`.
- insights-engine (renderer paths) — needs item/buff/projectile names.

**Reconciliation.** One `LangNameCache` (§3.2 in this file). The existing `BossSampler._nameCache` becomes a slice of `LangNameCache._npcNames`. The persistence-side caches consolidate into `LangNameCache` calls. Renderer paths use the same. **One cache, four id-spaces, all consumers.**

### 4.3 `UnixMsNow` and `Time` origin in `ObjectId.NewObjectId`

**Conflict.** persistence §5.6 wants to defer `ObjectId.NewObjectId` to the writer thread inside Apply (because it reads `DateTime.UtcNow`). §3.1 `Time.UnixMsNow` is a cheaper read. If the writer thread uses `Time.UnixMsNow` to fill `ObjectId`, the LiteDB index ordering is preserved because both clocks are monotonic.

**Reconciliation.** Writer thread uses `Time.UnixMsNow` to seed the per-process counter inside `ObjectId.NewObjectId` (LiteDB's API doesn't expose this, so we may need to subclass or work around — see persistence §5.6 for the chosen path). Game-thread emitters do **not** generate ObjectIds; they enqueue ops with `Id = ObjectId.Empty` and the writer fills.

### 4.4 `DateTimeOffset.UtcNow` removal sequence

**Conflict.** Removing `DateTimeOffset.UtcNow` from 28 sites in one mechanical sweep risks landing a clock-drift bug across the session. metric-collection §3.4, stall-detection §5.F, persistence §5.6, code-health-audit §14.1, events-and-context all touch this.

**Reconciliation.** Sequence:

1. Land `Time.UnixMsNow` helper + `Time.Reset()` call at `OnWorldLoad`.
2. Verification test: assert `|Time.UnixMsNow() - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()| < 100 ms` after 60 s of synthetic ticks.
3. Sweep replace all 28 sites in one commit (mechanical, low risk after the verification gate).
4. Add a writer-thread refresh that re-anchors `_wallOriginUnixMs` and `_originStopwatchTicks` once per minute (drift mitigation).

### 4.5 Insight detector LINQ vs. spike-detector snapshot reads

**Conflict.** insights-engine §4.1 wants `LoadoutCorrelatedCostDetector` to read indexed ranges from LiteDB. spike-detection §4.7 wants the spike-window storage format to change (v1→v2 packed bytes). The insight detector also reads spike windows.

**Reconciliation.** The insight detector reads spikes via `SpikeDetector.Windows[]` (the in-RAM `IReadOnlyList`), not via LiteDB query. The schema bump (§4.7) only affects writer-thread on-disk format; the in-RAM `SpikeWindow.PerModCatMs` stays `float[]`. The insight detector's read path is unchanged.

### 4.6 `Process.GetCurrentProcess()` ownership

**Conflict.** stall-detection §5.A wants to drop the Process wrapper entirely (replaced by `ThreadCpuTime` API on Linux/macOS/Windows). hook-instrumentation has a `_self?.Refresh()` in `ProfilerSelfHealth` once per second.

**Reconciliation.** stall-detection's removal happens in its own code path (`StallDetector.CaptureBaseline`); ProfilerSelfHealth keeps its single cached Process instance for the 1 Hz refresh (which exists for the SELF tab heap/working-set columns, not for stall detection). One Process instance lives in ProfilerSelfHealth; StallDetector uses `ThreadCpuTime` and does not reach the Process at all.

### 4.7 IL `EnterCpuAlloc` vs. `Frame` data shape

**Conflict.** allocation-tracking §5.13 proposes co-locating ticks+bytes in one `Cell` struct. allocation-tracking §5.14 proposes embedding ModId/CategoryId into Frame. hook-instrumentation CPU-2 proposes the same pair.

**Reconciliation.** They are the same proposal stated from two angles. One change, one PR, two anchors. Verification matches both files' acceptance tests.

### 4.8 Test-harness allocation contracts

**Conflict.** test-harness §6 defines `Tick_*_AllocatesZeroBytes` tests for `MetricCollector.Tick`, `ProbeStack.Enter/Leave`, `RingBuffer<T>.Push`. allocation-tracking §1.8 names a GC-fed round-trip test as a separate concern.

**Reconciliation.** test-harness §6 owns the *zero-alloc contracts* (per-tick path). allocation-tracking §1.8 owns the *attribution-correctness round-trip* (per-hook path). Both ship; they test different invariants on the same surface.

---

## 5. Hidden-allocation finds

Patterns the per-system docs flagged briefly or missed entirely. Each is verified or noted as needing verification.

### 5.1 `[ThreadStatic]` first-touch allocations

`ProbeStack` uses `[ThreadStatic] private static Frame[]? _stack;`. The first time a thread touches it, the array (32 × 24 B = 768 B) is allocated. tModLoader spawns multiple threads (update thread, draw thread, occasional async hooks). Each thread pays one allocation on first hook fire. **Verdict:** acceptable — one alloc per thread for the process lifetime. Document but do not change.

hook-instrumentation §4 notes this with a CPU-3 sketch to consolidate TLS into a `ProbeStackState` class object. The same first-touch allocation happens; not worse, not better.

### 5.2 `yield return` iterator allocations

Sites verified:
- `SpikeDetector.Windows.GetEnumerator()` — `yield return` (spike-detection §4.6).
- `StallDetector.RecentEvents` (probable) — needs source check.
- `IPersistenceStream.Enumerate*` paths in legacy replay code.

Each `foreach` over a `yield return` iterator allocates one enumerator class per call. Two production readers in spike use the indexer; the latent allocation never fires today. Fix: struct enumerator (covered by spike-detection §4.6; same fix applies elsewhere).

### 5.3 `IReadOnlyList<T>` boxing on hot indexers

**Site.** `Sparkline.Render(IReadOnlyList<double> values)`. Each indexer call goes through interface dispatch. If `values` is a `double[]`, the JIT can devirtualise — but only if it sees the concrete type. Through a method parameter it can't.

**Fix.** Provide a `ReadOnlySpan<double>` overload; route per-frame paths through it. The `IReadOnlyList<double>` overload remains for compatibility but is no longer hot.

**Cross-doc anchor.** overlay §4.7.

### 5.4 LiteDB `BsonExpression` parsing per query

**Site.** `db.Stream.Query().Where(x => x.SessionId == sid)` — the lambda is parsed into a BsonExpression on every call. Cached by LiteDB internally? **Needs verification** — insights-engine §4 implies yes, allocation profile would tell. If cached, ignore; if not, precompile expressions at startup.

### 5.5 LiteDB `ObjectId.NewObjectId` reads `DateTime.UtcNow`

**Site.** Every event row's auto-id construction. Already on the §3.1 sweep (deferred to writer thread).

### 5.6 `Substring` in `InteractionNpc.OnSpawn`

**Site.** `source.GetType().Name.Substring("EntitySource_".Length)` per spawn. Allocates one string per spawn. Fix: cache per source type (§3.5).

### 5.7 BSON serialiser per-Upsert byte buffer

**Site.** `BsonSerializer.Serialize` allocates one byte[] per Upsert. ArrayPool fix is straightforward (persistence §5.7).

### 5.8 `Process.GetCurrentProcess()` allocates

**Site.** `ProfilerSelfHealth.Refresh` (if not cached). The Process wrapper is a managed object holding a native handle; it allocates ~280 B per call. **Currently cached** in `_self` field. Verify the cache holds across reloads. Fix: hold instance for the process lifetime, refresh in place.

### 5.9 LiteDB index `BsonValue` per string key

**Site.** The `LoadoutFingerprint` index on the `damageDealt` collection. Each row indexed allocates a `BsonValue` wrapping the string. Fix: persistence §4 — replace string-keyed index with int-keyed via a session-local string-intern table.

### 5.10 Implicit `string + int` concat

**Sites.**
- `"buff-" + buffType` in `InteractionPlayer.cs` catch branch — boxes int via `Int32.ToString()` + concat.
- `"npc-" + type`, `"proj-" + type`, `"item-" + type` — same.
- `"weather:" + flag.ToString()` in `EventAggregator` BumpBucket (events-and-context §R5).

Fix: the `LangNameCache._fallbacks` dictionary in §3.2 holds the canonical fallback strings; concat happens once per (kind, type) tuple.

### 5.11 `ConcurrentDictionary` per-item allocations

**Sites.** `ModOwnerCache._ownerByEntitySourceType` is a Dictionary; if it grew under concurrent fill (worker threads racing in `OnSpawn`), inserts would allocate node buckets. Since spawns are on the game thread, no race; a plain Dictionary suffices.

### 5.12 `EffectPassCollection.GetEnumerator()`

**Site.** `DonutChart`'s `foreach (EffectPass pass in e.CurrentTechnique.Passes)` allocates an enumerator each frame. Fix: indexer (overlay §4.3).

### 5.13 `Predicate<T>` from `layers.FindIndex`

**Site.** `layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text")` allocates a delegate per call (closure-free since the literal is constant, but C# compiler caches only for instance-static patterns reliably; needs verification). Fix: cache static Predicate field (overlay §4.6).

### 5.14 `LegacyGameInterfaceLayer` constructor capture

**Site.** `new LegacyGameInterfaceLayer(name, () => DrawSelf(), ...)` — the lambda captures `this`, so it's a closure. C# 11+ method-group conversion caches it, but if the codebase still uses lambda syntax it allocates per construction. Constructor is called once at OnModLoad; one-shot cost.

### 5.15 `Func<bool>` / `Func<object?>` over reflection in `SubworldProbe.Sample`

**Site.** Three `MethodInfo.Invoke` / `PropertyInfo.GetValue` calls per slow tick. The Invoke boxes the bool return into a heap object. Fix: events-and-context §R3 — compile delegates once.

### 5.16 `BitArray` reflection field-fetch per sample

**Site.** `Player.modBiomeFlags` via `FieldInfo.GetValue(player)` every 10 Hz. The field is a `BitArray` (reference), no box, but the `RuntimeFieldHandle` dispatch costs ~50 ns. Fix: events-and-context §R2.

### 5.17 `string.Format` vs interpolated handlers

**Note.** Interpolated strings on .NET 8 (`$"{x:F2}"`) compile to `DefaultInterpolatedStringHandler` which uses an ArrayPool char buffer internally — so the *intermediate buffer* doesn't allocate, but the **resulting string** still does. Killing it requires a per-tab format cache filled at 1 Hz (overlay §4.1).

### 5.18 `dnSpy`-visible Cecil ldc.i4 short-form check

**Note.** allocation-tracking §5.6 — verify Cecil already emits short-form for hookId < 128. If not, ~3 IL bytes per hook × 10k hooks = ~30 KB install-time saving. Marginal.

### 5.19 `Mod.Logger` per-call format

**Note.** `Mod.Logger.Info($"foo {bar}")` allocates the interpolated string before passing to Logger. If Logger filters by level, the allocation is wasted. Fix: gate behind `if (Logger.IsInfoEnabled)` or use the parameterised overload. **Verify**: which Logger interface tModLoader exposes. Likely log4net under the hood which supports `Logger.Info("foo {0}", bar)` deferred formatting.

### 5.20 Reflection-based `AssemblyManager.GetLoadableTypes(mod.Code)`

**Site.** Called twice per mod during install (one per backend). Returns a new `Type[]` each call. Fix: shared install scan (mod-lifecycle §4.5, hook-instrumentation INST-2).

---

## 6. Master allocation roadmap

Phased order, with cross-system dependencies surfaced. Each phase ends with a verification step.

### 6.1 Phase α — Build the shared infrastructure (2–3 days)

| Step | Helper | Files | Sites unlocked |
|---|---|---|---|
| α1 | `Time.UnixMsNow` + `Time.Reset()` | new `Profiling/Time.cs` | 28 sites in subsequent phases |
| α2 | `LangNameCache` | new `Profiling/LangNameCache.cs`, called from `PostSetupContent` | 7 sites |
| α3 | `RowPool<T>` + `IPoolReset` interface | new `Profiling/Pools/RowPool.cs` | 14 row types |
| α4 | `ListPool<T>` | new `Profiling/Pools/ListPool.cs` | 10 sites |
| α5 | `ModOwnerCache` | new `Profiling/ModOwnerCache.cs` | 2 sites |
| α6 | `EnumStringTable` (5 enums) | new `Profiling/EnumStringTable.cs` | 5 enum-to-string sites |
| α7 | `BoolIndex` | new `Profiling/Util/BoolIndex.cs` | buff-diff + similar |

**Verification.**
- `Time.UnixMsNow` returns within 100 ms of `DateTimeOffset.UtcNow` over a 60 s synthetic tick run.
- `LangNameCache` returns the same string as `Lang.Get*` for all known ids.
- `RowPool<T>.Rent/Return` smoke test exercises the borrow/fill/serialise/return cycle.
- All helpers covered by allocation-aware xUnit tests (test-harness §6).

**No data-stack change.** No behaviour change. All sweeps that follow are mechanical.

### 6.2 Phase β — Per-tick zeroing (1–2 days)

| Step | Site | Change |
|---|---|---|
| β1 | `MetricCollector.BeginTick/EndTick` `DateTimeOffset.UtcNow` × 2 | Replace with `Time.UnixMsNow` |
| β2 | `StallDetector.OnTick` `DateTimeOffset.UtcNow` | Defer to stall-fired branch (stall-detection §5.F) |
| β3 | `StallDetector.OnTick` 2× `GC.GetTotalPauseDuration` | One call, shared (stall-detection §5.J) |
| β4 | `SpikeDetector.OnTick` window-open allocation | Pool-backed snapshot slots (spike-detection §4.4) |
| β5 | `ProbeStack.Enter/Leave/EnterCpuAlloc/LeaveCpuAlloc` | `[AggressiveInlining]` + 5.13/5.14 data-shape changes (allocation-tracking §5.3, §5.13, §5.14) |
| β6 | `PerModAttribution.Add` | `[AggressiveInlining]` + const `CategoryCount` (allocation-tracking §5.5, §5.4) |

**Verification.**
- `Tick_Standard_AllocatesZeroBytes` test passes (test-harness §6.2).
- BDN micro-bench `MetricCollector.Tick` reports 0 B/op in all three modes.
- Per-tick PerformanceProfiler cost drops from 0.27 ms toward 0.20 ms.

### 6.3 Phase γ — Per-event row reuse (3–4 days)

| Step | Site | Change |
|---|---|---|
| γ1 | `InteractionPlayer.OnHurt` → `DamageTakenRow` + `ActiveBuffs` list | `RowPool<DamageTakenRow>.Rent` + `ListPool<int>.Rent` |
| γ2 | `InteractionPlayer.OnHitNPCWithItem`/`WithProj` → `DamageDealtRow` | `RowPool<DamageDealtRow>.Rent` + `LangNameCache.Npc(type)` |
| γ3 | `InteractionPlayer.PostUpdateBuffs` → diff + `BuffEventRow` | Fix diff bug + pool row + `LangNameCache.Buff(type)` |
| γ4 | `InteractionPlayer.PostUpdateEquips` → `LoadoutSnapshotRow` + slots list + fingerprint SB | Pool row + reusable slots list + pooled StringBuilder |
| γ5 | `InteractionNpc.OnSpawn` → `NpcSpawnRow` + source substring | Pool row + `ModOwnerCache.FromEntitySource(src)` |
| γ6 | `InteractionItem.OnCreated` → `ItemCreatedRow` | Pool row + `LangNameCache.Item(type)` |
| γ7 | `ContextTransitionWatcher.OnTick` → `ContextTransitionRow` on edge | Pool row + defer `Lang.GetNPCNameValue` to alloc branch |
| γ8 | `WorldSnapshotter.OnSnapshot` → `WorldSnapshotRow` | Pool row + cached boss name |
| γ9 | `PlayerDeathDetector.Capture` → `PlayerDeathRow` + LiteDB query | Pool row + in-RAM rolling damage window (events-and-context §R10b) |

**Verification.**
- BDN `Enqueue_GameThread_Latency` drops from 441 ns/op toward < 200 ns/op.
- `Tick_Standard_AllocatesZeroBytes` still passes (per-event paths are exercised in event-aware tests, not per-tick).
- New test: 1,000 synthetic damage events allocate < 1 KB total (vs ~600 B × 1,000 = 600 KB today).

### 6.4 Phase δ — Writer-thread reductions (2–3 days)

| Step | Site | Change |
|---|---|---|
| δ1 | `EventJournal.AppendBatch` StringBuilder + JsonSerializer + UTF8 transcoding | `JournalEmitter` (§3.8) with `Utf8JsonWriter` + ArrayPool buffer |
| δ2 | Per-stream `BsonMapper.Serialize` reflection | `JsonTypeInfo<T>` source generation (§3.9) for the hottest streams |
| δ3 | `BsonSerializer.Serialize` byte[] per Upsert | `ArrayPool<byte>.Shared.Rent` |
| δ4 | `SpikeWindowRow.PerModCatMs` `List<double>` | Schema bump v1→v2 — `float[]` direct (spike-detection §4.7) |
| δ5 | `BuildSpikeTopContributors` | Struct array top-K (spike-detection §4.8) |
| δ6 | `BuildStallTopContributors` | Pre-sized struct array (stall-detection §5.H) |
| δ7 | `DbWriteOp.Payload` boxing | Struct union (§3.7) for the hot streams |
| δ8 | `ObjectId.NewObjectId()` deferred | Writer-thread Apply fills the id (persistence §5.6) |

**Verification.**
- Writer-thread ops/sec rises from 314 toward 1,000+.
- 10-min session DB size drops from 1,064 KB toward < 600 KB.
- LiteDB `Find/Insert` integration tests still pass round-trip.

### 6.5 Phase ε — Session-end + install (2–3 days)

| Step | Site | Change |
|---|---|---|
| ε1 | `OnWorldUnload` session-end aggregation | Move to writer thread via `SessionEndSnapshot` pool (mod-lifecycle §4.1) |
| ε2 | `BuildModAggregates` per-mod allocations | Pooled buffers (mod-lifecycle §4.9) |
| ε3 | `BuildHookAggregates` per-hook allocations | Move to writer thread + pre-sized list |
| ε4 | `ILHookInterceptor.Install` per-hook Cecil ModuleDefinition retention | Dispose `DynamicMethodDefinition` after install (hook-instrumentation ALLOC-1) |
| ε5 | `ILHookInterceptor.Install` `DisplayName` × 3 interpolated per hook | Intern strings + reuse across closed-generic instantiations (hook-instrumentation ALLOC-3) |
| ε6 | `_installedHooks` List.Add growth | Pre-size based on type-count estimate (hook-instrumentation ALLOC-5) |
| ε7 | `SelfHealth.MarkInstallStart/End` forced Gen2 | Replace with `GC.GetAllocatedBytesForCurrentThread` delta (mod-lifecycle §4.8) |
| ε8 | `OnWorldLoad` SessionRecorder + watcher allocations | Defer to first `PostUpdateEverything` (mod-lifecycle §4.4) |

**Verification.**
- End-of-session UiOverlayBlocking stall drops from 8.5 s toward 0 (writer-thread aggregation).
- Hook install delta drops from 233 MB toward < 80 MB.
- First-tick freeze drops from 172 ms toward ~110 ms.

### 6.6 Phase ζ — Insight detector LINQ removal (1–2 days)

| Step | Site | Change |
|---|---|---|
| ζ1 | `AllocationBurstDetector` + `GcPauseCulpritDetector` `new double[modCount]` per pass | Promote to field, reuse |
| ζ2 | `LoadoutCorrelatedCostDetector` 5 LINQ chains | Indexed range scans + pooled buffers (insights-engine §4.1) |
| ζ3 | `EventConditionalCostDetector` `GroupBy` + 5 LINQ chains | Explicit pass with `Dictionary<int, struct>` (insights-engine §4.2) |

**Verification.**
- BDN micro-bench on insight pass reports < 4 KB allocated per pass (down from ~50 KB).
- Insight output is byte-for-byte equivalent with the LINQ path (per-fixture test).

### 6.7 Phase η — Overlay per-frame zeroing (2–3 days)

| Step | Site | Change |
|---|---|---|
| η1 | `OverviewTab` per-frame format strings | `OverviewCache` filled at 1 Hz Tick (overlay §4.1) |
| η2 | `TreeTab` row-format strings | Row-format-cache at 1 Hz |
| η3 | `SpikesTab` reason strings | `EnumStringTable.Cause` |
| η4 | `EventsTab` per-row strings | Already 1 Hz; verify |
| η5 | `InsightsTab` pattern + body strings | `EnumStringTable.Pattern` + body cache |
| η6 | `OverlayDraw.FormatBytes` | Coarse LRU cache (overlay §4.1) |
| η7 | `DonutChart.foreach (pass in e.CurrentTechnique.Passes)` | Indexer (overlay §4.3) |
| η8 | `LayoutStatCards` array | Pre-allocate `_layoutCache.StatCardRects` (overlay §4.6) |
| η9 | `Sparkline.Render(IReadOnlyList<double>)` | `ReadOnlySpan<double>` overload (overlay §4.7) |

**Verification.**
- `OverlayDraw_AllocatesZeroBytes` test passes for SUMMARY, OVERVIEW, TREE, SPIKES, EVENTS, INSIGHTS tabs.
- 60-second overlay-open soak test allocates < 10 KB total (cache fills + occasional dirty-flag refills).

### 6.8 Phase summary

| Phase | Duration | Highest-impact metric moved |
|---|---|---|
| α | 2–3 days | none yet (infrastructure) |
| β | 1–2 days | Per-tick cost 0.27 → 0.20 ms |
| γ | 3–4 days | Enqueue latency 441 → < 200 ns |
| δ | 2–3 days | Writer ops/sec 314 → 1,000+; DB 1,064 → < 600 KB |
| ε | 2–3 days | UiOverlayBlocking 8.5 → 0 s; install 233 → < 80 MB |
| ζ | 1–2 days | Insight pass 50 → < 4 KB |
| η | 2–3 days | Per-frame overlay zero alloc |
| **Total** | **~13–20 days** | every baseline.md row moves |

### 6.9 Cross-system dependency map

```
α (helpers)
 ├── β1, β2 use α1 Time
 ├── γ1-γ9 use α2 Lang, α3 RowPool, α4 ListPool, α5 ModOwner
 ├── δ1-δ8 use α3 RowPool, α8 JournalEmitter, α9 JsonTypeInfo
 ├── ζ uses α4 ListPool
 └── η uses α6 EnumStringTable + α2 Lang

β (per-tick zeroing) — independent of γ/δ
γ (per-event row reuse) — depends on α only
δ (writer-thread) — depends on γ (rows must be poolable before journal hot path uses them)
ε (session-end + install) — depends on δ (writer-thread infra) and γ (pooled rows)
ζ (insight LINQ) — depends on α; can land in parallel with γ/δ
η (overlay) — depends on α2/α6; can land in parallel with γ/δ
```

Critical path: α → γ → δ → ε. Parallel opportunities: β, ζ, η can run independently after α lands.

---

## 7. Verification surface

Every phase ends with a measurable allocation gate. The gates feed test-harness §6 (allocation-aware tests) and §4 (BDN benches).

| Phase | Allocation contract | Test handle |
|---|---|---|
| α | All helpers covered by smoke tests | `TimeTests`, `LangNameCacheTests`, `RowPoolTests`, etc. |
| β | `MetricCollector.Tick` allocates 0 B/op | `Tick_*_AllocatesZeroBytes` (test-harness §6.2) |
| β | `ProbeStack.Enter/Leave` pair allocates 0 B | `EnterLeave_AllocatesZeroBytes` |
| β | `SpikeDetector.OnTick` opens window without allocation | `SpikeDetectorPoolTests` (spike-detection §4.4) |
| γ | Per-event emit allocates < 64 B | `Enqueue_GameThread_Latency` + new alloc gate |
| γ | 1,000 synthetic damage events allocate < 1 KB total | New `InteractionsAllocationContract` test |
| δ | Writer-thread drain `Allocated` per op < 256 B | BDN `[MemoryDiagnoser]` on `DbWriterThread.Drain` |
| δ | LiteDB round-trip preserves event identity | Existing `PersistenceRoundTripTests` continue to pass |
| ε | Hook install Gen2 delta < 80 MB | `InstallHeapSnapshotTests` (hook-instrumentation Phase E) |
| ε | Session-end blocking time < 100 ms | New `SessionEndBlockingDuration` test |
| ζ | Insight pass allocates < 4 KB | BDN on `InsightsEngine.Tick` |
| η | `OverlayDraw` frame allocates 0 B at steady state | New `OverlayDraw_AllocatesZeroBytes` (test-harness extension) |

**Master gate (Phase E in baseline.md):** every row in baseline.md moves in the better direction. The test that proves this is a full session-replay against a captured baseline.md fixture.

### 7.1 Continuous allocation-rate monitor (Diagnostic)

A dual-surface diagnostic that runs during a live session and reports per-tier alloc rate to `client.log` once a minute (agent surface) and as a SELF tab card (player surface):

```
[PERF] 2026-05-20 16:14:20  alloc-rate report  (last 60 s)
  T0 per-tick:        0 B   (0 B / sec)            target 0
  T2 per-event:    14.2 KB  (243 B / sec)          target < 1 KB/sec
  T3 writer-thread: 31.0 KB (530 B / sec)          target < 4 KB/sec
  T4 per-frame:       0 B   (0 B / sec)            target 0
  T5 per-second:    1.8 KB  (30 B / sec)           target < 4 KB/sec
  T8 install (cumulative): 76 MB                   target < 80 MB
```

This is the post-pass watchdog. Any drift surfaces here before users notice. Implemented by sampling `GC.GetAllocatedBytesForCurrentThread` at tier boundaries.

---

## 8. References

### 8.1 Per-system research dossiers cited

All in `context/perf-pass/research/`:

- `allocation-tracking.md` — §1 (current emission shape), §5 (optimisation candidates), §6 (cross-system dependencies), §8 (.NET runtime sources).
- `hook-instrumentation.md` — §1.6 (install-time allocations), §3.1 (Cecil retention), §4 (CPU + ALLOC opportunities), §6 (phase order).
- `metric-collection.md` — §1.2 (DateTimeOffset.UtcNow), §3 (.NET 8 API costs), §4 (optimisation candidates), §5 (writer-thread fixes).
- `persistence.md` — §1.2 (per-call-site allocation profile), §4 (BSON serialiser allocations), §5 (game-thread row pooling, writer-thread reductions), §6.3 (in-RAM damage window).
- `spike-detection.md` — §1.2 (per-tick spike path allocations), §4.4 (pool-backed snapshot slots), §4.7 (schema bump for List<double> drop), §4.8 (drain allocation reduction).
- `stall-detection.md` — §3 (GC.* API costs), §5.F (DateTimeOffset.UtcNow deferral), §5.H (List allocation drop), §5.I (enum-to-string), §5.J (shared GC snapshot).
- `events-and-context.md` — §R2 (BitArray delegate cache), §R3 (SubworldProbe delegate cache), §R4 (defer Lang.GetNPCNameValue), §R5 (weather emit), §R6 (boss-name array cache), §R10b (in-RAM damage window).
- `insights-engine.md` — §1.3 (AllocationBurstDetector array), §1.6 (GcPauseCulpritDetector array), §4 (LINQ-to-loop migration).
- `overlay.md` — §1.2 (per-frame string allocation profile), §1.4 (hidden allocations inside collaborators), §4.1 (format cache shape), §4.3 (BasicEffect enumerator), §4.6 (LayoutStatCards array), §4.7 (IReadOnlyList<T> boxing).
- `mod-lifecycle.md` — §1.9 (per-method allocation ledger), §4.1 (SessionEndSnapshot pool), §4.4 (defer SessionRecorder), §4.8 (replace forced Gen2), §4.9 (lifecycle alloc removal).
- `test-harness.md` — §4.3 (B-003 alloc-counter cost bench), §6 (allocation-aware tests).
- `code-health-audit.md` — §2 (hot-path allocations headline finding), §14 (DateTime epidemic, Lang.Get* epidemic, Span underadoption).

### 8.2 Project-internal sources

- `context/perf-pass/baseline.md` — the contract this pass moves.
- `context/notes/philosophy.md` — capture-vs-presentation discipline; veto on scope-reducing recommendations.
- `context/notes/spikes-and-allocations-plan.md` — the existing implementation design this pass refines.
- `context/notes/litedb-migration-plan.md` — the BSON path persistence touches.
- `CLAUDE.md` — the five Project Invariants, particularly Invariant 2 (zero-alloc per-tick hot path) and Invariant 5 (no mod-specific code).

### 8.3 External sources cited by referenced docs

- `dotnet/runtime` source for `GetAllocatedBytesForCurrentThread`, `GetTotalPauseDuration`, `Stopwatch.GetTimestamp` FCall bodies (allocation-tracking §3, §8; stall-detection §3; metric-collection §3).
- `dotnet/runtime` issues #17891 (per-thread alloc counter cost), #66036 (TotalPauseDuration proposal), discussions on per-thread vs per-async semantics.
- `MonoMod.RuntimeDetour` 25.3.2 source, particularly `ILHook` + `DetourManager` + `DynamicMethodDefinition` lifecycle (hook-instrumentation §3.1).
- `Mono.Cecil` 0.11.6 module retention behaviour (hook-instrumentation §3.1).
- BenchmarkDotNet `[MemoryDiagnoser]` source — `GcStats.cs` confirms the diagnoser uses the same per-thread API we use, so no measurement contamination (allocation-tracking §2.2; test-harness §3).
- LiteDB 5.x docs on `BsonMapper`, `ObjectId.NewObjectId`, BSON serialisation costs (persistence §4).
- `System.Text.Json` source generation pattern for AOT-friendly serialisation (persistence §5.7, this file §3.9).
- `System.IO.Pipelines` / `Utf8JsonWriter` / `ArrayBufferWriter<byte>` docs for the journal rewrite (persistence §5.7, this file §3.8).

### 8.4 Code references in this file

All file:line citations from §1 and §5 trace back to the per-system docs above; this file does not re-cite source paths the per-system narratives already established. The cross-cutting reverse-index in §1.10 is the bridge: each pattern's listed sites are documented in the named per-system doc.

---

*Cross-allocations dossier closes here. The phased roadmap in §6 feeds `master-plan.md`. The verification gates in §7 feed the new tests the test-harness dossier scaffolds. Every recommendation in this file preserves the full data stack: same observable output, same capture coverage, same event count, same insight surface — just cheaper to produce.*
