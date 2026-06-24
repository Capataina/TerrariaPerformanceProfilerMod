#nullable enable

using System.Collections.Generic;
using Terraria.ModLoader;
using PerformanceProfiler.Data.Aggregators.Segments;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// SEGMENT_DEATH_CORRELATION — observe whether deaths cluster in
/// expensive segments. Specifically: does the average frame-ms-per-tick of
/// death-containing segments exceed that of clean ones by ≥30%?
///
/// <para>
/// Strictly correlative — Invariant 3 forbids causation copy. The card
/// reads "deaths in this session occurred in segments averaging X ms/t vs Y
/// ms/t for clean segments." The player can decide what to do with that.
/// </para>
///
/// <para>
/// Emits at most one record per pass. Needs at least 3 death-containing
/// segments AND at least 3 clean segments to compare; otherwise stays
/// silent.
/// </para>
/// </summary>
public sealed class SegmentDeathCorrelationDetector : IInsightDetector
{
    private const int MinPerGroup = 3;
    private const double DeltaThreshold = 0.30d;

    public PatternKey Pattern => PatternKey.SegmentDeathCorrelation;
    public Audience DefaultAudience => Audience.Both;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector)
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        return sys?.SegmentStore != null;
    }

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<InsightRecord> emit)
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        SegmentStore? store = sys?.SegmentStore;
        if (store == null) return;

        int deathSegs = 0, cleanSegs = 0;
        double deathTotalMs = 0d, cleanTotalMs = 0d;
        long deathTicks = 0L, cleanTicks = 0L;

        foreach (Segment seg in store.Recent)
        {
            // Only count biome / weather / boss / invasion segments where the
            // ms/t is interpretable (not death-bracket which contains a death by definition).
            switch (seg.Family)
            {
                case SegmentFamily.DeathBracket:
                case SegmentFamily.UserBookmark:
                case SegmentFamily.Hardmode:
                    continue;
            }
            if (seg.Ticks <= 0) continue;

            if (seg.DeathCount > 0)
            {
                deathSegs++;
                deathTotalMs += seg.TotalFrameMs;
                deathTicks += seg.Ticks;
            }
            else
            {
                cleanSegs++;
                cleanTotalMs += seg.TotalFrameMs;
                cleanTicks += seg.Ticks;
            }
        }

        if (deathSegs < MinPerGroup || cleanSegs < MinPerGroup) return;
        if (deathTicks == 0L || cleanTicks == 0L) return;

        double deathAvg = deathTotalMs / deathTicks;
        double cleanAvg = cleanTotalMs / cleanTicks;
        if (cleanAvg <= 0d) return;

        double delta = (deathAvg / cleanAvg) - 1d;
        if (delta < DeltaThreshold) return;

        emit.Add(new InsightRecord
        {
            Pattern = PatternKey.SegmentDeathCorrelation,
            Subject = SubjectRef.ForMod(-1),
            Magnitude = new Magnitude
            {
                BaselineMs = cleanAvg,
                ObservedMs = deathAvg,
                RatioOrDelta = delta,
                Count = deathSegs,
            },
            Evidence = new Evidence
            {
                SampleN = deathSegs,
                BaselineN = cleanSegs,
                PValue = 1.0d,
                EffectSize = delta,
                Baseline = BaselineKind.ComparableContexts,
            },
            // Emit as Preliminary; InsightStore.PromoteConfidence drives
            // the real confidence based on observation count + PValue.
            // The previous emit-time tier was silently overwritten on
            // store-side Submit (caught by code-health audit 2026-05-21).
            Confidence = Confidence.Preliminary,
            Audience = Audience.Both,
            Scope = EvidenceScope.ThisSession,
            ConfirmationCount = 1,
        });
    }
}
