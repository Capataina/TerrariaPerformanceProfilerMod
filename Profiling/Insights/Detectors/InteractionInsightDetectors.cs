#nullable enable

using System.Collections.Generic;
using System.Linq;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Profiling.Insights.Detectors;

/// <summary>
/// LoadoutCorrelatedCost: fires when a loadout change is followed by a
/// sustained cost shift within the same session. Reads
/// <c>loadoutSnapshots</c> + per-second tick aggregates; flags the
/// shift's magnitude relative to the player's own baseline (Invariant —
/// nothing absolute).
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
        if (db == null || system?.LiveRecorderSessionId is not LiteDB.ObjectId sid) return;

        var snaps = db.LoadoutSnapshots.Query()
            .Where(x => x.SessionId == sid && x.Reason == "change")
            .OrderByDescending(x => x.UnixMs)
            .Limit(2).ToList();
        if (snaps.Count < 2) return;
        var after = snaps[0];
        if (after.Fingerprint == snaps[1].Fingerprint) return;

        long centreSec = after.Tick / 60;
        var beforeRows = db.TickAggregatesWarm.Query()
            .Where(x => x.SessionId == sid && x.SecondIndex <= centreSec && x.SecondIndex > centreSec - 30)
            .ToList();
        var afterRows = db.TickAggregatesWarm.Query()
            .Where(x => x.SessionId == sid && x.SecondIndex > centreSec && x.SecondIndex <= centreSec + 30)
            .ToList();
        if (beforeRows.Count < 5 || afterRows.Count < 5) return;

        double beforeMean = beforeRows.Average(r => r.AvgFrameMs);
        double afterMean = afterRows.Average(r => r.AvgFrameMs);
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
                Count = beforeRows.Count + afterRows.Count,
            },
            Evidence = new Evidence
            {
                Baseline = BaselineKind.SessionMean,
                SampleN = afterRows.Count,
                BaselineN = beforeRows.Count,
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
/// </summary>
public sealed class EventConditionalCostDetector : IInsightDetector
{
    public PatternKey Pattern => PatternKey.EventConditionalCost;
    public Audience DefaultAudience => Audience.Both;

    public bool IsAvailable(MetricCollector collector) => PerformanceProfiler.Database != null;
    public bool IsGated => false;
    public string? GatedOn => null;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<InsightRecord> emit)
    {
        var db = PerformanceProfiler.Database;
        var system = Terraria.ModLoader.ModContent.GetInstance<ProfilerSystem>();
        if (db == null || system?.LiveRecorderSessionId is not LiteDB.ObjectId sid) return;

        var events = db.BuffEvents.Query()
            .Where(x => x.SessionId == sid)
            .OrderByDescending(x => x.UnixMs)
            .Limit(50).ToList();
        if (events.Count < 3) return;

        var byBuff = events.GroupBy(e => e.BuffType)
            .Where(g => g.Any(e => e.Edge == "on") && g.Any(e => e.Edge == "off"))
            .Take(3).ToList();
        if (byBuff.Count == 0) return;

        double baseline = collector.Baseline.TickPeriodMsMedian;
        if (baseline <= 0) return;

        foreach (var group in byBuff)
        {
            var ordered = group.OrderBy(e => e.UnixMs).ToList();
            BuffEventRow? lastOn = null;
            foreach (var ev in ordered)
            {
                if (ev.Edge == "on") { lastOn = ev; continue; }
                if (ev.Edge != "off" || lastOn == null) continue;

                long onSec = lastOn.Tick / 60;
                long offSec = ev.Tick / 60;
                if (offSec - onSec < 1) { lastOn = null; continue; }

                var windowRows = db.TickAggregatesWarm.Query()
                    .Where(x => x.SessionId == sid && x.SecondIndex >= onSec && x.SecondIndex <= offSec)
                    .ToList();
                if (windowRows.Count < 2) { lastOn = null; continue; }

                double inWindowMean = windowRows.Average(r => r.AvgFrameMs);
                double overBaseline = inWindowMean / baseline;
                if (overBaseline < 3d) { lastOn = null; continue; }

                emit.Add(new InsightRecord
                {
                    Pattern = Pattern,
                    Audience = DefaultAudience,
                    Confidence = Confidence.Preliminary,
                    Scope = EvidenceScope.ThisSession,
                    FirstSeenTick = lastOn.Tick,
                    LastSeenTick = ev.Tick,
                    ConfirmationCount = 1,
                    Subject = SubjectRef.ForMod(-1),
                    Magnitude = new Magnitude
                    {
                        BaselineMs = baseline,
                        ObservedMs = inWindowMean,
                        RatioOrDelta = overBaseline,
                        Count = windowRows.Count,
                    },
                    Evidence = new Evidence
                    {
                        Baseline = BaselineKind.SessionMean,
                        SampleN = windowRows.Count,
                        BaselineN = 0,
                        PValue = 1d,
                        PValueAdjusted = 1d,
                        FirstTickIndex = lastOn.Tick,
                        LastTickIndex = ev.Tick,
                    },
                });
                lastOn = null;
                break;
            }
        }
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
