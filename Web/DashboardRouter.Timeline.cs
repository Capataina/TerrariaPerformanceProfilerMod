#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Detectors.Insights;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Web.Server;

namespace PerformanceProfiler.Web;

internal static partial class DashboardRouter
{
    // ----------------------------------------------------------------------
    // /api/segments — open + recent closed.
    // ----------------------------------------------------------------------
    private static string BuildSegments()
    {
        // Migration step 11 — segments via registry adapter. The
        // SegmentAggregator wraps the existing SegmentDetector + Store
        // and exposes their live collections as a single snapshot.
        var snap = Data.DataRegistry.Shared
            .Lookup<Data.Aggregators.SegmentsSnapshot>(Data.Aggregators.SegmentAggregator.StreamName)?
            .CurrentSnapshot() ?? Data.Aggregators.SegmentsSnapshot.Empty;

        var open = new List<object>();
        if (snap.Open != null)
        {
            string[] modNames = HookInterceptor.ProfiledModNames;
            long nowUnix = Time.UnixMsNow();
            foreach (OpenSegment s in snap.Open)
            {
                int bestMod = -1; double bestMs = 0d;
                for (int m = 0; m < s.PerModMs.Length; m++)
                {
                    if (s.PerModMs[m] > bestMs) { bestMs = s.PerModMs[m]; bestMod = m; }
                }
                open.Add(new
                {
                    family = s.Family.ToString(),
                    key = s.Key,
                    name = s.Name,
                    elapsedMs = nowUnix - s.StartUnixMs,
                    ticks = s.Ticks,
                    spikeCount = s.SpikeCount,
                    stallCount = s.StallCount,
                    deathCount = s.DeathCount,
                    topModId = bestMod,
                    topModName = bestMod >= 0 && bestMod < modNames.Length ? modNames[bestMod] : null,
                    topModMsPerTick = s.Ticks > 0 ? bestMs / s.Ticks : 0d,
                });
            }
        }

        var recent = new List<object>();
        if (snap.Recent != null)
        {
            string[] modNames = HookInterceptor.ProfiledModNames;
            foreach (Segment s in snap.Recent)
            {
                var topMods = s.TopMods(3);
                var topList = new List<object>(topMods.Count);
                double totalMs = s.TotalFrameMs > 0 ? s.TotalFrameMs : 1d;
                foreach (var (modId, ms) in topMods)
                {
                    topList.Add(new
                    {
                        id = modId,
                        name = modId >= 0 && modId < modNames.Length ? modNames[modId] : "mod:" + modId,
                        ms,
                        share = ms / totalMs,
                    });
                }
                recent.Add(new
                {
                    family = s.Family.ToString(),
                    key = s.Key,
                    name = s.Name,
                    startUnixMs = s.StartUnixMs,
                    endUnixMs = s.EndUnixMs,
                    durationMs = s.DurationMs,
                    ticks = s.Ticks,
                    avgFrameMs = s.AvgFrameMs,
                    spikeCount = s.SpikeCount,
                    stallCount = s.StallCount,
                    deathCount = s.DeathCount,
                    bossKillCount = s.BossKillCount,
                    promoted = s.Promoted,
                    promotionReason = s.PromotionReason,
                    topMods = topList,
                });
            }
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            open,
            recent,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/events — pre-merged events feed (segments + spikes + stalls).
    // Used to be assembled in JS by joining /api/segments + /api/spikes;
    // moved server-side so it's a single endpoint and the dashboard
    // doesn't need to re-merge on every render.
    // ----------------------------------------------------------------------
    private static string BuildEvents()
    {
        // v0.9.x unified data pipeline migration step 4. The router no
        // longer merges segments + spikes + stalls — that math is owned
        // by EventsFeedStat, which calls into the pure-logic
        // EventsFeed.Build. Router just serialises the snapshot.
        var snap = Data.DataRegistry.Shared
            .Lookup<Data.Stats.EventsFeedSnapshot>(Data.Stats.EventsFeedStat.StreamName)?
            .CurrentSnapshot() ?? Data.Stats.EventsFeedSnapshot.Empty;

        var serialised = new List<object>(snap.Events.Count);
        foreach (var e in snap.Events)
        {
            serialised.Add(new
            {
                kind = e.Kind,
                text = e.Text,
                unixMs = e.UnixMs,
                tickIndex = e.TickIndex,
            });
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            events = serialised,
        }, JsonOpts);
    }
}
