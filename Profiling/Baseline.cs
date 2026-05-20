#nullable enable

using System;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// Per-session baseline values that downstream detectors compare against.
/// Computed once per tick by <see cref="MetricCollector"/> after the new
/// <see cref="TickFrame"/> is pushed into history; every detector reads the
/// snapshotted values instead of recomputing or hardcoding absolute floors.
///
/// <para>
/// <b>Why this exists.</b> The previous design buried hardcoded millisecond
/// floors in each detector — <c>SpikeDetector.AbsoluteFloorMs = 5</c>,
/// <c>FreeRemoval.EpsilonMsPerTick = 0.10</c>. Those floors lie about reality
/// on machines whose normal frame time is well above (slow hardware) or well
/// below (high-refresh) the implicit 60 Hz assumption. A 5 ms "above floor"
/// filter discards every frame on a 25 fps player and almost nothing on a
/// 120 fps player. Routing every threshold through this class makes it
/// relative to the user's actual session, not the developer's hardware.
/// </para>
///
/// <para>
/// <b>Implementation.</b> Median and Median Absolute Deviation come from a
/// 512-bucket histogram (0.5 ms width, range 0..256 ms) over the collector's
/// rolling tick history. One pass per tick, no allocation past construction
/// (Invariant 2). Cost at 1800-tick history × 60 Hz ≈ 140 µs/sec total — well
/// inside the overhead budget even in Lite mode, and the value is shared by
/// every detector instead of each computing its own median.
/// </para>
///
/// <para>
/// <b>Calibration window.</b> <see cref="IsCalibrated"/> is false until at
/// least <see cref="MinCalibrationTicks"/> samples have accumulated. Detectors
/// must read this flag and skip (or fall back to a conservative default)
/// during warmup — emitting "lag detected" against a 3-tick history is the
/// exact noise the older absolute floors were trying to suppress.
/// </para>
/// </summary>
public sealed class Baseline
{
    /// <summary>Minimum samples before <see cref="IsCalibrated"/> turns true (~1 s at 60 Hz).</summary>
    public const int MinCalibrationTicks = 60;

    // 0.5 ms × 512 buckets covers 0..256 ms. Anything above clamps to the top
    // bucket (a 256 ms frame is already so extreme the exact value doesn't
    // change the median).
    private const int HistogramBuckets = 512;
    private const double HistogramBucketMs = 0.5;
    private readonly int[] _histogramScratch = new int[HistogramBuckets];

    // EMA smoothing for the allocation-rate baseline. Different cadence than
    // frame/period (which use exact median over the ring); allocation noise is
    // already high-variance per tick and EMA smooths it enough for the gated
    // detectors that consume it.
    private const double AllocEmaAlpha = 0.05;

    /// <summary>Median frame time (ms) over the rolling history. Source of truth for relative spike thresholds.</summary>
    public double FrameMsMedian { get; private set; }

    /// <summary>Median Absolute Deviation of frame time (ms). Robust jitter estimator; useful for "k × MAD" outlier rules.</summary>
    public double FrameMsMad { get; private set; }

    /// <summary>Median tick period (ms) — wall time between consecutive <c>BeginTick</c> calls. Source of truth for stall thresholds.</summary>
    public double TickPeriodMsMedian { get; private set; }

    /// <summary>MAD of tick period (ms).</summary>
    public double TickPeriodMsMad { get; private set; }

    /// <summary>Median per-tick allocation rate (bytes). Zero when allocation tracking is off.</summary>
    public double AllocBytesPerTickMedian { get; private set; }

    /// <summary>True once we have at least <see cref="MinCalibrationTicks"/> samples in history.</summary>
    public bool IsCalibrated { get; private set; }

    /// <summary>
    /// Recomputes baseline values from <paramref name="history"/>. Called once
    /// per tick by <see cref="MetricCollector.EndTick"/> after the new frame
    /// is pushed.
    /// </summary>
    /// <param name="history">The collector's rolling tick history (oldest first).</param>
    /// <param name="tracksAllocations">True if alloc bytes are being measured this session.</param>
    /// <param name="allocBytesThisTick">This tick's total allocation; ignored when <paramref name="tracksAllocations"/> is false.</param>
    public void Recompute(RingBuffer<TickFrame> history, bool tracksAllocations, double allocBytesThisTick)
    {
        IsCalibrated = history.Count >= MinCalibrationTicks;

        // Frame time: median + MAD.
        FrameMsMedian = FrameMedian(history);
        FrameMsMad = FrameMad(history, FrameMsMedian);

        // Tick period: successive TimestampUnixMs deltas. Pairwise from history.
        TickPeriodMsMedian = TickPeriodMedian(history);
        TickPeriodMsMad = TickPeriodMad(history, TickPeriodMsMedian);

        // Allocation rate: EMA-smoothed against this tick's byte count.
        if (tracksAllocations)
        {
            AllocBytesPerTickMedian += AllocEmaAlpha * (allocBytesThisTick - AllocBytesPerTickMedian);
        }
        else
        {
            AllocBytesPerTickMedian = 0d;
        }
    }

    /// <summary>Resets the baseline so the next session starts cold.</summary>
    public void Reset()
    {
        FrameMsMedian = 0d;
        FrameMsMad = 0d;
        TickPeriodMsMedian = 0d;
        TickPeriodMsMad = 0d;
        AllocBytesPerTickMedian = 0d;
        IsCalibrated = false;
    }

    // ---- Internal histogram helpers -----------------------------------------

    private double FrameMedian(RingBuffer<TickFrame> history)
    {
        int n = history.Count;
        if (n == 0) return 0d;
        ClearHistogram();
        for (int i = 0; i < n; i++) BumpBucket(history[i].FrameTimeMs);
        return BucketMedian(n);
    }

    private double FrameMad(RingBuffer<TickFrame> history, double median)
    {
        int n = history.Count;
        if (n == 0) return 0d;
        ClearHistogram();
        for (int i = 0; i < n; i++)
        {
            double dev = history[i].FrameTimeMs - median;
            if (dev < 0d) dev = -dev;
            BumpBucket(dev);
        }
        return BucketMedian(n);
    }

    private double TickPeriodMedian(RingBuffer<TickFrame> history)
    {
        int n = history.Count;
        if (n < 2) return 0d;
        ClearHistogram();
        for (int i = 1; i < n; i++)
        {
            double period = history[i].TimestampUnixMs - history[i - 1].TimestampUnixMs;
            BumpBucket(period);
        }
        return BucketMedian(n - 1);
    }

    private double TickPeriodMad(RingBuffer<TickFrame> history, double median)
    {
        int n = history.Count;
        if (n < 2) return 0d;
        ClearHistogram();
        for (int i = 1; i < n; i++)
        {
            double period = history[i].TimestampUnixMs - history[i - 1].TimestampUnixMs;
            double dev = period - median;
            if (dev < 0d) dev = -dev;
            BumpBucket(dev);
        }
        return BucketMedian(n - 1);
    }

    private void ClearHistogram() => Array.Clear(_histogramScratch, 0, _histogramScratch.Length);

    private void BumpBucket(double v)
    {
        int b = (int)(v / HistogramBucketMs);
        if (b < 0) b = 0;
        if (b >= HistogramBuckets) b = HistogramBuckets - 1;
        _histogramScratch[b]++;
    }

    /// <summary>
    /// Returns the bucket-midpoint of the median sample. Half-bucket of error
    /// (0.25 ms) is far below any threshold derived from this — the worst
    /// downstream user (stall detection at 3× median) needs only millisecond
    /// precision.
    /// </summary>
    private double BucketMedian(int totalSamples)
    {
        int target = totalSamples / 2;
        int running = 0;
        for (int b = 0; b < HistogramBuckets; b++)
        {
            running += _histogramScratch[b];
            if (running > target) return (b + 0.5d) * HistogramBucketMs;
        }
        return (HistogramBuckets - 0.5d) * HistogramBucketMs;
    }
}
