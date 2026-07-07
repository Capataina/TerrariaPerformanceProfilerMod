#nullable enable

using System.Collections.Generic;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>One draw-bound mod finding. See <see cref="DrawBoundModCore"/>.</summary>
internal readonly struct DrawBoundModResult
{
    public readonly int ModId;
    public readonly double TotalMs;
    public readonly double DrawMs;
    public readonly double DrawShare;

    public DrawBoundModResult(int modId, double totalMs, double drawMs, double drawShare)
    {
        ModId = modId;
        TotalMs = totalMs;
        DrawMs = drawMs;
        DrawShare = drawShare;
    }
}

/// <summary>
/// DRAW_BOUND_MOD — Family E (structure across signals), atlas S01. "X is
/// draw-bound: 72% of its cost sits in the draw phase — its weight shows in
/// render cadence, not game speed." The sentence that resolves the class of
/// mystery this project spent 2026-07-07 diagnosing: a draw-bound stack reads
/// healthy in update-window terms while the game visibly struggles.
///
/// <para>
/// Pure over the two smoothed grids (the house core pattern; unit-tested
/// against synthetic arrays). Fire conditions: the mod must carry a real cost
/// (≥ <see cref="MinTotalMs"/> — sub-noise mods stay silent) AND a dominant
/// draw share (≥ <see cref="DrawShareThreshold"/>). Descriptive, never
/// normative: the finding names where the cost sits, not what to do about it.
/// </para>
/// </summary>
internal static class DrawBoundModCore
{
    /// <summary>Minimum smoothed total ms/t before a mod's phase shape is worth a finding.</summary>
    public const double MinTotalMs = 1.0d;

    /// <summary>Draw share at which a mod counts as draw-bound.</summary>
    public const double DrawShareThreshold = 0.60d;

    /// <summary>Cap on findings per pass — the shape is interesting for the leaders, not the whole roster.</summary>
    public const int MaxFindings = 3;

    /// <summary>
    /// Scan the per-mod grids (row-major [modId * catCount + categoryId];
    /// <paramref name="totalMs"/> carries the full cost, <paramref name="drawMs"/>
    /// the draw-phase share) and return the draw-bound leaders, costliest
    /// first. Empty when the lanes are disabled (drawMs null) or nothing fires.
    /// </summary>
    public static List<DrawBoundModResult> Compute(
        IReadOnlyList<double>? totalMs,
        IReadOnlyList<double>? drawMs,
        int modCount,
        int catCount)
    {
        var results = new List<DrawBoundModResult>();
        if (totalMs == null || drawMs == null || modCount <= 0 || catCount <= 0)
        {
            return results;
        }

        for (int m = 0; m < modCount; m++)
        {
            double total = 0d, draw = 0d;
            int baseIdx = m * catCount;
            for (int c = 0; c < catCount; c++)
            {
                int idx = baseIdx + c;
                if (idx < totalMs.Count) total += totalMs[idx];
                if (idx < drawMs.Count) draw += drawMs[idx];
            }
            if (total < MinTotalMs) continue;
            double share = draw / total;
            if (share < DrawShareThreshold) continue;
            results.Add(new DrawBoundModResult(m, total, draw, share));
        }

        // Costliest first; cap. Insertion sort over a ≤roster-sized list at
        // ≤1 Hz worker cadence — no allocation concerns here.
        results.Sort((a, b) => b.TotalMs.CompareTo(a.TotalMs));
        if (results.Count > MaxFindings)
        {
            results.RemoveRange(MaxFindings, results.Count - MaxFindings);
        }
        return results;
    }
}
