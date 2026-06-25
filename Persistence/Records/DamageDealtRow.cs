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
/// One row per local-player damage-dealt event. Captured via three vanilla
/// hooks that distinguish *how* the damage was applied:
///
/// <list type="bullet">
/// <item><c>Path = "melee"</c> — <see cref="Terraria.ModLoader.ModPlayer.OnHitNPC"/>:
/// a direct hit that's neither item-specific nor projectile-specific.</item>
/// <item><c>Path = "item"</c> — <see cref="Terraria.ModLoader.ModPlayer.OnHitNPCWithItem"/>:
/// a weapon swing hit. <see cref="ItemId"/> carries the weapon type id.</item>
/// <item><c>Path = "projectile"</c> — <see cref="Terraria.ModLoader.ModPlayer.OnHitNPCWithProj"/>:
/// a projectile hit. <see cref="ProjectileId"/> carries the projectile type id;
/// <see cref="ItemId"/> carries the originating weapon if available.</item>
/// </list>
///
/// Together these three paths answer the "is it the sword, the projectile,
/// or an accessory damage chain" question from a single damage event.
/// </summary>
public sealed class DamageDealtRow : IPoolReset
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long Tick { get; set; }
    public long UnixMs { get; set; }

    public string Path { get; set; } = "melee";
    public int ItemId { get; set; }
    public int ProjectileId { get; set; }

    public int NpcType { get; set; }
    public string NpcName { get; set; } = "";

    public int DamageDealt { get; set; }
    public bool Crit { get; set; }

    /// <summary>Fingerprint of the player's loadout at hit time (links to a LoadoutSnapshotRow).</summary>
    public string LoadoutFingerprint { get; set; } = "";

    public void Reset()
    {
        Id = ObjectId.NewObjectId();
        Schema = 1;
        SessionId = ObjectId.Empty;
        Tick = 0; UnixMs = 0;
        Path = "melee"; ItemId = 0; ProjectileId = 0;
        NpcType = 0; NpcName = "";
        DamageDealt = 0; Crit = false;
        LoadoutFingerprint = "";
    }
}
