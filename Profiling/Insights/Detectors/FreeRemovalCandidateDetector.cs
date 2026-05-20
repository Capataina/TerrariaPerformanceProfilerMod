#nullable enable

using System.Collections.Generic;

namespace PerformanceProfiler.Profiling.Insights.Detectors;

/// <summary>
/// FREE_REMOVAL_CANDIDATE — mod's session cost sits below an epsilon ms/tick
/// floor across the whole observed history.
///
/// <para>
/// <b>Currently gated.</b> The plan (§4.9) requires a real engagement signal
/// (Events tab boss/biome buckets + persistence-backed lifetime visits) before
/// this pattern can responsibly fire. <see cref="InsightsEngine.Evaluate"/>
/// skips gated detectors before calling <see cref="Evaluate"/>, so this
/// detector currently emits zero records — it's registered solely so the
/// roster + gate visibility is honest in the overlay's gated-pattern label.
/// The <see cref="Evaluate"/> implementation stays in place against the day
/// the gate clears, and the resulting record's <see cref="InsightRecord.Scope"/>
/// is already set to <see cref="EvidenceScope.NeedsPersistence"/>.
/// </para>
///
/// <para>
/// Honesty contract: the rendered string is descriptive only. It states the
/// cost and the absence of a measured engagement signal, badges the time
/// window as "this session", and never says "remove this mod". See plan §8.3.
/// </para>
/// </summary>
public sealed class FreeRemovalCandidateDetector : IInsightDetector
{
    /// <summary>A mod is "cheap" if its smoothed session cost is at or below this many ms/tick.</summary>
    public const double EpsilonMsPerTick = 0.10;

    /// <summary>Minimum session length before the detector emits anything (plan §5.6).</summary>
    public const long MinSessionTicks = 60L * 60L * 30L;

    public PatternKey Pattern => PatternKey.FreeRemovalCandidate;
    public Audience DefaultAudience => Audience.Player;

    /// <summary>Gated until an engagement signal is wired (plan §4.9). Records still emit; the renderer flags the gate.</summary>
    public bool IsGated => true;
    public string? GatedOn => "engagement-signal";

    public bool IsAvailable(MetricCollector collector) => collector.History.Count > 0;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<InsightRecord> emit)
    {
        if (sessionLengthTicks < MinSessionTicks) return;

        IReadOnlyList<double> categoryMs = collector.PerModCategoryAverageMs;
        string[] modNames = HookInterceptor.ProfiledModNames;
        int catCount = PerModAttribution.CategoryCount;

        for (int modId = 0; modId < modNames.Length; modId++)
        {
            double modTotal = 0d;
            for (int c = 0; c < catCount; c++)
            {
                int cell = modId * catCount + c;
                if (cell < categoryMs.Count) modTotal += categoryMs[cell];
            }
            if (modTotal > EpsilonMsPerTick) continue;

            emit.Add(new InsightRecord
            {
                Pattern = Pattern,
                Subject = SubjectRef.ForMod(modId),
                Magnitude = new Magnitude
                {
                    BaselineMs = EpsilonMsPerTick,
                    ObservedMs = modTotal,
                    RatioOrDelta = 0d,
                    AllocBytes = 0,
                    Count = collector.History.Count,
                },
                Evidence = new Evidence
                {
                    SampleN = collector.History.Count,
                    BaselineN = 0,
                    PValue = 1d,
                    EffectSize = 0d,
                    PValueAdjusted = 1d,
                    FirstTickIndex = 0,
                    LastTickIndex = nowTick,
                    Baseline = BaselineKind.SessionMean,
                },
                Confidence = Confidence.Preliminary,
                Audience = DefaultAudience,
                Scope = EvidenceScope.NeedsPersistence,
            });
        }
    }
}
