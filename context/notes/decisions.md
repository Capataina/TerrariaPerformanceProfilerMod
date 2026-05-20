# Decisions

Resolved decisions from working sessions, newest first. Project-internal record; the README is the directional summary.

## 2026-05-20 — Context-transition dynamic-vs-hardcoded audit

Caner asked whether ContextTransitionWatcher adapts to modded content or whether new mods need code changes here. Audit result:

**Dynamic (works automatically when a mod adds content):** biomes (BiomeRegistry enumerates ModContent.GetContent<ModBiome>() at PostSetupContent; bitset grows; watcher loops all bits), bosses (BossSampler reads Main.npc[] + NPCID.Sets.ShouldBeCountedAsBoss, Lang.GetNPCNameValue gives modded NPC names), sub-worlds (SubworldProbe reflects SubworldLibrary.Current), hardmode (Main.hardMode bool), time-of-day (Main.dayTime bool), player deaths (Main.LocalPlayer.dead regardless of damage source), world snapshots (live reads of position / statLife / mana / entity counts). The dimensions a player actually walks through and interacts with all pick up modded content with no code change.

**Vanilla-only — tML 1.4.4 platform gap, not our hardcoding:** weather flags (no ModWeather API to enumerate modded weather events; existing comment in WeatherSources.cs already documents this), invasions (no ModInvasion API; InvasionId enum mirrors vanilla Terraria.ID.InvasionID + DD2Event), game mode (no API to register new difficulties at Main.GameModeInfo level).

**Decision:** wait for tML 1.4.5+ to ship those APIs rather than build a reflection-based workaround. Caner's framing: if an API doesn't exist in tML, almost nobody is adding that content type anyway, so the gap is narrow in practice. Revisit when a tML release notes promise ModWeather / ModInvasion / mod difficulty support.

When that ships, the changes are bounded: WeatherSources gains modded-flag enumeration, InvasionId becomes a string keyed off ModInvasion identity, GameMode enum becomes a string. The ContextTransitionWatcher's diff logic doesn't change — it's already shaped to iterate whatever flag/id space the data layer provides.

## 2026-05-20 — Tracker arsenal + v0.4 (evening session)

Triggered by a real-world playtest where Caner's session lagged dramatically when he opened CheatSheet's NPC-spawn menu, but my profiler couldn't narrate what happened — I had to write an external C# inspector script to query the DB by hand, and even then misread the data twice before he corrected me ("it wasn't the BindingOfRarria spike, it was the spawn-menu cluster; it wasn't EoC dying, it was EoC killing me"). The lesson: data sitting in the DB without a narrator is data the user has to interpret manually, which defeats the entire point of the database. v0.4 fixes that.

**Per-stall mod attribution.** `StallEvent` now carries top-5 contributors captured at stall time from the rolling 30s smoothed per-mod CPU. The CheatSheet stall cluster will now say "dominant contributor: CheatSheet" instead of leaving the user to figure out which mod caused the freeze. `StallDetector.OnBeginTick` gained an overload taking `IReadOnlyList<double>? perModSmoothedMs`; `MetricCollector` passes the smoothed array through.

**Cluster-aware classifier.** New `StallCause.UiOverlayBlocking` covers sustained runs of medium-duration stalls (≥5 stalls in 5s, each <500ms) — the CheatSheet menu signature. The previous v0.3 classifier mislabelled every menu-stall as `ProcessSuspended` because the per-event signals (low CPU, no GC) are identical to a real cmd-tab. The cluster shape is the tell, and we now use it. New `StallCause.WorldLoad` for stalls during the world-load tick window. Priority chain documented in `ClassifyCause`: long+lone+cpu-starved → ProcessSuspended; GC-dominated → MajorGc/MinorGc; clustered+short → UiOverlayBlocking; short+lone+cpu-starved → ProcessSuspended (cmd-tab under 800ms); else → LongFrame.

**Stall clustering.** New `StallClusterRow` aggregates consecutive `StallEventRow`s into a single "what the player perceived as one freeze" record — span, total duration, worst-event duration, dominant cause, dominant contributor. The CheatSheet playtest produced 47 individual stall events but only 1 cluster; the cluster row tells the story in one line. Implementation: `SessionRecorder` tracks a live cluster, flushes when no new stall arrives within 2s of the last event or at session end. Dominant cause = most-frequent across events; dominant contributor = mod with highest cumulative `RecentMs` across the cluster's per-event snapshots.

**StallEventRow schema v2.** Persists the GC counts (Gen0/1/2), heap delta bytes, CPU time delta ms, severity, warming flag, cluster id, and top-5 contributors — all of which the StallDetector was already capturing but the writer was throwing away.

**Context transitions, full expansion.** The `ContextTransitionWatcher` now tracks: every biome bit (not just the primary), every weather flag bit individually, vanilla invasion id, game mode, hardmode, sub-world key, time-of-day (Day/Night), boss start/end with outcome classification, and a "session-open" anchor transition. v0.3's watcher emitted 4 transitions for a 5-minute session; v0.4 will emit dozens, enough to reconstruct "the player walked through Forest → Underground → Hallow, it rained for 2 minutes, then Blood Moon, then Eye of Cthulhu". Boss outcome reads `LocalPlayer.dead` at the boss-gone edge to classify killed-player vs boss-gone (defeated or fled).

**Player death events.** New `playerDeaths` collection with one row per false→true transition on `Main.LocalPlayer.dead`. Captures position (tile coords), HP at death, active boss NPC types, and a human-readable summary ("killed by Eye of Cthulhu in Forest at (3500, 240)"). `PlayerDeathDetector` runs on every tick from `ProfilerSystem`.

**Periodic world snapshots.** New `worldSnapshots` collection, one row every 30s of in-world time. Captures player position, HP/mana, primary biome, hardmode, game mode, time-of-day band, entity counts (npc/proj/dust/item), primary active boss. Cheap (~60 rows per session) but transforms "what was the player doing at minute 7" into a single point query. `WorldSnapshotter` runs from `ProfilerSystem.PostUpdateEverything`.

**Chat commands for in-game queries.** Five new `ModCommand`s so a player or agent can ask the DB questions without writing a C# inspector script:
- `/profiler-summary` — current/latest session totals
- `/profiler-stalls [N]` — top stall clusters by worst hitch
- `/profiler-mods [N]` — top mods by total CPU
- `/profiler-deaths` — deaths this session
- `/profiler-tail [N]` — interleaved recent events

`QueryCommandBase` resolves "current or latest session" via the live `ProfilerSystem.LiveRecorderSessionId` first, then falls back to the most recent `sessions` row.

**Auto-log significant events to client.log.** The motivating problem was that I read `client.log` first when debugging, and the log had no profiler-side narration — I had to query the DB. Now:
- Every stall ≥ 500ms writes an inline `Mod.Logger.Info` line with cause + top contributor.
- Every spike ≥ 100ms (non-warming) writes an inline line with top contributor.
- Every cluster-flush writes a one-liner summarising the run.
- Every headline context transition (boss start/end, hardmode flip, invasion, subworld, session-open) writes a line.
- Every player death writes a line.
- At `OnWorldUnload`, `SessionSummaryLogger.Write` emits a multi-line `=== profiler session-summary ===` block with session id, totals, worst spike, worst cluster, top 3 mods. A future log-only inspection can grep for this block and see the whole story.

`PerformanceProfiler.LoggerOrNull` static exposes `Mod.Logger` (`log4net.ILog`) to the persistence layer without a `Mod` singleton dependency.

**Persistence layer extension shape proven.** Adding three new collections (`stallClusters`, `playerDeaths`, `worldSnapshots`) required: one record file + one stream file + one `DbWriteOp` factory each. The `ProfilerDatabase` facade got 3 new typed accessor lines; the `StreamRegistry.Default()` got 3 new lines. No edit to writer thread, journal, dispatch logic, or any other stream. The Checkpoint-C modular shape did exactly what it was designed to do.

**Tests: 52/52 passing.** New `StallClassifierTests` cover lone-suspend, clustered UI-blocking, GC dominance, severity buckets, edge cases. Existing 51 still pass after the classifier reorder.

**Bumped build.txt 0.3 → 0.4.** Three new collections, five new chat commands, a re-architected classifier, inline log narration, and the cluster aggregation are clearly a minor bump.

## 2026-05-20 — LiteDB persistence + v0.3 (afternoon session)

The JSON-per-session writer (`SessionLogWriter.cs`, ~940 lines) is gone. The new persistence layer is a single LiteDB file plus a redo log plus three rotating backups, all under `Profiling/Persistence/`. Implementation followed `context/notes/litedb-migration-plan.md` end to end; every decision below is the plan's recommendation accepted.

**LiteDB 5.0.21, not 6.0 prerelease.** The 6.0 line has been in prerelease for a year; we cannot accept that risk on the persistence layer of a public mod. 5.0.21 is MIT, 100% managed, single 510 KB DLL, ships inside the `.tmod` via `dllReferences = LiteDB` and `lib/LiteDB.dll`. Re-evaluate when 6.0 ships stable.

**Modular stream registry, not a monolith.** The first cut had `ProfilerDatabase.ApplyOne` as a 14-case switch covering every `DbOpKind`; `ReconstructOp` duplicated the same fan-out for journal replay; `EnsureIndexes` listed every collection's indexes centrally. Adding a new tracked subsystem meant editing three places in one 600-line file. Refactored to `IPersistenceStream` + `StreamRegistry`: each logical collection group is a single file under `Streams/` declaring its own `Kinds[]`, `Apply`, `Reconstruct`, `EnsureIndexes`. Adding a new collection is now a single new file plus one registry line. `ProfilerDatabase` dropped from 605 lines to 431 and now owns only cross-cutting concerns (open + recovery, schema versioning, journal replay routing, backup rotation, compact, lifecycle).

**Single writer thread, lock-free producer queue.** `DbWriterThread` owns every LiteDB write. The game thread enqueues via `System.Threading.Channels.Channel.CreateUnbounded` (single-reader, multi-writer); enqueue cost measured at 276 ns/op. The writer batches up to 64 ops per LiteDB pass, runs an explicit `Checkpoint()` every 60s of activity (#1568 mitigation), and drains the queue on dispose. Invariant 2 (overhead budget) intact.

**Four-layer crash safety.** LiteDB built-in WAL (free) → `profiler.events.log` append-only NDJSON redo log → 3 rotating `profiler.litedb.bak-{1,2,3}` backups → quarantine-and-fresh if all backups unreadable. Recovery flow on every open: probe the main file read-only; promote the newest readable backup if it fails; replay any non-empty journal idempotently; mark orphan sessions as `crash-detected` without fabricating an `EndedUtc` (honesty contract — Invariant 3).

**One-shot legacy JSON ingestion.** `LegacyJsonImporter.RunOnceIfNeeded` walks `Sessions/*.json` once at startup, ingests `(identity, startedUtc, endedUtc, mode, modFingerprint)` into `SessionRow` rows, and moves each file to `ImportedLegacyJson/`. A `.imported` sentinel guards against re-runs.

**24h warm-tier TTL, lifetime cold + archive.** Per-tick raw never reaches disk (stays in the existing 30s RAM ring); 1Hz `tickAggregatesWarm` rows expire 24h after creation; 1/min `tickAggregatesCold` and 1/session `tickAggregatesArchive` rows are kept forever. Sweep runs at every open via a single indexed `DeleteMany(x => x.ExpireAtUtc < now)`.

**Per-stream idempotency on natural key.** Every stream's `Apply` upserts: warm aggregates on `(sessionId, secondIndex)`, cold on `(sessionId, minuteIndex)`, archive on `sessionId`, mod / hook aggregate batches use wipe-and-bulk on `sessionId`, identity upserts use the existing natural keys. Replay re-runs ops that already landed without duplicating rows.

**Compaction is manual, never silent.** `/profiler-compact` chat command runs `db.Checkpoint()` then `db.Rebuild()` (the LiteDB equivalent of SQLite VACUUM). Refuses to run inside a world because `Rebuild()` is not concurrency-safe with the writer thread's bursts.

**Performance characteristics (measured, debug build, M-series):** game-thread enqueue 276 ns/op, writer-thread sustained 310 ops/sec (above the 60/sec floor), 10-min Calamity-scale session DB 752 KB (well under the 5 MB §3.4 target), `FindTop10` by start time 0.4 ms across 50 sessions. The "let's see how well LiteDB performs" question has a number now.

**Two real bugs surfaced during test wiring:** `Pragma("USER_VERSION")` returns BsonValue wrapping Int32 (not Int64), so the original `(int)(long)` cast threw on every DB open in production; replaced with `.AsInt32`. `Channel.Reader.Count` is unsupported on the unbounded channel variant on .NET 8 / macOS; replaced with an `Interlocked`-tracked `_approxQueueDepth` counter. Without the test pass these would have shipped silently.

**44/44 xUnit tests passing.** Round-trip fixtures (open + reopen, session start/end, warm idempotency, crash detection, backup rotation, spike/stall row landing) plus benchmark fixtures (enqueue latency, drain throughput, file size, read latency). The benchmark fixtures are observability tests — they assert sanity floors but the real value is the numbers in `ITestOutputHelper`.

**Bumped build.txt 0.2 → 0.3.** A new schema version, a deleted writer, a new chat command, a benchmark surface; clearly a minor bump.

## 2026-05-20 — UI overhaul + v0.2

Eight commits landed the complete UI overhaul plus the SelfHealth cadence-guard bug fix and the v0.2 version bump.

**Two-mode overlay sizing.** Default mode is 1120 px wide (the "stand-in-the-base-and-read-it" view, full charts, larger typography). Compact mode is 720 px (the "walk-around-during-a-boss" HUD view, denser, closer to the pre-overhaul size). Resize handle at bottom-right lets the user override either default; ModConfig.PanelWidthOverride persists across sessions. Min/max bounds `[640, 1600]`. The original plan-draft had default at 880 px; Caner's feedback flipped the framing — default is the at-rest mode, not the moderate middle.

**No rounded corners.** Considered as part of the overhaul; rejected after Caner's "I thought it was as easy as a CSS value" call. Procedural rounded-rect drawing requires per-corner pixel math and the visual win didn't justify the cost. Visual hierarchy comes from surface tiers (Background → Panel → SurfaceElevated) and the 5-stop heat ramp instead. Decision date 2026-05-20 logged in `context/notes/ui-overhaul-plan.md` §12.

**Donut chart via `GraphicsDevice.DrawUserPrimitives`.** Third iteration; the first two failed. Attempt #1 (rotated thin rectangles via `SpriteBatch.Draw`) painted screen-spanning cyan diagonals across the Terraria game world — a runtime artifact the math didn't predict. Attempt #2 (stacked horizontal bar with two-band fill) was safe but flat and didn't read as a chart. Attempt #3 wraps `sb.End()` / `DrawUserPrimitives(TriangleList, ...)` / `sb.Begin(..., Main.UIScaleMatrix)` around a cached `BasicEffect` + reusable `VertexPositionColor[]` buffer. Triangles directly, no SpriteBatch geometry abuse. ~720 triangles per donut at 2° angular resolution. Two concentric rings per slice: outer 75% in identity colour (which mod), inner 25% in dominant-axis tint (cpu/alloc/spike). Scales to any N — angular space is constant whether the modlist has 2 mods or 200. Memory captured at `~/.claude/projects/<this-cwd>/memory/spritebatch-rotation-trap.md` so the next session doesn't hit the same rake.

**ImpactSkyline kept as alternate component.** The three-axis city skyline (vertical bars, each split into cpu/alloc/spike segments) was the runner-up to the donut. Doesn't scale past ~15 bars (each bar gets too thin to read labels). Kept at `UI/Overlay/Components/ImpactSkyline.cs` for future surfaces that want top-N detail rather than total-population share — biggest-spenders deep-dives, side-panels triggered by donut-slice clicks, etc.

**Six tabs:** SUMMARY (was OVERVIEW, multi-dimensional impact view) · TREE · LAG (was SPIKES, unified spikes + stalls feed with timeline strip) · EVENTS · INSIGHTS (card-per-record layout) · SELF (new, profiler's own diagnostics with install-delta projection across bigger modlist sizes). TabRegistry has SelfTab appended at index 5.

**Component library** under `UI/Overlay/Components/`: `ProfilerCard`, `HeatBar`, `Sparkline`, `DonutChart`, `Pill`, `StatBlock`, `SeverityBadge`, `TimelineStrip`, `ImpactSkyline`. Plus `OverlayMode` enum (Default/Compact) and `OverlayLayoutCurrent` static accessors that resolve every dual-mode constant in one place.

**Layout constants are mode-aware.** Old constants in `OverlayLayout` stay alongside `*V2` and `*Compact` variants; `OverlayLayoutCurrent.X` resolves to the right value for `OverlayState.Mode`. Tabs read from `OverlayLayoutCurrent.ChromeHeight` for their content-top Y (was `OverlayLayout.RowsTopOffset` constant 194 px). Old constant still in place for backward compatibility but no longer the source of truth.

**ModConfig as the persistence surface.** First `ModConfig` for the mod: `ProfilerConfig.cs` with `DefaultMode`, `DefaultTabIndex`, `PanelWidthOverride` properties. `OnChanged()` pushes mode + default tab into `OverlayState` so menu changes take effect without re-opening the overlay. `ProfilerOverlay.OnInitialize` reads the config to apply persisted preferences on overlay-open.

**SelfHealth cadence-guard bug.** Sentinel value `long.MinValue` for `_lastRefreshTickIndex` caused signed-overflow on the first `Refresh()` call (`currentTickIndex - long.MinValue` wraps negative; `negative < 60` is true; guard returns early; `Refresh` never fires). Replaced with explicit `_hasEverRefreshed` bool. The 562 MB install delta + 56 KB/hook numbers visible on screen now actually come from a live refresh path, not stale zeros. Headline finding: 562 MB on an 18-mod modlist projects to ~1.5 GB at kitchen-sink (40-mod) scale. Memory-burn mitigation is the next major sub-project after LiteDB.

**Mod versioning discipline.** `build.txt` bumped 0.1 → 0.2. Today's scope (StallDetector, Baseline service, ProfilerSelfHealth, schema v5, full UI overhaul, ModConfig, donut via DrawUserPrimitives, audit fixes) is a clear minor bump. CLAUDE.md gains a "Mod versioning" subsection under Version Control with rules of thumb: patch for pure bug fixes; minor for new features / new tabs / new detectors / new schema versions / significant UI work; major for first Workshop release or breaking JSON schema. End-of-session check is an explicit obligation now.

**TREE + EVENTS visual polish deferred.** Both tabs continue to use their existing layouts inside the new chrome with the larger panel width. They render correctly but leave whitespace on the right edge at 1120 px. Plan §11 acknowledges this as a future polish pass — not a blocker for the v0.2 ship.

**Next-up:** LiteDB migration (cross-session persistence; unblocks `SustainedCostShift`, `NewContributor`, `LifetimeData` evidence scope), then dedicated perf research on the 56 KB/hook footprint (Mono.Cecil retained state is the suspected dominant cost).

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
