# Data Pipeline (Unified Data Interface)

*Maturity: comprehensive · Stability: unstable — the registry contract and stage model are settled; new tab streams are added most feature passes.*

## Scope / Purpose

Landed in v0.10 (2026-05-21), expanded in v0.12 with the F1/F2/F3 foundations + 17 tab-specific Stats and Aggregators powering the reworked Timeline / Lag / Insights tabs. The pipeline is the brain: every number the mod produces flows through one named, typed stream; every consumer (router, exporter, future Mod.Call) looks up streams by name from `DataRegistry.Shared` instead of reaching into named subsystems.

The policy is structural, not aspirational: *if it produces a number it lives in `Data/`; if it consumes a number it asks the registry.* The dashboard router and the persistence streams format snapshots; they do not derive numbers.

## Boundaries / Ownership

This subsystem owns the `Data/` tree: the registry (`DataRegistry`), the stream contracts (`IDataStream`, the stage marker interfaces, `DataStage`), the per-tick `TickContext` and per-session `SessionContext`, the frozen snapshot contracts (`Data/Contracts/RolloutContracts.cs`), and every concrete stream under `Collectors/ Aggregators/ Stats/ Detectors/ Streams/`. It does **not** own the hot-path measurement engine (`Profiling/MetricCollector` + the hook backends) or the database (`Profiling/Persistence/`); it adapts over the former and writes through the latter. The dividing line is detailed under "What stays in `Profiling/`" below.

## Current Implemented Reality

### v0.12 expansion — foundations + tab streams

**Foundations** (`Data/Contracts/RolloutContracts.cs` holds every snapshot signature):

- `Data/Collectors/ModRosterScanner.cs` — F1, install-time roster of every loaded mod's content (items, NPCs, buffs, projectiles, mounts, accessories, biomes, bosses). Scanned once from `PostSetupContent`. Stream name `"modRoster"`.
- `Data/Aggregators/PerModUsageAggregator.cs` — F2, per-mod session usage counters fed from event streams (item creations, NPC spawns/kills, buff edges, loadout snapshots) + per-tick context fold (biome ticks, invasion edges, boss-presence diffs, accessory equipped ticks). Zero per-tick allocations. Stream name `"perModUsage"`.
- `Data/Aggregators/PerModCostTimeSeriesAggregator.cs` — F3, 1Hz per-mod cost buckets in a 3600-bucket (one hour) ring. Per-tick callback folds `MetricCollector.PerModCategoryRawMs` into the current bucket; closes the bucket on second boundary. Stream name `"perModCostTimeSeries"`.

**Timeline streams** (7):

- `Data/Aggregators/Segments/SegmentLifetimeStat.cs` — `"segmentLifetime"` (T2 lifetime delta per closed segment).
- `Data/Aggregators/Segments/SegmentModAttributionStat.cs` — `"segmentModAttribution"` (T1 per-segment per-mod ms waterfall).
- `Data/Stats/TransitionTrackStat.cs` — `"transitionTrack"` (T3 context-transition rows projected into time domain).
- `Data/Aggregators/SessionActivityHeatStripAggregator.cs` — `"activityHeatStrip"` (T4 minute-bucketed activity intensity).
- `Data/Stats/PerModContextAttendanceStat.cs` — `"attendance"` (T5 per-mod biome/invasion/boss roll-up, reads F2).
- `Data/Stats/DeathReplayStat.cs` — `"deathReplay"` (T6 30s pre-death event window per death).
- `Data/Stats/SessionChronicleStat.cs` — `"sessionChronicle"` (T7 timestamped factual sentences; Invariant-3-guarded vocabulary).

**Lag streams** (5):

- `Data/Aggregators/LagFingerprintAggregator.cs` — `"lagClusters"` (L1+L2 fingerprint clusters + cause×context cell matrix).
- `Data/Stats/GcPressureStat.cs` — `"gcPressure"` (L3 gen0/1/2 rates, paused ms, heap MB sparkline).
- `Data/Stats/PerSegmentLagDensityStat.cs` — `"segmentLagDensity"` (L4 events/min per segment vs baseline).
- `Data/Stats/AllocationCausalityStat.cs` — `"allocCausality"` (L6 5s-window allocation→GC chain per stall).
- `Data/Aggregators/LagRhythmAggregator.cs` — `"lagRhythm"` (L7 inter-event interval histogram + rhythm clusters).

**Insights streams** (5):

- `Insights/Publish/ModObservatoryStat.cs` — `"modObservatory"` (I1+I3+I4 per-mod cards composing roster + usage + cost + biome attendance + loadout influence).
- `Insights/Publish/DormantSurfaceStat.cs` — `"dormantSurface"` (I2 usage/roster ratios + dormant tier classification).
- `Insights/Publish/CrossCuttingSignalStat.cs` — `"crossCutting"` (I5 InsightRecord rollup grouped by pattern class).
- `Insights/Publish/EngagementCostScatterStat.cs` — `"engagementCost"` (I6 per-mod (usageShare, cpuShare, rosterSize) tuples).
- `Insights/Publish/ModInteractionAggregator.cs` — `"modInteraction"` (I7 pairwise Pearson correlation over F3 time series, cached 5s).

The interpreted I-series stats live in the top-level `Insights/` module (`Insights.Publish` namespace) rather than under `Data/`; they are still registered into `DataRegistry.Shared` like any other stream (`PerformanceProfiler.RegisterDataPipeline`). The insights engine itself (`Insights/InsightsEngine.cs` + `Insights/Detectors/`) is documented in `systems/insights-engine.md`.

All 17 + foundations registered in `PerformanceProfiler.RegisterDataPipeline`. Honest limitations documented in each file's class doc-comment (e.g. lag clusters lack per-event EventContext yet; ModObservatory's biome attendance is per-mod aggregate not per-biome breakdown; etc.).

---


The 12-step migration plan was deleted once the work landed in v0.11; this file is the canonical reality.

### Folder layout

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
│   ├── Baseline.cs                 Rolling baseline statistics.
│   ├── ModImpactScorer.cs          Per-mod impact ranking model.
│   └── HookCoverageView.cs         Backend-aware coverage projection.
├── Detectors/
│   ├── SpikeDetector.cs            Frame-time spike threshold detector.
│   └── StallDetector.cs            Multi-tick stall + GC pause detector.
│                                   (The insights engine + its detectors no
│                                    longer live here — they moved to the
│                                    top-level Insights/ module. See
│                                    systems/insights-engine.md.)
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

### Contracts

- **`IDataStream`** — every stream declares `Name`, `Cadence` (PerTick / OneHz / OnEvent / OnDemand), `Stage` (which lifecycle role it plays), plus `Initialise(SessionContext) / Reset / Dispose` and `CurrentSnapshotBoxed()`.
- **`IDataStream<TSnapshot>`** — typed read accessor. The dashboard router calls `DataRegistry.Shared.Lookup<TSnapshot>(name).CurrentSnapshot()` — no boxing on the hot dashboard path.
- **`IHasPerTickCallback`** — marker interface implemented by every PerTick stream. `DataRegistry.Freeze()` captures the per-tick callback array; `ProfilerSystem.PostUpdateEverything` drives the loop with a for-loop over the frozen array (zero virtual dispatch).
- **Snapshots are immutable values.** Each `CurrentSnapshot()` returns a fresh struct or readonly record. No caller has to free anything; producers do not cache snapshots.

### Stages

| Stage | Responsibility | Example |
|---|---|---|
| Collector | Capture raw signal per tick (zero-alloc on hot path) | FrameTimeCollector |
| Aggregator | Group / fold many ticks into structured bins | HeatmapAggregator |
| Stat | Derive numbers from aggregator/collector state | KpiStat |
| Detector | Run threshold logic + emit events (off-thread OK) | SpikeDetector (Profiling/) |
| Stream | Persistence-facing writer | TickAggregateStream (Data/Streams/) |
| Exporter | Output-facing reader (HTTP, future Mod.Call) | DashboardRouter |

## Key Interfaces / Data Flow

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

## Implemented Outputs / Artifacts

The pipeline's output is the set of named, typed snapshots every consumer reads. The dashboard router (`Web/DashboardRouter.*`) pulls them per `/api/*` endpoint; the persistence streams (`Data/Streams/*`) write them to LiteDB through `SessionRecorder`; a future Mod.Call API would dispatch the same registry. Each stream's snapshot shape is frozen in `Data/Contracts/RolloutContracts.cs`.

### Policy commitments

- **If it produces a number, it lives in `Data/`.** Routers and exporters must not derive numbers; they format snapshots into wire shapes.
- **`ProfilerSystem.Collector` is `internal`.** External consumers route through the registry. Same-assembly consumers inside `Data/` and `Profiling/` keep direct access for the hot path.
- **Per-tick callbacks are frozen.** `DataRegistry.PerTickCallbacks` is an immutable array snapshot; mutations happen at `Freeze`, never per-tick.

## Known Issues / Active Risks

- **Stream names are stringly-typed coupling.** Every consumer looks a stream up by its stable string name (the `RolloutStreamNames` constants + the names passed to `Register`). A typo or a rename without updating every call site silently returns null at the lookup. Downstream impact: a dashboard endpoint serves empty JSON rather than failing loudly. The contract file mitigates this by centralising the name constants.
- **Per-tick callback array is frozen at world load.** `DataRegistry.Freeze()` snapshots the `PerTickCallbacks` array once per world. A stream registered after `Freeze` would not be driven per-tick until the next world load. Today every stream is registered at `Mod.Load`, before any world exists, so this is not a live bug.

## Partial / In Progress

Nothing structurally in progress. New tab streams are added per feature pass against the stable registry contract; the v0.12 streams each document in their own class doc-comment the data their producer cannot yet emit (per-event `EventContext` on spikes/stalls, per-biome breakdown in F2, biome-at-death-time), so a future pass knows what to add without re-deriving the gap.

## Planned / Missing / Likely Changes

- **A second consumer (the post-session HTML report exporter)** is the planned next consumer that makes the registry pay for itself beyond the dashboard; sketched in `notes/future-html-report.md`.
- **A Mod.Call API** would dispatch the registry by name, giving other mods read access to the profiler's numbers without a direct reference.

## Durable Notes / Discarded Approaches

The original framing note for this pipeline is `notes/future-unified-data-interface.md` (it carries the two-tier folder-reorg-then-runtime-registry reasoning). The 12-step migration plan that drove the v0.11 physical move was deleted once the work landed; the file moves are visible in git history via `git log --diff-filter=R --name-status`.

### Notable migration scars

- **DashboardRouter.BuildNow (the 250ms-poll endpoint)** was migrated last; pre-v0.10 it reached into `ProfilerSystem.Collector.History` from the HTTP worker thread, racing the game thread that mutates the ring. All endpoints are now race-free via snapshots.
- **Heatmap aggregation** lived inline in DashboardRouter.BuildHeatmap pre-migration. Extracting it to `HeatmapAggregator` was the canonical "kill the inline math" step.
- **Cadence vs callback honesty.** Three collectors initially declared `PerTick` cadence with no-op delegates. v0.10 audit corrected this to `OnDemand` (pull-side adapters; MetricCollector itself owns the per-tick capture).

### What stays in `Profiling/`

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

## Obsolete / No Longer Relevant

- **Inline calculation in `DashboardRouter` and JS.** Pre-v0.10, the router did heatmap bucketing and median maths inline, and some derivation lived in the dashboard JS. Those paths were migrated into pipeline stages (`HeatmapAggregator`, `KpiCalculator`, etc.); deriving a persist-worthy number outside a `Data/` stage is now disallowed by convention (`notes/conventions.md` §15). The router and JS that remain only format and present.
- **Direct `ProfilerSystem.Collector` reads by external consumers.** Made `internal` in v0.10. Reaching into the live collector from a consumer (the BuildNow data-race) is superseded by the name-keyed snapshot lookup.
