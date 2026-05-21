# Idea: a unified `Data/` pipeline — single calculation locus, dumb consumers

**Status:** notes only, not implemented. This is the explicit architectural target — bigger lift than a file reorg, but lays the foundation for everything downstream.
**Captured:** 2026-05-21 conversation, clarified across three messages same day.

---

## The user's framing (final, after two clarifications)

> Imagine a `Data/` folder in the root with collectors, aggregators, stats, streams, etc — and **this way our DB and HTML practically only present information; they don't calculate anything.**

The architectural principle: **the pipeline is the brain. The DB stores what it produces. The HTML renders what it serves. Neither does math.**

This is stronger than a file reorganization. It's a **policy about where calculation is allowed to live.** Today calculation happens in three places — C# subsystems, inline inside the DashboardRouter (heatmap bucketing, mod median, etc.), and historically in JS (mostly migrated to C# in the v0.9.x KPI/EventsFeed work, but the rule against it living in JS isn't structurally enforced). The unified-pipeline target makes the rule structural: **the only place calculation is allowed is inside a pipeline stage.** Every derived number is computed once, named, registered, and emitted by exactly one stage.

This is the **observability pipeline** shape (collectors → aggregators → stats → detectors → streams → exporters). Cross-industry term; what we're literally building.

---

## Proposed folder shape (Tier 1 — file reorg)

```
Data/
├── Collectors/         ← "read raw values from the game"
│   ├── HookCollector
│   ├── FrameTimeCollector
│   ├── EventContextCollector
│   ├── AllocationCollector
│   └── ...
├── Aggregators/        ← "organise raw values into structured groups"
│   ├── PerModAggregator       (currently PerModAttribution.cs)
│   ├── SegmentAggregator      (currently Segments/SegmentDetector + SegmentStore)
│   ├── BiomeBucketAggregator  (currently Events/EventAggregator.cs)
│   └── ...
├── Stats/              ← "derived calculations from aggregates"
│   ├── KpiCalculator          (already in Profiling/Stats/)
│   ├── BaselineMedian         (currently Baseline.cs)
│   ├── EventsFeed             (already in Profiling/Stats/)
│   └── ...
├── Detectors/          ← "fire when patterns match"
│   ├── SpikeDetector
│   ├── StallDetector
│   ├── InsightsEngine + its Detectors/
│   └── ...
├── Streams/            ← "persist to LiteDB"
│   ├── SegmentStream
│   ├── SpikeStream
│   ├── ...             (one stream per Record class)
└── Exporters/          ← "serve to consumers"
    ├── DashboardRouter         (currently Web/DashboardRouter.cs)
    ├── SessionReportExporter   (future v1.0 — post-session HTML report)
    └── ChatCommandExporter     (potential future)
```

Each stage has the **same shape** across all data types. Want to add allocation-per-biome tracking? Add an `AllocPerBiomeAggregator` to `Aggregators/`, an `AllocPerBiomeStream` to `Streams/`, an `AllocPerBiomeStats` calculator if needed. You don't touch any other folder.

This is a **file-organization refactor** — cleaner repo, same plumbing. ~1 evening of work. No behavioural risk; pure move-and-update-namespaces. The biggest concern is keeping git blame readable (use `git mv`).

---

## Tier 2 — runtime registry (the optional second step)

On top of the folder reorg, each stage exposes a common interface:

```csharp
public interface IDataStream<TPoint, TSnapshot>
{
    string Name { get; }                               // "modCpu", "spikes", "segments"
    DataStreamCadence Cadence { get; }                 // PerTick, OneHz, OnEvent, OnDemand

    // Hot-path capture (only PerTick streams get this per tick)
    void Capture(TickContext ctx);

    // Snapshot for read consumers (dashboard, exporter, chat)
    TSnapshot CurrentSnapshot();

    // Persistence
    IReadOnlyList<IDataStreamPersistOp> DrainPending();

    // Export to a session report
    void WriteToSessionReport(SessionReportBuilder b);
}

public sealed class DataRegistry
{
    public static DataRegistry Shared { get; }
    public void Register(IDataStream stream);
    public IDataStream? Lookup(string name);
    public IEnumerable<IDataStream> All { get; }
}
```

Adding a new data type then becomes "implement the interface once + register". Dashboard, HTML exporter, chat command can all iterate `DataRegistry.Shared.All` to discover what's available — new data shows up everywhere automatically without editing the consumers.

This is a **runtime-architecture refactor** — bigger lift, real risk on hot-path code, real payoff once there's a second consumer. ~1 week of work, gated on actually having that second consumer.

---

## The two tiers, post-clarification

The original note treated these as independent choices. After the third clarification message it's clear they're **one architectural target, in two parts** — the folder shape is the visible half, the runtime registry is what enforces the "calculation lives only in the pipeline" policy.

| Tier | What it gives us | Without it… |
|---|---|---|
| **1 — Folder reorg** | Every data lifecycle stage has one named home (`Data/Collectors/`, `Data/Stats/`, …). New data types stop sprawling across 4 unrelated folders. | The codebase looks organized but new "convenience" calculation can still leak into routers, exporters, JS — because the rule "calc only here" isn't structurally enforced. |
| **2 — Runtime registry + IDataStream interface** | The dashboard router becomes thin: it just dispatches `Lookup(name) → CurrentSnapshot()`. The exporter does the same. Every derived number is computed once by the named stage that owns it; consumers can't accidentally do their own math. | Routers + exporters keep doing inline math (the v0.9.x heatmap bucketing inside DashboardRouter is exactly this anti-pattern). The "single calculation locus" policy is aspirational instead of enforced. |

Tier 2 is what makes the principle real. Tier 1 alone is cosmetic.

**Order to ship them:** Tier 1 first (mechanical, low risk) so the folder shape is correct; Tier 2 immediately after, ideally driven by an actual second consumer (post-session HTML report exporter — see below). Both can be done in the same week of focused work, just not in the same evening.

---

## Why we held off Tier 2 the first time around

(Kept from the original note — these concerns still apply once Tier 2 happens.)

1. **Hot-path code can't be wrapped.** `MetricCollector.Add` and `ProbeStack.Leave` are zero-allocation, called millions of times per tick. Virtual interface calls there would be measurable overhead. The registry must either exclude them (asymmetry) or accept the overhead (Invariant 2 violation).
2. **Different lifecycles.** MetricCollector resets per world. Insights persist across. Self-health is process-wide. A unifying registry has to model lifecycle as a per-stream property, not flatten it.
3. **Different threading.** Game thread (per-tick), DB writer thread (queue drain), HTTP request threads (read snapshots). The registry has to be lock-free for snapshot reads or it slows the fast paths.
4. **One consumer today.** The dashboard router IS effectively the facade — it reaches into each subsystem with a stable contract. Adding a layer between two things that already talk fine is pure cost.

The unlock conditions:
- **Tier 1** unlocks anytime the codebase is calm — pure mechanical refactor.
- **Tier 2** unlocks when the post-session HTML report exporter (planned v1.0) starts being built — building it AGAINST the registry from the start is cheaper than retrofitting later.

---

## Holding pattern in the meantime

Until either tier ships:

- Every new "fact about the session" goes in `Profiling/Stats/`. Follow the `XxxSnapshot` (struct) + `XxxCalculator` (static helper) pattern that `KpiSnapshot`/`KpiCalculator` and `EventsFeed` established. ← Already enforced as of 2026-05-21.
- Every new persisted record goes through `Profiling/Persistence/Streams/` and registers in `StreamRegistry.Default`. The pattern is already idempotent and consistent.
- Every new API surface lives in `Web/DashboardRouter.BuildXxx` and follows the existing JSON conventions.

When Tier 1 happens, these three folders all move under `Data/` with the rest of the pipeline.

---

## Picking the moment

**Tier 1 — folder reorg:** schedule it for a deliberate "tidy" pass on a clean working tree. Updates ~50 files (namespaces + `using`s, no behaviour change). Pre-condition: no in-flight feature work. ~1 evening.

**Tier 2 — runtime registry:** schedule it as part of the post-session HTML report effort. Build the report exporter against `IDataStream<,>` from the start; migrate the dashboard's reads onto the registry as the second step. The post-session report is the natural second consumer that makes the registry's existence pay for itself — without it, Tier 2 is over-architected.

The win once both are done: **adding new data tracking is one file in `Data/Collectors/`, one in `Data/Stats/` if it has derived math, one entry in the registry. The dashboard surfaces it automatically because the dashboard doesn't know about specific data types any more — it just iterates the registry.**

---

## What this rules OUT, deliberately

To be unambiguous: once the pipeline is in place, the following become disallowed-by-convention:

- Inline `BuildXxx` math in `DashboardRouter` (the v0.9 heatmap bucketing inside the router is the cautionary example — it should have been `HeatmapAggregator` in `Data/Aggregators/`, called by reference).
- Calculation in JS that produces a value worth persisting. (Visual-only math — color band selection, chart-pixel positions, FPS↔ms conversion for a UI toggle — stays in JS; that's presentation, not derivation.)
- Detectors / exporters reaching directly into `MetricCollector` or `SegmentStore`. They go through the registry.
- A future Mod.Call API doing its own arithmetic. It dispatches the registry.

The pattern in one sentence: **if it produces a number, it lives in `Data/`. If it consumes a number, it asks the registry.**
