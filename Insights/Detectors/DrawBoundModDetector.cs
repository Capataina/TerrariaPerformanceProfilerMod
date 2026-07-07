#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Aggregators;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// DRAW_BOUND_MOD (atlas S01) — surfaces mods whose cost is dominated by the
/// draw phase. Maths and fire conditions in <see cref="DrawBoundModCore"/>
/// (pure, unit-tested). One record per draw-bound leader, refreshed each pass;
/// silent when the phase lanes are disabled in config.
/// </summary>
public sealed class DrawBoundModDetector : IInsightDetector
{
    public PatternKey Pattern => PatternKey.DrawBoundMod;
    public Audience DefaultAudience => Audience.Player;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector)
        => PerModAttribution.PhaseLanesEnabled && collector.Baseline.IsCalibrated;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        List<DrawBoundModResult> found = DrawBoundModCore.Compute(
            collector.PerModCategoryMs,
            collector.PerModCategoryDrawMs,
            PerModAttribution.ModCount,
            PerModAttribution.CategoryCount);

        for (int i = 0; i < found.Count; i++)
        {
            DrawBoundModResult r = found[i];
            emit.Add(new Insight
            {
                Pattern = PatternKey.DrawBoundMod,
                Subject = SubjectRef.ForMod(r.ModId),
                Magnitude = new Magnitude
                {
                    Shape = MagnitudeShape.Share,
                    RatioOrDelta = r.DrawShare,   // draw share, [0,1]
                    ObservedMs = r.TotalMs,       // the mod's smoothed total
                    BaselineMs = r.DrawMs,        // the draw slice of it
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
}
