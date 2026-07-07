#nullable enable

using System.Text.Json;

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
            // Per-tick self-overhead (A2 + A3): the profiler's own CPU cost that
            // its FrameTimeMs metric structurally cannot see. harvestMsEma is the
            // exact central bookkeeping cost; probeCallsPerTick is the count of
            // instrumented calls per tick (observer-effect magnitude).
            harvestMsEma = snap.HarvestMsEma,
            probeCallsPerTick = snap.ProbeCallsPerTickEma,
            probeCallsDrawPerTick = snap.ProbeCallsDrawPerTickEma,
            memoryGuard = BuildMemoryGuard(),
        }, JsonOpts);
    }

    /// <summary>
    /// The S04 memory-guard block: trend verdict + series + per-install arm
    /// history. Reads the process-singleton SelfHealth directly (same
    /// precedent as the routers' HookInterceptor static reads — the guard is
    /// process-level, world-independent) and the InstallArms collection via
    /// the open database. Null when the profiler hasn't armed yet.
    /// </summary>
    private static object? BuildMemoryGuard()
    {
        Profiling.ProfilerSelfHealth health = Profiling.ProfilerSystem.SelfHealth;

        var trend = health.MemoryTrendRing.Snapshot();
        var (unixMs, wsMb, managedMb) = health.MemoryTrendRing.CopySeries(maxPoints: 240);

        // Arm history for THIS process — the reload-stack surface.
        var arms = new System.Collections.Generic.List<object>();
        try
        {
            var db = PerformanceProfiler.Database;
            if (db != null)
            {
                using var proc = System.Diagnostics.Process.GetCurrentProcess();
                string processKey = $"{proc.Id}:{proc.StartTime.ToUniversalTime().Ticks}";
                foreach (var arm in db.InstallArms.Find(x => x.ProcessKey == processKey))
                {
                    arms.Add(new
                    {
                        armIndex = arm.ArmIndex,
                        installDeltaMb = arm.InstallDeltaBytes / (1024d * 1024d),
                        bytesPerHookKb = arm.BytesPerHook / 1024d,
                        hookCount = arm.HookCount,
                    });
                }
            }
        }
        catch { /* degraded: no arm history, trend still serves */ }

        return new
        {
            enabled = health.MemoryGuardEnabled,
            phase = trend.Phase.ToString(),
            growthMbPerMin10 = trend.GrowthMbPerMin10,
            currentWorkingSetMb = trend.CurrentWorkingSetMb,
            sessionStartWorkingSetMb = trend.SessionStartWorkingSetMb,
            peakWorkingSetMb = trend.PeakWorkingSetMb,
            sampleCount = trend.SampleCount,
            series = new { unixMs, wsMb, managedMb },
            armHistory = arms,
        };
    }
}
