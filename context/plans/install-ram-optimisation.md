# Plan — Install-Path RAM Optimisation

> Goal: cut the profiler's install-time memory footprint (currently the single
> largest RAM consumer in any modlist) **without removing or weakening any
> instrumentation**. Same coverage, same per-mod/per-category/per-hook data,
> same emitted IL — just stop holding dead memory.
>
> Status: **safe tier + Step 2 EXECUTED** (2026-06-22 autonomous run, Step 2
> validated in-game shortly after). Harness restored, measurement made honest,
> hygiene fixes landed, and the structural RAM cut (Step 2 — trim retained
> `LastContext`) is live and verified: profiler RAM 3.7 → 1.0 GB (tML
> attribution), 58 → 30 KB/hook, coverage intact (62,203 detours, 62,203 contexts
> disposed). Only Step 3 (own the emission) remains open as the Step-2 fallback.
> See the execution log at the foot of this file.
>
> Date opened: 2026-06-22. Mod version at baseline: `0.12`.

---

## The capability contract (why this is safe)

This optimisation is behaviour-preserving by construction. The features below
are explicitly **not** at risk; the whole point is to make the kitchen-sink
scale viable so they can be built at all.

| Capability | Preserved? | Why |
|---|---|---|
| Full ~100% hook coverage (a detour on every override) | **Yes** | Step 2 drops only the *build-time* Cecil scaffolding after a hook compiles; the patched method + detour stay |
| Per-mod / per-category / per-hook timing | **Yes** | `PerModAttribution` data model untouched; identical hookIds, categories, numbers |
| Chain-of-triggers / "which mods+actions combined drop FPS" | **Yes** | analysis layers built on the captured per-tick/per-hook/per-context stream; we reduce nothing captured |
| Allocation tracking, GC causality, lag fingerprinting | **Yes** | emitted IL (Enter/Leave + alloc variants) is byte-identical |
| Runtime enable/disable of hooks (future) | **Yes** | detour handles retained; only the re-manipulation scaffolding is dropped, and it can be reconstructed on demand |
| Future runtime re-emit of timing IL (hypothetical) | **Yes, with a re-read** | rather than holding ~60 KB/hook forever, re-read the method when (if) ever needed |

The **only** coverage-reducing idea discussed is "skip load-only methods"
(`SetDefaults` / `SetStaticDefaults` / `AddRecipes`). It is **out of scope for
this plan** and flagged as a separate, opt-in decision. Lite mode is likewise a
future opt-in feature, not part of this work.

---

## Baseline — recorded state (pre-world, mods loaded)

Captured 2026-06-22 from `tModLoader-Logs/client.log` (the 14:10–14:14 load) and
live process inspection. Ollama and Docker/Hindsight are OFF for this session,
so process RAM is essentially the game + mods.

### Active modlist (26 mods, the reduced "dev pack")

```
CalamityMod, CalamityModMusic, ThoriumMod, ThoriumRework, Verdant,
VitalityMod, VitalityMusic, LifeSourcesLight, Deadcells, BAM, BSWLmod,
Daybreak, Nitrate, NotQuiteNitrate, HighFPSSupport, BossChecklist,
RecipeBrowser, ImproveGame, ImmersiveInventory, SilkyUIFramework,
BannerCollector, EssentialExtraSlots, Antisocial, Overheal, Combinations,
+ PerformanceProfiler
```

### Install numbers (from the profiler's own log)

| Metric | Value | Source |
|---|---|---|
| Mods profiled | 26 | `ILHookInterceptor` summary |
| IL timing detours installed | **62,180** | `ILHookInterceptor` summary |
| via closed-generic inheritance pass | 49 | `ILHookInterceptor` summary |
| overrides skipped / manipulator failures | 0 / 0 | `ILHookInterceptor` summary |
| Self-health install delta | **3,714 MB** | `Profiler self-health` |
| Self-health per-hook | **61.0 KB/hook** | `Profiler self-health` |

### RAM distribution (pre-world)

| Source | Value |
|---|---|
| tModLoader process RSS | **10.41 GB** |
| Top mod by RAM (tModLoader's own attribution) | **PerformanceProfiler 3.5 GB** |
| 2nd | CalamityMod 2.1 GB |
| 3rd | ThoriumMod 566.5 MB |
| System memory | 31 GB used, **102 MB unused, ~9 GB compressor** (heavy compression, no hard swap yet) |

**Reading of the baseline.** The profiler is the #1 RAM consumer even at 26
mods, larger than Calamity. Per-hook cost (~61 KB) is essentially identical to
the 100-mod load (~60 KB), i.e. it scales linearly with detour count, not
super-linearly. Whether that ~61 KB/hook is *retained* or *uncollected
transient garbage* is still unknown (see Step 0/1) because `MarkInstallEnd`
samples without forcing a collection.

### Reference point — the full 100-mod load (the crash session, 13:42–13:50)

| Metric | 26-mod (now) | 100-mod (crashed) |
|---|---|---|
| Detours | 62,180 | 152,310 |
| Self-health install delta | 3.7 GB | 9.0 GB |
| Per-hook | 61.0 KB | 60.0 KB |
| Profiler RAM (tML attribution) | 3.5 GB | 8.2 GB |
| Process RSS | 10.41 GB | 13.78 GB |
| Outcome | stable, tight | **OOM on world load** |

### Live / in-world data (session A — 26-mod pack, ~14:14–14:31, ~16 min play)

From the profiler's own end-of-session summary in `client.log`:

| In-world metric | Value |
|---|---|
| **Profiler per-tick CPU** | **avg 0.32 ms/t, peak 0.3 ms, total 2,991 ms** — #3 mod by CPU |
| Top mod by CPU | CalamityMod avg 3.14 ms/t (total 28,985 ms) |
| 2nd | ImproveGame avg 0.50 ms/t |
| Worst spike | 48 ms at tick 109 (top CalamityMod 15.0 ms) |
| Events | spikes=50, stalls=10, clusters=8, deaths=1 |
| Worst stall cluster | **23,029 ms MainThreadFreeze**, contributor CalamityMod |
| World-unload teardown | clean ("Profiler disarmed: world unloaded") |

### Live findings (these reshape the priority)

1. **The per-tick hot path is healthy — RAM is the *only* problem.** 0.32 ms/t
   is ~1.9% of a 16.6 ms frame, inside the Standard 2–4% band. The profiler's
   *CPU* cost is fine; nothing about the optimisation needs to touch the tick
   path. This reinforces the capability contract: we are fixing memory, not
   trimming work.

2. **The RAM footprint is plausibly *causing* the stalls the profiler records.**
   Four giant `MainThreadFreeze` stalls (4 s / **23 s** / 5 s / 4 s) all
   attribute to CalamityMod, but Calamity's *recent* cost at each was only
   ~1.2–1.5 ms. A 23-second freeze with 1.4 ms of mod CPU is not Calamity
   computing — it reads as a memory-pressure / GC freeze. The system was at
   **80 MB unused, ~8.9 GB compressed** during play. So the profiler's ~3.5 GB
   is a prime suspect for the very freezes it logs: a feedback loop where the
   measurement tool degrades what it measures. Strongest evidence yet that the
   RAM work matters for *gameplay*, not just tidiness. (Hypothesis, not proven —
   could also be Calamity worldgen/boss sync; worth confirming.)

3. **F2 measurement instability, confirmed in the wild.** The *identical*
   152,310-hook full load measured **9,018 MB (60 KB/hook)** at 13:42 and
   **7,212 MB (48 KB/hook)** at 14:04 — a ~25% swing on the same hook set, from
   GC timing alone. Exactly what F2 predicts; the number is untrustworthy until
   the symmetric Gen2 lands.

4. **Side finding (not RAM):** the session summary printed `ticks 0 duration
   0 ms` despite 28,985 ms of measured Calamity work — the summary fired while
   the session was still "open" on world-unload, so tick/duration didn't
   finalise. Minor logging bug; logged here, not part of this plan.

### Current state — session C (29-mod pack, pre-world, settled)

Added `WMITF`, `HomingProj`, `OmniSwing` to the 26-mod pack → **29 mods**.
Both packs recorded side by side:

| Metric | 26-mod pack (session A) | 29-mod pack (session C) |
|---|---|---|
| Mods | 26 | 29 (+3 QoL) |
| IL detours installed | 62,180 | **62,203 (+23)** |
| Self-health install delta | 3,714 MB | 3,550 MB |
| Self-health per-hook | 61.0 KB | 58.0 KB |
| Profiler RAM (tML attribution) | 3.5 GB (#1) | **3.7 GB (#1)** |
| 2nd / 3rd by RAM | Calamity 2.1 GB / Thorium 567 MB | Calamity 1.9 GB / ThoriumRework 465 MB |
| Process RSS (settled) | 10.41 GB | **8.78 GB** |
| System | 102 MB free, ~9 GB compressed | 78 MB free, ~11 GB compressed |

**What the 26→29 comparison teaches:**

1. **Detour count (and thus RAM) is driven by heavy *content* mods, not mod
   count.** Three QoL mods (WMITF, HomingProj, OmniSwing) added only **+23
   detours** and moved the profiler's footprint by noise (3.5→3.7 GB). The
   62k detours come overwhelmingly from Calamity + Thorium. Implication for the
   dev pack: its RAM is set by *which* big mods are in it, not how many mods.
2. **Transient really is reclaimed over time.** Process RSS fell 10.95 GB
   (mid-load) → 8.78 GB (settled, ~8 min later) as install garbage was GC'd,
   while the *retained* ~3.7 GB held. So of the load-time peak, ~2 GB was
   transient and ~3.7 GB is the persistent retained cost. F2's forced Gen2 would
   surface that retained number *at measurement time* instead of waiting for the
   heap to settle.
3. **F2 instability, third data point.** 29 mods reported a *lower* install
   delta (3,550 MB / 58 KB/hook) than 26 mods (3,714 MB / 61 KB/hook) despite
   +23 hooks — the metric is moving on GC timing, not real cost.

### Verification anchor for the optimisation

After any change, on the same pack: detour count must read **unchanged** (62,180
for the 26-mod pack) = coverage proof; install delta and tML per-mod attribution
must drop; per-tick overhead must stay ~0.32 ms/t.

---

## The plan (audit-informed, 2026-06-22)

The `code-health-audit` agent decompiled the shipped MonoMod binaries
(`RuntimeDetour 25.3.2` / `Utils 25.0.10`, via `ilspycmd`) and settled the
central question with ground truth. Full evidence:
`context/plans/code-health-audit/hook-install-ram.md`.

**Verdict: the ~8.2 GB is genuinely RETAINED, not reclaimable garbage.** Per
hooked method MonoMod keeps two Cecil method bodies alive for the hook's whole
applied lifetime:
- `SourceCloneIl` — a `DynamicMethodDefinition` cloning the original body
  (`dm.cs:346`); held so MonoMod can rebuild the chain; disposed only on
  `RemoveILHook` (our unload).
- `LastContext` — the manipulated body; `MakeReadOnly()`'d, **not**
  `Dispose()`'d after the final apply (`dm.cs:679`/`695`).

This confirms the standing suspicion (`decisions.md:201`). It also **refutes the
earlier hope** that a GC after install reclaims gigabytes: the bulk is live,
rooted state, not collectable.

| # | Lever | Status | Coverage | Risk | Payoff |
|---|---|---|---|---|---|
| P0 | **Fix the test harness (F4)** — 11/25 Compile paths stale after the v0.11 move | **done** | none | none | unblocks dev loop + equivalence checks |
| 1 | **Force Gen2 in `MarkInstallEnd` (F2)** | **done** | none | ~none | honest, repeatable measurement (NOT a footprint cut) |
| 2 | **Trim retained `LastContext` post-install** via guarded reflection | **done (validated in-game)** | none | med (needs Invariant-4 guard) | ~half the retained Cecil graph (GB-scale) |
| 3 | **Own the emission** (our DMD → `Dispose()` → plain Detour) | not started | none if verified | higher (coexistence) | full retention control, in-repo |
| 4 | Upstream a `TrimRetainedIL()` to MonoMod | out-of-repo | none | low | clean, but not ours to ship, slow |
| 5 | Cost-model doc fix (F3) + duplicate-`using` cleanup (F6/F7/F8) | **done** | none | none | honesty + tidy |

### P0 — fix the test harness first (blocking)

F4: the xUnit project's `<Compile Include>` list still points at pre-v0.11
paths (`PerModSample`, `PerModAttribution`, `Baseline`, `StallDetector`,
`Insights/*`, `Persistence/*` all moved `Profiling/`→`Data/`). 11 of 25 includes
are stale, so the suite does not build. Everything downstream — the fast
`dotnet test` dev loop AND the Step-2/3 equivalence checks that prove coverage
is preserved — depends on this. The audit's `build-and-tests.md` (F5) has the
exact stale→current path map. Fix before any structural change.

### 1 — force Gen2 in `MarkInstallEnd` (F2): do now, but reset expectations

`MarkInstallStart` forces a Gen2 then samples; `MarkInstallEnd` samples without
one. Add the symmetric Gen2 so install-delta / KB-per-hook / Severity become
honest and repeatable (the precondition for validating any later fix).
**Honest correction:** given the decompilation verdict this will NOT meaningfully
shrink the 8.2 GB — the bulk is genuinely retained. It fixes the *number*, not
the footprint.

### 2 — trim the retained read-only context (the real in-repo lever, not free)

MonoMod holds `LastContext` (one of the two Cecil bodies per hook) only to
support re-chaining, which our install-once hooks never do. Disposing it after
install reclaims ~half the retained Cecil graph (GB-scale at 152k hooks). But
MonoMod exposes no public "trim" call, so this needs **guarded reflection** into
`ILHookEntry.LastContext`. That is exactly the loader-internal Invariant 4 warns
about, so it must be guarded: verify the field shape at load; if the MonoMod
version doesn't match, skip the trim and keep every hook live (abort-clean).
Coverage-preserving, medium risk, needs equivalence + RAM verification (hence
P0 first).

### 3 — own the emission (the deeper in-repo structural option)

Instead of MonoMod's `ILHook` (which retains the Cecil graph), build each
patched method via our own `DynamicMethodDefinition`, generate the `MethodInfo`,
`Dispose()` the DMD immediately (freeing its Cecil module), then install a plain
`Detour` to the generated method. We'd retain only the generated method + detour
handle, sidestepping MonoMod's retention entirely, fully in-repo. Cost: real
work, plus careful verification that it coexists with other mods' hooks on the
same method and reproduces today's per-hook attribution byte-for-byte. Larger
than Step 2; the fallback if Step 2's reflection proves too fragile.

### 4 — upstream a `TrimRetainedIL()` to MonoMod

The clean fix, but out of our hands and slow. Worth filing upstream regardless.

### 5 — free cleanups

- **F3:** README claims ~36 KB/hook (~1.7 GB at 50k hooks); reality is ~60 KB/hook
  (~9 GB at 152k). Correct after F2 gives the honest number; the `BaselineBytesPerHook`
  constant already crosses its own `Concerning` ratio.
- **F6:** 18 files (not 16) with duplicate `using` directives — exact pairs in the audit.
- **F7/F8:** `conventions.md` #13 contradicts the code; a phantom doc reference.

---

## Async install — explicitly NOT a RAM lever

Three distinct stages get conflated as "loading":

1. **Build** (compile source → `.tmod`): one-time per code change. Not the slow step.
2. **Load** (tModLoader): loads every enabled mod's assembly, registers content,
   runs their lifecycle. This is the ~2.1 GB Calamity etc. **tModLoader's job;
   we never touch or bypass it.**
3. **Instrument** (the profiler): our `PostSetupContent` walks the loaded mods
   and adds the detours. **This** is the long step and the multi-GB cost — 100%
   our code, bolted on after stage 2.

Only stage 3 could go async (install detours on a background thread, let the
load screen finish). But async changes *when* the memory is allocated, not
*whether* — total RAM is identical. Plus it risks patching live method entry
points mid-execution and leaves early gameplay unmeasured. **Async is a
load-screen-UX tool, parked. It does nothing for the RAM goal.**

---

## Verification methodology (before/after)

Run the identical 26-mod dev pack, same world, same ~5-min play loop. A change
is accepted only if:

1. **Coverage identical** — detour count stays exactly 62,180 (proves no hook lost).
2. **RAM down** — self-health install delta and tModLoader's per-mod attribution both drop.
3. **Attribution equivalent** — per-mod/per-category/per-hook numbers match the baseline within timing noise (equivalence checks).
4. **Hot path unaffected** — per-tick overhead unchanged (Invariant 2).

---

## Open questions

**Resolved by the audit (2026-06-22):**
- ~~Retained vs transient split~~ → **mostly retained** (decompiled ground truth, `hook-install-ram.md`).
- ~~Does MonoMod 25.3.2 retain the DMD/Cecil module post-apply?~~ → **yes**, two Cecil bodies per hook (`SourceCloneIl` + `LastContext`), reclaimed only on unload.

**Still open:**
- Exact *retained* per-hook KB — needs an in-game install pass with a forced Gen2 on both measurement ends; blocked today by F4 (no off-game install harness).
- Step 2 vs Step 3 decision: is guarded reflection into `ILHookEntry.LastContext` robust enough across MonoMod versions, or do we go straight to owning the emission?
- Whether upstreaming `TrimRetainedIL()` to MonoMod is worth opening regardless.

---

## Execution log — 2026-06-22 (autonomous run)

Executed the safe, verifiable tier end-to-end; deliberately deferred the risky
structural RAM cut (see below). Committed in logical groups.

| Item | Status | Evidence |
|---|---|---|
| P0: test harness (F4/F5) | **done** | 69/69 `dotnet test` green (was: did not compile at all) |
| 1: F2 honest measurement | **done** | forced Gen2 in `MarkInstallEnd`, symmetric with start |
| F9/F10 keybind log drift | **done** | 4 stale `F10` → `F9` in `PerformanceProfiler.cs` |
| F6: 18 duplicate usings | **done** | 18 removed, 0 remain |
| F3: README cost model | **done** | ~36 KB/hook → measured ~50-60 KB; multi-GB at scale |
| F7: conventions #13 | **done** | AggressiveInlining reality recorded |
| F8: phantom `_TempAllocBench` | **done** | dangling doc reference dropped |
| 2: trim retained `LastContext` | **DONE (validated in-game)** | profiler 3.7 → 1.0 GB (tML attribution); self-health 3,550 → 1,867 MB (58 → 30 KB/hook); 62,203 coverage intact; disposed 62,203 contexts; clean load + play, no abort/crash |
| 3: own the emission | not started | larger rewrite; fallback if Step 2 proves fragile |

**The harness repair was deeper than the audit's "fix 7 paths."** The v0.11 move
also (a) gave nearly every source file a blanket `Data.*` using-header (importing
`Data.Collectors` / `Data.Streams` / `Data.Aggregators.Segments`), and (b) coupled
`ProfilerDatabase` to `StreamRegistry` (now under `Data/Streams/`). Resolved by:
correcting the F5 path map, re-linking `Data/Streams/` for `StreamRegistry`, adding
empty test-only namespace stubs (`Tests/_TestNamespaceStubs.cs`) so the inert
Collectors/Segments header usings resolve without dragging Terraria in, and
serialising the suite (`DisableTestParallelization`) to stop a `BsonMapper.Global`
race between the two persistence test classes.

**Why Step 2 was not auto-enabled (unsupervised run).** The independent audit
classed it P-1, "not a free in-repo change"; it needs guarded reflection into
MonoMod loader internals (the exact surface Invariant 4 guards against); and its
payoff is only confirmable by an in-game RAM measurement that requires the user
present. Shipping it blind, with nobody to revert a bad launch, is precisely the
failure mode Invariant 4 exists to prevent. The groundwork is now in place (green
harness for equivalence checks, honest measurement), so it becomes a clean
measure-and-ship when the user is back.
