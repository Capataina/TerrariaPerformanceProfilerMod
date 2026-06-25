#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Insights.ReferenceFrames;
using PerformanceProfiler.Insights.Shared;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// CONTEXT_CONDITIONAL_COST — a mod costs significantly more while a game context
/// is active than the rest of the time ("Calamity runs 1.8× its baseline during a
/// Blood Moon"). Family A: the comparison is the same mod outside the context, so
/// the result is a deviation from a comparable baseline, never an absolute cost.
///
/// <para>
/// Un-gated in Wave 3 against the <see cref="ContextBaseline"/> substrate the engine
/// now feeds at 1 Hz. The detector is read-only over that frame, so it is pure logic
/// over declared input (the L1 test axis). It does not yet condition on biome or
/// weather dimensions — those are not vanilla surfaces tML exposes per-tick, so the
/// engine buckets on hardmode / boss-present / invasion / subworld only; that is an
/// honest scope statement, not a hidden gap.
/// </para>
///
/// <para>
/// <b>p-hacking guards (the plan's hard rule).</b> First line: a comparison is only a
/// candidate once both the in-context and out-of-context samples clear
/// <see cref="ContextBaseline.MinSamplesForTest"/> (enforced by
/// <see cref="ContextBaseline.TryConditional"/>) AND the effect is at least
/// <see cref="MinEffect"/> (a large Cohen's d) AND the cost is actually higher in
/// context. Second line: the Welch's-t p-value is Bonferroni-corrected by the number
/// of comparisons actually run this pass, so testing many (context, mod) pairs cannot
/// manufacture significance — the corrected p flows into the store's confidence gate,
/// which keeps an untested-looking record at Low.
/// </para>
/// </summary>
public sealed class ContextConditionalCostDetector : IInsightDetector
{
    /// <summary>Minimum Cohen's d (a large effect) before a difference is worth surfacing.</summary>
    private const double MinEffect = 0.8d;

    /// <summary>Cap on records emitted per pass; the strongest by effect size win (the store dedups across passes).</summary>
    private const int MaxEmitPerPass = 8;

    public PatternKey Pattern => PatternKey.ContextConditionalCost;
    public Audience DefaultAudience => Audience.Both;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector) => InsightsEngine.Shared?.ContextBaseline != null;

    private readonly List<Candidate> _candidates = new List<Candidate>(16);

    private struct Candidate
    {
        public long Bucket;
        public int ModId;
        public double InMean;
        public double OutMean;
        public double Effect;
        public double P;
        public long InN;
        public long OutN;
    }

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        ContextBaseline? cb = InsightsEngine.Shared?.ContextBaseline;
        if (cb == null) return;

        int modCount = cb.ModCount;
        _candidates.Clear();
        int testsRun = 0;

        foreach (long bucket in cb.Buckets)
        {
            for (int m = 0; m < modCount; m++)
            {
                if (!cb.TryConditional(bucket, m, out RunningStat inCtx, out RunningStat outCtx)) continue;
                testsRun++; // a real comparison ran — counts toward the Bonferroni denominator

                if (inCtx.Mean <= outCtx.Mean) continue; // only "costs MORE in context"
                double d = Stats.CohensD(inCtx, outCtx);
                if (d < MinEffect) continue;

                _candidates.Add(new Candidate
                {
                    Bucket = bucket,
                    ModId = m,
                    InMean = inCtx.Mean,
                    OutMean = outCtx.Mean,
                    Effect = d,
                    P = Stats.WelchTTestP(inCtx, outCtx),
                    InN = inCtx.Count,
                    OutN = outCtx.Count,
                });
            }
        }

        if (_candidates.Count == 0) return;

        // Bonferroni denominator. The tests are NOT independent — every bucket's
        // out-of-context complement is derived from the same global per-mod series —
        // so the effective number of independent tests is below testsRun and this
        // correction is conservative (it can suppress real signals, never manufacture
        // them). A dependence-aware step (Holm/BH) would recover that power.
        int adjust = testsRun < 1 ? 1 : testsRun;
        _candidates.Sort((a, b) => b.Effect.CompareTo(a.Effect));

        int take = _candidates.Count < MaxEmitPerPass ? _candidates.Count : MaxEmitPerPass;
        for (int i = 0; i < take; i++)
        {
            Candidate c = _candidates[i];
            double pAdjusted = Math.Min(1d, c.P * adjust);
            double ratio = c.OutMean > 1e-9 ? c.InMean / c.OutMean : 0d;

            emit.Add(new Insight
            {
                Pattern = PatternKey.ContextConditionalCost,
                // A mod-in-context subject: this mod, while this context bucket is
                // active. The full-width dedup key keeps (mod, context) pairs distinct.
                Subject = new SubjectRef(SubjectKind.Context, c.ModId, -1,
                    ContextBaseline.BucketValue(c.Bucket), ContextBaseline.BucketDim(c.Bucket)),
                Magnitude = new Magnitude
                {
                    Shape = MagnitudeShape.Deviation,
                    BaselineMs = c.OutMean,
                    ObservedMs = c.InMean,
                    RatioOrDelta = ratio,
                    Count = (int)Math.Min(int.MaxValue, c.InN),
                },
                Evidence = new Evidence
                {
                    SampleN = (int)Math.Min(int.MaxValue, c.InN),
                    BaselineN = (int)Math.Min(int.MaxValue, c.OutN),
                    PValue = c.P,
                    PValueAdjusted = pAdjusted,
                    EffectSize = c.Effect,
                    Baseline = BaselineKind.ComparableContexts,
                },
                Confidence = Confidence.Preliminary, // the store promotes on confirmations + pAdjusted
                Audience = Audience.Both,
                // LifetimeData once the frame carries prior-session baselines (Wave 6);
                // the larger sample count is also what lets confidence climb past Low.
                Scope = cb.WasSeeded ? EvidenceScope.LifetimeData : EvidenceScope.ThisSession,
                ConfirmationCount = 1,
            });
        }
    }
}
