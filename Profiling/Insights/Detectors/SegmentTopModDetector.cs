#nullable enable

using System.Collections.Generic;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling.Segments;

namespace PerformanceProfiler.Profiling.Insights.Detectors;

/// <summary>
/// SEGMENT_TOP_MOD — when the same mod ranks #1 in cost across many
/// segments of the same (family, key) pair, that's worth surfacing. The
/// player can read the pattern themselves: "Verdant is the top contributor
/// on every Jungle visit you've had" is more useful than a per-segment
/// breakdown of those same Jungle visits.
///
/// <para>
/// Folds across the live <see cref="SegmentStore.Recent"/> ring. Emits at
/// most one record per (family, key) per pass. Threshold: a mod must rank
/// #1 in ≥80% of the recent segments of that pair, with at least 3
/// segments to compare. Pattern reaches Audience.Both because both players
/// (decision-making) and modders (debugging) read it.
/// </para>
/// </summary>
public sealed class SegmentTopModDetector : IInsightDetector
{
    private const int MinSegments = 3;
    private const double Threshold = 0.8d;

    public PatternKey Pattern => PatternKey.SegmentTopMod;
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

        // Group recent segments by (family, key); count how often each mod was #1.
        var byPair = new Dictionary<long, List<Segment>>();
        foreach (Segment seg in store.Recent)
        {
            switch (seg.Family)
            {
                case SegmentFamily.UserBookmark:
                case SegmentFamily.DeathBracket:
                case SegmentFamily.Combat:
                    continue;
            }
            if (seg.ModIds.Length == 0) continue;
            long composite = ((long)(byte)seg.Family << 56) | (uint)seg.Key;
            if (!byPair.TryGetValue(composite, out List<Segment>? list))
            {
                list = new List<Segment>(4);
                byPair[composite] = list;
            }
            list.Add(seg);
        }

        foreach (var kv in byPair)
        {
            List<Segment> segs = kv.Value;
            if (segs.Count < MinSegments) continue;

            // Count top-mod wins.
            var wins = new Dictionary<int, int>();
            for (int i = 0; i < segs.Count; i++)
            {
                var top = segs[i].TopMods(1);
                if (top.Count == 0) continue;
                int modId = top[0].ModId;
                wins.TryGetValue(modId, out int c);
                wins[modId] = c + 1;
            }
            if (wins.Count == 0) continue;

            int bestMod = -1;
            int bestCount = 0;
            foreach (var w in wins)
            {
                if (w.Value > bestCount) { bestCount = w.Value; bestMod = w.Key; }
            }
            double share = (double)bestCount / segs.Count;
            if (share < Threshold) continue;

            Segment first = segs[0];
            var rec = new InsightRecord
            {
                Pattern = PatternKey.SegmentTopMod,
                Subject = new SubjectRef(modId: bestMod, hookId: -1, contextKey: first.Key, contextDim: (byte)first.Family),
                Magnitude = new Magnitude
                {
                    RatioOrDelta = share,
                    Count = bestCount,
                },
                Evidence = new Evidence
                {
                    SampleN = segs.Count,
                    BaselineN = segs.Count,
                    PValue = 1.0d,
                    EffectSize = share,
                    Baseline = BaselineKind.ComparableContexts,
                },
                Confidence = share >= 0.95d ? Confidence.High
                            : share >= 0.85d ? Confidence.Medium
                            : Confidence.Low,
                Audience = Audience.Both,
                Scope = EvidenceScope.LifetimeData,
                ConfirmationCount = bestCount,
            };
            emit.Add(rec);
        }
    }
}
