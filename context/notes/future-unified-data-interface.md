# Idea: unified data interface for collection, aggregation, persistence, export

**Status:** notes only. Not implemented. Revisit when a second client of the data layer lands (e.g. post-session HTML report exporter, in-game chat-command surface).
**Captured:** 2026-05-21 conversation.

---

## The user's framing

> For all imports and exports of data — the DB, the HTML dashboard, actually collecting the data from the game — we should have a dedicated interface so that when you want to add more data collections or collect different types of data (hooks, latency, allocs, events, etc; everything is "data"), you just expand this system rather than creating new files all around the app. This way: want to add new data tracking? One place. Want to generate some other interesting insights? You grab the data from one place.

The underlying principle: **data is a first-class citizen of the codebase.** The current architecture is good at the individual concerns (measure, aggregate, persist, serve) but the path through them is per-subsystem and the seams aren't named. The proposal is to give every data flow the same shape so adding new data is "implement one interface and register" instead of "edit 4 files in different folders."

---

## Current state (75% there already)

```
Raw measurement   →  Aggregation       →  Persistence            →  Export
─────────────────    ────────────────     ────────────────────       ───────────────
MetricCollector       Stats/             Persistence/Streams/       Web/DashboardRouter
Baseline              Segments/          Persistence/Records/       (future: HTML report)
SpikeDetector         Events/            Persistence/Migrations     (future: chat commands)
StallDetector         Insights/          LiteDB
ProfilerSelfHealth                        EventJournal
```

What's already unified:
- One persistence boundary (`Persistence/Streams/*` — each stream gets one record class, one apply path, one indexer)
- One dispatcher serving the dashboard (`DashboardRouter` reaches into every subsystem)
- Three explicit layers (measure / aggregate / persist) with each subsystem placed at exactly one layer

What's NOT unified:
- No single `IDataSource` interface; consumers reach into named subsystems via `ModContent.GetInstance<ProfilerSystem>()` + chained accessors
- Adding "track item-use velocity per mod" today means: write a new collector hook, write a new aggregate type in some appropriate folder, write a new record class, write a new stream, write a new DashboardRouter endpoint. Five separate files in four folders. Each step is easy but the path through them is unnamed.

---

## Proposed shape

The Facade isn't a wrapper; it's a **registry of declared data streams** with a uniform shape per stream:

```csharp
public interface IDataStream<TPoint, TSnapshot>
{
    string Name { get; }                              // "modCpu", "spikes", "segments", ...
    DataStreamCadence Cadence { get; }                 // PerTick, OneHz, OnEvent, OnDemand

    // Hot path (only PerTick streams get this called per tick)
    void Capture(TickContext ctx);

    // Snapshot for read consumers (dashboard, exporter, chat command)
    TSnapshot CurrentSnapshot();

    // Persistence — each stream decides its own DB shape via Records/
    IReadOnlyList<IDataStreamPersistOp> DrainPending();

    // Export — for the post-session HTML report and any future exporter
    void WriteToSessionReport(SessionReportBuilder b);
}
```

Plus a registry:

```csharp
public sealed class DataRegistry
{
    public static DataRegistry Shared { get; }
    public void Register(IDataStream stream);
    public IDataStream? Lookup(string name);
    public IEnumerable<IDataStream> All { get; }
}
```

Adding a new data type then becomes:

1. Implement `IDataStream<TPoint, TSnapshot>` in a single new file under `Profiling/Data/<StreamName>/`.
2. Register it once in `DataRegistry.RegisterDefaults()`.

The dashboard router, the HTML exporter, and any future chat-command can all iterate `DataRegistry.Shared.All` to discover what's available. New data shows up everywhere automatically.

---

## Why we're holding off

1. **Hot-path code can't be wrapped.** `MetricCollector.Add` and `ProbeStack.Leave` are zero-allocation, called millions of times per tick. They can't go through a virtual interface call without measurable overhead. The facade would have to deliberately exclude them, producing a "some things are in here, some things aren't" asymmetry that's worse than no facade.

2. **Different lifecycle.** MetricCollector resets per world. Insights persist across (intentionally). Self-health is process-wide. A facade that unifies the lifecycle could leak state cross-world. A facade that doesn't unify is just five lifecycles wearing one trench coat.

3. **Different threading.** Game thread (per-tick), DB writer thread (queue drain), HTTP request threads (read snapshots). The current arrangement gets correctness by giving each subsystem its own lock-discipline. A facade enforcing one access model risks slowing the fast paths or silently corrupting the read paths.

4. **One consumer today.** The dashboard router IS effectively the facade — it reaches into each subsystem with a stable contract. A second layer between two things that already talk fine adds zero capability and one more place to break.

**The moment to revisit:** when a second consumer that needs the same data lands. Most likely candidates:
- **Post-session HTML report exporter** (planned for v1.0): builds a standalone, self-contained HTML file from the just-ended session. Wants the same snapshots the dashboard does. Building the facade then saves the duplication.
- **Chat-command surface** revival: if we ever want `/profiler events --tail 10` to print live stats in chat the same way the dashboard renders them.
- **Programmatic mod-call API**: other mods asking us "what's my CPU cost right now" via tModLoader's `Mod.Call`.

When that second consumer ships, the facade earns its keep.

---

## Holding pattern in the meantime

The convention going forward:

- **Every new "fact about the session" lives in `Profiling/Stats/`.** Follow the `XxxSnapshot` (struct) + `XxxCalculator` (static helper) pattern that `KpiSnapshot`/`KpiCalculator` and `EventsFeed` established.
- **Every new persisted record type goes through `Persistence/Streams/`** as a one-line registry addition. The pattern is already idempotent and consistent.
- **Every new API surface lives in `Web/DashboardRouter.BuildXxx`** and follows the existing JSON shape conventions.

This keeps the codebase moving toward cohesion file-by-file without committing to the unified-interface refactor before it's worth it.

When the post-session exporter starts, the right migration order is:
1. Define `IDataStream<,>` and the registry interface.
2. Adapt the existing subsystems one at a time — they can implement the interface AND keep their original public surface, so consumers migrate at their own pace.
3. Build the exporter against the registry from the start; the dashboard router can migrate to it after.
