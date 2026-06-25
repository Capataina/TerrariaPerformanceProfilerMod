#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Insights.ReferenceFrames;
using PerformanceProfiler.Insights.Shared;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// CONTEXT_CORRELATED_SPIKE — Family A. Lag spikes cluster in a context far more than
/// that context's share of playtime would predict ("3 in 4 of your spikes hit while a
/// boss was alive, but bosses were alive only a fifth of the time"). The comparison is
/// the context's dwell fraction, so it is relative, not an absolute spike count.
///
/// <para>
/// Un-gated against the spike-per-context counts the <see cref="ContextBaseline"/> now
/// accumulates. A binomial test (normal-approximated, one-sided for over-representation)
/// asks whether a context's spike share exceeds its dwell share by more than chance.
/// Guards: candidate-gating (enough total spikes AND enough in the bucket AND actually
/// over-represented) before the test; Bonferroni over the contexts tested.
/// </para>
///
/// <para>
/// Honest about its approximation: a spike is credited to the context live at the 1 Hz
/// pass that first sees it (exact unless the context changed within that ~1 s window),
/// stated in <see cref="ContextBaseline.ObserveSpikes"/>.
/// </para>
/// </summary>
public sealed class ContextCorrelatedSpikeDetector : IInsightDetector
{
    private const long MinTotalSpikes = 8;
    private const long MinBucketSpikes = 3;
    private const double MinLift = 1.5d; // spike share at least 1.5x the dwell share
    private const int MaxEmitPerPass = 4;

    public PatternKey Pattern => PatternKey.ContextCorrelatedSpike;
    public Audience DefaultAudience => Audience.Both;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector) => InsightsEngine.Shared?.ContextBaseline != null;

    private readonly List<(long bucket, double lift, double p, long spikes)> _cand = new();

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        ContextBaseline? cb = InsightsEngine.Shared?.ContextBaseline;
        if (cb == null || cb.TotalSpikes < MinTotalSpikes || cb.TotalSamples <= 0) return;

        _cand.Clear();
        int tests = 0;
        foreach (long bucket in cb.Buckets)
        {
            long bucketSpikes = cb.BucketSpikeCount(bucket);
            if (bucketSpikes < MinBucketSpikes) continue;
            long dwell = cb.BucketSampleCount(bucket);
            if (dwell <= 0) continue;
            tests++;

            double dwellFraction = (double)dwell / cb.TotalSamples;
            double spikeFraction = (double)bucketSpikes / cb.TotalSpikes;
            if (dwellFraction <= 0d || dwellFraction >= 1d) continue;
            double lift = spikeFraction / dwellFraction;
            if (lift < MinLift) continue;

            // One-sided binomial via normal approximation: is the spike share above the
            // dwell share by more than sampling noise?
            double se = Math.Sqrt(dwellFraction * (1d - dwellFraction) / cb.TotalSpikes);
            double z = se > 1e-12 ? (spikeFraction - dwellFraction) / se : 0d;
            double p = 1d - Stats.NormalCdf(z);
            _cand.Add((bucket, lift, p, bucketSpikes));
        }
        if (_cand.Count == 0) return;

        // Bonferroni denominator. The per-context tests all lean on the same total
        // spike/dwell counts, so they are not independent; the effective test count is
        // below tests and the correction is conservative (suppresses real correlations,
        // never manufactures them). A Holm/BH step would recover the lost power.
        int adjust = tests < 1 ? 1 : tests;
        _cand.Sort((a, b) => b.lift.CompareTo(a.lift));
        int take = Math.Min(_cand.Count, MaxEmitPerPass);
        for (int i = 0; i < take; i++)
        {
            var c = _cand[i];
            emit.Add(new Insight
            {
                Pattern = PatternKey.ContextCorrelatedSpike,
                Subject = SubjectRef.ForContext(ContextBaseline.BucketValue(c.bucket), ContextBaseline.BucketDim(c.bucket)),
                Magnitude = new Magnitude
                {
                    Shape = MagnitudeShape.Deviation,
                    // spike share / dwell share — an over-representation LIFT ratio (can be >> 1),
                    // NOT a [0,1] share. Correctly excluded from RankingScorer.IsSharePattern so it
                    // ranks via RatioCurve; do not move it into the share set, which would clamp a
                    // legitimate 3× lift and lose the very over-representation this pattern measures.
                    RatioOrDelta = c.lift,
                    Count = (int)Math.Min(int.MaxValue, c.spikes),
                },
                Evidence = new Evidence
                {
                    SampleN = (int)Math.Min(int.MaxValue, c.spikes),
                    BaselineN = (int)Math.Min(int.MaxValue, cb.TotalSpikes),
                    PValue = c.p,
                    PValueAdjusted = Math.Min(1d, c.p * adjust),
                    Baseline = BaselineKind.ComparableContexts,
                },
                Confidence = Confidence.Preliminary,
                Audience = Audience.Both,
                Scope = EvidenceScope.ThisSession,
                ConfirmationCount = 1,
            });
        }
    }
}
