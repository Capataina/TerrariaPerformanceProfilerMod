#nullable enable

using System.Collections.Generic;
using LiteDB;

namespace PerformanceProfiler.Profiling.Persistence.Records;

/// <summary>
/// One row per local-player <c>OnHurt</c> event. Captures the vanilla
/// <see cref="Terraria.PlayerDeathReason"/> shape so we know exactly what
/// dealt damage to the player without taking a dependency on any
/// specific mod's identity (Invariant 5).
///
/// <para>
/// Damage sources are encoded by category + integer id:
/// <list type="bullet">
/// <item><c>SourceKind = "npc"</c> with <c>SourceId</c> = NPC type id.</item>
/// <item><c>SourceKind = "projectile"</c> with <c>SourceId</c> = projectile type id.</item>
/// <item><c>SourceKind = "item"</c> with <c>SourceId</c> = item type id.</item>
/// <item><c>SourceKind = "self"</c> for fall damage, drowning, suffocation, etc.</item>
/// <item><c>SourceKind = "other"</c> with <c>SourceId</c> = the SourceOtherIndex.</item>
/// </list>
/// </para>
/// </summary>
public sealed class DamageTakenRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long Tick { get; set; }
    public long UnixMs { get; set; }

    public string SourceKind { get; set; } = "unknown";
    public int SourceId { get; set; }
    public string SourceName { get; set; } = "";

    public int DamageRaw { get; set; }
    public int DamageDealt { get; set; }
    public bool Pvp { get; set; }
    public bool Crit { get; set; }

    public int HpBefore { get; set; }
    public int HpAfter { get; set; }
    public int MaxHp { get; set; }

    /// <summary>Buff type ids active when the hurt fired.</summary>
    public List<int> ActiveBuffs { get; set; } = new();
}
