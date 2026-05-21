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
    // /api/hooks — full per-mod / per-category / per-hook breakdown for
    // the cascading tree view. Heavier payload than /api/mods (one row
    // per installed hook = ~10k entries on a kitchen-sink modlist), so
    // the dashboard only fetches this on demand when the tree is expanded.
    // ----------------------------------------------------------------------
    private static string BuildHooks()
    {
        // Migration step 11 — per-hook cost via HookCpuCollector. The
        // hook descriptor list still comes from PerModAttribution (it's
        // the canonical install-time registration); only the per-hook
        // ms / bytes arrays go through the pipeline adapters.
        var cpuSnap = Data.DataRegistry.Shared
            .Lookup<Data.Collectors.HookCpuSnapshot>(Data.Collectors.HookCpuCollector.StreamName)?
            .CurrentSnapshot() ?? Data.Collectors.HookCpuSnapshot.Empty;
        var allocSnap = Data.DataRegistry.Shared
            .Lookup<Data.Collectors.AllocationSnapshot>(Data.Collectors.AllocationCollector.StreamName)?
            .CurrentSnapshot() ?? Data.Collectors.AllocationSnapshot.Empty;
        if (!cpuSnap.WorldLoaded || cpuSnap.PerHookMs == null)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, hooks = Array.Empty<object>() }, JsonOpts);
        }

        string[] modNames = HookInterceptor.ProfiledModNames;
        IReadOnlyList<HookDescriptor> hooks = PerModAttribution.Hooks;
        IReadOnlyList<double> hookMs = cpuSnap.PerHookMs;
        IReadOnlyList<double> hookAvgMs = cpuSnap.PerHookAverageMs!;
        IReadOnlyList<double>? hookBytes = allocSnap.PerHookBytes;
        bool tracksAlloc = allocSnap.TracksAllocations && hookBytes != null;

        var hookList = new List<object>(hooks.Count);
        for (int hookId = 0; hookId < hooks.Count; hookId++)
        {
            HookDescriptor d = hooks[hookId];
            double ms = hookId < hookMs.Count ? hookMs[hookId] : 0d;
            double avg = hookId < hookAvgMs.Count ? hookAvgMs[hookId] : 0d;
            // Skip totally inactive hooks to keep the payload compact.
            // The tree view shows only hooks with non-zero current OR average cost.
            if (ms <= 0d && avg <= 0d) continue;
            double bytes = tracksAlloc && hookId < hookBytes!.Count ? hookBytes[hookId] : 0d;
            hookList.Add(new
            {
                modId = d.ModId,
                modName = d.ModId >= 0 && d.ModId < modNames.Length ? modNames[d.ModId] : "mod:" + d.ModId,
                categoryId = d.CategoryId,
                category = d.CategoryId >= 0 && d.CategoryId < PerModAttribution.CategoryCount
                    ? PerModAttribution.CategoryNames[d.CategoryId]
                    : "?",
                hookId,
                display = d.DisplayName,
                cpuMs = ms,
                avgCpuMs = avg,
                allocBytes = bytes,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            tracksAllocations = tracksAlloc,
            categories = PerModAttribution.CategoryNames,
            hooks = hookList,
        }, JsonOpts);
    }
}
