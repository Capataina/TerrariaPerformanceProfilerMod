#nullable enable

using System.Collections.Generic;
using LiteDB;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// LoadoutCorrelatedCost: fires when a loadout change is followed by a
/// sustained cost shift within the same session. Reads
/// <c>loadoutSnapshots</c> + per-second tick aggregates; flags the
/// shift's magnitude relative to the player's own baseline (Invariant —
/// nothing absolute).
///
/// v0.6: replaced 5 LINQ chains with explicit foreach loops + inline
/// mean accumulators. Allocation profile drops from ~30 KB/pass (LINQ
/// iterator + List<T> tail) to ~0 (every state is a stack local).
/// Per insights-engine dossier §4.1.
/// </summary>
public sealed class LoadoutCorrelatedCostDetector : IInsightDetector
{
    public PatternKey Pattern => PatternKey.LoadoutCorrelatedCost;
    public Audience DefaultAudience => Audience.Both;

    public bool IsAvailable(MetricCollector collector) => PerformanceProfiler.Database != null;
    public bool IsGated => false;
    public string? GatedOn => null;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<InsightRecord> emit)
    {
        var db = PerformanceProfiler.Database;
        var system = Terraria.ModLoader.ModContent.GetInstance<ProfilerSystem>();
        if (db == null || system?.LiveRecorderSessionId is not ObjectId sid) return;

        // Find the two most-recent "change"-reason loadout snapshots for the
        // active session. Foreach over the Query enumerator caps at 2; no
        // intermediate list allocation.
        LoadoutSnapshotRow? after = null;
        LoadoutSnapshotRow? before = null;
        int seenChanges = 0;
        foreach (var row in db.LoadoutSnapshots.Find(
            Query.And(Query.EQ("SessionId", sid), Query.EQ("Reason", "change")),
            limit: 50))
        {
            if (after == null) { after = row; seenChanges = 1; continue; }
            if (row.UnixMs <= after.UnixMs) { before = row; seenChanges++; break; }
            before = after;
            after = row;
            seenChanges++;
        }
        if (after == null || before == null) return;
        if (after.Fingerprint == before.Fingerprint) return;

        long centreSec = after.Tick / 60;
        double beforeSum = 0, afterSum = 0;
        int beforeCount = 0, afterCount = 0;

        // Single scan over the session's tick aggregates; partition by the
        // centre tick. The Query.Between bracket on SecondIndex would let
        // LiteDB index-scan; for now Find on SessionId + in-memory partition.
        foreach (var r in db.TickAggregatesWarm.Find(Query.EQ("SessionId", sid)))
        {
            if (r.SecondIndex > centreSec - 30 && r.SecondIndex <= centreSec)
            {
                beforeSum += r.AvgFrameMs;
                beforeCount++;
            }
            else if (r.SecondIndex > centreSec && r.SecondIndex <= centreSec + 30)
            {
                afterSum += r.AvgFrameMs;
                afterCount++;
            }
        }
        if (beforeCount < 5 || afterCount < 5) return;

        double beforeMean = beforeSum / beforeCount;
        double afterMean = afterSum / afterCount;
        double baseline = collector.Baseline.TickPeriodMsMedian;
        if (beforeMean <= 0 || baseline <= 0) return;

        double ratio = afterMean / beforeMean;
        double overBaseline = afterMean / baseline;
        if (ratio < 1.5d || overBaseline < 2d) return;

        emit.Add(new InsightRecord
        {
            Pattern = Pattern,
            Audience = DefaultAudience,
            Confidence = Confidence.Preliminary,
            Scope = EvidenceScope.ThisSession,
            FirstSeenTick = after.Tick,
            LastSeenTick = nowTick,
            ConfirmationCount = 1,
            Subject = SubjectRef.ForMod(-1),
            Magnitude = new Magnitude
            {
                BaselineMs = beforeMean,
                ObservedMs = afterMean,
                RatioOrDelta = ratio,
                Count = beforeCount + afterCount,
            },
            Evidence = new Evidence
            {
                Baseline = BaselineKind.SessionMean,
                SampleN = afterCount,
                BaselineN = beforeCount,
                PValue = 1d,
                PValueAdjusted = 1d,
                FirstTickIndex = after.Tick,
                LastTickIndex = nowTick,
            },
        });
    }
}

/// <summary>
/// EventConditionalCost: fires when frame cost rises within a buff-active
/// window (off→on→off) compared to baseline. Reads <c>buffEvents</c> +
/// <c>tickAggregatesWarm</c>. The Dead Cells Mechanics "RecentlyHit
/// activates only after damage" pattern surfaces through this detector.
///
/// v0.6: replaced GroupBy + .Any + .Where + 4 .ToList + .Average with an
/// explicit pass-through. Allocation per pass drops from ~50 KB to ~0
/// (dictionary keyed by BuffType is a single field-cached reuse).
/// Per insights-engine dossier §4.2.
/// </summary>
public sealed class EventConditionalCostDetector : IInsightDetector
{
    // Field-cached scratch space; reused across Evaluate calls. The
    // detector is per-session and runs at 1 Hz on the insights tick.
    private readonly Dictionary<int, BuffWindow> _byBuff = new();
    private readonly List<int> _processOrder = new();

    public PatternKey Pattern => PatternKey.EventConditionalCost;
    public Audience DefaultAudience => Audience.Both;

    public bool IsAvailable(MetricCollector collector) => PerformanceProfiler.Database != null;
    public bool IsGated => false;
    public string? GatedOn => null;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<InsightRecord> emit)
    {
        var db = PerformanceProfiler.Database;
        var system = Terraria.ModLoader.ModContent.GetInstance<ProfilerSystem>();
        if (db == null || system?.LiveRecorderSessionId is not ObjectId sid) return;

        _byBuff.Clear();
        _processOrder.Clear();

        // One-pass scan: bucket the recent 50 buff events by buffType, capturing
        // last on-edge + last off-edge per buff. The dictionary reuses its
        // backing storage across detector ticks (no per-pass alloc).
        int seen = 0;
        foreach (var ev in db.BuffEvents.Find(Query.EQ("SessionId", sid)))
        {
            if (++seen > 50) break;
            if (!_byBuff.TryGetValue(ev.BuffType, out var bw))
            {
                bw = default;
                _processOrder.Add(ev.BuffType);
                if (_processOrder.Count > 3) break;
            }
            if (ev.Edge == "on" && bw.OnTick == 0) { bw.OnTick = ev.Tick; bw.OnUnixMs = ev.UnixMs; }
            else if (ev.Edge == "off" && bw.OffTick == 0) { bw.OffTick = ev.Tick; bw.OffUnixMs = ev.UnixMs; }
            _byBuff[ev.BuffType] = bw;
        }
        if (_byBuff.Count == 0) return;

        double baseline = collector.Baseline.TickPeriodMsMedian;
        if (baseline <= 0) return;

        foreach (int buffType in _processOrder)
        {
            var bw = _byBuff[buffType];
            if (bw.OnTick == 0 || bw.OffTick == 0) continue;
            if (bw.OnTick >= bw.OffTick) continue;   // need on before off

            long onSec = bw.OnTick / 60;
            long offSec = bw.OffTick / 60;
            if (offSec - onSec < 1) continue;

            double sum = 0;
            int count = 0;
            foreach (var r in db.TickAggregatesWarm.Find(Query.EQ("SessionId", sid)))
            {
                if (r.SecondIndex >= onSec && r.SecondIndex <= offSec)
                {
                    sum += r.AvgFrameMs;
                    count++;
                }
            }
            if (count < 2) continue;

            double inWindowMean = sum / count;
            double overBaseline = inWindowMean / baseline;
            if (overBaseline < 3d) continue;

            emit.Add(new InsightRecord
            {
                Pattern = Pattern,
                Audience = DefaultAudience,
                Confidence = Confidence.Preliminary,
                Scope = EvidenceScope.ThisSession,
                FirstSeenTick = bw.OnTick,
                LastSeenTick = bw.OffTick,
                ConfirmationCount = 1,
                Subject = SubjectRef.ForMod(-1),
                Magnitude = new Magnitude
                {
                    BaselineMs = baseline,
                    ObservedMs = inWindowMean,
                    RatioOrDelta = overBaseline,
                    Count = count,
                },
                Evidence = new Evidence
                {
                    Baseline = BaselineKind.SessionMean,
                    SampleN = count,
                    BaselineN = 0,
                    PValue = 1d,
                    PValueAdjusted = 1d,
                    FirstTickIndex = bw.OnTick,
                    LastTickIndex = bw.OffTick,
                },
            });

            // Limit to one emission per Evaluate (per buff at a time) so a
            // single detector pass doesn't flood the insight stream.
            return;
        }
    }

    /// <summary>Per-buff on/off bookkeeping. Value-type to keep the
    /// dictionary entry lean — no per-buff heap allocation.</summary>
    private struct BuffWindow
    {
        public long OnTick;
        public long OffTick;
        public long OnUnixMs;
        public long OffUnixMs;
    }
}

/// <summary>
/// LoadoutCombinationCost: synergy detector. Gated on cross-session
/// loadout aggregation; emits nothing until that lands. Pattern key is
/// reserved so the gated-detector list surfaces it in the UI.
/// </summary>
public sealed class LoadoutCombinationCostDetector : IInsightDetector
{
    public PatternKey Pattern => PatternKey.LoadoutCombinationCost;
    public Audience DefaultAudience => Audience.Both;

    public bool IsAvailable(MetricCollector collector) => false;
    public bool IsGated => true;
    public string? GatedOn => "cross-session-loadout-aggregation";

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<InsightRecord> emit)
    {
        // Within a single session it's rare to have enough distinct
        // loadout fingerprints to triangulate a synergy claim. Wired
        // once cross-session aggregates land; pattern key reserved.
    }
}
