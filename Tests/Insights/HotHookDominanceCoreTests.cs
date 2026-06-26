#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Insights;
using PerformanceProfiler.Insights.Detectors;
using Xunit;

namespace PerformanceProfiler.Tests.Insights;

/// <summary>
/// Pins the hot-hook-dominance floors after the play-test fix that raised the
/// absolute-cost floor from 0.05 to 0.5 ms/tick. The old floor surfaced "this hook is
/// 100% of mod X's cost" even when mod X cost 0.19 ms/tick (100% of nearly nothing);
/// these guard that a sub-0.5 ms mod no longer fires while a genuinely costly one with
/// a dominant hook still does. catCount is 1 so the per-mod row is a single cell.
/// </summary>
public sealed class HotHookDominanceCoreTests
{
    private const double ShareFloor = 0.60d;
    private const double ModTotalFloor = 0.5d;

    private static List<HookDescriptor> OneHook(int modId) =>
        new List<HookDescriptor> { new HookDescriptor(modId, 0, "Mod" + modId + ".Update()") };

    private static List<Insight> Run(double modTotalMs, double topHookMs)
    {
        var emit = new List<Insight>();
        HotHookDominanceCore.Evaluate(
            categoryMs: new[] { modTotalMs },
            hookMs: new[] { topHookMs },
            hooks: OneHook(0),
            modCount: 1, catCount: 1,
            shareFloor: ShareFloor, modTotalFloorMs: ModTotalFloor,
            historyCount: 100, nowTick: 1000,
            audience: Audience.Modder, emit);
        return emit;
    }

    [Fact]
    public void SubHalfMsMod_DoesNotFire_EvenAtFullShare()
    {
        // Mod costs 0.3 ms/tick; its single hook owns 100% of that — but 100% of nearly
        // nothing is noise. The raised absolute-cost floor suppresses it.
        Assert.Empty(Run(modTotalMs: 0.3d, topHookMs: 0.3d));
    }

    [Fact]
    public void CostlyModWithDominantHook_Fires()
    {
        var emit = Run(modTotalMs: 1.0d, topHookMs: 0.8d);
        Assert.Single(emit);
        Assert.Equal(PatternKey.HotHookDominance, emit[0].Pattern);
        Assert.Equal(0, emit[0].Subject.ModId);
        Assert.Equal(0, emit[0].Subject.HookId);
        Assert.Equal(0.8d, emit[0].Magnitude.RatioOrDelta, 6);
    }

    [Fact]
    public void CostlyModWithSharedHooks_BelowShareFloor_DoesNotFire()
    {
        // Mod clears the cost floor (1.0 ms) but no single hook owns ≥60%: the share
        // floor (unchanged at 0.60) still gates it.
        Assert.Empty(Run(modTotalMs: 1.0d, topHookMs: 0.5d));
    }
}
