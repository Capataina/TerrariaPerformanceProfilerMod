#nullable enable

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Detectors;
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
    public void ClassifyCause_CpuMuchLessThanWall_FocusLost_IsProcessSuspended()
    {
        // 800 ms wall, 50 ms CPU, focus LOST during the gap — the OS gave
        // focus to another app (real cmd-tab / sleep / background).
        StallCause c = StallDetector.ClassifyCause(
            wallMs: 800, gcMs: 0, gen2Delta: 0, cpuMs: 50,
            recentStallsInLast5s: 0, baselineMs: 16.67, focusHeldAcrossGap: false);
        Assert.Equal(StallCause.ProcessSuspended, c);
    }

    [Fact]
    public void ClassifyCause_CpuMuchLessThanWall_FocusHeld_IsMainThreadFreeze()
    {
        // Same wall + CPU shape, but the game kept focus throughout. This
        // is what the v0.4 playtest hit: a real game-thread freeze that
        // was misclassified as ProcessSuspended. v0.5 distinguishes them.
        StallCause c = StallDetector.ClassifyCause(
            wallMs: 1800, gcMs: 0, gen2Delta: 0, cpuMs: 50,
            recentStallsInLast5s: 0, baselineMs: 16.67, focusHeldAcrossGap: true);
        Assert.Equal(StallCause.MainThreadFreeze, c);
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
        // delta + focus-loss is the unambiguous suspend signal so it
        // wins over a stale gc reading that might look dominant.
        StallCause c = StallDetector.ClassifyCause(
            wallMs: 1000, gcMs: 600, gen2Delta: 1, cpuMs: 50,
            recentStallsInLast5s: 0, baselineMs: 16.67, focusHeldAcrossGap: false);
        Assert.Equal(StallCause.ProcessSuspended, c);
    }

    // ---- ClassifySeverity: relative-to-baseline ladder ---------------------

    [Theory]
    // At a 10 ms baseline so the multiplier math is exact (no floating-
    // point boundary cases). < 3× = Minor; 3–8× = Noticeable;
    // 8–20× = Disruptive; ≥ 20× = Freeze.
    [InlineData(20, 10, StallSeverity.Minor)]
    [InlineData(30, 10, StallSeverity.Noticeable)]
    [InlineData(79, 10, StallSeverity.Noticeable)]
    [InlineData(80, 10, StallSeverity.Disruptive)]
    [InlineData(199, 10, StallSeverity.Disruptive)]
    [InlineData(200, 10, StallSeverity.Freeze)]
    [InlineData(1200, 10, StallSeverity.Freeze)]
    public void ClassifySeverity_FollowsRelativeLadder(double wallMs, double baselineMs, StallSeverity expected)
    {
        Assert.Equal(expected, StallDetector.ClassifySeverity(wallMs, baselineMs));
    }

    [Fact]
    public void ClassifySeverity_Adapts_To_HighFps_Player()
    {
        // 120 fps player has 8.33 ms baseline. A 100 ms hitch is 12× baseline =
        // Disruptive. The old absolute ladder said 100 ms = "only Noticeable",
        // which under-reports how bad it actually felt to that player.
        Assert.Equal(StallSeverity.Disruptive, StallDetector.ClassifySeverity(100, 8.33));
    }

    [Fact]
    public void ClassifySeverity_Adapts_To_LowFps_Player()
    {
        // 30 fps modded player has ~33 ms baseline. A 100 ms hitch is only
        // 3× baseline = Noticeable. The old absolute ladder said 100 ms =
        // Noticeable too, which happens to coincide here — but a 250 ms
        // hitch on the same player is 7.5× = still Noticeable, where the
        // old ladder would have flagged it Disruptive and over-reported.
        Assert.Equal(StallSeverity.Noticeable, StallDetector.ClassifySeverity(100, 33));
        Assert.Equal(StallSeverity.Noticeable, StallDetector.ClassifySeverity(250, 33));
    }
}
