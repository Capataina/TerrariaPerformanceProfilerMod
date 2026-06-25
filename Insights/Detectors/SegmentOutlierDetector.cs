#nullable enable

using System.Collections.Generic;
using Terraria.ModLoader;
using PerformanceProfiler.Data.Aggregators.Segments;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// SEGMENT_OUTLIER — for each <see cref="SegmentFamily"/>/key pair with at
/// least <see cref="MinSamples"/> closed segments, flag any single segment
/// whose avg-ms-per-tick was more than <see cref="OutlierMultiplier"/>×
/// the lifetime average for that pair.
///
/// <para>
/// Emits one record per outlier segment per pass. Pattern:
/// "this Jungle visit was 73% above your 8-visit average."
/// </para>
///
/// <para>
/// Strictly descriptive — never names a mod as causal. The "why" lives in
/// the segment's top-mod breakdown, which the InsightsTab can drill into via
/// click-through.
/// </para>
/// </summary>
public sealed class SegmentOutlierDetector : IInsightDetector
{
    private const int MinSamples = 5;
    private const double OutlierMultiplier = 1.5d;

    public PatternKey Pattern => PatternKey.SegmentOutlier;
    public Audience DefaultAudience => Audience.Both;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector)
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        return sys?.SegmentStore != null;
    }

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        SegmentStore? store = sys?.SegmentStore;
        if (store == null) return;

        foreach (Segment seg in store.Recent)
        {
            // Skip families that don't repeat (subworld/hardmode/bookmark/deathbracket)
            // — outlier math needs a meaningful lifetime baseline.
            switch (seg.Family)
            {
                case SegmentFamily.Subworld:
                case SegmentFamily.Hardmode:
                case SegmentFamily.UserBookmark:
                case SegmentFamily.DeathBracket:
                case SegmentFamily.Combat:
                    continue;
            }

            int samples = store.LifetimeSampleCount(seg.Family, seg.Key);
            if (samples < MinSamples) continue;

            double lifetimeAvg = store.LifetimeAvgMsPerTick(seg.Family, seg.Key);
            if (lifetimeAvg <= 0d) continue;

            double avg = seg.AvgFrameMs;
            if (avg <= 0d) continue;

            double ratio = avg / lifetimeAvg;
            if (ratio < OutlierMultiplier) continue;

            var rec = new Insight
            {
                Pattern = PatternKey.SegmentOutlier,
                Subject = SubjectRef.ForContext(seg.Key, (byte)seg.Family),
                Magnitude = new Magnitude
                {
                    BaselineMs = lifetimeAvg,
                    ObservedMs = avg,
                    RatioOrDelta = ratio - 1d,
                    Count = samples,
                },
                Evidence = new Evidence
                {
                    SampleN = (int)seg.Ticks,
                    BaselineN = samples,
                    PValue = 1.0d,
                    EffectSize = ratio - 1d,
                    FirstTickIndex = seg.StartTick,
                    LastTickIndex = seg.EndTick,
                    Baseline = BaselineKind.ComparableContexts,
                },
                // Emit Preliminary; store promotes on confirmations.
                Confidence = Confidence.Preliminary,
                Audience = Audience.Both,
                Scope = EvidenceScope.LifetimeData,
                FirstSeenTick = seg.StartTick,
                LastSeenTick = seg.EndTick,
                ConfirmationCount = 1,
            };
            emit.Add(rec);
        }
    }
}
