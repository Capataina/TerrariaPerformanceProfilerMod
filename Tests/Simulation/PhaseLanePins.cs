#nullable enable

using System.Collections.Generic;
using System.Diagnostics;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Insights.Detectors;
using Xunit;

namespace PerformanceProfiler.Tests.Simulation;

/// <summary>
/// Loop-anatomy (S01) pins: the phase-lane accumulator contract and the
/// draw-bound finding maths. PerModAttribution is static shared state and the
/// suite runs serially (see _TestNamespaceStubs), so each test Configures its
/// own world.
/// </summary>
public sealed class PhaseLanePins
{
    private static long MsToTicks(double ms) => (long)(ms / 1000d * Stopwatch.Frequency);

    [Fact]
    public void DrawLane_CarriesOnlyOutOfWindowCredits_AndPrimaryCarriesTotal()
    {
        PerModAttribution.Configure(modCount: 2, backendCount: 1, trackAllocations: false, phaseLanes: true);
        int hook = PerModAttribution.RegisterHook(0, 0, "TestMod::Hook");
        PerModAttribution.BeginTick();

        // Update-phase credit: 4 ms.
        PerModAttribution.CurrentPhaseIsUpdate = true;
        PerModAttribution.Add(0, 0, 0, hook, MsToTicks(4.0));
        // Draw-phase credit: 6 ms.
        PerModAttribution.CurrentPhaseIsUpdate = false;
        PerModAttribution.Add(0, 0, 0, hook, MsToTicks(6.0));

        var total = new double[2 * PerModAttribution.CategoryCount];
        var draw = new double[2 * PerModAttribution.CategoryCount];
        PerModAttribution.HarvestInto(total, backendId: 0);
        PerModAttribution.HarvestDrawInto(draw, backendId: 0);

        // Primary = TOTAL (update + draw) — the pre-S01 contract, bit-preserved.
        Assert.InRange(total[0], 9.9, 10.1);
        // Mirror = draw share only.
        Assert.InRange(draw[0], 5.9, 6.1);
        // Update = total − draw.
        Assert.InRange(total[0] - draw[0], 3.9, 4.1);
    }

    [Fact]
    public void PhaseLanesOff_DrawHarvestIsZero_AndTotalsUnchanged()
    {
        PerModAttribution.Configure(modCount: 1, backendCount: 1, trackAllocations: false, phaseLanes: false);
        int hook = PerModAttribution.RegisterHook(0, 0, "TestMod::Hook");
        PerModAttribution.BeginTick();

        PerModAttribution.CurrentPhaseIsUpdate = false; // draw phase, but lanes off
        PerModAttribution.Add(0, 0, 0, hook, MsToTicks(5.0));

        Assert.False(PerModAttribution.PhaseLanesEnabled);
        var total = new double[PerModAttribution.CategoryCount];
        var draw = new double[PerModAttribution.CategoryCount];
        PerModAttribution.HarvestInto(total, backendId: 0);
        PerModAttribution.HarvestDrawInto(draw, backendId: 0);

        Assert.InRange(total[0], 4.9, 5.1); // total untouched by the lanes being off
        Assert.Equal(0d, draw[0]);          // mirror zero-filled
    }

    [Fact]
    public void BeginTick_ClearsTheDrawMirror()
    {
        PerModAttribution.Configure(modCount: 1, backendCount: 1, trackAllocations: false, phaseLanes: true);
        int hook = PerModAttribution.RegisterHook(0, 0, "TestMod::Hook");
        PerModAttribution.CurrentPhaseIsUpdate = false;
        PerModAttribution.Add(0, 0, 0, hook, MsToTicks(3.0));

        PerModAttribution.BeginTick();

        var draw = new double[PerModAttribution.CategoryCount];
        PerModAttribution.HarvestDrawInto(draw, backendId: 0);
        Assert.Equal(0d, draw[0]);
    }

    // ---------------------------------------------------------------- core

    private static double[] Grid(int mods, int cats, params (int mod, double ms)[] cells)
    {
        var g = new double[mods * cats];
        foreach (var (mod, ms) in cells) g[mod * cats] = ms;
        return g;
    }

    [Fact]
    public void DrawBoundCore_FindsTheDrawBoundLeader_AndOnlyIt()
    {
        const int cats = 7;
        // Mod 0: 8 ms total, 6.4 draw (80% — draw-bound).
        // Mod 1: 8 ms total, 1.6 draw (20% — update-bound).
        // Mod 2: 0.4 ms total, 0.4 draw (100% but sub-noise — must stay silent).
        var total = Grid(3, cats, (0, 8.0), (1, 8.0), (2, 0.4));
        var draw = Grid(3, cats, (0, 6.4), (1, 1.6), (2, 0.4));

        List<DrawBoundModResult> found = DrawBoundModCore.Compute(total, draw, 3, cats);

        Assert.Single(found);
        Assert.Equal(0, found[0].ModId);
        Assert.InRange(found[0].DrawShare, 0.79, 0.81);
    }

    [Fact]
    public void DrawBoundCore_NullDrawGrid_MeansLanesOff_NoFindings()
    {
        var total = Grid(2, 7, (0, 10.0));
        Assert.Empty(DrawBoundModCore.Compute(total, null, 2, 7));
    }
}
