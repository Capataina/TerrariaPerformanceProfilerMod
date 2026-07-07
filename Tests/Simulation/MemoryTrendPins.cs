#nullable enable

using PerformanceProfiler.Data.Stats;
using Xunit;

namespace PerformanceProfiler.Tests.Simulation;

/// <summary>
/// S04 memory-guard pins: the slope fit, the phase table, the warming gate,
/// the GC-dip robustness requirement, and the wire downsampler. The synthetic
/// streams replay the 2026-07-07 live case shapes (a ~35 MB/min climb is the
/// "4.2 → 10.4 GB across a session" walk at compressed scale).
/// </summary>
public sealed class MemoryTrendPins
{
    private const long Mb = 1024L * 1024L;

    /// <summary>Push samples at 5 s cadence: baseMb + slopeMbPerMin·t, with optional dips.</summary>
    private static MemoryTrend Stream(int samples, double baseMb, double slopeMbPerMin,
        int dipEvery = 0, double dipMb = 0d)
    {
        var trend = new MemoryTrend();
        long t0 = 1_700_000_000_000L;
        for (int i = 0; i < samples; i++)
        {
            double minutes = i * 5 / 60d;
            double mb = baseMb + slopeMbPerMin * minutes;
            if (dipEvery > 0 && i % dipEvery == dipEvery - 1) mb -= dipMb;
            trend.Push(t0 + i * 5000L, (long)(mb * Mb), (long)(mb * 0.8 * Mb));
        }
        return trend;
    }

    [Fact]
    public void Warming_UnderTenMinutes_NeverJudges()
    {
        // 119 samples = one under the 10-minute floor.
        var snap = Stream(MemoryTrend.MinSamplesForVerdict - 1, 4000d, 50d).Snapshot();
        Assert.Equal(MemoryTrendPhase.Warming, snap.Phase);
        Assert.Equal(0d, snap.GrowthMbPerMin10);
    }

    [Theory]
    [InlineData(0d, MemoryTrendPhase.Flat)]
    [InlineData(3d, MemoryTrendPhase.Flat)]       // under the 5 MB/min flat band
    [InlineData(10d, MemoryTrendPhase.Growing)]
    [InlineData(35d, MemoryTrendPhase.Climbing)]  // the live session's shape
    public void SteadySlopes_ClassifyByTheBands(double slope, MemoryTrendPhase expected)
    {
        var snap = Stream(240, 4000d, slope).Snapshot();
        Assert.Equal(expected, snap.Phase);
        if (expected != MemoryTrendPhase.Flat)
        {
            Assert.InRange(snap.GrowthMbPerMin10, slope - 2d, slope + 2d);
        }
    }

    [Fact]
    public void SingleGcDip_DoesNotFlipAClimbVerdict()
    {
        // A steady 30 MB/min climb with a 500 MB collection dip every 60
        // samples: the regression fit must still read the climb (last-minus-
        // first sampling would swing wildly on where the dip lands).
        var snap = Stream(240, 4000d, 30d, dipEvery: 60, dipMb: 500d).Snapshot();
        Assert.True(snap.Phase is MemoryTrendPhase.Climbing or MemoryTrendPhase.Growing,
            $"dip flipped the verdict to {snap.Phase} (slope {snap.GrowthMbPerMin10:F1})");
    }

    [Fact]
    public void Reclaim_ReadsOnlyAfterGrowth()
    {
        // Downhill from the start, never grew: flat-ish decline is NOT "reclaimed".
        Assert.Equal(MemoryTrendPhase.Flat, MemoryTrend.Classify(-10d, sawGrowthBefore: false));
        // The same decline after a growth episode IS the reclaim story.
        Assert.Equal(MemoryTrendPhase.Reclaimed, MemoryTrend.Classify(-10d, sawGrowthBefore: true));
    }

    [Fact]
    public void CopySeries_DownsamplesToTheCap_OldestFirst()
    {
        var trend = Stream(1000, 4000d, 10d);
        var (unixMs, wsMb, managedMb) = trend.CopySeries(maxPoints: 240);

        Assert.True(unixMs.Length <= 240);
        Assert.Equal(unixMs.Length, wsMb.Length);
        Assert.Equal(unixMs.Length, managedMb.Length);
        // Oldest-first, monotonically increasing timestamps.
        for (int i = 1; i < unixMs.Length; i++)
        {
            Assert.True(unixMs[i] > unixMs[i - 1]);
        }
        // The climb is visible across the copied series.
        Assert.True(wsMb[^1] > wsMb[0] + 50d);
    }

    [Fact]
    public void PeakAndStart_TrackTheExtremes()
    {
        var trend = Stream(240, 4000d, 20d);
        var snap = trend.Snapshot();
        Assert.InRange(snap.SessionStartWorkingSetMb, 3999d, 4001d);
        Assert.True(snap.PeakWorkingSetMb >= snap.CurrentWorkingSetMb - 1d);
        Assert.True(snap.PeakWorkingSetMb > snap.SessionStartWorkingSetMb);
    }
}
