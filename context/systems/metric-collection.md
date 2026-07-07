# Metric Collection

*Maturity: working · Stability: stable — the per-tick frame engine has been steady since M1; recent changes are additive (alloc columns, backend divergence).*

## Scope / Purpose

Metric collection is the per-tick frame engine. It opens a tick at `PreUpdateEntities`, accumulates per-mod CPU and allocation deltas through whichever backend(s) the hook-instrumentation subsystem has installed, and closes the tick at `PostUpdateEverything` by sealing a `TickFrame` into the ring buffer.

This is the layer everything downstream reads from: the dashboard stats query `MetricCollector.History` and the per-mod accumulators; the spike detector consumes raw per-tick samples; the `SessionRecorder` rolls up totals into LiteDB; the insights engine reads aggregated state.

## Boundaries / Ownership

Files: `Profiling/MetricCollector.cs`, `Profiling/RingBuffer.cs`, `Profiling/TickFrame.cs`. The per-mod accumulators moved into `Data/` in v0.11: `Data/Aggregators/PerModAttribution.cs`, `Data/Aggregators/PerModSample.cs`, `Data/Aggregators/PerTickAttributionRing.cs`.

Owns:

- The `RingBuffer<TickFrame>` (30-second rolling history at 60 Hz = 1800 frames).
- The `PerModAttribution` accumulator (per `(modId, categoryId, hookId)` ticks + optional alloc bytes).
- Per-tick allocation column reads via `GC.GetAllocatedBytesForCurrentThread()`.
- `BackendTotalMs0` / `BackendTotalMs1` and `BackendDivergence` for Parallel-mode comparison.
- The `BeginTick` / `EndTick` lifecycle paired with `ProfilerSystem`.

Does not own:

- The detour wrap itself — see `systems/hook-instrumentation.md`.
- Spike detection over the raw history — see `systems/spike-detection.md`.
- Tag/context snapshotting — see `systems/events-and-context.md`.

## Current Implemented Reality

### `TickFrame`

Per-tick observation struct (`Profiling/TickFrame.cs`):

- `TickIndex` (`long`) — `Main.GameUpdateCount` at close-of-tick.
- `FrameTimeMs` (`double`) — **update-window work only**: the span from `BeginTick`
  (`PreUpdateEntities`) to `EndTick` (`PostUpdateEverything`). This is the Update half of the
  game loop; it excludes the Draw phase and the profiler's own post-timestamp harvest, so it is
  NOT the player-facing frame time. (v0.28.0, A1: before this, `FrameTimeMs` *was* treated as the
  frame time, which is why a draw-bound slow-motion session read a healthy ~3 ms while the game
  crawled — the dashboard said "60 fps smooth" during genuine slow-motion.)
- `RealFrameTimeMs` (`double`) — **the honest frame period** (v0.28.0, A1): wall time between
  consecutive `BeginTick` stamps, spanning the whole loop (Update + Draw + vsync sleep). Locked at
  60 fps it reads ~16.7 ms; in slow-motion it rises to the true elongated period. Carries a
  one-frame lag (only knowable at the next `BeginTick`), invisible to the per-second aggregates.
  The `TickDownsampler` feeds THIS into `WarmAggregate.AvgFrameMs`, and `SessionRecorder`'s "worst
  frame" uses it — so every player-facing frame number is now the real one. The spike detector +
  `Baseline` still key off the update-window `FrameTimeMs` (internally consistent work-vs-median);
  the stall detector already used the real inter-`BeginTick` period.
- `AllocBytes` (`long`) — `GC.GetAllocatedBytesForCurrentThread()` delta across the tick.
- `NpcCount`, `ProjectileCount`, `DustCount` (`int` each) — entity counts at close-of-tick.
- `Context` (`EventContext`) — the `ContextTagger.Snapshot` for the tick (biome/boss/weather/invasion). Optional; zeroed if no tagger is alive.
- Per-mod totals are **not** in `TickFrame`. They live on `PerModAttribution` and are queried by ModId; the ring buffer carries only the frame-level scalars.

### `PerModAttribution`

The wide accumulator (`Data/Aggregators/PerModAttribution.cs`). Conceptually a 3D `(modId, categoryId, hookId)` grid plus per-backend slots when Parallel mode runs. The shape is set at `PostSetupContent`:

```
PerModAttribution.Configure(modCount, backendCount, allocTracking)
```

- `modCount` — number of mods (from `HookInterceptor.ProfiledMods`).
- `backendCount` — 1 in single-mode, 2 in Parallel.
- `allocTracking` — whether to allocate parallel alloc-byte columns.

Both backends call:

```
PerModAttribution.Add(modId, categoryId, hookId, deltaTicks)
PerModAttribution.AddAlloc(modId, categoryId, hookId, deltaBytes)  // alloc path only
```

Hookid registration is via `RegisterHook` (single-backend mode) or `RegisterOrReuseHook` (Parallel mode). The Parallel-mode reuse ensures both backends write to the same row for the same `(modId, categoryId, displayName)` tuple — divergence comparisons would be meaningless otherwise.

### Per-hook averages via EMA, and profiler self-overhead (v0.28.0)

Two rework facts the harvest now carries:

- **Per-hook rolling average is a slow EMA, not a windowed mean (B1).** The old windowed mean
  needed a `_perHookHistoryMs` ring of `HookCount * 1800` doubles — on a 62k-hook stack that is
  ~896 MB *per array*, ~1.8 GB with the bytes twin, allocated at world-entry (a prime "ran out of
  RAM" cause). It was removed; `PerHookAverageMs`/`PerHookAverageBytes` are now EMAs with
  `alpha = 2/(1800+1)` (same ~30 s horizon, O(HookCount) memory). The per-**mod** average stays an
  exact windowed mean (its ring is small: `modCount*cats*1800`). All EMA folds flush values below
  `DenormalFloor = 1e-12` to zero (C2) — an idle-cost EMA otherwise bottoms out as a subnormal
  double that serialised as garbage AND hits the x86 subnormal penalty every tick.
- **The profiler measures its own per-tick cost (A2 + A3).** `EndTick` captures its end timestamp
  BEFORE its harvest runs, so the harvest/smoothing/detector work was invisible to `FrameTimeMs`.
  It is now timed (`harvestMs`) and folded into `ProfilerSelfHealth.HarvestMsEma`; `ProbeStack`
  counts instrumented calls (`_callCount`, read+reset per tick via `TakeCallCount`) into
  `ProbeCallsPerTickEma`. Both surface on the Self tab (`harvest / tick`, `probe calls / tick`) so
  the profiler's true cost is visible, not hidden — the "verifiable, not trusted" claim made honest.

### `BeginTick` / `EndTick`

```
BeginTick():
    if _tickOpen: return  // guard against double-open
    _tickStartTs = Stopwatch.GetTimestamp()
    _tickEntryAllocBytes = GC.GetAllocatedBytesForCurrentThread()
    PerModAttribution.SnapshotForTick()  // copy current row state into a per-tick scratch
    _tickOpen = true

EndTick(tickIndex, npcCount, projectileCount, dustCount):
    if !_tickOpen: return  // partial frame: PreUpdateEntities did not fire
    long exitTs = Stopwatch.GetTimestamp()
    long exitAlloc = GC.GetAllocatedBytesForCurrentThread()
    TickFrame frame = new TickFrame {
        TickIndex      = tickIndex,
        FrameTimeMs    = TicksToMs(exitTs - _tickStartTs),
        AllocBytes     = exitAlloc - _tickEntryAllocBytes,
        NpcCount       = npcCount,
        ProjectileCount= projectileCount,
        DustCount      = dustCount,
        // Context is stamped separately by ContextTagger.Snapshot
    }
    _ring.Push(frame)
    _spikeDetector.Observe(frame)
    PerModAttribution.CloseTick()  // compute per-mod deltas for this tick
    if HookBackend.Mode == Parallel:
        _backendTotalMs0 += PerModAttribution.LastTickBackendMs(0)
        _backendTotalMs1 += PerModAttribution.LastTickBackendMs(1)
        if abs divergence > threshold: _divergenceLogTrigger = true
    _tickOpen = false
```

### `BackendDivergence`

Parallel-mode metric (`MetricCollector.cs:167`). Computed as a relative delta between the two backends' running totals:

```
BackendDivergence = (BackendTotalMs1 - BackendTotalMs0) / max(BackendTotalMs0, 1)
```

`ConsumeDivergenceLogTrigger()` returns true and resets the trigger once. The `[backend-compare]` log line is emitted from `ProfilerSystem.PostUpdateEverything` only when triggered, to avoid spamming `client.log`.

### `FlushSpikes`

`MetricCollector.FlushSpikes()` delegates to `_spikeDetector.Flush()` (`MetricCollector.cs:239`). Called from `ProfilerSystem.KickOffSessionEndAsync()` (run from `PreSaveAndQuit` or `OnWorldUnload`) **before** `SessionRecorder.End()` so an in-progress spike window lands in the persisted session.

### Ring buffer

`RingBuffer<T>` is a generic fixed-capacity circular buffer (`Profiling/RingBuffer.cs`). Capacity = `30 * 60 = 1800` frames (30 seconds at 60 Hz, hard-coded in `ProfilerSystem.HistoryCapacity`). Pinned by `Tests/RingBufferTests.cs`.

`Push(item)` writes to the next slot and advances `Newest`/`Count`. Wrap-around is the steady state after the first 1800 frames. Access patterns:

- `History.Newest` — most recent frame.
- `History.Count` — number of valid entries (caps at capacity).
- `History[i]` — indexer with the convention that `[0]` is the oldest, `[Count-1]` is the newest.

### `PerTickAttributionRing`

A separate 50-window ring (`Data/Aggregators/PerTickAttributionRing.cs`) that retains per-tick per-mod CPU samples for spike attribution. The 30-second `RingBuffer<TickFrame>` carries frame-level scalars only; the spike detector needs per-tick per-mod attribution to answer "which mod was responsible for that 60ms spike?" The 50-window ring is the answer.

## Key Interfaces / Data Flow

```
PostSetupContent:
   PerModAttribution.Configure(modCount, backendCount, allocTracking)

per tick:
   ProfilerSystem.PreUpdateEntities → Collector.BeginTick()
       _tickStartTs = Stopwatch.GetTimestamp()
       _tickEntryAllocBytes = GC.GetAllocatedBytesForCurrentThread()
       PerModAttribution.SnapshotForTick()

   [for each hook dispatched by tModLoader]:
       HookProbe.Time*(orig, args) or ILHook prologue/finally:
           PerModAttribution.Add(modId, categoryId, hookId, deltaTicks)
           PerModAttribution.AddAlloc(...) // alloc path only

   ProfilerSystem.PostUpdateEverything → Collector.EndTick(tickIndex, counts)
       _ring.Push(new TickFrame { ... })
       _spikeDetector.Observe(frame)
       PerModAttribution.CloseTick()

readers (dashboard stats, SessionRecorder, insights detectors):
   collector.History → TickFrame[]
   collector.PerModSamples (cached aggregates)
   collector.SpikeDetector.Windows
   collector.BackendDivergence (Parallel mode only)

session end (PreSaveAndQuit or OnWorldUnload → KickOffSessionEndAsync):
   collector.FlushSpikes()  // before SessionRecorder.End()
```

## Implemented Outputs / Artifacts

The overlay tabs named below (Overview/Tree/Spikes/Events) are part of the archived in-game overlay (v0.9.0); the live player surface is the browser dashboard (see `systems/web-dashboard.md`). The `Data/Stats/` adapters that feed the dashboard read this same layer.

| Surface | Source |
|---------|--------|
| Dashboard frame-time / GC / entity-count chrome | `MetricCollector.History.Newest` |
| Leaderboard rows (archived OverviewTab / dashboard) | `MetricCollector` per-mod totals |
| Per-`(mod, category, hookId)` rows (archived TreeTab / dashboard) | `PerModAttribution` |
| Spike windows + per-mod attribution | `SpikeDetector.Windows` + `PerTickAttributionRing` |
| Event dimension buckets | `EventAggregator` reading `TickFrame.Context` |
| Persisted session `modSummary` block | per-mod totals via `SessionRecorder` |
| InsightsEngine input | every detector's `Evaluate(collector, …)` reads through this layer |

## Known Issues / Active Risks

- **`DustCount` iterates `Main.dust` (~6000 slots) every tick.** Acceptable for M1 (a few thousand bool checks, microseconds). If a later overhead measurement flags it, switch to Lite-mode sampling cadence rather than scanning every tick (a comment in `ProfilerSystem.cs:246-250` carries this note).
- **`PerModAttribution.Configure` is called once at `PostSetupContent`** and never re-called. A `Mods → Reload` would reset everything via `Mod.Unload` → next session's `PostSetupContent`. If the modlist changes mid-process some other way (impossible today, but the codebase is not defensive about it), the accumulator shape would not match `HookInterceptor.ProfiledMods` and writes would misalign.
- **`SnapshotForTick` and `CloseTick` are paired but not asserted.** A bug that called `Add` without a matching `BeginTick` would silently mis-attribute. Today the only callers are the two interceptor backends, both correct.
- **Backend divergence threshold is process-constant.** No overlay surface, no settings UI, no logging cadence control. Today the audit is satisfied; if Parallel mode becomes a player-visible feature, this needs revisiting.

## Partial / In Progress

Nothing in progress. The subsystem is the load-bearing baseline that every audit round took as given.

## Planned / Missing / Likely Changes

- **Dust-count sampling cadence.** Conditional on future overhead measurement.
- **Per-tick allocation tracking expansion.** Currently `EnterCpuAlloc/LeaveCpuAlloc` measures per-detour alloc bytes; per-tick alloc is also captured in `TickFrame.AllocBytes`. The two paths are independent. A future per-mod-per-tick alloc-delta history would feed the gated `GcPauseCulpritDetector`.

## Durable Notes / Discarded Approaches

- **`Stopwatch.GetTimestamp()` always**, never `new Stopwatch()`. Documented in `notes/conventions.md §5`. The class would allocate per call; the static `long` read does not.
- **`PerModSample[]` is pre-allocated.** No `List<PerModSample>` on the hot path. The convention is in `notes/conventions.md §6`.
- **Ring buffer wrap-around is the steady state.** Tests/`RingBufferTests.cs` pins the wrap semantics; a regression in wrap would break Spike detection and the overlay's 30-second window.

## Obsolete / No Longer Relevant

- **Two-arg `PerModAttribution.Add(modId, categoryId, ticks)`.** Removed in commit `77a99d2` (audit potential-issue #4). The per-hook attribution model needs hookId, so the two-arg overload was dead.

## Cross-references

- `systems/hook-instrumentation.md` — the layer that calls `PerModAttribution.Add`.
- `systems/spike-detection.md` — consumes `TickFrame` stream and `PerTickAttributionRing`.
- `systems/allocation-tracking.md` — the `EnterCpuAlloc`/`LeaveCpuAlloc` path that writes alloc columns.
- `systems/events-and-context.md` — `ContextTagger.Snapshot` stamps `TickFrame.Context`.
- `systems/persistence.md` — the `SessionRecorder` that rolls up per-mod totals into LiteDB.
- `tmodloader/lifecycle-and-loop.md` — `PreUpdateEntities` / `PostUpdateEverything` as the tick boundaries.
- `Tests/RingBufferTests.cs` — pins ring-buffer wrap-around.

## The 2026-07-07 honesty + anatomy layer (0.30.0–0.32.0)

Four additions completed the honest-measurement arc the morning pass started:

- **Read-side repoint finished (`448f447`).** `Baseline`'s frame histogram/MAD,
  `SpikeDetector`'s trigger, `HeatmapFold` (extracted pure from the
  aggregator), `FrameTimeCollector.frameMs`, and both `ProfilerSystem`
  narration sites now read `RealFrameTimeMs`. The rule: player-facing = real
  cadence; attribution internals + self-overhead = compute time, each
  deliberate and commented.
- **RealtimeSpeed (`Data/Stats/RealtimeSpeed.cs`, pure).** Period EMA → speed
  fraction (clamped at 1: 60 UPS is a ceiling), deficit ms/s, 30s sustained-
  fire constants. `MetricCollector.EndTick` folds it from the suspend-guarded
  `realFrameMs`, so alt-tabs never read as slow. Accumulators:
  `ConsecutiveSlowMs` (resets on recovery) + `TimeBelowThresholdMs` (session).
- **Phase lanes (`84409c1`).** `PerModAttribution.CurrentPhaseIsUpdate` is set
  true in `BeginTick`, false at the end of `EndTick`; `Add` additionally
  credits a draw-mirror grid when the flag is false. The PRIMARY grid keeps
  the TOTAL (bit-identical for every prior consumer); update = total − draw.
  Collector folds `_perModDrawSmoothedMs` with the same smoothing + denormal
  flush; `PerModCategoryDrawMs` exposes it. Measured: +0.001 ms/t update-path,
  +0.158 ms draw-path on a synthetic 62k-credit tick (PhaseLaneBench).
  `ProbeStack` counts draw-phase entries separately →
  `SelfHealth.ProbeCallsDrawPerTickEma` (the "24,227 calls while paused"
  number is now legible: all draw traffic).
- **Config surface (`88f10f4`).** `ConfigureDetectorSensitivity` (sensitivity
  is a divisor on the tuned threshold multipliers), history capacity from
  `FrameHistoryTicks` at arm, insights stride cached into
  `_insightsCadenceTicks` — the hot path never reads `ModContent.GetInstance`.
