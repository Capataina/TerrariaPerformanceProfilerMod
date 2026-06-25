#nullable enable

using System;
using System.Collections.Generic;
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

/// <summary>One row per second per session (1 Hz bucket). Kept ~24h then expired.</summary>
public sealed class TickAggregateWarm
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long SecondIndex { get; set; }
    public double AvgFrameMs { get; set; }
    public double P95FrameMs { get; set; }
    public double GcMs { get; set; }
    public List<double> PerModMs { get; set; } = new();
    public List<double>? PerModBytes { get; set; }
    public DateTime ExpireAtUtc { get; set; }
}
