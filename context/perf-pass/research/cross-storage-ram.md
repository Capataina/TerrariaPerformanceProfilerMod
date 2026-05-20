# Cross-System Storage and RAM — v0.6 Optimisation Pass Research Dossier

> Scope: the entire memory + disk footprint across the whole lifecycle. Install-time RAM, steady-state RAM, steady-state disk growth, session-lifetime growth curves, downsampling/compaction tiers, backup rotation, schema migration roadmap. This dossier sits **on top of** the eleven sibling per-system dossiers and reconciles every storage and RAM finding into one numbered, additive plan.
>
> Anchor invariants (`CLAUDE.md` Project Invariants + `philosophy.md` data-stack rule + `baseline.md` §5):
>
> 1. **Read-only instrumentation.** Nothing in this plan mutates game/world/save state.
> 2. **Overhead is a budget.** Per-tick paths stay zero-allocation. Storage/RAM levers are layout, encoding, sequencing, batching, threading, lifetime, lifecycle and lazy materialisation; never capture truncation.
> 3. **Descriptive not normative.** No new normative UI copy.
> 4. **Abort-clean on host drift.** Any storage/RAM change that touches MonoMod or Cecil internals is guarded by try/catch and degrades to today's behaviour on signature mismatch.
> 5. **No mod-specific code.** Every encoding scheme keys on generic-surface identifiers (mod id, hook id, category id) never named mod identifiers.
>
> Plus the pass-specific rule:
>
> > **Optimisation = doing what we already do at maximum efficiency. It is not = doing less.**
>
> No row, no column, no event stream, no snapshot cadence, no tier, no backup, no index is dropped by this plan. The data stack stays whole.

---

## Table of contents

1. Install-time RAM cross-system reconciliation
2. Steady-state RAM census
3. Steady-state disk census + projected growth curves
4. BSON-layer cross-system opportunities
5. Tier sizing under v0.6 capture rates
6. Schema-migration roadmap (the unified v6 → v7 plan)
7. Backup + journal footprint audit
8. Cross-system dependencies and landing order
9. References

---

## 1. Install-time RAM cross-system reconciliation

### 1.1 The headline numbers (baseline.md §2)

```
hook install delta, first install        481 MB
hook install delta, subsequent reloads   322 - 618 MB
hooks installed                          10,258
KB per hook (sustained, 233 MB ÷ 10,258) 23 KB
target hook install delta                < 80 MB at same coverage
PerformanceProfiler Mod-RAM in session   234 MB  (essentially = the install delta)
```

The 234 MB figure attributed to `PerformanceProfiler` by tModLoader's own mod-RAM panel is, to within rounding, the hook-install delta. There is no "second invisible 200 MB" hiding in the runtime; the 234 MB is the install plus a few MB of steady-state.

### 1.2 Three independent attributions of the 233 MB

Three sibling dossiers attribute this number, and they do not contradict each other; they describe **the same heap in three slices**.

| Dossier | Attribution | Slice it owns | Confidence |
|---|---|---|---|
| `hook-instrumentation.md` §1.6 (B), §3.1, §3.2 | Two Cecil `ModuleDefinition` modules retained per hook (the `SourceCloneIl` and the `Active` `ILContext`), each ~5-15 KB of importer caches + method-body. **60-190 MB** projected; matches the upper 233 MB number once trampolines + DynamicMethod bodies are added. | Cecil retention pillar. | High (verified against MonoMod sources via `gh api`). |
| `mod-lifecycle.md` §1.9, §3.1 | Static `List<ILHook>` retained at the process-static level for the whole world; per-mod `int[]` counters; `Type[]` and `MethodInfo[]` reflection scratch surviving the install loop. | Reflection scratch + bookkeeping pillar. | Medium (estimated). |
| `metric-collection.md` §1.2 | `List<HookDescriptor>` of 10,258 entries (~880 KB at 88 B/descriptor including the interned display-name string), parallel `long[]` ticks and bytes arrays per backend keyed by hookId. | Per-mod attribution pillar — small contribution (~3-5 MB). | High. |

These add up rather than overlap. The Cecil pillar dominates; the per-mod attribution pillar is rounding error against it.

### 1.3 The joint plan (reconciled)

Numbered in landing order. Every entry is one of the proposals in the per-system dossiers, repeated here with cross-system context so the reader sees the whole shape.

| # | Lever | Owner dossier | Expected RAM delta | Risk | Land before |
|---|---|---|---|---|---|
| I-1 | Force Gen2 + LOH compaction immediately after install (`ALLOC-2`). | hook-instrumentation §4 | -20 to -50 MB transient retention freed | Low. One-time ~100-300 ms pause inside the world-enter window. | I-2 (the diagnostic) so the snapshot reads a clean number. |
| I-2 | Install-heap-snapshot diagnostic — pre/post `MarkInstallEnd`, before/after a forced GC, plus the optional `WriteHeapDump` behind a debug flag. **No code change to instrumentation; this is the evidence gate** that decides whether I-3 lands as scoped. | hook-instrumentation §6 Phase E, §8.12 | n/a (diagnostic) | None. | I-3. |
| I-3 | Dispose per-hook `ILContext` immediately after `ILHook` install (`ALLOC-1`). Holds only the chain's `SourceCloneIl`. **Single largest RAM win in the entire pass.** | hook-instrumentation §4 | **-50 to -150 MB** if Cecil is the dominant pillar (the §1.2 high-confidence finding). | Medium-high. MonoMod chain may need the `ILContext` for later `Refresh`. Guard with try/catch; on any exception, skip the optimisation for that hook (Invariant 4). | I-4. |
| I-4 | Dictionary-backed `RegisterOrReuseHook` to eliminate the O(n²) registration in Parallel mode (`ALLOC-4`). | hook-instrumentation §4 | Wall-time only in Parallel mode; RAM neutral in default single-backend mode. | Low. | n/a. |
| I-5 | Intern `DisplayName` strings (`ALLOC-3`). | hook-instrumentation §4 | -0.5 to -1.5 MB | None. | n/a. |
| I-6 | Pre-size install collections (`ALLOC-5`). | hook-instrumentation §4 | -hundreds of KB transient garbage | None. | n/a. |
| I-7 | Yielding install across multiple frames (`mod-lifecycle.md` §4.2). Splits `ILHookInterceptor.Install`'s ~5-8 s wall and ~322-618 MB peak alloc by walking ProfiledMods one slice per frame, returning to the splash-screen renderer between slices. This does **not** reduce final retention; it reduces **peak transient allocation** during install, which is the number that matters for low-memory devices. | mod-lifecycle §4.2 | Peak transient -100 to -200 MB | Medium. Splits the install into N tModLoader pump cycles; cancellation handling must be airtight. | I-3 (independent ordering, can land in parallel). |
| I-8 | Move install off the splash thread entirely (`mod-lifecycle.md` §4.5). Optional; only if I-7 isn't enough on low-RAM hosts. | mod-lifecycle §4.5 | Same retention; no peak change. Player-perceived latency only. | Medium. | After I-3 + I-7 evidence. |

### 1.4 The expected joint outcome

Starting state: **233 MB install delta, 23 KB/hook**.

If I-1 frees 35 MB of transient scratch and I-3 frees 100 MB of per-hook Cecil contexts:

```
   233 MB install delta
 - 35 MB transient (I-1)
 - 100 MB per-hook Active context (I-3)
 - 1 MB DisplayName interning (I-5)
 - 0.5 MB pre-sized collections (I-6)
 ─────
 ~97 MB residual at same hook coverage
 → ~9.5 KB / hook
```

Within striking distance of the < 80 MB target. The remaining gap closes by ALLOC-4 (registration churn) and any RAM-2 wins from disposing the unpatched `SourceClone` if the MonoMod-internals research confirms feasibility.

**Counter-scenario.** If I-2's heap snapshot shows Cecil is only 30 % of the install delta — i.e. trampoline pages or DynamicMethod bodies dominate — then I-3's win is proportionally smaller, and the pass pivots toward a `TrampolinePool`-sharing investigation (RAM-1, filed upstream). The decision tree is in `hook-instrumentation.md` §8.12 and the diagnostic is the gate.

### 1.5 The mod-lifecycle reconciliation

The hook-instrumentation dossier proposes "shrink RAM per hook"; mod-lifecycle proposes "spread the install across frames". These do not conflict — they attack different perceived costs:

| Approach | What it shrinks | What it doesn't shrink |
|---|---|---|
| I-3 (dispose `ILContext`) | Steady-state retention | Peak transient during install loop |
| I-7 (yielding install) | Peak transient during install loop | Steady-state retention |

Land both. They compose; the player who watches mod RAM in the workshop panel sees 234 MB → ~95 MB *and* the OS-reported peak commit during install drops by a further ~150 MB.

### 1.6 The "deferred install" idea (not in scope)

The mod-lifecycle dossier briefly considers `applyByDefault: false` on the `ILHook` constructor + later batched `.Apply()` on a background warmup. Per `hook-instrumentation.md` §8.4 this **does not save memory** (the Cecil module is built at construct-time, not at apply-time) and only redistributes JIT cost. It is not part of this RAM plan, but it is filed for a future "world-enter freeze" pass.

---

## 2. Steady-state RAM census

Every long-lived allocation in the running profiler, summed by system. Pulled from each dossier's allocation ledger and reconciled against the live code paths.

### 2.1 The census

```
SYSTEM                                  ALLOCATION                           STEADY-STATE BYTES         OWNED BY
─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
Hook instrumentation
  ILHook chain (per hook × 10,258)      managed wrapper + Cecil contexts     233 MB → ~95 MB (post I-3)  ILHookInterceptor._installedHooks
  ProbeStack thread-local stack         32-frame × 24 B = 768 B per thread   < 4 KB total                ProbeStack._stack ThreadStatic
  PerModAttribution.Hooks               List<HookDescriptor> × 10,258         ~880 KB                     PerModAttribution._hooks
  PerModAttribution per-backend ticks   long[modCount × catCount] × 2 backends  ~1 KB                    PerModAttribution._ticksByBackend
  PerModAttribution per-hook ticks      long[hookCount] × 2 backends         ~160 KB                     PerModAttribution._hookTicksByBackend
  HookCoverageView arrays               int[modCount] × 2 backends           ~160 B                      ILHookInterceptor counters
  HookCategoryRouter lookup             Dictionary<Type,int>                 ~few KB                     static cache

Metric collection
  RingBuffer<TickFrame>                 1800 × 64 B TickFrame struct         115 KB                      MetricCollector._history
  Baseline histogram scratch            int[512]                             2 KB                        Baseline._histogramScratch
  Per-mod CPU smoothed arrays           double[modCount × catCount] × 5      ~7 MB at 18 mods × 7 cat × 1800 slots × 8 B (history)  MetricCollector
  Per-mod alloc smoothed arrays         double[modCount × catCount] × 5      ~7 MB (alloc path)          MetricCollector
  Per-hook smoothed arrays              double[hookCount] × 5                ~410 KB at 10k hooks        MetricCollector
  Per-hook alloc smoothed arrays        double[hookCount] × 5                ~410 KB (alloc path)        MetricCollector
  PerTickAttributionRing main           float[1800 × modCount]               ~127 KB at 18 mods          PerTickAttributionRing._perMod
  PerTickAttributionRing per-cat        float[120 × modCount × catCount]     ~60 KB at 18 mods × 7 cat   PerTickAttributionRing._perCatSnapshot
  SpikeDetector window                  300-tick rolling                     ~5 KB                       SpikeDetector
  StallDetector window                  300-tick rolling                     ~5 KB                       StallDetector

Persistence
  DbWriteOp Channel<T> bounded segments unbounded queue, typical 0-5000 ops  variable, ~0.5-2 MB peak    DbWriterThread
  EventJournal StringBuilder            transient per-batch                  reset to ~16 KB cap         EventJournal._builder
  LiteDB BsonMapper cache               per-type reflection cache            ~few MB                     LiteDB internal
  LiteDB B-tree pages cached            ~1000 page × 8 KB max log            ~8 MB peak                  LiteDB._log
  SessionRecorder per-tick smoothed     reused per emit                      ~free                       SessionRecorder
  RecentDamageRing (proposed)           64 × 24 B tuples                     1.5 KB                      SessionRecorder (post A3)
  ContextTransitionWatcher state        last bitset + scalars                ~few KB                     ContextTransitionWatcher

Events + context
  BiomeRegistry vanilla delegates       Func<Player,bool> × ~38              ~6 KB (delegates)           BiomeRegistry
  BiomeRegistry modded bit map          int[moddedBiomes]                    ~few hundred B              BiomeRegistry
  WeatherSources.All                    (flag, Func<bool>) × 12              ~1 KB                       static
  BossSampler.npcs                      references vanilla Main.npc, no copy 0 B                         transient
  BossSampler cache                     Dictionary<int,string> NPC names     ~10-20 KB on a busy session BossSampler
  EventAggregator buckets               Dictionary per dimension × 6         ~10-30 KB                   EventAggregator

Overlay
  OverlayPanel render scratch           a few StringBuilder + cache          ~50 KB                      Overlay
  TreeTab cached rows                   ~1 KB per mod                        ~20 KB                      TreeTab

Insights
  Insights window cache                 last evaluated insights              ~10-30 KB                   InsightsEngine

Self-health
  ProfilerSelfHealth                    counters + small history             ~few KB                     ProfilerSelfHealth
─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
TOTAL steady-state working set, today:  ~250 MB (dominated by hook install)
TOTAL steady-state working set, post-pass:  ~115 MB
```

### 2.2 Largest non-hook contributors

Outside the 233 MB hook pillar the largest contributors are the **MetricCollector smoothed double-arrays** at ~7 MB per side (CPU + alloc) = ~14 MB. These hold the per-mod-per-category history for the 1800-tick (30-second) ring. They are the on-tick "scratch" the smoothing pass writes into, and the rolling-average pass reads from. They are not in the data stack rule's protective scope — they are working memory, not capture — but they are also not load-bearing for capture either; they exist purely so the spike detector and overlay see smoothed values without recomputing.

**Lever (filed, not proposed in this pass).** If the smoothed arrays were stored as `float[]` instead of `double[]`, the footprint halves to ~7 MB total. This is a cross-system change (`MetricCollector` smoothed APIs return `double`; spike-detector consumes `double`) and the precision loss is irrelevant for a smoothed ms-value at 6 decimal places. Filed as a v0.7 candidate.

### 2.3 The "234 MB Mod-RAM" reading after the pass

The mod-RAM panel reads `GC.GetTotalMemory(forceFullCollection: true)` minus a clean baseline. After:

```
233 MB hooks   →   ~95 MB hooks    (I-3 + I-5 + I-6)
+ 14 MB MetricCollector smoothed arrays (unchanged)
+ ~3 MB MetricCollector ring + per-tick attribution
+ ~3 MB LiteDB BsonMapper cache + page cache
+ ~5 MB everything else
─────
~120 MB steady-state Mod-RAM, post-pass
```

A 49 % reduction in the mod's reported RAM, with **zero capture surfaces lost and zero UI density reduced**.

### 2.4 Ring-buffer working-set sizing — verification

The 1800-frame `TickFrame[]` is the central history. `TickFrame` today is 64 B with one managed reference (`PerModSample[]? ModSamples`, always null on the hot path per `metric-collection.md` §1.6). If that reference is removed for the pass — the field carries no data today — the struct becomes a pure value type, and every slot in the ring buffer is one fewer GC root. At 1800 slots that is 1800 fewer pointer slots the GC walks each cycle. **Filed as a small win; not the headline of this dossier.**

---

## 3. Steady-state disk census and projected growth curves

### 3.1 The 10-minute Calamity baseline (1,064 KB)

From `persistence.md` §1.3 the per-minute writer load on busy combat decomposes as:

| Stream | Ops/min busy | Bytes/row | Bytes/min |
|---|---|---|---|
| damageDealtEvents | ~3,600 | ~250 | ~900 KB worst, ~120 KB playtest |
| damageTakenEvents | up to 60 | ~300 | ~18 KB |
| npcSpawnEvents | a few/sec | ~280 | ~40 KB |
| itemCreatedEvents | 1-10 (post-A1 fix: 60+) | ~180 | ~1 KB → ~12 KB |
| loadoutSnapshots | ~2 | ~600 + slots | ~2 KB |
| buffEvents | 1-20 (post-A2 fix: 20-200) | ~150 | ~3 KB → ~30 KB |
| contextTransitions | 1-100 | ~120 | ~12 KB |
| tickAggregatesWarm | 60 | ~120 + per-mod | ~55 KB |
| tickAggregatesCold | 1 | ~200 + per-mod | ~1 KB |
| spikeWindows | 1-10 | ~2 KB + per-mod-cat | ~80 KB |
| stallEvents | 0-20 normal | ~300 + 5 contrib | ~10 KB |
| worldSnapshots | 2 | ~250 | ~0.5 KB |

Summed playtest: ~370-450 KB/min of raw row data → 10-min Calamity DB lands at ~1,064 KB after BSON framing, indexes, and journal overhead. v0.3 was 752 KB; the 41 % regression (752 → 1,064 KB) is six new event streams.

### 3.2 Where the bytes go (cross-system reconciliation)

Five distinct cost classes, with their owner dossier:

| Cost class | Bytes/min on a busy combat min | Owner | Reconciled lever |
|---|---|---|---|
| **BSON field-name overhead** | ~475 KB (per persistence.md §4.1.1) | persistence.md §5.1 | Short field-name mapping. Half the DB shrink. |
| **Repeated string values** (`LoadoutFingerprint`, `OwningMod`, `Path`, `SourceKind`, `ContextCategory`, `NpcName`, `BuffName`) | ~260 KB | persistence.md §5.4 | String table or FK-via-ObjectId (the `LoadoutFingerprint → LoadoutSnapshotId` swap is the single biggest event-stream win). |
| **All-string enums in `Cause`, `Severity`, `Kind`, `Category`** | ~30-60 KB | stall-detection.md §5.G, insights-engine.md §4 | Byte-encoded enums; schema bump v2 → v3 on stall rows; coordinated v6 → v7 plan in §6 below. |
| **Numeric arrays as BsonArray** (spike per-mod-cat doubles, warm/cold aggregate per-mod doubles) | ~30-50 KB | persistence.md §5.5, spike-detection.md §4.7 | `byte[]` blob (BSON binary 0x05) instead of BsonArray. 3× compression. |
| **`_schema` field name per row** | ~46 KB | persistence.md §4.1.2 | Rename to `v` (saves 6 B name × every row). |

These sum to ~870 KB of the 1,064 KB. The rest is index pages, journal overhead, and B-tree metadata — load-bearing infrastructure that this pass cannot meaningfully shrink without breaking indexes.

### 3.3 Target outcome — recovered, not removed

```
                                   v0.5        v0.6 target     after-pass projection
                                   ────        ──────────      ──────────────────────
field-name overhead                ~475 KB     ~80 KB          short-name map (§4.1)
repeated strings                   ~260 KB     ~50 KB          FK-via-ObjectId + interning (§4.2)
all-string enums                   ~30-60 KB   ~5 KB           byte enums + schema v3 (§4.3)
numeric arrays as BsonArray        ~30-50 KB   ~10 KB          binary blob 0x05 (§4.4)
_schema per row                    ~46 KB      ~9 KB           rename to `v` (§4.5)
indexes + B-tree                   ~160 KB     ~160 KB         unchanged
journal overhead                   ~30 KB      ~5 KB           binary frame replaces NDJSON (§4.6, persistence §5.7)
─────                              ──────      ──────
10-min Calamity DB                 1,064 KB    < 600 KB        ~480 KB (projected, summed levers)
```

The target — 600 KB / 10 min — is comfortably hit, with **every row, every column, every capture stream intact**.

### 3.4 Growth curves under v0.5 vs v0.6 shapes

Projecting per-session DB growth at the v0.5 capture rates (busy-combat steady state). Curves assume continuous combat (worst case); a real session would mix combat with menu/idle time at lower rates.

```
Cumulative on-disk size at 60-tick rate, busy combat:

  Time (min) │ v0.5 size    │ v0.6 (projected, all levers landed)
  ───────────┼──────────────┼─────────────────────────────────────
       1     │     ~100 KB  │      ~48 KB
       5     │     ~530 KB  │     ~240 KB
      10     │   ~1,064 KB  │     ~480 KB
      30     │   ~3,192 KB  │   ~1,440 KB
      60 (1h)│   ~6,384 KB  │   ~2,880 KB
     240 (4h)│  ~25,536 KB  │  ~11,520 KB
     1 100-session campaign at 30-min/session avg
     = 100 × 3,192 KB = ~312 MB on disk under v0.5
                       ~144 MB on disk under v0.6
```

At v0.5 rates a year of moderate play (200 sessions × 30 min) reaches ~640 MB of LiteDB on disk before any compaction. At v0.6 rates that drops to ~290 MB. **Comfortable on any modern system**, but worth noting that the warm/cold/archive tier sizing (§5) is what keeps this from being even larger.

### 3.5 Where the off-thread session-end work changes the curve

`persistence.md` §5.6 moves `BuildModAggregates`, `BuildHookAggregates`, `BuildArchive` and `SessionSummaryLogger.Write` to the writer thread. This does **not** change the steady-state disk growth curve — the same bytes still land on disk — but it changes the **shape of the growth at session-end** from "spike at unload" to "amortised across the writer's drain window". The bytes-per-minute average is unchanged; the bytes-per-second peak drops by ~10×.

This is the storage side of the 8.5 s end-of-session stall fix. The stall is CPU-thread, but the writer-drain backlog at session-end is the on-disk consequence; once the work moves off-thread, both go away.

---

## 4. BSON-layer cross-system opportunities

The BSON wire format pays a per-row cost for **type tag + field name + value**. Across hundreds of thousands of rows per session these add up to a substantial fraction of the v0.5 DB size. Five concrete levers, ranked by impact-per-engineering-week.

### 4.1 Short field-name mapping (`[BsonField("…")]`)

**Surface.** Every `Records/*.cs` file across persistence and the event-stream records.

**Cross-system constraint.** The NDJSON journal in `EventJournal.cs` uses `System.Text.Json`, which serialises by C# property name and ignores `[BsonField]`. So the short-name change **must land together with** the binary journal frame replacement in `persistence.md` §5.7 (§4.6 below) — otherwise the journal grows by exactly the bytes the BSON layer saves.

**Field-name map (uniform across rows for cross-collection consistency).**

| Long property | Short BSON name | Bytes saved per row | Notes |
|---|---|---|---|
| `_schema` | `v` | 6 | universal |
| `SessionId` | `s` | 8 | universal — every event-stream row |
| `Tick` / `TickIndex` | `t` | 3 | universal |
| `UnixMs` / `TimestampUnixMs` | `u` | 5-12 | universal |
| `ModlistFingerprint` | `mf` | 16 | session row only |
| `LoadoutFingerprint` | `lf` | 16 | but see §4.2 — superseded by FK |
| `OwningMod` / `ModInternalName` | `m` | 9 | per-event |
| `Path` | `p` | 3 | damage-dealt |
| `ItemId` | `i` | 5 | per-event |
| `ProjectileId` | `pj` | 10 | per-event |
| `NpcType` | `nt` | 5 | per-event |
| `NpcName` | `nn` | 5 | per-event |
| `DamageDealt` | `d` | 10 | per-event |
| `Crit` | `c` | 3 | per-event |
| `ContextCategory` | `cc` | 13 | per-event |
| `SourceCategory` | `sc` | 12 | per-event |
| `BuffType` | `bt` | 6 | per-event |
| `FrameTimeMs` | `fm` | 9 | per-tick aggregate |
| `GcTimeMs` | `gm` | 6 | per-tick aggregate |
| `PerModMs` | `pm` | 6 | per-tick aggregate |
| `PerModBytes` | `pb` | 9 | per-tick aggregate |
| `PerModCatMs` | `pc` | 9 | spike row |
| `WorstFrameMs` | `wf` | 11 | spike row |
| `Cause` | `cs` | 3 | stall row |
| `Severity` | `sv` | 6 | stall row |
| `_id` | (preserve, mandatory) | 0 | LiteDB primary key |

**Expected delta.** Per `persistence.md` §4.1.1: a `DamageDealtRow` saves ~73 B per row. At 3,600 rows/min that is ~260 KB/min. Summed across all event streams the saving lands at ~400 KB on the 10-min Calamity baseline. **This single lever is roughly 38 % of the v0.6 disk target on its own.**

**Risk.** Breaks any external reader that walks BSON by long property name. We have none — every reader is in-tree and uses `BsonMapper`.

**Migration.** Fresh-DB only. Bump `USER_VERSION` so the existing DB is recovered+wiped on first v0.6 launch. Old session data is lost on upgrade; this is acceptable because we have backup rotation and the prior session JSON exports live in the LegacyJsonImporter path. Alternative: a `BsonMapper` resolver table (`persistence.md` §5.10) — equivalent saving, less invasive to records, more single-point-of-failure surface. **Recommend `[BsonField]` attributes** per §5.1 of the owner dossier.

### 4.2 FK-via-ObjectId for the high-cardinality-low-distinct fields

**Surface.** `LoadoutFingerprint` on `DamageDealtRow`. Repeated ~3,600 times per minute in combat at ~80 B per copy.

**Cross-system change.** Replace `string LoadoutFingerprint` with `ObjectId LoadoutSnapshotId` pointing at the `LoadoutSnapshotRow` whose fingerprint is the truth. Reader-side: join `damageDealt → loadoutSnapshots → fingerprint` to recover the original semantics.

**Expected delta.** 12 B + 3 B (field name `lf` after short-name) per row vs ~80 B + 3 B today. Saves ~68 B/row × 3,600 = **240 KB/min in combat**, ~2.4 MB/hour. Across event streams that reference `LoadoutFingerprint` (currently only `DamageDealtRow`; `DamageTakenRow` and `BuffEventRow` could also opt-in for richer reconstruction) the saving compounds.

**Risk.** Schema shape change. Coordinated under §6's v6 → v7 migration.

**Cross-reference.** This is `persistence.md` §5.4 verbatim. Repeated here because it is the single biggest event-stream win and the §6 plan needs to schedule it.

### 4.3 Byte-encoded enums

**Surface.** Every all-string enum field across the schema:

| Field | Today | After |
|---|---|---|
| `DamageDealtRow.Path` | `"melee"` / `"item"` / `"projectile"` (BSON string, ~10-14 B incl. length + null) | `byte PathCode` (1 B BSON byte) |
| `DamageTakenRow.SourceKind` | `"npc"` / `"projectile"` / `"item"` / `"self"` / `"other"` / `"unknown"` | `byte SourceKindCode` |
| `NpcSpawnRow.SourceCategory` | `"Drop:DropFromNPC"` etc. | A `byte CategoryCode` indexed into a per-session lookup table. The long string form is recoverable. |
| `ItemCreatedRow.ContextCategory` | `"Recipe"` / `"Pickup"` / `"Drop:DropFromNPC"` | A `byte ContextCode` + a small auxiliary string when the code does not cover the case (a 1-byte discriminator + a string only for the "Other:" suffix). |
| `BuffEventRow.Edge` | `"on"` / `"off"` | `byte EdgeCode` (0 = on, 1 = off) |
| `StallEventRow.Cause` | `"UiOverlayBlocking"` etc. (stall-detection.md §5.G) | `byte CauseCode` |
| `StallEventRow.Severity` | `"low"` / `"med"` / `"high"` | `byte SeverityCode` |
| `InsightRow.Severity` | (insights-engine.md §4) | `byte` |

**Expected delta.** ~10-14 B per row saved per enum field. On the combat path with three enum-bearing rows per swing (`DamageDealtRow.Path` plus join keys), ~30 B × 3,600/min = ~108 KB/min reclaimed. On the stall path during a stall cluster, ~24 B × ~50 stalls/cluster = a few KB per cluster.

**Risk.** Schema bump on every record that gains a byte-enum field. Coordinated under §6.

**Reader-side migration shape.** Each enum has a tiny **lookup table** (a `string[]` indexed by byte code, defined in the same record file). The migration adds the byte field, populates from the string field on read, and writes byte on every new row. For old DBs, the migration walks once during open and back-fills.

### 4.4 Numeric arrays as BSON binary

**Surface.** `SpikeWindowRow.PerModCatMs`, `SpikeWindowRow.PerModCatBytes`, `TickAggregateWarm.PerModMs`, `TickAggregateWarm.PerModBytes`, `TickAggregateCold.PerModMs`, `TickAggregateCold.PerModBytes`. All `List<double>` of length `modCount × categoryCount` ≈ 100 × 7 = 700, today serialised as `BsonArray` with per-element name `"0"`, `"1"`, … and per-element type tag.

**Encoding switch.** Register a `BsonMapper.RegisterType<double[]>` serialiser that packs to `byte[]` via `Buffer.BlockCopy`, writing BSON type 0x05 (binary) with subtype 0x00. The wire format becomes:

```
   1 B type tag (0x05)
   1 B cstring null
   4 B name length (e.g. "pc\0")
   4 B binary length
   1 B subtype (0x00)
   N B raw doubles
```

vs the BsonArray:

```
   1 B type tag (0x04)
   1 B cstring null
   variable name length (e.g. "PerModCatMs\0" = 12 B today, "pc\0" = 3 B post-§4.1)
   For each of 700 elements:
     1 B type tag (0x01 double)
     1 B cstring null
     variable name length ("0\0", "1\0", ... "699\0" — avg ~4 B)
     8 B double
   1 B document terminator
```

For 700 doubles: BsonArray ≈ ~8,400 B; binary blob = 2,800 B + ~10 B overhead = ~2,810 B. **3× compression**, exactly as `persistence.md` §4.6 measured. At 50 spikes/playtest × 5.6 KB saved = ~280 KB. At 60 warm-aggregates/min × 500 B saved = ~30 KB/min.

**Cross-system coordination.** The spike-detection dossier (§4.7) and the insights-engine dossier consume these fields; the binary representation needs decoders on both sides. The decoder is a single line — `Buffer.BlockCopy` back into a `double[]` — so this is mostly a no-cost lift once the `BsonMapper` registration lands.

**Width reduction (compatible).** `spike-detection.md` §4.7 suggests `float[]` instead of `double[]` for the per-mod-cat array (the ms precision below 1e-3 is meaningless for a smoothed budget). That additionally halves the bytes to ~1,400 B per spike. **Land together with §4.4** — they ship behind the same schema bump.

### 4.5 `_schema` rename to `v`

Already covered in §4.1. Calling out separately because it is a **uniform** change across every record — every row in every collection gains 6 B back. At ~5,000 event rows per playtest the saving is ~30 KB per session. The naming convention also signals "this is the schema version" more compactly than the underscore-prefixed mirror of a system field.

### 4.6 Binary journal frame format

**Surface.** `EventJournal.cs` and every `IPersistenceStream.Reconstruct`.

**Cross-system constraint.** This is owned by `persistence.md` §5.7 but listed here because it is **the lever that makes §4.1 viable**. The NDJSON journal serialises by C# property name; without the binary-frame switch, every `[BsonField("…")]` shortening is undone by the journal writer.

**The frame.**

```
   4 B magic       (e.g. 0x50504A52 = 'P','P','J','R' — "Performance Profiler Journal Record")
   4 B length      (frame length, little-endian uint32)
   1 B kind        (the DbWriteOp.Kind discriminator)
   12 B session_id (the ObjectId associated with the row's session)
   N B bson        (the same BSON the LiteDB write will produce — computed once)
```

A single BSON pass per op, reused for both the journal write and the LiteDB upsert. Eliminates ~90 % of the writer-thread allocations on the journal path.

**Migration.** The journal is purely operational — a redo log replayed only after an unclean shutdown. Format changes require a journal-format marker in the file header so the reader picks the right decoder. The migration is fresh-DB friendly: on v0.6 launch, replay any v0.5 NDJSON journal once (existing code path), then start writing the binary frame. A `/profiler-journal-dump` command (filed in `persistence.md` §5.7) decodes binary frames into human-readable lines on demand.

### 4.7 Summed savings across §4

```
LEVER                                       BYTES SAVED on 10-min Calamity baseline
──────────────────────────────────────────────────────────────────────────────────
§4.1 short field names                       ~400 KB
§4.2 FK-via-ObjectId for LoadoutFingerprint  ~80 KB
§4.3 byte-encoded enums                      ~50 KB
§4.4 numeric arrays as binary                ~40 KB
§4.5 _schema → v                             ~7 KB
§4.6 binary journal frame                    (journal-side; not in DB size but in writer-thread allocs)
───────                                      ──────
TOTAL DB size reduction                      ~580 KB
v0.5 → v0.6 projected                        1,064 KB → ~480 KB
```

Comfortably under the < 600 KB target with margin for variance.

---

## 5. Tier sizing under v0.6 capture rates

The downsampling tiers exist precisely so the data stack can grow without the storage stack failing. Current sizing (from `persistence.md` §4.3 index inventory and the system docs):

| Tier | Cadence | TTL | Purpose |
|---|---|---|---|
| `tickAggregatesWarm` | 1 Hz (every second) | 24 hours | Recent-session detail; UI queries hit this first |
| `tickAggregatesCold` | 1/min | session-lifetime | Coarser-grain; survives the warm sweep |
| `tickAggregatesArchive` | end-of-session | forever (per-session, unique) | Permanent record; one row per session |

### 5.1 Verifying capacity under v0.6

With every §4 lever landed, **the per-row size shrinks but the row count is unchanged**. The tier sizing was set against v0.5 row sizes; under v0.6 the same tiers comfortably fit more sessions in less space.

| Tier | Rows per session (1-hr) | Row size v0.5 | Row size v0.6 | v0.5 disk | v0.6 disk |
|---|---|---|---|---|---|
| warm | 3,600 | ~920 B | ~420 B | 3.31 MB | 1.51 MB |
| cold | 60 | ~1,000 B | ~480 B | 60 KB | 29 KB |
| archive | 1 | ~5 KB | ~2.5 KB | 5 KB | 2.5 KB |
| **total per 1-hr session** | | | | **~3.4 MB** | **~1.55 MB** |

Across the 24-hour TTL window the warm tier holds (at busy-combat capture rates) ~85 MB worth of rows at v0.5 sizes, ~37 MB at v0.6. **Well within the 100,000 op `QueueSoftCap`** flagged in `persistence.md` §1.3 — the writer never gets close.

### 5.2 Does the warm/cold split need adjustment?

**No.** The 1 Hz / 1-min / per-session cadence shape continues to make sense. The 24-hour warm TTL matches the average gap between sessions for an active player and gives the overlay's last-24-hour view full-resolution data. The cold tier preserves the trend at 1/min granularity for as long as the session DB lives. The archive tier is the durable record.

**Tier additions worth considering, all additive.**

| Proposed tier | Cadence | TTL | Rationale | Owner |
|---|---|---|---|---|
| `tickAggregatesHot` | 10 Hz (every 6 ticks) | 60 minutes | The 10 Hz cadence already exists for context-tagger reads; persisting 10 Hz aggregates would let the spike-attribution UI show sub-second detail without going to the per-tick attribution ring (which is RAM-only, 30 s). Adds ~360 rows/min at ~80 B (after §4 levers) = ~28 KB/min. | Future feature (not in this pass). |
| `spikeAggregates` (lifetime tier under spikes) | end-of-session | per-session forever | Currently `spikeWindows` is per-window, no per-session roll-up. A `spikeAggregates` row per session would summarise spike density / max severity / worst-mod for cross-session trend queries. | insights-engine.md (planned). |
| `stallAggregates` | end-of-session | per-session forever | Symmetric with `spikeAggregates` for stalls. | stall-detection.md (planned). |

**None of these add a per-tick capture rate**; they are end-of-session roll-ups of existing capture. They are filed as v0.7+ features, not part of the storage pass.

### 5.3 The "drop tier" lever — explicitly rejected

A naive pass would say: "the cold and archive tiers carry data that the warm tier captures more precisely; drop the warm tier after a week and reconstruct from cold."

This is wrong per `philosophy.md`'s data-stack rule and per `baseline.md` §5 ("Don't capture Y, it's redundant"). Each tier is a different time-frequency view of the same data. Dropping the warm tier loses sub-minute detail forever for old sessions; reconstructing it from cold is lossy. **The tier shape stays.**

### 5.4 Tier-size verification under post-A1/A2 bug-fix capture rates

The two correctness bugs (`itemCreatedEvents = 0`, `buffEvents = 2`) inflate the per-session row counts substantially once fixed:

| Stream | v0.5 (buggy) | post-A1/A2 fix |
|---|---|---|
| itemCreatedEvents | 0/session | ~60-200/session (every craft + pickup + drop) |
| buffEvents | 2/session | ~100-500/session (every on/off edge) |

That is **a real new persistence load** of perhaps ~30-50 KB/session (raw rows) before BSON framing. After §4 levers the marginal cost is ~10-15 KB/session. The warm tier easily absorbs this; the cold tier doesn't change (cold is 1/min, independent of these events).

---

## 6. Schema-migration roadmap — the unified v6 → v7 plan

### 6.1 Inventory of proposed schema bumps across all dossiers

Each per-system dossier proposes one or more `_schema` bumps. Collected and reconciled:

| Source | Collection | From | To | Reason |
|---|---|---|---|---|
| persistence.md §6.1 | `ItemCreatedRow` | 1 | 2 | New `SourceContext` field (forward-compatible additive) |
| persistence.md §6.3 | `PlayerDeathRow` | 1 | 2 | Damage-weighted attribution: new `DamageWeighting` / `DamageAttributionWindowSeconds` fields |
| persistence.md §5.4 | `DamageDealtRow` | 1 | 2 | Replace `string LoadoutFingerprint` with `ObjectId LoadoutSnapshotId` (FK swap) |
| persistence.md §5.1, §4.1 | **every record** | 1 | 2 | Short BSON field names + `_schema` → `v` (cosmetically a uniform bump but only behaviour-changing where the writer's field name shape changes) |
| persistence.md §5.5, §4.4 | `SpikeWindowRow`, `TickAggregateWarm`, `TickAggregateCold` | 1 | 2 | Numeric arrays as BSON binary 0x05 |
| spike-detection.md §4.7 | `SpikeWindowRow` | 1 | 2 | `float[]` per-mod-cat blob — same v2 bump as persistence.md §5.5 |
| stall-detection.md §5.G | `StallEventRow` | 2 | 3 | Byte-encoded `Cause`, `Severity` |
| stall-detection.md §5.H | `StallEventRow` | 2 | 3 | Drop the `List<>` allocation; coordinate with §5.G — one schema v3 bump |
| stall-detection.md §5.G | `StallClusterRow` | 1 | 2 | Byte-encoded `Cause` + parity with stall-row v3 |
| events-and-context.md §R5 | `ContextTransitionRow` | 1 | 2 | `Type` column changes from `"weather"` to `"weather:Rain"` etc. |
| insights-engine.md §4, §7.2 | `InsightRow` (+ insights indexed-query collections) | 1 | 2 | Byte-encoded `Severity`, indexed-query schema |

### 6.2 Per-collection vs global versioning

The proposed bumps are **per-collection independent**. Each row carries its own `_schema` (renamed to `v` in the storage pass) integer. A reader checks the row's `v` and routes to the matching deserialiser. This is the established pattern documented in `persistence.md` §4.1.2.

**Alternative considered and rejected.** A single global `databaseSchemaVersion` integer in the `metadata` collection that bumps once per release. This is cleaner for "what version is this DB on" queries but **forces every row to be rewritten on every release**, which we cannot afford for the warm/cold tiers (millions of rows). The per-collection approach lets us migrate at row-read time and lets the migration cost amortise.

### 6.3 The unified v6 → v7 migration plan

Sequenced as one cohesive migration step. The numbered phases align with the prioritised execution order in `persistence.md` §8 and ride on top of each per-system dossier's local plan.

```
PHASE 0 — DATABASE WRAPPER PREP
  ▸ Bump USER_VERSION 6 → 7
  ▸ Migrations.cs registers a step "v6_to_v7" that runs at open if USER_VERSION = 6
  ▸ The step opens each affected collection, walks rows, applies per-row migrators

PHASE 1 — CORRECTNESS BUMPS (A1, A2, A3 from persistence.md §6)
  ▸ ItemCreatedRow      v1 → v2  (add SourceContext)
  ▸ PlayerDeathRow      v1 → v2  (add DamageWeighting + DamageAttributionWindowSeconds)
  ▸ Migration: new fields default to null on old rows — pure additive

PHASE 2 — UNIVERSAL BSON COSMETIC RENAME (persistence.md §5.1, §4.1.2)
  ▸ Every record gains [BsonField("…")] on every property
  ▸ _schema (long form) becomes v (short form) on every record
  ▸ Migration: this is the breaking-shape bump — read-side fall-through:
       v ≥ 2: short-name mapper
       v ≤ 1: long-name mapper
    Both BsonMapper instances live in ProfilerDatabase; row gets routed by v.

PHASE 3 — FK SWAP (persistence.md §5.4)
  ▸ DamageDealtRow      v1 → v2  (string LoadoutFingerprint → ObjectId LoadoutSnapshotId)
  ▸ Migration on old rows: walk loadoutSnapshots for the same session, match
    fingerprint string, write the snapshot's ObjectId; if not found, leave
    LoadoutSnapshotId = ObjectId.Empty + retain LoadoutFingerprint as legacy
    field (mark deprecated; future cleanup).

PHASE 4 — NUMERIC ARRAY BLOB (persistence.md §5.5, §4.6, spike-detection.md §4.7)
  ▸ SpikeWindowRow      v1 → v2  (PerModCatMs / PerModCatBytes → double[] or float[] blob)
  ▸ TickAggregateWarm   v1 → v2  (PerModMs / PerModBytes → double[] blob)
  ▸ TickAggregateCold   v1 → v2  (PerModMs / PerModBytes → double[] blob)
  ▸ Migration: convert BsonArray → byte[]. One-pass walk per collection.

PHASE 5 — BYTE-ENCODED ENUMS (stall-detection.md §5.G, §5.H, events-and-context.md §R5, insights-engine.md §4)
  ▸ StallEventRow       v2 → v3  (Cause / Severity → byte codes + contributor list reshaped)
  ▸ StallClusterRow     v1 → v2  (Cause → byte code)
  ▸ ContextTransitionRow v1 → v2 (Type → "category:flag" prefixed form)
  ▸ InsightRow          v1 → v2  (Severity → byte code)
  ▸ Migration: map old string → byte via lookup tables defined per record.

PHASE 6 — JOURNAL FORMAT FLIP (persistence.md §5.7)
  ▸ EventJournal: add journal-format header (1 B version + 3 B magic)
  ▸ On open, read header:
       v0.5 (no header / starts with '{'): replay as NDJSON
       v0.6 (header present): replay as binary frames
  ▸ After successful migration replay, truncate journal and start writing v0.6 frames.

PHASE 7 — VERIFICATION
  ▸ Migration tests: a v0.5 DB snapshot ships in Tests/Fixtures/, the migration is
    run on it, every collection's row count is preserved, every row's content
    is bit-for-bit reconstructable (modulo the FK swap, where the round-trip
    is "fingerprint string → ObjectId → fingerprint string" via the join).
  ▸ Schema-roundtrip: PersistenceRoundTripTests gain a test that writes a row at
    v0.6, reads it back at v0.6, asserts equality.
  ▸ Reader-fallback: a test that opens a fresh v0.6 DB, hand-crafts a v0.5-style
    row in a collection, reads it back through the migration path, asserts the
    expected up-converted shape.
```

### 6.4 Versioning policy going forward

**Per-collection schemas.** Each record's `v` field is bumped independently. A schema bump is required when:

1. A field is renamed (the BsonField name changes) and the old name has shipped in a release.
2. A field's type changes (string → byte, BsonArray → binary).
3. A field is removed (which the data-stack rule prohibits, so this never happens).
4. A field's *meaning* changes (semantic redefinition).

A new field that is forward-compatible — new property defaulting to null/zero on old rows — **does not require a bump** under LiteDB's `BsonMapper` semantics. But we bump anyway when the field is materially load-bearing for a new feature, so the migration test surface stays explicit.

**No "v7 means everything is v7" rule.** Collections move at their own pace. The `metadata` collection's `databaseSchemaVersion` (the existing `USER_VERSION` machinery in LiteDB pragmas) is the **floor** — a number representing "every collection is at least this version, never below". Crossing the floor (v6 → v7) is a wrapper-level migration step; bumping individual collections above the floor is collection-local.

### 6.5 Backward-incompatible read on stale DBs

A player who skips a release reading a too-old DB. The migration step in `Migrations.cs` is **idempotent and incremental** — if a player goes from v0.4 (USER_VERSION=5) directly to v0.6 (USER_VERSION=7), the migration runs `v5_to_v6` then `v6_to_v7` in sequence. Each step is replayable; either fully succeeds (USER_VERSION advanced) or rolls back (the step left no partial state, USER_VERSION unchanged).

### 6.6 Cross-session-DB-file concern

There is exactly **one** `profiler.db` file per modlist (LiteDB single-file model). Migration is global per-file, not per-session. The Crash Sessions collection is migrated in PHASE 0 along with everything else.

---

## 7. Backup and journal footprint audit

### 7.1 Current state

| Artefact | Size | Cadence | Owner |
|---|---|---|---|
| `profiler.db` (main) | ~9.5 MB after 5 sessions | per-write | LiteDB |
| `profiler-log` (WAL) | up to ~8 MB peak (1000-page checkpoint trigger) | per-batch | LiteDB |
| `profiler.db.journal` (NDJSON redo log) | up to ~5 MB peak (rotates) | per-batch | EventJournal |
| `profiler.db.bak.{0,1,2}` (rotated backups) | ~1 MB each × 3 = ~3 MB | session-end | RotateBackups |

**Total on-disk footprint** of the profiler's data layer: ~9.5 + 8 + 5 + 3 = **~25.5 MB peak**, settling to ~10-13 MB between sessions after a checkpoint.

### 7.2 Are three rotating backups right?

**Yes.** The argument:

- Backups are insurance against corruption (LiteDB issue #2401 ENSURE-page-corruption on burst inserts, mitigated by pre-warm but not eliminated). Three rotations give the player a ~3-session window to recover.
- Backups rotate at the writer thread, off the game thread (`mod-lifecycle.md` §1.2 confirms). The 8.5 s end-of-session stall is **not** caused by backup rotation — it is caused by aggregate building + checkpoint + summary logging. Moving those off-thread (`persistence.md` §5.6) leaves backup rotation where it is.
- 3 MB of on-disk overhead is unmeasurable on any modern device.

**Cross-system note.** A future "compact at idle" feature could keep one fully-compacted backup distinct from two rolling ones, so a corruption recovery has a guaranteed-clean fallback. Filed; not in this pass.

### 7.3 Journal footprint after §4.6

The binary journal frame format reduces the per-op byte cost by ~60-70 % (skipping JSON property names, eliding type discriminators, sharing one BSON pass with the LiteDB write). The peak journal size drops from ~5 MB to ~1.5-2 MB. **A clear additive win on disk overhead.**

### 7.4 The `_log` (WAL) tuning question

`persistence.md` §4.4 settled this: leave checkpoint cadence at 60 s. Tighter checkpoints reduce `-log` peak at the cost of more fsync contention; looser checkpoints have the opposite tradeoff. The 60 s default is conservative and works well at our row rates. **No change**.

### 7.5 Backup-size projection under v0.6

After all §4 levers land, each session writes ~55 % fewer bytes. The main DB grows ~55 % slower per session. Backups, taken at session-end and roughly proportional to the main DB's per-session delta, shrink to ~450 KB each. Three backups = ~1.4 MB total. **Comfortable**.

### 7.6 The "crash session" scratch surface

The `EnsureSchemaVersion` + `RecoverIfNeeded` path in `ProfilerDatabase` ctor reads the journal on every open. After a crash, this can replay tens of thousands of operations. The replay path **is the slowest** path the storage layer has — single-threaded, no batching, full deserialisation per row. Under v0.6 the binary frame format makes replay ~5× faster than NDJSON replay (no JSON parsing, no per-row string allocation).

**Cross-system reconciliation.** This is owned by `persistence.md` §5.7 (binary journal) and `mod-lifecycle.md` §1.1 (DB-open path). The two dossiers do not conflict; they describe the same lever from different vantage points.

---

## 8. Cross-system dependencies and landing order

This section explicitly sequences which sub-passes must land before which others so a future implementer does not pick up an isolated lever and break a downstream consumer.

### 8.1 Dependency graph

```
                                  CORRECTNESS GATE
                                  ━━━━━━━━━━━━━━━━
                                  A1: item-created hooks
                                  A2: buff-events snapshot fix
                                  A3: damage-weighted death attribution
                                          │
            ┌─────────────────────────────┼─────────────────────────────┐
            │                             │                             │
       PERSISTENCE                  HOOK INSTALL                  METRIC COLLECTION
       ━━━━━━━━━━━                  ━━━━━━━━━━━━                 ━━━━━━━━━━━━━━━━━━
       B1 off-thread session-end     I-2 install-heap snapshot    CPU-1,2,3 hot-path
            │                             │                             │
       B2 short BSON names           I-1 forced GC after install   D1-D5 misc enqueue
       B3 LoadoutFingerprint FK             │                            │
       B4 numeric array blobs        I-3 dispose ILContext         (lands independently)
            │                             │
       (schema v6 → v7 PHASES 1-5)   I-5 intern DisplayName
            │                        I-6 pre-size collections
       C1 binary journal frame              │
            │                        I-4 dictionary-RegisterOrReuseHook
       C2 InsertBulk for events             │
       C3 deferred non-unique indexes I-7 yielding install (mod-lifecycle §4.2)
            │
       PHASE 6 journal-format flip
            │
       PHASE 7 verification (full schema-roundtrip + migration tests)
```

### 8.2 Hard prerequisites

| Lever | Requires | Why |
|---|---|---|
| §4.1 short field names | §4.6 binary journal (or accept journal growth) | NDJSON journal serialises by C# property name, undoing the saving on the journal side |
| §4.2 LoadoutFingerprint FK | DamageDealtRow v2 schema migration | The FK replaces an existing string field; migration walks old rows to backfill the ObjectId |
| §4.3 byte enums | Per-record v2/v3 schema migrations | Reader needs the byte→string lookup tables |
| §4.4 numeric array blob | BsonMapper registration in `ProfilerDatabase` ctor | The BsonMapper must register the `double[]` ↔ `byte[]` converter before any collection is opened |
| Off-thread session-end (`persistence.md` §5.6) | `MetricCollector.SnapshotAggregates()` API | Writer thread needs a stable snapshot of per-mod-cat double arrays |
| I-3 (ILContext dispose) | I-2 (heap snapshot diagnostic) | The diagnostic confirms Cecil is the dominant pillar before code committed to the hypothesis |
| Yielding install (I-7) | None | Independent — can land in any order |
| Death attribution (A3) | RecentDamageRing in SessionRecorder | Already small; lands with A3 |

### 8.3 Soft dependencies (better-together)

| Pair | Why ship together |
|---|---|
| §4.1 + §4.6 | Without §4.6 the journal undoes the saving; without §4.1 §4.6 is half a win |
| §4.3 stall enums + §5.H stall List allocation | Same row, same schema v3 bump — atomic |
| §4.4 spike binary + spike-detection.md §4.7 float-width | Both touch the same field's wire format |
| events-and-context.md R5 + persistence.md PHASE 5 | Schema v2 on ContextTransitionRow rides PHASE 5 |
| Phase A correctness (A1, A2, A3) | Lands first to set the post-fix baseline; perf measurements after are meaningful |

### 8.4 Sequencing recommendation

The pass implements in this order, with commits at each phase boundary so the diff stays reviewable:

1. **Phase A correctness** (A1, A2, A3) — land + verify capture surfaces are correct.
2. **I-2 heap snapshot** — diagnostic only, decides I-3's expected ROI.
3. **I-3 + I-5 + I-6 + I-1 + I-4** — install-RAM headline win.
4. **B1 off-thread session-end** — independent of the BSON levers, kills the 8.5 s stall.
5. **PHASE 1 schema migration** — additive bumps for A1/A3.
6. **PHASE 2-4 BSON shape changes** — short field names, FK swap, numeric blobs. Lands together because the schema migration must touch every record once.
7. **PHASE 5 byte enums + stall List reshape + ContextTransitionRow flag-named** — across all dossiers' enum stories at once.
8. **PHASE 6 journal binary frames** — independent of BSON shape; lands after the BSON pass to share the schema-version inspection plumbing.
9. **PHASE 7 verification** — migration tests, round-trip tests, baseline.md re-verification.
10. **I-7 yielding install** — landable in parallel from step 3 onward; this is the player-perceived install-latency lever.

### 8.5 Risk concentration

The two highest-risk levers, each in a different system, land in different sub-phases so a failure in one does not block the other:

- **I-3 (ILContext dispose)** — risk: MonoMod chain functionality after disposal. Land in step 3 with a heap-snapshot guard; fall back per-hook if the dispose throws.
- **§4.6 (binary journal frame)** — risk: replay correctness on stale journal during the version transition. Land in step 8 with the journal-format header detection; fall back to NDJSON replay for v0.5 journals.

Neither failure is catastrophic. I-3's failure mode is "this hook still retains its ILContext, like today"; §4.6's failure mode is "fall back to NDJSON, lose the writer-thread alloc reduction".

### 8.6 What happens if a step is skipped

| Step | If skipped |
|---|---|
| Phase A | Perf numbers in §3-§4 are still valid (the bugs are independent of the bytes-on-disk picture) but the bug surface persists. |
| I-2 diagnostic | I-3 can still land but without evidence that Cecil is the dominant pillar; the §1.4 projection may be off by a factor. |
| I-3 | Install delta stays ~233 MB. Other levers still land. The < 80 MB target is missed by the largest margin. |
| §4.1 | DB size target ~480 KB → ~700 KB. Still under v0.5; misses < 600 KB target. |
| §4.2 | DB size target ~480 KB → ~520 KB. Hits target. |
| §4.6 | Writer-thread allocations stay high. Drain target may be missed. |
| Schema migration | Old DBs remain unreadable; players lose history on upgrade. Unacceptable; never skipped. |

---

## 9. References

### 9.1 In-tree

- `/Users/atacanercetinkaya/.../PerformanceProfiler/CLAUDE.md` — five Invariants
- `context/notes/philosophy.md` — data-stack vs storage-stack rule
- `context/perf-pass/baseline.md` — v0.5 measured baseline + v0.6 targets
- `context/perf-pass/research/persistence.md` — owner of the LiteDB / BSON / journal recommendations and schema migration shape (§1, §3-§7 of this dossier reconcile against it)
- `context/perf-pass/research/hook-instrumentation.md` — owner of the install-time RAM analysis, the Cecil pillar attribution, and ALLOC-1/2/3/4/5 + RAM-1/2/3 (§1 of this dossier reconciles against it)
- `context/perf-pass/research/mod-lifecycle.md` — owner of the install lifecycle and yielding install (I-7) and the session-end work relocation (off-thread session-end ties to persistence §5.6)
- `context/perf-pass/research/metric-collection.md` — owner of the smoothed array RAM census (§2.2)
- `context/perf-pass/research/events-and-context.md` — owner of the ContextTransitionRow schema bump (PHASE 5)
- `context/perf-pass/research/spike-detection.md` — owner of the SpikeWindowRow v1→v2 (numeric blob + float width)
- `context/perf-pass/research/stall-detection.md` — owner of the StallEventRow v2→v3 byte enums and List reshape
- `context/perf-pass/research/insights-engine.md` — owner of the InsightRow byte-severity and indexed-query schema
- `context/perf-pass/research/overlay.md` — read for steady-state RAM census (UI cache lines)
- `context/perf-pass/research/allocation-tracking.md` — read for steady-state RAM census (per-mod alloc arrays parallel the per-mod CPU arrays)
- `context/perf-pass/research/code-health-audit.md` — read for cross-cutting overhead patterns
- `context/perf-pass/research/test-harness.md` — owner of the verification test surface (PHASE 7)

### 9.2 Wire-format and platform references

- `bsonspec.org/spec.html` — BSON wire format. Specifically:
  - Element shape: `1 B type + cstring (name) + value`
  - String: `int32 length + UTF-8 + 0x00`
  - Binary: `int32 length + 1 B subtype + bytes` (subtype 0x00 used for `double[]` blobs)
  - Type tags relevant to this dossier: `0x01` double, `0x02` string, `0x03` document, `0x04` array, `0x05` binary, `0x07` ObjectId, `0x08` bool, `0x10` int32, `0x12` int64
- `litedb.org/docs/pragmas/` — `USER_VERSION` semantics, checkpoint behaviour, journal cadence
- `github.com/mbdavid/LiteDB/blob/master/LiteDB/Document/Bson/BsonSerializer.cs` — confirms property-name verbatim serialisation unless `[BsonField]` overrides

### 9.3 MonoMod / Cecil references (driving §1 of this dossier)

- `MonoMod/MonoMod` repo, `src/MonoMod.RuntimeDetour/DetourManager.Managed.cs` — `ManagedDetourState.SourceCloneIl` lifetime
- `MonoMod/MonoMod`, `src/MonoMod.Utils/DynamicMethodDefinition.cs` — Cecil module retained per DMD
- `MonoMod/MonoMod`, `src/MonoMod.Utils/MMReflectionImporter.cs` L50-66 — the five per-module caches that dominate the 23 KB/hook number
- `cecil.pe` + `mono-project.com/docs/.../Mono.Cecil/faq/` — Cecil 0.10 in-memory module semantics

### 9.4 .NET 8 references

- `System.Threading.Channels.UnboundedChannel<T>` — lock-free `TryWrite` on the happy path; the journal-frame replacement preserves this property
- `System.Buffers.ArrayPool<byte>.Shared` — pooled byte buffers for the binary frame writer
- `System.Text.Json` source generator (`JsonSerializable`) — used to eliminate per-call reflection if a JSON path survives (it doesn't, under §4.6)
- `System.Buffer.BlockCopy` — `double[]` ↔ `byte[]` conversion for §4.4
- `System.Runtime.GCSettings.LargeObjectHeapCompactionMode` — `CompactOnce` setter for I-1

---

## Appendix A — invalid recommendations (rejected up front)

Recorded so the next reader doesn't re-invent them and so the additive-only ratchet is visible.

| Tempting recommendation | Why it's invalid (specific) |
|---|---|
| "Drop the warm tier after one week" | Tier-shape capture truncation. Warm-tier sub-minute resolution is irrecoverable. |
| "Compress the LiteDB file with gzip at session-end" | Adds CPU + a new artefact at session-end. The §4 levers already shrink the DB by ~55 %; further compression at the cost of read-side decode is a worse trade. |
| "Cap LoadoutSnapshot per-slot list at top-N items" | Capture mutilation. Fingerprint depends on every slot. |
| "Skip a backup rotation cycle if the DB hasn't changed much" | Backups are insurance; saving 1 MB by skipping an insurance write fails the data-stack rule's spirit. |
| "Drop the `_id` ObjectId — use a sequential int" | Saves 8 B/row but breaks cross-collection joins (used by the FK swap §4.2). Net cost > saving. |
| "Move the warm tier into a separate LiteDB file" | Two WAL logs, two writer threads, two checkpoint cadences. Adds operational complexity without saving bytes. Owner-dossier persistence.md Appendix A confirms. |
| "Truncate `DamageDealtRow.NpcName` to 16 chars" | Capture mutilation. Use field-name shortening (§4.1) plus per-session string-interning instead. |
| "Drop the per-row `Summary` string on PlayerDeathRow" | Pre-rendered human-readable text is a deliberate convenience for the chat-command consumer and session-summary logger. Reconstructing at read time shifts CPU to readers and breaks the data-stack-vs-presentation-stack split. |
| "Skip the worldSnapshot emit if the player hasn't moved" | Periodic-state capture is a discrete-time signal, not a delta-encoded log. Skipping makes 'what was the player doing at minute 7' return null for an idle player. |
| "Make spike per-mod-cat arrays sparse (skip zero entries)" | The data-stack representation is dense by design. Sparsifying breaks downstream consumers (insights engine, overlay drill-down). Use the binary blob (§4.4) instead. |
| "Run RebuildDb at session-end to reclaim deleted pages" | LiteDB's `Rebuild()` is full-file-rewrite. Acceptable as an explicit `/profiler-compact` command (already exists). Doing it at every session-end adds seconds of writer-thread work for marginal page-reclaim. |
| "Drop the `_schema` field entirely; encode in metadata" | The per-row schema discriminator is what makes the multi-version reader (§6) work. Removing it means a global rebuild on every release. |

Every entry above was considered, anchored to its origin dossier, and rejected against the data-stack rule.

---

## Appendix B — the additive-only ratchet, applied to this dossier

The categories every proposal in this dossier falls into (mirroring CLAUDE.md's skill-discipline taxonomy):

- **Cov (Coverage)** — none. The data stack does not grow in this pass.
- **Ver (Verification)** — PHASE 7 (migration round-trip tests), I-2 (install-heap diagnostic).
- **Bug** — A1, A2, A3 (restore advertised capture behaviour).
- **Refactor (perf)** — I-1, I-3, I-4, I-5, I-6, I-7, §4.1 through §4.6, B1 off-thread session-end. Same observable output, cheaper to produce.
- **Feat** — none. No new tabs, no new capture surfaces.

No entry in this dossier proposes lowering a budget, dropping a row, capping a sample rate, removing an index, or skipping a step "when not strictly needed". The data-stack-vs-storage-stack discipline is intact.

---

## Appendix C — open questions (raised, not resolved)

- **What fraction of the 233 MB is Cecil vs DynamicMethod bodies vs trampolines?** I-2 answers this. The whole §1 plan is conditional on Cecil being >50 %. The decision tree is in `hook-instrumentation.md` §8.12.
- **Does `LiteDB.Rebuild()` after the v6 → v7 migration shrink the file to its post-migration size, or do reclaimed pages stay in the file until the next checkpoint?** Filed; trivial follow-up.
- **Should the LoadoutSnapshotId FK on DamageDealtRow be enforced (the snapshot must exist) or tolerant (LoadoutSnapshotId may be ObjectId.Empty for old rows)?** Tolerant is safer; old rows degrade gracefully.
- **Is `double[]` ↔ `float[]` for spike per-mod-cat arrays a meaningful precision loss anywhere?** The smoothed ms-value loses precision below 1e-3 ms; below that we are in floating-point noise anyway. Empirical test against a fixed-seed synthetic session would confirm.
- **Does the binary journal frame want a compression option (LZ4) once the frame format is stable?** Filed for v0.7. Today the bytes saved per frame are already in the noise of the writer-thread budget; compression adds CPU for marginal benefit.
- **Should the per-session string-interning table (NpcName, BuffName, OwningMod, ContextCategory) be persisted as a separate `interning` collection or live only in RAM?** Persisted means the reader can decode without recomputing; not-persisted means a tiny RAM table on the writer. Recommendation: persist as a `stringTable` collection keyed by `(sessionId, kind, code)`. Filed for the schema implementation phase.

---

## Appendix D — the headline diff in numbers

If every recommendation in §1, §3, §4, §5, §6 lands as estimated:

```
                                v0.5                v0.6 target        after-pass projection
                                ────                ───────────        ──────────────────────
HOOK INSTALL RAM                233 MB              < 80 MB            ~95 MB    (I-1 + I-3 + I-4 + I-5 + I-6)
PROFILER MOD-RAM (steady)       234 MB              n/a                ~120 MB   (driven by install RAM drop)
PEAK INSTALL TRANSIENT          322-618 MB          < 200 MB           ~150 MB   (I-7 yielding install)
PROFILER WORKING SET            ~250 MB             n/a                ~115 MB

10-MIN CALAMITY DB              1,064 KB            < 600 KB           ~480 KB   (§4.1 + §4.2 + §4.3 + §4.4 + §4.5)
1-HR SESSION DB                 ~3.4 MB             n/a                ~1.55 MB
100-SESSION 30-MIN AVG          ~312 MB             n/a                ~144 MB

JOURNAL PEAK                    ~5 MB               n/a                ~1.5 MB   (§4.6 binary frame)
THREE-BACKUP TOTAL              ~3 MB               n/a                ~1.4 MB   (§7.5)

SCHEMA VERSION                  v6 (USER_VERSION 6) v7 (USER_VERSION 7) v7
SCHEMA-BUMPED COLLECTIONS       0                   n/a                7 collections coordinated
                                                                       (Session, ItemCreated, PlayerDeath,
                                                                       DamageDealt, SpikeWindow, TickAggregateWarm/Cold,
                                                                       StallEvent, StallCluster, ContextTransition, Insight,
                                                                       plus all-record short-name bump)

CAPTURE STREAMS                 17                  17 (untouched)     17        — no data-stack truncation
RING-TIER CADENCES              warm 1Hz / cold     unchanged          unchanged
                                1/min / archive 1
                                /session
BACKUPS                         3 rotating          3 rotating         3 rotating (smaller each)
```

Every dial in `baseline.md` (rows that this dossier owns or co-owns) moves in the better direction. Every capture surface stays whole. Every tier holds. Every backup rotates. The data stack is intact.

This is the storage-and-RAM contract for v0.6.

---

*End of cross-system storage and RAM research dossier. Implementation sequencing belongs in `context/perf-pass/plans/cross-storage-ram.md` once this is reviewed.*
