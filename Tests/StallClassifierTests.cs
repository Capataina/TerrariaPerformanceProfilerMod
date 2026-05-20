#nullable enable

using PerformanceProfiler.Profiling;
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
    public void Lone_LongStall_LowCpu_IsProcessSuspended()
    {
        // 2-second stall, CPU was idle, no GC, no recent neighbours.
        var cause = StallDetector.ClassifyCause(wallMs: 2000, gcMs: 0, gen2Delta: 0, cpuMs: 50, recentStallsInLast5s: 0);
        Assert.Equal(StallCause.ProcessSuspended, cause);
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
        // 2 stalls in 5s is below the cluster threshold (5).
        var cause = StallDetector.ClassifyCause(wallMs: 1800, gcMs: 0, gen2Delta: 0, cpuMs: 40, recentStallsInLast5s: 2);
        Assert.Equal(StallCause.ProcessSuspended, cause);
    }

    [Fact]
    public void Severity_Buckets_Correct()
    {
        Assert.Equal(StallSeverity.Minor,      StallDetector.ClassifySeverity(50));
        Assert.Equal(StallSeverity.Noticeable, StallDetector.ClassifySeverity(120));
        Assert.Equal(StallSeverity.Disruptive, StallDetector.ClassifySeverity(300));
        Assert.Equal(StallSeverity.Freeze,     StallDetector.ClassifySeverity(700));
    }
}
