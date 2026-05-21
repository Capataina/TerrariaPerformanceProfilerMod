#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Profiling.Events;

/// <summary>
/// Running summary of one bucket's frame-time samples: tick count, sum / sum
/// of squares for mean and std-dev, peak, and a spike counter (frames where
/// the tick exceeded twice the session running mean).
///
/// <para>
/// Mutated in place by <see cref="BucketAggregator"/>; no per-tick allocation
/// (lookup goes through <c>Dictionary&lt;int, BucketStats&gt;</c> by integer
/// key). See plan §8.1.
/// </para>
/// </summary>
internal sealed class BucketStats
{
    public long Ticks;
    public double SumMs;
    public double SumSqMs;
    public double PeakMs;
    public int SpikeCount;
    public long FirstSeenTick;
    public long LastSeenTick;

    /// <summary>Adds one tick worth of frame time. Updates running stats.</summary>
    public void Add(long tickIndex, double frameMs, bool isSpike)
    {
        if (Ticks == 0) FirstSeenTick = tickIndex;
        LastSeenTick = tickIndex;
        Ticks++;
        SumMs += frameMs;
        SumSqMs += frameMs * frameMs;
        if (frameMs > PeakMs) PeakMs = frameMs;
        if (isSpike) SpikeCount++;
    }

    public double AvgMs => Ticks == 0 ? 0d : SumMs / Ticks;

    public double StdDevMs
    {
        get
        {
            if (Ticks < 2) return 0d;
            double mean = AvgMs;
            double var = (SumSqMs / Ticks) - (mean * mean);
            return var <= 0d ? 0d : System.Math.Sqrt(var);
        }
    }

    /// <summary>Wall-clock seconds at Terraria's 60 ticks per second; not paused-game-aware (game-pause stops the tick counter anyway).</summary>
    public double DwellSec => Ticks / 60d;
}
