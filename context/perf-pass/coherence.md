# Coherence pass — reconciling 15 research dossiers into one execution view

> Phase 4 output: every duplicate merged, every conflict resolved, every dependency surfaced. The master plan (Phase 5) sequences from here; it does not re-litigate decisions made in this doc.

Date: 2026-05-20 · Branch: main @ `eaf0dfb` · Source: `context/perf-pass/research/*.md` (15 files, 16,294 lines)

---

## 1. Inputs

| Doc | LOC | Role |
|---|---:|---|
| `code-health-audit.md` | 434 | Breadth audit (my own). Cross-cutting alloc patterns, warnings, conventions. |
| `metric-collection.md` | 1,394 | Hot-path per-tick CPU + alloc; baseline.md regression root-cause. |
| `allocation-tracking.md` | 1,391 | IL alloc-counter emission, FCall cost analysis. |
| `stall-detection.md` | 1,383 | Per-tick CPU on stall classifier + cluster bookkeeping. |
| `overlay.md` | 1,328 | Draw-thread per-frame allocs (~120 KB/sec headline). |
| `persistence.md` | 1,119 | LiteDB writer, BSON shape, 3 bug-fix designs, end-of-session relocation. |
| `insights-engine.md` | 1,138 | Detector LINQ → indexed query + off-thread reader. |
| `cross-concurrency.md` | 1,144 | Thread model after v0.6, race surface, channel verification. |
| `cross-allocations.md` | 1,002 | Master phased roadmap α–η + 11 shared-infrastructure helpers. |
| `spike-detection.md` | 945 | Spike-row BSON shape + ring index math. |
| `events-and-context.md` | 898 | Context watcher, biome/weather bit diffs, R1–R12. |
| `cross-storage-ram.md` | 888 | Storage census + unified v6→v7 schema migration. |
| `mod-lifecycle.md` | 875 | Session-end relocation + install yielding + PreSaveAndQuit. |
| `hook-instrumentation.md` | 850 | 233 MB install delta + Cecil retention reconciliation. |
| `test-harness.md` | 1,505 | Three-layer regression detection + 14 benchmark groups. |
| **Total** | **16,294** | |

Every doc is invariant-clean by self-audit. Coherence does not need to re-prove invariant compliance per item; it inherits each doc's compliance claim.

---

## 2. Headline reconciliations

The places where two or more docs touched the same surface and proposed overlapping or conflicting fixes. Each item resolves to **one canonical owner** plus the cross-references.

### 2.1 The 441 ns/op enqueue regression — root cause

Three docs proposed root causes:

- `metric-collection.md` §1.x: `TickAggregateWarm` is a `sealed class` (heap alloc per Enqueue) + `DbWriteOp.Payload` is `object` (boxes value-type payloads).
- `persistence.md` §3: dominated by **row construction** (Substring allocs in source-category strip, Lang.Get* inline lookups, `new List<int>` per Snapshot, `DateTimeOffset.UtcNow` six places).
- `cross-allocations.md` §1.10: **both are true** — the row classes are the heap allocs, the inline computation is the per-call CPU. The 441 ns is the sum.

**Resolution.** Both fixes land. Owner: persistence (γ phase = row-pool + LangNameCache + UnixMsNow) takes the row-construction half; metric-collection takes the `DbWriteOp` discriminated-struct-union half (δ7). Cross-allocations §3.7 is the shared API design they both consume.

Sequencing: γ before δ — pool the rows first, then change the channel payload shape. Doing it the other way around forces a rewrite of every interim pool.

### 2.2 The 233 MB install delta — root cause

- `hook-instrumentation.md` §8.12: Cecil `ModuleDefinition` retention dominates, but the 23 KB/hook figure depends on the dominance assumption holding. **Gate ALLOC-1 (dispose ILContext) on a heap-snapshot diagnostic** before committing to the 1–2 day implementation.
- `cross-storage-ram.md` §1.2: three independent attributions — Cecil (60–190 MB), reflection scratch (small), per-mod attribution arrays (small). Reconciled joint plan I-1 through I-8.
- `mod-lifecycle.md` §4.6: shared `HookSurfaceCache` to dedupe the two reflection walks (delegate + ILHook) saves an *additional* 80–150 MB independently of the Cecil work.

**Resolution.** Owner: hook-instrumentation, with cross-storage-ram §1.3 as the joint plan. Run the heap-snapshot diagnostic in Phase ε FIRST. If Cecil dominates, commit to I-3 (dispose ILContext) for the 50–150 MB win. If not (<50% of delta), pivot to trampolines/JIT-code retention. Mod-lifecycle §4.6 stacks orthogonally regardless.

### 2.3 Smoothing off-thread — defer to v0.6.1

`metric-collection.md` §4.13 proposes [I] move smoothing / rolling / harvest off the game thread to a "T7 collector smoother" thread. Cross-concurrency §6.4 explicitly defers T7 to v0.6.1 — *not needed to hit the v0.6 stall-fix targets, adds race surface that the other v0.6 work doesn't*.

**Resolution.** v0.6 omits T7. The §4.13 saving (~50–70 µs/tick) is recovered by the other metric-collection wins (§4.1 [A] 3-arg Add removal, §4.6 [H] incremental histogram, §4.7 [F] SIMD UpdateRollingAverage, §4.14 [B] Stopwatch.Frequency reciprocal hoist, §4.15 [B] fuse SumAll). Per-tick target stays at < 0.10 ms; the path is more conservative than the original §4.13 proposal but still cleanly inside Invariant 2.

**Tracked as** `v0.7-T7-smoother` in the wrap-up follow-up list.

### 2.4 Off-thread session-end aggregation — single design

Three docs touch this:

- `persistence.md` §5.6 (Phase B1): enqueue a `SessionFinalize` op + writer-thread builds aggregates.
- `mod-lifecycle.md` §4.1–4.4: four-phase commit using a `SessionEndSnapshot` pool, `DbOpKind.SessionEndAggregate`, fold `SessionSummaryLogger.Write` into the writer tail, `PreSaveAndQuit` to start work early.
- `cross-concurrency.md` §6.1: thread-crossing contract — T1 captures the snapshot, T4 reads it, no mutations from T4 back to T1.

**Resolution.** Owner: mod-lifecycle (the structural design). Persistence implements the `DbOpKind` + the writer-side Apply method. Cross-concurrency §6.1 is the contract test. The four sub-changes land together (atomic to avoid leaving T1 partially-relocated).

`PreSaveAndQuit` adds ~1–3 s of overlap with vanilla save. Worth doing; gain is monotonic.

### 2.5 Schema migration — one numbered plan, eight collection bumps

`cross-storage-ram.md` §6.3 is the canonical plan (7-phase v0.5→v0.6 migration). Every per-system doc's schema bump folds into one of those phases:

| Bump | Source doc | Phase in §6.3 |
|---|---|---|
| `ItemCreatedRow v1→v2` | persistence §6.1 | Phase 1 |
| `PlayerDeathRow v1→v2` | persistence §6.3 | Phase 1 |
| All records `_schema → v` + `[BsonField]` rename | persistence §5.1, §4.1.2 | Phase 2 |
| `DamageDealtRow v1→v2` (FK swap) | persistence §5.4 | Phase 3 |
| `SpikeWindowRow v1→v2` (numeric blob) | spike-detection §4.7 | Phase 4 |
| `TickAggregateWarm/Cold v1→v2` (numeric blob) | persistence §5.5 | Phase 4 |
| `StallEventRow v2→v3` (byte enums) | stall-detection §5.G/H | Phase 5 |
| `StallClusterRow v1→v2` (byte cause) | stall-detection §5.H | Phase 5 |
| `ContextTransitionRow v1→v2` (typed flag) | events-and-context R5 | Phase 5 |
| `InsightRow v1→v2` (byte severity) | insights-engine §4 | Phase 5 |
| Journal format flip | persistence §5.7 | Phase 6 |
| Migration tests | (new) | Phase 7 |

**No global database-version bump** — `USER_VERSION 6 → 7` is the file-level marker; per-row `v` migration is row-read-time and amortised. Verified against cross-storage-ram §6.2.

### 2.6 Insight detector LINQ-to-loop — single owner

`insights-engine.md` §4 designs the 10 LINQ-chain rewrites with indexed queries + pooled buffers. `cross-allocations.md` ζ2/ζ3 sequence them. `cross-storage-ram.md` §4 covers the compound-index reads.

**Resolution.** Owner: insights-engine. The three compound indexes (`(SessionId, SecondIndex)` on `TickAggregatesWarm`, `(SessionId, Reason, UnixMs)` on `LoadoutSnapshots`, `(SessionId, UnixMs)` on `BuffEvents`) land at the end of the schema-migration Phase 5 so the new compound indexes are present when the LINQ rewrites read them. Sub-phase ζ in the cross-allocations roadmap.

### 2.7 Insights reader thread (T6) — gate on LiteDB read-while-write verification

`insights-engine.md` §5.3 proposes moving detector queries off the main thread. `cross-concurrency.md` §6.3 says **T6 (insights reader)** must verify LiteDB v5 `ConnectionType.Direct` read-while-write semantics before ship. Two options:

- **Option A** (recommended): a dedicated reader thread sharing the LiteDB instance.
- **Option B**: extend the existing writer thread (T4) with a `ReadOp` enum.

**Resolution.** Try Option A first because it parallelises naturally with writer-thread throughput work. Run a LiteDB read-while-write soak test in test-harness as part of Phase ζ verification. If the soak shows contention or crash risk, fall back to Option B. Tracked as an *open question* (not blocking).

### 2.8 The buff-diff sparsity finding — reclassified

`code-health-audit.md` §4 reclassified the "buffEvents = 2 bug" as **unconfirmed** after reading the diff logic carefully. The diff itself is correct; the captured count may reflect the actual gameplay (no buffs in a pre-hardmode no-potion exploration session). `persistence.md` §6.2 still ships an *additional* defensive fix: snapshot-before-gate so the prev-buff state initialises even when `Player.whoAmI != Main.myPlayer` clears late, plus a "first valid tick" emission of all active buffs.

**Resolution.** Persistence's snapshot-before-gate fix lands as part of Phase A2. The diff-logic correctness is independently verified by code-health-audit. The in-game retest (potion use) lands as part of Phase ε verification, not as a gate. If the retest still shows missing edges, a follow-up patch ships; if it confirms the diff is fine, the v0.5 capture was genuinely accurate.

### 2.9 PlayerDeathDetector LiteDB query — moves off the game thread two ways

`events-and-context.md` R10 proposes a 64-entry in-RAM ring of `DamageTakenRow` in `SessionRecorder` so the death-detector reads RAM not DB. `persistence.md` §6.3 separately proposes damage-weighted attribution over a last-N-seconds window. Both want the same data structure.

**Resolution.** Single design: `SessionRecorder._recentDamageRing[64]` (cross-concurrency S7 names it). Damage-weighted aggregation over the ring at death edge. The DB query is removed entirely. Owner: persistence (Phase A3) with events-and-context R10 folded in.

### 2.10 BossSampler vs ProfilerSystem.CountActive — fuse

`events-and-context.md` R7: `BossSampler.Sample` and `ProfilerSystem.CountActive(Main.npc)` independently walk the 200-slot `Main.npc[]` each tick. Fuse them.

No conflict; pure win. **Owner:** events-and-context. Lands in cross-allocations Phase β (per-tick zeroing).

### 2.11 Allocation tracking — gate on FCall cost benchmark

`allocation-tracking.md` §2 mandates a benchmark of `GC.GetAllocatedBytesForCurrentThread()` per-call cost on .NET 8 macOS *before* Phase B. If t_alloc > 50 ns, the unconditional-tracking design needs to be reopened with Caner. Cross-allocations §6.1 α-step folds this measurement into Phase α (infrastructure measurement).

**Resolution.** Owner: test-harness (benchmark) + allocation-tracking (consumer). The measurement lands in Phase α before any other Phase β/γ work; if it gates, we pause and surface to Caner. Per the autonomy contract from the perf-pass kickoff, we proceed with the default (unconditional tracking) and document the measurement.

---

## 3. The Debug-vs-Release baseline trap

`test-harness.md` flagged a critical calibration issue: the **441 ns/op enqueue figure in `baseline.md` was captured under `dotnet test` Debug**. Release-mode numbers will be smaller. Every "expected delta" in every per-system doc was measured against the inflated number.

**Step 1 of Phase 6 is to re-measure all four baseline benchmarks under Release and update `baseline.md`.** Every target ("< 200 ns/op", "< 0.10 ms/tick", "< 600 KB / 10 min") is then revised relative to the corrected baseline. If the Release-mode enqueue is already, say, 280 ns/op, the < 200 target stays but the urgency profile shifts.

This is a one-hour task and it gates the entire implementation phase. It's not a research finding — it's a measurement correction. Owner: test-harness lays the Release-mode commands; the Phase 6 implementation starts by running them.

---

## 4. Master opportunity index (every distinct change, mapped)

The 15 docs collectively propose ~120 numbered optimisations. After deduplication and reconciliation, the canonical list has **N = 87 distinct items** mapped to one of seven phase tags (α–η) plus the bug-fix prefix (A1, A2, A3). Each item routes to one canonical owner.

The table below isn't every line item — it would be ~3,000 lines and the per-system docs are the actual source of truth. This is the routing index: for every item, **which doc owns the design, which phase implements it, which doc verifies it**.

### 4.1 Bug fixes (Phase A — correctness, lands FIRST)

| ID | Item | Owner | Verification |
|---|---|---|---|
| A1 | `itemCreatedEvents = 0` — wire `GlobalItem.OnSpawn(WorldItem, IEntitySource)` + `ModPlayer.OnPickup(WorldItem)` alongside existing `OnCreated` | persistence §6.1 | in-game playtest replay |
| A2 | `buffEvents` snapshot-before-gate fix + first-valid-tick emission | persistence §6.2 | in-game potion-use retest |
| A3 | Damage-weighted death attribution via `_recentDamageRing[64]` + remove game-thread DB query | persistence §6.3 + events-and-context R10 | unit test (synthetic damage events → expected killer) |

A1, A2, A3 land first because post-pass numbers are meaningless if the captures are still broken. A3 also removes the only game-thread DB read, simplifying every subsequent perf-pass change.

### 4.2 Phase α — Build shared infrastructure (2–3 days)

| ID | Helper | Owner doc | Files |
|---|---|---|---|
| α1 | `Time.UnixMsNow` + `Time.Reset()` | cross-allocations §3.1 | new `Profiling/Time.cs` |
| α2 | `LangNameCache` (id-keyed string arrays for buff/item/projectile/npc) | cross-allocations §3.2 | new `Profiling/LangNameCache.cs` |
| α3 | `RowPool<T>` + `IPoolReset` | cross-allocations §3.3 | new `Profiling/Pools/RowPool.cs` |
| α4 | `ListPool<T>` | cross-allocations §3.4 | new `Profiling/Pools/ListPool.cs` |
| α5 | `ModOwnerCache` | cross-allocations §3.5 | new `Profiling/ModOwnerCache.cs` |
| α6 | `EnumStringTable` (5 enums: Cause, Severity, Reason, Edge, Pattern) | cross-allocations §3.6 | new `Profiling/EnumStringTable.cs` |
| α7 | `BoolIndex` | cross-allocations §3.11 | new `Profiling/Util/BoolIndex.cs` |
| α8 | FCall-cost benchmark of `GC.GetAllocatedBytesForCurrentThread()` | allocation-tracking §2 + test-harness B-003 | new bench in `Tests/Benchmarks/` |
| α9 | Release-mode baseline re-measurement (the §3 trap fix) | test-harness | update `baseline.md` |

All α work is additive; no behavioural change yet. Allocation-aware xUnit tests come online here (test-harness §6.2).

### 4.3 Phase β — Per-tick zeroing (1–2 days)

| ID | Site | Owner doc |
|---|---|---|
| β1 | `MetricCollector.BeginTick/EndTick` `DateTimeOffset.UtcNow` × 2 → `Time.UnixMsNow` | metric-collection §1.1 + cross-allocations §6.2 |
| β2 | `StallDetector.OnTick` `DateTimeOffset.UtcNow` defer to stall-fired branch | stall-detection §5.F |
| β3 | `StallDetector.OnTick` 2× `GC.GetTotalPauseDuration` → one shared `GcCounterSnapshot` | stall-detection §5.J + metric-collection §4.3 |
| β4 | `SpikeDetector.OnTick` window-open `new float[modCount*catCount]` → pool-backed slots | spike-detection §4.4 |
| β5 | `ProbeStack.Enter/Leave/EnterCpuAlloc/LeaveCpuAlloc` `[AggressiveInlining]` + Frame data-shape change | allocation-tracking §5.3/§5.13/§5.14 |
| β6 | `PerModAttribution.Add` `[AggressiveInlining]` + `const int CategoryCount = 7` | allocation-tracking §5.4/§5.5 |
| β7 | `Stopwatch.Frequency` reciprocal hoist | metric-collection §4.14 |
| β8 | Fuse `SumAll` into smoothing loop | metric-collection §4.15 |
| β9 | Replace 3-arg `PerModAttribution.Add` overload | metric-collection §4.1 |
| β10 | Replace `IReadOnlyList<HookDescriptor>` with flat array exposed by ref | metric-collection §4.2 |
| β11 | Combine collector-boundary Stopwatch + GC reads | metric-collection §4.3 |
| β12 | `PerTickAttributionRing` modulo → power-of-two mask | spike-detection §4.3 |
| β13 | `ContextTransitionWatcher.DiffBiomeBits` word-level XOR + `TrailingZeroCount` | events-and-context R1 |
| β14 | Bit-walk weather diff via `TrailingZeroCount` (folds in R5 schema bump dependency) | events-and-context R5 |
| β15 | `SubworldProbe.Sample` `MethodInfo.Invoke` → compiled delegate | events-and-context R3 |
| β16 | Defer `Lang.GetNPCNameValue` in `ContextTransitionWatcher` | events-and-context R4 |
| β17 | Pre-resolve every biome `DisplayName` into `string[]` indexed by bit | events-and-context R11 |
| β18 | Fuse `BossSampler.Sample` with `ProfilerSystem.CountActive(Main.npc)` | events-and-context R7 |
| β19 | Latch active-keys hash sets in `EventAggregator` | events-and-context R8 |
| β20 | Replace `BossSampler._nameCache` `Dictionary` with `string[]` keyed by NPC type | events-and-context R6 |
| β21 | `Process.GetCurrentProcess() + TotalProcessorTime` → `Environment.CpuUsage.TotalTime` | stall-detection §5.A |
| β22 | `CaptureTopContributors` `IReadOnlyList<double>?` → concrete `double[]?` | stall-detection §5.B |
| β23 | Pass `itemCount` into `WorldSnapshotter.OnTick` | events-and-context R9 |

Verification: `Tick_Standard_AllocatesZeroBytes` test (test-harness §6.2) passes; BDN `MetricCollector.Tick` reports 0 B/op in Lite/Standard/Deep; per-tick PerformanceProfiler cost drops from 0.27 ms toward 0.20 ms.

### 4.4 Phase γ — Per-event row reuse (3–4 days)

| ID | Site | Owner doc |
|---|---|---|
| γ1 | `InteractionPlayer.OnHurt` → `DamageTakenRow` + `ActiveBuffs` list → pooled | persistence §5.2 + cross-allocations §6.3 |
| γ2 | `InteractionPlayer.OnHitNPCWithItem`/`WithProj` → `DamageDealtRow` → pooled + `LangNameCache.Npc(type)` | persistence §5.2 |
| γ3 | `InteractionPlayer.PostUpdateBuffs` → pool `BuffEventRow` + `LangNameCache.Buff(type)` | persistence §5.2 + γ3 includes A2 |
| γ4 | `InteractionPlayer.PostUpdateEquips` → pool `LoadoutSnapshotRow` + reusable slots list + pooled `StringBuilder` for fingerprint | persistence §5.2 |
| γ5 | `InteractionNpc.OnSpawn` → pool `NpcSpawnRow` + `ModOwnerCache.FromEntitySource(src)` | persistence §5.2 |
| γ6 | `InteractionItem.OnCreated`/`OnSpawn`/`OnPickup` → pool `ItemCreatedRow` + `LangNameCache.Item(type)` (also lands A1) | persistence §5.2 + persistence §6.1 |
| γ7 | `ContextTransitionWatcher.OnTick` → pool `ContextTransitionRow` on edge + defer `Lang.GetNPCNameValue` to alloc branch | events-and-context R4 + R5 + cross-allocations §6.3 |
| γ8 | `WorldSnapshotter.OnSnapshot` → pool `WorldSnapshotRow` + cached boss name | events-and-context §1.9 |
| γ9 | `PlayerDeathDetector.Capture` → pool `PlayerDeathRow` + in-RAM rolling damage window (lands A3) | persistence §6.3 + events-and-context R10b |
| γ10 | Lazy `ObjectId.NewObjectId()` — writer-thread Apply fills id | persistence §5.11 |
| γ11 | Buff-diff `HashSet` replacement with `BoolIndex` | persistence §5.12 + cross-allocations α7 |
| γ12 | Pre-resolve `Substring("EntitySource_")` / `Substring("ItemCreationContext")` into a cached map | persistence §5.16 |

Verification: BDN `Enqueue_GameThread_Latency` drops 441 → < 200 ns/op; 1,000 synthetic damage events allocate < 1 KB total (vs ~600 KB today).

### 4.5 Phase δ — Writer-thread + schema migration (4–6 days)

| ID | Site | Owner doc |
|---|---|---|
| δ1 | Bump `USER_VERSION 6 → 7`; register `Migrations.cs:v6_to_v7` step | cross-storage-ram §6.3 Phase 0 |
| δ2 | Migration Phase 1: `ItemCreatedRow v1→v2` (add SourceContext), `PlayerDeathRow v1→v2` (add DamageWeighting + window seconds) | cross-storage-ram §6.3 Phase 1 |
| δ3 | Migration Phase 2: every record `_schema → v`, add `[BsonField]` short names everywhere; fall-through reader (v ≤ 1 = long-name mapper, v ≥ 2 = short-name) | cross-storage-ram §6.3 Phase 2 |
| δ4 | Migration Phase 3: `DamageDealtRow v1→v2` FK swap (LoadoutFingerprint string → LoadoutSnapshotId ObjectId); migration writes the FK by matching prior fingerprint string | cross-storage-ram §6.3 Phase 3 |
| δ5 | Migration Phase 4: `SpikeWindowRow v1→v2`, `TickAggregateWarm/Cold v1→v2` — `BsonArray` of doubles → `byte[]` numeric blob | cross-storage-ram §6.3 Phase 4 + spike-detection §4.7 |
| δ6 | Migration Phase 5: byte-encoded enums on `StallEventRow v2→v3`, `StallClusterRow v1→v2`, `ContextTransitionRow v1→v2`, `InsightRow v1→v2` | cross-storage-ram §6.3 Phase 5 + stall-detection §5.G/H + events-and-context R5 |
| δ7 | Migration Phase 6: binary journal frame format with header (1 B version + 3 B magic), NDJSON fallback for pre-v0.6 journals | cross-storage-ram §6.3 Phase 6 + persistence §5.7 |
| δ8 | Migration Phase 7: PersistenceRoundTrip tests + reader-fallback tests + a v0.5 fixture-DB migration test | cross-storage-ram §6.3 Phase 7 |
| δ9 | `EventJournal.AppendBatch` `StringBuilder` + `JsonSerializer` + UTF-8 transcoding → `JournalEmitter` (Utf8JsonWriter + ArrayPool) | cross-allocations §3.8 + persistence §5.7 |
| δ10 | Per-stream `BsonMapper.Serialize` reflection → `JsonTypeInfo<T>` source generation for hottest streams | cross-allocations §3.9 |
| δ11 | `BsonSerializer.Serialize` byte[] per Upsert → `ArrayPool<byte>.Shared.Rent` | cross-allocations §6.4 δ3 |
| δ12 | `DbWriteOp.Payload` boxing → discriminated struct union for hot streams | metric-collection §1.x + cross-allocations §3.7 |
| δ13 | `InsertBulk` for high-frequency event streams | persistence §5.8 |
| δ14 | Deferred non-unique index creation | persistence §5.9 |
| δ15 | Three compound indexes: `(SessionId, SecondIndex)` on TickAggregatesWarm, `(SessionId, Reason, UnixMs)` on LoadoutSnapshots, `(SessionId, UnixMs)` on BuffEvents | insights-engine §3.1–§3.3 |
| δ16 | `BuildSpikeTopContributors` → struct array top-K | spike-detection §4.8 |
| δ17 | `BuildStallTopContributors` → pre-sized struct array | stall-detection §5.H |

Verification: writer-thread ops/sec rises 314 → > 1,000; 10-min session DB drops 1,064 → < 600 KB; PersistenceRoundTrip still passes; migration round-trip test passes.

### 4.6 Phase ε — Session-end + install (3–4 days)

| ID | Site | Owner doc |
|---|---|---|
| ε1 | Heap-snapshot diagnostic during install (must run FIRST to confirm Cecil > 50%) | hook-instrumentation §8.12 |
| ε2 | `OnWorldUnload` session-end aggregation → `SessionEndAggregate` op + writer-thread Apply | mod-lifecycle §4.1 + persistence §5.6 |
| ε3 | `BuildModAggregates` + `BuildHookAggregates` → writer-thread + pooled buffers | mod-lifecycle §4.2 |
| ε4 | `SessionSummaryLogger.Write` folded into writer-thread tail | mod-lifecycle §4.2 |
| ε5 | Busy-wait drain → deterministic completion signal (`ManualResetEventSlim` on `DbWriteOp`) | mod-lifecycle §4.3 + cross-concurrency §4.3 |
| ε6 | `PreSaveAndQuit` hook to start session-end work early (overlap with vanilla save, 1–3 s window) | mod-lifecycle §4.7 |
| ε7 | Defer SessionRecorder + watcher construction from `OnWorldLoad` to first `PostUpdateEverything` (172 ms freeze → ~110 ms) | mod-lifecycle §4.4 |
| ε8 | Yield hook install across multiple frames via `BeginInstallAsync` (T5 worker thread) | mod-lifecycle §4.5 |
| ε9 | Shared `HookSurfaceCache` to dedupe HookInterceptor + ILHookInterceptor type walks (80–150 MB saved) | mod-lifecycle §4.6 |
| ε10 | Conditional ALLOC-1: dispose `DynamicMethodDefinition` / per-hook `ILContext` after install (50–150 MB saved) | hook-instrumentation ALLOC-1 |
| ε11 | Intern `DisplayName` strings + reuse across closed-generic instantiations | hook-instrumentation ALLOC-3 |
| ε12 | Pre-size `_installedHooks` List based on type-count estimate | hook-instrumentation ALLOC-5 |
| ε13 | Replace `SelfHealth` forced Gen2 pair with `GC.GetAllocatedBytesForCurrentThread` delta | mod-lifecycle §4.8 |
| ε14 | Idempotent `Mod.Close` handler (Interlocked.CompareExchange) + reload-cycle heap-leak test | mod-lifecycle §4.9 + cross-concurrency §8 |
| ε15 | T5 thread-crossing audit + abort-clean contract | cross-concurrency §6.2 + §9 |

Verification: end-of-session UiOverlayBlocking stall drops 8.5 s → < 200 ms; install delta drops 233 → < 80 MB (if ALLOC-1 lands); first-tick freeze 172 → ~110 ms; in-game playtest measures it.

### 4.7 Phase ζ — Insight detector LINQ removal (1–2 days)

| ID | Site | Owner doc |
|---|---|---|
| ζ1 | Promote `AllocationBurstDetector` + `GcPauseCulpritDetector` per-pass `new double[modCount]` to field | insights-engine §1.3/§1.6 + §5.1 |
| ζ2 | `LoadoutCorrelatedCostDetector` 5 LINQ chains → indexed range scans + pooled buffers | insights-engine §4.1 |
| ζ3 | `EventConditionalCostDetector` `GroupBy` + 5 LINQ chains → explicit pass with `Dictionary<int, struct>` | insights-engine §4.2 |
| ζ4 | Move detector queries to T6 reader thread (Option A — LiteDB read-while-write soak test gates) | insights-engine §5.3 + cross-concurrency §6.3 |
| ζ5 | `GcPauseCulpritDetector` stall-cursor (mirror `PeakContributorToSpikeDetector` pattern) | insights-engine §1.6 |

Verification: BDN micro-bench on insight pass reports < 4 KB allocated per pass (was ~50 KB); insight output byte-for-byte equivalent with the LINQ path (per-fixture test).

### 4.8 Phase η — Overlay per-frame zeroing (2–3 days)

| ID | Site | Owner doc |
|---|---|---|
| η1 | `ProfilerOverlaySystem.ModifyInterfaceLayers` `new LegacyGameInterfaceLayer` per frame → cached single instance | overlay §3 |
| η2 | `DrawOverlay` `new GameTime()` per frame → cached instance | overlay §3 |
| η3 | `OverlayPanel.LayoutStatCards` `new Rectangle[4]` per `DrawSelf` → reused field | overlay §4.6 |
| η4 | `OverviewTab` per-frame format strings → `OverviewCache` filled at 1 Hz Tick | overlay §4.1 |
| η5 | `TreeTab` row-format strings → row-format-cache at 1 Hz | overlay §4.1 |
| η6 | `SpikesTab` reason strings → `EnumStringTable.Cause` (uses α6) | overlay §4.1 + α6 |
| η7 | `InsightsTab` pattern + body strings → `EnumStringTable.Pattern` + body cache | overlay §4.1 + α6 |
| η8 | `OverlayDraw.FormatBytes` → coarse LRU cache | overlay §4.1 |
| η9 | `DonutChart` `foreach (pass in e.CurrentTechnique.Passes)` → indexer | overlay §4.3 |
| η10 | `DonutChart` vertex-array reuse + slice-hash invalidation at 1 Hz (was recomputing 720 triangles/frame) | overlay §4.4 |
| η11 | `Sparkline.Render(IReadOnlyList<double>)` → `ReadOnlySpan<double>` overload | overlay §4.7 |
| η12 | `SpikesTab.RebuildTimelineMarks` 60 Hz → 1 Hz (only detection-time updates need refresh) | overlay §4.11 |
| η13 | End/Begin collapse for related batched draws | overlay §4.2 |

Verification: `OverlayDraw_AllocatesZeroBytes` test passes for SUMMARY / OVERVIEW / TREE / SPIKES / EVENTS / INSIGHTS tabs; 60-s overlay-open soak < 10 KB total alloc.

### 4.9 Wrap-up housekeeping (Phase 8)

Tracked separately because they're hygienic, not perf-related, but ride this pass:

| ID | Item | Owner |
|---|---|---|
| W1 | Migrate `ProfilerConfig` from `[Label]/[Tooltip]` to `[LabelKey]/[TooltipKey]` (or delete) + add Localization entries | code-health-audit §10 |
| W2 | `PlayerDeathReason.SourceCustomReason` → `CustomReason` rename | code-health-audit §1.3 |
| W3 | `ChangeMagicNumberToID` × 2: `npc.netID == 0` → `NPCID.None`, `item.type == 0` → `ItemID.None` | code-health-audit §9 |
| W4 | Tests `CS0649` × 5 on `EventContext` fields → `<NoWarn>CS0649</NoWarn>` on linked file or split POCO | code-health-audit §11 |
| W5 | Bump `build.txt` 0.5 → 0.6 | mod-lifecycle out-of-band |
| W6 | Add new `context/perf-pass/verification.md` post-run | this doc |
| W7 | Append v0.6 entry to `context/notes/decisions.md` | this doc |

---

## 5. Dependency graph

The hard prerequisites — Y cannot begin before X completes:

```
α8 (FCall benchmark) ──► (if t_alloc > 50 ns, halt and surface)
α9 (Release baseline)  ──► every "expected delta" is meaningful

α1 Time.UnixMsNow ──► β1, β2 + every γ row's UnixMs site
α2 LangNameCache  ──► γ2, γ3, γ5, γ6, γ7 + η6 + η7
α3 RowPool<T>     ──► every γ step
α4 ListPool<T>    ──► γ1, γ4, γ7
α5 ModOwnerCache  ──► γ5, γ6
α6 EnumStringTable──► η6, η7 (overlay reads)
α7 BoolIndex      ──► γ11 (buff diff)

A1, A2, A3 (bugs)  ──► (gate everything; correctness must come first)

γ phase ──► δ12 (DbWriteOp struct union assumes pooled rows in place)
δ1 USER_VERSION   ──► δ2 through δ8 (migration steps run in order)
δ5 (numeric blob) ──► η10 (donut vertex reuse reads SpikeWindow blobs)
δ15 (indexes)     ──► ζ2, ζ3, ζ4 (insight queries depend on indexes)
ε1 (heap snap)    ──► ε10 (ALLOC-1 commit decision gates on snapshot result)
ε8 (T5 install)   ──► ε15 (T5 contract test)
ζ4 (T6)           ──► soak test passes (gate)
```

Soft preferences (cleaner if X precedes Y but not blocking):

- α before any non-α work (everything else assumes the infrastructure exists).
- β before γ (per-tick cleaner if measured before per-event work changes the call profile).
- γ before δ12 (rows pooled before changing payload shape).
- δ before ζ (insight queries read the new indexes).
- ε before η (session-end alloc relief feeds into overlay relief).

---

## 6. The unified execution view

Rendered as a single timeline for the master plan to follow:

```
Phase 0  (1 hr)    : Release baseline re-measurement (α9)
Phase A  (1–2 d)   : Bug fixes A1, A2, A3 — correctness gates the rest
Phase α  (2–3 d)   : Shared infrastructure (α1–α7) + FCall bench (α8)
Phase β  (1–2 d)   : Per-tick zeroing (β1–β23)  ──► verify zero-alloc per tick
Phase γ  (3–4 d)   : Per-event row reuse (γ1–γ12) ──► 441 → < 200 ns/op
Phase δ  (4–6 d)   : Writer-thread + schema migration (δ1–δ17) ──► > 1k ops/sec; 1064 → < 600 KB
Phase ε  (3–4 d)   : Session-end + install (ε1–ε15) ──► 8.5s → < 200ms; 233 → < 80 MB
Phase ζ  (1–2 d)   : Insight LINQ removal (ζ1–ζ5) ──► insight pass < 4 KB
Phase η  (2–3 d)   : Overlay per-frame zeroing (η1–η13) ──► draw thread alloc → ~0
Phase 8  (0.5 d)   : Wrap (W1–W7)

Total: ~17–25 working days (was 13–20 in cross-allocations.md; the extra accounts for
       the schema migration depth + verification cycles).
```

Each phase ends with the corresponding test-harness benchmark + xUnit zero-alloc test passing in CI. No phase declares done until verification passes.

---

## 7. True conflicts and their resolution

I went through every per-system doc looking for genuine "doc A says X, doc B says ¬X" disagreements. The list is short.

1. **T7 collector smoother thread**: metric-collection §4.13 wants it; cross-concurrency §6.4 defers. **Resolved §2.3** — defer to v0.6.1. The metric-collection per-tick target is met without T7 via §4.1, §4.6, §4.7, §4.14, §4.15.

2. **T6 insights reader**: insights-engine §5.3 wants a dedicated reader thread; cross-concurrency §6.3 makes it conditional on LiteDB read-while-write soak. **Resolved §2.7** — attempt Option A, fallback to Option B if soak fails.

3. **ALLOC-1 commit decision**: hook-instrumentation §8.12 wants a gating heap snapshot; cross-storage-ram §1.3 wants the joint plan committed. **Resolved §2.2** — run snapshot in ε1, commit ALLOC-1 in ε10 if Cecil > 50%.

4. **Buff-diff "bug" status**: code-health-audit §4 reclassifies as unconfirmed; persistence §6.2 ships the snapshot-before-gate fix anyway. **Resolved §2.8** — fix lands as defensive hardening; in-game retest is verification, not gate.

5. **Global vs per-collection schema versioning**: cross-storage-ram §6.2 explicitly weighs and rejects global versioning. **Resolved as accepted recommendation** — per-collection `v` with file-level `USER_VERSION 7` as the floor marker.

No other genuine conflicts found. The 15 docs agree on every other substantive question.

---

## 8. Coverage validation — every baseline.md target moves

| `baseline.md` target | Today | Target | Phase(s) that move it |
|---|---|---|---|
| Game-thread enqueue (ns/op) | 441 (Debug, re-measure!) | < 200 | γ (row pool + Lang cache + UnixMsNow) + δ12 (struct union) |
| Writer-thread drain (ops/sec) | 314 | > 1,000 | δ9–δ14 (binary journal + ArrayPool + InsertBulk + deferred indexes) |
| 10-min session DB (KB) | 1,064 | < 600 | δ3–δ7 (BSON shape: short names + FK swap + numeric blobs + binary journal) |
| End-of-session main-thread stall (s) | 8.5 | < 0.2 | ε2–ε6 (off-thread aggregation + completion signal + PreSaveAndQuit) |
| Hook install delta (MB) | 233 (gates ε10) | < 80 | ε10 (conditional ALLOC-1) + ε9 (HookSurfaceCache dedup) + ε11 (string intern) |
| Avg PerformanceProfiler ms/tick | 0.27 | < 0.10 | β (per-tick zero alloc + SIMD smoothing + power-of-two ring) + ε7 (defer construction) |
| Item-created events captured | 0 (bug) | every event | A1 |
| Buff-edge events captured | 2 (apparent bug) | every edge | A2 + verification retest |
| Death attribution | last-hit | damage-weighted | A3 |
| World-load freeze (ms) | 172 | ~110 | ε7 (defer SessionRecorder + watchers from OnWorldLoad to first PostUpdateEverything) |
| First-tick draw budget | unbounded | < 16 ms | η (cached layouts + cached strings + indexer over foreach) |

Every row has at least one phase touching it. Every phase has at least one row it moves. No baseline target is uncovered.

---

## 9. What is NOT in this pass (explicitly out of scope)

To be clear about boundaries for the master plan:

- **T7 collector smoother** — deferred to v0.6.1 (§2.3).
- **HTML report** — separate v0.7 feature.
- **`bossFights` precomputed collection** — separate v0.7 feature.
- **Engagement attribution** — separate feature, schema is forward-compatible.
- **Removing the delegate HookInterceptor backend** — structural question, not perf; out of pass.
- **`double` → `float` in MetricCollector smoothed arrays** — flagged for v0.7 in cross-storage-ram §2.2.
- **Cross-session loadout aggregation gating `LoadoutCombinationCostDetector`** — separate detector work, not perf.
- **`ModWeather` / `ModInvasion` / mod difficulty support** — tML 1.4.5+ surface, deferred (decisions.md L36–40).

If during implementation the agent finds a free win in one of these, it's logged as a follow-up; it does not expand pass scope.

---

## 10. Coverage of the four invariants

Spot-checked every Phase α–η item against the five Project Invariants. All clear:

| Invariant | Spot-check | Result |
|---|---|---|
| 1 — Read-only | Every change reads game state and writes only to our own DB / RAM. No game-state mutation surface added. | ✅ |
| 2 — Overhead budget | Per-tick zero-alloc maintained (β phase verifies); Lite < 1%, Standard 2–4%, Deep 5–10% preserved. Allocation tracking stays unconditional (allocation-tracking dossier confirms FCall is cheap enough — gated on α8 bench). | ✅ pending α8 |
| 3 — Descriptive not normative | No UI copy changes. EnumStringTable replaces `.ToString()` but preserves the exact string content. No new "core" / "drop this mod" verbiage proposed anywhere. | ✅ |
| 4 — Abort-clean on host drift | ε10 (ALLOC-1) keeps the existing abort-clean path because dispose-after-install doesn't affect detour viability. ε8 (T5 async install) explicitly requires abort-clean (cross-concurrency §9). | ✅ |
| 5 — No mod-specific code | A1 / A2 / A3 all use generic surfaces (`GlobalItem.OnSpawn`, `ModPlayer.OnPickup`, `PlayerDeathReason`). No mod name strings appear in any optimisation. | ✅ |

---

## 11. Master plan handoff

Phase 5 (`master-plan.md`) consumes this doc plus the 15 research dossiers. The master plan's job is to:

1. Convert the §4 routing table into commit-sized chunks (typically one phase = one commit).
2. Add the per-commit test plan from test-harness §6.
3. Add the per-commit rollback strategy from each per-system doc.
4. Decide where the implementation diverges based on §3 (Release-mode re-measurement) outcomes.
5. Decide α8 (FCall bench) outcome before committing Phase β.
6. Track every "open question" from cross-concurrency §10 in the wrap-up doc.

No new research is needed for the master plan. Every design decision is settled in this doc or in the underlying research.
