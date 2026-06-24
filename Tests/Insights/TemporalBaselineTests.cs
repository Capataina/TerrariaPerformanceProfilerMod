#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Insights;
using PerformanceProfiler.Insights.Drivers;
using PerformanceProfiler.Insights.ReferenceFrames;
using PerformanceProfiler.Insights.Shared;
using Xunit;

namespace PerformanceProfiler.Tests.Insights;

/// <summary>
/// Pins the Family-B substrate: the early/late split that lets the leak + cost-shift
/// detectors compare a session's later behaviour against its own start, and the
/// driver dimensions they control for. The detectors' live emission is verified
/// in-game; this locks the windowing + the driver maths.
/// </summary>
public sealed class TemporalBaselineTests
{
    private static void Fill(TemporalBaseline tb, long count, double heap, double entities, double modCost)
    {
        var cost = new double[] { modCost };
        for (long i = 0; i < count; i++) tb.Observe(heap, entities, cost);
    }

    [Fact]
    public void Observe_SplitsEarlyFromLateAtTheWindowBoundary()
    {
        var tb = new TemporalBaseline(modCount: 1);
        // EarlySamples (120) at heap 100, then 100 more at heap 300.
        Fill(tb, TemporalBaseline.EarlySamples, heap: 100, entities: 50, modCost: 0.1);
        Fill(tb, 100, heap: 300, entities: 50, modCost: 0.9);

        Assert.True(tb.IsReady);
        Assert.Equal(TemporalBaseline.EarlySamples, tb.EarlyHeap.Count);
        Assert.Equal(100, tb.LateHeap.Count);
        Assert.Equal(100d, tb.EarlyHeap.Mean, 6);
        Assert.Equal(300d, tb.LateHeap.Mean, 6);

        Assert.True(tb.TryPerMod(0, out RunningStat early, out RunningStat late));
        Assert.Equal(0.1d, early.Mean, 6);
        Assert.Equal(0.9d, late.Mean, 6);
    }

    [Fact]
    public void NotReady_BeforeBothWindowsFill()
    {
        var tb = new TemporalBaseline(modCount: 1);
        Fill(tb, 30, heap: 100, entities: 10, modCost: 0.1); // under MinPerSide
        Assert.False(tb.IsReady);
    }

    // ---- Drivers ---------------------------------------------------------

    private sealed class FakeInput : IInsightInput
    {
        public IReadOnlyList<double> PerModCategoryAverageMs => System.Array.Empty<double>();
        public IReadOnlyList<double> PerHookAverageMs => System.Array.Empty<double>();
        public IReadOnlyList<double>? PerModCategoryAverageBytes => null;
        public bool TracksAllocations => false;
        public Baseline Baseline => new Baseline();
        public int LatestNpcCount { get; set; }
        public int LatestProjectileCount { get; set; }
        public long ManagedHeapBytes { get; set; }
        public long SessionTicks { get; set; }
    }

    [Fact]
    public void Drivers_SampleTheirDimensions()
    {
        var input = new FakeInput
        {
            LatestNpcCount = 40,
            LatestProjectileCount = 60,
            ManagedHeapBytes = 256L * 1024 * 1024, // 256 MB
            SessionTicks = 3600,                    // 1 minute at 60 tps
        };
        Assert.Equal(100d, new EntityCountDriver().Sample(input));
        Assert.Equal(256d, new HeapDriver().Sample(input), 6);
        Assert.Equal(1d, new SessionAgeDriver().Sample(input), 6);
    }
}
