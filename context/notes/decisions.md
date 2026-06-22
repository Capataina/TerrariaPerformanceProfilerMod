# Decisions

Resolved decisions from working sessions, newest first. Project-internal record; the README is the directional summary.

## 2026-05-20 — v0.6.1 hot-path follow-through

The v0.6 playtest showed all five user-visible wins worked (world-unload stall 8.5 s → 0.5 s = 16× reduction; world-enter freeze 172 ms → ~110 ms; itemCreatedEvents capturing 21 rows in 3 min vs v0.5's 0; damage-weighted death attribution with `"killed by Demon Eye"`; buffEvents correctly sparse for a no-potion session) but **PerformanceProfiler's own per-tick cost barely moved** (0.27 → 0.31 ms/tick, within noise but the wrong direction). The user's complaint: "we did 40+ optimisations and seems like the overall performance is the same".

Root cause: most of the v0.6 wins were on cold paths or per-event paths. The actual per-tick hot code on the game thread was still doing the same per-tick allocations + scans as v0.5.

v0.6.1 fixes the hot path. Six commits:

- **Dirty-flag PostUpdateEquips + PostUpdateBuffs**: the single biggest per-tick allocation. v0.5/v0.6 ran `CaptureLoadout` and the buff diff loop EVERY tick on the game thread, allocating a fresh `List<EquipmentSlotEntry>` + N entries + a `StringBuilder.ToString()` + a `LoadoutSnapshotRow` even when nothing changed (99%+ of ticks). v0.6.1 computes a 20-op FNV-1a hash of armor types / buff types FIRST; matching hash → return immediately (~20 ns total). The periodic 30-second anchor still fires when due.

- **Incremental histogram baseline (metric-collection §4.6 [H])**: v0.5/v0.6 ran four full 1800-frame ring scans per tick in `Baseline.Recompute` — ~13,600 ops/tick. v0.6.1 maintains persistent histograms + shadow ring buffers; OnFramePushed is +1 the new bucket / -1 the evicted bucket / store / advance head. ~6 array ops/tick + a 512-bucket median scan. MAD recomputed every 30 ticks via amortisation. ~13,600 → ~518 ops/tick. Resync detection keeps the existing batched-push test path correct.

- **Power-of-two retention windows in PerTickAttributionRing**: rounds 1800 → 2048 and 120 → 128, replaces `% _historyTicks` with `& _historyTicksMask` at 8 call sites in Push + GetPerMod* + GetPerModCat*. ~15-25 ns/tick saved on the Push path. Spike-detection §4.3.

- **HookSurfaceCache (mod-lifecycle §4.6 ε9)**: both backends called `AssemblyManager.GetLoadableTypes(mod.Code)` per mod independently — duplicate work × duplicate retained reflection state. New `Profiling/HookSurfaceCache.cs` is a process-scoped `Dictionary<int, Type[]>` cache; HookInterceptor populates, ILHookInterceptor reads. Estimated 80-150 MB install-RAM saving per the dossier.

- **AggressiveInlining on every LangNameCache lookup**: the four per-event hot-path methods (`Buff` / `Item` / `Projectile` / `Npc`) are array indexer accesses that should fold into the call site. Tightens the per-event emit path further.

- **Fall-damage naming fix**: the v0.6 playtest showed `PlayerDeathRow.Summary = "killed by other-0"` for a fall through a self-dug shaft. `PlayerDeathReason.ByOther(0)` is reached by the fall-damage path that doesn't carry the `Fall_TooHigh = 1` tag. `OtherIndexName(0)` now returns `"Fall"`. Plus `StallClusterRow` added to `BsonShortNames` mapping (was missed in the v0.6 sweep — was still writing long-name BSON).

Expected per-tick cost reduction: ~26 µs/tick combined, on a v0.6 baseline of 270 µs/tick = ~10% per-tick improvement. Not the dramatic 50%+ promised by the master plan, but **finally directionally correct**. The bigger wins still on the table (BSON numeric blobs + FK swap + struct union + binary journal + InsertBulk + Cecil ILContext dispose + full row pool Rent/Return + full per-tab overlay format caching) ride larger refactors and are tracked for v0.6.2+.

Bumped `build.txt` 0.6 → 0.6.1.

## 2026-05-20 — v0.6 autonomous performance pass

Caner's framing: spawn 11 per-system + 3 cross-system Opus background research agents (plus a self code-health audit), produce a master plan, then implement end-to-end. Hard constraint: pure perf upgrade — no scope cuts, no feature lightening, no capture-surface reduction. "Optimisation = doing what we already do at maximum efficiency. It is not = doing less" (philosophy.md). Full design lives in `context/perf-pass/` — baseline.md, coherence.md, master-plan.md, verification.md, and research/*.md (15 files, ~16,300 lines).

Run produced **17 commits** delivering:

- **A1 (itemCreatedEvents bug):** `GlobalItem.OnCreated` only fires on Recipe / Initialization / Buy / JourneyDuplication in tML 1.4.4. The 4.5-min v0.5 playtest captured zero rows of a session full of mining + torch placement. Fixed by wiring `GlobalItem.OnSpawn(WorldItem, IEntitySource)` (world-drops, NPC loot, chest reveal, debug command) + `GlobalItem.OnPickup(WorldItem, Player)` (always returns true — Invariant 1 read-only). New `SourceContext` field disambiguates the surface. Schema bump 1→2; old rows default to "Create".

- **A2 (buffEvents diff bug):** PostUpdateBuffs returned early before updating the prev-buff snapshot when the local-player gate failed; the prev state stayed uninitialised. Refactored to gate hard on `Player.whoAmI == Main.myPlayer`, snapshot unconditionally before exit, and emit every active buff as "on" on the first valid tick. Pure-logic fix; the diff itself was already correct.

- **A3 (damage-weighted death attribution):** v0.5 read the most-recent DamageTakenRow from LiteDB at the death edge — last-hit credit, which over-credits whichever source delivered the final blow on a softened player. The 16:09-16:14 playtest's death #1 showed it: vultures dealt 93/100 dmg, a Blue Slime stole the 21-dmg kill, the row read "killed by Blue Slime". v0.6 keeps a 64-slot in-RAM `RecentDamageEntry` ring on SessionRecorder populated alongside `OnDamageTaken`. At the death edge, `AggregateRecentDamage` over a 10-second window sorts by total damage; the killer is the largest contributor. Full breakdown persists in `PlayerDeathRow.DamageWeighting` (Honesty contract, Invariant 3). Schema bump 1→2. Also removes the only game-thread LiteDB read in the hot lifecycle path.

- **Phase α — shared infrastructure:** `Profiling/Time.cs` (UnixMsNow at ~5 ns vs 150-250 ns for DateTimeOffset.UtcNow — single Stopwatch + multiplier), `Profiling/Pools/RowPool.cs<T>` + `ListPool<T>` + `IPoolReset`, `Profiling/LangNameCache.cs` (id-keyed string arrays for buff/item/proj/npc populated at PostSetupContent), `Profiling/ModOwnerCache.cs` (lazy mod-name + `FromEntitySource` source-stripping), `Profiling/EnumStringTable.cs` (5 enums), `Profiling/Util/BoolIndex.cs` (O(1) bit membership for buff diff). 13 new xUnit tests cover the runtime-independent helpers.

- **Phase β — per-tick zero-alloc:** 12 × `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` → `Time.UnixMsNow()` across MetricCollector, WorldSnapshotter, PlayerDeathDetector, Interaction{Player,Npc,Item}. `[MethodImpl(MethodImplOptions.AggressiveInlining)]` on ProbeStack.Enter/Leave/EnterCpuAlloc/LeaveCpuAlloc + PerModAttribution.Add (both overloads). ContextTransitionWatcher.DiffBiomeBits rewritten from bit-by-bit IsSet scan to word-level XOR + `BitOperations.TrailingZeroCount` (events-and-context R1; 680 ns/tick → ~6 ns/tick steady state).

- **Phase γ partial — per-event row efficiency:** every interaction tracker uses cached lookups instead of inline `Lang.Get*` + `Loader.GetXxx`. InteractionPlayer reuses a single `_fpBuilder` StringBuilder field instead of allocating per CaptureLoadout. `SnapshotActiveBuffTypes` gets a capacity hint. The full Rent/Return cycle (writer-thread Return after Apply) is deferred to v0.6.1 because it needs the writer-thread refactor.

- **Phase δ partial — BSON short field names:** `Profiling/Persistence/BsonShortNames.cs` centralises every short-name mapping via LiteDB's fluent `BsonMapper.Global.Entity<T>()` API. Common fields uniformly: SessionId→`s`, Tick→`t`, UnixMs→`u`. Per-record short names follow a 2-3-char convention. Forward-only schema break: v0.5 DB rows aren't readable through the v0.6 mapper. Documented in cross-storage-ram §6.5.

- **Phase ε partial — session-end relocation:** OnWorldUnload's session-end block (recorder.End + DrainAndTruncateJournal + SessionSummaryLogger.Write — the 40-stall 8.5-s UiOverlayBlocking cluster in the v0.5 playtest) wrapped in `Task.Run`. Captures Collector/Recorder/Database/Logger by strong-ref before spawning. Main thread returns immediately to vanilla world-unload; background work runs while player is on title screen.

- **Phase ε7 — deferred world-load init:** OnWorldLoad sets `_deferredInitPending = true` and returns immediately. First PostUpdateEverything tick runs the actual construction (MetricCollector ring, ModlistFingerprint.Compute, SessionRecorder, ContextTransitionWatcher, WorldSnapshotter, PlayerDeathDetector, ContextTagger, EventAggregator) then skips that tick's per-tick path. World-enter freeze drops 172 ms → ~110 ms; tick 1 spikes (allowed during gameplay per Invariant 2 budgets).

- **Phase ζ — insight detector LINQ removal:** LoadoutCorrelatedCostDetector + EventConditionalCostDetector rewritten from LINQ chains (Where / OrderByDescending / GroupBy / Average / ToList × 14 sites) to explicit foreach loops + field-cached `Dictionary<int, BuffWindow>` for the buff aggregation. Per-pass allocation drops from ~50 KB to ~0. Same insight rows emitted; only the path is cheaper. AllocationBurstDetector + GcPauseCulpritDetector promote per-pass `new double[modCount]` to field-cached scratch buffers.

- **Phase η partial — overlay mount allocs:** `ProfilerOverlaySystem.ModifyInterfaceLayers` caches the `LegacyGameInterfaceLayer` instance (was `new` every frame = ~3,600 allocs/sec). `DrawOverlay` uses a single shared `_cachedGameTime` sentinel (was `new GameTime()` every frame). `OverlayPanel.LayoutStatCards` reuses a `_statCardRectsCache` field (was `new Rectangle[4]` per DrawSelf). Full per-tab format-string caching is deferred to v0.6.1.

- **Stall-cluster span fix (§5.L):** `_liveCluster.EndUnixMs = s.StartTimestampUnixMs` was using the start moment; now `+ (long)s.TickPeriodMs`. The 40-stall UiOverlayBlocking cluster previously reported its span ~80 ms shorter than it actually was. Cosmetic but documented.

- **Wrap housekeeping:** `PlayerDeathReason.SourceCustomReason` (obsolete) → `CustomReason` (returns NetworkText; ToString() drives the string conversion). Two `ChangeMagicNumberToID` warnings: literal `0` → `ItemID.None` / `NPCID.None`.

Bumped `build.txt` 0.5 → 0.6. Minor bump per the versioning policy (three bug fixes + headline 8.5-s stall fix + the foundational infrastructure that future v0.6.1+ rides).

Build green. 63 / 63 unit tests passing (50 from v0.5 + 13 new α infrastructure tests). The synthetic xUnit benchmark suite is unreliable for v0.6's deltas because it doesn't exercise the paths that changed (event-stream emit + Lang/ModOwner caches + the InteractionPlayer hot path) — see verification.md §6. The real measurement is in-game playtest comparison against the 16:09-16:14 v0.5 baseline.

Deferred to v0.6.1 (full designs in research/*.md): Phase δ continued (FK swap + numeric blobs + byte enums + binary journal + DbWriteOp struct union + InsertBulk + compound indexes), Phase ε continued (heap-snapshot diagnostic + conditional ALLOC-1 + HookSurfaceCache + BeginInstallAsync + PreSaveAndQuit), Phase η full (per-tab format caches + donut vertex reuse + Sparkline span overload), full row-pool Rent/Return cycle, remaining β items (incremental histogram baseline + SIMD UpdateRollingAverage), Environment.CpuUsage migration (gated on tML reference assemblies exposing the .NET 7+ API), ProfilerConfig `[LabelKey]` migration.

## 2026-05-20 — Interaction-tracking arsenal + v0.5 (data-stack expansion)

Caner's framing: the profiler shifts from *event logger* (records discrete things and attributes to the busiest mod's hooks) to *interaction tracker* (records game-state windows so cost can be correlated with what the player was doing, wearing, fighting, getting hit by). The presentation/storage stack is a downstream concern; capture is a one-way door. `context/notes/philosophy.md` is the durable note for this posture. Invariant 5 (no mod-specific code) was added to `CLAUDE.md` to enforce the universality side of the shift.

**Phase A — MainThreadFreeze vs ProcessSuspended.** `Main.hasFocus` is now snapshotted per-tick and threaded through `StallDetector.OnBeginTick` as `hadFocusThisTick`. Across a stall window the detector tracks `_focusHeldAcrossGap`; the classifier uses it to disambiguate a real OS suspend (focus lost) from a main-thread freeze (focus held, CPU went idle anyway). New `StallCause.MainThreadFreeze` cause. Fixes the v0.4 misdiagnosis where the player never alt-tabbed but the log said "ProcessSuspended" because the per-event signals (low CPU, no GC) were identical.

**Phase B — Damage-taken tracker + death-cause attribution.** New `damageTakenEvents` collection populated by a `ModPlayer.OnHurt` override on a new `InteractionPlayer`. Captures `PlayerDeathReason` via the universal struct: SourceProjectileType / SourceNPCIndex / SourceOtherIndex / SourceCustomReason map into `(SourceKind, SourceId, SourceName)` columns. `PlayerDeathDetector` now reads the most recent damage-taken row at the death edge — the killer is whatever last hurt the player. The mother-slime case becomes self-narrating.

**Phase C — NPC + item spawn trackers.** New `npcSpawnEvents` (`GlobalNPC.OnSpawn`) and `itemCreatedEvents` (`GlobalItem.OnCreated`) collections. Both encode the source via the `IEntitySource` / `ItemCreationContext` subclass name (with the prefix/suffix stripped) — the universal vanilla surface every spawning mod uses. CheatSheet shows up as `DebugCommand`; recipe crafts show up as `Recipe`; boss-summon items as `BossSpawn`. Owning mod resolved dynamically from the NPC/item type's owning mod, never hardcoded.

**Phase D — Loadout snapshot tracker.** New `loadoutSnapshots` collection. Hooks `ModPlayer.PostUpdateEquips`; diffs every occupied slot (armor / accessory / vanity / dye / modSlot — modAccessorySlot picked up generically by iterating the slot array, not by mod name) against the previous tick. Emits on change. Periodic 30s anchor written even when unchanged so cost-correlation queries can always find at least one snapshot per time window. Stable fingerprint string used as the join key.

**Phase E — Damage-dealt tracker.** New `damageDealtEvents` collection. Three rows per hit type: `OnHitNPC` (melee path), `OnHitNPCWithItem` (weapon path, carries weapon id), `OnHitNPCWithProj` (projectile path, carries projectile id). Each row carries the current loadout fingerprint. The "is it the sword, the projectile, or the accessory" question answerable from a single damage event by joining loadout state to weapon / projectile id.

**Phase F — Buff lifecycle tracker.** New `buffEvents` collection. Hooks `ModPlayer.PostUpdateBuffs`; diffs the buff array against last tick. Emits per buff-on / buff-off edge. Owning mod resolved via `BuffLoader.GetBuff(type)?.Mod`. The Dead Cells Mechanics pattern — buff fires only after damage taken, hooks light up while it's active — becomes capturable as the off→on→off triple.

**Phase G — Three new insight detectors.** New `PatternKey`s: `LoadoutCorrelatedCost`, `EventConditionalCost`, `LoadoutCombinationCost`.
- `LoadoutCorrelatedCostDetector`: when loadout changes, computes mean tick-aggregate AvgFrameMs in the 30s before vs the 30s after the change. Fires if the post window is ≥ 1.5× pre AND ≥ 2× baseline.
- `EventConditionalCostDetector`: for each recent off→on→off buff triple, computes mean cost in the buff-active window. Fires if window-mean is ≥ 3× baseline. The Dead Cells case lights up as "frame cost averaged 28ms while 'RecentlyHit' was active (3.2× baseline)".
- `LoadoutCombinationCostDetector`: synergy claim — gated on cross-session loadout aggregation. Pattern key reserved, UI shows it under gated detectors until persistence-driven analysis lands.

**Tests:** 54/54 passing. New `MainThreadFreeze` tests cover focus-held + focus-lost branches; existing tests updated to pass `focusHeldAcrossGap` explicitly. Classifier signature: `ClassifyCause(wallMs, gcMs, gen2Delta, cpuMs, recentStallsInLast5s, baselineMs, focusHeldAcrossGap)`. Back-compat overloads preserved.

**Bumped build.txt 0.4 → 0.5.** Six new collections, three new insight detectors, one new stall cause, new philosophy + invariant note. Clearly a minor bump.

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

The JSON-per-session writer (`SessionLogWriter.cs`, ~940 lines) is gone. The new persistence layer is a single LiteDB file plus a redo log plus three rotating backups, all under `Profiling/Persistence/`. Implementation followed the LiteDB-migration plan note end to end (that note was deleted once the work landed; its durable rationale is captured in this entry and in `systems/persistence.md`); every decision below is the plan's recommendation accepted.

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


## 2026-05-21 — v0.10 unified data pipeline + multi-agent code-health audit

**Unified data pipeline landed.** The Data/ folder now houses every stream-shaped artefact the mod produces: collectors (FrameTime, HookCpu, Allocation, ContextTagger), aggregators (Heatmap, Segment, Event, PerModAttribution + Segments/), stats (Kpi, EventsFeed, SelfHealth, Spikes, Stalls, Insights, Baseline, ModImpactScorer, HookCoverageView), detectors (Spike, Stall, the Insights engine + 10 concrete detectors), and persistence streams (SessionRecorder + 14 concrete streams + StreamRegistry). Every dashboard endpoint consumes data via `DataRegistry.Shared.Lookup<TSnapshot>(name).CurrentSnapshot()` instead of reaching into ProfilerSystem.Collector. Canonical reality in `context/systems/data-pipeline.md`. The 12-step migration plan was deleted once the work landed; the file moves are visible in git history via `git log --diff-filter=R --name-status v0.10..v0.11`.

**Two sub-items in the original plan deliberately not done** (cosmetic, no behavioural impact): the `SegmentDetector` internal split into `SegmentEdgeCollector` + `SegmentAggregator` (the existing `Data/Aggregators/SegmentAggregator.cs` adapter already serves the external-API role; splitting the detector's private state machine has no behavioural gain), and the cosmetic renames `ContextTagger → EventContextCollector`, `EventAggregator → BiomeBucketAggregator`, `PerModAttribution → PerModAggregator` (especially `PerModAttribution` is referenced from IL-emit metadata in `ILHookInterceptor` and renaming would ripple through the detour IL stream for aesthetic gain only).

**ProfilerSystem.Collector is now `internal`.** The visibility tighten is the policy commitment: external consumers route through the registry; same-assembly code inside `Data/` and `Profiling/` keeps direct access. Documented in `context/systems/data-pipeline.md` under Policy commitments.

**Web folder modularised; TinyHttpServer renamed.** `Web/Assets/Css/Css.*.cs` (17 files) and `Web/Assets/Js/Js.*.cs` (15 files) split the previously-monolithic CSS/JS bundles into per-section partial classes. The 1000+-line `DashboardAssets.Js.cs` is gone. `TinyHttpServer` was a misleading name (it's the production server, not a test stub) — renamed to `DashboardHttpServer` and moved to `Web/Server/`.

**Multi-agent code-health audit pass.** Five parallel general-purpose subagents audited Data/, Profiling/ core, Persistence+Insights, Web/, and UI/ — total ~73 BUG-class findings plus larger counts of PERF/SMELL/NIT. The high-priority slice landed in two follow-up commits (`code-health-audit: first wave` and `second wave`). Critical fixes by class:

- *Invariant 2 (zero per-tick alloc):* `SegmentDetector.ComputeBiomeComposite` was allocating a StringBuilder + final string every tick on stable biome state — now memoised by bitset.
- *Invariant 3 (descriptive, not normative):* dashboard text "possibly removable" → "idle most of session"; "clean session" → "no spikes or stalls observed in the last 30s"; "if this mod were removed" → "modelled cost without this mod's contribution, descriptive of measured cost, not a recommendation".
- *Data races:* `DashboardRouter.BuildNow` was reading `MetricCollector.History` from the HTTP worker thread while the game thread mutated it. Now reads through pipeline snapshots. `DataRegistry.Register/DisposeAll` lock both views together.
- *Correctness:* `BoolIndex.EnsureCapacity` infinite loop on `capacity == 0` (`0 *= 2` never grows). `PlayerDeathDetector` boss-id cast to `short` truncated modded types ≥ 32768 — dropped. `ContextTransitionWatcher` weather rows encode the flag identity in the Type field; pre-fix every weather flip collapsed into an indistinguishable row. `TickDownsampler.RollingFrame._max` recomputes on eviction (was monotonic-since-session-start). `ModlistStream` derives SessionCount from the Sessions collection and dedupes VersionHistory — replay-idempotent. `LegacyJsonImporter` parses dates with InvariantCulture. `ProfilerDatabase.EnsureSchemaVersion` falls through to v0 on a torn USER_VERSION pragma.
- *Insights confidence model honesty:* SegmentDeathCorrelation, SegmentOutlier, SegmentTopMod detectors now emit `Confidence.Preliminary` rather than computing a tier at emit. `InsightStore.PromoteConfidence` overwrites on Submit; the per-detector ratio-based tiering was silently dead code. Honesty: store owns confidence, detectors emit evidence.
- *Performance:* `DashboardAssets.Css/Js` switched from get-only property `=> string.Concat(...)` to `static readonly` initialised once. `DashboardRouter` caches the UTF-8 bytes of those bundles at type-init. `KpiCalculator` uses a ThreadStatic scratch buffer for the median sort instead of allocating `double[1800]` per `/api/now` poll. `SessionRecorder` stall-event writes go through `EnumStringTable` instead of `enum.ToString()` boxing.

**Cadence-vs-callback honesty.** Three collectors initially declared `PerTick` cadence with no-op delegates. Switched to `OnDemand` (pull-side adapters; MetricCollector itself owns the per-tick capture) — the cadence label now matches who-does-work.

**`UI/Overlay/**` retained, audit recommendation flagged.** The UI audit recommended deleting the 5,500-line archived overlay tree, arguing git history is the archive and revival cost (Steam Deck) would be a rewrite anyway. Decision deferred: the v0.9.0 README explicitly preserves the tree on disk for future revival. Reversing that is a policy call, not a code-quality call. Reconsider when scope of v1.0 is firm.

**Audit findings explicitly not addressed in this pass.** Documented for a follow-up sweep:
- Live-collection thread-safety pattern (snapshots leak refs to mutable underlying collections — class of races on the worker thread; needs a system change to fix systemically).
- ILHookInterceptor ret-rewrite handler-scope edge cases (Cecil work).
- Several persistence-layer index additions (LiteDB compound indexes for stream upsert paths).
- Detector cursor missing on GcPauseCulpritDetector / EventConditionalCostDetector (re-scan every Evaluate pass).
- Various NIT-class findings.


## 2026-05-21 — v0.12 tab rework + visualisation patch

**21-addition tab rework landed across Timeline, Lag, Insights.** Each tab moved from a flat ledger to a multi-panel Palantir-style dashboard. The plan was decomposed into 76 atomic tasks in 6 waves and executed largely by delegated background agents:
- Wave 0: prep (file splits + locked snapshot contracts) — main thread
- Wave 1: 3 foundation agents in parallel (F1 ModRosterScanner, F2 PerModUsageAggregator, F3 PerModCostTimeSeriesAggregator)
- Wave 2: 3 data-layer agents in parallel (T-data, L-data, I-data) producing 17 new Stats/Aggregators
- Wave 3: 3 per-tab UI agents in parallel (T-UI, L-UI, I-UI) producing API endpoints, HTML, CSS, and JS renderers
- Wave 4: 3 visualisation-patch agents in parallel enriching each tab with creative visualisations
- Wave 5: integration (docs, tooltips, version bump)

**14 background agents total** vs ~76 hours sequential; wall-clock ~25 hours. The contract-decoupling pattern (snapshot types frozen in Wave 0 = `Data/Contracts/RolloutContracts.cs`) let downstream agents compile against types whose implementations didn't yet exist, enabling Wave 2 + Wave 3 to overlap with Wave 1 + 2 respectively.

**21 additions delivered:**

Timeline: T1 per-segment mod-attribution waterfall, T2 lifetime comparison badges, T3 context-transition overlay track, T4 session activity heatstrip, T5 per-mod biome/invasion attendance roll-up, T6 death-replay micro-strips (30s pre-death event window), T7 session chronicle (factual sentences with timestamps).

Lag: L1 lag fingerprint clustering, L2 cause×context heatmap, L3 GC pressure narrative panel, L4 per-segment lag density, L5 attribution confidence visualisation, L6 allocation→GC causality chain, L7 lag rhythm/periodicity detection.

Insights: I1 per-mod observatory cards (composing roster + usage + cost), I2 dormant content surface, I3 per-mod attendance breakdown, I4 loadout influence trace, I5 cross-cutting signal aggregation, I6 engagement-vs-cost scatter, I7 mod interaction correlation matrix.

**Plus the foundations** F1 ModRosterScanner, F2 PerModUsageAggregator, F3 PerModCostTimeSeriesAggregator that everything-per-mod reads from.

**Contract decoupling worked.** Every Wave 2/3 agent looked streams up by name through `DataRegistry.Shared.Lookup<TSnapshot>(streamName)` — never direct class refs to F1/F2/F3 implementations. The compile dependency chain was: contracts → everything. This meant Wave 2 could fire alongside Wave 1, Wave 3 alongside Wave 2. Pearl-on-string parallelism.

**Honest limitations documented in code.** Each Wave 2 stream's class doc-comment names the data the producer can't yet emit (per-event EventContext on spikes/stalls, per-biome breakdown in F2, biome at death-time, etc.) so future passes know what to add without re-deriving the gap.

**Visualisation patch (Wave 4) is descriptive-only.** Every visual metaphor reflects measurement, not judgement: dust-shelf for dormant content (dust = quantity), narrative ribbon for chronicle (text), lag galaxy for clusters (positional similarity). Banned: skulls, "junk" tags, recommendation copy. Invariant 3 holds across the rework.

**Plan file deleted.** Unlike v0.11's unified-data-pipeline plan which was preserved with a status header, the v0.12 plan never lived as a separate file — the task list (Wave 0.1 ... Wave 5.2) was the plan. After completion the tasks remain in the agent system but the canonical implementation lives in `context/systems/data-pipeline.md` (this update) + the code itself.
