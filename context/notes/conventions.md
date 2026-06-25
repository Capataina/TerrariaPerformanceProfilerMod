# Conventions

Repository-wide patterns and idioms that are **not enforced by tooling** but matter enough that a newcomer (human or agent) needs to know them. Surfaced from the 2026-05-20 upkeep convention-capture pass.

## 1. `#nullable enable` at every file head

Every `.cs` file under `Profiling/` and `UI/` begins with `#nullable enable`. The project's `.csproj` does not set `<Nullable>enable</Nullable>` at the project level (it stays per-file). New files must include the directive; the compiler will not warn that it is missing.

## 2. Static-class backend state

Backends (`HookInterceptor`, `ILHookInterceptor`, `HookCoverageView`, `HookBackend`, `ProbeStack`) are `static class` with static fields. They are not `Mod`-derived or `ModSystem`-derived — they are process-scoped singletons accessed by class name. Lifecycle is driven by explicit `Install(Mod)` / `Uninstall()` calls from `ProfilerSystem.PostSetupContent` and `PerformanceProfiler.Unload`. Do not introduce instance state for these subsystems; the static shape is load-bearing for IL-emitted callers (`ProbeStack.Enter` is a `call`, not a `callvirt`).

## 3. `try/finally` (never `try/catch`) inside hook probes

Both `HookProbe.Time*` and the ILHook manipulator wrap the original method body in `try/finally`, never `try/catch`. A mod's thrown exception is the mod's own behaviour and is never swallowed (Invariant 1). The `finally` credits the time up to the throw and lets the exception propagate. Code review: any new probe variant that adds a `catch` to `Time*` is broken by definition.

## 4. `Mod.Logger` for lifecycle and install/teardown only

`Mod.Logger.Info` / `Warn` / `Error` is the agent-surface channel (writes to `client.log`). It is called at:

- Mod load (`PerformanceProfiler.Load`).
- After hook install (`HookInterceptor.Install`, `ILHookInterceptor.Install`).
- World load / unload boundaries (`ProfilerSystem.OnWorldLoad` / `OnWorldUnload`).
- One-shot warnings (sample hook install failure, session log self-disable, backend divergence).

It is **never** called from the per-tick hot path or any IL-emitted probe. The probe stack reads `Stopwatch.GetTimestamp()` and writes to pre-allocated arrays only. Logging inside a hook would be overhead the profiler is meant to measure, not add (Invariant 2).

## 5. `Stopwatch.GetTimestamp()` static reads, never `new Stopwatch()`

Every timing path uses `Stopwatch.GetTimestamp()` (a static `long` read) and computes deltas. `Stopwatch` is a class; `new Stopwatch()` would allocate per call. The convention is symmetric across the delegate path and the ILHook path. New per-tick timing must follow it.

## 6. Pre-allocated arrays + ModId indexing

`PerModSample[]`, `_measuredHookCounts`, `_totalHookCounts`, `_unsupportedHookSamples`, `ProfiledModNames`, `ProfiledModVersions` are all `T[]` allocated once at install and indexed by `modId`. No `List<T>` on the hot path; `List` would resize. The convention is that any per-mod accumulator is a fixed-size array sized at `PostSetupContent` and never grown.

## 7. `RegisterOrReuseHook` between backends sharing identity

When both backends are alive (Parallel mode), they share the same `(modId, categoryId, hookId)` identity space via `PerModAttribution.RegisterOrReuseHook`. The ILHook path calls `RegisterOrReuseHook` to obtain a hookId; if the delegate path already registered the same `(modId, categoryId, displayName)`, the same id is returned. Without this, the two backends would write to different rows and divergence comparisons would be meaningless.

## 8. Per-frame caching at the 1 Hz Tick cadence

UI tabs cache derived state in `Tick` and read from the cache in `Draw`. The cache cadence is 1 Hz (every 60th `Tick` invocation), matching the 30-second smoothed accessors the data is sourced from. Examples: `OverviewTab._truncatedNames`, `InsightsTab._rankedBodies`, `EventsTab._cachedNowSummary`. New tabs should follow this pattern; per-frame allocation in `Draw` is the failure mode the audit's overlay findings landed on.

## 9. `IsAvailable` and `Visible(collector)` for tab availability

A tab whose data dependency is not satisfied (`SpikesTab` when there is no spike history, `EventsTab` before any context has accumulated) returns `IsAvailable(collector) = false`. `TabRegistry.Visible(collector)` filters the visible list; click handlers, draw, and the active-tab resolver all index against that visible list. Disabling a tab transparently shifts later tabs up. New tabs must implement `IsAvailable` honestly — defaulting to `true` re-introduces the audit-flagged contract violation.

## 10. ModSystem vs static class for lifecycle

Lifecycle work (per-world allocation, per-tick driving, per-world teardown) lives on `ProfilerSystem : ModSystem`. Subsystem implementation lives in `static class` files. The split is: `ModSystem` is the seam tModLoader calls; `static class` is the implementation that seam drives. Do not add per-tick `ModSystem` subclasses for new subsystems unless they need an independent lifecycle.

## 11. `using` directives at the top, namespace-block style

Every file uses `namespace PerformanceProfiler.X.Y;` (file-scoped, C# 10+ style), not `namespace X.Y { … }` brace-blocks. New files must match.

## 12. XML doc comments with `<para>` blocks for non-trivial types

Every `internal` or `public` type carrying non-trivial behaviour has an XML doc summary on the type. Multi-paragraph summaries use `<para>...</para>` blocks (see `ILHookInterceptor`, `HookCoverageView`, `DataRegistry`). Method-level docs are added to anything called from another file or another assembly; private helpers stay terse. The convention is "doc what a caller needs to know about contract and invariants", not "doc every line".

## 13. The `MethodImpl(AggressiveInlining)` boundary

`[MethodImpl(MethodImplOptions.AggressiveInlining)]` IS applied on the per-tick probe entry/exit path (`ProbeStack.Enter/Leave/EnterCpuAlloc/LeaveCpuAlloc` and `Data/Aggregators/PerModAttribution.Add`), added in v0.6 Phase β so those tiny hot-path calls fold into their call sites. Everywhere else uses plain methods. The convention going forward: new hot-path inlining goes through the Invariant-2 measurement gate (CLAUDE.md) and gets a comment with the measurement before being committed, and the existing annotations are not added to or removed without measuring.

## 14. Audit category tags in commit messages

Commits that implement an audit finding use a prefix like `CHA round 1:` / `CHA round 2:` / `CHA round 3:` and group findings by audit file (`hook-instrumentation`, `overlay-ui`, `persistence-session-logging`, `insights-engine`, `build-and-tests`). The body lists every finding addressed in that commit. Future audit-driven work should keep this shape so the implementation receipt in `plans/code-health-audit/index.md` stays easy to maintain.

## 15. Numbers live in `Data/`; consumers ask the registry

The single load-bearing rule of the unified data pipeline: if a piece of code *produces* a number it lives under `Data/` (a collector, aggregator, stat, or detector); if it *consumes* a number it asks the registry via `DataRegistry.Shared.Lookup<TSnapshot>(name)?.CurrentSnapshot()`. Routers, exporters, and dashboard JS must never derive a number themselves — the KPI cards used to be computed in JavaScript and drifted from the C# values; `BuildSpikes` and every other `DashboardRouter` builder now only reshapes a looked-up snapshot into JSON. A computation that appears in a consumer is the failure this convention prevents.

## 16. Streams are looked up by stable string name, never class reference

Every `IDataStream` is registered once in `PerformanceProfiler.RegisterDataPipeline` under a stable string `Name` (`"kpi"`, the `RolloutStreamNames` constants), and every consumer resolves it through `DataRegistry.Shared.Lookup<TSnapshot>(name)`, never by holding a class reference. The name is the contract; renaming a registered stream's `Name` silently breaks every consumer that looks it up, since the registry returns `null` rather than a compile error. New streams expose their name as a `const` (e.g. `KpiStat.StreamName`) so consumers cite the constant rather than a literal.

## 17. The Snapshot + Stat + Calculator triad for derived facts

Every new derived "fact about the session" is built as three pieces: an immutable `XxxSnapshot` value (struct or readonly record), an `XxxStat` that implements `IDataStream<XxxSnapshot>` and is registered by name, and a pure-logic `XxxCalculator` whose static method computes the snapshot from inputs. `KpiSnapshot` + `KpiStat` + `KpiCalculator` is the reference template. The split keeps attribution maths testable without a running game (Calculator), the registry contract uniform (Stat), and the wire shape immutable (Snapshot); a stat that computes inline instead of delegating to a calculator breaks the testability half.

## 18. Snapshots are fresh and immutable; nobody caches or frees them

`CurrentSnapshot()` returns a fresh immutable value built at call time; collection fields are `IReadOnlyList`/`IReadOnlyDictionary` copies, never live mutable references into producer state. Producers do not cache the snapshot they hand out and consumers do not own or free it — it is a value, garbage-collected when the consumer is done. Every snapshot also exposes an `Empty` default for the no-world-loaded path. This is what lets the router read a snapshot on a request thread while the producer mutates its own state on the game thread without a lock.

## 19. Snapshot contracts frozen in one file for parallel builds

When a feature wave is built across parallel agents, every snapshot type and every stream-name constant is frozen first in `Data/Contracts/RolloutContracts.cs` before any implementation exists. Downstream code (data layer, router, UI) then compiles against types whose producers are still being written, so independent agents never block on each other's in-progress work. The contracts file is the single source of truth for those shapes; changing a frozen contract mid-wave breaks every agent already compiling against it, so contract edits are a Wave-0 act, not a mid-build one.

## 20. Per-section partial-class asset bundles

The dashboard's CSS, JS, and HTML are each split into per-section partial classes (`Css.Palette.cs`, `Js.Polling.cs`, `IndexHtml.Summary.cs`), each contributing one `private const string` fragment that `DashboardAssets.Css`/`Js` concatenates in a stable, order-sensitive sequence into a bundle cached once at type-init. `DashboardRouter` is likewise split into per-tab partials (`DashboardRouter.Lag.cs`, `.Insights.cs`). A new dashboard section adds a new partial and one line to the concatenation list; it never bloats a monolith. The concatenation order is load-bearing for CSS cascade and JS declaration order, so new fragments slot into the right position, not the end.

## 21. The shared chart-component contract (`Js.Charts`)

Every chart on the dashboard is a pure function `fn(o)` in `Js.Charts.cs` that takes an options object and returns an SVG/HTML string, built on a shared scale core (`niceScale`/`seriesPaths`) and geometry helpers (`_polar`/`_ring`/`_catmullSegs`). The vocabulary is `streamArea` (stacked smooth bands), `sankey` (left→right value-width ribbons), `waffle` (unit-square grid; returns cells, caller renders the key), `scatter` (x/y + optional bubble `r` + optional y=x `diag`), plus `lineChart`/`barChart`/`heatmapMatrix`/`gauge`/`donut`/`sparkline`. No pane hand-rolls SVG — the consistent idiom is that a datum carrying an `id` becomes a `data-mod` click target across `donut`/`scatter`/`legend`. A new chart is a new `fn(o)` in this module, never inline markup in a per-tab renderer.

## 22. Monochrome chrome, colourful data (OKLCH tokens)

The design language is shadcn-neutral over OKLCH: the chrome (surfaces, text, borders) is zero-chroma neutral grey plus one near-white accent (`--accent` == `--primary`), and the *only* colour on screen is the data-viz layer (`--perf-0..4` severity ramp, per-mod `MOD_COLORS`). Colour always *encodes* (a per-mod hue, a magnitude, a severity), never decorates. The rule, stated at `Css.Palette.cs`: "greying the chrome never greys the data." A decorative hue on a panel or a greyed data series is the failure this prevents.

## 23. Insights are relative to a reference frame, never absolute (Welford-online)

The insights engine's spine law (verbatim in `Insights/Contracts/IReferenceFrame.cs`): no insight is ever an absolute magnitude; every insight is the deviation of a signal from the comparable baseline for that signal, on this machine, expressed as an effect size. Baselines accumulate via Welford-online `RunningStat` (`Insights/Shared/Stats.cs`) — `Merge` (Chan's parallel) for cross-session fold, `Without` (reverse-merge) to recover an out-of-context complement; persistence round-trips the raw `(Count, Mean, M2)`. Multi-comparison detectors count `testsRun` in-loop and apply `pAdjusted = min(1, p·testsRun)` (Bonferroni) behind a candidate-gate (co-occurrence + Cohen's d ≥ 0.8). All detectors emit `Confidence.Preliminary`; the store's `PromoteConfidence` is the sole confidence authority. A detector that reports a raw absolute, or promotes on repetition rather than a p-value, breaks the honesty contract.

## 24. The honesty contract is enforced at the render call site

`Insights/InsightRenderer.cs` carries a hard header: slot-filling only, no LLM, with a banned vocabulary (`"caused by"`, `"must remove"`, `"core mod"`, `"removable"`, `"bad mod"`). Insight copy is assembled from templated slots, and the descriptive-never-prescriptive rule (Invariant 3) is enforced by inspection at this one call site rather than by a regex elsewhere. New rendered strings stay descriptive and declare their baseline; a verb of causation or a removal recommendation in a template is the failure this prevents.

## 25. Interpretation publishes through the registry, like data

The interpretation layer (`Insights/`) mirrors the data-pipeline discipline (§15) one level up: the I-series interpreted stats (`Insights/Publish/` — `ModObservatoryStat`, `DormantSurfaceStat`, `CrossCuttingSignalStat`, `EngagementCostScatterStat`, `ModInteractionAggregator`, `InsightsStat`) read foundation streams only via `DataRegistry.Shared.Lookup`, compute, and register back into the same registry under stable names with `Cadence = OnDemand`. So every consumer (dashboard router, persistence) reads interpretation the one way it reads data. An interpreted number derived inside a router instead of an `Insights/Publish` stat is the failure this prevents.

## 26. The L4/L8 testing split + DOM-discovery genericisation

The off-game dashboard audit harness (`tools/testing/`) splits by a hard rule: if a property can be a deterministic assertion it is **L4** (Playwright layout/interaction invariants), and if it needs a human-style eye it is **L8** (the agent-driven vision audit). Both are genericised — nothing hardcodes the dashboard's shape: tabs are discovered from `.tab[data-tab]`, panes from `.tab-pane[data-pane]`, panels from `.panel`, endpoints by regexing `/api/<name>` out of the extracted JS, poll fns from `window` keys matching `/^poll/`. A seventh tab is audited with zero harness change. The harness reuses `tools/preview/render.py`'s verbatim-string extractor so the tested dashboard is byte-identical to the preview — a second C#-string parser would be a second source of truth and the failure this avoids.
