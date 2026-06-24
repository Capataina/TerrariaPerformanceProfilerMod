#nullable enable

using System;
using System.Collections.Generic;

namespace PerformanceProfiler.Insights.Shared;

/// <summary>
/// Share-of-total and top-N primitives. The "fraction of the whole" and
/// "rank then keep the top K" idioms recurred at 8+ and 8 sites respectively
/// across the interpretation surfaces (the duplication census in
/// <c>context/plans/insights-engine.md</c>); this is their single home.
///
/// <para>
/// The guard matters: a share is <c>0</c> when the total is non-positive, never
/// a divide-by-zero or a NaN that would poison a downstream ratio. Keeping that
/// guard in one place is the point.
/// </para>
/// </summary>
public static class Shares
{
    /// <summary>Fraction <c>value / total</c> in <c>[0,∞)</c>, or <c>0</c> when total ≤ 0.</summary>
    public static double SafeShare(double value, double total) =>
        total > 0d ? value / total : 0d;

    /// <summary>
    /// Long-input overload of <see cref="SafeShare(double,double)"/>; casts the
    /// numerator to double before the divide, matching the inline form
    /// <c>(double)value / total</c> at the usage-share call sites.
    /// </summary>
    public static double SafeShare(long value, long total) =>
        total > 0L ? (double)value / total : 0d;

    /// <summary>Percentage <c>value / total * 100</c>, or <c>0</c> when total ≤ 0.</summary>
    public static double Percentage(double value, double total) =>
        total > 0d ? value * 100d / total : 0d;

    /// <summary>
    /// Drops everything past index <paramref name="max"/> from an
    /// already-ordered list. The mechanical half of the "sort then keep top-N"
    /// idiom; no-op when the list is already within bound.
    /// </summary>
    public static void Truncate<T>(List<T> list, int max)
    {
        if (max < 0) max = 0;
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
    }

    /// <summary>
    /// Sorts <paramref name="list"/> by <paramref name="comparison"/> (the
    /// caller's order, ties and all) and truncates to the top
    /// <paramref name="n"/>. The full "rank then keep K" idiom in one call.
    /// </summary>
    public static void TopN<T>(List<T> list, int n, Comparison<T> comparison)
    {
        list.Sort(comparison);
        Truncate(list, n);
    }

    /// <summary>
    /// How many of the largest contributors are needed to reach
    /// <paramref name="fraction"/> of <paramref name="total"/>, given
    /// <paramref name="sortedDesc"/> already in descending order — the Pareto /
    /// concentration count behind "N mods are X% of cost". Returns 0 when the
    /// total is non-positive, and the full length if the fraction is never reached.
    /// </summary>
    public static int ParetoCount(IReadOnlyList<double> sortedDesc, double total, double fraction)
    {
        if (total <= 0d) return 0;
        double target = total * fraction;
        double acc = 0d;
        for (int i = 0; i < sortedDesc.Count; i++)
        {
            acc += sortedDesc[i];
            if (acc >= target) return i + 1;
        }
        return sortedDesc.Count;
    }
}
