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

/// <summary>Exactly one row per session — the whole-session story compressed to a record.</summary>
public sealed class TickAggregateArchive
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public double AvgFrameMs { get; set; }
    public double MedianFrameMs { get; set; }
    public double P95FrameMs { get; set; }
    public double P99FrameMs { get; set; }
    public double MaxFrameMs { get; set; }
    public double TotalGcMs { get; set; }
    public long TicksObserved { get; set; }
    public long SpikeCount { get; set; }
    public long StallCount { get; set; }
    public List<ArchivePerMod> PerMod { get; set; } = new();
}

public sealed class ArchivePerMod
{
    public int ModId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double AvgMs { get; set; }
    public double TotalMs { get; set; }
    public double PeakMs { get; set; }
    public double TotalBytes { get; set; }
}
