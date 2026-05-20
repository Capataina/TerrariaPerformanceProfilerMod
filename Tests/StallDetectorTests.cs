#nullable enable

using PerformanceProfiler.Profiling;
using Xunit;

namespace PerformanceProfiler.Tests;

/// <summary>
/// Pins the two stall-detector contracts that the audit-relative-thresholds
/// design hinges on: cause classification must produce the right bucket for
/// each combination of signals, and perceptual severity must follow the
/// absolute-ms ladder regardless of relative trigger state.
///
/// The full <see cref="StallDetector.OnBeginTick"/> path is intentionally not
/// tested here — it reads <c>GC.GetTotalPauseDuration</c>, <c>GC.CollectionCount</c>,
/// and a cached <see cref="System.Diagnostics.Process"/> instance, none of
/// which can be deterministically driven from a unit test. We test the
/// classifier-public surface in isolation; the integration glue is covered
/// by in-game playtest verification of the session JSON.
/// </summary>
public class StallDetectorTests
{
    // ---- ClassifyCause: truth table ----------------------------------------

    [Fact]
    public void ClassifyCause_GcDominatesAndGen2_IsMajorGc()
    {
        // 1.2 s stall, 1.1 s of which was GC pause, Gen2 fired. The exact
        // shape we saw in the playthrough that prompted this work.
        StallCause c = StallDetector.ClassifyCause(
            wallMs: 1200, gcMs: 1100, gen2Delta: 1, cpuMs: 1180);
        Assert.Equal(StallCause.MajorGc, c);
    }

    [Fact]
    public void ClassifyCause_GcDominatesNoGen2_IsMinorGc()
    {
        StallCause c = StallDetector.ClassifyCause(
            wallMs: 150, gcMs: 120, gen2Delta: 0, cpuMs: 145);
        Assert.Equal(StallCause.MinorGc, c);
    }

    [Fact]
    public void ClassifyCause_CpuMuchLessThanWall_IsProcessSuspended()
    {
        // 800 ms wall, 50 ms CPU — the process was sleeping (laptop closed,
        // app backgrounded, OS preempted us for another process).
        StallCause c = StallDetector.ClassifyCause(
            wallMs: 800, gcMs: 0, gen2Delta: 0, cpuMs: 50);
        Assert.Equal(StallCause.ProcessSuspended, c);
    }

    [Fact]
    public void ClassifyCause_CpuAdvancedNoGc_IsLongFrame()
    {
        // 300 ms wall, 290 ms CPU, no GC pause. Something blocked on the main
        // thread (lock contention, sync I/O in a draw hook, JIT compile).
        StallCause c = StallDetector.ClassifyCause(
            wallMs: 300, gcMs: 5, gen2Delta: 0, cpuMs: 290);
        Assert.Equal(StallCause.LongFrame, c);
    }

    [Fact]
    public void ClassifyCause_DegenerateZeroWall_IsUnknown()
    {
        StallCause c = StallDetector.ClassifyCause(
            wallMs: 0, gcMs: 0, gen2Delta: 0, cpuMs: 0);
        Assert.Equal(StallCause.Unknown, c);
    }

    [Fact]
    public void ClassifyCause_ProcessSuspendedTrumpsGcCheck()
    {
        // Edge case: gc reading happened to span the suspend window. CPU
        // delta is the most reliable suspend signal so it wins over a stale
        // gc reading that might look dominant.
        StallCause c = StallDetector.ClassifyCause(
            wallMs: 1000, gcMs: 600, gen2Delta: 1, cpuMs: 50);
        Assert.Equal(StallCause.ProcessSuspended, c);
    }

    // ---- ClassifySeverity: perceptual ladder -------------------------------

    [Theory]
    [InlineData(50, StallSeverity.Minor)]
    [InlineData(99, StallSeverity.Minor)]
    [InlineData(100, StallSeverity.Noticeable)]
    [InlineData(200, StallSeverity.Noticeable)]
    [InlineData(250, StallSeverity.Disruptive)]
    [InlineData(450, StallSeverity.Disruptive)]
    [InlineData(500, StallSeverity.Freeze)]
    [InlineData(1200, StallSeverity.Freeze)]
    public void ClassifySeverity_FollowsAbsolutePerceptualLadder(double wallMs, StallSeverity expected)
    {
        Assert.Equal(expected, StallDetector.ClassifySeverity(wallMs));
    }

    [Fact]
    public void ClassifySeverity_IsAbsoluteNotRelative()
    {
        // The point of perceptual severity: a 600 ms freeze is Freeze for
        // every player, regardless of whether their baseline is 8 ms (75x)
        // or 40 ms (15x). The eye doesn't care about the multiplier.
        Assert.Equal(StallSeverity.Freeze, StallDetector.ClassifySeverity(600));
    }
}
