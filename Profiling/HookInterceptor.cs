#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.ModLoader.Core;
using Terraria.UI;

namespace PerformanceProfiler.Profiling;

/// <summary>The original-method delegate for a parameterless instance hook.</summary>
public delegate void OrigVoidHook(object self);

/// <summary>The On-hook delegate that wraps a parameterless instance hook.</summary>
public delegate void VoidHookWrapper(OrigVoidHook orig, object self);

/// <summary>The original-method delegate for a GlobalNPC hook carrying an NPC.</summary>
public delegate void OrigNpcHook(object self, NPC npc);

/// <summary>The On-hook delegate that wraps a GlobalNPC hook carrying an NPC.</summary>
public delegate void NpcHookWrapper(OrigNpcHook orig, object self, NPC npc);

/// <summary>The original-method delegate for a GlobalProjectile hook carrying a Projectile.</summary>
public delegate void OrigProjectileHook(object self, Projectile projectile);

/// <summary>The On-hook delegate that wraps a GlobalProjectile hook carrying a Projectile.</summary>
public delegate void ProjectileHookWrapper(OrigProjectileHook orig, object self, Projectile projectile);

/// <summary>The original-method delegate for a ModSystem hook carrying GameTime.</summary>
public delegate void OrigGameTimeHook(object self, GameTime gameTime);

/// <summary>The On-hook delegate that wraps a ModSystem hook carrying GameTime.</summary>
public delegate void GameTimeHookWrapper(OrigGameTimeHook orig, object self, GameTime gameTime);

/// <summary>The original-method delegate for ModifyInterfaceLayers.</summary>
public delegate void OrigInterfaceLayersHook(object self, List<GameInterfaceLayer> layers);

/// <summary>The On-hook delegate that wraps ModifyInterfaceLayers.</summary>
public delegate void InterfaceLayersHookWrapper(OrigInterfaceLayersHook orig, object self, List<GameInterfaceLayer> layers);

/// <summary>The original-method delegate for a ModSystem hook carrying SpriteBatch.</summary>
public delegate void OrigSpriteBatchHook(object self, SpriteBatch spriteBatch);

/// <summary>The On-hook delegate that wraps a ModSystem hook carrying SpriteBatch.</summary>
public delegate void SpriteBatchHookWrapper(OrigSpriteBatchHook orig, object self, SpriteBatch spriteBatch);

/// <summary>The original-method delegate for a parameterless bool hook.</summary>
public delegate bool OrigBoolHook(object self);

/// <summary>The On-hook delegate that wraps a parameterless bool hook.</summary>
public delegate bool BoolHookWrapper(OrigBoolHook orig, object self);

/// <summary>The original-method delegate for a bool hook carrying an NPC.</summary>
public delegate bool OrigBoolNpcHook(object self, NPC npc);

/// <summary>The On-hook delegate that wraps a bool hook carrying an NPC.</summary>
public delegate bool BoolNpcHookWrapper(OrigBoolNpcHook orig, object self, NPC npc);

/// <summary>The original-method delegate for a bool hook carrying a Projectile.</summary>
public delegate bool OrigBoolProjectileHook(object self, Projectile projectile);

/// <summary>The On-hook delegate that wraps a bool hook carrying a Projectile.</summary>
public delegate bool BoolProjectileHookWrapper(OrigBoolProjectileHook orig, object self, Projectile projectile);

/// <summary>The original-method delegate for a bool hook carrying a Player.</summary>
public delegate bool OrigBoolPlayerHook(object self, Player player);

/// <summary>The On-hook delegate that wraps a bool hook carrying a Player.</summary>
public delegate bool BoolPlayerHookWrapper(OrigBoolPlayerHook orig, object self, Player player);

/// <summary>The original-method delegate for a bool hook carrying an Item.</summary>
public delegate bool OrigBoolItemHook(object self, Item item);

/// <summary>The On-hook delegate that wraps a bool hook carrying an Item.</summary>
public delegate bool BoolItemHookWrapper(OrigBoolItemHook orig, object self, Item item);

/// <summary>The original-method delegate for a void hook carrying a Player.</summary>
public delegate void OrigVoidPlayerHook(object self, Player player);

/// <summary>The On-hook delegate that wraps a void hook carrying a Player.</summary>
public delegate void VoidPlayerHookWrapper(OrigVoidPlayerHook orig, object self, Player player);

/// <summary>The original-method delegate for a void hook carrying an Item.</summary>
public delegate void OrigVoidItemHook(object self, Item item);

/// <summary>The On-hook delegate that wraps a void hook carrying an Item.</summary>
public delegate void VoidItemHookWrapper(OrigVoidItemHook orig, object self, Item item);

/// <summary>The original-method delegate for a void hook carrying an Item and a Player.</summary>
public delegate void OrigItemPlayerHook(object self, Item item, Player player);

/// <summary>The On-hook delegate that wraps a void(Item, Player) hook.</summary>
public delegate void ItemPlayerHookWrapper(OrigItemPlayerHook orig, object self, Item item, Player player);

/// <summary>The original-method delegate for a bool hook carrying an Item and a Player.</summary>
public delegate bool OrigBoolItemPlayerHook(object self, Item item, Player player);

/// <summary>The On-hook delegate that wraps a bool(Item, Player) hook.</summary>
public delegate bool BoolItemPlayerHookWrapper(OrigBoolItemPlayerHook orig, object self, Item item, Player player);

/// <summary>The original-method delegate for a void hook carrying an NPC and a Player.</summary>
public delegate void OrigNpcPlayerHook(object self, NPC npc, Player player);

/// <summary>The On-hook delegate that wraps a void(NPC, Player) hook.</summary>
public delegate void NpcPlayerHookWrapper(OrigNpcPlayerHook orig, object self, NPC npc, Player player);

/// <summary>The original-method delegate for a bool hook carrying an NPC and a Player.</summary>
public delegate bool OrigBoolNpcPlayerHook(object self, NPC npc, Player player);

/// <summary>The On-hook delegate that wraps a bool(NPC, Player) hook.</summary>
public delegate bool BoolNpcPlayerHookWrapper(OrigBoolNpcPlayerHook orig, object self, NPC npc, Player player);

/// <summary>The original-method delegate for a void hook carrying a Projectile and a Player.</summary>
public delegate void OrigProjectilePlayerHook(object self, Projectile projectile, Player player);

/// <summary>The On-hook delegate that wraps a void(Projectile, Player) hook.</summary>
public delegate void ProjectilePlayerHookWrapper(OrigProjectilePlayerHook orig, object self, Projectile projectile, Player player);

/// <summary>The original-method delegate for a bool hook carrying a Projectile and a Player.</summary>
public delegate bool OrigBoolProjectilePlayerHook(object self, Projectile projectile, Player player);

/// <summary>The On-hook delegate that wraps a bool(Projectile, Player) hook.</summary>
public delegate bool BoolProjectilePlayerHookWrapper(OrigBoolProjectilePlayerHook orig, object self, Projectile projectile, Player player);

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
/// Standard-mode coverage includes the per-entity GlobalNPC / GlobalProjectile
/// hooks that carry the entity parameter. Each installed hook also registers
/// a hot-path row so the UI can drill from mod -> category -> hook.
/// </summary>
public static class HookInterceptor
{
    // Hook categories, matching PerModAttribution.CategoryNames indices.
    private const int CategorySystems = 0;
    private const int CategoryPlayers = 1;
    private const int CategoryNpcs = 2;
    private const int CategoryProjectiles = 3;
    private const int CategoryItems = 4;
    private const int CategoryWorld = 5;
    private const int CategoryBuffs = 6;

    private const int MaxUnsupportedSamplesPerMod = 12;

    private static readonly string[] SystemHooks =
    {
        "PreUpdateEntities", "PostUpdateNPCs", "PostUpdatePlayers",
        "PostUpdateProjectiles", "PostUpdateEverything",
    };

    private static readonly string[] SystemGameTimeHooks = { "UpdateUI" };

    private static readonly string[] SystemInterfaceLayerHooks = { "ModifyInterfaceLayers" };

    private static readonly string[] SystemSpriteBatchHooks = { "PostDrawInterface" };

    private static readonly string[] PlayerHooks =
    {
        "PreUpdate", "PostUpdate", "PostUpdateEquips", "PostUpdateMiscEffects",
    };

    private static readonly string[] EntityHooks = { "AI", "PostAI" };

    private static readonly string[] GlobalNpcHooks = { "AI", "PostAI" };

    private static readonly string[] GlobalProjectileHooks = { "AI", "PostAI" };

    private static bool _sampleFailureLogged;
    private static int _unsupportedHookSignatures;
    private static int[] _measuredHookCounts = Array.Empty<int>();
    private static int[] _totalHookCounts = Array.Empty<int>();
    private static List<string>[] _unsupportedHookSamples = Array.Empty<List<string>>();
    private static readonly Dictionary<string, int> _unsupportedSignatureFrequency = new Dictionary<string, int>();

    /// <summary>Internal names of the mods being profiled, in ModId order. Empty until <see cref="Install"/> runs.</summary>
    public static string[] ProfiledModNames { get; private set; } = Array.Empty<string>();

    /// <summary>Mod versions, in ModId order. Empty until <see cref="Install"/> runs.</summary>
    public static string[] ProfiledModVersions { get; private set; } = Array.Empty<string>();

    /// <summary>Overrides discovered but skipped because their signature is not timed yet.</summary>
    public static int UnsupportedHookSignatures => _unsupportedHookSignatures;

    /// <summary>Measured hook count by ModId.</summary>
    public static IReadOnlyList<int> MeasuredHookCounts => _measuredHookCounts;

    /// <summary>Total discovered hook-override count by ModId.</summary>
    public static IReadOnlyList<int> TotalHookCounts => _totalHookCounts;

    /// <summary>Sample unsupported signatures by ModId, capped for report/UI readability.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> UnsupportedHookSamples => _unsupportedHookSamples;

    /// <summary>Frequency of each unsupported canonical signature shape, sorted descending by count.</summary>
    public static IReadOnlyDictionary<string, int> UnsupportedSignatureFrequency => _unsupportedSignatureFrequency;

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
            _unsupportedHookSignatures = 0;
            _unsupportedSignatureFrequency.Clear();
            List<Mod> profiled = new List<Mod>();
            foreach (Mod mod in ModLoader.Mods)
            {
                // Skip only the synthetic ModLoaderMod. The profiler itself is
                // included so its own hooks are measured like any other mod's.
                if (mod.Name != "ModLoader")
                {
                    profiled.Add(mod);
                }
            }

            ProfiledModNames = new string[profiled.Count];
            ProfiledModVersions = new string[profiled.Count];
            for (int i = 0; i < profiled.Count; i++)
            {
                ProfiledModNames[i] = profiled[i].Name;
                ProfiledModVersions[i] = profiled[i].Version?.ToString() ?? "unknown";
            }

            _measuredHookCounts = new int[profiled.Count];
            _totalHookCounts = new int[profiled.Count];
            _unsupportedHookSamples = new List<string>[profiled.Count];
            for (int i = 0; i < _unsupportedHookSamples.Length; i++)
            {
                _unsupportedHookSamples[i] = new List<string>();
            }

            PerModAttribution.Configure(profiled.Count);

            int detours = 0;
            for (int modId = 0; modId < profiled.Count; modId++)
            {
                detours += InstallForMod(modId, profiled[modId], self);
            }

            Installed = true;
            self.Logger.Info(
                $"HookInterceptor: {detours} timing detours installed across {profiled.Count} mods; " +
                $"{_unsupportedHookSignatures} overridden hooks skipped because their signature is not timed yet.");
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
                count += HookSupportedOverrides(type, modId, CategorySystems, self);
            }
            else if (typeof(ModPlayer).IsAssignableFrom(type))
            {
                count += HookSupportedOverrides(type, modId, CategoryPlayers, self);
            }
            else if (typeof(ModNPC).IsAssignableFrom(type))
            {
                count += HookSupportedOverrides(type, modId, CategoryNpcs, self);
            }
            else if (typeof(ModProjectile).IsAssignableFrom(type))
            {
                count += HookSupportedOverrides(type, modId, CategoryProjectiles, self);
            }
            else if (typeof(GlobalNPC).IsAssignableFrom(type))
            {
                count += HookSupportedOverrides(type, modId, CategoryNpcs, self);
            }
            else if (typeof(GlobalProjectile).IsAssignableFrom(type))
            {
                count += HookSupportedOverrides(type, modId, CategoryProjectiles, self);
            }
            else if (typeof(ModItem).IsAssignableFrom(type) || typeof(GlobalItem).IsAssignableFrom(type))
            {
                count += HookSupportedOverrides(type, modId, CategoryItems, self);
            }
            else if (typeof(ModTile).IsAssignableFrom(type) || typeof(GlobalTile).IsAssignableFrom(type) ||
                typeof(ModWall).IsAssignableFrom(type) || typeof(GlobalWall).IsAssignableFrom(type))
            {
                count += HookSupportedOverrides(type, modId, CategoryWorld, self);
            }
            else if (typeof(ModBuff).IsAssignableFrom(type))
            {
                count += HookSupportedOverrides(type, modId, CategoryBuffs, self);
            }
        }

        return count;
    }

    /// <summary>
    /// Installs timing detours on every override whose signature this interceptor
    /// can wrap. Unsupported signatures are counted as coverage debt, never as
    /// zero-cost behaviour.
    /// </summary>
    private static int HookSupportedOverrides(Type type, int modId, int categoryId, Mod self)
    {
        int count = 0;
        MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        foreach (MethodInfo method in methods)
        {
            if (method.IsSpecialName || method.IsAbstract)
            {
                continue;
            }

            if (!IsHookOverride(method))
            {
                continue;
            }

            if (TryHookSupportedOverride(method, type, modId, categoryId, self))
            {
                count++;
                _totalHookCounts[modId]++;
                _measuredHookCounts[modId]++;
            }
            else
            {
                RecordUnsupported(modId, type, method);
            }
        }

        return count;
    }

    private static bool IsHookOverride(MethodInfo method)
    {
        MethodInfo baseDefinition = method.GetBaseDefinition();
        return baseDefinition != method && baseDefinition.DeclaringType != typeof(object);
    }

    private static void RecordUnsupported(int modId, Type type, MethodInfo method)
    {
        _unsupportedHookSignatures++;

        ParameterInfo[] parameters = method.GetParameters();
        string shape = SignatureShape(method.ReturnType, parameters);
        _unsupportedSignatureFrequency[shape] = _unsupportedSignatureFrequency.TryGetValue(shape, out int existing) ? existing + 1 : 1;

        if ((uint)modId >= (uint)_totalHookCounts.Length)
        {
            return;
        }

        _totalHookCounts[modId]++;
        List<string> samples = _unsupportedHookSamples[modId];
        if (samples.Count < MaxUnsupportedSamplesPerMod)
        {
            samples.Add(DisplayName(type, method, parameters));
        }
    }

    private static string SignatureShape(Type returnType, ParameterInfo[] parameters)
    {
        if (parameters.Length == 0)
        {
            return $"{returnType.Name}()";
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append(returnType.Name).Append('(');
        for (int i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            Type pt = parameters[i].ParameterType;
            sb.Append(pt.IsByRef ? pt.GetElementType()!.Name + "&" : pt.Name);
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static bool TryHookSupportedOverride(MethodInfo method, Type type, int modId, int categoryId, Mod self)
    {
        ParameterInfo[] parameters = method.GetParameters();
        Type returnType = method.ReturnType;
        try
        {
            if (parameters.Length == 0 && returnType == typeof(void))
            {
                HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                MonoModHooks.Add(method, new VoidHookWrapper(probe.Time));
                return true;
            }

            if (parameters.Length == 0 && returnType == typeof(bool))
            {
                HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                MonoModHooks.Add(method, new BoolHookWrapper(probe.TimeBool));
                return true;
            }

            if (parameters.Length == 1)
            {
                Type p0 = parameters[0].ParameterType;
                if (p0 == typeof(NPC) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new NpcHookWrapper(probe.TimeNpc));
                    return true;
                }

                if (p0 == typeof(NPC) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolNpcHookWrapper(probe.TimeBoolNpc));
                    return true;
                }

                if (p0 == typeof(Projectile) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new ProjectileHookWrapper(probe.TimeProjectile));
                    return true;
                }

                if (p0 == typeof(Projectile) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolProjectileHookWrapper(probe.TimeBoolProjectile));
                    return true;
                }

                if (p0 == typeof(Player) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new VoidPlayerHookWrapper(probe.TimeVoidPlayer));
                    return true;
                }

                if (p0 == typeof(Player) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolPlayerHookWrapper(probe.TimeBoolPlayer));
                    return true;
                }

                if (p0 == typeof(Item) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new VoidItemHookWrapper(probe.TimeVoidItem));
                    return true;
                }

                if (p0 == typeof(Item) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolItemHookWrapper(probe.TimeBoolItem));
                    return true;
                }

                if (p0 == typeof(GameTime) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new GameTimeHookWrapper(probe.TimeGameTime));
                    return true;
                }

                if (p0 == typeof(List<GameInterfaceLayer>) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new InterfaceLayersHookWrapper(probe.TimeInterfaceLayers));
                    return true;
                }

                if (p0 == typeof(SpriteBatch) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new SpriteBatchHookWrapper(probe.TimeSpriteBatch));
                    return true;
                }
            }

            if (parameters.Length == 2)
            {
                Type p0 = parameters[0].ParameterType;
                Type p1 = parameters[1].ParameterType;

                if (p0 == typeof(Item) && p1 == typeof(Player) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new ItemPlayerHookWrapper(probe.TimeItemPlayer));
                    return true;
                }

                if (p0 == typeof(Item) && p1 == typeof(Player) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolItemPlayerHookWrapper(probe.TimeBoolItemPlayer));
                    return true;
                }

                if (p0 == typeof(NPC) && p1 == typeof(Player) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new NpcPlayerHookWrapper(probe.TimeNpcPlayer));
                    return true;
                }

                if (p0 == typeof(NPC) && p1 == typeof(Player) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolNpcPlayerHookWrapper(probe.TimeBoolNpcPlayer));
                    return true;
                }

                if (p0 == typeof(Projectile) && p1 == typeof(Player) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new ProjectilePlayerHookWrapper(probe.TimeProjectilePlayer));
                    return true;
                }

                if (p0 == typeof(Projectile) && p1 == typeof(Player) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolProjectilePlayerHookWrapper(probe.TimeBoolProjectilePlayer));
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            LogSampleHookFailure(type, method.Name, ex, self);
            return true;
        }

        return false;
    }

    private static HookProbe CreateProbe(int modId, int categoryId, Type type, MethodInfo method, ParameterInfo[] parameters)
    {
        int hookId = PerModAttribution.RegisterHook(modId, categoryId, DisplayName(type, method, parameters));
        return new HookProbe(modId, categoryId, hookId);
    }

    private static string DisplayName(Type type, MethodInfo method, ParameterInfo[] parameters)
    {
        return parameters.Length switch
        {
            0 => $"{type.Name}.{method.Name}()",
            1 => $"{type.Name}.{method.Name}({parameters[0].ParameterType.Name})",
            _ => $"{type.Name}.{method.Name}({parameters[0].ParameterType.Name}, {parameters[1].ParameterType.Name})",
        };
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
                int hookId = PerModAttribution.RegisterHook(modId, categoryId, $"{type.Name}.{name}()");
                HookProbe probe = new HookProbe(modId, categoryId, hookId);
                MonoModHooks.Add(method, new VoidHookWrapper(probe.Time));
                count++;
            }
            catch (Exception ex)
            {
                LogSampleHookFailure(type, name, ex, self);
            }
        }

        return count;
    }

    /// <summary>Installs timing detours on GlobalNPC hooks with signature void Hook(NPC npc).</summary>
    private static int HookNpcOverrides(Type type, string[] hookNames, int modId, Mod self)
    {
        int count = 0;
        foreach (string name in hookNames)
        {
            MethodInfo? method = type.GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, types: new[] { typeof(NPC) }, modifiers: null);

            if (method == null || method.DeclaringType != type || method.ReturnType != typeof(void))
            {
                continue;
            }

            try
            {
                int hookId = PerModAttribution.RegisterHook(modId, CategoryNpcs, $"{type.Name}.{name}(NPC)");
                HookProbe probe = new HookProbe(modId, CategoryNpcs, hookId);
                MonoModHooks.Add(method, new NpcHookWrapper(probe.TimeNpc));
                count++;
            }
            catch (Exception ex)
            {
                LogSampleHookFailure(type, name, ex, self);
            }
        }

        return count;
    }

    /// <summary>Installs timing detours on ModSystem hooks with signature void Hook(GameTime gameTime).</summary>
    private static int HookGameTimeOverrides(Type type, string[] hookNames, int modId, Mod self)
    {
        int count = 0;
        foreach (string name in hookNames)
        {
            MethodInfo? method = type.GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, types: new[] { typeof(GameTime) }, modifiers: null);

            if (method == null || method.DeclaringType != type || method.ReturnType != typeof(void))
            {
                continue;
            }

            try
            {
                int hookId = PerModAttribution.RegisterHook(modId, CategorySystems, $"{type.Name}.{name}(GameTime)");
                HookProbe probe = new HookProbe(modId, CategorySystems, hookId);
                MonoModHooks.Add(method, new GameTimeHookWrapper(probe.TimeGameTime));
                count++;
            }
            catch (Exception ex)
            {
                LogSampleHookFailure(type, name, ex, self);
            }
        }

        return count;
    }

    /// <summary>Installs timing detours on ModifyInterfaceLayers hooks.</summary>
    private static int HookInterfaceLayerOverrides(Type type, string[] hookNames, int modId, Mod self)
    {
        int count = 0;
        foreach (string name in hookNames)
        {
            MethodInfo? method = type.GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, types: new[] { typeof(List<GameInterfaceLayer>) }, modifiers: null);

            if (method == null || method.DeclaringType != type || method.ReturnType != typeof(void))
            {
                continue;
            }

            try
            {
                int hookId = PerModAttribution.RegisterHook(modId, CategorySystems, $"{type.Name}.{name}(layers)");
                HookProbe probe = new HookProbe(modId, CategorySystems, hookId);
                MonoModHooks.Add(method, new InterfaceLayersHookWrapper(probe.TimeInterfaceLayers));
                count++;
            }
            catch (Exception ex)
            {
                LogSampleHookFailure(type, name, ex, self);
            }
        }

        return count;
    }

    /// <summary>Installs timing detours on ModSystem hooks with signature void Hook(SpriteBatch spriteBatch).</summary>
    private static int HookSpriteBatchOverrides(Type type, string[] hookNames, int modId, Mod self)
    {
        int count = 0;
        foreach (string name in hookNames)
        {
            MethodInfo? method = type.GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, types: new[] { typeof(SpriteBatch) }, modifiers: null);

            if (method == null || method.DeclaringType != type || method.ReturnType != typeof(void))
            {
                continue;
            }

            try
            {
                int hookId = PerModAttribution.RegisterHook(modId, CategorySystems, $"{type.Name}.{name}(SpriteBatch)");
                HookProbe probe = new HookProbe(modId, CategorySystems, hookId);
                MonoModHooks.Add(method, new SpriteBatchHookWrapper(probe.TimeSpriteBatch));
                count++;
            }
            catch (Exception ex)
            {
                LogSampleHookFailure(type, name, ex, self);
            }
        }

        return count;
    }

    /// <summary>Installs timing detours on GlobalProjectile hooks with signature void Hook(Projectile projectile).</summary>
    private static int HookProjectileOverrides(Type type, string[] hookNames, int modId, Mod self)
    {
        int count = 0;
        foreach (string name in hookNames)
        {
            MethodInfo? method = type.GetMethod(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, types: new[] { typeof(Projectile) }, modifiers: null);

            if (method == null || method.DeclaringType != type || method.ReturnType != typeof(void))
            {
                continue;
            }

            try
            {
                int hookId = PerModAttribution.RegisterHook(modId, CategoryProjectiles, $"{type.Name}.{name}(Projectile)");
                HookProbe probe = new HookProbe(modId, CategoryProjectiles, hookId);
                MonoModHooks.Add(method, new ProjectileHookWrapper(probe.TimeProjectile));
                count++;
            }
            catch (Exception ex)
            {
                LogSampleHookFailure(type, name, ex, self);
            }
        }

        return count;
    }

    private static void LogSampleHookFailure(Type type, string name, Exception ex, Mod self)
    {
        if (!_sampleFailureLogged)
        {
            _sampleFailureLogged = true;
            self.Logger.Warn(
                $"HookInterceptor: a detour failed to install on {type.FullName}.{name} " +
                $"({ex.GetType().Name}: {ex.Message}); skipping it and continuing.");
        }
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
    private readonly int _hookId;

    public HookProbe(int modId, int categoryId, int hookId)
    {
        _modId = modId;
        _categoryId = categoryId;
        _hookId = hookId;
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
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a GlobalNPC hook and credits it without changing behaviour.</summary>
    public void TimeNpc(OrigNpcHook orig, object self, NPC npc)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, npc);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a GlobalProjectile hook and credits it without changing behaviour.</summary>
    public void TimeProjectile(OrigProjectileHook orig, object self, Projectile projectile)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, projectile);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a ModSystem GameTime hook and credits it without changing behaviour.</summary>
    public void TimeGameTime(OrigGameTimeHook orig, object self, GameTime gameTime)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, gameTime);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times ModifyInterfaceLayers and credits it without changing behaviour.</summary>
    public void TimeInterfaceLayers(OrigInterfaceLayersHook orig, object self, List<GameInterfaceLayer> layers)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, layers);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a ModSystem SpriteBatch hook and credits it without changing behaviour.</summary>
    public void TimeSpriteBatch(OrigSpriteBatchHook orig, object self, SpriteBatch spriteBatch)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, spriteBatch);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a bool hook and returns the original value unchanged.</summary>
    public bool TimeBool(OrigBoolHook orig, object self)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return orig(self);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a bool NPC hook and returns the original value unchanged.</summary>
    public bool TimeBoolNpc(OrigBoolNpcHook orig, object self, NPC npc)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return orig(self, npc);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a bool Projectile hook and returns the original value unchanged.</summary>
    public bool TimeBoolProjectile(OrigBoolProjectileHook orig, object self, Projectile projectile)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return orig(self, projectile);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a bool Player hook and returns the original value unchanged.</summary>
    public bool TimeBoolPlayer(OrigBoolPlayerHook orig, object self, Player player)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return orig(self, player);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a bool Item hook and returns the original value unchanged.</summary>
    public bool TimeBoolItem(OrigBoolItemHook orig, object self, Item item)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return orig(self, item);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a void(Player) hook.</summary>
    public void TimeVoidPlayer(OrigVoidPlayerHook orig, object self, Player player)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, player);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a void(Item) hook.</summary>
    public void TimeVoidItem(OrigVoidItemHook orig, object self, Item item)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, item);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a void(Item, Player) hook.</summary>
    public void TimeItemPlayer(OrigItemPlayerHook orig, object self, Item item, Player player)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, item, player);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a bool(Item, Player) hook and returns the original value unchanged.</summary>
    public bool TimeBoolItemPlayer(OrigBoolItemPlayerHook orig, object self, Item item, Player player)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return orig(self, item, player);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a void(NPC, Player) hook.</summary>
    public void TimeNpcPlayer(OrigNpcPlayerHook orig, object self, NPC npc, Player player)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, npc, player);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a bool(NPC, Player) hook and returns the original value unchanged.</summary>
    public bool TimeBoolNpcPlayer(OrigBoolNpcPlayerHook orig, object self, NPC npc, Player player)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return orig(self, npc, player);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a void(Projectile, Player) hook.</summary>
    public void TimeProjectilePlayer(OrigProjectilePlayerHook orig, object self, Projectile projectile, Player player)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, projectile, player);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>Times a bool(Projectile, Player) hook and returns the original value unchanged.</summary>
    public bool TimeBoolProjectilePlayer(OrigBoolProjectilePlayerHook orig, object self, Projectile projectile, Player player)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return orig(self, projectile, player);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }
}
