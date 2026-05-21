#nullable enable

using System.Collections.Generic;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data.Detectors.Insights.Detectors;

/// <summary>
/// PEAK_CONTRIBUTOR_TO_SPIKE — for each captured spike, names the mod with
/// the largest share of the per-mod attribution snapshot. The plan's full
/// form (§4.8) uses per-mod deviation from each mod's rolling mean; the
/// sibling spikes-and-allocations plan defines that array. Until that
/// lands, we use the absolute share of the snapshot, which is close enough
/// for the player-facing surface and identical for the modder export when
/// the top contributor is clear.
///
/// <para>
/// Emits one record per spike. The store's dedup folds repeated spikes of
/// the same top-contributor into a single live record with rising
/// confirmation count, so the overlay does not spam.
/// </para>
/// </summary>
public sealed class PeakContributorToSpikeDetector : IInsightDetector
{
    /// <summary>Top contributor must own at least this fraction of the spike's per-mod ms snapshot.</summary>
    public const double ShareFloor = 0.40;

    public PatternKey Pattern => PatternKey.PeakContributorToSpike;
    public Audience DefaultAudience => Audience.Both;
    public bool IsGated => false;
    public string? GatedOn => null;

    private long _lastConsumedSpikeStart = long.MinValue;

    public bool IsAvailable(MetricCollector collector) => collector.Spikes.Count > 0;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<InsightRecord> emit)
    {
        IReadOnlyList<SpikeWindow> spikes = collector.Spikes;
        int catCount = PerModAttribution.CategoryCount;
        string[] modNames = HookInterceptor.ProfiledModNames;

        for (int i = 0; i < spikes.Count; i++)
        {
            SpikeWindow w = spikes[i];
            if (w.StartTick <= _lastConsumedSpikeStart) continue;
            _lastConsumedSpikeStart = w.StartTick;
            if (w.Warming) continue;

            // Sum per-mod totals across categories.
            int modCount = w.PerModCatMs.Length / (catCount > 0 ? catCount : 1);
            if (modCount > modNames.Length) modCount = modNames.Length;
            if (modCount <= 0) continue;

            int bestMod = -1;
            double bestMs = 0d;
            double totalMs = 0d;
            for (int mod = 0; mod < modCount; mod++)
            {
                double sum = 0d;
                int baseIdx = mod * catCount;
                for (int c = 0; c < catCount; c++) sum += w.PerModCatMs[baseIdx + c];
                totalMs += sum;
                if (sum > bestMs) { bestMs = sum; bestMod = mod; }
            }
            if (bestMod < 0 || totalMs <= 0d) continue;

            double share = bestMs / totalMs;
            if (share < ShareFloor) continue;

            emit.Add(new InsightRecord
            {
                Pattern = Pattern,
                Subject = SubjectRef.ForMod(bestMod),
                Magnitude = new Magnitude
                {
                    BaselineMs = w.BaselineMs,
                    ObservedMs = w.WorstFrameMs,
                    RatioOrDelta = share,
                    AllocBytes = 0,
                    Count = 1,
                },
                Evidence = new Evidence
                {
                    SampleN = 1,
                    BaselineN = 0,
                    PValue = 1d,
                    EffectSize = share,
                    PValueAdjusted = 1d,
                    FirstTickIndex = w.StartTick,
                    LastTickIndex = w.EndTick,
                    Baseline = BaselineKind.PerModRollingMean,
                },
                Confidence = Confidence.Preliminary,
                Audience = DefaultAudience,
            });
        }
    }

    /// <summary>Resets the consumed-cursor; used by end-of-session pass to drain every spike.</summary>
    public void Reset()
    {
        _lastConsumedSpikeStart = long.MinValue;
    }
}
