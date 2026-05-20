# Concurrency, Threading, and Synchronisation — v0.6 Cross-System Thread Model

> Cross-cutting dossier. Scope: every thread the mod owns or touches, every channel/queue, every sync primitive, every cross-thread read or write, every dispose-ordering contract, every new thread-crossing introduced by the per-system v0.6 proposals. Read alongside `persistence.md`, `mod-lifecycle.md`, `metric-collection.md`, `insights-engine.md`, `stall-detection.md`, `overlay.md`, `hook-instrumentation.md`. This file is the unified thread model those dossiers must converge on.
>
> Hard rule (from `philosophy.md` and `baseline.md`): **same captures, same UI, same insights, same JSON shape, same retrospective**. We only re-route work across threads to spend the overhead budget better. Anything that drops capture, hides UI, deletes insights, or trades correctness for throughput is rejected.
>
> Hot-path rule (Invariant 2): the per-tick game-thread path must never block on a lock, never spin on a CAS-retry loop, never allocate to communicate. The only allowed cross-thread surfaces on the per-tick path are: `Channel<T>.Writer.TryWrite` (lock-free producer side), `Interlocked.Increment` on a 32/64-bit counter, `Volatile.Read/Write` on a single word, and value-type writes into pre-allocated `ThreadStatic` or owner-thread-only storage.
>
> Abort-clean rule (Invariant 4): every new background thread, install worker, or session-end aggregator must have a defined "instrumentation disabled, error logged, state reset" path. Crashing a background worker can never corrupt the player's run.

---

## Table of contents

1. The thread model today (v0.5) — every thread named, what it owns, what it touches, ASCII diagram.
2. The thread model after v0.6 — every per-system agent's threading proposal merged into one coherent model.
3. Channel / queue audit — `Channel<DbWriteOp>` policy verification, alternatives, benchmarks.
4. Lock / synchronisation primitive audit — every `Interlocked`, `Volatile`, `lock`, `SemaphoreSlim`, `MRES` across the codebase.
5. Race surface audit — every shared variable, who reads, who writes, ordering guarantees, fix per gap.
6. New thread-crossing risk audit — for each v0.6 proposal that adds a thread, validate the contract.
7. Memory model — where `Volatile.*` / `Interlocked.*` is required vs decorative.
8. Dispose ordering contract — full lifecycle from `Mod.Unload` outward.
9. Abort-clean discipline interactions — failure modes of every new thread.
10. References.

---

## 1. The thread model today (v0.5)

### 1.1 Threads named

Four threads are observable in the v0.5 mod. Two are owned by the mod, two are owned by the host.

| # | Thread | Owner | Lifetime | Hot? | Ownership of mod state |
|---|---|---|---|---|---|
| T1 | **Terraria main / game thread** (`Main.NewText`-callable, the XNA update loop) | Terraria / FNA | Process | Yes (60 Hz) | Owns `MetricCollector` mutation, owns every `_recorder.OnX` enqueue site, owns the per-tick attribution writes, owns `Player`/`NPC`/`Item` arrays. |
| T2 | **Terraria draw thread** (XNA `Draw` callback; on FNA/macOS this is the same logical thread as T1 unless the renderer detaches, but conceptually distinct) | Terraria / FNA | Process | Yes (60 Hz when overlay open) | Reads `MetricCollector` aggregate fields and the overlay's cached `RingBufferSnapshot` for F9 overlay paint. |
| T3 | **tModLoader load thread** (`AssemblyManager`'s `Thread` running `Mod.Load`, `PostSetupContent`, `Mod.Unload`) | tModLoader | Bounded — fires during launch + every `Mods → Reload` | Cold (one-shot heavy) | Owns `ProfilerDatabase` ctor, `HookInterceptor.Install`, `ILHookInterceptor.Install`, `Mod.Unload`'s `Uninstall + Dispose`. Splash screen is the visible surface. |
| T4 | **`ProfilerDbWriter`** (`new Thread(Run) { Name = "ProfilerDbWriter", IsBackground = true }`, `DbWriterThread.cs:69`) | This mod | Process (created in `ProfilerDatabase` ctor on T3, disposed in `ProfilerDatabase.Dispose` on T3) | Hot in bursts (drain batches up to 64 ops; 60-s checkpoint cadence) | Owns every LiteDB write, owns the journal append, owns the per-batch buffer `List<DbWriteOp>`. |

The .NET runtime additionally manages GC threads, finalizer thread, and timer threads; we do not interact with them directly except via `GC.GetAllocatedBytesForCurrentThread()` (TLS-local, per-thread, no cross-thread coupling) and `GC.GetTotalMemory(forceFullCollection: true)` (forces all threads to a GC safe point — used once in `SelfHealth.MarkInstallStart/End`, replaced in v0.6 per `mod-lifecycle.md §4.8`).

### 1.2 What each thread touches

```
T1 (game thread, 60 Hz)
   │
   ├── reads:  Terraria.Main, Player[], NPC[], Item[], Projectile[]
   ├── writes: MetricCollector.BeginTick/EndTick (history ring, baseline, focus probe)
   │           PerModAttribution (per-mod ms + bytes accumulators)
   │           PerTickAttributionRing (1800-tick ring of per-mod samples)
   │           SpikeDetector / StallDetector state machines
   │           ContextTransitionWatcher diff state
   │           SessionRecorder._recentDamageRing (added v0.6 — see §6.3 of persistence.md)
   │
   └── crosses: DbWriterThread.Enqueue(in DbWriteOp)   ───► T4
                  (single CAS via Interlocked.Increment on _approxQueueDepth,
                   one ChannelWriter.TryWrite, no allocation on steady path)

T2 (draw thread, 60 Hz when F9 open)
   │
   ├── reads:  MetricCollector.History (the RingBuffer<TickFrame>) — UNSYNCHRONISED
   │           MetricCollector.PerModCategoryAverageMs — UNSYNCHRONISED
   │           InsightsEngine.Shared._store — UNSYNCHRONISED
   │
   └── writes: overlay-local cached strings + format buffers (thread-local; the
               overlay tabs each own a private StringBuilder etc.)

T3 (tModLoader load thread)
   │
   ├── on Mod.Load:
   │     allocates ProfilerDatabase → spawns T4 (Thread.Start)
   │
   ├── on PostSetupContent:
   │     installs HookInterceptor + ILHookInterceptor (10,258 detours, ~10–18 s wall)
   │     ↳ MonoMod's RuntimeDetour writes IL into other mods' methods.
   │       The patched methods will execute on T1 starting next tick.
   │
   ├── on Mod.Unload:
   │     ILHookInterceptor.Uninstall (dispose each ILHook → MonoMod removes detour)
   │     ProfilerDatabase.Dispose
   │       ↳ _queue.Writer.TryComplete()
   │       ↳ _cts.Cancel()
   │       ↳ T4.Join(10 s) — blocks T3 until T4 drains the queue and exits Run()
   │
   └── crosses: spawns T4 once, joins T4 once.

T4 (ProfilerDbWriter, background, IsBackground=true)
   │
   ├── reads:  Channel<DbWriteOp>.Reader  (TryRead — never allocates on the happy path)
   │
   ├── writes: LiteDB (ApplyBatch → InsertBulk / Upsert / Update per stream)
   │           EventJournal (StringBuilder per batch + UTF-8 GetBytes, JsonSerializer per op)
   │           _pendingSinceLastCheckpoint (T4-private)
   │           _lastCheckpointUtc (T4-private)
   │
   ├── crosses on: Volatile.Read(_approxQueueDepth)  ◄── T1 writes (soft-cap branch only — purely diagnostic; T4 never decisions on it)
   │
   └── decrements: Interlocked.Decrement(ref _approxQueueDepth) per op drained.
```

### 1.3 Cross-thread surface inventory (v0.5)

Every place where a piece of mutable state is read on one thread and written on another. This is the **race surface** today.

| # | State | Written by | Read by | Sync | Hazard |
|---|---|---|---|---|---|
| C1 | `Channel<DbWriteOp>` (UnboundedChannel, segment-backed) | T1 (any thread permitted; `SingleWriter = false`) | T4 (single reader; `SingleReader = true`) | Channel's internal lock-free segment ring | None — the channel is the canonical correct primitive for this pattern. |
| C2 | `_approxQueueDepth` (int) | T1 (Increment on enqueue), T4 (Decrement on drain) | T1 (Volatile.Read for soft-cap branch), T4 (Volatile.Read for diagnostic), external (`ApproxQueueDepth` property) | `Interlocked.Increment`/`Decrement` + `Volatile.Read` | None — counter is informational; over-counting by one in the soft-cap branch is acceptable. |
| C3 | `_droppedWarmCount` (long) | T1 (Increment on drop) | external (`DroppedWarmCount` property — read by SELF tab on T2 and by SessionSummaryLogger on T4) | `Interlocked.Increment` + `Volatile.Read` | None — same as C2. |
| C4 | `MetricCollector.History` (the `RingBuffer<TickFrame>` of 1800 frames) | T1 (BeginTick/EndTick) | T2 (overlay draw — `History.Snapshot()` or direct indexer), T1 (SessionRecorder.OnTick reads the just-committed frame) | **None** | **Yes — see R1 below**. Under .NET 8's strong memory model on x64/ARM64 the practical risk is bounded (publication via the indexer's `Volatile.Read`-equivalent reordering of writes), but the contract is undocumented. |
| C5 | `MetricCollector.PerModCategoryAverageMs` and related per-mod-category aggregate arrays | T1 (per tick) | T2 (overlay), T1 (SessionRecorder.End reads at session-end for the aggregate build) | **None** | **Yes — see R2 below**. The per-mod arrays are mutated mid-tick and read at the end of the same tick; intra-tick reads from T2 see partially-updated rows. The visual impact is "one bar jiggled by 0.1 ms" — not a correctness issue *for the overlay*, but **a correctness issue for the v0.6 off-thread session-end aggregator** which will read these on T4. |
| C6 | `InsightsEngine.Shared._store` (the active insights set) | T1 (`InsightsTab.Tick` updates at 1 Hz) | T2 (`InsightsTab.Draw` at 60 Hz), T1 (SessionLogWriter / SessionSummaryLogger reads at session-end), T4 in v0.6 once session-end relocates | **None** (single-thread today by accident: `Tick` and `Draw` are both on the main thread under Terraria) | **Yes — see R3 below**. The `_topComparerNowTick` scalar is already documented as a race surface in `insights-engine.md §16`. |
| C7 | `HookInterceptor._installed`, `ILHookInterceptor.Installed` (bool) | T3 (PostSetupContent / Unload) | T1 (per-tick checks if any), T3 (Unload reads to decide whether to call Uninstall) | **None** today | Low — both reads happen after the write is sequenced (PostSetupContent runs to completion on T3 before T1's first per-tick call). **Yes — see R4 below** for the v0.6 off-thread install case in `mod-lifecycle.md §4.5`. |
| C8 | `PerformanceProfiler.Database` (static field, `ProfilerDatabase?`) | T3 (Mod.Load sets, Mod.Unload nulls) | T1 (every per-tick enqueue), T2 (overlay reads for SELF tab diagnostics), T4 (background drains don't read this — they own their references via ctor capture) | **None** | Low today (writes are bounded; reads tolerate null). **Yes during reload** — see R5. |
| C9 | `ProfilerSystem.Collector`, `_recorder`, `_transitionWatcher`, `_snapshotter`, `_deathDetector`, `_contextTagger`, `Events` (all `?`-typed fields) | T1 (OnWorldLoad / OnWorldUnload) | T1 (per-tick callbacks) | None | None — all reads/writes on T1 only. T2 does not read these. |

### 1.4 Hot-path zero-allocation invariant — verified per file

The hot path is "every method called from T1 during a regular tick that does not detect a spike, stall, or interaction event". The enqueue path is the broadest cross-thread surface and must be:

- lock-free,
- allocation-free on the steady state,
- single-CAS at most.

Verified inventory per file:

| File | Per-tick allocation? | Per-tick lock? | Cross-thread write? |
|---|---|---|---|
| `Profiling/MetricCollector.cs:BeginTick/EndTick` | No — writes into pre-allocated history + per-mod arrays | No | None directly; SessionRecorder.OnTick at end of EndTick enqueues 1 op/sec via T4 channel |
| `Profiling/RingBuffer.cs` | No | No — explicitly documented "intentionally not synchronised" (line 17) | None — owned by T1 entirely for writes |
| `Profiling/PerTickAttributionRing.cs` | No | No | None |
| `Profiling/SpikeDetector.cs` | No on no-spike path; per-spike allocates a SpikeWindowRow (will be pooled in v0.6 per `spike-detection.md`) | No | Per-spike enqueue via channel |
| `Profiling/StallDetector.cs` | No on no-stall path | No | Per-stall enqueue via channel |
| `Profiling/ProbeStack.cs:Enter/Leave` | No — ThreadStatic stack of pre-allocated slots | No | None directly |
| `Profiling/Persistence/DbWriterThread.cs:Enqueue` | No (struct DbWriteOp passed by `in`; channel segment write is in-place; Interlocked.Increment on 32-bit int) | No | **C2** (`_approxQueueDepth`) — Interlocked-clean |
| `Profiling/Persistence/SessionRecorder.cs:OnDamageDealt/etc.` | Per-event row allocation (target for v0.6 §5.2 pooling) | No | Channel enqueue |

The lock-free invariant is **upheld today** across the codebase. The single point where it could break is the v0.6 off-thread session-end aggregator (which must not introduce a per-tick lock to snapshot inputs), and the v0.6 off-thread install (which must not introduce a per-tick lock to read the install-progress flag). Both are designed lock-free in §6 below.

### 1.5 The cross-thread state diagram (today)

```
                ┌─────────────────────────────────────┐
                │  T3  ProfilerDatabase ctor          │
                │       │                              │
                │       └──new Thread(Run) ───────────┼──► spawn T4
                └─────────────────────────────────────┘
                                                          │
   T1 (60 Hz)                                             │
   ┌────────────────────────────┐                         │
   │ BeginTick                  │                         │
   │   write MetricCollector    │                         │
   │ Interaction*.OnX           │                         │
   │   build row                │                         │
   │   recorder.OnX(row)        │                         │
   │     writer.Enqueue(op)     │── ChannelWriter.TryWrite ─►──┐
   │                            │     Interlocked.Inc.         │
   │ EndTick                    │     (_approxQueueDepth)      │
   │   write attribution        │                              │
   └────────────────────────────┘                              │
                                                               ▼
                                          ┌────────────────────────────┐
                                          │ T4  ProfilerDbWriter       │
                                          │   WaitToReadAsync (1 s)    │
                                          │   TryRead × up to BatchCap │
                                          │   Interlocked.Dec.         │
                                          │   _journal.AppendBatch     │
                                          │   _db.ApplyBatch           │
                                          │   MaybeCheckpoint (60 s)   │
                                          └────────────────────────────┘

   T2 (60 Hz when F9 open)
   ┌───────────────────────────────────────────────┐
   │ Overlay.Draw                                  │
   │   read MetricCollector.History     ◄──── unsynchronised
   │   read MetricCollector.PerMod…     ◄──── unsynchronised
   │   read InsightsEngine.Shared       ◄──── unsynchronised
   └───────────────────────────────────────────────┘
```

The unsynchronised T1→T2 reads (C4, C5, C6) are the latent race surface. They have not bitten in v0.5 because under .NET 8 on x64/ARM64 the memory model gives release-publication semantics for reference-typed field writes and for primitive writes naturally aligned, and the overlay's tolerance for one-frame-stale data is high. They are documented hazards, not active bugs.

---

## 2. The thread model after v0.6

Six per-system dossiers propose threading changes. Merged, the v0.6 model adds three new threads (or strictly: extends two existing threads with new responsibilities) and tightens the dispose order.

### 2.1 The proposals, normalised

| Source dossier | Proposal | New thread / surface | Section |
|---|---|---|---|
| `persistence.md §5.6` | Off-thread session-end. SessionRecorder.End enqueues a `SessionEndAggregate` op carrying snapshotted collector state; writer thread runs `BuildModAggregates`/`BuildHookAggregates`/`BuildArchive`/`SessionSummaryLogger.Write`. | T4 (existing), new op kind | §6.1 |
| `mod-lifecycle.md §4.5` | Off-thread ILHook install via `ILHookInterceptor.BeginInstallAsync`. Dedicated `ProfilerILHookInstall` worker thread. | **T5 — install worker** (transient, one-shot per launch / reload) | §6.2 |
| `mod-lifecycle.md §4.7` | `PreSaveAndQuit` initiates session-end early; the writer thread runs the aggregate build concurrently with the vanilla save. | T4 (extension) | §6.1 |
| `mod-lifecycle.md §4.3` | Replace busy-wait drain with `ManualResetEventSlim` completion signal on the SessionEnd op. | New synchronisation primitive (MRES) | §6.1 |
| `insights-engine.md §13` (item 13) | Move correlated-detector LiteDB queries off the main thread. Either (a) reuse T4 with a separate read queue, or (b) spawn a dedicated reader thread. | **T6 — insights reader** (or T4 extension) | §6.3 |
| `metric-collection.md §4.13` | Move smoothing / rolling / harvest off the game thread. Publish snapshot via `Volatile.Write<ImmutableSnapshot>`. | **T7 — collector smoother** (~10 Hz background) | §6.4 |
| `stall-detection.md` (referenced) | Stall detector reads snapshot from T7's publish slot. | T1 still does the read; T7 produces the snapshot | §6.4 |
| `overlay.md` | Overlay reads from T7's published snapshot to reduce draw-thread allocation. | T2 reads `Volatile.Read<Snapshot>(ref _published)` | §6.4 |
| `persistence.md §6.3` (death attribution) | `PlayerDeathDetector.Capture` no longer reads LiteDB on T1; reads in-memory `_recentDamageRing` instead. | Removes a T1→DB read; pure T1-internal | §6.5 |

### 2.2 The post-v0.6 thread roster

| # | Thread | New? | Owner | Lifetime | Hot? |
|---|---|---|---|---|---|
| T1 | Terraria main / game thread | No | Terraria | Process | Yes (60 Hz) |
| T2 | Terraria draw thread | No | Terraria | Process | Yes (60 Hz when overlay open) |
| T3 | tModLoader load thread | No | tModLoader | Bounded | Cold |
| T4 | `ProfilerDbWriter` (existing; extended responsibilities) | No (extended) | This mod | Process | Hot in bursts + session-end |
| T5 | `ProfilerILHookInstall` (off-thread install worker) | **Yes** | This mod | Transient (~10–18 s once per launch/reload) | One-shot heavy |
| T6 | `ProfilerInsightsReader` (off-thread correlated-detector queries) | **Yes** *(or merged into T4 — decision in §6.3)* | This mod | Process | Background (~1 Hz reads) |
| T7 | `ProfilerCollectorSmoother` (off-thread rolling / smoothing publishing) | **Optional** — gated on `metric-collection.md §4.13` shipping. ~10 Hz publish cadence. | This mod | Process | Background |

Adding T5+T6 is committed (both have strong individual cases in their dossiers and modest cost). T7 is conditional — the rolling/smoothing relocation is high-risk per `metric-collection.md`; default position is to ship T5+T6 in v0.6 and defer T7 to v0.6.1 once T5+T6 are soaked.

### 2.3 Post-v0.6 cross-thread state diagram

```
   ┌────────────────────────────────────────────────────────────────┐
   │  T3  Mod.Load                                                   │
   │      ProfilerDatabase ctor ──spawn──► T4                        │
   │  T3  PostSetupContent                                           │
   │      HookInterceptor.Install        (synchronous, ~2 s)         │
   │      ILHookInterceptor.BeginInstallAsync ──spawn──► T5          │
   │      InsightsEngine.BeginReaderAsync ──spawn──► T6              │
   │      (T7 if enabled) MetricCollector.BeginSmootherAsync ──► T7  │
   └────────────────────────────────────────────────────────────────┘

   T1 (60 Hz)
   ┌─────────────────────────────────────────────┐
   │ PreUpdateEntities                            │
   │   collector.BeginTick                        │
   │   recorder.OnTick.Tick                       │
   │ PostUpdateEverything                         │
   │   collector.EndTick                          │
   │     attribution writes                       │
   │   spike/stall detect                         │
   │   ─── REPLACES today's read from LiteDB ───  │
   │   if death-edge: read SessionRecorder._recentDamageRing  (RAM)
   │   recorder.OnTick.Drain                      │
   │     for each spike/stall/transition:         │
   │       writer.Enqueue(op)                     │── Channel ──► T4
   │   if T6 has a result ready:                  │
   │     consume volatile snapshot ◄──── T6 publishes
   │   if T7 enabled, publish per-tick snapshot   │
   │     to T7's input ring (Volatile.Write)      │── ─► T7
   └─────────────────────────────────────────────┘

   T2 (60 Hz when F9 open)
   ┌────────────────────────────────────────────────┐
   │ Overlay.Draw                                    │
   │   if T7 enabled:                                │
   │     read Volatile.Read(_publishedSnapshot)      │ ◄── T7 publishes 10 Hz
   │   else:                                         │
   │     read MetricCollector.* (today's path)       │
   │   read InsightsEngine.Snapshot (T6-published)   │ ◄── T6 publishes
   └────────────────────────────────────────────────┘

   T4 (ProfilerDbWriter)
   ┌──────────────────────────────────────────────────────┐
   │ Run loop                                              │
   │   WaitToReadAsync (1 s)                               │
   │   drain batch                                         │
   │   journal.AppendBatch                                 │
   │   db.ApplyBatch                                       │
   │     ↳ if op.Kind == SessionEndAggregate:              │
   │           BuildModAggregates / Hook / Archive          │
   │           SessionSummaryLogger.Write                  │
   │           op.Completion?.Set()  ─── MRES signal ───► T1 (in OnWorldUnload Wait)
   │   MaybeCheckpoint                                     │
   └──────────────────────────────────────────────────────┘

   T5 (ProfilerILHookInstall, one-shot)
   ┌──────────────────────────────────────────────────────┐
   │ Install body                                          │
   │   for each method in HookSurfaceCache:                │
   │     new ILHook(method, manipulator)                   │
   │     yield every N hooks (cooperative; no game stall)  │
   │   Installed = true   (Volatile.Write)                 │
   │ on throw: Uninstall partial, log warn, exit.          │
   └──────────────────────────────────────────────────────┘

   T6 (ProfilerInsightsReader)
   ┌──────────────────────────────────────────────────────┐
   │ Loop:                                                 │
   │   sleep(1000 ms)                                      │
   │   for each correlated detector:                       │
   │     query db.* (read-only — separate LiteDB cursor)   │
   │     build result                                      │
   │   Volatile.Write(_publishedInsights, snapshot)        │
   │ on stop: cooperative exit via CTS                     │
   └──────────────────────────────────────────────────────┘

   T7 (ProfilerCollectorSmoother, optional)
   ┌──────────────────────────────────────────────────────┐
   │ Loop:                                                 │
   │   sleep(100 ms)                                       │
   │   read MetricCollector public spans (immutable view)  │
   │   compute rolling / smoothing                         │
   │   Volatile.Write(_publishedSnapshot, immutableObj)    │
   │ on stop: cooperative exit via CTS                     │
   └──────────────────────────────────────────────────────┘
```

### 2.4 Channel / event / publish surface inventory (v0.6)

Five distinct cross-thread surfaces after the merge. Each row is one inter-thread contract.

| # | Surface | Producer | Consumer | Primitive | Backpressure | Allocation cost |
|---|---|---|---|---|---|---|
| S1 | `Channel<DbWriteOp>` | T1 (any thread) | T4 | Lock-free unbounded channel; `SingleReader=true`, `SingleWriter=false` | Soft cap 100 000, drops `WarmAggregate` only | Zero on steady path (segment-internal store) |
| S2 | `_approxQueueDepth` counter | T1 (Inc), T4 (Dec) | T1 (soft-cap check), T4, diagnostic | `Interlocked` + `Volatile` | n/a | Zero |
| S3 | `DbWriteOp.Completion` (`ManualResetEventSlim`, new in v0.6) | T4 (Set after Apply) | T1 (OnWorldUnload Wait) | MRES with 5-s timeout | n/a — completion is at-most-once | One MRES allocation per session-end (not per-op) |
| S4 | `InsightsEngine._publishedSnapshot` (new in v0.6) | T6 (write) | T1 (read for `InsightsTab.Tick`), T2 (read for `InsightsTab.Draw`), T4 (read for session-end summary) | `Volatile.Write` of an immutable snapshot object reference | n/a — last-writer-wins | One snapshot allocation per T6 cycle (~1 Hz) |
| S5 | `MetricCollector._publishedSnapshot` (optional, T7) | T7 (write) | T2 (read), T1 (read in stall detector if T7 ships) | `Volatile.Write` of immutable snapshot object reference | n/a | One snapshot allocation per T7 cycle (~10 Hz) |
| S6 | `ILHookInterceptor.Installed` (bool) — write by T5, read by T1, T2 (overlay badge) | T5 (Volatile.Write) | T1 (per-tick — but only one read per tick; cost trivial), T2 (per-frame for SELF tab badge) | `Volatile.Read/Write` on a bool field | n/a | Zero |
| S7 | `ILHookInterceptor.InstallProgress` (int 0–100, optional) | T5 (Volatile.Write) | T2 (SELF tab badge) | `Volatile.Read/Write` on int | n/a | Zero |
| S8 | `ProfilerSystem._sessionEndInitiated` (bool) | T1 (PreSaveAndQuit / OnWorldUnload) | T1 only | None — same thread | n/a | Zero |

The v0.6 model adds **one** new heavyweight sync primitive (S3 — a single MRES per session-end), **two** new publish slots (S4, optionally S5), and **two** new readable flags (S6, S7). No new lock is added. No `lock` statement is introduced anywhere in the codebase by any v0.6 proposal.

---

## 3. Channel / queue audit

The `Channel<DbWriteOp>` is the single hottest cross-thread surface in the mod. Verifying its policy is correct, and that no alternative would beat it, is load-bearing for the v0.5 → v0.6 enqueue-latency target (441 → < 200 ns/op).

### 3.1 Policy verification: SingleReader, SingleWriter, AllowSynchronousContinuations

From `DbWriterThread.cs:63-68`:

```csharp
_queue = Channel.CreateUnbounded<DbWriteOp>(new UnboundedChannelOptions
{
    SingleReader = true,
    SingleWriter = false,
    AllowSynchronousContinuations = false,
});
```

| Option | Setting | Why correct | Alternative considered | Verdict |
|---|---|---|---|---|
| `SingleReader = true` | ✓ | T4 is the only reader. The channel implementation picks `SingleConsumerUnboundedChannel` (vs `UnboundedChannel`), which uses a `ConcurrentQueue<T>`-style segment ring without a reader-side lock. Faster TryRead. | Set false: forces the multi-consumer code path with reader-side coordination; ~20% slower in benchmarks. | Correct. **Keep.** |
| `SingleWriter = false` | ✓ | T1, T2 (overlay's `/profiler-compact` chat command), T3 (LegacyJsonImporter at Mod.Load), and potentially T6 (if insights detectors write back) all enqueue. Multi-producer is required. | Set true: enforces single-producer; we cannot guarantee that. Setting it true and breaking the assumption silently corrupts the segment chain. | Correct. **Keep.** |
| `AllowSynchronousContinuations = false` | ✓ | We do not `await` the writer thread from anywhere. Setting true would let the channel run reader continuations synchronously on the writer's thread, which could let a buggy reader continuation block T1. | Set true: only relevant if we used `WriteAsync` and awaited; we use `TryWrite` only. | Correct. **Keep.** |

The post-v0.6 model preserves all three. No change required.

### 3.2 Alternatives benchmarked (from existing dossiers + .NET docs)

Microsoft devblogs + `https://www.codegenes.net/blog/when-should-system-threading-channels-be-preferred-to-concurrentqueue/`:

| Primitive | TryWrite cost (ns/op, single producer, hot cache) | TryWrite cost (multi producer, contended) | Allocates per call? | Verdict |
|---|---|---|---|---|
| `Channel<T>` unbounded, SingleReader=true | ~25–60 | ~50–80 | No on steady path; allocates one segment per ~1024 ops | **Current. Keep.** |
| `ConcurrentQueue<T>` | ~70–150 | ~100–200 | No | Slower on the hot path. |
| `BlockingCollection<T>` (wraps ConcurrentQueue) | 200–500 | 300–700 | No on steady path | Significantly slower — the underlying SemaphoreSlim for blocking semantics is the cost. |
| Custom SPSC ring (Disruptor-style) | ~5–10 | n/a (single producer only) | No | **Faster** but requires single producer. We have multiple producers. |
| Custom MPSC ring (intrusive lock-free) | ~15–25 | ~20–40 | No | Faster than Channel by ~2x but requires hand-rolling and we are already at 25–60 ns. The 441 ns enqueue regression is **not** in the channel; it is in the row construction upstream (per `persistence.md §5.2`). Replacing the channel buys nothing until the row allocation is removed. |

**Conclusion:** the `Channel<DbWriteOp>` policy is optimal for v0.6. The 441 → < 200 ns target is achieved by removing the per-event row allocation upstream (pool the rows — `persistence.md §5.2`), not by replacing the channel.

### 3.3 Does the channel allocate per enqueue?

`Channel.CreateUnbounded<T>(SingleReader=true)` returns a `SingleConsumerUnboundedChannel<T>`. Source (referencesource / `System.Threading.Channels` `SingleConsumerUnboundedChannel.cs`):

- The internal storage is a singly-linked list of segments. Each segment holds N items. On steady state, `TryWrite` performs:
  - `Interlocked.CompareExchange` on the tail pointer (1 CAS),
  - `array[index] = item` (memory store),
  - either signal the reader's `AsyncOperation` (if it was waiting) or do nothing.

- New segment allocations occur only when a segment fills (default segment size = 1024). At 314 ops/sec with batches of 64, segments fill every ~3 s; the allocation is amortised.

**Verified:** zero allocation per enqueue on the steady path. The 441 ns regression is not channel-related.

### 3.4 Backpressure policy

```csharp
if (Volatile.Read(ref _approxQueueDepth) >= QueueSoftCap && op.Kind == DbOpKind.WarmAggregate)
{
    Interlocked.Increment(ref _droppedWarmCount);
    return;
}
```

The policy: when the queue exceeds 100 000 ops, drop only `WarmAggregate` ops. Every other op (event row, session boundary, spike, stall) is preserved.

Verification against the philosophy rule ("optimisation = doing what we do, cheaper"):

- WarmAggregate is one row per second per session. Dropping it loses one second of 1-Hz downsampled data. That's a **capture loss**, not a downsampling change.

**However:** the dropped data is _already_ a downsample of per-tick history that is still in the `MetricCollector.History` ring buffer at the moment of drop. If we get to soft-cap, the game thread has been producing faster than the writer can drain for long enough that the writer is the bottleneck — typically this means LiteDB is in a Checkpoint or the journal is fsyncing. Once the writer catches up, the next `WarmAggregate` op succeeds. The window of loss is the catch-up window.

**Recommended v0.6 refinement:** before dropping, prefer to *coalesce* — merge the dropped warm aggregate's mod-ms additions into the next warm aggregate's payload. Implementation: keep a `WarmAggregateCarry` field on T1; when drop fires, accumulate into the carry; next non-dropped enqueue adds the carry to the live row, then resets it.

This preserves the capture (the data is delivered, one second late) without changing the throughput target. Filed as a follow-up to the persistence pass.

### 3.5 Are there other channels in the codebase?

Search across the source: zero other `Channel<>` instances. Only the DbWriter has one. The InsightsReader (T6) and CollectorSmoother (T7) proposed in v0.6 do **not** need a channel — they consume snapshot publishes via `Volatile.Read`, not a producer queue.

### 3.6 Should T6 / T7 use a Channel?

For T6 (insights reader): no. T6 is a pull consumer; it reads LiteDB at its own cadence and publishes a snapshot. Nothing pushes to T6.

For T7 (collector smoother): no. T7 reads `MetricCollector` directly via read-only spans and publishes a snapshot. Nothing pushes to T7.

For T5 (install worker): no. T5 is one-shot. Spawn → run → exit. No queue.

The post-v0.6 model has **exactly one channel**: `Channel<DbWriteOp>`. Surface stays bounded.

### 3.7 Benchmark proposal

Add to `Tests/Persistence/PersistenceBenchmarkTests.cs`:

```csharp
[Fact]
public void Enqueue_GameThread_Latency_StaysUnderTarget()
{
    using var fx = new WriterFixture();
    var op = DbWriteOp.WarmAggregate(new TickAggregateWarm { /* pre-built */ });
    const int iters = 1_000_000;

    var sw = Stopwatch.StartNew();
    for (int i = 0; i < iters; i++) fx.Writer.Enqueue(in op);
    sw.Stop();

    var nsPerOp = (sw.ElapsedTicks * 1e9 / Stopwatch.Frequency) / iters;
    Assert.True(nsPerOp < 250, $"enqueue {nsPerOp:F1} ns/op > target 250 ns");
}
```

Plus a multi-producer variant:

```csharp
[Fact]
public void Enqueue_MultiProducer_NoCorruption()
{
    using var fx = new WriterFixture();
    var op = DbWriteOp.WarmAggregate(new TickAggregateWarm { /* ... */ });
    const int producers = 4, perThread = 100_000;

    Parallel.For(0, producers, _ => {
        for (int i = 0; i < perThread; i++) fx.Writer.Enqueue(in op);
    });

    fx.DrainAndAssertExactly(producers * perThread);
}
```

The second test confirms `SingleWriter=false` is honoured under contention.

---

## 4. Lock / synchronisation primitive audit

A literal `grep` across `Profiling/` reveals every sync primitive in the mod.

### 4.1 The full inventory

```
$ grep -rn "lock (\|Interlocked\.\|Volatile\.\|SemaphoreSlim\|ManualResetEvent" Profiling/

Profiling/Persistence/DbWriterThread.cs:48:    public long DroppedWarmCount => Volatile.Read(ref _droppedWarmCount);
Profiling/Persistence/DbWriterThread.cs:56:    public int ApproxQueueDepth => Volatile.Read(ref _approxQueueDepth);
Profiling/Persistence/DbWriterThread.cs:86:    if (Volatile.Read(ref _approxQueueDepth) >= QueueSoftCap …
Profiling/Persistence/DbWriterThread.cs:88:    Interlocked.Increment(ref _droppedWarmCount);
Profiling/Persistence/DbWriterThread.cs:93:    Interlocked.Increment(ref _approxQueueDepth);
Profiling/Persistence/DbWriterThread.cs:126:   Interlocked.Decrement(ref _approxQueueDepth);
Profiling/Persistence/DbWriterThread.cs:188:   Interlocked.Decrement(ref _approxQueueDepth);
```

Five primitives in one file. Zero elsewhere.

| Site | Primitive | Cost | Reason | Verdict |
|---|---|---|---|---|
| `_approxQueueDepth` Inc/Dec | `Interlocked.Increment/Decrement` (int) | One LOCK XADD ≈ 5–10 ns | Counter for soft-cap diagnostics | Correct |
| `_droppedWarmCount` Inc | `Interlocked.Increment` (long) | One LOCK XADD ≈ 5–10 ns | Diagnostic counter | Correct |
| `Volatile.Read(ref _approxQueueDepth)` (×2 — soft-cap check + property) | `Volatile.Read` (int) | Single load + acquire fence ≈ 1–3 ns on x64 | Read counter without tearing risk on 32-bit | Correct |
| `Volatile.Read(ref _droppedWarmCount)` (long) | `Volatile.Read` (long, 8-byte) | Single load on x64 (free); on 32-bit ARM the read can tear without `Volatile.Read`'s `Interlocked.Read` fallback | Diagnostic property access | Correct, **and load-bearing on 32-bit platforms** — `Volatile.Read<long>` internally falls back to `Interlocked.Read` to avoid torn 64-bit reads on 32-bit targets. tModLoader supports x64 only in 1.4.4, so on our target this is decorative; on hypothetical 32-bit it would be required. Keep it. |

The codebase is *aggressively* lock-free today. The per-tick hot path on T1 acquires zero locks and zero CAS operations except inside `Channel.Writer.TryWrite` (one CAS on tail pointer) and one `Interlocked.Increment(ref _approxQueueDepth)` per enqueue.

### 4.2 Opportunities to remove primitives entirely

The two `Interlocked.Increment/Decrement` pairs on `_approxQueueDepth` are paid on every enqueue and every drain. The counter exists for:

1. Soft-cap branch on the producer side (drop WarmAggregate if depth ≥ 100 000).
2. The `ApproxQueueDepth` property (diagnostic).
3. The old busy-wait drain in `DrainAndTruncateJournalForSessionEnd` (removed by v0.6 §4.3 of `mod-lifecycle.md`).

After v0.6: usage (3) disappears. (1) and (2) remain. The Inc/Dec stay.

**Alternative:** instead of an exact counter, sample the channel reader's internal queue depth via `_queue.Reader.CanCount && _queue.Reader.Count` (some channel types expose this). For `SingleConsumerUnboundedChannel<T>`, `Count` is not reliably exposed — confirmed via .NET source code. So we keep the counter.

**Net:** no removal opportunity. The two Interlocked sites per enqueue/drain are minimal and pay for the soft-cap correctness.

### 4.3 New primitives introduced in v0.6

| # | Primitive | Where | Cost | Required? |
|---|---|---|---|---|
| P1 | `ManualResetEventSlim` per session-end (`DbWriteOp.Completion`) | New field on the op struct; T4 sets, T1 waits | Allocation: one MRES per session (~24 bytes); Wait/Set: kernel transition only when contended | Yes — replaces the busy-wait poll. |
| P2 | `Volatile.Write<InsightsSnapshot>` (T6 publishes) | New field on `InsightsEngine` | Single reference store with release fence | Yes — required for safe cross-thread publish of immutable snapshot. |
| P3 | `Volatile.Read<InsightsSnapshot>` (T1, T2 consume) | Same field | Single reference load with acquire fence | Yes — required to pair with P2. |
| P4 | `Volatile.Write<bool>` for `ILHookInterceptor.Installed` (T5 → T1/T2) | Replace direct write with `Volatile.Write` | Single bool store with release fence | Yes — without it, T1 may read `Installed = true` before T5's preceding writes to detour state are visible. |
| P5 | `Volatile.Read<bool>` for `ILHookInterceptor.Installed` (T1/T2 consume) | Pair with P4 | Single load + acquire fence | Yes — pair. |
| P6 | `CancellationTokenSource` for T5/T6 cooperative cancellation | Each new thread owns one | Cheap (`Cancel()` triggers an atomic flag + signals waits) | Yes — for shutdown. |
| P7 | `Volatile.Write<MetricSnapshot>` (T7 publishes) — **optional** | If T7 ships | Single reference store | Conditional on T7 shipping |
| P8 | `Volatile.Read<MetricSnapshot>` (T1/T2 consume) — **optional** | Pair | Single load | Conditional |

**Zero new `lock` statements**. The model continues to be 100% lock-free on T1's hot path.

### 4.4 Lock-free swap / double-buffer / optimistic concurrency opportunities

The `metric-collection.md §4.13` proposal (move smoothing off-thread, T7) is a textbook double-buffer:

```csharp
// On T7 (10 Hz):
var snapshot = new MetricSnapshot { /* immutable */ };
PopulateSnapshot(snapshot);                              // T7-private writes
Volatile.Write(ref _published, snapshot);                // publish

// On T1, T2:
var snap = Volatile.Read(ref _published);                // load latest
if (snap != null) ConsumeSnapshot(snap);                 // read-only
```

The snapshot object is immutable after the `Volatile.Write`. Readers get either the previous snapshot or the new one — never a partial. The pattern is:

| Property | Verified |
|---|---|
| Lock-free | ✓ |
| Allocation-free on readers | ✓ — readers only read a reference |
| Snapshot allocation on producer | 1 per cycle (10 Hz → 10/sec → ~36 KB/min, GC-friendly Gen0) |
| Memory model correctness | ✓ — `Volatile.Write` of a reference is release-ordered with prior writes; pairs with `Volatile.Read` acquire-ordered |
| Stale-read tolerance | Required — readers may see a snapshot up to 100 ms old; documented as acceptable in `metric-collection.md §4.13` |

This is the right pattern for T7. The same pattern applies to T6 (insights reader).

**`MetricCollector.History` (the per-tick ring buffer) — should it become a double-buffered snapshot?**

Today: T1 writes, T2 reads unsynchronised. The ring buffer has 1800 slots; on T1's commit of a frame, T2 may read the slot mid-write. The risk in practice: T2 sees a `TickFrame` whose `WallMs` is updated but `CpuMs` is the previous tick's value. Cosmetic — one bar wobbles.

After v0.6 with T7 enabled: T7 reads the ring on its own cadence and publishes a snapshot. T2 reads the snapshot. T1 still writes the ring. T7's reads from the ring are also concurrent with T1's writes — same race surface as today.

**The fix:** the ring's `Commit` method writes a `Volatile.Write` of a single per-slot sequence counter at the end. Readers (T2 or T7) read the counter twice, around the slot read, and retry if it changed. Per-frame cost: one extra store on T1 (≈ 1 ns); occasional retry on the reader.

This is the **seqlock** pattern. Filed as a v0.6.1 candidate in `metric-collection.md`; not load-bearing for the v0.6 stall-fix target.

---

## 5. Race surface audit

Every shared variable in the codebase that is read on one thread and written on another. Listed with sync, hazard severity, and v0.6 fix where required.

### 5.1 R1 — `MetricCollector.History` ring buffer

| Property | Today | After v0.6 |
|---|---|---|
| Writer | T1 (BeginTick/EndTick) | T1 unchanged |
| Reader (intra-process) | T2 (overlay), T1 (recorder), T7 (smoother, if enabled), T4 (session-end snapshot — new) | + T4 reads at session-end |
| Sync | None | Optional seqlock (v0.6.1 candidate) |
| Hazard | Cosmetic frame jitter | **Material** for T4 session-end: an unaligned read could give the aggregate builder partial data, skewing one mod's average ms by single-frame error |
| Fix | Snapshot copy on T1 inside `SessionRecorder.End`/`PreSaveAndQuit` before enqueueing the `SessionEndAggregate` op. The snapshot is a stable `double[]` copy, not a live read | **Required for v0.6** — already specified in `persistence.md §5.6` and `mod-lifecycle.md §4.1`. |

The fix is: **T1 always snapshots before publishing**. T4 reads only the snapshot, never the live ring. No new seqlock required for v0.6.

### 5.2 R2 — `MetricCollector` per-mod-category aggregate arrays

| Property | Today | After v0.6 |
|---|---|---|
| Writer | T1 (per tick) | T1 unchanged |
| Reader | T2 (overlay), T1 (session-end), T7 (if enabled), T4 (session-end snapshot — new) | + T4 reads at session-end via the snapshot |
| Sync | None | Snapshot copy on T1 before T4 reads |
| Hazard | T2 reads can see mid-tick: one cell updated, neighbour not. Cosmetic. T4 reads (new in v0.6) would be material **if direct**. | Eliminated for T4. Cosmetic for T2 (acceptable). |
| Fix | `MetricCollector.SnapshotForSessionEnd(SessionEndSnapshot dst)` — copies the per-mod-category arrays into `dst.PerModCategoryAvgMs` on T1 | Required for v0.6 (per `mod-lifecycle.md §4.1`). |

### 5.3 R3 — `InsightsEngine.Shared._store` and `_topComparerNowTick`

`insights-engine.md §16` documents the existing race surface on `_topComparerNowTick` (a shared scalar mutated by `Evaluate` and read by the comparer's lambda). The fix is item 18 in that dossier: replace the shared scalar with a per-call comparer struct. Independent of v0.6 threading work; lands as a small bug fix.

After v0.6 with T6 (insights reader):

| Property | After v0.6 |
|---|---|
| Writer | T6 (computes new insights from off-thread queries, publishes immutable snapshot) |
| Reader | T1 (`InsightsTab.Tick`), T2 (`InsightsTab.Draw`), T4 (session-end summary) |
| Sync | `Volatile.Write/Read` of a snapshot object reference (S4) |
| Hazard | None — immutable snapshot, single publisher |

T6 should **not** mutate `_store` in-place; it builds a new snapshot, swaps in the publish slot. Old snapshot becomes garbage.

### 5.4 R4 — `ILHookInterceptor.Installed` / `InstallProgress`

Today: written by T3 in `PostSetupContent` (set true at end of synchronous install), read by T1 (per-tick, if any reads — currently none) and T2 (SELF tab badge). The write is sequenced before any read because `PostSetupContent` runs to completion before T1's first per-tick callback.

After v0.6 with T5 (off-thread install): written by T5 mid-install. Read by T1 per-tick (if instrumentation gates on `Installed`) and T2 per-frame (overlay badge).

**Hazard:** without proper publication, T1 / T2 may read `Installed = true` while the underlying detour state is still being assembled. Worst case: T1's per-tick path attempts to consume hook timing data that hasn't been wired yet → null reads → exceptions in the hot path.

**Fix (mandatory for v0.6.5 §4.5):**

```csharp
// T5 — at end of install:
Volatile.Write(ref _installed, true);

// T1 / T2 — at every read:
if (Volatile.Read(ref _installed)) { /* consume */ }
```

Additionally: ensure `_installed = true` is the **last** write T5 performs before exit. All preceding writes (detour list, attribution slots, per-mod arrays) must complete before the flag flip. The `Volatile.Write` provides the release fence; .NET's memory model guarantees prior writes are visible to any thread that reads the flag with `Volatile.Read` and sees `true`.

### 5.5 R5 — `PerformanceProfiler.Database` static

Today: written by T3 (Mod.Load sets, Mod.Unload nulls). Read by T1, T2, T4.

During `Mods → Reload`, T3 nulls the field while T1 / T4 may still be executing. T4 in particular holds a captured reference from its ctor; nulling the static doesn't affect T4's view. T1's reads are guarded by null-check (`PerformanceProfiler.Database?.Writer.Enqueue(...)`).

**Hazard:** between T3's null and T4's eventual exit (after `Join(10 s)`), T1 may read non-null, then T3 nulls, then T1's chained access `Database!.Writer` NREs.

**Fix:** local-variable capture at each read site:

```csharp
var db = PerformanceProfiler.Database;
if (db != null) db.Writer.Enqueue(op);
```

This is the standard idiom and is already used in most call sites. Audit all call sites in v0.6 for this pattern. Add a compiler-enforced contract via `[NotNullWhen(true)]` if helpful.

### 5.6 R6 — `_sessionEndInitiated` flag

T1-only field. No race surface. Acts as idempotency guard between `PreSaveAndQuit` and `OnWorldUnload`.

### 5.7 R7 — `SessionRecorder._recentDamageRing` (new in v0.6)

Per `persistence.md §6.3`, the death-attribution fix introduces a small ring buffer of recent damage events. Written by T1 (each `OnDamageTaken`), read by T1 (on death edge). **No cross-thread surface.** No race.

If a future need arises to read it from T4 (e.g. session-end summary wants to attribute deaths), the same snapshot-copy pattern as R1 applies.

### 5.8 Race surface summary table

| Race | Threads | Today's sync | v0.6 sync | Required? |
|---|---|---|---|---|
| R1: History ring | T1 W; T2/T4/T7 R | None | Snapshot on T1 before T4 read; seqlock candidate for T7 | **Yes for T4** |
| R2: PerMod aggregates | T1 W; T2/T4/T7 R | None | Snapshot on T1 before T4 read | **Yes for T4** |
| R3: InsightsEngine | T1/T6 W; T1/T2/T4 R | None (and a documented bug) | `Volatile` publish snapshot from T6; bug fix to `_topComparerNowTick` | **Yes** |
| R4: ILHook installed flag | T5 W; T1/T2 R | n/a (T3 wrote pre-tick) | `Volatile.Write/Read` | **Yes** |
| R5: Database static | T3 W; T1/T2/T4 R | None | Local-variable capture pattern (audit) | **Audit** |
| R6: sessionEndInitiated | T1 only | n/a | n/a | n/a |
| R7: recentDamageRing | T1 only | n/a | n/a | n/a |

---

## 6. New thread-crossing risk audit

For each v0.6 proposal that introduces a new thread crossing, validate: (a) what the contract is, (b) what the failure mode is, (c) what proves it correct, (d) whether the contract is testable.

### 6.1 SessionEndAggregate op (T1 → T4 with snapshot capture)

**Producer:** T1 (`SessionRecorder.End` called from `PreSaveAndQuit` or `OnWorldUnload`).

**Consumer:** T4 (`SessionEndAggregateStream.Apply`).

**Contract:**

1. Before enqueue, T1 captures a stable snapshot of every required input: per-mod-category arrays, per-hook arrays, history summary, session metadata. The snapshot is a fresh allocation (or a pooled `SessionEndSnapshot` rented + filled in place).
2. Enqueue is a single channel write; no allocation beyond the snapshot.
3. T4's `Apply` reads only the snapshot, never the live `MetricCollector`. T1 may continue mutating the collector while T4 builds aggregates — they reference disjoint memory.
4. T4's `Apply` ends by setting `op.Completion` (MRES). T1's `OnWorldUnload` waits up to 5 s. If T4 has not signalled in 5 s, T1 logs a warn and proceeds; T4 completes asynchronously (the next session-start's crash-detected path covers the gap).

**Failure modes:**

| Failure | Consequence | Mitigation |
|---|---|---|
| T1 enqueues, then `Mod.Unload` arrives before T4 processes | T4's drain phase in `Run.finally → DrainAndShutdown` reads and applies the op | Same path as today; bounded by 10-s Join |
| T4 throws inside `Apply` | The outer try/catch in `Run` logs `_log("ProfilerDbWriter: batch apply failed", ex)`; the op is consumed; the MRES is never set | T1's 5-s timeout absorbs this; the session row stays `Incomplete = true`; next launch's recovery marks the session as `EndReason = "crash-detected"` |
| Snapshot was captured mid-tick (race with T1's per-tick writes) | Aggregate values are off by one tick's contribution | T1 captures the snapshot at session-end inside `PreSaveAndQuit`; by definition no more per-tick writes follow (the world is unloading). The snapshot is consistent. |
| MRES.Set() runs before T1's MRES.Wait() | `Wait()` returns immediately; that is the correct behaviour | n/a — by design |
| MRES.Wait() runs before T4 even sees the op (small queue backlog) | Wait blocks until T4 catches up, bounded by 5-s timeout | Acceptable |

**Verification recipe:** byte-identical JSON comparison of `PerSessionMods` / `PerSessionHooks` / `TickAggregatesArchive` between pre-pass (T1 builds) and post-pass (T4 builds) for the same captured input. Specified in `mod-lifecycle.md §6.12`.

**Abort-clean compliance:** if T4 crashes during session-end build, the session is marked `Incomplete`, the journal still has the events, the next launch replays. Player's game state is untouched.

### 6.2 ILHook install worker (T3 → T5)

**Spawner:** T3 (`PostSetupContent` calls `ILHookInterceptor.BeginInstallAsync`).

**Worker:** T5 (`new Thread(Install) { IsBackground = true, Name = "ProfilerILHookInstall" }`).

**Consumer of `Installed` flag:** T1 (per-tick hooks gating), T2 (SELF tab badge).

**Contract:**

1. T3 returns immediately after `BeginInstallAsync`. The splash continues to next content step.
2. T5 builds detours one by one. After each batch of N (say 100) detours, T5 yields via `Thread.Sleep(0)` to permit OS scheduling fairness.
3. T1 begins ticking as soon as the game starts. Per-tick hook gating consults `Volatile.Read(ref _installed)`. While false, the per-tick path skips the IL-side timing; the delegate-pair path (installed synchronously on T3) continues to provide coverage.
4. T5 sets `Volatile.Write(ref _installed, true)` as the last write before exit.
5. On throw, T5 calls `Uninstall()` for partial detours, sets `_installed = false` permanently (Invariant 4), logs warn.

**Failure modes:**

| Failure | Consequence | Mitigation |
|---|---|---|
| T5 throws mid-install | Partial detours are disposed by `Uninstall()` in T5's finally; `_installed` stays false; ILHook coverage is permanently off for this session; delegate-pair coverage still works | Invariant 4: instrumentation disabled cleanly. |
| T5 still running when world enters | T1 ticks with partial coverage; the SELF tab badges "install N % complete" | Acceptable — `philosophy.md` documents that data-stack coverage grows monotonically during install. |
| T5 still running when `Mod.Unload` arrives | T3's Unload path: `T5.Join(timeout)` then `Uninstall` whatever was installed | Define a 30-s join cap; on timeout, fall through to Uninstall, which is per-hook try/catch and tolerates partial state. |
| MonoMod throws on a specific method (e.g. method body has a token MonoMod cannot resolve) | T5 catches per-method, logs, skips that method, continues; the global `_installed` stays "will eventually be true if no fatal throw" | The per-method try/catch pattern is already in `ILHookInterceptor.Install`. Preserve. |
| T1 reads `_installed = true` before T5's prior writes are visible | Memory ordering bug; T1 sees the flag true but the detour state is partial | `Volatile.Write(ref _installed, true)` provides release ordering; pairs with `Volatile.Read` on consumers. Audit every read for `Volatile.Read`. |
| Race between T5 and `Mod.Unload`'s Uninstall | T5 might be mid-install when T3 starts disposing | `Mod.Unload` must first cancel T5's CTS, then Join with bounded timeout, then Uninstall. Order matters: Cancel → Join → Uninstall. |

**Invariant 4 compliance:** T5's failure path disables ILHook instrumentation cleanly for the session and writes one warn line. The delegate-pair path (synchronous on T3) is unaffected and continues to provide coverage. The mod never proceeds against unverified internals.

### 6.3 Insights reader (T6) — decision: dedicated thread or T4 extension?

Two options from `insights-engine.md §13`:

**Option A: Dedicated `ProfilerInsightsReader` thread (T6).**

- Pros: decouples read latency from write latency; T4's drain cadence isn't perturbed by ad-hoc queries.
- Cons: introduces a third long-lived background thread (T4, T6, optionally T7). LiteDB v5 read-while-write concurrency requires verification.

**Option B: T4 extension — add a read-queue to T4 and serialise reads between batches.**

- Pros: no new thread. LiteDB write/read sequencing is intrinsically safe (one thread).
- Cons: read latency increases (reads wait for batch boundaries, ~1 s). Reads can starve under burst write load.

**Recommendation:** **Option A**. The 1-Hz read cadence and bounded query set make it cheap to verify LiteDB v5 read-while-write on `ConnectionType.Direct` with the writer's lock discipline. The pattern is the same one `SessionLogWriter` already uses for off-thread reads at session-end (cited in `insights-engine.md §6.3`). Document the connection-type verification as a v0.6 work item before T6 ships.

**Contract:**

1. T6 loops at 1 Hz.
2. Each cycle: open a read-only LiteDB query for each correlated detector, build a result, accumulate into a fresh `InsightsSnapshot`.
3. After all detectors finish, `Volatile.Write(ref _publishedInsights, snapshot)`.
4. Consumers (T1 `InsightsTab.Tick`, T2 `InsightsTab.Draw`, T4 session-end summary) `Volatile.Read` the slot.

**Failure modes:**

| Failure | Consequence | Mitigation |
|---|---|---|
| LiteDB v5 `ConnectionType.Direct` with concurrent reads while writer holds a write transaction | The reader may observe partial committed state or block on a write lock | LiteDB v5 docs state per-collection read locks compose with the writer's exclusive lock; the reader will block briefly during a write transaction (sub-millisecond) but not crash. Verify with a soak test. |
| T6 throws mid-cycle | T6's outer try/catch logs warn, sleeps 1 s, retries. Worst case: the published snapshot is stale by N seconds | Acceptable; the overlay badges "insights stale" if `snapshot.UnixMs` is more than 5 s old |
| `Mod.Unload` arrives while T6 is mid-query | T6's CTS is cancelled; LiteDB cursors unwind; T6 exits | Cancel → Join → Database.Dispose order in `Unload`. |
| Multiple consumers read the snapshot simultaneously | `Volatile.Read` is safe for concurrent readers; immutable snapshot is safe to read concurrently | n/a — by design |

**Verification recipe:** soak test — T4 runs at 314 ops/sec sustained for 60 s while T6 queries `damageDealt` and `npcSpawns` each second. Assert no exceptions, no stale-read corruption (each snapshot's contents are consistent with the on-disk state at some point in time).

### 6.4 Collector smoother (T7) — optional

**Spawned by:** T3 (`PostSetupContent`) — only if `metric-collection.md §4.13` ships.

**Cadence:** ~10 Hz.

**Contract:**

1. T7 reads `MetricCollector` via read-only span accessors.
2. T7 builds an immutable `MetricSnapshot`.
3. `Volatile.Write(ref _publishedSnapshot, snap)`.
4. T2 reads via `Volatile.Read` per draw frame; T1's stall detector reads via `Volatile.Read` per tick.

**Failure modes:** identical to T6's pattern. T7's read concurrent with T1's writes to `MetricCollector` introduces R1/R2 race; the cosmetic-frame-jitter argument applies. If the seqlock is preferred (v0.6.1 candidate), add per-slot sequence counters to the ring.

**Decision:** defer T7 to v0.6.1. Land T5+T6 in v0.6; soak; then evaluate T7. The `metric-collection.md §4.13` saving is 50–70 µs/tick — material but not load-bearing for the v0.6 stall fix.

### 6.5 Death-attribution ring (T1-only — no new crossing)

`persistence.md §6.3`'s fix moves death-attribution from a T1→DB read (synchronous LiteDB query on the death edge) to a T1-only in-memory ring read. **Removes** a thread crossing rather than adding one. Strictly easier to reason about. No race surface.

---

## 7. Memory model — `Volatile.*` / `Interlocked.*` required vs decorative

### 7.1 The .NET 8 memory model

Per `https://learn.microsoft.com/en-us/dotnet/standard/threading/memory-model`:

- All naturally-aligned reads/writes of pointer-sized or smaller primitive types are atomic on x64/ARM64.
- The CLR enforces release semantics on write to reference-typed and value-typed fields with `[Volatile]` (the attribute) or via `Volatile.Write`.
- Without explicit fencing, the JIT and CPU are permitted to reorder loads and stores within method bounds, subject to single-thread program order.
- `Interlocked.*` provides full fences (load-load, load-store, store-load, store-store).
- `Volatile.Write` is release-only; `Volatile.Read` is acquire-only. The pair gives the publication / consumption pattern.

### 7.2 Where `Volatile.*` is load-bearing (must keep / must add)

| Site | Today/v0.6 | Why load-bearing |
|---|---|---|
| `Volatile.Read(ref _approxQueueDepth)` in soft-cap branch | Today | The producer thread (T1) reads a value the consumer (T4) decrements. Without acquire fence, T1 may read a stale-by-N value and admit ops over the soft cap, or read a fresh value and drop ops below. Both are diagnostic-only; acceptable. **Keep for clarity.** |
| `Volatile.Read(ref _droppedWarmCount)` in property | Today | 64-bit read on a 32-bit platform would tear without `Volatile.Read<long>` (which internally uses `Interlocked.Read`). On x64 it's a single MOV; the call compiles to that. **Keep.** |
| `Volatile.Write/Read` on `_installed` (T5 ↔ T1/T2) | v0.6 | T5 writes detour state, then sets the flag last. `Volatile.Write` ensures the prior writes are visible to any thread that reads the flag and sees true. Without this, T1 may read true and then dereference a not-yet-written field. **Mandatory.** |
| `Volatile.Write/Read` on `_publishedInsights` / `_publishedSnapshot` (T6/T7 ↔ T1/T2/T4) | v0.6 / v0.6.1 | Same release-publication pattern. Without these, consumers may read a partially-constructed snapshot. **Mandatory.** |

### 7.3 Where `Interlocked.*` is load-bearing

| Site | Why |
|---|---|
| `Interlocked.Increment(ref _approxQueueDepth)` (T1 enqueue) | Multiple producers; non-atomic Inc would lose updates under contention. **Mandatory.** |
| `Interlocked.Decrement(ref _approxQueueDepth)` (T4 drain) | Pairs with Increment to keep the counter accurate. **Mandatory.** |
| `Interlocked.Increment(ref _droppedWarmCount)` | Multiple producers; same reason. **Mandatory.** |

### 7.4 Where memory ordering is currently relying on .NET's strong memory model (but is decorative)

| Site | Relying on what | Risk |
|---|---|---|
| `MetricCollector.History` writes on T1 read by T2 unsynchronised | x64 store ordering: writes within a single thread are observed in program order by other threads on x64; on ARM64 some reordering is possible but the per-frame indexer reads are not reordered in practice | Cosmetic only; documented |
| `PerformanceProfiler.Database` static read by T1/T2/T4 set by T3 | Reference-typed static writes are release-ordered on x64; pairs with implicit acquire on read | None on x64; on ARM64 a release fence on the static would be belt-and-braces. Add `[Volatile]` attribute to the field as a v0.6 belt-and-braces. |
| `ProfilerSystem.Collector` etc. on T1 only | Same thread | n/a |

### 7.5 Recommendation matrix

| Field | Today | After v0.6 |
|---|---|---|
| `_approxQueueDepth` | Interlocked + Volatile | Unchanged |
| `_droppedWarmCount` | Interlocked + Volatile | Unchanged |
| `Channel<DbWriteOp>` | Internal lock-free | Unchanged |
| `_installed` (ILHook) | Plain bool (T3 writes sequenced before T1 reads) | `Volatile.Write/Read` |
| `_publishedInsights` | n/a | `Volatile.Write/Read` |
| `_publishedSnapshot` (T7) | n/a | `Volatile.Write/Read` (if T7 ships) |
| `DbWriteOp.Completion` (MRES) | n/a | MRES (Set/Wait are themselves full fences) |
| `PerformanceProfiler.Database` static | Plain | Recommend `[Volatile]` belt-and-braces |
| `MetricCollector.History` ring | Plain | T4 reads via T1-side snapshot copy (R1 fix). T7 reads via seqlock (v0.6.1 candidate). |

---

## 8. Dispose ordering contract

The full lifecycle from `Mod.Unload` outward. Order is load-bearing — getting it wrong leaks IL detours into other mods' code or loses session-end writes.

### 8.1 Today's order (v0.5)

```
T3: Mod.Unload
   │
   ├── ILHookInterceptor.Uninstall()
   │     for each ILHook: try { hook.Dispose(); } catch (log warn)
   │     clear _instrumentedHandles
   │     Installed = false
   │
   ├── try {
   │     Database?.Dispose()
   │         ↳ DbWriterThread.Dispose
   │             _queue.Writer.TryComplete()                  (signals T4 to stop)
   │             _cts.Cancel()                                (cancels WaitToReadAsync)
   │             T4.Join(10 s)                                (blocks T3)
   │                ↳ T4 hits finally → DrainAndShutdown
   │                    drain remaining ops from channel
   │                    journal.AppendBatch / Flush
   │                    db.ApplyBatch
   │                    db.Checkpoint()
   │                T4 exits Run
   │             _cts.Dispose()
   │         ↳ ProfilerDatabase.RotateBackups (synchronous on T3)
   │         ↳ LiteDatabase.Dispose
   │         ↳ EventJournal.TruncateOnCleanShutdown
   │   }
   │   catch (log warn)
   │
   └── Database = null
       LoggerOrNull = null
```

This order is **correct today**: detours are removed before the DB closes, the writer drains before the DB disposes, the journal truncates last.

### 8.2 Order after v0.6 (with T5, T6, optional T7)

```
T3: Mod.Unload
   │
   ├── 1. Stop new work
   │     T5.CTS.Cancel()  (install worker — if still running)
   │     T6.CTS.Cancel()  (insights reader)
   │     T7.CTS.Cancel()  (collector smoother — if enabled)
   │
   ├── 2. Wait for workers to exit (parallel joins; each capped)
   │     T5.Join(30 s)    (install can take 30 s to unwind cleanly)
   │     T6.Join(2 s)     (insights reader exits at next sleep boundary)
   │     T7.Join(2 s)     (smoother exits at next sleep boundary)
   │
   ├── 3. Uninstall detours
   │     ILHookInterceptor.Uninstall()
   │       (now safe — T5 has exited; no concurrent install/uninstall race)
   │
   ├── 4. Close the database (drains T4)
   │     Database?.Dispose()
   │       ↳ DbWriterThread.Dispose
   │           _queue.Writer.TryComplete()
   │           _cts.Cancel()
   │           T4.Join(10 s)
   │       ↳ … same as today …
   │
   └── 5. Null statics
       Database = null
       LoggerOrNull = null
```

**Why step 1 (cancel all) before step 2 (join):** if any worker is doing IO and blocks for a long time, cancelling all up-front lets them all unwind in parallel rather than serialising.

**Why step 3 after T5 joins:** if T5 is still installing detours while T3 starts uninstalling, the bookkeeping races. Joining first guarantees the set of installed detours is stable when Uninstall walks it.

**Why step 4 after step 3:** the journal flush in step 4's drain depends on the DB being writable. Step 3's Uninstall doesn't touch the DB. Order is robust.

### 8.3 Channel completion order

`_queue.Writer.TryComplete()` signals "no more writes will arrive". Important: T1 may still be in the middle of an enqueue call when TryComplete fires. The Channel implementation guarantees that any `TryWrite` racing with `TryComplete`:

- If TryWrite wins the race: the op is enqueued; T4 will see it before TryRead returns false-on-empty.
- If TryComplete wins: TryWrite returns false; T1's `if (_queue.Writer.TryWrite(op))` branch is not taken; the Interlocked.Increment is skipped. Correct.

After TryComplete, the channel acts as drained: `WaitToReadAsync` returns false when empty, `TryRead` returns false. T4's `Run` loop sees `readReady = false`, exits the while, hits the `finally`, calls `DrainAndShutdown`.

**`DrainAndShutdown` is reached only via T4's natural exit.** If T3 forces a kill (which it doesn't; we Join only), the drain doesn't run.

### 8.4 Journal flush ordering with the new SessionEndAggregate op

In v0.6, the SessionEndAggregate op runs `BuildModAggregates`/`BuildHookAggregates`/`SessionSummaryLogger.Write` on T4. These are reads of T1-snapshotted data plus writes to LiteDB. They must complete before the journal truncate.

Sequence inside T4's `Run` loop for a session-end batch:

```
batch = [..., SessionEndAggregate(snap, completion), ...]
journal.AppendBatch(batch)                            ← session-end op in journal
db.ApplyBatch(batch)                                  ← runs SessionEndAggregateStream.Apply
   ↳ BuildModAggregates(snap)
   ↳ BuildHookAggregates(snap)
   ↳ BuildArchive(snap, modAggs)
   ↳ db.PerSessionMods.InsertBulk(modAggs)
   ↳ db.PerSessionHooks.InsertBulk(hookAggs)
   ↳ db.TickAggregatesArchive.Insert(archive)
   ↳ db.Sessions.Update(...)
   ↳ SessionSummaryLogger.Write(logger, db, snap.SessionId)
   ↳ completion.Set()                                  ← unblocks T1's Wait
```

`SessionSummaryLogger.Write` runs **inside** Apply, which is **inside** ApplyBatch, which is **after** the journal append. The journal contains the SessionEndAggregate op; LiteDB contains the aggregate rows. Crash safety: if T4 crashes between Apply and completion.Set(), the next launch replays the journal — the SessionEndAggregate op is rerun → the aggregates are re-built → the session row is updated. Idempotent.

The journal truncate runs only in `DrainAndTruncateJournalForSessionEnd` on T1 (after MRES.Wait returns), or in `Run.finally → DrainAndShutdown` on T4. Order:

1. T1 (PreSaveAndQuit): enqueue SessionEnd op with MRES.
2. T4 (background): drains the op, applies, sets MRES.
3. T1 (OnWorldUnload): waits on MRES.
4. T1 (OnWorldUnload): calls `_db.Checkpoint()` then `_journal.TruncateOnCleanShutdown()`.

Step 4 truncates the journal **after** T4 has applied. The journal at truncate time contains only ops T4 has confirmed are durable in LiteDB. Safe.

### 8.5 What if Mod.Close fires (per mod-lifecycle.md §4.10)

`Mod.Close` may fire multiple times per tModLoader's documented behaviour. The implementation in `mod-lifecycle.md §4.10`:

```csharp
public override void Close() {
    base.Close();
    var sys = ModContent.GetInstance<ProfilerSystem>();
    if (sys?.HasLiveSession == true) {
        sys.ForceDirtyEnd("mod-close");
    }
}
```

`ForceDirtyEnd` must be idempotent: if it has run, the second call short-circuits. Implementation:

```csharp
public bool ForceDirtyEnd(string reason) {
    if (Interlocked.CompareExchange(ref _dirtyEndDone, 1, 0) != 0) return false;
    // … do the dirty end …
    return true;
}
```

`Interlocked.CompareExchange` ensures multiple `Mod.Close` calls collapse into one End. No race surface.

---

## 9. Abort-clean discipline interactions (Invariant 4)

Invariant 4: instrumentation failure disables instrumentation and reports it. Never proceeds against unverified internals. Never corrupts the player's run.

Audit each new failure mode introduced by v0.6 threading work.

### 9.1 T5 (install worker) failure

**Failure A — MonoMod throws on a specific method.**

- Caught by per-method try/catch (already in `ILHookInterceptor.Install`).
- Method is skipped; install continues.
- Log: `Logger.Warn($"ILHook install skipped method {method.DeclaringType}.{method.Name}: {ex.Message}")`.
- Final state: `_installed = true` (partial coverage, badged in SELF tab).

Conforms to Invariant 4: instrumentation disabled for the skipped method; rest of the surface is verified.

**Failure B — T5 throws a fatal exception (e.g. OOM during install).**

- Outer try/catch in T5's worker body.
- Finally: call `Uninstall()` to dispose every partial detour.
- Set `_installed = false` permanently for this session.
- Set `_installFailed = true` for diagnostics.
- Log: `Logger.Error($"ILHook install failed; instrumentation disabled for session: {ex}")`.
- Per-tick path: `if (!Volatile.Read(ref _installed)) skip IL timing`. The delegate-pair path continues to provide partial coverage.

Conforms to Invariant 4: full ILHook surface disabled; session continues with delegate-pair coverage; one warn line; no corruption.

**Failure C — `Mods → Reload` while T5 is mid-install.**

- T3 cancels T5's CTS; T5 sees cancellation at the next yield point; runs `finally` → Uninstall partial → exits.
- T3 then joins T5 (30 s cap); proceeds to step 3 of dispose order.

### 9.2 T4 (session-end aggregator) failure

**Failure A — T4 throws inside `SessionEndAggregateStream.Apply`.**

- Outer try/catch in T4's `Run` logs the error.
- MRES is never set.
- T1's `OnWorldUnload` Wait times out after 5 s, logs warn, proceeds.
- Session row stays `Incomplete = true`. Next launch's crash-detected path marks it `EndReason = "crash-detected"`.

Conforms to Invariant 4: data captured up to the failure is durable in LiteDB; the player's world save is unaffected (the world save runs in parallel on T1, not gated on T4).

**Failure B — Mod.Unload arrives while T4 is mid-Apply.**

- TryComplete + CTS.Cancel hit; T4's WaitToReadAsync returns or T4 is mid-Apply.
- If mid-Apply, T4 completes the current op (Apply is not interruptible — by design; mid-LiteDB-transaction abort risks DB corruption).
- T4's finally calls DrainAndShutdown.
- T3 joins T4 (10-s cap).

If Apply runs longer than 10 s, T3's Join times out. T3 proceeds past Dispose with T4 still running. The IsBackground = true flag means T4 dies when the process exits. LiteDB may be left in an inconsistent state — but LiteDB's WAL guarantees recoverability on next open.

**Mitigation:** Apply should not take longer than 10 s. The aggregate builders are bounded (modCount × hookCount ≈ 600 K operations ≈ 1 s on the writer thread). The journal append is bounded. The risk band is the SessionSummaryLogger.Write block (6 LiteDB queries) — measured at 0.5–1.5 s. Total worst case: ~3 s. Well under 10 s.

### 9.3 T6 (insights reader) failure

**Failure A — T6 throws during a query.**

- Outer try/catch in T6's loop logs warn, sleeps 1 s, retries.
- Published snapshot stays stale.
- Consumers continue to read the last-good snapshot.

**Failure B — LiteDB read-while-write reveals incompatibility.**

- During v0.6 soak, this is the specific risk. The connection-type verification step before T6 ships catches it. If incompatible, fall back to Option B (serialise T6's reads onto T4's loop).

**Failure C — Mod.Unload mid-query.**

- CTS.Cancel; LiteDB cursor either completes the row or unwinds.
- T6's finally exits.

### 9.4 T7 (collector smoother) failure

If T7 ships:

- T7 reads `MetricCollector` via read-only spans. The spans are immutable views; T1's concurrent writes only mutate cell values, not span bounds.
- T7 throwing on a NaN or out-of-range read: caught by outer try/catch, logs warn, retries.
- Mod.Unload: same pattern as T6.

### 9.5 Cross-thread interactions during world transition

Player presses Save & Quit while T5 is still installing (rare, but possible on a fresh launch followed by immediate world entry):

1. T3 fires `PreSaveAndQuit` (no — `PreSaveAndQuit` is T1, not T3). Sequence: T1's `PreSaveAndQuit` fires on the main thread. T5 is on its own thread, still installing. No interaction.
2. T1 enqueues SessionEnd op on T4.
3. T4 runs Apply; at this point T5 may still be writing to per-mod attribution arrays (because partial install means some mods are being added to the array set as T5 progresses). The snapshot T1 captured for the SessionEnd op was at the moment of `PreSaveAndQuit`, **before** any further T5 writes; T4 reads the snapshot, not the live arrays. Safe.
4. T1's `OnWorldUnload` waits on MRES. Returns.
5. World unload completes; player returns to main menu.
6. T5 continues installing in the background. When done, `_installed = true`. The next world enter has full coverage.

The interaction is handled: T1's snapshot decouples T4 from T5's ongoing writes.

### 9.6 The "host drift" interaction

Invariant 4's primary concern is host drift: tModLoader changes an internal signature; our IL detour reads the wrong field; corruption.

T5's install path performs the same signature checks as today's synchronous install. The only change is **when** the check fires. The check itself is unchanged: per-method, the manipulator reads the IL, verifies expected shape, emits the wrapper. On mismatch, the method is skipped (Invariant 4 hard rule). T5 still upholds this.

**Conclusion:** every new thread crossing introduced by v0.6 has a documented failure path that disables instrumentation cleanly, logs once, and never corrupts game state. Invariant 4 audit clears.

---

## 10. References

### 10.1 In-tree sources

- `Profiling/Persistence/DbWriterThread.cs:1-225` — the canonical channel + writer thread implementation. Every line of sync primitive usage in the mod lives in this file.
- `Profiling/Persistence/DbWriteOp.cs:1-140` — the op struct passed across the channel.
- `Profiling/MetricCollector.cs:1-529` — per-tick state, the per-mod-category aggregates that T4 will snapshot in v0.6.
- `Profiling/RingBuffer.cs:1-120` — line 17 explicitly documents "intentionally not synchronised" for the per-tick history ring; the design intent that T1 owns writes and T2 reads tolerantly.
- `Profiling/PerTickAttributionRing.cs` — same single-writer pattern.
- `Profiling/ILHookInterceptor.cs:137-215` — current synchronous install; v0.6's BeginInstallAsync wraps this body in a Thread.
- `Profiling/HookInterceptor.cs:1-120` — delegate-pair install (synchronous on T3; not relocated in v0.6).
- `Profiling/ProfilerSystem.cs:85-239` — lifecycle entry points; the dispose order audit anchors here.
- `Profiling/Persistence/SessionRecorder.cs:199-222` — `End` body whose contents move to T4 in v0.6.
- `Profiling/Persistence/ProfilerDatabase.cs:83-247` — DB open / dispose; T4 spawn site.
- `Profiling/Persistence/SessionSummaryLogger.cs:25-87` — six LiteDB reads relocated from T1 to T4 in v0.6.
- `context/perf-pass/baseline.md` — the v0.5 measurements all v0.6 targets reduce against.
- `context/notes/philosophy.md` — the "optimisation = doing the same work cheaper" rule.
- `context/perf-pass/research/persistence.md §5.2, §5.6, §6.3` — the persistence-side threading proposals merged here.
- `context/perf-pass/research/mod-lifecycle.md §4.1, §4.3, §4.5, §4.7, §4.10` — the lifecycle-side threading proposals merged here.
- `context/perf-pass/research/insights-engine.md §6.3, §13, §16, §18` — the insights reader thread proposal and the `_topComparerNowTick` race.
- `context/perf-pass/research/metric-collection.md §4.13, §4.20` — the optional T7 smoother thread proposal and the enqueue regression analysis.
- `context/perf-pass/research/stall-detection.md` — stall detector's relationship to the off-thread snapshot.
- `context/perf-pass/research/overlay.md` — draw-thread reads from the published snapshot.

### 10.2 .NET / runtime sources

- `https://learn.microsoft.com/en-us/dotnet/standard/threading/memory-model` — .NET 8 memory model; release/acquire semantics of `Volatile.Write/Read`.
- `https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels.channel` — Channel<T> contract; `SingleConsumerUnboundedChannel` internal type.
- `https://devblogs.microsoft.com/dotnet/an-introduction-to-system-threading-channels/` — Microsoft's introduction; performance characteristics of unbounded vs bounded channels.
- `https://www.codegenes.net/blog/when-should-system-threading-channels-be-preferred-to-concurrentqueue/` — Channels vs ConcurrentQueue vs BlockingCollection benchmarks (cited in `metric-collection.md §3.5`).
- `https://learn.microsoft.com/en-us/dotnet/api/system.threading.interlocked` — Interlocked atomics; LOCK XADD semantics.
- `https://learn.microsoft.com/en-us/dotnet/api/system.threading.volatile` — Volatile.Read/Write semantics; 64-bit-on-32-bit fallback to Interlocked.Read.
- `https://learn.microsoft.com/en-us/dotnet/api/system.threading.manualreseteventslim` — MRES contract; lightweight wait/set with optional spin before kernel transition.
- `https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-8.0` — per-thread allocation read; no cross-thread coupling.

### 10.3 LiteDB sources

- LiteDB issue #1568 — log-file growth mitigation drives the 60-second checkpoint cadence; in v0.6, T4's checkpoint cadence is unchanged.
- LiteDB issue #1511 / #1775 — `db.Checkpoint()` deadlocks if called from inside an Insert callback. In v0.6, the SessionEndAggregateStream calls Checkpoint via the T4 main loop's MaybeCheckpoint, not inside Apply.
- LiteDB v5 read-while-write semantics — to be verified before T6 ships; the soak test in §6.3 is the verification path.

### 10.4 tModLoader sources (verified via `gh api`)

- `patches/tModLoader/Terraria/ModLoader/Mod.cs` — `Mod.Load` / `Mod.Unload` thread is the AssemblyManager load worker (T3).
- `patches/tModLoader/Terraria/ModLoader/ModSystem.cs` — lifecycle methods all fire on the main thread (T1) except where overridden.
- `patches/tModLoader/Terraria/ModLoader/SystemLoader.cs:156-190, 457-463` — `OnWorldLoad`/`OnWorldUnload`/`PreSaveAndQuit` enumerate hooks synchronously on the calling thread; `PreSaveAndQuit` has no exception wrapper at SystemLoader level (mandates a mod-side try/catch — see `mod-lifecycle.md §4.7`).
- `patches/tModLoader/Terraria/WorldGen.cs.patch:331-355` — call sites for `OnWorldUnload` and the `SaveAndQuit` sequence; confirms T1 is the thread.
- MonoMod RuntimeDetour 25.3.2 (`MonoMod.RuntimeDetour/DetourManager.cs`) — `ILHook` is thread-safe at construction and disposal level; per-method state guarded by internal `lock`. This is the basis for T5's off-thread install being safe.

### 10.5 Invariants exercised in this dossier

- **Invariant 1 (read-only instrumentation):** none of the v0.6 thread changes mutates game state. T5 patches IL into other mods' code (the same surface as today's synchronous install); T4 writes only to our own LiteDB; T6/T7 are read-only against game state.
- **Invariant 2 (overhead budget, zero-alloc hot path):** T1's per-tick path adds one `Volatile.Read<bool>` (`_installed`) per gated hook call (~1 ns), one `Volatile.Read<reference>` per snapshot consumer (~1 ns). No new allocation, no new lock. Within budget.
- **Invariant 3 (descriptive, never normative):** no insight or UI copy is introduced by this dossier. The only new UI text is the "install N % complete" badge in §6.2 — descriptive measurement.
- **Invariant 4 (abort-clean on host drift):** §9 audits every new failure mode. Each disables instrumentation cleanly, logs once, never corrupts game state.
- **Invariant 5 (no mod-specific code):** unchanged. All new threading work operates on generic surfaces.

### 10.6 Open questions for follow-up sessions

1. **Verify LiteDB v5 read-while-write on `ConnectionType.Direct`.** Required before T6 ships. Soak test in §6.3.
2. **Decide T7 ship in v0.6 or v0.6.1.** Current recommendation: defer. The stall-fix targets are met without T7.
3. **Quantify the `_approxQueueDepth` Volatile.Read cost on ARM64.** On Apple Silicon (the dev target), the acquire fence is `dmb ld`; cost ~3–5 ns. Negligible. Confirm with `dotnet-counters` once v0.6 ships.
4. **Audit every `PerformanceProfiler.Database` read site for local-variable capture.** R5; pure-mechanical sweep across `Profiling/`.
5. **Decide whether to ship the seqlock on `MetricCollector.History`.** Filed as a v0.6.1 candidate; the current frame-jitter is cosmetic, the seqlock cost is one extra store per commit (~1 ns) and an occasional reader retry.

---

*Dossier ends. The thread model after v0.6 is: T1 (game), T2 (draw), T3 (loader), T4 (writer, extended with SessionEndAggregate handling), T5 (install worker, transient), T6 (insights reader). T7 (smoother) is conditional on the metric-collection §4.13 ship decision. Zero `lock` statements anywhere. One `Channel<DbWriteOp>` (unchanged policy). Five new sync primitive sites: one MRES per session-end, two `Volatile` publish slots, two `Volatile` flags. The per-tick hot path remains lock-free and zero-allocation. Every new thread crossing has a defined Invariant-4 failure path. The dispose order is Cancel → Join (parallel) → Uninstall → Database.Dispose → null statics.*
