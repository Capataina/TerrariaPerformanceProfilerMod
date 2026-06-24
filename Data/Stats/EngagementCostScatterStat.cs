#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Insights.Shared;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// I6 — engagement-vs-cost scatter. One dot per mod: x = usage share,
/// y = CPU share, dot radius driven by roster size.
///
/// <para>
/// Truly-inert mods (zero usage AND CPU share &lt; 0.001) are filtered
/// out of the payload — they don't add information and would crowd the
/// origin. Threshold matches the renderer's sub-tick noise floor.
/// </para>
/// </summary>
public sealed class EngagementCostScatterStat : IDataStat<EngagementCostSnapshot>
{
    public const string StreamName = RolloutStreamNames.EngagementCost;
    private const double InertCpuThreshold = 0.001d;

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public EngagementCostSnapshot CurrentSnapshot()
    {
        string[] modNames = HookInterceptor.ProfiledModNames;
        int modCount = modNames.Length;
        if (modCount == 0) return EngagementCostSnapshot.Empty;

        // CPU share from HookCpuSnapshot.
        var cpuStream = DataRegistry.Shared.Lookup<HookCpuSnapshot>(HookCpuCollector.StreamName);
        HookCpuSnapshot cpu = cpuStream?.CurrentSnapshot() ?? HookCpuSnapshot.Empty;
        IReadOnlyList<double>? smoothed = cpu.SmoothedMsByCategory;
        int catCount = cpu.CategoryCount;

        double[] perModCpu = new double[modCount];
        double totalCpu = 0d;
        if (smoothed != null && catCount > 0 && cpu.ModCount > 0)
        {
            int n = Math.Min(modCount, cpu.ModCount);
            for (int m = 0; m < n; m++)
            {
                double sum = ModMetrics.SumModCategories(smoothed, m, catCount);
                perModCpu[m] = sum;
                totalCpu += sum;
            }
        }

        // Roster + usage via registry.
        ModRosterSnapshot roster = DataRegistry.Shared
            .Lookup<ModRosterSnapshot>(RolloutStreamNames.ModRoster)
            ?.CurrentSnapshot() ?? ModRosterSnapshot.Empty;
        ModUsageSnapshot usage = DataRegistry.Shared
            .Lookup<ModUsageSnapshot>(RolloutStreamNames.PerModUsage)
            ?.CurrentSnapshot() ?? ModUsageSnapshot.Empty;

        var rosterById = new Dictionary<int, ModRosterEntry>(roster.Mods.Count);
        for (int i = 0; i < roster.Mods.Count; i++)
        {
            rosterById[roster.Mods[i].ModId] = roster.Mods[i];
        }

        long[] usedPerMod = new long[modCount];
        long usedTotal = 0;
        for (int i = 0; i < usage.Entries.Count; i++)
        {
            var u = usage.Entries[i];
            if ((uint)u.ModId >= (uint)modCount) continue;
            long used = ModMetrics.UsageWeight(u);
            usedPerMod[u.ModId] = used;
            usedTotal += used;
        }

        var dots = new List<EngagementCostDot>(modCount);
        for (int m = 0; m < modCount; m++)
        {
            double usageShare = Shares.SafeShare(usedPerMod[m], usedTotal);
            double cpuShare = Shares.SafeShare(perModCpu[m], totalCpu);

            // Drop truly inert mods.
            if (usageShare == 0d && cpuShare < InertCpuThreshold) continue;

            rosterById.TryGetValue(m, out ModRosterEntry r);
            int rosterSize = ModMetrics.RosterSize(r);

            dots.Add(new EngagementCostDot(
                ModId: m,
                ModName: modNames[m],
                UsageShare: usageShare,
                CpuShare: cpuShare,
                RosterSize: rosterSize));
        }

        return new EngagementCostSnapshot(worldLoaded: true, dots: dots);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
