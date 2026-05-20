# Decisions

Resolved decisions from working sessions, newest first. Project-internal record; the README is the directional summary.

## 2026-05-20 — Post-audit implementation pass

Three commits (`77a99d2`, `aa914ce`, `14fac59`) landed the audit's certain findings and all six potential issues.

**Backend coverage is tri-state.** `HookInterceptor.TryHookSupportedOverride` now returns `Installed` / `UnsupportedSignature` / `InstallFailed`. Install failures (MonoMod runtime errors) are counted separately from coverage debt (signatures the interceptor doesn't have a delegate pair for yet). The unsupported-signature histogram stays clean of install errors. `HookCoverageVersion` bumped to 3 so old session JSONs measured under the previous accounting prune automatically.

**`HookCategoryRouter` is the single category map.** Both backends (`HookInterceptor`, `ILHookInterceptor`) share `ResolveCategory(Type) → categoryId`. A future category addition has one edit site, not two. `HookCoverageView` is the single source of truth for which counters the active backend writes to; the overlay PROFILER HEALTH strip, the TREE tab badge, and the SessionLogWriter `coverage` block all route through it. Fixes the audit-flagged "overlay says 100% / JSON says 0/X" divergence.

**Atomic session log writes.** `SessionLogWriter.AtomicWrite` writes to a temp file and uses `File.Replace` (with `File.Move` first-write fallback). A crash mid-write leaves either the previous complete report or the new one, never a truncated file. Prune narrowed to writer-owned filename shape via `LooksLikeOurReport` so hand-saved JSON in the same directory survives.

**Session log self-disables on IO failure.** `ProfilerSystem` wraps `SessionLogWriter.Create` / `Tick` / `End` in `try/catch` for `IOException` / `UnauthorizedAccessException` / `SecurityException`. `Tick` throws `SessionLogFailureException`; the system catches it, logs once via `Mod.Logger.Warn`, drops the reference. Metric collection continues regardless. Invariant 4: instrumentation may decline, never crash the game.

**ILHook install is abort-clean across methods.** `ILHookInterceptor.Install`'s outer catch now calls `Uninstall()` to dispose hooks that landed before the failure. Without it, tModLoader would unload our assembly while patched IL still calls into `ProbeStack` and the next tick blows up.

**Spike-window flush at world unload.** `MetricCollector.FlushSpikes()` is called in `ProfilerSystem.OnWorldUnload` before the final session report write. An in-progress spike that coincided with the world exit lands in the JSON instead of being lost in the detector's scratch.

**Insight ranking is pattern-aware.** `RankingScorer.NormaliseMagnitude` now splits by `PatternKey`. Share patterns (`HotHookDominance`, `AllocationBurst`, `PeakContributorToSpike`) store fractions in `[0,1]` and pass through unchanged; ratio patterns keep the soft-knee 10× curve. Pre-fix every share collapsed to magnitude zero, so a 40% contributor and a 90% contributor ranked identically.

**Confidence promotion requires statistical evidence.** `InsightStore.PromoteConfidence` gates Medium on `PValueAdjusted <= 0.10` (alongside `ConfirmationCount >= 3`). A record with `PValueAdjusted = 1` (detector explicitly declares "no hypothesis test ran") can never reach Medium by repetition alone. The honesty contract requires badges to be defensible independently of how often the same untested observation re-fires.

**`EvidenceScope` is orthogonal to `Confidence`.** New enum on `InsightRecord` with values `ThisSession` / `LifetimeData` / `NeedsPersistence`, rendered as a second badge alongside `Confidence` in the InsightsTab. A record can be statistically tight within a single session and still weaker than lifetime data accumulated across sessions — the UI now makes that distinction visible. `FreeRemovalCandidateDetector` sets `Scope = NeedsPersistence`.

**Dual-surface insights JSON.** `SessionLogWriter` schema bumped to v4 with an `insights` block (`live`, `history`, `gated`). The InsightsTab and the session JSON both read the same `InsightsEngine.Shared` instance (lazy `GetOrCreateShared()`), so the two surfaces cannot disagree about what fired. `ProfilerSystem.OnWorldUnload` clears `InsightsEngine.Shared` so the next session starts with an empty store.

**Overlay tab availability is enforced.** `TabRegistry.Visible(collector)` computes the visible-tab subset via `IOverlayTab.IsAvailable`. The click handler, the tab-strip drawer, and the active-tab resolver all index against the visible list. Disabling a tab transparently shifts later tabs up and a previously-active-but-now-hidden tab falls back to index 0.

**Truncation caches.** `OverviewTab._truncatedNames` (keyed by `ModId`) and `InsightsTab._rankedBodies` (parallel to `_ranked`) are refilled at the 1 Hz Tick cadence. No more per-frame `OverlayDraw.Truncate` allocations on those paths.

**Non-shipping test harness.** New `Tests/PerformanceProfiler.Tests.csproj` (xUnit, net8.0). Pure-logic source files (`RingBuffer`, `InsightRecord`, `InsightStore`, `RankingScorer`) lift in via `Compile Include + Link` — never a `ProjectReference` to the mod project, so tModLoader assemblies stay out of the runner. The main mod build excludes `Tests/**` via `<Compile Remove>`; `build.txt`'s `buildIgnore` excludes `Tests/*`. Three fixtures pin the audit findings (`RankingScorerTests`, `InsightStoreTests`, `RingBufferTests`). 10/10 passing in 16 ms. Run: `dotnet test Tests/PerformanceProfiler.Tests.csproj`.

**Deferred for a follow-up commit.** Splitting `SessionLogWriter` into `SessionReportBuilder` (pure) + IO wrapper, plus the schema snapshot test, were deliberately not done in this pass. Pure refactor with zero behaviour change; best landed once the test harness can safety-net the relocation, which now exists. See `plans/code-health-audit/index.md`'s implementation receipt.

## 2026-05-19 — Milestone 1 + 2 build session

**Repository published.** The project is now a public GitHub repo, `Capataina/TerrariaPerformanceProfilerMod` (MIT licence), and is listed in the profile README under Active Projects.

**Milestone 0 dropped.** The feasibility-spike phase is removed. Every M0 spike was premised on a 94-mod stack the dev machine cannot load, so the spikes had nothing to measure. Milestones now run from M1, and overhead is validated on whatever modlist the dev machine can actually run.

**API-first, clone-on-wall.** Build on tModLoader's public API first; when a genuine wall is hit, read the tModLoader source from GitHub (via `gh`) rather than guessing. The wall was reached at per-mod attribution — the source was read and the approach confirmed against it.

**Per-mod attribution uses MonoMod On-hooks, never IL edits.** *Confirmed in the source: `ModLoader.Mods` is public, `MonoModHooks.Add` On-hooks auto-remove on mod unload, and every `Mod*`/`Global*` instance carries its owning `Mod`. An On-hook wraps a method and cannot corrupt it, so a fault is wrong numbers, never a crash (Invariants 1 and 4). IL editing (`MonoModHooks.Modify`) is reserved for cases On-hooks cannot reach.* **Superseded 2026-05-20:** the ILHook backend (`ILHookInterceptor`) is now the default. The original safety reasoning still holds — `Mod.Unload` explicitly disposes every installed `ILHook` so the IL patches do not outlive our assembly. IL editing is no longer "reserved"; it is the production path because it lifts coverage from ~71.6% to ~100%.

**Attribution is split by hook category.** Cost is accumulated per mod and per category (Systems / Players / NPCs / Projectiles / Items / World / Buffs). The overlay tree folds a mod row open into a per-category breakdown. The seven categories are constants in `HookCategoryRouter`.

**First-cut hook scope: parameterless instance hooks only.** *The interceptor hooks the void-signature per-tick hooks (`ModSystem`/`ModPlayer` update hooks, `ModNPC`/`ModProjectile` AI) — one delegate shape, lowest risk. The per-entity `GlobalNPC`/`GlobalProjectile` hooks, which carry a parameter, are a planned follow-up to widen coverage.* **Superseded 2026-05-20:** the delegate path now wraps roughly 30 distinct signature families (see `Profiling/HookInterceptor.cs:282-790`), and the ILHook backend wraps every override regardless of signature.
