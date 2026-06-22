# Architecture

> Top-down structural map of the Performance Profiler mod as of v0.12 (the unified `Data/` pipeline + browser-dashboard reality). Subsystem-level detail lives in `systems/`; how each subsystem plugs into tModLoader lives in `tmodloader/`; the cross-component map lives in `integration/integration-map.md`.

## Scope / Purpose

Performance Profiler is a tModLoader 1.4.4 client-side mod that attributes per-tick CPU and allocation cost to individual mods in the player's modlist, correlates that cost with what the player was doing (biome, boss, weather, loadout, deaths), and surfaces the result through a **local browser dashboard** plus an agent-readable LiteDB store. The mod is read-only by Invariant 1: it observes, never changes the game, save data, or any other mod's state.

This file describes what the repository contains and how the pieces fit. It does **not** restate per-subsystem reality (that lives in `systems/*.md`) or per-API plug-in detail (that lives in `tmodloader/*.md`).

## Repository Overview

The codebase is a single .NET 8 C# class library packaged as a `.tmod`. Four production source trees sit under the root (`Data/`, `Profiling/`, `Web/`, `UI/`), one entry-point file at the root (`PerformanceProfiler.cs`), and a non-shipping test harness in `Tests/`. The mod self-disables on host drift (Invariant 4) and stays inside the overhead budgets named in `README.md` (Invariant 2).

| | |
|---|---|
| Language / runtime | C# on .NET 8 (tModLoader 1.4.4 is pinned to .NET 8) |
| Build | `dotnet msbuild` from the mod folder, or tModLoader's in-game Develop Mods → Build + Reload |
| Tests | `dotnet test` against the `Tests/` xUnit project (pure-logic detectors; excluded from the `.tmod`). See `systems/test-harness.md` for the current fixture set and a known csproj-path drift. |
| Production source | `Data/` (~100 files), `Profiling/` (~86), `Web/` (~57), `UI/` (~33, archived overlay) |
| Persistence | LiteDB 5.0.21 single-file DB (`Profiling/Persistence/`), packed in the `.tmod` via `dllReferences = LiteDB` + `lib/LiteDB.dll` |
| Player surface | Browser dashboard at `http://127.0.0.1:27277/` (F9 opens it); the in-game overlay is archived |

### The two-stack model (architectural posture)

The mod deliberately separates a **data stack** (everything captured: ticks, per-mod CPU/alloc, spikes, stalls, segments, context, deaths, interactions) from a **presentation/storage stack** (what is written to disk, served to the dashboard, surfaced as an insight). The data stack is "how much of the game we can observe"; more is always better. The presentation stack is "how we spend the overhead budget and the player's attention". The rule is structural since v0.10: the only place calculation is allowed is inside a `Data/` pipeline stage; routers and exporters format snapshots, they never derive numbers. Full posture in `notes/philosophy.md` and `notes/future-unified-data-interface.md`; the implemented mechanism in `systems/data-pipeline.md`.

## Repository Structure

```text
PerformanceProfiler/
├── PerformanceProfiler.cs                  Mod entry point: opens LiteDB Database +
│                                           binds Dashboard HTTP server, RegisterDataPipeline,
│                                           ILHook teardown. Also hosts ProfilerPlayer (F9 → browser).
├── PerformanceProfiler.csproj              Main mod project (excludes Tests/** from compile)
├── ProfilerConfig.cs                       ModConfig (default tab, panel-width override — overlay legacy)
├── build.txt                               tModLoader manifest; version=0.12; buildIgnore=Tests/*, *.md, design/*, context/*
├── description.txt                         Workshop description
├── lib/LiteDB.dll                          Vendored LiteDB 5.0.21 (packed in the .tmod)
├── Localization/en-US_Mods.PerformanceProfiler.hjson
├── README.md / CLAUDE.md / AGENTS.md / LICENSE
│
├── Data/                                   THE PIPELINE — every stream-shaped artefact (v0.11 home)
│   ├── DataRegistry.cs                      Process-wide stream registry (.Shared singleton); Freeze()
│   ├── DataStage.cs / IDataStream.cs        Stage enum + base/typed stream contracts + marker interfaces
│   ├── TickContext.cs / SessionContext.cs   Per-tick ref struct + per-session immutable record
│   ├── Contracts/RolloutContracts.cs        Frozen v0.12 snapshot types + RolloutStreamNames constants
│   ├── Collectors/                          Raw per-tick signal (zero-alloc adapters)
│   │   FrameTimeCollector, HookCpuCollector, AllocationCollector,
│   │   ContextTagger (per-tick biome/boss/weather snapshot),
│   │   ModRosterScanner (F1: install-time per-mod content roster)
│   ├── Aggregators/                         Fold many ticks into structured bins
│   │   PerModAttribution (hot-path per-mod accumulator), PerModSample, PerTickAttributionRing,
│   │   EventAggregator, HeatmapAggregator, SegmentAggregator,
│   │   PerModUsageAggregator (F2), PerModCostTimeSeriesAggregator (F3),
│   │   SessionActivityHeatStripAggregator, LagFingerprintAggregator,
│   │   LagRhythmAggregator, ModInteractionAggregator,
│   │   Segments/ (SegmentDetector, SegmentStore, OpenSegment, Segment,
│   │             SegmentLifetimeStat, SegmentModAttributionStat, ...)
│   ├── Stats/                               Derived numbers (OnDemand; the dashboard pulls these)
│   │   KpiStat/KpiCalculator/KpiSnapshot, EventsFeedStat, SelfHealthStat,
│   │   SpikesStat, StallsStat, InsightsStat, Baseline, ModImpactScorer,
│   │   HookCoverageView, plus the v0.12 tab stats (ModObservatoryStat,
│   │   DormantSurfaceStat, CrossCuttingSignalStat, EngagementCostScatterStat,
│   │   GcPressureStat, PerSegmentLagDensityStat, AllocationCausalityStat,
│   │   TransitionTrackStat, PerModContextAttendanceStat, DeathReplayStat,
│   │   SessionChronicleStat, ...)
│   ├── Detectors/                           Threshold logic + pattern firing
│   │   SpikeDetector, StallDetector,
│   │   Insights/ (InsightsEngine, InsightStore, InsightRecord, RankingScorer,
│   │             InsightRenderer, IInsightDetector, Detectors/ — 10 concrete)
│   └── Streams/                             Persistence-facing writers (LiteDB-backed)
│       SessionRecorder (orchestrator), StreamRegistry, IPersistenceStream,
│       Session/Modlist/Spike/Stall/StallCluster/Segment/TickAggregate/
│       ContextTransition/Insight/PlayerDeath/WorldSnapshot/Interaction/
│       PerSessionAggregate streams + StreamJson helpers
│
├── Profiling/                              MEASUREMENT INFRASTRUCTURE (not "data" itself)
│   ├── PerformanceProfiler entry lives at root; this folder holds the engine.
│   ├── HookInterceptor.cs                   Delegate-pair backend: MonoModHooks.Add per signature
│   ├── ILHookInterceptor.cs                 IL backend (DEFAULT): per-method ILHook + ProbeStack
│   ├── HookCategoryRouter.cs                Shared type→category map (both backends)
│   ├── HookBackend.cs                       Mode flags (Delegate/ILHook/Parallel) + AllocationTracking
│   ├── HookSurfaceCache.cs                  Process-scoped GetLoadableTypes cache (shared by backends)
│   ├── ProbeStack.cs                        Static Enter/Leave[CpuAlloc] called from emitted IL
│   ├── MetricCollector.cs                   Per-tick frame engine; owns the ring buffer + spike detector
│   ├── RingBuffer.cs / TickFrame.cs         Generic circular buffer (TickFrame[1800]) + per-tick struct
│   ├── ProfilerSystem.cs                    ModSystem lifecycle glue; drives the per-tick pipeline loop
│   ├── ProfilerSelfHealth.cs               Process-wide install-delta + bytes-per-hook
│   ├── Time.cs / LangNameCache.cs / ModOwnerCache.cs / EnumStringTable.cs / Util/BoolIndex.cs
│   ├── Events/                             Context support structures (NOT streams)
│   │   EventContext, BiomeRegistry, BiomeBitset, BiomeDescriptor, BossSampler,
│   │   BossSlotArray, BucketStats, WeatherFlags, WeatherSources, GameMode,
│   │   InvasionId, SubworldProbe
│   ├── Pools/                              ListPool, RowPool, IPoolReset
│   └── Persistence/                        DB infrastructure (the DB + side-channel detectors)
│       ProfilerDatabase, DbWriterThread, EventJournal, Migrations, DbWriteOp,
│       ModlistFingerprint, ProfilerPaths, PersistenceFileNames, BsonShortNames,
│       ProfilerCompactCommand, SessionSummaryLogger, TickDownsampler,
│       WorldSnapshotter, ContextTransitionWatcher, PlayerDeathDetector,
│       LegacyJsonImporter, Commands/, Interactions/, Records/
│
├── Web/                                    BROWSER DASHBOARD (the live player surface)
│   ├── Server/                             Raw-TCP loopback HTTP server
│   │   DashboardHttpServer (127.0.0.1:27277, port search to 27287),
│   │   HttpRequest, HttpResponse
│   ├── DashboardRouter.cs                  Route() switch → ~28 /api/* endpoints + asset serving
│   ├── DashboardRouter.{Summary,Mods,Hooks,Timeline,Lag,Insights,Self}.cs
│   │                                       Per-tab Build* endpoint builders (read Data/ snapshots)
│   └── Assets/                             SPA shell + per-section partial-class bundles
│       DashboardAssets (concatenates + UTF-8-caches the bundles),
│       IndexHtml.*.cs (HTML shell), Css.*.cs (17 fragments), Js.*.cs (16 fragments)
│
├── UI/                                     ARCHIVED in-game overlay (NOT the player surface)
│   ├── ProfilerOverlaySystem.cs            Now only owns the F9 "OpenDashboard" keybind
│   ├── ProfilerOverlay.cs / ProfilerTheme.cs
│   └── Overlay/                            Tab framework + five tabs, kept on disk for a future
│       (IOverlayTab, TabRegistry, OverlayPanel, Tabs/*, Components/*)
│                                           Steam-Deck / handheld revival; not compiled into the
│                                           player path. See systems/overlay.md.
│
├── Tests/                                  Non-shipping xUnit harness (excluded from .tmod)
│   BaselineTests, BoolIndexTests, InsightStoreTests, PoolsTests,
│   RankingScorerTests, RingBufferTests, StallClassifierTests,
│   StallDetectorTests, TimeTests, Persistence/{RoundTrip,Benchmark}Tests
│
└── context/                               This folder (implementation memory)
    ├── _Overview.md / architecture.md / notes.md / _staleness-report.md / .context-lint.json
    ├── integration/                        Cross-cutting maps
    ├── tmodloader/                         Per-API reference + "how we plug in"
    ├── systems/                            Per-subsystem deep dives (this layer)
    ├── notes/                              Topical inbox (decisions, conventions, philosophy, future work)
    ├── perf-pass/                          v0.5→v0.6 performance-research record (baseline / deferred / verification)
    └── plans/code-health-audit/            Code-health audit + implementation receipt
```

## Subsystem Responsibilities

| # | Subsystem | Canonical home | Owns |
|---|-----------|----------------|------|
| 1 | Mod lifecycle | `systems/mod-lifecycle.md` | `Mod.Load`/`Unload`, `ModSystem` world lifecycle, deferred world-load init, backend selection, ILHook teardown, Database + Dashboard singletons |
| 2 | Data pipeline | `systems/data-pipeline.md` | The `Data/` registry: every named stream (collector/aggregator/stat/detector/stream), `DataRegistry.Shared`, frozen per-tick callbacks, name-keyed `Lookup<TSnapshot>` |
| 3 | Hook instrumentation | `systems/hook-instrumentation.md` | Delegate-pair detours, IL detours, shared category routing, coverage tri-state, abort-clean install |
| 4 | Metric collection | `systems/metric-collection.md` | Per-tick frame engine, ring buffer, per-mod attribution, frame-time accounting |
| 5 | Spike detection | `systems/spike-detection.md` | Median/MAD spike windows, stall detection, per-tick attribution ring, flush-on-unload |
| 6 | Allocation tracking | `systems/allocation-tracking.md` | `EnterCpuAlloc/LeaveCpuAlloc` IL emission, per-mod alloc columns |
| 7 | Events and context | `systems/events-and-context.md` | Biome/boss/weather/invasion snapshotting, segment detection, per-dimension bucket aggregation |
| 8 | Insights engine | `systems/insights-engine.md` | Detector roster, store with TTL + p-value-gated confidence, ranking, gated stub map |
| 9 | Persistence | `systems/persistence.md` | LiteDB store, single writer thread, four-layer crash safety, schema migrations, `SessionRecorder` + the persistence streams |
| 10 | Web dashboard | `systems/web-dashboard.md` | Loopback HTTP server, the `/api/*` router, the SPA asset bundles — the live player surface |
| 11 | Overlay (archived) | `systems/overlay.md` | The in-game tab framework + five tabs; kept on disk, not compiled into the player path |
| 12 | Test harness | `systems/test-harness.md` | Non-shipping xUnit project, pure-logic file linking, exclusion from `.tmod` |

The twelve entries above plus the per-API plug-in slices in `tmodloader/` cover every meaningful piece of the repository. Anything not named here is a small helper inside one of these subsystems.

## Dependency Direction

```
                       ┌──────────────────────────────┐
                       │ PerformanceProfiler (Mod)    │
                       │  Load  → open Database        │
                       │        → bind Dashboard server│
                       │        → RegisterDataPipeline │
                       │  Unload→ ILHook teardown +    │
                       │          DisposeAll + dispose │
                       └──────────────┬───────────────┘
                                      │
                       ┌──────────────▼───────────────┐
                       │ ProfilerSystem (ModSystem)    │
                       │  PostSetupContent install     │
                       │  OnWorldLoad → deferred init   │
                       │  PostUpdateEverything drives:  │
                       │    EndTick + pipeline callbacks│
                       └─────┬──────────────┬──────────┘
                             │              │
              ┌──────────────▼──┐   ┌───────▼──────────┐
              │ HookInterceptor │   │ ILHookInterceptor│   (Profiling/)
              │  delegate path  │   │  IL path (default)│
              └────┬────────────┘   └─────────┬─────────┘
                   │   shared HookCategoryRouter│
                   ▼   shared PerModAttribution ▼
              ┌─────────────────────────────────┐
              │   MetricCollector / RingBuffer   │   (Profiling/)
              │   SpikeDetector / StallDetector  │   (Data/Detectors/)
              └──────────────┬──────────────────┘
                             │
                ┌────────────▼──────────────────────────┐
                │      Data/ pipeline (DataRegistry)     │
                │  Collectors → Aggregators → Stats →    │
                │  Detectors → Streams                   │
                │  every number named + registered here  │
                └─────┬───────────────────┬──────────────┘
                      │ pull by name       │ persist
                      ▼ (snapshots)        ▼
            ┌──────────────────┐   ┌──────────────────────┐
            │ Web/DashboardRouter│  │ Data/Streams →        │
            │  /api/* endpoints  │  │ SessionRecorder →     │
            │  (HTTP worker)     │  │ ProfilerDatabase      │
            │  → browser SPA     │  │ (LiteDB, writer thread)│
            └──────────────────┘   └──────────────────────┘
                  player surface          agent surface
```

The arrows are unidirectional. The hot path stays inside the measurement layer (Hook + Metric + the frozen per-tick callbacks). The two consuming surfaces — the dashboard router (player) and the persistence streams (agent) — only read pipeline snapshots; neither reaches into the live collector. The archived overlay (`UI/`) is not in the active path. This is the v0.10 race-free posture: `DashboardRouter.BuildNow` once read `MetricCollector.History` directly from the HTTP worker thread, racing the game thread; it now reads through immutable snapshots.

## Core Execution / Data Flow

A single hook timing observation, end-to-end, extended to the browser surface (the dependency chain trace referenced in `notes/decisions.md` and `integration/integration-map.md`):

1. `Main.Update` advances one tick. `ModSystem.PreUpdateEntities` fires on `ProfilerSystem` (`Profiling/ProfilerSystem.cs`); `Collector.BeginTick()` opens a frame, reads the entry alloc-bytes counter, stamps `_tickOpen = true`.
2. tModLoader's `*Loader.HookList<T>` iterates each profiled mod's hook override. Each iteration enters a method patched by one of the two backends:
   - **Delegate path:** the wrapper delegate from `MonoModHooks.Add` runs; `HookProbe.Time*` reads `Stopwatch.GetTimestamp()`, calls `orig(...)` inside a `try/finally`, credits the elapsed ticks via `PerModAttribution.Add(modId, categoryId, hookId, deltaTicks)`.
   - **IL path (default):** the manipulator-injected `ProbeStack.Enter(hookId)` prologue runs; the body runs inside a finally-protected region; every `ret` is rewritten to `stloc retLocal; leave end`; `ProbeStack.Leave()` runs as the finally and credits `PerModAttribution.Add(...)`.
   - The `try/finally` (never `try/catch`) means a mod-thrown exception bubbles unchanged; only the time up to the throw is credited (Invariant 1).
3. After every hook in the tick has fired, `ModSystem.PostUpdateEverything` calls `Collector.EndTick(tickIndex, npc/proj/dust counts)`. `EndTick` reads the exit alloc-bytes counter, assembles a `TickFrame`, pushes it into the ring buffer, runs `SpikeDetector.Observe` against it.
4. `ProfilerSystem.PostUpdateEverything` then drives the per-session machinery: `_recorder.OnTick(latest, collector)` downsamples to LiteDB (queue-only, never blocks on disk); the `ContextTagger` stamps `TickFrame.Context` and `EventAggregator.Accumulate`s; the `SegmentDetector` opens/closes segments; the death/snapshot/transition watchers fire; and the frozen `DataRegistry.PerTickCallbacks` array is driven in a tight for-loop (zero virtual dispatch).
5. The insights engine evaluates off-thread every ~60 ticks, gated on the previous run finishing (`Interlocked.CompareExchange`), because an inline `Evaluate` once wedged the main thread for over a second.
6. **Player read path:** the browser SPA polls `/api/now` (~500 ms) and the per-tab endpoints (~1.5–3 s) on an HTTP worker thread. `DashboardHttpServer` accepts the request, `DashboardRouter.Route` dispatches to a `Build*` method, which calls `DataRegistry.Shared.Lookup<TSnapshot>(name).CurrentSnapshot()` and serialises the immutable snapshot to JSON. No game-thread blocking; no inline math.
7. **Agent read path:** `Data/Streams/*` writers enqueue `DbWriteOp`s through `SessionRecorder`; the single `DbWriterThread` batches and applies them to LiteDB; the redo journal + rotating backups protect against crash. `PreSaveAndQuit` / `OnWorldUnload` kicks off the session-end aggregation asynchronously.

The hot path is steps 2–4's per-tick capture. Both backends keep it zero-allocation (pre-allocated structs, `Stopwatch.GetTimestamp()` static reads, frozen callback array). Step 6 runs on a separate thread reading immutable snapshots. Step 7's disk writes are queued, never per-tick on the game thread.

## Inter-System Relationships

The relationships below are the ones a reader needs to navigate. The full per-component map lives in `integration/integration-map.md`.

| A | B | Mechanism | What breaks if the connection fails |
|---|---|-----------|--------------------------------------|
| `{HookInterceptor, ILHookInterceptor}` | `HookCategoryRouter` | static `ResolveCategory(Type)` call | Both backends lose category attribution; the tree/observatory per-category views break and the two backends disagree on bucket assignment |
| `ILHookInterceptor` | `ProbeStack` | IL-emitted `call` instructions | Every wrapped method throws on first call → instrumentation crashes the game unless `Mod.Unload` runs `ILHookInterceptor.Uninstall()` before our assembly unloads (Invariant 4 mitigation) |
| `MetricCollector` / `PerModAttribution` | `Data/` collectors + stats | `Data/Collectors/*` adapt over the collector's public read accessors; `Data/Stats/*` derive numbers from them | Every dashboard endpoint and persisted aggregate loses its source signal; the pipeline is empty |
| `ProfilerSystem` | `DataRegistry.PerTickCallbacks` | frozen array driven each `PostUpdateEverything` | Per-tick streams (F2 usage, F3 cost time series) stop folding; the v0.12 per-mod views go stale-empty |
| F1 `ModRosterScanner` / F2 `PerModUsageAggregator` / F3 `PerModCostTimeSeriesAggregator` | the 17 v0.12 tab streams | name-keyed `DataRegistry.Shared.Lookup<TSnapshot>(name)` (never direct class refs) | The observatory, attendance, dormant-surface, rhythm, and interaction views lose their per-mod foundation; the contract-decoupling that let the rework parallelise is what isolates this |
| `DashboardRouter.Build*` | `DataRegistry` snapshots | `Lookup<TSnapshot>(name).CurrentSnapshot()` on the HTTP worker thread | The dashboard serves stale or empty JSON; pre-v0.10 this read the live collector directly and raced the game thread |
| `SessionRecorder` | `ProfilerDatabase` (writer thread) | `DbWriteOp` enqueue → `DbWriterThread` drain | Nothing persists to LiteDB; the agent surface and cross-session lifetime data go dark, but metric collection + the live dashboard continue (Invariant 4) |
| `InsightsEngine.Shared` | `InsightsStat` (dashboard) + `Data/Streams/InsightStream` (DB) | static singleton; both read the same store | Live records would diverge between the player surface and the agent surface; the audit's potential-issue #6 |
| `RankingScorer` | `InsightStore.TopInto` | comparer-captured method call in the sort closure | Magnitude collapse to zero for share patterns (pre-fix) made 40% and 90% contributors rank identically |
| `ProfilerSystem` | `SessionRecorder` + `PreSaveAndQuit`/`OnWorldUnload` | wrapped Create/OnTick/End + async session-end kickoff | Without the try/catch wrappers + idempotent kickoff latch, a permissions/IO error or a double-fire would corrupt the session record or block the world save |

## State Ownership

| State | Owner | Visibility | Lifecycle |
|-------|-------|------------|-----------|
| `ProfiledMods` / `ProfiledModNames` / `ProfiledModVersions` | `HookInterceptor` (static) | public read | Populated at `PostSetupContent`, never cleared (process lifetime). The ILHook backend reads `ProfiledMods` to share the same modlist. |
| `_measuredHookCounts` / `_totalHookCounts` | each backend independently | internal; projected via `Data/Stats/HookCoverageView` | Populated at install |
| `DataRegistry.Shared` | static singleton | public | Registered once at `Mod.Load`; per-session state inside each stream is `InitialiseAll`'d on world load and `ResetAll`'d on world unload; `DisposeAll` at `Mod.Unload`. `PerTickCallbacks` is a frozen immutable array snapshot. |
| `Collector` | `ProfilerSystem` instance | **internal** (v0.10 tighten) | One per world; allocated on first `PostUpdateEverything` after `OnWorldLoad` (deferred init), nulled on `OnWorldUnload`. External consumers route through the registry. |
| `Database` (LiteDB) | `PerformanceProfiler` (static) | public read | Opened at `Mod.Load`, disposed at `Mod.Unload`; null if the open path failed (degrades to no-persistence) |
| `Dashboard` (HTTP server) | `PerformanceProfiler` (static) | public read | Bound at `Mod.Load`, disposed at `Mod.Unload`; null if every port in the search range was busy (F9 then inert) |
| `_recorder` (SessionRecorder) | `ProfilerSystem` instance | internal/private | One per world; constructed in deferred init; nulled on disposal OR on an IO failure (self-disable) |
| `InsightsEngine.Shared` | static field, lazy `GetOrCreateShared()` | public | One per session; explicitly cleared by `ProfilerSystem.OnWorldUnload` so the next world starts clean |
| `_installedHooks` (ILHook list) | `ILHookInterceptor` (static) | internal | Process-lifetime; disposed only via `Mod.Unload` → `Uninstall()` |
| Tab instances (archived overlay) | `TabRegistry.Tabs` | internal static | Not in the active player path; see `systems/overlay.md` |

## Structural Notes / Current Reality

- **Browser dashboard is the player surface; the overlay is archived.** As of v0.9.0 the in-game overlay was archived (kept on disk in `UI/` for a possible Steam-Deck revival, not compiled into the player path). F9 now opens the default browser to the loopback dashboard; the only in-game footprint is the F9 keybind and a one-line chat hint on world enter. See `systems/web-dashboard.md` and `systems/overlay.md`.
- **The `Data/` pipeline is the calculation locus.** Every number lives in a `Data/` stage and is looked up by stable name through `DataRegistry.Shared`. Routers and exporters format snapshots; they do not derive numbers. `ProfilerSystem.Collector` is `internal` to enforce this. v0.11 physically moved every stream-shaped class out of `Profiling/` into `Data/`; the infrastructure that produces or stores streams (the hook engine, `MetricCollector`, the DB layer, the `Events/` support structs) stayed in `Profiling/`. Canonical reality in `systems/data-pipeline.md`.
- **v0.12 tab rework.** Timeline / Lag / Insights moved from flat ledgers to multi-panel dashboards: 3 foundation streams (F1 `ModRosterScanner`, F2 `PerModUsageAggregator`, F3 `PerModCostTimeSeriesAggregator`) plus 17 new tab streams, built behind the frozen `Data/Contracts/RolloutContracts.cs` so ~14 background agents could compile against types whose implementations did not yet exist. The dashboard ships **five** SPA tabs (Summary, Timeline, Lag, Insights, Self); the README narrates six conceptual views (Now and Mods are merged into Summary). The code is reality.
- **Two coexisting hook backends.** `HookInterceptor` (delegate-pair) and `ILHookInterceptor` (IL) both live in the code; `HookBackend.Mode` chooses. ILHook is the default (~100% coverage vs the delegate path's ~71.6% signature-matched). `Parallel` mode runs both and logs divergence; player-visible numbers stay on whichever backend the mode selects.
- **Persistence is LiteDB, not JSON.** The legacy JSON `SessionLogWriter` was deleted in v0.3. Persistence is now a single LiteDB file + an append-only NDJSON redo journal + three rotating backups, driven by a single writer thread the game thread never touches. `SessionRecorder` orchestrates the `Data/Streams/*` writers. Crash safety is four-layered. See `systems/persistence.md`.
- **Abort-clean everywhere.** Hook install (both backends) wraps its loop in an outer try/catch; the IL backend disposes already-installed hooks on failure. Session persistence self-disables on IO failure and continues metric collection. The dashboard degrades to F9-inert if it cannot bind a port. The DB degrades to no-persistence if it cannot open. Invariant 4: instrumentation may decline, never crash the game.
- **EvidenceScope is orthogonal to Confidence.** An insight can be `Confidence.High` and still `EvidenceScope.ThisSession`; both badges render independently so a reader can argue with either dimension. `RankingScorer.NormaliseMagnitude` splits the magnitude regime by `PatternKey` (share patterns pass `[0,1]` through; ratio patterns keep the soft-knee curve).
- **Non-shipping tests.** The `Tests/` xUnit project lifts pure-logic source files in via `Compile Include + Link` (never `ProjectReference`, to keep tModLoader assemblies out of the runner); `build.txt`'s `buildIgnore` excludes `Tests/*` from the `.tmod`. NOTE: the committed test csproj still references several pre-v0.11 `Profiling/` paths for files that moved to `Data/`; this is a real path drift documented in `systems/test-harness.md` and is owned by the test/build maintenance pass, not this context update.

## Coverage

What this upkeep run (2026-06-22, the v0.10→v0.12 reconciliation) actually inspected vs noted vs inferred:

| Class | Files |
|-------|-------|
| **Directly inspected (read in full)** | `PerformanceProfiler.cs`, `Profiling/ProfilerSystem.cs`, `Web/DashboardRouter.cs` (Route + endpoint switch), every existing `context/*.md` (architecture, _Overview, notes.md, _staleness-report, all `systems/*.md`, `integration/integration-map.md`, `notes/decisions.md`, `notes/conventions.md`, `notes/philosophy.md`, `notes/ui-overhaul-plan.md`, `notes/future-unified-data-interface.md`, `notes/modlist-pre-upgrade-2026-06-22.md`, `perf-pass/baseline.md`). Subagents read in full: every `Web/*` source (server, 8 router partials, asset bundler, IndexHtml + Css/Js fragments), the 8 repaired `systems/*.md` source dependencies. |
| **Inspected by grep / partial read** | `Profiling/HookInterceptor.cs` + `ILHookInterceptor.cs` (via the prior pass + cross-check), `Data/DataRegistry.cs` / `IDataStream.cs` / `Contracts/RolloutContracts.cs` (via the conventions subagent), `Data/Stats/KpiStat.cs` family, the moved-file `find` verification across `Data/` vs `Profiling/`. |
| **Inferred from file structure or commit bodies** | The internal emission logic of the 17 v0.12 tab streams (read names + registration + class doc-comments, not every method), the per-detector insight emission, the exact line geometry of the moved files. |
| **Not inspected this run** | `Profiling/Persistence/Records/*` field-by-field, `Data/Aggregators/Segments/*` internal state machines, the SPA JS renderer internals beyond polling cadence + tab structure, `ProfilerTheme`/overlay component draw code (archived). |

Verification questions for the next session, where being wrong would mislead:

- The committed `Tests/*.csproj` `Compile Include` globs still point at pre-v0.11 `Profiling/` paths; confirm whether the test project currently builds, and repoint the globs at `Data/` if not (owned by the test/build pass, flagged in `systems/test-harness.md`).
- Three comments in `PerformanceProfiler.cs` and the `Load()` log line say "F10" while the keybind is registered at F9 ("OpenDashboard"); these are stale source comments, not behaviour. Confirm at the next code touch.
- The KpiStat/KpiCalculator doc-comments reference `ProfilerSystem.Load` as the registration call site, but the live site is `PerformanceProfiler.RegisterDataPipeline`; a source-comment drift to fix at the next code touch.
