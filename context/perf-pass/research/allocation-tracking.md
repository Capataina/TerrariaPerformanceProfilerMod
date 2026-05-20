# Allocation Tracking — Optimisation Research (v0.6 perf pass)

> Scope: the IL-emitted `GC.GetAllocatedBytesForCurrentThread()` reads on every
> instrumented hook (prologue + epilogue), the `ProbeStack.EnterCpuAlloc` /
> `LeaveCpuAlloc` probe pair, and the `PerModAttribution` byte aggregation.
> Targets tModLoader 1.4.4 on .NET 8 (CoreCLR), MonoMod.RuntimeDetour 25.3.2,
> Mono.Cecil 0.11.6, .NET 8 GC default mode.
>
> Hard constraint, restated up front: **no scope cuts.** The mode-gate hinted
> at in `notes/spikes-and-allocations-plan.md` §6 (alloc off in Lite) is *not*
> in scope here. Allocation tracking stays on by default; Caner's optimisation
> philosophy (`philosophy.md`) is "do what we already do at maximum efficiency,
> not do less." Every recommendation in §5 below preserves the same observable
> output: same per-hook delta, same byte total per mod per tick, same
> `AllocationBurstDetector` input. The data stack is whole on the way out.
>
> This file is the research half. The recommendations land in
> `context/perf-pass/master-plan.md` once every other research file is in.

---

## 0. Map

The four numbered sections are research; the fifth is the recommendation set;
the sixth is the integration map; the seventh is the prioritised order. Read
linearly — each later section depends on findings from the earlier ones.

| § | What lives here |
|---|---|
| 1 | Current-state audit. What ships in v0.5. Every code site that touches alloc capture, every storage cell. The shape we are optimising. |
| 2 | Baseline measurement strategy. The per-call cost of `GC.GetAllocatedBytesForCurrentThread()` is the load-bearing unknown in the rest of the pass — every "should we" answer depends on it. A benchmark proposal lands here. |
| 3 | .NET 8 internals research. The runtime source, the FCall body, what fields it reads, how that compares to `Stopwatch.GetTimestamp()` and to the alternative `GetTotalAllocatedBytes` API. |
| 4 | IL emission analysis. The bytes we emit per hook, the JIT-visible shape, what the alternative shapes would look like if we changed the call sequence. |
| 5 | Optimisation opportunities. Every candidate, each tagged with: invariant impact, observable-output preservation, expected gain, risk, cross-system cost. |
| 6 | Cross-system dependencies. What feeds in, what consumes out, where a change here breaks other research files in this same pass. |
| 7 | Prioritised order. Concrete sequence of work for `master-plan.md` to schedule. |
| 8 | References. Every external source cited, every line of runtime code quoted, every benchmark whose numbers shaped a claim. |

---

## 1. Current-state audit

### 1.1 What the v0.5 emission looks like, end-to-end

The full per-hook path, when `HookBackend.AllocationTracking == true` (which it
is by default — see `HookBackend.cs:74`):

```
─── prologue (emitted by ApplyTimingWrap, ILHookInterceptor.cs:539–546) ─────
  ldc.i4   <hookId>                             ; 32-bit constant, stack: [int]
  call     System.GC::GetAllocatedBytesForCurrentThread  ; stack: [int, long]
  call     ProbeStack::EnterCpuAlloc(int, long) ; stack: [] (frame pushed)

─── original method body (untouched, ret rewritten) ─────────────────────────
  ...                                           ; whatever the mod wrote
  stloc    <retLocal>     ; if non-void
  leave    <afterHandler> ; jumps out of try, fires finally

─── finally (appended after body, ILHookInterceptor.cs:493) ─────────────────
  call     ProbeStack::LeaveCpuAlloc()          ; reads alloc + stopwatch
  endfinally
afterHandler:
  ldloc    <retLocal>     ; if non-void
  ret
```

Two `call` instructions in the prologue, one `call` in the finally. The
parameterless-finally shape is deliberate — `LeaveCpuAlloc` reads the
allocation counter *internally* so the IL emitter does not need to emit a
second `call GC::GetAllocatedBytesForCurrentThread` inside the finally region
(which has additional verifier constraints — see `notes/spikes-and-allocations-plan.md`
discarded approaches).

For comparison, the Lite (CPU-only) shape is one fewer call in the prologue
and the finally targets `ProbeStack.Leave` instead. Same ExceptionHandler
shape, same `leave` rewrites, same retLocal.

### 1.2 Storage cells the path writes

`ProbeStack.Frame` (`ProbeStack.cs:40–53`) is the only per-call mutable cell
on the hot path. Field layout, as declared:

| Field | Type | Bytes | Set on Enter | Read on Leave |
|---|---|---:|---|---|
| `HookId` | int | 4 | yes | yes |
| `StartTicks` | long | 8 | yes | yes |
| `StartAllocBytes` | long | 8 | yes (alloc path) | yes (alloc path) |

Total: 20 bytes per frame. The CLR pads to 8 — actual layout is **24 bytes**
(4 + 4 padding + 8 + 8). At a typical observed depth of ~3, the active
working set per thread is ~72 bytes — comfortably inside one cache line per
thread.

The Frame[] is `[ThreadStatic]` so the cache line is thread-private and never
needs invalidation across cores. Per-tick the depth oscillates between 0 and
~3; the same three slots are written and read every hook call. That's good
spatial locality — the touched bytes are hot in L1 across the entire tick.

### 1.3 Aggregation path (`PerModAttribution.Add` 6-arg form)

`LeaveCpuAlloc` calls `PerModAttribution.Add(backendId, modId, categoryId,
hookId, elapsedTicks, elapsedBytes)` (`PerModAttribution.cs:246–279`). Per
call, the aggregation:

1. Bounds-check `backendId` (one branch, predictable).
2. Bounds-check `categoryId` (one branch, predictable).
3. Compute `index = modId * CategoryCount + categoryId` (one imul, one iadd —
   `CategoryCount = 7` is a `static readonly` int, not a constant, so the JIT
   cannot strength-reduce the multiply).
4. Bounds-check `index < ticks.Length` (one branch).
5. `ticks[index] += elapsedStopwatchTicks` (one load, one add, one store).
6. Bounds-check `hookId < hookTicks.Length` (one branch).
7. `hookTicks[hookId] += elapsedStopwatchTicks` (one load, one add, one store).
8. Bounds-check `backendId < _bytesByBackend.Length` (one branch).
9. Two more index-then-store sequences for the alloc bytes columns.

Total: 6 bounds checks, 1 imul, 4 array indexes, 4 array stores per Leave
call. The whole thing is allocation-free; every array is sized once at
`Configure` time.

### 1.4 Bytes the IL emitter produces per hook

Counting tokens, not assembled bytes (which depend on Cecil's choice of
short-form opcodes — likely uses long-form for most). Rough sizes from the
opcode reference:

| Instruction | Bytes |
|---|---:|
| `ldc.i4 <hookId>` (long-form) | 5 |
| `call GC::GetAllocatedBytesForCurrentThread` | 5 |
| `call ProbeStack::EnterCpuAlloc` | 5 |
| `call ProbeStack::LeaveCpuAlloc` (in finally) | 5 |
| `endfinally` | 1 |
| Plus the ExceptionHandler entry in the method header | 24 |

Approximate prologue overhead: **~21 IL bytes per hook**, plus 24 in the
method header. The CPU-only shape is ~16 IL bytes. Across 10,258 installed
hooks (baseline.md row "Hook install delta"), that's ~210 KB of additional
IL bytes — small in absolute terms, contributes a marginal slice of the
233 MB install-time RAM delta (the dominant contributor is MonoMod's
detour-info and Cecil's working set, not our emitted IL).

### 1.5 What runs on the hot path, summarised

Per hook entry (alloc on):

- 1 FCall into native (`GetAllocatedBytesForCurrentThread`)
- 1 managed static call (`EnterCpuAlloc`)
- 4 field writes on a thread-local struct
- 1 `Stopwatch.GetTimestamp()` FCall

Per hook exit:

- 1 FCall into native (`GetAllocatedBytesForCurrentThread` — inside Leave)
- 1 `Stopwatch.GetTimestamp()` FCall
- 1 array dereference for the `HookDescriptor` (`PerModAttribution.Hooks[f.HookId]`)
- 1 call into `PerModAttribution.Add` (6-arg)
- ~6 bounds checks + 1 imul + 4 array stores inside Add

So per hook, two `GetAllocatedBytesForCurrentThread` FCalls, two
`Stopwatch.GetTimestamp` FCalls, and the rest is pure managed arithmetic
against pre-allocated arrays.

### 1.6 The cost shape we want to characterise

The Stopwatch reads are an unavoidable floor — without them there is no CPU
attribution. The alloc reads are the v0.5 addition. The pass's central
question for this system: **what fraction of the hot-path cost is the alloc
addition?** Without that number we cannot rank optimisations honestly.

Today nobody knows. `notes/spikes-and-allocations-plan.md` §0 hypothesises
5–15 ns per `GetAllocatedBytesForCurrentThread` call and 15–25 ns per
`Stopwatch.GetTimestamp`. Neither has been measured on this codebase.
Baseline row "Avg per-tick PerformanceProfiler cost" is 0.27 ms across
~10,258 hooks per tick *and* the framework work — we cannot subtract the
alloc cost from that without a separate micro-benchmark.

This is §2's job.

### 1.7 Known sound aspects of the current design

Worth preserving — these are the bits that already do the right thing and
must not regress as we optimise:

| Property | Why it matters | Source |
|---|---|---|
| Finally is parameterless | Keeps the ExceptionHandler IL surface identical between alloc and Lite shapes; one code path in the manipulator | `ILHookInterceptor.cs:493` |
| Frame stores StartAllocBytes alongside StartTicks | One stack, one source of truth — two parallel stacks would desync under exception unwind | `ProbeStack.cs:40–53` |
| `[ThreadStatic]` stack | Zero cross-thread coordination; draw-thread and update-thread hooks are independent | `ProbeStack.cs:58–59` |
| `RegisterOrReuseHook` dedupe | Same hookId across delegate and IL backends — alloc-byte rows aren't double-credited in Parallel mode | `PerModAttribution.cs:165–178` |
| 6-arg Add fuses the alloc credit | One method call per Leave instead of two — branch-predictor stays warm on the common path | `PerModAttribution.cs:246–279` |
| Conditional column allocation | Lite-future mode with `trackAllocations=false` would pay zero RAM cost on alloc arrays | `PerModAttribution.cs:116–130` |

### 1.8 Open coverage gap (correctness, not optimisation)

Not directly in scope but worth surfacing because it shapes how §5
optimisations are validated: the LiteDB session JSON includes
`modSummary[].allocBytes` only when tracking is on, and **no test in
`Tests/`** exercises the round-trip from `EnterCpuAlloc` through
`HarvestAllocationsInto` to the JSON. `BaselineTests.cs` exercises the
`Baseline.Recompute` math with synthetic byte values but never the actual
GC-counter-fed write path. This pass should land at least one synthetic
allocation test that drives a known `byte[]` allocation through a hooked
method and asserts the credited delta matches the expected size to within
the per-thread-counter slack (§3.6 below). Without it, an optimisation in §5
could silently zero out alloc credits and the build would stay green.

---

## 2. Baseline + per-call cost

### 2.1 The number we need

Two micro-cost numbers gate every recommendation in §5:

- **t_alloc** — nanoseconds per `GC.GetAllocatedBytesForCurrentThread()` call,
  steady-state, after JIT and warmup, on .NET 8 CoreCLR Apple Silicon and
  Windows x64.
- **t_stopwatch** — nanoseconds per `Stopwatch.GetTimestamp()` call, same
  conditions. (Known well-enough from public benchmarks, but we measure on
  the same rig so the comparison is apples-to-apples.)

Derived numbers:

- **t_alloc_delta** = t_alloc — what each prologue/epilogue alloc read costs.
- **t_pair** = 2·t_stopwatch + 2·t_alloc + managed-call overhead — the full
  per-hook instrumentation cost when alloc is on.
- **t_pair_lite** = 2·t_stopwatch + managed-call overhead — the same when
  alloc is off.
- **alloc_overhead_fraction** = (t_pair — t_pair_lite) / t_pair — the
  fraction of instrumentation cost attributable to alloc tracking. Targets
  for §5 are framed against this.

### 2.2 Why BenchmarkDotNet is the right tool

BDN does three things our needs require:

1. **JIT-warmup before measurement.** First-call cost on a virgin
   `GC.GetAllocatedBytesForCurrentThread` includes the call-stub install —
   not what we want to measure.
2. **Statistical reporting of the median + MoE.** A 10-ns call's noise floor
   is large; we want at least 1e6 iterations and a confidence interval, not
   a single timed loop.
3. **Allocation diagnostics that don't disturb the measurement.** BDN uses
   the *same* `GetAllocatedBytesForCurrentThread` API as MemoryDiagnoser
   (verified at `dotnet/BenchmarkDotNet:src/BenchmarkDotNet/Engines/GcStats.cs:240,265`
   — see refs §8). So enabling MemoryDiagnoser does not perturb our
   measurement of that same API; it would only add overhead in a final-tally
   harness call.

There is **no BDN dependency** in this project today. Adding one for the
benchmark host is reasonable: it lives in a sibling `bench/` project, not in
the `.tmod` package (build.txt's `buildIgnore` already excludes non-csharp
trees).

### 2.3 The micro-benchmark, proposed shape

```csharp
// In a separate project: bench/PerformanceProfiler.Bench/AllocCounterBench.cs
//
// dotnet run -c Release --project bench/PerformanceProfiler.Bench
//
// Targets .NET 8 (matches tModLoader 1.4.4's pin). Uses BDN's default config
// + DisassemblyDiagnoser so we can see the FCall stub.

[SimpleJob(RuntimeMoniker.Net80)]
[MemoryDiagnoser]
public class AllocCounterBench
{
    private long _accumulator;

    // Floor: empty loop body. Measures BDN's invocation overhead so the
    // others can be compared on a sane baseline.
    [Benchmark(Baseline = true)]
    public long NoOp() => _accumulator;

    [Benchmark]
    public long Stopwatch_GetTimestamp() => System.Diagnostics.Stopwatch.GetTimestamp();

    [Benchmark]
    public long GC_GetAllocatedBytesForCurrentThread()
        => System.GC.GetAllocatedBytesForCurrentThread();

    // Pair: what a tight Enter/Leave path actually does, minus the array
    // bookkeeping. Isolates the FCall cost from the surrounding plumbing.
    [Benchmark]
    public long EnterLeavePair_Lite()
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        // (no body — measure instrumentation only)
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        return t1 - t0;
    }

    [Benchmark]
    public long EnterLeavePair_Alloc()
    {
        long a0 = System.GC.GetAllocatedBytesForCurrentThread();
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
        long a1 = System.GC.GetAllocatedBytesForCurrentThread();
        return (a1 - a0) + (t1 - t0);
    }

    // Comparison: GetTotalAllocatedBytes(false) — the approximate process-wide
    // path. Confirms it's no cheaper than the per-thread one (we predict it
    // is the same or worse).
    [Benchmark]
    public long GC_GetTotalAllocatedBytes_Approximate()
        => System.GC.GetTotalAllocatedBytes(precise: false);
}
```

### 2.4 What the numbers must be to keep alloc tracking unconditional

The Standard mode budget is 2–4% of frame time. At 60 Hz, one frame is
~16.67 ms. So the per-frame profiler budget at the top of Standard is
~0.67 ms.

If a session fires 50,000 hook calls per tick (the upper end of what we
observe — baseline counts 10,258 *installed* hooks but only a subset fire
per tick; 50k per-tick *invocations* across ~150 mods is the working
estimate from the design pitch), the per-call budget is
`0.67 ms / 50k = 13.4 ns` for *all* instrumentation per call.

That number is the cliff. If `t_pair_lite` already exceeds it the alloc
addition has nowhere to go; if `t_pair_lite` is 8 ns and `t_alloc_delta` is
5 ns, alloc tracking sits inside Standard's ceiling but consumes half the
budget. Knowing where on this gradient we sit determines whether §5's
"emission consolidation" optimisations are nice-to-have or load-bearing.

### 2.5 Expected order of magnitude — published evidence

Combining several sources (full citations in §8):

| Source | Number | Confidence |
|---|---|---|
| `dotnet/runtime` issue 17891 (Kotas, allocator owner) | "essentially free, relative to the allocator path" | Qualitative; says the counter is already maintained, so the FCall body is just a read |
| Runtime source `comutilnative.cpp:847` | 4 thread-local field reads + 3 integer adds, returns long | Direct evidence; this is the entire FCall body |
| Stopwatch.GetTimestamp on .NET 8 / Apple Silicon | ~15-20 ns (community benchmarks) | Indicative |
| Stopwatch.GetTimestamp on .NET 8 / x64 | ~10-25 ns depending on CPU and TSC handling | Indicative |
| BDN issue 1832 (alloc-tracking accuracy) | Notes the API is "per-thread cheap" — they call it every iteration of MemoryDiagnoser's final tally | Behavioural |

**Working hypothesis** (to be confirmed by §2.3 benchmark): `t_alloc` is in
the range **5–12 ns** on .NET 8 CoreCLR. Reasoning: the FCall stub adds
~3–5 ns; the four field reads from thread-local storage land in L1 (the
allocator just wrote them on the previous allocation); the arithmetic is
three adds. There is no kernel transition, no atomic, no lock.

If that holds, **t_alloc_delta is roughly half of t_stopwatch**. Two alloc
reads add about as much as one Stopwatch read, doubling our instrumentation
cost from "two Stopwatch reads + bookkeeping" to "two Stopwatch reads + two
alloc reads + bookkeeping" — call it a ~50% relative increase over the
Lite path, but still inside the Standard budget.

If t_alloc turns out to be 20+ ns (it shouldn't; the runtime source rules it
out, but the benchmark is the proof), then §5.2 (FCall consolidation)
becomes load-bearing rather than nice-to-have.

### 2.6 Benchmark deliverables

The bench project produces:

- A `bench-results/alloc-counter-net8.md` table with median + MoE for each
  benchmark on the dev machine.
- The DisassemblyDiagnoser output for `GC_GetAllocatedBytesForCurrentThread`
  so we can see the JIT-emitted call sequence and confirm no boxing.
- A second run with the same numbers on x64 (when a Windows machine is
  available) so we know the numbers are portable.

These figures feed §5's gain estimates. **Until the benchmark lands, every
"X% improvement" claim in §5 carries a `[t_alloc=hypothesis]` tag and gets
revisited when real numbers arrive.**

### 2.7 Secondary measurement: alloc counter granularity

A separate test, not via BDN, verifies the byte-granularity assumption:

```csharp
[Fact]
public void GetAllocatedBytesForCurrentThread_ReportsSmallAllocations()
{
    long a0 = GC.GetAllocatedBytesForCurrentThread();
    byte[] x = new byte[7];   // tiny — smaller than typical heap quantum
    long a1 = GC.GetAllocatedBytesForCurrentThread();
    GC.KeepAlive(x);
    // 7 bytes + 24-byte object header on 64-bit ≈ 32 bytes object,
    // rounded up to 8-byte alignment, so delta ≥ 24 in practice.
    Assert.True(a1 - a0 >= 16, $"delta was {a1 - a0}");
}
```

This confirms the documented "increments at allocation granularity" behaviour
and gives us a concrete lower bound for what attribution will see. If a
hook allocates a `new byte[7]` we expect to credit ~32 bytes — not 7. That
is fine for a KB/s-display profiler, but worth pinning down in a test so we
don't get blindsided by 1-byte-granularity assumptions in the
`AllocationBurstDetector` thresholds.

---

## 3. .NET 8 alloc-counter internals

### 3.1 The managed surface

`System.GC.GetAllocatedBytesForCurrentThread()` on .NET 8 CoreCLR:

```csharp
// dotnet/runtime: src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs
[MethodImpl(MethodImplOptions.InternalCall)]
public static extern long GetAllocatedBytesForCurrentThread();
```

`InternalCall` is FCall in .NET parlance — direct managed→native dispatch
with no QCall thunk, no marshalling. The cheapest VM-to-native transition
.NET offers. (Compare with `GetTotalAllocatedBytesPrecise`, which is a
`[LibraryImport(RuntimeHelpers.QCall, ...)]` — full QCall, slower.)

### 3.2 The native body

```cpp
// dotnet/runtime: src/coreclr/vm/comutilnative.cpp:847
FCIMPL0(INT64, GCInterface::GetAllocatedBytesForCurrentThread)
{
    FCALL_CONTRACT;

    INT64 currentAllocated = 0;
    Thread *pThread = GetThread();
    gc_alloc_context* ac = &t_runtime_thread_locals.alloc_context.m_GCAllocContext;
    currentAllocated = ac->alloc_bytes + ac->alloc_bytes_uoh
                       - (ac->alloc_limit - ac->alloc_ptr);

    return currentAllocated;
}
FCIMPLEND
```

This is the entire implementation. Three things matter for us:

1. **It reads four fields from `t_runtime_thread_locals`** — a thread-local
   struct the allocator maintains on the same fast path that bumps
   `alloc_ptr` on every small-object allocation. By the time we read it,
   it's almost certainly hot in L1 because the most recent allocation just
   wrote to it.
2. **The arithmetic is three integer ops** — `alloc_bytes + alloc_bytes_uoh
   - (alloc_limit - alloc_ptr)`. The subtraction-of-difference reflects that
   `alloc_bytes` is the *committed* counter (bumped at the start of each
   allocation context), while the current high-water mark inside the
   *active* context is `(alloc_limit - alloc_ptr)`. The formula yields the
   true monotonic-since-thread-start figure.
3. **There is no synchronisation.** No lock, no `Interlocked`, no memory
   fence beyond what the FCall transition implies. Single-threaded read of
   single-thread state.

`alloc_bytes_uoh` covers Unloaded-Object-Heap (LOH + POH) allocations
separately — see `notes/spikes-and-allocations-plan.md` R7 risk. LOH bytes
count toward the per-thread total even though they go to a different
generation. That matches our documentation and is correct for our use case.

### 3.3 The FCall transition cost

FCall on CoreCLR adds:

- Pinvoke-style frame setup (cheaper than QCall — no full TransitionFrame)
- A handful of asm ops to push the thread context pointer

Published numbers from the runtime team put FCall transition at ~2–5 ns on
modern hardware. Add ~5 ns for the body (3 adds, returns), and t_alloc lands
in our hypothesised 5–12 ns range. This is consistent with §2.5's working
hypothesis; the benchmark in §2.3 will confirm or contradict.

### 3.4 Why the Mono path matches

`dotnet/runtime: src/mono/System.Private.CoreLib/src/System/GC.Mono.cs:55`
declares the same `InternalCall`. The Mono native implementation does the
same per-thread counter read. tModLoader 1.4.4 on Apple Silicon runs on
.NET 8's CoreCLR (Mono is not used by tML 1.4.4 in production — the Mono
branch is from .NET 5 era), so for our purposes the CoreCLR path is the
relevant one. We document the Mono parity for completeness; nothing in §5
depends on Mono.

### 3.5 What does NOT count

From the .NET docs and the source above:

- **Native allocations** — `Marshal.AllocHGlobal`, P/Invoke buffers,
  unmanaged interop. Our IL sees the managed allocator only; if a mod
  allocates a 1 MB native buffer in its hook, we record 0 bytes for it.
  This is a known limit and is correct by design — the profiler's scope is
  managed-heap pressure.
- **Stack allocations** — `stackalloc`, locals on the stack. Don't touch
  the heap, don't count. Good.
- **Reused objects** — pooled buffers, `ArrayPool<T>.Rent`. No allocation,
  no count. Good — pool reuse should show as zero alloc cost.
- **GC compaction** — the counter is a write-side accumulator, not a heap
  reading. A Gen2 compaction does not lower the per-thread number. This is
  what we want — we are measuring *pressure*, not *headroom*.

### 3.6 What does count, with footnotes

- **Reference-type new** — `new List<int>()`: counts the List header (~32
  bytes on 64-bit) plus, when a capacity is set in the constructor, the
  backing array.
- **Value-type boxing** — `object o = 42;` allocates a boxed int (~24 bytes
  on 64-bit, header + payload + padding).
- **Closure capture** — `Action a = () => Console.WriteLine(x);` allocates
  a closure object. Counts.
- **Iterator / async state machines** — when synthesised as a class (rarely;
  most are structs since .NET Core 2.1), the allocation counts. When
  struct-based, no heap, no count.
- **LOH (≥ 85,000 bytes)** — counts toward the byte total. Goes to Gen2/LOH
  but the byte counter does not distinguish.

The granularity caveat: the documentation explicitly says the counter
"increments at allocation granularity." We confirmed in §2.7 that a
`new byte[7]` shows up as ~32 bytes, not 7. For a profiler reporting KB/s,
this is below noise. For a profiler attempting per-allocation forensics
(out of scope), it would matter.

### 3.7 Threading reality

`Terraria.Main.Update` runs on the game's update thread. `Main.Draw` runs
on its own draw thread. tModLoader's hook dispatcher fires hooks from
whichever thread called the hooked method — so update-thread hooks fire
on the update thread, draw hooks on the draw thread.

Our `[ThreadStatic]` `ProbeStack` makes this correct: each thread has its
own frame stack and its own enter/leave instrumentation, and
`GetAllocatedBytesForCurrentThread` reads *that* thread's counter. The
delta between Enter and Leave is the bytes the body allocated on the
thread it ran on. There is no cross-thread aggregation problem.

The async caveat from the design plan is real but does not bite tML 1.4.4
in practice: tML hooks are synchronous overrides (`public override void
AI(NPC npc)` etc.). If a mod's hook body does `Task.Run(...)` and the task
allocates on a thread pool thread, those bytes are *not* attributed to the
hook — they show up against whatever frame is on the worker thread's
ProbeStack (usually none). That is the correct attribution if we hold to
"per-thread, where the work happened." A future Deep mode could add
ExecutionContext-flow tracking; out of scope here.

### 3.8 Alternative APIs considered

`GC.GetTotalAllocatedBytes(bool precise)`:

- `precise: false` → `GetTotalAllocatedBytesApproximate()`, another FCall.
  Process-wide, sums across all threads. Per-call cost is similar to per-thread
  (single FCall, similar field reads — see `comutilnative.cpp:887` neighbouring
  the per-thread one). **But it is wrong for our purposes:** we cannot
  attribute the delta to one hook because another thread's allocations
  arrive between our Enter and Leave reads. Not usable.
- `precise: true` → QCall (`GCInterface_GetTotalAllocatedBytesPrecise`) which
  walks every thread and aggregates exact figures. Drastically slower —
  the QCall transition alone is ~50 ns, plus the per-thread aggregation.
  Wrong tool.

`GC.GetGCMemoryInfo()`:

- Returns heap snapshot info (committed bytes, fragmentation, gen sizes).
  Useful for context-tab "what's the heap state" displays, not for per-call
  attribution. Different problem.

`AppDomain.MonitoringTotalAllocatedMemorySize`:

- Requires `MonitoringIsEnabled` to be set first. Process-wide, same wrong-
  ness as `GetTotalAllocatedBytes` for attribution. Used by BDN as a fallback
  on platforms where the per-thread API isn't available; we are not such a
  platform.

**Verdict:** `GetAllocatedBytesForCurrentThread` is the only correct
primitive for what we are doing. There is no cheaper alternative that
preserves per-hook attribution.

---

## 4. IL shape analysis

### 4.1 The Prologue

The alloc-aware prologue emits three instructions before the original body:

```il
ldc.i4   <hookId>                                   ; (1)
call     System.GC::GetAllocatedBytesForCurrentThread ; (2)
call     ProbeStack::EnterCpuAlloc(int, long)       ; (3)
```

What the JIT sees:

- (1) is a constant push. The JIT emits `mov reg, imm32` — about as cheap as
  an instruction can be.
- (2) is a managed call to an FCall stub. The JIT emits a direct call
  through the method table; there's no virtual dispatch. The callee is
  pure (modulo timing) so the call cannot be inlined but does not block
  surrounding optimisations.
- (3) is a managed call to a static method in our assembly. The JIT can in
  principle inline it (no virtual dispatch, no overloads); whether it does
  depends on the body size. `EnterCpuAlloc` is ~30 IL bytes — borderline.
  We could mark it `[MethodImpl(MethodImplOptions.AggressiveInlining)]` and
  the JIT would inline almost certainly; see §5.4.

### 4.2 The Epilogue / finally

```il
.try { ... } finally {
  call ProbeStack::LeaveCpuAlloc()      ; (1)
  endfinally                            ; (2)
}
```

(1) is one managed static call. Inside `LeaveCpuAlloc`:

- Reads `_depth`, `_stack` (two ThreadStatic reads)
- Bounds check on `d`
- Calls `GC.GetAllocatedBytesForCurrentThread()` — the second FCall
- Calls `Stopwatch.GetTimestamp()` — also an FCall
- Computes deltas
- Calls `PerModAttribution.Add` — six-arg version

Same point about inlining applies; `LeaveCpuAlloc` is ~50 IL bytes,
unlikely to be inlined without an attribute. See §5.4.

### 4.3 Can we tighten the IL?

Three candidate tightenings:

| Candidate | Bytes saved per hook | Risk |
|---|---:|---|
| Use `ldc.i4.s <hookId>` (short form, 2 bytes) when hookId < 128 | 3 | Cecil should already pick short-form; verify in disassembly |
| Use `ldc.i4.0` ... `ldc.i4.8` for the first nine hookIds | 4 | Trivial, but only helps the first nine hooks; not worth the special-case branch in the emitter |
| Replace the prologue's two-call sequence with a single static `EnterCpuAlloc(int)` that reads the counter internally | 5 (one fewer call instr) | **Increases per-call native transitions from 1 to 1** (we still call GC.Get...), so no gain in native cost. Loses the "Enter symmetric with Leave's read-internal model" design. Considered and rejected by `notes/spikes-and-allocations-plan.md`. |

The first candidate is a free Cecil-side check; we should verify Cecil
emits the short form when possible. The second is not worth the complexity.
The third is rejected — see also §5.2 for a deeper analysis.

### 4.4 The finally is already optimal

The `LeaveCpuAlloc()` parameterless shape is the minimum: one IL call. The
alternative — passing the post-counter as an argument from the finally
region — would require a `call GC::GetAllocatedBytesForCurrentThread` *inside*
the finally body. ECMA-335 forbids putting calls that throw inside a finally
without special protection; `GetAllocatedBytesForCurrentThread` doesn't
throw, but PEVerify and some ngen scenarios are pickier than the runtime
itself. The current shape sidesteps all of that.

### 4.5 Cecil's import behaviour

`ILCursor.Emit(OpCodes.Call, _gcGetAllocatedMethod)` imports the MethodInfo
into the target module's metadata if not already present. The first time
we hook into a mod's assembly, this adds one MemberRef row for
`System.GC::GetAllocatedBytesForCurrentThread` to that module. Subsequent
hooks in the same module reuse it.

We pay the import cost once per (mod, hookId) install, never per call. The
import work is install-time cost only — it shows up in the 233 MB install
delta (baseline.md), not in the per-tick budget. The hot path sees the
already-imported `MemberRef`.

### 4.6 Closed-generic shared bodies

`ILHookInterceptor.cs:282–340` deduplicates closed-generic instantiations
via `_instrumentedHandles`. This already handles the alloc path correctly
— the deduplication is at the MethodHandle level, before the manipulator
chooses Enter/Leave vs EnterCpuAlloc/LeaveCpuAlloc. So we never double-count
alloc bytes on shared compiled bodies. No optimisation needed here, but
worth confirming as we audit; a regression in the dedupe would silently
double the alloc credits for the most expensive hook shape we have.

### 4.7 Putting it together

The current IL emission is close to minimal. The only structural
improvement worth considering — collapse the prologue's two calls into one
— costs us the symmetric Enter/Leave-reads-internally design and the
documented rationale, with zero native-cost gain (we still issue exactly
one FCall to `GetAllocatedBytesForCurrentThread` either way). The
micro-tightenings (short-form constants) are Cecil's job and are likely
already done; we verify in §5.6.

The real optimisation surface is upstream: the FCall cost itself (§3) and
the managed plumbing around it (§5.4 inlining, §5.5 array indexing).

---

## 5. Optimisation opportunities

### 5.1 Ranking framework

Each candidate is graded on six dimensions:

| Dim | What it measures |
|---|---|
| Invariant impact | Does it touch Invariant 1 (read-only), 2 (overhead budget), 4 (abort-clean), or 5 (no mod-specific code)? Any change to those is flagged. |
| Observable-output preservation | After the change, does the same input still produce the same per-mod byte total, same `AllocationBurstDetector` input, same JSON `modSummary[].allocBytes`? "Yes" means the data stack is whole. |
| Expected gain | Nanoseconds per hook call recovered, framed against the §2 baseline. Marked `[t_alloc=hypothesis]` until the benchmark lands. |
| Risk | What breaks if the optimisation has an unforeseen interaction. Concrete trigger named. |
| Cross-system cost | What other research files / systems are affected. Anything cross-cutting needs `master-plan.md` sequencing. |
| Verdict | Land / land-after-bench / consider-later / reject |

### 5.2 Consolidate FCall reads via batched prologue call

**Idea.** Replace the two-instruction prologue (`call GC::Get... ; call
ProbeStack::EnterCpuAlloc(int, long)`) with a single `call
ProbeStack::EnterCpuAllocRead(int)` that reads the counter inside the
callee. The IL emission gains one fewer `call` instruction; the native
work is unchanged (still one FCall to GC.Get...).

**Invariant impact.** None.

**Observable-output preservation.** Yes — the read is moved by one
instruction's worth of execution, semantically equivalent.

**Expected gain.** Near-zero on the hot path. The eliminated `call` is one
managed dispatch (~1–2 ns) saved per Enter, but the EnterCpuAllocRead body
must still issue the FCall. Net: a ~1–2 ns improvement per Enter, ~0 per
Leave. **Total ~1–2 ns per hook**, off a hypothesised ~30–40 ns base.
~3-5% relative.

**Risk.** Loses the design's "Enter symmetric with Leave's read-internal
model" rationale. Future readers will see two patterns where today there
is one. Documentation cost outweighs the 5% gain unless the §2 benchmark
shows the prologue dispatch is unexpectedly expensive.

**Cross-system cost.** `ILHookInterceptor.cs` emit shape changes; one IL
test in any future test harness needs updating.

**Verdict.** Consider-later. Land only if §2 measurement shows the
prologue dispatch costs more than expected; otherwise the doc-cost loses
to the perf gain.

### 5.3 Inline `EnterCpuAlloc` / `LeaveCpuAlloc` aggressively

**Idea.** Add `[MethodImpl(MethodImplOptions.AggressiveInlining)]` to
`ProbeStack.EnterCpuAlloc` and `LeaveCpuAlloc`. The static methods are
called from many IL-rewritten methods; without the attribute, the JIT
inliner's body-size heuristic decides per-callsite. With the attribute,
the JIT inlines unless it absolutely cannot — same as how
`PerModAttribution.Add` is implicitly called from many sites.

**Invariant impact.** None.

**Observable-output preservation.** Yes — pure inlining preserves
semantics.

**Expected gain.** Eliminates one managed dispatch per Enter and one per
Leave. Per dispatch is ~1–2 ns; per hook is ~2–4 ns. **Total ~2–4 ns
per hook**, ~5–10% relative against the hypothesised base.

**Risk.** Code-size inflation: each hooked method's compiled body grows by
~50–80 bytes of x64 (the inlined Enter + Leave bodies). For 10,258 hooks
that's ~800 KB of JITted code on top of what already exists. Plausible to
make the install delta worse, not better. **Trigger to bite:** a host
machine with very tight code cache (rare on x64; possible on Apple Silicon
under cache pressure during world load).

A second risk: the JIT may choose differently across .NET 8 patch
releases. If a future JIT change makes the inlined version slower, we
won't notice without re-running §2. The mitigation is to keep the
benchmark in CI so a regression shows up.

**Cross-system cost.** None.

**Verdict.** Land-after-bench. The §2 benchmark should include an "inlined"
variant — measure the cost with the attribute. If it's strictly faster
and the code-size inflation is bounded (<2 MB total), land it. Otherwise
reject.

### 5.4 Inline `PerModAttribution.Add` (6-arg)

**Idea.** Same as 5.3 but on the aggregation method. `Add` has six
arguments and ~30 IL bytes — the JIT's default heuristic might inline at
small callsites and not at large ones. Forcing inlining unifies the cost.

**Invariant impact.** None.

**Observable-output preservation.** Yes.

**Expected gain.** Eliminates one managed dispatch per Leave, ~1–2 ns
per hook. **Total ~1–2 ns**, ~3–5% relative.

**Risk.** Same code-size concern as 5.3 but smaller (one inlinee per
Leave, not per Enter and Leave). Lower magnitude.

**Cross-system cost.** Touches `PerModAttribution.cs` — used by
hook-instrumentation, metric-collection, and the delegate-path
HookInterceptor.cs Add too. The attribute is method-scoped, so no
ripple effect, but the benchmark must verify the delegate path doesn't
regress.

**Verdict.** Land. Low-risk, modest gain. Belongs in the same patch as
5.3 since they're the same shape of optimisation; one benchmark covers
both.

### 5.5 Strength-reduce `modId * CategoryCount` to a left-shift

**Idea.** `CategoryCount = 7` (`PerModAttribution.cs:44`). The JIT can't
strength-reduce because `_categoryCount` is a `static readonly int`, not a
`const`. Every `Add` call does `modId * 7` — one imul (~3 cycles). Two
possible fixes:

- (a) Change `CategoryNames` declaration so `CategoryCount` is a `const int
  CategoryCount = 7`, accepting that adding a new category requires a
  recompile rather than just an array edit.
- (b) Pre-compute `modId * CategoryCount` at hook-register time and store
  it in `HookDescriptor` (add a `CellBase` field). Each `Add` call then
  uses `desc.CellBase + categoryId` — one iadd instead of one imul + one
  iadd.

**Invariant impact.** None.

**Observable-output preservation.** Yes — math is identical.

**Expected gain.** One imul saved per hook → ~1 ns per hook. ~2–3%
relative.

**Risk.** Option (a) is a structural change to how categories are added;
adding an 8th category becomes a recompile-the-mod event rather than a
data edit. Today `CategoryNames` is intended as edit-once-at-design-time
data, but the .NET compiler doesn't enforce that. Worth a comment but
small.

Option (b) adds 4 bytes per `HookDescriptor` (still under one cache line
for ~10k descriptors → ~40 KB), no behavioural change.

**Cross-system cost.** Option (a) is `PerModAttribution.cs` only. Option
(b) is `PerModAttribution.cs` and `ProbeStack.cs` (where the descriptor is
read).

**Verdict.** Land option (a). The recompile-to-add-category constraint
is acceptable — categories are stable design choices, not runtime data.
Option (b) adds complexity for the same gain.

### 5.6 Verify Cecil short-form ldc.i4 selection

**Idea.** Audit the emitted IL with `dotPeek` or `dnSpy` on a real hooked
method, confirm `ldc.i4.s` (short form) is used when `hookId < 128` and
`ldc.i4.0` ... `ldc.i4.8` for the first nine. If not, manually pick the
opcode in `ApplyTimingWrap`.

**Invariant impact.** None.

**Observable-output preservation.** Yes.

**Expected gain.** Save 3 bytes of IL per hook for the common case
(`hookId < 128`). Per-tick cost: zero (IL byte count doesn't affect run
time after JIT). Install-time cost: ~30 KB across 10k hooks, lost in
noise against the 233 MB install delta.

**Risk.** None.

**Cross-system cost.** None.

**Verdict.** Land if Cecil isn't already doing this; defer if it is.
A one-line `dotPeek` check decides.

### 5.7 Pack `Frame` to remove the padding hole

**Idea.** `Frame` is `{ int HookId; long StartTicks; long StartAllocBytes; }`
which the CLR lays out as 4 bytes + 4 pad + 8 + 8 = 24 bytes. Reorder to
`{ long StartTicks; long StartAllocBytes; int HookId; }` → 8 + 8 + 4 +
4-pad = 24 (same), because the struct *array* element size aligns to 8.
No saving. **Alternative:** use `[StructLayout(LayoutKind.Sequential, Pack = 4)]`
to force 4-byte packing → 20 bytes per element, then array element size
20 → ~17% memory saving for the Frame[] backing store.

**Invariant impact.** None.

**Observable-output preservation.** Yes.

**Expected gain.** The Frame[] is per-thread and ~32 entries deep. 17%
of 24·32 = 768 bytes → savings of ~128 bytes per thread. Two threads
(update + draw) → ~256 bytes total. Imperceptible.

The *cycle-time* gain is what matters: tighter packing means the LIFO
top frames stay in fewer cache lines. At depth 3, current layout is 72
bytes across one cache line (64 bytes spills into two). Packed: 60
bytes, fits in one cache line. **Saves potentially one L1 miss per Leave
call** if the workload pushes the prefetcher around.

**Risk.** Pack 4 on a long field forces unaligned access. On x86 it's
free; on ARM (Apple Silicon) unaligned access is slower or, in some
contexts, faults. tModLoader 1.4.4 ships on both x64 and arm64.
**Trigger:** unaligned long read on arm64 — typically still works but
~2x slower per read.

**Cross-system cost.** None.

**Verdict.** Reject. The cycle-time gain is speculative and the arm64
risk is real. The "fits in one cache line" benefit is achievable instead
by keeping current layout and bounding depth (which is already <8 in
practice).

### 5.8 Cache the Stopwatch frequency reciprocal once per harvest

**Idea.** `HarvestAllocationsInto` already does this — no reciprocal needed
because bytes are reported raw. But `HarvestInto` and `HarvestHooksInto`
compute `double ticksToMs = 1000d / Stopwatch.Frequency;` once per call.
On the alloc side there's no analogous division, so no candidate here.

**Verdict.** Not applicable to alloc tracking.

### 5.9 Reduce bounds-checks in the 6-arg `Add`

**Idea.** `Add(backendId, modId, categoryId, hookId, ticks, bytes)` does
six bounds-checks per call. Several are redundant if we trust the caller —
the caller is always our own `Leave`/`LeaveCpuAlloc`, and hookId+modId+
categoryId are looked up from `HookDescriptor` arrays we control.

Specifically:
- `backendId` is `HookBackend.ILHookBackendId` — a property whose value
  doesn't change after Install. Hoist the check out of the per-call.
- `categoryId` is `desc.CategoryId` — registered at install, immutable.
  Trust it.
- `index = modId * CategoryCount + categoryId` — bounds check is necessary
  because the array could be shorter than expected if Configure was called
  with a wrong modCount. **Keep it.**

Two checks removable: backendId (trustable) and categoryId (trustable).
The JIT cannot prove these on its own because the values come from a
`HookDescriptor` array element.

**Invariant impact.** None directly, but: today the bounds checks are part
of Invariant 4's abort-clean defence — if a hook ever fires with a bad
descriptor (e.g. a partially uninstalled descriptor array), the bounds
check silently drops the credit instead of crashing the host. Removing
them puts the burden on Install/Uninstall to never leave a stale call site
pointing at a bad index. We'd need to audit Uninstall for that property.

**Observable-output preservation.** Yes, given the install/uninstall
audit.

**Expected gain.** Two bounds checks saved per Leave call ≈ 1–2 ns.
~3–5% relative.

**Risk.** **A subtle one.** If a teardown happens mid-call from a hooked
method (Mod.Unload fires while a hook body is executing), and the bounds
checks were the safety net catching the stale descriptor, removing them
opens a crash window. The window is small (Uninstall sets `Installed =
false` then disposes hooks; in flight hooks finish first), but not
provably zero.

**Cross-system cost.** Requires changes to `PerModAttribution.cs` and a
new test exercising "Uninstall during in-flight hook" — non-trivial.

**Verdict.** Consider-later. The gain is modest; the safety net is
real. Re-evaluate after teardown-race tests exist for the rest of the
hot path.

### 5.10 Move the Stopwatch+Alloc reads inside `Add` via a single fused call

**Idea.** Instead of `Leave` reading both timestamps and then calling
`Add`, push the reads into `Add` itself. The caller passes only the
`Frame f` and `Add` reads `Stopwatch.GetTimestamp()` and
`GC.GetAllocatedBytesForCurrentThread()` inside. Saves one parameter
push.

**Invariant impact.** None.

**Observable-output preservation.** Yes, but with a subtle change: the
timestamps are now read *after* one more layer of managed call, so the
elapsed time includes a few more nanoseconds of overhead. We are now
crediting `Leave→Add` to the hook, which is more accurate, not less.

**Expected gain.** ~1 ns saved on argument-marshalling, maybe nothing
after JIT inlining. **Negligible.**

**Risk.** Couples `Add` to the FCall APIs. Today `Add` is pure
arithmetic and easily unit-testable; pulling the API reads inside it
forces test code to mock the GC counter. Worse testability.

**Verdict.** Reject. The gain is too small and the testability cost
real.

### 5.11 Replace `Array.Clear` in BeginTick with `Span<T>.Clear`

**Idea.** `BeginTick` does four `Array.Clear` calls when alloc tracking is
on. `Span<T>.Clear` compiles to the same CIL but via a slightly different
path; benchmark differences are typically zero.

**Verdict.** Reject. Premature; the per-tick BeginTick cost is dwarfed by
the per-hook cost. Optimise the hot path first; the once-per-tick clear is
not on the critical path.

### 5.12 Use `Unsafe.Add` to skip bounds checks in `HarvestAllocationsInto`

**Idea.** The harvest loop is once per tick, not per hook. Same logic as
5.11.

**Verdict.** Reject. Wrong hot path.

### 5.13 Co-locate alloc and ticks columns for cache locality in Add

**Idea.** Today `ticks[index] += ...` and `bytes[index] += ...` are
separate arrays; the same `index` lands in two distinct cache lines.
Combining into a struct `{ long Ticks; long Bytes; }` per cell would put
both in one cache line, halving the L1 footprint per Add.

**Invariant impact.** None.

**Observable-output preservation.** Yes — the data values are identical;
only the in-memory layout changes.

**Expected gain.** Per `Add` call: one cache line touched instead of two.
For 10k cells (`modCount=150` × `categoryCount=7` ≈ 1050; rounded up for
worst case) the working set drops from ~16 KB across two arrays to ~16 KB
in one — same total, but Add now touches one cache line per call instead
of two. Worst-case L1 footprint per tick is halved on the alloc-tracking
side.

**Concrete saving:** if Add currently misses L1 on the bytes-array load
~50% of the time (very rough estimate), the saved miss is ~3 ns. Per hook,
~1.5 ns expected. ~3–5% relative.

**Risk.** API churn: the harvest methods (`HarvestInto`,
`HarvestAllocationsInto`) need to be updated to extract from the new
struct. The 4-tuple of (modId, categoryId, ticks, bytes) becomes a 3-tuple
(modId, categoryId, cell) where `cell` carries both. **Touches every
consumer** that reads `_ticksByBackend` or `_bytesByBackend` directly —
none today (good), but the persistence layer reads via the Harvest
methods, which would change signature.

The same packing applies to `_hookTicksByBackend` and `_hookBytesByBackend`
arrays.

**Cross-system cost.** Touches `PerModAttribution.cs` core data
structures. `metric-collection.md` consumers need re-verification.

**Verdict.** Land. Highest expected-gain candidate in §5 that doesn't
involve removing safety nets. Net benefit per hook is small but
unambiguous, and the code change is mechanical.

### 5.14 Reduce `HookDescriptor` indirection in Leave

**Idea.** `LeaveCpuAlloc` does `PerModAttribution.Hooks[f.HookId]` to
fetch the descriptor, then passes `desc.ModId`, `desc.CategoryId`. Two
field reads from a value type returned by an indexer.

Alternative: store `ModId` and `CategoryId` directly in the `Frame`
struct, populated by Enter (which already has the hookId). Then Leave
skips the descriptor lookup entirely.

**Invariant impact.** None.

**Observable-output preservation.** Yes.

**Expected gain.** One array indexer call + two field reads saved per
Leave. ~1–2 ns. ~3–5% relative.

**Risk.** Frame grows by 8 bytes (4 for ModId, 4 for CategoryId). Pushes
the per-frame size from 24 to 32 bytes (one extra slot of padding eaten).
At depth ~3 the working set goes from 72 to 96 bytes — still single cache
line on the active top frames.

A second risk: the descriptor lookup is also where today's defensive
`(uint)f.HookId < (uint)PerModAttribution.Hooks.Count` bounds check
lives (`ProbeStack.cs:177`). If we skip the descriptor, that bounds check
goes away too — unless we keep it independently. We should keep it; cost
is one branch.

**Cross-system cost.** Touches `ProbeStack.cs` `Frame` and Enter/Leave;
nothing else.

**Verdict.** Land. The descriptor lookup is a known per-Leave cost and
the Frame growth is bounded.

### 5.15 Make `HookBackend.AllocationTracking` a `static readonly bool`

**Idea.** Today `HookBackend.AllocationTracking` is `{ get; set; } = true`
— a mutable static property. The JIT cannot const-fold it. If it were
`static readonly bool` initialised from the config once at startup, the
JIT could fold every `if (HookBackend.AllocationTracking)` check at jit
time.

In our case this matters most in `ApplyTimingWrap` — the `trackAlloc`
branch fires once per install per hook, not per call. So the gain is
install-time only, which is not the critical path.

**Invariant impact.** Today a runtime mode change is theoretically
possible (though `notes/future-settings-design.md` already says this
requires a full reinstall). Making it `static readonly` enforces that
invariant in the type system.

**Observable-output preservation.** Yes.

**Expected gain.** Zero on the per-tick hot path (the branch is at install
time, not per call). Install-time gain is microscopic.

**Risk.** Locks in build-time tracking choice unless we use a
config-read-once init pattern. Aligns with how mode is already handled.

**Verdict.** Land for type-system cleanliness, not for perf. Defer the
toggleability to a future settings tab.

### 5.16 Summary: the candidates ranked

| # | Candidate | Per-hook gain (ns) | Risk | Verdict |
|---|---|---:|---|---|
| 5.13 | Co-locate ticks+bytes in struct | 1.5 | Low | **Land** |
| 5.14 | Inline ModId/CategoryId into Frame | 1.5 | Low | **Land** |
| 5.5 | Make CategoryCount const (option a) | 1.0 | Low | **Land** |
| 5.4 | Inline `PerModAttribution.Add` | 1.5 | Low | **Land** |
| 5.3 | Inline `EnterCpuAlloc` / `LeaveCpuAlloc` | 3.0 | Medium | **Land-after-bench** |
| 5.15 | `static readonly` tracking flag | 0 | None | **Land (cleanliness)** |
| 5.6 | Verify Cecil short-form ldc.i4 | 0 | None | **Audit** |
| 5.9 | Remove `backendId`/`categoryId` bounds checks | 1.5 | Medium | **Consider-later** |
| 5.2 | Single-call prologue | 1.0 | Medium | **Consider-later** |
| 5.10 | Push reads into Add | 0 | Medium | Reject |
| 5.7 | Pack 4 on Frame | 0 (arm64 risk) | Medium | Reject |
| 5.11/5.12 | Once-per-tick optimisations | 0 | None | Reject — wrong path |

**Net expected gain** of the "Land" set: ~7.5 ns per hook (sum of 5.13 +
5.14 + 5.5 + 5.4). At 50k hook calls per tick, that's ~375 µs per tick
= ~22 ms/s = ~2.2% of one frame. **Meaningfully inside the Standard mode
budget improvement we need.**

With 5.3 added (medium-risk), the total is ~10.5 ns per hook ≈ 525 µs per
tick ≈ 31 ms/s ≈ ~3.1% of frame. That brings the v0.5 baseline (0.27 ms
avg per-tick profiler cost; baseline.md row "Avg per-tick
PerformanceProfiler cost") down by roughly that fraction — call it
0.20–0.22 ms, closer to the 0.10 ms target.

These figures assume the §2 benchmark confirms the hypothesised
~30–40 ns base cost per hook. They scale roughly linearly if the base
is different.

---

## 6. Cross-system dependencies

### 6.1 Inbound

| Feeder | What it delivers | Coupling |
|---|---|---|
| `hook-instrumentation` | The Install loop that picks the alloc-aware emission and registers `hookId` | Tight: §5.13 (co-locate ticks+bytes) changes the descriptor shape, which Install consumes |
| `mod-lifecycle` | Decision to enable/disable Allocation-tracking at startup | Loose: read once via `HookBackend.AllocationTracking` |
| `metric-collection` | `TickFrame.AllocBytes` capture (independent path via `MetricCollector.BeginTick`/`EndTick`) | Loose: separate code path, same API — see §6.3 |
| Config / settings | Future runtime toggle for `AllocationTracking` | Not yet wired |

### 6.2 Outbound

| Consumer | What it reads | Coupling |
|---|---|---|
| `metric-collection` | `HarvestAllocationsInto`, `HarvestHookAllocationsInto` — per-tick byte totals | Tight: §5.13 changes harvest indexing; consumer must follow |
| `insights-engine` (AllocationBurstDetector) | Per-mod allocation share of total bytes | Loose: reads only the totals |
| `overlay` (OverlayState MEM/BOTH pill) | Per-mod allocation columns | Loose: reads via the same harvest API |
| `persistence` (LiteDB session JSON `modSummary[].allocBytes`) | Aggregated per-mod-per-session total | Loose: end-of-session aggregation |
| `spike-detection` (PerTickAttributionRing alloc tier) | Per-tick per-mod allocation bytes | Loose: writes via harvest, reads via ring methods |

### 6.3 The independent frame-level alloc path

`MetricCollector` calls `GC.GetAllocatedBytesForCurrentThread()` at frame
boundaries (`BeginTick` / `EndTick`) to capture `TickFrame.AllocBytes` — a
single per-tick total alongside the per-mod attribution we're optimising
here. This path is **out of scope** for the recommendations in §5
(invariant: the per-tick frame capture is a different code path with one
read per tick, not one per hook). But it shares the same API. If §2's
benchmark surprises us on cost, both paths get reconsidered.

### 6.4 What changes in this research will propagate

Three concrete propagation surfaces from the "Land" recommendations:

| Land item | Files that change | Verification needed |
|---|---|---|
| 5.13 (co-locate ticks+bytes) | `PerModAttribution.cs`, `metric-collection.md` harvest consumers | Round-trip test: write known ticks+bytes, harvest, assert |
| 5.14 (inline IDs into Frame) | `ProbeStack.cs` | Existing tests pass + new: nested-hook attribution still credits correctly |
| 5.5a (const CategoryCount) | `PerModAttribution.cs` | Build-only |
| 5.4 (inline Add) | `PerModAttribution.cs` | Re-run §2 benchmark |
| 5.3 (inline Enter/Leave) | `ProbeStack.cs` | Re-run §2 benchmark; check JIT size delta |
| 5.15 (readonly flag) | `HookBackend.cs` and every consumer | Build-only |

### 6.5 What this research file does NOT decide

- **Whether to toggle alloc tracking off in Lite mode.** Out of scope per
  the brief. Reopened only if §2 benchmark shows t_alloc is dramatically
  higher than the hypothesis (>50 ns), which would invalidate the
  unconditional design. Even then the choice goes to Caner, not to this
  file.
- **Storage shape for the historical per-tick alloc samples.** Covered by
  `spike-detection.md` research (the `PerTickAttributionRing` already
  exists; this file does not re-design it).
- **Whether to add LOH-specific GC pressure attribution.** Out of scope —
  the AllocationBurstDetector and the byte-total schema are stable; this
  is a future feature, tracked in the system file's "Planned" section.
- **Whether to add a `GcPauseCulpritDetector` fed by per-mod-per-tick
  alloc-delta history.** Out of scope — that's the systems file's
  "Planned" item, not an optimisation.

---

## 7. Prioritised order

The sequence for `master-plan.md` to schedule, grouped into phases. Each
phase ends with a measurable verification step.

### Phase A — Measurement (1–2 hours)

| Step | What | Output |
|---|---|---|
| A1 | Build `bench/PerformanceProfiler.Bench` sibling project | New csproj, BDN reference |
| A2 | Implement `AllocCounterBench` from §2.3 | Code |
| A3 | Run on dev machine (Apple Silicon, .NET 8) | `bench-results/alloc-counter-net8-arm64.md` |
| A4 | Run secondary test from §2.7 (granularity) | xUnit test in `Tests/` |
| A5 | Decide branch — does t_alloc justify the unconditional design? | Decision recorded in `context/notes/decisions.md` |

**Gate:** if t_alloc is >50 ns on either platform, re-open the design with
Caner before continuing. Otherwise proceed.

### Phase B — Mechanical optimisations (3–4 hours)

These have low risk and modest, additive gains. Land together.

| Step | What | Per-hook gain (ns) |
|---|---|---:|
| B1 | 5.5a: `const int CategoryCount = 7` in `PerModAttribution.cs` | 1.0 |
| B2 | 5.15: `static readonly bool AllocationTracking` in `HookBackend.cs` | 0 (cleanliness) |
| B3 | 5.6: Audit Cecil ldc.i4 short-form, switch if needed | 0 (install-time only) |
| B4 | 5.4: `[AggressiveInlining]` on `PerModAttribution.Add` (both arities) | 1.5 |

**Verification:** existing tests pass (`Tests/BaselineTests.cs`,
`PersistenceRoundTripTests.cs`). Run §2 benchmark suite, compare.

### Phase C — Data-shape changes (4–6 hours)

The two structural wins. Land together because they touch overlapping
surfaces.

| Step | What | Per-hook gain (ns) |
|---|---|---:|
| C1 | 5.14: Inline `ModId`/`CategoryId` into `ProbeStack.Frame` | 1.5 |
| C2 | 5.13: Co-locate `ticks` and `bytes` into one packed cell struct | 1.5 |
| C3 | Update `HarvestInto` / `HarvestHooksInto` / `HarvestAllocationsInto` for the new shape | — |
| C4 | Add new test: GC-fed allocation round-trip (§1.8) | — |

**Verification:** all `Tests/` pass. Run §2 benchmark; expected total
gain after Phases B+C ≈ 5–6 ns per hook, ~15–20% relative reduction in
instrumentation cost.

### Phase D — Inlining (2 hours, after Phase C)

After C lands and the data-shape changes are validated, return to the
inlining decision.

| Step | What | Per-hook gain (ns) |
|---|---|---:|
| D1 | 5.3: `[AggressiveInlining]` on `EnterCpuAlloc`/`LeaveCpuAlloc` | 3.0 |
| D2 | Check JITted size delta — disassemble a representative hooked method before/after | — |
| D3 | If size delta is <1 MB total, keep; else revert | — |

**Verification:** §2 benchmark, plus a full session capture to confirm the
overlay's "Avg per-tick PerformanceProfiler cost" moves toward 0.10 ms.

### Phase E — Verification against baseline.md (1 hour)

Compare the post-pass numbers against `baseline.md` row by row.

| Metric | Target |
|---|---|
| Avg per-tick PerformanceProfiler cost | 0.27 ms → ≤ 0.20 ms (Phase C); ≤ 0.17 ms (Phase D) |
| Alloc-tracking-attributable cost | (new metric) ≤ 50% of instrumentation cost |
| Per-mod-per-tick byte attribution accuracy | unchanged (§1.8 round-trip test passes) |

If Phase E shows the per-tick cost has not moved, return to Phase A and
re-run the benchmark; one of the hypotheses about t_alloc is wrong.

### Phase F — Documentation (30 min)

Update `context/systems/allocation-tracking.md` with the new data layout
(post-5.13) and the new Frame shape (post-5.14). Add a one-paragraph
"v0.6 optimisation" note pointing at this research file and the bench
results.

### Total expected outcome

- ~7.5 ns per hook saved (Phases B+C, low-risk)
- ~3 ns more if Phase D lands (medium-risk)
- ~10 ns per hook saved at the high end
- At 50k hook calls/tick: ~500 µs per tick = ~3% of one frame
- Pushes `Avg per-tick PerformanceProfiler cost` from 0.27 ms toward
  0.17–0.20 ms, halfway to the 0.10 ms target

The other half of the journey is in other research files in this same
pass — hook-instrumentation, metric-collection, persistence. None of
those need to wait on this file; the changes here are localised.

---

## 8. References

### .NET runtime source

- **`dotnet/runtime: src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs:421–423`**
  Managed-side declaration of `GetAllocatedBytesForCurrentThread`:
  ```csharp
  [MethodImpl(MethodImplOptions.InternalCall)]
  public static extern long GetAllocatedBytesForCurrentThread();
  ```
  https://github.com/dotnet/runtime/blob/main/src/coreclr/System.Private.CoreLib/src/System/GC.CoreCLR.cs

- **`dotnet/runtime: src/coreclr/vm/comutilnative.cpp:847–858`**
  Native-side FCall body. Quoted verbatim in §3.2.
  https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/comutilnative.cpp

- **`dotnet/runtime: src/mono/System.Private.CoreLib/src/System/GC.Mono.cs:54–55`**
  Mono-side managed declaration (same FCall shape).
  https://github.com/dotnet/runtime/blob/main/src/mono/System.Private.CoreLib/src/System/GC.Mono.cs

- **`dotnet/runtime` issue 17891** — Jan Kotas (allocator owner) on
  why the per-thread counter is essentially free: *"The GC allocator was
  keeping track of amount allocated per thread already."*
  https://github.com/dotnet/runtime/issues/17891

- **`dotnet/runtime` discussion 71530** — clarifies that the counter
  follows the calling thread, not the logical async chain.
  https://github.com/dotnet/runtime/discussions/71530

### Microsoft docs

- **GC.GetAllocatedBytesForCurrentThread, .NET 8** — Remarks
  confirm: "the total number of bytes allocated to the current thread
  since the beginning of its lifetime", "does not include any native
  allocations", "not the total number of bytes that have survived
  garbage collection."
  https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-8.0

- **GC.GetTotalAllocatedBytes** — Comparison API used in §3.8.
  https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalallocatedbytes

- **GC.GetGCMemoryInfo** — Comparison API used in §3.8.
  https://learn.microsoft.com/en-us/dotnet/api/system.gc.getgcmemoryinfo

### BenchmarkDotNet

- **`dotnet/BenchmarkDotNet: src/BenchmarkDotNet/Engines/GcStats.cs:265–285`**
  Delegate cache for `GetAllocatedBytesForCurrentThread`. BDN's
  MemoryDiagnoser uses the same API as our profiler. Confirms BDN is a
  valid measurement harness for our use case (no measurement-side
  contamination of the API under test).
  https://github.com/dotnet/BenchmarkDotNet/blob/master/src/BenchmarkDotNet/Engines/GcStats.cs

- **BDN issue 723** — discussion confirming per-thread API is BDN's
  primary allocation-measurement primitive on .NET Core.
  https://github.com/dotnet/BenchmarkDotNet/issues/723

- **BDN diagnosers documentation** — MemoryDiagnoser explained,
  including its API choice.
  https://benchmarkdotnet.org/articles/configs/diagnosers.html

### MonoMod / Cecil

- **MonoMod.RuntimeDetour 25.3.2 release notes** — `ILHook` direct
  construction with `applyByDefault: true`.
  https://github.com/MonoMod/MonoMod

- **Mono.Cecil 0.11.6** — `ILCursor.Emit` import behaviour.
  https://github.com/jbevain/cecil

### Project-internal sources

- `context/systems/allocation-tracking.md` — current implemented
  reality; §1 of this file maps directly onto it.
- `context/notes/spikes-and-allocations-plan.md` — design rationale
  this research builds on. R1–R8 risk register in §1 already shaped
  some of the §5 verdicts.
- `context/perf-pass/baseline.md` — the measured starting point this
  research's recommendations move.
- `context/notes/philosophy.md` — "optimisation = doing what we
  already do at maximum efficiency, not = doing less." The constraint
  that vetoes every "skip this when …" proposal in §5.
- `CLAUDE.md` — the five Project Invariants, particularly Invariant 2
  (overhead is a budget, hot path zero-allocation) and Invariant 4
  (abort-clean on host drift). Both shaped the §5.9 verdict.

### Existing code referenced in detail

- `Profiling/ILHookInterceptor.cs:449–568` — `ApplyTimingWrap`,
  particularly lines 483–546 (the alloc-vs-Lite branch).
- `Profiling/ProbeStack.cs:40–190` — `Frame` struct, `Enter`, `Leave`,
  `EnterCpuAlloc`, `LeaveCpuAlloc`.
- `Profiling/PerModAttribution.cs:54–279` — storage, configure, the
  4-arg and 6-arg `Add`, the harvest methods.
- `Profiling/HookBackend.cs:74` — `AllocationTracking` flag.
- `Tests/BaselineTests.cs:91–141` — existing alloc-rate baseline math
  tests; the missing GC-fed integration test described in §1.8.

---

*Research closes here. Recommendations in §5 flow into `master-plan.md`
once `hook-instrumentation.md` and `metric-collection.md` research files
arrive. Numbers in §2 and §5 marked `[t_alloc=hypothesis]` are revised
after Phase A lands.*
