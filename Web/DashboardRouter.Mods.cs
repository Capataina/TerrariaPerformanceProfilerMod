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
using PerformanceProfiler.Insights;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Web.Server;

namespace PerformanceProfiler.Web;

internal static partial class DashboardRouter
{
    // ----------------------------------------------------------------------
    // /api/mods — per-mod ranking with per-category breakdown + allocation.
    // ----------------------------------------------------------------------
    private static string BuildMods()
    {
        // Migration step 11 — per-mod cost via the HookCpuCollector +
        // AllocationCollector adapters. Router does the per-mod
        // accumulation (sum across categories) for the wire shape, but
        // does not derive ratios, ranks, or thresholds — those are
        // visual choices made downstream in the JS.
        var cpuSnap = Data.DataRegistry.Shared
            .Lookup<Data.Collectors.HookCpuSnapshot>(Data.Collectors.HookCpuCollector.StreamName)?
            .CurrentSnapshot() ?? Data.Collectors.HookCpuSnapshot.Empty;
        var allocSnap = Data.DataRegistry.Shared
            .Lookup<Data.Collectors.AllocationSnapshot>(Data.Collectors.AllocationCollector.StreamName)?
            .CurrentSnapshot() ?? Data.Collectors.AllocationSnapshot.Empty;

        if (!cpuSnap.WorldLoaded || cpuSnap.SmoothedMsByCategory == null)
        {
            // No live world — serve the last session's per-mod ranking from the
            // persisted archive ("reading from db" mode). Per-category breakdown
            // and live allocation aren't in the archive, so the cascading tree
            // and alloc column stay empty; the headline ranking populates.
            var last = DbReadModel.GetLastSession();
            if (last != null && last.Archive.PerMod != null && last.Archive.PerMod.Count > 0)
            {
                var dbMods = new List<object>(last.Archive.PerMod.Count);
                foreach (var pm in last.Archive.PerMod)
                {
                    dbMods.Add(new
                    {
                        id = pm.ModId,
                        name = pm.Name,
                        cpuMs = pm.AvgMs,
                        avgCpuMs = pm.AvgMs,
                        categories = Array.Empty<double>(),
                        allocBytes = pm.TotalBytes,
                        avgAllocBytes = pm.TotalBytes,
                        categoryBytes = (double[]?)null,
                    });
                }
                return JsonSerializer.Serialize(new
                {
                    worldLoaded = true,
                    source = "db",
                    tracksAllocations = false,
                    categories = PerModAttribution.CategoryNames,
                    mods = dbMods,
                }, JsonOpts);
            }
            return JsonSerializer.Serialize(new { worldLoaded = false, mods = Array.Empty<object>() }, JsonOpts);
        }

        int categoryCount = cpuSnap.CategoryCount;
        string[] modNames = HookInterceptor.ProfiledModNames;
        IReadOnlyList<double> smoothed = cpuSnap.SmoothedMsByCategory;
        IReadOnlyList<double> averaged = cpuSnap.AverageMsByCategory!;
        IReadOnlyList<double>? smoothedBytes = allocSnap.SmoothedBytesByCategory;
        IReadOnlyList<double>? avgBytes = allocSnap.AverageBytesByCategory;
        bool tracksAlloc = allocSnap.TracksAllocations && smoothedBytes != null;

        var mods = new List<object>(modNames.Length);
        for (int i = 0; i < modNames.Length; i++)
        {
            double cpu = 0d, avgCpu = 0d, alloc = 0d, avgAlloc = 0d;
            double[] cats = new double[categoryCount];
            double[]? catBytes = tracksAlloc ? new double[categoryCount] : null;
            int baseIdx = i * categoryCount;
            for (int cat = 0; cat < categoryCount; cat++)
            {
                cats[cat] = smoothed[baseIdx + cat];
                cpu += smoothed[baseIdx + cat];
                avgCpu += averaged[baseIdx + cat];
                if (tracksAlloc)
                {
                    catBytes![cat] = smoothedBytes![baseIdx + cat];
                    alloc += smoothedBytes[baseIdx + cat];
                    avgAlloc += avgBytes![baseIdx + cat];
                }
            }
            mods.Add(new
            {
                id = i,
                name = modNames[i],
                cpuMs = cpu,
                avgCpuMs = avgCpu,
                categories = cats,
                allocBytes = alloc,
                avgAllocBytes = avgAlloc,
                categoryBytes = catBytes,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            tracksAllocations = tracksAlloc,
            categories = PerModAttribution.CategoryNames,
            mods,
        }, JsonOpts);
    }
}
