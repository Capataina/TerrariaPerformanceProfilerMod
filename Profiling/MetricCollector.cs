#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// Times each game tick, stores a rolling history of <see cref="TickFrame"/>s,
/// and harvests the per-mod, per-category CPU attribution accumulated by the
/// timing detours.
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
    // How fast the smoothed per-mod costs track the raw per-tick numbers. At
    // 60 ticks/s, 0.06 settles in roughly a second -- enough to kill per-tick
    // jitter without feeling laggy.
    private const double PerModSmoothing = 0.06d;

    private readonly RingBuffer<TickFrame> _history;

    // Per-mod, per-category CPU, [modId * CategoryCount + categoryId]. _raw is
    // this tick's harvest; _smoothed is what the UI displays.
    private readonly double[] _perModRawMs;
    private readonly double[] _perModSmoothedMs;
    private readonly double[] _perModAverageMs;
    private readonly double[] _perModHistoryMs;
    private readonly double[] _perModRollingMs;
    private readonly double[] _perHookRawMs;
    private readonly double[] _perHookSmoothedMs;
    private readonly double[] _perHookAverageMs;
    private readonly double[] _perHookHistoryMs;
    private readonly double[] _perHookRollingMs;
    private readonly int _historyCapacity;
    private int _sampleSlot;

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
        _historyCapacity = historyCapacity;
        int cells = PerModAttribution.ModCount * PerModAttribution.CategoryCount;
        _perModRawMs = new double[cells];
        _perModSmoothedMs = new double[cells];
        _perModAverageMs = new double[cells];
        _perModHistoryMs = new double[cells * historyCapacity];
        _perModRollingMs = new double[cells];
        _perHookRawMs = new double[PerModAttribution.HookCount];
        _perHookSmoothedMs = new double[PerModAttribution.HookCount];
        _perHookAverageMs = new double[PerModAttribution.HookCount];
        _perHookHistoryMs = new double[PerModAttribution.HookCount * historyCapacity];
        _perHookRollingMs = new double[PerModAttribution.HookCount];
    }

    /// <summary>The rolling per-tick history, oldest record first. The UI reads this to draw.</summary>
    public RingBuffer<TickFrame> History => _history;

    /// <summary>
    /// Smoothed per-mod, per-category CPU in milliseconds, indexed
    /// [modId * <see cref="PerModAttribution.CategoryCount"/> + categoryId].
    /// This is the stable value the per-mod tree displays; a mod's total is the
    /// sum of its category cells.
    /// </summary>
    public IReadOnlyList<double> PerModCategoryMs => _perModSmoothedMs;

    /// <summary>
    /// Rolling 30-second per-mod/category average in milliseconds. This is the
    /// stable view for inspecting lag-spike contribution without row churn.
    /// </summary>
    public IReadOnlyList<double> PerModCategoryAverageMs => _perModAverageMs;

    /// <summary>
    /// Smoothed per-hook CPU in milliseconds, indexed by hookId. Hook metadata is
    /// available through <see cref="PerModAttribution.Hooks"/>.
    /// </summary>
    public IReadOnlyList<double> PerHookMs => _perHookSmoothedMs;

    /// <summary>Rolling 30-second per-hook average in milliseconds, indexed by hookId.</summary>
    public IReadOnlyList<double> PerHookAverageMs => _perHookAverageMs;

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

        // Harvest this tick's per-mod cost, then fold it into the smoothed view.
        PerModAttribution.HarvestInto(_perModRawMs);
        UpdateRollingAverage(_perModRawMs, _perModHistoryMs, _perModRollingMs, _perModAverageMs, _sampleSlot);
        for (int i = 0; i < _perModSmoothedMs.Length; i++)
        {
            _perModSmoothedMs[i] += PerModSmoothing * (_perModRawMs[i] - _perModSmoothedMs[i]);
        }

        PerModAttribution.HarvestHooksInto(_perHookRawMs);
        UpdateRollingAverage(_perHookRawMs, _perHookHistoryMs, _perHookRollingMs, _perHookAverageMs, _sampleSlot);
        for (int i = 0; i < _perHookSmoothedMs.Length; i++)
        {
            _perHookSmoothedMs[i] += PerModSmoothing * (_perHookRawMs[i] - _perHookSmoothedMs[i]);
        }

        _sampleSlot++;
        if (_sampleSlot == _historyCapacity)
        {
            _sampleSlot = 0;
        }

        _tickStartTimestamp = -1L;
    }

    private void UpdateRollingAverage(double[] source, double[] history, double[] rolling, double[] average, int slot)
    {
        int offset = slot * source.Length;
        int samples = _history.Count < _historyCapacity ? _history.Count + 1 : _historyCapacity;
        for (int i = 0; i < source.Length; i++)
        {
            int index = offset + i;
            rolling[i] += source[i] - history[index];
            history[index] = source[i];
            average[i] = rolling[i] / samples;
        }
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
