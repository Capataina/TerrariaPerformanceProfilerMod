#nullable enable

using System;
using System.Collections.Generic;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// One coalesced spike, captured at detection time and frozen thereafter.
///
/// A spike is one or more **consecutive** ticks whose frame time exceeded both
/// a relative threshold (frame ≥ 2× baseline) and an absolute floor (≥ 5 ms).
/// A sub-threshold tick closes the window — the brief recovery between two
/// adjacent bad stretches produces two records, not one big one. See the
/// "Spike storms" worked example in the spikes-and-allocations plan §5.
///
/// <see cref="PerModCatMs"/> and <see cref="PerModCatBytes"/> hold the
/// per-mod-per-category breakdown at the WORST tick in the window (not the
/// opening tick) — that's the moment the drill-down most wants to see. If the
/// worst tick gets surpassed during the window, the snapshot is re-captured.
/// </summary>
public struct SpikeWindow
{
    /// <summary>Tick the spike opened on (frame time first crossed threshold).</summary>
    public long StartTick;
    /// <summary>Tick the spike closed on (last consecutive over-threshold tick).</summary>
    public long EndTick;
    /// <summary>Tick within the window whose frame time was the worst.</summary>
    public long WorstTick;
    /// <summary>Tick whose per-mod snapshot is stored in <see cref="PerModCatMs"/>/<see cref="PerModCatBytes"/>. Tracks WorstTick.</summary>
    public long SnapshotTick;

    /// <summary>The worst frame time observed inside the window, in ms.</summary>
    public double WorstFrameMs;

    /// <summary>The 30-second median frame time at the moment of detection. The baseline the spike beat.</summary>
    public double BaselineMs;

    /// <summary>Median Absolute Deviation at the moment of detection. Robust scale estimator — usable as a secondary sort/filter.</summary>
    public double MadMs;

    /// <summary>True if the spike fired within the first ~10 s of the session, where JIT warmup makes spikes expected.</summary>
    public bool Warming;

    /// <summary>
    /// Per-mod-per-category ms at <see cref="SnapshotTick"/>, layout
    /// <c>[modId * CategoryCount + categoryId]</c>. Sized at construction
    /// to <c>modCount * CategoryCount</c>.
    /// </summary>
    public float[] PerModCatMs;

    /// <summary>
    /// Per-mod-per-category allocation bytes at <see cref="SnapshotTick"/>. Same
    /// layout as <see cref="PerModCatMs"/>. Null when allocation tracking is off.
    /// </summary>
    public float[]? PerModCatBytes;

    /// <summary>
    /// Optional human-readable context summary (e.g. "Cryogen Phase 2 · Sulphurous Sea"),
    /// populated when the Events tab ships. Null in the meantime.
    /// </summary>
    public string? ContextSummary;
}

/// <summary>
/// Robust median-based spike detector. Operates on the live
/// <see cref="RingBuffer{T}"/> of <see cref="TickFrame"/>s the collector
/// already keeps. Pure logic; no tModLoader dependency. Unit-testable.
///
/// <para>
/// <b>Why median, not mean.</b> Tick-time distributions in modded Terraria are
/// heavy-tailed and bimodal — quiet ticks at 1-2 ms, occasional GC pauses at
/// 20-40 ms. A mean-based baseline (or EMA) walks toward the pauses and the
/// "spike threshold = 2× mean" silently inflates until real spikes hide
/// underneath. Median ignores the top half by construction. The plan §5
/// documents the alternatives considered (EMA, MAD threshold, raw stddev) with
/// rationale for the chosen scheme.
/// </para>
///
/// <para>
/// <b>Two-stage triggering.</b> An exact median over 1800 ticks is O(N) per
/// call. We don't pay that per tick. Stage 1 is a cheap EMA pre-check: if the
/// frame is even close to the EMA's 2× threshold, we move to stage 2 which
/// computes the exact median over the history ring and only opens a spike
/// window if the frame still passes. False positives at stage 1 are filtered
/// at stage 2; we only pay the exact-median cost on candidate ticks.
/// </para>
///
/// <para>
/// <b>Window de-dup.</b> Sustained bad runs produce one record, not N. A
/// sub-threshold tick closes the window; a brief recovery followed by another
/// bad run opens a new window. Memory-bounded via a 50-slot ring buffer.
/// </para>
/// </summary>
public sealed class SpikeDetector
{
    /// <summary>Number of consecutive sub-threshold ticks needed to consider a window closed.</summary>
    /// <remarks>
    /// Set to 1: a single recovered tick closes the window. The worked example
    /// in plan §5 ("spike storms") relies on this. Could be raised to 3 if we
    /// observe that legitimate single-frame dips in the middle of a long bad
    /// stretch are creating phantom split windows.
    /// </remarks>
    public const int RecoveryTicksToCloseWindow = 1;

    /// <summary>Number of ticks at session start where spikes are badged "warming" (JIT).</summary>
    public const int WarmupTicks = 600;

    /// <summary>Default relative threshold (frame must beat this multiple of the baseline).</summary>
    public const double DefaultThresholdMultiplier = 2.0;

    /// <summary>Default absolute floor (frame must also beat this many ms).</summary>
    public const double DefaultAbsoluteFloorMs = 5.0;

    private readonly RingBuffer<SpikeWindow> _windows = new RingBuffer<SpikeWindow>(50);
    private readonly SpikeWindowsView _windowsView;
    private readonly int _modCount;
    private readonly bool _tracksAllocations;

    // EMA used only for the cheap pre-check before paying for the exact median.
    private double _emaFrameMs;
    private const double EmaAlpha = 0.05;

    private int _ticksSeen;
    private SpikeWindow _openWindow;
    private bool _windowOpen;
    private int _consecutiveSubThreshold;

    // Scratch arrays reused across exact-median calls to avoid per-spike allocation
    // for the histogram. The bucket size is 0.5 ms across 0..256 ms; anything
    // above 256 ms gets clamped to the top bucket (a 256 ms tick is already so
    // extreme that the exact value doesn't matter for the median).
    private const int HistogramBuckets = 512;
    private const double HistogramBucketMs = 0.5;
    private readonly int[] _histogramScratch = new int[HistogramBuckets];

    public SpikeDetector(int modCount, bool tracksAllocations)
    {
        _modCount = modCount;
        _tracksAllocations = tracksAllocations;
        _windowsView = new SpikeWindowsView(_windows);
    }

    /// <summary>Relative threshold; configurable later via ModConfig.</summary>
    public double ThresholdMultiplier { get; set; } = DefaultThresholdMultiplier;

    /// <summary>Absolute floor; configurable later via ModConfig.</summary>
    public double AbsoluteFloorMs { get; set; } = DefaultAbsoluteFloorMs;

    /// <summary>
    /// The captured spike windows in chronological order, oldest first. The
    /// returned wrapper is constructed once at detector construction so reads
    /// from the overlay and session writer are allocation-free.
    /// </summary>
    public IReadOnlyList<SpikeWindow> Windows => _windowsView;

    /// <summary>Number of captured spike windows currently retained.</summary>
    public int Count => _windows.Count;

    /// <summary>
    /// Drive the detector with the just-committed tick. Reads from the collector's
    /// history (for the median baseline) and per-tick ring (for the per-mod snapshot
    /// at the worst-tick).
    /// </summary>
    public void OnTick(TickFrame frame, RingBuffer<TickFrame> history, PerTickAttributionRing perTickRing)
    {
        _ticksSeen++;
        double frameMs = frame.FrameTimeMs;
        _emaFrameMs += EmaAlpha * (frameMs - _emaFrameMs);

        // Stage 1: cheap pre-check. The EMA can drift up under sustained spikes,
        // which is actually what we want for the pre-check — when many recent
        // frames were 30 ms, a 30 ms frame is not a spike here. The exact median
        // in stage 2 catches the long-tail cases EMA misses.
        bool emaCandidate = frameMs >= _emaFrameMs * ThresholdMultiplier
                         && frameMs >= AbsoluteFloorMs;

        if (!emaCandidate)
        {
            HandleSubThreshold();
            return;
        }

        // Stage 2: exact median over the retained history. Pay the cost only on
        // candidates. The histogram approach is O(N) one-pass — at 1800 ticks
        // that's a sub-millisecond cost, far below the per-tick budget.
        double median = ExactMedian(history);
        double mad = ExactMad(history, median);

        // Recompute the candidate test against the exact baseline.
        bool spike = frameMs >= median * ThresholdMultiplier
                  && frameMs >= AbsoluteFloorMs;

        if (!spike)
        {
            HandleSubThreshold();
            return;
        }

        // A real spike tick. Open a new window or extend the open one.
        _consecutiveSubThreshold = 0;
        if (!_windowOpen)
        {
            _openWindow = new SpikeWindow
            {
                StartTick = frame.TickIndex,
                EndTick = frame.TickIndex,
                WorstTick = frame.TickIndex,
                SnapshotTick = frame.TickIndex,
                WorstFrameMs = frameMs,
                BaselineMs = median,
                MadMs = mad,
                Warming = _ticksSeen <= WarmupTicks,
                PerModCatMs = new float[_modCount * PerModAttribution.CategoryCount],
                PerModCatBytes = _tracksAllocations
                    ? new float[_modCount * PerModAttribution.CategoryCount]
                    : null,
            };
            CaptureSnapshot(ref _openWindow, perTickRing);
            _windowOpen = true;
        }
        else
        {
            _openWindow.EndTick = frame.TickIndex;
            if (frameMs > _openWindow.WorstFrameMs)
            {
                _openWindow.WorstFrameMs = frameMs;
                _openWindow.WorstTick = frame.TickIndex;
                _openWindow.SnapshotTick = frame.TickIndex;
                CaptureSnapshot(ref _openWindow, perTickRing);
            }
        }
    }

    private void HandleSubThreshold()
    {
        if (!_windowOpen) return;
        _consecutiveSubThreshold++;
        if (_consecutiveSubThreshold >= RecoveryTicksToCloseWindow)
        {
            _windows.Push(in _openWindow);
            _windowOpen = false;
            _consecutiveSubThreshold = 0;
        }
    }

    /// <summary>
    /// Force-close any open window. Called at world unload so an in-progress
    /// spike doesn't dangle past the session boundary.
    /// </summary>
    public void Flush()
    {
        if (_windowOpen)
        {
            _windows.Push(in _openWindow);
            _windowOpen = false;
            _consecutiveSubThreshold = 0;
        }
    }

    private static void CaptureSnapshot(ref SpikeWindow window, PerTickAttributionRing ring)
    {
        // CopyLatestCategorySnapshot (not TryGetCategorySnapshot-by-tick): the
        // detector is called immediately after MetricCollector.EndTick pushes
        // the current tick's row into the ring, so the "latest" row IS the
        // tick we're reasoning about. Going through the by-game-tick lookup
        // would force us to keep the ring's internal counter and the game's
        // tickIndex in lockstep, which is fragile -- the early bug where all
        // SnapshotTick lookups returned zero came from comparing the ring's
        // monotonic counter against Main.GameUpdateCount directly.
        ring.CopyLatestCategorySnapshot(
            window.PerModCatMs.AsSpan(),
            window.PerModCatBytes != null ? window.PerModCatBytes.AsSpan() : Span<float>.Empty);
    }

    /// <summary>
    /// Bucketed median over the frame-time history. O(N) one-pass; bucket
    /// width = 0.5 ms. A frame above 256 ms clamps to the top bucket (its exact
    /// value doesn't change the median anyway). N is bounded by the history
    /// ring capacity (1800 by default).
    /// </summary>
    private double ExactMedian(RingBuffer<TickFrame> history)
    {
        int n = history.Count;
        if (n == 0) return 0d;
        Array.Clear(_histogramScratch, 0, _histogramScratch.Length);

        for (int i = 0; i < n; i++)
        {
            double ms = history[i].FrameTimeMs;
            int bucket = (int)(ms / HistogramBucketMs);
            if (bucket < 0) bucket = 0;
            if (bucket >= HistogramBuckets) bucket = HistogramBuckets - 1;
            _histogramScratch[bucket]++;
        }

        int target = n / 2;
        int running = 0;
        for (int b = 0; b < HistogramBuckets; b++)
        {
            running += _histogramScratch[b];
            if (running > target)
            {
                // Mid-bucket as the estimate. Half-bucket of error (0.25 ms)
                // is far below the spike-threshold granularity (5 ms floor),
                // so this is sufficient.
                return (b + 0.5d) * HistogramBucketMs;
            }
        }
        return (HistogramBuckets - 0.5d) * HistogramBucketMs;
    }

    /// <summary>
    /// Median Absolute Deviation against the given median, by the same bucketing
    /// trick. Used as a robust-scale sidecar metric on each spike record.
    /// </summary>
    private double ExactMad(RingBuffer<TickFrame> history, double median)
    {
        int n = history.Count;
        if (n == 0) return 0d;
        Array.Clear(_histogramScratch, 0, _histogramScratch.Length);

        for (int i = 0; i < n; i++)
        {
            double dev = history[i].FrameTimeMs - median;
            if (dev < 0d) dev = -dev;
            int bucket = (int)(dev / HistogramBucketMs);
            if (bucket < 0) bucket = 0;
            if (bucket >= HistogramBuckets) bucket = HistogramBuckets - 1;
            _histogramScratch[bucket]++;
        }

        int target = n / 2;
        int running = 0;
        for (int b = 0; b < HistogramBuckets; b++)
        {
            running += _histogramScratch[b];
            if (running > target)
            {
                return (b + 0.5d) * HistogramBucketMs;
            }
        }
        return (HistogramBuckets - 0.5d) * HistogramBucketMs;
    }

    /// <summary>
    /// IReadOnlyList view over the underlying ring buffer in chronological order
    /// (oldest first). Avoids exposing the mutable RingBuffer reference.
    /// </summary>
    private sealed class SpikeWindowsView : IReadOnlyList<SpikeWindow>
    {
        private readonly RingBuffer<SpikeWindow> _source;
        public SpikeWindowsView(RingBuffer<SpikeWindow> source) => _source = source;
        public int Count => _source.Count;
        public SpikeWindow this[int index] => _source[index];

        public IEnumerator<SpikeWindow> GetEnumerator()
        {
            int n = _source.Count;
            for (int i = 0; i < n; i++) yield return _source[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
