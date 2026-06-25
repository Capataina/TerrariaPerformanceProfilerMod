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

/// <summary>
/// Single row (<c>_id = "metadata"</c>) tracking DB-level state across sessions.
/// </summary>
public sealed class MetadataRow
{
    [BsonId] public string Id { get; set; } = "metadata";
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public DateTime DbCreatedUtc { get; set; }
    public DateTime LastOpenedUtc { get; set; }
    public List<string> ProfilerVersionSeen { get; set; } = new();
    public int SessionCount { get; set; }
}
