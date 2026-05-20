#nullable enable

using System.Collections.Concurrent;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// Cache for "which mod owns this id" resolution. v0.5 called
/// <c>ItemLoader.GetItem(item.type)?.Mod?.Name</c> (and similar for
/// NPCs / Projectiles / Buffs) every emit time — the second-largest
/// repeated Lang-style lookup after <see cref="LangNameCache"/>.
///
/// <para>
/// The cache is populated lazily on first lookup. Vanilla content
/// (type &lt; vanilla id ceiling) resolves to <c>"Terraria"</c> without
/// a dictionary entry. Modded content stores the resolved mod-name
/// in a concurrent dictionary keyed by <c>(kind, id)</c>.
/// </para>
/// </summary>
public static class ModOwnerCache
{
    public enum Kind : byte { Item, Npc, Projectile, Buff }

    private static readonly ConcurrentDictionary<(Kind, int), string> _byTypeId = new();

    public static string ForItem(int itemType)
    {
        if (itemType < Terraria.ID.ItemID.Count) return "Terraria";
        return _byTypeId.GetOrAdd((Kind.Item, itemType), static k =>
            ItemLoader.GetItem(k.Item2)?.Mod?.Name ?? "Terraria");
    }

    public static string ForNpc(int npcType)
    {
        if (npcType < Terraria.ID.NPCID.Count) return "Terraria";
        return _byTypeId.GetOrAdd((Kind.Npc, npcType), static k =>
            NPCLoader.GetNPC(k.Item2)?.Mod?.Name ?? "Terraria");
    }

    public static string ForProjectile(int projType)
    {
        if (projType < Terraria.ID.ProjectileID.Count) return "Terraria";
        return _byTypeId.GetOrAdd((Kind.Projectile, projType), static k =>
            ProjectileLoader.GetProjectile(k.Item2)?.Mod?.Name ?? "Terraria");
    }

    public static string ForBuff(int buffType)
    {
        if (buffType < Terraria.ID.BuffID.Count) return "Terraria";
        return _byTypeId.GetOrAdd((Kind.Buff, buffType), static k =>
            BuffLoader.GetBuff(k.Item2)?.Mod?.Name ?? "Terraria");
    }

    /// <summary>
    /// Resolve the source-category name from an <see cref="IEntitySource"/>
    /// subclass. Strips the <c>EntitySource_</c> prefix from the type name.
    /// Cached via <see cref="System.Type.Name"/> (which is itself cached
    /// by the runtime), so repeated calls with the same source subclass
    /// are zero-alloc beyond the initial bookkeeping.
    /// </summary>
    public static string FromEntitySource(IEntitySource? source)
    {
        if (source == null) return "unknown";
        string n = source.GetType().Name;
        if (n.StartsWith("EntitySource_"))
            return n.Substring("EntitySource_".Length);
        return n;
    }

    /// <summary>Diagnostic: entries currently cached.</summary>
    public static int CachedCount => _byTypeId.Count;
}
