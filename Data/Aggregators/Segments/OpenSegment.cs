#nullable enable

using System;
using PerformanceProfiler.Profiling.Events;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data.Aggregators.Segments;

/// <summary>
/// A segment that's currently in flight — opened on an edge-trigger of a
/// predicate on <see cref="EventContext"/>, accumulating frame-time and
/// per-mod CPU each tick, waiting for its end predicate to fire.
///
/// <para>
/// Mutable by design. The detector owns the lifecycle: <see cref="Reset"/>
/// when checked out from the pool, fold this tick's frame-ms + per-mod
/// array on every tick, close it when the end predicate fires (the
/// detector reads the running totals, builds a <see cref="SegmentRow"/>,
/// and returns the OpenSegment to the pool).
/// </para>
///
/// <para>
/// Allocation profile: <see cref="PerModMs"/> is sized to the active
/// modlist at allocation time and reused across many segments via the
/// pool, so steady-state segment churn is allocation-free aside from the
/// closed-segment LiteDB row (which is a once-per-segment cost, not a
/// per-tick cost).
/// </para>
/// </summary>
public sealed class OpenSegment
{
    public SegmentFamily Family;
    public int Key;
    public string Name = string.Empty;

    public long StartTick;
    public long StartUnixMs;

    public long Ticks;
    public double TotalFrameMs;

    public int SpikeCount;
    public int StallCount;
    public int DeathCount;
    public int BossKillCount;

    /// <summary>Per-mod CPU ms accumulated during this segment. Sized to <c>PerModAttribution.ModCount</c>; index = modId.</summary>
    public double[] PerModMs = Array.Empty<double>();

    /// <summary>Resets every counter and the per-mod array; the array is reallocated only if mod count changed.</summary>
    public void Reset(int modCount)
    {
        Family = SegmentFamily.Biome;
        Key = 0;
        Name = string.Empty;
        StartTick = 0L;
        StartUnixMs = 0L;
        Ticks = 0L;
        TotalFrameMs = 0d;
        SpikeCount = 0;
        StallCount = 0;
        DeathCount = 0;
        BossKillCount = 0;

        if (PerModMs.Length != modCount)
        {
            PerModMs = new double[modCount];
        }
        else
        {
            Array.Clear(PerModMs, 0, PerModMs.Length);
        }
    }
}
