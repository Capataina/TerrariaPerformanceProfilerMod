# Integration Map

> Cross-component map: which of our subsystems plugs into which tModLoader API, and what depends on what inside the mod itself. The per-API surface lives in `../tmodloader/*.md`; the per-subsystem implementation reality lives in `../systems/*.md`. This file is the connective tissue between the two.

## Status of the post-implementation pass

As of 2026-05-20 every README milestone through M2 (Tree + Standard mode) is implemented. M3 (Persistence + retrospective) has its session-log half landed and atomic; the lifetime-data half is `notes/litedb-migration-plan.md`'s territory. M4 (Insights engine) is the four-live-six-gated state described in `systems/insights-engine.md`.

The previous tier model (Tier 1 buildable today, Tier 2 needs game, Tier 3 metadata confirmation, Tier 4 spike) is no longer informative; the spike happened, the gaps are resolved. The current model is "what's live, what's gated, what's deferred":

| Status | Items |
|--------|-------|
| **Live** | Hook instrumentation (both backends), metric collection, spike detection, allocation tracking, events-and-context, insights engine (4 detectors), overlay (5 tabs), session logging, test harness |
| **Gated** | Six insight detectors (`events`-gated: ContextCorrelatedSpike, ContextConditionalCost, GcPauseCulprit, HookFrequencyTail; `litedb`-gated: SustainedCostShift, NewContributor) |
| **Deferred** | `SessionLogWriter` split + schema snapshot test (audit deferral); player settings UI (sketched); LiteDB lifetime persistence; HTML report sibling; `PreSaveAndQuit` clean-close badge |

## Per-component integration

Each row names one of our subsystems, the tModLoader surface it plugs into, and the file pair that owns the canonical description.

| Subsystem | Plugs into | Canonical home | Per-API reference |
|-----------|-----------|----------------|-------------------|
| Hook instrumentation (delegate) | `MonoModHooks.Add(MethodBase, Delegate)` | `systems/hook-instrumentation.md` | `tmodloader/monomod-detours.md` |
| Hook instrumentation (IL) | `new MonoMod.RuntimeDetour.ILHook(MethodBase, ILContext.Manipulator, applyByDefault: true)` + `Mono.Cecil.Cil` IL editing | `systems/hook-instrumentation.md` | `tmodloader/monomod-detours.md` |
| Mod enumeration | `ModLoader.Mods` (foreach), `AssemblyManager.GetLoadableTypes(Mod.Code)` | `systems/hook-instrumentation.md` (Install path) | `tmodloader/mod-identity.md` |
| Per-mod attribution | `MethodBase.DeclaringType.Assembly` ↔ `Mod.Code` (our own reflection) | `systems/metric-collection.md` | `tmodloader/mod-identity.md` |
| Per-tick lifecycle | `ModSystem.PreUpdateEntities` / `PostUpdateEverything` | `systems/mod-lifecycle.md` | `tmodloader/lifecycle-and-loop.md` |
| World lifecycle | `ModSystem.OnWorldLoad` / `OnWorldUnload` | `systems/mod-lifecycle.md` | `tmodloader/lifecycle-and-loop.md` |
| Content set-up | `Mod.PostSetupContent` (install + populate + initialise) | `systems/mod-lifecycle.md` | `tmodloader/lifecycle-and-loop.md` |
| ILHook teardown | `Mod.Unload` (explicit, before assembly unload) | `systems/mod-lifecycle.md` (calls `ILHookInterceptor.Uninstall`) | `tmodloader/monomod-detours.md` |
| F9 keybind | `KeybindLoader.RegisterKeybind(Mod, "ToggleOverlay", Keys.F9)`, `ModKeybind.JustPressed`, `ModPlayer.ProcessTriggers` | `systems/overlay.md` (mount) | `tmodloader/ui-system.md` |
| Overlay mount | `ModSystem.ModifyInterfaceLayers` → `LegacyGameInterfaceLayer` | `systems/overlay.md` | `tmodloader/ui-system.md` |
| UI update pump | `ModSystem.UpdateUI(GameTime)` → mod-owned `UserInterface.Update` | `systems/overlay.md` | `tmodloader/ui-system.md` |
| Input suppression | `Player.mouseInterface = true` while hovering panel | `systems/overlay.md` | `tmodloader/ui-system.md` |
| Boss / event / biome context | `Player.Zone*` fields, `ModBiome.IsBiomeActive`, `NPC.boss`, `NPC.realLife`, `Main.bloodMoon` / `eclipse` / `pumpkinMoon` / `snowMoon` / `invasionType`, `Main.dayTime` | `systems/events-and-context.md` | `tmodloader/engagement-surfaces.md` |
| Modded biome enumeration | `BiomeRegistry.Populate()` over `ModContent.GetContent<ModBiome>()` plus reflection over `typeof(Player)`'s `Zone*` fields | `systems/events-and-context.md` | `tmodloader/engagement-surfaces.md` |
| Optional SubworldLibrary probe | reflection over `SubworldLibrary.SubworldSystem.Current` | `systems/events-and-context.md` | `tmodloader/engagement-surfaces.md` |
| Frame stats | `Main.GameUpdateCount`, `Main.npc[]` / `projectile[]` / `dust[]` (count `.active`), `GC.GetAllocatedBytesForCurrentThread()`, `Stopwatch.GetTimestamp()` | `systems/metric-collection.md` | `tmodloader/engagement-surfaces.md` (entity arrays), `tmodloader/lifecycle-and-loop.md` (Main.GameUpdateCount) |
| Session JSON path | `Environment.SpecialFolder.ApplicationData` → `Terraria/tModLoader/PerformanceProfiler/sessions/` (resolved via `SessionDirectory()`); `Main.SavePath` reflection probe is the preferred resolution where available | `systems/session-logging.md` | `tmodloader/lifecycle-and-loop.md` |
| Agent surface | `Mod.Logger.Info` / `Warn` / `Error` writing to `client.log` | every subsystem at lifecycle boundaries | `tmodloader/monomod-detours.md` (also names Logger) |

## Hot-path dependency chain

The single most important chain to keep in mind: one hook timing observation, end to end.

```
Main.Update
   │
   ▼
ModSystem.PreUpdateEntities  ── ProfilerSystem ──▶ Collector.BeginTick
   │                                                  GC.GetAllocatedBytesForCurrentThread
   │                                                  Stopwatch.GetTimestamp
   │                                                  PerModAttribution.SnapshotForTick
   ▼
tModLoader's *Loader.HookList<T>.Enumerate iterates each mod's overrides:
   │
   │  ┌────────────────────────────────────────────────────────────────────┐
   │  │ patched method runs                                                 │
   │  │                                                                     │
   │  │   [delegate path]                                                   │
   │  │     HookProbe.Time*(orig, args):                                    │
   │  │        long start = Stopwatch.GetTimestamp();                       │
   │  │        try { orig(self, args); }                                    │
   │  │        finally {                                                    │
   │  │            PerModAttribution.Add(modId, cat, hook, delta)           │
   │  │        }                                                            │
   │  │                                                                     │
   │  │   [ILHook path]                                                     │
   │  │     emitted prologue: ldc.i4 hookId                                 │
   │  │                       (call GC.GetAllocatedBytesForCurrentThread)?  │
   │  │                       call ProbeStack.Enter[CpuAlloc]               │
   │  │     original body inside try region                                 │
   │  │     emitted finally: call ProbeStack.Leave[CpuAlloc]                │
   │  │                          → PerModAttribution.Add(..., delta)         │
   │  └────────────────────────────────────────────────────────────────────┘
   │
   ▼
ModSystem.PostUpdateEverything ── ProfilerSystem ──▶ Collector.EndTick
   │                                                    _ring.Push(TickFrame)
   │                                                    SpikeDetector.Observe(frame)
   │                                                    PerModAttribution.CloseTick
   │                                                    (Parallel mode: divergence delta)
   │                                                  _sessionLog?.Tick(collector)
   │                                                    timeline cadence: every 3600 ticks
   │                                                    AtomicWrite → temp + File.Replace
   │                                                    catch SessionLogFailureException → self-disable
   │                                                  _contextTagger.Snapshot(tickIndex)
   │                                                  Events.Accumulate(tagger.Current, frameMs)
   ▼
Overlay (next frame's UpdateUI tick):
   tab.Tick(collector)   // 1 Hz cadence inside each tab
   tab.Draw(sb, area, collector)
   Player.mouseInterface = true while hovering
```

A broken link anywhere in that chain has a different failure mode:

| Broken link | Failure |
|-------------|---------|
| `ModSystem.PreUpdateEntities` not firing (partial frame) | `Collector.TickOpen` stays false; `EndTick` skips; no ghost zero-ms tick (correct) |
| Mod's override throws inside `orig(...)` | `try/finally` credits time-up-to-throw; exception propagates unchanged (Invariant 1) |
| `MonoModHooks.Add` throws at install | counted via `InstallFailures`; not measured but also not crashed |
| ILHook manipulator throws | per-method catch in `InstrumentTypeOverrides`; counted via `_failures`; rest of install continues |
| `ILHookInterceptor.Install` outer catch fires | `Uninstall()` disposes everything; `Installed = false`; Logger.Warn |
| Session log IO error | `SessionLogFailureException` → catch in `PostUpdateEverything` → `_sessionLog = null` for the rest of the world; metric collection continues |
| `Mod.Unload` skipped (tModLoader bug) | ILHook references our types after assembly unload → next-tick crash; no defence today beyond tModLoader correctness |
| `ModLoader.Mods` empty or wrong | `Install` enumerates zero mods; nothing measured; coverage shows 0/0 |

## Cross-cutting concerns

### Invariant 1 (read-only) enforcement points

| Risk surface | Enforcement |
|--------------|-------------|
| Hook delegate probes | `try/finally` (never `try/catch`); mod-thrown exception propagates unchanged. Convention #3. |
| ILHook manipulator | `ApplyTimingWrap` inserts only — never removes existing IL or alters locals that flow into game logic. Reviewed per-emission. |
| Engagement-style hooks (none today) | If `GlobalItem.UseItem` / `Shoot` / `CanUseItem` are ever added to the instrumentation surface, their probes must return the upstream value unchanged. |
| `Player.mouseInterface = true` | The one write in the codebase; vanilla-sanctioned UI convention; suppresses player's own click being double-counted. |

### Invariant 2 (overhead budget) enforcement points

| Hot-path | Enforcement |
|----------|-------------|
| Per-detour entry/leave | `Stopwatch.GetTimestamp()` static reads; no `new Stopwatch()`; convention #5 |
| Per-mod accumulators | Pre-allocated `T[]` indexed by ModId; no `List<T>`; convention #6 |
| Per-tick logging | `Mod.Logger` only at lifecycle boundaries; never per tick; convention #4 |
| Overlay tab refresh | 1 Hz Tick; truncation caches; convention #8 |
| Insight store `Top` | `TopInto` with reusable scratch buffers; allocation-free past warmup |
| `_windowsView` | Cached at `SpikeDetector` construction; no per-read allocation |
| Backend divergence log | `ConsumeDivergenceLogTrigger` consumed-and-reset; no per-tick spam |

### Invariant 4 (abort-clean) enforcement points

| Surface | Behaviour |
|---------|-----------|
| `HookInterceptor.Install` outer catch | `Installed = false`; `Logger.Warn`; no partial instrumentation surfaced |
| `ILHookInterceptor.Install` outer catch | Same plus `Uninstall()` to dispose any hooks that already landed |
| Per-method ILHook manipulator failure | Caught in `InstrumentTypeOverrides`; `_failures++`; one sampled `Logger.Warn`; rest of install continues |
| `SessionLogWriter.Create` IO failure | `_sessionLog = null` for the world; `Logger.Warn` once; metric collection continues |
| `SessionLogWriter.Tick` IO failure | `SessionLogFailureException` → catch → `_sessionLog = null` for the rest of the world |
| `SubworldProbe.Initialise` reflection failure | `Available = false`; `CurrentId()` returns sentinel; no crash |
| `BiomeRegistry.Populate` reflection failure | `ModBiomeBindingOk = false`; `Logger.Info` reports the state |

## The 2026-05-19 design-wording correction (still valid)

The README and the design pitch say per-mod attribution "comes for free because tModLoader tracks per-assembly detour ownership through `MonoModHooks`." The public tModLoader API does not expose any such ownership table. Attribution **is** genuinely free, but via the profiler's own `MethodBase.DeclaringType.Assembly → Mod.Code` reflection — the dictionary built once at `PostSetupContent` and probed at each detour callsite. Same outcome; correct the wording when the README/design is next touched.
