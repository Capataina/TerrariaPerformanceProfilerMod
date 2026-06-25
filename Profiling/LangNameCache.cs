#nullable enable

using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Profiling;

/// <summary>
/// Project-wide cache of <c>Lang.Get*Name</c> lookups. v0.5 called
/// <c>Lang.GetBuffName(buffType)</c>, <c>Lang.GetItemName(itemType)</c>,
/// <c>Lang.GetProjectileName(projType).Value</c>, and
/// <c>Lang.GetNPCNameValue(npcType)</c> inline at row-emit time, on the
/// game thread, every event — 7 hot-path call sites per the cross-
/// allocations dossier §3.2.
///
/// <para>
/// Each underlying call hits a localisation dictionary plus a
/// <c>LocalizedText.Value</c> property that may format or allocate. By
/// pre-resolving every id at <c>PostSetupContent</c> into a flat
/// <c>string[]</c> indexed by type id, every per-event call becomes a
/// single array indexer access. Modded ids are resolved at population
/// time via the respective <c>Loader.GetXxx</c> APIs.
/// </para>
///
/// <para>
/// The cache fails open: if a type id is out of range, or population
/// hasn't happened yet, the lookup returns a synthetic
/// <c>"&lt;kind&gt;-&lt;id&gt;"</c> string. Callers don't need a null check.
/// </para>
/// </summary>
public static class LangNameCache
{
    private static string[] _buffNames = System.Array.Empty<string>();
    private static string[] _itemNames = System.Array.Empty<string>();
    private static string[] _projectileNames = System.Array.Empty<string>();
    private static string[] _npcNames = System.Array.Empty<string>();
    private static bool _populated;

    /// <summary>
    /// Populate the caches. Call once at <c>PostSetupContent</c> after every
    /// modded id has been registered. Idempotent — a second call rebuilds
    /// the arrays (useful for mod-reload paths).
    /// </summary>
    public static void Populate()
    {
        // Buffs: BuffLoader.BuffCount covers vanilla + every modded buff.
        int buffMax = BuffLoader.BuffCount;
        _buffNames = new string[buffMax];
        for (int i = 0; i < buffMax; i++)
        {
            try { _buffNames[i] = Lang.GetBuffName(i) ?? ("buff-" + i); }
            catch { _buffNames[i] = "buff-" + i; }
        }

        // Items.
        int itemMax = ItemLoader.ItemCount;
        _itemNames = new string[itemMax];
        for (int i = 0; i < itemMax; i++)
        {
            try
            {
                var lt = Lang.GetItemName(i);
                _itemNames[i] = lt?.Value ?? ("item-" + i);
            }
            catch { _itemNames[i] = "item-" + i; }
        }

        // Projectiles.
        int projMax = ProjectileLoader.ProjectileCount;
        _projectileNames = new string[projMax];
        for (int i = 0; i < projMax; i++)
        {
            try
            {
                var lt = Lang.GetProjectileName(i);
                _projectileNames[i] = lt?.Value ?? ("proj-" + i);
            }
            catch { _projectileNames[i] = "proj-" + i; }
        }

        // NPCs.
        int npcMax = NPCLoader.NPCCount;
        _npcNames = new string[npcMax];
        for (int i = 0; i < npcMax; i++)
        {
            try { _npcNames[i] = Lang.GetNPCNameValue(i) ?? ("npc-" + i); }
            catch { _npcNames[i] = "npc-" + i; }
        }

        _populated = true;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static string Buff(int buffType)
    {
        if ((uint)buffType < (uint)_buffNames.Length) return _buffNames[buffType];
        return "buff-" + buffType;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static string Item(int itemType)
    {
        if ((uint)itemType < (uint)_itemNames.Length) return _itemNames[itemType];
        return "item-" + itemType;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static string Projectile(int projType)
    {
        if ((uint)projType < (uint)_projectileNames.Length) return _projectileNames[projType];
        return "proj-" + projType;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static string Npc(int npcType)
    {
        if ((uint)npcType < (uint)_npcNames.Length) return _npcNames[npcType];
        return "npc-" + npcType;
    }

    /// <summary>Diagnostic: how many entries are cached per kind, plus the populated flag.</summary>
    public static (bool populated, int buffs, int items, int projectiles, int npcs) Stats()
        => (_populated, _buffNames.Length, _itemNames.Length, _projectileNames.Length, _npcNames.Length);
}
