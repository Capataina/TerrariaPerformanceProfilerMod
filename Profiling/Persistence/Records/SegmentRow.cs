#nullable enable

using System.Collections.Generic;
using LiteDB;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
namespace PerformanceProfiler.Profiling.Persistence.Records;

/// <summary>
/// One closed segment: a temporally-bounded slice of gameplay defined by a
/// single predicate (a biome bit, a weather flag, an active boss, an
/// invasion, a subworld key, hardmode, a combat window, a death-bracket, or
/// a user bookmark).
///
/// <para>
/// Captures everything the retrospective card surfaces: duration, the
/// per-mod CPU cost accumulated <i>during</i> the segment, spike/stall/
/// death counters, and an optional name override (used by user bookmarks
/// and by named bosses that we resolve at close-time so the row stays
/// readable without the registry).
/// </para>
///
/// <para>
/// Per-mod cost is stored as a parallel <see cref="ModIds"/> + <see cref="ModMs"/>
/// list rather than a <c>Dictionary&lt;int, double&gt;</c> so BSON
/// serialises it as a small array instead of a per-row dynamic map.
/// </para>
/// </summary>
public sealed class SegmentRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;

    /// <summary>Family discriminator. See <see cref="Profiling.Segments.SegmentFamily"/>.</summary>
    public byte Family { get; set; }

    /// <summary>
    /// Secondary identifier inside the family. Together (Family, Key)
    /// uniquely identifies "this Jungle visit" vs "this Underground visit".
    /// </summary>
    public int Key { get; set; }

    /// <summary>Pre-resolved display name ("Jungle visit", "Blood Moon", "Eye of Cthulhu fight").</summary>
    public string Name { get; set; } = string.Empty;

    public long StartTick { get; set; }
    public long EndTick { get; set; }
    public long StartUnixMs { get; set; }
    public long EndUnixMs { get; set; }
    public long DurationMs { get; set; }

    /// <summary>Frames recorded during the segment (≈ duration × 60 / 1000 in normal play).</summary>
    public long Ticks { get; set; }

    /// <summary>Total frame-time accrued during the segment in ms. Sum / Ticks = avg frame ms.</summary>
    public double TotalFrameMs { get; set; }

    /// <summary>Number of spike events that fired while this segment was open.</summary>
    public int SpikeCount { get; set; }

    /// <summary>Number of stall events that fired while this segment was open.</summary>
    public int StallCount { get; set; }

    /// <summary>Number of player-death events that fired while this segment was open.</summary>
    public int DeathCount { get; set; }

    /// <summary>Number of bosses killed while this segment was open (closed boss segments).</summary>
    public int BossKillCount { get; set; }

    /// <summary>True if the segment tripped promotion logic and fired a retrospective toast.</summary>
    public bool Promoted { get; set; }

    /// <summary>Why the segment was promoted ("spike", "death", "boss-kill", "outlier", "rare", "bookmark", or empty).</summary>
    public string PromotionReason { get; set; } = string.Empty;

    /// <summary>Parallel with <see cref="ModMs"/>. ModId is from <c>HookInterceptor.ProfiledMods</c>.</summary>
    public List<int> ModIds { get; set; } = new();

    /// <summary>Per-mod CPU cost accumulated during the segment in ms. Parallel with <see cref="ModIds"/>.</summary>
    public List<double> ModMs { get; set; } = new();
}
