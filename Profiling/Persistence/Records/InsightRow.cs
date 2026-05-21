#nullable enable

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

/// <summary>One row per surfaced insight. Schema placeholder for the M4+ live store.</summary>
public sealed class InsightRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public string PatternKey { get; set; } = string.Empty;
    public string Audience { get; set; } = "player";
    public string RenderedShort { get; set; } = string.Empty;
    public string RenderedLong { get; set; } = string.Empty;
    public string Confidence { get; set; } = "preliminary";
    public string EvidenceScope { get; set; } = "ThisSession";
    public double PValueAdjusted { get; set; } = 1.0;
    public long FirstSeenTick { get; set; }
    public long LastConfirmedTick { get; set; }
}
