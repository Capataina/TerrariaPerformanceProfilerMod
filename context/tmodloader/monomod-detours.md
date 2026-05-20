# tModLoader Integration Surface — MonoMod Detour & Instrumentation API

> Source: tModLoader.xml (tModLoader 1.4.4, build ~#5089). Serves components: Hook Interceptor (1).

## Summary

The blessed detour API, `Terraria.ModLoader.MonoModHooks`, has **exactly five documented members** — `Add`, `Modify`, and three diagnostic dumpers — and the *type itself* is not documented (a `T:` entry is absent; only `M:` entries exist). The two installation methods (`Add` for runtime detours, `Modify` for IL hooks) are public and sufficient to *install* instrumentation, but **every other concern the Hook Interceptor needs — detour removal, per-mod ownership tracking, the loader-method dispatch internals, and signature stability checking — is either undocumented or lives in tModLoader internals**. The `*Loader` dispatch hosts (`ItemLoader`, `NPCLoader`, etc.) and their content-hook methods (e.g. `ItemLoader.CanShoot`) *are* documented as public `M:` entries, which means they are reflection-discoverable by `MethodInfo`. This is the slice with the most gaps, exactly as anticipated: the install verb is public; the lifecycle, ownership, and abort-clean surfaces are not.

## The surface

| Fully-qualified member | Kind | What it does / why the profiler cares |
|---|---|---|
| `Terraria.ModLoader.MonoModHooks` | type `[needs-internals]` | The blessed wrapper over `MonoMod.RuntimeDetour`. No documented `T:` entry — only methods are documented. tModLoader routes all mod detours through it so it can track per-assembly ownership and auto-unload on mod reload. |
| `M:Terraria.ModLoader.MonoModHooks.Add(System.Reflection.MethodBase,System.Delegate)` | method | "Adds a hook (implemented by `hookDelegate`) to `method`." This is the **On-hook / runtime-detour** install primitive: pass the target `MethodBase` and a delegate whose signature is `(orig, args...)`. The Hook Interceptor's Standard/Deep per-`(GlobalType, hookMethod)` detours install through this. |
| `M:Terraria.ModLoader.MonoModHooks.Modify(System.Reflection.MethodBase,MonoMod.Cil.ILContext.Manipulator)` | method | "Adds an IL hook (implemented by `callback`) to `method`." This is the **ILHook** install primitive. The Lite-mode design ("ILHook the `*Loader.<HookName>` method bodies") installs through this — the manipulator wraps the dispatch foreach body with `Stopwatch` start/stop IL. |
| `M:Terraria.ModLoader.MonoModHooks.DumpILHooks` | method | "Dumps the list of currently registered IL hooks to the console." Agent-surface diagnostic: a way to confirm the profiler's own IL hooks landed. Throws `System.Exception` on failure. |
| `M:Terraria.ModLoader.MonoModHooks.DumpOnHooks` | method | "Dumps the list of currently registered On hooks to the console." Same role for `Add`-installed runtime detours. Throws `System.Exception`. |
| `M:Terraria.ModLoader.MonoModHooks.DumpIL(Terraria.ModLoader.Mod,MonoMod.Cil.ILContext)` | method | "Dumps the information about the given ILContext to a file in `Logs/ILDumps/{Mod Name}/{Method Name}.txt`." Used inside a `Modify` manipulator to snapshot IL before/after an edit — verification aid, not a runtime path. |
| `MonoMod.Cil.ILContext.Manipulator` | delegate type `[public-API]` | The callback signature `Modify` consumes. The only MonoMod-library type besides `ILContext` that appears anywhere in the tModLoader XML. |
| `MonoMod.Cil.ILContext` | type `[public-API]` | The IL-editing cursor object passed to a manipulator. Referenced by `Modify` and `DumpIL`; the MonoMod library (`monomod.runtimedetour 25.3.2`) supplies its full surface, not the tML XML. |
| `T:Terraria.ModLoader.ILoadable` | interface `[public-API]` | Autoload contract: any default-constructible implementer is auto-passed to `Mod.AddContent`. `Load(Mod)` / `Unload()` / `IsLoadingEnabled(Mod)` give the Hook Interceptor a clean install/teardown owner that is *not* `Mod.Load` itself. |
| `M:Terraria.ModLoader.ILoadable.Load(System.Reflection... Mod)` | method | "Called when loading the type." Legal install point — runs during the mod's load phase. |
| `M:Terraria.ModLoader.ILoadable.Unload` | method | "Called during unloading when needed." The teardown owner (CLAUDE.md "every detour installed at load is torn down at unload"). |
| `M:Terraria.ModLoader.Mod.Load` | method | Runs "after all content has been autoloaded"; no world exists yet. Mod-wide setup point — viable but `ILoadable.Load` is the more modular seam. |
| `M:Terraria.ModLoader.Mod.Unload` | method | "Called whenever this mod is unloaded… Mods are guaranteed to be unloaded in the reverse order they were loaded in." Teardown counterpart. |
| `P:Terraria.ModLoader.Mod.Code` | property | "The assembly code… loaded when tModLoader loads this mod." A mod's `System.Reflection.Assembly` — the join key for per-mod CPU attribution (`MethodBase.DeclaringType.Assembly` ↔ `Mod.Code`). |
| `P:Terraria.ModLoader.Mod.Name` / `P:...Mod.Version` / `P:...Mod.TModLoaderVersion` | properties | Identity + modlist-fingerprint inputs. `TModLoaderVersion` is "the version of tModLoader that was being used when this mod was built." |
| `P:Terraria.ModLoader.Mod.Logger` | property | "A logger with this mod's name." The agent-surface channel (`client.log`) for load/teardown/abort events. |
| `M:Terraria.ModLoader.Core.AssemblyManager.GetLoadableTypes(System.Reflection.Assembly)` | method | Safe replacement for `Assembly.GetTypes()` on a modded assembly — required because `Assembly.GetTypes()` throws on mods using `ExtendsFromModAttribute`. The profiler must use this for any type enumeration over `Mod.Code`. |
| `M:Terraria.ModLoader.ModLoader.GetMod(System.String)` | method | "Gets the instance of the Mod with the specified name." Throws `KeyNotFoundException` if absent. |
| `M:Terraria.ModLoader.ModLoader.TryGetMod(System.String,Terraria.ModLoader.Mod@)` | method | Safe variant — preferred for enumerating an arbitrary modlist. |
| `M:Terraria.ModLoader.ModLoader.HasMod(System.String)` | method | Presence check. |
| `T:Terraria.ModLoader.ItemLoader` (+ `NPCLoader`, `PlayerLoader`, `ProjectileLoader`, `TileLoader`, `BuffLoader`, `SystemLoader`, `WallLoader`, `MountLoader`, `ProjectileLoader`, `RecipeLoader`, `CommandLoader`, …) | types `[public-API]` | The per-content dispatch hosts. Their hook-dispatch methods (e.g. `M:Terraria.ModLoader.ItemLoader.CanShoot(Terraria.Item,Terraria.Player)`, `ItemLoader.ConsumeItem`, `ItemLoader.CanHitNPC`) are documented public `M:` entries — therefore discoverable by `typeof(ItemLoader).GetMethod(name, ...)`. These are the Lite-mode ILHook targets. |
| `M:Terraria.ModLoader.Core.HookList\`1.Create(...)` / `M:...GlobalHookList\`1.Create(...)` | methods `[partial]` | tModLoader's own per-hook dispatch-list builders. Documented as members but the `HookList<T>` *type* surface (the `Enumerate` iteration the design wants to time) is not documented. See Plug-in point 4. |
| `M:Terraria.ModLoader.ModLoader.BuildGlobalHook\`\`2(...)` | method `[partial]` | Builds the array of globals implementing a given hook. "Allows type inference on T and F" is the entire documented summary — mechanism undocumented. |
| `F:Terraria.ModLoader.BuildInfo.stableVersion` | field | "The Major.Minor version of the stable release at the time this build was created." A coarse abort-clean signal — see Invariant 4 surface. |
| `F:Terraria.ModLoader.BuildInfo.BuildDate` | field | "local time, for display purposes." Build-stamp, weaker signal than `stableVersion`. |

## Plug-in points

1. **Install an IL hook on a loader dispatch method — `M:Terraria.ModLoader.MonoModHooks.Modify(System.Reflection.MethodBase,MonoMod.Cil.ILContext.Manipulator)`.**
   The Lite-mode core. The profiler resolves the target via `typeof(Terraria.ModLoader.ItemLoader).GetMethod("CanShoot", ...)` (the loader methods are public and documented), then calls `MonoModHooks.Modify(target, manipulator)`. The manipulator uses `MonoMod.Cil.ILContext` to wrap the per-mod dispatch foreach body with `Stopwatch` start/stop, summing per-assembly afterwards. **`[public-API]`** for the install call itself; the *manipulator body* depends on the IL shape of tML's dispatch loop, which is **`[needs-internals]`** — see Invariant 4.

2. **Install a runtime On-detour on a specific content hook — `M:Terraria.ModLoader.MonoModHooks.Add(System.Reflection.MethodBase,System.Delegate)`.**
   Standard/Deep mode. For per-`(GlobalType, hookMethod)` granularity the profiler detours an individual mod's overridden method (e.g. a specific `ModItem.UseItem` override resolved by reflecting over `Mod.Code` types via `AssemblyManager.GetLoadableTypes`). The delegate signature must be `(orig_delegate, original_args...)`; calling `orig(...)` and timing around it yields the per-call cost. **`[public-API]`** — `Add` and its inputs are fully documented. Resolving *which* methods to detour is reflection over documented `MethodBase`, also public.

3. **Tear down every installed hook at mod/world unload — `M:Terraria.ModLoader.ILoadable.Unload` / `M:Terraria.ModLoader.Mod.Unload`.**
   The profiler must dispose its detours so a `Mods → Reload` leaves no orphaned IL. **The critical gap: no documented `MonoModHooks` member removes a hook.** `MonoModHooks` exposes only `Add`/`Modify`/dumpers. In MonoMod proper, `Hook` and `ILHook` are `IDisposable` and undone by `Dispose()`/`Undo()`; the design must hold the objects `Add`/`Modify` return — **and the XML does not document the return type of `Add` or `Modify`** (the `<member>` blocks have no `<returns>`). tModLoader additionally auto-unloads a mod's detours when the mod unloads (the reason `Add` exists rather than raw MonoMod). Whether explicit disposal is *needed* or tML's auto-unload suffices is **`[needs-internals]`** — verify the return types and the auto-unload path with a decompiler. The teardown *trigger* (`ILoadable.Unload`) is **`[public-API]`**.

4. **Time tModLoader's own per-loader hook dispatch — `T:Terraria.ModLoader.Core.HookList\`1` / `GlobalHookList\`1` / `M:...BuildGlobalHook\`\`2`.**
   The design's Lite-mode technique ("time each `HookList.Enumerate` foreach body once, sum per-mod-assembly") depends on the *shape* of tML's dispatch loop. `HookList<T>.Create` and `GlobalHookList<T>.Create` are documented members, and `BuildGlobalHook` is documented — but only with `<inheritdoc>` pointers and the one-line summary "Allows type inference on T and F". The `Enumerate` method, the iteration internals, and the field that holds the per-hook global array are **not in the XML**. **`[needs-internals]`** — the profiler can ILHook the *loader method that calls into* the dispatch (point 1), but timing the `HookList` enumeration directly requires decompiler verification of `HookList<T>`.

5. **Resolve per-mod assembly identity for attribution — `P:Terraria.ModLoader.Mod.Code` + `M:...AssemblyManager.GetLoadableTypes(System.Reflection.Assembly)`.**
   At a detour callsite the profiler has a `MethodBase`; `MethodBase.DeclaringType.Assembly` gives the owning assembly; matching it against each loaded `Mod.Code` yields the owning mod. Enumeration over `Mod.Code` types must go through `GetLoadableTypes`, never `Assembly.GetTypes()` (throws on `ExtendsFromModAttribute` mods). **`[public-API]`** — both members documented; this is the load-bearing part of the "free attribution" claim that *is* verifiable.

6. **Choose a legal, modular install/teardown lifecycle owner — `T:Terraria.ModLoader.ILoadable` (`Load`/`Unload`/`IsLoadingEnabled`).**
   Rather than installing from `Mod.Load`, the Hook Interceptor should be an `ILoadable` so install and teardown are co-located in one swappable component (CLAUDE.md modularity invariant — "comment one out, the rest still works"). `IsLoadingEnabled(Mod)` additionally gives a clean "Off mode / abort-clean" gate: return `false` and the component never loads. **`[public-API]`**.

7. **Enumerate the live modlist for the fingerprint — `M:...ModLoader.TryGetMod` + `Mod.Name`/`Mod.Version`/`Mod.TModLoaderVersion`.**
   Not detour surface per se, but the Hook Interceptor stamps which modlist a session belongs to. `TryGetMod` is the safe accessor; `Name`+`Version` are the hash inputs. **`[public-API]`**. Note: a documented *enumerable* of all loaded `Mod` instances (`ModLoader.Mods`) does **not** appear in the XML — see Open questions.

## Per-mod detour ownership

The design claims per-mod identity attribution "comes for free because tModLoader tracks per-assembly detour ownership through `MonoModHooks`." **What the public docs actually support is weaker and more indirect than that phrasing suggests.**

What is **verified** from the XML:

- `MonoModHooks.Add`/`Modify` exist and are the blessed install path. The *existence* of a tML-owned wrapper is consistent with tML tracking ownership — a raw `MonoMod.RuntimeDetour.Hook` would give tML nothing to track.
- `Mod.Code` exposes each mod's `Assembly`. At any detour callsite, `MethodBase.DeclaringType.Assembly` is obtainable, and matching it against the set of `Mod.Code` assemblies is a fully public, deterministic way to attribute a method to a mod.

What is **not** in the docs (`[needs-internals]`):

- There is **no documented summary, member, or property** describing tModLoader recording "this detour belongs to mod X." `MonoModHooks` has five documented methods; none mention ownership. The `DumpOnHooks`/`DumpILHooks` summaries say only "dumps the list" — they do not state the dump is keyed by mod.
- The mechanism by which tML auto-unloads a mod's detours on reload (strongly implied by `Add` existing) is internal and undocumented.

**Conclusion on the "free attribution" claim:** the *attribution itself* is genuinely free and public — but the free part is `MethodBase → DeclaringType.Assembly → Mod.Code`, **reflection the profiler does itself**, not a tML-provided ownership table. The claim "tModLoader tracks per-assembly detour ownership through `MonoModHooks`" is plausible (it explains why `Add` is mandated over raw MonoMod) but **is not stated anywhere in the public XML and must be tagged `NEEDS DECOMPILER VERIFICATION`**. Practical impact: low. The profiler does not depend on reading tML's ownership table — it derives ownership from `DeclaringType.Assembly`, which is solid public ground. The design wording should be softened from "comes for free [via tML]" to "comes for free [via `DeclaringType.Assembly` reflection]."

## Abort-clean (Invariant 4) surface

Invariant 4 requires the profiler to detect that a tModLoader loader-method signature has changed across an update **before** installing a detour against it. The public surface for this is **thin but workable**, and splits into two layers:

**Coarse version gate (`[public-API]`).**
- `F:Terraria.ModLoader.BuildInfo.stableVersion` — "Major.Minor version of the stable release at the time this build was created." The profiler can compare the `stableVersion` it was *built against* (compile-time constant baked into the `.tmod`) with the `stableVersion` of the *running* tML. A mismatch is a cheap "we are on an untested tML build — consider not installing" signal.
- `P:Terraria.ModLoader.Mod.TModLoaderVersion` — the tML version the profiler's own mod was built with; another anchor for the same comparison.
- `F:Terraria.ModLoader.BuildInfo.BuildDate` — weaker (display-only) corroboration.
- Limitation: a version gate is **coarse**. tML can change a loader method's IL within the *same* Major.Minor (the docs explicitly warn loader internals are perf-tuned and change). A version match does not prove signature stability; a version mismatch does not prove breakage.

**Fine signature check via reflection (`[public-API]` for the mechanism, `[needs-internals]` for the IL-body shape).**
- Because the `*Loader` dispatch methods are documented public `M:` entries, the profiler can, at install time, reflect the target: `typeof(ItemLoader).GetMethod("CanShoot", BindingFlags...)`. If `GetMethod` returns `null`, the method was renamed/removed → **abort that hook, log, continue**.
- The profiler can further compare `MethodInfo.GetParameters()` (count, `ParameterType`, `ParameterInfo.ParameterType.IsByRef`) and `ReturnType` against a baseline signature recorded at build time. A parameter-list change → abort that hook.
- This catches **signature** drift (name, params, return type). It does **not** catch **IL-body** drift — and the Lite-mode ILHook manipulator (plug-in point 1/4) is sensitive precisely to the body shape (the dispatch foreach loop). Detecting body drift would need either a recorded IL hash of the target method body (obtainable via `MethodBody.GetILAsByteArray()` from `System.Reflection`, a public API but brittle across compiler versions) or running the manipulator inside a `try`/`catch` and treating a `MonoMod` exception as the abort trigger.

**Recommended abort-clean posture (all on public API):**
1. Build-time: bake in `BuildInfo.stableVersion` baseline + a baseline signature descriptor (param types, return type) for every loader method the profiler targets.
2. Install-time, per target: `GetMethod` non-null → signature matches baseline → only then call `MonoModHooks.Modify`/`Add`. Any failure: skip that hook, `Logger.Warn` it, continue with the rest.
3. Wrap each `Modify` install in `try`/`catch` so an IL-shape mismatch surfaces as a caught exception → disable that layer, report on both surfaces (overlay + `client.log`).
4. If too many targets fail, escalate to disabling instrumentation wholesale (the "abort-clean" end state) and show the last completed session — exactly the "Off" mode behaviour the README already defines.

What is **`[needs-internals]`**: there is no public "is this loader method's body the shape I expect" predicate. The IL-shape verification is necessarily defensive (hash or try/catch), not declarative.

## Invariant checks

**Invariant 1 — read-only.** `MonoModHooks.Modify`/`Add` are general-purpose and *can* mutate behaviour (an IL hook can rewrite the method; an On-hook can skip calling `orig`). The read-only guarantee is therefore a **discipline the profiler must self-enforce, not an API guarantee**: every IL manipulator only *inserts* timing instructions and never removes/rewrites existing IL or alters locals that flow into game logic; every On-detour *always* calls `orig(...)` with unmodified arguments and never inspects-then-mutates. This must be a hard code-review gate on the Hook Interceptor — the API does not enforce it. `DumpILHooks`/`DumpIL` give an audit trail to prove inserted-only.

**Invariant 2 — hot-path / overhead budget.** Both install methods are *load-time* calls — zero per-tick cost from installation itself. The per-tick cost is entirely in what the *manipulator inserts*. The design's "time the foreach body once, sum per-assembly" keeps this to ≈ `Stopwatch` start/stop per loader-method per frame, not per mod per call. `MonoModHooks` itself imposes no per-tick overhead. The `Dump*` methods are diagnostic and must never be called per-tick (they write to console/disk). `Logger` calls (CLAUDE.md dual-surface rule) belong at install/teardown only.

**Invariant 4 — abort-clean.** Covered above. The key honest finding: the public API gives a **coarse** version gate and a **good** signature-shape check, but **no** declarative IL-body-stability check — abort-clean against body drift must be defensive (recorded IL hash and/or try/catch around `Modify`). This is sufficient to honour the invariant (the worst tolerable case — the mod declining to instrument — is reachable purely from public API), but it is reactive at the IL-body layer rather than predictive.

**Invariant 3 — honesty.** Not engaged by this slice (no insight strings here).

## Coverage verdict

The Hook Interceptor is **buildable on documented public API for installation and lifecycle, but its highest-value Lite-mode technique and its ownership claim rest on tModLoader internals.**

| Concern | Public-API coverage |
|---|---|
| Install an IL hook / runtime detour | **Full** — `MonoModHooks.Modify` / `Add`, fully documented |
| Resolve loader methods to hook | **Full** — `*Loader` dispatch methods are public documented `M:` entries, reflection-discoverable |
| Per-mod attribution | **Full but self-served** — `MethodBase.DeclaringType.Assembly` ↔ `Mod.Code`; the *profiler* does the attribution, not tML |
| Install/teardown lifecycle | **Full** — `ILoadable.Load`/`Unload`, `Mod.Load`/`Unload` |
| Detour *removal* | **Gap** — no documented `MonoModHooks` removal member; relies on undocumented return types of `Add`/`Modify` and/or tML's undocumented auto-unload |
| Timing tML's `HookList` dispatch directly | **Gap** — `HookList<T>`/`GlobalHookList<T>` enumeration internals undocumented; must ILHook the *enclosing loader method* instead |
| Abort-clean: signature drift | **Full** — `GetMethod` + `MethodInfo` reflection |
| Abort-clean: IL-body drift | **Gap** — no declarative check; must use recorded IL hash or try/catch |

**Estimate: roughly 60–70% of the Hook Interceptor stands on documented public API** — install, target resolution, attribution, and signature-level abort-clean are all solid. The remaining 30–40% (detour removal semantics, the `HookList` dispatch shape that Lite mode times, and IL-body-drift detection) requires decompiler verification. None of the gaps *block* the component — each has a public-API workaround (try/catch teardown, ILHook the loader method instead of `HookList`, defensive IL hashing) — but the workarounds are less precise than the design's wording assumes, and a Milestone 0 decompiler spike is warranted before Milestone 1 commits to the exact Lite-mode hook targets.

## Open questions / NEEDS DECOMPILER VERIFICATION

- **`MonoModHooks.Add` / `Modify` return types.** The XML `<member>` blocks have no `<returns>`. MonoMod's own `Hook`/`ILHook` are `IDisposable`; if `Add`/`Modify` return those, the profiler can hold and `Dispose()` them for deterministic teardown. **NEEDS DECOMPILER VERIFICATION** of the actual return types.
- **Detour auto-unload on mod reload.** Whether tModLoader automatically removes a mod's `MonoModHooks`-installed detours when the mod unloads (strongly implied — it is the reason `Add` is mandated over raw MonoMod) and whether explicit disposal is therefore optional or required. **NEEDS DECOMPILER VERIFICATION.**
- **Per-mod ownership table.** Whether tML keeps an internal `detour → owning mod/assembly` map reachable from any public surface. Nothing in the XML exposes one. **NEEDS DECOMPILER VERIFICATION** — but note the profiler does not strictly need it (attribution is derivable from `DeclaringType.Assembly`).
- **`HookList<T>` / `GlobalHookList<T>` enumeration shape.** The `Enumerate` method, the iteration loop, and the backing per-hook arrays are undocumented. The exact IL shape of the dispatch foreach the Lite-mode manipulator must wrap is unknown from the XML. **NEEDS DECOMPILER VERIFICATION** — this is the single most important gap for the Lite-mode spike.
- **Loader dispatch method bodies.** The `*Loader.<HookName>` methods are documented as *signatures*; their *IL bodies* (where the per-mod foreach lives) are not. The Lite-mode ILHook targets and their loop structure need decompiler confirmation per tML version.
- **Enumerable of loaded mods.** No `ModLoader.Mods`-style public enumerable appears in the XML. The profiler can enumerate via known names + `TryGetMod`, but a clean "all loaded mods" accessor — needed for the modlist fingerprint — may exist only internally. **NEEDS DECOMPILER VERIFICATION** (or confirm whether `ModLoader.Mods` exists undocumented).
- **Running tML's `BuildInfo.stableVersion` at runtime.** `BuildInfo` is documented with two fields; confirm `stableVersion` is a *static* field readable at runtime (not just a build-time constant invisible after compilation) so the install-time version gate is actually evaluable. The summary ("at the time this build was created") suggests it is baked per-build and readable — but **NEEDS DECOMPILER VERIFICATION** of accessibility/staticness.
- **`MonoMod.RuntimeDetour` direct types** (`Hook`, `ILHook`, `Detour`, `NativeDetour`) are supplied by `monomod.runtimedetour 25.3.2` / `monomod.core 1.3.2`, not the tML XML. They are not to be used directly (Invariant: always go through `MonoModHooks`), but their public surface — should the profiler need to *hold* the objects `Add`/`Modify` return — comes from the MonoMod library docs, not this file's source.

---

## How `ILHookInterceptor` actually wires (post-implementation status, 2026-05-20)

The 2026-05-19 analysis (above) flagged "`MonoModHooks.Add / Modify` return types" as a `NEEDS DECOMPILER VERIFICATION` gap because the XML carried no `<returns>` documentation. The 2026-05-20 implementation **resolves the gap by sidestepping `MonoModHooks.Modify` entirely**.

### The wiring

`ILHookInterceptor.InstallTimingHook` (`Profiling/ILHookInterceptor.cs:435-442`):

```csharp
private static ILHook InstallTimingHook(MethodInfo target, int hookId)
{
    ILContext.Manipulator manipulator = il => ApplyTimingWrap(il, hookId);
    // applyByDefault: true matches MonoModHooks.Modify semantics
    return new ILHook(target, manipulator, applyByDefault: true);
}
```

Direct construction of `MonoMod.RuntimeDetour.ILHook` rather than going through `MonoModHooks.Modify` because (as documented inline at `ILHookInterceptor.cs:36-41`):

> *We construct `ILHook` directly rather than going through `MonoModHooks.Modify` because the tModLoader API returns `void` from `Modify` — we'd never get the `ILHook` back to `ILHook.Dispose` on unload. Direct construction is the supported public API in MonoMod.RuntimeDetour 25.3.2 and gives deterministic teardown.*

So the resolution of the 2026-05-19 gap is **not** "the return type is X"; it is "the return type is void, which is the wrong shape for our lifetime, so we use the underlying `ILHook` ctor directly."

### Direct dependencies (NuGet)

The ILHook backend takes direct references to:

- `Mono.Cecil` (and `Mono.Cecil.Cil`) — for `MethodBody`, `Instruction`, `OpCodes`, `VariableDefinition`, `ExceptionHandler`, `ExceptionHandlerType`.
- `MonoMod.Cil` — for `ILContext`, `ILContext.Manipulator`, `ILCursor`, `MoveType`.
- `MonoMod.RuntimeDetour` — for `ILHook` itself.

All three are transitively available via tModLoader's own dependencies (tModLoader ships `monomod.runtimedetour 25.3.2` and `monomod.core 1.3.2`), so no extra package references are needed.

### The manipulator pattern

`ApplyTimingWrap(ILContext il, int hookId)` (`Profiling/ILHookInterceptor.cs:449-568`):

1. Decide whether the method returns a value (`body.Method.ReturnType.MetadataType != MetadataType.Void`).
2. If non-void, allocate a `VariableDefinition` for the return value and set `body.InitLocals = true`.
3. Anchor the original first instruction (`firstOriginal = body.Instructions[0]`). Cecil keeps instruction references by identity, so existing branches remain valid.
4. Choose the leave/enter call targets based on `HookBackend.AllocationTracking`:
   - CPU only: `_enterMethod` (`ProbeStack.Enter`) + `_leaveMethod` (`ProbeStack.Leave`).
   - CPU+alloc: `_enterCpuAllocMethod` (`ProbeStack.EnterCpuAlloc`) + `_leaveCpuAllocMethod` (`ProbeStack.LeaveCpuAlloc`). The prologue gains a `call GC.GetAllocatedBytesForCurrentThread()` between the `ldc.i4 hookId` and the `EnterCpuAlloc` call.
5. Build the tail anchors: `handlerStart` (the `call Leave[CpuAlloc]`), `endFinally`, `afterHandler` (either `ldloc retLocal` or `ret`), and (for non-void) a final `ret`.
6. Rewrite every existing `ret`:
   - **Non-void:** `ret` → `stloc retLocal`; insert `leave afterHandler` immediately after.
   - **Void:** `ret` → `leave afterHandler`.
7. Emit the prologue before `firstOriginal` via `ILCursor.Goto(firstOriginal, MoveType.Before)`.
8. Append the tail (`handlerStart`, `endFinally`, `afterHandler`, optional final `ret`).
9. Register a new outer `ExceptionHandler(Finally)` covering `firstOriginal → handlerStart`. Existing inner handlers stay legally nested.

### Lifecycle

Constructed `ILHook` instances are stored in a static `List<ILHook> _installedHooks` (`Profiling/ILHookInterceptor.cs:79`). `Mod.Unload` calls `ILHookInterceptor.Uninstall()` (`PerformanceProfiler.cs:33-36`), which iterates the list and calls `.Dispose()` on each. Disposal exceptions are swallowed per-hook because we cannot let one bad hook strand the rest of the teardown — the worst case is a stale IL patch on a method whose mod is unloading anyway.

`HookInterceptor` (delegate backend) does **not** need explicit teardown. `MonoModHooks.Add` detours are tracked per-assembly by tModLoader and auto-removed when our mod unloads.

### Per-method failure handling

Per-method ILHook construction is wrapped in `try/catch (Exception)` inside `InstrumentTypeOverrides` (`Profiling/ILHookInterceptor.cs:370-394`). On failure:

- `_failures++`.
- One sampled `Logger.Warn` per Install run (only the first failure is logged; subsequent ones are silent to avoid spam).
- The rest of the install loop continues.

Per-mod failures (a `GetLoadableTypes` throw, etc.) are caught one level up and counted as that mod being skipped.

The **outer** catch around the entire Install — for a failure between two methods — calls `Uninstall()` to dispose every hook that already landed (`Profiling/ILHookInterceptor.cs:166-182`). Invariant 4 abort-clean: never proceed against internals that no longer match; never leave instrumentation in a partial state.

### Canonical home

`systems/hook-instrumentation.md` carries the implementation reality, including the closed-generic inheritance pass and the JIT shared-body trap mitigation. `systems/allocation-tracking.md` carries the CPU+alloc emission variant.
