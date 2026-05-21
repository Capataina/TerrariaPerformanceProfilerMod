# Data Pipeline (Unified Data Interface)

Landed in v0.10 (2026-05-21). The pipeline is the brain: every number the mod produces flows through one named, typed stream; every consumer (router, exporter, future Mod.Call) looks up streams by name from `DataRegistry.Shared` instead of reaching into named subsystems.

The migration plan in `context/plans/unified-data-pipeline.md` documents the original 12-step strategy; this file is the canonical reality post-implementation.

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
│   └── AllocationCollector.cs      Wraps optional allocation arrays.
├── Aggregators/
│   ├── HeatmapAggregator.cs        DB + in-memory heatmap bucketing.
│   └── SegmentAggregator.cs        Adapter over SegmentDetector + SegmentStore.
└── Stats/
    ├── KpiStat.cs                  /api/now headline numbers.
    ├── EventsFeedStat.cs           /api/events feed.
    ├── SelfHealthStat.cs           Process WorkingSet + per-hook overhead.
    ├── SpikesStat.cs               Latest spike windows.
    ├── StallsStat.cs               Latest stall events.
    └── InsightsStat.cs             Live insights from InsightsEngine.
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

## Deferred work

The original migration plan included physical file moves (steps 7-10): `ContextTagger → Data/Collectors/EventContextCollector.cs`, `SegmentDetector` split, `EventAggregator → BiomeBucketAggregator`, and the `Profiling/Persistence/Streams/**` move to `Data/Streams/`. These are pure renames with no behavioural impact; they remain in `Profiling/*` for now. The pipeline is functionally complete via the API-level migration in step 11 + the visibility tightening in step 12.

If the moves are picked up, use `git mv` to preserve blame and update the namespaces in lockstep with the consumer references (the rename surface is large but mechanical).
