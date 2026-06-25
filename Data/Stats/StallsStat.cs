#nullable enable

using System.Collections.Generic;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Data.Stats;

/// <summary>Live reference into <see cref="MetricCollector.Stalls"/>.</summary>
public readonly struct StallsSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<StallEvent>? Events;

    public StallsSnapshot(bool worldLoaded, IReadOnlyList<StallEvent>? events)
    {
        WorldLoaded = worldLoaded;
        Events = events;
    }

    public static readonly StallsSnapshot Empty = new StallsSnapshot(false, null);
}

/// <summary>
/// Pipeline-facing adapter over <see cref="StallDetector"/>'s live events
/// list. Parallel to <see cref="SpikesStat"/>; same wrapping semantics.
/// </summary>
public sealed class StallsStat : IDataStat<StallsSnapshot>
{
    public const string StreamName = "stalls";

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public StallsSnapshot CurrentSnapshot()
    {
        MetricCollector? c = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (c == null) return StallsSnapshot.Empty;
        return new StallsSnapshot(worldLoaded: true, events: c.Stalls);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
