#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Terraria.ModLoader;
using Terraria.ModLoader.Core;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// Signature-agnostic timing instrumentation via MonoMod ILHook.
///
/// <para>
/// Where <see cref="HookInterceptor"/> matches each hook override's signature
/// against a hand-written set of delegate pairs and installs a MonoMod
/// <c>Add</c> detour per match, this class injects timing IL directly into the
/// override's body. Every override gets wrapped, regardless of signature, by
/// the same manipulator. Coverage rises from the delegate path's ~71.6% to the
/// expected ~100%.
/// </para>
///
/// <para><b>What the manipulator does:</b> for each override
/// <c>TReturn Override(args...) { /* body */ }</c> it produces
/// <c>{ ProbeStack.Enter(hookId); try { /* body, returning via local */ } finally { ProbeStack.Leave(); } }</c>.
/// Each <c>ret</c> in the original body is rewritten to <c>stloc retLocal; leave end</c>
/// (or just <c>leave end</c> for void). A single <c>ExceptionHandler(Finally)</c> is
/// appended to wrap the whole body. Existing inner handlers stay untouched -- the new
/// outer handler legally encloses them.
/// </para>
///
/// <para><b>Lifecycle:</b> we construct <see cref="ILHook"/> directly rather than going
/// through <see cref="MonoModHooks.Modify"/> because the tModLoader API returns
/// <c>void</c> from <c>Modify</c> -- we'd never get the <see cref="ILHook"/> back to
/// <see cref="ILHook.Dispose"/> on unload. Direct construction is the supported public
/// API in MonoMod.RuntimeDetour 25.3.2 and gives deterministic teardown.
/// </para>
///
/// <para><b>Invariant compliance:</b> the manipulator only reads
/// <c>Stopwatch.GetTimestamp()</c> and writes profiler memory; game state is never
/// touched (Invariant 1). Per-call overhead is two timestamp reads + one static call,
/// matching the delegate-pair path (Invariant 2). Install failures are caught
/// per-hook and routed to a coverage-debt counter (Invariant 4).
/// </para>
/// </summary>
public static class ILHookInterceptor
{
    // Hook categories, matching PerModAttribution.CategoryNames indices.
    // (Duplicated from HookInterceptor to keep this file standalone; both must agree.)
    private const int CategorySystems = 0;
    private const int CategoryPlayers = 1;
    private const int CategoryNpcs = 2;
    private const int CategoryProjectiles = 3;
    private const int CategoryItems = 4;
    private const int CategoryWorld = 5;
    private const int CategoryBuffs = 6;

    // Cached MethodInfo for our IL-emitted call targets.
    private static readonly MethodInfo _enterMethod =
        typeof(ProbeStack).GetMethod(nameof(ProbeStack.Enter),
            BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo _leaveMethod =
        typeof(ProbeStack).GetMethod(nameof(ProbeStack.Leave),
            BindingFlags.Public | BindingFlags.Static)!;

    // Installed ILHook references: held for the process lifetime so we can
    // deterministically Dispose() each on Mod.Unload. MonoMod.RuntimeDetour
    // does not auto-track these the way MonoModHooks.Add tracks On-hooks.
    private static readonly List<ILHook> _installedHooks = new List<ILHook>();

    private static int _failures;
    private static bool _sampleFailureLogged;
    private static int _measuredOverrides;
    private static int _skippedOverrides;
    private static int _closedGenericHooks;

    // Dedup set for closed-generic methods discovered via inheritance: many
    // concrete subclasses of the same closed generic (e.g. ten mods inheriting
    // BaseFoo<int>) would each surface the same MethodInfo for the inherited
    // method. We must hook each unique MethodInfo at most once, otherwise
    // MonoMod stacks detours on the same compiled body and double-counts the
    // cost. RuntimeMethodHandle comparison is the cheapest reliable identity.
    private static readonly HashSet<RuntimeMethodHandle> _instrumentedHandles = new HashSet<RuntimeMethodHandle>();

    // Per-mod coverage counters, mirroring HookInterceptor's so the overlay can
    // render PROFILER HEALTH for whichever backend is active. Total = every
    // discovered hook override (whether or not we ended up instrumenting it);
    // measured = ones we successfully wrapped.
    private static int[] _measuredHookCounts = Array.Empty<int>();
    private static int[] _totalHookCounts = Array.Empty<int>();

    /// <summary>True once <see cref="Install"/> has run successfully.</summary>
    public static bool Installed { get; private set; }

    /// <summary>Count of hooks the ILHook backend successfully wrapped.</summary>
    public static int MeasuredOverrides => _measuredOverrides;

    /// <summary>Count of overrides skipped due to install failure (abstract / no body / unsupported IL).</summary>
    public static int SkippedOverrides => _skippedOverrides;

    /// <summary>Manipulator-application exceptions caught during install.</summary>
    public static int Failures => _failures;

    /// <summary>
    /// Count of hooks recovered via the closed-generic inheritance pass. These
    /// are methods declared on open generic bases that MonoMod refuses to hook
    /// directly, but whose closed instantiations (e.g. BaseFoo&lt;T&gt; with T
    /// resolved to a concrete type by a subclass) do have compiled bodies and
    /// are hookable.
    /// </summary>
    public static int ClosedGenericHooks => _closedGenericHooks;

    /// <summary>Per-mod count of hooks the ILHook backend successfully wrapped.</summary>
    public static IReadOnlyList<int> MeasuredHookCounts => _measuredHookCounts;

    /// <summary>Per-mod count of discovered hook overrides (measured + skipped).</summary>
    public static IReadOnlyList<int> TotalHookCounts => _totalHookCounts;

    /// <summary>
    /// Walks the same mod surface as <see cref="HookInterceptor.Install"/> but
    /// installs an ILHook on each discovered override instead of a delegate
    /// detour. Mod identity, category bucketing, and hook naming reuse the same
    /// logic, so the resulting <see cref="PerModAttribution.Hooks"/> entries
    /// are identical -- the two backends share hookIds (via
    /// <see cref="PerModAttribution.RegisterOrReuseHook"/>) when both are active.
    /// </summary>
    public static void Install(Mod self, IReadOnlyList<Mod> profiledMods)
    {
        if (Installed)
        {
            return;
        }

        _failures = 0;
        _measuredOverrides = 0;
        _skippedOverrides = 0;
        _closedGenericHooks = 0;
        _sampleFailureLogged = false;
        _instrumentedHandles.Clear();
        _measuredHookCounts = new int[profiledMods.Count];
        _totalHookCounts = new int[profiledMods.Count];

        try
        {
            for (int modId = 0; modId < profiledMods.Count; modId++)
            {
                InstallForMod(modId, profiledMods[modId], self);
            }

            Installed = true;
            self.Logger.Info(
                $"ILHookInterceptor: {_installedHooks.Count} IL timing detours installed across {profiledMods.Count} mods; " +
                $"{_closedGenericHooks} via closed-generic inheritance pass; " +
                $"{_skippedOverrides} overrides skipped (no-body / dynamic / unsupported); {_failures} manipulator failures.");
        }
        catch (Exception ex)
        {
            Installed = false;
            self.Logger.Warn(
                $"ILHookInterceptor disabled, install failed cleanly: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Disposes every installed ILHook. Called from <c>Mod.Unload</c> so the
    /// IL patches are removed before tModLoader unloads our assembly -- without
    /// this, methods owned by other mods would still carry calls to types in
    /// our assembly that's about to disappear.
    /// </summary>
    public static void Uninstall()
    {
        foreach (ILHook h in _installedHooks)
        {
            try
            {
                h.Dispose();
            }
            catch
            {
                // Swallow per-hook disposal exceptions: we cannot let one bad
                // hook strand the rest of the teardown. Net effect of a swallow
                // is a stale IL patch on a method whose mod is unloading anyway.
            }
        }

        _installedHooks.Clear();
        _instrumentedHandles.Clear();
        Installed = false;
        _measuredOverrides = 0;
        _skippedOverrides = 0;
        _failures = 0;
        _closedGenericHooks = 0;
        _measuredHookCounts = Array.Empty<int>();
        _totalHookCounts = Array.Empty<int>();
    }

    private static void InstallForMod(int modId, Mod mod, Mod self)
    {
        Type[] types;
        try
        {
            types = AssemblyManager.GetLoadableTypes(mod.Code);
        }
        catch (Exception ex)
        {
            self.Logger.Warn($"ILHookInterceptor: skipped {mod.Name}, could not read its types: {ex.Message}");
            return;
        }

        // Skip dynamic assemblies (Reflection.Emit-built code) -- their MethodBody
        // is not reliably introspectable by Cecil.
        if (mod.Code.IsDynamic)
        {
            return;
        }

        foreach (Type type in types)
        {
            if (type.IsAbstract)
            {
                continue;
            }

            // Open generic type definitions are templates with no compiled body
            // -- MonoMod refuses to hook them, producing one "manipulator failed"
            // warning per such type per launch. The closed instantiations are
            // discovered separately via the inheritance pass on concrete subclasses
            // (BaseMusicBoxItem<MusicA>, <MusicB>, ...). Skip the open form here
            // so the warnings disappear and the closed form is the only entry point.
            if (type.IsGenericTypeDefinition)
            {
                continue;
            }

            int categoryId = ResolveCategory(type);
            if (categoryId < 0)
            {
                continue;
            }

            InstrumentTypeOverrides(type, modId, categoryId, self);
        }
    }

    // tModLoader's own assembly. Resolved once so the hot install loop avoids
    // repeated typeof(Mod) lookups.
    private static readonly Assembly _tmlAssembly = typeof(Terraria.ModLoader.Mod).Assembly;

    /// <summary>
    /// Mirrors <see cref="HookInterceptor.InstallForMod"/>'s type-to-category
    /// routing so both backends produce the same category attribution.
    /// </summary>
    private static int ResolveCategory(Type type)
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

    private static void InstrumentTypeOverrides(Type type, int modId, int categoryId, Mod self)
    {
        // Walk WITHOUT DeclaredOnly so the iteration also surfaces methods this
        // type inherits from a closed-generic base. Open generics like
        // BaseMusicBoxItem<T> have no compiled body and MonoMod refuses to hook
        // them; the closed instantiations (BaseMusicBoxItem<MusicA>) do, and
        // their MethodInfo only becomes visible via the concrete subclass's
        // inherited member list.
        //
        // Without DeclaredOnly we also see methods inherited from non-generic
        // bases (e.g. ModItem's own virtuals that this type doesn't override).
        // Those would already be hooked when we processed the base type, so we
        // filter them out by checking DeclaringType below.
        MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic);

        foreach (MethodInfo method in methods)
        {
            if (method.IsSpecialName || method.IsAbstract || method.IsStatic)
            {
                continue;
            }

            if (!IsHookOverride(method))
            {
                continue;
            }

            Type? declaringType = method.DeclaringType;
            if (declaringType == null)
            {
                continue;
            }

            bool isDirectOverride = declaringType == type;
            bool isClosedGenericInherit = !isDirectOverride
                && declaringType.IsGenericType
                && !declaringType.IsGenericTypeDefinition;

            if (!isDirectOverride && !isClosedGenericInherit)
            {
                // Inherited from a non-generic base. That base type was either
                // already enumerated (in which case it's already hooked) or is
                // outside our scope (a tModLoader base — never hookable). Skip
                // either way to avoid double-instrumentation.
                continue;
            }

            // Critical filter for the closed-generic case: we only ever hook
            // methods whose declaring type lives in a MOD assembly. tModLoader's
            // ModType<TEntity, TInstance> is the parent of every ModItem,
            // ModProjectile, ModPlayer, ModNPC, etc. -- so every concrete mod
            // content type "inherits" methods declared on ModType<...>. The
            // .NET JIT shares compiled bodies between reference-type generic
            // instantiations: hooking ModType<Projectile, ModProjectile>::NewInstance
            // also patches the body that ModType<Player, ModPlayer>::NewInstance
            // dispatches through, so tModLoader's player path crashes with an
            // InvalidCastException when it expects a ModPlayer and meets the
            // projectile-typed hook frame. Don't go there.
            if (isClosedGenericInherit && declaringType.Assembly == _tmlAssembly)
            {
                continue;
            }

            // Dedupe closed-generic instantiations: if BaseFoo<int> is the base
            // of both Bar1 and Bar2, both surfaces the same MethodInfo when we
            // walk Bar1 and Bar2. Hooking that MethodInfo twice would stack two
            // detours on the same compiled body and double-count it.
            if (isClosedGenericInherit && !_instrumentedHandles.Add(method.MethodHandle))
            {
                continue;
            }

            // Every method that reaches this point counts as a "discovered" hook
            // override for coverage purposes, whether or not we end up wrapping it.
            // That keeps the denominator consistent with HookInterceptor.
            if ((uint)modId < (uint)_totalHookCounts.Length)
            {
                _totalHookCounts[modId]++;
            }

            // Skip methods with no body (extern, [MethodImpl(InternalCall)],
            // anything Cecil can't read). Abstract is already filtered above;
            // this catches the rare runtime-implemented cases. Fully qualify
            // System.Reflection.MethodBody because Mono.Cecil.Cil.MethodBody is
            // also in scope from the manipulator code below.
            try
            {
                System.Reflection.MethodBody? clrBody = method.GetMethodBody();
                if (clrBody == null)
                {
                    _skippedOverrides++;
                    continue;
                }
            }
            catch
            {
                _skippedOverrides++;
                continue;
            }

            try
            {
                // For closed-generic inherited methods, the type shown in the
                // tree is the concrete subclass we walked (more useful to the
                // player than the synthetic closed-generic name); the method
                // is still the inherited one, which is what we actually hook.
                string displayName = DisplayName(type, method);
                int hookId = PerModAttribution.RegisterOrReuseHook(modId, categoryId, displayName);
                ILHook hook = InstallTimingHook(method, hookId);
                _installedHooks.Add(hook);
                _measuredOverrides++;
                if (isClosedGenericInherit)
                {
                    _closedGenericHooks++;
                }
                if ((uint)modId < (uint)_measuredHookCounts.Length)
                {
                    _measuredHookCounts[modId]++;
                }
            }
            catch (Exception ex)
            {
                _failures++;
                LogSampleFailure(type, method.Name, ex, self);
            }
        }
    }

    private static bool IsHookOverride(MethodInfo method)
    {
        MethodInfo baseDefinition = method.GetBaseDefinition();
        return baseDefinition != method && baseDefinition.DeclaringType != typeof(object);
    }

    private static string DisplayName(Type type, MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        return parameters.Length switch
        {
            0 => $"{type.Name}.{method.Name}()",
            1 => $"{type.Name}.{method.Name}({parameters[0].ParameterType.Name})",
            _ => $"{type.Name}.{method.Name}({parameters[0].ParameterType.Name}, {parameters[1].ParameterType.Name})",
        };
    }

    private static void LogSampleFailure(Type type, string name, Exception ex, Mod self)
    {
        if (!_sampleFailureLogged)
        {
            _sampleFailureLogged = true;
            self.Logger.Warn(
                $"ILHookInterceptor: a manipulator failed on {type.FullName}.{name} " +
                $"({ex.GetType().Name}: {ex.Message}); skipping it and continuing.");
        }
    }

    // ---- The IL manipulator -------------------------------------------------

    /// <summary>
    /// Constructs an <see cref="ILHook"/> on <paramref name="target"/> whose
    /// manipulator wraps the original body in
    /// <c>ProbeStack.Enter(<paramref name="hookId"/>); try { /* body */ } finally { ProbeStack.Leave(); }</c>.
    /// Bypasses <see cref="MonoModHooks.Modify"/> so the returned reference can
    /// be <see cref="ILHook.Dispose"/>'d on unload.
    /// </summary>
    private static ILHook InstallTimingHook(MethodInfo target, int hookId)
    {
        ILContext.Manipulator manipulator = il => ApplyTimingWrap(il, hookId);
        // applyByDefault: true matches MonoModHooks.Modify semantics -- the
        // hook is live immediately. Without this, IsApplied stays false and
        // the IL is never inserted.
        return new ILHook(target, manipulator, applyByDefault: true);
    }

    /// <summary>
    /// The actual IL transform. Runs once per ILHook application, on a fresh
    /// copy of the method body (so it's safe to mutate freely; MonoMod re-applies
    /// the chain from original each time another hook is added).
    /// </summary>
    private static void ApplyTimingWrap(ILContext il, int hookId)
    {
        Mono.Cecil.Cil.MethodBody body = il.Body;
        if (body.Instructions.Count == 0)
        {
            // No body to wrap (degenerate case). Just no-op.
            return;
        }

        bool returnsValue = body.Method.ReturnType.MetadataType != MetadataType.Void;

        // Anchor the original first instruction. Branches inside the body that
        // target it stay valid because Cecil keeps instruction references by
        // identity, not by offset.
        Instruction firstOriginal = body.Instructions[0];

        // For non-void returns, allocate a local to route every original `ret`'s
        // value through. We `stloc` it before `leave`-ing the try, then `ldloc`
        // it after the finally and `ret`.
        VariableDefinition? retLocal = null;
        if (returnsValue)
        {
            retLocal = new VariableDefinition(body.Method.ReturnType);
            body.Variables.Add(retLocal);
            body.InitLocals = true;
        }

        // Tail anchors (the new instructions appended after the body):
        //   handlerStart = call ProbeStack.Leave()    <-- finally region opens here
        //   endFinally   = endfinally                 <-- finally region closes here
        //   afterHandler = ldloc retLocal             <-- (non-void) loads return value
        //                | ret                         <-- (void) the final return
        //   finalRet     = ret                         <-- (non-void only) final return
        Instruction handlerStart = Instruction.Create(OpCodes.Call, il.Import(_leaveMethod));
        Instruction endFinally = Instruction.Create(OpCodes.Endfinally);
        Instruction afterHandler = returnsValue
            ? Instruction.Create(OpCodes.Ldloc, retLocal!)
            : Instruction.Create(OpCodes.Ret);
        Instruction? finalRet = returnsValue ? Instruction.Create(OpCodes.Ret) : null;

        // Rewrite every existing `ret` so control leaves the try region through
        // `leave` (which fires the finally) instead of `ret` (which would be
        // illegal inside a protected region).
        for (int i = 0; i < body.Instructions.Count; i++)
        {
            Instruction ins = body.Instructions[i];
            if (ins.OpCode != OpCodes.Ret)
            {
                continue;
            }

            if (returnsValue)
            {
                // `ret` consumes the top stack value; route it into retLocal,
                // then leave to afterHandler.
                ins.OpCode = OpCodes.Stloc;
                ins.Operand = retLocal;
                Instruction leaveIns = Instruction.Create(OpCodes.Leave, afterHandler);
                body.Instructions.Insert(i + 1, leaveIns);
                i++; // skip the freshly inserted leave
            }
            else
            {
                ins.OpCode = OpCodes.Leave;
                ins.Operand = afterHandler;
            }
        }

        // Prologue: emit `ldc.i4 hookId ; call ProbeStack.Enter` BEFORE firstOriginal.
        // Using ILCursor here so module import is handled correctly.
        ILCursor c = new ILCursor(il);
        c.Goto(firstOriginal, MoveType.Before);
        c.Emit(OpCodes.Ldc_I4, hookId);
        c.Emit(OpCodes.Call, _enterMethod);

        // Append the finally handler at the very end of the body.
        body.Instructions.Add(handlerStart);
        body.Instructions.Add(endFinally);
        body.Instructions.Add(afterHandler);
        if (returnsValue)
        {
            body.Instructions.Add(finalRet!);
        }

        // Register the new outer exception handler. Cecil resolves offsets at
        // serialise time, so we only need correct instruction references here.
        // Existing inner handlers (if any) stay untouched and remain legally
        // nested inside the new outer try region.
        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
        {
            TryStart = firstOriginal,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = afterHandler,
        });
    }
}
