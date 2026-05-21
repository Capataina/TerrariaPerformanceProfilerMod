#nullable enable

using System;
using System.Collections.Generic;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data.Detectors;

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
/// already keeps, reading its baseline from the shared <see cref="Baseline"/>
/// service. Pure logic; no tModLoader dependency. Unit-testable.
///
/// <para>
/// <b>Pure-relative threshold.</b> A spike is a frame whose duration is at
/// least <see cref="ThresholdMultiplier"/> times the user's session median.
/// No absolute millisecond floor. A 120 fps player whose baseline is 8 ms
/// gets flagged at ≥16 ms; a 25 fps player whose baseline is 40 ms gets
/// flagged at ≥80 ms. Both are real outliers in their respective sessions;
/// neither is hidden by a one-size-fits-no-one floor like the previous
/// 5 ms constant.
/// </para>
///
/// <para>
/// <b>Why median, not mean.</b> Tick-time distributions in modded Terraria are
/// heavy-tailed and bimodal — quiet ticks at 1-2 ms, occasional GC pauses at
/// 20-40 ms. A mean-based baseline walks toward the pauses and the threshold
/// silently inflates until real spikes hide underneath. Median ignores the
/// top half by construction.
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

    /// <summary>Default relative threshold (frame must beat this multiple of the baseline median).</summary>
    public const double DefaultThresholdMultiplier = 2.0;

    private readonly RingBuffer<SpikeWindow> _windows = new RingBuffer<SpikeWindow>(50);
    private readonly SpikeWindowsView _windowsView;
    private readonly int _modCount;
    private readonly bool _tracksAllocations;

    private int _ticksSeen;
    private SpikeWindow _openWindow;
    private bool _windowOpen;
    private int _consecutiveSubThreshold;

    public SpikeDetector(int modCount, bool tracksAllocations)
    {
        _modCount = modCount;
        _tracksAllocations = tracksAllocations;
        _windowsView = new SpikeWindowsView(_windows);
    }

    /// <summary>Relative threshold; configurable later via ModConfig.</summary>
    public double ThresholdMultiplier { get; set; } = DefaultThresholdMultiplier;

    /// <summary>
    /// The captured spike windows in chronological order, oldest first. The
    /// returned wrapper is constructed once at detector construction so reads
    /// from the overlay and session writer are allocation-free.
    /// </summary>
    public IReadOnlyList<SpikeWindow> Windows => _windowsView;

    /// <summary>Number of captured spike windows currently retained.</summary>
    public int Count => _windows.Count;

    /// <summary>
    /// Drive the detector with the just-committed tick. Reads its baseline
    /// from the shared <see cref="Baseline"/> snapshot the collector recomputed
    /// at this same EndTick, and the per-tick ring for the worst-tick snapshot.
    /// </summary>
    public void OnTick(TickFrame frame, Baseline baseline, PerTickAttributionRing perTickRing)
    {
        _ticksSeen++;

        // During the calibration window the median is unreliable. Surface
        // candidate spikes only after Baseline has enough samples to be trusted
        // — the warmup also matches SpikeDetector's "warming" badge so the UI
        // can hedge appropriately.
        if (!baseline.IsCalibrated)
        {
            HandleSubThreshold();
            return;
        }

        double frameMs = frame.FrameTimeMs;
        double median = baseline.FrameMsMedian;
        double mad = baseline.FrameMsMad;

        // Pure-relative trigger. No absolute floor — a "5 ms minimum" hides
        // real spikes on 120 fps players (8 ms baseline → 16 ms threshold;
        // anything above 5 ms used to qualify even when nothing was wrong)
        // and floods 25 fps players with bogus spikes (40 ms baseline → 80 ms
        // threshold; ordinary 50 ms jitter used to qualify).
        bool spike = frameMs >= median * ThresholdMultiplier;

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
