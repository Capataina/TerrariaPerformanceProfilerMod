#nullable enable

using System;
using LiteDB;

namespace PerformanceProfiler.Profiling.Persistence.Records;

/// <summary>One row per (worldName, worldUniqueId). Sessions link to this via WorldId.</summary>
public sealed class WorldRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public string Name { get; set; } = string.Empty;
    public Guid UniqueId { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
}
