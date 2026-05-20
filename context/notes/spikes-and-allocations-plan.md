# Spikes + Allocations Plan — From smoothed averages to per-tick attribution

> **Status (2026-05-20): SHIPPED — preserved as historical research record.** SpikeDetector, PerTickAttributionRing, SpikesTab, per-mod allocation tracking (EnterCpuAlloc/LeaveCpuAlloc IL emission), MEM/BOTH overlay pill all shipped in 08dd5eb, 45baf02, f32d33d. FlushSpikes at world unload landed in audit round 77a99d2. Canonical reality: systems/spike-detection.md and systems/allocation-tracking.md.
>
> Read the system files for current reality; this plan is the design brief that shipped, kept for the rationale.


> Scope: extend the ILHook backend with **per-mod allocation tracking** and surface a **spike feed** that exposes the discrete moments where tick time exceeded a rolling baseline. Both features share the same underlying change — raw per-tick per-mod attribution retained in a ring buffer rather than smoothed away on each `EndTick`. Targets tModLoader 1.4.4 on .NET 8, MonoMod.RuntimeDetour 25.3.2, Mono.Cecil 0.11.6. Honours all four Project Invariants.

---

## 0. Research evidence ledger

Every claim about the .NET 8 / MonoMod / Cecil surface is cited; every assumption that cannot be verified at design time is flagged for the in-game validation step in §13.

| Claim | Evidence |
|---|---|
| `System.GC.GetAllocatedBytesForCurrentThread()` exists on .NET 8 with signature `public static long GetAllocatedBytesForCurrentThread()` | Microsoft Learn, [.NET 8 API ref](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-8.0). The "Other Supported Versions" list explicitly enumerates `net-8.0` alongside net-5/6/7/9/10/11. |
| The counter measures *allocated* bytes, not *live* heap: GC compaction does not reduce it | Same Learn page, Remarks: *"returns the total number of bytes allocated on the managed heap during the lifetime of a thread, not the total number of bytes that have survived garbage collection."* |
| Native allocations (P/Invoke, pinned native buffers, interop) are not counted | Same Learn page, Remarks: *"The returned value also does not include any native allocations."* |
| The counter is per-thread, monotonic since thread start | Same Learn page: *"the total number of bytes allocated to the current thread since the beginning of its lifetime."* |
| BenchmarkDotNet uses this API as its primary allocation-measurement primitive on .NET Core | [BenchmarkDotNet diagnosers docs](https://benchmarkdotnet.org/articles/configs/diagnosers.html); discussion in [BDN issue #723](https://github.com/dotnet/BenchmarkDotNet/issues/723) confirms it is the per-thread allocation source. |
| The implementation is an FCall whose body reads a counter the allocator already maintains per thread | [.NET runtime issue #17891](https://github.com/dotnet/runtime/issues/17891) — Jan Kotas, the .NET allocator owner: *"The GC allocator was keeping track of amount allocated per thread already."* Implies the FCall is a structure-load, not a sweep. |
| Per-call cost is not analytically derivable | The API is documented but the runtime does not publish a per-call cost. The dotnet/runtime discussion threads emphasise the API is "essentially free" relative to the allocator path, but no nanosecond figure is published. **This is the single uncertainty the plan resolves empirically in step 7 below — a benchmark loop on the dev machine produces the figure before the IL emission lands.** |
| Async-method tracking caveat: the counter follows the calling thread, not the logical async chain | [Microsoft Q&A discussion #71530](https://github.com/dotnet/runtime/discussions/71530) — *"GetAllocatedBytesForCurrentThread … is only measuring the calling thread"* in the context of async methods. For our use case (synchronous hook bodies running on the game's update thread or draw thread), this matches what we want: we attribute to whichever thread is executing the hook. |
| `GC.GetTotalPauseDuration()` (already in `MetricCollector`) is .NET 5+, monotonic since process start, returns `TimeSpan` | Existing `MetricCollector.GcPauseMilliseconds()` already depends on it; [.NET 8 API ref](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalpauseduration). No change needed here. |
| Median Absolute Deviation (MAD) is the robust-statistics alternative to "n× of mean" for spike detection — robust to a small fraction of outliers, no Gaussian assumption | [InfluxData: Anomaly Detection with MAD](https://www.influxdata.com/blog/anomaly-detection-with-median-absolute-deviation/); [Hampel Filter overview](https://medium.com/data-and-beyond/outlier-detection-in-r-hampel-filter-for-time-series-15ca7d166067) describes the median ± k·MAD form, k = 3 as a common threshold. |
| The IL prologue can append `call System.GC::GetAllocatedBytesForCurrentThread()` immediately before our `call ProbeStack.Enter` and the value stays available as the next IL stack slot | MonoMod.Cil `ILCursor.Emit(OpCodes.Call, MethodInfo)` handles module import; the returned `long` is just a stack value, callable into any static long-taking signature. Verified against existing `ApplyTimingWrap` in `ILHookInterceptor.cs:452–547` which already uses the same emit shape for the `Enter`/`Leave` static calls. |

Two findings are load-bearing and worth surfacing here:

1. **The allocator already keeps the per-thread counter.** The FCall is a read, not a computation, so the *floor* on its cost is "near a Stopwatch read." That is consistent with §13's hypothesis that two GC reads add ~30–50 ns per hook call, the same order as the existing Stopwatch pair. Whether that holds is the only empirical uncertainty in the design.
2. **No removal API question this time.** Both features extend storage and the existing IL manipulator. There is no new disposal contract — the existing `Mod.Unload` → `ILHookInterceptor.Uninstall()` chain covers everything.

---

## 1. Viability verdict

**Both features are doable. Both ship together. Spike feed first, then allocations.**

The two features are siblings, not independent epics. They share three pieces of infrastructure:

```
                ┌───────────────────────────────────────────┐
                │   raw per-tick per-mod ring buffer        │ <─ new
                │   (no smoothing, fixed memory budget)     │
                └────────────────────┬──────────────────────┘
                                     │ reads
              ┌──────────────────────┼──────────────────────┐
              ▼                      ▼                      ▼
   ┌──────────────────┐   ┌───────────────────┐   ┌───────────────────┐
   │ spike detector   │   │ existing smoothed │   │ session log       │
   │ (new)            │   │ EMA + 30s average │   │ (drill-downs)     │
   └────────┬─────────┘   └───────────────────┘   └───────────────────┘
            │ records
            ▼
   ┌──────────────────┐
   │ spike ring (50)  │ <─ new
   │ + UI panel       │
   └──────────────────┘
```

Both features need raw per-tick attribution. Building only one of them and then layering the other on later wastes a redesign of the ring buffer; building both together is the cheaper path.

### Risks the plan must address

| ID | Risk | Trigger | Mitigation |
|---|---|---|---|
| **R1** | The IL allocation-read pair (`Enter`-side + `Leave`-side `GC.GetAllocatedBytesForCurrentThread`) adds enough overhead to violate Invariant 2's Lite-mode <1% budget. | Two extra FCalls per hook in the hot path, fired thousands of times per tick. | Make allocation tracking **mode-gated**: off in Lite, on in Standard and Deep. The ILHook manipulator emits the allocation reads behind a `static readonly bool _trackAllocations` constant the JIT folds; switching modes requires a re-emit (full uninstall + reinstall), but mode switches are user-initiated and rare. See §6. |
| **R2** | Raw per-tick per-mod ring buffer memory budget grows too large for the player's machine. | Naïve sizing: `modCount × categoryCount × historyTicks × 8 bytes × 2 metrics`. For 100 mods × 7 categories × 1800 ticks × 8 × 2 = 20 MB. | Store **per-mod totals only**, not per-mod-per-category, in the raw ring buffer. The spike snapshot still gets a category breakdown by reading the current-tick accumulator at spike-detection time, not a 30-second-old breakdown. Memory: 100 × 1800 × 8 × 2 = 2.9 MB. See §4. |
| **R3** | Spike storms — a sustained slow period producing hundreds of consecutive spike records. | A boss fight that holds 25 FPS for 2 minutes is 7,200 ticks above baseline. | Adjacent ticks both qualifying as spikes are coalesced into a single spike *window* with start/end ticks and worst-frame data inside it. Storage cap of 50 windows hard-caps the ring; eviction is oldest-first. See §5. |
| **R4** | Allocation attribution to the wrong mod when a hook calls into shared library code. | `Mod A`'s hook calls `Mod B`'s exported helper which allocates. Bytes are billed to whichever stack frame is currently on top of `ProbeStack`. | Document as expected: the attribution rule is "whose hook is the outermost active frame". This matches CPU attribution — there is no different rule we could apply without far heavier instrumentation (call-graph capture, Deep-mode territory). Surface this in `context/notes` and in the overlay tooltip. |
| **R5** | Cross-thread allocation: a hook fires on the draw thread, the counter is for the draw thread. | tModLoader's `*Draw*` hooks. | This is correct behaviour. Document it. The thread the hook runs on is also the thread that did the allocation; per-thread is the right granularity. See §3. |
| **R6** | JIT compilation of mod methods on first call shows as an apparent spike + allocation surge for that mod. | First tick a hot path is exercised, the JIT compiles the body. | This is real cost, not noise — the user genuinely paid the JIT pause. But it deserves a flag: spikes within the first ~10 seconds of a world load are badged `warming` so the user knows not to over-interpret them. See §5. |
| **R7** | LOH (Large Object Heap) allocations count toward the byte total but their GC pause impact is disproportionate. An 85 KB allocation and a 90 KB allocation read identically from `GetAllocatedBytesForCurrentThread` but the second one goes to LOH and matters far more. | Mods allocating arrays ≥ 85 KB. | Out of scope for this plan. Document as a Deep-mode follow-up; the current allocation column is "total bytes", not "GC pressure points". See §14. |
| **R8** | Spike de-dup window collapses two genuinely separate spikes that happen near each other. | A short stall, a brief recovery, then another stall — coalesce or not? | The de-dup rule is "consecutive ticks both above threshold". If a single tick comes back below threshold, the window closes. A genuine "two close spikes" case produces two windows. See §5. |

None of these are blockers. R1 is the only one that demands measurement before the design is locked.

### Honest uncertainties at the design stage

- **Per-call cost of `GetAllocatedBytesForCurrentThread`.** Cited evidence says "essentially free relative to the allocator", but no nanosecond figure is published. We resolve this in step 7 with a tight benchmark loop before the IL emission lands.
- **Whether boxing of value types is reflected in the counter.** Documentation says "the managed heap" without distinguishing boxed value types from reference types. Boxing produces a heap object, so a-priori the bytes should be counted. We verify with a synthetic hook (`object o = 42;` produces ~24 bytes) as part of step 7.
- **Behaviour during a concurrent background GC.** A background GC running while a hook executes does not pause the allocator counter (the counter is a write-side accumulator), so we expect no correlated noise. Unverified at design time; the empirical step looks for surprising variance.

---

## 2. The data model — what is a "per-tick per-mod snapshot"

Today's storage (verified against `MetricCollector.cs:67–89` and `PerModAttribution.cs:54–58`):

```
PerModAttribution._ticksByBackend[backend][mod * CategoryCount + cat]   // long, reset each tick
PerModAttribution._hookTicksByBackend[backend][hookId]                  // long, reset each tick

MetricCollector._perModRawMs[cell]                  // double, this tick's harvest
MetricCollector._perModSmoothedMs[cell]             // double, EMA
MetricCollector._perModAverageMs[cell]              // double, 30s average
MetricCollector._perModHistoryMs[cell * historyCapacity]  // double, ring of per-cell history
MetricCollector._perModRollingMs[cell]              // double, running sum for the average
```

There is already a per-cell history ring (`_perModHistoryMs`) — but it is sized **per category cell**, not per mod. The categories are stored sliced-by-cell, not packed-by-tick, which is awkward for "show me the breakdown for tick N." We need a structure indexed *by tick*, not by cell.

### The new storage

Add a second ring whose entries are "this tick's per-mod totals" — one float per mod per tick (not per category). For category breakdown of a specific spike, we read from a separate small ring of category-resolution snapshots only at spike-detection time. The asymmetry reflects how the data is read:

- **Every tick, every mod, by-mod total** → spike detection scans the totals to compute the baseline and compare. Needs full 30-second window.
- **At spike-detection time, by-mod-by-category for that tick** → drill-down on the spike. Needs only "the most recent N ticks" so we can snapshot the category breakdown when a spike fires.

```csharp
internal sealed class PerTickAttributionRing
{
    // Hot path: one float per mod per tick. modCount × historyTicks × 4 bytes.
    // 100 mods × 1800 ticks × 4 = 720 KB (CPU ms) — easily affordable.
    private readonly float[] _perModMs;          // [tick * modCount + mod]

    // Second metric, mirror of the first. 720 KB if allocation tracking is on.
    private readonly float[]? _perModAllocBytes; // [tick * modCount + mod]

    // Category breakdown stored only for the last K ticks (K small — say 120 ≈ 2s)
    // because spike drill-down only needs the spike tick, not the whole 30s window.
    // 100 mods × 7 cats × 120 × 4 = 336 KB. Tiny.
    private readonly float[] _perModCatMs;       // [tick * (modCount * 7) + ...]
    private readonly float[]? _perModCatAllocBytes;

    private readonly int _modCount;
    private readonly int _historyTicks;   // ≈ 1800
    private readonly int _categorySnapshotTicks; // ≈ 120
    private int _writeTick;               // monotonic
}
```

### Memory budget (worst-case 100-mod stack, both metrics on)

| Buffer | Size formula | Bytes |
|---|---|---|
| `_perModMs` | 100 × 1800 × 4 | 720 KB |
| `_perModAllocBytes` | 100 × 1800 × 4 | 720 KB |
| `_perModCatMs` | 100 × 7 × 120 × 4 | 336 KB |
| `_perModCatAllocBytes` | 100 × 7 × 120 × 4 | 336 KB |
| **Total** | | **≈ 2.1 MB** |

Plus the existing `_perModHistoryMs` (which is sized identically already — 100 × 7 × 1800 × 8 = 5 MB at `double`). The new ring stores `float` instead of `double`, because the precision needed for spike detection and the UI is "ms to 3 decimal places", which a `float` carries comfortably.

### Why `float`, not `double`

- Storage is bandwidth-heavy. Halving each entry doubles cache density.
- Spike detection compares against a rolling baseline (also computed in `float`); error compounds only across the multiply, not across the full history.
- The existing `_perModHistoryMs[cell * historyCapacity]` uses `double` because it carries a running sum used in the rolling average. We do not need a running sum on the raw ring — we only need point reads.

### Alternative considered: keep `double`, drop allocation arrays from the always-on path

Same total memory (≈ 2.1 MB) but loses the "Standard mode shows allocations" feature. Rejected: the user explicitly asked for "allocations and spikes as the two most important things"; both belong in Standard.

---

## 3. The allocation API — what we will call and where

### The call shape

The only API we use is:

```csharp
public static long GetAllocatedBytesForCurrentThread();   // System.GC, .NET 8
```

Read at the start and end of every hook body. The delta is what the body allocated on its thread. The IL manipulator emits:

```il
// PROLOGUE (before original first instruction):
ldc.i4    <hookId>
call      void ProbeStack::EnterCpu(int32)        // existing
call      int64 System.GC::GetAllocatedBytesForCurrentThread()
call      void ProbeStack::EnterAlloc(int64)      // new
```

The two static calls are folded into a single combined entry point so that in Lite mode (allocation tracking off) the manipulator emits *only* `EnterCpu` and skips the alloc reads entirely:

```csharp
// One call site, two implementations chosen at install time.
internal static class ProbeStack
{
    public static void EnterCpu(int hookId) { /* existing implementation */ }

    // Standard / Deep only — JIT folds away when the static readonly flag is false
    // and the manipulator skips emitting it entirely.
    public static void EnterCpuAlloc(int hookId, long allocBytesAtEnter)
    {
        // push (hookId, startTicks, allocBytesAtEnter)
    }

    public static void LeaveCpu()         { /* existing */ }
    public static void LeaveCpuAlloc()    { /* read alloc again, diff, credit */ }
}
```

The frame struct grows by one field:

```csharp
internal struct Frame
{
    public int  HookId;
    public long StartTicks;
    public long StartAllocBytes;   // 0 in Lite mode (allocation tracking off)
}
```

### What `GetAllocatedBytesForCurrentThread` counts on .NET 8

Confirmed from the Learn docs (see §0):

- **Counts:** every managed-heap allocation by the current thread, including
  - reference-type object allocations (`new List<int>()` → ~32 bytes for the list header + array allocation when capacity is set)
  - value-type boxing (`object o = 42;` → ~24 bytes for the boxed `int`)
  - implicit allocations (closure capture, iterator state machine, async state machine)
  - LOH allocations (≥ 85 000 bytes) — counted toward the byte total, even though they go to a different generation
- **Does not count:**
  - native/unmanaged allocations (P/Invoke `Marshal.AllocHGlobal`, native interop)
  - stack allocations (`Span<T>` over `stackalloc`, locals)
  - reused objects (pool hits — by definition no allocation occurred)
- **Unaffected by:**
  - GC compaction (it is an allocation counter, not a heap-size measurement)
  - GC pauses (the counter is updated on the allocator path, not on GC traversal)

### Threading

The counter is per-thread. Three thread classes execute hooks:

1. **The game's update thread** — every per-tick hook. The dominant case.
2. **The draw thread** — `*Draw*` hooks during render. Separate counter, but a draw-thread hook's allocations are properly attributed to whoever was in the draw-thread `ProbeStack`.
3. **Mod-spawned background threads** — `Task.Run`-style code. Same story: per-thread counter, separate `ProbeStack` (since `[ThreadStatic]`), works correctly.

No cross-thread aggregation is needed *for the per-call delta*. The total session-level allocation per mod is computed by summing the per-tick deltas across all threads — same shape as CPU attribution today.

### What the read costs

This is the single empirical uncertainty in the design (see §0). The plan validates it in step 7 below — a tight loop on the dev machine produces a nanoseconds-per-call figure before the IL emission ships. The hypothesis is:

```
Stopwatch.GetTimestamp()                ≈ 15–25 ns   (well-known)
GC.GetAllocatedBytesForCurrentThread()  ≈ 5–15 ns    (hypothesis — to be measured)
```

If the hypothesis holds, the per-hook addition is ~10–30 ns on top of the existing ~30–50 ns CPU instrumentation. For a session that fires 50 000 hook calls per tick at 60 ticks/s, the budget impact is `50 000 × 60 × 20 ns = 60 ms/s = 6%` worst case — at the very top of Standard mode's 2–4% budget. **If the measurement comes back outside that range, the design switches to "sample-mode allocation tracking"**: read the counter once per N hooks rather than every hook. The accuracy degrades to "average per call", which is still useful for ranking but loses the per-spike attribution. The plan picks the right path at step 7, not now.

### Why not `GC.GetTotalAllocatedBytes(precise: true)`

Process-wide, not per-thread. To attribute to a single hook we would need to know that no other thread allocated during the hook body — which is hostile to make true. Per-thread is the only sound primitive.

### Why not `GC.GetGCMemoryInfo()`

Returns heap *snapshot* info (committed bytes, fragmentation, generation sizes) — not allocation tracking. Wrong tool.

---

## 4. The raw per-tick ring buffer

### Storage

```csharp
namespace PerformanceProfiler.Profiling;

/// <summary>
/// Holds raw per-tick per-mod attribution for spike drill-down. The existing
/// MetricCollector smooths and 30-second-averages everything; this ring keeps
/// the unsmoothed signal so we can answer "what did mod X look like at tick N?"
/// when N is a spike.
/// </summary>
public sealed class PerTickAttributionRing
{
    private readonly float[] _perModMs;          // 30s window, every tick
    private readonly float[]? _perModAllocBytes; // null when allocation tracking is off

    // Category-resolution snapshots, only for the last ~2 seconds. Spike
    // drill-down reads these at detection time; we never need 30s of category
    // breakdown.
    private readonly float[] _perModCatMs;
    private readonly float[]? _perModCatAllocBytes;

    private readonly int _modCount;
    private readonly int _historyTicks;
    private readonly int _categorySnapshotTicks;
    private int _writeTick;            // monotonic, wraps via modulo at read

    public PerTickAttributionRing(int modCount, int historyTicks, int categorySnapshotTicks, bool trackAllocations)
    {
        _modCount = modCount;
        _historyTicks = historyTicks;
        _categorySnapshotTicks = categorySnapshotTicks;
        _perModMs = new float[modCount * historyTicks];
        _perModCatMs = new float[modCount * PerModAttribution.CategoryCount * categorySnapshotTicks];

        if (trackAllocations)
        {
            _perModAllocBytes = new float[modCount * historyTicks];
            _perModCatAllocBytes = new float[modCount * PerModAttribution.CategoryCount * categorySnapshotTicks];
        }
    }

    /// <summary>
    /// Writes one tick's row from the harvest buffers. Called from MetricCollector.EndTick
    /// AFTER the smoothing pass. Allocation-free.
    /// </summary>
    public void Push(double[] perModCatMs, double[]? perModCatBytes)
    {
        int tickSlot = _writeTick % _historyTicks;
        int catTickSlot = _writeTick % _categorySnapshotTicks;
        int catCount = PerModAttribution.CategoryCount;

        int byTickBase = tickSlot * _modCount;
        int byCatTickBase = catTickSlot * _modCount * catCount;

        for (int mod = 0; mod < _modCount; mod++)
        {
            float modTotalMs = 0f;
            float modTotalBytes = 0f;
            int catBase = mod * catCount;

            for (int c = 0; c < catCount; c++)
            {
                float ms = (float)perModCatMs[catBase + c];
                _perModCatMs[byCatTickBase + catBase + c] = ms;
                modTotalMs += ms;

                if (perModCatBytes != null && _perModCatAllocBytes != null)
                {
                    float b = (float)perModCatBytes[catBase + c];
                    _perModCatAllocBytes[byCatTickBase + catBase + c] = b;
                    modTotalBytes += b;
                }
            }

            _perModMs[byTickBase + mod] = modTotalMs;
            if (_perModAllocBytes != null)
            {
                _perModAllocBytes[byTickBase + mod] = modTotalBytes;
            }
        }

        _writeTick++;
    }

    /// <summary>Per-mod ms total at a specific past tick. Returns 0 if the tick is outside the window.</summary>
    public float GetPerModMs(long tickIndex, int modId)
    {
        long ago = _writeTick - 1 - tickIndex;
        if (ago < 0 || ago >= _historyTicks) return 0f;
        int slot = (int)(tickIndex % _historyTicks);
        return _perModMs[slot * _modCount + modId];
    }

    /// <summary>Per-mod-per-category snapshot at a specific past tick, within the category window.</summary>
    public bool TryGetCategorySnapshot(long tickIndex, Span<float> destinationMs, Span<float> destinationBytes)
    {
        long ago = _writeTick - 1 - tickIndex;
        if (ago < 0 || ago >= _categorySnapshotTicks) return false;
        int catCount = PerModAttribution.CategoryCount;
        int slot = (int)(tickIndex % _categorySnapshotTicks);
        int baseIdx = slot * _modCount * catCount;
        int n = _modCount * catCount;

        for (int i = 0; i < n && i < destinationMs.Length; i++)
        {
            destinationMs[i] = _perModCatMs[baseIdx + i];
        }
        if (_perModCatAllocBytes != null && destinationBytes.Length > 0)
        {
            for (int i = 0; i < n && i < destinationBytes.Length; i++)
            {
                destinationBytes[i] = _perModCatAllocBytes[baseIdx + i];
            }
        }
        return true;
    }

    public long CurrentTickIndex => _writeTick - 1;
}
```

### Memory comparison (table form)

| Configuration | Mods | History | Bytes |
|---|---:|---:|---:|
| Lite (no alloc, no category), 18-mod stack | 18 | 1800 | 18 × 1800 × 4 = **129 KB** |
| Standard (CPU + alloc, full snapshots), 18 mods | 18 | 1800 / 120 cat | 129 + 129 + (18 × 7 × 120 × 4) × 2 = **863 KB** |
| Standard, 100 mods (theoretical hard case) | 100 | 1800 / 120 cat | 720 + 720 + 336 + 336 = **2.1 MB** |
| Standard, 200 mods (modlist nightmare) | 200 | 1800 / 120 cat | **4.2 MB** |

Even the 200-mod case fits inside a hard 5 MB budget. The category snapshot window (`120 ticks = 2 seconds`) is the tunable; it can be cut to `60 ticks` if memory pressure becomes a real concern.

### Integration

The ring lives inside `MetricCollector` as a sibling of `_history`. `EndTick` already harvests `_perModRawMs`; after the smoothing pass, push that array (plus a parallel allocation harvest) into the ring.

```csharp
// MetricCollector.EndTick, near line 207:
PerModAttribution.HarvestInto(_perModRawMs, backendId: 0);
PerModAttribution.HarvestAllocationsInto(_perModRawBytes, backendId: 0);  // new
UpdateRollingAverage(_perModRawMs, _perModHistoryMs, _perModRollingMs, _perModAverageMs, _sampleSlot);
for (int i = 0; i < _perModSmoothedMs.Length; i++) { /* unchanged */ }

// New: push the raw snapshot into the per-tick ring.
_perTickRing.Push(_perModRawMs, _perModRawBytes);   // allocation-free
```

---

## 5. Spike detection

### Definition

A **spike** is one or more consecutive ticks where:

```
frameTimeMs >= baseline × thresholdMultiplier   AND   frameTimeMs >= absoluteFloorMs
```

The absolute floor (`= 5 ms`) prevents the detector firing on baseline-low ticks where a `0.1 ms → 1.0 ms` jump produces a meaningless "10×" ratio.

Default thresholds:
- `thresholdMultiplier = 2.0`
- `absoluteFloorMs = 5.0`
- `baseline = median over last 1800 ticks` (= the 30-second window already retained)

Both `thresholdMultiplier` and `absoluteFloorMs` are configurable via tModLoader's `ModConfig`.

### Why median, not exponential moving average

| Property | EMA | Median |
|---|---|---|
| Robust to outliers | No — a single 100 ms tick lifts the EMA for seconds | Yes — median ignores the top 50% by construction |
| Computation cost per tick | O(1) (one multiply + add) | O(N log N) naïve, O(log N) with a maintained heap, O(N) with bucket sort for fixed N |
| Memory | 1 float | The 1800-tick window (already retained) |
| Sensitivity to bias | EMA can drift up if spikes are frequent (counter-intuitively suppressing detection) | Median holds even when 49% of ticks are spikes |

Median is the right primitive for the *purpose*. The cost is one-off per spike-candidate tick (only computed when `frameTimeMs > 2× rough EMA`), not every tick. Concretely:

```csharp
double EstimateBaselineMs(RingBuffer<TickFrame> history)
{
    // Fast path: smoothed EMA, computed every tick for the threshold pre-check.
    return _emaFrameMs;
}

double ExactBaselineMs(RingBuffer<TickFrame> history)
{
    // Only called when a candidate is detected. Bucket-sort-style histogram
    // over the 1800-tick window, log buckets of 0.5 ms width — bounded compute,
    // ~3μs amortised. Far below the per-tick budget.
    return Median(history);
}
```

Pre-check uses the EMA so the median path runs only when a frame already looks suspicious. False positives at the pre-check stage get filtered by the exact median test.

### Alternative considered: MAD (Median Absolute Deviation)

[MAD](https://www.influxdata.com/blog/anomaly-detection-with-median-absolute-deviation/) is the median of `|x_i − median|` and is the textbook robust scale estimator. A `median ± k·MAD` threshold (Hampel filter, k = 3) is the stricter alternative to `baseline × 2`.

| Test | Pros | Cons |
|---|---|---|
| `frame > 2× median` | Cheap, intuitive, the "2× baseline" framing the user uses in their words | Multiplier is arbitrary; on a stable workload `2×` is too coarse, on a noisy one it under-fires |
| `frame > median + 3·MAD` | Robust statistical foundation; adapts to workload noise | Requires computing both median and MAD; the "3·MAD" threshold is less intuitive than "2× average" |

**Decision: use `frame > 2× median` for the primary trigger, expose `MAD` in the session log as a side channel.** The user's mental model is "2× the average"; matching it is worth the looser statistical footing. MAD lands as a secondary score on each recorded spike (`spike.deviation_in_mads`) so power users can sort or filter on the more robust metric.

### Algorithm — single-pass spike windowing

```csharp
internal sealed class SpikeDetector
{
    private readonly RingBuffer<SpikeWindow> _windows = new RingBuffer<SpikeWindow>(capacity: 50);
    private double _emaFrameMs;                  // pre-check baseline
    private SpikeWindow? _openWindow;            // null = no spike currently active
    private int _ticksSinceWarmup;               // for R6: badge first ~10s as "warming"

    public void OnTick(TickFrame frame, MetricCollector collector)
    {
        // EMA pre-check (cheap)
        const double emaAlpha = 0.05;
        _emaFrameMs += emaAlpha * (frame.FrameTimeMs - _emaFrameMs);
        _ticksSinceWarmup++;

        bool candidate = frame.FrameTimeMs >= _emaFrameMs * 2.0
                      && frame.FrameTimeMs >= 5.0;

        if (!candidate)
        {
            CloseOpenWindow(frame);  // a sub-threshold tick closes any open window
            return;
        }

        // Exact check: compute median over the history. Only runs on candidates,
        // typically O(history) once per spike, not per tick.
        double medianMs = ExactMedian(collector.History);
        double absMad   = MedianAbsoluteDeviation(collector.History, medianMs);
        if (frame.FrameTimeMs < medianMs * 2.0) { CloseOpenWindow(frame); return; }

        // We have a real spike tick. Either extend the open window or open a new one.
        if (_openWindow == null)
        {
            _openWindow = new SpikeWindow
            {
                StartTick = frame.TickIndex,
                EndTick = frame.TickIndex,
                WorstFrameMs = frame.FrameTimeMs,
                WorstTick = frame.TickIndex,
                BaselineMs = medianMs,
                MadMs = absMad,
                Warming = _ticksSinceWarmup < 600,    // first 10 s @ 60 tps
                SnapshotTick = frame.TickIndex,
            };
            // Capture per-mod context for the opening tick.
            CapturePerModSnapshot(_openWindow, collector);
        }
        else
        {
            _openWindow.EndTick = frame.TickIndex;
            if (frame.FrameTimeMs > _openWindow.WorstFrameMs)
            {
                _openWindow.WorstFrameMs = frame.FrameTimeMs;
                _openWindow.WorstTick = frame.TickIndex;
                _openWindow.SnapshotTick = frame.TickIndex;
                CapturePerModSnapshot(_openWindow, collector);
            }
        }
    }

    private void CloseOpenWindow(TickFrame frame)
    {
        if (_openWindow == null) return;
        _windows.Push(in _openWindow);   // ring evicts oldest if full
        _openWindow = null;
    }

    private void CapturePerModSnapshot(SpikeWindow w, MetricCollector collector)
    {
        // Reads PerTickAttributionRing for the worst tick in the window.
        // Captures top N mods by contribution + per-category breakdown for
        // each. Bounded size: at most 8 top-mod rows per snapshot.
        collector.PerTickRing.TryGetCategorySnapshot(
            w.WorstTick, w.PerModCatMs.AsSpan(), w.PerModCatBytes.AsSpan());
    }
}
```

```csharp
public struct SpikeWindow
{
    public long   StartTick;
    public long   EndTick;
    public long   WorstTick;
    public long   SnapshotTick;        // which tick's per-mod data is in PerModCatMs
    public double WorstFrameMs;
    public double BaselineMs;          // median at time of detection
    public double MadMs;               // median absolute deviation, robust-stats sidecar
    public bool   Warming;             // first ~10s of session

    // Inline per-mod-per-category snapshot for the worst tick.
    // Sized at construction by collector's mod count × category count.
    public float[] PerModCatMs;        // [modId * CategoryCount + cat]
    public float[] PerModCatBytes;     // null when allocation tracking is off

    // Context — biome / boss / event, populated later when events tab lands.
    public string? ContextSummary;     // e.g. "Cryogen Phase 2 · Sulphurous Sea"
}
```

### Spike storms (R3) — worked example

```
ticks  → 10000 10001 10002 10003 10004 10005 10006 10007 10008 10009 10010
frame  → 12 ms 9 ms  25 ms 28 ms 31 ms 18 ms 6  ms 4 ms  3 ms  29 ms 8 ms
spike? → F     F     T     T     T     T     F     F     F     T     F
window →             [ ........... window A ............ ]    [w B]
```

Two windows, three frames apart. The de-dup rule is "a sub-threshold tick closes the window"; the brief recovery at 10006–10008 closes Window A, and 10009 opens Window B. The spike feed shows two entries, each with its own worst-tick snapshot, instead of either ten entries (no de-dup) or one entry (over-aggressive coalesce).

### Ring capacity and eviction

`RingBuffer<SpikeWindow>(50)` matches the existing `RingBuffer<T>` primitive (already in the codebase). Once full, each new spike evicts the oldest one. This bounds memory at:

```
50 × (24 fixed + 100 mods × 7 × 4 × 2)  = 50 × ~5.6 KB = ~280 KB
```

For an 18-mod stack:

```
50 × (24 + 18 × 7 × 4 × 2)  = 50 × ~1 KB = ~50 KB
```

Acceptable.

---

## 6. The ILHook manipulator changes

### Today's emit (from `ILHookInterceptor.cs:520–525`)

```il
// Prologue, inserted before firstOriginal:
ldc.i4   <hookId>
call     void ProbeStack::Enter(int32)

// Finally handler at end of body:
call     void ProbeStack::Leave()
endfinally
```

### New emit — Lite mode (no allocation tracking)

Unchanged. The existing `Enter`/`Leave` static calls remain. Lite mode pays the cost it pays today; this is the explicit budget protection for R1.

### New emit — Standard / Deep mode (allocation tracking on)

```il
// Prologue, inserted before firstOriginal:
ldc.i4   <hookId>
call     int64 [System.Runtime]System.GC::GetAllocatedBytesForCurrentThread()
call     void ProbeStack::EnterCpuAlloc(int32, int64)

// Finally handler at end of body:
call     void ProbeStack::LeaveCpuAlloc()
endfinally
```

Two changes:

1. The prologue gains one `call` to `GC.GetAllocatedBytesForCurrentThread()`. Its result (a `long`) sits on the IL stack and is consumed by `EnterCpuAlloc`.
2. `EnterCpuAlloc` and `LeaveCpuAlloc` replace `Enter`/`Leave`. `LeaveCpuAlloc` reads `GetAllocatedBytesForCurrentThread()` again internally (not from the IL prologue side) and credits the diff.

The reason `LeaveCpuAlloc` reads the post-counter inside the method body rather than from the IL is that the finally handler has no value-stack inputs in this design — keeping `Leave` parameterless preserves the existing ExceptionHandler shape verbatim. The cost is one extra static call; the IL surface stays identical to today, which preserves the IL-correctness reasoning from `ILHook-migration-plan.md`.

### Which emit path runs

Decided at install time by `HookBackend.AllocationTracking` (new static, set from the active mode):

```csharp
public static bool AllocationTracking { get; private set; }

internal static void SetMode(HookBackendMode mode)
{
    Mode = mode;
    AllocationTracking = (mode == HookBackendMode.ILHookStandard || mode == HookBackendMode.ILHookDeep);
}
```

`ApplyTimingWrap` picks the prologue / handler shape from `AllocationTracking`:

```csharp
private static void ApplyTimingWrap(ILContext il, int hookId)
{
    // ... existing setup ...

    if (HookBackend.AllocationTracking)
    {
        // Prologue: hookId + (GC read pushed onto stack) + EnterCpuAlloc(hookId, bytes)
        c.Emit(OpCodes.Ldc_I4, hookId);
        c.Emit(OpCodes.Call, _gcGetAllocatedMethod);
        c.Emit(OpCodes.Call, _enterCpuAllocMethod);
    }
    else
    {
        c.Emit(OpCodes.Ldc_I4, hookId);
        c.Emit(OpCodes.Call, _enterCpuMethod);
    }

    // Finally handler:
    Instruction handlerStart = Instruction.Create(OpCodes.Call,
        il.Import(HookBackend.AllocationTracking ? _leaveCpuAllocMethod : _leaveCpuMethod));
    // ... rest unchanged ...
}
```

### Why not two parallel probe stacks

Option B from the brief — keep CPU and allocation as completely separate probe stacks — was considered. Rejected because:

- The Frame data is small (`int + long + long = 20 bytes`); the extra `long` per frame is a rounding error on the stack.
- A second stack doubles the `[ThreadStatic]` lookup cost. Two-field reads from one stack are cheaper than one-field reads from two.
- A second stack doubles the divergence-risk surface: if one probe pushes and the other doesn't, the stacks desync and recovery is hard. One stack means one source of truth.

### Mode switching at runtime

User flips Lite → Standard from the overlay's mode pill. Switching requires re-emitting the IL (the prologue shape changed):

1. `ILHookInterceptor.Uninstall()` disposes every existing `ILHook` (~100 ms for an 18-mod stack, fine because it is user-initiated).
2. `HookBackend.SetMode(newMode)` flips `AllocationTracking`.
3. `ILHookInterceptor.Install(...)` re-emits with the new prologue shape.
4. Allocation ring is reallocated with `trackAllocations: AllocationTracking`.

No per-tick cost from mode switching. The transition is visible to the user as a brief ~100 ms hitch.

---

## 7. PerModAttribution changes

### Storage extension

```csharp
public static class PerModAttribution
{
    // Existing
    private static long[][] _ticksByBackend = Array.Empty<long[]>();
    private static long[][] _hookTicksByBackend = Array.Empty<long[]>();

    // New: parallel arrays for allocation bytes. Same layout. Allocated only when
    // allocation tracking is on, so Lite mode pays zero memory cost.
    private static long[][] _bytesByBackend = Array.Empty<long[]>();
    private static long[][] _hookBytesByBackend = Array.Empty<long[]>();

    public static void Configure(int modCount, int backendCount, bool trackAllocations)
    {
        // ... existing CPU storage allocation ...
        if (trackAllocations)
        {
            _bytesByBackend = new long[backendCount][];
            _hookBytesByBackend = new long[backendCount][];
            for (int b = 0; b < backendCount; b++)
            {
                _bytesByBackend[b] = new long[cells];
                _hookBytesByBackend[b] = Array.Empty<long>();
            }
        }
    }
}
```

### Hot-path entry point

Add a sibling to `Add(...)` that credits both metrics at once. The existing single-metric `Add` stays for the Lite path:

```csharp
public static void Add(int backendId, int modId, int categoryId, int hookId,
                       long elapsedStopwatchTicks, long allocatedBytes)
{
    // Existing CPU credit (verbatim from current Add)
    if ((uint)backendId >= (uint)_ticksByBackend.Length) return;
    if ((uint)categoryId >= (uint)CategoryCount) return;

    long[] ticks = _ticksByBackend[backendId];
    int index = modId * CategoryCount + categoryId;
    if ((uint)index < (uint)ticks.Length) ticks[index] += elapsedStopwatchTicks;
    long[] hookTicks = _hookTicksByBackend[backendId];
    if ((uint)hookId < (uint)hookTicks.Length) hookTicks[hookId] += elapsedStopwatchTicks;

    // New: parallel byte credit, no-op if allocation tracking is off
    if (_bytesByBackend.Length == 0) return;
    long[] bytes = _bytesByBackend[backendId];
    if ((uint)index < (uint)bytes.Length) bytes[index] += allocatedBytes;
    long[] hookBytes = _hookBytesByBackend[backendId];
    if ((uint)hookId < (uint)hookBytes.Length) hookBytes[hookId] += allocatedBytes;
}
```

### Harvest

Mirror the existing CPU harvest path with a byte variant:

```csharp
public static void HarvestAllocationsInto(double[] destination, int backendId)
{
    if (_bytesByBackend.Length == 0 || (uint)backendId >= (uint)_bytesByBackend.Length)
    {
        Array.Clear(destination, 0, destination.Length);
        return;
    }
    long[] bytes = _bytesByBackend[backendId];
    int n = bytes.Length < destination.Length ? bytes.Length : destination.Length;
    for (int i = 0; i < n; i++)
    {
        destination[i] = bytes[i];   // raw bytes, no unit conversion
    }
}
```

### BeginTick

Already iterates all backends and clears arrays. Add a parallel clear of the byte arrays:

```csharp
public static void BeginTick()
{
    for (int b = 0; b < _ticksByBackend.Length; b++)
    {
        Array.Clear(_ticksByBackend[b], 0, _ticksByBackend[b].Length);
        Array.Clear(_hookTicksByBackend[b], 0, _hookTicksByBackend[b].Length);
    }
    for (int b = 0; b < _bytesByBackend.Length; b++)
    {
        Array.Clear(_bytesByBackend[b], 0, _bytesByBackend[b].Length);
        Array.Clear(_hookBytesByBackend[b], 0, _hookBytesByBackend[b].Length);
    }
}
```

---

## 8. ProbeStack changes

The frame grows by one long:

```csharp
internal struct Frame
{
    public int  HookId;
    public long StartTicks;
    public long StartAllocBytes;    // 0 when allocation tracking is off
}
```

Two new entry/exit methods sit alongside the existing `Enter`/`Leave`:

```csharp
public static class ProbeStack
{
    // Existing — Lite path
    public static void Enter(int hookId) { /* unchanged */ }
    public static void Leave()           { /* unchanged */ }

    // New — Standard/Deep path
    public static void EnterCpuAlloc(int hookId, long allocBytesAtEnter)
    {
        Frame[]? s = _stack;
        if (s == null) { s = new Frame[InitialCapacity]; _stack = s; }
        else if (_depth == s.Length)
        {
            Frame[] grown = new Frame[s.Length * 2];
            Array.Copy(s, grown, s.Length);
            s = grown; _stack = grown;
        }

        s[_depth].HookId = hookId;
        s[_depth].StartTicks = Stopwatch.GetTimestamp();
        s[_depth].StartAllocBytes = allocBytesAtEnter;
        _depth++;
    }

    public static void LeaveCpuAlloc()
    {
        int d = _depth - 1;
        Frame[]? s = _stack;
        if (s == null || (uint)d >= (uint)s.Length) return;

        // Read post-counter inside this method, NOT from IL — keeps the finally
        // handler shape (parameterless `call`) identical to today.
        long endAllocBytes = GC.GetAllocatedBytesForCurrentThread();
        long endTicks = Stopwatch.GetTimestamp();

        _depth = d;
        Frame f = s[d];
        long elapsedTicks = endTicks - f.StartTicks;
        long elapsedBytes = endAllocBytes - f.StartAllocBytes;
        // elapsedBytes can never be negative — the counter is monotonic per thread.

        if ((uint)f.HookId < (uint)PerModAttribution.Hooks.Count)
        {
            HookDescriptor desc = PerModAttribution.Hooks[f.HookId];
            PerModAttribution.Add(HookBackend.ILHookBackendId,
                desc.ModId, desc.CategoryId, f.HookId,
                elapsedTicks, elapsedBytes);
        }
    }
}
```

### Re-entrancy correctness for allocations

The same LIFO discipline that makes CPU attribution correct under nesting also makes allocation attribution correct. Consider `Mod A's NPC.AI → spawn → Mod B's GlobalNPC.OnSpawn`:

```
t=0    Mod A Enter   counterAtEnter = 1,000,000
t=1     (Mod A allocates 100 bytes)            counter = 1,000,100
t=2      Mod B Enter   counterAtEnter = 1,000,100
t=3       (Mod B allocates 500 bytes)         counter = 1,000,600
t=4      Mod B Leave   delta = 1,000,600 − 1,000,100 = 500  → credit Mod B
t=5     (Mod A allocates 200 bytes)            counter = 1,000,800
t=6    Mod A Leave   delta = 1,000,800 − 1,000,000 = 800   → credit Mod A
```

Mod A is credited with **800 bytes** even though it directly allocated only 300 — the 500 bytes Mod B allocated are *also* charged to Mod A because Mod B's allocation happened inside Mod A's hook call. This is the same behaviour as CPU attribution (which credits Mod A with all the elapsed wall-clock time during its call, including the inner Mod B work). The honesty contract requires this: a hook that delegates expensive work to a helper still pays for that work in the attribution view.

If the user wants "just my code" attribution, they read the per-hook breakdown — `Mod B.GlobalNPC.OnSpawn` was credited 500 bytes directly, and a drill-down on Mod A's `NPC.AI` shows the delta minus child contributions. The per-hook table reveals the inner cost; the per-mod-category table reveals the rolled-up cost. Both are useful, neither is wrong.

---

## 9. MetricCollector changes

### New raw allocation arrays

```csharp
// New, parallel to _perModRawMs
private readonly double[] _perModRawBytes;
private readonly double[] _perModSmoothedBytes;
private readonly double[] _perModAverageBytes;
private readonly double[] _perModHistoryBytes;
private readonly double[] _perModRollingBytes;
private readonly double[] _perHookRawBytes;
private readonly double[] _perHookSmoothedBytes;
private readonly double[] _perHookAverageBytes;

// New: the raw per-tick per-mod ring (CPU + alloc)
private readonly PerTickAttributionRing _perTickRing;

// New: spike detector
private readonly SpikeDetector _spikeDetector;
```

### `EndTick` mutation

Insert two new operations between the existing CPU harvest/smoothing block and the per-hook block:

```csharp
PerModAttribution.HarvestInto(_perModRawMs, backendId: 0);

if (HookBackend.AllocationTracking)
{
    PerModAttribution.HarvestAllocationsInto(_perModRawBytes, backendId: 0);
    UpdateRollingAverage(_perModRawBytes, _perModHistoryBytes, _perModRollingBytes, _perModAverageBytes, _sampleSlot);
    for (int i = 0; i < _perModSmoothedBytes.Length; i++)
    {
        _perModSmoothedBytes[i] += PerModSmoothing * (_perModRawBytes[i] - _perModSmoothedBytes[i]);
    }
}

// Push the raw row into the per-tick ring (after smoothing, before category
// breakdowns are overwritten next tick).
_perTickRing.Push(_perModRawMs, HookBackend.AllocationTracking ? _perModRawBytes : null);

// Spike detection runs against the latest frame and reads the ring for context.
_spikeDetector.OnTick(frame, this);

UpdateRollingAverage(_perModRawMs, _perModHistoryMs, _perModRollingMs, _perModAverageMs, _sampleSlot);
// ... rest unchanged ...
```

### Public accessors

```csharp
public IReadOnlyList<double> PerModCategoryBytes => _perModSmoothedBytes;
public IReadOnlyList<double> PerModCategoryAverageBytes => _perModAverageBytes;
public IReadOnlyList<double> PerHookBytes => _perHookSmoothedBytes;
public IReadOnlyList<double> PerHookAverageBytes => _perHookAverageBytes;
public PerTickAttributionRing PerTickRing => _perTickRing;
public IReadOnlyList<SpikeWindow> Spikes => _spikeDetector.Windows;
```

---

## 10. UI integration

### The mod tree gains a "MEM" column

Current row (from `ProfilerOverlay.cs:544–551`):

```
[+ ] Calamity Mod                    ████████████░░░░░     7.812 ms   full
```

New row, with allocation column behind a header toggle:

```
[+ ] Calamity Mod         CPU ████████████░░░░░     7.812 ms     MEM ████░░░  12.4 KB   full
```

The toggle replaces the current "NOW / 30S AVG" toggle with a three-state cycle: `NOW · CPU+MEM` → `30S AVG · CPU+MEM` → `NOW · CPU` → `NOW · MEM`. A second toggle, `CPU/MEM/BOTH`, lives next to the existing live/paused toggle:

```
┌─ PERFORMANCE PROFILER ─────────────────────────────  [BOTH ▾] [30S AVG] [LIVE] ─┐
```

Layout adjustments:
- Bar column width drops from 170 px to 120 px when both metrics are shown.
- A second narrow bar (10 px wide × row height) sits to the right of the existing one when in `BOTH` mode.
- The MS / MEM values share the right edge of the row.

### Memory column formatting

```csharp
static string FormatBytes(double bytes)
{
    if (bytes < 1024d)         return $"{bytes:F0} B";
    if (bytes < 1024d * 1024d) return $"{bytes / 1024d:F1} KB";
    if (bytes < 1024d * 1024d * 1024d) return $"{bytes / (1024d * 1024d):F1} MB";
    return $"{bytes / (1024d * 1024d * 1024d):F2} GB";
}
```

### Spike feed — placement decision

Three placements were considered:

| Option | Strengths | Weaknesses |
|---|---|---|
| **A. Dedicated SPIKES tab** next to LIVE / EVENTS | Clear discoverability; full panel area to render | Tab interaction breaks the current single-panel design |
| **B. Permanent ticker** at the bottom of the overlay | Always-visible — the user sees the latest spike without clicking | Eats vertical space even when no spike has occurred |
| **C. Pop-out panel** triggered by a spike | Zero idle UI cost; visually arresting when it fires | Easy to miss if the user is looking at the live tree; modal feel |

**Decision: A — dedicated SPIKES tab, with a ticker badge.** The overlay header gains one badge (`◆ 3 spikes`) when at least one spike has occurred in the session. Clicking the badge switches to the SPIKES tab. The badge is the discoverability surface; the tab is the analytical surface.

```
┌─ PERFORMANCE PROFILER ─────────────────  [◆ 3]  [BOTH ▾] [30S AVG] [LIVE] ─┐
│  [ LIVE ] [ SPIKES ] [ EVENTS ]                                            │
│                                                                            │
│  ───────────────── SPIKES tab content ─────────────────────────────────    │
│  ▸ #1  87 ms · tick 8,234  · 2.14 s  · Calamity 67%  · warming             │
│  ▸ #2  41 ms · tick 12,098 · 1 tick  · Spirit Reforged 51% · Forest        │
│  ▸ #3  28 ms · tick 14,401 · 3 ticks · Fargo's Souls 44%  · Sky            │
│                                                                            │
│  ┌─ #1  Cryogen Phase 2 — 87 ms worst frame ──────────────────────────┐    │
│  │   start tick 8231  →  end tick 8362  (131 ticks, 2.18s @ 60 tps)    │    │
│  │   baseline 18.4 ms  ·  ratio 4.7×  ·  MAD score 9.2σ                │    │
│  │                                                                     │    │
│  │   PER-MOD CONTRIBUTION at worst tick (8,234, 87 ms)                 │    │
│  │   Calamity Mod          58.3 ms  ████████████  67 %   12.4 KB       │    │
│  │   Fargo's Souls Mod     11.0 ms  ███░░░░░░░░░  13 %    2.1 KB       │    │
│  │   Spirit Reforged        7.4 ms  ██░░░░░░░░░░   8 %    0.4 KB       │    │
│  │   (other 15 mods)       10.3 ms  ██░░░░░░░░░░  12 %    1.7 KB       │    │
│  │                                                                     │    │
│  │   tick context: NPC 42 · Proj 184 · Dust 612                        │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
└────────────────────────────────────────────────────────────────────────────┘
```

### Live-view treatment

When the spike feed records a new spike, the SPIKES badge increments and briefly pulses (one second, accent colour). The current LIVE tree gets a subtle "row highlight" effect on the mods that contributed >20% to the spike — a faint accent-coloured row background that fades over 3 seconds. This is the "visual treatment of spikes in the live view" from the brief.

The implementation reuses the existing `ProfilerTheme.RowHover` colour, blended at decreasing alpha over 180 frames.

### Drill-down behaviour

Clicking a spike row in the SPIKES tab expands the per-mod contribution block beneath it (shown above as `#1`). The data is read from the spike's captured `PerModCatMs` / `PerModCatBytes` arrays — not from live state — so the values match what the spike-detector observed.

---

## 11. Session log integration

### Today's schema (`SessionLogWriter.cs:128–145`, schema 2)

```json
{
  "schema": 2,
  "spikes": [ { tick, frameMs, gcMs, npcCount, ..., topMods: [...] } ]
}
```

The existing `spikes` array uses a simple `frameTimeMs >= 50ms` threshold and stores the top 10 mods at that tick. That logic gets replaced by the new spike-detector output.

### New schema (schema 3)

```json
{
  "schema": 3,
  "identity": "...",
  "coverage": {
    "installedHooks": 7314,
    "trackAllocations": true,
    "mode": "Standard"
  },
  "spikes": [
    {
      "startTick": 8231,
      "endTick": 8362,
      "worstTick": 8234,
      "durationTicks": 131,
      "worstFrameMs": 87.4,
      "baselineMs": 18.4,
      "ratio": 4.75,
      "madScore": 9.2,
      "warming": false,
      "context": "Cryogen Phase 2 · Sulphurous Sea",
      "perMod": [
        { "id": 3, "name": "Calamity Mod",
          "ms": 58.3, "msPercent": 0.67,
          "bytes": 12698, "bytesPercent": 0.74,
          "topCategory": "NPCs" },
        { "id": 7, "name": "Fargo's Souls Mod", "ms": 11.0, ... }
      ]
    }
  ],
  "allocations": {
    "sessionTotalBytes": 1842394,
    "perMod": [
      { "id": 3, "name": "Calamity Mod", "totalBytes": 894231, "perTickAvgBytes": 412 }
    ]
  },
  "tickStream": null
}
```

The new field is `tickStream` — null in Standard mode (too large to write), populated in Deep mode with a compressed per-tick `bytesByModCat` and `msByModCat` payload. The Deep-mode payload is a separate sibling concern not implemented in this plan; the field exists in the schema so Deep-mode work can extend without a schema bump.

### `HookCoverageVersion` bump

```csharp
public const int HookCoverageVersion = 4;   // schema 3 with allocations
```

Bumping this invalidates every prior session file (existing behaviour through `SessionLogWriter.Hash`).

### Spike threshold change

`SessionLogWriter.cs:33–34`:

```csharp
private const int MaxSpikeRows = 50;
private const double SpikeThresholdMs = 50d;   // DELETE — replaced by SpikeDetector output
```

The session writer no longer detects spikes itself; it reads from `collector.Spikes`.

---

## 12. Events tab forward-compatibility

The brief notes a concurrent sibling plan for an Events / Encounters tab. That file does not exist yet in `context/notes/`. Designing defensively:

### What "context" means in a spike record today

Today: tick index, frame time, NPC / projectile / dust counts.

### What "context" should mean once events lands

A spike that occurred during "Cryogen Phase 2 in the Sulphurous Sea" should carry that string. The current `ContextSummary` field on `SpikeWindow` is `string?` — populated by whatever encounter detector is active. The shape `EncounterTag` was already named in `TickFrame.cs:13–18` as a planned-but-not-yet-built type.

### The forward-compat contract

When the events tab lands:

1. `TickFrame` gains an `EncounterTag activeEncounter` field (already mentioned in `TickFrame.cs:13`).
2. `SpikeDetector.OnTick` reads `frame.activeEncounter` and writes its display string into `SpikeWindow.ContextSummary` at capture time.
3. No schema bump — `context` is already a string-typed field in the schema.

The shape is forward-compatible because the spike snapshot captures context **at the moment the spike occurs**, not as a live-link to current state. Whatever the encounter detector is producing at that moment, the spike record keeps a copy.

---

## 13. Step-by-step implementation sequence

Two discovery passes (1–2), then execution (3–10). Each step is one logical commit.

| # | Action | Files | Verify | Risk |
|---|---|---|---|---|
| **1** | Re-read `MetricCollector.cs`, `PerModAttribution.cs`, `ProbeStack.cs`, `ILHookInterceptor.cs`, `SessionLogWriter.cs`, `ProfilerOverlay.cs`. Enumerate every public symbol that downstream code reads from each (esp. the `IReadOnlyList<double>` collector accessors that the overlay binds to). | read-only | List in scratch; confirm none break. | **Low** — read-only. |
| **2** | Grep across the repo for `PerModAttribution.Add(`, `PerModAttribution.HarvestInto(`, `PerModAttribution.BeginTick(`, `Stopwatch.GetTimestamp`. Confirm the call sites. | grep | Zero unexpected call sites. | **Low** — if anything outside the profiler folder hits these, raise before changing signatures. |
| **3** | Write the empirical allocation-API benchmark as a temporary `ModSystem.OnWorldLoad` one-shot: in a tight loop of 1,000,000 iterations, call `GC.GetAllocatedBytesForCurrentThread()` and accumulate. Log `client.log` with `ns/call`. Repeat for `Stopwatch.GetTimestamp()` as a baseline. Capture the numbers. **Decide the design path here:** if alloc-call < 30 ns, proceed with per-call tracking (Option A); if 30–60 ns, proceed but document tighter budget; if > 60 ns, fall back to sampled allocation (every Nth call) and revise §6 emit shape. | `Profiling/_TempAllocBench.cs`, removed in step 9 | `client.log` shows the ns/call figures. | **High** — this resolves R1. The whole plan branches here. |
| **4** | Add `PerTickAttributionRing` (new file `Profiling/PerTickAttributionRing.cs`). Add the matching arrays to `MetricCollector` but do NOT populate them yet. Compile. | new file, `MetricCollector.cs` | `dotnet msbuild` succeeds; existing UI / sessions unchanged. | **Low** — additive. |
| **5** | Populate `PerTickAttributionRing` from `MetricCollector.EndTick`. Verify by reading the most-recent-tick row and confirming the ms values match the smoothed read. | `MetricCollector.cs` | Add a temporary `Logger.Debug` line printing `_perTickRing.GetPerModMs(latest.TickIndex, modId=0)` vs `_perModRawMs[0]`; they should match. Remove the debug line after verification. | **Low** — pure data plumbing. |
| **6** | Add the spike detector (`Profiling/SpikeDetector.cs`). Wire `SpikeDetector.OnTick` into `MetricCollector.EndTick`. Expose `collector.Spikes`. Replace the existing `SpikeThresholdMs` logic in `SessionLogWriter.cs` with a read from `collector.Spikes`. | `SpikeDetector.cs`, `MetricCollector.cs`, `SessionLogWriter.cs` | In-game session: a deliberate stall (alt-tab and back) produces a spike entry in `current-session.json`. The session JSON shows the new schema-3 fields. | **Medium** — first user-visible change; verify the threshold isn't firing on baseline noise. |
| **7** | Add the SPIKES tab to `ProfilerOverlay.cs`. Initially read-only — the tab lists spikes, no drill-down yet. | `ProfilerOverlay.cs` | F9 → SPIKES shows the same entries as `current-session.json`. | **Low** — UI-only. |
| **8** | Extend `PerModAttribution` with byte arrays and the new `Add(...)` overload. Extend `ProbeStack` with `EnterCpuAlloc` / `LeaveCpuAlloc`. Add `HookBackend.AllocationTracking` flag. Compile. **Do not change the emit yet.** | `PerModAttribution.cs`, `ProbeStack.cs`, `HookBackend.cs` | `dotnet msbuild` succeeds; in-game with `AllocationTracking = false`, behaviour is verbatim today. | **Low** — additive, guarded. |
| **9** | Update `ILHookInterceptor.ApplyTimingWrap` to emit the allocation-tracking prologue when `HookBackend.AllocationTracking` is true. Add a `_gcGetAllocatedMethod` cached MethodInfo to the static cache. Flip `AllocationTracking = true` from a debug config option for testing. | `ILHookInterceptor.cs` | In-game with allocation tracking on: per-mod allocation values are non-zero, plausible (e.g. Calamity Mod's afterimage path shows kilobytes/tick, a quiet mod shows zero), and stable across ticks. | **High** — IL emission change. The step-7 benchmark validates the per-call cost; this validates the integration. |
| **10** | Add the MEM column toggle to `ProfilerOverlay.cs`. Wire `collector.PerModCategoryBytes` / `PerHookBytes` into the tree. Add the drill-down expansion to the SPIKES tab. Remove the temporary benchmark from step 3. | `ProfilerOverlay.cs`, delete `_TempAllocBench.cs` | F9 → BOTH shows both columns; values match the JSON log. Spike drill-down shows per-mod ms + bytes for the captured tick. | **Medium** — final UX surface. |
| **11** | Final overhead measurement against Invariant 2: a 5-minute Eye of Cthulhu session in `Lite`, `Standard`, `Standard+alloc` modes. Capture the profiler-self-overhead numbers from the overlay header (the README says the overlay reports its own overhead). Confirm: Lite < 1%, Standard 2–4%, Standard+alloc still ≤ 4% (the budget for allocation tracking is in Standard). | runtime verification | Numbers within budget. If Standard+alloc breaks 4%, fall back to sampled tracking from step 3's decision tree. | **High** — Invariant 2 gate. |
| **12** | Write `context/notes/spike-and-allocation-decisions.md` capturing the resolved threshold values (multiplier, floor), the per-call cost measured in step 3, the sampling strategy if any, and the schema-3 bump. Propose `context/_Overview.md` edit noting the new components. | `context/notes/` | User reviews; commit. | **Low** — durable memory. |
| **13** | Commit at logical checkpoints: (a) ring + detector + UI tab (steps 4–7), (b) allocation storage + emit + UI column (steps 8–10), (c) overhead validation + notes (steps 11–12). | git | Each commit builds and runs. | **Low** — discipline. |

---

## 14. Honest gaps — what this plan does not solve

These are real follow-ups; flagged so they don't accumulate as silent debt.

| Gap | Why it's deferred | Where it should land |
|---|---|---|
| **LOH-allocation distinction.** A 90 KB allocation looks identical to a 80 KB allocation in `GetAllocatedBytesForCurrentThread`, but only the first goes to LOH and dramatically worsens GC pause behaviour. | Distinguishing LOH requires either a custom allocator hook (out of scope for read-only instrumentation) or a `ClrEtwAll` event listener (Deep-mode territory). | Deep-mode milestone. |
| **Pinned / native allocations.** Mods using `Marshal.AllocHGlobal` or pinning managed buffers for native interop are invisible to the per-thread counter. | The API doesn't expose them. | Deep-mode; alternative is sampling RSS via `Process.WorkingSet64` for whole-process bias. |
| **Async work attribution.** A `Task.Run(() => { allocate(); })` started inside a hook reaches `GetAllocatedBytesForCurrentThread` on a thread pool thread, not the calling thread. Its allocations are not attributed to the hook. | The .NET runtime team has no general async-flow allocation tracking. | Documented limitation; not solvable within the per-thread counter primitive. |
| **JIT compilation appearing as a mod's allocation spike.** First call to a hot mod method JITs the body — a real one-time cost that shows as a spike attributed to that mod. | This is correct behaviour, not a bug. | Mitigated by the `warming` flag on early-session spikes; full solution is a warmup phase that pre-runs known hot paths, which contradicts the "always-on, no setup" stance. Documented and accepted. |
| **Spike context (biome / boss / event).** Currently `ContextSummary` is null. | Depends on the Events tab work that hasn't landed yet. | When events lands, `SpikeDetector` reads `TickFrame.activeEncounter` and populates the string. No schema change required. |
| **Cross-mod attribution chains.** "Mod A's projectile triggered Mod B's status applied via Mod C's accessory" as a chain attributed to all three. | Requires call-graph capture, which is Deep-mode work and architecturally far heavier than per-thread instrumentation. | Cross-Mod Chains milestone (research-gated per README). |
| **Per-allocation-site attribution within a hook.** "Mod A's `NPC.AI` allocates 12 KB" — which line of the AI did the allocating? | Would require sampling the call stack at allocation time (ETW `GCSampledObjectAllocation` events). | Deep-mode milestone. |

---

## 15. What gets stored where (concrete map)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  PerModAttribution.cs                                                       │
│    + _bytesByBackend       : long[backend][modId * Cat + catId]   per tick  │
│    + _hookBytesByBackend   : long[backend][hookId]                per tick  │
│    + Add(... bytes)        : new overload                                   │
│    + HarvestAllocationsInto: mirrors HarvestInto                            │
│    + BeginTick clears the new arrays                                        │
└─────────────────────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────────────────────┐
│  ProbeStack.cs                                                              │
│    Frame.StartAllocBytes    : long                                          │
│    + EnterCpuAlloc(int, long): pushes the extended frame                    │
│    + LeaveCpuAlloc()         : reads GC counter, diffs, credits both        │
└─────────────────────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────────────────────┐
│  ILHookInterceptor.cs                                                       │
│    + _gcGetAllocatedMethod cached MethodInfo                                │
│    + _enterCpuAllocMethod / _leaveCpuAllocMethod cached MethodInfo          │
│    ApplyTimingWrap branches on HookBackend.AllocationTracking               │
└─────────────────────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────────────────────┐
│  HookBackend.cs                                                             │
│    + AllocationTracking : bool (set from active mode at install time)       │
└─────────────────────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────────────────────┐
│  PerTickAttributionRing.cs                                                  │
│    NEW FILE — see §4 above                                                  │
└─────────────────────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────────────────────┐
│  SpikeDetector.cs                                                           │
│    NEW FILE — see §5 above                                                  │
│    Owns:  RingBuffer<SpikeWindow>(50), _emaFrameMs, _openWindow             │
└─────────────────────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────────────────────┐
│  MetricCollector.cs                                                         │
│    + _perModRawBytes / _perModSmoothedBytes / _perModAverageBytes / ...     │
│    + _perTickRing       : PerTickAttributionRing                            │
│    + _spikeDetector     : SpikeDetector                                     │
│    + PerModCategoryBytes, PerHookBytes, Spikes accessors                    │
│    EndTick gains the byte-harvest, ring push, spike detect block            │
└─────────────────────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────────────────────┐
│  SessionLogWriter.cs                                                        │
│    SchemaVersion 2 → 3                                                      │
│    Reads collector.Spikes instead of computing spikes itself                │
│    Adds top-level `allocations` block                                       │
│    Removes SpikeThresholdMs constant                                        │
└─────────────────────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────────────────────┐
│  ProfilerOverlay.cs (UI)                                                    │
│    Header toggle becomes CPU / MEM / BOTH                                   │
│    Mod rows gain optional MEM column                                        │
│    New SPIKES tab + spike badge                                             │
│    Spike row click expands per-mod-per-category drill-down                  │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Honest summary

The plan adds two metrics — per-mod allocation bytes per tick, and discrete spike windows — without changing the core ILHook architecture. The new storage is bounded: ~2 MB for the per-tick ring on a 100-mod stack, ~5 KB per stored spike, schema-3 session JSON. The IL prologue grows by one `call` in Standard / Deep mode and stays unchanged in Lite mode, so the existing Invariant 2 budget for Lite is unaffected.

The single empirical uncertainty is the per-call cost of `GC.GetAllocatedBytesForCurrentThread()` in the .NET 8 / tModLoader runtime — published documentation calls it cheap but does not commit a nanosecond figure. Step 7 of the implementation sequence resolves that with a benchmark loop before the IL emission lands; if the read costs more than ~30 ns on the dev machine, the design falls back to sampled allocation tracking (every Nth call) and the schema still works.

The largest design choice the plan makes is **median, not EMA, for the spike baseline** — robust to spike storms, supported by the literature on outlier-detection in time series, and computed only on candidate ticks so the per-tick budget is unaffected. MAD is recorded alongside as a secondary score for power users.

The two features ship together because they share the raw per-tick ring buffer. Building one without the other costs a redesign; building both at once costs nothing extra in storage and one extra commit in execution.
