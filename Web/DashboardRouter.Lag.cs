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
    // /api/spikes — recent spike windows with top contributors.
    // ----------------------------------------------------------------------
    private static string BuildSpikes()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<Data.Stats.SpikesSnapshot>(Data.Stats.SpikesStat.StreamName)?
            .CurrentSnapshot() ?? Data.Stats.SpikesSnapshot.Empty;
        if (!snap.WorldLoaded || snap.Windows == null)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, spikes = Array.Empty<object>() }, JsonOpts);
        }

        int categoryCount = PerModAttribution.CategoryCount;
        string[] modNames = HookInterceptor.ProfiledModNames;

        var spikes = new List<object>();
        foreach (var w in snap.Windows)
        {
            var contribs = TopContributors(w, modNames, categoryCount, take: 5);
            spikes.Add(new
            {
                startTick = w.StartTick,
                endTick = w.EndTick,
                worstTick = w.WorstTick,
                worstFrameMs = w.WorstFrameMs,
                baselineMs = w.BaselineMs,
                madMs = w.MadMs,
                warming = w.Warming,
                contributors = contribs,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            spikes,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/stalls — recent stall events.
    // ----------------------------------------------------------------------
    private static string BuildStalls()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<Data.Stats.StallsSnapshot>(Data.Stats.StallsStat.StreamName)?
            .CurrentSnapshot() ?? Data.Stats.StallsSnapshot.Empty;
        if (!snap.WorldLoaded || snap.Events == null)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, stalls = Array.Empty<object>() }, JsonOpts);
        }

        var stalls = new List<object>();
        foreach (var s in snap.Events)
        {
            stalls.Add(new
            {
                startTick = s.StartTickIndex,
                endTick = s.EndTickIndex,
                startUnixMs = s.StartTimestampUnixMs,
                durationMs = s.TickPeriodMs,
                baselineMs = s.BaselineMs,
                excessMs = s.ExcessOverBaselineMs,
                cause = s.Cause.ToString(),
                severity = s.Severity.ToString(),
                warming = s.Warming,
                gcPauseMs = s.GcPauseDurationMs,
                gen0 = s.Gen0Collections,
                gen1 = s.Gen1Collections,
                gen2 = s.Gen2Collections,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            stalls,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/lag-clusters — L1 fingerprint clusters + L2 cause×context cells.
    // ----------------------------------------------------------------------
    private static string BuildLagClusters()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<LagClusterSnapshot>(RolloutStreamNames.LagClusters)?
            .CurrentSnapshot() ?? LagClusterSnapshot.Empty;

        if (!snap.WorldLoaded)
        {
            return JsonSerializer.Serialize(new
            {
                worldLoaded = false,
                clusters = Array.Empty<object>(),
                causeContext = Array.Empty<object>(),
            }, JsonOpts);
        }

        var clusters = new List<object>(snap.Clusters.Count);
        foreach (var c in snap.Clusters)
        {
            clusters.Add(new
            {
                fingerprintId = c.FingerprintId,
                causeClass = c.CauseClass,
                primaryBiome = c.PrimaryBiome,
                weatherFlags = c.WeatherFlags,
                hardmode = c.Hardmode,
                topModId = c.TopModId,
                topModName = c.TopModName,
                topModShare = c.TopModShare,
                eventCount = c.EventCount,
                p95Ms = c.P95Ms,
                totalMs = c.TotalMs,
            });
        }

        var causeContext = new List<object>(snap.CauseContext.Count);
        foreach (var cell in snap.CauseContext)
        {
            causeContext.Add(new
            {
                causeClass = cell.CauseClass,
                context = cell.Context,
                eventCount = cell.EventCount,
                totalMs = cell.TotalMs,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            clusters,
            causeContext,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/gc — L3 GC pressure panel.
    // ----------------------------------------------------------------------
    private static string BuildGcPressure()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<GcPressureSnapshot>(RolloutStreamNames.GcPressure)?
            .CurrentSnapshot() ?? GcPressureSnapshot.Empty;

        if (!snap.WorldLoaded)
        {
            return JsonSerializer.Serialize(new
            {
                worldLoaded = false,
                gen0PerMin = 0d, gen1PerMin = 0d, gen2PerMin = 0d,
                gen0Total = 0L, gen1Total = 0L, gen2Total = 0L,
                totalPausedMs = 0L, freedMbDuringPauses = 0L,
                heapMbSeries = Array.Empty<double>(),
                currentManagedMb = 0d,
            }, JsonOpts);
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            gen0PerMin = snap.Gen0PerMin,
            gen1PerMin = snap.Gen1PerMin,
            gen2PerMin = snap.Gen2PerMin,
            gen0Total = snap.Gen0Total,
            gen1Total = snap.Gen1Total,
            gen2Total = snap.Gen2Total,
            totalPausedMs = snap.TotalPausedMs,
            freedMbDuringPauses = snap.FreedMbDuringPauses,
            heapMbSeries = snap.HeapMbSeries,
            currentManagedMb = snap.CurrentManagedMb,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/lag-density — L4 per-segment lag density.
    // ----------------------------------------------------------------------
    private static string BuildSegmentLagDensity()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<SegmentLagDensitySnapshot>(RolloutStreamNames.SegmentLagDensity)?
            .CurrentSnapshot() ?? SegmentLagDensitySnapshot.Empty;

        if (!snap.WorldLoaded)
        {
            return JsonSerializer.Serialize(new
            {
                worldLoaded = false,
                sessionBaselinePerMin = 0d,
                entries = Array.Empty<object>(),
            }, JsonOpts);
        }

        var entries = new List<object>(snap.Entries.Count);
        foreach (var e in snap.Entries)
        {
            entries.Add(new
            {
                segmentStartTick = e.SegmentStartTick,
                family = e.Family,
                name = e.Name,
                durationMs = e.DurationMs,
                spikeCount = e.SpikeCount,
                stallCount = e.StallCount,
                eventsPerMin = e.EventsPerMin,
                vsBaseline = e.VsBaseline,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            sessionBaselinePerMin = snap.SessionBaselinePerMin,
            entries,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/gc-causality — L6 allocation→GC causality chains.
    // ----------------------------------------------------------------------
    private static string BuildAllocCausality()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<AllocCausalitySnapshot>(RolloutStreamNames.AllocCausality)?
            .CurrentSnapshot() ?? AllocCausalitySnapshot.Empty;

        if (!snap.WorldLoaded)
        {
            return JsonSerializer.Serialize(new
            {
                worldLoaded = false,
                chains = Array.Empty<object>(),
            }, JsonOpts);
        }

        var chains = new List<object>(snap.Chains.Count);
        foreach (var ch in snap.Chains)
        {
            var contribs = new List<object>(ch.TopContributors.Count);
            foreach (var c in ch.TopContributors)
            {
                contribs.Add(new
                {
                    modId = c.ModId,
                    modName = c.ModName,
                    bytesInWindow = c.BytesInWindow,
                    sharePct = c.SharePct,
                });
            }
            chains.Add(new
            {
                gcStallUnixMs = ch.GcStallUnixMs,
                gcStallTickIndex = ch.GcStallTickIndex,
                pauseMs = ch.PauseMs,
                freedBytes = ch.FreedBytes,
                gcKind = ch.GcKind,
                windowMs = ch.WindowMs,
                totalBytesInWindow = ch.TotalBytesInWindow,
                topContributors = contribs,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            chains,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/lag-rhythm — L7 lag rhythm histogram + clusters.
    // ----------------------------------------------------------------------
    private static string BuildLagRhythm()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<LagRhythmSnapshot>(RolloutStreamNames.LagRhythm)?
            .CurrentSnapshot() ?? LagRhythmSnapshot.Empty;

        if (!snap.WorldLoaded)
        {
            return JsonSerializer.Serialize(new
            {
                worldLoaded = false,
                histogram = Array.Empty<object>(),
                clusters = Array.Empty<object>(),
            }, JsonOpts);
        }

        var histogram = new List<object>(snap.Histogram.Count);
        foreach (var h in snap.Histogram)
        {
            histogram.Add(new
            {
                intervalSeconds = h.IntervalSeconds,
                count = h.Count,
            });
        }

        var clusters = new List<object>(snap.Clusters.Count);
        foreach (var c in snap.Clusters)
        {
            clusters.Add(new
            {
                centreSeconds = c.CentreSeconds,
                widthSeconds = c.WidthSeconds,
                eventCount = c.EventCount,
                shareOfSession = c.ShareOfSession,
                topModId = c.TopModId,
                topModName = c.TopModName,
                topModShare = c.TopModShare,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            histogram,
            clusters,
        }, JsonOpts);
    }
}
