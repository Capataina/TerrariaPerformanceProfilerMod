# ILHook Migration Plan — From hand-written delegate pairs to signature-agnostic coverage

> **Status (2026-05-20): SHIPPED — preserved as historical research record.** The IL backend (`ILHookInterceptor`) is the default since `b52f8b6`. Closed-generic inheritance pass landed in `7da4058`; the JIT shared-body crash fix in `5725572`. The 2026-05-20 audit's hook-instrumentation findings are all marked done in `context/plans/code-health-audit/index.md`. Canonical reality: `context/systems/hook-instrumentation.md`. This file is kept as the research record that shaped the implementation.
>

> Scope: replace `HookInterceptor.TryHookSupportedOverride` and every `HookProbe.Time*` overload with a single ILHook-based instrumentation path that times any tModLoader hook override regardless of signature, lifting coverage from ~71.6 % (7 314 / 10 220 overrides on an 18-mod modlist) to ~100 %. Honours all four Project Invariants. Targets tModLoader 1.4.4 on .NET 8, MonoMod.RuntimeDetour 25.3.2, Mono.Cecil 0.11.6.

---

## 0. Research evidence ledger

The plan is grounded in API surfaces verified by reflection against the live tModLoader install. Evidence:

| Claim | Evidence |
|---|---|
| `MonoMod.RuntimeDetour.ILHook` exists, `IDisposable`, takes `(MethodBase, ILContext.Manipulator)` and supports `DetourConfig`, `applyByDefault`, `Apply()`, `Undo()`, `Dispose()` | Reflection over `/Library/.../monomod.runtimedetour/25.3.2/lib/net8.0/MonoMod.RuntimeDetour.dll` — constructors `(MethodBase source, Manipulator manip)`, `(MethodBase source, Manipulator manip, Boolean applyByDefault)`, `(MethodBase source, Manipulator manip, DetourConfig config)` all present; type declared `: IDisposable`; properties `IsApplied`, `IsValid`, `HookInfo`, `Manipulator`, `Method`. |
| `ILContext.Manipulator` signature is `void (ILContext il)` | Reflection: `MonoMod.Cil.ILContext+Manipulator.Invoke(ILContext) -> Void`. |
| `ILContext.Body` exposes Cecil's `MethodBody`; `Body.Instructions`, `Body.Variables`, `Body.ExceptionHandlers`, `Body.GetILProcessor()` reachable | Reflection: `ILContext` properties `Body : MethodBody`, `IL : ILProcessor`, `Instrs : Collection`1`, plus Cecil `MethodBody` exposes `ExceptionHandlers`, `Variables`, `Instructions`, `GetILProcessor()`. |
| `ExceptionHandlerType.Finally` is a real enum value | Reflection over `Mono.Cecil.dll` shows enum values `Catch, Filter, Finally, Fault`. |
| `ILCursor.EmitDelegate<T>(T)` exists, returns `Int32` (a reference token), takes a single `T` (the delegate) | Reflection: `MonoMod.Cil.ILCursor.EmitDelegate<T>(T)` returns `Int32`. |
| `MonoModHooks.Modify(MethodBase, Manipulator)` returns `void` — tModLoader does **not** hand the `ILHook` instance back | Reflection over `tModLoader.dll`: `System.Void Modify(MethodBase method, Manipulator callback)`. Same for `Add`. |
| `MonoModHooks` exposes only `Add / Modify / RequestNativeAccess / DumpILHooks / DumpOnHooks / DumpIL` — no removal API | Same reflection probe. |
| `AssemblyManager.GetLoadableTypes(Assembly)` is the safe enumeration path for mod assemblies | `context/tmodloader-mod-identity.md` + existing call site in `HookInterceptor.InstallForMod`. |
| Cecil 0.11.6 ships `ILProcessor`, `Instruction.Create(OpCode, …)`, `VariableDefinition(TypeReference)` | Reflection over `Mono.Cecil.dll`. |

The "no removal API in `MonoModHooks`" finding is the single most consequential decision-driver in this document; everything in §4 (lifecycle) hangs off it.

---

## 1. Viability verdict

**Doable. Recommended. Two real risks, both manageable.**

The migration replaces a finite, growing-by-hand table of delegate pairs with one IL transform that wraps the original method body in `try { … } finally { Stop(hookId); }`. The transform is signature-agnostic because it never needs to know parameter or return types — it only needs to:

1. Insert a prologue *before* the first existing instruction.
2. Insert a finally handler *around* the entire existing body.

Both operations are first-class Cecil/MonoMod features and are routinely used by content mods on tModLoader 1.4.4. The IL surface required is small (`ldc.i4`, `call`, `endfinally`, `ExceptionHandler` of type `Finally`) and stable across .NET 8 JIT and Mono.Cecil 0.11.6.

### Risks the plan must address (each is engaged in a later section)

| Risk | Trigger | Mitigation |
|---|---|---|
| **R1. Stack-state aware finally injection.** If a method's existing IL ends mid-stack (rare; only if it returns a value via leaving a value on the stack before `ret`), wrapping in `try`/`finally` requires moving the return value into a local before the leave. | Any non-`void` method (≈40 % of hooks). | Always allocate a return-value local for non-void methods, replace each `ret` with `stloc <ret>; leave <end>`, emit `ldloc <ret>; ret` after the finally. Standard pattern. |
| **R2. Pre-existing exception handlers in a hook body.** Some hook overrides have their own try/catch/finally. Wrapping these in a *new* outer finally is legal IL but the new handler must extend from the very first instruction to immediately past the body — never *inside* an existing handler. | Hook overrides with internal try/catch. | The outer try region is `[firstInstr … lastBodyInstr]` and the new finally is appended to the end of `Body.ExceptionHandlers`. Existing handlers stay untouched; nesting is legal because the inner regions are strictly contained inside the outer. |
| **R3. Allocation-/timing-state safety under re-entrancy.** A mod's `NPC.AI` override may itself trigger another hooked method on the same thread, and exceptions may unwind through several layers. The naive "store start timestamp in a static field" approach is wrong. | Cross-hook nesting (common — e.g. `PostUpdateEverything` walking npcs that fire `GlobalNPC.AI`). | Push `(hookId, startTicks)` onto a thread-local `ProbeStack` at entry, pop at exit. See §2. |
| **R4. No removal API in `MonoModHooks`**. `Modify` returns `void`. | Mod reload, world reload while mod stays loaded. | Bypass `MonoModHooks.Modify` for the timing detours; construct `new MonoMod.RuntimeDetour.ILHook(method, manip, applyByDefault: true)` directly, store the references, `Dispose()` on `Mod.Unload`. See §4. |
| **R5. Chaining with another mod's `Modify` on the same target.** MonoMod composes IL hooks in installation order; our prologue/finally must not assume it is the only edit. | Another mod IL-edits the same override (rare for content mods, possible for utility mods). | Our injection only *adds* instructions (read-only invariant). Each successive ILHook receives the previous IL state and edits over it; appending a finally that wraps the *current* whole body is robust regardless of prior edits. See §5. |
| **R6. The non-`void` `Modify` means abort-clean on per-hook failure is reactive, not declarative.** A bad target throws inside `Modify`. | Loader internals shift, a method has unusual IL (e.g. tail calls). | Wrap each install in `try`/`catch`, increment a coverage-debt counter, `Logger.Warn`, continue. Mirrors the existing `LogSampleHookFailure` pattern. |

None of these are blockers. The plan is straightforward C# + Cecil.

### Honest uncertainties

- **`DynamicMethod` overrides.** A handful of mods (typically those generating runtime code via Reflection.Emit) compile their hook overrides as dynamic methods. MonoMod's ILHook can target these but with reduced guarantees. Treat any `MethodInfo` where `method.DeclaringType?.Assembly.IsDynamic` is true as unsupported and skip; preserves the abort-clean posture.
- **Methods with no body.** Abstract / `extern` / `[MethodImpl(InternalCall)]` — `MethodBody` is null. Already filtered today (`method.IsAbstract` check); keep the filter and add a `method.GetMethodBody() != null` guard before installing.
- **Iterator state machines.** A `yield return` hook (none in current tML hook surface) would have a moved body inside a compiler-generated state machine. Our enumeration over types is `BindingFlags.DeclaredOnly`; we never see the compiler-generated nested types. Safe.

---

## 2. The re-entrancy / timing problem

### Why a single `static long _start` field is wrong

Consider:

```
GlobalNPC.AI (mod A)
  └─ calls some helper that NPC.NewNPC()
       └─ triggers GlobalNPC.OnSpawn (mod A) — also instrumented
```

Two hook bodies are simultaneously live on the same thread. A `static long _start` would be overwritten by the inner hook entry; the outer hook's elapsed time would be measured as the duration of just the post-inner-call tail.

### Chosen solution: thread-local probe stack

A `[ThreadStatic]` stack-of-structs holding `(hookId, startTicks)`. Entry pushes; exit pops. Allocation-free after warmup because the stack is a pre-grown array.

```csharp
internal struct ProbeFrame
{
    public int HookId;
    public long StartTicks;
}

internal static class ProbeStack
{
    // Per-thread; tModLoader hooks fire on the game's update thread, the
    // draw thread (for *Draw* hooks), and occasionally background threads
    // (for mod-spawned tasks). All paths must be safe.
    [ThreadStatic] private static ProbeFrame[]? _stack;
    [ThreadStatic] private static int _depth;

    // Called from IL prologue; one int32 arg (hookId).
    public static void Enter(int hookId)
    {
        ProbeFrame[]? s = _stack;
        if (s == null)
        {
            s = new ProbeFrame[32];
            _stack = s;
        }
        else if (_depth == s.Length)
        {
            Array.Resize(ref s, s.Length * 2);
            _stack = s;
        }

        s[_depth].HookId = hookId;
        s[_depth].StartTicks = Stopwatch.GetTimestamp();
        _depth++;
    }

    // Called from IL finally; pops the most recent frame and commits the
    // elapsed time. Tolerant of zero-depth (defensive — should never fire).
    public static void Leave()
    {
        int d = _depth - 1;
        if ((uint)d >= (uint)(_stack?.Length ?? 0))
        {
            return;
        }

        _depth = d;
        ProbeFrame f = _stack![d];
        long elapsed = Stopwatch.GetTimestamp() - f.StartTicks;

        // Look up the descriptor and credit elapsed time. PerModAttribution.Add
        // is already bounds-checked and allocation-free.
        HookDescriptor desc = PerModAttribution.Hooks[f.HookId];
        PerModAttribution.Add(desc.ModId, desc.CategoryId, f.HookId, elapsed);
    }
}
```

### Justification

| Property | How it holds |
|---|---|
| **Correct under nesting.** | LIFO discipline — `Leave` always pops the frame `Enter` just pushed because the finally guarantees execution. |
| **Correct under thrown exceptions.** | The finally we inject runs even on unwind, so `Leave` is called once per `Enter`. |
| **Zero allocation per call** after warmup. | `_stack` is allocated once per thread; `ProbeFrame` is a struct slot in the existing array; resize only on depth growth. Resizes are rare (max nesting we observe is ~6 in practice). |
| **Thread-safe** with no locking. | `[ThreadStatic]` gives one stack per thread; no cross-thread state. |
| **Survives Invariant 1 (read-only).** | We only read `Stopwatch.GetTimestamp()` and write to per-thread profiler memory. Game state untouched. |
| **Survives Invariant 2 (overhead budget).** | Two static call sites per hook (push + pop), one `Stopwatch.GetTimestamp` per probe. Same cost shape as the current `HookProbe.Time*` methods — no regression. |

### Why not `Stopwatch` itself / locals / closures

| Alternative | Why rejected |
|---|---|
| Allocate a local `Stopwatch` per call | Per-tick allocation; violates Invariant 2. |
| `var start = Stopwatch.GetTimestamp()` in a local, pass via IL stack | Cannot survive an exception unwind cleanly — the local is gone when the finally runs; you would have to widen its scope, which is exactly what a stack frame does. The stack we maintain is the explicit version of that. |
| Closure over a `HookProbe` instance per detour | Re-introduces per-detour delegate allocations and forces the per-signature wrapper mess we are removing. |

---

## 3. The finally-block injection problem

### What we are emitting, in source terms

Conceptually we want to transform every hook override from

```csharp
TReturn Override(args…) { /* original body */ }
```

into

```csharp
TReturn Override(args…) {
    ProbeStack.Enter(<hookId>);
    try { /* original body, returns moved through a local */ }
    finally { ProbeStack.Leave(); }
}
```

For `void` returns the local is omitted.

### The IL shape, end-to-end

Source body (void return, simplified):
```
IL_0000:  ldarg.0
IL_0001:  call    SomeHelper
IL_0006:  ret
```

After injection (void return):
```
IL_0000:  ldc.i4    <hookId>
IL_0005:  call      void PerformanceProfiler.Profiling.ProbeStack::Enter(int32)
IL_000a:  nop                              // try start marker
IL_000b:  ldarg.0
IL_000c:  call      SomeHelper
IL_0011:  leave.s   IL_001b                 // replaces original 'ret'
IL_0013:  call      void PerformanceProfiler.Profiling.ProbeStack::Leave()
IL_0018:  endfinally
IL_001b:  ret

ExceptionHandlers:
  Finally  TryStart=IL_000a  TryEnd=IL_0013  HandlerStart=IL_0013  HandlerEnd=IL_001b
```

For a non-`void` body (`int` return shown; same pattern for ref-struct returns, reference returns, etc.):

```
IL_0000:  ldc.i4    <hookId>
IL_0005:  call      void ProbeStack::Enter(int32)
IL_000a:  nop                              // try start marker
... original body, but each 'ret' becomes 'stloc <ret>; leave <end>' ...
IL_xxxx:  call      void ProbeStack::Leave()
IL_yyyy:  endfinally
IL_zzzz:  ldloc     <ret>
IL_zzzz+: ret

ExceptionHandlers:
  Finally  TryStart=IL_000a  TryEnd=IL_xxxx  HandlerStart=IL_xxxx  HandlerEnd=IL_zzzz
```

### The manipulator, step by step (real C# against verified API surface)

```csharp
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;

private static ILHook InstallTimingHook(MethodInfo target, int hookId)
{
    void Manipulator(ILContext il)
    {
        MethodBody body = il.Body;
        ModuleDefinition module = il.Module;
        ILProcessor pr = body.GetILProcessor();

        // 0. Resolve our static methods through the target module so the
        //    MethodReference is bound to the right context. EmitCall takes a
        //    MethodBase or MethodReference; using ILCursor.EmitCall(MethodBase)
        //    handles the import internally.
        MethodInfo enterMi = typeof(ProbeStack).GetMethod(nameof(ProbeStack.Enter),
            BindingFlags.Public | BindingFlags.Static)!;
        MethodInfo leaveMi = typeof(ProbeStack).GetMethod(nameof(ProbeStack.Leave),
            BindingFlags.Public | BindingFlags.Static)!;

        // 1. Snapshot the body's anchor instructions BEFORE we touch anything.
        //    Cecil's Body.Instructions is a live collection; if we mutate it
        //    while iterating we corrupt offsets. Pin the first instruction so
        //    we know where the try block must start AFTER the prologue.
        Instruction firstOriginal = body.Instructions[0];

        // 2. Build the new tail anchors we will branch to.
        bool returnsValue = body.Method.ReturnType.MetadataType != MetadataType.Void;
        VariableDefinition? retLocal = null;
        if (returnsValue)
        {
            retLocal = new VariableDefinition(body.Method.ReturnType);
            body.Variables.Add(retLocal);
            body.InitLocals = true;
        }

        Instruction handlerStart = Instruction.Create(OpCodes.Call, module.ImportReference(leaveMi));
        Instruction endFinally = Instruction.Create(OpCodes.Endfinally);
        Instruction afterHandler = returnsValue
            ? Instruction.Create(OpCodes.Ldloc, retLocal!)
            : Instruction.Create(OpCodes.Ret);
        Instruction finalRet = returnsValue ? Instruction.Create(OpCodes.Ret) : afterHandler;

        // 3. Rewrite every existing `ret` in the body so control leaves the
        //    try region through `leave` (so the finally fires) and the value
        //    is routed through retLocal.
        //
        //    We iterate by index because we are mutating the collection.
        for (int i = 0; i < body.Instructions.Count; i++)
        {
            Instruction ins = body.Instructions[i];
            if (ins.OpCode != OpCodes.Ret) continue;

            if (returnsValue)
            {
                // Replace `ret` with `stloc retLocal` and follow with `leave afterHandler`.
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

        // 4. Prologue: ldc.i4 hookId ; call ProbeStack.Enter(int32). Inserted
        //    BEFORE firstOriginal so existing branches that target firstOriginal
        //    still hit it (they will not be retargeted).
        ILCursor c = new ILCursor(il);
        c.Goto(firstOriginal, MoveType.Before);
        c.Emit(OpCodes.Ldc_I4, hookId);
        c.Emit(OpCodes.Call, enterMi); // ILCursor handles module import

        // 5. Append the finally handler. The handler body is two instructions:
        //    `call ProbeStack.Leave()` and `endfinally`. After the handler we
        //    place `ldloc retLocal; ret` for value-returning methods, or just
        //    `ret` for void.
        //
        //    The "try" region runs from firstOriginal up to (but not including)
        //    handlerStart; the "handler" region runs from handlerStart up to
        //    (but not including) afterHandler.
        body.Instructions.Add(handlerStart);
        body.Instructions.Add(endFinally);
        body.Instructions.Add(afterHandler);
        if (returnsValue)
        {
            body.Instructions.Add(finalRet);
        }

        // 6. Register the exception handler. Cecil computes offsets at write
        //    time, so we only need correct instruction references.
        body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
        {
            TryStart = firstOriginal,
            TryEnd = handlerStart,
            HandlerStart = handlerStart,
            HandlerEnd = afterHandler,
        });
    }

    // 7. Construct the hook directly (NOT through MonoModHooks.Modify) so we
    //    can Dispose on unload. applyByDefault: true matches Modify semantics.
    return new ILHook(target, Manipulator, applyByDefault: true);
}
```

### Why each step is necessary

| Step | Reason |
|---|---|
| Snapshot `firstOriginal` before any edit | Cecil collections are live; later inserts must reference a stable anchor. |
| Add the return-value local before walking `ret`s | We need its `VariableDefinition` reference to embed in the rewritten instructions. |
| Walk `ret` → `stloc; leave` (or `leave`) | A `ret` from inside a try region is illegal IL. `leave` is the only legal exit. |
| Insert prologue using `ILCursor.Goto(firstOriginal, MoveType.Before)` | Branches inside the body that targeted `firstOriginal` continue to do so (the cursor mutates the underlying collection, not the references). |
| Append handler at end | Cleaner than splicing into the middle of the body; offsets are computed by Cecil on serialise. |
| Register one new `ExceptionHandler(Finally)` | Adds outer-most handler; existing handlers in the body are not modified, so their TryStart/TryEnd/HandlerStart/HandlerEnd are still valid. The new outer handler can legally enclose them. |

### `ILCursor.EmitDelegate` — why we are not using it

`ILCursor.EmitDelegate<T>(T)` is convenient for embedding ad-hoc lambdas, but:

- It allocates a `DynamicReferenceCell` per emitted delegate; we install thousands of hooks.
- Each call goes through MonoMod's reference table, an extra indirection vs. a direct `call <static method>`.
- The captured-state semantics ("does `hookId` get captured into a closure?") are murky — for an `Action` with no captures, the lambda is cached, but we still pay an indirection.

A direct `call` to `ProbeStack.Enter(int32 hookId)` is simpler, allocation-free, and faster. The `hookId` is emitted as a literal `Ldc_I4` constant, which is exactly the shape JIT can fold.

---

## 4. ILHook object lifecycle

### Storage

```csharp
// One reference per installed timing hook. Held for the process lifetime; we
// do not need quick lookups, so a flat list is enough.
private static readonly List<ILHook> _installedHooks = new List<ILHook>();
```

The list is owned by `HookInterceptor` (static, single-instance class). One `ILHook` per timed method override. For an 18-mod, ~10 000-override modlist that is ~10 000 references — ~80 KB of references plus the underlying detour state. Negligible.

### Why not `MonoModHooks.Modify`?

`MonoModHooks.Modify` returns `void`. We cannot reach the underlying `ILHook` object, which means we cannot deterministically `Dispose()` it. Reading tModLoader's source would tell us whether tML auto-unloads on `Mod.Unload`, but we already know:

1. The mod survives a `Mods → Reload` only because the entire mod assembly is replaced and the old assembly's methods are no longer reachable as detour targets.
2. We need deterministic teardown for the existing `Mods → Reload` iteration loop the README documents (edit, build, reload, re-enter world). Leaving stale IL on top of methods owned by *other* mods that were not reloaded is unacceptable risk against Invariant 1.

Construct `new ILHook(target, manip, applyByDefault: true)` directly. This is supported by MonoMod.RuntimeDetour 25.3.2's public surface (verified via reflection). It bypasses `MonoModHooks`'s ownership table, which:

- Loses us the `DumpILHooks` enumeration (a diagnostic the agent surface can use). Acceptable cost for deterministic teardown.
- Means our hooks are *not* tracked by tML for auto-unload — so we **must** Dispose them ourselves. `Mod.Unload` is the contract: see below.

### Teardown

```csharp
public static void Uninstall()
{
    foreach (ILHook h in _installedHooks)
    {
        try { h.Dispose(); }
        catch (Exception ex)
        {
            // Continue tearing down the rest. We never leave a partial state.
            _teardownExceptions++;
        }
    }
    _installedHooks.Clear();
    Installed = false;
}
```

Called from a new `PerformanceProfiler.Unload()` override (mod-wide unload, mirrors `Mod.Load`'s install path). Important: `Mod.Unload` runs in *reverse* load order, so by the time our `Unload` fires, every other mod's content is still present (otherwise the methods we patched would already have been unloaded). This is the only safe disposal point.

`OnWorldUnload` is the wrong place — the user can re-enter a world without reloading the mod, and we do not want to install-then-tear-down 10 000 detours every world transition.

### What happens on `Mods → Reload`

1. tModLoader fires `Mod.Unload` on every mod in reverse order.
2. Our `Unload` disposes every `ILHook` we installed.
3. tModLoader unloads our assembly.
4. tModLoader loads the new assemblies and fires `Mod.Load` → `PostSetupContent` on each.
5. Our `PostSetupContent` re-enumerates and reinstalls.

There is no window in which a method has *our* IL but our assembly is unloaded — the dispose runs before our assembly goes.

### What happens if `Dispose` throws

The `try`/`catch` per item is the safety net. We never leave a partial-disposal state on the caller's side; the method has whatever MonoMod could not undo, but our list is empty and `Installed = false`. The catch increments `_teardownExceptions`; the count is logged on the next install and surfaced in the JSON-lines session header as a coverage-debt signal.

---

## 5. Conflict with other mods' IL hooks

### How MonoMod composes multiple IL hooks on the same method

MonoMod 25.x layers IL hooks in installation order. Each `ILHook` registers a `Manipulator` delegate; on application MonoMod re-applies *all* registered manipulators to the original method body in registration order. When a new ILHook is added, the entire chain is re-run from a fresh copy of the original IL — manipulators are not cumulative inside each other's edits but cumulative on the *output* of the previous one.

This matters for us:

1. **Our hook applies last in load order if installed late.** `PostSetupContent` is the last lifecycle phase before world entry. Any IL hook another mod installed in *its* `PostSetupContent` may run before or after ours depending on mod load order. We do not control mod load order.
2. **Our manipulator must be idempotent under composition.** Each time MonoMod re-runs the chain on a fresh body copy, our manipulator gets a fresh `ILContext` whose `Body` reflects the body shape *after* prior manipulators. Wrapping "the current whole body" in try/finally is correct regardless of what those prior manipulators did, *provided* their edits did not break the assumption that the body has at least one instruction and the body's `Instructions[0]` is a valid entry point.

### Worst-case failure modes

| Conflict | Symptom | What we do |
|---|---|---|
| Another mod's `ModifyIL` deletes the body entirely (rewrites to a single `ret`) | We wrap the lone `ret`; timing fires once per call. | Correct; nothing to do. |
| Another mod's `ModifyIL` introduces its own try/finally around the body | Our outer try encloses theirs; legal IL. | Correct; nothing to do. |
| Another mod's `ModifyIL` uses tail calls (`tail.` prefix) | Tail calls are incompatible with being inside a try region — the JIT silently downgrades to a regular call (no semantic break) but the user loses the tail-call optimisation. | Acceptable. Document. (No mods are known to use `tail.` on hook overrides.) |
| Our hook chain is loaded before the mod's runtime On-hook | The On-hook wraps the IL we already injected; the On-hook's `orig()` calls into our timed body. We measure the wrapped time including the On-hook overhead, which is what we want. | Correct. |
| Our hook chain is loaded *after* the mod's runtime On-hook | On-hook fires first, then enters our timing wrap inside `orig()`. We measure only the body, missing the On-hook overhead. | Documented limitation. The original delegate-pair system has the identical limitation; this is not a regression. |
| Another mod also uses `new ILHook(method, ...)` directly | Both ILHooks register; MonoMod composes both manipulators. | Correct; standard MonoMod semantics. |
| Another mod uses a non-MonoMod detour mechanism (raw P/Invoke, runtime emit overrides) | We see the post-detour method; our timing is correct relative to the detour. | Correct. |

### The one real risk

If a content mod uses the exact same approach (full-body ILHook wrapping) on the same method, our wrap-around-wrap is correct but the **inner** measurement is no longer pure mod cost — it includes the other mod's instrumentation overhead. Probability: vanishingly small. No known mod profiles other mods. If it happens, our overhead is the other profiler's overhead plus ours, and the result is a small over-attribution to that mod. Acceptable.

---

## 6. Category and naming continuity

**Nothing about `PerModAttribution` changes.** Concretely:

| Concern | Before | After | Delta |
|---|---|---|---|
| Category bucket | `InstallForMod` switches on declaring type, assigns `categoryId` ∈ {0..6} | Identical code path | none |
| Display name | `DisplayName(type, method, parameters)` builds `Type.Method(P0, P1)` | Identical | none |
| `RegisterHook(modId, categoryId, displayName)` | called at install | called at install | none |
| `Add(modId, categoryId, hookId, elapsedTicks)` | called from `HookProbe.Time*` | called from `ProbeStack.Leave` | semantics identical, fewer call sites |
| `HookDescriptor` shape | `(ModId, CategoryId, DisplayName)` | unchanged | none |
| Hook count tracking (`_measuredHookCounts`, `_totalHookCounts`) | incremented in `HookSupportedOverrides` | incremented in the equivalent loop in the new code | semantics identical |
| `_unsupportedHookSamples`, `_unsupportedSignatureFrequency` | populated when no delegate matched | populated only when ILHook install *throws* — should be near-empty | semantics same, debt approaches zero |
| `ProfiledModNames`, `ProfiledModVersions` | populated at install | unchanged | none |

The only structural change in the attribution side is: `_unsupportedHookSamples` now means "hooks we tried to wrap but ILHook construction or manipulator application threw" rather than "hooks whose signature was outside our hard-coded set." That re-meaning is captured cleanly by bumping `HookCoverageVersion` — see §12.

---

## 7. What gets deleted from `HookInterceptor.cs`

### Top-level delegate declarations (lines 18–189)

Every one of the 26 delegate type pairs:

- `OrigVoidHook` / `VoidHookWrapper`
- `OrigNpcHook` / `NpcHookWrapper`
- `OrigProjectileHook` / `ProjectileHookWrapper`
- `OrigGameTimeHook` / `GameTimeHookWrapper`
- `OrigInterfaceLayersHook` / `InterfaceLayersHookWrapper`
- `OrigSpriteBatchHook` / `SpriteBatchHookWrapper`
- `OrigBoolHook` / `BoolHookWrapper`
- `OrigBoolNpcHook` / `BoolNpcHookWrapper`
- `OrigBoolProjectileHook` / `BoolProjectileHookWrapper`
- `OrigBoolPlayerHook` / `BoolPlayerHookWrapper`
- `OrigBoolItemHook` / `BoolItemHookWrapper`
- `OrigVoidPlayerHook` / `VoidPlayerHookWrapper`
- `OrigVoidItemHook` / `VoidItemHookWrapper`
- `OrigItemPlayerHook` / `ItemPlayerHookWrapper`
- `OrigBoolItemPlayerHook` / `BoolItemPlayerHookWrapper`
- `OrigNpcPlayerHook` / `NpcPlayerHookWrapper`
- `OrigBoolNpcPlayerHook` / `BoolNpcPlayerHookWrapper`
- `OrigProjectilePlayerHook` / `ProjectilePlayerHookWrapper`
- `OrigBoolProjectilePlayerHook` / `BoolProjectilePlayerHookWrapper`
- `OrigNullableBoolHook` / `NullableBoolHookWrapper`
- `OrigBoolRefColorHook` / `BoolRefColorHookWrapper`
- `OrigGetAlphaHook` / `GetAlphaHookWrapper`
- `OrigIntHook` / `IntHookWrapper`
- `OrigPlayerBoolHook` / `PlayerBoolHookWrapper`
- `OrigNpcRefHitModifiersHook` / `NpcRefHitModifiersHookWrapper`
- `OrigNpcHitInfoIntHook` / `NpcHitInfoIntHookWrapper`
- `OrigTileIntIntBoolRefIntHook` / `TileIntIntBoolRefIntHookWrapper`
- `OrigDrawItemHook` / `DrawItemHookWrapper`
- `OrigShootHook` / `ShootHookWrapper`

### Static helpers inside `HookInterceptor`

- `private static readonly string[] SystemHooks`, `SystemGameTimeHooks`, `SystemInterfaceLayerHooks`, `SystemSpriteBatchHooks`, `PlayerHooks`, `EntityHooks`, `GlobalNpcHooks`, `GlobalProjectileHooks` — these legacy curated arrays are no longer needed; the ILHook path walks every override automatically. Delete.
- `TryHookSupportedOverride(MethodInfo, Type, int, int, Mod)` — the entire 270-line signature switch. Delete.
- `SignatureShape(Type, ParameterInfo[])` — kept (used by the new unsupported counter for the rare ILHook construction failures, see §8). **Retain.**
- `HookOverrides(Type, string[], int, int, Mod)`, `HookNpcOverrides`, `HookGameTimeOverrides`, `HookInterfaceLayerOverrides`, `HookSpriteBatchOverrides`, `HookProjectileOverrides` — none are still called from `InstallForMod` (verify in code review); they are dead helpers from earlier milestones. Delete.

### The entire `HookProbe` class (lines 991–1408)

Every `Time*` method (29 of them) is replaced by the single `ProbeStack.Leave()`. Delete the class.

### Net effect

`HookInterceptor.cs` shrinks from ~1 400 lines to ~250 lines. The remaining code is: install/uninstall, mod enumeration, type→category routing, ILHook installation and disposal, the unsupported-sample counters, and `SignatureShape`/`DisplayName`.

---

## 8. What gets added to `HookInterceptor.cs`

### New imports

```csharp
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
```

### New static state

```csharp
private static readonly List<ILHook> _installedHooks = new List<ILHook>();
private static int _teardownExceptions;
```

### New `ProbeStack` (in a new file `Profiling/ProbeStack.cs` — keeps `HookInterceptor.cs` focused)

Use the full implementation from §2 verbatim.

### New install path — replaces `HookSupportedOverrides`

```csharp
private static int HookEveryOverride(Type type, int modId, int categoryId, Mod self)
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

        // Skip dynamic / no-body methods up front (Invariant 4: never touch
        // internals we cannot verify).
        if (method.DeclaringType?.Assembly.IsDynamic == true)
        {
            RecordUnsupported(modId, type, method, reason: "dynamic-assembly");
            continue;
        }

        if (method.GetMethodBody() == null)
        {
            // Abstract was filtered above; this catches extern / InternalCall.
            continue;
        }

        try
        {
            int hookId = PerModAttribution.RegisterHook(modId, categoryId,
                DisplayName(type, method, method.GetParameters()));
            ILHook hook = InstallTimingHook(method, hookId);
            _installedHooks.Add(hook);
            count++;
            _totalHookCounts[modId]++;
            _measuredHookCounts[modId]++;
        }
        catch (Exception ex)
        {
            // ILHook construction or manipulator application failed. Record as
            // coverage debt and continue — Invariant 4 abort-clean per hook.
            RecordUnsupported(modId, type, method, reason: ex.GetType().Name);
            LogSampleHookFailure(type, method.Name, ex, self);
        }
    }

    return count;
}
```

### The manipulator

Use the full `InstallTimingHook` implementation from §3 verbatim.

### Updated `RecordUnsupported`

Add a `reason` parameter; otherwise unchanged. Stored in the existing samples list as `"<DisplayName> [<reason>]"` so the agent can distinguish IL-application failures from dynamic-assembly skips in `client.log` and the JSON-lines session file.

### New `Uninstall`

```csharp
public static void Uninstall()
{
    foreach (ILHook h in _installedHooks)
    {
        try { h.Dispose(); }
        catch { _teardownExceptions++; }
    }
    _installedHooks.Clear();
    Installed = false;
}
```

### Update `HookCoverageVersion`

```csharp
public const int HookCoverageVersion = 3; // ILHook migration: signature-agnostic coverage
```

Bumping this invalidates every prior session file in the user's profile cache — the JSON-lines fingerprint includes it (`SessionLogWriter.cs:562`).

### What `Install` looks like after

The shape is almost unchanged; only the inner `HookSupportedOverrides` call swaps for `HookEveryOverride`. The mod enumeration, `Configure(modCount)`, and the per-type category dispatch in `InstallForMod` remain identical.

---

## 9. Changes to other files

| File | Change |
|---|---|
| `Profiling/ProbeStack.cs` | **New file.** Contents per §2. |
| `Profiling/HookInterceptor.cs` | Rewritten per §7/§8. |
| `PerformanceProfiler.cs` | **Add** `public override void Unload() => HookInterceptor.Uninstall();`. This is the only entry point that guarantees teardown across reloads. |
| `Profiling/ProfilerSystem.cs` | **No change.** `PostSetupContent` still calls `HookInterceptor.Install(Mod)`. |
| `Profiling/PerModAttribution.cs` | **No change.** |
| `Profiling/MetricCollector.cs` | **No change.** |
| `Profiling/PerModSample.cs` | **No change.** |
| `Profiling/RingBuffer.cs` | **No change.** |
| `Profiling/TickFrame.cs` | **No change.** |
| `Profiling/SessionLogWriter.cs` | **No change.** `HookCoverageVersion` bump (§8) propagates automatically through `Hash(...)` at line 562. The session schema (`unsupportedHookSignatures`, `UnsupportedSignatureFrequency`, sample names) still applies; the values will simply approach zero post-migration. |
| `UI/ProfilerOverlay.cs` | **No change.** Reads from `PerModAttribution`, which is unchanged. |
| `UI/ProfilerOverlaySystem.cs` | **No change.** |
| `UI/ProfilerTheme.cs` | **No change.** |
| `PerformanceProfiler.csproj` | **No change.** The required MonoMod / Cecil assemblies are pulled in transitively by tModLoader.targets — verified by the existing `MonoModHooks.Add` reference compiling today. |
| `build.txt` | **No change.** |
| `context/_Overview.md` | **Recommended edit** noting the new ILHook lifecycle and the `Mod.Unload` teardown contract. Confirm with user before editing. |
| `context/tmodloader-monomod-detours.md` | **Recommended edit** to record that `MonoModHooks.Modify` returns `void` and that the profiler now constructs `ILHook` directly for deterministic teardown. Confirm with user before editing. |
| `context/notes/` | **New note** `ilhook-migration.md` capturing: why we bypass `MonoModHooks.Modify`, the probe-stack rationale, and the coverage-version bump procedure. Write after the change lands. |

---

## 10. Step-by-step implementation sequence

Implementation is structured as a discovery pass (steps 1–2) and an execution pass (steps 3–9), per the project's task-decomposition discipline.

| # | Action | File(s) | Verify | Risk |
|---|---|---|---|---|
| **1** | Re-read `HookInterceptor.cs` cover-to-cover; enumerate every public symbol referenced from outside the file (currently: `Install`, `Installed`, `HookCoverageVersion`, `ProfiledModNames`, `ProfiledModVersions`, `UnsupportedHookSignatures`, `MeasuredHookCounts`, `TotalHookCounts`, `UnsupportedHookSamples`, `UnsupportedSignatureFrequency`). | read-only | Confirm each symbol is preserved post-migration; record in scratch list. | **Low** — read-only. |
| **2** | Grep across the repo for each delegate name being deleted, to confirm none is referenced outside `HookInterceptor.cs`. | grep over repo | Zero non-`HookInterceptor.cs` hits. | **Low** — if a hit appears, the public-surface assumption is wrong; pause and decide. |
| **3** | Add `Profiling/ProbeStack.cs` with the `ProbeStack` class. Compile with `dotnet msbuild` from the mod folder. | new file | `dotnet msbuild` succeeds; no references yet. | **Low** — isolated. |
| **4** | Add `InstallTimingHook` and `HookEveryOverride` private methods to `HookInterceptor.cs` alongside the existing code (do not delete the old code yet). Add a feature flag `private const bool UseIlHook = false;` and an alternate switch in `HookSupportedOverrides` so the new path can be exercised without removing the old one. | `HookInterceptor.cs` | `dotnet msbuild` succeeds. Build with `UseIlHook = false` first (no behaviour change), then switch to `true` for a single in-game session and confirm `_measuredHookCounts` rises toward `_totalHookCounts` and the overlay shows reasonable per-mod numbers. | **Medium** — first ILHook installation may surface an unexpected method shape; the try/catch around `InstallTimingHook` catches it and routes to the existing unsupported counter. |
| **5** | In a small modlist (3–4 content mods plus a hard case like Calamity or Thorium), capture `client.log` and a session JSON-lines file. Compare per-hook totals for the methods the old delegate path covered against the new ILHook path. Differences > 5 % per hook are a red flag. | runtime verification only | Side-by-side numbers within tolerance; no `Logger.Warn` storm. | **High** — this is where instrumentation accuracy is validated; failures here motivate either a probe-stack bug fix or an IL emission fix. |
| **6** | Flip `UseIlHook = true` by default; remove the flag. Delete the old delegate pairs, `HookProbe`, `TryHookSupportedOverride`, and the dead helper methods (`HookOverrides` and friends) per §7. | `HookInterceptor.cs` | `dotnet msbuild` succeeds. In-game session shows the same per-mod numbers as in step 5. | **Low** — by this point the new path is proven; deletion is mechanical. |
| **7** | Bump `HookCoverageVersion` from 2 to 3. Add `public override void Unload()` in `PerformanceProfiler.cs` calling `HookInterceptor.Uninstall()`. | `HookInterceptor.cs`, `PerformanceProfiler.cs` | Reload the mod twice in succession (`Mods → Reload`); on the second reload `client.log` shows the disposal count from the prior session. Prior session files in `tModLoader-Logs` are pruned by `SessionLogWriter` at next load (existing behaviour through the fingerprint hash). | **Medium** — failure to dispose is invisible until an unrelated mod is also reloaded; the second-reload test exercises both. |
| **8** | Write the `context/notes/ilhook-migration.md` capture; propose the `context/_Overview.md` and `context/tmodloader-monomod-detours.md` edits to user. | `context/` | User reviews and accepts; commit. | **Low** — documentation. |
| **9** | Commit at logical checkpoints: (a) ProbeStack + dual-mode install (step 3+4), (b) old code removal (step 6), (c) version bump + Unload (step 7), (d) context updates (step 8). | git | Each commit builds and runs in-game. | **Low** — discipline. |

---

## 11. Rollback plan

The migration is fully reversible at any point up to and including step 8.

### Coexistence design (intentional, see step 4)

The dual-mode `UseIlHook` flag in step 4 is not just a development convenience — it is the rollback contract. Steps 3 and 4 leave the repository in a state where both paths coexist:

- `TryHookSupportedOverride` + `HookProbe` (delegate pairs) — the proven path.
- `HookEveryOverride` + `InstallTimingHook` + `ProbeStack` — the new path.

The switch is one constant. If a regression surfaces in step 5 or even after release, flipping `UseIlHook` back to `false` and shipping a hotfix restores the old behaviour with zero residual ILHook state.

### Minimal rollback (post step-6 deletion)

If the old code has been deleted (step 6) and a serious regression surfaces:

1. `git revert` the deletion commit (step 6).
2. The repository returns to the dual-mode state of step 5.
3. Flip `UseIlHook` to `false`.
4. Build, reload, verify.

### Catastrophic-rollback (full revert)

If the ILHook system is fundamentally broken in a way that affects the host (very unlikely given §3's read-only guarantee and the per-hook `try`/`catch`):

1. `git revert` the entire feature branch.
2. `HookCoverageVersion` returns to 2; existing session files re-validate; users see no UI change.
3. Net effect: identical to never having shipped the migration.

There is no permanent state on disk that the ILHook system writes outside the existing JSON-lines schema, so rollback is always clean.

---

## 12. Testing strategy

Testing splits into four layers, matching the four invariants and the dual-surface observability contract.

### 12a. Session pruning verification

**Hypothesis:** old (`HookCoverageVersion = 2`) session files are pruned on the first load after the migration.

**Steps:**
1. Pre-migration: enter a world, play 60 seconds, exit. Confirm a session file lands in the tModLoader logs directory with `coverage=2` in its identity hash (`SessionLogWriter.cs:562`).
2. Apply the migration (`HookCoverageVersion = 3`).
3. Load tModLoader; observe `client.log` for the pruning log line written by `SessionLogWriter` on session-cache rehydration.
4. Confirm the pre-migration file is no longer surfaced in the in-game session list.

**Pass criterion:** prior `coverage=2` files vanish from the active session view but are not deleted from disk (the README treats session files as user-owned data — prune from view, not from disk).

### 12b. Per-hook accuracy regression

**Hypothesis:** for hooks the old delegate-pair path covered, the ILHook path produces measurements within 5 % of the old measurements over a 60-second session at matched gameplay conditions.

**Steps:**
1. With `UseIlHook = false` (step 4 dual-mode build): run a 60-second session at the start of a Plantera arena fight against a fixed enemy spawn (deterministic-ish workload). Capture the JSON-lines hook totals into `baseline.json`.
2. With `UseIlHook = true`: same world, same fight, ideally same RNG seed if obtainable. Capture into `migrated.json`.
3. Compare per-hook totals (`HookDescriptor.DisplayName` is stable across the two builds). Compute relative difference per hook.

**Pass criterion:** for hooks present in both files, median relative difference < 5 %; no individual hook differs by more than 20 %. Larger differences are investigated as instrumentation bugs (likely candidates: probe-stack imbalance, wrong `ret`-rewrite for an edge-case method shape, missed exception path).

### 12c. New coverage verification

**Hypothesis:** previously-unsupported hooks (counted in `UnsupportedSignatureFrequency` with `coverage=2`) now appear in `PerModAttribution.Hooks` with non-zero measurements.

**Steps:**
1. From a `coverage=2` session, list the top 20 `UnsupportedSignatureFrequency` entries — the highest-impact uncovered shapes.
2. From a `coverage=3` session under similar gameplay, query `PerModAttribution.Hooks` for the corresponding `(Type.Method)` display names.
3. Confirm each appears, with non-zero `_hookTicks`.

**Pass criterion:** ≥ 90 % of the top-20 previously-unsupported shapes are now measured. Remaining gaps are either methods that genuinely never fire under that workload, or methods that legitimately failed ILHook installation (visible in the new `RecordUnsupported` reason field).

### 12d. Lifecycle / teardown verification

**Hypothesis:** disposing every `ILHook` on `Mod.Unload` leaves no residual IL state.

**Steps:**
1. Enter a world; observe `HookInterceptor.Installed == true`, `_installedHooks.Count > 0`.
2. `Mods → Reload`. Observe `client.log` for the install line of the *new* session and absence of any `MonoMod` warning about residual hooks.
3. Repeat 5 times in succession. The install count per session should be stable (within ± 5 hooks, reflecting natural mod-load-order variance).
4. After the 5th reload, in tModLoader's debug surface (or via `MonoModHooks.DumpILHooks` if reachable), confirm only one set of timing hooks is registered, not five stacked sets.

**Pass criterion:** stable hook count; no `MonoMod` warnings about double-application; no IL-disposal exceptions logged (or if any are logged, the `_teardownExceptions` counter is surfaced and explained).

### 12e. In-game smoke test (per CLAUDE.md operating loop)

A single 5-minute Eye of Cthulhu fight on a modlist with at least one heavy content mod (Calamity, Thorium, or Spirit Mod). Acceptance:

- F9 overlay opens without freezes or visual artefacts.
- Per-mod numbers are non-zero and stable (not erratic NaN/infinity).
- Total measured CPU time across all mods ≤ ~1.5 × frame time (sanity bound — instrumented time should not exceed wall-clock time per tick by a wild margin).
- Session JSON-lines file is well-formed and parses cleanly.
- `client.log` shows the install line, no `Warn`/`Error` from `HookInterceptor`, and the disarm line on world exit.

### Failure mode triage

| Symptom | Likely cause | First check |
|---|---|---|
| Crash on world entry, `InvalidProgramException` | Malformed IL — typically a `ret` we missed rewriting, or an exception handler with a bad anchor. | Enable `Logger.Debug` around `InstallTimingHook`; the failing method is named in the stack trace. |
| Crash on world entry, `BadImageFormatException` | Method body was mutated in a way Cecil rejects on serialise. Usually means we tried to wrap a method we should have skipped (e.g. one with `[MethodImpl(NoInlining)]` plus generic context — rare but real). | Add the failing method's full signature to a skip list keyed on `MethodInfo.MetadataToken`. |
| Hook count rises with each reload | `Uninstall()` not called. | `client.log` Disarm line missing → confirm `PerformanceProfiler.Unload()` actually overrides `Mod.Unload`. |
| Per-hook timings vastly higher than baseline | Probe stack leaking — `Enter` called without matching `Leave`. | Add depth-overflow `Logger.Warn` in `ProbeStack.Enter`; surface the count in the session JSON. |
| Per-hook timings near zero across the board | Probe stack `Leave` short-circuiting on bounds check. | Same instrumentation as above; check `_depth` and `_stack.Length` post-warmup. |

---

## Honest summary

The migration removes ~1 150 lines of brittle signature-matching code and replaces it with ~200 lines of one ILHook manipulator plus a per-thread probe stack. Coverage rises from ~71.6 % to expected ~98–100 % (the residual being the small set of dynamic-assembly and degenerate-body methods we intentionally skip). The only API contract change is that we bypass `MonoModHooks.Modify` in favour of direct `new ILHook(...)` so we can dispose deterministically — this is supported public API and explicitly recommended over `Modify` whenever explicit teardown is required.

The largest remaining honest uncertainty is the per-hook accuracy regression test (§12b) — we believe the new measurement is at least as accurate as the old, but the proof is empirical, not analytical. The dual-mode build (step 4) gives us a controlled environment to validate that before the old code is removed.
