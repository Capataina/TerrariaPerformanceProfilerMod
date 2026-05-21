# Data Pipeline (Unified Data Interface)

Landed in v0.10 (2026-05-21). The pipeline is the brain: every number the mod produces flows through one named, typed stream; every consumer (router, exporter, future Mod.Call) looks up streams by name from `DataRegistry.Shared` instead of reaching into named subsystems.

The 12-step migration plan was deleted once the work landed in v0.11; this file is the canonical reality.

## Folder layout

```
Data/
├── DataRegistry.cs       Process-wide stream registry. .Shared singleton.
├── DataStage.cs          Enum: Collector | Aggregator | Stat | Detector | Stream | Exporter
├── IDataStream.cs        Base contract + IDataStream<TSnapshot> + marker
│                         interfaces (IDataCollector / IDataAggregator /
│                         IDataStat / IDataDetector / IDataExporter)
├── TickContext.cs        readonly ref struct passed to per-tick callbacks
├── SessionContext.cs     Immutable per-session record passed to Initialise
├── Collectors/
│   ├── FrameTimeCollector.cs       Wraps MetricCollector frame history.
│   ├── HookCpuCollector.cs         Wraps per-mod/per-hook CPU arrays.
│   ├── AllocationCollector.cs      Wraps optional allocation arrays.
│   └── ContextTagger.cs            Per-tick game-state snapshotter (biomes,
│                                   bosses, weather, invasion, subworld).
├── Aggregators/
│   ├── HeatmapAggregator.cs        DB + in-memory heatmap bucketing.
│   ├── SegmentAggregator.cs        Adapter exposing SegmentDetector + Store.
│   ├── EventAggregator.cs          Per-dimension bucket aggregator for the
│   │                               Events tab; consumes EventContext stream.
│   ├── PerModAttribution.cs        Hot-path per-mod / per-hook accumulator
│   │                               (called from the IL-emitted timing path).
│   ├── PerModSample.cs             Per-frame per-mod sample struct.
│   ├── PerTickAttributionRing.cs   Ring buffer of per-tick per-mod samples.
│   └── Segments/
│       ├── SegmentDetector.cs      Opens / closes Biome/Boss/etc segments.
│       ├── SegmentStore.cs         Ring of closed segments + DB writer.
│       ├── OpenSegment.cs          In-flight segment (pooled).
│       ├── Segment.cs              Closed-segment value record.
│       ├── SegmentFamily.cs        Family enum (Biome/Boss/Weather/...).
│       ├── SegmentNameTable.cs     Display-name resolver per family.
│       └── SegmentPromoter.cs      Decides which closed segments get badges.
├── Stats/
│   ├── KpiStat.cs                  /api/now headline numbers.
│   ├── KpiCalculator.cs            Pure logic computing KpiSnapshot.
│   ├── KpiSnapshot.cs              Immutable KPI value struct.
│   ├── EventsFeedStat.cs           /api/events feed adapter.
│   ├── EventsFeed.cs               Pure feed builder used by EventsFeedStat.
│   ├── SelfHealthStat.cs           Process WorkingSet + per-hook overhead.
│   ├── SpikesStat.cs               Latest spike windows.
│   ├── StallsStat.cs               Latest stall events.
│   ├── InsightsStat.cs             Live insights from InsightsEngine.
│   ├── Baseline.cs                 Rolling baseline statistics.
│   ├── ModImpactScorer.cs          Per-mod impact ranking model.
│   └── HookCoverageView.cs         Backend-aware coverage projection.
├── Detectors/
│   ├── SpikeDetector.cs            Frame-time spike threshold detector.
│   ├── StallDetector.cs            Multi-tick stall + GC pause detector.
│   └── Insights/
│       ├── InsightsEngine.cs       Off-thread evaluation driver.
│       ├── InsightStore.cs         Live + history records, confidence promotion.
│       ├── InsightRecord.cs        Immutable record value type.
│       ├── InsightRenderer.cs      Descriptive string templates.
│       ├── RankingScorer.cs        Pattern-aware insight ranking.
│       ├── IInsightDetector.cs     Detector contract.
│       └── Detectors/              10 concrete detectors (HotHookDominance,
│                                   AllocationBurst, FreeRemovalCandidate,
│                                   PeakContributorToSpike, SegmentOutlier,
│                                   SegmentTopMod, SegmentDeathCorrelation,
│                                   GcPauseCulprit, etc.).
└── Streams/
    ├── IPersistenceStream.cs       Contract: Apply(DbWriteOp), Reconstruct.
    ├── StreamRegistry.cs           Maps DbOpKind → IPersistenceStream.
    ├── SessionRecorder.cs          Orchestrator — drives all streams.
    └── *Stream.cs                  14 concrete streams (Session, Modlist,
                                    Spike, Stall, StallCluster, Segment,
                                    TickAggregate, ContextTransition,
                                    Insight, PlayerDeath, WorldSnapshot,
                                    Interaction, PerSessionAggregate,
                                    StreamJson helpers).
```

## Contracts

- **`IDataStream`** — every stream declares `Name`, `Cadence` (PerTick / OneHz / OnEvent / OnDemand), `Stage` (which lifecycle role it plays), plus `Initialise(SessionContext) / Reset / Dispose` and `CurrentSnapshotBoxed()`.
- **`IDataStream<TSnapshot>`** — typed read accessor. The dashboard router calls `DataRegistry.Shared.Lookup<TSnapshot>(name).CurrentSnapshot()` — no boxing on the hot dashboard path.
- **`IHasPerTickCallback`** — marker interface implemented by every PerTick stream. `DataRegistry.Freeze()` captures the per-tick callback array; `ProfilerSystem.PostUpdateEverything` drives the loop with a for-loop over the frozen array (zero virtual dispatch).
- **Snapshots are immutable values.** Each `CurrentSnapshot()` returns a fresh struct or readonly record. No caller has to free anything; producers do not cache snapshots.

## Stages

| Stage | Responsibility | Example |
|---|---|---|
| Collector | Capture raw signal per tick (zero-alloc on hot path) | FrameTimeCollector |
| Aggregator | Group / fold many ticks into structured bins | HeatmapAggregator |
| Stat | Derive numbers from aggregator/collector state | KpiStat |
| Detector | Run threshold logic + emit events (off-thread OK) | SpikeDetector (Profiling/) |
| Stream | Persistence-facing writer | TickAggregateStream (Profiling/Persistence/Streams/) |
| Exporter | Output-facing reader (HTTP, future Mod.Call) | DashboardRouter |

## Lifecycle

```
PerformanceProfiler.Load
   └── RegisterDataPipeline()      Registers every IDataStream
                                   (Collectors, Aggregators, Stats).

ProfilerSystem.OnWorldLoad
   └── DataRegistry.Shared.InitialiseAll(sessionCtx)
       ├── Each stream.Initialise(sessionCtx)
       └── DataRegistry.Freeze()    Rebuilds PerTickCallbacks array.

ProfilerSystem.PostUpdateEverything (per tick, game thread)
   ├── MetricCollector.EndTick      Hot-path: owns per-mod EMAs etc.
   └── for i in PerTickCallbacks:
         callback(in TickContext)    Currently empty — Collector adapters
                                     are OnDemand pull-side adapters.

HTTP worker thread (loopback dashboard)
   └── DashboardRouter.BuildXxx
       └── DataRegistry.Shared.Lookup<TSnapshot>(name)
                                  .CurrentSnapshot()
                                  ← pull, no boxing

ProfilerSystem.OnWorldUnload
   └── DataRegistry.Shared.ResetAll()      Each stream.Reset().

PerformanceProfiler.Unload
   └── DataRegistry.Shared.DisposeAll()    Tears down and empties
                                           the registry.
```

## Policy commitments

- **If it produces a number, it lives in `Data/`.** Routers and exporters must not derive numbers; they format snapshots into wire shapes.
- **`ProfilerSystem.Collector` is `internal`.** External consumers route through the registry. Same-assembly consumers inside `Data/` and `Profiling/` keep direct access for the hot path.
- **Per-tick callbacks are frozen.** `DataRegistry.PerTickCallbacks` is an immutable array snapshot; mutations happen at `Freeze`, never per-tick.

## Notable migration scars

- **DashboardRouter.BuildNow (the 250ms-poll endpoint)** was migrated last; pre-v0.10 it reached into `ProfilerSystem.Collector.History` from the HTTP worker thread, racing the game thread that mutates the ring. All endpoints are now race-free via snapshots.
- **Heatmap aggregation** lived inline in DashboardRouter.BuildHeatmap pre-migration. Extracting it to `HeatmapAggregator` was the canonical "kill the inline math" step.
- **Cadence vs callback honesty.** Three collectors initially declared `PerTick` cadence with no-op delegates. v0.10 audit corrected this to `OnDemand` (pull-side adapters; MetricCollector itself owns the per-tick capture).

## What stays in `Profiling/`

Things that aren't streams themselves but support them — kept in `Profiling/` because they're not "data" in the pipeline sense, they're infrastructure:

- **`MetricCollector.cs`** — the hot-path per-tick engine. Owns the EMA loops, the ring buffer, the spike/stall lists. Streams adapt over it; it isn't a stream itself.
- **Hook machinery** — `HookInterceptor.cs`, `ILHookInterceptor.cs`, `HookBackend.cs`, `HookCategoryRouter.cs`, `HookSurfaceCache.cs`, `ProbeStack.cs`. The instrumentation layer that produces the raw signal `MetricCollector` consumes.
- **`ProfilerSystem.cs`** — the `ModSystem` lifecycle owner. Drives the per-tick callback loop into `Data/`.
- **`ProfilerSelfHealth.cs`** — process-wide health (read by `SelfHealthStat`).
- **Caches / primitives** — `LangNameCache`, `ModOwnerCache`, `RingBuffer`, `TickFrame`, `Time`, `EnumStringTable`, `ProfilerFocusProbe`.
- **`Profiling/Events/`** support types — `BiomeBitset`, `BiomeRegistry`, `BossSampler`, `BossSlotArray`, `BucketStats`, `EventContext`, `GameMode`, `InvasionId`, `SubworldProbe`, `WeatherFlags`, `WeatherSources`. Internal data structures that `ContextTagger` (in `Data/Collectors/`) and `EventAggregator` (in `Data/Aggregators/`) operate on.
- **`Profiling/Persistence/`** infrastructure — `ProfilerDatabase`, `DbWriterThread`, `EventJournal`, `DbWriteOp`, `BsonShortNames`, `Migrations`, `ModlistFingerprint`, `ProfilerPaths`, `PersistenceFileNames`, `ProfilerCompactCommand`, `SessionSummaryLogger`, `TickDownsampler`, `WorldSnapshotter`, `ContextTransitionWatcher`, `PlayerDeathDetector`, `LegacyJsonImporter`, `Commands/`, `Interactions/`, `Records/`. The DB layer + the side-channel event detectors that feed the streams. Streams themselves now live in `Data/Streams/`; their orchestration and the database that backs them stay here.
- **`Profiling/Pools/`** — `ListPool`, `RowPool`, `IPoolReset`. Pooling primitives the streams use.

The dividing line: **if it produces a stream-shaped artefact, it's in `Data/`. If it's infrastructure for producing or storing them, it stays in `Profiling/`.**
