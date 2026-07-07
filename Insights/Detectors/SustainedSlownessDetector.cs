#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Stats;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// SUSTAINED_SLOWNESS — Family D (relative to a ceiling): the game has been
/// delivering less than real-time speed continuously. The level-detector
/// counterpart to the spike/stall variance detectors; the maths and fire
/// conditions live in <see cref="SustainedSlownessCore"/> (pure, unit-tested).
///
/// <para>
/// Mutually exclusive with <see cref="FrameHeadroomDetector"/> by
/// construction: headroom requires ≥98% speed, slowness requires &lt;90% held
/// for ≥30 s. A direct measurement, not a hypothesis — PValueAdjusted stays 1
/// and confidence Preliminary (honest single-session observation).
/// </para>
/// </summary>
public sealed class SustainedSlownessDetector : IInsightDetector
{
    public PatternKey Pattern => PatternKey.SustainedSlowness;
    public Audience DefaultAudience => Audience.Player;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector) => collector.Baseline.IsCalibrated;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        SustainedSlownessResult? result = SustainedSlownessCore.Compute(
            collector.RealtimeSpeedNow,
            collector.ConsecutiveSlowMs,
            collector.TimeBelowThresholdMs,
            collector.DeficitMsPerSecond,
            collector.PerModCategoryMs,
            PerModAttribution.ModCount,
            PerModAttribution.CategoryCount);
        if (result == null) return;
        SustainedSlownessResult res = result.Value;

        emit.Add(new Insight
        {
            Pattern = PatternKey.SustainedSlowness,
            Subject = SubjectRef.ForSession(),
            Magnitude = new Magnitude
            {
                Shape = MagnitudeShape.Headroom,
                Ceiling = 1d,                      // full real-time speed
                Remaining = res.Speed,             // where the game actually sits
                ObservedMs = res.ConsecutiveSlowMs,
                RecoveryMs = res.TimeBelowMs,      // session-cumulative slow time
                RatioOrDelta = 1d - res.Speed,     // severity as a [0,1] share for ranking
            },
            Evidence = new Evidence
            {
                SampleN = collector.History.Count,
                PValue = 1d,
                PValueAdjusted = 1d,
                Baseline = BaselineKind.None,
            },
            Contributors = res.Contributors.Count > 0 ? res.Contributors : null,
            Confidence = Confidence.Preliminary,
            Audience = Audience.Player,
            Scope = EvidenceScope.ThisSession,
            ConfirmationCount = 1,
        });
    }
}
