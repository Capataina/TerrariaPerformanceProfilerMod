#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Data.Stats;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>Result of the sustained-slowness evaluation. See <see cref="SustainedSlownessCore"/>.</summary>
internal readonly struct SustainedSlownessResult
{
    /// <summary>Real-time speed fraction while slowed (0..1).</summary>
    public readonly double Speed;
    /// <summary>How long the game has been continuously below threshold, ms.</summary>
    public readonly double ConsecutiveSlowMs;
    /// <summary>Session-cumulative ms below threshold.</summary>
    public readonly double TimeBelowMs;
    /// <summary>Game-time ms lost per wall second at the current pace.</summary>
    public readonly double DeficitMsPerSecond;
    /// <summary>Top per-mod costs while slowed, descending; share is of the measured per-mod total.</summary>
    public readonly List<InsightContributor> Contributors;

    public SustainedSlownessResult(double speed, double consecutiveSlowMs, double timeBelowMs,
        double deficitMsPerSecond, List<InsightContributor> contributors)
    {
        Speed = speed;
        ConsecutiveSlowMs = consecutiveSlowMs;
        TimeBelowMs = timeBelowMs;
        DeficitMsPerSecond = deficitMsPerSecond;
        Contributors = contributors;
    }
}

/// <summary>
/// SUSTAINED_SLOWNESS — the level-detector counterpart to the variance
/// detectors (the X2 fix, 2026-07-07 honesty pass). A game running uniformly
/// at 33 ms/frame produces zero spike or stall events; this pattern says the
/// thing the player already feels: "the game has been running at 55% speed
/// for four minutes".
///
/// <para>
/// Pure over its inputs (the house core pattern) so the fire conditions are
/// unit-testable: fires only when the smoothed speed sits below
/// <see cref="RealtimeSpeed.SlowThreshold"/> and has done so continuously for
/// at least <see cref="RealtimeSpeed.SustainedFireMs"/> — a single long frame
/// or a brief dip never fires it. Contributor naming is descriptive: the
/// costliest mods *while slowed*, never "the cause" (Invariant 3 — with the
/// draw phase unattributed until the loop-anatomy slot lands, claiming cause
/// would overreach the evidence).
/// </para>
/// </summary>
internal static class SustainedSlownessCore
{
    /// <summary>How many top contributors to name on the insight.</summary>
    public const int TopContributors = 3;

    /// <summary>
    /// Evaluate the fire condition and build the result.
    /// <paramref name="perModCategorySmoothedMs"/> is the row-major
    /// <c>[modId * catCount + categoryId]</c> smoothed cost grid;
    /// null/empty is tolerated (no contributors named).
    /// Returns null when the game is not in sustained slow-motion.
    /// </summary>
    public static SustainedSlownessResult? Compute(
        double speedNow,
        double consecutiveSlowMs,
        double timeBelowMs,
        double deficitMsPerSecond,
        IReadOnlyList<double>? perModCategorySmoothedMs,
        int modCount,
        int catCount)
    {
        if (speedNow >= RealtimeSpeed.SlowThreshold) return null;
        if (consecutiveSlowMs < RealtimeSpeed.SustainedFireMs) return null;

        var contributors = new List<InsightContributor>(TopContributors);
        if (perModCategorySmoothedMs != null && modCount > 0 && catCount > 0)
        {
            // Per-mod totals across categories, then top-N by straight selection
            // (N is 3; a sort of the full roster would be wasted work).
            double totalMs = 0d;
            double[] perMod = new double[modCount]; // ≤1 Hz worker cadence, not per-tick — allocation acceptable
            for (int m = 0; m < modCount; m++)
            {
                double sum = 0d;
                int baseIdx = m * catCount;
                for (int c = 0; c < catCount; c++)
                {
                    int idx = baseIdx + c;
                    if (idx < perModCategorySmoothedMs.Count) sum += perModCategorySmoothedMs[idx];
                }
                perMod[m] = sum;
                totalMs += sum;
            }
            if (totalMs > 0d)
            {
                bool[] taken = new bool[modCount];
                for (int pick = 0; pick < TopContributors; pick++)
                {
                    int best = -1;
                    double bestMs = 0d;
                    for (int m = 0; m < modCount; m++)
                    {
                        if (!taken[m] && perMod[m] > bestMs)
                        {
                            best = m;
                            bestMs = perMod[m];
                        }
                    }
                    if (best < 0 || bestMs <= 0d) break;
                    taken[best] = true;
                    contributors.Add(new InsightContributor(
                        SubjectRef.ForMod(best), bestMs, bestMs / totalMs));
                }
            }
        }

        return new SustainedSlownessResult(
            speedNow, consecutiveSlowMs, timeBelowMs, deficitMsPerSecond, contributors);
    }
}
