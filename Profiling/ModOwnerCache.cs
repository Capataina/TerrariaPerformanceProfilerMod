#nullable enable

using System.Collections.Concurrent;
using Terraria.DataStructures;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
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

    // Memoises the EntitySource_-stripped name per source subclass Type so the
    // Substring runs once per subclass for the whole session, not once per call.
    // Same pattern as _byTypeId; the key set is the count of loaded IEntitySource
    // subclasses (tens), so growth is bounded.
    private static readonly ConcurrentDictionary<System.Type, string> _bySourceType = new();

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
    /// subclass, stripping the <c>EntitySource_</c> prefix from the type name.
    /// The stripped result is memoised per source <see cref="System.Type"/> in
    /// <see cref="_bySourceType"/>, so the <c>Substring</c> allocation happens
    /// exactly once per subclass for the session; repeated calls with the same
    /// source subclass return the cached reference with no allocation.
    /// </summary>
    public static string FromEntitySource(IEntitySource? source)
    {
        if (source == null) return "unknown";
        return _bySourceType.GetOrAdd(source.GetType(), static t =>
        {
            string n = t.Name;
            return n.StartsWith("EntitySource_") ? n.Substring("EntitySource_".Length) : n;
        });
    }

    /// <summary>Diagnostic: entries currently cached.</summary>
    public static int CachedCount => _byTypeId.Count;
}
