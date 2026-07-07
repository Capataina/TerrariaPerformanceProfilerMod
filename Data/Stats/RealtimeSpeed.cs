#nullable enable

using System;

namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// Pure math for the sustained-slowness signal (the X2 fix, 2026-07-07 honesty
/// pass): how fast is the game running relative to real time?
///
/// <para>
/// Terraria updates at a fixed 60 UPS. When the full loop (update + draw)
/// cannot finish inside 16.67 ms, the game does not drop update ticks — game
/// TIME dilates (slow motion) while an fps counter can still read 60. The
/// spike/stall detectors are variance detectors and structurally cannot see
/// uniform slowness (a session running at a steady 33 ms/frame produces zero
/// "events"). This stat measures the level directly: an EMA of the real
/// inter-frame period, expressed as a fraction of real-time speed.
/// </para>
///
/// <para>
/// Pure static folds so the maths is unit-testable off-game (the house
/// pure-core pattern); <see cref="Profiling.MetricCollector"/> owns the state
/// and calls the folds once per tick — zero allocation, Invariant 2.
/// </para>
/// </summary>
public static class RealtimeSpeed
{
    /// <summary>The 60 UPS tick budget in milliseconds.</summary>
    public const double TargetTickMs = 1000d / 60d;

    /// <summary>
    /// EMA factor for the real-frame period. ~0.02 ⇒ a ~50-tick (≈1 s at
    /// 60 UPS) time constant: fast enough that entering/leaving slow-motion
    /// shows within a second or two, slow enough that a single long frame
    /// does not read as "the game is slow".
    /// </summary>
    public const double Smoothing = 0.02d;

    /// <summary>Below this fraction of real-time speed the session counts as "slowed".</summary>
    public const double SlowThreshold = 0.90d;

    /// <summary>
    /// Minimum speed at which "you have headroom" is a defensible claim —
    /// the FrameHeadroom detector's emission gate (the X1 fix): a headroom
    /// insight may only exist while the game measurably holds full speed.
    /// </summary>
    public const double FullSpeedGate = 0.98d;

    /// <summary>Sustained-slowness insights require at least this long continuously below threshold.</summary>
    public const double SustainedFireMs = 30_000d;

    /// <summary>One EMA fold of the real inter-frame period. Seeds on the first sample.</summary>
    public static double Fold(double emaMs, double realFrameMs)
        => emaMs <= 0d ? realFrameMs : emaMs + Smoothing * (realFrameMs - emaMs);

    /// <summary>
    /// Real-time speed fraction from the period EMA. Clamped to [0, 1]: the
    /// update rate is capped at 60 UPS, so "faster than real time" is not a
    /// state the game can be in — an EMA under 16.67 ms just means vsync
    /// slack, not extra speed.
    /// </summary>
    public static double SpeedFrom(double emaMs)
        => emaMs > 0d ? Math.Clamp(TargetTickMs / emaMs, 0d, 1d) : 1d;

    /// <summary>
    /// How many milliseconds of game-time progress are lost per wall-clock
    /// second at the current period EMA. 0 when at full speed. At 33 ms/frame
    /// the game achieves ~30 of its 60 ticks per second ⇒ ~500 ms lost/s.
    /// </summary>
    public static double DeficitMsPerSecond(double emaMs)
    {
        if (emaMs <= TargetTickMs) return 0d;
        double ticksPerSecond = 1000d / emaMs;          // achieved update rate
        double gameMsPerSecond = ticksPerSecond * TargetTickMs; // game-time delivered
        return 1000d - gameMsPerSecond;                  // wall ms of progress lost
    }
}
