#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// FRAME_JITTER — Family C (shape of the distribution). Distinguishes <i>stutter</i>
/// (frame times that swing widely around their median — many small hitches) from a
/// <i>smooth</i> session, using the robust coefficient of variation MAD/median off
/// the calibrated baseline. High dispersion at a fine median is the "stutter, not
/// slowdown" signal the plan calls out; it is a different complaint from a high
/// median (which FrameHeadroom covers).
///
/// <para>
/// Selective: only emits when the jitter is actually notable
/// (CV ≥ <see cref="JitterThreshold"/>). A steady session has nothing to say.
/// A direct measurement (no hypothesis test), so it sits at Low confidence —
/// honest about being a single-session observation. Subject is the session.
/// </para>
/// </summary>
public sealed class FrameJitterDetector : IInsightDetector
{
    /// <summary>MAD/median at or above this reads as stutter worth surfacing (~15% swing).</summary>
    private const double JitterThreshold = 0.15d;

    public PatternKey Pattern => PatternKey.FrameJitter;
    public Audience DefaultAudience => Audience.Player;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector) => collector.Baseline.IsCalibrated;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        double median = collector.Baseline.FrameMsMedian;
        double mad = collector.Baseline.FrameMsMad;
        if (median <= 0d) return;

        double cv = mad / median; // robust coefficient of variation
        if (cv < JitterThreshold) return;

        emit.Add(new Insight
        {
            Pattern = PatternKey.FrameJitter,
            Subject = SubjectRef.ForSession(),
            Magnitude = new Magnitude
            {
                Shape = MagnitudeShape.Distribution,
                P50 = median,
                ObservedMs = mad,
                RatioOrDelta = cv, // the jitter fraction (ranks as a share)
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
