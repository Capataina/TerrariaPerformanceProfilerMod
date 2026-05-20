#nullable enable

using System.Collections.Generic;
using LiteDB;

namespace PerformanceProfiler.Profiling.Persistence.Records;

/// <summary>
/// One row per local-player death event. Captured on the
/// <c>Main.LocalPlayer.dead</c> false→true edge in
/// <c>ContextTransitionWatcher</c>; lets a session retrospective answer
/// "did the player die, when, where, doing what" without depending on
/// the player's memory of the run.
/// </summary>
public sealed class PlayerDeathRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long Tick { get; set; }
    public long UnixMs { get; set; }

    /// <summary>World-tile X coordinate of the player at time of death (Player.position / 16f).</summary>
    public float TileX { get; set; }
    public float TileY { get; set; }

    /// <summary>Player HP just before death (usually 0 but record the last seen value).</summary>
    public int LastHp { get; set; }
    public int MaxHp { get; set; }

    /// <summary>Active boss NPC types when the death fired (so "killed by boss X" is reconstructable).</summary>
    public List<int> ActiveBossNpcTypes { get; set; } = new();

    /// <summary>Display name of the deepest-HP boss active at death time, or "(no boss)".</summary>
    public string PrimaryBoss { get; set; } = "(no boss)";

    /// <summary>Free-form summary string (e.g. "killed by Eye of Cthulhu in Forest at (3500, 240)").</summary>
    public string Summary { get; set; } = string.Empty;
}
