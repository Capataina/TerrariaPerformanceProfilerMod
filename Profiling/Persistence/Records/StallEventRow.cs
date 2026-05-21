#nullable enable

using System.Collections.Generic;
using LiteDB;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
namespace PerformanceProfiler.Profiling.Persistence.Records;

/// <summary>
/// One row per detected stall (wall-clock gap between BeginTick calls
/// exceeding the baseline tick period × multiplier). Distinct from spikes:
/// stalls are missing time, spikes are too much work in one tick.
///
/// <para>
/// Schema v2 (post-CheatSheet-menu playtest): now carries the GC + heap
/// diagnostic deltas the detector already captured but the writer used to
/// throw away, plus top-mod attribution at stall-capture time and a
/// cluster id linking related stalls. The cluster row in
/// <c>stallClusters</c> aggregates a run of consecutive stalls into the
/// "one event the player saw" — much closer to how the user perceives
/// the lag than a per-event dump.
/// </para>
/// </summary>
public sealed class StallEventRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 2;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long TickIndex { get; set; }
    public long UnixMs { get; set; }
    public double DurationMs { get; set; }
    public double BaselineTickMs { get; set; }
    public string Cause { get; set; } = "unknown";
    public string Severity { get; set; } = "minor";

    public double GcPauseDurationMs { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public long HeapDeltaBytes { get; set; }
    public double CpuTimeDeltaMs { get; set; }
    public bool Warming { get; set; }

    /// <summary>Cluster id this stall belongs to (links to <c>stallClusters._id</c>). Null for isolated stalls.</summary>
    public ObjectId? ClusterId { get; set; }

    /// <summary>Top contributors at stall capture time. Up to 5 entries.</summary>
    public List<StallContributorEntry> TopContributors { get; set; } = new();
}

/// <summary>One contributor's snapshot at stall time: mod name + ms it was costing per-tick (rolling 30s avg).</summary>
public sealed class StallContributorEntry
{
    public int ModId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double RecentMs { get; set; }
}
