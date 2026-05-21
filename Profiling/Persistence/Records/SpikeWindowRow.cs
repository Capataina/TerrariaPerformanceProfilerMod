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

/// <summary>One row per coalesced spike window. Sourced from <c>MetricCollector.Spikes</c>.</summary>
public sealed class SpikeWindowRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long StartTick { get; set; }
    public long EndTick { get; set; }
    public long WorstTick { get; set; }
    public double WorstFrameMs { get; set; }
    public double BaselineMs { get; set; }
    public double MadMs { get; set; }
    public bool Warming { get; set; }
    public string? Context { get; set; }

    public List<SpikeContributor> TopContributors { get; set; } = new();
    public List<double> PerModCatMs { get; set; } = new();
    public List<double>? PerModCatBytes { get; set; }
}

public sealed class SpikeContributor
{
    public int ModId { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Ms { get; set; }
    public double Bytes { get; set; }
}
