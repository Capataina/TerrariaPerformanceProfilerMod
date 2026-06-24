#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Insights.ReferenceFrames;
using PerformanceProfiler.Insights.Shared;
using Xunit;

namespace PerformanceProfiler.Tests.Insights;

/// <summary>
/// Pins the Wave 3 relativity substrate: the Welford running stats, the two-sample
/// statistics, and the per-context accumulator that the conditional-cost detector
/// reads. The detector's in-game emission is verified by playtest; this locks the
/// maths it stands on.
/// </summary>
public sealed class ReferenceFrameTests
{
    // ---- RunningStat (Welford) -----------------------------------------

    [Fact]
    public void RunningStat_MeanAndVariance()
    {
        var s = new RunningStat();
        foreach (double x in new[] { 2d, 4d, 4d, 4d, 5d, 5d, 7d, 9d }) s.Add(x);
        Assert.Equal(8, s.Count);
        Assert.Equal(5d, s.Mean, 9);
        // Population variance of that classic set is 4.
        Assert.Equal(4d, s.Variance, 6);
    }

    [Fact]
    public void RunningStat_Without_RecoversTheComplement()
    {
        // global = all 10 values; subset = the first 4. The complement must match a
        // RunningStat built from the remaining 6 directly.
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

    // ---- Stats ----------------------------------------------------------

    [Fact]
    public void NormalCdf_KnownPoints()
    {
        Assert.Equal(0.5d, Stats.NormalCdf(0d), 4);
        Assert.Equal(0.8413d, Stats.NormalCdf(1d), 3);   // +1 sigma
        Assert.Equal(0.9772d, Stats.NormalCdf(2d), 3);   // +2 sigma
    }

    [Fact]
    public void CohensD_SeparatedDistributions_IsLarge()
    {
        var a = new RunningStat();
        var b = new RunningStat();
        for (int i = 0; i < 50; i++) { a.Add(10d + (i % 2)); b.Add(1d + (i % 2)); }
        // Means ~9 apart with ~0.5 spread -> a very large effect size.
        Assert.True(Stats.CohensD(a, b) > 2d, "well-separated means should give a large d");
    }

    [Fact]
    public void WelchTTestP_IdenticalDistributions_IsNotSignificant()
    {
        var a = new RunningStat();
        var b = new RunningStat();
        for (int i = 0; i < 100; i++) { double x = i % 7; a.Add(x); b.Add(x); }
        Assert.True(Stats.WelchTTestP(a, b) > 0.5d, "identical samples must not look significant");
    }

    [Fact]
    public void WelchTTestP_SeparatedDistributions_IsSignificant()
    {
        var a = new RunningStat();
        var b = new RunningStat();
        for (int i = 0; i < 100; i++) { a.Add(10d + (i % 3)); b.Add(0d + (i % 3)); }
        Assert.True(Stats.WelchTTestP(a, b) < 0.001d, "well-separated samples must be significant");
    }

    // ---- ContextBaseline ------------------------------------------------

    [Fact]
    public void ContextBaseline_BelowMinSamples_NotAComparison()
    {
        var cb = new ContextBaseline(modCount: 2);
        long bucket = ContextBaseline.MakeBucket(2, 1);
        var active = new List<long> { bucket };
        // Only 5 samples — under MinSamplesForTest (30).
        for (int i = 0; i < 5; i++) cb.Observe(active, new double[] { 1d, 2d });
        Assert.False(cb.TryConditional(bucket, 0, out _, out _));
    }

    [Fact]
    public void ContextBaseline_SplitsInContextFromOutOfContext()
    {
        var cb = new ContextBaseline(modCount: 1);
        long boss = ContextBaseline.MakeBucket(ContextBaselineDimBoss, 1);
        var inCtx = new List<long> { boss };
        var noCtx = new List<long>();

        // 40 in-context samples at cost 5, 40 out-of-context at cost 1.
        for (int i = 0; i < 40; i++) cb.Observe(inCtx, new double[] { 5d });
        for (int i = 0; i < 40; i++) cb.Observe(noCtx, new double[] { 1d });

        Assert.True(cb.TryConditional(boss, 0, out RunningStat hot, out RunningStat cold));
        Assert.Equal(40, hot.Count);
        Assert.Equal(5d, hot.Mean, 6);
        Assert.Equal(40, cold.Count);     // global(80) − inContext(40)
        Assert.Equal(1d, cold.Mean, 6);   // the out-of-context cost
    }

    [Fact]
    public void ContextBaseline_EvictsLeastSampledOverCap()
    {
        var cb = new ContextBaseline(modCount: 1);
        var cost = new double[] { 1d };
        // Fill MaxBuckets, each with one sample, then add one more bucket.
        for (int b = 0; b <= ContextBaseline.MaxBuckets; b++)
        {
            cb.Observe(new List<long> { ContextBaseline.MakeBucket(9, b) }, cost);
        }
        Assert.Equal(1, cb.Evictions);
    }

    // mirror of InsightsEngine.DimBoss (internal const) for the readable test above.
    private const byte ContextBaselineDimBoss = 2;
}
