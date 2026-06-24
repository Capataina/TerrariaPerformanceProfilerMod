#nullable enable

using PerformanceProfiler.Data;

using System;
using System.Collections.Generic;
using LiteDB;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Insights.Shared;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Insights.Publish;

/// <summary>
/// Per-mod observatory cards — the I1 + I3 + I4 composite for the Insights
/// tab's mod-detail surface.
///
/// <para>
/// Composition: F1 roster + F2 usage + per-mod CPU smoothed/avg from
/// <see cref="HookCpuSnapshot"/>, plus I3 modded-biome attendance
/// (aggregated, see limitation below) and I4 top-equipped items (queried
/// from the LiteDB <c>loadoutSnapshots</c> collection for the live
/// session).
/// </para>
///
/// <para>
/// Limitations:
/// <list type="bullet">
///   <item>F2's <see cref="ModUsageEntry.TicksInOwnedBiomes"/> is a
///   per-mod aggregate, not a per-biome breakdown — so each card emits a
///   single <see cref="BiomeAttendanceEntry"/> summarising the modded-biome
///   time. When a per-biome aggregator lands later, replace the loop in
///   <see cref="BuildBiomeAttendance"/>.</item>
///   <item>I4's "top items" is computed by approximating equipped-ticks
///   as the gap between consecutive snapshots' Tick values. A periodic
///   30s anchor + change-edge snapshot scheme gives this enough
///   resolution to rank stable favourites.</item>
/// </list>
/// </para>
/// </summary>
public sealed class ModObservatoryStat : IDataStat<ModObservatorySnapshot>
{
    public const string StreamName = RolloutStreamNames.ModObservatory;

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public ModObservatorySnapshot CurrentSnapshot()
    {
        string[] modNames = HookInterceptor.ProfiledModNames;
        int modCount = modNames.Length;
        if (modCount == 0) return ModObservatorySnapshot.Empty;

        // ---- CPU surface ---------------------------------------------------
        var cpuStream = DataRegistry.Shared.Lookup<HookCpuSnapshot>(HookCpuCollector.StreamName);
        HookCpuSnapshot cpu = cpuStream?.CurrentSnapshot() ?? HookCpuSnapshot.Empty;
        int catCount = cpu.CategoryCount;
        IReadOnlyList<double>? smoothed = cpu.SmoothedMsByCategory;
        IReadOnlyList<double>? averaged = cpu.AverageMsByCategory;

        double[] perModSmoothed = new double[modCount];
        double[] perModAverage = new double[modCount];
        double totalSmoothed = 0d;
        if (smoothed != null && catCount > 0 && cpu.ModCount > 0)
        {
            int n = Math.Min(modCount, cpu.ModCount);
            for (int m = 0; m < n; m++)
            {
                double sumS = ModMetrics.SumModCategories(smoothed, m, catCount);
                double sumA = averaged != null ? ModMetrics.SumModCategories(averaged, m, catCount) : 0d;
                perModSmoothed[m] = sumS;
                perModAverage[m] = sumA;
                totalSmoothed += sumS;
            }
        }

        // ---- Roster + usage via registry ----------------------------------
        ModRosterSnapshot roster = DataRegistry.Shared
            .Lookup<ModRosterSnapshot>(RolloutStreamNames.ModRoster)
            ?.CurrentSnapshot() ?? ModRosterSnapshot.Empty;
        ModUsageSnapshot usage = DataRegistry.Shared
            .Lookup<ModUsageSnapshot>(RolloutStreamNames.PerModUsage)
            ?.CurrentSnapshot() ?? ModUsageSnapshot.Empty;

        // Index by ModId for O(1) lookup.
        var rosterById = new Dictionary<int, ModRosterEntry>(roster.Mods.Count);
        for (int i = 0; i < roster.Mods.Count; i++)
        {
            var e = roster.Mods[i];
            rosterById[e.ModId] = e;
        }
        var usageById = new Dictionary<int, ModUsageEntry>(usage.Entries.Count);
        long usageTotal = 0;
        long[] usageWeightPerMod = new long[modCount];
        for (int i = 0; i < usage.Entries.Count; i++)
        {
            var u = usage.Entries[i];
            usageById[u.ModId] = u;
            long w = ModMetrics.UsageWeight(u);
            if ((uint)u.ModId < (uint)modCount)
            {
                usageWeightPerMod[u.ModId] = w;
                usageTotal += w;
            }
        }

        // ---- Loadout top-items per mod (I4) -------------------------------
        Dictionary<int, List<LoadoutInfluenceItem>> topItemsByMod
            = BuildTopLoadoutItems(modCount);

        // ---- Compose cards -------------------------------------------------
        var cards = new List<ObservatoryCard>(modCount);
        int active = 0;
        for (int m = 0; m < modCount; m++)
        {
            double share = Shares.SafeShare(perModSmoothed[m], totalSmoothed);
            double usageShare = Shares.SafeShare(usageWeightPerMod[m], usageTotal);

            rosterById.TryGetValue(m, out ModRosterEntry rEntry);
            if (rEntry.ModName == null)
            {
                rEntry = new ModRosterEntry(m, modNames[m], 0, 0, 0, 0, 0, 0, 0, 0, 0);
            }
            usageById.TryGetValue(m, out ModUsageEntry uEntry);
            if (uEntry.ModId == 0 && m != 0 && !usageById.ContainsKey(m))
            {
                uEntry = new ModUsageEntry(m, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            }

            bool isActive = perModSmoothed[m] > 0 || usageWeightPerMod[m] > 0;
            if (isActive) active++;

            IReadOnlyList<BiomeAttendanceEntry> biomeAttendance
                = BuildBiomeAttendance(m, modNames[m], uEntry);

            topItemsByMod.TryGetValue(m, out List<LoadoutInfluenceItem>? topItems);
            IReadOnlyList<LoadoutInfluenceItem> topReadonly
                = topItems ?? (IReadOnlyList<LoadoutInfluenceItem>)Array.Empty<LoadoutInfluenceItem>();

            cards.Add(new ObservatoryCard(
                ModId: m,
                ModName: modNames[m],
                CpuSharePct: share,
                SmoothedMsThisTick: perModSmoothed[m],
                AverageMs: perModAverage[m],
                Roster: rEntry,
                Usage: uEntry,
                UsageSharePct: usageShare,
                BiomeAttendance: biomeAttendance,
                TopLoadoutItems: topReadonly));
        }

        // Sort by composite signal (CPU share + usage share), descending.
        cards.Sort((a, b) => (b.CpuSharePct + b.UsageSharePct)
            .CompareTo(a.CpuSharePct + a.UsageSharePct));

        int dormant = modCount - active;
        return new ModObservatorySnapshot(worldLoaded: true,
            active: active, dormant: dormant, cards: cards);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static IReadOnlyList<BiomeAttendanceEntry> BuildBiomeAttendance(
        int modId, string modName, ModUsageEntry uEntry)
    {
        // Limitation: F2 stores per-mod aggregate TicksInOwnedBiomes only.
        // Emit one entry summarising that aggregate. Replace with a per-biome
        // breakdown once a per-(mod,biome) tick aggregator lands.
        if (uEntry.TicksInOwnedBiomes <= 0)
        {
            return Array.Empty<BiomeAttendanceEntry>();
        }

        // Pick a representative biome bit index belonging to this mod, if any.
        int representativeBit = -1;
        string representativeName = modName + " (modded biomes)";
        IReadOnlyList<BiomeDescriptor> biomes = BiomeRegistry.Biomes;
        for (int i = 0; i < biomes.Count; i++)
        {
            BiomeDescriptor d = biomes[i];
            if (d.ModName != null && string.Equals(d.ModName, modName, StringComparison.Ordinal))
            {
                representativeBit = d.Id;
                representativeName = d.DisplayName;
                break;
            }
        }

        // Share-of-modded-biome-time is 1.0 since we collapse all owned
        // biomes into one bucket. Descriptive, not normative — Invariant 3.
        return new[]
        {
            new BiomeAttendanceEntry(
                BiomeBitIndex: representativeBit,
                BiomeName: representativeName,
                Ticks: uEntry.TicksInOwnedBiomes,
                SharePct: 1.0d),
        };
    }

    private static Dictionary<int, List<LoadoutInfluenceItem>> BuildTopLoadoutItems(int modCount)
    {
        var result = new Dictionary<int, List<LoadoutInfluenceItem>>();

        ProfilerSystem? system = Terraria.ModLoader.ModContent.GetInstance<ProfilerSystem>();
        var db = PerformanceProfiler.Database;
        if (db == null || system?.LiveRecorderSessionId is not ObjectId sid)
        {
            return result;
        }

        // Per-item accumulator: (modId, itemType) -> (slotKind, equippedTicks).
        // Held item is rolled in alongside slot items; its "slot kind" tag is "held".
        var perItem = new Dictionary<(int modId, int itemType),
            (string slotKind, string itemName, long ticks)>();

        // Pull every loadout snapshot for this session. Sort by Tick ascending
        // so consecutive deltas are well-defined.
        var rows = new List<LoadoutSnapshotRow>(64);
        try
        {
            foreach (var row in db.LoadoutSnapshots.Find(Query.EQ("SessionId", sid)))
            {
                rows.Add(row);
            }
        }
        catch
        {
            // DB read failure is non-fatal for a stat — return what we have.
            return result;
        }
        rows.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        if (rows.Count < 2) return result;

        for (int i = 0; i < rows.Count - 1; i++)
        {
            LoadoutSnapshotRow cur = rows[i];
            long delta = rows[i + 1].Tick - cur.Tick;
            if (delta <= 0) continue;

            // Held item, if any.
            if (cur.HeldItemType > 0)
            {
                AccumulateItem(perItem, cur.HeldItemType, cur.HeldItemName,
                    "held", delta, modCount);
            }

            // Each occupied slot.
            for (int s = 0; s < cur.Slots.Count; s++)
            {
                EquipmentSlotEntry slot = cur.Slots[s];
                if (slot.ItemType <= 0) continue;
                AccumulateItem(perItem, slot.ItemType, slot.ItemName,
                    slot.Kind, delta, modCount);
            }
        }

        // Group by owning mod, then top-5 per mod by ticks.
        var byMod = new Dictionary<int, List<LoadoutInfluenceItem>>();
        foreach (var kv in perItem)
        {
            int mid = kv.Key.modId;
            if (mid < 0) continue;
            if (!byMod.TryGetValue(mid, out var list))
            {
                list = new List<LoadoutInfluenceItem>(8);
                byMod[mid] = list;
            }
            list.Add(new LoadoutInfluenceItem(
                ItemType: kv.Key.itemType,
                ItemName: kv.Value.itemName,
                SlotKind: kv.Value.slotKind,
                EquippedTicks: kv.Value.ticks));
        }
        foreach (var kv in byMod)
        {
            Shares.TopN(kv.Value, 5, (a, b) => b.EquippedTicks.CompareTo(a.EquippedTicks));
            result[kv.Key] = kv.Value;
        }
        return result;
    }

    private static void AccumulateItem(
        Dictionary<(int, int), (string, string, long)> bag,
        int itemType, string itemName, string slotKind, long ticks, int modCount)
    {
        // Resolve owning mod via ModOwnerCache (Invariant 5: generic surface,
        // never a hardcoded mod identifier).
        string ownerName = ModOwnerCache.ForItem(itemType);
        int ownerId = ResolveModId(ownerName, modCount);
        if (ownerId < 0) return; // Vanilla items skipped — observatory is per-mod.

        var key = (ownerId, itemType);
        if (bag.TryGetValue(key, out var cur))
        {
            bag[key] = (cur.Item1, cur.Item2, cur.Item3 + ticks);
        }
        else
        {
            bag[key] = (slotKind, itemName, ticks);
        }
    }

    private static int ResolveModId(string modName, int modCount)
    {
        if (string.IsNullOrEmpty(modName) || modName == "Terraria") return -1;
        string[] profiled = HookInterceptor.ProfiledModNames;
        for (int i = 0; i < profiled.Length && i < modCount; i++)
        {
            if (string.Equals(profiled[i], modName, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }
}
