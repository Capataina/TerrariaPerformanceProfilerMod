#nullable enable

using System.Collections.Generic;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// HOT_HOOK_DOMINANCE — one hook accounts for &gt;= 60 % of its owning mod's
/// session cost. Reads <c>PerHookAverageMs</c> and folds it up against
/// <c>PerModCategoryAverageMs</c> per mod; emits a record when the share
/// crosses the floor.
///
/// <para>
/// This is the in-session, context-agnostic form: no Events-tab bucket, so
/// the baseline is the mod's total session cost rather than a per-context
/// total. When the Events tab lands, the same logic moves up to the
/// (mod, context) plane — see plan §4.3 and §11 step 8.
/// </para>
/// </summary>
public sealed class HotHookDominanceDetector : IInsightDetector
{
    /// <summary>Minimum share of the mod's cost a hook must own to fire (plan §4.3).</summary>
    public const double ShareFloor = 0.60;

    /// <summary>
    /// The mod itself must spend at least this many ms/tick on average for the share to
    /// be meaningful. A hook owning 100% of a mod that costs 0.19 ms/tick is "100% of
    /// nearly nothing" — noise, not a frame-cost lever. The floor is set so a dominant
    /// hook is only surfaced when the mod is a non-trivial slice of a 16.6 ms frame.
    /// </summary>
    public const double ModTotalFloorMs = 0.5;

    public PatternKey Pattern => PatternKey.HotHookDominance;
    public Audience DefaultAudience => Audience.Modder;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector) =>
        collector.History.Count > 0 && PerModAttribution.Hooks.Count > 0;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        HotHookDominanceCore.Evaluate(
            collector.PerModCategoryAverageMs,
            collector.PerHookAverageMs,
            PerModAttribution.Hooks,
            HookInterceptor.ProfiledModNames.Length,
            PerModAttribution.CategoryCount,
            ShareFloor,
            ModTotalFloorMs,
            collector.History.Count,
            nowTick,
            DefaultAudience,
            emit);
    }
}
