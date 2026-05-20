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
/// <b>v0.6.1 implementation — incremental histogram.</b> v0.5/v0.6 ran four
/// full passes over the 1800-frame ring buffer every tick (FrameMedian +
/// FrameMad + TickPeriodMedian + TickPeriodMad), clearing + bumping +
/// scanning the histogram each time. That's ~13,600 ops/tick of pure
/// bookkeeping, all to recompute values that change by at most one bucket
/// per tick.
/// </para>
/// <para>
/// v0.6.1 maintains persistent histograms that track the live state:
/// <see cref="OnFramePushed"/> updates by +1 on the new frame's bucket and
/// -1 on the bucket of the frame being evicted (when the ring is full).
/// Median is then a single 512-bucket scan per tick — same upper bound, much
/// smaller average (most ticks have the median in an early bucket).
/// </para>
/// <para>
/// MAD depends on |x - median|; the median can change per tick so MAD can't
/// be tracked the same way. v0.6.1 recomputes MAD every <see cref="MadRecomputeInterval"/>
/// ticks, amortising the cost. Stall + spike detectors that use MAD operate
/// on second-scale dynamics; a half-second lag on the MAD value is below
/// their threshold sensitivity.
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

    /// <summary>How often (in ticks) to recompute the MAD values. ~0.5 s at 60 Hz.</summary>
    private const int MadRecomputeInterval = 30;

    // Persistent histograms maintained incrementally. Two histograms × two
    // metrics (frame-time + tick-period) = four total. Allocation-free
    // after construction; updates are single ++/-- operations.
    private readonly int[] _frameHist = new int[HistogramBuckets];
    private readonly int[] _frameMadHist = new int[HistogramBuckets];
    private readonly int[] _periodHist = new int[HistogramBuckets];
    private readonly int[] _periodMadHist = new int[HistogramBuckets];

    // Per-frame bucket shadow rings. Mirror the ring buffer of frames so
    // we know which bucket to decrement on eviction. Sized to match
    // MetricCollector.HistoryCapacity.
    private short[] _frameBuckets = Array.Empty<short>();
    private short[] _periodBuckets = Array.Empty<short>();
    private int _ringCapacity;
    private int _frameHead;     // next write position in _frameBuckets
    private int _frameCount;
    private int _periodHead;
    private int _periodCount;

    // Most recent UnixMs we saw, for incremental period calculation.
    private long _lastUnixMs;
    private bool _hasLastTimestamp;

    // MAD amortisation counter.
    private int _ticksSinceMadRecompute;

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
    ///
    /// <para>
    /// v0.6.1: this method is now a thin shell that calls
    /// <see cref="OnFramePushed"/> for incremental update, then scans the
    /// histograms for medians. MAD is recomputed amortised every
    /// <see cref="MadRecomputeInterval"/> ticks.
    /// </para>
    /// </summary>
    public void Recompute(RingBuffer<TickFrame> history, bool tracksAllocations, double allocBytesThisTick)
    {
        IsCalibrated = history.Count >= MinCalibrationTicks;

        // Lazy-size the shadow rings on first call (we don't know history
        // capacity at construction time — it's MetricCollector's choice).
        if (_ringCapacity == 0 && history.Capacity > 0)
        {
            _ringCapacity = history.Capacity;
            _frameBuckets = new short[_ringCapacity];
            _periodBuckets = new short[_ringCapacity];
        }

        // The new frame is at history.Count - 1 (most recent).
        // Resync if our incremental state doesn't match history's count
        // (e.g. test path pushes N frames then calls Recompute once, or
        // history was cleared and replayed). The mismatch detection is
        // free; the rebuild only fires when state is stale.
        bool stateMatches = history.Count == _frameCount
                         || (_frameCount == _ringCapacity && history.Count >= _ringCapacity);
        if (!stateMatches)
        {
            RebuildFromHistory(history);
        }
        else if (history.Count > 0)
        {
            TickFrame newest = history[history.Count - 1];
            OnFramePushed(newest);
        }

        // Median: cheap scan over 512 buckets, every tick.
        FrameMsMedian = HistogramMedian(_frameHist, _frameCount);
        TickPeriodMsMedian = HistogramMedian(_periodHist, _periodCount);

        // MAD: amortised — recompute every MadRecomputeInterval ticks.
        // Between recomputes, the previous MAD value stays put. Detector
        // thresholds use MAD as a scale factor on the median; a 30-tick
        // lag is invisible to them.
        _ticksSinceMadRecompute++;
        if (_ticksSinceMadRecompute >= MadRecomputeInterval)
        {
            _ticksSinceMadRecompute = 0;
            FrameMsMad = ComputeMadFromHistory(history, FrameMsMedian, useFrameMs: true);
            TickPeriodMsMad = ComputeMadFromHistory(history, TickPeriodMsMedian, useFrameMs: false);
        }

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

    /// <summary>
    /// Incrementally maintain the frame + period histograms. Called every
    /// tick from <see cref="Recompute"/>.
    ///
    /// <para>
    /// Two histograms tracked here:
    /// <list type="bullet">
    ///   <item><description><c>_frameHist</c>: tracks <c>frame.FrameTimeMs</c> for the most recent <see cref="_ringCapacity"/> frames.</description></item>
    ///   <item><description><c>_periodHist</c>: tracks UnixMs deltas between consecutive frames.</description></item>
    /// </list>
    /// Each tick: +1 the new bucket, -1 the bucket of the frame being evicted
    /// (if any). Constant work per tick, independent of ring capacity.
    /// </para>
    /// </summary>
    private void OnFramePushed(in TickFrame newFrame)
    {
        // ---- Frame histogram --------------------------------------------
        int newFrameBucket = BucketFor(newFrame.FrameTimeMs);
        if (_frameCount >= _ringCapacity)
        {
            // Ring is full — evict the bucket of the frame about to be overwritten.
            short evictBucket = _frameBuckets[_frameHead];
            _frameHist[evictBucket]--;
        }
        else
        {
            _frameCount++;
        }
        _frameBuckets[_frameHead] = (short)newFrameBucket;
        _frameHist[newFrameBucket]++;
        _frameHead = _frameHead + 1 == _ringCapacity ? 0 : _frameHead + 1;

        // ---- Period histogram -------------------------------------------
        if (_hasLastTimestamp)
        {
            double periodMs = newFrame.TimestampUnixMs - _lastUnixMs;
            int newPeriodBucket = BucketFor(periodMs);
            if (_periodCount >= _ringCapacity)
            {
                short evictBucket = _periodBuckets[_periodHead];
                _periodHist[evictBucket]--;
            }
            else
            {
                _periodCount++;
            }
            _periodBuckets[_periodHead] = (short)newPeriodBucket;
            _periodHist[newPeriodBucket]++;
            _periodHead = _periodHead + 1 == _ringCapacity ? 0 : _periodHead + 1;
        }
        _lastUnixMs = newFrame.TimestampUnixMs;
        _hasLastTimestamp = true;
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

        Array.Clear(_frameHist, 0, _frameHist.Length);
        Array.Clear(_frameMadHist, 0, _frameMadHist.Length);
        Array.Clear(_periodHist, 0, _periodHist.Length);
        Array.Clear(_periodMadHist, 0, _periodMadHist.Length);
        if (_frameBuckets.Length > 0) Array.Clear(_frameBuckets, 0, _frameBuckets.Length);
        if (_periodBuckets.Length > 0) Array.Clear(_periodBuckets, 0, _periodBuckets.Length);
        _frameHead = _frameCount = _periodHead = _periodCount = 0;
        _hasLastTimestamp = false;
        _lastUnixMs = 0;
        _ticksSinceMadRecompute = 0;
    }

    /// <summary>
    /// Rebuild the persistent histograms from the current history. Called
    /// when incremental state has fallen out of sync with history (test
    /// paths that batch-push frames, or post-Reset replay). O(history.Count)
    /// rather than O(1) but only fires on resync.
    /// </summary>
    private void RebuildFromHistory(RingBuffer<TickFrame> history)
    {
        Array.Clear(_frameHist, 0, _frameHist.Length);
        Array.Clear(_periodHist, 0, _periodHist.Length);
        if (_frameBuckets.Length > 0) Array.Clear(_frameBuckets, 0, _frameBuckets.Length);
        if (_periodBuckets.Length > 0) Array.Clear(_periodBuckets, 0, _periodBuckets.Length);
        _frameHead = _frameCount = _periodHead = _periodCount = 0;
        _hasLastTimestamp = false;
        _lastUnixMs = 0;

        for (int i = 0; i < history.Count; i++)
        {
            OnFramePushed(history[i]);
        }
    }

    // ---- Internal helpers ---------------------------------------------------

    private static int BucketFor(double v)
    {
        int b = (int)(v / HistogramBucketMs);
        if (b < 0) b = 0;
        if (b >= HistogramBuckets) b = HistogramBuckets - 1;
        return b;
    }

    /// <summary>
    /// Scan a populated histogram for the bucket containing the median sample.
    /// Returns the bucket-midpoint (half-bucket precision = 0.25 ms, far
    /// below downstream threshold sensitivity).
    /// </summary>
    private static double HistogramMedian(int[] hist, int totalSamples)
    {
        if (totalSamples == 0) return 0d;
        int target = totalSamples / 2;
        int running = 0;
        for (int b = 0; b < HistogramBuckets; b++)
        {
            running += hist[b];
            if (running > target) return (b + 0.5d) * HistogramBucketMs;
        }
        return (HistogramBuckets - 0.5d) * HistogramBucketMs;
    }

    /// <summary>
    /// Compute MAD from the actual history (not the persistent histogram —
    /// MAD depends on |x - median| which changes when median changes). Called
    /// every <see cref="MadRecomputeInterval"/> ticks for amortisation.
    /// </summary>
    private double ComputeMadFromHistory(RingBuffer<TickFrame> history, double median, bool useFrameMs)
    {
        int n = history.Count;
        if (n < (useFrameMs ? 1 : 2)) return 0d;

        // Reuse _frameMadHist for both branches; clear once per call.
        int[] hist = _frameMadHist;
        Array.Clear(hist, 0, HistogramBuckets);

        int samples = 0;
        if (useFrameMs)
        {
            for (int i = 0; i < n; i++)
            {
                double dev = history[i].FrameTimeMs - median;
                if (dev < 0d) dev = -dev;
                hist[BucketFor(dev)]++;
                samples++;
            }
        }
        else
        {
            for (int i = 1; i < n; i++)
            {
                double period = history[i].TimestampUnixMs - history[i - 1].TimestampUnixMs;
                double dev = period - median;
                if (dev < 0d) dev = -dev;
                hist[BucketFor(dev)]++;
                samples++;
            }
        }

        return HistogramMedian(hist, samples);
    }
}
