#nullable enable

using PerformanceProfiler.Profiling.Events;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data;

/// <summary>
/// Per-tick payload handed to every <see cref="IDataCollector{TSnapshot}"/>
/// that runs at <see cref="DataStreamCadence.PerTick"/> cadence.
///
/// <para>
/// Declared as <c>readonly ref struct</c> so it is stack-allocated only
/// and cannot escape the per-tick callback path. The
/// <see cref="EventContext"/> is passed by ref-readonly so the per-tick
/// loop never copies the bitset structure.
/// </para>
/// </summary>
public readonly ref struct TickContext
{
    public readonly long TickIndex;
    public readonly long UnixMs;
    public readonly double FrameTimeMs;
    public readonly double GcTimeMs;
    public readonly int NpcCount;
    public readonly int ProjectileCount;
    public readonly int DustCount;

    /// <summary>The event-context snapshot captured for this tick (biome bitset, weather, bosses, etc).</summary>
    public readonly ref readonly EventContext Context;

    public TickContext(long tickIndex, long unixMs, double frameMs, double gcMs,
        int npcCount, int projectileCount, int dustCount,
        in EventContext ctx)
    {
        TickIndex = tickIndex;
        UnixMs = unixMs;
        FrameTimeMs = frameMs;
        GcTimeMs = gcMs;
        NpcCount = npcCount;
        ProjectileCount = projectileCount;
        DustCount = dustCount;
        Context = ref ctx;
    }
}
