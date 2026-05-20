#nullable enable

using System.Collections.Generic;

namespace PerformanceProfiler.Profiling.Insights.Detectors;

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

    /// <summary>The mod itself must spend at least this many ms/tick on average for the share to be meaningful.</summary>
    public const double ModTotalFloorMs = 0.05;

    public PatternKey Pattern => PatternKey.HotHookDominance;
    public Audience DefaultAudience => Audience.Modder;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector) =>
        collector.History.Count > 0 && PerModAttribution.Hooks.Count > 0;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<InsightRecord> emit)
    {
        IReadOnlyList<double> hookMs = collector.PerHookAverageMs;
        IReadOnlyList<double> categoryMs = collector.PerModCategoryAverageMs;
        IReadOnlyList<HookDescriptor> hooks = PerModAttribution.Hooks;
        string[] modNames = HookInterceptor.ProfiledModNames;
        int catCount = PerModAttribution.CategoryCount;

        for (int modId = 0; modId < modNames.Length; modId++)
        {
            double modTotal = 0d;
            for (int c = 0; c < catCount; c++)
            {
                int cell = modId * catCount + c;
                if (cell < categoryMs.Count) modTotal += categoryMs[cell];
            }
            if (modTotal < ModTotalFloorMs) continue;

            int topHookId = -1;
            double topHookMs = 0d;
            int hookN = hookMs.Count < hooks.Count ? hookMs.Count : hooks.Count;
            for (int h = 0; h < hookN; h++)
            {
                if (hooks[h].ModId != modId) continue;
                double ms = hookMs[h];
                if (ms > topHookMs) { topHookMs = ms; topHookId = h; }
            }
            if (topHookId < 0) continue;

            double share = topHookMs / modTotal;
            if (share < ShareFloor) continue;

            emit.Add(new InsightRecord
            {
                Pattern = Pattern,
                Subject = SubjectRef.ForHook(modId, topHookId),
                Magnitude = new Magnitude
                {
                    BaselineMs = modTotal,
                    ObservedMs = topHookMs,
                    RatioOrDelta = share,
                    AllocBytes = 0,
                    Count = collector.History.Count,
                },
                Evidence = new Evidence
                {
                    SampleN = collector.History.Count,
                    BaselineN = collector.History.Count,
                    PValue = 1d,
                    EffectSize = share,
                    PValueAdjusted = 1d,
                    FirstTickIndex = 0,
                    LastTickIndex = nowTick,
                    Baseline = BaselineKind.SessionMean,
                },
                Confidence = Confidence.Preliminary,
                Audience = DefaultAudience,
            });
        }
    }
}
