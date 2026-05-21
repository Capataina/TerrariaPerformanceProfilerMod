#nullable enable

using System.Collections.Generic;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// Snapshot of all detected <see cref="SpikeWindow"/>s this session. The
/// list is a live reference into <see cref="MetricCollector.Spikes"/>;
/// callers must not mutate it.
/// </summary>
public readonly struct SpikesSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<SpikeWindow>? Windows;

    public SpikesSnapshot(bool worldLoaded, IReadOnlyList<SpikeWindow>? windows)
    {
        WorldLoaded = worldLoaded;
        Windows = windows;
    }

    public static readonly SpikesSnapshot Empty = new SpikesSnapshot(false, null);
}

/// <summary>
/// Pipeline-facing adapter over <see cref="SpikeDetector"/>'s live
/// windows list. The detector keeps owning the per-tick detection path
/// (registered as a future <c>IDataDetector</c> in step 9); this stat
/// is the snapshot surface for consumers.
/// </summary>
public sealed class SpikesStat : IDataStat<SpikesSnapshot>
{
    public const string StreamName = "spikes";

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public SpikesSnapshot CurrentSnapshot()
    {
        MetricCollector? c = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (c == null) return SpikesSnapshot.Empty;
        return new SpikesSnapshot(worldLoaded: true, windows: c.Spikes);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
