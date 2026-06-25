#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Insights.ReferenceFrames;
using PerformanceProfiler.Insights.Shared;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// SUSTAINED_COST_SHIFT — Family B. A mod's cost moved to a new, higher level partway
/// through the session and stayed there ("Calamity settled from 0.4 to 1.1 ms/t after
/// the first two minutes"). The comparison is the same mod's early-session window, so
/// it is a deviation from a comparable baseline, not an absolute.
///
/// <para>
/// Un-gated against the <see cref="TemporalBaseline"/>. Same two p-hacking guards as
/// the context detector: candidate-gating (both windows past the sample floor, a LARGE
/// effect, and an actual increase) before any test; Bonferroni over the comparisons
/// run, so sweeping every mod cannot manufacture significance.
/// </para>
/// </summary>
public sealed class SustainedCostShiftDetector : IInsightDetector
{
    private const double MinEffect = 0.8d;
    private const double MinLateMs = 0.05d; // ignore mods that are still essentially idle
    private const int MaxEmitPerPass = 6;

    public PatternKey Pattern => PatternKey.SustainedCostShift;
    public Audience DefaultAudience => Audience.Both;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector) => InsightsEngine.Shared?.TemporalBaseline?.IsReady == true;

    private readonly List<(int mod, double early, double late, double effect, double p)> _cand = new();

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        TemporalBaseline? tb = InsightsEngine.Shared?.TemporalBaseline;
        if (tb == null || !tb.IsReady) return;

        _cand.Clear();
        int tests = 0;
        for (int m = 0; m < tb.ModCount; m++)
        {
            if (!tb.TryPerMod(m, out RunningStat early, out RunningStat late)) continue;
            tests++;
            if (late.Mean <= early.Mean || late.Mean < MinLateMs) continue;
            double d = Stats.CohensD(late, early);
            if (d < MinEffect) continue;
            _cand.Add((m, early.Mean, late.Mean, d, Stats.WelchTTestP(late, early)));
        }
        if (_cand.Count == 0) return;

        // Bonferroni denominator. The per-mod early/late tests share the same temporal
        // baseline, so they are not independent; the effective test count is below tests
        // and the correction is conservative (suppresses real shifts, never manufactures
        // them). A Holm/BH step would recover the lost power.
        int adjust = tests < 1 ? 1 : tests;
        _cand.Sort((a, b) => b.effect.CompareTo(a.effect));
        int take = Math.Min(_cand.Count, MaxEmitPerPass);
        for (int i = 0; i < take; i++)
        {
            var c = _cand[i];
            emit.Add(new Insight
            {
                Pattern = PatternKey.SustainedCostShift,
                Subject = SubjectRef.ForMod(c.mod),
                Magnitude = new Magnitude
                {
                    Shape = MagnitudeShape.Deviation,
                    BaselineMs = c.early,
                    ObservedMs = c.late,
                    RatioOrDelta = c.early > 1e-9 ? c.late / c.early : 0d,
                },
                Evidence = new Evidence
                {
                    PValue = c.p,
                    PValueAdjusted = Math.Min(1d, c.p * adjust),
                    EffectSize = c.effect,
                    Baseline = BaselineKind.SessionFirstHalf,
                },
                Confidence = Confidence.Preliminary,
                Audience = Audience.Both,
                Scope = EvidenceScope.ThisSession,
                ConfirmationCount = 1,
            });
        }
    }
}
