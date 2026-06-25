#nullable enable

using System;
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
/// One row per game-load session. Mirrors the schema in §3.2 of
/// <c>context/notes/litedb-migration-plan.md</c>. The session id is the
/// reference target for every per-session derived row (aggregates, spikes,
/// transitions, tier rows).
///
/// Field shapes are deliberately permissive — <see cref="EndedUtc"/> stays
/// null until the writer thread receives the SessionEnd op so a crash-cut
/// session leaves a detectable "no end" marker for the recovery path.
/// </summary>
public sealed class SessionRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    public long DurationMs { get; set; }
    public string ProfilerVersion { get; set; } = string.Empty;
    public string TmlVersion { get; set; } = string.Empty;
    public ObjectId? WorldId { get; set; }
    public string ModlistFingerprint { get; set; } = string.Empty;
    public int HookCoverageVersion { get; set; }
    public string Mode { get; set; } = "lite";
    public bool TracksAllocations { get; set; }
    public long TicksObserved { get; set; }

    /// <summary>One of <c>clean</c>, <c>crash-detected</c>, <c>host-drift-aborted</c>.</summary>
    public string EndReason { get; set; } = "clean";

    /// <summary>True while in progress; flipped false at session end.</summary>
    public bool Incomplete { get; set; } = true;
}
