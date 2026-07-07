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
        }, JsonOpts);
    }
}
