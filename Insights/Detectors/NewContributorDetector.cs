#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Insights.ReferenceFrames;
using PerformanceProfiler.Insights.Shared;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// NEW_CONTRIBUTOR — Family B. A mod that was idle early in the session started
/// costing later on ("Fargo's was ~0 ms/t for the first two minutes, then climbed to
/// 0.7 ms/t"). A special case of a cost shift where the early baseline is near zero,
/// so it reads as "came online" rather than "got heavier" — often a mechanic that
/// only activates under conditions met later (a boss, a biome, a spawned structure).
///
/// <para>
/// Un-gated against the <see cref="TemporalBaseline"/>. Candidate-gating: the early
/// window must be essentially idle (&lt; <see cref="IdleMs"/>) and the late window
/// clearly active (&gt;= <see cref="ActiveMs"/>) with a large effect; Bonferroni over
/// the comparisons run.
/// </para>
/// </summary>
public sealed class NewContributorDetector : IInsightDetector
{
    private const double IdleMs = 0.02d;    // early window must be near-silent
    private const double ActiveMs = 0.10d;  // late window must be clearly contributing
    private const double MinEffect = 0.8d;
    private const int MaxEmitPerPass = 6;

    public PatternKey Pattern => PatternKey.NewContributor;
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
            if (early.Mean >= IdleMs || late.Mean < ActiveMs) continue; // not an idle→active transition
            double d = Stats.CohensD(late, early);
            if (d < MinEffect) continue;
            _cand.Add((m, early.Mean, late.Mean, d, Stats.WelchTTestP(late, early)));
        }
        if (_cand.Count == 0) return;

        // Bonferroni denominator. The per-mod idle→active tests share the same temporal
        // baseline, so they are not independent; the effective test count is below tests
        // and the correction is conservative (suppresses real transitions, never
        // manufactures them). A Holm/BH step would recover the lost power.
        int adjust = tests < 1 ? 1 : tests;
        _cand.Sort((a, b) => b.late.CompareTo(a.late)); // surface the biggest new costs first
        int take = Math.Min(_cand.Count, MaxEmitPerPass);
        for (int i = 0; i < take; i++)
        {
            var c = _cand[i];
            emit.Add(new Insight
            {
                Pattern = PatternKey.NewContributor,
                Subject = SubjectRef.ForMod(c.mod),
                Magnitude = new Magnitude
                {
                    Shape = MagnitudeShape.Deviation,
                    BaselineMs = c.early,
                    ObservedMs = c.late,
                    // ~from-zero sentinel. NewContributor is a ratio pattern, so RankingScorer.RatioCurve
                    // saturates 99 to magnitude 1.0 — every from-zero record pins maximum magnitude
                    // regardless of how small the late cost actually is (the player copy renders `late` ms,
                    // not this ratio, so the surface stays honest; the distortion is ranking-order only).
                    // Ranking it on the late level (a MagnitudeShape.Rate) would be more honest but is a
                    // behaviour-changing follow-on, out of audit scope.
                    RatioOrDelta = c.early > 1e-9 ? c.late / c.early : 99d,
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
