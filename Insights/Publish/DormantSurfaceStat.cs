#nullable enable

using PerformanceProfiler.Data;

using System.Collections.Generic;
using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Insights.Shared;

namespace PerformanceProfiler.Insights.Publish;

/// <summary>
/// I2 — dormant content surface. For each loaded mod, compares its roster
/// size (sum of items/NPCs/buffs/projectiles/mounts) against the count of
/// usage events recorded in the active session, and tags the category
/// most responsible for the gap.
///
/// <para>
/// The output is descriptive — a card titled "Items" simply names which
/// category currently shows the largest unused-roster delta. Invariant 3
/// forbids any "should remove" framing here; the renderer decides how to
/// present, the producer only states the measurement.
/// </para>
///
/// <para>
/// Both inputs come through <see cref="DataRegistry"/> lookups against the
/// foundation stream names, never direct refs to F1/F2 classes.
/// </para>
/// </summary>
public sealed class DormantSurfaceStat : IDataStat<DormantSurfaceSnapshot>
{
    public const string StreamName = RolloutStreamNames.DormantSurface;

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public DormantSurfaceSnapshot CurrentSnapshot()
    {
        ModRosterSnapshot roster = DataRegistry.Shared
            .Lookup<ModRosterSnapshot>(RolloutStreamNames.ModRoster)
            ?.CurrentSnapshot() ?? ModRosterSnapshot.Empty;
        ModUsageSnapshot usage = DataRegistry.Shared
            .Lookup<ModUsageSnapshot>(RolloutStreamNames.PerModUsage)
            ?.CurrentSnapshot() ?? ModUsageSnapshot.Empty;

        if (roster.Mods.Count == 0)
        {
            return DormantSurfaceSnapshot.Empty;
        }

        // Index usage by ModId, and find the most-used mod so the ratio is a
        // normalised active-use intensity (0..1) rather than the old
        // used/roster fraction — which is no longer meaningful now that "used"
        // is active-use ticks, not a content count (Wave 4).
        var usageById = new Dictionary<int, ModUsageEntry>(usage.Entries.Count);
        long maxUsed = 0;
        for (int i = 0; i < usage.Entries.Count; i++)
        {
            var u = usage.Entries[i];
            usageById[u.ModId] = u;
            long w = ModMetrics.UsageWeight(u);
            if (w > maxUsed) maxUsed = w;
        }

        var entries = new List<DormantEntry>(roster.Mods.Count);
        int zero = 0, lowUse = 0;
        for (int i = 0; i < roster.Mods.Count; i++)
        {
            ModRosterEntry r = roster.Mods[i];
            int rosterSize = ModMetrics.RosterSize(r);
            usageById.TryGetValue(r.ModId, out ModUsageEntry u);
            // Active-use ticks: content held / worn / stood-in this session. Zero
            // means genuinely dormant; a wielded-but-not-crafted weapon now counts
            // (the Flute-reads-zero fix), where the old creation-only weight read 0.
            long used = ModMetrics.UsageWeight(u);
            int usedCount = used > int.MaxValue ? int.MaxValue : (int)used;

            // Intensity relative to your most-used mod (0..1): 1.0 = the mod whose
            // content you engaged with most, 0 = never touched.
            double ratio = Shares.SafeShare(used, maxUsed);
            string dominant = DominantUnusedCategory(r, u);

            if (used == 0) zero++;
            if (ratio < 0.05d) lowUse++;

            entries.Add(new DormantEntry(
                ModId: r.ModId,
                ModName: r.ModName,
                RosterSize: rosterSize,
                UsedCount: usedCount,
                UsageRatio: ratio,
                DominantUnusedCategory: dominant));
        }

        // Most dormant first.
        entries.Sort((a, b) => a.UsageRatio.CompareTo(b.UsageRatio));

        return new DormantSurfaceSnapshot(worldLoaded: true,
            loaded: roster.Mods.Count, zero: zero, lowUse: lowUse, entries: entries);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();

    /// <summary>
    /// Picks the category with the largest (roster_count - used_count) delta.
    /// The label set is descriptive ("items", "NPCs", "buffs", "bosses") and
    /// matches the roster field names; Invariant 3 keeps the strings neutral.
    /// </summary>
    private static string DominantUnusedCategory(ModRosterEntry r, ModUsageEntry u)
    {
        long itemDelta  = r.Items   - u.ItemsCreated;
        long npcDelta   = r.NPCs    - u.NpcsSpawned;
        long buffDelta  = r.Buffs   - u.BuffsApplied;
        long bossDelta  = r.Bosses  - u.BossesFought;

        long best = itemDelta;
        string bestLabel = "items";
        if (npcDelta > best) { best = npcDelta; bestLabel = "NPCs"; }
        if (buffDelta > best) { best = buffDelta; bestLabel = "buffs"; }
        if (bossDelta > best) { best = bossDelta; bestLabel = "bosses"; }

        // If everything's been used at least as much as it exists, surface that.
        if (best <= 0) return "none";
        return bestLabel;
    }
}
