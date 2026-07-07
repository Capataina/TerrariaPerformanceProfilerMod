#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Aggregators;

/// <summary>One minute of session frame-time data, plus the worst single frame in that minute.</summary>
public readonly struct HeatmapBucket
{
    public readonly long StartUnixMs;
    public readonly int Ticks;
    public readonly double AvgMs;
    public readonly double WorstMs;

    public HeatmapBucket(long startUnixMs, int ticks, double avgMs, double worstMs)
    {
        StartUnixMs = startUnixMs;
        Ticks = ticks;
        AvgMs = avgMs;
        WorstMs = worstMs;
    }
}

/// <summary>
/// The pure per-minute bucket fold behind <see cref="HeatmapAggregator"/>,
/// lifted into its own Terraria-free file (the house pure-core pattern) so the
/// bucket maths is unit-testable against synthetic tick streams.
///
/// <para>
/// Reads <see cref="TickFrame.RealFrameTimeMs"/> — the honest whole-loop
/// period (2026-07-07 honesty pass). These buckets feed the minute-by-minute
/// panel and the session gradient ribbon; they must show what the player
/// FELT, so a draw-bound slow-motion minute has to read hot even though its
/// update-window cost looked tiny.
/// </para>
/// </summary>
internal static class HeatmapFold
{
    /// <summary>
    /// Fold the rolling history into per-bucket (start, ticks, avg, worst)
    /// rows. Timestamps are approximated backwards from <paramref name="nowUnixMs"/>
    /// at the nominal 60 UPS tick rate, matching the aggregator's original
    /// behaviour (the ring carries no absolute per-tick wall clock).
    /// </summary>
    public static List<HeatmapBucket> Fold(RingBuffer<TickFrame> history, long nowUnixMs, long bucketMs)
    {
        var result = new List<HeatmapBucket>();
        long bucketStart = -1L;
        int curTicks = 0;
        double curTotalMs = 0d;
        double curWorstMs = 0d;
        for (int i = 0; i < history.Count; i++)
        {
            var tf = history[i];
            long approxUnix = nowUnixMs - (history.Count - 1 - i) * 1000 / 60;
            long bs = (approxUnix / bucketMs) * bucketMs;
            if (bs != bucketStart)
            {
                if (bucketStart >= 0L)
                {
                    double avg = curTicks > 0 ? curTotalMs / curTicks : 0d;
                    result.Add(new HeatmapBucket(bucketStart, curTicks, avg, curWorstMs));
                }
                bucketStart = bs;
                curTicks = 0;
                curTotalMs = 0d;
                curWorstMs = 0d;
            }
            curTicks++;
            curTotalMs += tf.RealFrameTimeMs;
            if (tf.RealFrameTimeMs > curWorstMs) curWorstMs = tf.RealFrameTimeMs;
        }
        if (bucketStart >= 0L)
        {
            double avg = curTicks > 0 ? curTotalMs / curTicks : 0d;
            result.Add(new HeatmapBucket(bucketStart, curTicks, avg, curWorstMs));
        }
        return result;
    }
}
