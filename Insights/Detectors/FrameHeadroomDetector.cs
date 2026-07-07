#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// FRAME_HEADROOM — Family D (relative to a ceiling). "Update work uses
/// ~8 ms of the 16.7 ms budget — ~8 ms of compute headroom free." The number
/// the 99-mod plan needs to answer "how much more can I add".
///
/// <para>
/// <b>Reworked in the 2026-07-07 honesty pass (X1).</b> The old form read the
/// baseline frame median and phrased the result as "you sustain 60 fps" —
/// which was captured live claiming exactly that during 31-fps slow-motion,
/// because the median it read was update-window compute time. Two changes
/// close the class: (1) the detector now reads the update-window EMA as a
/// deliberate compute-budget input (the baseline median is real-cadence now
/// and sits at ~16.67 ms under vsync by construction — useless for headroom),
/// and (2) it emits ONLY while <see cref="MetricCollector.RealtimeSpeedNow"/>
/// proves the game genuinely holds full speed. During slow-motion it stays
/// silent and <see cref="SustainedSlownessDetector"/> speaks instead — the
/// two are mutually exclusive by construction. The copy also names what the
/// number does not cover (draw-phase cost, unattributed until the
/// loop-anatomy slot lands).
/// </para>
///
/// <para>
/// A direct measurement, not a hypothesis, so it carries no statistical test
/// (PValueAdjusted = 1) and the store keeps it at Low confidence — honest: a
/// single-session observation. One record, refreshed each pass.
/// </para>
/// </summary>
public sealed class FrameHeadroomDetector : IInsightDetector
{
    /// <summary>The 60 fps frame budget in milliseconds (the ceiling).</summary>
    public const double FrameBudgetMs = 1000d / 60d;

    /// <summary>The emission gate lives in the pure, test-linked <see cref="Data.Stats.RealtimeSpeed"/>.</summary>
    public const double FullSpeedGate = Data.Stats.RealtimeSpeed.FullSpeedGate;

    public PatternKey Pattern => PatternKey.FrameHeadroom;
    public Audience DefaultAudience => Audience.Player;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector) => collector.Baseline.IsCalibrated;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        // The gate that kills X1: no full speed, no headroom claim.
        if (collector.RealtimeSpeedNow < FullSpeedGate) return;

        double median = collector.UpdateWindowEmaMs;
        if (median <= 0d) return;

        double remaining = FrameBudgetMs - median;
        double usedFraction = median / FrameBudgetMs; // 1.0 = at budget; >1 = over

        emit.Add(new Insight
        {
            Pattern = PatternKey.FrameHeadroom,
            Subject = SubjectRef.ForSession(),
            Magnitude = new Magnitude
            {
                Shape = MagnitudeShape.Headroom,
                Ceiling = FrameBudgetMs,
                Remaining = remaining,
                ObservedMs = median,
                RatioOrDelta = usedFraction < 0d ? 0d : usedFraction, // ranks by budget pressure
            },
            Evidence = new Evidence
            {
                SampleN = collector.History.Count,
                PValue = 1d,
                PValueAdjusted = 1d,
                Baseline = BaselineKind.None,
            },
            Confidence = Confidence.Preliminary,
            Audience = Audience.Player,
            Scope = EvidenceScope.ThisSession,
            ConfirmationCount = 1,
        });
    }
}
