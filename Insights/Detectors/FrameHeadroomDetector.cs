#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// FRAME_HEADROOM — Family D (relative to a ceiling). "You sustain 60 fps with
/// ~3 ms of frame budget free." The reference frame is the 60 fps budget
/// (16.67 ms/frame); the insight is how much of it is unspent, which is the
/// number the 99-mod plan needs to answer "how much more can I add".
///
/// <para>
/// A direct measurement off the calibrated session baseline, not a hypothesis,
/// so it carries no statistical test (PValueAdjusted = 1) and the store keeps it
/// at Low confidence — honest: it is a single-session observation, not a strong
/// cross-session claim. Subject is the session (a process-level subject, newly
/// expressible after Wave 0). One record, refreshed each pass.
/// </para>
/// </summary>
public sealed class FrameHeadroomDetector : IInsightDetector
{
    /// <summary>The 60 fps frame budget in milliseconds (the ceiling).</summary>
    public const double FrameBudgetMs = 1000d / 60d;

    public PatternKey Pattern => PatternKey.FrameHeadroom;
    public Audience DefaultAudience => Audience.Player;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector) => collector.Baseline.IsCalibrated;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        double median = collector.Baseline.FrameMsMedian;
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
