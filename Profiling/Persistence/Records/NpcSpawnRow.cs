#nullable enable

using LiteDB;

namespace PerformanceProfiler.Profiling.Persistence.Records;

/// <summary>
/// One row per NPC spawn detected via
/// <see cref="Terraria.ModLoader.GlobalNPC.OnSpawn"/>. The
/// <c>IEntitySource</c> subclass name is recorded verbatim
/// (<c>EntitySource_SpawnNPC</c>, <c>EntitySource_DebugCommand</c>,
/// <c>EntitySource_BossSpawn</c>, etc) — that's how CheatSheet, HEROsMod,
/// vanilla, and every future spawning mod surface identically. Per
/// Invariant 5 we never match on a mod's name.
/// </summary>
public sealed class NpcSpawnRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long Tick { get; set; }
    public long UnixMs { get; set; }

    public int NpcType { get; set; }
    public string NpcName { get; set; } = "";

    /// <summary>Owning mod's internal name (or "Terraria" for vanilla). Resolved dynamically from the NPC type's owning mod, not from a hardcoded table.</summary>
    public string OwningMod { get; set; } = "";

    /// <summary>Short name of the <c>IEntitySource</c> subclass (without the EntitySource_ prefix).</summary>
    public string SourceCategory { get; set; } = "unknown";

    /// <summary>Optional textual context the source carries (vanilla source classes expose a Context string for some spawns).</summary>
    public string SourceContext { get; set; } = "";

    public float TileX { get; set; }
    public float TileY { get; set; }
    public bool IsBoss { get; set; }
}
