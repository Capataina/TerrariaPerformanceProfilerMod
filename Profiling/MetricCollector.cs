#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// Times each game tick and stores a rolling history of <see cref="TickFrame"/>s,
/// and harvests the per-mod CPU attribution accumulated by the timing detours.
///
/// Pure logic with no tModLoader dependency: every game-sourced value (the tick
/// index, the entity counts) is passed in by the caller, so the collector is
/// unit-testable without a running game (CLAUDE.md testability standard). The
/// glue that reads those values from the game lives in the ModSystem that
/// drives this class.
///
/// Usage is a strict pair per tick: <see cref="BeginTick"/> at the start of a
/// tick, <see cref="EndTick"/> at the end. <see cref="EndTick"/> with no open
/// tick is ignored, so a partial-update frame (where the start hook never
/// fired) records nothing rather than a bogus 0 ms tick.
/// </summary>
public sealed class MetricCollector
{
    private readonly RingBuffer<TickFrame> _history;

    // Per-mod CPU for the most recently completed tick, in milliseconds,
    // indexed by ModId. Sized once at construction; harvested every EndTick.
    private readonly double[] _perModCpuMs;

    // Stopwatch timestamp captured at BeginTick; -1 means "no tick currently open".
    private long _tickStartTimestamp = -1L;

    // Cumulative GC pause time (ms) read at BeginTick, so EndTick can report the
    // pause time that accrued during this tick alone.
    private double _gcPauseMsAtTickStart;

    /// <summary>Creates a collector whose history holds <paramref name="historyCapacity"/> ticks.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="historyCapacity"/> is not positive.</exception>
    public MetricCollector(int historyCapacity)
    {
        _history = new RingBuffer<TickFrame>(historyCapacity);
        _perModCpuMs = new double[PerModAttribution.ModCount];
    }

    /// <summary>The rolling per-tick history, oldest record first. The UI reads this to draw.</summary>
    public RingBuffer<TickFrame> History => _history;

    /// <summary>
    /// Per-mod CPU for the most recently completed tick, in milliseconds,
    /// indexed by ModId (see <see cref="HookInterceptor.ProfiledModNames"/>).
    /// </summary>
    public IReadOnlyList<double> PerModCpuMs => _perModCpuMs;

    /// <summary>True between a <see cref="BeginTick"/> and its matching <see cref="EndTick"/>.</summary>
    public bool TickOpen => _tickStartTimestamp >= 0L;

    /// <summary>
    /// Marks the start of a tick: captures the wall-clock and GC-pause baselines
    /// the matching <see cref="EndTick"/> measures against, and clears the
    /// per-mod accumulator so the detours start the tick from zero.
    /// </summary>
    public void BeginTick()
    {
        _tickStartTimestamp = Stopwatch.GetTimestamp();
        _gcPauseMsAtTickStart = GcPauseMilliseconds();
        PerModAttribution.BeginTick();
    }

    /// <summary>
    /// Marks the end of a tick, builds its <see cref="TickFrame"/>, commits it
    /// to the history, and harvests the per-mod attribution for the tick. Does
    /// nothing if no tick is open: a partial-update frame is "not sampled",
    /// never recorded as a 0 ms tick.
    /// </summary>
    /// <param name="tickIndex">Session-relative tick index (the game's update counter).</param>
    /// <param name="npcCount">Active NPC count at tick close.</param>
    /// <param name="projectileCount">Active projectile count at tick close.</param>
    /// <param name="dustCount">Active dust count at tick close.</param>
    public void EndTick(long tickIndex, int npcCount, int projectileCount, int dustCount)
    {
        if (_tickStartTimestamp < 0L)
        {
            return;
        }

        long endTimestamp = Stopwatch.GetTimestamp();

        double gcTimeMs = GcPauseMilliseconds() - _gcPauseMsAtTickStart;
        if (gcTimeMs < 0d)
        {
            // The pause counter is monotonic; clamp only as a defensive guard.
            gcTimeMs = 0d;
        }

        TickFrame frame = new TickFrame
        {
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TickIndex = tickIndex,
            FrameTimeMs = TimestampDeltaMs(_tickStartTimestamp, endTimestamp),
            GcTimeMs = gcTimeMs,
            NpcCount = npcCount,
            ProjectileCount = projectileCount,
            DustCount = dustCount,
            ModSamples = null, // Per-frame per-mod arrays are a later memory-tuning step.
        };

        _history.Push(in frame);
        PerModAttribution.HarvestInto(_perModCpuMs);
        _tickStartTimestamp = -1L;
    }

    /// <summary>Converts a delta of <see cref="Stopwatch"/> timestamps to milliseconds.</summary>
    private static double TimestampDeltaMs(long startTimestamp, long endTimestamp)
    {
        return (endTimestamp - startTimestamp) * 1000d / Stopwatch.Frequency;
    }

    /// <summary>
    /// Cumulative GC pause time since process start, in milliseconds.
    /// <see cref="GC.GetTotalPauseDuration"/> (.NET 8+) is monotonic, so the
    /// difference of two readings is the pause time within the interval.
    /// </summary>
    private static double GcPauseMilliseconds()
    {
        return GC.GetTotalPauseDuration().TotalMilliseconds;
    }
}
