#nullable enable

using LiteDB;
using PerformanceProfiler.Profiling.Pools;

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
/// One row per buff add/remove edge on <see cref="Terraria.Main.LocalPlayer"/>.
/// Captures the "Dead Cells Mechanics activates after damage" pattern —
/// the buff flips on, hooks fire while it's active, the buff flips off,
/// hooks go quiet. The triple gives an insight detector everything it
/// needs to spot event-conditional cost.
/// </summary>
public sealed class BuffEventRow : IPoolReset
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long Tick { get; set; }
    public long UnixMs { get; set; }

    /// <summary>"on" or "off".</summary>
    public string Edge { get; set; } = "on";

    public int BuffType { get; set; }
    public string BuffName { get; set; } = "";
    public string OwningMod { get; set; } = "";

    /// <summary>Duration the buff was on for, in ticks. Only filled on the "off" edge; -1 on the "on" edge.</summary>
    public int DurationTicks { get; set; } = -1;

    public void Reset()
    {
        Id = ObjectId.NewObjectId();
        Schema = 1;
        SessionId = ObjectId.Empty;
        Tick = 0; UnixMs = 0;
        Edge = "on";
        BuffType = 0; BuffName = ""; OwningMod = "";
        DurationTicks = -1;
    }
}
