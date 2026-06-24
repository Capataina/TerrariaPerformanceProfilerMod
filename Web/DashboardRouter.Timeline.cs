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
using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Insights;
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
                    startUnixMs = s.StartUnixMs,
                    startTick = s.StartTick,
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
                    startTick = s.StartTick,
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
    // ----------------------------------------------------------------------
    private static string BuildEvents()
    {
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

    // ----------------------------------------------------------------------
    // /api/segment-lifetime — per-segment lifetime baseline + delta. T1+T2.
    //
    // Returns a flat list of recently-closed segments paired with their
    // lifetime mean and the deviation of this segment vs the running
    // average. The Gantt UI uses delta to outlier-mark a block; the detail
    // pane reads lifetime samples to qualify the badge.
    // ----------------------------------------------------------------------
    private static string BuildSegmentLifetime()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<SegmentLifetimeSnapshot>(RolloutStreamNames.SegmentLifetime)?
            .CurrentSnapshot() ?? SegmentLifetimeSnapshot.Empty;

        var entries = new List<object>(snap.Recent?.Count ?? 0);
        if (snap.Recent != null)
        {
            foreach (var e in snap.Recent)
            {
                entries.Add(new
                {
                    segmentStartTick = e.SegmentStartTick,
                    family = ((SegmentFamily)e.Family).ToString(),
                    key = e.Key,
                    name = e.Name,
                    lifetimeAvgMs = e.LifetimeAvgMs,
                    lifetimeSampleCount = e.LifetimeSampleCount,
                    thisSegmentAvgMs = e.ThisSegmentAvgMs,
                    deltaFraction = e.DeltaFraction,
                });
            }
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            entries,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/segment-mod-attribution — per-segment per-mod ms attribution. T1.
    //
    // The Gantt block's inline waterfall and the detail pane drill both
    // read from this. Mod ids are dereferenced into names server-side so
    // the renderer does not need to cross-join against /api/mods.
    // ----------------------------------------------------------------------
    private static string BuildSegmentModAttribution()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<SegmentModAttributionSnapshot>(RolloutStreamNames.SegmentModAttribution)?
            .CurrentSnapshot() ?? SegmentModAttributionSnapshot.Empty;

        string[] modNames = HookInterceptor.ProfiledModNames;
        var entries = new List<object>(snap.Recent?.Count ?? 0);
        if (snap.Recent != null)
        {
            foreach (var e in snap.Recent)
            {
                var ids = e.ModIds;
                var ms = e.ModMs;
                int n = ids != null && ms != null ? Math.Min(ids.Count, ms.Count) : 0;
                var perMod = new List<object>(n);
                for (int i = 0; i < n; i++)
                {
                    int id = ids![i];
                    perMod.Add(new
                    {
                        modId = id,
                        modName = id >= 0 && id < modNames.Length ? modNames[id] : "mod:" + id,
                        ms = ms![i],
                    });
                }
                entries.Add(new
                {
                    segmentStartTick = e.SegmentStartTick,
                    family = ((SegmentFamily)e.Family).ToString(),
                    key = e.Key,
                    perMod,
                });
            }
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            entries,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/transitions — context transitions for the transition track. T3.
    // ----------------------------------------------------------------------
    private static string BuildTransitions()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<TransitionTrackSnapshot>(RolloutStreamNames.TransitionTrack)?
            .CurrentSnapshot() ?? TransitionTrackSnapshot.Empty;

        var transitions = new List<object>(snap.Transitions?.Count ?? 0);
        if (snap.Transitions != null)
        {
            foreach (var t in snap.Transitions)
            {
                transitions.Add(new
                {
                    unixMs = t.UnixMs,
                    tickIndex = t.TickIndex,
                    type = t.Type,
                    from = t.From,
                    to = t.To,
                });
            }
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            transitions,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/activity-strip — minute-bucketed activity heat strip. T4.
    // ----------------------------------------------------------------------
    private static string BuildActivityStrip()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<ActivityHeatStripSnapshot>(RolloutStreamNames.ActivityHeatStrip)?
            .CurrentSnapshot() ?? ActivityHeatStripSnapshot.Empty;

        var minutes = new List<object>(snap.Minutes?.Count ?? 0);
        if (snap.Minutes != null)
        {
            foreach (var m in snap.Minutes)
            {
                minutes.Add(new
                {
                    minuteIndex = m.MinuteIndex,
                    unixMs = m.UnixMs,
                    segmentCount = m.SegmentCount,
                    spikeCount = m.SpikeCount,
                    stallCount = m.StallCount,
                    avgFrameMs = m.AvgFrameMs,
                });
            }
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            minutes,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/attendance — per-mod context attendance roll-up. T5.
    // ----------------------------------------------------------------------
    private static string BuildAttendance()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<AttendanceSnapshot>(RolloutStreamNames.Attendance)?
            .CurrentSnapshot() ?? AttendanceSnapshot.Empty;

        var byMod = new List<object>(snap.ByMod?.Count ?? 0);
        if (snap.ByMod != null)
        {
            foreach (var e in snap.ByMod)
            {
                byMod.Add(new
                {
                    modId = e.ModId,
                    modName = e.ModName,
                    biomeTicks = e.BiomeTicks,
                    invasionCount = e.InvasionCount,
                    bossSegmentCount = e.BossSegmentCount,
                });
            }
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            totalBiomeTicks = snap.TotalBiomeTicks,
            moddedBiomeTicks = snap.ModdedBiomeTicks,
            totalInvasions = snap.TotalInvasions,
            totalBossSegments = snap.TotalBossSegments,
            byMod,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/deaths — per-death 30s pre-window replay strips. T6.
    // ----------------------------------------------------------------------
    private static string BuildDeaths()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<DeathReplaySnapshot>(RolloutStreamNames.DeathReplay)?
            .CurrentSnapshot() ?? DeathReplaySnapshot.Empty;

        string[] modNames = HookInterceptor.ProfiledModNames;
        var deaths = new List<object>(snap.Deaths?.Count ?? 0);
        if (snap.Deaths != null)
        {
            foreach (var d in snap.Deaths)
            {
                var events = new List<object>(d.Events?.Count ?? 0);
                if (d.Events != null)
                {
                    foreach (var ev in d.Events)
                    {
                        events.Add(new
                        {
                            offsetMs = ev.OffsetMs,
                            kind = ev.Kind,
                            label = ev.Label,
                            modId = ev.ModId,
                            modName = ev.ModId >= 0 && ev.ModId < modNames.Length ? modNames[ev.ModId] : null,
                            magnitude = ev.Magnitude,
                        });
                    }
                }
                deaths.Add(new
                {
                    deathUnixMs = d.DeathUnixMs,
                    deathTickIndex = d.DeathTickIndex,
                    primaryBiome = d.PrimaryBiome,
                    primaryBoss = d.PrimaryBoss,
                    finalDamageModId = d.FinalDamageModId,
                    finalDamageModName = d.FinalDamageModId >= 0 && d.FinalDamageModId < modNames.Length ? modNames[d.FinalDamageModId] : null,
                    finalDamageSource = d.FinalDamageSource,
                    finalDamageAmount = d.FinalDamageAmount,
                    events,
                });
            }
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            deaths,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/chronicle — session chronicle lines. T7.
    // ----------------------------------------------------------------------
    private static string BuildChronicle()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<SessionChronicleSnapshot>(RolloutStreamNames.SessionChronicle)?
            .CurrentSnapshot() ?? SessionChronicleSnapshot.Empty;

        var lines = new List<object>(snap.Lines?.Count ?? 0);
        if (snap.Lines != null)
        {
            foreach (var l in snap.Lines)
            {
                lines.Add(new
                {
                    unixMs = l.UnixMs,
                    tickIndex = l.TickIndex,
                    kind = l.Kind,
                    text = l.Text,
                });
            }
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            lines,
        }, JsonOpts);
    }
}
