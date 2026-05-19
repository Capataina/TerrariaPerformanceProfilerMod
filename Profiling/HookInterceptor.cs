#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Terraria.ModLoader;
using Terraria.ModLoader.Core;

namespace PerformanceProfiler.Profiling;

/// <summary>The original-method delegate for a parameterless instance hook.</summary>
public delegate void OrigVoidHook(object self);

/// <summary>The On-hook delegate that wraps a parameterless instance hook.</summary>
public delegate void VoidHookWrapper(OrigVoidHook orig, object self);

/// <summary>
/// Installs per-mod CPU timing detours and holds the discovered modlist.
///
/// At setup it walks every loaded mod, finds that mod's overrides of a curated
/// set of parameterless per-tick hook methods, and installs a MonoMod On-hook
/// on each via <see cref="MonoModHooks.Add"/>. Each detour times the wrapped
/// call and credits the elapsed time to the owning mod and hook category
/// through <see cref="PerModAttribution"/>.
///
/// On-hooks (not IL edits) are used deliberately: an On-hook wraps a method and
/// can never corrupt its body, so a fault is contained to wrong numbers, never
/// a crash (Invariant 1 read-only, Invariant 4 abort-clean). tModLoader removes
/// these hooks automatically when this mod unloads, because every hook delegate
/// is declared in this assembly.
///
/// First cut: parameterless (void) instance hooks only -- one delegate shape.
/// The per-entity GlobalNPC / GlobalProjectile hooks, which carry a parameter,
/// are a planned follow-up.
/// </summary>
public static class HookInterceptor
{
    // Hook categories, matching PerModAttribution.CategoryNames indices.
    private const int CategorySystems = 0;
    private const int CategoryPlayers = 1;
    private const int CategoryNpcs = 2;
    private const int CategoryProjectiles = 3;

    private static readonly string[] SystemHooks =
    {
        "PreUpdateEntities", "PostUpdateNPCs", "PostUpdatePlayers",
        "PostUpdateProjectiles", "PostUpdateEverything",
    };

    private static readonly string[] PlayerHooks =
    {
        "PreUpdate", "PostUpdate", "PostUpdateEquips", "PostUpdateMiscEffects",
    };

    private static readonly string[] EntityHooks = { "AI", "PostAI" };

    private static bool _sampleFailureLogged;

    /// <summary>Internal names of the mods being profiled, in ModId order. Empty until <see cref="Install"/> runs.</summary>
    public static string[] ProfiledModNames { get; private set; } = Array.Empty<string>();

    /// <summary>True once the timing detours are installed.</summary>
    public static bool Installed { get; private set; }

    /// <summary>
    /// Discovers loaded mods and installs the timing detours. Call once, after
    /// all mod content is set up. Every step is guarded: a failure logs and
    /// disables the interceptor rather than leaving partial instrumentation
    /// (Invariant 4).
    /// </summary>
    public static void Install(Mod self)
    {
        if (Installed)
        {
            return;
        }

        try
        {
            List<Mod> profiled = new List<Mod>();
            foreach (Mod mod in ModLoader.Mods)
            {
                // Skip the synthetic ModLoaderMod and the profiler itself.
                if (mod.Name != "ModLoader" && mod != self)
                {
                    profiled.Add(mod);
                }
            }

            ProfiledModNames = new string[profiled.Count];
            for (int i = 0; i < profiled.Count; i++)
            {
                ProfiledModNames[i] = profiled[i].Name;
            }

            PerModAttribution.Configure(profiled.Count);

            int detours = 0;
            for (int modId = 0; modId < profiled.Count; modId++)
            {
                detours += InstallForMod(modId, profiled[modId], self);
            }

            Installed = true;
            self.Logger.Info($"HookInterceptor: {detours} timing detours installed across {profiled.Count} mods.");
        }
        catch (Exception ex)
        {
            Installed = false;
            self.Logger.Warn($"HookInterceptor disabled, install failed cleanly: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static int InstallForMod(int modId, Mod mod, Mod self)
    {
        Type[] types;
        try
        {
            types = AssemblyManager.GetLoadableTypes(mod.Code);
        }
        catch (Exception ex)
        {
            self.Logger.Warn($"HookInterceptor: skipped {mod.Name}, could not read its types: {ex.Message}");
            return 0;
        }

        int count = 0;
        foreach (Type type in types)
        {
            if (type.IsAbstract)
            {
                continue;
            }

            if (typeof(ModSystem).IsAssignableFrom(type))
            {
                count += HookOverrides(type, SystemHooks, modId, CategorySystems, self);
            }
            else if (typeof(ModPlayer).IsAssignableFrom(type))
            {
                count += HookOverrides(type, PlayerHooks, modId, CategoryPlayers, self);
            }
            else if (typeof(ModNPC).IsAssignableFrom(type))
            {
                count += HookOverrides(type, EntityHooks, modId, CategoryNpcs, self);
            }
            else if (typeof(ModProjectile).IsAssignableFrom(type))
            {
                count += HookOverrides(type, EntityHooks, modId, CategoryProjectiles, self);
            }
        }

        return count;
    }

    /// <summary>
    /// Installs a timing detour on each parameterless hook in <paramref name="hookNames"/>
    /// that <paramref name="type"/> actually overrides (declares itself).
    /// </summary>
    private static int HookOverrides(Type type, string[] hookNames, int modId, int categoryId, Mod self)
    {
        int count = 0;
        foreach (string name in hookNames)
        {
            MethodInfo? method = type.GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, types: Type.EmptyTypes, modifiers: null);

            // Hook only a hook the mod genuinely overrides on this type
            // (DeclaringType == type), and only the parameterless form.
            if (method == null || method.DeclaringType != type || method.GetParameters().Length != 0)
            {
                continue;
            }

            try
            {
                HookProbe probe = new HookProbe(modId, categoryId);
                MonoModHooks.Add(method, new VoidHookWrapper(probe.Time));
                count++;
            }
            catch (Exception ex)
            {
                // One uninstallable hook is skipped; the rest still install. The
                // first failure is logged so a wrong assumption is diagnosable.
                if (!_sampleFailureLogged)
                {
                    _sampleFailureLogged = true;
                    self.Logger.Warn(
                        $"HookInterceptor: a detour failed to install on {type.FullName}.{name} " +
                        $"({ex.GetType().Name}: {ex.Message}); skipping it and continuing.");
                }
            }
        }

        return count;
    }
}

/// <summary>
/// One installed timing detour: holds the ModId and hook category its hook
/// belongs to and times the wrapped call. One instance per detour, captured by
/// the hook delegate so the delegate is owned by this assembly (required for
/// correct teardown).
/// </summary>
internal sealed class HookProbe
{
    private readonly int _modId;
    private readonly int _categoryId;

    public HookProbe(int modId, int categoryId)
    {
        _modId = modId;
        _categoryId = categoryId;
    }

    /// <summary>Times the original hook and credits the elapsed time to the mod and category.</summary>
    public void Time(OrigVoidHook orig, object self)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self);
        }
        finally
        {
            // finally, not catch: a mod throwing is the mod's own behaviour and
            // is never swallowed (Invariant 1). The time up to the throw is
            // still credited.
            PerModAttribution.Add(_modId, _categoryId, Stopwatch.GetTimestamp() - start);
        }
    }
}
