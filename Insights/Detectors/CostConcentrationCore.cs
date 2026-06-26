#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Insights.Shared;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// The result of a cost-concentration evaluation: which mods form the lever set,
/// how big the contributing and loaded rosters are, and the named contributors.
/// A value type carrying everything <see cref="CostConcentrationDetector"/> needs
/// to build its <see cref="Insight"/>.
/// </summary>
internal readonly struct ConcentrationResult
{
    /// <summary>How many mods the lever set is (the smallest set carrying the threshold share).</summary>
    public readonly int LeverCount;
    /// <summary>Mods with measurable cost this session (cost &gt; 0). The "active" denominator.</summary>
    public readonly int ContributingCount;
    /// <summary>Every mod the roster pass iterated, including idle / zero-cost ones. The "loaded" denominator.</summary>
    public readonly int LoadedCount;
    /// <summary>The lever set's share of the total measured cost, a fraction in [0,1].</summary>
    public readonly double Share;
    /// <summary>The lever set's summed cost in ms/tick.</summary>
    public readonly double LeverCostMs;
    /// <summary>The total measured per-mod cost in ms/tick.</summary>
    public readonly double TotalMs;
    /// <summary>The lever mods, ordered by cost descending, each with its value and share of the total.</summary>
    public readonly List<InsightContributor> Contributors;

    public ConcentrationResult(int leverCount, int contributingCount, int loadedCount,
        double share, double leverCostMs, double totalMs, List<InsightContributor> contributors)
    {
        LeverCount = leverCount;
        ContributingCount = contributingCount;
        LoadedCount = loadedCount;
        Share = share;
        LeverCostMs = leverCostMs;
        TotalMs = totalMs;
        Contributors = contributors;
    }
}

/// <summary>
/// The cost-concentration math, separated from <see cref="CostConcentrationDetector.Evaluate"/>
/// so it is unit-testable against a synthetic per-mod/category array without a
/// <c>MetricCollector</c> or the live <c>HookInterceptor</c> roster. Pure over its
/// inputs: ranks the per-mod costs, finds the smallest lever set reaching the
/// threshold share, and names the lever mods. Returns null when there is no notable
/// concentration to surface (cost spread evenly, too few contributors, or the lever
/// would be most of the roster rather than a handful).
/// </summary>
internal static class CostConcentrationCore
{
    /// <summary>
    /// Evaluates concentration over <paramref name="perModCategoryAvgMs"/> (row-major
    /// <c>[modId * catCount + categoryId]</c>). <paramref name="modCount"/> is the full
    /// loaded roster the caller iterates; it becomes <see cref="ConcentrationResult.LoadedCount"/>
    /// so the renderer can name the idle remainder. Returns null when nothing is worth surfacing.
    /// </summary>
    public static ConcentrationResult? Compute(IReadOnlyList<double> perModCategoryAvgMs,
        int modCount, int catCount, double concentrationThreshold, int maxLeverMods)
    {
        // (modId, cost) pairs for every contributing mod, so the lever set keeps its
        // identity through the sort and can be named (not just counted).
        var contributing = new List<(int ModId, double Cost)>(modCount);
        double total = 0d;
        for (int m = 0; m < modCount; m++)
        {
            double c = ModMetrics.SumModCategories(perModCategoryAvgMs, m, catCount);
            if (c > 0d) { contributing.Add((m, c)); total += c; }
        }
        // Need enough contributing mods for "concentration" to mean anything.
        if (contributing.Count <= maxLeverMods || total <= 0d) return null;

        contributing.Sort((a, b) => b.Cost.CompareTo(a.Cost)); // descending by cost

        // ParetoCount reads a plain descending value list; project the sorted costs.
        var sortedCosts = new List<double>(contributing.Count);
        for (int i = 0; i < contributing.Count; i++) sortedCosts.Add(contributing[i].Cost);
        int leverCount = Shares.ParetoCount(sortedCosts, total, concentrationThreshold);
        if (leverCount <= 0 || leverCount > maxLeverMods) return null;

        // The lever set must be a genuine minority of the contributing roster.
        if (leverCount * 4 >= contributing.Count) return null;

        var contributors = new List<InsightContributor>(leverCount);
        double leverCost = 0d;
        for (int i = 0; i < leverCount; i++)
        {
            (int modId, double cost) = contributing[i];
            leverCost += cost;
            contributors.Add(new InsightContributor(SubjectRef.ForMod(modId), cost, Shares.SafeShare(cost, total)));
        }

        return new ConcentrationResult(
            leverCount: leverCount,
            contributingCount: contributing.Count,
            loadedCount: modCount,
            share: leverCost / total,
            leverCostMs: leverCost,
            totalMs: total,
            contributors: contributors);
    }
}
