# Architecture

> Top-down structural map of the Performance Profiler mod after the 2026-05-20 code-health-audit implementation pass. Subsystem-level detail lives in `systems/`; how each subsystem plugs into tModLoader lives in `tmodloader/`; the cross-component map lives in `integration/integration-map.md`.

## Scope / Purpose

Performance Profiler is a tModLoader 1.4.4 client-side mod that attributes per-tick CPU and allocation cost to individual mods in the player's modlist, surfaces the result through an F9 overlay, and exports an agent-readable JSON session report. The mod is read-only by Invariant 1: it observes, never changes the game, save data, or any other mod's state.

This file describes what the repository contains and how the pieces fit. It does **not** restate per-subsystem reality (that lives in `systems/*.md`) or per-API plug-in detail (that lives in `tmodloader/*.md`).

## Repository Overview

The codebase is a single .NET 8 C# class library packaged as a `.tmod`. Eleven subsystems sit under two top-level folders (`Profiling/` and `UI/`), one entry-point file at the root (`PerformanceProfiler.cs`), and a non-shipping test harness in `Tests/`. The mod self-disables on host drift (Invariant 4) and stays inside the overhead budgets named in `README.md` (Invariant 2).

| | |
|---|---|
| Language / runtime | C# on .NET 8 (tModLoader 1.4.4 is pinned to .NET 8) |
| Build | `dotnet msbuild` from the mod folder, or tModLoader's in-game Develop Mods → Build + Reload |
| Tests | `dotnet test Tests/PerformanceProfiler.Tests.csproj` (xUnit, 10/10 passing as of `14fac59`) |
| Production source | ~11,000 lines of C# across `Profiling/` (45 files) and `UI/` (17 files) |
| Test source | ~235 lines of pure-logic test code in `Tests/` (3 fixtures) |

## Repository Structure

```text
PerformanceProfiler/
├── PerformanceProfiler.cs                  Mod entry point: Mod.Load + ILHook teardown
├── PerformanceProfiler.csproj              Main mod project (excludes Tests/** from compile)
├── build.txt                               tModLoader manifest; buildIgnore=Tests/*
├── description.txt                         Workshop description
├── Localization/en-US_Mods.PerformanceProfiler.hjson
├── README.md / CLAUDE.md / AGENTS.md / LICENSE
│
├── Profiling/                              Measurement engine
│   ├── HookInterceptor.cs                  Delegate-pair backend: MonoModHooks.Add per signature
│   ├── ILHookInterceptor.cs                IL backend: per-method ILHook + ProbeStack
│   ├── HookCategoryRouter.cs               Shared type→category map (both backends)
│   ├── HookCoverageView.cs                 Backend-aware coverage counters (single source of truth)
│   ├── HookBackend.cs                      Mode + AllocationTracking flags (Delegate/ILHook/Parallel)
│   ├── ProbeStack.cs                       Static Enter/Leave called from emitted IL
│   ├── MetricCollector.cs                  Per-tick frame engine; owns the ring buffer + spike detector
│   ├── PerModAttribution.cs                Per-(mod, category, hookId) cost + alloc accumulators
│   ├── PerModSample.cs                     Compact per-tick struct (sums + alloc bytes per mod)
│   ├── PerTickAttributionRing.cs           50-window raw per-tick ring for spike attribution
│   ├── RingBuffer.cs                       Generic fixed-capacity circular buffer (TickFrame[1800])
│   ├── SpikeDetector.cs                    Median + MAD spike detection with FlushSpikes()
│   ├── TickFrame.cs                        One per-tick observation (CPU, alloc, counts, context)
│   ├── ModImpactScorer.cs                  Composite Overview-tab ranking
│   ├── ProfilerSystem.cs                   ModSystem lifecycle glue + SessionLogFailureException catch
│   ├── SessionLogWriter.cs                 Atomic JSON writer (schema v4 with insights block)
│   ├── Events/                             Game-state snapshotters
│   │   ├── ContextTagger.cs                Per-tick biome/boss/weather/invasion snapshot
│   │   ├── BiomeRegistry.cs                Vanilla + modded biome enumeration via reflection
│   │   ├── BossSampler.cs                  Active boss identity + segmented-boss dedup
│   │   ├── EventAggregator.cs              Per-dimension bucket stats for the Events tab
│   │   ├── SubworldProbe.cs                Optional SubworldLibrary reflection probe
│   │   ├── WeatherSources.cs / WeatherFlags.cs / GameMode.cs / InvasionId.cs
│   │   ├── BiomeBitset.cs / BiomeDescriptor.cs / BossSlotArray.cs / BucketStats.cs / EventContext.cs
│   └── Insights/                           Heuristic insight engine
│       ├── InsightsEngine.cs               Shared singleton; detector roster + Evaluate pass
│       ├── InsightStore.cs                 Live + history store, p-value-gated promotion
│       ├── InsightRecord.cs                EvidenceScope + Confidence + Audience enums + record
│       ├── RankingScorer.cs                Pattern-aware magnitude normaliser
│       ├── InsightRenderer.cs              Template strings per pattern
│       ├── IInsightDetector.cs             Detector interface
│       └── Detectors/                      Four live + six gated detectors
│
├── UI/                                     Overlay (player surface)
│   ├── ProfilerOverlay.cs                  Trimmed shell; tab work moved into UI/Overlay/
│   ├── ProfilerOverlaySystem.cs            Layer mount + F9 keybind + UpdateUI pump
│   ├── ProfilerTheme.cs                    Colour palette + scale knobs
│   └── Overlay/
│       ├── IOverlayTab.cs                  Tab contract incl. IsAvailable
│       ├── TabRegistry.cs                  Static tab list + Visible()/ResolveActive() clamp
│       ├── OverlayPanel.cs                 Chrome: header, tab strip, PROFILER HEALTH bar
│       ├── OverlayDraw.cs / OverlayLayout.cs / OverlayState.cs
│       └── Tabs/
│           ├── OverviewTab.cs              Composite-impact leaderboard (default landing tab)
│           ├── TreeTab.cs                  Foldable mod → category → hook tree
│           ├── SpikesTab.cs                Spike windows + median + per-mod attribution
│           ├── EventsTab.cs                Per-dimension event buckets
│           └── InsightsTab.cs              Ranked insight cards + gated detector list
│
├── Tests/                                  Non-shipping xUnit harness (excluded from .tmod)
│   ├── PerformanceProfiler.Tests.csproj    Net8.0, links pure-logic files via Compile Include
│   ├── RankingScorerTests.cs               Pin: share vs ratio pattern magnitude
│   ├── InsightStoreTests.cs                Pin: PValueAdjusted promotion gate + dedup
│   └── RingBufferTests.cs                  Pin: wrap-around semantics
│
└── context/                                This folder (implementation memory)
    ├── _Overview.md / architecture.md / notes.md / _staleness-report.md
    ├── integration/                        Cross-cutting maps
    ├── tmodloader/                         Per-API reference + "how we plug in"
    ├── systems/                            Per-subsystem deep dives (this layer)
    ├── notes/                              Topical inbox (decisions, conventions, future work)
    └── plans/code-health-audit/            2026-05-20 audit + implementation receipt
```

## Subsystem Responsibilities

| # | Subsystem | Canonical home | Owns |
|---|-----------|----------------|------|
| 1 | Mod lifecycle | `systems/mod-lifecycle.md` | `Mod.Load` / `Mod.Unload` / `ModSystem.OnWorldLoad` orchestration; backend selection; ILHook teardown |
| 2 | Hook instrumentation | `systems/hook-instrumentation.md` | Delegate-pair detours, IL detours, shared category routing, coverage tri-state, abort-clean install |
| 3 | Metric collection | `systems/metric-collection.md` | Per-tick frame engine, ring buffer, per-mod attribution, frame-time accounting |
| 4 | Spike detection | `systems/spike-detection.md` | Median/MAD spike windows, per-tick attribution ring, flush-on-unload |
| 5 | Allocation tracking | `systems/allocation-tracking.md` | `EnterCpuAlloc/LeaveCpuAlloc` IL emission, per-mod alloc columns, MEM/BOTH overlay pill |
| 6 | Events and context | `systems/events-and-context.md` | Biome/boss/weather/invasion snapshotting, per-dimension bucket aggregation |
| 7 | Insights engine | `systems/insights-engine.md` | Detector roster, store with TTL + p-value-gated confidence, ranking, gated stub map |
| 8 | Session logging | `systems/session-logging.md` | Schema v4 JSON writer, atomic temp-file+replace, self-disable on IO failure, prune of incompatible logs |
| 9 | Overlay | `systems/overlay.md` | Tab registry, chrome, five concrete tabs, input dispatch through visible-list indices |
| 10 | Test harness | `systems/test-harness.md` | Non-shipping xUnit project, pure-logic file linking, exclusion from `.tmod` |

The eleven entries above plus the per-API plug-in slices in `tmodloader/` cover every meaningful piece of the repository. Anything not named here is a small helper inside one of these subsystems.

## Dependency Direction

```
                       ┌──────────────────────────┐
                       │ PerformanceProfiler (Mod)│
                       │  Load → Logger.Info      │
                       │  Unload → ILHookIntcptr  │
                       └────────────┬─────────────┘
                                    │
                       ┌────────────▼─────────────┐
                       │ ProfilerSystem (ModSystem)│
                       │  PostSetupContent install│
                       │  OnWorldLoad / Unload    │
                       │  PreUpdateEntities/Post… │
                       └─────┬──────────────┬─────┘
                             │              │
              ┌──────────────▼──┐   ┌───────▼──────────┐
              │ HookInterceptor │   │ ILHookInterceptor│
              │  delegate path  │   │  IL path          │
              └────┬────────────┘   └─────────┬─────────┘
                   │      shared              │
                   │  HookCategoryRouter      │
                   │  PerModAttribution       │
                   ▼                          ▼
              ┌─────────────────────────────────┐
              │   MetricCollector / RingBuffer  │
              │   PerTickAttributionRing        │
              │   SpikeDetector                 │
              └──────────────┬──────────────────┘
                             │
                ┌────────────┼─────────────┐
                ▼            ▼             ▼
       ┌─────────────┐ ┌───────────┐ ┌───────────────┐
       │ Overlay     │ │ Insights  │ │ SessionLog    │
       │ (player UI) │ │ Engine    │ │ Writer        │
       │ five tabs   │ │ Shared    │ │ schema v4     │
       └──────┬──────┘ └──────┬────┘ └───────┬───────┘
              │               │              │
              └───────────────┴──────────────┘
              Both player + agent surfaces consume the
              same InsightsEngine.Shared instance.
```

The arrows are unidirectional. The hot path stays inside the measurement layer (Hook + Metric); presentation layers (Overlay, SessionLogWriter) only read from it. The Insights engine reads from `MetricCollector` and produces `InsightRecord`s that the Overlay and SessionLogWriter both consume from the same shared store.

## Core Execution / Data Flow

A single hook timing observation, end-to-end (the dependency chain trace referenced in `notes/decisions.md`):

1. `Main.Update` advances one tick. `ModSystem.PreUpdateEntities` fires on the profiler's `ProfilerSystem` (`Profiling/ProfilerSystem.cs:155`).
2. `Collector.BeginTick()` opens a frame, reads the entry alloc-bytes counter via `GC.GetAllocatedBytesForCurrentThread()`, stamps `_tickOpen = true`.
3. tModLoader's `*Loader.HookList<T>` iterates each profiled mod's hook override. Each iteration enters a method patched by one of the two backends:
   - **Delegate path:** the wrapper delegate stored by `MonoModHooks.Add` runs first. `HookProbe.Time*` reads `Stopwatch.GetTimestamp()`, calls `orig(self, args)` inside a `try/finally`, credits the elapsed ticks via `PerModAttribution.Add(modId, categoryId, hookId, deltaTicks)`. The `try/finally` (never `try/catch`) means a mod-thrown exception bubbles unchanged; only the time up to the throw is credited.
   - **IL path:** the manipulator-injected `ProbeStack.Enter(hookId)` prologue runs first. Body runs inside the finally-protected region. Every original `ret` is rewritten to `stloc retLocal; leave end`. `ProbeStack.Leave()` runs as the finally; it computes the elapsed ticks and credits `PerModAttribution.Add(modId, categoryId, hookId, deltaTicks)`.
4. After every hook in the tick has fired, `ModSystem.PostUpdateEverything` (`Profiling/ProfilerSystem.cs:165`) calls `Collector.EndTick(tickIndex, npcCount, projectileCount, dustCount)`.
5. `EndTick` reads the exit alloc-bytes counter, computes the per-mod alloc bytes from `PerModAttribution`, assembles a `TickFrame`, pushes it into the ring buffer, runs the spike detector against it.
6. If a `SessionLogWriter` is alive, `_sessionLog.Tick(collector)` advances the timeline (one row per 60×60 = 3600 ticks) and writes the report atomically via `File.Replace`. A failure throws `SessionLogFailureException`; `ProfilerSystem` catches it, logs to `client.log`, drops the writer reference for the rest of the world.
7. If a `ContextTagger` is alive, `tagger.Snapshot(tickIndex)` stamps the new `TickFrame.Context` and feeds `EventAggregator.Accumulate(in tagger.Current, frameMs)`.
8. The Overlay reads from `collector.History` and `InsightsEngine.Shared` on its next `UpdateUI` tick (driven by `ProfilerOverlaySystem`).

The hot path is steps 3–5. Both backends keep it zero-allocation (no boxing, pre-allocated structs, `Stopwatch.GetTimestamp()` static reads). Step 6's atomic write is **not** per-tick — the timeline cadence is 60 seconds, so the IO cost amortises away. Step 8 runs at 60 Hz but reads only.

## Inter-System Relationships

The eight relationships below are the ones a reader needs to know to navigate. The full per-component map lives in `integration/integration-map.md`.

| A | B | Mechanism | What breaks if the connection fails |
|---|---|-----------|--------------------------------------|
| `HookInterceptor` | `HookCategoryRouter` | static method call (`ResolveCategory`) | Delegate path would lose category attribution; tree tab's per-category fold breaks |
| `ILHookInterceptor` | `HookCategoryRouter` | static method call | IL path loses category; the two backends would disagree on bucket assignment and divergence logs would mislead |
| `{HookInterceptor, ILHookInterceptor}` | `HookCoverageView` | reads `MeasuredHookCounts` / `TotalHookCounts` per backend depending on `HookBackend.Mode` | Overlay PROFILER HEALTH / TreeTab badge / SessionLogWriter `coverage` block all diverge — the failure the audit explicitly named |
| `ILHookInterceptor` | `ProbeStack` | IL-emitted `call` instructions | Without ProbeStack reachable from the patched assembly, every wrapped method throws on first call → instrumentation crashes the game (Invariant 4 mitigation: `Mod.Unload` must `ILHookInterceptor.Uninstall()` before our assembly unloads) |
| `InsightsEngine.Shared` | `InsightsTab` + `SessionLogWriter` | static singleton field; both call `GetOrCreateShared()` | Live records would diverge between player surface and agent surface; the audit's potential-issue #6 |
| `RankingScorer` | `InsightStore.TopInto` | comparer-captured method call inside the sort closure | Magnitude collapse to zero for share patterns (pre-fix) made 40% and 90% contributors rank identically |
| `ProfilerSystem` | `SessionLogWriter` | wrapped Create/Tick/End calls + `SessionLogFailureException` catch | Without the catch wrappers, a permissions/IO error in the session log would have taken the world's profiler down |
| `TabRegistry.Visible` | `IOverlayTab.IsAvailable` | iteration + collector probe per chrome paint | Without `Visible`, `IsAvailable` was the docstring contract the chrome ignored; tabs that should hide were still drawn and dispatched |

## State Ownership

| State | Owner | Visibility | Lifecycle |
|-------|-------|------------|-----------|
| `ProfiledMods` / `ProfiledModNames` / `ProfiledModVersions` | `HookInterceptor` (static) | public read | Populated at `PostSetupContent`, never cleared (process lifetime). The ILHook backend reads `ProfiledMods` to share the same modlist. |
| `_measuredHookCounts` / `_totalHookCounts` | each backend independently | internal | Populated at install, surfaced through `HookCoverageView` |
| `Collector` | `ProfilerSystem` instance | public read | One per world; allocated `OnWorldLoad`, nulled `OnWorldUnload` |
| `InsightsEngine.Shared` | static field, lazy `GetOrCreateShared()` | public | One per session; explicitly cleared by `ProfilerSystem.OnWorldUnload` so the next world starts clean |
| `_installedHooks` (ILHook list) | `ILHookInterceptor` (static) | internal | Process-lifetime; disposed only via `Mod.Unload` → `Uninstall()` |
| `_sessionLog` | `ProfilerSystem` instance | private | One per world; nulled on disposal OR on `SessionLogFailureException` self-disable |
| Tab instances | `TabRegistry.Tabs` | internal static | Singletons across F9 toggles; the chrome resolves the active tab from `OverlayState.ActiveTabIndex` clamped against the visible list |

## Structural Notes / Current Reality

- **Two coexisting backends.** `HookInterceptor` (delegate-pair) and `ILHookInterceptor` (IL) both live in the code and both can run; `HookBackend.Mode` chooses which. As of the 2026-05-20 audit, ILHook is the default. `Parallel` mode runs both and logs divergence via `MetricCollector.BackendDivergence`; the player-visible numbers stay on the delegate side because that path is the proven baseline.
- **Coverage tri-state.** `HookInterceptor.TryHookSupportedOverride` returns one of `Installed` / `UnsupportedSignature` / `InstallFailed`. The histogram of unsupported-signature shapes tracks coverage debt; `InstallFailures` tracks MonoMod runtime errors separately. `HookCoverageVersion = 3` is folded into the session identity hash so old session JSONs measured under a narrower coverage set get pruned automatically.
- **Atomic session writes.** Every `SessionLogWriter.WriteReport` call goes through `AtomicWrite`: temp-file + `File.Replace` (with `File.Move` first-write fallback). A crash mid-write leaves either the previous complete report or the new complete report — never a truncated file. Pruning is narrowed to the writer-owned filename shape via `LooksLikeOurReport`.
- **Self-disable on IO failure.** `ProfilerSystem` wraps `SessionLogWriter.Create` / `Tick` / `End` in try/catch for `IOException` / `UnauthorizedAccessException` / `SecurityException`. The `Tick` path throws `SessionLogFailureException`; the system catches it, logs once, drops the reference. Metric collection continues regardless. Invariant 4: instrumentation may decline, never crash the game.
- **EvidenceScope is orthogonal to Confidence.** A record can be `Confidence.High` and still `EvidenceScope.ThisSession` if every observation came from the current world only. The InsightsTab renders both badges side by side so a reader can argue with either dimension independently.
- **Pattern-aware magnitude.** `RankingScorer.NormaliseMagnitude` splits the magnitude regime by `PatternKey`. Share patterns (`HotHookDominance`, `AllocationBurst`, `PeakContributorToSpike`) pass through `[0,1]` unchanged; ratio patterns keep the soft-knee 10× curve. Pre-fix every share got mapped to zero, erasing the strongest live signal.
- **Insights JSON parity.** `SessionLogWriter` schema v4 includes an `insights` block (`live`, `history`, `gated`) sourced from `InsightsEngine.Shared`. The player surface (InsightsTab) and the agent surface (session JSON) read the same store, so they cannot disagree about what fired.
- **Overlay tab availability is enforced.** `TabRegistry.Visible(collector)` computes the visible-tab subset via `IOverlayTab.IsAvailable`; click handler, draw, and dispatch all index against the visible list. Disabling a tab transparently shifts later tabs up.
- **Truncation caches.** `OverviewTab._truncatedNames` (keyed by `ModId`) and `InsightsTab._rankedBodies` (parallel to `_ranked`) are refilled at the 1 Hz Tick cadence. No per-frame `OverlayDraw.Truncate` allocations on those paths.
- **Non-shipping tests.** `Tests/PerformanceProfiler.Tests.csproj` lifts pure-logic source files in via `Compile Include + Link` (never `ProjectReference`, to keep tModLoader assemblies out of the runner). The main mod build excludes `Tests/**` via `<Compile Remove>`; `build.txt`'s `buildIgnore` excludes `Tests/*` from the `.tmod`. Run: `dotnet test Tests/PerformanceProfiler.Tests.csproj`.

## Coverage

What this upkeep run actually inspected vs noted vs inferred:

| Class | Files |
|-------|-------|
| **Directly inspected (read in full)** | `PerformanceProfiler.cs`, `Profiling/ProfilerSystem.cs`, `Profiling/HookInterceptor.cs`, `Profiling/ILHookInterceptor.cs`, `Profiling/HookCategoryRouter.cs`, `Profiling/HookCoverageView.cs`, `Profiling/Insights/InsightsEngine.cs`, `Profiling/Insights/InsightStore.cs`, `Profiling/Insights/RankingScorer.cs`, `Profiling/Insights/InsightRecord.cs` (first 80 lines), `UI/Overlay/IOverlayTab.cs`, `UI/Overlay/TabRegistry.cs`, the audit `index.md`, every existing `context/*.md` file. |
| **Inspected by grep / partial read** | `Profiling/SessionLogWriter.cs` (atomic-write + insights + prune sections), `Profiling/MetricCollector.cs` (BackendDivergence + FlushSpikes), `Profiling/Insights/Detectors/GatedDetectors.cs` (TODO markers), `Tests/PerformanceProfiler.Tests.csproj`. |
| **Inferred from file structure or commit messages** | The Events/ subsystem internals (BiomeRegistry, EventAggregator, BossSampler, SubworldProbe), the four live + six gated detectors (read names + roster only), the five tab implementations (read class signatures from the OverviewTab line counts, commit bodies). |
| **Not inspected** | Per-detector emission logic, `OverlayPanel`'s exact draw shape, `MetricCollector`'s spike-detector integration internals, `PerModAttribution`'s alloc-tracking column geometry, `ProfilerTheme`. |

Verification questions for the next session, where being wrong would mislead:

- Does the `Parallel` backend mode actually surface both backends' coverage counters somewhere, or only via the `[backend-compare]` log line? Inspect `MetricCollector.BackendTotalMs0/1` consumers.
- The InsightsTab caches `_rankedBodies` parallel to `_ranked` — confirm the parallel-array invariant holds under partial-refresh edge cases (cap reached, eviction during refresh).
- `ILHookInterceptor._instrumentedHandles` is cleared in `Uninstall()` but not on `Install()` re-entry; the guard `if (Installed) return;` prevents re-entry today. Confirm no path re-installs without uninstalling.
