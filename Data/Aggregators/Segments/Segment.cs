#nullable enable

using System;
using System.Collections.Generic;
using LiteDB;
using PerformanceProfiler.Persistence.Records;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Persistence;
namespace PerformanceProfiler.Data.Aggregators.Segments;

/// <summary>
/// Immutable closed-segment value the UI reads. Built either from a freshly-
/// closed <see cref="OpenSegment"/> (live session, before the DB write
/// lands) or from a queried <see cref="SegmentRow"/> (Timeline scroll-back,
/// lifetime aggregates).
///
/// <para>
/// Holds only what tabs need to render. The expensive blobs (per-mod ms,
/// per-mod ids) are still here but as ready-to-iterate arrays rather than
/// LiteDB lists.
/// </para>
/// </summary>
public sealed record Segment
{
    public ObjectId Id { get; init; } = ObjectId.Empty;
    public ObjectId SessionId { get; init; } = ObjectId.Empty;
    public SegmentFamily Family { get; init; }
    public int Key { get; init; }
    public string Name { get; init; } = string.Empty;

    public long StartTick { get; init; }
    public long EndTick { get; init; }
    public long StartUnixMs { get; init; }
    public long EndUnixMs { get; init; }
    public long DurationMs { get; init; }
    public long Ticks { get; init; }
    public double TotalFrameMs { get; init; }

    public int SpikeCount { get; init; }
    public int StallCount { get; init; }
    public int DeathCount { get; init; }
    public int BossKillCount { get; init; }

    public bool Promoted { get; init; }
    public string PromotionReason { get; init; } = string.Empty;

    /// <summary>Parallel arrays. <see cref="ModIds"/>[i] gets <see cref="ModMs"/>[i] ms during this segment.</summary>
    public int[] ModIds { get; init; } = Array.Empty<int>();
    public double[] ModMs { get; init; } = Array.Empty<double>();

    public double AvgFrameMs => Ticks > 0 ? TotalFrameMs / Ticks : 0d;

    /// <summary>
    /// Top-N entries of (modId, ms) sorted descending. Allocates a new
    /// list; only used in the retrospective card / tab, never per-tick.
    /// </summary>
    public List<(int ModId, double Ms)> TopMods(int n)
    {
        var list = new List<(int ModId, double Ms)>(ModIds.Length);
        for (int i = 0; i < ModIds.Length; i++) list.Add((ModIds[i], ModMs[i]));
        list.Sort((a, b) => b.Ms.CompareTo(a.Ms));
        if (list.Count > n) list.RemoveRange(n, list.Count - n);
        return list;
    }

    /// <summary>Rehydrate from a LiteDB row.</summary>
    public static Segment FromRow(SegmentRow row)
    {
        int[] modIds = row.ModIds.Count > 0 ? row.ModIds.ToArray() : Array.Empty<int>();
        double[] modMs = row.ModMs.Count > 0 ? row.ModMs.ToArray() : Array.Empty<double>();
        return new Segment
        {
            Id = row.Id,
            SessionId = row.SessionId,
            Family = (SegmentFamily)row.Family,
            Key = row.Key,
            Name = row.Name,
            StartTick = row.StartTick,
            EndTick = row.EndTick,
            StartUnixMs = row.StartUnixMs,
            EndUnixMs = row.EndUnixMs,
            DurationMs = row.DurationMs,
            Ticks = row.Ticks,
            TotalFrameMs = row.TotalFrameMs,
            SpikeCount = row.SpikeCount,
            StallCount = row.StallCount,
            DeathCount = row.DeathCount,
            BossKillCount = row.BossKillCount,
            Promoted = row.Promoted,
            PromotionReason = row.PromotionReason,
            ModIds = modIds,
            ModMs = modMs,
        };
    }
}
