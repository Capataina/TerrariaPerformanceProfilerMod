#nullable enable

using LiteDB;

namespace PerformanceProfiler.Profiling.Persistence.Records;

/// <summary>
/// One row per detected stall (wall-clock gap between BeginTick calls
/// exceeding the baseline tick period × multiplier). Distinct from spikes:
/// stalls are missing time, spikes are too much work in one tick.
/// </summary>
public sealed class StallEventRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long TickIndex { get; set; }
    public long UnixMs { get; set; }
    public double DurationMs { get; set; }
    public double BaselineTickMs { get; set; }
    public string Cause { get; set; } = "unknown";
}
