#nullable enable

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
/// Periodic world-state snapshot taken every ~30 s. Cheap (one row per 30s
/// × maybe 30 minutes = 60 rows per session) but a huge reconstruction
/// help: lets a query answer "what was the player doing at minute 7" by
/// reading position + biome + HP + entity counts directly.
///
/// <para>
/// Distinct from <c>tickAggregatesWarm</c> (per-second perf rollup) and
/// <c>contextTransitions</c> (event log) — this is a periodic *state*
/// snapshot the other two don't carry.
/// </para>
/// </summary>
public sealed class WorldSnapshotRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long Tick { get; set; }
    public long UnixMs { get; set; }

    // Player state.
    public float TileX { get; set; }
    public float TileY { get; set; }
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Mana { get; set; }
    public int MaxMana { get; set; }

    // World state.
    public string PrimaryBiome { get; set; } = "(none)";
    public bool IsHardmode { get; set; }
    public string GameMode { get; set; } = "(unknown)";
    public string TimeOfDayBand { get; set; } = "(unknown)"; // Dawn / Day / Dusk / Night
    public bool IsDayTime { get; set; }

    // Entity counts (active).
    public int NpcCount { get; set; }
    public int ProjectileCount { get; set; }
    public int DustCount { get; set; }
    public int ItemCount { get; set; }

    /// <summary>Display name of the primary active boss, or "(no boss)".</summary>
    public string PrimaryBoss { get; set; } = "(no boss)";
}
