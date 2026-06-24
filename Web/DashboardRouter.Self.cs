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
    // /api/self — profiler self-health detail.
    // ----------------------------------------------------------------------
    private static string BuildSelf()
    {
        // Migration step 11 — self-health via registry.
        var snap = Data.DataRegistry.Shared
            .Lookup<Data.Stats.SelfHealthSnapshot>(Data.Stats.SelfHealthStat.StreamName)?
            .CurrentSnapshot() ?? default;

        return JsonSerializer.Serialize(new
        {
            installed = snap.Installed,
            installDeltaBytes = snap.InstallDeltaBytes,
            installDeltaMb = snap.InstallDeltaBytes / (1024d * 1024d),
            bytesPerHook = snap.BytesPerHook,
            bytesPerHookKb = snap.BytesPerHook / 1024d,
            installedHookCount = snap.InstalledHookCount,
            processWorkingSetMb = snap.ProcessWorkingSetBytes / (1024d * 1024d),
            processManagedHeapMb = snap.ProcessManagedHeapBytes / (1024d * 1024d),
            managedFractionOfWorkingSet = snap.ManagedFractionOfWorkingSet,
            severity = snap.Severity.ToString(),
            backend = snap.BackendMode.ToString(),
        }, JsonOpts);
    }
}
