#nullable enable

using System;
using System.Collections.Generic;
using Terraria.ModLoader;
using Terraria.ModLoader.Core;

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
/// Process-scoped cache of "what types live in this mod's assembly?"
///
/// <para>
/// Both <see cref="HookInterceptor"/> (delegate backend, dormant in v0.6)
/// and <see cref="ILHookInterceptor"/> (active backend) call
/// <see cref="AssemblyManager.GetLoadableTypes(System.Reflection.Assembly)"/>
/// once per loaded mod at install time. Without sharing, the same per-mod
/// walk runs twice: once for the delegate path's "discover candidates"
/// pass, then again for the ILHook path's "iterate every override" pass.
/// </para>
///
/// <para>
/// Per the mod-lifecycle dossier §4.6 (ε9): even though the actual
/// <c>GetLoadableTypes</c> call may itself be cached inside tML, the
/// downstream reflection state — <see cref="System.Reflection.MethodInfo"/>
/// arrays from <c>type.GetMethods(...)</c>, the per-method
/// <c>GetBaseDefinition()</c> lookup, the lazy reflection-cache entries
/// .NET retains — is paid twice. The dossier estimated 80-150 MB
/// retained-state reduction from sharing the type list (and downstream
/// method-walk byproducts) across both backends.
/// </para>
///
/// <para>
/// This file holds only the Type[] cache. Each backend still applies its
/// own filter (which methods qualify as hookable signatures); the heavy
/// part — the per-mod assembly walk — runs once.
/// </para>
/// </summary>
internal static class HookSurfaceCache
{
    private static readonly Dictionary<int, Type[]?> _byModId = new();

    /// <summary>
    /// Return the cached <see cref="Type"/> array for this mod, populating
    /// the cache on first call. Null entries (from a failed walk) are
    /// memoised so subsequent backends see the same outcome without
    /// re-throwing the same exception.
    /// </summary>
    public static Type[]? GetTypes(int modId, Mod mod, Mod self)
    {
        if (_byModId.TryGetValue(modId, out var cached)) return cached;

        Type[]? types;
        try
        {
            types = AssemblyManager.GetLoadableTypes(mod.Code);
        }
        catch (Exception ex)
        {
            self.Logger.Warn(
                $"HookSurfaceCache: skipped {mod.Name}, could not read its types: {ex.Message}");
            types = null;
        }
        _byModId[modId] = types;
        return types;
    }

    /// <summary>
    /// Wipe the cache. Called on profiler uninstall so a subsequent install
    /// re-scans (e.g. after mod reload).
    /// </summary>
    public static void Clear() => _byModId.Clear();
}
