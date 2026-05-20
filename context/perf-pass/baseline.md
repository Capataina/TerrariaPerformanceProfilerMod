# Performance Pass — Baseline (v0.5)

> Numbers captured 2026-05-20, before the v0.6 optimisation pass begins. Every claim in `research/*.md` and every "expected delta" in `master-plan.md` is measured against this file.

---

## 1. Synthetic benchmarks (xUnit `PersistenceBenchmarkTests`)

Captured by `dotnet test Tests/PerformanceProfiler.Tests.csproj --filter "Benchmark"` on the dev machine (M-series Apple Silicon, .NET 8). Both Debug (default) and Release (`-c Release`) configs captured — the test-harness research doc flagged the Debug-only capture as a calibration trap.

| Benchmark | v0.5 Debug | v0.5 Release (run 1 / run 2) | v0.3 prior | Delta vs v0.3 |
|---|---|---|---|---|
| Game-thread enqueue latency | **441.2 ns/op** | **570 / 555 ns/op** | 276 ns/op (Debug) | **+60%–+106% regression since v0.3** |
| Writer-thread sustained drain | **314 ops/sec** | **314 / 320 ops/sec** | 310 ops/sec | flat (disk-bound, JIT-independent) |
| Read last-10 sessions from 50 | **0.426 ms** | **0.383 / 0.395 ms** | 0.39 ms | -10% in Release |
| Simulated 10-min session DB size | **1,064 KB** | **1,064 KB** | 752 KB | +41% (six new event streams) |

**Calibration note.** Release is *worse than* Debug on the enqueue benchmark (570 / 555 vs 441 ns/op). The most likely cause is tiered-JIT warmup: the synthetic bench runs only 10k ops in ~5 ms — too short for the tier-1 → tier-2 promotion to fire on the hot path. Debug skips tiered JIT and goes straight to tier-1, paradoxically giving lower variance on a microbench at this scale. The BDN suite (test-harness B-series) with proper `[GlobalSetup]` + warmup iterations will give the authoritative number; until then the **practical target is `< 200 ns/op` regardless of which baseline is real**. Both numbers indicate the same direction-of-travel: dramatic reduction needed.

The enqueue-latency regression is the most telling number. It is the cost the game thread pays for every event we capture; six new event streams in v0.5 (`damageTakenEvents`, `damageDealtEvents`, `npcSpawnEvents`, `itemCreatedEvents`, `loadoutSnapshots`, `buffEvents`) drove it from sub-300 ns up to 441–570 ns. The pass's primary target is to bring it back under 200 ns *without* dropping any stream.

The DB-size growth is similarly load-bearing: 41% more bytes per 10-minute session means storage-tier work (BSON shape, FK swap, numeric blobs, compaction) becomes a primary lever in this pass.

## 2. In-game playtest baseline (session `6a0dcea5`)

Real playtest, 16:09:25 → 16:14:20, 4 min 55 s wall time, 16,009 ticks (~4.5 min of in-world time).

| Surface | Value |
|---|---|
| Average frame ms | 0.96 ms |
| Max frame ms | 172.0 ms (world-enter freeze) |
| Spikes detected | 50 |
| Stalls detected | 50 |
| Stall clusters | 10 |
| Player deaths | 2 |
| Context transitions | 10 |
| World snapshots | 10 |
| Damage-taken events | 10 |
| Damage-dealt events | 354 |
| NPC spawn events | 34 |
| Item-created events | **0 (bug)** |
| Loadout snapshots | 41 |
| Buff lifecycle events | **2 (bug — first on/off pair only)** |
| End-of-session UiOverlayBlocking cluster | **40 stalls over 8.5 s** — contributor `PerformanceProfiler` itself |
| Mod-RAM (Mods using the most RAM) | PerformanceProfiler 234 MB, Verdant 78 MB, BossChecklist 58 MB |
| Hook install delta | **481 MB at first install, 322–618 MB across subsequent reloads, ~10,258 hooks installed → ≈23–60 KB/hook** |
| LiteDB on disk (after 5 sessions) | 9.5 MB |

### Top-3 mod CPU contributors in that session

| Rank | Mod | Avg ms/tick | Peak ms | Total ms |
|---|---|---|---|---|
| 1 | PerformanceProfiler | 0.27 | 0.3 | 4,488 |
| 2 | CheatSheet | 0.27 | 0.3 | 4,422 |
| 3 | Verdant | 0.23 | 0.2 | 3,877 |

The profiler itself was the top CPU contributor in its own session. That is the headline number this pass must move.

## 3. Hot-path / measurement surface inventory

Where the per-tick overhead actually accrues, by file:

| File | Per-tick role |
|---|---|
| `Profiling/MetricCollector.cs` | Frame timing, baseline tracking, GC-pause + alloc reads, focus probe call |
| `Profiling/PerModAttribution.cs` | Per-mod CPU + alloc aggregation each tick |
| `Profiling/PerTickAttributionRing.cs` | 1800-tick ring buffer of per-mod samples |
| `Profiling/RingBuffer.cs` | Generic ring buffer |
| `Profiling/SpikeDetector.cs` | Robust MAD-based spike classifier |
| `Profiling/StallDetector.cs` | Wall-vs-CPU gap detector + focus probe |
| `Profiling/ProbeStack.cs` | The `Enter/Leave` static targets emitted by IL hooks |
| `Profiling/ILHookInterceptor.cs` | IL emission for every hook on Load |
| `Profiling/Persistence/Interactions/InteractionPlayer.cs` | `OnHurt`, `OnHitNPC*`, `PostUpdateBuffs`, `PostUpdateEquips` |
| `Profiling/Persistence/Interactions/InteractionNpc.cs` | `GlobalNPC.OnSpawn` |
| `Profiling/Persistence/Interactions/InteractionItem.cs` | `GlobalItem.OnCreated` |

Anything in the per-tick hot path must stay zero-allocation (Invariant 2).

## 4. Known bugs to fix in this pass (not optimisations — correctness)

1. **`itemCreatedEvents = 0`** across a 4.5-min real session that included mining and torch placement. The `GlobalItem.OnCreated` surface in tML 1.4.4 fires only on craft, not on pickup or world-drop. Need to wire the correct generic surfaces (`ModPlayer.OnPickup` for pickups, plus the existing `OnCreated` for crafts, plus `IL.Terraria.NPC.NPCLoot` or equivalent for drops). All generic, no mod-specific code.
2. **`buffEvents = 2`** across the same session despite a constant Radar accessory and intermittent torch placement. The `PostUpdateBuffs` diff is either initialising the prev-buffs snapshot to match the live array (so nothing diffs) or indexing wrong. Pure-logic fix.
3. **Death attribution is last-hit-credit, not damage-weighted.** Death #1 reads "killed by Blue Slime" because the slime threw the final 21 dmg; vultures actually dealt 93/100 dmg. Fix: damage-weighted aggregation over the last N seconds before `dead = true`.

These ride along in implementation (Phase 6); the perf research phase ignores them.

## 5. Hard constraints for the pass (from `philosophy.md` + Invariants)

Every recommendation in `research/*.md` and every change in `master-plan.md` must satisfy:

- **Invariant 1**: Read-only instrumentation. No mutation of game/world/save state.
- **Invariant 2**: Per-tick hot path zero-allocation. Lite < 1%, Standard 2–4%, Deep 5–10% overhead.
- **Invariant 3**: Descriptive not normative. No new "core" / "removable" / "should drop" copy.
- **Invariant 4**: Abort-clean on host drift. Failed hook install disables instrumentation; never proceed against unverified internals.
- **Invariant 5**: No mod-specific code. Generic vanilla / tML surfaces only.

And the pass-specific rule from `philosophy.md`:

> **Optimisation = doing what we already do at maximum efficiency. It is not = doing less.** Never let the presentation/storage stack constrain what the data stack captures.

Invalid recommendations include:

- "Reduce the sampling rate of X"
- "Don't capture Y, it's redundant"
- "Aggregate Z so we throw away the individual events"
- "Skip the W tab to save UI draw cost"
- "Drop the V column from the schema"
- Anything else that *removes capture*, *lowers UI density*, *reduces event types*, *thins insight detail*, or *truncates the data stack* in any way.

Valid recommendations: alloc removal, struct repacking, cache reuse, work batching, off-main-thread relocation, IL emission tightening, downsampling tiers (where the raw is still captured and queryable), compaction, lazy materialisation, indexed lookups, queue/channel sizing, lock removal, prefetching, SIMD, span-based parsing, pool-backed buffers, and similar — anything that makes the same observable output cheaper to produce.

## 6. Target deltas

Aspirational, refined once `master-plan.md` is in place. Anything in the same direction is progress; the exact numbers will be set after research lands.

| Surface | Today | Target |
|---|---|---|
| Game-thread enqueue | 441 ns/op | < 200 ns/op |
| Writer-thread drain | 314 ops/sec | > 1,000 ops/sec |
| 10-min session DB | 1,064 KB | < 600 KB at same capture coverage |
| End-of-session main-thread stall | 8.5 s | 0 (moved off-thread) |
| Hook install delta | 233 MB / 10,258 hooks | < 80 MB at same coverage |
| Avg per-tick PerformanceProfiler cost | 0.27 ms | < 0.10 ms |
| Item-created events captured | 0 (bug) | every craft + pickup + drop |
| Buff lifecycle edges captured | 2 (bug) | every on/off edge |
| Death-cause attribution | last-hit credit | damage-weighted last N seconds |

The cumulative outcome that defines "this pass shipped": **every baseline number in this file moves in the better direction, no capture surface lost, no UI density reduced, no insight removed.**

---

*This file is the contract. Verification (Phase 7) compares the post-pass build against it row by row.*
