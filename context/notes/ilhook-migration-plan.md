# ILHook Migration Plan

> **Status (2026-05-20): SHIPPED — preserved as historical research record.** ILHookInterceptor + ProbeStack + closed-generic inheritance pass + JIT shared-body filter all shipped in 3eccf89, d2da531, 83dfa49, 7da4058, 5725572. Default backend since b52f8b6. Coverage tri-state install outcomes + HookCategoryRouter + HookCoverageView landed in audit round 77a99d2. Outer-catch Uninstall hardening in same round. Canonical reality: systems/hook-instrumentation.md.
>
> Read the system files for current reality; this plan is the design brief that shipped, kept for the rationale.


> Tracks the path from the current delegate-pair system to ILHook-based
> instrumentation, through a parallel coexistence phase that validates accuracy
> before any cutover.
>
> The delegate-pair system is never removed until the ILHook system has been
> proven correct against it on a real modlist. The two systems run in parallel
> during validation; rollback is a single flag flip.

---

## Why migrate at all

The current `HookInterceptor` installs one `MonoModHooks.Add` delegate per
`(mod, hookMethod)` pair it can match to a supported signature shape. That means
it only instruments methods whose exact parameter type combination has been
hand-coded into `TryHookSupportedOverride`. Any hook whose signature is missing
from the switch goes into `UnsupportedSignatureFrequency` and is silently dark.

ILHook targets the `*Loader.<HookName>` dispatch method directly. One ILHook
instruments every mod's implementations of that hook in one shot, regardless of
signature complexity. The coverage jump is significant: all current `[partial]`
signatures disappear as a concept and the loader-level foreach timing covers hooks
the delegate system will never reach without a combinatorial explosion of new
delegate types.

The migration also changes the overhead model. Each current delegate costs one
delegate-dispatch frame per call. The ILHook cost is two `Stopwatch.GetTimestamp()`
reads injected inline around the per-mod call site -- cheaper than a virtual
dispatch and, critically, a fixed cost per loader method rather than scaling with
the number of installed detours.

---

## Background context

### The delegate-pair system (current)

`HookInterceptor.Install` walks every loaded mod's assembly via
`AssemblyManager.GetLoadableTypes`, finds hook overrides, and calls
`MonoModHooks.Add(method, wrapperDelegate)` for each one it can match. Attribution
flows through `HookProbe`, which calls `PerModAttribution.Add(modId, categoryId,
hookId, elapsed)` inside a `try/finally`. The data path:

```
mod's hook override
    -> MonoMod On-hook chain
        -> HookProbe.Time*(orig, ...)
            -> PerModAttribution.Add(modId, categoryId, hookId, elapsed)
                -> _ticks[modId * CategoryCount + categoryId]
                -> _hookTicks[hookId]
```

`MetricCollector` harvests from `PerModAttribution` once per tick and feeds the
UI and session JSON.

### ILHook system (to build)

An ILHook on a loader dispatch method (e.g. `NPCLoader.NPCAI`) injects
`Stopwatch.GetTimestamp()` reads around the body of the per-mod foreach loop.
Attribution is derived from the delegate or instance at the current iteration --
`MethodBase.DeclaringType.Assembly` matched against each loaded `Mod.Code`.

The data path would feed the same `PerModAttribution.Add` call. Which path writes
to which slots is the coexistence problem (see below).

---

## Coexistence architecture

Both systems must run simultaneously in parallel mode without double-counting.
The solution is separate attribution slots per backend, with a clear demarcation
at harvest time.

### Slot separation

Extend `PerModAttribution.Configure` to accept a `backendCount` parameter (default
1, extended to 2 for the parallel phase). Every attribution array is sized
`modCount * CategoryCount * backendCount`.

| Index formula | Backend | Used by |
|---|---|---|
| `modId * CategoryCount + categoryId` | Delegate (backend 0) | Current `HookProbe.Add` calls, unchanged |
| `backendCount * modCount * CategoryCount + modId * CategoryCount + categoryId` | ILHook (backend 1) | New ILHook probe calls |

Both backends write into the same `PerModAttribution` static storage, but into
non-overlapping halves of the array. The harvest methods grow a `backend` parameter
so `MetricCollector` can read either half or (in parallel mode) both.

An alternative simpler approach: duplicate the storage class entirely into
`PerModAttributionDelegate` and `PerModAttributionILHook`. This avoids index
arithmetic in the hot path at the cost of more allocation at setup. The index-split
approach is preferred because it keeps `PerModAttribution.Add` a single call per
detour with no branching.

**Constraint: no double-counting in normal display.** When only one backend is
active (delegate or ILHook, not parallel), `MetricCollector` reads only that
backend's slice. When parallel mode is active, the UI and session JSON display the
active backend's slice. The comparison logic reads both slices -- it is
non-display-path work and runs after tick close.

### What the ILHook system instruments that the delegate system does not

The delegate system instruments per-mod hook method overrides found by reflection.
The ILHook system instruments the loader dispatch foreach -- it sees every call
site, including hooks the delegate system skips because it lacks a matching
delegate type. These are genuinely different measurements:

- **Delegate system:** per `(mod, override)` cost, measured by wrapping the method.
- **ILHook system:** per `(mod, hook-slot)` cost, measured at the dispatch call site.

For hooks covered by both (those the delegate system has a delegate type for), the
measurements should agree. For hooks not covered by the delegate system, only the
ILHook system produces a number.

---

## Backend mode flag

```csharp
public enum HookBackendMode
{
    Delegate,    // current delegate-pair system only (default, production path)
    ILHook,      // ILHook system only
    Parallel,    // both run simultaneously; comparison logged each tick
}
```

Set in `HookInterceptor` as a static field, configurable at build time or via a
`build.txt` flag for the validation phase. Default: `Delegate`.

`HookInterceptor.Install` reads the mode and branches:

- `Delegate`: installs delegate detours as today, ILHook path dormant.
- `ILHook`: skips all `MonoModHooks.Add` delegate paths, runs ILHook install only.
- `Parallel`: runs both install paths.

When mode is `Delegate` and the ILHook code exists in the assembly but is not
invoked, it contributes zero per-tick overhead -- the C# dead-code eliminator
removes unreachable bodies before JIT.

---

## Validation mode (Parallel)

### What gets compared

After `PerModAttribution.HarvestInto` in `MetricCollector.EndTick`, when backend
mode is `Parallel`, the comparison pass runs:

For each `(modId, categoryId)` pair where both backends produced a non-zero
measurement this tick:

```
delta = |delegateMs - ilHookMs| / max(delegateMs, ilHookMs)
if (delta > DivergenceThreshold) -> log divergence
```

### Divergence threshold

Default `DivergenceThreshold = 0.20` (20%). Rationale: timing measurements on a
live game tick have inherent noise -- task preemptions, JIT warmup, GC interleaving.
A 5% threshold would produce spurious warnings on every tick. 20% captures genuine
disagreements (a hook counted twice, a hook missed entirely, attribution error)
while filtering ordinary jitter.

The threshold should be configurable without a rebuild. Store it in a static field
that the `HookInterceptor` summary log line prints so it is visible in `client.log`.

### What gets logged

`Mod.Logger.Debug` (not `Warn`) per divergent hook so the log is not polluted in
normal validation runs. A summary `Mod.Logger.Info` line at tick end counts total
divergent hooks / total compared hooks: `ILHook validation: 12/847 hooks diverged
this tick (threshold 20%)`.

A session-level divergence summary is appended to `client.log` at world unload:
total ticks compared, mean divergent-hook rate, worst-case hook name and delta.

### What "validation passed" means

Over a representative play session (5+ minutes with combat, NPCs spawning, items
in use):

1. The mean per-tick divergent-hook rate drops below 5% after JIT warmup (first
   120 ticks discarded).
2. No hook shows a persistent structural divergence (always > 20% off in the same
   direction), which would indicate a systematic attribution error.
3. The ILHook system produces non-zero measurements for hooks the delegate system
   skipped (proving expanded coverage).

When conditions 1 and 2 are met, the ILHook system is considered validated and
the cutover can proceed.

---

## ILHook technical approach

### Target discovery

For each hook name the profiler currently tracks (the `SystemHooks`,
`PlayerHooks`, `GlobalNpcHooks`, `GlobalProjectileHooks` arrays), resolve the
corresponding loader dispatch method by reflection:

```csharp
typeof(SystemLoader).GetMethod("PostUpdateEverything",
    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
```

Loader method names and accessibility need decompiler confirmation (see Open
questions). The `tModLoader.xml` documents `ItemLoader.*` dispatch methods;
`SystemLoader`, `NPCLoader`, `PlayerLoader`, `ProjectileLoader` dispatch methods
must be confirmed before any reflection lookup is written.

Abort-clean (Invariant 4): if `GetMethod` returns null, log
`Mod.Logger.Warn("ILHook: loader method {name} not found; skipping")` and
continue with remaining hooks. The ILHook backend degrades gracefully per-hook,
not all-or-nothing.

### Manipulator structure

Each manipulator must:

1. Locate the `foreach` body over the per-mod hook implementations.
2. Inject `Stopwatch.GetTimestamp()` call before the per-mod call site.
3. Inject `Stopwatch.GetTimestamp()` and `PerModAttributionILHook.Add(modId,
   categoryId, elapsed)` after the per-mod call site (inside a try/finally).
4. Derive `modId` by matching `MethodBase.DeclaringType.Assembly` against the
   `Mod.Code` map built at setup -- this is a lookup, not a game call.

The exact IL pattern depends on whether the dispatch loop iterates delegates,
`Global*` instances, or a struct array. This is the primary `[needs-internals]`
gap (see context/tmodloader-monomod-detours.md). The manipulator must be written
defensively: if the expected IL pattern is not found, the manipulator throws, the
wrapping `try/catch` catches it, and that hook's ILHook is skipped.

### Assembly-to-modId map

Cached at setup in `HookInterceptor.Install` (or a new `ILHookInterceptor.Install`)
before any manipulator runs:

```csharp
private static readonly Dictionary<Assembly, int> _assemblyToModId = new();
// Populated once during Install:
for (int i = 0; i < profiled.Count; i++)
    _assemblyToModId[profiled[i].Code] = i;
```

Hot path: `_assemblyToModId.TryGetValue(assembly, out modId)`. The dictionary is
read-only after setup, so there is no lock needed.

---

## Step-by-step sequence

### Phase 1 — Coexistence scaffold (no ILHook logic yet)

**Goal:** The ILHook system exists in the codebase and is structurally wired up,
but does nothing. Existing delegate measurements are unchanged.

1. Add `HookBackendMode` enum to `HookInterceptor.cs`.
2. Add `static HookBackendMode BackendMode = HookBackendMode.Delegate` to
   `HookInterceptor`.
3. Extend `PerModAttribution.Configure` to accept a `backendCount` parameter and
   size the storage accordingly. Keep the `Configure(int modCount)` overload for
   compatibility.
4. Add `PerModAttribution.Add(int backendId, int modId, int categoryId, int hookId,
   long elapsed)` overload alongside the existing one. The existing path calls
   `Add(backendId: 0, ...)` implicitly.
5. Add `PerModAttribution.HarvestInto(double[] destination, int backendId)` overload.
   `MetricCollector` uses `backendId: 0` by default (no behaviour change).
6. Add a stub `ILHookInterceptor.cs` that exposes `Install(Mod self, int[] modIdByAssembly)`
   and `Uninstall()` -- empty bodies for now.
7. In `HookInterceptor.Install`, after the delegate install loop, call
   `ILHookInterceptor.Install(self, assemblyMap)` if `BackendMode != Delegate`.
   Since the bodies are stubs, nothing changes.

**Verification:** Mod builds and runs. Overlay measurements are identical to
before the scaffold. `client.log` shows no ILHook lines.

**HookCoverageVersion: unchanged (still 2).**

### Phase 2 — ILHook proof-of-concept on one loader method

**Goal:** One ILHook manipulator is written and wired for a single loader method
(suggested: `SystemLoader.PostUpdateEverything` -- once-per-tick, not per-entity,
low risk). The ILHook backend produces measurements for that one hook. Parallel
mode is enabled locally by changing the flag.

1. Confirm `SystemLoader.PostUpdateEverything` dispatch method name and IL shape
   via `MonoModHooks.DumpIL` -- call it inside the manipulator before injecting
   anything, read `Logs/ILDumps/` output to see the actual dispatch loop structure.
2. Write the manipulator for `SystemLoader.PostUpdateEverything`. Inject timing IL
   around the per-mod call site.
3. Wire the manipulator in `ILHookInterceptor.Install` behind the `ILHook` and
   `Parallel` mode branches.
4. Set `BackendMode = HookBackendMode.Parallel` locally.
5. Load the game, enter a world, observe:
   - `client.log` shows ILHook install succeeded for `PostUpdateEverything`.
   - Per-tick divergence log shows the ILHook and delegate measurements for
     `PostUpdateEverything` converging (expected: ~same numbers after warmup).
6. Confirm delegate measurements for all other hooks are unchanged (backend 0
   harvest path reads the same slice it always did).

**HookCoverageVersion: unchanged.** The ILHook backend exists but the session
identity hash is still determined by the delegate backend's coverage.

### Phase 3 — Full ILHook coverage

**Goal:** The ILHook backend covers all loader methods the delegate system
currently tracks, plus new hooks the delegate system skips.

1. Extend `ILHookInterceptor` to cover: `PlayerLoader`, `NPCLoader`,
   `ProjectileLoader`, `ItemLoader`, plus any additional loader methods confirmed
   by decompiler.
2. The same DumpIL-before-inject pattern applies to every new loader method.
3. Run in `Parallel` mode for a full play session. Confirm divergence rate meets
   validation criteria (mean < 5% after warmup, no systematic outliers).
4. Log the session-level divergence summary to `client.log` and review.

**HookCoverageVersion: unchanged.**

### Phase 4 — Coverage expansion verification

**Goal:** Confirm the ILHook backend produces numbers for hooks the delegate
system skipped (the `UnsupportedSignatureFrequency` population).

1. In parallel mode, enumerate `UnsupportedSignatureFrequency` entries. For each
   mod that had unsupported hooks, verify the ILHook backend's per-mod total is
   higher than the delegate backend's total (since it now captures those hooks
   too).
2. Log this comparison at world unload: `ILHook expanded coverage: ModA gained
   +0.12 ms/tick vs delegate baseline (3 previously-skipped hooks)`.
3. If some unsupported hooks are still dark in the ILHook backend (e.g. hooks on
   loader methods not yet covered), record them as remaining coverage debt.

**HookCoverageVersion: unchanged.**

### Phase 5 — Cutover (delegate pairs removed)

**Gate:** Phases 3 and 4 validation criteria met, confirmed in a real play session.

1. Remove all `MonoModHooks.Add` delegate-pair paths from `HookInterceptor`.
2. Remove all `HookProbe` delegate types and the `VoidHookWrapper`, `NpcHookWrapper`
   etc. delegate type declarations.
3. Remove `HookBackendMode.Delegate` and `HookBackendMode.Parallel` enum cases.
4. Remove the parallel comparison path from `MetricCollector.EndTick`.
5. Simplify `PerModAttribution` back to single-backend storage (remove `backendId`
   parameter from all methods).
6. Bump `HookCoverageVersion` from 2 to 3. This invalidates session files recorded
   against the delegate backend.
7. Update `SessionLogWriter` to write the new coverage version into the session
   identity hash.

**This is the only commit that bumps `HookCoverageVersion`.**

---

## Rollback

Rollback during Phases 1--4 is a single flag change:

```csharp
private static HookBackendMode BackendMode = HookBackendMode.Delegate;
```

Because the delegate-pair paths are never removed during the parallel phase, this
restores the pre-ILHook system exactly. No data migration, no user-visible change.

Rollback after Phase 5 (post-cutover) requires reverting the commit that removed
the delegate paths. The delegate types and `TryHookSupportedOverride` code must
be restored from git history. This is a full revert, not a flag flip -- which is
why the cutover gate (Phase 5) must only be crossed after the validation criteria
are met.

---

## Open questions requiring decompiler verification

These must be resolved before writing any manipulator in Phase 2. The recommended
approach is `MonoModHooks.DumpIL` at runtime, which reveals the IL without a
clone.

| Question | Why it matters | Resolution method |
|---|---|---|
| `SystemLoader.PostUpdateEverything` -- static or instance? Exact name? | Reflection lookup signature | `DumpIL` at runtime; or inspect `tModLoader.dll` via dnSpy |
| `NPCLoader.NPCAI`, `PlayerLoader.PreUpdate` -- names confirmed? | Same | Same |
| `HookList<T>` -- does it exist? Is the foreach over it or over a plain array? | Determines where timing IL is injected | `DumpIL` on the loader method body |
| Per-mod identity at dispatch site -- delegate, instance, or struct? | Determines how `Assembly` is extracted per iteration | `DumpIL` |
| `MonoModHooks.Add`/`Modify` return types -- `IDisposable`? | Determines whether explicit disposal is needed or tML auto-unload suffices | `DumpIL` / dnSpy on `MonoModHooks` |

---

## Invariant checks

**Invariant 1 (read-only):** The ILHook manipulator injects only `Stopwatch.GetTimestamp()` reads and `PerModAttribution.Add` calls. It never removes, reorders, or rewrites existing IL instructions, never mutates `ref`/`out` arguments at the call site, and always re-emits the original call. This must be a mandatory code-review gate: every manipulator diff is reviewed for read-only compliance before merge.

**Invariant 2 (overhead budget):** The ILHook per-tick cost is two `GetTimestamp()` reads and one dictionary lookup per loader-method iteration. Measured against the budget before Phase 5. The delegate-pair system's per-call delegate frame overhead is replaced by this inlined cost, which is expected to be lower at the per-entity hooks (GlobalNPC.AI, GlobalProjectile.AI) where the delegate frame multiplied by entity count.

**Invariant 3 (honesty contract):** No change to UI copy or insight strings. Attribution numbers change (ILHook covers more hooks), but the display model is identical: descriptive cost with data-strength badge.

**Invariant 4 (abort-clean):** Per-hook abort on reflection failure (Phase 2+ install). Per-manipulator try/catch wrapping. If the ILHook backend degrades below a useful threshold at runtime, fall back to `BackendMode.Delegate` and report. The delegate path is never removed until Phase 5.
