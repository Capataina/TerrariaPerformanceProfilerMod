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

Every `internal` or `public` type carrying non-trivial behaviour has an XML doc summary on the type. Multi-paragraph summaries use `<para>...</para>` blocks (see `ILHookInterceptor`, `HookCoverageView`, `SessionLogWriter`). Method-level docs are added to anything called from another file or another assembly; private helpers stay terse. The convention is "doc what a caller needs to know about contract and invariants", not "doc every line".

## 13. The two `MethodImpl(...)` boundary

`Profiling/MetricCollector.cs` and the probe stack use plain methods; no `[MethodImpl(MethodImplOptions.AggressiveInlining)]` is used anywhere in the codebase. If a hot-path measurement ever shows inlining as a win, that decision goes through the Invariant-2 measurement gate (CLAUDE.md) and gets a comment with the measurement before being committed.

## 14. Audit category tags in commit messages

Commits that implement an audit finding use a prefix like `CHA round 1:` / `CHA round 2:` / `CHA round 3:` and group findings by audit file (`hook-instrumentation`, `overlay-ui`, `persistence-session-logging`, `insights-engine`, `build-and-tests`). The body lists every finding addressed in that commit. Future audit-driven work should keep this shape so the implementation receipt in `plans/code-health-audit/index.md` stays easy to maintain.
