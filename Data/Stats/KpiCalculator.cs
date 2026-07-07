#nullable enable

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// Computes a <see cref="KpiSnapshot"/> from a live <see cref="MetricCollector"/>.
/// Pure logic; no allocation per call once the helper arrays warm up.
///
/// <para>
/// Lives in <c>Profiling/Stats/</c> alongside the other rolled-up
/// summaries (heatmap, events feed) so all "facts about the session"
/// computations sit in one folder. The dashboard router reads
/// <see cref="Compute"/> on every <c>/api/now</c> request.
/// </para>
/// </summary>
public static partial class KpiCalculator
{
    /// <summary>Perceptual threshold for "this hitched" — used by the LAG SPIKES KPI count.</summary>
    public const double LagSpikeMsThreshold = 50d;

    // ThreadStatic scratch buffer for the median sort. Avoids a fresh
    // double[1800] (~14 KB) allocation on every /api/now poll (~1.5 Hz);
    // the KPI endpoint was the dominant allocator before this cache.
    [System.ThreadStatic] private static double[]? _medianScratch;

    // The MetricCollector-facing Compute(...) overload lives in
    // KpiCalculator.Live.cs — the collector cannot link into the runtime-free
    // test project, and this file is linked so the scenario engine can drive
    // ComputeCore directly (e2e Ring 1).

    /// <summary>
    /// The pure KPI fold, decomposed from the collector so the semantics are
    /// unit-testable against synthetic sessions (2026-07-07 honesty pass; the
    /// e2e scenario engine drives this directly — MetricCollector itself
    /// cannot link into the runtime-free test project).
    /// </summary>
    public static KpiSnapshot ComputeCore(
        RingBuffer<TickFrame> hist,
        System.Collections.Generic.IReadOnlyList<Detectors.StallEvent> stalls,
        int spikeCount,
        double renderFps,
        double realtimeSpeed,
        double timeBelowThresholdMs,
        double deficitMsPerSecond)
    {
        int n = hist.Count;
        if (n == 0)
        {
            return new KpiSnapshot { IsEmpty = true };
        }

        double sumMs = 0d;
        double maxMs = 0d;
        double minMs = double.MaxValue;
        double totalLagMs = 0d;
        int lagCount = 0;

        // Single forward pass: avg, max, min, lag count, total-lag-ms. Median needs sort.
        for (int i = 0; i < n; i++)
        {
            double v = hist[i].RealFrameTimeMs;
            sumMs += v;
            if (v > maxMs) maxMs = v;
            if (v < minMs) minMs = v;
            if (v > LagSpikeMsThreshold) { lagCount++; totalLagMs += v; }
        }
        double avgMs = sumMs / n;
        if (minMs == double.MaxValue) minMs = 0d;

        // Stall stats — cause-aware (X3, 2026-07-07): ProcessSuspended and
        // WorldLoad gaps are wall time in which the game was not running
        // (alt-tab, OS sleep, loading), so they are EXCLUDED from the stall
        // headline and reported separately as pausedMs — a 122s alt-tab must
        // never read as the session's "biggest stall". Real in-app causes
        // (freeze / GC / UI-blocking / long frame / unknown) keep the headline.
        double worstStall = 0d;
        double stallSum = 0d;
        int realStallCount = 0;
        double pausedMs = 0d;
        int pauseCount = 0;
        for (int i = 0; i < stalls.Count; i++)
        {
            var ev = stalls[i];
            bool isPause = ev.Cause == Detectors.StallCause.ProcessSuspended
                        || ev.Cause == Detectors.StallCause.WorldLoad;
            if (isPause)
            {
                pausedMs += ev.TickPeriodMs;
                pauseCount++;
                continue;
            }
            double d = ev.TickPeriodMs;
            if (d > worstStall) worstStall = d;
            stallSum += d;
            realStallCount++;
        }
        double avgStall = realStallCount > 0 ? stallSum / realStallCount : 0d;

        // Median via copy + sort, using a ThreadStatic scratch buffer to
        // avoid per-poll allocation. n is bounded at 1800 (rolling
        // history capacity) so the buffer never grows beyond that.
        double[] sorted = _medianScratch ??= new double[n];
        if (sorted.Length < n) sorted = _medianScratch = new double[n];
        for (int i = 0; i < n; i++) sorted[i] = hist[i].RealFrameTimeMs;
        System.Array.Sort(sorted, 0, n);
        double median = sorted[n / 2];

        // Honest FPS from the real inter-frame period: 1000 / mean real-frame-ms.
        // No 60-clamp — the old clamp existed because this read compute time
        // (FrameTimeMs), which produced absurd "300 fps" figures it had to cap.
        // RealFrameTimeMs is the actual game-loop cadence, so it sits at ~60 on a
        // healthy tick and drops below 60 during genuine slow-motion; clamping
        // that would re-hide the very slow-down this metric exists to show.
        double avgFps = avgMs > 0d ? 1000d / avgMs : 0d;

        return new KpiSnapshot
        {
            AvgFps = avgFps,
            RenderFps = renderFps,
            RealtimeSpeed = realtimeSpeed,
            TimeBelowThresholdMs = timeBelowThresholdMs,
            DeficitMsPerSecond = deficitMsPerSecond,
            WorstFrameMs = maxMs,
            MedianFrameMs = median,
            LagSpikeCount = lagCount,
            StallCount = realStallCount,
            SpikeCount = spikeCount,
            SampleN = n,
            BestFrameMs = minMs,
            TotalLagMs = totalLagMs,
            WorstStallMs = worstStall,
            AvgStallMs = avgStall,
            PausedMs = pausedMs,
            PauseCount = pauseCount,
            IsEmpty = false,
        };
    }
}
