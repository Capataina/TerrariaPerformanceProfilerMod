#nullable enable

using System.Collections.Generic;
using LiteDB;

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
/// One row per local-player death event. Captured on the
/// <c>Main.LocalPlayer.dead</c> false→true edge in
/// <c>ContextTransitionWatcher</c>; lets a session retrospective answer
/// "did the player die, when, where, doing what" without depending on
/// the player's memory of the run.
/// </summary>
public sealed class PlayerDeathRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 2;

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

    /// <summary>
    /// Full damage-weighted breakdown over the death window (v0.6, schema 2):
    /// every source that hit the player within
    /// <see cref="DamageAttributionWindowSeconds"/> of death, sorted by total
    /// damage descending. The first entry is the killer by damage weight (not
    /// last-hit credit, which over-credits whichever source delivered the
    /// final blow on a softened player). Empty on schema-1 rows (read-time
    /// default for v0.5 data).
    /// </summary>
    public List<DeathDamageContributor> DamageWeighting { get; set; } = new();

    /// <summary>
    /// Window the attribution was computed against, in seconds. Persisted so
    /// future analysis knows what the row was computed against if the
    /// window is later tuned.
    /// </summary>
    public int DamageAttributionWindowSeconds { get; set; } = 10;

    /// <summary>Free-form summary string (e.g. "killed by Eye of Cthulhu (78%) + Vulture (22%) in Forest at (3500, 240)").</summary>
    public string Summary { get; set; } = string.Empty;
}
