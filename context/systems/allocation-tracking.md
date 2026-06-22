# Allocation Tracking

*Maturity: working · Stability: stable.*

## Scope / Purpose

Allocation tracking attributes per-tick managed-heap allocation bytes to individual mods, parallel to CPU attribution. In the MEM or BOTH metric mode the rendered surface shows allocation columns alongside (or instead of) CPU columns. The data also feeds the `AllocationBurstDetector` in the insights engine.

The mechanism is an alternate IL emission shape in `ILHookInterceptor`: the manipulator wraps each method with `ProbeStack.EnterCpuAlloc(hookId, gcBytesAtEnter)` and `ProbeStack.LeaveCpuAlloc()` instead of the cheap `Enter/Leave` pair. The leave reads the post-counter internally so the prologue stays a single `call` instruction.

## Boundaries / Ownership

Files (shared with hook instrumentation and metric collection):

- IL emission: `Profiling/ILHookInterceptor.cs` (`ApplyTimingWrap`'s alloc branch, `_enterCpuAllocMethod` / `_leaveCpuAllocMethod`).
- Probe targets: `Profiling/ProbeStack.cs` (`EnterCpuAlloc`, `LeaveCpuAlloc`).
- Storage: `Data/Aggregators/PerModAttribution.cs` (parallel alloc-byte columns, allocated only when `HookBackend.AllocationTracking == true`; moved out of `Profiling/` in v0.11).
- Switch: `Profiling/HookBackend.AllocationTracking` flag.

Owns:

- The alloc-aware IL emission shape.
- The decision (at `Configure` time) whether to allocate alloc-byte columns.
- The CPU / MEM / BOTH metric-mode behaviour the renderer reads (live surface is the dashboard; the in-game MEM/BOTH overlay pill is archived under `UI/`).

Does not own:

- The CPU-only emission shape — that is the base case in `ILHookInterceptor`.
- The frame-level `TickFrame.AllocBytes` capture (independent path via `GC.GetAllocatedBytesForCurrentThread()` at `Begin/EndTick`).

## Current Implemented Reality

### IL emission branches

`ApplyTimingWrap` (`ILHookInterceptor.cs:483-489`) chooses one of two shapes:

```csharp
bool trackAlloc = HookBackend.AllocationTracking;
MethodInfo leaveTarget  = trackAlloc ? _leaveCpuAllocMethod  : _leaveMethod;
MethodInfo enterTarget  = trackAlloc ? _enterCpuAllocMethod  : _enterMethod;
```

Prologue (`ILHookInterceptor.cs:540-546`):

```
CPU-only:
    ldc.i4 hookId
    call ProbeStack.Enter(int32)

CPU+alloc:
    ldc.i4 hookId
    call GC.GetAllocatedBytesForCurrentThread()  // pushes long
    call ProbeStack.EnterCpuAlloc(int32, int64)
```

The finally always emits a single `call` to either `Leave` or `LeaveCpuAlloc` — the alloc-aware leave reads `GC.GetAllocatedBytesForCurrentThread()` internally and computes the delta. Keeping the finally as a parameterless call is what lets the same exception-handler shape work for both variants.

### `ProbeStack.EnterCpuAlloc` / `LeaveCpuAlloc`

`Profiling/ProbeStack.cs:130+`. The Enter call stores `(hookId, allocBytesAtEnter, Stopwatch.GetTimestamp())` in a thread-local stack frame. The Leave call:

1. Reads `Stopwatch.GetTimestamp()` and the current alloc-bytes counter.
2. Pops the top stack frame.
3. Calls `PerModAttribution.Add(modId, categoryId, hookId, cpuDelta)`.
4. Calls `PerModAttribution.AddAlloc(modId, categoryId, hookId, allocDelta)`.

The (modId, categoryId) for the hookId comes from `PerModAttribution.Hooks[hookId]` — registered at install time via `RegisterHook` / `RegisterOrReuseHook`.

### Configure-time allocation

`PerModAttribution.Configure(modCount, backendCount, allocTracking)` reads `allocTracking` and conditionally allocates the parallel alloc-byte columns. With `allocTracking = false`, alloc-write code paths still exist but their target arrays are `null` or empty; the writes no-op. With `allocTracking = true`, the columns are sized parallel to the CPU columns.

### Metric mode (CPU / MEM / BOTH)

`OverlayState.MetricMode` cycles through `CPU` / `MEM` / `BOTH`:

- `CPU` — rows show CPU ms only.
- `MEM` — rows show allocation KB/s only.
- `BOTH` — rows show CPU ms with an allocation annotation underneath.

`OverlayState` is part of the in-game overlay archived in v0.9.0 (under `UI/`); the live surface is the browser dashboard (`systems/web-dashboard.md`), which exposes the same column selection. The mode semantics above describe the column choice any renderer reuses.

## Key Interfaces / Data Flow

```
HookBackend.AllocationTracking (bool, build-time today)
    │
    ├─ PerModAttribution.Configure(modCount, backendCount, AllocationTracking)
    │       conditionally allocates _allocBytesPerModHook[]
    │
    └─ ILHookInterceptor.ApplyTimingWrap reads AllocationTracking
            picks EnterCpuAlloc/LeaveCpuAlloc vs Enter/Leave

per dispatch:
    [emitted prologue] ldc.i4 hookId
                       call GC.GetAllocatedBytesForCurrentThread   (alloc only)
                       call ProbeStack.EnterCpuAlloc(hookId, allocAtEntry)
    [original body]
    [emitted finally]  call ProbeStack.LeaveCpuAlloc()
                            reads timestamp + alloc-bytes counter
                            computes cpuDelta + allocDelta
                            PerModAttribution.Add(modId, cat, hook, cpuDelta)
                            PerModAttribution.AddAlloc(modId, cat, hook, allocDelta)

overlay/insights consumers:
    PerModAttribution.AllocBytesForMod(modId)
    AllocationBurstDetector reads per-mod alloc share
```

## Implemented Outputs / Artifacts

| Surface | Source |
|---------|--------|
| Dashboard MEM column (archived Overview/Tree/Spikes tabs) | `PerModAttribution.AllocBytesForMod / Hook` |
| `AllocationBurst` insight | `AllocationBurstDetector` reads alloc share |
| Persisted `modSummary[].allocBytes` (when tracking on) | aggregated `PerModAttribution` via `SessionRecorder` |

## Known Issues / Active Risks

- **`HookBackend.AllocationTracking` is a build-time constant.** Today it is true in development. A player-facing toggle is sketched in `notes/future-settings-design.md`; until then, every Workshop user runs with allocation tracking on. Overhead implication: the alloc-path adds one `call GC.GetAllocatedBytesForCurrentThread()` per detour (a cheap BCL read), still well inside the budget.
- **`GC.GetAllocatedBytesForCurrentThread()` is a per-thread counter.** Terraria runs the main update loop on one thread, so single-thread attribution is correct. If a mod ever spawns its own thread and does work there during a dispatched hook, that allocation would not show up in our tracking — but it also would not affect the player's main-thread frame time, so the gap is in the same direction as the rest of the profiler's focus.
- **Sub-byte accuracy.** `GC.GetAllocatedBytesForCurrentThread()` increments at allocation granularity, not byte granularity; allocations smaller than a slot boundary may not increment between Enter and Leave. For a profiler reporting KB/s, this is below noise.

## Partial / In Progress

Nothing in progress.

## Planned / Missing / Likely Changes

- **Settings UI toggle for `HookBackend.AllocationTracking`.** See `notes/future-settings-design.md`.
- **Per-mod-per-tick alloc-delta history** would feed the gated `GcPauseCulpritDetector`. Today only the running per-detour total is retained; the historical stream is lost.

## Durable Notes / Discarded Approaches

- **Allocation tracking was originally a separate `MemHookInterceptor`.** Folded into `ILHookInterceptor` with the alloc-branch in `ApplyTimingWrap` so the same install loop covers both. Two `ProbeStack` method pairs (`Enter/Leave` vs `EnterCpuAlloc/LeaveCpuAlloc`) keep the emitted IL minimal for the CPU-only path; the leave-side read-internal design avoids needing a second emitted `call` in the finally.
- **The finally always calls a parameterless leave.** Tested designs that passed the post-counter as an argument required emitting `call GC.GetAllocatedBytesForCurrentThread()` inside the finally region, which conflicts with the simpler exception-handler shape. The current design keeps the finally as a single static call regardless of variant.

## Obsolete / No Longer Relevant

Nothing.

## Cross-references

- `systems/hook-instrumentation.md` — the ILHook install loop that picks the alloc-aware emission.
- `systems/metric-collection.md` — `TickFrame.AllocBytes` (independent path, tick-scoped).
- `systems/insights-engine.md` — `AllocationBurstDetector`.
- `systems/web-dashboard.md` — the live MEM/BOTH surface; the in-game metric pill is archived under `UI/`.
- `notes/decisions.md` — the spikes-and-allocations design rationale (the per-feature plan note was folded in here).
