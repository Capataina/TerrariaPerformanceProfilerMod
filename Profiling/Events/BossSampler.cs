#nullable enable

using System.Collections.Generic;
using Terraria;
using Terraria.ID;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Profiling.Events;

/// <summary>
/// Scans <c>Main.npc[]</c> for active bosses and writes their NPC type ids
/// into a <see cref="BossSlotArray"/>. Multi-segment bosses (Eater of Worlds,
/// Wall of Flesh) are collapsed to their head via the <c>realLife</c>
/// convention so one fight yields one bucket regardless of segment count
/// (plan §3.6, R4).
///
/// <para>
/// Display-name resolution is lazy and cached: bucket lookup is keyed on the
/// integer NPC type, and the friendly name is fetched the first time a
/// bucket appears in the UI.
/// </para>
/// </summary>
internal static class BossSampler
{
    private static readonly Dictionary<int, string> _nameCache = new();

    /// <summary>
    /// Walks the NPC array, picking out actives that qualify as bosses,
    /// collapsing segments to heads, and de-duplicating per scan. Returns
    /// the number of slots populated. Allocation-free (the slot array is
    /// caller-owned by value).
    /// </summary>
    public static int Sample(ref BossSlotArray dest)
    {
        dest.Clear();

        NPC[] npcs = Main.npc;
        bool[] countsAsBoss = NPCID.Sets.ShouldBeCountedAsBoss;
        int count = 0;

        for (int i = 0; i < npcs.Length; i++)
        {
            NPC npc = npcs[i];
            if (!npc.active) continue;

            // Multi-segment collapse: realLife points to the head's whoAmI.
            // Negative on head/solo. Only the head contributes; segments
            // fold into the head's slot.
            int headWhoAmI = npc.realLife >= 0 ? npc.realLife : i;
            if (headWhoAmI != i) continue;

            int type = npc.type;
            bool qualifies = npc.boss
                || (type >= 0 && type < countsAsBoss.Length && countsAsBoss[type]);
            if (!qualifies) continue;

            short typeShort = (short)type;
            if (dest.Contains(typeShort)) continue;

            if (!dest.TryAdd(typeShort))
            {
                // Eight slots filled; chaotic modded encounter. Stop here
                // — the truncation is visible because Count remains 8 while
                // the scan continues to find more.
                break;
            }
            count++;
        }

        return count;
    }

    /// <summary>Friendly localised name for <paramref name="type"/>; cached after first lookup.</summary>
    public static string DisplayName(int type)
    {
        if (_nameCache.TryGetValue(type, out string? cached)) return cached;

        string name;
        try
        {
            string raw = Lang.GetNPCName(type)?.Value ?? string.Empty;
            name = string.IsNullOrEmpty(raw) ? "NPC:" + type : raw;
        }
        catch
        {
            name = "NPC:" + type;
        }

        _nameCache[type] = name;
        return name;
    }

    /// <summary>Empties the friendly-name cache; called on world unload.</summary>
    public static void Clear() => _nameCache.Clear();
}
