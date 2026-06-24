#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Insights;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data.Stats;

/// <summary>Live reference into <see cref="InsightsEngine.Shared"/>'s store.</summary>
public readonly struct InsightsSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IEnumerable<InsightRecord>? Live;

    public InsightsSnapshot(bool worldLoaded, IEnumerable<InsightRecord>? live)
    {
        WorldLoaded = worldLoaded;
        Live = live;
    }

    public static readonly InsightsSnapshot Empty = new InsightsSnapshot(false, null);
}

/// <summary>
/// Pipeline-facing adapter over <see cref="InsightsEngine.Shared"/>'s
/// live record set. The engine evaluates off-thread already (see the
/// 2026-05-21 Interlocked latch in <c>ProfilerSystem</c>); this stat
/// is the pull-side adapter for the dashboard.
/// </summary>
public sealed class InsightsStat : IDataStat<InsightsSnapshot>
{
    public const string StreamName = "insights";

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public InsightsSnapshot CurrentSnapshot()
    {
        var eng = InsightsEngine.Shared;
        if (eng == null) return InsightsSnapshot.Empty;
        return new InsightsSnapshot(worldLoaded: true, live: eng.Store.AllLive());
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
