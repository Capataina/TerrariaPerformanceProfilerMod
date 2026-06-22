#nullable enable

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Detectors;
using Xunit;

namespace PerformanceProfiler.Tests;

/// <summary>
/// Truth-table tests for <see cref="StallDetector.ClassifyCause"/> covering
/// the new cluster-shape signal. The CheatSheet NPC-spawn-menu playtest
/// produced 47 consecutive 100–220ms stalls; the classifier must catch
/// that pattern as UiOverlayBlocking instead of misreading every event as
/// ProcessSuspended.
/// </summary>
public class StallClassifierTests
{
    [Fact]
    public void Lone_LongStall_LowCpu_FocusLost_IsProcessSuspended()
    {
        // 2-second stall, CPU was idle, no GC, no recent neighbours, focus
        // LOST during the gap — the real OS-suspend signature.
        var cause = StallDetector.ClassifyCause(wallMs: 2000, gcMs: 0, gen2Delta: 0, cpuMs: 50,
            recentStallsInLast5s: 0, baselineMs: 16.67, focusHeldAcrossGap: false);
        Assert.Equal(StallCause.ProcessSuspended, cause);
    }

    [Fact]
    public void Lone_LongStall_LowCpu_FocusHeld_IsMainThreadFreeze()
    {
        // Same shape, focus held — the v0.4 misdiagnosis case. Now correctly
        // classifies as MainThreadFreeze instead of ProcessSuspended.
        var cause = StallDetector.ClassifyCause(wallMs: 2000, gcMs: 0, gen2Delta: 0, cpuMs: 50,
            recentStallsInLast5s: 0, baselineMs: 16.67, focusHeldAcrossGap: true);
        Assert.Equal(StallCause.MainThreadFreeze, cause);
    }

    [Fact]
    public void Clustered_MediumStalls_LowCpu_ClassifiesAsUiOverlayBlocking()
    {
        // 200ms stall, CPU low, 10 stalls within 5s — the CheatSheet menu signature.
        var cause = StallDetector.ClassifyCause(wallMs: 200, gcMs: 0, gen2Delta: 0, cpuMs: 30, recentStallsInLast5s: 10);
        Assert.Equal(StallCause.UiOverlayBlocking, cause);
    }

    [Fact]
    public void Major_Gc_With_Gen2Delta_Takes_Priority()
    {
        // GC pause dominates the stall window, Gen2 collected.
        var cause = StallDetector.ClassifyCause(wallMs: 300, gcMs: 200, gen2Delta: 1, cpuMs: 280, recentStallsInLast5s: 0);
        Assert.Equal(StallCause.MajorGc, cause);
    }

    [Fact]
    public void Minor_Gc_Without_Gen2_StillMaps_To_MinorGc()
    {
        var cause = StallDetector.ClassifyCause(wallMs: 150, gcMs: 100, gen2Delta: 0, cpuMs: 140, recentStallsInLast5s: 0);
        Assert.Equal(StallCause.MinorGc, cause);
    }

    [Fact]
    public void LongFrame_When_All_Signals_Match()
    {
        // CPU advanced fully, no GC, no cluster.
        var cause = StallDetector.ClassifyCause(wallMs: 250, gcMs: 0, gen2Delta: 0, cpuMs: 240, recentStallsInLast5s: 0);
        Assert.Equal(StallCause.LongFrame, cause);
    }

    [Fact]
    public void Zero_Wall_Maps_To_Unknown()
    {
        var cause = StallDetector.ClassifyCause(wallMs: 0, gcMs: 0, gen2Delta: 0, cpuMs: 0, recentStallsInLast5s: 0);
        Assert.Equal(StallCause.Unknown, cause);
    }

    [Fact]
    public void Long_Lone_Stall_With_Cluster_Below_Threshold_Still_Suspended()
    {
        // 2 stalls in 5s is below the cluster threshold (5). Focus lost
        // → real OS suspend, not a freeze.
        var cause = StallDetector.ClassifyCause(wallMs: 1800, gcMs: 0, gen2Delta: 0, cpuMs: 40,
            recentStallsInLast5s: 2, baselineMs: 16.67, focusHeldAcrossGap: false);
        Assert.Equal(StallCause.ProcessSuspended, cause);
    }

    [Fact]
    public void Severity_Buckets_Are_Relative_To_Baseline()
    {
        // Same wall ms reads differently for different player baselines.
        // 100 ms hitch:
        //   on 60 fps (16.67 ms baseline) is 6× → Noticeable
        //   on 120 fps (8.33 ms baseline) is 12× → Disruptive
        //   on 30 fps modded (33 ms baseline) is 3× → Noticeable (3× is the threshold)
        Assert.Equal(StallSeverity.Noticeable, StallDetector.ClassifySeverity(100, 16.67));
        Assert.Equal(StallSeverity.Disruptive, StallDetector.ClassifySeverity(100, 8.33));
        Assert.Equal(StallSeverity.Noticeable, StallDetector.ClassifySeverity(100, 33));

        // 500 ms freeze on any baseline ≥ 20× → Freeze.
        // 500 ms on 16.67 baseline = 30× → Freeze.
        // 500 ms on 33 baseline = 15× → Disruptive (not Freeze!) — the
        // 30 fps player perceives a 500 ms hitch as less freeze-y because
        // their normal frame is already long; correct.
        Assert.Equal(StallSeverity.Freeze,     StallDetector.ClassifySeverity(500, 16.67));
        Assert.Equal(StallSeverity.Disruptive, StallDetector.ClassifySeverity(500, 33));
    }
}
