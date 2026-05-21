#nullable enable

using System.Collections.Generic;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// Timeline T5 — per-mod context attendance roll-up.
///
/// <para>
/// Projects the upstream F2 <c>perModUsage</c> snapshot (owned by another
/// agent) into the timeline tab's attendance shape. We never reference
/// the F2 class directly because it may not exist at compile time — the
/// registry indirection is the contract.
/// </para>
///
/// <para>
/// Per Invariant 5 the modded-vs-vanilla split is decided by reading
/// <see cref="HookInterceptor.ProfiledModNames"/> for each
/// <see cref="ModUsageEntry.ModId"/>: the literal "Terraria" entry is
/// vanilla, everything else is modded. No string-matching on a specific
/// mod's name.
/// </para>
/// </summary>
public sealed class PerModContextAttendanceStat : IDataStat<AttendanceSnapshot>
{
    public string Name => RolloutStreamNames.Attendance;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public AttendanceSnapshot CurrentSnapshot()
    {
        IDataStream<ModUsageSnapshot>? stream = DataRegistry.Shared
            .Lookup<ModUsageSnapshot>(RolloutStreamNames.PerModUsage);
        ModUsageSnapshot usage = stream?.CurrentSnapshot() ?? ModUsageSnapshot.Empty;
        if (!usage.WorldLoaded || usage.Entries.Count == 0)
        {
            return AttendanceSnapshot.Empty;
        }

        string[] modNames = HookInterceptor.ProfiledModNames;

        long totalBiome = 0L;
        long moddedBiome = 0L;
        int totalInvasions = 0;
        int totalBoss = 0;

        List<AttendanceEntry> byMod = new(usage.Entries.Count);
        foreach (ModUsageEntry e in usage.Entries)
        {
            string name = (e.ModId >= 0 && e.ModId < modNames.Length)
                ? modNames[e.ModId]
                : ("mod-" + e.ModId);
            long biome = e.TicksInOwnedBiomes;
            int invasions = (int)e.InvasionsFought;
            int boss = (int)e.BossesFought;

            totalBiome += biome;
            totalInvasions += invasions;
            totalBoss += boss;
            // Vanilla owner is the literal "Terraria" entry. Everything else
            // is treated as modded for the split — Invariant 5 keeps this
            // string-comparison generic (it is not branching on any specific
            // mod's identity).
            if (!string.Equals(name, "Terraria", System.StringComparison.Ordinal))
            {
                moddedBiome += biome;
            }

            byMod.Add(new AttendanceEntry(
                ModId: e.ModId,
                ModName: name,
                BiomeTicks: biome,
                InvasionCount: invasions,
                BossSegmentCount: boss));
        }

        return new AttendanceSnapshot(
            worldLoaded: true,
            total: totalBiome,
            modded: moddedBiome,
            invasions: totalInvasions,
            bossSegs: totalBoss,
            byMod: byMod);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
