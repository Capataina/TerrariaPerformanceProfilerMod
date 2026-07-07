#nullable enable

using System;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Stats;
using Xunit;

namespace PerformanceProfiler.Tests;

/// <summary>
/// Code-health audit pin for the <see cref="Baseline.Recompute(RingBuffer{TickFrame}, in TickFrame, bool, double)"/>
/// fast-path overload (metric-collection finding #2, Medium severity). The
/// steady-state path used to re-fetch the just-pushed frame back out of the
/// ring via <c>history[history.Count - 1]</c> (bounds check + wrap arithmetic +
/// full struct copy); the overload feeds the caller's in-hand frame straight to
/// <c>OnFramePushed</c>, skipping that round-trip.
///
/// <para>
/// The claim is that the two overloads are observationally identical: feeding
/// the in-hand frame must produce the same medians, MADs, calibration state,
/// and allocation rate as reading the same frame back through the indexer. This
/// pin drives two fresh baselines down the two overloads with an identical frame
/// stream (push then recompute, tick by tick — the production cadence) and
/// asserts every public field agrees at the end, including after the 30-tick MAD
/// recompute interval has fired.
/// </para>
/// </summary>
public sealed class AuditPin_Baseline_FastPath
{
    private static TickFrame Frame(long unixMs, double frameMs) => new TickFrame
    {
        TimestampUnixMs = unixMs,
        TickIndex = unixMs,
        FrameTimeMs = frameMs,
        RealFrameTimeMs = frameMs,
        GcTimeMs = 0d,
        NpcCount = 0,
        ProjectileCount = 0,
        DustCount = 0,
        ModSamples = null,
    };

    [Theory]
    [InlineData(5)]     // below MinCalibrationTicks — uncalibrated branch.
    [InlineData(60)]    // past one MAD-recompute interval.
    [InlineData(2048)]  // exercises the full-ring eviction branch.
    public void FastPathOverload_MatchesHistoryOnlyOverload(int tickCount)
    {
        Baseline viaHistory = new Baseline();
        Baseline viaFrame = new Baseline();
        RingBuffer<TickFrame> hist1 = new RingBuffer<TickFrame>(1800);
        RingBuffer<TickFrame> hist2 = new RingBuffer<TickFrame>(1800);

        var rng = new Random(424242);
        long unix = 1_700_000_000_000L;
        for (int t = 0; t < tickCount; t++)
        {
            // Vary frame time and period so medians/MADs are non-trivial and the
            // two paths have something real to disagree about if they diverged.
            double frameMs = 14d + rng.NextDouble() * 8d;
            unix += 15L + rng.Next(0, 4);
            double allocBytes = rng.Next(0, 50_000);
            TickFrame f = Frame(unix, frameMs);

            hist1.Push(in f);
            viaHistory.Recompute(hist1, tracksAllocations: true, allocBytesThisTick: allocBytes);

            hist2.Push(in f);
            viaFrame.Recompute(hist2, in f, tracksAllocations: true, allocBytesThisTick: allocBytes);
        }

        Assert.Equal(viaHistory.IsCalibrated, viaFrame.IsCalibrated);
        Assert.Equal(viaHistory.FrameMsMedian, viaFrame.FrameMsMedian);
        Assert.Equal(viaHistory.FrameMsMad, viaFrame.FrameMsMad);
        Assert.Equal(viaHistory.TickPeriodMsMedian, viaFrame.TickPeriodMsMedian);
        Assert.Equal(viaHistory.TickPeriodMsMad, viaFrame.TickPeriodMsMad);
        Assert.Equal(viaHistory.AllocBytesPerTickMedian, viaFrame.AllocBytesPerTickMedian);
    }
}
