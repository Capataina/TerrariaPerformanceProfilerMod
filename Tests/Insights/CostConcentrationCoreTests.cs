#nullable enable

using PerformanceProfiler.Insights;
using PerformanceProfiler.Insights.Detectors;
using Xunit;

namespace PerformanceProfiler.Tests.Insights;

/// <summary>
/// Pins the cost-concentration core after the play-test fix that made the card name
/// its lever mods and report the loaded roster. The card used to read "3 of 26" with
/// no names and no idle context; these guard that the core now produces the named
/// contributors (descending by cost), the active/loaded denominators, and the share.
/// catCount is 1 throughout so the row-major array is one cell per mod.
/// </summary>
public sealed class CostConcentrationCoreTests
{
    private const double Threshold = 0.70d;
    private const int MaxLever = 5;

    [Fact]
    public void Compute_SingleDominantMod_NamesItAndReportsRoster()
    {
        // 8 contributing mods (mod0 = 80% of cost) + 3 idle mods → 11 loaded.
        double[] cost = { 80, 4, 4, 3, 3, 2, 2, 2, 0, 0, 0 };
        ConcentrationResult? r = CostConcentrationCore.Compute(cost, modCount: 11, catCount: 1, Threshold, MaxLever);

        Assert.True(r.HasValue);
        ConcentrationResult res = r!.Value;
        Assert.Equal(1, res.LeverCount);            // one mod carries ≥70%
        Assert.Equal(8, res.ContributingCount);     // mods with cost > 0 (the "active" denominator)
        Assert.Equal(11, res.LoadedCount);          // every mod iterated, idle ones included
        Assert.Equal(0.80, res.Share, 6);
        Assert.Single(res.Contributors);
        InsightContributor c = res.Contributors[0];
        Assert.Equal(SubjectKind.Mod, c.Subject.Kind);
        Assert.Equal(0, c.Subject.ModId);
        Assert.Equal(80d, c.Value, 6);
        Assert.Equal(0.80, c.Share, 6);             // share is of the whole measured cost
    }

    [Fact]
    public void Compute_ThreeModLever_NamesThemInCostDescendingOrder()
    {
        // Lever = mod1(38), mod2(29), mod0(8) = 75 of 100; 12 filler mods carry the rest;
        // 2 idle mods → 17 loaded. The lever must stay a minority of the 15 contributors.
        double[] cost =
        {
            8, 38, 29,                                  // mod0, mod1, mod2 (the lever, deliberately out of order)
            3, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1,         // mod3..mod14 filler = 25
            0, 0,                                       // mod15, mod16 idle
        };
        ConcentrationResult? r = CostConcentrationCore.Compute(cost, modCount: 17, catCount: 1, Threshold, MaxLever);

        Assert.True(r.HasValue);
        ConcentrationResult res = r!.Value;
        Assert.Equal(3, res.LeverCount);
        Assert.Equal(15, res.ContributingCount);
        Assert.Equal(17, res.LoadedCount);
        Assert.Equal(3, res.Contributors.Count);
        // Ordered by cost descending: mod1 (38), mod2 (29), mod0 (8).
        Assert.Equal(1, res.Contributors[0].Subject.ModId);
        Assert.Equal(2, res.Contributors[1].Subject.ModId);
        Assert.Equal(0, res.Contributors[2].Subject.ModId);
        Assert.Equal(0.38, res.Contributors[0].Share, 6);
        Assert.Equal(0.29, res.Contributors[1].Share, 6);
        Assert.Equal(0.08, res.Contributors[2].Share, 6);
    }

    [Fact]
    public void Compute_EvenSpread_NoLever_ReturnsNull()
    {
        // Eight equal mods: reaching 70% needs six of them, past the lever cap → no concentration.
        double[] cost = { 10, 10, 10, 10, 10, 10, 10, 10 };
        Assert.False(CostConcentrationCore.Compute(cost, modCount: 8, catCount: 1, Threshold, MaxLever).HasValue);
    }

    [Fact]
    public void Compute_TooFewContributors_ReturnsNull()
    {
        // Only five contributing mods (≤ MaxLever): "concentration" has nothing to mean.
        double[] cost = { 50, 10, 10, 10, 10 };
        Assert.False(CostConcentrationCore.Compute(cost, modCount: 5, catCount: 1, Threshold, MaxLever).HasValue);
    }
}
