# Hook Instrumentation

*Maturity: comprehensive · Stability: unstable — backend selection is settled, but Parallel-mode divergence behaviour and signature widening of the delegate path are still in flux.*

## Scope / Purpose

Hook instrumentation is the load-bearing subsystem that turns every mod's per-tick hook invocations into per-mod CPU and allocation samples. It owns the install and teardown of timing detours, the shared category map, the backend-aware coverage view, and the abort-clean failure path mandated by Invariant 4. Every other measurement subsystem (metric collection, spike detection, allocation tracking, insights) consumes data this subsystem produces.

## Boundaries / Ownership

Files: `Profiling/HookInterceptor.cs`, `Profiling/ILHookInterceptor.cs`, `Profiling/HookCategoryRouter.cs`, `Profiling/HookBackend.cs`, `Profiling/ProbeStack.cs`. The backend-aware coverage view moved into `Data/` in v0.11: `Data/Stats/HookCoverageView.cs`.

Owns:

- Discovering the loaded modlist via `ModLoader.Mods` and building `ProfiledMods` + `ProfiledModNames` + `ProfiledModVersions` (`HookInterceptor.cs:246-323`).
- Resolving each content type to a category id via `HookCategoryRouter.ResolveCategory` (`HookCategoryRouter.cs:34-47`).
- Wrapping every discovered hook override with timing instrumentation — either a `MonoModHooks.Add` delegate detour (delegate backend) or an `ILHook` IL injection (IL backend).
- Tracking install outcomes per backend and surfacing them through `HookCoverageView`.
- Aborting clean if anything between two methods throws; partially-installed hooks are disposed.

Does not own:

- Per-tick accumulation arithmetic — that belongs to `PerModAttribution` (see `systems/metric-collection.md`).
- What to do with the per-mod totals — overlay, session log, and insights are downstream consumers.
- The choice of which backend to run — `HookBackend.Mode` is set elsewhere (currently a build-time constant; player-facing toggle is in `notes/future-settings-design.md`).

## Current Implemented Reality

### Two coexisting backends

| Backend | Install primitive | Coverage | Where it lives |
|---------|-------------------|----------|----------------|
| Delegate-pair | `MonoModHooks.Add(MethodBase, Delegate)` | ~71.6% (signature-matched only) | `HookInterceptor.cs` |
| IL injection | `new ILHook(target, manipulator, applyByDefault: true)` | ~100% (signature-agnostic) | `ILHookInterceptor.cs` |

`HookBackend.Mode` chooses one of three modes:

- `Delegate` — only the delegate backend installs. Conservative; legacy of the M1 build.
- `ILHook` — only the IL backend installs. **Current default** since commit `b52f8b6`.
- `Parallel` — both install on the same modlist. The IL path uses `PerModAttribution.RegisterOrReuseHook` so both backends share hook identity. The player surface stays on the delegate-side totals (the proven baseline); divergence is logged as `MetricCollector.BackendDivergence` and `[backend-compare]` lines in `client.log`.

### Coverage tri-state (delegate backend)

`HookInterceptor.TryHookSupportedOverride` returns one of three outcomes (`HookInterceptor.cs:386-394`):

| Outcome | Counter advanced | Semantics |
|---------|------------------|-----------|
| `Installed` | `_measuredHookCounts[modId]++`, `_totalHookCounts[modId]++` | Detour installed; signature is in the supported set |
| `UnsupportedSignature` | `_totalHookCounts[modId]++` + `_unsupportedSignatureFrequency[shape]++` | Override exists, signature not in the delegate pair set — coverage debt |
| `InstallFailed` | `_totalHookCounts[modId]++` + `_installFailures++` | Signature was supported but `MonoModHooks.Add` threw — MonoMod runtime error |

Counted separately because the remediations differ: unsupported signatures get a new delegate pair added; install failures need investigation in the tModLoader/MonoMod runtime. The unsupported-signature histogram stays clean of install errors.

`HookCoverageVersion = 3` (`HookInterceptor.cs:230`). Bumped any time accounting changes in a way that makes old session totals non-comparable. Folded into the persisted session's identity (via `SessionRecorder`) so old records prune automatically.

### Shared category router

`HookCategoryRouter` (`HookCategoryRouter.cs`) has seven category ids:

```
Systems=0, Players=1, Npcs=2, Projectiles=3, Items=4, World=5, Buffs=6
```

`ResolveCategory(Type)` returns `-1` for any type that is not one of the profiled `Mod*`/`Global*` content kinds. Both backends call this exact method; a new category gets added in one place.

### Backend-aware coverage view

`HookCoverageView` (`Data/Stats/HookCoverageView.cs`) decides which backend's counters are live. `UseILHookCounters = (HookBackend.Mode == HookBackendMode.ILHook)`; otherwise the delegate counters are returned.

Consumers route through it:

1. The archived overlay's PROFILER HEALTH strip / TreeTab coverage badge (under `UI/`, no longer instantiated since v0.9.0).
2. The dashboard coverage surface (via the `Data/Stats/` self-health/coverage path).
3. `SessionRecorder`'s persisted `coverage` block.

This is the single fix for the audit-flagged "100% on one surface / 0/X on another" divergence: the source of truth is one struct, not several independent reads.

### Delegate-path supported signatures

`TryHookSupportedOverride` matches roughly 30 signature families (`HookInterceptor.cs:501-776`):

```
arity 0:  void()  bool()  bool?()
arity 1:  void(NPC)  bool(NPC)  void(Projectile)  bool(Projectile)
          void(Player)  bool(Player)  void(Item)  bool(Item)
          void(GameTime)  void(List<GameInterfaceLayer>)
          void(SpriteBatch)  void(int)
          Color?(Color)            ← GetAlpha
          bool(ref Color)          ← PreDraw
arity 2:  void(Item, Player)  bool(Item, Player)
          void(NPC, Player)  bool(NPC, Player)
          void(Projectile, Player)  bool(Projectile, Player)
          void(Player, bool)
          void(NPC, ref NPC.HitModifiers)   ← ModifyHitNPC
arity 3:  void(NPC, NPC.HitInfo, int)       ← OnHitNPC
arity 4:  void(int, int, bool, ref int)     ← KillTile
arity 6:  void(SpriteBatch, Color, Color, float, float, int)  ← PreDrawInInventory
arity 7:  bool(Player, EntitySource_ItemUse_WithAmmo, Vector2, Vector2, int, int, float)  ← Shoot
```

Adding a new signature: add an `Orig*` + `*Wrapper` delegate pair at the top of `HookInterceptor.cs`, a `Time*` method on `HookProbe`, and the matching branch in `TryHookSupportedOverride`. Three edits, all in one file.

### IL-path manipulator

`ILHookInterceptor.InstallTimingHook` wraps every override regardless of signature (`ILHookInterceptor.cs:435-442`):

```
new ILHook(target, manipulator, applyByDefault: true)
```

`applyByDefault: true` matches `MonoModHooks.Modify` semantics — the hook is live immediately. Without it `IsApplied` stays false and the IL is never inserted.

`ApplyTimingWrap` (`ILHookInterceptor.cs:449-568`) does the transform:

1. Reads `body.Method.ReturnType` to decide if a return-value local is needed.
2. Allocates a `VariableDefinition` for the return local (non-void only); sets `body.InitLocals = true`.
3. Rewrites every `ret` in the body:
   - **Non-void:** `ret` becomes `stloc retLocal`, followed by an inserted `leave afterHandler`.
   - **Void:** `ret` becomes `leave afterHandler`.
   - Existing branches keep pointing at the same `Instruction` reference (Cecil identity, not offset).
4. Emits the prologue before the first original instruction:
   - **CPU path:** `ldc.i4 hookId; call ProbeStack.Enter(int32)`.
   - **CPU+alloc path:** `ldc.i4 hookId; call GC.GetAllocatedBytesForCurrentThread(); call ProbeStack.EnterCpuAlloc(int32, int64)`. The alloc-path leave reads the post-counter internally.
5. Appends `call ProbeStack.Leave[CpuAlloc](); endfinally; afterHandler; ret(non-void)` at the body tail.
6. Registers a new outer `ExceptionHandler(Finally)` covering `firstOriginal → handlerStart`. Existing inner handlers stay nested inside the new outer try region.

The original body executes inside the try region; `ProbeStack.Leave` always runs. A mod-thrown exception still propagates because the `endfinally` does not consume it.

### Closed-generic inheritance pass

`InstrumentTypeOverrides` walks types **without** `BindingFlags.DeclaredOnly` so closed-generic inherited methods surface (`ILHookInterceptor.cs:282-340`). The classification:

| `declaringType == type` | declaring type generic? | Treatment |
|------------------------|-------------------------|-----------|
| yes | n/a | Direct override — hook |
| no | open generic def (`IsGenericTypeDefinition`) | Skipped at the type level (`type.IsGenericTypeDefinition` filter) |
| no | closed generic instantiation | Hook **only if** declaring type's assembly is a mod (not tModLoader) |
| no | non-generic base | Skip — already enumerated when the base was walked |

The tModLoader-assembly filter at `ILHookInterceptor.cs:328-331` prevents the .NET JIT's shared-body trap: `ModType<Projectile, ModProjectile>::NewInstance` and `ModType<Player, ModPlayer>::NewInstance` JIT-share a compiled body for reference-type generic instantiations, so patching one patches both, and tModLoader's player path crashes with an `InvalidCastException` when it expects a ModPlayer and meets a projectile-typed frame.

The dedup set `_instrumentedHandles` (`HashSet<RuntimeMethodHandle>`) prevents stacking the same closed-generic body twice when multiple concrete subclasses surface the same `MethodInfo`. `RuntimeMethodHandle` comparison is the cheapest reliable identity.

### Abort-clean install

`HookInterceptor.Install` and `ILHookInterceptor.Install` both wrap their per-mod loop in a single outer try/catch. On exception:

- The flag (`Installed`) is left `false`.
- A single `Mod.Logger.Warn` line names the exception type and message.
- For the IL backend, `Uninstall()` is called to dispose every `ILHook` already in `_installedHooks` (`ILHookInterceptor.cs:176-182`). Without this, tModLoader unloads our assembly while patched IL still calls into `ProbeStack`, and the next tick blows up.
- For the delegate backend, no explicit dispose runs — `MonoModHooks.Add` detours are tracked by tModLoader per-assembly and auto-removed on mod unload.

Per-method failures are caught inside the per-mod loop and counted (`_installFailures` for the delegate path, `_failures` for the IL path), not propagated.

## Key Interfaces / Data Flow

```
PostSetupContent
       │
       ▼
HookInterceptor.Install(self)
       │
       ├─ enumerate ModLoader.Mods, skip "ModLoader"
       ├─ ProfiledMods / ProfiledModNames / ProfiledModVersions
       ├─ PerModAttribution.Configure(modCount, backendCount, allocTracking)
       │
       ├─ if HookBackend.DelegateActive:
       │     for each mod, walk types via AssemblyManager.GetLoadableTypes
       │     for each type, HookCategoryRouter.ResolveCategory(type)
       │     for each method that overrides a base virtual:
       │         TryHookSupportedOverride →
       │             Installed:        MonoModHooks.Add + counters++
       │             UnsupportedSig:   counters++ + histogram
       │             InstallFailed:    counter++
       │
       └─ (caller) if HookBackend.ILHookActive:
             ILHookInterceptor.Install(self, ProfiledMods)
                 same walk, but for each method:
                     InstallTimingHook(method, hookId) →
                         new ILHook(target, manipulator, applyByDefault:true)
                     _installedHooks.Add(hook)
                 outer-catch on type-walking exception: Uninstall()

per tick:
   tModLoader dispatches a hook → patched method runs →
       [delegate]  HookProbe.Time*(orig, args) → try/finally credits via PerModAttribution.Add
       [ILHook]    emitted prologue ProbeStack.Enter(hookId)
                   original body inside try
                   finally ProbeStack.Leave[CpuAlloc]()
                   ProbeStack credits via PerModAttribution.Add
```

The hot path is one `Stopwatch.GetTimestamp()` static read at entry, one at leave, one `PerModAttribution.Add(modId, categoryId, hookId, deltaTicks)` indexed-array write. No allocation. The two backends produce identical credit shapes; only the wrap mechanism differs.

## Implemented Outputs / Artifacts

| Surface | Source |
|---------|--------|
| Dashboard coverage strip (archived overlay PROFILER HEALTH) | `HookCoverageView.MeasuredHooks() / TotalHooks()` |
| Per-mod coverage badge (dashboard / archived TreeTab) | `HookCoverageView.MeasuredForMod(modId) / TotalForMod(modId)` |
| Persisted session `coverage` block | `SessionRecorder` reads through `HookCoverageView` (via `Data/Stats/`) |
| `[backend-compare] delegate=… ilhook=… Δ=…` log line | `ProfilerSystem.PostUpdateEverything` after `collector.ConsumeDivergenceLogTrigger()` |
| Install summary in `client.log` | `Mod.Logger.Info` at the end of each `Install` |
| Per-detour cost feed | `PerModAttribution.Add(modId, categoryId, hookId, deltaTicks)` per dispatch |

## Known Issues / Active Risks

- **JIT shared-body trap (mitigated, watch).** The `_tmlAssembly` filter at `ILHookInterceptor.cs:328-331` is the load-bearing protection against the .NET JIT sharing compiled bodies across reference-type generic instantiations. A new closed-generic inheritance scenario (a mod base class with non-tModLoader generic parents) could re-introduce the failure; the dedup set protects the same-instantiation case but not a new shared-body source. Downstream impact: world-load crash with an `InvalidCastException` from tModLoader's player path — a player-visible regression, not a silent attribution error.
- **Backend divergence is logged, not surfaced.** `Parallel` mode records `BackendDivergence` to `MetricCollector` and emits `[backend-compare]` lines, but there is no overlay surface for it. A player running Parallel sees two backends installing and one set of numbers. Acceptable today (Parallel is a dev tool), but the future settings UI (`notes/future-settings-design.md`) should expose it.
- **Delegate-path coverage debt is not fully enumerated for the IL default.** The IL path is signature-agnostic so the delegate path's ~28% miss does not affect production today. If the default ever flips back, the unsupported-signature histogram in `client.log` is the only place coverage debt is visible — there is no overlay surface for it. Downstream impact: silent attribution gap if the player picks the wrong backend.
- **`_installedHooks` and `_instrumentedHandles` are process-scoped.** Both static lists outlive any single world. A `Mods → Reload` calls `Mod.Unload`, which calls `ILHookInterceptor.Uninstall()`, which clears both. If `Unload` ever stopped firing (a tModLoader bug or an abort-clean in tModLoader itself), the lists would carry stale state into the next load. The `Installed` flag's `if (Installed) return;` guard at the top of `Install` would short-circuit a re-install, leaving the next session with no instrumentation. Watch item.

## Partial / In Progress

Nothing in this subsystem is in progress as of 2026-05-20. The audit's hook-instrumentation findings are all marked done or potential-issue-resolved in `plans/code-health-audit/index.md`. The deferred audit items (the persistence schema snapshot test) belong to `systems/persistence.md`, not here.

## Planned / Missing / Likely Changes

- **Settings UI exposure of `HookBackend.Mode`.** Currently a build-time constant. Player-facing toggle is sketched in `notes/future-settings-design.md`.
- **Coverage-debt overlay surface for the delegate backend.** If Parallel mode becomes a player-visible feature, the unsupported-signature histogram needs an overlay tab or section.
- **Multiplayer hook coverage.** v1 is single-player. `GlobalNPC.OnKill` / `OnSpawn` and certain other engagement hooks are single-player/server-only. Multiplayer hook coverage was deferred to v2 in the 2026-05-19 decisions. The instrumentation surface does not change, but the coverage *interpretation* does.

## Durable Notes / Discarded Approaches

- **On-hooks alone were considered safer than IL edits.** The original 2026-05-19 decision restricted attribution to MonoMod On-hooks, reasoning that an On-hook wraps a method and cannot corrupt it (a fault is wrong numbers, never a crash). The IL backend was added in commit `d2da531` after the coverage gap became visible. The original safety reasoning still holds for the delegate path; the IL path mitigates the same risk by (a) the `try/finally` exception-handler shape in the emitted IL and (b) explicit `Uninstall()` from `Mod.Unload`. The lesson worth carrying forward: explicit ownership and deterministic teardown are the load-bearing safety property, not the wrap mechanism itself.
- **`MonoModHooks.Modify` returns void.** The IL path uses `new ILHook(...)` directly (from `MonoMod.RuntimeDetour 25.3.2`) rather than going through `MonoModHooks.Modify`, because the tModLoader API returns void and we would never get the `ILHook` reference back to `Dispose()` on unload. Direct construction is the supported public API in `RuntimeDetour 25.3.2` and gives deterministic teardown. See `ILHookInterceptor.cs:36-41`.
- **Open generic types are unhookable.** `ILHookInterceptor` skips `type.IsGenericTypeDefinition` early. The closed instantiations surface via the inheritance pass on concrete subclasses (`BaseMusicBoxItem<MusicA>`, `<MusicB>`, ...). Without this skip MonoMod logged one "manipulator failed" warning per open type per launch.
- **The closed-generic inheritance pass landed in `7da4058`** ("Hook closed-generic instantiations to cover mods built on generic bases") and was crash-fixed in `5725572` ("Fix world-load crash from hooking tModLoader-internal closed generics") with the tModLoader-assembly filter. The two commits together are the cautionary tale: enabling broader coverage without filtering by assembly was a one-day regression.

## Obsolete / No Longer Relevant

- **`SystemHooks` / `PlayerHooks` / `EntityHooks` arrays.** Hand-curated arrays of hook names that the delegate-pair installer once iterated. Deleted in commit `77a99d2` (~180 lines). The `MethodInfo.GetBaseDefinition()` check (`HookInterceptor.cs:450-454`) is the replacement; it discovers every override structurally rather than enumerating names.
- **`HookOverrides` / `HookNpcOverrides` / `HookGameTimeOverrides` / `HookInterfaceLayerOverrides` / `HookSpriteBatchOverrides` / `HookProjectileOverrides` helpers.** Per-signature helper methods deleted in the same commit. Logic collapsed into the single `HookSupportedOverrides` flow plus the in-method category resolution.
- **Two-arg `PerModAttribution.Add(modId, categoryId, ticks)`.** Unused since the per-hook attribution model landed; removed in commit `77a99d2`.

## Cross-references

- `tmodloader/hook-surface.md` — the tModLoader hook surface our backends instrument.
- `tmodloader/monomod-detours.md` — the MonoMod detour API and how `ILHookInterceptor` wires it.
- `tmodloader/mod-identity.md` — how `MethodBase.DeclaringType.Assembly → ModId` attribution actually works.
- `systems/metric-collection.md` — what `PerModAttribution.Add` does with the per-detour ticks.
- `systems/allocation-tracking.md` — the CPU+alloc IL emission variant.
- `plans/code-health-audit/hook-instrumentation.md` — the audit findings that drove the 2026-05-20 changes.

## 2026-07-07: phase lanes + backend config

`PerModAttribution.Configure` gained the `phaseLanes` parameter (sized from
`PhaseSplitAttribution` at install; off ⇒ the draw mirrors never allocate and
`Add`'s lane branch is dead — behaviour bit-identical to pre-S01).
`HookBackend.Mode` is config-driven at Load: `PerHookAttribution=false` means
the Delegate backend, not lost attribution. The install path now also persists
an `InstallArmRow` per arm and WARNs on the reload-stack signature (see
persistence.md).
