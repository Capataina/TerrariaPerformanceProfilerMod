# Integration Map

> Cross-component map: which of our subsystems plugs into which tModLoader API, and what depends on what inside the mod itself. The per-API surface lives in `../tmodloader/*.md`; the per-subsystem implementation reality lives in `../systems/*.md`. This file is the connective tissue between the two.

## Status model

The mod is past its dashboard-first pivot (v0.9), its `Data/`-pipeline consolidation (v0.10–v0.11), its v0.12 Timeline/Lag/Insights rework, and the v0.13→v0.22 arc: the insights engine consolidated into a **top-level `Insights/` module** (5 families, reference frames + drivers + cross-session baselines), the dashboard rebuilt on a shadcn-neutral OKLCH **component library** with four new chart encodings and a sixth **Memory** tab, and an off-game **L4/L6/L8 testing harness** (`tools/testing/`). Persistence is LiteDB-backed (the legacy JSON `SessionLogWriter` was deleted in v0.3). The status model is "what's live, what's gated, what's deferred":

| Status | Items |
|--------|-------|
| **Live** | Hook instrumentation (both backends, ILHook default), metric collection, spike + stall detection, allocation tracking, events-and-context + segment detection, the unified `Data/` pipeline (foundations F1/F2/F3 + the v0.12 tab streams), the top-level `Insights/` engine (5 families, 13 live detectors, cross-session `contextBaselines`), LiteDB persistence (`SessionRecorder` + streams), the browser dashboard (loopback HTTP + **6-tab SPA**: Summary/Timeline/Lag/Insights/Self/Memory), the L1 xUnit test harness, the L4/L6/L8 dashboard audit harness |
| **Gated** | Three insight detectors await their prerequisites: `FreeRemovalCandidate` (engagement-signal), `LoadoutCombinationCost` (cross-session loadout aggregation), `HookFrequencyTail` (per-hook call-time histograms — an unmeasured hot-path addition, blocked by Invariant 2). See `systems/insights-engine.md` for the current roster |
| **Deferred** | The player-facing **insight feed** on the Insights tab (the engine ranks insights; the tab renders observatory + charts, not the feed yet — the named next item); player settings UI (`notes/future-settings-design.md`); post-session HTML report (`notes/future-html-report.md`); the per-insight LiteDB collection has a writer scaffold but no producer (the live feed is in-memory only); per-hook `CallCount`; multiplayer hook coverage (v2) |
| **Archived** | The in-game overlay (`UI/`) — kept on disk for a Steam-Deck revival, not in the player path; see `systems/overlay.md` |

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
| Data pipeline | `Mod.Load` → `RegisterDataPipeline`; `OnWorldLoad` → `DataRegistry.Shared.InitialiseAll` + `Freeze`; per-tick frozen callbacks; `OnWorldUnload` → `ResetAll`; `Mod.Unload` → `DisposeAll` | `systems/data-pipeline.md` | n/a (internal pipeline, not a tModLoader surface) |
| Dashboard server | `PerformanceProfiler.Dashboard` = `DashboardHttpServer`, bound at `Mod.Load` on 127.0.0.1:27277 (port search to 27287), disposed at `Mod.Unload` | `systems/web-dashboard.md` | `tmodloader/ui-system.md` (keybind only) |
| Browser-open keybind | `KeybindLoader.RegisterKeybind(Mod, "OpenDashboard", "F9")`, `ModKeybind.JustPressed`, `ModPlayer.ProcessTriggers` → launch default browser (`open`/`xdg-open`/shell) | `systems/web-dashboard.md` + `systems/mod-lifecycle.md` (poll) | `tmodloader/ui-system.md` |
| Archived overlay mount | `ModSystem.ModifyInterfaceLayers` → `LegacyGameInterfaceLayer`, `UpdateUI`, `Player.mouseInterface = true` — present in `UI/` but not in the player path as of v0.9.0 | `systems/overlay.md` (archived) | `tmodloader/ui-system.md` |
| Boss / event / biome context | `Player.Zone*` fields, `ModBiome.IsBiomeActive`, `NPC.boss`, `NPC.realLife`, `Main.bloodMoon` / `eclipse` / `pumpkinMoon` / `snowMoon` / `invasionType`, `Main.dayTime` | `systems/events-and-context.md` | `tmodloader/engagement-surfaces.md` |
| Modded biome enumeration | `BiomeRegistry.Populate()` over `ModContent.GetContent<ModBiome>()` plus reflection over `typeof(Player)`'s `Zone*` fields | `systems/events-and-context.md` | `tmodloader/engagement-surfaces.md` |
| Optional SubworldLibrary probe | reflection over `SubworldLibrary.SubworldSystem.Current` | `systems/events-and-context.md` | `tmodloader/engagement-surfaces.md` |
| Frame stats | `Main.GameUpdateCount`, `Main.npc[]` / `projectile[]` / `dust[]` (count `.active`), `GC.GetAllocatedBytesForCurrentThread()`, `Stopwatch.GetTimestamp()` | `systems/metric-collection.md` | `tmodloader/engagement-surfaces.md` (entity arrays), `tmodloader/lifecycle-and-loop.md` (Main.GameUpdateCount) |
| Persistence path | `ProfilerPaths.Root()` under tModLoader's per-platform save dir → `profiler.litedb` + `profiler.events.log` + rotating `.bak-{1,2,3}` | `systems/persistence.md` | `tmodloader/lifecycle-and-loop.md` |
| Session lifecycle | `Mod.Load` opens `ProfilerDatabase`; `OnWorldLoad` (deferred) builds `SessionRecorder` + upserts modlist; `PostUpdateEverything` → `SessionRecorder.OnTick`; `PreSaveAndQuit`/`OnWorldUnload` kicks off async session-end | `systems/persistence.md` + `systems/mod-lifecycle.md` | `tmodloader/lifecycle-and-loop.md` |
| Player read surface | browser SPA polls `/api/*` on an HTTP worker thread → `DashboardRouter.Build*` → `DataRegistry.Shared.Lookup<TSnapshot>(name).CurrentSnapshot()` | `systems/web-dashboard.md` | n/a (loopback HTTP, not a tModLoader surface) |
| Agent surface | `Mod.Logger.Info` / `Warn` / `Error` writing to `client.log`, plus the queryable LiteDB store | every subsystem at lifecycle boundaries | `tmodloader/monomod-detours.md` (also names Logger) |

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
   │                                                  _recorder?.OnTick(latest, collector)
   │                                                    TickDownsampler 1Hz/1min → DbWriteOp
   │                                                    enqueue (queue-only, never blocks)
   │                                                  _contextTagger.Snapshot(tickIndex)
   │                                                  Events.Accumulate(tagger.Current, frameMs)
   │                                                  SegmentDetector.OnTick(...)
   │                                                  for cb in DataRegistry.PerTickCallbacks: cb(in ctx)
   │                                                  (off-thread, ~60 ticks) InsightsEngine.Evaluate
   ▼
Browser SPA (separate HTTP worker thread, polling /api/* every 0.5–3s):
   DashboardHttpServer.Accept → DashboardRouter.Route → BuildXxx
   → DataRegistry.Shared.Lookup<TSnapshot>(name).CurrentSnapshot()
   → HttpResponse.Json (immutable snapshot, race-free; no game-thread block)
```

A broken link anywhere in that chain has a different failure mode:

| Broken link | Failure |
|-------------|---------|
| `ModSystem.PreUpdateEntities` not firing (partial frame) | `Collector.TickOpen` stays false; `EndTick` skips; no ghost zero-ms tick (correct) |
| Mod's override throws inside `orig(...)` | `try/finally` credits time-up-to-throw; exception propagates unchanged (Invariant 1) |
| `MonoModHooks.Add` throws at install | counted via `InstallFailures`; not measured but also not crashed |
| ILHook manipulator throws | per-method catch in `InstrumentTypeOverrides`; counted via `_failures`; rest of install continues |
| `ILHookInterceptor.Install` outer catch fires | `Uninstall()` disposes everything; `Installed = false`; Logger.Warn |
| Persistence IO error | `SessionRecorder.OnTick` throws → caught in `PostUpdateEverything` → `_recorder = null` for the rest of the world; metric collection + the live dashboard continue |
| DB open fails at `Mod.Load` | `Database = null`; everything runs in-memory only; no cross-session persistence; dashboard still serves the live session |
| Dashboard port bind fails | `Dashboard = null`; F9 is inert and chat shows the failure; the rest of the mod runs |
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
| Per-tick pipeline callbacks | `DataRegistry.PerTickCallbacks` is a frozen immutable array driven by a for-loop; zero virtual dispatch; convention #15–16 |
| Dashboard asset serving | CSS/JS/HTML bundles concatenated once and UTF-8-cached at type-init; no per-request rebuild; convention #20 |
| Dashboard reads | HTTP worker thread pulls immutable snapshots via `Lookup<TSnapshot>(name).CurrentSnapshot()`; no game-thread block, no inline math; convention #15 |
| Insight store `Top` | `TopInto` with reusable scratch buffers; allocation-free past warmup |
| Backend divergence log | `ConsumeDivergenceLogTrigger` consumed-and-reset; no per-tick spam |

### Invariant 4 (abort-clean) enforcement points

| Surface | Behaviour |
|---------|-----------|
| `HookInterceptor.Install` outer catch | `Installed = false`; `Logger.Warn`; no partial instrumentation surfaced |
| `ILHookInterceptor.Install` outer catch | Same plus `Uninstall()` to dispose any hooks that already landed |
| Per-method ILHook manipulator failure | Caught in `InstrumentTypeOverrides`; `_failures++`; one sampled `Logger.Warn`; rest of install continues |
| `ProfilerDatabase` open failure (`Mod.Load`) | `Database = null`; the mod runs in-memory only; the live dashboard still serves the current session |
| `SessionRecorder.OnTick` IO failure | caught in `PostUpdateEverything` → `_recorder = null` for the world; metric collection + dashboard continue |
| `DashboardHttpServer` bind failure | `Dashboard = null`; F9 inert; chat shows the failure; rest of the mod runs |
| `SubworldProbe.Initialise` reflection failure | `Available = false`; `CurrentId()` returns sentinel; no crash |
| `BiomeRegistry.Populate` reflection failure | `ModBiomeBindingOk = false`; `Logger.Info` reports the state |

## The 2026-05-19 design-wording correction (still valid)

The README and the design pitch say per-mod attribution "comes for free because tModLoader tracks per-assembly detour ownership through `MonoModHooks`." The public tModLoader API does not expose any such ownership table. Attribution **is** genuinely free, but via the profiler's own `MethodBase.DeclaringType.Assembly → Mod.Code` reflection — the dictionary built once at `PostSetupContent` and probed at each detour callsite. Same outcome; correct the wording when the README/design is next touched.
