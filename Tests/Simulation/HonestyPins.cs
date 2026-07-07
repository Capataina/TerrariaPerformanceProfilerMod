#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Stats;
using Xunit;

namespace PerformanceProfiler.Tests.Simulation;

/// <summary>
/// The honesty battery (e2e plan Ring 1): every pin here is a bug class that
/// actually shipped, re-expressed as a scenario assertion so reintroducing
/// the class fails the suite instead of a playtest. X-numbers reference
/// context/plans/ui-ux-audit.md; the scenarios mirror the live sessions that
/// exposed each class on 2026-07-07.
/// </summary>
public sealed class HonestyPins
{
    // ---------------------------------------------------------------- X1

    [Fact]
    public void Slowmo30_ReadsAsSlowMotion_NotSixtyFps()
    {
        var r = ScenarioRunner.Run(Scenarios.Slowmo30());

        // The dashboard's headline numbers must say what the player feels.
        Assert.InRange(r.Kpi.AvgFps, 28d, 32d);
        Assert.InRange(r.Kpi.MedianFrameMs, 32d, 35d);
        Assert.InRange(r.RealtimeSpeed, 0.45d, 0.55d);

        // The X1 sentence is impossible by construction: the headroom gate is
        // closed, and the paired level detector fires instead.
        Assert.False(r.HeadroomGateOpen,
            "FrameHeadroom's emission gate must be CLOSED during slow-motion — " +
            "the 'you sustain 60 fps' class (X1) regressed.");
        var slowness = r.Slowness;
        Assert.NotNull(slowness);
        Assert.InRange(slowness!.Value.Speed, 0.45d, 0.55d);
        Assert.True(slowness.Value.ConsecutiveSlowMs >= RealtimeSpeed.SustainedFireMs);
    }

    [Fact]
    public void Healthy60_ReadsHealthy_AndHeadroomGateOpens()
    {
        var r = ScenarioRunner.Run(Scenarios.Healthy60());

        Assert.InRange(r.Kpi.AvgFps, 58d, 62d);
        Assert.True(r.RealtimeSpeed >= 0.98d);
        Assert.True(r.HeadroomGateOpen);
        Assert.Null(r.Slowness);
        Assert.Equal(0, r.Kpi.LagSpikeCount);
        // Update-window EMA carries the compute budget the reworked headroom
        // insight reasons about — it must reflect the script's compute time,
        // not the loop period.
        Assert.InRange(r.UpdateWindowEmaMs, 3.8d, 4.6d);
    }

    // ---------------------------------------------------------------- X2

    [Fact]
    public void Slowmo30_ProducesZeroVarianceEvents_ButTheLevelSignalSpeaks()
    {
        var r = ScenarioRunner.Run(Scenarios.Slowmo30());

        // The old Lag tab's lie, pinned: uniform slowness produces NO spikes
        // and NO stalls (variance detectors are level-blind by design)...
        Assert.Equal(0, r.Spikes.Windows.Count);
        Assert.Equal(0, r.Kpi.StallCount);

        // ...which is exactly why the level signal must exist and fire.
        Assert.True(r.TimeBelowThresholdMs > 100_000d,
            "a 2-minute half-speed session must accumulate visible slow time");
        Assert.True(r.Kpi.DeficitMsPerSecond > 400d,
            "at ~50% speed the game loses ~500 ms of game time per wall second");
    }

    // ---------------------------------------------------------------- X3

    [Fact]
    public void AltTabbed_SuspendsAreExcludedFromStallHeadline_AndDoNotCraterSpeed()
    {
        var r = ScenarioRunner.Run(Scenarios.AltTabbed());

        // The three long gaps must classify as pauses, not stalls: the
        // headline may keep sub-second real events, but never a 25-45 s one,
        // and the pause bucket must carry roughly the injected 111 s.
        Assert.True(r.Kpi.WorstStallMs < 5_000d,
            $"a suspend leaked into the stall headline (worst={r.Kpi.WorstStallMs:F0}ms) — the 122s-alt-tab class (X3) regressed");
        Assert.True(r.Kpi.PauseCount >= 3);
        Assert.InRange(r.Kpi.PausedMs, 100_000d, 122_000d);

        // The suspend guard: pauses fall back to compute time in the real-
        // frame series, so the speed metric stays honest about PLAY time.
        Assert.True(r.RealtimeSpeed >= 0.95d,
            $"alt-tabs polluted the realtime-speed metric (speed={r.RealtimeSpeed:F2})");
        Assert.Null(r.Slowness);
    }

    // ---------------------------------------------------------------- variance still works

    [Fact]
    public void Spiky_FiresSpikes_ButNeverSustainedSlowness()
    {
        var r = ScenarioRunner.Run(Scenarios.Spiky());

        Assert.True(r.Spikes.Windows.Count >= 8,
            $"the spike detector went blind (windows={r.Spikes.Windows.Count})");
        // Spike worst is the real-cadence hitch, not the compute time (H4).
        double worst = 0d;
        for (int i = 0; i < r.Spikes.Windows.Count; i++)
        {
            if (r.Spikes.Windows[i].WorstFrameMs > worst) worst = r.Spikes.Windows[i].WorstFrameMs;
        }
        Assert.InRange(worst, 115d, 125d);
        Assert.Null(r.Slowness);
        Assert.True(r.RealtimeSpeed >= 0.9d);
    }

    // ---------------------------------------------------------------- warming

    [Fact]
    public void Warming_StaysQuiet()
    {
        var r = ScenarioRunner.Run(Scenarios.Warming());

        Assert.False(r.Baseline.IsCalibrated);
        Assert.Equal(0, r.Spikes.Windows.Count);
        Assert.Equal(0, r.Kpi.StallCount);
        Assert.Equal(45, r.Kpi.SampleN);
    }

    // ---------------------------------------------------------------- RealtimeSpeed maths

    [Theory]
    [InlineData(16.67, 0.99, 1.01)]   // at budget ⇒ full speed (clamped at 1)
    [InlineData(33.33, 0.49, 0.51)]   // 2× budget ⇒ half speed
    [InlineData(8.0, 0.99, 1.01)]     // faster than budget ⇒ still 1.0 (60 UPS cap)
    public void SpeedFrom_MapsPeriodsToSpeedFractions(double emaMs, double lo, double hi)
    {
        Assert.InRange(RealtimeSpeed.SpeedFrom(emaMs), lo, hi);
    }

    [Fact]
    public void Deficit_AtHalfSpeed_IsAboutHalfASecondPerSecond()
    {
        Assert.InRange(RealtimeSpeed.DeficitMsPerSecond(33.33), 480d, 520d);
        Assert.Equal(0d, RealtimeSpeed.DeficitMsPerSecond(16.0));
    }
}
