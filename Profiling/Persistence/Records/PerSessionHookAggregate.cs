#nullable enable

using LiteDB;

namespace PerformanceProfiler.Profiling.Persistence.Records;

/// <summary>One row per <c>(sessionId, hookId)</c>. The deep-drill source.</summary>
public sealed class PerSessionHookAggregate
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public int HookId { get; set; }
    public int ModId { get; set; }
    public int CategoryId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public double AvgMs { get; set; }
    public double PeakMs { get; set; }
    public double TotalMs { get; set; }
    public double AvgBytes { get; set; }
    public long CallCount { get; set; }
}
