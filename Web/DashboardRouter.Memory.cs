#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Web;

internal static partial class DashboardRouter
{
    // ----------------------------------------------------------------------
    // /api/memory — per-mod RAM. Two honest bases joined per mod:
    //
    //   * scaffolding (ours): the profiler's measured install-delta
    //     (ProfilerSelfHealth) distributed across mods by each mod's share of
    //     installed hooks, so the slices sum exactly to the Self tab's number.
    //     This is the RAM we hold to instrument each mod, not the mod's own RAM.
    //
    //   * tML estimate: tModLoader's own per-mod footprint (code / textures /
    //     sounds / managed), read via guarded reflection (ModRamReader). This is
    //     the mod's own RAM, the same numbers tML logs as "most RAM".
    //
    // The strip can render either basis; the drawer shows both. RAM is reshaped,
    // never invented — the scaffolding total is measured and the tML numbers are
    // tML's own. Uniform bytes/hook is a first-order assumption for the
    // distribution (a mod hooking larger bodies retains slightly more per hook);
    // the UI badges the scaffolding column as an estimate (Invariant 3).
    // ----------------------------------------------------------------------
    private static string BuildMemory()
    {
        var cpuSnap = Data.DataRegistry.Shared
            .Lookup<Data.Collectors.HookCpuSnapshot>(Data.Collectors.HookCpuCollector.StreamName)?
            .CurrentSnapshot() ?? Data.Collectors.HookCpuSnapshot.Empty;
        var allocSnap = Data.DataRegistry.Shared
            .Lookup<Data.Collectors.AllocationSnapshot>(Data.Collectors.AllocationCollector.StreamName)?
            .CurrentSnapshot() ?? Data.Collectors.AllocationSnapshot.Empty;
        var selfSnap = Data.DataRegistry.Shared
            .Lookup<Data.Stats.SelfHealthSnapshot>(Data.Stats.SelfHealthStat.StreamName)?
            .CurrentSnapshot() ?? default;

        string[] modNames = HookInterceptor.ProfiledModNames;
        if (modNames.Length == 0)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, mods = Array.Empty<object>() }, JsonOpts);
        }

        // Per-mod installed-hook counts from the active backend. Same modId index
        // as ProfiledModNames — both derive from HookInterceptor.ProfiledMods.
        IReadOnlyList<int> hookCounts = HookBackend.ILHookActive
            ? ILHookInterceptor.MeasuredHookCounts
            : HookInterceptor.MeasuredHookCounts;
        long scaffoldBytes = selfSnap.InstallDeltaBytes;
        long totalHooks = 0;
        for (int i = 0; i < modNames.Length && i < hookCounts.Count; i++)
        {
            totalHooks += hookCounts[i];
        }
        bool scaffoldAvailable = selfSnap.Installed && scaffoldBytes > 0 && totalHooks > 0;

        // tML's own per-mod estimate (guarded reflection; may be unavailable).
        bool tmlAvailable = ModRamReader.TryRead(out var tml);
        long tmlTotalBytes = 0;

        // Per-mod allocation rate (smoothed bytes/tick summed across categories).
        int categoryCount = cpuSnap.CategoryCount;
        IReadOnlyList<double>? smoothedBytes = allocSnap.SmoothedBytesByCategory;
        bool tracksAlloc = allocSnap.TracksAllocations && smoothedBytes != null && categoryCount > 0;

        var mods = new List<object>(modNames.Length);
        for (int i = 0; i < modNames.Length; i++)
        {
            int hookCount = i < hookCounts.Count ? hookCounts[i] : 0;
            double hookBytes = scaffoldAvailable ? scaffoldBytes * ((double)hookCount / totalHooks) : 0d;

            double alloc = 0d;
            if (tracksAlloc)
            {
                int baseIdx = i * categoryCount;
                for (int cat = 0; cat < categoryCount; cat++)
                {
                    alloc += smoothedBytes![baseIdx + cat];
                }
            }

            long tmlTotal = 0, tmlManaged = 0, tmlCode = 0, tmlTex = 0, tmlSnd = 0;
            if (tmlAvailable && tml.TryGetValue(modNames[i], out var r))
            {
                tmlTotal = r.Total;
                tmlManaged = r.Managed;
                tmlCode = r.Code;
                tmlTex = r.Textures;
                tmlSnd = r.Sounds;
                tmlTotalBytes += tmlTotal;
            }

            mods.Add(new
            {
                id = i,
                name = modNames[i],
                hookBytes,
                hookCount,
                allocBytes = alloc,
                tmlTotal,
                tmlManaged,
                tmlCode,
                tmlTextures = tmlTex,
                tmlSounds = tmlSnd,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = cpuSnap.WorldLoaded,
            scaffoldAvailable,
            scaffoldBytes,
            installedHookCount = selfSnap.InstalledHookCount,
            bytesPerHook = selfSnap.BytesPerHook,
            tmlAvailable,
            tmlTotalBytes,
            processRssBytes = selfSnap.ProcessWorkingSetBytes,
            processManagedBytes = selfSnap.ProcessManagedHeapBytes,
            tracksAllocations = tracksAlloc,
            mods,
        }, JsonOpts);
    }
}
