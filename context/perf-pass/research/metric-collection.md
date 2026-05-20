# Metric Collection — Optimisation Research & Plan (v0.6 pass)

> Scope: the per-tick hot path. Files in scope: `Profiling/MetricCollector.cs`,
> `PerModAttribution.cs`, `PerModSample.cs`, `PerTickAttributionRing.cs`,
> `RingBuffer.cs`, `TickFrame.cs`, `Baseline.cs`, `ProfilerFocusProbe.cs`,
> `ProbeStack.cs` (per-tick callback shape only — IL emission is shared with
> the hook-instrumentation pass and lives in that dossier).
>
> Hard rule for the entire document: **zero new allocations on the per-tick
> path; no capture surface lost**. Every recommendation that follows preserves
> the full data stack — per-mod CPU, per-mod alloc bytes, per-hook CPU,
> per-hook alloc bytes, GC pause delta, entity counts, focus state, baseline
> medians, spike-grade per-mod-per-category snapshots. If a proposal cannot
> hold that line it is not in this document.

---

## Table of contents

1. Current state audit (per-file walkthrough)
2. Measured baseline (re-stated from `baseline.md`, plus hot-path decomposition)
3. .NET 8 API performance characteristics (Stopwatch, GC.Get*, Channel, span)
4. Optimisation opportunities (categorised)
5. Root-cause hypothesis for the 441 ns/op enqueue regression
6. Cross-system dependencies and constraints
7. Prioritised execution order
8. References

---

## 1. Current state audit (per-file walkthrough)

The metric-collection subsystem is the hot loop. Every other system in the
mod either feeds it (`ProbeStack.Enter/Leave`, hook-instrumentation backends,
`ContextTagger.Snapshot`, focus probe) or reads from it (overlay tabs, spike
detector, stall detector, baseline, persistence). The walkthrough below traces
the per-tick path in execution order, line-referenced, and flags every
inefficiency it sees. Subsequent sections grade those into hot/warm/cold and
attach a fix.

### 1.1 `Profiling/MetricCollector.cs` (529 lines)

The orchestrator. It owns the rolling `RingBuffer<TickFrame>` history, every
per-mod/per-hook double[] array (raw, smoothed, average, history,
rolling — × CPU and bytes), the `Baseline`, the `SpikeDetector`, the
`StallDetector`, the `PerTickAttributionRing`, and the `ProfilerSelfHealth`
cadence-driver.

**Per-tick path** (sorted by execution order):

| Phase | Lines | Cost class | What it does | First-pass concerns |
|---|---|---|---|---|
| `BeginTick(tickIndex)` | 307-333 | **hot** | Captures Stopwatch start, GC pause baseline, focus state, runs StallDetector | Two API calls (`Stopwatch.GetTimestamp`, `GC.GetTotalPauseDuration`) + boxing in `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` |
| `EndTick(...)` body | 353-471 | **hot** | Stopwatch + GC read, builds TickFrame, pushes ring, harvests two backends × CPU+bytes, smooths, runs baseline + spike + stall, refreshes self-health, clears accumulator | The bulk of the 0.27 ms/tick. Smoothing + rolling-average loops dominate. |
| `UpdateRollingAverage` | 501-512 | **hot** | `for (int i = 0; i < source.Length; i++)` × 5 arrays. Called 4× per tick (perMod ms, perHook ms, perMod bytes, perHook bytes). | At 18 mods × 7 categories = 126 cells plus ~80 hooks = ~206 cells × 4 = 824 multiplies + 824 subtractions + 824 divisions per tick. This is the largest single CPU drain. |
| `SumAll` | 491-499 | **warm** | Two calls (one per backend slot). Re-iterates the raw arrays after they were already iterated for smoothing. | Redundant — the smoothing loop already sees every cell. |

**Inefficiencies flagged on the first pass:**

1. **`DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` at lines 318 and 371.**
   `DateTimeOffset.UtcNow` is roughly 200-400 ns on .NET 8 (it ultimately
   reads `GetSystemTimeAsFileTime`/`clock_gettime(CLOCK_REALTIME)` and
   constructs a DateTimeOffset struct). Called twice per tick. The unix
   millis on `TickFrame` does **not** need wall-clock precision below the
   tick boundary; `Stopwatch.GetTimestamp()` is monotonic and could supply
   a session-relative timestamp instead, with one wall-clock anchor captured
   at world-load. The unix millis is consumed downstream by:
   - `Baseline.TickPeriodMedian` (pairwise `TimestampUnixMs` deltas — could
     equally use monotonic stopwatch ticks).
   - `StallDetector.OnBeginTick` (wall-clock gap detection — needs the wall
     clock, but only at stall-emission cadence, not every tick).
   - JSON row timestamps (cold path — once at session end, or once per
     downsample bucket).

   **Net:** the per-tick wall-clock read is over-spec'd; a monotonic
   stopwatch timestamp + a single session anchor is identical for every
   downstream consumer and ~20× cheaper per call.

2. **Five smoothing/rolling passes** in `EndTick` (lines 388-417). Each
   `UpdateRollingAverage` pass touches three arrays (history, rolling,
   average) — that is `5 × 3 = 15` distinct memory regions in cache traffic
   per tick. The history array is the largest: `cells × historyCapacity` = 126
   × 1800 = 226 800 doubles = 1.81 MB for perMod CPU alone, doubled if alloc
   tracking is on, doubled again for hooks. Total resident hot data: ~7 MB
   of double[] sloshing through L1/L2 each tick.

3. **`PerModAttribution.HarvestInto` allocates nothing** but iterates a
   `long[]` and writes a `double[]`, performing a `* (1000d / Stopwatch.Frequency)`
   multiply per cell (line 310 of PerModAttribution.cs). The constant
   `ticksToMs` is recomputed every harvest call (4 calls × 2 backend
   slots possible). Trivial fix: cache once at world-load.

4. **`Baseline.Recompute` runs four full histogram passes** over the entire
   ring buffer at every EndTick. That is `4 × 1800 = 7200` histogram bumps
   per tick (Baseline.cs:121-170). At 60 Hz, 432 000 bumps/sec — measurable
   even though each is one divide + one array bump. The frame-median and
   tick-period-median can be maintained incrementally with a P² estimator or
   a running balanced-tree sketch rather than recomputed from scratch each
   tick. See §4.6.

5. **`ProfilerFocusProbe.Read()` is a try/catch wrapping a static field
   read.** `Terraria.Main.hasFocus` is a `bool` static. The try/catch is
   defensive against tests where Main may be uninitialised. On the hot path,
   the JIT cannot inline through the `catch (Exception)`. Better idiom: a
   `bool _focusAvailable` flag set once at world-load, then a plain read.
   See §4.10.

6. **`PerModAttribution.BeginTick()` clears two backends' worth of `long[]`
   arrays** at the end of every tick (line 462, deferred from BeginTick by the
   audit fix in commit `5b28e41`). `Array.Clear` is fast (uses memset) but
   touches the same 7 MB of arrays that the smoothing loop just touched plus
   the alloc-byte arrays. Cache pollution is real.

7. **`_history.Push(in frame)` (line 381)** — `TickFrame` is a 64-byte
   struct (8 longs/doubles + 3 ints + `PerModSample[]?` reference + a
   nested `EventContext`). The Newest read pattern is one struct copy out
   per consumer. Could be made smaller — see §4.5.

### 1.2 `Profiling/PerModAttribution.cs` (401 lines)

A static class that owns the accumulator. Backend 0 (delegate) and backend 1
(ILHook, parallel-mode only) each have their own `long[]` for ticks and
hook-ticks, plus optional parallel arrays for alloc bytes.

**Per-tick path:**

| Function | Lines | Cost class | What it does | First-pass concerns |
|---|---|---|---|---|
| `Add(int modId, int categoryId, int hookId, long ticks)` (3-arg) | 201-204 | **very hot** | Calls 5-arg overload with `backendId=0` | Every delegate-backend HookProbe call site lands here. ~thousand calls/tick. |
| `Add(backendId, modId, categoryId, hookId, ticks)` (5-arg) | 213-237 | **very hot** | Validates 3 bounds, writes 2 array slots | Three unsigned-cast bounds checks per call. JIT can prove some elidable, but cannot remove the `backendId` check on the static path. |
| `Add(backendId, modId, categoryId, hookId, ticks, bytes)` (6-arg, alloc) | 246-279 | **very hot** | Same as above plus 2 more array writes | 5 bounds checks total. Alloc-tracking ILHook path. |
| `BeginTick()` | 181-193 | **hot** | `Array.Clear` × (backends × (2 + alloc?2)) | Touches up to 8 arrays. Reaches ~10 KB of memset traffic per tick at 18 mods. |
| `HarvestInto(double[] dest, int backendId)` | 297-312 | **warm** | One pass `long * (1000/Freq)` → `double[]` | Recomputes the ticks→ms factor each call. |
| `HarvestHooksInto` etc. | 318-385 | **warm** | Symmetrical with HarvestInto | Same constant-recompute issue. |

**Inefficiencies flagged on the first pass:**

1. **The 3-arg `Add` overload is one extra method-frame deep** because it
   forwards to the 5-arg one. The JIT will usually inline a static method
   marked aggressively-inlinable, but neither overload has
   `[MethodImpl(MethodImplOptions.AggressiveInlining)]`. The IL-emitted
   `call PerModAttribution.Add(...)` from the delegate-path HookProbe code
   will not benefit from any inlining the C# compiler could otherwise do —
   the call is from a dynamically-emitted method body. The cost is a managed
   call (~5-15 ns including arg passing) per attributed event, multiplied by
   thousands of events per tick. See §4.1.

2. **Triple bounds-check per `Add`.** Each `(uint)x >= (uint)len` is a
   single compare-and-branch; collectively they cost a couple of ns plus
   a branch prediction slot. In steady state these checks always succeed,
   so the branch predictor handles them fine. The genuine cost is the
   instruction-cache space (each check is ~5 bytes of x64). Marginal.

3. **Two array-write paths in the CPU-only Add** (line 229 perMod cell, line
   235 perHook cell). Both are random-access writes into different cache
   lines. The hookTicks slot is small (~80 entries × 8 bytes = 640 bytes,
   fits in 10 cache lines) so it tends to stay hot. The perMod ticks array
   is 18 × 7 × 8 = 1008 bytes — also small. Both fit in L1 easily.

4. **`List<HookDescriptor>` for hook metadata** (line 70). Accessed in
   `ProbeStack.Leave` to resolve modId/categoryId from hookId (ProbeStack.cs
   :117). Reads through `IReadOnlyList<HookDescriptor> Hooks`, which forces
   the `List<T>` indexer. Better: a flat `HookDescriptor[]` exposed by
   reference. See §4.2.

5. **`Array.Resize` in `RegisterHook` (lines 150 and 154)** — install-time
   only, so cold path. Currently O(n²) in registrations because each register
   resizes every backend's hookTicks/hookBytes array by one. With ~10 000
   hooks installed at startup that is 50 M element-copies. Cold path, but
   the install-delta numbers (481 MB RAM, several seconds wall time) say
   this matters even off the hot path. Trivial fix: doubling growth or one
   final resize after the full registration pass.

### 1.3 `Profiling/PerModSample.cs` (43 lines)

A 4-field struct (`ModId int`, `CpuMs double`, `AllocatedBytes long`,
`HookCalls int`). Total size: 24 bytes (with padding).

Currently **unused on the hot path** — `TickFrame.ModSamples` is always
`null` (MetricCollector.cs:378). The struct exists as a planned shape for a
future per-frame per-mod array. The comment in TickFrame.cs:51-56 acknowledges
this is a later memory-tuning step.

**Implication for this pass:** if/when we wire it up we must ensure the
backing array lives in the ring buffer slot rather than being allocated per
tick (one `PerModSample[ModCount]` per slot × 1800 slots = 1800 × 18 × 24 =
~777 KB at default sizing; acceptable).

### 1.4 `Profiling/PerTickAttributionRing.cs` (253 lines)

The 1800-tick × per-mod-totals ring (plus a smaller 120-tick × per-mod-per-cat
snapshot ring). Holds `float[]` arrays instead of `double[]` to halve the
memory footprint — comment at line 39 confirms the conversion cost is
accepted.

**Per-tick path:**

| Function | Lines | Cost class | What it does | First-pass concerns |
|---|---|---|---|---|
| `Push(gameTick, perModCatMs, perModCatBytes)` | 105-148 | **hot** | Two nested loops: outer = mods (18), inner = categories (7). Reads `double[]`, writes `float[]`. | 126 iterations × (read double, cast to float, write float, accumulate two totals). One mul-add per cell. No allocations. |
| `GetPerModMs(gameTick, modId)` | 154-164 | **cold** | One slot lookup. | UI tab, off-tick. |
| `CopyLatestCategorySnapshot(Span<float>, Span<float>)` | 188-210 | **rare** | Per-spike, not per-tick. | Only fires when a spike is detected. |
| `TryGetCategorySnapshot(gameTick, Span, Span)` | 226-252 | **cold** | UI drill-down. | Off-tick. |

**Inefficiencies flagged on the first pass:**

1. **Double-to-float cast happens inside the inner loop** (line 127, 133).
   The JIT compiles this to a `cvtsd2ss` per cell — about 5 cycles each. For
   126 cells × 2 (CPU + bytes), that is 252 conversions per tick. Acceptable
   but a SIMD pass that converts 4 doubles → 4 floats in one
   `Vector256<double>` → `Vector128<float>` would do this in ~30
   instructions total. See §4.7.

2. **Loop reads `perModCatMs[cell]` and `perModCatBytes![cell]`** — two
   independent arrays accessed by the same index. Could be interleaved as a
   `(double, double)` struct or a single Span pair, but cache lines are 64 B
   so prefetching handles this fine. Marginal.

3. **Slot arithmetic** `_writeCount % _historyTicks` (line 111, 162, 176, 192,
   232) — `_historyTicks` is 1800, not a power of two. The JIT cannot
   replace `%` with `&`. Each modulo is a ~20-cycle DIV. Five-ish modulos
   per Push. **If we widen historyTicks to 2048 (next power of 2), every
   modulo becomes an AND**. See §4.4.

4. **`bool trackBytes = _perModBytes != null && perModCatBytes != null`**
   (line 116) is a per-tick null check. Hoist it out of the loop — the JIT
   should already, but checking the disassembly will confirm. Negligible.

### 1.5 `Profiling/RingBuffer.cs` (120 lines)

Simple. The indexer (`this[int]`, lines 78-99) is the only thing the hot path
touches via `_history.Newest`, and the property looks up `this[_count - 1]`,
which executes an `if (physical < 0) physical += _items.Length`. The full
indexer is called once per tick from `EndTick` (via `_history.Push(in frame)`,
not indirectly — Push is the hot path call). Reads via `history[i]` happen
inside Baseline's four histogram passes (1800 calls per pass × 4 passes =
7200 indexer calls per tick).

**Inefficiencies flagged:**

1. **The indexer has a branch on `physical < 0`** (line 92). For sequential
   access patterns (the histogram passes) the branch is predictable, but the
   bounds check at line 82 cannot be elided by the JIT because `_count` is a
   private field, not a compile-time bound.

2. **Baseline's `history[i].FrameTimeMs` reads** are abstraction-broken:
   they could iterate the underlying array twice (head→end, 0→head) and
   skip the indexer entirely. See §4.6.

### 1.6 `Profiling/TickFrame.cs` (67 lines)

8 fields: 4 × 8 B (`TimestampUnixMs`, `TickIndex`, `FrameTimeMs`, `GcTimeMs`),
3 × 4 B (`ProjectileCount`, `NpcCount`, `DustCount`), 1 reference
(`ModSamples`), 1 nested struct `EventContext`. The struct's total size
depends on `EventContext`'s layout (event-tagger system). With the reference
field the struct can be GC-scanned; a struct without references would not be.

**Inefficiencies flagged:**

1. **`PerModSample[]? ModSamples` reference field** makes every slot in
   `_items[]` (1800 slots) a GC-scanned object. Unused today (always null).
   Remove it for the pass, or leave it null but tag the type as
   `[StructLayout(LayoutKind.Sequential)]` after replacement design.

2. **No explicit `[StructLayout]`** — the C# compiler defaults to
   `Sequential` for structs that meet certain rules, but explicit is safer.

3. **Field ordering** is reasonable (longs first, then ints, then ref, then
   nested struct). One small win: keep all longs/doubles together to avoid
   any future padding inserted between an int and a long.

### 1.7 `Profiling/Baseline.cs` (199 lines)

Median + MAD over the 1800-frame history via a 512-bucket histogram. One
`_histogramScratch = new int[512]` allocated once at construction; cleared
and re-populated four times per tick.

**Per-tick cost decomposition (Baseline.Recompute):**

| Pass | Lines | Reads | Writes | Notes |
|---|---|---|---|---|
| `FrameMedian` | 121-128 | 1800 indexer reads | 1800 bucket bumps | One `ClearHistogram` (memset 512 ints = 2048 B) |
| `FrameMad` | 130-142 | 1800 indexer + abs() | 1800 bucket bumps | Reads the same field. |
| `TickPeriodMedian` | 144-155 | 1799 pairwise reads | 1799 bumps | TimestampUnixMs deltas. |
| `TickPeriodMad` | 157-170 | 1799 pairwise reads | 1799 bumps | Same data, different aggregation. |

Total: ~7200 indexer reads + 7200 histogram bumps + 4 histogram memsets per
tick. At 60 Hz, that is 432 000 reads/sec + 432 000 bumps/sec. On Apple
Silicon at ~3 GHz with ~3 cycles per bucket-bump (divide + array store + cmp),
this costs ~430 µs/sec = 0.026 ms/tick. **Roughly 10 % of the profiler's
0.27 ms/tick budget is the baseline recompute.** Lots of headroom: see
§4.6 for incremental algorithm options.

**Inefficiencies flagged:**

1. **Four passes over the same data with two distinct aggregations** —
   `FrameMedian` and `FrameMad` could fuse into one pass that first
   computes the median, then computes MAD against it; today they fully
   independently iterate.

2. **The histogram bucket-bump has a divide by a constant**
   (`(int)(v / 0.5)` — line 176). The JIT replaces division by a constant
   with reciprocal multiplication, so this is a single mul+shift in
   x64/ARM64. Fine.

3. **Pairwise delta computation reads `TimestampUnixMs` via the indexer
   twice per slot.** Two indexer calls per pair × 1799 pairs = 3598 indexer
   calls just for the period median.

4. **Allocation EMA (line 100)** is fine.

### 1.8 `Profiling/ProfilerFocusProbe.cs` (43 lines)

Already noted in §1.1 point 5. Try/catch wrapping a single field read on a
hot path is wrong by construction. A one-shot init flag converts it into a
straight load.

### 1.9 `Profiling/ProbeStack.cs` (191 lines) — per-tick callback shape

The IL emitter installs `ProbeStack.Enter(hookId) / try { } finally
{ ProbeStack.Leave(); }` around every wrapped method, or
`EnterCpuAlloc(hookId, allocBytesAtEnter) / LeaveCpuAlloc()` on the
allocation-tracking path.

**Per-call cost decomposition (Enter + Leave pair, non-alloc path):**

| Step | Lines | Op | Approx ns @ 3 GHz |
|---|---|---|---|
| Enter: read `_stack` ThreadStatic | 73 | Field read | 5-10 ns (first call), <1 ns when JIT keeps register |
| Enter: null/length check | 74-86 | Branch | <1 ns predicted |
| Enter: assign HookId | 89 | Memory store | 1 ns |
| Enter: Stopwatch.GetTimestamp | 90 | Syscall-equivalent | 10-30 ns (see §3.1) |
| Enter: _depth++ | 91 | Store | 1 ns |
| `<body>` | - | mod's code | varies |
| Leave: Stopwatch.GetTimestamp | 112 | API | 10-30 ns |
| Leave: bounds + descriptor read | 115-117 | Indexer | 2-4 ns |
| Leave: PerModAttribution.Add | 118 | Method call | 5-15 ns |
| **Total per pair (Lite path)** | | | **~30-90 ns + body** |

The allocation-path pair (`EnterCpuAlloc/LeaveCpuAlloc`) adds **two
`GC.GetAllocatedBytesForCurrentThread()` reads** (one at Enter side via
IL-emitted argument, one inside LeaveCpuAlloc at line 165). Each is ~30 ns on
.NET 8 (§3.2). So the allocation pair is roughly **2× the cost of the Lite
pair** — ~60-150 ns per attributed event.

**Inefficiencies flagged:**

1. **No `[MethodImpl(AggressiveInlining)]` on `Enter`, `Leave`, `EnterCpuAlloc`,
   `LeaveCpuAlloc`.** These are static methods called from IL-emitted bodies;
   the C# inliner won't touch the call sites anyway, but the methods'
   internal helpers (e.g. the bounds checks) would benefit if subdivided.
   Marginal.

2. **ThreadStatic field reads are non-trivial** — under .NET 8 on Linux they
   use TLS, on Windows they use the FLS slot. Apple Silicon: TLS via
   `mrs x0, tpidrro_el0` plus an offset — single-digit ns. The JIT caches
   the address per method body, so the second read inside the same method
   is free.

3. **The Frame struct (3 fields: HookId int, StartTicks long,
   StartAllocBytes long)** is 24 bytes with padding. That means one cache
   line (64 B) holds 2 frames. Probe-stack depth of ~8 fits in 4 cache
   lines — fine.

4. **`PerModAttribution.Hooks[f.HookId]`** indexer call returns a struct
   copy of `HookDescriptor` (12 bytes: 2 ints + ref-to-string). Then the
   call passes its fields. A flat `HookDescriptor[]` accessed by ref would
   skip the IList<T> indirection. See §4.2.

---

## 2. Measured baseline

Restated from `context/perf-pass/baseline.md` and annotated.

| Surface | v0.5 | v0.3 | Notes |
|---|---|---|---|
| Game-thread enqueue latency | **441 ns/op** | 276 ns/op | +60 % regression |
| Per-tick PerformanceProfiler cost | **0.27 ms/tick** | (not measured) | Mod is its own top contributor |
| Avg frame ms (real session) | 0.96 | - | Profiler is ~28 % of the avg frame |
| Hook install delta | 233 MB / 10 258 hooks | - | ~23 KB / hook (cold) |
| End-of-session main-thread stall | 8.5 s | - | Profiler-attributed (separate dossier) |

### 2.1 Per-tick cost decomposition (estimate, pre-pass)

Working from the audit above, the 0.27 ms/tick budget for the profiler
itself decomposes roughly as:

| Phase | Estimated cost | Reasoning |
|---|---|---|
| Per-hook Enter/Leave on the IL-instrumented bodies | ~120-150 µs | ~1500-2000 hook firings/tick × ~70 ns/pair |
| `PerModAttribution.Add` calls | ~30-50 µs | inside the per-hook number above (it's not a separate sample) |
| `MetricCollector.EndTick` orchestration (smoothing/rolling/harvest) | ~50-70 µs | 5 × `UpdateRollingAverage`, 4 × harvest, 2 × `SumAll` |
| `Baseline.Recompute` (4 histogram passes) | ~25-30 µs | 7200 indexer reads + 7200 bumps + 4 memsets |
| `PerTickAttributionRing.Push` | ~10-15 µs | 126 cells × cvtsd2ss + array store |
| `SpikeDetector.OnTick` + `StallDetector.OnBeginTick` | ~10 µs | per-tick path of those subsystems |
| `Stopwatch.GetTimestamp` × ~4 / tick (collector itself) | ~0.1 µs | Negligible at collector scope, but each hook also pays 2× |
| `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` × 2 | ~0.5-0.8 µs | Worth removing on principle |
| `GC.GetTotalPauseDuration` × 2 | ~0.4-1 µs | Worth checking |
| `GC.GetAllocatedBytesForCurrentThread` × 2 (collector) + per-hook (alloc path) | ~30-60 µs (alloc) | Per-hook reads dominate |

These are estimates from API costs (§3) × call counts. The first
benchmark task of the pass is to verify these numbers with a JIT-warm
`BenchmarkDotNet` micro-bench. The above is the prior for prioritisation.

### 2.2 What "< 0.10 ms/tick" requires

The target is a 2.7× reduction. The audit suggests four headline levers:

1. Cut per-hook Enter/Leave from ~70 ns → ~30 ns: saves ~80 µs/tick.
2. Replace 4-pass histogram baseline with incremental P²: saves ~25 µs/tick.
3. Fuse smoothing/rolling/harvest into a single SIMD-able pass: saves
   ~30 µs/tick.
4. Hoist the redundant `Stopwatch.Frequency` divide out of every harvest:
   saves ~1 µs/tick (small, near-free).

Adding these: ~136 µs/tick saved. Starting from 270 µs, that lands at
~134 µs — short of the < 100 µs goal by ~30 %. A fifth lever is needed:
either reduce hook-firing count (out of scope — that would reduce capture),
or move the heaviest collector work off the game thread (e.g. defer
smoothing/rolling to a background pass that publishes a frame-stale view
to the UI). See §4.13.

---

## 3. .NET 8 API performance characteristics

This section lays out the cost shape of every system API the metric-
collection hot path touches, so §4's recommendations have a numerical
anchor. Apple Silicon (M-series) is the primary dev target; Windows x64
is the primary distribution target. Numbers below are from official docs
and well-cited benchmarks; cite the source on first use.

### 3.1 `Stopwatch.GetTimestamp()`

| Platform | Backing primitive | Typical cost |
|---|---|---|
| Windows x64 (invariant TSC) | `QueryPerformanceCounter` → `rdtsc` direct (no syscall) | **15-18 ns** |
| Linux x64 (TSC) | `clock_gettime(CLOCK_MONOTONIC)` vDSO → rdtsc | **30-35 ns** |
| Linux x64 (HPET fallback) | `clock_gettime` → kernel HPET read | 500-800 ns |
| macOS ARM64 (Apple Silicon) | `mach_absolute_time()` → CNT_VIRT_EL0 reg | **~10-20 ns** |

Source: <https://aakinshin.net/posts/stopwatch/> (Andrey Akinshin,
BenchmarkDotNet maintainer). The Windows fast path is the canonical case
for our distribution target. Apple Silicon is an ARM64 register read with
no kernel transition.

**Cost on our hot path:** Two reads per `MetricCollector.EndTick` cycle
(BeginTick + EndTick boundary) + two reads per IL-wrapped hook
(`ProbeStack.Enter/Leave`). At ~2000 hook firings/tick: **4000 reads × 15 ns ≈
60 µs/tick on Windows; ~80 µs/tick on Linux**. This is a non-negligible
slice of the 270 µs/tick total. The only safe reduction is at the
collector boundary (2 → 1 per tick) — see §4.3 — because the per-hook
reads are the entire point of the instrumentation.

### 3.2 `GC.GetAllocatedBytesForCurrentThread()`

Microsoft docs (<https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-8.0>)
do not publish a per-call cost. Reference implementations in CoreCLR
(see `runtime/src/coreclr/vm/comutilnative.cpp`) show the method reads
the current thread's per-thread allocation context (a struct in the
CLR's thread-local block) — it does not take a lock, does not walk
heap structures, and is a single field-style read after the TLS
dereference.

Empirically, on .NET 8 + invariant TSC:

- Windows x64: ~10-20 ns per call.
- Apple Silicon: ~5-15 ns per call.
- Linux x64: ~15-25 ns per call.

Source: extrapolation from the BenchmarkDotNet `MemoryDiagnoser`
implementation, which calls `GetAllocatedBytesForCurrentThread` before
and after each iteration (cited by Adam Sitnik:
<https://adamsitnik.com/the-new-Memory-Diagnoser/>). The overhead it
adds to a typical benchmark is "less than 10 ns" per pair on .NET 6+.

**Cost on our hot path:** On the alloc-tracking path, two reads per
hook firing × ~2000 firings/tick = **40-80 µs/tick** of allocation reads
alone. On the Lite path, only 2 reads per tick at the collector boundary
— negligible.

### 3.3 `GC.GetTotalPauseDuration()`

Microsoft docs
(<https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalpauseduration?view=net-8.0>)
confirm the method is available in .NET 7+ (so .NET 8 is fine, despite
some Microsoft documentation pages saying .NET 9 — that refers to the
OpenTelemetry metric name, not the API).

Source link (CoreCLR):
`https://github.com/dotnet/runtime/blob/main/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs`

The implementation reads a `TimeSpan` constructed from a `long`
maintained by the GC (`g_total_suspended_time_in_nanoseconds`-equivalent).
It is monotonic and cumulative — exactly the property the collector relies
on (MetricCollector.cs:521-528).

Cost: one TLS-free field read wrapped in a `TimeSpan` ctor. Expect ~5-15
ns per call. We call it twice per tick (BeginTick + EndTick).
**Cost: ~0.5-1 µs/tick.** Effectively free.

### 3.4 `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`

Reads the system real-time clock. On Windows: `GetSystemTimePreciseAsFileTime`
or `GetSystemTimeAsFileTime`. On Linux: `clock_gettime(CLOCK_REALTIME)`. On
macOS: `gettimeofday`. All three go through a vDSO/kernel-fast-path; the
managed wrapper then constructs a `DateTimeOffset` struct, then the
`ToUnixTimeMilliseconds()` call performs a divide. Net cost:

- Windows: ~150-250 ns
- Linux: ~50-100 ns
- macOS: ~50-100 ns

These are the largest single per-call cost in the collector boundary path.
At 2 calls per tick × 60 Hz = ~30 µs/sec on Windows, ~12 µs/sec on
macOS — small in absolute terms but disproportionate given the
information content (a wall-clock millis we only need once per session
for anchoring).

Source: <https://github.com/dotnet/runtime/issues/15207> discussion, plus
the well-known property of `clock_gettime`-backed APIs in managed
runtimes.

### 3.5 `System.Threading.Channels<T>.Writer.TryWrite` (unbounded)

Cited benchmarks (<https://www.codegenes.net/blog/when-should-system-threading-channels-be-preferred-to-concurrentqueue/>,
Microsoft's own `An Introduction to System.Threading.Channels`
<https://devblogs.microsoft.com/dotnet/an-introduction-to-system-threading-channels/>):

- Unbounded channel `TryWrite`: ~25-60 ns per call when the queue is not
  contended; goes higher under reader-side back-pressure.
- ConcurrentQueue `Enqueue`: ~70-150 ns per call.
- BlockingCollection: 200-500 ns per call.

The Channels implementation uses a lock-free linked list of segments,
where each segment is a power-of-two-sized array with sequence-number
synchronisation per slot. The producer pays one CAS for sequence updating
on success path.

**Per-op overhead breakdown for `DbWriterThread.Enqueue`:**

```
Enqueue cost = ChannelWriter.TryWrite cost
             + Interlocked.Increment cost (~5-10 ns)
             + Volatile.Read in soft-cap branch (~1-2 ns)
             + the DbWriteOp struct copy (~4-8 ns at 56 B size)
             + the allocation of the payload object (varies, ~30-200 ns)
             + the Channel's per-op allocation if any (segment growth)
```

For our 441 ns/op measurement, the **payload allocation dominates** —
see §5 for the regression root-cause.

### 3.6 `Array.Clear`

Source: <https://github.com/dotnet/runtime/blob/main/src/coreclr/System.Private.CoreLib/src/System/Buffer.cs>.
Calls `memset` via the runtime helper. SIMD on modern x64
(AVX-512 if available, AVX2 by default). On Apple Silicon: NEON memset.

Cost: ~5 ns + ~0.05 ns/byte. For our 10 KB of accumulator arrays at
`PerModAttribution.BeginTick`: ~5 + 500 = 505 ns per tick — negligible.

### 3.7 Span&lt;T&gt; / ref struct / `MemoryMarshal.GetArrayDataReference`

`MemoryMarshal.GetArrayDataReference(arr)` returns a `ref T` to index 0
without a bounds check. Using `Unsafe.Add(ref T, n)` then iterates without
bounds checks. JIT compiles to identical native code as a raw pointer.

This is the canonical "remove bounds checks" pattern in performance-
critical .NET 8 code. Worth applying to:

- `MetricCollector.UpdateRollingAverage` inner loop (5 per tick × 200ish cells).
- `PerTickAttributionRing.Push` inner loop (126 cells).
- `Baseline.BumpBucket` cluster (7200 calls/tick).

Source: <https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.memorymarshal.getarraydatareference>.

### 3.8 SIMD on .NET 8

`Vector256<double>` (AVX2) and `Vector128<double>` (SSE2 / NEON) are
auto-vectorisable through the `System.Numerics.Tensors`-style intrinsics
in `System.Runtime.Intrinsics`. The JIT does NOT auto-vectorise arbitrary
for-loops; it requires the developer to write
`Vector256.LoadUnsafe(ref source, (nuint)i)` etc. explicitly.

For `UpdateRollingAverage` (3-array fused op):

```
rolling[i] += source[i] - history[index];
history[index] = source[i];
average[i] = rolling[i] / samples;
```

This is three simple BLAS-1-like operations. Vectorised:

```
Vector256<double> src = Vector256.LoadUnsafe(ref sourceRef, i);
Vector256<double> hist = Vector256.LoadUnsafe(ref historyRef, index);
Vector256<double> roll = Vector256.LoadUnsafe(ref rollingRef, i);
roll = roll + src - hist;
Vector256.StoreUnsafe(roll, ref rollingRef, i);
Vector256.StoreUnsafe(src, ref historyRef, index);
Vector256<double> avg = roll / Vector256.Create((double)samples);
Vector256.StoreUnsafe(avg, ref averageRef, i);
```

4 doubles per iteration instead of 1. At 200 cells × 5 passes per tick:
~250 vector ops/tick vs 1000 scalar ops/tick. Estimated saving: ~10-15 µs/tick.

Source: <https://learn.microsoft.com/en-us/dotnet/standard/simd>,
plus the dotnet/runtime blog posts on tensors and intrinsics
(<https://devblogs.microsoft.com/dotnet/dotnet-8-performance-improvements-in-dotnet-8/>
section on SIMD).

### 3.9 Apple Silicon specifics

The dev box is Apple Silicon (per CLAUDE.md). Several APIs behave better
on Apple Silicon than on x64:

- `Stopwatch.GetTimestamp`: ~10 ns (CNTVCT_EL0 register).
- `GC.GetAllocatedBytesForCurrentThread`: ~5-10 ns (TLS read is one MRS).
- Wall-clock APIs: `gettimeofday` is vDSO → ~50 ns.

Distribution-time the mod ships to Windows x64 mostly, so optimisations
must hold up there too. The win:lose pattern for the optimisations in §4
is mostly the same across platforms; nothing in our recommendations is
ARM-specific.

---

## 4. Optimisation opportunities

Categorised: **A.** hot-path CPU cuts (per-hook), **B.** hot-path CPU cuts
(per-tick), **C.** allocation removal, **D.** memory layout & cache, **E.**
branch & bounds-check elimination, **F.** SIMD / span usage, **G.** ring
buffer indexing math, **H.** algorithmic improvements, **I.** thread
relocation. Each entry: what, why, expected delta, risk, evidence path.

### 4.1 [A] Eliminate the 3-arg `PerModAttribution.Add` overload

**What:** Delete `Add(int modId, int categoryId, int hookId, long ticks)`
(line 201). Inline its body into the 5-arg overload's call sites by
emitting `call PerModAttribution.Add(0, modId, categoryId, hookId, ticks)`
directly from the delegate-path probes.

**Why:** Saves one method-frame per attributed event. The delegate-path
`HookProbe.Time*` methods always credit backend 0; making them emit the
5-arg call removes one indirection.

**Expected delta:** ~5-10 ns/event × ~2000 events/tick = **10-20 µs/tick**.

**Risk:** None — purely a re-emit of the IL.

**Evidence:** Disassemble both overloads with `DOTNET_JitDisasm` and
confirm the 3-arg version is a non-inlined tailcall to the 5-arg version.
Verify post-change with the same disassembly.

### 4.2 [A] Replace `IReadOnlyList<HookDescriptor>` with a flat array exposed by ref

**What:** Change `private static readonly List<HookDescriptor> _hooks` to
`private static HookDescriptor[] _hooksArr` (sized once at install end). Expose
`internal static ref HookDescriptor GetHook(int hookId)`. Update
`ProbeStack.Leave/LeaveCpuAlloc` to use `ref var desc = ref
PerModAttribution.GetHook(hookId)` instead of the indexer.

**Why:** Removes the `IList<T>.this[int]` indirection (a virtual call on
the interface). Allows ref access — no struct copy on read.

**Expected delta:** ~2-4 ns/event × ~2000 events/tick = **5-10 µs/tick**.

**Risk:** None at runtime — `_hooks` is append-only during install, frozen
at world-load. The flat-array view becomes immutable post-install. Tests
must still pass.

**Evidence:** Read the IL for ProbeStack.Leave before/after.

### 4.3 [A] Combine the collector-boundary Stopwatch + GC reads

**What:** Read `Stopwatch.GetTimestamp()` once at `BeginTick`, once at
`EndTick`, and use the same value where we currently read it again. We
already do this; the win is removing the `DateTimeOffset.UtcNow.ToUnixTime
Milliseconds()` reads at BeginTick (line 318) and EndTick (line 371).
Replace with: capture one wall-clock anchor at world-load, store the
session-relative monotonic offset on TickFrame.

**What changes downstream:** TickFrame gains a `long SessionTicksFromAnchor`
field (or repurposes `TimestampUnixMs` as monotonic ticks). The wall-clock
ms is materialised at JSON-write time (`anchor + ticks * (1000.0 /
Stopwatch.Frequency)`). Baseline's `TickPeriodMedian` uses the monotonic
delta — strictly better than the wall-clock delta (no clock skew, no NTP
jumps).

**Expected delta:** ~150-250 ns × 2 = **300-500 ns/tick (~0.3 µs/tick)**.

**Risk:** Baseline's tick-period assumes ms units. Switch the units to
stopwatch ticks (or convert once when comparing). The StallDetector uses
wall-clock ms for "OS suspended" detection; that still needs the wall
clock, but only when a stall is suspected — a per-stall call, not per-tick.

**Evidence:** Microbench `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`
in BenchmarkDotNet before/after.

### 4.4 [G] Power-of-two history capacity

**What:** Change `ProfilerSystem.HistoryCapacity` from `30 * 60 = 1800` to
`2048` (or `30.something * 60` rounded up). Same for
`CategorySnapshotTicks` (120 → 128). The ring's modulo becomes a bitmask.

**Why:** `_writeCount % _historyTicks` becomes `_writeCount & (_historyTicks
- 1)`. `%` against a non-power-of-two is a hardware DIV (~20 cycles on
x64, ~10-15 on ARM64). `&` against a power-of-two-minus-1 is one cycle.

Hit sites in PerTickAttributionRing alone:

- `Push` line 111, 112: 2 modulos.
- `GetPerModMs` line 161, 162: 2 modulos.
- `GetPerModBytes` line 176, 177: 2 modulos.
- `CopyLatestCategorySnapshot` line 192: 1 modulo.
- `TryGetCategorySnapshot` line 232, 233: 2 modulos.

Push happens every tick: 2 modulos × 60 Hz = 120 modulos/sec. The Gets and
Snapshot calls happen on UI access and on spike — say 100/sec aggregate.
Total ~220 modulos/sec × 15 ns = ~3 µs/sec.

Marginal in isolation, but **the win compounds with every other slot-math
heavy path** — `RingBuffer<T>.this[int]` doesn't currently use modulo
because it uses subtract-and-correct, but the same `Capacity` would let it
do the same correction with a single `& mask`.

**Expected delta:** **~3-5 µs/sec, plus simpler bounds reasoning for SIMD.**

**Risk:** History capacity becomes 2048 ticks (34.13 s @ 60 Hz) vs 1800
(30 s). The 30-s assumption is documented but not load-bearing; every
consumer reads `History.Count` and `History.Capacity`. Pin a test that
rejects `(capacity & (capacity - 1)) != 0` to prevent regressions.

**Evidence:** Two BenchmarkDotNet benchmarks of `PerTickAttributionRing.Push`,
one at 1800, one at 2048. Expected ratio: 1.1-1.3×.

### 4.5 [D] Shrink `TickFrame` and drop the `PerModSample[]?` reference field

**What:** Remove `PerModSample[]? ModSamples` from `TickFrame.cs:56`. The
field is always null today; its presence makes every TickFrame a managed
GC scan target.

**Why:** Without the ref field, `TickFrame` becomes a pure POD struct. The
GC no longer has to scan 1800 × `_items` slots for references each Gen2
collection. This compound with §4.16 (move TickFrame to a struct-of-arrays
layout) gives an order-of-magnitude reduction in GC card-table work.

**Expected delta:** Indirect — reduces background GC cost, particularly on
Gen2. ~1-2 µs/sec on a steady-state heap.

**Risk:** Future per-frame per-mod attribution loses the planned slot. The
replacement design (§4.16) covers that case with a parallel array, which
is what a `PerModSample[]` per slot would have been anyway — just owned
by the collector, not embedded in the struct.

### 4.6 [H] Replace 4-pass histogram baseline with an incremental algorithm

**What:** Replace `Baseline.Recompute`'s four full passes with one of:

| Option | Median precision | MAD precision | Per-tick cost | Notes |
|---|---|---|---|---|
| **P² quantile estimator** (Jain & Chlamtac 1985) | ~1 % error | Hardcoded as separate P² for abs-dev | O(1) per insertion | Five-marker algorithm, O(1) per update |
| **Welford's online mean+variance** | Mean, not median | Std, not MAD | O(1) per insertion | Robust but doesn't give median/MAD directly |
| **EMA of median + EMA of \|x-median\|** | ~5 % error | ~5 % error | O(1) | Simple, cheap, no allocations |
| **Order-statistic tree (skip list)** | Exact median | Exact MAD | O(log n) per update; O(1) median read | More code, more memory; might allocate |
| **Sliding-window histogram (incremental bump+unbump)** | ~0.25 ms (bucket-mid) | same | O(1) per insertion | Decrements the oldest entry when ring wraps |
| **Frugal-2U (Ma, Muthukrishnan, Sandler 2014)** | ~1 % error | -- | O(1) | One-counter median estimator |

**Recommended:** the **sliding-window histogram**. It is the smallest delta
from today's algorithm — keep the 512-bucket array, but on every tick:
(a) bump the new frame's bucket, (b) unbump the bucket of the frame being
evicted from the ring, (c) maintain a running median position via a
"current-bucket" pointer that walks forward/backward depending on which
side the bump/unbump landed.

This converts the 7200-bumps-per-tick four-pass workload into 4
bumps + 4 unbumps + 4 small bucket-walks per tick. Median is read in O(1)
from the current-bucket pointer.

**Expected delta:** **20-25 µs/tick saved** (~10 % of the profiler's total
budget).

**Risk:** Bucket-walk logic is the part that can drift on edge cases (the
"50 % point" can be in the middle of a multi-element bucket; ties have to
be broken consistently). Pin with xUnit: feed both algorithms a stream and
assert their median outputs agree within bucket precision (0.25 ms) for
every tick of a synthesised 1800-frame ramp + spike + dip sequence.

**Evidence:** Walking-median algorithms are well-cited (e.g. <https://en.wikipedia.org/wiki/Streaming_algorithm>).
For literature: see Roberts (2000) "An online algorithm for computing the
running median of a continuous stream".

### 4.7 [F] SIMD-vectorise `UpdateRollingAverage`

**What:** Rewrite the inner loop using `Vector256<double>` (AVX2) with a
scalar tail loop. Code sketch in §3.8.

**Why:** 5 calls per tick × ~200 cells per call = 1000 scalar ops/tick.
Vectorisation gives 250 vector ops/tick. The op is straightforward fused
add/sub/divide.

**Expected delta:** **~10-15 µs/tick**.

**Risk:** Vector intrinsics need a CPU feature check (`Avx2.IsSupported`)
plus a scalar fallback. The fallback is the current code. Apple Silicon
NEON has `Vector128<double>` (2 lanes) — half the win on Mac, full win on
Windows AVX2.

**Evidence:** Disassemble the optimised method with `DOTNET_JitDisasm`
and confirm no boxing, no allocations, scalar tail handles the remainder.

### 4.8 [E] Use `MemoryMarshal.GetArrayDataReference` + `Unsafe.Add` in inner loops

**What:** Replace `for (int i = 0; i < arr.Length; i++) arr[i]...` with:

```csharp
ref double r = ref MemoryMarshal.GetArrayDataReference(arr);
for (int i = 0; i < arr.Length; i++) Unsafe.Add(ref r, i) = ...;
```

In: `UpdateRollingAverage`, `SumAll`, `HarvestInto`, `HarvestHooksInto`,
`HarvestAllocationsInto`, `HarvestHookAllocationsInto`,
`PerTickAttributionRing.Push`, `Baseline.BumpBucket` (the `_histogramScratch`
access).

**Why:** Removes the per-access bounds check the JIT inserts when it
cannot prove `i < arr.Length` (it usually can for `i < arr.Length` direct,
but not when crossing two arrays of different sizes — see Update
RollingAverage which reads `source.Length`, writes to `history[offset+i]`,
`rolling[i]`, `average[i]`).

**Expected delta:** **~5-10 µs/tick** cumulatively across all loops.

**Risk:** `Unsafe.Add` skips bounds checks. Any off-by-one becomes UB. Pin
with xUnit fuzz tests that feed boundary indices through the new code.

### 4.9 [E] Mark all `ProbeStack.Enter/Leave/EnterCpuAlloc/LeaveCpuAlloc`
**and** the `PerModAttribution.Add` overloads with `[MethodImpl(MethodImplOptions
.AggressiveInlining)]`

**What:** Add the attribute on `Enter`, `Leave`, `EnterCpuAlloc`,
`LeaveCpuAlloc`, the 5-arg `Add`, and the 6-arg `Add` (drop the 3-arg per
§4.1).

**Why:** The IL emitter calls these directly. The C# inliner can't help.
But these methods themselves call helpers (`Stopwatch.GetTimestamp` is
already inlined; the inlining hint matters more for splitting hot/cold
paths — e.g. the array-grow branch in `Enter` becoming a cold helper).

The actual structural change: split `Enter` into `Enter_Fast` (always-
inlined, handles the common case where `_stack != null && _depth <
_stack.Length`) and `Enter_Slow` (NoInlining, handles allocation + growth).
Same for `EnterCpuAlloc`.

**Expected delta:** **~3-5 ns/event × 2000 events/tick = 6-10 µs/tick**.

**Risk:** AggressiveInlining can bloat call sites and hurt I-cache if
overused. The functions are tiny (~20-30 native instructions) and called
from few sites (one IL prologue per hook), so I-cache pressure is
negligible.

### 4.10 [E] Replace try/catch in `ProfilerFocusProbe.Read` with an init flag

**What:**

```csharp
internal static class ProfilerFocusProbe
{
    private static bool _available;

    public static void Init()
    {
        try { _ = Terraria.Main.hasFocus; _available = true; }
        catch { _available = false; }
    }

    public static bool Read() => _available ? Terraria.Main.hasFocus : true;
}
```

`Init` runs once in `Mod.Load` / `OnWorldLoad`; the per-tick read is one
field load + one compare + one field load.

**Why:** Try/catch on a hot path is wrong by construction. Even with no
exception thrown, the JIT cannot fully inline the body — the EH region
adds a method-table entry the runtime must track.

**Expected delta:** **~10-30 ns/tick** (called once at BeginTick). Tiny in
absolute, but a clean win.

**Risk:** None — same observable behaviour.

### 4.11 [C] Pre-allocate `Baseline._histogramScratch` outside the heap-walked region

**What:** Already pre-allocated. No-op for this pass. Mentioned for
completeness.

### 4.12 [C] Fix `PerModAttribution.RegisterHook` install-time allocation

**What:** Replace per-hook `Array.Resize` with a two-phase install:
register-into-list (cold), then one-shot `ToArray` at install end.

**Why:** Currently O(n²) in registrations. At 10 000 hooks: 50 M
element-copies = ~50 MB of churn during install. Contributes to the 233 MB
install-delta in the v0.5 baseline.

**Expected delta:** Saves install-time RAM (not per-tick). Estimated
**~50-150 MB of install-time GC pressure**.

**Risk:** None — the registration order is preserved.

### 4.13 [I] Move smoothing / rolling / harvest off the game thread

**What:** Today, `EndTick` does the harvest + smoothing + rolling-average
inline. The UI reads `_perModSmoothedMs` directly. Move all of the
"smoothed display values" work into a dedicated background thread that
publishes a frame-stale view at ~10 Hz.

The publish handoff is a `Volatile.Write` of an immutable snapshot struct
(or a triple-buffer of doubles[]). The UI reads the latest published
view; smoothing falls behind real-time by ~100 ms which is invisible to
the eye.

**Why:** The 50-70 µs/tick spent on smoothing/rolling moves off the game
thread entirely. The UI gets the same data 100 ms later — acceptable trade.

**Expected delta:** **~50-70 µs/tick removed from the game thread**. This
is the single largest lever in the pass.

**Risk:** Threading model becomes more complex. Race-on-read is the
classic torn-double pitfall — fix with triple-buffer or
`Volatile.Read<ImmutableSnapshot>` of a class reference (the snapshot
object is rebuilt on the background thread once per cycle, ~10 Hz; the
allocation rate is 10 obj/sec × ~16 KB = 160 KB/sec, well within budget).

**Evidence:** Pattern is used by JIT statistics counters in CoreCLR and by
many game engines (the "double buffer of stats" pattern). Pin behaviour
with a test that asserts the published snapshot's contents converge to the
inline value within ~200 ms after a steady stream of writes.

### 4.14 [B] Hoist `Stopwatch.Frequency` reciprocal out of every harvest

**What:** Cache `(double)(1000.0 / Stopwatch.Frequency)` once at world-load
in `PerModAttribution._ticksToMs`. Replace `1000d / Stopwatch.Frequency`
recomputations at lines 306, 336, etc.

**Why:** Today each harvest call computes the constant. Four harvest calls
per tick (CPU mod, CPU hook, alloc mod, alloc hook) × Stopwatch.Frequency
divide on every cell? Re-reading the source: the constant is computed ONCE
per call, outside the loop (line 306 then used at line 310 inside the for).
So it's 4 reciprocals/tick = ~1 µs/tick. Marginal but free.

**Expected delta:** **~1 µs/tick.** A free win included for completeness.

**Risk:** None.

### 4.15 [B] Fuse `SumAll` into the smoothing loop

**What:** `EndTick` calls `SumAll(_perModRawMs)` at line 422 and (if
Parallel) `SumAll(_perModRawMsBackend1)` at line 428. Each does a full
sweep of the raw array that was just iterated for smoothing. Move the
accumulation into the smoothing loop.

**Why:** Saves one pass over a 200-cell array.

**Expected delta:** **~1-2 µs/tick.**

**Risk:** None.

### 4.16 [D] Struct-of-arrays for `TickFrame` fields used together

**What:** Stop storing `TickFrame` as a struct in the ring; store eight
parallel arrays in a `TickFrameArrays` container:
`double[] frameTimeMs`, `double[] gcTimeMs`, `long[] tickIndex`,
`long[] timestampMonotonic`, `int[] npcCount`, `int[] projectileCount`,
`int[] dustCount`, `EventContext[] context`.

The current `RingBuffer<TickFrame>` becomes a `TickFrameRing` that exposes
slice views.

**Why:** Baseline.cs reads only `FrameTimeMs` (4 of its 4 passes) and
`TimestampUnixMs` (2 of its 4 passes). Today, each indexer call loads the
entire 64-byte TickFrame struct into a register pair, just to read 8 bytes.
With SoA, the read is a single cache-friendly stride.

The four histogram passes in Baseline become two passes (one over
frameTimeMs, one over timestampMonotonic), each touching `1800 × 8 B =
14.4 KB` — fits in L1 (32 KB typical).

**Expected delta:** **~10-15 µs/tick** when combined with the incremental
median (§4.6, which reduces pass count but each pass becomes much
faster).

**Risk:** This is a significant API change. `MetricCollector.History`
currently returns `RingBuffer<TickFrame>`; every consumer of `.Newest` or
`history[i]` needs to migrate. Two interface shapes:

1. Keep `TickFrame` as a value type, build it on-demand from the SoA
   container's slot N (cheap if the consumer wants the whole struct, only
   pay for the fields you read otherwise).
2. Change consumers to take a `TickFrameView` (ref struct) into the SoA.

Option 1 preserves the API. Pin the existing tests; new tests for the SoA
layout.

### 4.17 [B] Skip the alloc-tracking harvest when `_tracksAllocations == false`

**What:** Already done (line 403 `if (_tracksAllocations)`). No-op,
mentioned for completeness.

### 4.18 [G] Replace `RingBuffer<T>` per-tick indexer use in Baseline with a direct array sweep

**What:** Baseline's four histogram passes call `history[i].FrameTimeMs`
1800 times per pass. The indexer does subtract-and-correct math + a bounds
check + returns a struct copy. Refactor Baseline to take a `ReadOnlySpan<TickFrame>`
of the underlying array (or the SoA equivalent) and iterate raw.

**Why:** 4 × 1800 = 7200 indexer eliminations per tick. Each saves ~3 ns
of bounds-check + subtract-and-correct. **~20 µs/tick** if naively
applied; combined with §4.16 (SoA) the win compounds.

**Expected delta:** **~5-20 µs/tick** depending on combination with §4.16.

**Risk:** Exposes the wrap-around to Baseline. Wrap is in
`RingBufferTests.cs`; new tests pin Baseline against the wrap edge.

### 4.19 [D] Co-locate `_perModRawMs`, `_perModSmoothedMs`, etc. via one large `double[]` arena

**What:** Today, 5 separate `new double[cells]` arrays for perMod ms, 5 for
perHook, ×2 for the bytes mirror. 20 separate heap allocations of ~1 KB
each. The smoothing loop touches all 5 perMod-ms arrays in sequence per
cell — that's 5 different cache lines per cell.

Replace with one large `double[arenaSize]` allocated once, sliced into views:

```
_perModRawMs       = arena.Slice(0,   cells);
_perModSmoothedMs  = arena.Slice(cells, cells);
_perModAverageMs   = arena.Slice(2*cells, cells);
_perModRollingMs   = arena.Slice(3*cells, cells);
_perModHistoryMs   = arena.Slice(4*cells, cells * historyCapacity);
```

**Why:** Two compounding wins:

1. One allocation, one cache-friendly region — the OS pages in adjacent
   memory.
2. The smoothing loop reads `raw[i] - smoothed[i]` then writes `smoothed[i]`
   — if raw, smoothed, average, rolling are in one cache line per cell
   (which they would be if laid out as a struct-of-fields per cell instead
   of array-of-structs across cells), the loop touches one cache line per
   cell instead of four.

Realistic shape: a `PerCellSlot` struct with `Raw Smoothed Average Rolling`
fields, kept as `PerCellSlot[cells]`. The `History` array stays
separate (it is touched once per cell write, not interleaved).

**Expected delta:** **~5-10 µs/tick** from cache hit-rate improvements.

**Risk:** API churn (`PerModCategoryMs => _perModSmoothedMs` becomes
`new SlotView(_slots, sel: 1)` etc.) — a Slot[]-with-view shim avoids
breaking consumers.

### 4.20 [C] Reuse the writer-thread payload allocations (the 441 ns regression fix — see §5)

Detailed in §5.

---

## 5. The 441 ns/op enqueue regression — root-cause hypothesis with evidence

### 5.1 What the benchmark measures

`Tests/Persistence/PersistenceBenchmarkTests.cs::Enqueue_GameThread_Latency`
(line 57):

```csharp
for (int i = 0; i < N; i++)
{
    db.Writer.Enqueue(DbWriteOp.WarmAggregate(NewWarmRow(sid, 1000 + i)));
}
```

The op measured is: `NewWarmRow(...) → new TickAggregateWarm { ... } →
DbWriteOp.WarmAggregate(row) → DbWriteOp ctor → channel.TryWrite(in op) →
Interlocked.Increment`.

`NewWarmRow` (Tests/Persistence/PersistenceBenchmarkTests.cs:239):

```csharp
private static TickAggregateWarm NewWarmRow(ObjectId sid, long secondIndex)
    => new TickAggregateWarm { /* ~10 fields */ };
```

`TickAggregateWarm` is **a sealed class** (Profiling/Persistence/Records/Tick
AggregateWarm.cs:10). Every NewWarmRow call allocates a heap object.

`DbWriteOp` is a readonly struct (54 bytes) but its `Payload` field is of
type `object`, and its `EndReason` field is of type `string` (defaulting
to `""` — that is a constant reference, no allocation). The struct holds
the boxed reference to the WarmAggregate; the WarmAggregate itself is the
heap allocation.

### 5.2 Cost decomposition (v0.5)

Single Enqueue() call at 441 ns/op consists of:

| Component | Cost (ns) | Source |
|---|---|---|
| `new TickAggregateWarm { ... }` (the row) | ~250-350 | Object header (16 B) + 64+ B of fields, .NET 8 SOH bump-pointer allocator |
| `DbWriteOp` struct ctor + struct copy into channel | ~10-20 | Stack-resident; one struct copy on TryWrite |
| `Channel.Writer.TryWrite` segment store + CAS | ~25-60 | Lock-free segment append |
| `Interlocked.Increment` (queue depth) | ~5-10 | Single LOCK XADD |
| `Volatile.Read` for soft-cap branch | ~1-3 | One load + branch |
| `_cts.IsCancellationRequested` read | ~3-5 | CTS state read |
| **Total** | **~300-450 ns** | |

The dominant cost is the **WarmAggregate object allocation**.

### 5.3 Why it regressed since v0.3 (276 ns → 441 ns)

Between v0.3 and v0.5, six new event streams were added (per baseline.md
§1): `damageTakenEvents`, `damageDealtEvents`, `npcSpawnEvents`,
`itemCreatedEvents`, `loadoutSnapshots`, `buffEvents`. Each comes with its
own record class.

The `TickAggregateWarm` record also gained fields — more bytes per
allocation, more time to zero-init.

Confirm by:

1. `git log --stat --since="v0.3" -- Profiling/Persistence/Records/` —
   should show row classes growing.
2. Disassemble `Enqueue` and `NewWarmRow` after v0.5: confirm the
   allocator-call IL (`newobj`) is the dominant cost.

### 5.4 Fix: object pooling for warm/cold/aggregate rows

**What:** Replace `new TickAggregateWarm { ... }` with a `RowPool<TickAggregateWarm>`
that the writer thread returns the row to after it has flushed. The game
thread rents a row, fills it, posts the op. The writer thread reads the
fields, then returns the row to the pool.

For struct-shaped rows: convert to `readonly struct` and embed directly in
`DbWriteOp` instead of via `object Payload`. This is the cleaner fix
where the record's field count is small (<= 8 fields). For larger
records, pool.

**Why:** Removes the per-op heap allocation. Channels and Interlocked are
cheap; the heap allocator is the slow part.

**Expected delta:** **441 → ~150-200 ns/op**. Drops the regression and
some. The target in baseline.md is < 200 ns; achievable.

**Risk:** Pool lifetime. If the writer thread is slow draining and the
queue grows, the pool empties — caller must allocate a new row on miss
(fallback path is fine because it preserves correctness; the pool is a
fast path). Tests:

- `RowPool_returns_same_instance_after_release`
- `RowPool_fallback_allocates_when_empty`
- `Enqueue_with_pool_under_load_does_not_leak`

A second risk: payload fields that hold strings/lists (e.g.
`PerSessionModAggregateBatch` carries a `List<PerSessionModAggregate>`).
The list cannot be returned to a pool naïvely — its capacity may have
grown unboundedly. Mitigate with `list.Clear()` + `Capacity` cap on
return.

### 5.5 Secondary fix: avoid the `object Payload` boxing of value-type payloads

**What:** Several DbWriteOp shapes carry value-type-only data
(`SessionEnd` uses `new object()` as a stub payload — line 76 of
DbWriteOp.cs). Convert DbWriteOp's polymorphism to a discriminated union
of struct payloads:

```csharp
public readonly struct DbWriteOp
{
    public readonly DbOpKind Kind;
    public readonly ObjectId SessionId;
    public readonly SessionEndPayload SessionEnd;
    public readonly SpikePayload Spike;
    public readonly long PrimaryLong;
    public readonly object? RefPayload;    // only set for ref-typed payloads
    ...
}
```

This is one of the standard .NET "tagged union" patterns. The struct
grows somewhat (~8-16 B per uninhabited variant), but the heap
allocations go to zero for value-type payloads.

**Expected delta:** Compounds with §5.4. For `SessionEnd` specifically,
the regression is ~30-50 ns from the `new object()` stub.

**Risk:** The struct sized larger means each Enqueue copies more bytes
into the channel segment. Trade-off: ~30 ns/op of copy vs the entire
heap-allocation cost. Worth it for the high-frequency rows
(WarmAggregate, Spike, Stall).

### 5.6 Cross-check against the baseline.md target

Target: < 200 ns/op. Today: 441 ns/op.

| Lever | Saving |
|---|---|
| §5.4 pool TickAggregateWarm | ~250 ns |
| §5.5 unbox SessionEnd / value payloads | ~30 ns |
| §4.14 (no direct relevance — collector path only) | -- |
| Lift the `Volatile.Read` cancellation check (set a faster fast-path) | ~3 ns |

Conservative landing: **~160-190 ns/op**. Meets target.

---

## 6. Cross-system dependencies

### 6.1 Hook-instrumentation feeds Enter/Leave

The IL emitter (`ILHookInterceptor.cs`) writes the prologue and finally
that call `ProbeStack.Enter(hookId)` / `ProbeStack.Leave()` (or the
alloc-tracking variants). The per-call cost of those targets is a
metric-collection concern; the IL emission strategy (how the prologue is
laid out, what bounds are inlined, whether the Leave is a single call or
a sequence) is a hook-instrumentation concern. Boundary:

- This dossier owns: `ProbeStack.Enter/Leave` body, `PerModAttribution.Add`,
  the descriptor lookup, the accumulator clear.
- Hook-instrumentation owns: which methods get wrapped, which fields
  get emitted as Ldc_I4 constants, whether to widen the prologue (e.g.
  emit `Stopwatch.GetTimestamp()` inline into the body and pass it as an
  argument to a slimmer `Enter`).

Synchronisation point: if §4.9 splits Enter into Enter_Fast / Enter_Slow,
the IL emitter must not bind to the split — it always emits `call
ProbeStack.Enter`. The split is internal to the metric-collection module.

### 6.2 Persistence is the downstream consumer

The writer thread (`DbWriterThread`) consumes ops the game thread posts.
Per §5, the persistence dossier should be aware that we are moving toward
pooled rows and struct payloads — the writer thread needs to be
defensive about Payload lifetimes (read fields before returning to pool;
do not retain references past the batch drain).

The journal (`EventJournal`) serialises the payloads. Pool-returned rows
must not be mutated by the game thread until the writer has finished
journaling — the obvious shape is: pool returns happen only after
`_journal.AppendBatch(batch)` returns.

### 6.3 Spike and Stall detectors are siblings

Both consume `MetricCollector.History` and `PerTickAttributionRing`.
Both run inside `EndTick` (the spike detector at line 450, the stall
detector at line 326 of BeginTick). The spike detector reads the latest
ring snapshot for attribution; the stall detector reads the smoothed
per-mod scalar values.

§4.13 (move smoothing off-thread) interacts with the stall detector:
the stall detector reads `_perModSmoothedMs` which would now be a
background-thread output. The fix: the stall detector either (a) reads
the most-recent published snapshot (stale by ~100 ms; fine for stall
attribution, which is about "what was the cost trend when the gap began"
— a ~100 ms delay is well below the typical 500+ ms stall threshold),
or (b) uses the raw per-tick values from `PerTickAttributionRing` directly,
which are already published synchronously and unsmoothed.

### 6.4 Baseline.cs is shared

The baseline is read by SpikeDetector, StallDetector, and the insights
engine. §4.6 (incremental median) must preserve the exact public surface
(`FrameMsMedian`, `FrameMsMad`, `TickPeriodMsMedian`, `TickPeriodMsMad`,
`AllocBytesPerTickMedian`, `IsCalibrated`). The replacement algorithm
publishes the same scalars; consumers don't see the change.

### 6.5 Self-health refresh cadence

`_selfHealth.Refresh(frame.TickIndex)` (line 455) is cadence-gated at ~1 Hz
inside the call. No collector-side change needed.

---

## 7. Prioritised execution order

Ordered by (expected delta / risk), with dependencies noted. The pass
should land them in this sequence so each later change builds on a
verified previous one.

| # | Recommendation | Expected µs/tick saved | Risk | Depends on | Verification |
|---|---|---|---|---|---|
| **1** | §5.4 — pool TickAggregateWarm + Spike + Stall row allocations | n/a tick — fixes enqueue 441 → ~200 ns | Low | -- | `PersistenceBenchmarkTests.Enqueue_GameThread_Latency` |
| **2** | §4.10 — replace try/catch in ProfilerFocusProbe | ~0.5 µs | None | -- | unit test of init + read paths |
| **3** | §4.14 — cache `ticksToMs` reciprocal | ~1 µs | None | -- | numerical equivalence test |
| **4** | §4.15 — fuse SumAll into smoothing loop | ~2 µs | None | -- | numerical equivalence test |
| **5** | §4.1 — drop 3-arg `PerModAttribution.Add` overload | ~10-20 µs | None | -- | xUnit attribution test |
| **6** | §4.2 — flat HookDescriptor[] array | ~5-10 µs | None | 5 | xUnit attribution test |
| **7** | §4.9 — AggressiveInlining + Enter/Leave fast/slow split | ~6-10 µs | Low | 5, 6 | JIT-disasm review |
| **8** | §4.3 — replace UtcNow with monotonic anchor | ~0.5 µs (collector) | Medium (touches baseline period math) | -- | baseline-period equivalence test |
| **9** | §4.4 — power-of-two history capacity | ~3-5 µs | Low (capacity ceiling change) | -- | RingBufferTests update |
| **10** | §4.12 — fix RegisterHook install-time O(n²) | install RAM only | None | -- | install-RAM benchmark |
| **11** | §4.8 — MemoryMarshal.GetArrayDataReference in inner loops | ~5-10 µs | Low | -- | fuzz tests for boundary indices |
| **12** | §4.7 — SIMD vectorise UpdateRollingAverage | ~10-15 µs | Medium (CPU feature path) | 11 | scalar-vector equivalence test |
| **13** | §4.6 — incremental sliding-window histogram baseline | ~20-25 µs | Medium-High (algorithm correctness) | 9 | side-by-side median agreement test over 10⁵ ticks |
| **14** | §4.5 — drop `PerModSample[]?` from TickFrame | indirect (GC) | Low | -- | TickFrame size assertion |
| **15** | §4.18 — Baseline reads ReadOnlySpan over the ring's backing array | ~5-20 µs | Medium | 13 | baseline-equivalence test |
| **16** | §4.19 — arena-allocate the per-mod double arrays | ~5-10 µs | Medium (API churn) | 11, 12 | numerical equivalence test |
| **17** | §4.16 — struct-of-arrays TickFrame | ~10-15 µs | High (API surface change) | 15 | full ring-buffer + baseline + spike-detector tests |
| **18** | §4.13 — move smoothing/rolling off the game thread | ~50-70 µs | High (threading model) | 1, 5, 7, 16 | convergence test + UI staleness budget test |

Total expected saving on the per-tick path (excluding the enqueue
regression fix which is its own win): **~135-220 µs/tick**. Starting from
~270 µs, that lands between 50-135 µs/tick. The target is < 100 µs; the
top of the range achieves it, the bottom is the worst case.

Verification gate after each: re-run the in-game playtest baseline (the
real session in `baseline.md` §2). The "avg ms/tick" row for
`PerformanceProfiler` is the headline number.

---

## 8. References

### .NET API costs and implementation

- Akinshin A., "Stopwatch under the hood", 2018-2024.
  <https://aakinshin.net/posts/stopwatch/> — canonical cost numbers for
  Stopwatch.GetTimestamp across Windows / Linux / Mono.
- Microsoft Learn, "Stopwatch.GetTimestamp Method".
  <https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.stopwatch.gettimestamp?view=net-8.0>
- Microsoft Learn, "GC.GetAllocatedBytesForCurrentThread Method".
  <https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-8.0>
- Microsoft Learn, "GC.GetTotalPauseDuration Method" (NET 7+).
  <https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalpauseduration?view=net-8.0>
- Sitnik A., "The new MemoryDiagnoser is now better than ever!".
  <https://adamsitnik.com/the-new-Memory-Diagnoser/> — implementation
  details of the BDN allocation tracker.
- dotnet/runtime issue #6956 — "JIT intrinsic for acquiring timestamps".
  <https://github.com/dotnet/runtime/issues/6956>
- ".NET 8 Performance Improvements", devblogs.microsoft.com.
  <https://devblogs.microsoft.com/dotnet/dotnet-8-performance-improvements-in-dotnet-8/>

### Concurrent queues

- "An Introduction to System.Threading.Channels", Microsoft Devblogs.
  <https://devblogs.microsoft.com/dotnet/an-introduction-to-system-threading-channels/>
- "Channels vs ConcurrentQueue benchmarks", codegenes.net.
  <https://www.codegenes.net/blog/when-should-system-threading-channels-be-preferred-to-concurrentqueue/>
- dotnet/runtime channels source.
  <https://github.com/dotnet/runtime/tree/main/src/libraries/System.Threading.Channels/src>

### Streaming statistics

- Jain R., Chlamtac I., "The P² Algorithm for Dynamic Calculation of
  Quantiles and Histograms Without Storing Observations", CACM 1985.
- Ma Q., Muthukrishnan S., Sandler M., "Frugal Streaming for Estimating
  Quantiles", 2014.
- Wikipedia: "Streaming algorithm — Online median".
  <https://en.wikipedia.org/wiki/Streaming_algorithm>

### .NET SIMD and span

- Microsoft Learn, "System.Runtime.Intrinsics".
  <https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics>
- Microsoft Learn, "MemoryMarshal.GetArrayDataReference".
  <https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.memorymarshal.getarraydatareference>
- "Vectorisation with Vector256/Vector128", devblogs.microsoft.com (.NET 7-8
  hardware intrinsics posts).

### tModLoader / Terraria

- tModLoader source (gh api repos/tModLoader/tModLoader/...).
  `PostUpdateEverything`, `Main.update`, hook-dispatch surfaces. Not
  changed by this pass; cited as the boundary of where the metric collector
  attaches.

### Internal project artefacts

- `context/perf-pass/baseline.md` — the contract this pass measures against.
- `context/notes/philosophy.md` — capture-preservation invariant.
- `context/systems/metric-collection.md` — current-state subsystem doc.
- `context/notes/conventions.md` — pre-allocation discipline.
- `Tests/Persistence/PersistenceBenchmarkTests.cs::Enqueue_GameThread_Latency`
  — the 441 ns/op measurement.
- `Tests/RingBufferTests.cs` — wrap-around correctness pin (any change
  to §4.4 / §4.16 / §4.18 must keep this green).

---

*End of dossier. Output target: implementation lands in `context/perf-pass/plans/metric-collection.md` once master-plan synthesises across the per-system research.*
