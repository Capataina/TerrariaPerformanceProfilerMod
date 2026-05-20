#nullable enable

using System;
using Terraria.ModLoader;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// Maps a tModLoader content type to one of <see cref="PerModAttribution.CategoryNames"/>'
/// category ids. Shared by both instrumentation backends so a future category
/// addition has one edit site, not two -- the delegate backend
/// (<see cref="HookInterceptor"/>) and the IL backend
/// (<see cref="ILHookInterceptor"/>) must always agree on category assignment or
/// the player- and agent-visible totals diverge.
/// </summary>
internal static class HookCategoryRouter
{
    // Indices into PerModAttribution.CategoryNames. Kept as named constants so
    // call sites read intent rather than magic numbers.
    internal const int CategorySystems = 0;
    internal const int CategoryPlayers = 1;
    internal const int CategoryNpcs = 2;
    internal const int CategoryProjectiles = 3;
    internal const int CategoryItems = 4;
    internal const int CategoryWorld = 5;
    internal const int CategoryBuffs = 6;

    /// <summary>
    /// Returns the category id for <paramref name="type"/>, or <c>-1</c> if the
    /// type is not one of the profiled content kinds. The fallback exists so
    /// callers can skip types early without throwing -- mods often expose
    /// internal helper types alongside their ModX content classes.
    /// </summary>
    internal static int ResolveCategory(Type type)
    {
        if (typeof(ModSystem).IsAssignableFrom(type)) return CategorySystems;
        if (typeof(ModPlayer).IsAssignableFrom(type)) return CategoryPlayers;
        if (typeof(ModNPC).IsAssignableFrom(type)) return CategoryNpcs;
        if (typeof(ModProjectile).IsAssignableFrom(type)) return CategoryProjectiles;
        if (typeof(GlobalNPC).IsAssignableFrom(type)) return CategoryNpcs;
        if (typeof(GlobalProjectile).IsAssignableFrom(type)) return CategoryProjectiles;
        if (typeof(ModItem).IsAssignableFrom(type) || typeof(GlobalItem).IsAssignableFrom(type)) return CategoryItems;
        if (typeof(ModTile).IsAssignableFrom(type) || typeof(GlobalTile).IsAssignableFrom(type) ||
            typeof(ModWall).IsAssignableFrom(type) || typeof(GlobalWall).IsAssignableFrom(type)) return CategoryWorld;
        if (typeof(ModBuff).IsAssignableFrom(type)) return CategoryBuffs;
        return -1;
    }
}
