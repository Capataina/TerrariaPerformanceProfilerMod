#nullable enable

using System;
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
/// The per-modlist level of the two-level rollup: one row per
/// <c>(InternalName, Fingerprint)</c> — the same lifetime distributions as
/// <see cref="ModLifetimeRollupRow"/> but scoped to a single modlist (stack). This is
/// the cross-modpack substrate: it makes a mod's rank / percentile <i>within each
/// stack it has appeared in</i> recoverable, so a detector can say "ranks bottom-10%
/// in every modlist it has been in" or "cheap alone but costly in modpack B"
/// (decisions B and C).
///
/// <para>
/// No recency ring at this level — the global row's ring covers "last N"; here the
/// Welford blocks plus the counts are enough to rank a mod against its stack-mates.
/// The fingerprint is the modpack key (decision C gates cross-modpack analysis on
/// ≥2 distinct well-sampled stacks); identity is still the stable
/// <see cref="InternalName"/>, never the fingerprint.
/// </para>
/// </summary>
public sealed class ModModlistRollupRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    /// <summary>The stable cross-session key.</summary>
    public string InternalName { get; set; } = string.Empty;

    /// <summary>The modlist (stack) this row scopes the mod's stats to.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    /// <summary>Sessions of this mod under this specific stack.</summary>
    public int SessionCount { get; set; }

    /// <summary>Sessions under this stack in which the mod did measurable work.</summary>
    public int ActiveSessionCount { get; set; }

    public WelfordStat Cost { get; set; } = new();
    public WelfordStat Alloc { get; set; } = new();
    public WelfordStat Engagement { get; set; } = new();

    public long TotalSpikeContributions { get; set; }
    public long TotalStallContributions { get; set; }
}
