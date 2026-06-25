#nullable enable

using PerformanceProfiler.Insights.Shared;
using Xunit;

namespace PerformanceProfiler.Tests;

/// <summary>
/// Code-health audit pin for the <c>RunningStat.Without</c> catastrophic-cancellation
/// guard (finding "RunningStat.Without can silently floor catastrophic-cancellation M2
/// to 0", Insights cluster, High severity).
///
/// <para>
/// The reverse-Chan M2 recovery subtracts two nearly-equal large doubles. When a mod
/// costs the same in and out of a context (the common no-signal case), that subtraction
/// loses its significant digits and the recovered M2 collapses toward a tiny residue or
/// goes slightly negative. The old code floored it to exactly 0, producing a degenerate
/// zero-variance complement that <see cref="Stats.WelchTTestP"/> reads as "infinitely
/// confident" — a spurious significant p-value, which would promote a record's confidence
/// and violate the honesty contract (Invariant 3).
/// </para>
///
/// <para>
/// These tests drive <c>Without</c> into the cancellation regime and assert the post-fix
/// complement has a non-negative, non-degenerate variance and that Welch's test does NOT
/// manufacture significance against a statistically indistinguishable in-context sample.
/// The pre-fix code fails <see cref="WelchTTestP_NotSpuriouslySignificant_InCancellationRegime"/>.
/// </para>
/// </summary>
public sealed class AuditPin_Insights_Without
{
    /// <summary>
    /// Builds the parent ("global") distribution by interleaving the in-context and
    /// out-of-context samples, so the recovered complement is the genuine out-of-context
    /// set. With both halves drawn from near-identical large means and a small in-context
    /// subset, the recovery sits squarely in the cancellation regime.
    /// </summary>
    private static (RunningStat global, RunningStat inContext) BuildCancellationCase(
        int inN, int outN, double mean, double jitter)
    {
        var global = new RunningStat();
        var inContext = new RunningStat();

        // In-context: a small subset around the large mean.
        for (int i = 0; i < inN; i++)
        {
            double x = mean + (i % 2 == 0 ? jitter : -jitter);
            inContext.Add(x);
            global.Add(x);
        }
        // Out-of-context: a large set around the SAME large mean (statistically the same).
        for (int i = 0; i < outN; i++)
        {
            double x = mean + (i % 2 == 0 ? jitter : -jitter);
            global.Add(x);
        }
        return (global, inContext);
    }

    [Fact]
    public void Without_RecoveredVariance_IsNonNegative_InCancellationRegime()
    {
        // ~200 near-identical large samples; remove a 10-sample in-context subset. The
        // remaining 190 differ from the parent by floating-point noise only.
        (RunningStat global, RunningStat inContext) = BuildCancellationCase(
            inN: 10, outN: 190, mean: 1000.0, jitter: 0.001);

        RunningStat complement = global.Without(inContext);

        Assert.Equal(190, complement.Count);
        Assert.True(complement.Variance >= 0d,
            $"recovered variance must never be negative, got {complement.Variance}");
    }

    [Fact]
    public void Without_RecoveredVariance_IsNotDegenerateZero_WhenParentHadSpread()
    {
        // The parent has real per-sample spread (jitter != 0), so a complement of 190
        // samples must NOT collapse to an exact zero-variance degenerate — that is the
        // failure mode the guard prevents.
        (RunningStat global, RunningStat inContext) = BuildCancellationCase(
            inN: 10, outN: 190, mean: 1000.0, jitter: 0.001);

        RunningStat complement = global.Without(inContext);

        Assert.True(complement.Variance > 0d,
            "a complement of a spread parent must keep a non-degenerate variance, " +
            $"not floor to a zero-variance 'infinitely confident' stat (got {complement.Variance})");
    }

    [Fact]
    public void WelchTTestP_NotSpuriouslySignificant_InCancellationRegime()
    {
        // In-context and out-of-context are drawn from the SAME distribution, so they are
        // statistically indistinguishable. The Welch test must NOT report significance.
        // Pre-fix, the floored zero-variance complement gave a finite tiny denominator,
        // a large t, and a spurious p < 0.05.
        (RunningStat global, RunningStat inContext) = BuildCancellationCase(
            inN: 10, outN: 190, mean: 1000.0, jitter: 0.001);

        RunningStat complement = global.Without(inContext);
        double p = Stats.WelchTTestP(inContext, complement);

        Assert.True(p >= 0.05d,
            $"indistinguishable in/out samples must not look significant, got p={p}");
    }

    [Fact]
    public void WelchTTestP_AsymmetricCancellation_NotSpuriouslySignificant()
    {
        // Worst documented case: in-context has real spread, the out-of-context complement
        // would be floored to zero spread. With the guard, the complement borrows the
        // parent's per-sample spread so the test still cannot manufacture significance
        // against an indistinguishable mean.
        var global = new RunningStat();
        var inContext = new RunningStat();
        for (int i = 0; i < 12; i++)
        {
            double x = 500.0 + (i % 2 == 0 ? 0.5 : -0.5); // in-context: visible spread
            inContext.Add(x);
            global.Add(x);
        }
        for (int i = 0; i < 188; i++)
        {
            double x = 500.0 + (i % 2 == 0 ? 0.0005 : -0.0005); // out: near-flat
            global.Add(x);
        }

        RunningStat complement = global.Without(inContext);
        double p = Stats.WelchTTestP(inContext, complement);

        Assert.True(complement.Variance >= 0d, "variance must stay non-negative");
        Assert.True(p >= 0.05d,
            $"near-equal means must not be manufactured significant by a floored complement, got p={p}");
    }

    [Fact]
    public void Without_WellSeparatedValues_StillRecoversComplementExactly()
    {
        // Regression guard: the cancellation fix must NOT perturb the normal,
        // well-conditioned path (mirrors ReferenceFrameTests, the existing pin).
        double[] all = { 1, 2, 3, 4, 10, 11, 12, 13, 14, 15 };
        var global = new RunningStat();
        foreach (double x in all) global.Add(x);

        var subset = new RunningStat();
        for (int i = 0; i < 4; i++) subset.Add(all[i]);

        var direct = new RunningStat();
        for (int i = 4; i < all.Length; i++) direct.Add(all[i]);

        RunningStat complement = global.Without(subset);

        Assert.Equal(direct.Count, complement.Count);
        Assert.Equal(direct.Mean, complement.Mean, 6);
        Assert.Equal(direct.Variance, complement.Variance, 6);
    }
}
