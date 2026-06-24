#nullable enable

using System;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Insights;

/// <summary>
/// Scoring formula used by <see cref="InsightStore.Top"/> to choose which
/// records surface in the overlay. Six weighted components: magnitude,
/// confidence, recency, actionability, novelty, audience-match. Weights
/// match plan §7.1; tweak there and here together.
///
/// <para>
/// The scorer is intentionally stateless. Records cache nothing here so
/// rank-changes propagate immediately; the small recompute cost (a few
/// multiplies per record) is dwarfed by the per-frame draw work.
/// </para>
/// </summary>
public static class RankingScorer
{
    private const double WMagnitude = 0.30;
    private const double WConfidence = 0.25;
    private const double WRecency = 0.15;
    private const double WActionability = 0.15;
    private const double WNovelty = 0.10;
    private const double WAudience = 0.05;

    /// <summary>
    /// Computes the ranking score for <paramref name="rec"/> at
    /// <paramref name="nowTick"/>. Higher wins. The <paramref name="ttlTicks"/>
    /// argument is the store's TTL window, used by the recency component.
    /// </summary>
    public static double Score(Insight rec, long nowTick, long ttlTicks)
    {
        double mag = NormaliseMagnitude(rec);
        double conf = ConfidenceWeight(rec.Confidence);
        double rec_ = RecencyWeight(nowTick - rec.LastSeenTick, ttlTicks);
        double act = Actionability(rec.Pattern);
        double nov = Novelty(rec.ConfirmationCount);
        double aud = AudienceMatch(rec.Audience);

        return WMagnitude * mag
             + WConfidence * conf
             + WRecency * rec_
             + WActionability * act
             + WNovelty * nov
             + WAudience * aud;
    }

    /// <summary>
    /// Normalises a record's <see cref="Magnitude.RatioOrDelta"/> to <c>[0,1]</c>
    /// according to its <see cref="PatternKey"/>. Two regimes coexist:
    /// <list type="bullet">
    ///   <item>Share patterns — <c>HotHookDominance</c>, <c>AllocationBurst</c>,
    ///   <c>PeakContributorToSpike</c> — store a fraction in <c>[0,1]</c>
    ///   (e.g. "this hook is 42% of category cost"). They pass through unchanged.</item>
    ///   <item>Ratio patterns — everything else — store a multiple where
    ///   <c>1.0</c> means baseline. A soft knee at 10× saturates to 1.</item>
    /// </list>
    /// <para>
    /// Before this split, every detector ran through the ratio curve, which
    /// collapsed all share values in <c>[0,1]</c> to a magnitude of zero. A
    /// 40% contributor and a 90% contributor ranked identically on magnitude,
    /// erasing the strongest live signal the in-scope detectors produce.
    /// </para>
    /// </summary>
    private static double NormaliseMagnitude(Insight rec)
    {
        double v = rec.Magnitude.RatioOrDelta;
        return IsSharePattern(rec.Pattern)
            ? ClampUnit(v)
            : RatioCurve(v);
    }

    /// <summary>True if the pattern emits a fractional share in <c>[0,1]</c> as its magnitude.</summary>
    private static bool IsSharePattern(PatternKey k) => k switch
    {
        PatternKey.HotHookDominance       => true,
        PatternKey.AllocationBurst        => true,
        PatternKey.PeakContributorToSpike => true,
        // GcPauseCulprit's RatioOrDelta is the share of allocations the top
        // contributor held in the K=60 ticks before a GC stall fired.
        PatternKey.GcPauseCulprit         => true,
        // Wave 5: CostConcentration's RatioOrDelta is the lever's share of cost;
        // FrameHeadroom stores the fraction of the 60 fps budget used; FrameJitter
        // stores the coefficient of variation (all read as [0,1]-ish shares).
        PatternKey.CostConcentration      => true,
        PatternKey.FrameHeadroom          => true,
        PatternKey.FrameJitter            => true,
        _                                 => false,
    };

    /// <summary>Soft knee at 10×: <c>1×</c>→0, <c>2×</c>→~0.11, <c>5×</c>→~0.44, <c>10×+</c>→1.</summary>
    private static double RatioCurve(double ratio)
    {
        if (ratio <= 1d) return 0d;
        double v = (ratio - 1d) / 9d;
        return v >= 1d ? 1d : v;
    }

    private static double ClampUnit(double v) => v < 0d ? 0d : v > 1d ? 1d : v;

    private static double ConfidenceWeight(Confidence c) => c switch
    {
        Confidence.High => 1.0,
        Confidence.Medium => 0.75,
        Confidence.Low => 0.5,
        _ => 0.25,
    };

    private static double RecencyWeight(long ageTicks, long ttlTicks)
    {
        if (ttlTicks <= 0) return 1d;
        double frac = 1d - (double)ageTicks / ttlTicks;
        return frac < 0.2d ? 0.2d : frac;
    }

    private static double Actionability(PatternKey pattern) => pattern switch
    {
        PatternKey.PeakContributorToSpike => 1.0,
        PatternKey.HotHookDominance => 1.0,
        PatternKey.FreeRemovalCandidate => 1.0,
        PatternKey.ContextCorrelatedSpike => 0.8,
        PatternKey.AllocationBurst => 0.8,
        PatternKey.ContextConditionalCost => 0.6,
        PatternKey.NewContributor => 0.6,
        PatternKey.SustainedCostShift => 0.5,
        PatternKey.GcPauseCulprit => 0.5,
        PatternKey.HookFrequencyTail => 0.4,
        _ => 0.5,
    };

    private static double Novelty(int confirmationCount)
    {
        if (confirmationCount <= 0) return 1d;
        // 1 → 1.0, 3 → 0.5, 7 → 0.33.
        return 1d / (1d + System.Math.Log(1d + confirmationCount, 2d));
    }

    private static double AudienceMatch(Audience audience)
    {
        // The overlay is Player-facing; modder-only records are demoted a
        // touch. JSONL export uses a different scorer call site if it needs
        // modder ranking, so this is fine as a default.
        return audience == Audience.Modder ? 0.5 : 1.0;
    }
}
