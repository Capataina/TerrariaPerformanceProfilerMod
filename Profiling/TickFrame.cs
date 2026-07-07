#nullable enable

using PerformanceProfiler.Profiling.Events;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Profiling;

/// <summary>
/// A single profiled game tick: the per-tick record the <see cref="RingBuffer{T}"/>
/// stores. The Metric Collector opens a frame at the start of a tick and commits
/// it at the end (see context/tmodloader-lifecycle-and-loop.md for the hook
/// boundaries).
///
/// Pure data with no tModLoader dependency, so it is unit-testable without a
/// running game.
///
/// The Context Tagger fields the README's data model lists (current biome,
/// active encounter) are deliberately absent for now: the types that describe
/// them belong to components not yet built (Context Tagger, Encounter Detector).
/// They will be added when those components define their types, rather than
/// guessed at here (CLAUDE.md: no speculative abstraction). What is present is
/// the performance record, which is fully defined.
/// </summary>
public struct TickFrame
{
    /// <summary>Unix timestamp, in milliseconds, at which the tick was committed.</summary>
    public long TimestampUnixMs;

    /// <summary>
    /// Session-relative tick index, sourced from Terraria's per-world update
    /// counter. Resets to 0 at world-load, so it is a natural frame origin for
    /// one session.
    /// </summary>
    public long TickIndex;

    /// <summary>
    /// Update-window work, in milliseconds: the span from PreUpdateEntities
    /// (BeginTick) to PostUpdateEverything (EndTick). This is only the update
    /// half of the game loop — it excludes the Draw phase and the inter-tick
    /// gap, so it must NOT be used as the player-facing frame time (a draw-bound
    /// slow-motion session reads as a healthy ~3 ms here while the game crawls).
    /// Use <see cref="RealFrameTimeMs"/> for the honest frame budget; this value
    /// is the update-vs-total-work breakdown.
    /// </summary>
    public double FrameTimeMs;

    /// <summary>
    /// The real inter-frame wall-clock period, in milliseconds: the time between
    /// this tick's update-start and the previous tick's update-start. It spans
    /// the whole game loop the player experiences — Update + Draw + any vsync
    /// sleep — so when the game is locked to 60 fps it reads ~16.7 ms and when it
    /// drops into slow-motion it rises to the true elongated period. This is the
    /// honest "are we hitting frame budget" signal; <see cref="FrameTimeMs"/>
    /// (update-window only) is structurally blind to draw-phase and profiler-
    /// harvest cost. Carries a one-frame lag (measured at the next BeginTick),
    /// invisible to the per-second rolling aggregates that consume it.
    /// </summary>
    public double RealFrameTimeMs;

    /// <summary>GC pause time attributed to the tick, in milliseconds.</summary>
    public double GcTimeMs;

    /// <summary>Active projectile count sampled at tick close.</summary>
    public int ProjectileCount;

    /// <summary>Active NPC count sampled at tick close.</summary>
    public int NpcCount;

    /// <summary>Active dust count sampled at tick close.</summary>
    public int DustCount;

    /// <summary>
    /// Per-mod cost samples for this tick, or null until per-mod attribution is
    /// wired (a later milestone). Once populated, the Ring Buffer will own one
    /// fixed-size array per slot, reused so committing a tick never allocates
    /// (Invariant 2).
    /// </summary>
    public PerModSample[]? ModSamples;

    /// <summary>
    /// Game-state context for the tick: biome bits, weather flags, hardmode,
    /// difficulty, vanilla invasion, active boss types, optional subworld
    /// key. Populated by <see cref="Events.ContextTagger"/> after
    /// <c>MetricCollector.EndTick</c> closes the frame, then aggregated by
    /// <see cref="Events.EventAggregator"/>. See
    /// <c>context/notes/events-tab-plan.md</c> §3.1.
    /// </summary>
    public EventContext Context;
}
