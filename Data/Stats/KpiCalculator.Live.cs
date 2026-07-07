#nullable enable

using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// The live half of <see cref="KpiCalculator"/>: the collector-facing
/// overload. Split from the pure fold (KpiCalculator.cs, which the test
/// project links) because <see cref="MetricCollector"/> is
/// tModLoader-transitive and cannot compile in the runtime-free test harness.
/// </summary>
public static partial class KpiCalculator
{
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
        return ComputeCore(
            collector.History,
            collector.Stalls,
            collector.Spikes.Count,
            collector.RenderFps,
            collector.RealtimeSpeedNow,
            collector.TimeBelowThresholdMs,
            collector.DeficitMsPerSecond);
    }
}
