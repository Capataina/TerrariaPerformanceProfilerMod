# Idea: a unified `Data/` pipeline — by-stage folders + (later) runtime registry

**Status:** notes only, not implemented.
**Captured:** 2026-05-21 conversation. Clarified later same day — see "two-tier proposal" below.

---

## The user's framing

> Imagine a `Data/` folder in the root, and then we can add **collectors** which collect the data, **aggregators** that organise it, **stats** that calculate things like last-30s, **streams**, and so on. Want to add new data tracking? One place. Want to generate new insights? You grab the data from one place.

The principle: **data is a first-class citizen of the codebase, organized by lifecycle stage rather than by feature.** Today's arrangement scatters one logical data type across several folders ("spike detection" lives in `Profiling/SpikeDetector.cs`, `Profiling/Persistence/Streams/SpikeStream.cs`, `Profiling/Persistence/Records/SpikeWindowRow.cs`, and `Web/DashboardRouter.BuildSpikes`). Adding a new data type means editing 4–5 files in 3–4 folders. Each step is easy; the path through them is unnamed.

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

## The two tiers are independent

| Path | Get | Cost | Risk |
|---|---|---|---|
| **Tier 1 only** (folder reorg) | "Where do I put this file" is obvious | 1 evening | low — pure file moves |
| **Tier 2 only** (registry, current folder layout) | "How does a new consumer find data" is obvious | ~1 week | medium — touches hot-path |
| **Both** | The full observability-pipeline shape | ~1 week + evening | medium |

My read on the user's actual ask after the clarifying message: **Tier 1 first.** It addresses the immediate pain (file sprawl when adding new data) without the migration risk. Tier 2 follows naturally when the post-session HTML report exporter needs to iterate the same data the dashboard does — at that point the registry earns its keep.

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

**Tier 1 — file reorg:** schedule it for a deliberate "tidy" pass. Best done as a single commit (or short series) on a clean working tree. Updates ~50 files (each `using` statement + namespace), one large diff. Pre-condition: the codebase compiles cleanly with no in-flight feature work. ~1 evening.

**Tier 2 — runtime registry:** schedule it as part of the post-session HTML report effort. Build the report exporter against `IDataStream<,>` from the start, migrate the dashboard's reads onto the registry as the second step. Don't do it in isolation — without a second consumer there's nothing the registry buys you.
