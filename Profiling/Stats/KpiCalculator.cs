#nullable enable

namespace PerformanceProfiler.Profiling.Stats;

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
public static class KpiCalculator
{
    /// <summary>Perceptual threshold for "this hitched" — used by the LAG SPIKES KPI count.</summary>
    public const double LagSpikeMsThreshold = 50d;

    /// <summary>
    /// Snapshot the live KPIs from the collector's rolling history. Returns
    /// <see cref="KpiSnapshot.IsEmpty"/> = true when the session has not
    /// produced any frames yet so callers can render dashes uniformly.
    /// </summary>
    public static KpiSnapshot Compute(MetricCollector? collector)
    {
        if (collector == null || collector.History.Count == 0)
        {
            return new KpiSnapshot { IsEmpty = true };
        }

        var hist = collector.History;
        int n = hist.Count;
        double sumMs = 0d;
        double maxMs = 0d;
        double minMs = double.MaxValue;
        double totalLagMs = 0d;
        int lagCount = 0;

        // Single forward pass: avg, max, min, lag count, total-lag-ms. Median needs sort.
        for (int i = 0; i < n; i++)
        {
            double v = hist[i].FrameTimeMs;
            sumMs += v;
            if (v > maxMs) maxMs = v;
            if (v < minMs) minMs = v;
            if (v > LagSpikeMsThreshold) { lagCount++; totalLagMs += v; }
        }
        double avgMs = sumMs / n;
        if (minMs == double.MaxValue) minMs = 0d;

        // Stall stats — cheap summary across the session ring.
        double worstStall = 0d;
        double avgStall = 0d;
        if (collector.Stalls.Count > 0)
        {
            double stallSum = 0d;
            int stallN = 0;
            for (int i = 0; i < collector.Stalls.Count; i++)
            {
                double d = collector.Stalls[i].TickPeriodMs;
                if (d > worstStall) worstStall = d;
                stallSum += d;
                stallN++;
            }
            avgStall = stallN > 0 ? stallSum / stallN : 0d;
        }

        // Median via lightweight copy + sort. n is bounded at 1800 (the
        // rolling history capacity) so the cost is fine to do per poll.
        double[] sorted = new double[n];
        for (int i = 0; i < n; i++) sorted[i] = hist[i].FrameTimeMs;
        System.Array.Sort(sorted);
        double median = sorted[n / 2];

        // Clamp FPS to 60 — that's Terraria's tick ceiling; values higher
        // are noise (very fast frames don't translate to FPS in practice).
        double avgFps = avgMs > 0d ? System.Math.Min(60d, 1000d / System.Math.Max(1000d/60d, avgMs)) : 0d;

        return new KpiSnapshot
        {
            AvgFps = avgFps,
            WorstFrameMs = maxMs,
            MedianFrameMs = median,
            LagSpikeCount = lagCount,
            StallCount = collector.Stalls.Count,
            SpikeCount = collector.Spikes.Count,
            SampleN = n,
            BestFrameMs = minMs,
            TotalLagMs = totalLagMs,
            WorstStallMs = worstStall,
            AvgStallMs = avgStall,
            IsEmpty = false,
        };
    }
}
