# Spike Detection — Optimisation Research & Plan

> Scope: `Profiling/SpikeDetector.cs`, `Profiling/PerTickAttributionRing.cs`, `Profiling/RingBuffer.cs`, `Profiling/Persistence/Streams/SpikeStream.cs`, `Profiling/Persistence/Records/SpikeWindowRow.cs`, plus every reader/writer that touches a `SpikeWindow` or a `SpikeWindowRow`.
>
> Baseline of measurement: `context/perf-pass/baseline.md` (v0.5, captured 2026-05-20). The 4.5-min real playtest fired **50 spikes (11/min)** with the spike detector running on the game-thread `EndTick` path; every spike triggered a `CopyLatestCategorySnapshot` over an `18 mod × 7 cat = 126`-cell span and a freshly heap-allocated `float[126]` (plus a parallel byte array when allocation tracking is on).
>
> Cohort: a v0.6 optimisation pass that does what we already do at maximum efficiency. No scope cuts, no capture losses, no "skip small spikes" sampling. Every detected spike still writes a window. The objective is to lower the per-tick spike-detector overhead **and** the per-spike snapshot overhead while preserving the data stack.
>
> The five Project Invariants are the constraint floor; this dossier never proposes a change that crosses them.

---

## Table of contents

1. Current state audit (data flow, allocations, hot-path inventory)
2. Baseline numbers (what we are measuring against)
3. Algorithm research — robust-statistics alternatives, streaming quantiles, sorting tiny arrays
4. Optimisation opportunities — concrete proposals against `SpikeDetector` + ring + persistence
5. Cross-system dependencies (`MetricCollector` source, persistence sink, insights/UI readers)
6. Prioritised order — what lands first, what is sequenced, what is gated
7. References

---

## 1. Current state audit

### 1.1 Component map (today, post 0.5)

```
                      ┌───────────────────────────────┐
                      │   MetricCollector.EndTick     │
                      │   ─ tick frame committed      │
                      │   ─ baseline.Recompute()      │
                      │   ─ perTickRing.Push(…)       │
                      │   ─ spikeDetector.OnTick(…)   │
                      └──────────────┬────────────────┘
                                     │ frame, baseline, ring
                                     ▼
                     ┌────────────────────────────────┐
                     │ SpikeDetector.OnTick           │
                     │   median + MAD via Baseline    │
                     │   threshold = frame ≥ k·median │
                     │   open / extend / close window │
                     │   CaptureSnapshot(ref window,  │
                     │       PerTickAttributionRing)  │
                     └────────────┬───────────────────┘
                                  │ Push when window closes
                                  ▼
                     ┌────────────────────────────────┐
                     │ RingBuffer<SpikeWindow> (50)   │
                     └──┬────────────┬────────────────┘
                        │            │
                        ▼            ▼
            ┌──────────────────┐   ┌──────────────────────────┐
            │ SpikesTab UI     │   │ SessionRecorder.DrainSpikes
            │ (overlay reads)  │   │  → SpikeWindowRow         │
            └──────────────────┘   │  → SpikeStream.Apply      │
                                   │  → LiteDB collection      │
                                   └──────────────────────────┘
```

The detector's input is the *baseline*: a median + MAD computed by `Baseline.Recompute(history, …)` over the 30-s tick-frame ring at every `EndTick`. The detector itself does **not** sort or compute the median; it reads a pre-computed scalar. That is the most important fact in this whole document. The per-tick spike-detection cost in isolation is small — six comparisons, two struct writes, an in-place `Push` if the window closes. The expensive work is the *snapshot copy* at window open (and on every worst-tick re-capture) and the *baseline recompute* every tick over the rolling history.

### 1.2 Allocations on the per-tick spike path (verified)

| Site | File | Allocation? | Frequency | Notes |
|---|---|---|---|---|
| `OnTick` entry | `SpikeDetector.cs:148` | none | every tick | reads value-type frame and baseline scalars |
| `_openWindow = new SpikeWindow { … PerModCatMs = new float[…], PerModCatBytes = … }` | `SpikeDetector.cs:182-196` | **yes** — two `float[modCount * catCount]` allocations | once per window open | 18 mods × 7 cat × 4 B = **504 B** per window, doubled to **1 008 B** when alloc tracking is on. ~11/min × 1 008 B ≈ 11 KB/min of Gen0 churn |
| `CaptureSnapshot` | `SpikeDetector.cs:239-252` | none (writes into pre-allocated spans) | per worst-tick re-capture | `CopyLatestCategorySnapshot` is a managed `for`-loop, not `Buffer.BlockCopy` / `Span.CopyTo` |
| `_windows.Push(in _openWindow)` | `RingBuffer.cs:62` | none | once per window close | a single struct store; the `in` parameter prevents the multi-field copy at the call site |
| `Windows` getter | `SpikeDetector.cs:137` | none (cached `_windowsView`) | every overlay/log read | pre-cached at construction since commit `77a99d2` |
| `SpikeWindowsView.GetEnumerator()` | `SpikeDetector.cs:265` | **yes** — `yield return` compiler-generated enumerator | per session-log drain & overlay refresh | only matters if anything actually calls `IEnumerator<SpikeWindow>`; in current code paths both readers use the indexer, so this allocation is latent rather than active |
| `SessionRecorder.DrainSpikes` → `ToList(w.PerModCatMs)` | `SessionRecorder.cs:243, 615-620` | **yes** — `new List<double>(arr.Length)` + N `Add` calls, twice if alloc tracking is on | per spike at session-drain time | 126 floats → `List<double>` widens each cell from 4 B float to 8 B double. Off the game thread (writer thread), but still wasted bandwidth and GC pressure |
| `BuildSpikeTopContributors` | `SessionRecorder.cs:582-613` | **yes** — `List<SpikeContributor>(0)`, N `SpikeContributor` class instances, a `List.Sort(Comparison<T>)` delegate | per spike at session-drain time | off-thread; small but per-spike |
| `SpikeWindowRow` construction | `SessionRecorder.cs:232-246` | **yes** — class instance, two `List<double>` instances | per spike at session-drain time | crosses serialisation boundary; goes into LiteDB |

So the per-tick spike path itself is *almost* zero-allocation today — the only per-tick allocation site is window-open, ~11 times per minute. The session-drain path is the bigger waste because it widens `float` → `double` via a `List<double>` whose only purpose is to be re-serialised into BSON. That is paid 50 times per session (the spike cap), but at the moment of the session-end JSON write, which is also where the end-of-session UiOverlayBlocking 8.5 s stall comes from (baseline §2). Spike drain participates in that stall.

### 1.3 Hot-path inventory in `OnTick`

```csharp
public void OnTick(TickFrame frame, Baseline baseline, PerTickAttributionRing perTickRing)
{
    _ticksSeen++;                                         // 1 add

    if (!baseline.IsCalibrated) { HandleSubThreshold(); return; }   // branch on cold-start flag

    double frameMs = frame.FrameTimeMs;                   // 1 load
    double median  = baseline.FrameMsMedian;              // 1 load (property → field)
    double mad     = baseline.FrameMsMad;                 // 1 load (property → field)

    bool spike = frameMs >= median * ThresholdMultiplier; // 1 mul, 1 cmp

    if (!spike) { HandleSubThreshold(); return; }

    _consecutiveSubThreshold = 0;                         // 1 store
    if (!_windowOpen)
    {
        _openWindow = new SpikeWindow { … };              // ★ allocations + struct init
        CaptureSnapshot(ref _openWindow, perTickRing);    // ★ N×float copy
        _windowOpen = true;
    }
    else
    {
        _openWindow.EndTick = frame.TickIndex;
        if (frameMs > _openWindow.WorstFrameMs)
        {
            _openWindow.WorstFrameMs = frameMs;
            _openWindow.WorstTick    = frame.TickIndex;
            _openWindow.SnapshotTick = frame.TickIndex;
            CaptureSnapshot(ref _openWindow, perTickRing); // ★ N×float copy
        }
    }
}
```

On a sub-threshold tick the cost is **3 field loads + 1 mul + 2 branches**. That is sub-nanosecond on an M-series core. The cost only escalates on the spike ticks (~50 per 4.5 min, so ~0.018 % of ticks).

The `Baseline.Recompute` call is per-tick and is where the real per-tick cost sits. See §3.5 and §4.5.

### 1.4 `Baseline.Recompute` cost (per tick)

`Baseline.Recompute` is called from `MetricCollector.EndTick` *every* tick (not just spike ticks). It does, per tick:

- `FrameMedian(history)` → `ClearHistogram()` (1024-slot `int[]` clear) + 1800-iteration bucket pass + bucket scan
- `FrameMad(history, median)` → another `ClearHistogram()` + 1800-iteration bucket pass + bucket scan
- `TickPeriodMedian(history)` → another `ClearHistogram()` + 1800-iteration pairwise pass + bucket scan
- `TickPeriodMad(history, median)` → another `ClearHistogram()` + 1800-iteration pairwise pass + bucket scan

That is **four 1024-element `Array.Clear` calls and four 1800-element passes per tick**, every tick. At 60 tps that is 432 000 bucket-bumps per second just for the baseline the spike detector depends on. The hot-path budget on M-series silicon swallows it, but it is the single biggest per-tick win in the spike subsystem — see §4.5.

### 1.5 Ring-buffer indexing review (`RingBuffer<T>` and `PerTickAttributionRing`)

`RingBuffer<T>` indexer math (used by `_windows[index]`):

```csharp
int physical = _head - _count + index;
if (physical < 0) physical += _items.Length;
return _items[physical];
```

No modulo. Single conditional branch, well predicted because for an under-filled ring `physical` is always positive; once full, `physical < 0` happens roughly `_count/Capacity` of the time. Cache-friendly: the underlying `T[]` is contiguous, and at 50 × `SpikeWindow` (one ~96 B struct + two `float[]`) the spine fits in a single page. No work to do here.

`PerTickAttributionRing` slot math is modulo-based:

```csharp
int tickSlot    = (int)(_writeCount % _historyTicks);            // Push
int catTickSlot = (int)(_writeCount % _categorySnapshotTicks);   // Push
long latestSlot = (_writeCount - 1) % _historyTicks;             // GetPerModMs
long slot       = (latestSlot - ago + _historyTicks) % _historyTicks;
```

`_historyTicks` is **1800** at construction time (see `MetricCollector` ring sizing) and `_categorySnapshotTicks` is **120**. Both are *runtime-configurable constants*, so the JIT cannot fold the modulo to an AND. The CPU's integer divide is the expensive primitive here — on modern x86 it is 15–30 cycles, on ARM64 (Apple silicon) more like 10–25 cycles latency. Per tick we pay two divides on `Push`; on `GetPerModMs` we pay three more. The fix is to round `_historyTicks` and `_categorySnapshotTicks` to powers of two and replace `%` with `& (n-1)` — see §4.3.

The `CopyLatestCategorySnapshot` body is a hand-written `for` loop, not `Span.CopyTo` / `Buffer.BlockCopy`:

```csharp
for (int i = 0; i < copyMs; i++) destinationMs[i] = _perModCatMs[baseIdx + i];
```

For `n = 126` floats, that is 126 loads, 126 stores, a loop counter, and a bounds check the JIT will hoist if the access pattern is clean enough. `Span<T>.CopyTo(Span<T>)` lowers to `memmove`, which on .NET 8 for ≥ 64 bytes goes through an SSE2/AVX2 (or NEON on Apple silicon) intrinsic. For 504 B / 1 008 B copies, vectorised `memmove` beats the managed loop by 2–4× in throughput. See §4.2.

### 1.6 Snapshot allocation lifecycle

Each spike's `PerModCatMs` is heap-allocated at window open, retained for the window's lifetime in the 50-slot ring, then read by the session-drain path that re-copies its contents into a fresh `List<double>` (also heap-allocated). The ring is bounded at 50; once full, the oldest `SpikeWindow` is overwritten by `RingBuffer<T>.Push` — and its `PerModCatMs` array becomes eligible for Gen0 collection. That is exactly the GC pressure profile a 5 ms hot-path budget can absorb (50 windows × 504 B = 25 KB of churn per session in Lite mode, doubled in Standard), but it is *not* the zero-allocation contract the rest of the spike path follows. See §4.4 for the pool-backed replacement.

### 1.7 Coalescing logic (window de-dup)

`RecoveryTicksToCloseWindow = 1` means a single sub-threshold tick closes the open window. There is no allocation in `HandleSubThreshold`: when the window closes, the open struct is `in`-passed to `RingBuffer<SpikeWindow>.Push` which copies it into the slot. No coalescing of *across-close* spikes — by design, two close spikes are two records. This is the documented behaviour, not a bug. No allocation work to do here.

The worst-tick re-capture logic in the `else` branch is the only place where `CaptureSnapshot` can fire repeatedly inside one open window. The 4.5-min baseline session shows 50 spikes in 16 009 ticks, so average window length is short (~1–3 ticks), but a 172 ms world-enter freeze opens a window that gets re-snapshotted on every worst-tick advancement during the freeze. That is real allocation absent today *only* because `CaptureSnapshot` writes into the pre-allocated arrays — but every re-capture still pays the 126-float managed copy.

### 1.8 BSON shape (`SpikeWindowRow`)

```csharp
public sealed class SpikeWindowRow {
    [BsonId] public ObjectId Id { get; set; }
    [BsonField("_schema")] public int Schema { get; set; } = 1;
    public ObjectId SessionId { get; set; }
    public long StartTick { get; set; }
    public long EndTick { get; set; }
    public long WorstTick { get; set; }
    public double WorstFrameMs { get; set; }
    public double BaselineMs { get; set; }
    public double MadMs { get; set; }
    public bool Warming { get; set; }
    public string? Context { get; set; }
    public List<SpikeContributor> TopContributors { get; set; } = new();
    public List<double> PerModCatMs { get; set; } = new();
    public List<double>? PerModCatBytes { get; set; }
}
```

LiteDB BSON encodes field names as zero-terminated UTF-8 strings, **per row**. The dominant cost in this row is `PerModCatMs` — a `List<double>` of 126 elements (18 mods × 7 categories). Each element is encoded as a BSON `double` (type byte `0x01`) plus a key (`"0"` through `"125"` for an array element index), and LiteDB wraps the whole array in a 4 byte length + element list + 1 byte terminator. The per-element overhead is roughly:

```
1 byte  type byte
2-4 byte key string ("0".."125", length-prefixed)
8 byte  double payload
1 byte  separator (string null)
```

→ ~12-14 B per element × 126 elements = **~1.5–1.8 KB per row**, doubled if `PerModCatBytes` is non-null. For 50 spikes that is **75–90 KB/session for the spike collection alone**, well over half of the per-session DB growth observed in baseline §1 (1 064 KB at 10-min session, of which the spike collection is one of ~25 streams).

The win is twofold: (a) downgrade `double` → `float` on the wire because the in-RAM source is already `float`, and (b) collapse 126 BSON keys into one `BinaryData` blob whose bytes are the flat `float[]` payload. See §4.7.

The `TopContributors` list is bounded at 5 by `SessionRecorder.cs:611`, but each `SpikeContributor` is a class with four BSON fields (`ModId`, `Name`, `Ms`, `Bytes`) + LiteDB's `_type` discriminator. That is fine. No win there.

### 1.9 The `IReadOnlyList<SpikeWindow>` exposure

`SpikeDetector.Windows` returns the cached `SpikeWindowsView`. The view's enumerator is iterator-based (`yield return`) — every `foreach (var w in collector.Spikes)` allocates one enumerator. The two production readers (`SpikesTab.cs:92, 181` and `SessionRecorder.cs:228`) use the indexer, so the enumerator is dead code in those paths. The detector's `PeakContributorToSpikeDetector` (`Insights/Detectors/PeakContributorToSpikeDetector.cs:38-50`) also uses the indexer. The latent allocation never fires today, but the surface invites it. See §4.9.

---

## 2. Baseline numbers

From `context/perf-pass/baseline.md` and the source inspection above:

| Surface | v0.5 measurement | Source |
|---|---|---|
| Spike-detector OnTick (sub-threshold path) | not separately measured | embedded in 0.27 ms/tick PerformanceProfiler total |
| Spike-detector OnTick (spike tick, window-open) | not separately measured | one heap alloc of `float[126]` (Lite) or two of `float[126]` (Standard) per window-open |
| Spikes per 4.5-min playtest | 50 | baseline §2 |
| Worst spike frame ms | 172.0 (world-enter freeze) | baseline §2 |
| Spike-snapshot copy (per worst-tick) | 126 × 4 B managed `for` loop | source inspection |
| `Baseline.Recompute` per-tick cost (4× histogram passes over 1800 frames) | not separately measured | source inspection |
| `SessionRecorder.DrainSpikes` allocations per spike | `List<double>(126)` + `List<double>(126)` + `List<SpikeContributor>(0)` + 5 `SpikeContributor` instances + `SpikeWindowRow` | source inspection |
| Spike BSON row size | ~1.5–1.8 KB (Lite), ~3.0–3.6 KB (Standard) | BSON envelope calc above |
| Spike collection bytes/session | ~75–90 KB (Lite × 50 spikes), ~150–180 KB (Standard) | derived from above |

**The performance contract targets for this subsystem:**

| Target | v0.5 | v0.6 goal | Rationale |
|---|---|---|---|
| `SpikeDetector.OnTick` sub-threshold cost | unmeasured | < 5 ns | Measured floor; dominated by baseline reads + cmp. Setting an explicit target makes regressions visible. |
| `SpikeDetector.OnTick` spike-open cost (Lite) | one `float[126]` alloc | zero allocation | Pool-backed snapshot slot. Invariant 2 alignment. |
| `Baseline.Recompute` per-tick cost | unmeasured | ≥ 4× faster than today | Single-pass incremental sketch instead of 4× full-history bucket passes (§4.5). |
| Snapshot copy throughput | managed `for`, ~504 B in ~50 ns | `Span<float>.CopyTo` → `memmove`, ~504 B in ~15 ns | Vectorised intrinsic. |
| Spike BSON row size | 1.5–1.8 KB | < 400 B (Lite), < 700 B (Standard) | Flat blob + `float` width + key dedup. |
| `DrainSpikes` allocations per spike | 4 heap objects + 5 contributor classes | 1 row class only | Pre-built pooled lists, struct contributors. |

Anything in the same direction is progress; the exact numbers will be set by the master plan once micro-benchmarks confirm the deltas on the dev machine.

---

## 3. Algorithm research

### 3.1 Why median + MAD is the right primitive for this domain

The detector is a robust outlier finder over a heavy-tailed, bimodal distribution. Tick-time on modded Terraria has two visible modes — quiet ticks at 1–2 ms and GC-pause ticks at 20–40 ms — plus a long right tail from occasional asset loads and world-enter freezes. A mean-based threshold (`mean + k·σ`) walks toward the right tail and silently inflates the threshold; spikes hide under the new normal. Median + MAD ignores the top half of the distribution by construction:

- **Median** is the 50-th percentile; the right tail can grow arbitrarily without moving it (so long as the right tail is < 50 % of samples).
- **MAD** = `median(|x_i − median|)`. It is the median of absolute deviations. A symmetric Gaussian has `MAD ≈ 0.6745σ`; that scale factor is the conversion if one wants σ-compatible thresholds.
- A common Hampel-filter form is `|x − median| > k · MAD` with `k = 3`. We use a simpler relative form (`frame ≥ k · median`) because for a sub-millisecond baseline the absolute MAD term is too tight (a 1.0 ms median with a 0.1 ms MAD would flag every 1.3 ms tick), and the relative form already gives the user a normal-distribution-free outlier signal.

References:

- InfluxData, *Anomaly Detection with Median Absolute Deviation*: introduces the median-±-k·MAD form and discusses why it is more robust than mean-±-k·σ.
- Hampel filter overview (Medium piece linked in `spikes-and-allocations-plan.md` §0): same machinery on a sliding window.
- Wikipedia, *Median absolute deviation*: states the 0.6745 σ-to-MAD scaling factor and discusses heavy-tailed robustness.
- A. Akinshin, *Caveats of using MAD*: noted that MAD is consistent for Gaussians but breaks down for genuinely discrete or trivially-clustered distributions; not our case (frame-time is continuous and unimodal-with-tail).

The decision to keep median + MAD is right. The question is whether the *computation* of those scalars can be cheaper. That is the next four subsections.

### 3.2 Exact median over the rolling window — current method

`Baseline` uses a **fixed-bucket histogram** (`HistogramBuckets = 1024`, `HistogramBucketMs = 0.5`, so the maximum representable value is 512 ms; the bottom and top buckets are clamps). For each `Recompute` call it:

1. Zeroes the 1024-int histogram (`Array.Clear`).
2. Walks all `n` history entries, bumping the bucket index `(int)(value / 0.5)`.
3. Scans buckets left-to-right until cumulative count > `n/2`; returns the bucket's midpoint.

This is **O(n)** per call (n = 1800 for the 30-second history at 60 tps) and is called *every tick*. The MAD pass repeats the same machinery on the deviations. The tick-period pair adds two more passes. Total per tick: `4 × (1024 clear + 1800 bump + ≤1024 scan)`.

Pros today:

- Allocation-free: the histogram is a pre-allocated `int[1024]` field.
- O(1) memory (besides the histogram and the history ring).
- Bucket midpoint resolution (0.25 ms half-bucket error) is far below any downstream consumer's tolerance.

Cons:

- Four passes over a 1800-element ring per tick.
- The histogram is sized for a 0–512 ms range, but 99 % of tick-frame samples sit below 5 ms. The lower 16 buckets are saturated; the upper 1000 are empty. Cache footprint is fine (4 KB), but the bucket scan does dead work.
- Median + MAD are mathematically rank statistics. Computing them from scratch every tick when only one new sample has arrived is wasteful — the *update* between tick N and tick N+1 is one insertion and one eviction, which can be reflected incrementally on a sorted structure.

### 3.3 Streaming-quantile estimators (alternative families)

Three families of streaming-quantile estimators are relevant. Each trades exactness for sub-linear update cost.

#### 3.3.1 P² (Jain–Chlamtac, 1985)

The P² algorithm maintains 5 marker points whose positions track the 0-th, q/2, q, (1+q)/2, and 100-th percentiles. On every new observation it updates marker positions via a parabolic prediction formula. Storage: 5 doubles per quantile. Per-observation cost: ~10 floating-point ops + 5 comparisons.

Properties (Jain 1985, *The P-Square Algorithm for Dynamic Calculation of Percentiles and Histograms without Storing Observations*, CACM):

- O(1) storage, O(1) per-observation cost.
- Converges to the true quantile asymptotically for stationary or slowly-drifting distributions.
- **No sliding window** — it integrates over the full observation history. To get a 30-second median, one would have to run the estimator on a stream of *decaying* weights or reset periodically. Neither is a clean fit.
- Accuracy in practice: ~1 % relative error for the 50-th percentile on smooth distributions; degrades to ~5 % on sharply bimodal data.

P² gives us a per-sample O(1) update path but at the cost of "median of full history" rather than "median of last 30 s". The threshold the spike detector uses depends on *recent* baseline; over a long session, the all-history median lags the live behaviour. For our use case, P² is not a drop-in.

What it *could* be used for: an "all-session" baseline shown alongside the rolling baseline (e.g., "current spike threshold 12 ms, session-median threshold 8 ms"). That is feature work, not optimisation. Park for a future milestone.

References:

- Jain & Chlamtac, *The P-Square Algorithm for Dynamic Calculation of Percentiles and Histograms*, CACM 28(10), 1985.
- A. Akinshin, [*P² quantile estimator: estimating the median without storing values*](https://aakinshin.net/posts/p2-quantile-estimator-intro/).
- Erthalion, [*PSquare: practical quantiles*](https://erthalion.info/2021/10/04/psquare/).
- `rfrenoy/psquare` (reference Python implementation): https://github.com/rfrenoy/psquare.

#### 3.3.2 t-digest

A t-digest maintains a set of (mean, weight) centroids over the distribution, with finer resolution at the tails. Per-insertion cost is O(log k) where k is the centroid count (typically 50–100). Storage is O(k).

Properties:

- Excellent for extreme tail quantiles (p99.9, p99.99). Median accuracy is on par with P² but with more memory.
- Mergeable (multiple t-digests combine in O(k log k)).
- No native sliding-window form: same problem as P². Sliding requires either a windowed t-digest variant (more complex) or periodic reset.

Reference: OpenSearch's MAD implementation uses t-digest under the hood ([OpenSearch docs](https://docs.opensearch.org/latest/aggregations/metric/median-absolute-deviation/)), but they compute it over a finite query window — they batch.

For our use case, t-digest is **overkill on the median** (we want p50, the worst case for t-digest's tail-bias trade-off) and is **architecturally wrong for a sliding window** without bolt-ons. Reject as a primary primitive.

References:

- T. Dunning & O. Ertl, *Computing Extremely Accurate Quantiles Using t-Digests*, 2019.
- *Theory meets Practice at the Median: a worst case comparison of relative error quantile algorithms*, arXiv 2102.09299 — concludes ReqSketch beats t-digest on uniform distributions and t-digest "may fail to provide accurate estimates" on non-uniform repeated draws.

#### 3.3.3 Frugal streaming (Ma, Muthukrishnan, Sandler, 2014)

The frugal-1U algorithm uses **a single counter** (the current quantile estimate) and updates it by ±1 step on every observation, with the step direction biased by which side the observation falls on. Memory: one number. Convergence is slow for fast-changing distributions and the step size has to be tuned.

For tick-time spike detection this is too imprecise — a single-counter estimate cannot track the median when the median itself moves by 0.5 ms during a hot zone.

Reference: *Frugal Streaming for Estimating Quantiles*, arXiv 1407.1121.

#### 3.3.4 Verdict on streaming estimators

**None of the three is a drop-in replacement for the windowed median + MAD we want.** All three lose the sliding-window property that makes the current detector adaptive to local behaviour. The right optimisation is not "switch the algorithm" but **incremental maintenance of the existing histogram between ticks** — see §4.5.

### 3.4 Sorting tiny arrays — are we even doing this?

The detector never sorts. The histogram approach is partition-of-counts, not partition-of-values. So the literature on small-array sorting (insertion sort below 16 elements, branchless networks below 8) is **not on the spike detector's hot path** today.

It *would* become relevant if a future change reduced the rolling history (n) to a small number — say, "median over last 30 ticks only" — where direct sort becomes faster than bucket. The crossover from "incremental bucket" to "sort the whole window" is around n = 16 in cache. For our n = 1800 we are firmly in the bucket regime. Leave the sort literature in the references section for completeness.

References (preserved for future use, not active levers):

- Bingmann et al., *Engineering faster sorters for small sets of items*, SPE 51(7), 2021 — sorting networks for n ≤ 16, insertion sort for 16 < n ≤ 32, ips⁴o for n > 32.
- AndrDm/SortingNetworks-fast: SSE4/AVX2 sorting networks for n ≤ 6.
- .NET's `Array.Sort` uses introsort with insertion-sort fallback at n ≤ 16.

### 3.5 Incremental histogram maintenance (the real win)

The detector's baseline depends on the *current* contents of the 1800-frame ring. Between tick N and tick N+1, the ring evicts one frame (the oldest) and inserts one frame (the newest). The histogram can be updated incrementally:

```
on every tick:
    if ring is full:
        oldBucket = bucket_of(history[ring_oldest_index].FrameTimeMs)
        _histogram[oldBucket]--                              // 1 store
    newBucket = bucket_of(frame.FrameTimeMs)
    _histogram[newBucket]++                                  // 1 store
    // median scan still required, but on a histogram that is
    // already in steady state, the scan can start from the
    // last-known median bucket and walk locally.
```

Per-tick cost drops from `1024 + 1800 + ≤1024 = ~3 800` array ops to `2 + local_scan ≈ 10–20` array ops. The median scan can also be amortised by caching the previous tick's median bucket and walking outward — the cumulative count changes by at most 1 per tick, so the new median is at most one bucket away from the old.

MAD is the harder case. `|x − median|` depends on the current median, so a bucket update on the deviation histogram requires knowing the *median at the time the sample was first seen* (when it was inserted into the deviation histogram). Three valid approaches:

1. **Recompute MAD from scratch occasionally.** Run the deviation histogram pass every K ticks (K = 30, half a second) and use the cached MAD between recomputes. Drop from 60 × 1800 = 108 000 ops/s to 2 × 1800 = 3 600 ops/s. The MAD estimate is at most K/2 ticks stale, which is irrelevant for a threshold that only governs which side of a 2× multiple a 20 ms spike sits on.
2. **Bucket the raw samples, not the deviations.** Maintain a single 1024-bucket frame-time histogram and derive MAD analytically from the cumulative distribution: locate the median bucket M, then scan outward from M counting weighted samples until cumulative count ≥ n/2 of the "deviation distribution". This is a single-pass derivation per recompute, no second histogram needed.
3. **Approximate MAD via the inter-quartile half-range.** MAD ≈ 0.5 × IQR (Q75 − Q25) for symmetric distributions. Our distribution is right-skewed, so the half-IQR underestimates MAD; calibration is a constant multiplier. Easy to compute from a histogram (Q25 + Q75 scans), no second pass.

Option 1 is the lowest-risk incremental step. Option 2 is more elegant but requires a verified equivalence pass against the current bucket-of-deviations method. Option 3 changes the meaning of MAD slightly and would require updating documentation. Recommend **option 1 for v0.6** and revisit option 2 in v0.7 if profiling shows MAD recompute is still material.

The same machinery applies to `TickPeriodMedian` / `TickPeriodMad`.

References for incremental histograms / sliding-window quantiles:

- Chen, Lambert, Pinheiro, *Incremental Quantile Estimation for Massive Tracking*, KDD 2000.
- Karnin, Lang, Liberty, *Optimal Quantile Approximation in Streams*, FOCS 2016 (KLL sketch).

### 3.6 The snapshot copy primitive

A 504 B `float[126]` copy on Apple silicon NEON: a single `memcpy` lowers to 4 × 128-bit `ldr/str` pairs (since 504 ≈ 4 × 128). The JIT's vectorised `memmove` does this implicitly when called via `Span<T>.CopyTo`. The current managed loop does *not* vectorise — it lowers to a scalar loop with bounds checks the JIT will only partially elide. Throughput floor for the managed loop is ~5 GB/s; vectorised path is ~25 GB/s on M-series.

Reference:

- Adam Sitnik, [*Span*](https://adamsitnik.com/Span/) — discusses `Span<T>.CopyTo` lowering to `Buffer.Memmove`.
- Microsoft Learn, [*.NET 8 Span/Memory* docs](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/high-performance/spanowner).

---

## 4. Optimisation opportunities

Each opportunity below names the file, the change shape, the expected delta, the risk class, and how we verify it. None violates an Invariant.

### 4.1 `SpikeDetector.OnTick` micro-cleanup (sub-threshold path)

**Files:** `Profiling/SpikeDetector.cs`.

**Change:** Hoist the baseline reads into local doubles **outside** the `IsCalibrated` branch, and short-circuit when `_windowOpen` is false (the sub-threshold path does nothing if no window is open). Today's path on a sub-threshold tick with no open window does:

```
load baseline.IsCalibrated → branch
load FrameTimeMs, FrameMsMedian, FrameMsMad → multiply + cmp
load _windowOpen → branch → return
```

A cleaner shape:

```
if (!_windowOpen && frame.FrameTimeMs < CachedThreshold) return;   // fast-path
```

Where `CachedThreshold = baseline.FrameMsMedian * ThresholdMultiplier` is cached at `Baseline.Recompute` end (one extra multiply once per tick, not on every comparison). The fast-path skips three property accesses, two branches, and a multiply on the >99 % of ticks that are sub-threshold with no open window.

**Risk class:** trivial. Pure refactor.

**Expected delta:** ~5–10 ns / sub-threshold tick. On 60 tps that is 0.3–0.6 µs/s — meaningless in isolation, but it composes with §4.5's bigger win.

**Verification:** BenchmarkDotNet micro-bench of `OnTick` on a synthetic sub-threshold loop. Tag the result in `context/perf-pass/research/` follow-ups.

### 4.2 `CopyLatestCategorySnapshot` → `Span<float>.CopyTo`

**Files:** `Profiling/PerTickAttributionRing.cs:188–210, 226–252`.

**Change:** Replace the two hand-rolled `for` loops with `Span<float>.CopyTo`:

```csharp
public void CopyLatestCategorySnapshot(Span<float> destinationMs, Span<float> destinationBytes)
{
    if (_writeCount == 0) return;
    int catCount = PerModAttribution.CategoryCount;
    int latestSlot = (int)((_writeCount - 1) & (_categorySnapshotTicks - 1)); // see §4.3
    int baseIdx = latestSlot * _modCount * catCount;
    int n = _modCount * catCount;

    int copyMs = Math.Min(n, destinationMs.Length);
    _perModCatMs.AsSpan(baseIdx, copyMs).CopyTo(destinationMs);

    if (_perModCatBytes is not null && destinationBytes.Length > 0)
    {
        int copyBytes = Math.Min(n, destinationBytes.Length);
        _perModCatBytes.AsSpan(baseIdx, copyBytes).CopyTo(destinationBytes);
    }
}
```

`Span<float>.CopyTo` lowers to `Buffer.Memmove`, which on .NET 8 dispatches to the vectorised intrinsic for ≥ 64 B copies. For our 504 B / 1 008 B target, that is a 2–4× throughput win and removes the per-element bounds check.

**Risk class:** trivial. Behavioural equivalent.

**Expected delta:** snapshot copy time ~50 ns → ~15 ns per spike-open and per worst-tick re-capture. Cumulative over a session: 50 spikes × ~35 ns saved = 1.75 µs/session. Tiny in absolute terms, but it's a strictly-better swap with no downside and it lifts the per-spike cost ceiling for future feature work that wants to re-capture more often.

**Verification:** BenchmarkDotNet micro-bench, plus a round-trip test that the spike-row contents are byte-identical to the current implementation.

### 4.3 Power-of-two ring sizes → modulo to AND

**Files:** `Profiling/PerTickAttributionRing.cs:111-112, 161-162, 176-177, 192, 232-233` and the constructor call site in `Profiling/MetricCollector.cs:154`.

**Change:** Round `_historyTicks` and `_categorySnapshotTicks` up to the next power of two (`1800 → 2048`, `120 → 128`) and replace every `% _historyTicks` / `% _categorySnapshotTicks` with `& (_historyTicks - 1)` / `& (_categorySnapshotTicks - 1)`. Mask is a single ALU op (1 cycle); modulo is a divide (10–30 cycles).

The window-length contract is unchanged from the consumer's perspective: history retains the last 2 048 ticks rather than 1 800 (~34 s instead of 30 s); cat-snapshot retains 128 ticks rather than 120 (~2.13 s instead of 2.0 s). Memory grows by `(2048-1800)/1800 = 13.8%` on the totals array and `(128-120)/120 = 6.7%` on the cat-snapshot array. At 18 mods the totals array goes from 129 KB → 147 KB; in the 200-mod nightmare it goes from 1.4 MB → 1.6 MB. Still well inside the 5 MB ceiling cited in `notes/spikes-and-allocations-plan.md` §4.

The constructor must enforce the power-of-two invariant or round the input up:

```csharp
public PerTickAttributionRing(int modCount, int historyTicks, int categorySnapshotTicks, bool trackAllocations)
{
    _historyTicks = NextPow2(historyTicks);
    _categorySnapshotTicks = NextPow2(categorySnapshotTicks);
    _historyMask = _historyTicks - 1;
    _categorySnapshotMask = _categorySnapshotTicks - 1;
    // …
}
static int NextPow2(int n) => n <= 1 ? 1 : 1 << (32 - BitOperations.LeadingZeroCount((uint)(n - 1)));
```

**Risk class:** small. Memory grows ~14 %. The math is well-known; the constructor change must be tested.

**Expected delta:** `Push` saves 2 divides → 2 ANDs, ~20–50 ns/tick. At 60 tps that is **1.2–3.0 µs/s of game-thread time**, or ~0.01 % of the per-tick budget — bigger than it looks because divides also stall the pipeline. `GetPerModMs` and `TryGetCategorySnapshot` save 3 divides each, but those are called per-spike at session-drain time, off the hot path.

**Verification:** `PerTickAttributionRingTests` should already cover indexing; expand them to confirm rounded-up sizes still return the correct historical sample for a synthetic tick stream. Compare BSON output of a fixed-seed session before/after.

### 4.4 Pool-backed snapshot slots (eliminate per-window-open allocations)

**Files:** `Profiling/SpikeDetector.cs` (the `new SpikeWindow { PerModCatMs = new float[…], … }` site at lines 182-196) and a new helper.

**Problem:** Every `OnTick` that opens a window heap-allocates one or two `float[modCount*catCount]` arrays. 50 windows/session at 1 008 B = 50 KB/session of Gen0 churn. Tiny in absolute terms, but it is the *only* per-tick allocation site in the spike subsystem, and Invariant 2 says zero allocation on the hot path. It is also the floor that future feature work (more spike windows, larger mod counts, additional snapshot dimensions) builds on — leaving it as "allocates once per window-open" means the cost grows with `(modCount × spike_rate)`.

**Design:** A pre-allocated **snapshot pool** of `2 × (RingCapacity)` `float[]` slots, owned by `SpikeDetector`, indexed by window. When the ring evicts a window, its snapshot slot is reclaimed. The 50-window ring already has a stable slot identity (the physical `_head` walk in `RingBuffer<T>`), so the pool can be parallel-indexed by ring slot:

```csharp
private readonly float[][] _msSnapshotPool;     // [50][modCount*catCount]
private readonly float[][]? _bytesSnapshotPool; // [50][modCount*catCount] or null

public SpikeDetector(int modCount, bool tracksAllocations)
{
    int n = modCount * PerModAttribution.CategoryCount;
    _msSnapshotPool = new float[50][];
    for (int i = 0; i < 50; i++) _msSnapshotPool[i] = new float[n];
    if (tracksAllocations)
    {
        _bytesSnapshotPool = new float[50][];
        for (int i = 0; i < 50; i++) _bytesSnapshotPool[i] = new float[n];
    }
    // ...
}
```

The `RingBuffer<SpikeWindow>` needs a "next-write-slot" peek so the detector can grab the right snapshot array when opening a window. Add a `PeekWriteSlot()` method that returns the index `_items[_head]` will be written to (no state mutation):

```csharp
public int PeekWriteSlot() => _head;
```

At `Push` time the slot becomes occupied; the previous occupant's `PerModCatMs` is *the same array reference* (now overwritten in place). Zero allocations after the constructor.

**Behavioural equivalence:** Identical. The `SpikeWindow` struct's `PerModCatMs` reference points at a pool array instead of a fresh allocation; downstream readers don't distinguish. The pool's arrays are scribbled by `CopyLatestCategorySnapshot`, which zero-clears or full-overwrites the destination on every capture (the current code does *not* zero-clear before the loop because the loop copies all 126 cells unconditionally — verify this remains true after §4.2's `Span.CopyTo` change; it does, because `CopyTo` writes the full slice).

**Risk class:** small-medium. Three subtleties:

1. The snapshot pool is alive for the lifetime of the detector, not the lifetime of a window. Memory footprint grows from "average 11 windows live × 504 B = 5.5 KB" to "always 50 × 504 B = 25 KB". For Standard mode (both arrays), 50 KB. Compared to the `PerTickAttributionRing`'s 129 KB+, this is in the noise.
2. The `RingBuffer<SpikeWindow>.PeekWriteSlot` exposes implementation detail; constrain it to internal use by the detector (the `RingBuffer<T>` type is already in the `PerformanceProfiler.Profiling` namespace and the detector is the sole legitimate caller of this method).
3. When the buffer is cleared (`RingBuffer.Clear()`, called by the detector on world unload?), the snapshot pool slots stay allocated (correct — we want to reuse them next world).

**Expected delta:** removes the only per-tick allocation site in the spike subsystem. Strictly-better Gen0 profile. Per-spike CPU saved: ~30 ns (avoid the `new float[126]` + the `new float[126]` allocations and their type-init writes). 50 spikes/session × 30 ns × 2 arrays = 3 µs/session of allocator-thread savings. The headline value is the *invariant compliance*, not the µs.

**Verification:** test that 100 consecutive synthetic spikes do not increase Gen0 allocations beyond the initial pool allocation (use `GC.GetAllocatedBytesForCurrentThread` before/after). Test that a `SpikeWindow` returned from `Windows[i]` after eviction holds the correct (newer) data, not a stale view. Test that the snapshot pool tolerates a `Clear()` + replay cycle.

### 4.5 Incremental baseline maintenance

**Files:** `Profiling/Baseline.cs`.

**Problem:** Four 1024-clear + 1800-iterate + 1024-scan passes every tick. The biggest per-tick cost in the spike subsystem. See §1.4 and §3.5.

**Design:** Maintain the frame-time histogram **incrementally** on every `BeginTick` / `EndTick` cycle. When `MetricCollector.EndTick` pushes a new frame into the history ring, the same call drops the evicted frame and adds the new frame to the histogram:

```csharp
// In Baseline.cs, called from MetricCollector immediately after history.Push:
public void OnFrameInsertedIncrementally(double newFrameMs, double? evictedFrameMs)
{
    if (evictedFrameMs.HasValue) _frameHistogram[BucketIndex(evictedFrameMs.Value)]--;
    _frameHistogram[BucketIndex(newFrameMs)]++;

    // Median: walk the cached median bucket outward; cumulative count changed by at most ±1.
    UpdateMedianBucket();
    FrameMsMedian = BucketMidpoint(_medianBucket);

    // MAD: recompute lazily, every K=30 ticks.
    if ((++_ticksSinceMadRecompute) >= MadRecomputeInterval)
    {
        _ticksSinceMadRecompute = 0;
        RecomputeMad();
    }
}
```

Median update cost: 2 array ops + a local scan of at most a few buckets. MAD update cost: full pass every 30 ticks → 60× cheaper per-tick amortised.

Tick-period (the stall-detector primitive) is the same shape — incremental insert/evict on a separate histogram, MAD recomputed every K ticks.

**Risk class:** medium. Three correctness concerns:

1. **Histogram drift.** Float arithmetic guarantees the bucket index is the same as long as the input is the same, but a long-running session with billions of inserts/evictions over the histogram could in principle desync from "what `Recompute` would produce from scratch". Mitigation: run a *full* `Recompute` every N ticks (N = 1800, i.e. once every full history rotation) and assert the incremental result matches within 1 bucket. Log a `Logger.Warn` if it desyncs and fall back to the full recompute for that tick.
2. **Cold-start.** Before the history ring fills, `evictedFrameMs` is null. Handle the path explicitly; tests cover the first 1800 ticks of a fresh session.
3. **Reset on world unload.** `Baseline.Reset()` must also zero the histograms and the cached median bucket.

**Expected delta:** dominant. Per-tick `Baseline.Recompute` cost drops from ~3 800 array ops × 4 passes to **2 array ops + amortised MAD**. On the M-series dev machine, expect ~10 µs/tick → ~0.3 µs/tick. At 60 tps that is **~580 µs/s of game-thread cost reclaimed**, or ~0.06 % of the per-tick budget. Bigger on a high-end Windows box where divides are pricier.

Note this is also a `StallDetector` win — the stall path consumes `TickPeriodMsMedian` and `TickPeriodMsMad`. The optimisation lands in `Baseline`, both detectors benefit.

**Verification:** parametric test that runs a 60-tick stream through both the incremental and the from-scratch paths and asserts bucket-equality. A second test runs a 100 000-tick stream and asserts the incremental median + MAD never drift more than 1 bucket from the full-recompute oracle. BenchmarkDotNet measurement of per-tick cost.

### 4.6 `SpikeWindowsView.GetEnumerator()` zero-alloc

**Files:** `Profiling/SpikeDetector.cs:258-272`.

**Change:** Replace the `yield return` iterator with a struct enumerator implementing `IEnumerator<SpikeWindow>`. Today's `yield return` compiles to a compiler-generated class instance allocated on every `foreach`. A struct enumerator is allocation-free when used through `foreach` on the concrete view type (the C# compiler pattern-matches struct enumerators) but still safe through the `IEnumerator<T>` boxing path.

```csharp
public Enumerator GetEnumerator() => new Enumerator(_source);

public struct Enumerator : IEnumerator<SpikeWindow>
{
    private readonly RingBuffer<SpikeWindow> _src;
    private int _i;
    private SpikeWindow _current;
    public Enumerator(RingBuffer<SpikeWindow> src) { _src = src; _i = -1; _current = default; }
    public SpikeWindow Current => _current;
    object System.Collections.IEnumerator.Current => _current;
    public bool MoveNext()
    {
        int n = _src.Count;
        int next = _i + 1;
        if ((uint)next >= (uint)n) return false;
        _current = _src[next];
        _i = next;
        return true;
    }
    public void Reset() { _i = -1; _current = default; }
    public void Dispose() { }
}
```

**Risk class:** trivial.

**Expected delta:** zero-alloc enumeration when the view is iterated. Today's two production readers use the indexer, so this is **latent-allocation removal**, not active. The reason to make the change anyway: the `IReadOnlyList<SpikeWindow>` surface is the natural reader contract, and any future LINQ-ish use (`.Take(5)`, `.Where(…)` in an insight, an `foreach`) will allocate today and not after.

**Verification:** unit test that `foreach (var w in detector.Windows)` allocates zero bytes (via `GC.GetAllocatedBytesForCurrentThread` before/after).

### 4.7 `SpikeWindowRow` BSON shape — flat `BinaryData` blob, `float` width

**Files:** `Profiling/Persistence/Records/SpikeWindowRow.cs`, `Profiling/Persistence/Streams/SpikeStream.cs`, `Profiling/Persistence/SessionRecorder.cs:226-263, 615-620`.

**Problem:** As measured in §1.8, `PerModCatMs` and `PerModCatBytes` serialise as `List<double>` of 126 elements with per-element BSON key/type/separator overhead. ~1.5–1.8 KB per row in Lite mode, ~3.0–3.6 KB in Standard. 50 rows/session → 75–180 KB/session for the spike collection alone.

**Design:** Three independent changes that stack:

1. **`float` not `double`.** The in-RAM source is already `float[]` (see `SpikeWindow.PerModCatMs : float[]`). Widening to `double` for storage doubles every cell's payload for zero precision gain — the source data has ~7 decimal digits, the persistence format gives ~16. Drop the widen.

2. **Flat blob, not array.** Replace the `List<double>` with `byte[]` containing the raw `float[]` bytes. BSON encodes a `byte[]` as a `BinData` (type byte `0x05`) with subtype `0x00` (generic binary). The per-element overhead drops from ~12-14 B/cell to zero (just the leading byte-array header). For 126 cells × 4 B = 504 B blob, the BSON envelope is `~7 B header + 504 B payload = ~511 B` vs today's `~1 500 B`. ~3× shrink.

3. **Sidecar shape descriptor.** The flat blob needs `modCount` + `categoryCount` (or just `cellCount`) on the row so the reader can reconstruct the matrix layout. Both are already implicit today (`PerModCatMs.Count = modCount * categoryCount`). Add an explicit `int ModCount` and `int CategoryCount` to the row for verification.

```csharp
public sealed class SpikeWindowRow {
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 2;
    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long StartTick { get; set; }
    public long EndTick { get; set; }
    public long WorstTick { get; set; }
    public double WorstFrameMs { get; set; }
    public double BaselineMs { get; set; }
    public double MadMs { get; set; }
    public bool Warming { get; set; }
    public string? Context { get; set; }
    public int ModCount { get; set; }
    public int CategoryCount { get; set; }
    public byte[] PerModCatMsBlob { get; set; } = Array.Empty<byte>();
    public byte[]? PerModCatBytesBlob { get; set; }
    public List<SpikeContributor> TopContributors { get; set; } = new();
}
```

Schema bump from 1 → 2 marks the format change. Reader code (`SpikeStream.Reconstruct`, `SessionSummaryLogger`, `QueryChatCommands`, the insight detector) reads the schema and either parses the blob into a `Span<float>` on the fly (no allocation needed if the consumer just sums per mod) or — for backwards compatibility with old DB files — falls back to the `List<double>` path when `Schema = 1`.

**Risk class:** medium. Touches the on-wire format. Two consumers (`SessionSummaryLogger.worstSpike` and the `PeakContributorToSpikeDetector` insight) read these fields and must handle both schemas.

**Expected delta:** spike collection storage drops from ~75–90 KB/session (Lite) and ~150–180 KB/session (Standard) to ~25 KB/session (Lite) and ~50 KB/session (Standard). Cumulative on baseline §1's 1 064 KB / 10-min session: removing 50 KB at Standard is ~5 % of total DB growth — a chunk of the path back from 1 064 KB toward the < 600 KB target.

**Verification:** schema-migration test that reads a v1 row and a v2 row and produces identical `(ms, bytes)` per-mod totals for the same source data. Storage-size assertion on a fixed-seed 10-min synthetic session.

### 4.8 `DrainSpikes` allocation reduction

**Files:** `Profiling/Persistence/SessionRecorder.cs:226-263, 582-613, 615-620`.

**Problem:** Per spike at drain time we allocate `SpikeWindowRow` + two `List<double>` (gone after §4.7) + `List<SpikeContributor>(0)` + 5 `SpikeContributor` class instances. The drain runs on the writer thread (good), but the work feeds the end-of-session flush — the path that the baseline labels "8.5 s UiOverlayBlocking cluster".

**Change (post-§4.7):**

1. The two `List<double>` are already gone — replaced by `byte[]` blob.
2. `BuildSpikeTopContributors` currently allocates `List<SpikeContributor>(0)`, sorts it, and trims to 5. Replace with a fixed-size `SpikeContributor[5]` and an in-place top-K via insertion (5-element insertion-sort selection): for each mod, compare against the slot with the smallest `Ms` in the current top-5 and replace if larger. No list, no comparison delegate, no `Sort`.

```csharp
private void BuildSpikeTopContributors(in SpikeWindow w, SpikeContributorEntry[] outTop5)
{
    int modCount = PerModAttribution.ModCount;
    int catCount = PerModAttribution.CategoryCount;
    string[] names = HookInterceptor.ProfiledModNames;

    for (int i = 0; i < 5; i++) outTop5[i] = default;
    int filled = 0;

    for (int modId = 0; modId < modCount; modId++)
    {
        float ms = 0f, bytes = 0f;
        int offset = modId * catCount;
        for (int c = 0; c < catCount; c++)
        {
            int idx = offset + c;
            ms    += w.PerModCatMs[idx];
            if (w.PerModCatBytes is not null) bytes += w.PerModCatBytes[idx];
        }
        if (ms <= 0f) continue;

        // Insert into top-5 if larger than the current min.
        if (filled < 5)
        {
            outTop5[filled++] = new SpikeContributorEntry(modId, names[modId], ms, bytes);
        }
        else
        {
            int minIdx = 0;
            for (int i = 1; i < 5; i++) if (outTop5[i].Ms < outTop5[minIdx].Ms) minIdx = i;
            if (outTop5[minIdx].Ms < ms)
                outTop5[minIdx] = new SpikeContributorEntry(modId, names[modId], ms, bytes);
        }
    }

    // Sort descending in place — 5 elements, insertion sort.
    for (int i = 1; i < filled; i++)
    {
        var x = outTop5[i]; int j = i - 1;
        while (j >= 0 && outTop5[j].Ms < x.Ms) { outTop5[j + 1] = outTop5[j]; j--; }
        outTop5[j + 1] = x;
    }
}
```

3. `SpikeContributor` (class with 4 properties + LiteDB discriminator) → `SpikeContributorEntry` (readonly struct with the same fields). Stored as a fixed-length 5-slot array on the row. BSON encodes the struct as a sub-document; LiteDB handles structs via `BsonMapper`.

**Risk class:** medium-low. The struct conversion changes the BSON shape of the contributor list; that's a schema-2 concern and rides along with §4.7.

**Expected delta:** allocations per spike at drain: today 4 heap objects + 5 class instances + a comparer delegate; after this change, 1 row class + 1 byte[] (or 2 if alloc tracked) — three heap objects per spike. ~50× fewer allocations on the drain path. The drain runs off the game thread, so the user-visible win is "less GC pressure during session-end", which feeds the 8.5 s UiOverlayBlocking reduction.

**Verification:** session-end allocation assertion via `GC.GetAllocatedBytesForCurrentThread` (writer-thread scope). Round-trip equivalence test: today's `TopContributors` for a fixed-seed session match the new struct-based `TopContributors` byte-for-byte after BSON encode/decode.

### 4.9 Surface tightening — remove the latent enumerator path

**Files:** `Profiling/SpikeDetector.cs`.

**Change:** Make `Windows` return the concrete struct-enumerable view rather than `IReadOnlyList<SpikeWindow>`. Today the property type is `IReadOnlyList<SpikeWindow>`, which forces any consumer that wants enumeration to box through the interface enumerator. Replace with `SpikeWindowsView` directly (a public readonly struct or a public sealed class that implements `IReadOnlyList<SpikeWindow>` and also has the struct enumerator from §4.6). Consumers that bind to `IReadOnlyList<SpikeWindow>` continue to work (the view implements the interface); consumers that want zero-alloc enumeration get the struct enumerator via pattern-matching `foreach`.

**Risk class:** trivial. Pure type-surface tightening.

**Expected delta:** future feature work (insights engine, overlay) can iterate `Spikes` without allocation. Latent-allocation removal.

**Verification:** existing tests must compile against the tightened surface unchanged.

### 4.10 Worst-tick re-capture amortisation

**Files:** `Profiling/SpikeDetector.cs:200-211`.

**Problem:** Inside one open window, every frame whose time exceeds the current `WorstFrameMs` re-captures the snapshot. For a long-running spike (e.g. the 172 ms world-enter freeze), `CaptureSnapshot` can fire dozens of times across the freeze's duration. Each call copies 126 floats.

**Change:** Re-capture lazily. Track only `WorstFrameMs` and `WorstTick` on each tick; defer the snapshot copy until the window *closes*, then make one final `CopyLatestCategorySnapshot` at the WorstTick.

The blocker: by the time the window closes, the cat-snapshot ring may have evicted the WorstTick's row. The cat-snapshot ring holds 120 (→ 128 after §4.3) ticks ≈ 2 s; window durations on the baseline session are 1-3 ticks typical, with the world-enter freeze at ~170 ticks at 1 ms/tick or ~10 ticks at 17 ms/tick. Either way the WorstTick is still in the cat-snapshot ring at window close.

Add `TryGetCategorySnapshot(WorstTick, …)` — already exists. The detector simply calls it at window close instead of `CopyLatestCategorySnapshot` at every re-capture.

**Risk class:** low. The TryGetCategorySnapshot path is well-tested. The only edge case is "WorstTick has aged out of the cat-snapshot ring", which can happen if a spike window is open for > 2 s. In that case the detector falls back to the most-recent snapshot (current behaviour for the latest tick), and the row records `SnapshotTick` so the reader knows the snapshot is from end-of-window rather than WorstTick. This is a degraded but correct fallback.

**Expected delta:** for typical 1-3 tick windows, no change (snapshot copied exactly once at window open, then again at close — same total). For long windows, drops re-capture from O(ticks-in-window) to O(1). Saves ~50 ns × (window-length - 1) per long window. Material only for the rare extreme spike (world-enter, big asset load), but those are the spikes that the user *most* wants the right snapshot for, and forcing a single end-of-window capture also locks down the WorstTick attribution more precisely (today's "most recent re-capture during the worst tick" can drift if the per-tick attribution ring's contents shift between the two events).

**Verification:** synthetic spike test that opens a window with 1, 10, 100 ticks and asserts exactly one `CopyLatestCategorySnapshot` (or `TryGetCategorySnapshot`) call per window. Round-trip test that for a 1-tick window the snapshot is identical to today's.

### 4.11 Detector ↔ ring decoupling validation (defensive)

**Files:** `Profiling/SpikeDetector.cs:239-252`, `Profiling/PerTickAttributionRing.cs:188-210`.

**Note (not an optimisation, an invariant check):** The detector's `CaptureSnapshot` comment explicitly says "do not use `TryGetCategorySnapshot` by tick; use `CopyLatestCategorySnapshot`" because the ring's internal counter and the game tick can drift. §4.10 would re-introduce a `TryGetCategorySnapshot(WorstTick, …)` call. The drift hazard is bounded by the cat-snapshot window: if WorstTick has aged out, `TryGetCategorySnapshot` returns false and the detector falls back to `CopyLatestCategorySnapshot`. This is the *correct* contract for §4.10. The historical bug was "ring monotonic counter ≠ Main.GameUpdateCount"; the fixed `_lastGameTick` field plus `ago` window check resolves it.

Add an explicit unit test that exercises the "WorstTick aged out" fallback so a future change cannot silently regress this. Categorise as **Cov** (coverage), not optimisation.

### 4.12 Logger noise from `DrainSpikes`

**Files:** `Profiling/Persistence/SessionRecorder.cs:255-262`.

**Note (not an optimisation, an observability check):** The drain emits a `Logger.Info` for every spike with `WorstFrameMs ≥ 6 × BaselineMs ∧ !Warming`. In the baseline session (50 spikes), this fires per worst-spike. Volume is fine; the message format is per-spike not per-cluster. No change proposed; logging respects Invariant 2 by virtue of running on the writer thread. Documented for completeness.

---

## 5. Cross-system dependencies

### 5.1 Upstream: `MetricCollector` source

**Coupling shape:** `MetricCollector.EndTick(frame)` calls, in order:

```
baseline.Recompute(history, tracksAllocations, allocBytesThisTick);   // §4.5 lands here
perTickRing.Push(gameTick, perModRawMs, perModRawBytes);              // §4.3 lands here
spikeDetector.OnTick(frame, baseline, perTickRing);                   // §4.1, §4.4 land here
```

The order is load-bearing: the detector reads `baseline` and `perTickRing` *after* both have been updated for this tick. §4.5's incremental baseline maintenance must hook into the same `EndTick` ordering — the new `OnFrameInsertedIncrementally` call replaces (or wraps) `baseline.Recompute(…)` and runs after the history `Push`.

`MetricCollector._perTickRing` is exposed via `MetricCollector.PerTickRing` (a `public` getter) and consumed by the SpikesTab and the session recorder. Changing its slot math (§4.3) does not change its public contract, only its internal `%` to `&`.

`MetricCollector.Spikes` exposes `_spikeDetector.Windows` directly. Tightening the return type (§4.9) is a `MetricCollector` surface change as well — the collector's property declaration must match the detector's.

`MetricCollector.FlushSpikes` is the world-unload hook. It calls `_spikeDetector.Flush()` which `Push`-es any open window into the ring. With §4.4 (pool-backed snapshots), the same flush re-uses a pool slot rather than allocating — no behavioural change at the FlushSpikes contract level.

### 5.2 Downstream: persistence

**Coupling shape:** `SessionRecorder.DrainSpikes` (in `SessionRecorder.cs:226-263`) reads `collector.Spikes` via the cursor `_spikeCursor`, builds `SpikeWindowRow` instances, and enqueues `DbWriteOp.Spike(row)` against the writer-thread queue. The writer thread runs `SpikeStream.Apply` which is a single `db.SpikeWindows.Upsert(row)` call.

`SessionSummaryLogger.cs:32-67` reads the spike collection at session-end to compute the worst-spike summary log line. It accesses `worstSpike.TopContributors[0].Name` and `.Ms`. The §4.8 change (struct contributor) must preserve those accessors.

`QueryChatCommands.cs` exposes spike queries via the chat command surface; the §4.7 schema bump must produce decodable output on both schemas.

`PeakContributorToSpikeDetector.cs:38-65` is the insight reader. It walks `collector.Spikes`, sums `w.PerModCatMs[modOffset + c]` over categories per mod. With §4.7 the in-RAM `SpikeWindow.PerModCatMs` is *unchanged* (still `float[]`); only the persisted `SpikeWindowRow.PerModCatMsBlob` changes. The insight detector reads the in-RAM struct, not the persisted row, so this layer is unaffected.

`SpikesTab.cs:92-330` reads `collector.Spikes` similarly. Same insulation as the insight detector — only the persisted row shape changes, the in-RAM struct is identical.

The `EventAggregator.cs` and `ProfilerDatabase.cs` references in the grep above are the writer-thread queue and the database registration; no spike-shape logic in those files.

### 5.3 Sibling: `StallDetector`

`StallDetector` consumes `Baseline.TickPeriodMsMedian` and `Baseline.TickPeriodMsMad`. The §4.5 incremental baseline maintenance changes those scalars from "full-recompute" to "incremental". Stall detection is downstream of the same fix; both detectors benefit from the same change.

Cross-system invariant: any change to `Baseline.Recompute` semantics that the spike detector tolerates must also be tolerated by the stall detector. The MAD-recompute-every-K-ticks lag is bounded; both detectors are robust to it.

### 5.4 Test surfaces

`Tests/BaselineTests.cs`, `Tests/Persistence/PersistenceRoundTripTests.cs`, `Tests/Persistence/PersistenceBenchmarkTests.cs` are the existing harness. §4.5 needs a new `BaselineIncrementalTests`; §4.7 needs a `SpikeWindowRowSchemaMigrationTests`; §4.4 needs a `SpikeDetectorPoolTests`. The benchmark suite already has the enqueue-latency micro-bench; add `BaselineRecomputeBench` and `SpikeDetectorOnTickBench`.

---

## 6. Prioritised order

The order is shaped by three factors: **value** (how much per-tick cost / per-session storage the change reclaims), **risk** (how much downstream surface it touches), and **invariant alignment** (whether the change closes a per-tick-allocation hole that Invariant 2 forbids).

| Rank | Change | Value | Risk | Invariant alignment | Sequencing |
|---|---|---|---|---|---|
| 1 | **§4.5 Incremental baseline maintenance** | High — biggest per-tick win in the subsystem | Medium — requires drift-guard test | Direct (per-tick CPU budget) | Land first; downstream changes inherit a cheaper baseline |
| 2 | **§4.3 Power-of-two ring sizes** | Medium — 20–50 ns/tick on `Push` | Small — memory grows ~14 % | Aligned (no divides on hot path) | Independent; can land in parallel with §4.5 |
| 3 | **§4.4 Pool-backed snapshot slots** | Low absolute, high invariant value | Small-medium — exposes `PeekWriteSlot` | **Critical** — closes the last per-tick allocation hole | Land after §4.3 (it depends on stable slot identity via the buffer); can land before or after §4.5 |
| 4 | **§4.7 BSON shape — flat blob, float width** | High — ~50 KB/session reclaimed | Medium — schema bump, two reader paths | Aligned (storage budget) | Land after the in-RAM changes are stable; affects only persistence |
| 5 | **§4.8 DrainSpikes allocation reduction** | Medium — fewer GC objects at session-end | Low-medium — touches contributor shape | Aligned (session-end stall reduction) | Land with §4.7 (shared schema bump) |
| 6 | **§4.2 `Span<float>.CopyTo` for snapshot copy** | Low absolute, strictly-better | Trivial | Aligned | Can land any time; trivial diff |
| 7 | **§4.10 Lazy worst-tick re-capture** | Low typical, material for long spikes | Low | Aligned | Land after §4.4 (pool gives stable target buffer) |
| 8 | **§4.6 Struct enumerator on `SpikeWindowsView`** | Latent — zero today, future-proof | Trivial | Aligned | Any time |
| 9 | **§4.1 OnTick fast-path** | Tiny | Trivial | Aligned | Any time |
| 10 | **§4.9 Surface tightening** | Latent | Trivial | Aligned | Any time |
| 11 | **§4.11 Defensive test for WorstTick-aged-out fallback** | Coverage — not perf | Trivial | Defensive | After §4.10 |

### Recommended landing sequence

1. **Round 1 — invariant + per-tick CPU.** §4.3 + §4.4 + §4.5 + §4.2. These close the last per-tick allocation hole, eliminate the per-tick divides, drop the per-tick baseline cost ~4×, and vectorise the snapshot copy. All in-RAM; no persistence-format risk.
2. **Round 2 — persistence.** §4.7 + §4.8. Schema bump, BSON shrink, drain-allocation reduction. Carries the schema-migration test surface.
3. **Round 3 — surface + latent-alloc cleanup.** §4.1 + §4.6 + §4.9 + §4.10 + §4.11. Low risk, can be batched.

After Round 1, the per-tick spike subsystem is zero-allocation and the baseline cost is dominated by the histogram-update cost (a few array ops). After Round 2, the per-session spike-collection storage drops from ~75–180 KB to ~25–50 KB. After Round 3, the surface is future-proofed for additional readers and longer windows.

### Out of scope for v0.6

- **Switching to P² or t-digest.** §3.3's verdict: not a drop-in for the sliding-window form. Park for v0.7 as a possible "all-session" supplementary baseline display.
- **Sliding-window sorted structures.** Skip lists, order-statistic trees, etc. — overkill for n = 1800 with a fixed bucket histogram already in place.
- **SIMD bucket pass.** The histogram bump is data-dependent (the bucket index is derived from each input), which defeats SIMD scatter — current dispatch lacks AVX-512 scatter on Apple silicon entirely. Skip.
- **Reduce spike ring capacity.** Forbidden by the no-cuts rule. The 50-window cap is the documented contract; if anything it grows in a future milestone, never shrinks.

### Invariant audit per change

| § | Inv 1 (read-only) | Inv 2 (overhead) | Inv 3 (descriptive) | Inv 4 (abort-clean) | Inv 5 (no mod-specific) |
|---|---|---|---|---|---|
| 4.1 | ✓ | improves | ✓ | ✓ | ✓ |
| 4.2 | ✓ | improves | ✓ | ✓ | ✓ |
| 4.3 | ✓ | improves | ✓ | ✓ | ✓ |
| 4.4 | ✓ | **closes alloc hole** | ✓ | ✓ | ✓ |
| 4.5 | ✓ | **dominant improvement** | ✓ | drift-guard fallback aligns | ✓ |
| 4.6 | ✓ | improves (latent) | ✓ | ✓ | ✓ |
| 4.7 | ✓ | improves (storage) | ✓ | schema-2 migration handles old data | ✓ |
| 4.8 | ✓ | improves | ✓ | ✓ | ✓ |
| 4.9 | ✓ | improves (latent) | ✓ | ✓ | ✓ |
| 4.10 | ✓ | improves on long windows | ✓ | fallback to most-recent snapshot when aged out | ✓ |

No change reduces capture coverage, no change introduces normative UI copy, no change touches game state, no change names a specific mod. The pass is invariant-clean by construction.

---

## 7. References

### 7.1 Project-internal sources

- `Profiling/SpikeDetector.cs` — the detector implementation, 274 lines.
- `Profiling/PerTickAttributionRing.cs` — the per-tick ring, 254 lines.
- `Profiling/RingBuffer.cs` — the generic ring buffer, 121 lines.
- `Profiling/Baseline.cs` — median + MAD over the rolling history, 199 lines.
- `Profiling/MetricCollector.cs` — the per-tick orchestrator that drives the detector (key lines: 154, 159, 243, 246, 285, 450).
- `Profiling/Persistence/Records/SpikeWindowRow.cs` — the persisted spike row.
- `Profiling/Persistence/Streams/SpikeStream.cs` — the stream that upserts spike rows.
- `Profiling/Persistence/SessionRecorder.cs:226-263, 582-613, 615-620` — the drain path.
- `Profiling/Persistence/SessionSummaryLogger.cs:32-67` — session-end summary reader.
- `Profiling/Insights/Detectors/PeakContributorToSpikeDetector.cs` — insight reader.
- `UI/Overlay/Tabs/SpikesTab.cs` — overlay reader.
- `context/notes/philosophy.md` — the no-cuts, universal posture.
- `context/notes/spikes-and-allocations-plan.md` — the shipped design with full rationale (the section §0 evidence ledger is reused as the citation root here).
- `context/perf-pass/baseline.md` — the v0.5 baseline numbers this dossier measures against.
- `context/systems/spike-detection.md` — the maintained system reference.

### 7.2 Robust statistics — median + MAD

- Jain & Chlamtac, *The P-Square Algorithm for Dynamic Calculation of Percentiles and Histograms without Storing Observations*, Communications of the ACM 28(10), 1985 — http://www.cse.wustl.edu/~jain/papers/psqr.htm
- Akinshin, *P² quantile estimator: estimating the median without storing values* — https://aakinshin.net/posts/p2-quantile-estimator-intro/
- Akinshin, *Caveats of using the median absolute deviation* — https://aakinshin.net/posts/mad-caveats/
- Adekeye, *Performance of median absolute deviation and some alternatives to MAD control charts for skewed and heavily tailed processes*, Quality and Reliability Engineering International, 2021 — https://onlinelibrary.wiley.com/doi/abs/10.1002/qre.2926
- *Confidence intervals for median absolute deviations*, arXiv 1910.00229 — https://arxiv.org/pdf/1910.00229
- *Median Absolute Deviation*, Wikipedia — https://en.wikipedia.org/wiki/Median_absolute_deviation
- *Anomaly Detection with MAD*, InfluxData blog — https://www.influxdata.com/blog/anomaly-detection-with-median-absolute-deviation/
- OpenSearch, *Median Absolute Deviation aggregation* — https://docs.opensearch.org/latest/aggregations/metric/median-absolute-deviation/

### 7.3 Streaming-quantile estimators

- *PSquare: practical quantiles*, Erthalion's blog — https://erthalion.info/2021/10/04/psquare/
- *Frugal Streaming for Estimating Quantiles*, arXiv 1407.1121 — https://arxiv.org/pdf/1407.1121
- *Theory meets Practice at the Median: a worst case comparison of relative-error quantile algorithms*, arXiv 2102.09299 — https://arxiv.org/pdf/2102.09299
- Karnin, Lang, Liberty, *Optimal Quantile Approximation in Streams* (KLL sketch), FOCS 2016.
- Chen, Lambert, Pinheiro, *Incremental Quantile Estimation for Massive Tracking*, KDD 2000.
- rfrenoy/psquare (Python reference) — https://github.com/rfrenoy/psquare

### 7.4 Small-array sorting (preserved for future use, not active in v0.6)

- Bingmann et al., *Engineering faster sorters for small sets of items*, Software: Practice and Experience 51(7), 2021 — https://onlinelibrary.wiley.com/doi/full/10.1002/spe.2922
- AndrDm/SortingNetworks-fast: fastest CPU SIMD (SSE4) sorting networks for small integer arrays — https://github.com/AndrDm/SortingNetworks-fast
- Sedgewick & Wayne, *Quicksort*, algs4 — https://algs4.cs.princeton.edu/23quicksort/
- *Introsort*, Wikipedia — https://en.wikipedia.org/wiki/Introsort
- *Worst-Case Efficient Sorting with QuickMergesort*, arXiv 1811.00833.
- .NET runtime, `System.Array.Sort` — https://learn.microsoft.com/en-us/dotnet/api/system.array.sort?view=net-8.0

### 7.5 .NET 8 perf primitives — Span, ArrayPool, stackalloc, memmove

- Sitnik, *Span* — https://adamsitnik.com/Span/
- *Memory Management Masterclass in .NET: Stack vs Heap, Span, Memory, and ArrayPool* — https://developersvoice.com/blog/dotnet/c-sharp-high-performance-memory-management/
- *Dos and Don'ts of stackalloc*, Random Thoughts — https://vcsjones.dev/stackalloc/
- *C# Memory Spans and Performance-Critical Code* — https://dev.to/chakewitz/c-memory-spans-and-performance-critical-code-219l
- *SpanOwner<T>*, Microsoft Learn — https://learn.microsoft.com/en-us/dotnet/communitytoolkit/high-performance/spanowner
- Peshkov, *The BIG performance difference between ArrayPools in .NET* — https://medium.com/@epeshk/the-big-performance-difference-between-arraypools-in-net-b25c9fc5e31d

### 7.6 LiteDB / BSON

- LiteDB, *BsonDocument* — https://www.litedb.org/docs/bsondocument/
- LiteDB, *Data Structure* — https://www.litedb.org/docs/data-structure/
- LiteDB, *Object Mapping* — https://www.litedb.org/docs/object-mapping/
- BSON spec, *bsonspec.org* — http://bsonspec.org/spec.html (the canonical type-byte / key-name / payload encoding rules cited in §1.8).

### 7.7 Benchmark methodology

- BenchmarkDotNet — https://benchmarkdotnet.org/articles/configs/diagnosers.html
- BenchmarkDotNet issue #723 (per-thread allocation diagnoser) — https://github.com/dotnet/BenchmarkDotNet/issues/723
- dotnet/runtime issue #17891 (Jan Kotas on `GetAllocatedBytesForCurrentThread` cost) — https://github.com/dotnet/runtime/issues/17891

---

*End of dossier.*
