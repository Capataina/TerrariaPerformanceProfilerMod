# Unified Data Pipeline — Implementation Plan

> **Status (2026-05-21):** Substance complete. Steps 1-6, 11, 12 landed in v0.10. Steps 7-10 (the physical file moves) landed in v0.11 as a follow-up after the v0.10 deferral was called out. Every class that produces a stream-shaped artefact now lives in `Data/`: collectors, aggregators, stats, detectors, persistence streams. Canonical post-implementation reality is `context/systems/data-pipeline.md`.
>
> **Two sub-items deliberately not done** (cosmetic, no behavioural impact — pick up if/when convenient):
>
> 1. **`SegmentDetector` was moved wholesale, not split.** Step 7 called for splitting it into `SegmentEdgeCollector` (active-key sweep) + `SegmentAggregator` (open-segment accumulation + close emission). The class is now at `Data/Aggregators/Segments/SegmentDetector.cs` as a single class. The split is a real hot-path refactor; risk wasn't worth it without a measurable forcing function. The `Data/Aggregators/SegmentAggregator.cs` adapter (introduced in step 5) is the registry-facing surface and already serves the same external-API role; the internal split would only re-organise the detector's private state machine.
> 2. **Cosmetic renames not done.** Plan called for `ContextTagger → EventContextCollector`, `EventAggregator → BiomeBucketAggregator`, `PerModAttribution → PerModAggregator`. The classes moved to the correct folders but kept their original names. `PerModAttribution` in particular is referenced from IL-emit metadata in `ILHookInterceptor` — renaming would ripple through the detour IL stream and is a non-trivial change for an aesthetic gain. The existing names still describe what the classes do; future readers find them in the registry by stream name, not by class name.

**Owner:** next session, single developer, ~1 week elapsed.
**Source of truth for intent:** `context/notes/future-unified-data-interface.md`.

## 1. Goal

The pipeline is the brain. The DB stores what it produces. The HTML renders what it serves. Neither does math. Every derived number in the mod is computed once, named, registered, and emitted by exactly one stage of a `Data/` pipeline. Consumers (`DashboardRouter`, the future session-report exporter, a future `Mod.Call` API) iterate `DataRegistry.Shared.All` or call `DataRegistry.Shared.Lookup(name).CurrentSnapshot()`; they never reach into a named subsystem and they never perform arithmetic. If it produces a number it lives in `Data/`. If it consumes a number it asks the registry.

## 2. Current state inventory

Mapping of every data-bearing file to its target stage. Files marked **split** become two artefacts; files marked **move** are a pure relocation; files marked **wrap** keep their hot-path identity but gain a registry-facing adapter.

| Current file | Becomes | Stage | Notes |
|---|---|---|---|
| `Profiling/MetricCollector.cs` | `Data/Collectors/FrameTimeCollector` (+ `HookCpuCollector`, `AllocationCollector` views) | Collectors | wrap. The class stays; new thin adapters expose `IDataCollector` over its already-public read accessors. The hot-path `BeginTick`/`EndTick`/`Add` methods are NOT virtualised. |
| `Profiling/Events/ContextTagger.cs` | `Data/Collectors/EventContextCollector` | Collectors | wrap. Already per-tick; expose `CurrentSnapshot()` returning `in EventContext`. |
| `Profiling/Events/EventAggregator.cs` | `Data/Aggregators/BiomeBucketAggregator` | Aggregators | move + rename. Cadenced per-tick but off the hot path. |
| `Profiling/PerModAttribution.cs` | `Data/Aggregators/PerModAggregator` | Aggregators | wrap. Static class; expose a registry adapter that calls into existing accessors. |
| `Profiling/Segments/SegmentDetector.cs` | **split**: `Data/Collectors/SegmentEdgeCollector` (per-tick edge sweep) + `Data/Aggregators/SegmentAggregator` (open-segment accumulation, close emission) | Collectors + Aggregators | The current class fuses both responsibilities. Splitting cleanly localises the per-tick allocation contract on the collector side and lets the aggregator be unit-tested without a tick driver. |
| `Profiling/Segments/SegmentStore.cs` | `Data/Aggregators/SegmentStore` (in-memory) + `Data/Streams/SegmentStream` (persistence) | Aggregators + Streams | already partly split; tighten the boundary so the store never knows about LiteDB and the stream owns enqueue. |
| `Profiling/Stats/KpiCalculator.cs` + `KpiSnapshot.cs` | `Data/Stats/KpiStat` (implementing `IDataStat<KpiSnapshot>`) | Stats | move. Already pure. Becomes the canonical example. |
| `Profiling/Stats/EventsFeed.cs` | `Data/Stats/EventsFeedStat` | Stats | move. Caller flips from `EventsFeed.Build(c, store)` to `registry.Lookup("eventsFeed").CurrentSnapshot()`. |
| `Profiling/Baseline.cs` | `Data/Stats/BaselineMedianStat` | Stats | move. |
| `Profiling/SpikeDetector.cs` | `Data/Detectors/SpikeDetector` | Detectors | move. Per-tick today; stays per-tick, registered as a direct callback. |
| `Profiling/StallDetector.cs` | `Data/Detectors/StallDetector` | Detectors | move. |
| `Profiling/Insights/InsightsEngine.cs` + `IInsightDetector` + `Detectors/*` | `Data/Detectors/Insights/` (engine + per-pattern detectors) | Detectors | move. Already off-thread under `Interlocked` latch (`ProfilerSystem.cs:416`); preserve that. |
| `Profiling/ModImpactScorer.cs` | `Data/Stats/ModImpactStat` | Stats | move. |
| `Profiling/HookCoverageView.cs` | `Data/Stats/HookCoverageStat` | Stats | move. |
| `Profiling/ProfilerSelfHealth.cs` | `Data/Stats/SelfHealthStat` | Stats | wrap. Process-wide lifecycle stays; registry sees a static snapshot view. |
| `Profiling/Persistence/IPersistenceStream.cs` + `StreamRegistry.cs` + `Streams/*` | unchanged on disk; conceptually pinned under `Data/Streams/` | Streams | These already implement the registry-of-streams pattern; the new `IDataStream<TPoint,TSnapshot>` interface lives alongside, NOT in place of, `IPersistenceStream`. See §9. |
| `Profiling/Persistence/SessionRecorder.cs` | `Data/Streams/SessionRecorder` (driver) | Streams | move; the routing logic is already stream-aware. |
| `Web/DashboardRouter.cs` | `Data/Exporters/DashboardRouter` | Exporters | move + thin out. Every `BuildXxx` method becomes a dispatch to `Lookup(name).CurrentSnapshot()`; the heatmap bucketing in particular moves out (see §5). |
| `Web/Server/DashboardHttpServer.cs`, `DashboardAssets*.cs` | stay in `Web/` | n/a | These are HTTP transport + static assets, not data. |
| `UI/Overlay/**` | stay in place, wrapped in `#if false` | n/a | Out of scope. |

**Files explicitly NOT moved:** `Profiling/HookInterceptor.cs`, `ILHookInterceptor.cs`, `HookBackend*.cs`, `HookCategoryRouter.cs`, `HookSurfaceCache.cs`, `PerTickAttributionRing.cs`, `ProbeStack.cs`, `RingBuffer.cs`, `Pools/*`, `Util/*`. These are infrastructure under the pipeline, not pipeline stages themselves. They keep their current paths.

## 3. Stage definitions

### Collectors

**Responsibility:** read raw values from the game and hold them in a per-tick or per-event structure.
**Contract:**
- Input: callbacks from `ProfilerSystem.PreUpdateEntities` / `PostUpdateEverything`, or game hooks via `HookInterceptor`.
- Output: typed in-memory state (rolling rings, latest snapshot struct).
- Threading: game thread only.
- Lifecycle: constructed on `OnWorldLoad` deferred init, discarded on `OnWorldUnload`.
- Allocation: zero per tick after warmup (Invariant 2).
**Cadence:** PerTick or OnEvent.
**Examples:** `FrameTimeCollector` (wraps `MetricCollector`), `EventContextCollector` (wraps `ContextTagger`), `SegmentEdgeCollector` (per-tick active-key sweep extracted from `SegmentDetector`).

### Aggregators

**Responsibility:** organise raw collector values into structured groups (per-mod, per-biome, per-segment).
**Contract:**
- Input: a `TickContext` reference plus already-collected snapshots from the registry.
- Output: queryable group state (dictionaries, segment buckets).
- Threading: game thread (cadenced per-tick, but does NOT take the per-tick critical path).
- Lifecycle: same as Collectors.
- Allocation: zero per tick once warmed; segment-close allocations permitted as today.
**Cadence:** PerTick or OneHz.
**Examples:** `PerModAggregator` (wraps `PerModAttribution`), `BiomeBucketAggregator` (was `EventAggregator`), `SegmentAggregator` (open-segment accumulation extracted from `SegmentDetector`), `HeatmapAggregator` (the bucketing currently inline in `DashboardRouter.BuildHeatmap`).

### Stats

**Responsibility:** derived calculations from aggregates, expressed as immutable snapshot structs.
**Contract:**
- Input: aggregator state via the registry; never raw collector reads.
- Output: a `TSnapshot` struct (or readonly record) with no behaviour.
- Threading: invoked by snapshot reads (HTTP request threads) or 1 Hz scheduler.
- Lifecycle: stateless; instances may exist but hold no per-tick state.
- Allocation: per-call allowed (snapshots are not on the hot path).
**Cadence:** OnDemand.
**Examples:** `KpiStat` (already exemplary at `Profiling/Stats/KpiCalculator.cs:16`), `EventsFeedStat`, `BaselineMedianStat`, `ModImpactStat`, `HookCoverageStat`.

### Detectors

**Responsibility:** fire when patterns match.
**Contract:**
- Input: aggregator + stat snapshots.
- Output: discrete event records (`SpikeWindow`, `InsightRecord`, `StallEvent`) emitted into a sink.
- Threading: **background thread**, gated by an `Interlocked.CompareExchange` latch so the detector cannot overlap itself. Preserve the pattern at `ProfilerSystem.cs:416-438`.
- Lifecycle: tied to world load.
- Allocation: per-emission only; no per-tick allocation.
**Cadence:** OneHz (insights), OnEvent (spike, stall — fires whenever an aggregator emits a candidate window).
**Examples:** `SpikeDetector`, `StallDetector`, `InsightsEngine` + its detectors, `SegmentOutlierDetector`.

### Streams

**Responsibility:** persist records to LiteDB.
**Contract:** identical to the existing `IPersistenceStream` at `Profiling/Persistence/IPersistenceStream.cs:32`. Apply on the writer thread, idempotent, declarative indexes.
**Threading:** `DbWriterThread`.
**Cadence:** OnEvent (drained from queue).
**Examples:** every file under `Profiling/Persistence/Streams/`.

### Exporters

**Responsibility:** serve data to outside consumers.
**Contract:**
- Input: registry lookups only. **Never** reads collector state directly. **Never** does arithmetic.
- Output: HTTP JSON (`DashboardRouter`), session-report HTML (future), Mod.Call payloads (future).
- Threading: HTTP request threads, or end-of-session worker for the session report.
- Lifecycle: process-wide.
**Cadence:** OnDemand.
**Examples:** `DashboardRouter`, future `SessionReportExporter`, future `ChatCommandExporter`.

## 4. Core interface design

```csharp
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;

namespace PerformanceProfiler.Data;

/// <summary>Per-tick payload handed to collectors. Ref-passed; never boxed.</summary>
public readonly ref struct TickContext
{
    public readonly long TickIndex;
    public readonly long UnixMs;
    public readonly double FrameTimeMs;
    public readonly int NpcCount;
    public readonly int ProjectileCount;
    public readonly int DustCount;
    public readonly ref readonly EventContext Context;

    public TickContext(long tickIndex, long unixMs, double frameMs,
        int npcCount, int projectileCount, int dustCount,
        in EventContext ctx)
    {
        TickIndex = tickIndex; UnixMs = unixMs; FrameTimeMs = frameMs;
        NpcCount = npcCount; ProjectileCount = projectileCount; DustCount = dustCount;
        Context = ref ctx;
    }
}

/// <summary>Per-session payload handed at initialise/dispose.</summary>
public sealed class SessionContext
{
    public required LiteDB.ObjectId SessionId { get; init; }
    public required string ModlistFingerprint { get; init; }
    public required bool TracksAllocations { get; init; }
    public required string HookBackendMode { get; init; }
    public Persistence.ProfilerDatabase? Database { get; init; }
}

public enum DataStreamCadence
{
    PerTick,    // Capture(ctx) on every game tick. Hot-path discipline applies.
    OneHz,      // Capture(ctx) every 60th tick.
    OnEvent,    // Emits when an upstream condition fires; no scheduled capture.
    OnDemand,   // Pure-pull. CurrentSnapshot() is the only entry point.
}

/// <summary>Marker base. Use the generic IDataStream&lt;,&gt; for typed access.</summary>
public interface IDataStream
{
    string Name { get; }
    DataStreamCadence Cadence { get; }
    DataStage Stage { get; }

    void Initialise(SessionContext session);
    void Reset();
    void Dispose();

    /// <summary>Boxed snapshot, used by reflective exporters that iterate All.</summary>
    object CurrentSnapshotBoxed();
}

public interface IDataStream<TSnapshot> : IDataStream
{
    TSnapshot CurrentSnapshot();
}

public enum DataStage { Collector, Aggregator, Stat, Detector, Stream, Exporter }

/// <summary>Per-tick callback. Static so the call site is non-virtual.</summary>
public delegate void TickCapture(in TickContext ctx);

public interface IDataCollector<TSnapshot> : IDataStream<TSnapshot>
{
    /// <summary>
    /// Returns the per-tick capture delegate, or null when the collector is OnDemand.
    /// The driver caches the delegate once at Initialise and invokes it directly.
    /// </summary>
    TickCapture? PerTickCallback { get; }
}

public interface IDataAggregator<TSnapshot> : IDataStream<TSnapshot> { }
public interface IDataStat<TSnapshot> : IDataStream<TSnapshot> { }

public interface IDataDetector<TSnapshot> : IDataStream<TSnapshot>
{
    /// <summary>Run the detection pass. Invoked off-thread; latch enforced by the engine.</summary>
    void Evaluate(long nowTick, long sessionLengthTicks);
}

public interface IDataExporter : IDataStream
{
    // Exporters do not produce a snapshot of their own; CurrentSnapshotBoxed
    // returns the exporter's most-recent payload digest (for diagnostics).
}

public sealed class DataRegistry
{
    public static DataRegistry Shared { get; } = new DataRegistry();

    private readonly ConcurrentDictionary<string, IDataStream> _byName = new(StringComparer.Ordinal);
    private readonly List<IDataStream> _ordered = new();
    private readonly object _gate = new();

    public void Register(IDataStream stream)
    {
        if (!_byName.TryAdd(stream.Name, stream))
            throw new InvalidOperationException($"Data stream collision: '{stream.Name}'.");
        lock (_gate) _ordered.Add(stream);
    }

    public IDataStream? Lookup(string name)
        => _byName.TryGetValue(name, out var s) ? s : null;

    public IDataStream<TSnapshot>? Lookup<TSnapshot>(string name)
        => _byName.TryGetValue(name, out var s) ? s as IDataStream<TSnapshot> : null;

    public IReadOnlyList<IDataStream> All
    {
        get { lock (_gate) return _ordered.ToArray(); }
    }

    /// <summary>
    /// Returns the array of per-tick callbacks captured at Initialise. Stable
    /// reference; the driver iterates it directly without an enumerator. Callers
    /// must not mutate the array.
    /// </summary>
    public TickCapture[] PerTickCallbacks { get; private set; } = Array.Empty<TickCapture>();

    /// <summary>Called once after every stream is registered and Initialise has been called.</summary>
    public void Freeze()
    {
        var arr = new List<TickCapture>(_ordered.Count);
        foreach (var s in _ordered)
        {
            if (s.Cadence != DataStreamCadence.PerTick) continue;
            if (s is IHasPerTickCallback h && h.PerTickCallback is { } cb)
                arr.Add(cb);
        }
        PerTickCallbacks = arr.ToArray();
    }

    public void InitialiseAll(SessionContext s) { foreach (var x in All) x.Initialise(s); Freeze(); }
    public void ResetAll() { foreach (var x in All) x.Reset(); }
    public void DisposeAll() { foreach (var x in All) x.Dispose(); _byName.Clear(); lock (_gate) _ordered.Clear(); PerTickCallbacks = Array.Empty<TickCapture>(); }
}

/// <summary>Non-generic shim so the registry can pull the delegate without reflection.</summary>
public interface IHasPerTickCallback { TickCapture? PerTickCallback { get; } }
```

Per-tick drive site inside `ProfilerSystem.PostUpdateEverything` after `EndTick`:

```csharp
var ctx = new TickContext(tickIndex, unixMs, frameMs, npcCount, projCount, dustCount, in tagger.Current);
var cbs = DataRegistry.Shared.PerTickCallbacks;
for (int i = 0; i < cbs.Length; i++) cbs[i](in ctx);
```

The loop is `for(;;)` over a frozen array; the call through `TickCapture` is a static delegate invocation (no virtual table lookup). The cost is one extra delegate indirection per registered per-tick collector compared with the current direct method call. Measured budget: 4 collectors × ~2 ns = ~8 ns/tick. Below the 1% Lite budget (Invariant 2). The hot per-mod-per-hook detour timing remains where it lives now, untouched.

## 5. Hot-path discipline

Five rules, enforced by review:

1. Collectors at `PerTick` cadence MUST expose a `TickCapture` delegate; the registry iterates the frozen `PerTickCallbacks` array. Capture allocates nothing.
2. `MetricCollector.Add` / `ProbeStack.Leave` (per-mod-per-hook timing detours) are NOT pipeline calls. They write to pre-allocated arrays owned by `MetricCollector`; the pipeline reads those arrays through `MetricCollector`'s already-public accessors. The Add/Leave path remains zero-virtual-dispatch.
3. Aggregators are invoked from the per-tick drive site too, but only after their declared trigger fires (the aggregator declares "I want the post-EndTick tick" cadence, not "I time individual hook entries"). They do NOT participate in the inner hook timing loop.
4. Stats are pull-only. `Lookup("kpi").CurrentSnapshot()` is the entry point. They MUST NOT register a `PerTickCallback`.
5. Detectors run from `Task.Run` on the .NET thread pool, gated by `Interlocked.CompareExchange(ref _inflight, 1, 0) == 0`. Each detector class owns its own latch. Preserve the existing pattern at `ProfilerSystem.cs:416-438`; generalise it into a `DetectorScheduler` that lives in `Data/Detectors/`.
6. Exporters MUST go through `DataRegistry.Lookup(name).CurrentSnapshot()`. Inline arithmetic in an exporter is the cardinal sin this whole plan is for. The heatmap bucketing currently at `Web/DashboardRouter.cs:BuildHeatmap` is the cautionary example; that math moves to `HeatmapAggregator`.

## 6. Folder reorganisation

Use `git mv` for every move so blame stays attached.

```
Data/
├── DataRegistry.cs              (new)
├── DataStage.cs                 (new)
├── IDataStream.cs               (new)
├── TickContext.cs               (new)
├── SessionContext.cs            (new)
├── Collectors/
│   ├── FrameTimeCollector.cs    (new adapter over MetricCollector)
│   ├── HookCpuCollector.cs      (new adapter)
│   ├── AllocationCollector.cs   (new adapter; null when !TracksAllocations)
│   ├── EventContextCollector.cs (was Profiling/Events/ContextTagger.cs)
│   └── SegmentEdgeCollector.cs  (extracted from SegmentDetector)
├── Aggregators/
│   ├── PerModAggregator.cs      (was Profiling/PerModAttribution.cs)
│   ├── BiomeBucketAggregator.cs (was Profiling/Events/EventAggregator.cs)
│   ├── SegmentAggregator.cs     (extracted from SegmentDetector)
│   ├── SegmentStore.cs          (was Profiling/Segments/SegmentStore.cs)
│   └── HeatmapAggregator.cs     (extracted from DashboardRouter.BuildHeatmap)
├── Stats/
│   ├── KpiStat.cs               (was Profiling/Stats/KpiCalculator.cs + KpiSnapshot.cs)
│   ├── EventsFeedStat.cs        (was Profiling/Stats/EventsFeed.cs)
│   ├── BaselineMedianStat.cs    (was Profiling/Baseline.cs)
│   ├── ModImpactStat.cs         (was Profiling/ModImpactScorer.cs)
│   ├── HookCoverageStat.cs      (was Profiling/HookCoverageView.cs)
│   └── SelfHealthStat.cs        (wraps Profiling/ProfilerSelfHealth.cs)
├── Detectors/
│   ├── DetectorScheduler.cs     (new; generalises the Interlocked latch)
│   ├── SpikeDetector.cs         (was Profiling/SpikeDetector.cs)
│   ├── StallDetector.cs         (was Profiling/StallDetector.cs)
│   └── Insights/                (entire Profiling/Insights/ tree)
├── Streams/
│   ├── (every file from Profiling/Persistence/Streams/, plus IPersistenceStream + StreamRegistry)
│   └── SessionRecorder.cs       (was Profiling/Persistence/SessionRecorder.cs)
└── Exporters/
    └── DashboardRouter.cs       (was Web/DashboardRouter.cs)
```

`PerformanceProfiler.csproj` uses globbed `Compile Include`, so moves do not require csproj edits beyond the build smoke-test on every step.

## 7. Migration order

Twelve commits. Each is a single self-contained step; every step ends with a green build + a green test suite + a manual dashboard smoke check.

| # | Step | Files | Done when | Rollback |
|---|---|---|---|---|
| 1 | Land the interface scaffolding. | Add `Data/DataRegistry.cs`, `IDataStream.cs`, `DataStage.cs`, `TickContext.cs`, `SessionContext.cs`. No consumers yet. | Build green, no behavioural change. | `git revert` |
| 2 | Wire `DataRegistry.Shared` into `ProfilerSystem`. Drive `PerTickCallbacks` from `PostUpdateEverything` over an empty array. Call `InitialiseAll` / `ResetAll` / `DisposeAll` at world load / unload / mod unload. | `ProfilerSystem.cs` | Build green; dashboard still works; per-tick loop is empty. | `git revert` |
| 3 | Migrate KPI (the reference shape). Move `KpiSnapshot` + `KpiCalculator` to `Data/Stats/KpiStat.cs` implementing `IDataStat<KpiSnapshot>`. Register it. Switch `DashboardRouter.BuildNow` to `Lookup<KpiSnapshot>("kpi").CurrentSnapshot()`. Delete inline call to `KpiCalculator.Compute`. | `Data/Stats/KpiStat.cs`, `Web/DashboardRouter.cs` | `/api/now` returns the same KPI block byte-for-byte. xUnit `KpiStatRoundtripTests` passes. | `git revert` reinstates the old static. |
| 4 | Migrate EventsFeed. Same pattern. | `Data/Stats/EventsFeedStat.cs`, `Web/DashboardRouter.cs` (BuildEvents) | `/api/events` unchanged. | `git revert` |
| 5 | Extract `HeatmapAggregator` from `DashboardRouter.BuildHeatmap`. This is the canonical "kill the inline math" step. Add `HeatmapSnapshot` + `HeatmapAggregator : IDataAggregator<HeatmapSnapshot>`. Register, switch the endpoint. | `Data/Aggregators/HeatmapAggregator.cs`, `Web/DashboardRouter.cs` | `/api/heatmap` byte-equal to baseline; new aggregator's unit test passes. | `git revert` |
| 6 | Wrap `MetricCollector` in `FrameTimeCollector` / `HookCpuCollector` / `AllocationCollector` adapters. The underlying class is untouched. Register them as `PerTick` cadence with capture delegates that copy `ctx.FrameTimeMs` etc into already-existing fields — i.e. no-op delegates in this commit, just registration. | `Data/Collectors/*.cs`, `ProfilerSystem.cs` | Per-tick loop iterates ≥3 entries; `client.log` shows tick rate stable; no measurable overhead delta in a 10-minute Lite-mode session. | Delete the new files; `git revert`. |
| 7 | Move EventContext + Segment edge sweep. Move `ContextTagger` to `Data/Collectors/EventContextCollector.cs`. Split `SegmentDetector` into `Data/Collectors/SegmentEdgeCollector.cs` (the active-key sweep) + `Data/Aggregators/SegmentAggregator.cs` (open-segment accumulation, close emission). Update `ProfilerSystem.PostUpdateEverything` to drive them through the registry instead of named fields. | `Data/Collectors/*`, `Data/Aggregators/SegmentAggregator.cs`, `ProfilerSystem.cs` | Timeline tab still shows segments; per-tick overhead unchanged (measure with a Standard-mode test session). | Single revert; keep behaviour change isolated. |
| 8 | Move `EventAggregator` → `BiomeBucketAggregator`, `PerModAttribution` → `PerModAggregator` (wrapper static stays for the detour write side; the registry-facing adapter reads through it). | `Data/Aggregators/*`, `ProfilerSystem.cs`, `Web/DashboardRouter.cs` | Events tab data unchanged; per-mod ranking unchanged. | `git revert` |
| 9 | Move SpikeDetector + StallDetector + InsightsEngine into `Data/Detectors/`. Generalise the existing single-slot `Interlocked` latch (`ProfilerSystem.cs:178, 416-438`) into `DetectorScheduler` which the engines call. Preserve the off-thread invocation. | `Data/Detectors/*`, `ProfilerSystem.cs` | A 20-minute playtest does NOT reproduce the 1211 ms wedge that motivated the latch (`ProfilerSystem.cs:408-415`). | `git revert`; if latch regresses, restore prior block immediately. |
| 10 | Move every `Profiling/Persistence/Streams/*` + `IPersistenceStream` + `StreamRegistry` + `SessionRecorder` into `Data/Streams/`. Pure rename. The persistence interface stays distinct from `IDataStream` (see §9). | `Data/Streams/**`, `*.csproj` if needed | Build green; LiteDB writes still land; replay test still passes. | `git revert` |
| 11 | Move `DashboardRouter` to `Data/Exporters/DashboardRouter.cs`. Rewrite every remaining `BuildXxx` method as a single-line registry dispatch. Strict rule: no method in this file may contain an arithmetic operator or a loop body that produces a derived number. Any remaining computation belongs in a new Stat or Aggregator created in this commit. | `Data/Exporters/DashboardRouter.cs`, possibly new `Data/Stats/*.cs` | Every endpoint returns byte-equal output to the pre-migration baseline. (Capture baseline JSON before step 1 begins.) | `git revert` |
| 12 | Delete the deprecated direct-access paths. The named-field properties on `ProfilerSystem` (`Collector`, `Segments`, `SegmentStore`, `Events`) become `internal` or private; external consumers must use the registry. Add an analyzer / review note pinning the rule. Bump `build.txt` minor. | `ProfilerSystem.cs`, `build.txt`, `context/architecture.md` | The grep `MetricCollector\|SegmentStore\|EventAggregator` outside `Data/` returns only Profiling-internal hits. | Restore the public properties; this step is the cheapest to revert because everything still works without it. |

## 8. Threading and allocation model

```
Game thread (60 Hz)
   PreUpdateEntities ──► MetricCollector.BeginTick
                              │
   Hook detour entry  ────────┤  (PerModAttribution accumulates; ZERO virtual dispatch)
                              │
   PostUpdateEverything ─► MetricCollector.EndTick
                         ─► Build TickContext (stack-allocated, ref-passed)
                         ─► for each registry.PerTickCallbacks[i](in ctx)
                                 │
                                 ├──► FrameTimeCollector.Capture     (zero-alloc)
                                 ├──► EventContextCollector.Capture  (zero-alloc)
                                 ├──► SegmentEdgeCollector.Capture   (zero-alloc)
                                 ├──► SegmentAggregator.Capture      (allocates on close only)
                                 └──► BiomeBucketAggregator.Capture  (zero-alloc)
                         ─► DetectorScheduler.MaybeKick (1 Hz gate + Interlocked latch)

ThreadPool (off the game thread, latch-gated)
   InsightsEngine.Evaluate(captured collector, latestTick, depth)
   SpikeDetector / StallDetector run inline today; they may stay inline if their
   cost stays under ~50 µs/pass (measured) — otherwise migrate them to the
   scheduler in step 9.

DbWriterThread (single, owned by ProfilerDatabase)
   Drains DbWriteOp queue ─► IPersistenceStream.Apply for matching kind.

HTTP request threads (DashboardHttpServer worker pool)
   GET /api/* ─► DashboardRouter ─► DataRegistry.Lookup(name).CurrentSnapshot()
                                  ─► serialise to JSON, return.
```

**Snapshot ownership.** Snapshots are immutable structs (`KpiSnapshot`) or `readonly record class` instances. They are produced fresh on every `CurrentSnapshot()` call. No caller is required to free anything. The Stat layer never caches snapshots; if the aggregator state has not changed since the previous call, regenerating the snapshot still costs O(window-size), which is fine because Stats are OnDemand.

**Allocation budget.** The per-tick path allocates zero through warmup. The first 10 ticks after world-load are permitted to allocate (lazy dictionary growth, etc) as today. Stats allocate per call (HTTP request frequency). Detectors allocate per emission. Streams allocate per record.

## 9. Persistence integration

`IDataStream<TSnapshot>` and `IPersistenceStream` are distinct interfaces by design.

- `IDataStream<TSnapshot>` owns **in-memory** state. Lifecycle: world-load → ticks → snapshot reads → world-unload reset. Lives on the game thread (capture) and any thread (snapshot read).
- `IPersistenceStream` owns the **disk** side. Lifecycle: `Apply(in DbWriteOp, db)` on the writer thread, idempotent, declarative indexes. Already perfect; do not touch.

They cooperate through a shared **record class** (e.g. `SegmentRow`, `SpikeWindowRow`, `InsightRow`):

```
IDataDetector emits ── SpikeWindowRow ──► enqueue DbWriteOp.Spike(row)
                                            │
                                            ▼
                                    DbWriterThread drains
                                            │
                                            ▼
                                  IPersistenceStream.Apply
                                            │
                                            ▼
                                       LiteDB collection
```

Concretely: `SpikeDetector` (a data stream, lives in `Data/Detectors/`) produces a `SpikeWindowRow` value, hands it to `DbWriteOp.Spike(row)`, enqueues. The writer thread sees the op and routes it to `SpikeStream.Apply` (a persistence stream, lives in `Data/Streams/`). The two streams share `SpikeWindowRow` and nothing else.

This boundary is also why step 10 is "pure rename": `IPersistenceStream` already does its job correctly. Folding it into `IDataStream` would conflate threading models and lifecycles. Keep them apart.

## 10. Dashboard / Exporter contract

The router's `Route(HttpRequest)` switch shape stays. Endpoint paths stay. JSON wire shapes stay byte-for-byte (verified by a `DashboardSnapshotRegressionTests` run before step 1 and re-run after step 11). What changes is the **inside** of each `BuildXxx`:

```csharp
// before  (Web/DashboardRouter.cs:67-141, current main)
private static string BuildNow()
{
    ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
    MetricCollector? c = sys?.Collector;
    // … 70 lines of reads, sums, KPI compute, allocation rollup …
    return JsonSerializer.Serialize(new { … });
}

// after  (Data/Exporters/DashboardRouter.cs)
private static string BuildNow()
{
    var nowSnap = DataRegistry.Shared.Lookup<NowSnapshot>("now")?.CurrentSnapshot()
                  ?? NowSnapshot.NotLoaded;
    return JsonSerializer.Serialize(nowSnap, JsonOpts);
}
```

`NowSnapshot` is a new `record class` in `Data/Stats/NowStat.cs`. It composes existing stats (KPI, self-health, segment count). No arithmetic in the router.

Iteration-style discovery, used by the future session-report exporter:

```csharp
foreach (IDataStream s in DataRegistry.Shared.All)
{
    if (s.Stage != DataStage.Stat) continue;
    object snap = s.CurrentSnapshotBoxed();
    report.WriteSection(s.Name, snap);
}
```

This is the payoff. Adding a new stat means one file under `Data/Stats/`, one `Register`. The dashboard surfaces it the next session.

## 11. Testing strategy

**Surviving unchanged:**
- Every persistence test under `Tests/Persistence/` (replay, indexes, idempotent apply). The persistence interface is untouched.
- Every detector unit test that takes `MetricCollector` as input. The wrapper adapter passes `MetricCollector` through.
- Every hook-detour overhead micro-benchmark. The detour path does not go through the pipeline.

**New tests required:**

1. `DataRegistryRoundtripTests` — register N streams, look up by name, iterate `All`, assert order stability after `Freeze`, assert per-tick callback array equals registered `PerTick` streams.
2. `SnapshotImmutabilityTests` — for each `IDataStat<T>`, call `CurrentSnapshot()` twice and assert the returned struct is value-equal but does not share heap state. Use `record class` with `with`-clone where mutation might leak.
3. `LifecycleResetTests` — register every default stream, run `InitialiseAll`, drive 60 synthetic ticks, call `ResetAll`, assert every aggregator's count-style accessors return 0.
4. `EndpointParityTests` — capture every `/api/*` JSON response on `main` before step 1; re-run after step 11; diff must be empty modulo `unixMs`. This is the regression net.
5. `HotPathAllocationBenchmark` (BenchmarkDotNet) — drive 10 000 synthetic ticks through `DataRegistry.PerTickCallbacks` and assert zero managed allocations after warmup. Pinned to the existing per-tick budget.
6. `DetectorOverlapLatchTests` — fire `Evaluate` twice in quick succession; assert the second call is no-op while the first is in flight.

xUnit pattern matches existing project (`Tests/**`).

## 12. Failure modes (ranked by likelihood)

1. **Endpoint output drifts byte-for-byte during the heatmap extraction (step 5).** Most likely cause: bucket-boundary rounding differences when moving the inline math to a new file. Mitigation: capture JSON baseline before step 1 and run `EndpointParityTests` on every step. Acceptance criterion is byte equality, not "looks right".
2. **Per-tick overhead increases past the Lite 1% budget after step 6.** Cause: the new `TickCapture` indirection compounds when 6+ collectors register. Mitigation: profile during step 6 with a 10-minute Lite-mode session, log tick-time histogram to `client.log`. If overhead exceeds 1%, collapse the smallest two adapters into a single combined collector. The architecture allows this without changing the registry API.
3. **Insights detector wedge regression after step 9.** The 2026-05-21 playtest established a 1211 ms wedge as the worst-case if `Evaluate` runs on the game thread (`ProfilerSystem.cs:408-415`). Mitigation: keep `DetectorScheduler` as a near-mechanical lift of the existing `_insightsEvalInflight` block. Do NOT generalise the scheduler beyond what the existing latch does. Add a Logger.Warn if a scheduled detector overruns its previous-cycle by ≥500 ms.
4. **`SegmentDetector` split breaks the spike/stall/death side-channel.** The current detector has `OnSpike`/`OnStall`/`OnDeath` direct calls from `ProfilerSystem.PostUpdateEverything`. Splitting collector vs aggregator must preserve which class receives those calls. Mitigation: the `SegmentAggregator` receives them (it owns the open-segment dictionary). Verify with the existing segment-close tests + a new spike/stall side-channel test before step 7 lands.

## 13. What this rules out, deliberately

Once the pipeline is in place, the following are disallowed by convention. Capture in `context/architecture.md` at step 12.

- Inline `BuildXxx` arithmetic in any exporter. The heatmap bucketing was the cautionary example; after step 5 it cannot recur because the exporter file forbids loops that produce derived numbers.
- Calculation in JavaScript that produces a value worth persisting. Visual-only math (colour bands, chart-pixel positions, FPS↔ms toggle) stays in JS; that is presentation, not derivation.
- Detectors, exporters, or chat commands reaching directly into `MetricCollector`, `SegmentStore`, `EventAggregator`. They go through `DataRegistry.Lookup`. After step 12 the named-field properties are `internal`, structurally preventing the anti-pattern.
- A future `Mod.Call` API doing its own arithmetic. It dispatches the registry.

The principle, restated: **if it produces a number, it lives in `Data/`. If it consumes a number, it asks the registry.**

## 14. Estimated effort

| Step | Effort |
|---|---|
| 1. Interfaces | 1 h |
| 2. Wire into ProfilerSystem | 1.5 h |
| 3. KPI migration (reference) | 1 h |
| 4. EventsFeed | 30 min |
| 5. Heatmap extraction | 2 h |
| 6. MetricCollector wrappers + per-tick smoke test | 2.5 h |
| 7. Segment split + EventContext move | 4 h |
| 8. EventAggregator + PerModAttribution move | 2 h |
| 9. Detectors move + DetectorScheduler | 3 h |
| 10. Streams folder move (pure rename) | 1 h |
| 11. Exporter rewrite | 2.5 h |
| 12. Cleanup + visibility tightening + version bump | 1 h |
| Total | **~22 h** |

Realistic elapsed: one week of focused evenings, including a Standard-mode and a Deep-mode validation session between steps 6 and 7 and between steps 9 and 10.

## 15. Out of scope

- **UI restyle.** The dashboard JS / CSS are presentation. No changes beyond what is needed to consume identical JSON shapes.
- **New features.** The future session-report exporter is the natural second consumer that validates the design; it is built *against* the pipeline in a follow-up effort, not as part of this migration.
- **Multiplayer-server-side variant.** tModLoader's hook surface differs between client and server; cross-cutting that is a separate design.
- **Migrating the legacy JSON importer.** It already consumes records via the persistence stream; the new pipeline does not touch it.
- **Allocation-tracking config surface changes.** `PerModAttribution.TracksAllocations` remains the gate; the `AllocationCollector` is conditionally registered, not conditionally implemented.
- **The archived `UI/Overlay/**` tree.** Stays `#if false`'d per the existing code-health audit; no pipeline integration.
