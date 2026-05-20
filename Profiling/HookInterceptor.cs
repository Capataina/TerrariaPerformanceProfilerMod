#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
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

/// <summary>The original-method delegate for a parameterless nullable-bool hook (e.g. CanFallThroughPlatforms).</summary>
public delegate bool? OrigNullableBoolHook(object self);

/// <summary>The On-hook delegate that wraps a bool?() hook.</summary>
public delegate bool? NullableBoolHookWrapper(OrigNullableBoolHook orig, object self);

/// <summary>The original-method delegate for a bool hook with a ref Color parameter (e.g. PreDraw).</summary>
public delegate bool OrigBoolRefColorHook(object self, ref Color lightColor);

/// <summary>The On-hook delegate that wraps a bool(ref Color) hook.</summary>
public delegate bool BoolRefColorHookWrapper(OrigBoolRefColorHook orig, object self, ref Color lightColor);

/// <summary>The original-method delegate for a Color? hook carrying a Color (e.g. GetAlpha).</summary>
public delegate Color? OrigGetAlphaHook(object self, Color lightColor);

/// <summary>The On-hook delegate that wraps a Color?(Color) hook.</summary>
public delegate Color? GetAlphaHookWrapper(OrigGetAlphaHook orig, object self, Color lightColor);

/// <summary>The original-method delegate for a void hook carrying a single int (e.g. OnKill).</summary>
public delegate void OrigIntHook(object self, int value);

/// <summary>The On-hook delegate that wraps a void(int) hook.</summary>
public delegate void IntHookWrapper(OrigIntHook orig, object self, int value);

/// <summary>The original-method delegate for a void hook carrying a Player and a bool.</summary>
public delegate void OrigPlayerBoolHook(object self, Player player, bool flag);

/// <summary>The On-hook delegate that wraps a void(Player, bool) hook.</summary>
public delegate void PlayerBoolHookWrapper(OrigPlayerBoolHook orig, object self, Player player, bool flag);

/// <summary>The original-method delegate for a void hook carrying an NPC and a ref NPC.HitModifiers (e.g. ModifyHitNPC).</summary>
public delegate void OrigNpcRefHitModifiersHook(object self, NPC npc, ref NPC.HitModifiers modifiers);

/// <summary>The On-hook delegate that wraps a void(NPC, ref NPC.HitModifiers) hook.</summary>
public delegate void NpcRefHitModifiersHookWrapper(OrigNpcRefHitModifiersHook orig, object self, NPC npc, ref NPC.HitModifiers modifiers);

/// <summary>The original-method delegate for a void hook carrying an NPC, HitInfo, and int (e.g. OnHitNPC).</summary>
public delegate void OrigNpcHitInfoIntHook(object self, NPC target, NPC.HitInfo hit, int damageDone);

/// <summary>The On-hook delegate that wraps a void(NPC, HitInfo, int) hook.</summary>
public delegate void NpcHitInfoIntHookWrapper(OrigNpcHitInfoIntHook orig, object self, NPC target, NPC.HitInfo hit, int damageDone);

/// <summary>The original-method delegate for a void tile hook carrying two ints, a bool, and a ref int (e.g. KillTile).</summary>
public delegate void OrigTileIntIntBoolRefIntHook(object self, int i, int j, bool fail, ref int type);

/// <summary>The On-hook delegate that wraps a void(int, int, bool, ref int) hook.</summary>
public delegate void TileIntIntBoolRefIntHookWrapper(OrigTileIntIntBoolRefIntHook orig, object self, int i, int j, bool fail, ref int type);

/// <summary>The original-method delegate for an item-draw hook (e.g. PreDrawInInventory / PreDrawInWorld).</summary>
public delegate void OrigDrawItemHook(object self, SpriteBatch spriteBatch, Color color1, Color color2, float f1, float f2, int slot);

/// <summary>The On-hook delegate that wraps a void(SpriteBatch, Color, Color, float, float, int) hook.</summary>
public delegate void DrawItemHookWrapper(OrigDrawItemHook orig, object self, SpriteBatch spriteBatch, Color color1, Color color2, float f1, float f2, int slot);

/// <summary>The original-method delegate for ModItem/GlobalItem Shoot.</summary>
public delegate bool OrigShootHook(object self, Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockBack);

/// <summary>The On-hook delegate that wraps a bool(Player, EntitySource_ItemUse_WithAmmo, Vector2, Vector2, int, int, float) hook.</summary>
public delegate bool ShootHookWrapper(OrigShootHook orig, object self, Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockBack);

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
    /// <summary>
    /// Incremented every time a new batch of signature families is added to
    /// <see cref="TryHookSupportedOverride"/>, OR when coverage accounting
    /// semantics change in a way that would make old session totals
    /// non-comparable. <see cref="SessionLogWriter"/> folds this into the
    /// session identity hash so old sessions measured with a narrower coverage
    /// set are pruned automatically on the next load. v3 split install-failures
    /// out of the unsupported-signature bucket.
    /// </summary>
    public const int HookCoverageVersion = 3;

    // Category ids live in HookCategoryRouter — both backends share that map so
    // category drift between delegate and ILHook paths is impossible.

    private const int MaxUnsupportedSamplesPerMod = 12;

    private static bool _sampleFailureLogged;
    private static int _unsupportedHookSignatures;
    private static int _installFailures;
    private static int[] _measuredHookCounts = Array.Empty<int>();
    private static int[] _totalHookCounts = Array.Empty<int>();
    private static List<string>[] _unsupportedHookSamples = Array.Empty<List<string>>();
    private static readonly Dictionary<string, int> _unsupportedSignatureFrequency = new Dictionary<string, int>();

    /// <summary>Internal names of the mods being profiled, in ModId order. Empty until <see cref="Install"/> runs.</summary>
    public static string[] ProfiledModNames { get; private set; } = Array.Empty<string>();

    /// <summary>Mod versions, in ModId order. Empty until <see cref="Install"/> runs.</summary>
    public static string[] ProfiledModVersions { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// Discovered mods being profiled, in ModId order. <see cref="ILHookInterceptor"/>
    /// reads this so both backends instrument the same modlist with consistent ids.
    /// Empty until <see cref="Install"/> runs.
    /// </summary>
    public static IReadOnlyList<Mod> ProfiledMods { get; private set; } = Array.Empty<Mod>();

    /// <summary>Overrides discovered but skipped because their signature is not timed yet.</summary>
    public static int UnsupportedHookSignatures => _unsupportedHookSignatures;

    /// <summary>
    /// Overrides whose signature WAS in the supported set but where
    /// <see cref="MonoModHooks.Add"/> itself threw during install. Counted
    /// separately from unsupported signatures because the failure mode and the
    /// remediation are different: an unsupported signature is coverage debt
    /// (we add the delegate pair), an install failure is a tModLoader/MonoMod
    /// runtime issue worth surfacing in client.log and the session JSON.
    /// </summary>
    public static int InstallFailures => _installFailures;

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
            _installFailures = 0;
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

            ProfiledMods = profiled;

            _measuredHookCounts = new int[profiled.Count];
            _totalHookCounts = new int[profiled.Count];
            _unsupportedHookSamples = new List<string>[profiled.Count];
            for (int i = 0; i < _unsupportedHookSamples.Length; i++)
            {
                _unsupportedHookSamples[i] = new List<string>();
            }

            // Size attribution for the chosen number of backends (1 in single-mode,
            // 2 in Parallel) and optionally allocate the byte arrays for allocation
            // tracking. Must run before any RegisterHook call so backend slots
            // exist when detours start writing.
            PerModAttribution.Configure(profiled.Count, HookBackend.BackendCount, HookBackend.AllocationTracking);

            int detours = 0;
            if (HookBackend.DelegateActive)
            {
                for (int modId = 0; modId < profiled.Count; modId++)
                {
                    detours += InstallForMod(modId, profiled[modId], self);
                }
            }

            Installed = true;
            self.Logger.Info(
                $"HookInterceptor[{HookBackend.Mode}]: {detours} delegate timing detours installed across {profiled.Count} mods; " +
                $"{_unsupportedHookSignatures} overridden hooks skipped because their signature is not timed yet; " +
                $"{_installFailures} hooks failed to install (MonoMod runtime error).");
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

            int categoryId = HookCategoryRouter.ResolveCategory(type);
            if (categoryId < 0)
            {
                continue;
            }

            count += HookSupportedOverrides(type, modId, categoryId, self);
        }

        return count;
    }

    /// <summary>The three outcomes <see cref="TryHookSupportedOverride"/> can produce.</summary>
    private enum InstallOutcome
    {
        /// <summary>A delegate detour was installed for this override.</summary>
        Installed,
        /// <summary>The signature shape has no delegate pair in this interceptor.</summary>
        UnsupportedSignature,
        /// <summary>The signature was supported but <see cref="MonoModHooks.Add"/> threw.</summary>
        InstallFailed,
    }

    /// <summary>
    /// Installs timing detours on every override whose signature this interceptor
    /// can wrap. Unsupported signatures are counted as coverage debt, install
    /// failures are tracked separately, and both increment the discovered total
    /// so coverage shows the gap honestly.
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

            switch (TryHookSupportedOverride(method, type, modId, categoryId, self))
            {
                case InstallOutcome.Installed:
                    count++;
                    if ((uint)modId < (uint)_totalHookCounts.Length)
                    {
                        _totalHookCounts[modId]++;
                        _measuredHookCounts[modId]++;
                    }
                    break;

                case InstallOutcome.UnsupportedSignature:
                    RecordUnsupported(modId, type, method);
                    break;

                case InstallOutcome.InstallFailed:
                    // Counts as discovered (so coverage shows the gap) but not
                    // measured. Signature histogram stays clean of install errors.
                    _installFailures++;
                    if ((uint)modId < (uint)_totalHookCounts.Length)
                    {
                        _totalHookCounts[modId]++;
                    }
                    break;
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

    private static InstallOutcome TryHookSupportedOverride(MethodInfo method, Type type, int modId, int categoryId, Mod self)
    {
        ParameterInfo[] parameters = method.GetParameters();
        Type returnType = method.ReturnType;
        try
        {
            if (parameters.Length == 0 && returnType == typeof(void))
            {
                HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                MonoModHooks.Add(method, new VoidHookWrapper(probe.Time));
                return InstallOutcome.Installed;
            }

            if (parameters.Length == 0 && returnType == typeof(bool))
            {
                HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                MonoModHooks.Add(method, new BoolHookWrapper(probe.TimeBool));
                return InstallOutcome.Installed;
            }

            if (parameters.Length == 0 && returnType == typeof(bool?))
            {
                HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                MonoModHooks.Add(method, new NullableBoolHookWrapper(probe.TimeNullableBool));
                return InstallOutcome.Installed;
            }

            if (parameters.Length == 1)
            {
                Type p0 = parameters[0].ParameterType;
                if (p0 == typeof(NPC) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new NpcHookWrapper(probe.TimeNpc));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(NPC) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolNpcHookWrapper(probe.TimeBoolNpc));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(Projectile) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new ProjectileHookWrapper(probe.TimeProjectile));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(Projectile) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolProjectileHookWrapper(probe.TimeBoolProjectile));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(Player) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new VoidPlayerHookWrapper(probe.TimeVoidPlayer));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(Player) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolPlayerHookWrapper(probe.TimeBoolPlayer));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(Item) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new VoidItemHookWrapper(probe.TimeVoidItem));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(Item) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolItemHookWrapper(probe.TimeBoolItem));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(GameTime) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new GameTimeHookWrapper(probe.TimeGameTime));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(List<GameInterfaceLayer>) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new InterfaceLayersHookWrapper(probe.TimeInterfaceLayers));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(SpriteBatch) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new SpriteBatchHookWrapper(probe.TimeSpriteBatch));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(int) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new IntHookWrapper(probe.TimeInt));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(Color) && returnType == typeof(Color?))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new GetAlphaHookWrapper(probe.TimeGetAlpha));
                    return InstallOutcome.Installed;
                }

                // ref Color — e.g. PreDraw(ref Color lightColor)
                if (p0.IsByRef && p0.GetElementType() == typeof(Color) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolRefColorHookWrapper(probe.TimeBoolRefColor));
                    return InstallOutcome.Installed;
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
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(Item) && p1 == typeof(Player) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolItemPlayerHookWrapper(probe.TimeBoolItemPlayer));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(NPC) && p1 == typeof(Player) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new NpcPlayerHookWrapper(probe.TimeNpcPlayer));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(NPC) && p1 == typeof(Player) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolNpcPlayerHookWrapper(probe.TimeBoolNpcPlayer));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(Projectile) && p1 == typeof(Player) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new ProjectilePlayerHookWrapper(probe.TimeProjectilePlayer));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(Projectile) && p1 == typeof(Player) && returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new BoolProjectilePlayerHookWrapper(probe.TimeBoolProjectilePlayer));
                    return InstallOutcome.Installed;
                }

                if (p0 == typeof(Player) && p1 == typeof(bool) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new PlayerBoolHookWrapper(probe.TimePlayerBool));
                    return InstallOutcome.Installed;
                }

                // void(NPC, ref NPC.HitModifiers) — e.g. ModifyHitNPC
                if (p0 == typeof(NPC) && p1.IsByRef && p1.GetElementType() == typeof(NPC.HitModifiers) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new NpcRefHitModifiersHookWrapper(probe.TimeNpcRefHitModifiers));
                    return InstallOutcome.Installed;
                }
            }

            if (parameters.Length == 3)
            {
                Type p0 = parameters[0].ParameterType;
                Type p1 = parameters[1].ParameterType;
                Type p2 = parameters[2].ParameterType;

                // void(NPC, HitInfo, int) — e.g. OnHitNPC
                if (p0 == typeof(NPC) && p1 == typeof(NPC.HitInfo) && p2 == typeof(int) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new NpcHitInfoIntHookWrapper(probe.TimeNpcHitInfoInt));
                    return InstallOutcome.Installed;
                }
            }

            if (parameters.Length == 4)
            {
                Type p0 = parameters[0].ParameterType;
                Type p1 = parameters[1].ParameterType;
                Type p2 = parameters[2].ParameterType;
                Type p3 = parameters[3].ParameterType;

                // void(int, int, bool, ref int) — e.g. KillTile(i, j, fail, ref dustType)
                if (p0 == typeof(int) && p1 == typeof(int) && p2 == typeof(bool) &&
                    p3.IsByRef && p3.GetElementType() == typeof(int) && returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new TileIntIntBoolRefIntHookWrapper(probe.TimeTileIntIntBoolRefInt));
                    return InstallOutcome.Installed;
                }
            }

            if (parameters.Length == 6)
            {
                Type p0 = parameters[0].ParameterType;
                Type p1 = parameters[1].ParameterType;

                // void(SpriteBatch, Color, Color, float, float, int) — e.g. PreDrawInInventory / PreDrawInWorld
                if (p0 == typeof(SpriteBatch) && p1 == typeof(Color) &&
                    parameters[2].ParameterType == typeof(Color) &&
                    parameters[3].ParameterType == typeof(float) &&
                    parameters[4].ParameterType == typeof(float) &&
                    parameters[5].ParameterType == typeof(int) &&
                    returnType == typeof(void))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new DrawItemHookWrapper(probe.TimeDrawItem));
                    return InstallOutcome.Installed;
                }
            }

            if (parameters.Length == 7)
            {
                // bool(Player, EntitySource_ItemUse_WithAmmo, Vector2, Vector2, int, int, float) — Shoot
                if (parameters[0].ParameterType == typeof(Player) &&
                    parameters[1].ParameterType == typeof(EntitySource_ItemUse_WithAmmo) &&
                    parameters[2].ParameterType == typeof(Vector2) &&
                    parameters[3].ParameterType == typeof(Vector2) &&
                    parameters[4].ParameterType == typeof(int) &&
                    parameters[5].ParameterType == typeof(int) &&
                    parameters[6].ParameterType == typeof(float) &&
                    returnType == typeof(bool))
                {
                    HookProbe probe = CreateProbe(modId, categoryId, type, method, parameters);
                    MonoModHooks.Add(method, new ShootHookWrapper(probe.TimeShoot));
                    return InstallOutcome.Installed;
                }
            }
        }
        catch (Exception ex)
        {
            // An install failure is a different beast from an unsupported
            // signature: control reached past one of the supported-shape
            // branches before MonoMod threw, so this signature WAS in the
            // supported set. Caller counts it via the InstallFailed arm so
            // coverage shows the gap and the signature-shape histogram stays
            // clean of install errors.
            LogSampleHookFailure(type, method.Name, ex, self);
            return InstallOutcome.InstallFailed;
        }

        return InstallOutcome.UnsupportedSignature;
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

    public bool? TimeNullableBool(OrigNullableBoolHook orig, object self)
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

    public bool TimeBoolRefColor(OrigBoolRefColorHook orig, object self, ref Color lightColor)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return orig(self, ref lightColor);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    public Color? TimeGetAlpha(OrigGetAlphaHook orig, object self, Color lightColor)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return orig(self, lightColor);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    public void TimeInt(OrigIntHook orig, object self, int value)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, value);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    public void TimePlayerBool(OrigPlayerBoolHook orig, object self, Player player, bool flag)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, player, flag);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    public void TimeNpcRefHitModifiers(OrigNpcRefHitModifiersHook orig, object self, NPC npc, ref NPC.HitModifiers modifiers)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, npc, ref modifiers);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    public void TimeNpcHitInfoInt(OrigNpcHitInfoIntHook orig, object self, NPC target, NPC.HitInfo hit, int damageDone)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, target, hit, damageDone);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    public void TimeTileIntIntBoolRefInt(OrigTileIntIntBoolRefIntHook orig, object self, int i, int j, bool fail, ref int type)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, i, j, fail, ref type);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    public void TimeDrawItem(OrigDrawItemHook orig, object self, SpriteBatch spriteBatch, Color color1, Color color2, float f1, float f2, int slot)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            orig(self, spriteBatch, color1, color2, f1, f2, slot);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }

    public bool TimeShoot(OrigShootHook orig, object self, Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position, Vector2 velocity, int type, int damage, float knockBack)
    {
        long start = Stopwatch.GetTimestamp();
        try
        {
            return orig(self, player, source, position, velocity, type, damage, knockBack);
        }
        finally
        {
            PerModAttribution.Add(_modId, _categoryId, _hookId, Stopwatch.GetTimestamp() - start);
        }
    }
}
