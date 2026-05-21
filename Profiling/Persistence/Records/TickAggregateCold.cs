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

/// <summary>One row per minute per session. Kept session lifetime.</summary>
public sealed class TickAggregateCold
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long MinuteIndex { get; set; }
    public double AvgFrameMs { get; set; }
    public double P95FrameMs { get; set; }
    public double MaxFrameMs { get; set; }
    public double GcMs { get; set; }
    public List<double> PerModMs { get; set; } = new();
    public List<double>? PerModBytes { get; set; }
}
