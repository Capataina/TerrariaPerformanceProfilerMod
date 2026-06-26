#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Data.Aggregators;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// The hot-hook-dominance fold, separated from <see cref="HotHookDominanceDetector.Evaluate"/>
/// so the share floor and the absolute-cost floor are unit-testable against synthetic
/// per-mod/per-hook arrays without a <c>MetricCollector</c> or the live <c>HookInterceptor</c>
/// roster. Pure over its inputs: for each mod it sums the category cost, finds the mod's
/// hottest hook, and emits when that hook owns at least <paramref name="shareFloor"/> of a
/// mod that itself costs at least <paramref name="modTotalFloorMs"/> ms/tick.
/// </summary>
internal static class HotHookDominanceCore
{
    /// <summary>
    /// Appends a HOT_HOOK_DOMINANCE record for every mod whose top hook clears both floors.
    /// <paramref name="categoryMs"/> is row-major <c>[modId * catCount + categoryId]</c>;
    /// <paramref name="hookMs"/> is indexed by hookId and aligned with <paramref name="hooks"/>.
    /// </summary>
    public static void Evaluate(
        IReadOnlyList<double> categoryMs,
        IReadOnlyList<double> hookMs,
        IReadOnlyList<HookDescriptor> hooks,
        int modCount, int catCount,
        double shareFloor, double modTotalFloorMs,
        int historyCount, long nowTick,
        Audience audience,
        List<Insight> emit)
    {
        for (int modId = 0; modId < modCount; modId++)
        {
            double modTotal = 0d;
            for (int c = 0; c < catCount; c++)
            {
                int cell = modId * catCount + c;
                if (cell < categoryMs.Count) modTotal += categoryMs[cell];
            }
            if (modTotal < modTotalFloorMs) continue;

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
            if (share < shareFloor) continue;

            emit.Add(new Insight
            {
                Pattern = PatternKey.HotHookDominance,
                Subject = SubjectRef.ForHook(modId, topHookId),
                Magnitude = new Magnitude
                {
                    BaselineMs = modTotal,
                    ObservedMs = topHookMs,
                    RatioOrDelta = share,
                    AllocBytes = 0,
                    Count = historyCount,
                },
                Evidence = new Evidence
                {
                    SampleN = historyCount,
                    BaselineN = historyCount,
                    PValue = 1d,
                    EffectSize = share,
                    PValueAdjusted = 1d,
                    FirstTickIndex = 0,
                    LastTickIndex = nowTick,
                    Baseline = BaselineKind.SessionMean,
                },
                Confidence = Confidence.Preliminary,
                Audience = audience,
            });
        }
    }
}
