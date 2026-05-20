# Master Implementation Plan — Performance Pass v0.5 → v0.6

> Phase 5 output. Reads `coherence.md` + every `research/*.md` doc; converts the routing index into commit-sized chunks with per-commit test plans, rollback strategies, and progress gates. **Implementation (Phase 6) executes this file sequentially.**

Date: 2026-05-20 · Source: `context/perf-pass/coherence.md` + 15 research dossiers · Total scope: 87 distinct items across 9 phases

---

## 0. How to read this plan

Each phase is **one logical commit**. Multi-day phases are subdivided into sub-commits that each build green and ship green. The implementation agent (me, in Phase 6) follows the plan top-to-bottom. Every commit:

1. **Touches only files listed in the phase.** If during work a file outside the list needs editing, the implementation phase pauses, logs the surprise, decides in-line whether to bundle the change or defer it, and continues.
2. **Builds clean.** `dotnet msbuild PerformanceProfiler.csproj` + `dotnet msbuild Tests/PerformanceProfiler.Tests.csproj` both green. New warnings count against the pass.
3. **Passes existing tests.** `dotnet test Tests/PerformanceProfiler.Tests.csproj` stays green (currently 54/54).
4. **Adds the listed new tests / benchmarks.** Each phase declares what it adds; those must pass before commit.
5. **Commits with a comprehensive message** following `CLAUDE.md` §"Version control": what, why, non-obvious implications, Co-Authored-By trailer.
6. **Does not push.** Push happens at the end of Phase 8 only (per `CLAUDE.md` "do not push without explicit permission").

If at any point an Invariant would be violated, the phase halts and surfaces. There is no "ship it and fix it later" path for the five Invariants.

---

## 1. Phase ordering rationale

Coherence.md §6 sets the order. To restate the load-bearing constraints:

- **Phase 0 first** — Release-mode baseline re-measurement. Every "expected delta" downstream is meaningless until the baseline is corrected. ~1 hour.
- **Phase A next** — bug fixes (correctness gates the rest). Without A1/A2/A3 the post-pass capture surface is still broken and verification numbers lie.
- **Phase α before β** — shared infrastructure must exist before the per-tick and per-event sweeps use it. Doing it in the other order would require rewriting every β/γ change once α lands.
- **Phase β before γ** — per-tick is the strictest invariant (zero alloc); proving the per-tick path is clean before touching per-event work isolates regressions.
- **Phase γ before δ** — rows must be pooled before the channel payload shape changes. Pool first, then change the carrier.
- **Phase δ before ζ** — insight detector rewrites read the new compound indexes, which land in δ15.
- **Phase ε can run alongside δ** — session-end + install are mostly disjoint from writer-throughput. Practically I'll serialise them to keep blast radius small per commit, but they have no hard dependency.
- **Phase η last** — overlay caching reads enum tables from α6 and benefits from the alloc relief that the earlier phases have already landed. Doing it first would force two passes when α-ε change the data shapes the overlay reads.
- **Phase 8 (wrap)** — version bump, decisions.md, push.

---

## 2. Commit sequence — every commit specified

Commits are numbered C1, C2, … Phase tags shown for traceability back to coherence.md.

### Commit C1 — Release-mode baseline recapture (Phase 0)

**Scope:** rerun `Tests/Persistence/PersistenceBenchmarkTests` under Release config. Update `baseline.md` §1 with the new numbers. Adjust §6 (target deltas) only if the Release numbers are so different they invalidate a target.

**Files touched:** `context/perf-pass/baseline.md` only.

**Steps:**
1. `dotnet test Tests/PerformanceProfiler.Tests.csproj -c Release --filter "Benchmark" --logger "console;verbosity=detailed"` — capture every benchmark output line.
2. Compare each value against the Debug numbers already in `baseline.md`.
3. Update `baseline.md` §1 table inline. Preserve the Debug column for posterity (as "Debug" annotated row).
4. If any Release figure already meets the target without a code change, mark "already passing" and reduce the pressure on the dependent phase.
5. Commit message: "perf-pass: capture Release-mode baseline (gate for v0.6 measurement targets)".

**Test plan:** none beyond verifying benchmarks ran. No code changed.

**Rollback:** revert the single file. The pass continues against the prior baseline numbers.

**Expected outcome:** Release numbers will be 30–70% better than Debug across the board. If `441 → ~250` ns/op shows, the `< 200` target stays but the urgency profile drops. If `< 200` is already met in Release, γ phase becomes "stretch goal" rather than required.

---

### Commit C2 — Bug A1: `itemCreatedEvents` correct surfaces (Phase A)

**Scope:** wire the three generic surfaces (`GlobalItem.OnCreated` already exists for crafts; add `GlobalItem.OnSpawn(WorldItem, IEntitySource)` for world-drops; add `ModPlayer.OnPickup(WorldItem)` for pickups). Each writes an `ItemCreatedRow` with the correct `SourceContext` tag.

**Files touched:**
- `Profiling/Persistence/Interactions/InteractionItem.cs` (the GlobalItem)
- `Profiling/Persistence/Interactions/InteractionPlayer.cs` (the ModPlayer pickup hook)
- `Profiling/Persistence/Records/ItemCreatedRow.cs` (add `SourceContext` field — already in cross-storage-ram §6.3 Phase 1 migration)
- `Profiling/Persistence/Streams/ItemCreatedStream.cs` (if BSON map needs update)
- `Tests/InteractionItemTests.cs` (new — synthetic surface test)

**Steps:**
1. Read `gh api repos/tModLoader/tModLoader/contents/patches/tModLoader/Terraria.ModLoader/GlobalItem.cs` to confirm `OnSpawn(WorldItem, IEntitySource)` signature.
2. Read `ModPlayer.OnPickup(WorldItem)` signature similarly.
3. Add the two new hook overrides to `InteractionPlayer.cs` and `InteractionItem.cs` per generic surface. No mod-specific code.
4. `SourceContext` field gets one of: `"Recipe"` / `"Initialization"` / `"Buy"` / `"JourneyDuplication"` (existing surfaces) plus `"WorldDrop"` (new from `OnSpawn(WorldItem, IEntitySource)` via `IEntitySource` subclass name) and `"Pickup"` (new from `OnPickup`).
5. Bump `ItemCreatedRow._schema` (now `v` after δ3 but for this commit it's still `_schema`) from 1 to 2.
6. Migration step in `Migrations.cs` writes default `SourceContext = "Recipe"` for old rows (the only context that fired in v0.5).
7. New xUnit test: synthetic `IEntitySource` instance → `InteractionItem.OnSpawn` → `ItemCreatedRow` produced with correct `SourceContext`. Use a hand-rolled fake IEntitySource since tML isn't loaded in tests.
8. Commit: "v0.6 A1: wire GlobalItem.OnSpawn + ModPlayer.OnPickup so itemCreatedEvents captures world-drops and pickups, not just crafts".

**Test plan:** new test asserts each of the 6 SourceContext values produces a row. Existing 54 tests still pass.

**Rollback:** revert files. v0.5 capture restored.

**Expected outcome:** in-game playtest will show itemCreatedEvents going from 0 to dozens or hundreds per mining/combat session.

---

### Commit C3 — Bug A2: buff-diff snapshot-before-gate + first-tick emission (Phase A)

**Scope:** fix the early-return-before-snapshot-update in `PostUpdateBuffs`. If `Player.whoAmI != Main.myPlayer` clears late, the prev-buffs snapshot never initialises and the very first valid tick can't diff. Move snapshot update to *before* the early returns; on the first valid tick, emit all active buffs as "on" edges.

**Files touched:**
- `Profiling/Persistence/Interactions/InteractionPlayer.cs` (the `PostUpdateBuffs` method)
- `Tests/InteractionPlayerBuffDiffTests.cs` (new)

**Steps:**
1. Refactor `PostUpdateBuffs`: snapshot read of `Player.buffType` happens unconditionally. Then the gate checks. Then the diff emits if both prev and current exist.
2. Add `_firstValidTickSeen` boolean. On the first valid tick after `Player.whoAmI == Main.myPlayer`, emit every active buff as "on" edge.
3. New xUnit test: synthetic ModPlayer state → first tick emits N on-edges → second tick with one buff removed emits one off-edge.
4. Commit: "v0.6 A2: fix PostUpdateBuffs snapshot-before-gate + first-valid-tick emission so buffEvents captures every edge, not just the first on/off pair".

**Test plan:** new test covers: (a) first valid tick emits all active buffs as on; (b) adding a buff next tick emits one on; (c) removing a buff emits one off; (d) buff-array growth handled.

**Rollback:** revert single file + delete new test.

**Expected outcome:** in-game playtest with potion use will show on/off edges for Healing, Ironskin, Regen, etc. If the retest *still* shows sparsity, the bug is elsewhere (out of pass scope, files a separate ticket).

---

### Commit C4 — Bug A3: damage-weighted death attribution + in-RAM rolling damage ring (Phase A)

**Scope:** remove the only game-thread DB query (`PlayerDeathDetector.Capture` reads the last DamageTakenRow from LiteDB). Replace with `SessionRecorder._recentDamageRing[64]` populated by `OnHurt`. At death edge, aggregate damage by source over the last N seconds (config default: 10 s), report top contributor as killer.

**Files touched:**
- `Profiling/Persistence/SessionRecorder.cs` (add `_recentDamageRing[64]` + populate from OnHurt forwarded path)
- `Profiling/Persistence/Interactions/InteractionPlayer.cs` (`OnHurt` writes both to event queue AND to recorder's ring)
- `Profiling/Persistence/PlayerDeathDetector.cs` (replace LiteDB query with ring aggregation)
- `Profiling/Persistence/Records/PlayerDeathRow.cs` (add `DamageWeighting` field + `DamageAttributionWindowSeconds` field, bump schema 1 → 2)
- `Profiling/Persistence/Migrations.cs` (default new fields on old rows)
- `Tests/PlayerDeathAttributionTests.cs` (new)

**Steps:**
1. Define `RecentDamageEntry` struct (UnixMs, SourceKind, SourceId, SourceName, DamageDealt) — 32-byte struct.
2. `SessionRecorder._recentDamageRing` is a fixed-size struct array, populated FIFO.
3. `InteractionPlayer.OnHurt` calls `recorder.OnRecentDamage(...)` *in addition to* enqueuing the `DamageTakenRow`.
4. `PlayerDeathDetector.Capture` reads the ring, aggregates by `(SourceKind, SourceId)`, finds the max-total-damage contributor in the last 10 seconds, writes `PlayerDeathRow.SourceName = topContributor.Name`. The DB query is deleted.
5. `PlayerDeathRow` gains `DamageWeighting: List<DamageContributor>` to record the full attribution table (transparency: the player sees not just "killed by vulture" but "60% vulture, 30% blue slime, 10% lava"). This **adds** to the capture surface — doesn't reduce it.
6. New xUnit test: synthetic damage events (3 vultures + 2 slimes) → death edge → expected killer is vulture by damage weight (matches our 16:09–16:14 playtest case).
7. Commit: "v0.6 A3: damage-weighted death attribution via in-RAM ring; removes the last game-thread DB read".

**Test plan:** new test covers: (a) last-hit credit case (single source) still resolves to that source; (b) the playtest case (93/100 dmg from vultures + slime delivers kill shot) resolves to vulture; (c) the window cutoff works (damage > 10s ago is excluded).

**Rollback:** revert files + delete new test. Note: this commit removes the LiteDB query — rollback brings it back.

**Expected outcome:** v0.6 deaths report damage-weighted killer. Previous Phase v0.5 sessions remain interpretable through the LiteDB damage events; the new attribution is forward-only.

---

### Commit C5 — Phase 0 baseline re-anchor + Phase A end-of-phase verification

**Scope:** verify all three bug fixes work end-to-end. Build a v0.6-alpha `.tmod`, run the 4 existing benchmarks under Release, capture new numbers. Update `baseline.md` to reflect post-A reality (the new captures will increase per-session row counts, so the storage census needs updating).

**Files touched:** `context/perf-pass/baseline.md` only.

**Steps:**
1. `dotnet test Tests/PerformanceProfiler.Tests.csproj -c Release` — all benchmarks.
2. Update baseline.md if any per-session row-count projections shifted.
3. Commit: "perf-pass: re-anchor baseline after Phase A bug fixes; capture surface now complete".

**Rollback:** revert baseline.md.

---

### Commit C6 — Phase α: shared infrastructure (α1–α8)

**Scope:** create the seven helper classes + the FCall benchmark. No behavioural change. No existing call sites converted yet — they get converted in β / γ / η.

**Files added (new):**
- `Profiling/Time.cs` — `UnixMsNow()` + `Reset()` (α1)
- `Profiling/LangNameCache.cs` — populated at `PostSetupContent` (α2)
- `Profiling/Pools/RowPool.cs` + `IPoolReset.cs` (α3)
- `Profiling/Pools/ListPool.cs` (α4)
- `Profiling/ModOwnerCache.cs` (α5)
- `Profiling/EnumStringTable.cs` — 5 enums (α6)
- `Profiling/Util/BoolIndex.cs` (α7)
- `Tests/Benchmarks/AllocCounterBenchmarks.cs` — FCall cost bench (α8)
- `Tests/UnixMsNowTests.cs` (test for α1)
- `Tests/LangNameCacheTests.cs` (test for α2 — uses Fakes since Lang isn't loaded)
- `Tests/RowPoolTests.cs` (test for α3)
- `Tests/ListPoolTests.cs` (test for α4)
- `Tests/EnumStringTableTests.cs` (test for α6)
- `Tests/BoolIndexTests.cs` (test for α7)

**Files modified:**
- `Profiling/ProfilerSystem.cs` — call `Time.Reset()` and `LangNameCache.Populate()` at `PostSetupContent`.
- `Tests/PerformanceProfiler.Tests.csproj` — link the new test source files.
- `Tests/Benchmarks/PerformanceProfiler.Benchmarks.csproj` (new — separate project for BDN per test-harness recommendation).

**Steps:**
1. **α1 Time.UnixMsNow:** capture `(Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())` once at `Time.Reset()`. Per-call: `(now - origin) / Frequency * 1000` + the baseline UnixMs. No DateTime alloc per call.
2. **α2 LangNameCache:** at `PostSetupContent`, walk `BuffID` / `ItemID` / `ProjectileID` / `NPCID` value ranges plus `BuffLoader.GetBuff(i)` etc for modded ids. Cache results into `string[NameKind, id]` indexed arrays.
3. **α3 RowPool<T> + IPoolReset:** classic object pool with a thread-safe `ConcurrentBag<T>` backing; `Rent()` / `Return(item)`. `IPoolReset.Reset()` is called on Return so the row is clean for next use.
4. **α4 ListPool<T>:** same pattern for `List<T>`. Clear-on-return.
5. **α5 ModOwnerCache:** dictionary `(typeKind, id) → string?` populated lazily on first lookup via the existing `OwningModName` logic.
6. **α6 EnumStringTable:** for each of `StallCause`, `StallSeverity`, `LoadoutReason`, `BuffEdge`, `PatternKey` — flat `string[]` indexed by enum int value.
7. **α7 BoolIndex:** fixed-size bool[] wrapper exposing `Add(int id)` / `Remove(int id)` / `Contains(int id)`. For buff-diff fast path.
8. **α8 FCall bench:** BDN benchmark calling `GC.GetAllocatedBytesForCurrentThread()` 1M times, reports ns/op. Per test-harness B-003.
9. Run α8 in Release before completing the commit. **If t_alloc > 50 ns, halt the commit, surface the result, default decision per coherence.md §2.11: proceed with unconditional tracking and document the measurement.** If t_alloc ≤ 50 ns (expected), proceed.
10. Commit: "v0.6 Phase α: shared infrastructure for the perf pass (Time, LangNameCache, RowPool, ListPool, ModOwnerCache, EnumStringTable, BoolIndex, FCall bench)".

**Test plan:** each new helper has at least one unit test (boundaries, thread-safety where relevant). FCall bench is itself the test.

**Rollback:** delete new files + revert `ProfilerSystem.cs`. No production paths depend on these yet.

**Expected outcome:** ~600 LOC of new infrastructure. No measurable change in any baseline number (yet). FCall cost reported for the record.

---

### Commit C7 — Phase β1: per-tick zeroing (β1–β12)

**Scope:** the metric-collection + spike-detection + allocation-tracking per-tick changes. No interaction-tracker work yet (that's γ).

**Files modified:**
- `Profiling/MetricCollector.cs` (β1, β7, β8, β9, β10, β11)
- `Profiling/PerModAttribution.cs` (β6, β9)
- `Profiling/ProbeStack.cs` (β5)
- `Profiling/StallDetector.cs` (β2, β3, β21, β22)
- `Profiling/SpikeDetector.cs` (β4)
- `Profiling/PerTickAttributionRing.cs` (β12)
- `Profiling/TickFrame.cs` (if Frame data-shape changes per allocation-tracking §5.13)
- `Tests/MetricCollectorTickTests.cs` (new — zero-alloc per tick assertion)

**Steps:**
1. β1: `MetricCollector.BeginTick/EndTick` `DateTimeOffset.UtcNow` × 2 → `Time.UnixMsNow()`.
2. β2: `StallDetector.OnTick` `DateTimeOffset.UtcNow` deferred to stall-fired branch.
3. β3: 2× `GC.GetTotalPauseDuration` → one shared call into `GcCounterSnapshot` struct passed down.
4. β5: `[AggressiveInlining]` on `ProbeStack.Enter/Leave/EnterCpuAlloc/LeaveCpuAlloc` + Frame data-shape change (co-locate ticks+bytes, inline ModId/CategoryId).
5. β6: `[AggressiveInlining]` on `PerModAttribution.Add` + `const int CategoryCount = 7`.
6. β7: hoist `Stopwatch.Frequency` reciprocal as a precomputed `double _ticksToMs` field.
7. β8: fuse `SumAll` into smoothing loop (`UpdateRollingAverage` returns the sum).
8. β9: remove the 3-arg `PerModAttribution.Add` overload (callers all migrated).
9. β10: `IReadOnlyList<HookDescriptor>` → flat `HookDescriptor[]` exposed by ref.
10. β11: combine collector-boundary Stopwatch + GC reads into single struct.
11. β12: `PerTickAttributionRing` capacity rounded to 2048 (was 1800), mask replaces `%`.
12. β21: `Process.GetCurrentProcess() + TotalProcessorTime` → `Environment.CpuUsage.TotalTime`.
13. β22: `CaptureTopContributors` `IReadOnlyList<double>?` → `double[]?`.
14. **β4** (SpikeDetector snapshot allocation): introduce pool-backed snapshot slots. The float[] arrays are now Rented from `RowPool<SpikeSnapshotSlot>` at window open and Returned at window close (or in cluster coalesce). Uses α3 RowPool.
15. New test `Tick_Standard_AllocatesZeroBytes`: runs MetricCollector through 100 synthetic ticks, asserts `GC.GetAllocatedBytesForCurrentThread()` delta = 0.
16. Commit: "v0.6 Phase β: zero-allocation per-tick metric collection + stall/spike/attribution-ring tightening".

**Test plan:** zero-alloc test + every existing test still passes + BDN `MetricCollector.Tick` reports 0 B/op + per-tick cost regression check (must decrease vs Phase 0 baseline).

**Rollback:** revert files. Removes inlining hints, restores Process.GetCurrentProcess, etc. All semantics preserved.

**Expected outcome:** per-tick PerformanceProfiler cost drops from 0.27 ms toward 0.20 ms. Zero-alloc-per-tick passes.

---

### Commit C8 — Phase β2: events-and-context per-tick (β13–β20, β23)

**Scope:** the ContextTransitionWatcher + EventAggregator + WorldSnapshotter + BossSampler tightening. Separated from C7 because these touch a different file set (events surface) and a separate commit isolates blast radius.

**Files modified:**
- `Profiling/Persistence/ContextTransitionWatcher.cs` (β13, β14, β16)
- `Profiling/Events/BiomeRegistry.cs` (β17 — biome DisplayName arrays)
- `Profiling/Events/BossSampler.cs` (β20)
- `Profiling/Events/EventAggregator.cs` (β19)
- `Profiling/Events/SubworldProbe.cs` (β15)
- `Profiling/Persistence/WorldSnapshotter.cs` (β23)
- `Profiling/ProfilerSystem.cs` (β18 — fuse with BossSampler call)

**Steps:**
1. β13: word-level XOR + `BitOperations.TrailingZeroCount` in `DiffBiomeBits`.
2. β14: bit-walk weather diff. This includes the latent flag-identity bug fix (events-and-context R5): emit `Type` as `"weather:flagName"` not flat `"weather"`. This is a `ContextTransitionRow` schema bump but the schema infrastructure lands in δ — for now, we add the new typed Type but write the old flat Type alongside. Schema bump happens later in δ.
3. β15: compiled delegate for `SubworldProbe.Sample`.
4. β16: defer `Lang.GetNPCNameValue` to allocation branch.
5. β17: pre-resolve biome `DisplayName` into `string[]` at `PostSetupContent`. Uses α2 LangNameCache pattern but biome-specific (not folded into the general cache because biomes are a different id space).
6. β18: fuse `BossSampler.Sample` with `ProfilerSystem.CountActive(Main.npc)` — one walk over Main.npc[] populates both.
7. β19: latch active-keys hash sets in `EventAggregator`.
8. β20: `BossSampler._nameCache` `Dictionary` → `string[]` keyed by NPC type.
9. β23: pass `itemCount` into `WorldSnapshotter.OnTick`.
10. Commit: "v0.6 Phase β (events): zero-allocation context watcher + boss sampler + subworld probe + biome bit diff".

**Test plan:** existing tests still pass. New synthetic biome-diff test (covers β13 + β14) with multi-bit changes asserting correct edge emission order.

**Rollback:** revert files.

**Expected outcome:** events-watcher per-tick cost drops 680 → ~6 ns on the biome-diff hot path. Weather transitions now carry flag identity (forward-only; old rows still readable per δ migration).

---

### Commit C9 — Phase γ1: InteractionPlayer pooled rows + LangNameCache (γ1–γ4)

**Scope:** the biggest single commit by line-count delta. Pool the rows that fire on every damage event, every hit, every buff edge, every equip update. Use α infrastructure throughout.

**Files modified:**
- `Profiling/Persistence/Interactions/InteractionPlayer.cs` — every emit site
- `Profiling/Persistence/Records/DamageTakenRow.cs` — implement `IPoolReset`
- `Profiling/Persistence/Records/DamageDealtRow.cs` — implement `IPoolReset`
- `Profiling/Persistence/Records/BuffEventRow.cs` — implement `IPoolReset`
- `Profiling/Persistence/Records/LoadoutSnapshotRow.cs` — implement `IPoolReset`, slots list pooled
- `Profiling/Persistence/SessionRecorder.cs` — `OnBuffEvent` etc accept the pooled row, return it to pool after Apply

**Steps:**
1. Each Row gets an `IPoolReset.Reset()` implementation that clears every field to default.
2. Each `On*` hook in `InteractionPlayer.cs` switches to `RowPool<TRow>.Rent()`, fills, calls `recorder.OnXxx(row)`. The recorder forwards to writer thread; the writer's Apply method calls `RowPool<TRow>.Return(row)` after the LiteDB upsert.
3. `LoadoutSnapshotRow.Slots` (list of EquipmentSlotEntry) switches to `ListPool<EquipmentSlotEntry>.Rent()`. Cleared on Return.
4. `SnapshotActiveBuffTypes` returns a pooled `List<int>` (was `new List<int>()`).
5. The fingerprint StringBuilder is a field on `InteractionPlayer`, cleared at start of `PostUpdateEquips`. NOT new per call.
6. Replace `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` with `Time.UnixMsNow()` at every emit site.
7. Replace `Lang.GetBuffName(t)` with `LangNameCache.Buff(t)`.
8. Replace `Lang.GetItemName(t)` / `Lang.GetProjectileName(t)` similarly.
9. The try/catch wrappers around Lang lookups are removed — `LangNameCache` returns the cached result or the fallback string deterministically. The cache itself wraps the one-time Lang call in try/catch at `PostSetupContent`.
10. Commit: "v0.6 Phase γ (InteractionPlayer): pool every per-event row + use LangNameCache + remove DateTime/try-catch from hot path".

**Test plan:** existing InteractionPlayerBuffDiffTests + PlayerDeathAttributionTests + new `Tests/InteractionPlayerAllocTests.cs` asserting that 1,000 synthetic damage events allocate < 1 KB total.

**Rollback:** revert files. Big rollback; many call sites change. Each touched row has its pre-pool path preserved by the IPoolReset semantics (Reset() restores default state).

**Expected outcome:** BDN `Enqueue_GameThread_Latency` drops from 441 → ~280 ns/op (rest of the regression comes from δ12). Per-event allocation profile reads as < 1 KB / 1k events.

---

### Commit C10 — Phase γ2: InteractionNpc + InteractionItem + ContextTransition + WorldSnapshot row pools (γ5–γ8, γ10–γ12)

**Scope:** the remaining trackers. Smaller per-emit cost than InteractionPlayer but same pattern.

**Files modified:**
- `Profiling/Persistence/Interactions/InteractionNpc.cs` (γ5)
- `Profiling/Persistence/Interactions/InteractionItem.cs` (γ6 — includes A1 finalisation)
- `Profiling/Persistence/ContextTransitionWatcher.cs` (γ7)
- `Profiling/Persistence/WorldSnapshotter.cs` (γ8)
- `Profiling/Persistence/Records/NpcSpawnRow.cs` (IPoolReset)
- `Profiling/Persistence/Records/ItemCreatedRow.cs` (IPoolReset)
- `Profiling/Persistence/Records/ContextTransitionRow.cs` (IPoolReset)
- `Profiling/Persistence/Records/WorldSnapshotRow.cs` (IPoolReset)
- `Profiling/Persistence/Records/PlayerDeathRow.cs` (IPoolReset — also touches A3)
- `Profiling/Persistence/SessionRecorder.cs` (γ10 — lazy ObjectId; γ11 — BoolIndex for buff diff)
- `Profiling/Persistence/Interactions/InteractionPlayer.cs` (γ11 — switch to BoolIndex)
- `Profiling/Persistence/DbWriteOp.cs` (γ10 — ObjectId field set later)

**Steps:**
1. Pool every row type listed.
2. γ10: `ObjectId.NewObjectId()` deferred — DbWriteOp carries a placeholder; the writer-thread Apply assigns the ObjectId at Apply time.
3. γ11: `InteractionPlayer.PostUpdateBuffs` switches buff-diff from `Array.IndexOf` to `BoolIndex` (α7). The diff becomes O(1) per buff.
4. γ12: substring stripping (`"EntitySource_"`, `"ItemCreationContext"`) cached in a static dictionary populated once on first encounter.
5. Commit: "v0.6 Phase γ (npc/item/context/snapshot/death): pool every remaining per-event row + BoolIndex buff diff + lazy ObjectId".

**Test plan:** new allocation tests for each tracker type (NpcSpawn, ItemCreated, ContextTransition, WorldSnapshot, PlayerDeath) — each asserts < 1 KB / 1k events.

**Rollback:** revert files.

**Expected outcome:** writer-thread queue depth shrinks (DbWriteOp gets lighter). Enqueue latency continues to drop.

---

### Commit C11 — Phase δ1: schema migration scaffold (δ1, δ8)

**Scope:** lay the migration plumbing without making the on-disk shape changes yet. Adds the `USER_VERSION 6 → 7` bump + the migration step skeleton + the round-trip test fixtures. Lets later δ commits be smaller because the scaffold is in place.

**Files modified:**
- `Profiling/Persistence/ProfilerDatabase.cs` (bump `USER_VERSION`)
- `Profiling/Persistence/Migrations.cs` (register `v6_to_v7` step)
- `Tests/Persistence/PersistenceMigrationTests.cs` (new — but no real migration yet, just scaffold)
- `Tests/Fixtures/v0_5_database.litedb` (new — a hand-crafted v0.5-state DB checked into the repo for migration testing)

**Steps:**
1. Bump USER_VERSION 6 → 7.
2. Migrations.cs registers a step that opens each affected collection. The step is currently a no-op; later δ commits add the per-collection migrations.
3. The v0.5 fixture DB is generated once by running the v0.5 build against a fresh DB, capturing it, checked in.
4. New test: open the v0.5 fixture, run migration, assert collection counts preserved. No row-content changes yet.
5. Commit: "v0.6 Phase δ scaffold: USER_VERSION 6→7 + migration step skeleton + v0.5 fixture DB".

**Test plan:** scaffold migration runs; existing persistence tests pass.

**Rollback:** revert files; remove fixture DB.

**Expected outcome:** no shape change yet; just scaffold.

---

### Commit C12 — Phase δ2: BSON short field names + `_schema → v` (δ3)

**Scope:** every record gets `[BsonField("...")]` attributes mapping long C# property names to short BSON names. `_schema` becomes `v`. Reader fallback for v0.5-shaped rows.

**Files modified:**
- Every file under `Profiling/Persistence/Records/*.cs` (~20+ records)
- `Profiling/Persistence/ProfilerDatabase.cs` (register two BsonMappers — short-name v≥2, long-name v≤1)
- `Profiling/Persistence/Migrations.cs` (add v6_to_v7 Phase 2)
- `Tests/Persistence/PersistenceMigrationTests.cs` (assertion that v0.5 fixture migrates without row loss)

**Steps:**
1. Add `[BsonField("...")]` to every property of every Record. Use 1–4 character abbreviations (e.g. `SessionId` → `sid`, `UnixMs` → `t`, `BuffName` → `bn`, etc.). Cross-storage-ram §4.1 has the proposed mapping table.
2. Rename `_schema` field to `v` across every record.
3. BsonMapper registration: long-name (legacy) reader for v≤1, short-name (current) for v≥2.
4. Migration: read row at long-name, rewrite at short-name. Done per row at read time, batched by `v` field.
5. Commit: "v0.6 Phase δ: BSON short field names + `_schema → v` (Phase 2 of v6→v7 migration)".

**Test plan:** PersistenceRoundTrip tests pass round-trip at v0.6 shape; migration test confirms v0.5 fixture reads correctly.

**Rollback:** revert files. v0.6 DBs written so far become unreadable to the v0.5 build (one-way migration — documented).

**Expected outcome:** ~400 KB / 10-min saved per cross-storage-ram §4.1.

---

### Commit C13 — Phase δ3: FK swap + numeric blobs + byte enums (δ4–δ6)

**Scope:** the three biggest BSON-layer wins.

**Files modified:**
- `Profiling/Persistence/Records/DamageDealtRow.cs` (FK swap)
- `Profiling/Persistence/Records/SpikeWindowRow.cs` (numeric blob)
- `Profiling/Persistence/Records/TickAggregateRow*.cs` (numeric blobs)
- `Profiling/Persistence/Records/StallEventRow.cs` (byte enums)
- `Profiling/Persistence/Records/StallClusterRow.cs` (byte enums)
- `Profiling/Persistence/Records/ContextTransitionRow.cs` (typed Type field finalised — β14 work lands here)
- `Profiling/Persistence/Records/InsightRow.cs` (byte severity)
- `Profiling/Persistence/Migrations.cs` (Phase 3–5)
- All consumers of these rows (UI, insight engine, queries) — bump their reads

**Steps:**
1. δ4 FK swap: `DamageDealtRow.LoadoutFingerprint` (string) replaced by `LoadoutSnapshotId: ObjectId`. Migration walks loadoutSnapshots collection for each session, matches fingerprint string, writes the ObjectId. If not found, leaves `ObjectId.Empty` + retains legacy field.
2. δ5 Numeric blobs: `SpikeWindowRow.PerModCatMs` (`BsonArray` of doubles) → `byte[]` blob. Same for `TickAggregate` arrays. The byte[] is `float[]` packed (4 bytes/sample, ~half the size).
3. δ6 Byte enums: `StallEventRow.Cause` / `Severity` (string) → byte enum value. `StallClusterRow.Cause` likewise. `ContextTransitionRow.Type` becomes `"category:flag"` per β14 finalisation. `InsightRow.Severity` byte enum.
4. Migrations: walk each affected collection, transform rows.
5. UI/insight readers: update to read the new types.
6. Commit: "v0.6 Phase δ: FK swap + numeric BSON blobs + byte enums (Phase 3–5 of v6→v7 migration)".

**Test plan:** migration test against v0.5 fixture; PersistenceRoundTrip on v0.6 shape; new tests for the FK migration edge cases (missing snapshot, etc.).

**Rollback:** revert files.

**Expected outcome:** another ~200 KB / 10-min saved. Combined with C12 hits the < 600 KB target.

---

### Commit C14 — Phase δ4: writer-thread throughput (δ9–δ14)

**Scope:** the things that move ops/sec from 314 → > 1,000.

**Files modified:**
- `Profiling/Persistence/EventJournal.cs` (δ9 binary frames, δ11 ArrayPool)
- `Profiling/Persistence/IPersistenceStream.cs` (δ13 add `BulkApply` method)
- Every stream under `Streams/` (δ13)
- `Profiling/Persistence/DbWriterThread.cs` (consume new BulkApply; δ12 struct union; δ14 deferred indexes)
- `Profiling/Persistence/Migrations.cs` (Phase 6 journal format flip)
- `Profiling/Persistence/DbWriteOp.cs` (δ12)
- `Tests/Persistence/PersistenceBenchmarkTests.cs` (re-measure expected to show > 1,000)

**Steps:**
1. δ9 binary journal: write Utf8JsonWriter directly to an ArrayPool-rented byte buffer; flush to disk via the journal file with a frame header.
2. δ11 ArrayPool for BsonSerializer.Serialize byte[] per Upsert.
3. δ13 InsertBulk: streams that batch N rows per Apply use `LiteCollection<T>.Insert(IEnumerable<T>)`.
4. δ14 Deferred non-unique index creation: `EnsureIndexes` runs lazily on first read instead of at open.
5. δ12 DbWriteOp discriminated struct union (high-risk): replace `object Payload` with a fixed-set struct that carries the largest payload by value. Boxing eliminated.
6. Commit: "v0.6 Phase δ: writer-thread throughput — binary journal, ArrayPool, InsertBulk, deferred indexes, DbWriteOp struct union".

**Test plan:** PersistenceBenchmarkTests must show > 1,000 ops/sec drain; journal-replay test exercises the binary format; existing PersistenceRoundTrip still passes.

**Rollback:** revert files. δ12 is the highest-risk piece; can be deferred to a follow-up commit if soak issues.

**Expected outcome:** writer drain 314 → > 1,000 ops/sec; enqueue 280 → < 200 ns/op (the struct union closes the rest of the gap from γ).

---

### Commit C15 — Phase δ5: compound indexes (δ15) + writer-side spike/stall contributor structs (δ16, δ17)

**Scope:** the index reads insights-engine and the contributor-build paths.

**Files modified:**
- `Profiling/Persistence/Streams/TickAggregateStream.cs` (compound index)
- `Profiling/Persistence/Streams/LoadoutStream.cs` (compound index)
- `Profiling/Persistence/Streams/BuffStream.cs` (compound index)
- `Profiling/Persistence/SessionRecorder.cs` (BuildSpikeTopContributors, BuildStallTopContributors)

**Steps:**
1. Compound `(SessionId, SecondIndex)` on TickAggregatesWarm.
2. Compound `(SessionId, Reason, UnixMs)` on LoadoutSnapshots.
3. Compound `(SessionId, UnixMs)` on BuffEvents.
4. δ16 Spike top-K: replace List<SpikeContributor> with fixed `SpikeContributor[5]` struct array.
5. δ17 Stall top-K similarly.
6. Commit: "v0.6 Phase δ: compound indexes for insight queries + struct-array contributor builds".

**Test plan:** insight detector pass time should drop. Existing tests pass.

**Rollback:** revert files.

**Expected outcome:** sets up Phase ζ.

---

### Commit C16 — Phase ε1: heap-snapshot diagnostic + session-end relocation (ε1–ε5)

**Scope:** the headline 8.5-s stall fix.

**Files modified:**
- `Profiling/ProfilerSelfHealth.cs` (ε1 heap snapshot capture)
- `Profiling/Persistence/SessionRecorder.cs` (ε2 SessionEndAggregate op)
- `Profiling/Persistence/DbWriteOp.cs` (add `DbOpKind.SessionEndAggregate`)
- `Profiling/Persistence/Streams/*.cs` (writer-side Apply for SessionEndAggregate)
- `Profiling/Persistence/SessionSummaryLogger.cs` (ε4 fold into writer tail)
- `Profiling/Persistence/DbWriterThread.cs` (ε5 deterministic completion signal)
- `Profiling/ProfilerSystem.cs` (`OnWorldUnload` enqueues op, doesn't block)

**Steps:**
1. ε1 heap snapshot: capture `GC.GetTotalMemory(forceFullCollection: true)` immediately before and after the hook install. Log delta. This becomes the diagnostic for the ALLOC-1 decision.
2. ε2 OnWorldUnload session-end aggregation moves to a `SessionEndAggregate` op.
3. ε3 BuildModAggregates + BuildHookAggregates run on writer thread with pooled buffers.
4. ε4 SessionSummaryLogger.Write folded into the writer-thread tail.
5. ε5 Busy-wait drain replaced with `ManualResetEventSlim` on the op.
6. Commit: "v0.6 Phase ε: relocate session-end aggregation to writer thread (8.5s → <200ms)".

**Test plan:** new test for SessionEndAggregate op round-trip. In-game playtest measures the stall reduction.

**Rollback:** revert files. Session-end work returns to main thread.

**Expected outcome:** end-of-session UiOverlayBlocking stall 8.5 s → < 200 ms.

---

### Commit C17 — Phase ε2: PreSaveAndQuit overlap + deferred OnWorldLoad construction (ε6, ε7)

**Scope:** the first-tick freeze fix.

**Files modified:**
- `Profiling/ProfilerSystem.cs` (PreSaveAndQuit hook + defer construction)
- `Tests/ProfilerSystemLifecycleTests.cs` (new — synthetic lifecycle test)

**Steps:**
1. ε6 PreSaveAndQuit override starts the session-end snapshot before vanilla save.
2. ε7 SessionRecorder + watchers built lazily on first `PostUpdateEverything`, not in `OnWorldLoad`.
3. Commit: "v0.6 Phase ε: PreSaveAndQuit overlap + defer SessionRecorder construction to first tick (172ms → ~110ms freeze)".

**Test plan:** new lifecycle test; in-game playtest measures.

**Rollback:** revert files.

**Expected outcome:** world-load freeze 172 → ~110 ms.

---

### Commit C18 — Phase ε3: T5 install worker thread (ε8) + HookSurfaceCache (ε9)

**Scope:** the async install plus the type-walk dedup.

**Files modified:**
- `Profiling/HookInterceptor.cs` (HookSurfaceCache)
- `Profiling/ILHookInterceptor.cs` (BeginInstallAsync + read from HookSurfaceCache)
- `Profiling/ProfilerSelfHealth.cs` (install-progress reporting)
- `Profiling/ProfilerSystem.cs` (kick off install on Mod.Load worker thread)
- `Tests/HookSurfaceCacheTests.cs` (new)

**Steps:**
1. ε9 HookSurfaceCache: shared `Dictionary<ModId, List<HookDescriptor>>` populated once; both backends read from it.
2. ε8 BeginInstallAsync: the actual IL patching moves to a worker thread (T5). Coverage rises monotonically as hooks land. ProfilerSelfHealth reports progress.
3. Cross-concurrency §6.2 abort-clean contract: if T5 fails mid-install, mark instrumentation disabled, log error, do not retry.
4. Commit: "v0.6 Phase ε: async hook install on T5 + HookSurfaceCache deduplication".

**Test plan:** synthetic mod-load against a small test assembly; assert install completes; assert install-progress monotonic.

**Rollback:** revert files. Install returns to synchronous on Mod.Load worker.

**Expected outcome:** Mod.Load-blocking time 10–18 s → 1–2 s (install runs in background); install delta in steady state reduced by HookSurfaceCache by 80–150 MB independent of ALLOC-1.

---

### Commit C19 — Phase ε4: ALLOC-1 conditional commit (ε10–ε13)

**Scope:** **gated on the heap-snapshot result from C16/ε1.** If Cecil dominates the 233 MB (> 50%), commit ALLOC-1. If not, ship a smaller win (interning + pre-sizing) and document the gating result for a future pass.

**Files modified (if Cecil dominant):**
- `Profiling/ILHookInterceptor.cs` (dispose ILContext / DynamicMethodDefinition after install)
- `Profiling/HookInterceptor.cs` (similar pattern)
- `Profiling/ProfilerSelfHealth.cs` (record post-install heap delta as baseline check)

**Files modified (always):**
- `Profiling/ILHookInterceptor.cs` (ε11 intern DisplayName strings; ε12 pre-size _installedHooks list)
- `Profiling/ProfilerSelfHealth.cs` (ε13 replace forced Gen2 with allocation-delta)

**Steps:**
1. Read the ε1 heap snapshot result. If `installDelta_Cecil > installDelta * 0.5`, proceed with ALLOC-1.
2. ε10 ALLOC-1: dispose ILContext after each hook install. Hooks remain installed; the import context is what gets freed.
3. ε11 Intern DisplayName strings.
4. ε12 Pre-size _installedHooks.
5. ε13 Replace SelfHealth forced Gen2 with allocation-delta.
6. Commit: "v0.6 Phase ε: ALLOC-1 (conditional) + DisplayName interning + SelfHealth allocation-delta".

**Test plan:** install-time RAM delta benchmark; reload-cycle heap-leak test.

**Rollback:** revert files. Note ALLOC-1 is high-risk for hook longevity — if a hook fires after dispose and triggers a re-emit, things break. Verify there's no re-emit path.

**Expected outcome (if ALLOC-1 lands):** install delta 233 → < 80 MB. Otherwise: install delta 233 → ~150 MB from ε11/ε12/HookSurfaceCache combined.

---

### Commit C20 — Phase ε5: Mod.Close idempotency + reload-cycle leak test (ε14, ε15)

**Scope:** defensive hardening for the new thread model.

**Files modified:**
- `Profiling/ProfilerSystem.cs` (Mod.Close override with Interlocked.CompareExchange)
- `PerformanceProfiler.cs` (related cleanup paths)
- `Tests/ModLifecycleReloadTests.cs` (new — reload-cycle heap-leak test)

**Steps:**
1. ε14 Idempotent Mod.Close.
2. ε15 New test: simulate Mod.Unload → Mod.Load cycle 10 times; assert heap doesn't grow monotonically.
3. T5 thread abort-clean: verify cancellation during shutdown is clean.
4. Commit: "v0.6 Phase ε: idempotent Mod.Close + reload-cycle leak guard".

**Test plan:** new lifecycle reload test.

**Rollback:** revert files.

---

### Commit C21 — Phase ζ: insight detector LINQ removal (ζ1–ζ5)

**Scope:** the insight engine's LINQ chains → indexed queries + pooled buffers, plus T6 reader thread (if soak passes).

**Files modified:**
- `Profiling/Insights/Detectors/InteractionInsightDetectors.cs` (ζ2, ζ3)
- `Profiling/Insights/Detectors/AllocationBurstDetector.cs` (ζ1)
- `Profiling/Insights/Detectors/GcPauseCulpritDetector.cs` (ζ1, ζ5)
- `Profiling/Insights/InsightsEngine.cs` (ζ4 — T6 reader thread, with soak gate)
- `Tests/InsightDetectorTests.cs` (assert byte-for-byte equivalence pre/post)

**Steps:**
1. ζ1 Promote per-pass `new double[modCount]` to fields.
2. ζ2 LoadoutCorrelatedCost LINQ chains → indexed range scans (use δ15 compound indexes).
3. ζ3 EventConditionalCost similar.
4. ζ4 T6 reader thread (Option A). Run LiteDB read-while-write soak test:
   - Soak: writer thread writes at peak rate; reader thread queries indexed collection 100× / sec for 60 s.
   - Pass = no crashes, no deadlocks, query latency < 50 ms p99.
   - **If soak fails**, fall back to Option B (extend writer thread with ReadOp enum).
5. ζ5 GcPauseCulprit stall-cursor.
6. Commit: "v0.6 Phase ζ: insight detector LINQ removal + indexed queries + T6 reader thread".

**Test plan:** byte-equivalence test between old LINQ output and new pass output across 1000 synthetic sessions. T6 soak test passes.

**Rollback:** revert files. Insight detectors return to synchronous LINQ.

**Expected outcome:** insight pass < 4 KB allocated per pass. Detector latency < 5 ms.

---

### Commit C22 — Phase η1: overlay mount glue + per-frame allocation removal (η1–η3, η9)

**Scope:** the easy overlay wins.

**Files modified:**
- `UI/ProfilerOverlaySystem.cs` (η1, η2)
- `UI/Overlay/OverlayPanel.cs` (η3)
- `UI/Overlay/DonutChart.cs` (η9)

**Steps:**
1. η1 Cache `LegacyGameInterfaceLayer` instance.
2. η2 Cache `GameTime` instance.
3. η3 `_layoutCache.StatCardRects` field.
4. η9 `DonutChart` `foreach (pass in e.CurrentTechnique.Passes)` → indexer.
5. Commit: "v0.6 Phase η: overlay mount glue + per-frame allocation removal".

**Test plan:** allocation-aware test for the overlay mount path. In-game playtest verifies no visible regression.

**Rollback:** revert files.

---

### Commit C23 — Phase η2: tab format caches (η4–η8, η10, η11, η12, η13)

**Scope:** the bigger overlay work — tab-level caches.

**Files modified:**
- `UI/Overlay/Tabs/OverviewTab.cs` (η4)
- `UI/Overlay/Tabs/TreeTab.cs` (η5)
- `UI/Overlay/Tabs/SpikesTab.cs` (η6, η12)
- `UI/Overlay/Tabs/InsightsTab.cs` (η7)
- `UI/Overlay/OverlayDraw.cs` (η8 FormatBytes LRU)
- `UI/Overlay/Components/DonutChart.cs` (η10 vertex array reuse + slice-hash invalidation)
- `UI/Overlay/Components/Sparkline.cs` (η11 ReadOnlySpan overload)

**Steps:**
1. η4–η7 Tab format strings cached at 1 Hz Tick. Cache invalidates on data version-counter change.
2. η8 OverlayDraw.FormatBytes LRU cache (16-entry).
3. η10 DonutChart vertex array reuse with slice-hash invalidation at 1 Hz.
4. η11 Sparkline.Render gets a ReadOnlySpan<double> overload; old IReadOnlyList<double> overload kept for back-compat.
5. η12 SpikesTab.RebuildTimelineMarks 60 → 1 Hz throttle.
6. η13 End/Begin collapse for related batched draws.
7. Commit: "v0.6 Phase η: tab format caches + donut vertex reuse + sparkline span overload + 1 Hz timeline throttle".

**Test plan:** `OverlayDraw_AllocatesZeroBytes` test passes for every tab. 60-s overlay-open soak < 10 KB total alloc.

**Rollback:** revert files.

**Expected outcome:** draw thread per-frame allocation → ~0.

---

### Commit C24 — Phase 8 wrap: housekeeping (W1–W7)

**Scope:** the warnings + version bump + decisions entry.

**Files modified:**
- `ProfilerConfig.cs` (W1)
- `Localization/en-US_Mods.PerformanceProfiler.hjson` (W1)
- `Profiling/Persistence/Interactions/InteractionPlayer.cs` (W2, W3)
- `Profiling/Persistence/Interactions/InteractionNpc.cs` (W3)
- `Tests/PerformanceProfiler.Tests.csproj` (W4)
- `build.txt` (W5: 0.5 → 0.6)
- `context/notes/decisions.md` (W7: v0.6 entry)
- `context/perf-pass/verification.md` (W6: new file with re-measured baseline)
- `README.md` (if any user-visible thing changed — shouldn't have)

**Steps:**
1. W1 ProfilerConfig migration to [LabelKey]/[TooltipKey] + localization entries (or delete attrs).
2. W2 SourceCustomReason → CustomReason rename.
3. W3 ChangeMagicNumberToID × 2.
4. W4 CS0649 NoWarn on the linked EventContext file in Tests.csproj.
5. W5 build.txt bump.
6. W6 verification.md captures the post-pass numbers row-by-row vs baseline.md.
7. W7 decisions.md entry.
8. Commit: "v0.6: wrap perf pass — version bump, decisions entry, housekeeping warnings cleaned".

**Test plan:** every existing test passes. New verification.md numbers documented.

**Rollback:** revert files.

---

## 3. Total commit count: 24

| Phase | Commits | Days |
|---|---:|---|
| Phase 0 | 1 (C1) | 0.1 |
| Phase A | 4 (C2–C5) | 1.5 |
| Phase α | 1 (C6) | 2.5 |
| Phase β | 2 (C7–C8) | 1.5 |
| Phase γ | 2 (C9–C10) | 3.5 |
| Phase δ | 5 (C11–C15) | 5 |
| Phase ε | 5 (C16–C20) | 3.5 |
| Phase ζ | 1 (C21) | 1.5 |
| Phase η | 2 (C22–C23) | 2.5 |
| Phase 8 | 1 (C24) | 0.5 |
| **Total** | **24** | **~22** |

The day estimate is wall-clock for a working agent; not real calendar days.

---

## 4. Verification ladder (test-harness §6)

Three layers, all run before each commit:

- **L1 — xUnit smoke (every `dotnet test`).** Runs in seconds. Asserts: existing 54 tests pass; new tests pass; zero-alloc-per-tick after C7; per-event < 1 KB / 1k events after C10; insight pass < 4 KB after C21; overlay alloc near-zero after C23.
- **L2 — BDN statistical (commit and nightly).** Re-runs all 4 baseline benchmarks + the new ones (`MetricCollector.Tick`, `ProbeStack.EnterLeave`, `GC.GetAllocatedBytesForCurrentThread`, per-stream Enqueue + Apply, journal Append, insights pass, session-end aggregation, hook install). Each gates with `--statisticalTest 3%`.
- **L3 — JSON drift gate (nightly + wrap-up).** Per-commit baseline JSON captured under `context/perf-pass/baselines/<commit-hash>/`. Drift detector compares against prior commit; flags any cumulative > 5% regression.

Implementation commits in Phase 6 just run L1 per commit. L2 + L3 run at the wrap-up.

---

## 5. Risk register

The high-risk items and their mitigations:

| Risk | Mitigation |
|---|---|
| ALLOC-1 (ε10) — dispose ILContext might break hook re-emission paths | Gated on heap snapshot result (C19); if Cecil < 50% of delta, skip ALLOC-1 |
| δ12 (DbWriteOp struct union) — high-risk, can be deferred | Land C14 first, soak-test, defer struct union to follow-up if issues |
| T6 reader thread (ζ4) — LiteDB read-while-write semantics | Soak test gates (C21); fallback to Option B (extend writer) |
| BSON short field names (C12) — breaking schema, v0.5 DBs become readable only via migration | Migration test against v0.5 fixture (C11–C12) |
| T5 install worker (ε8) — abort-clean discipline interaction | Abort-clean contract test in cross-concurrency §9 (lands with C18) |
| Per-tick zero-alloc claim — silent regression risk | xUnit `Tick_Standard_AllocatesZeroBytes` test runs every commit |

---

## 6. Open questions (logged for wrap-up, not blocking)

These do not gate the implementation but get tracked:

1. **FCall cost actual** — α8 result. If > 50 ns, flag for follow-up about whether to make alloc tracking optional in Lite mode (currently unconditional per philosophy).
2. **Cecil dominance %** — ε1 result. Determines ALLOC-1 commit decision.
3. **LiteDB read-while-write semantics** — ζ4 soak result.
4. **T7 collector smoother** — deferred to v0.7 unless metric-collection's per-tick target isn't met after Phase β.
5. **Buff-edge retest** — A2 verification needs an in-game potion-use playtest to confirm the diff is correct.

---

## 7. Out-of-scope reminders (from coherence §9)

For the implementer (me, Phase 6) to bounce off if tempted:

- No removal or lightening of any feature.
- No new feature flags or runtime toggles to "disable" anything.
- No "drop tier", "skip stream", "sample 1 in N", "lower snapshot rate", or similar.
- No mod-specific code.
- No game-state mutation.
- No deferral of correctness fixes to a later version.
- No push without explicit permission (after C24).

---

## 8. Entry point for Phase 6

Phase 6 begins with **C1** (Release baseline recapture). Then C2 sequentially. No skipping. Each commit must build + test green before the next begins. If a commit fails on test or build, halt, surface the failure, do not advance.

The implementer reads this plan top-to-bottom. The 15 research dossiers in `research/` are reference material when a specific design choice needs deeper grounding. Coherence.md is the conflict-resolution authority.

**Total expected outcome:** every baseline.md target moves in the better direction, no capture surface lost, no UI density reduced, no insight removed, no feature lightened. The mod is invisible-to-user faster.

---

*Phase 6 = execute this. No new research, no new design decisions — every one is settled in this file or the underlying dossiers.*
