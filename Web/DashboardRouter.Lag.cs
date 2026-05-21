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
    // /api/spikes — recent spike windows with top contributors.
    // ----------------------------------------------------------------------
    private static string BuildSpikes()
    {
        // Migration step 11 — spikes via registry.
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
    // /api/stalls — recent stall events. Sustained main-thread freezes,
    // distinct from spikes (which are short outlier ticks).
    // ----------------------------------------------------------------------
    private static string BuildStalls()
    {
        // Migration step 11 — stalls via registry.
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
}
