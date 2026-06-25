#nullable enable

using System.Collections.Generic;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Insights.Detectors;

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
/// the gate clears, and the resulting record's <see cref="Insight.Scope"/>
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
    /// <summary>
    /// A mod counts as "cheap" if its smoothed session cost is at or below
    /// this fraction of the user's median frame time. Relative, not absolute:
    /// 5% of a 16 ms baseline ≈ 0.8 ms/tick; 5% of a 40 ms baseline (a 25 fps
    /// player) ≈ 2 ms/tick. Both ratios mean the same thing in player-felt
    /// terms — the mod barely registers against everything else they're
    /// running — even though the absolute thresholds differ by 2.5×.
    /// </summary>
    public const double EpsilonFractionOfBaselineFrame = 0.05;

    /// <summary>
    /// Pre-calibration fallback floor (ms/tick). Used only when the
    /// <see cref="Baseline"/> service hasn't reached <see cref="Baseline.MinCalibrationTicks"/>
    /// yet, so detector behaviour is sensible during the first second of a
    /// session before the relative threshold can be trusted. Calibrated
    /// sessions ignore this entirely.
    /// </summary>
    public const double UncalibratedFallbackMsPerTick = 0.10;

    /// <summary>Minimum session length before the detector emits anything (plan §5.6).</summary>
    public const long MinSessionTicks = 60L * 60L * 30L;

    public PatternKey Pattern => PatternKey.FreeRemovalCandidate;
    public Audience DefaultAudience => Audience.Player;

    /// <summary>Gated until an engagement signal is wired (plan §4.9). Records still emit; the renderer flags the gate.</summary>
    public bool IsGated => true;
    public string? GatedOn => "engagement-signal";

    public bool IsAvailable(MetricCollector collector) => collector.History.Count > 0;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        if (sessionLengthTicks < MinSessionTicks) return;

        IReadOnlyList<double> categoryMs = collector.PerModCategoryAverageMs;
        string[] modNames = HookInterceptor.ProfiledModNames;
        int catCount = PerModAttribution.CategoryCount;

        // Relative epsilon: a mod is "cheap" if it's below 5% of the user's
        // session median frame time. Falls back to a small absolute floor only
        // during the calibration window so we don't compare against a
        // wildly-inaccurate median.
        double epsilonMsPerTick = collector.Baseline.IsCalibrated
            ? collector.Baseline.FrameMsMedian * EpsilonFractionOfBaselineFrame
            : UncalibratedFallbackMsPerTick;

        for (int modId = 0; modId < modNames.Length; modId++)
        {
            double modTotal = 0d;
            for (int c = 0; c < catCount; c++)
            {
                int cell = modId * catCount + c;
                if (cell < categoryMs.Count) modTotal += categoryMs[cell];
            }
            if (modTotal > epsilonMsPerTick) continue;

            emit.Add(new Insight
            {
                Pattern = Pattern,
                Subject = SubjectRef.ForMod(modId),
                Magnitude = new Magnitude
                {
                    BaselineMs = epsilonMsPerTick,
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
