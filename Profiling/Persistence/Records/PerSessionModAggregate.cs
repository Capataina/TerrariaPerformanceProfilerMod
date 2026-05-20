#nullable enable

using System.Collections.Generic;
using LiteDB;

namespace PerformanceProfiler.Profiling.Persistence.Records;

/// <summary>
/// The headline per-mod summary driving the Overview tab. One row per
/// <c>(sessionId, modId)</c>. <see cref="CategoryMs"/> is length =
/// <see cref="PerModAttribution.CategoryCount"/>.
/// </summary>
public sealed class PerSessionModAggregate
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public int ModId { get; set; }
    public string ModInternalName { get; set; } = string.Empty;
    public double AvgMs { get; set; }
    public double PeakMs { get; set; }
    public double TotalMs { get; set; }
    public double P95Ms { get; set; }
    public double AvgBytes { get; set; }
    public double PeakBytes { get; set; }
    public double TotalBytes { get; set; }

    public ModCoverage Coverage { get; set; } = new();
    public List<double> CategoryMs { get; set; } = new();
    public List<TopHookEntry> TopHooks { get; set; } = new();
}

public sealed class ModCoverage
{
    public int Measured { get; set; }
    public int Total { get; set; }
    public string Badge { get; set; } = "full";
}

public sealed class TopHookEntry
{
    public int HookId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public double AvgMs { get; set; }
}
