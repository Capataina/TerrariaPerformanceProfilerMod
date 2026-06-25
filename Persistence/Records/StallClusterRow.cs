#nullable enable

using LiteDB;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
namespace PerformanceProfiler.Persistence.Records;

/// <summary>
/// One row per stall cluster — a contiguous run of stall events that the
/// player perceived as one freeze. Built by <c>SessionRecorder</c> from
/// consecutive <see cref="StallEventRow"/>s that arrived within the
/// cluster window of each other (default 2 s).
///
/// <para>
/// The cluster carries the dominant cause and dominant contributor for
/// the whole run, so a query like "what made the player lag yesterday"
/// reads from <c>stallClusters</c> (one row per visible-to-the-player lag
/// event) instead of <c>stallEvents</c> (one row per tick gap, often
/// dozens per cluster).
/// </para>
///
/// <para>
/// Empirically: the CheatSheet NPC-spawn-menu playtest produced 47
/// <see cref="StallEventRow"/>s but only 1 cluster. The cluster row tells
/// the story in one line ("13 s of UI-overlay blocking, dominant cost
/// CheatSheet"); the per-event rows are the receipt.
/// </para>
/// </summary>
public sealed class StallClusterRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long StartTick { get; set; }
    public long EndTick { get; set; }
    public long StartUnixMs { get; set; }
    public long EndUnixMs { get; set; }

    /// <summary>Number of <see cref="StallEventRow"/>s in this cluster.</summary>
    public int StallCount { get; set; }

    /// <summary>Sum of <c>DurationMs</c> across the cluster's events.</summary>
    public double TotalDurationMs { get; set; }

    /// <summary>Max single-event duration in the cluster (worst hitch).</summary>
    public double WorstDurationMs { get; set; }

    /// <summary>Wall-clock span from cluster start to end, in ms.</summary>
    public double SpanMs { get; set; }

    /// <summary>Most-frequent <see cref="StallCause"/> across the cluster's events.</summary>
    public string DominantCause { get; set; } = "unknown";

    /// <summary>Mod that had the highest total RecentMs across the cluster's contributor snapshots.</summary>
    public int DominantContributorModId { get; set; } = -1;
    public string DominantContributorName { get; set; } = "";
}
