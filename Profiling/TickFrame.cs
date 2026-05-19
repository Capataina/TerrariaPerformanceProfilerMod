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

    /// <summary>Wall-clock duration of the tick, in milliseconds.</summary>
    public double FrameTimeMs;

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
}
