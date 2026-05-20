# Mod Lifecycle — Optimisation Research and Plan (v0.5 → v0.6)

> System scope: `PerformanceProfiler.Load` / `Unload`, `ProfilerSystem.PostSetupContent` / `OnWorldLoad` / `OnWorldUnload` / `PreUpdateEntities` / `PostUpdateEverything`, the session-end aggregation path in `SessionRecorder.End`, and the install/teardown discipline of `HookInterceptor` + `ILHookInterceptor`. This is the **orchestrator layer** — the thin glue that decides *when* heavy subsystem work runs against tModLoader's lifecycle, not the per-tick math itself.
>
> The pass keeps every capture stream intact (philosophy.md: optimisation = doing the same observable work cheaper). Every recommendation here is a *relocation* in time/thread or a *batching* change — no aggregation surface is removed, no hook is skipped, no telemetry stream is sacrificed.
>
> Three pain points drive this dossier:
>
> 1. **End-of-session 8.5-s `UiOverlayBlocking` stall** with `PerformanceProfiler` named as contributor (baseline.md row 28). The session-summary aggregation runs synchronously on the main thread inside `OnWorldUnload`. The player sees the save-and-quit screen freeze for the duration.
> 2. **World-load freeze of 172 ms first tick** (baseline.md row 5) plus **10–18 s of `ILHookInterceptor` install** before the world becomes playable. Today both run blocking on the main thread inside `PostSetupContent`.
> 3. **Resource ownership across `Mods → Reload`** — what is allocated at `Mod.Load`, freed at `Mod.Unload`. Three latent leak surfaces, none confirmed but each cheap to make explicit.

---

## 1 — Current state audit

Every lifecycle entry point on the orchestrator, walked in dispatch order. For each, the audit names: when it fires, what thread, what work it does today, what it allocates, what it frees, and where each call site is sourced.

### 1.1 `PerformanceProfiler.Load()`  — `PerformanceProfiler.cs:39-70`

| Property | Reality |
|---|---|
| Fires | Once, during tModLoader content-load phase, *before* `PostSetupContent` runs for any mod. |
| Thread | tModLoader load thread (not the game thread; runs while the splash screen renders). |
| Work today | (a) cache `Logger` into `LoggerOrNull` static, (b) `Logger.Info` line, (c) `new ProfilerDatabase(root, log, version)` which opens LiteDB, runs `RecoverIfNeeded`, `EnsureSchemaVersion`, `EnsureAllIndexes`, `PreWarmCollections`, replays journal, marks crash sessions, sweeps warm tier, runs initial `Checkpoint`, spawns `DbWriterThread`, (d) `LegacyJsonImporter.RunOnceIfNeeded`, (e) one info log on success or one warn log on failure. |
| Allocates | `ProfilerDatabase` (long-lived; one `LiteDatabase`, one `EventJournal`, one `DbWriterThread`, one `StreamRegistry`, plus the channel + reader thread inside the writer). |
| Frees | Nothing. Paired by `Unload`. |
| Failure posture | Wrapped in try/catch on the whole bring-up block; failure leaves `Database = null` and the rest of the mod still runs (Invariant 4: abort-clean — the "host" here is the file system). |

The single dependency edge into the rest of the system is `PerformanceProfiler.Database`, a static read by `ProfilerSystem.OnWorldLoad` to construct the per-world `SessionRecorder`.

### 1.2 `PerformanceProfiler.Unload()` — `PerformanceProfiler.cs:79-92`

| Property | Reality |
|---|---|
| Fires | Once on `Mods → Reload`, on game-process shutdown, and (per `ProfilerDatabase.DrainAndTruncateJournalForSessionEnd` comment) **not reliably** on quit-to-desktop on macOS. |
| Thread | tModLoader unload thread. |
| Work today | (a) `ILHookInterceptor.Uninstall()` — disposes each tracked `ILHook` and clears bookkeeping, (b) try/catch around `Database?.Dispose()` — drains writer thread (10 s join timeout), rotates backups, disposes LiteDB, truncates journal, (c) clear `Database` + `LoggerOrNull` statics. |
| Allocates | A new `List<DbWriteOp>(BatchCap)` inside `DbWriterThread.DrainAndShutdown` (one-shot, transient). |
| Frees | Every long-lived resource introduced in `Load` plus the IL detours. |
| Known gap | `Mod.Unload` is **not** the right place for any work that depends on `_recorder` or the per-world `Collector` — those are nulled out in `OnWorldUnload`, which runs first. |

`Mod.Unload` order vs. `OnWorldUnload`: tModLoader fires `SystemLoader.OnWorldUnload()` inside `WorldGen.SaveAndQuit` / `Netplay.InnerClientLoop` (sources: `patches/tModLoader/Terraria/WorldGen.cs.patch:335`, `Netplay.cs.patch`, `Main.cs.patch`). `Mod.Unload` only fires on full mod-reload or process exit, **strictly later** than `OnWorldUnload`. So by the time we reach `Unload` here, the recorder has already drained and the DB writer has the session-end ops queued. The only thing this entry-point does that *cannot* be moved earlier is the `ILHook` dispose — IL detours patch *other mods'* methods which live as long as those mods do, not as long as the world does.

### 1.3 `ProfilerSystem.PostSetupContent()` — `Profiling/ProfilerSystem.cs:85-127`

Fires once per launch, after **every** mod's `Load` has run and content has been registered. tModLoader source: `SystemLoader.PostSetupContent` enumerates `HookPostSetupContent` in load order.

| Block | Work | Cost (baseline.md) |
|---|---|---|
| `SelfHealth.MarkInstallStart()` | Forces a `GC.Collect(2, Forced, blocking: true)` + `GC.WaitForPendingFinalizers()` + reads `GC.GetTotalMemory(forceFullCollection: true)` to capture a clean baseline. | ~50–150 ms once. |
| `HookInterceptor.Install(Mod)` | Walks `ModLoader.Mods` once, enumerates *every* hook-override `MethodInfo` on *every* content type across *every* mod, signature-matches against ~30 hand-written delegate-pair shapes, installs a `MonoModHooks.Add` detour per match, builds `PerModAttribution.Configure` row + `HookCategoryRouter` table. | Lion's share of the install — for the baseline 10,258 hooks the delegate path measures in the 5–10 s range. Single-threaded. |
| `ILHookInterceptor.Install(Mod, ProfiledMods)` | Re-walks the **same** mod surface, this time constructing an `ILHook` per discovered override (Cecil reads the method body, the manipulator emits the timing wrapper, MonoMod compiles a JITted trampoline). Every closed-generic instantiation is hashed into `_instrumentedHandles` for dedupe. | Dominant cost — Mono.Cecil method-body cache + JIT trampolines drive the 322–618 MB delta and the 5–8 s slice of the 10–18 s wall time. |
| `SelfHealth.MarkInstallEnd(HookCount)` | Reads `GC.GetTotalMemory(forceFullCollection: true)` again. Computes delta + bytes/hook. Logs one info line. | ~50 ms (another full GC). |
| `BiomeRegistry.Populate()` | Walks `ModContent.GetContent<ModBiome>()`, builds the bit-index table. | Sub-ms for typical modlists; scales with modded biome count. |
| `SubworldProbe.Initialise()` | Reflection probe binding to SubworldLibrary if present. | Sub-ms. |
| One `Logger.Info` line | Context registry summary. | trivial. |

**Total `PostSetupContent` cost:** 10–18 s wall, 322–618 MB heap delta. All on the tModLoader load thread, but that thread *is* the same thread that paints the splash + "Loading" screen — the player is staring at a static frame the whole time.

### 1.4 `ProfilerSystem.OnWorldLoad()` — `Profiling/ProfilerSystem.cs:134-185`

Fires once per world entry. tModLoader source `SystemLoader.OnWorldLoad` is called *before* `LoadWorldData` for every system.

Work, in order:

1. `Collector = new MetricCollector(HistoryCapacity, SelfHealth)` — allocates the 1800-frame `RingBuffer<TickFrame>` plus per-mod-category attribution slots. **Single large allocation pass**, paid every world enter.
2. Fingerprint compute: `ModlistFingerprint.Compute()` — hashes the active modlist into a stable id. Reasonably cheap (single-pass over `HookInterceptor.ProfiledModNames` + version strings).
3. `new SessionRecorder(db, profilerVersion, tmlVersion, mode, tracksAllocations, fingerprint, worldId: null)` which immediately enqueues `DbWriteOp.SessionStart(row)` to the writer thread.
4. `EnqueueModlistUpserts(db, fingerprint)` — for each mod, allocates one `ModRow` + one `ModVersionEntry` list, enqueues one upsert op per mod. **Allocation pressure** at exactly the moment the player is mid-fade-into-world.
5. `_transitionWatcher = new ContextTransitionWatcher()` / `_snapshotter = new WorldSnapshotter()` / `_deathDetector = new PlayerDeathDetector()` — three per-world watcher allocations.
6. `_contextTagger = new ContextTagger(); _contextTagger.Reset();` — context tagger plus its internal state init.
7. `Events = new EventAggregator()` — per-dimension bucket aggregator.
8. `Logger.Info` line.

**Cost surface:** five small constructor calls, one fingerprint pass, the upserts loop. The 172-ms first-tick freeze observed in baseline.md is *partly* attributable to this — but the first frame after `OnWorldLoad` also triggers JIT compilation of the per-tick pipeline (`MetricCollector.BeginTick/EndTick`, `PerTickAttributionRing`, `StallDetector`, etc.), so the 172 ms is a *composite* of (a) the OnWorldLoad allocation pass, (b) first-call JIT, (c) the first call into a freshly-detoured method which forces MonoMod's trampoline compilation. Section 4.4 unpicks the three contributors and proposes per-cause mitigation.

### 1.5 `ProfilerSystem.OnWorldUnload()` — `Profiling/ProfilerSystem.cs:188-239`

Fires on world transition (player chooses Save & Quit, server kicks client, game crashes cleanly). Thread: **main game thread**, inside `WorldGen.SaveAndQuit` / `Netplay.InnerClientLoop` / `Main.cs`. UI is still being painted at this point — *everything* this method does is in the player's stall budget.

Block-by-block walk:

1. `Collector?.FlushSpikes()` — closes the open spike window if any. Cheap (no IO).
2. **The `End(Collector, endReason: "clean")` block** — this is the headline 8.5-s contributor:
   - `DrainSpikes` + `DrainStalls` — final cursor advance, builds final `SpikeWindowRow` / `StallEventRow` per item and enqueues each. Allocation-heavy but the enqueue itself is fast (lock-free channel write).
   - `FlushCluster` — emits the final `StallClusterRow`.
   - `BuildModAggregates(collector)` — **per-mod loop, ~30–60 mods × `categoryCount` categories**. Allocates `List<double>(categoryCount)` per mod plus per-mod `BuildTopHooks` which scans every `PerModAttribution.Hooks` entry (10,258 in baseline) to find the top-5 hook contributors *for that mod*. Total: ≈ 10,258 × 60 hook-vs-mod comparisons + 60 × 5-element sort + 60 × `PerSessionModAggregate` allocations + 60 × `ModCoverage` allocations. This loop alone is the dominant chunk of the 8.5-s stall.
   - `BuildHookAggregates(collector)` — full 10,258-entry walk, allocates a `PerSessionHookAggregate` for each non-silent hook. Linear in total hook count.
   - `BuildArchive(collector, modAggs)` — one final pass over 1800-frame history, sums + maxes frame ms. Sub-ms; tiny.
   - Enqueues `DbWriteOp.ModAggregateBatch`, `HookAggregateBatch`, `ArchiveAggregate`, `SessionEnd`.
3. `PerformanceProfiler.Database?.DrainAndTruncateJournalForSessionEnd()` — **busy-wait** on `_writer.ApproxQueueDepth > 0` with `Thread.Sleep(20)` × up to 100 iterations (2 s soft cap). Then `_db.Checkpoint()` (synchronous on main thread). Then `_journal.TruncateOnCleanShutdown()`. The wait *can* be a significant fraction of the stall by itself: 64-op batches at 314 ops/sec (baseline.md row 1) means a final flush of, say, 50,000 queued events takes ~160 batches × ~200 ms each ≈ 30 s of writer-thread work, but the game thread only waits up to 2 s before giving up.
4. `SessionSummaryLogger.Write(Logger, Database, sessionId)` — **six synchronous LiteDB queries** (`db.SpikeWindows.Find(x => x.SessionId == ...)`, same for Stalls/Clusters/Deaths/Transitions/Snapshots) plus three more (`TickAggregatesArchive.FindOne`, ordered scan of clusters/spikes, `Take(3)` of `PerSessionMods`). Each `Find` materialises the result set fully. This block re-queries data that was *just* written and runs entirely on the main thread.
5. The teardown block — nulls every per-world reference, clears `InsightsEngine.Shared`, `BossSampler.Clear()`, `SubworldProbe.Clear()`, logs one info line. Cheap.

| Sub-step | Estimated contribution to 8.5-s stall |
|---|---|
| `SessionRecorder.End` body (BuildModAggregates + BuildHookAggregates) | ≈ 3–5 s |
| `DrainAndTruncateJournalForSessionEnd` (busy-wait + sync Checkpoint) | ≈ 1.5–3 s (Checkpoint scales with unflushed journal pages) |
| `SessionSummaryLogger.Write` (six queries + scan + Take(3) sort) | ≈ 0.5–1.5 s |
| Misc (FlushSpikes, teardown, logging) | ≈ 0.2 s |

The session-summary block is *all* main-thread, *all* synchronous, and *all* relocatable to the writer thread or to a post-unload deferred path.

### 1.6 `ProfilerSystem.PreUpdateEntities()` / `PostUpdateEverything()` — `Profiling/ProfilerSystem.cs:245-331`

Per-tick. Out of strict scope for the lifecycle pass — this dossier touches them only where ordering relative to OnWorldLoad/Unload matters. The relevant invariants:

- `Collector?.BeginTick((long)Main.GameUpdateCount)` is null-safe; if `OnWorldLoad` is mid-allocation when the first tick fires, the call is a no-op until `Collector` is assigned. This means there is a (small) window where `OnWorldLoad` *could* be reordered to allocate the Collector last, deferring the per-world recorder construction off the world-enter critical path. See §4.4.
- `PostUpdateEverything` guards on `Collector.TickOpen`. Partial-frame protection.
- The `_recorder.OnTick(latest, collector)` call inside `PostUpdateEverything` queues work to the writer thread on every tick. Game-thread work per tick is one method call, three `Drain*` cursor advances, and one channel write per spike/stall/transition/etc. Not in the lifecycle pass's scope, but the writer-thread capacity caps how fast `OnWorldUnload`'s drain can complete (§4.1).

### 1.7 `ProfilerPlayer.OnEnterWorld()` / `ProcessTriggers` — `PerformanceProfiler.cs:100-120`

Out of scope for performance; they are correct and cheap. Note only that `OnEnterWorld` fires *after* `OnWorldLoad` (tModLoader's `ModPlayer.OnEnterWorld` is invoked from `Player.Spawn` post-load); the chat announcement is therefore in the correct lifecycle window.

### 1.8 Cross-class entry summary

```
PROCESS START
   │
   ├── (tModLoader load thread) PerformanceProfiler.Load
   │       └─ open ProfilerDatabase ──► spawn DbWriterThread
   │
   ├── (tModLoader load thread) ProfilerSystem.PostSetupContent
   │       ├─ SelfHealth.MarkInstallStart (forced Gen2)
   │       ├─ HookInterceptor.Install (10,258 detours)            ◄── ~5-10 s
   │       ├─ ILHookInterceptor.Install (Cecil + JIT trampolines) ◄── ~5-8 s
   │       ├─ SelfHealth.MarkInstallEnd (forced Gen2)
   │       ├─ BiomeRegistry.Populate
   │       └─ SubworldProbe.Initialise
   │
WORLD ENTER
   │
   ├── (main thread) ProfilerSystem.OnWorldLoad
   │       ├─ new MetricCollector(1800)
   │       ├─ new SessionRecorder + EnqueueModlistUpserts
   │       ├─ new watchers (transition / snapshot / death)
   │       └─ new ContextTagger / EventAggregator
   │
PLAY
   │
   ├── (main thread, 60 Hz) PreUpdateEntities / PostUpdateEverything
   │
WORLD EXIT
   │
   ├── (main thread, BLOCKING UI) WorldGen.SaveAndQuit
   │       ├─ SystemLoader.PreSaveAndQuit  (not handled today)
   │       ├─ {vanilla save work}
   │       └─ ProfilerSystem.OnWorldUnload
   │             ├─ FlushSpikes
   │             ├─ SessionRecorder.End                ◄── 3-5 s
   │             ├─ Database.DrainAndTruncate          ◄── 1.5-3 s
   │             └─ SessionSummaryLogger.Write         ◄── 0.5-1.5 s
   │
   └── (eventual) Mod.Unload  (mods → reload / process exit)
           ├─ ILHookInterceptor.Uninstall
           └─ Database.Dispose (writer.Join 10 s)
```

Three thread-stall hotspots are visible from the diagram alone: the `PostSetupContent` 10–18 s install, the world-enter allocations + first-tick JIT, and the world-exit aggregation. All three are in scope.

### 1.9 Per-method allocation ledger (lifecycle layer only)

Catalogues every heap allocation visible from the lifecycle layer (does not chase into per-tick or per-event paths). Used to compute the §4.9 alloc-removal targets.

| Entry point | Allocation | Reason | Frequency | Lifetime |
|---|---|---|---|---|
| `Mod.Load` | `new ProfilerDatabase(...)` | DB facade | once / process | process |
| `Mod.Load` (inside DB ctor) | `new LiteDatabase`, `new EventJournal`, `new DbWriterThread`, `new StreamRegistry`, `new Channel<DbWriteOp>`, `new Thread` | DB subsystems | once / process | process |
| `Mod.Load` | `Action<string,Exception?>` closure for the `log` arg | closure capture of `Logger` | once / process | process |
| `PostSetupContent` | `int[ProfiledMods.Count]` × 2 in `ILHookInterceptor.Install` | per-mod measured / total hook counts | once / process | process |
| `PostSetupContent` | `Type[]` per `AssemblyManager.GetLoadableTypes` (× 2 walks × N mods) | reflection introspection | N×2 / process | transient (GC) |
| `PostSetupContent` | `MethodInfo[]` per `type.GetMethods` (× 2 walks × M types) | reflection introspection | M×2 / process | transient (GC) |
| `PostSetupContent` | `HashSet<RuntimeMethodHandle>` for closed-generic dedupe | dedupe set | once / process | process |
| `PostSetupContent` | `List<ILHook>` grows to ~10,258 entries | dispose-tracking list | once / process | process |
| `PostSetupContent` | one `ILHook` per detoured method | timing detour | 10,258 / process | process |
| `OnWorldLoad` | `new MetricCollector(1800, SelfHealth)` plus its internal ring buffer + per-mod-category slot arrays | per-world collector | once / world | world |
| `OnWorldLoad` | `new SessionRecorder(...)` | per-world recorder | once / world | world |
| `OnWorldLoad` (inside SessionRecorder ctor) | `new TickDownsampler`, `new Dictionary<int,double>`, `new Dictionary<string,int>`, `new SessionRow` | recorder internals | once / world | world |
| `OnWorldLoad` (`EnqueueModlistUpserts`) | per-mod `new ModlistRow`, `new List<ModEntry>(modCount)`, `new ModEntry`, `new ModRow`, `new List<ModVersionEntry>`, `new ModVersionEntry` | modlist upsert rows | modCount × 2 / world | transient — consumed by writer |
| `OnWorldLoad` | `new ContextTransitionWatcher`, `new WorldSnapshotter`, `new PlayerDeathDetector` | watcher trio | once / world | world |
| `OnWorldLoad` | `new ContextTagger`, `new EventAggregator` | context / events | once / world | world |
| `OnWorldUnload` | `DrainSpikes` per-row `new SpikeWindowRow` + `BuildSpikeTopContributors` `new List<SpikeContributor>` + per-spike `ToList(float[])` × 2 | row materialisation | spikeCount / world | transient — consumed by writer |
| `OnWorldUnload` | `DrainStalls` per-row `new StallEventRow` + `new List<StallContributorEntry>(5)` + `_clusterCauseCounts` entries | row materialisation | stallCount / world | transient — consumed by writer |
| `OnWorldUnload` (`BuildModAggregates`) | `new List<PerSessionModAggregate>(modCount)`, `new List<double>(categoryCount)` per mod, `new PerSessionModAggregate` per mod, `new ModCoverage` per mod, `new List<TopHookEntry>` per mod | aggregate rows | modCount × ~3 / world | transient — consumed by writer |
| `OnWorldUnload` (`BuildHookAggregates`) | `new List<PerSessionHookAggregate>(hooks.Count)` + per-non-silent-hook `new PerSessionHookAggregate` | aggregate rows | up to hookCount / world | transient — consumed by writer |
| `OnWorldUnload` (`BuildArchive`) | `new List<ArchivePerMod>(modCount)` + per-mod `new ArchivePerMod` + `new TickAggregateArchive` | archive row | once / world | transient — consumed by writer |
| `OnWorldUnload` (`SessionSummaryLogger.Write`) | `new StringBuilder(512)` + `Take(3).ToList()` + six `Find(...).Count()` enumerator allocations | log block | once / world | transient (GC) |
| `Mod.Unload` (`DbWriterThread.DrainAndShutdown`) | `new List<DbWriteOp>(BatchCap)` reuse + final journal append | drain | once / process | process-end |

This ledger drives §4.9. After phases 6.1–6.4, every "transient — consumed by writer" row migrates to the writer thread and stops adding to the main-thread allocation rate during world transitions.

### 1.10 Failure-mode matrix

| Failure | Where caught today | Behaviour today | What still leaks |
|---|---|---|---|
| `ProfilerDatabase` ctor throws (IO / corrupt DB) | `Mod.Load` outer try/catch | `Database = null`; metric collection continues in-memory | nothing — degraded-mode is intended |
| `HookInterceptor.Install` throws mid-pass | none at top level | exception propagates to tModLoader; mod load fails | every detour installed so far stays live → next tick crashes (Invariant 4 violation; unobserved because `HookInterceptor` install rarely throws — but §4.5's off-thread path must close this gap) |
| `ILHookInterceptor.Install` throws between methods | `Install` outer try/catch | `Installed = false`; calls `Uninstall()` | nothing — partial detours disposed |
| `SessionRecorder.End` throws | `OnWorldUnload` outer try/catch | warn log; world unload continues | recorder + writer state stays clean because the throw happens before the enqueue |
| `DrainAndTruncateJournalForSessionEnd` throws | inner try/catch | log line; continues | journal may be stale; recovered on next launch via `ReplayJournalIfNeeded` |
| `SessionSummaryLogger.Write` throws | inner try/catch | warn log; continues | nothing |
| `PreSaveAndQuit` throws (after §4.7) | tModLoader does **not** catch | exception propagates into `WorldGen.SaveAndQuit` — could prevent the world save | **MUST** wrap the new handler's body in try/catch (called out in §4.7 implementation note) |
| `Mod.Unload` throws | tModLoader caches via `MonoModException` reporting | `Logging.tML.Error` line; the mod stays unloaded | partially-disposed ILHooks may leak; mitigated by `Uninstall`'s per-hook try/catch |

The PreSaveAndQuit row is the one new exposure introduced by the pass; it is the single load-bearing safety wrap in the new code.

---

## 2 — Baseline numbers (referenced from `context/perf-pass/baseline.md`)

| Metric | v0.5 | Target (post-pass) | Note |
|---|---|---|---|
| End-of-session UI stall (contributor `PerformanceProfiler`) | **8.5 s across 40 stalls** | **0 ms** on the main thread — every aggregation step relocated to writer thread or post-unload deferred path. | The headline number. |
| World-load first-tick freeze | **172 ms** | **< 30 ms** by deferring construction + warm-priming the JIT. | Composite — see §4.4 for split. |
| Hook install delta | **322–618 MB heap, 5–10 s wall, 10,258 hooks** | **< 80 MB at same coverage** (target row in baseline.md). Wall-time target: stay under 6 s combined with the heap target. Yielding the install pass across multiple frames is the lever. | Cannot be skipped (Invariant 5: no mod-specific code, so we instrument the full surface). |
| Process-thread DB writer drain (queue-depth at OnWorldUnload entry) | up to 5,000+ ops queued at end-of-session | post-drain queue depth: 0 within < 1 s of OnWorldUnload entry — and that work happens off-thread. | The `Thread.Sleep(20) × 100` busy-wait disappears. |
| `Mod.Unload` writer-thread join | 10 s timeout, typically completes in < 1 s | unchanged target; current behaviour is correct. | Includes journal truncate + DB Checkpoint + backup rotation. |
| Heap leak across Mods → Reload | Unmeasured. | Add a self-test (Phase 6) that does five reload cycles and asserts the steady-state heap is within ±10 % of cycle 1. | One-shot diagnostic, lives in `Tests/`. |

Every other baseline row is owned by other research dossiers (`metric-collection.md`, `persistence.md`, etc.) and not re-litigated here.

---

## 3 — tModLoader lifecycle deep-dive

The cost of every line in §1 depends on *when* and *on which thread* tModLoader is willing to call us. Source verification from `tModLoader/tModLoader` patches.

### 3.1 Mod.Load and Mod.Unload

- `Mod.Load` is invoked from `AssemblyManager` during the assembly-load + content-load phase, sequentially per mod in dependency order. Source: `patches/tModLoader/Terraria/ModLoader/Mod.cs`. The thread is *not* the main XNA game thread; it is the tModLoader splash-screen worker thread. Implication: we can do CPU work here without blocking the game-thread, but we *cannot* touch `Main.dedServ`/`Main.npc`/etc.; the game has not yet booted.
- `Mod.Unload` runs on the same loader thread, in reverse order. Source: same file. Inside `Unload`, the assembly is *about to* be unloaded; if we leave any patched IL pointing into our types, the next tick after the unload `InvalidProgramException`s. This is why `ILHookInterceptor.Uninstall()` must live here.

**The non-deterministic case:** quit-to-desktop on macOS sometimes fails to fire `Mod.Unload` (documented inline as the rationale for `DrainAndTruncateJournalForSessionEnd` in `OnWorldUnload`). This is the load-bearing reason the journal-flush logic is duplicated between `OnWorldUnload` and `Mod.Unload`. The duplicate path is correct; we do not propose removing it.

### 3.2 ModSystem.PostSetupContent

- Fires from `SystemLoader.PostSetupContent` in load order across every `ModSystem`. Source: `patches/tModLoader/Terraria/ModLoader/SystemLoader.cs`.
- Thread: tModLoader load thread (same as `Mod.Load`). The splash screen is still up.
- All mods have finished `Load` by this point and `ModContent.GetContent<T>()` is fully populated; this is the earliest moment we can enumerate the mod surface for hook discovery.
- **Latitude:** this hook can take seconds without blocking the game thread per se, but the user perceives it as "tModLoader is taking forever to launch". We *cannot* defer to a later thread cleanly because `PostSetupContent` is the only callback where every mod is loaded **and** we are guaranteed to be called before any world enters. But we *can* split the work — see §4.2.

### 3.3 ModSystem.OnWorldLoad

- Fires from `SystemLoader.OnWorldLoad` on the **main game thread** during world bring-up (Main menu → loading screen). Source: `SystemLoader.cs:156`. Called **before** `LoadWorldData` per system (per the XML doc on `ModSystem.OnWorldLoad`).
- The "loading world" splash blocks the player; whatever we do here is in their visible stall budget.
- Allowed: allocate per-world state, register watchers, prime caches. *Not* allowed: anything that calls into `Main.npc` / `Main.player[]` (those are bound during `PostWorldLoad`, not `OnWorldLoad`).

### 3.4 ModSystem.OnWorldUnload (and the SaveAndQuit chain)

- Fires from `WorldGen.SaveAndQuit`, `Netplay.InnerClientLoop` (client kick), and `Main.cs` (cleanup). Source: `WorldGen.cs.patch:335`, `Netplay.cs.patch`, `Main.cs.patch`. The patches show the call site is **synchronous on the main thread** — the player's "Saving and quitting…" panel is the visible UI while `OnWorldUnload` runs.
- The order inside `SaveAndQuit` is: `SteamedWraps.StopPlaytimeTracking()` → `SystemLoader.PreSaveAndQuit()` → vanilla save block (writes the world file, flushes player data) → `SystemLoader.OnWorldUnload()`. So `PreSaveAndQuit` is called **before** the save happens, and `OnWorldUnload` is called **after** the save completes (and is still on the same main thread).
- Implication: `PreSaveAndQuit` is the right place to *initiate* off-thread session-end work. By the time the vanilla save has finished writing the world (which takes its own 1–3 s on large worlds), our background work has had the same window to make progress. By the time `OnWorldUnload` fires, the writer thread has either finished or is in its tail; the main thread waits *only* for any unfinished tail, not for the whole aggregation.

### 3.5 ModSystem.PreSaveAndQuit

- Fires from `WorldGen.SaveAndQuit` *before* the vanilla save block. Source: `WorldGen.cs.patch:349`.
- The known-issues row in `context/systems/mod-lifecycle.md` already names this as "not handled today" with low priority. This pass promotes it to **high priority**: it is the only main-thread hook that fires *before* the world-save work begins, giving us a multi-second window to start the aggregation while the vanilla save handles itself.
- **Single-client only** (the XML doc says "Called on the local client only"). Server-side world unload uses a different path (`SystemLoader.OnWorldUnload` only). Implementation must remain idempotent if both fire.

### 3.6 ModSystem.PreUpdateEntities / PostUpdateEverything

- Per-tick, main game thread. `PreUpdateEntities` is called only on full-update frames (skipped frames don't open a tick). `PostUpdateEverything` is the very last `ModSystem` hook each tick — after entity updates, after world updates, after invasions, after time updates. Source: `SystemLoader.cs` plus `Main.cs` patches.
- Out of strict scope for this dossier — they appear here because the writer thread's drain rate depends on per-tick enqueue rate, which feeds the OnWorldUnload drain budget.

### 3.7 Thread model summary

| Lifecycle method | Thread | Blocks game frame? | Blocks splash? | Suitable for heavy work? |
|---|---|---|---|---|
| `Mod.Load` | tML loader thread | No | Yes | DB open: yes. Hook install: yes but defer to PostSetupContent so all content is registered first. |
| `PostSetupContent` | tML loader thread | No | Yes | Heavy install lives here; can be split into yielding passes. |
| `OnWorldLoad` | Main game thread | Yes (loading screen) | No | Light allocation only; heavy work must be deferred. |
| `PreUpdateEntities` / `PostUpdateEverything` | Main game thread | Yes (per-tick) | No | Zero-allocation, microseconds only. |
| `PreSaveAndQuit` | Main game thread | Yes (save panel) | No | Best place to *initiate* off-thread session-end aggregation. |
| `OnWorldUnload` | Main game thread | Yes (save panel) | No | Should be a quick join/wait on already-running off-thread work — not a place to start aggregation. |
| `Mod.Unload` | tML loader thread | No | No (mod-reload spinner) | IL detour disposal + DB final close. |

The whole optimisation thesis is: **move heavy work from "main thread, blocks game frame" rows to "tML loader thread" or "background thread" rows**, exploiting `PreSaveAndQuit` as the trigger for session-end work and `PostSetupContent` yielding for install work.

### 3.8 Lifecycle expectation-vs-reality ledger

Recurring source of bugs is the gap between what the tModLoader docs say a hook means and what its concrete patch-site shows. This ledger consolidates the gaps that bite this pass:

| Hook | Doc says | Patch-site shows | Gap that matters |
|---|---|---|---|
| `OnWorldLoad` | "Called whenever a world is loaded, before LoadWorldData" | Called synchronously on the calling thread (main game thread for SP, server worker on dedicated server) | Implication: SP and DS have **different thread identities** for this hook. Allocation-heavy work blocks the SP loading screen; on a dedicated server it blocks the world bring-up worker. The deferred-construction in §4.4 is SP-safe (we defer to `PostUpdateEverything` which is also called on the world worker on DS); on DS the defer still lands on the same thread, no race. |
| `OnWorldUnload` | "Called whenever a world is unloaded" | Three call sites: `WorldGen.SaveAndQuit` (manual quit), `Netplay.InnerClientLoop` (client kicked), `Main.cs` (cleanup path). Exceptions are caught and logged at `SystemLoader` level. | Implication: never assume `OnWorldUnload` follows a `PreSaveAndQuit` — only the SaveAndQuit chain pairs them. Client-kick fires `OnWorldUnload` without `PreSaveAndQuit`. The §4.7 design covers both via `_sessionEndInitiated` flag. |
| `PreSaveAndQuit` | "Called on the local client only" before save | `WorldGen.SaveAndQuit:349`, no try/catch wrapper at the `SystemLoader.PreSaveAndQuit` level | Implication: SP-only; **not** dedicated-server. Throwing here propagates into `WorldGen.SaveAndQuit` and could prevent the world from saving — mod-side try/catch is mandatory. |
| `Mod.Load` | "When the mod is loaded" | Called sequentially on the load worker; `Main` is not booted; chat is unavailable | Implication: nothing here can post to chat, can read `Main.npc`, can hook things that need post-content registration. The DB-open lives here because it depends on file system only. |
| `Mod.Unload` | "When the mod is unloaded" | Called in reverse load order on the same load worker; the assembly is about to be unloaded; static state in our assembly is about to disappear | Implication: must release every cross-assembly reference (`ILHook` patches into other mods' code) before this returns. Already does. |
| `Mod.Close` | "Called before Unload; may fire multiple times" | Same load worker | Today not implemented; §4.10 adds it idempotently. |
| `PostSetupContent` | "After every mod's content is set up" | Same load worker as `Mod.Load`; called once per launch | Implication: heavy work here blocks the splash but not the game thread (game hasn't booted). Off-thread install (§4.5) is the right answer for the *perceived* launch time. |
| `PreUpdateEntities` | "Before entity updates each tick" | Main game thread for SP, world worker for DS; not called on skipped frames | Implication: do not allocate. Today: zero-alloc. Unchanged by this pass. |
| `PostUpdateEverything` | "Last hook in the update cycle" | Same thread/cadence as PreUpdateEntities | Implication: also where the deferred bring-up of §4.4 lands. Must add a single null-check + bool flag — negligible per-tick cost. |

This ledger is the source-of-truth answer for "what thread, what cadence, what error-propagation" for every lifecycle hook in scope. It is the test-bed for every "would this break under condition X" question this pass surfaces.

### 3.9 Mod.Load ordering vs other mods

`Mod.Load` runs in load order across all mods; this ordering is not in our control. The implication for the pass:

- Our `Database` open in `Mod.Load` does not depend on any other mod. Safe regardless of load order.
- `PostSetupContent`'s hook walk depends on every other mod having finished its own `PostSetupContent` — but tModLoader's `SystemLoader.PostSetupContent` enumerates every ModSystem in load order; our walk fires after each mod's content is set up but **not necessarily after another mod's `PostSetupContent` body has completed**. Worst case: a mod that injects content during its own `PostSetupContent` would be invisible to our walk on first launch. Mitigation: the walk runs after `ModContent.GetContent<T>()` is populated (which happens before any `PostSetupContent` body — it's part of the content-load phase). Verified: `AssemblyManager.GetLoadableTypes(mod.Code)` returns the fully-registered type surface from the mod's assembly, not from any registry that `PostSetupContent` could modify. Safe.

---

## 4 — Optimisation opportunities (ranked, with implementation sketches)

Every opportunity below is *additive efficiency* — same data captured, same JSON shape produced, same hooks installed, but the work happens on a different thread or is split into smaller chunks.

### 4.1 Move end-of-session aggregation to the writer thread

**Pain point**: 3–5 s of `SessionRecorder.End` (BuildModAggregates + BuildHookAggregates) + 0.5–1.5 s of `SessionSummaryLogger.Write` runs synchronously on the main thread inside `OnWorldUnload`. Player sees `UiOverlayBlocking` for 8.5 s with `PerformanceProfiler` as contributor.

**Approach**: `SessionRecorder.End` already does its real outputs as enqueues on the writer thread (`ModAggregateBatch`, `HookAggregateBatch`, `ArchiveAggregate`, `SessionEnd`). The expensive part is the **computation that builds the rows**, not the writes. Move the computation itself onto the writer thread by introducing a new `DbOpKind`: `DbOpKind.BuildAndApplySessionAggregates` that carries a snapshot of the *raw inputs* (PerModCategoryAverageMs arrays, HookAverageMs array, History buffer summary, top-K data) and lets the writer thread call the existing `BuildModAggregates` / `BuildHookAggregates` / `BuildArchive` builders.

**Snapshot inputs needed** (all small, all read-only from the game thread's perspective at session-end):

| Input | Source | Size |
|---|---|---|
| `PerModCategoryAverageMs` | `MetricCollector.PerModCategoryAverageMs` | `modCount × categoryCount` doubles ≈ 60 × 14 = 840 doubles ≈ 6.6 KB |
| `PerModCategoryAverageBytes` | same, optional | same |
| `PerHookAverageMs` | `MetricCollector.PerHookAverageMs` | 10,258 doubles ≈ 82 KB |
| `PerHookAverageBytes` | same, optional | same |
| Frame distribution (avg, max, totalGcMs, ticksObserved, spikeCount, stallCount) | `MetricCollector.History` final summary | already computed in 5 scalars |
| Mod descriptors (names + ids) | `HookInterceptor.ProfiledModNames` (static, shared) | borrowed by reference |
| Hook descriptors (names + ids + modIds + categoryIds) | `PerModAttribution.Hooks` (static, shared) | borrowed by reference |

Total snapshot allocation: ≈ 200 KB worst case (with allocations stream on). One pooled `SessionEndSnapshot` object reused across sessions; the writer thread reads it, runs the builders, applies the rows, and signals completion via a `ManualResetEventSlim` if `OnWorldUnload` needs to wait for it (it should not — `PreSaveAndQuit` initiates and `OnWorldUnload` only waits if the writer fell more than N seconds behind).

**New code shape (sketch)**:

```csharp
// new DbOpKind in DbWriteOp.cs
SessionEndAggregateBuild,

// new struct living on the snapshot (pooled)
public sealed class SessionEndSnapshot {
    public ObjectId SessionId;
    public double[] PerModCategoryAvgMs;          // pre-sized to modCount*categoryCount, zero-filled
    public double[]? PerModCategoryAvgBytes;
    public double[] PerHookAvgMs;
    public double[]? PerHookAvgBytes;
    public double AvgFrameMs, MaxFrameMs, TotalGcMs;
    public long TicksObserved;
    public int SpikeCount, StallCount;
    public string EndReason;
    public long DurationMs;
}

// new stream handler (writer-thread only)
class SessionEndAggregateStream : IPersistenceStream {
    public void Apply(in DbWriteOp op, ProfilerDatabase db) {
        var snap = op.SessionEndSnapshot;
        var modAggs = BuildModAggregates(snap);      // moved from SessionRecorder
        var hookAggs = BuildHookAggregates(snap);    // moved from SessionRecorder
        var archive = BuildArchive(snap, modAggs);   // moved from SessionRecorder
        // direct collection writes — same code path, just runs on writer thread
        db.PerSessionMods.InsertBulk(modAggs);
        db.PerSessionHooks.InsertBulk(hookAggs);
        db.TickAggregatesArchive.Insert(archive);
        // session end
        var s = db.Sessions.FindById(snap.SessionId);
        if (s != null) {
            s.EndedUtc = DateTime.UtcNow;
            s.EndReason = snap.EndReason;
            s.DurationMs = snap.DurationMs;
            s.TicksObserved = snap.TicksObserved;
            s.Incomplete = false;
            db.Sessions.Update(s);
        }
        SessionEndSnapshotPool.Return(snap);
    }
}
```

**Call-site changes**:

```csharp
// Replace SessionRecorder.End body with:
public void End(MetricCollector collector, string endReason = "clean") {
    DrainSpikes(collector);
    DrainStalls(collector);
    FlushCluster();
    var snap = SessionEndSnapshotPool.Rent();
    SnapshotInputs(collector, endReason, snap);  // O(modCount + hookCount) — fast
    _db.Writer.Enqueue(DbWriteOp.SessionEndAggregate(snap));
}
```

**Estimated saving**: 3–5 s of main-thread work removed; replaced by ~50 ms of snapshot-copy on the main thread (one `Array.Copy` per input). Writer thread runs the heavy builders during the vanilla save (~1–3 s), so by `OnWorldUnload` it is typically done.

**Invariant check**:
- Invariant 1 (read-only): we are not mutating game state — moving when *our* state is written changes nothing. ✓
- Invariant 2 (zero-alloc hot path): snapshot copy is O(modCount + hookCount) once per session, not per tick. ✓
- Invariant 3 (descriptive): no insight strings change. ✓
- Invariant 4 (abort-clean): if the writer thread is overloaded and `OnWorldUnload` is reached with the SessionEndAggregate op still pending, the main thread waits via `_writer.WaitForOp(snap, timeoutMs: 5000)`; on timeout we log and proceed (the writer will eventually finish and the next-session crash-detected path covers the gap). ✓
- Invariant 5 (no mod-specific code): unchanged. ✓

### 4.2 Move SessionSummaryLogger.Write to the writer thread (and the post-aggregate path)

**Pain point**: 0.5–1.5 s of synchronous LiteDB queries on the main thread for the session-summary log block.

**Approach**: chain it after the `SessionEndAggregateStream.Apply` call. The summary already needs the aggregates to be written; running it as the *final step inside the same writer-thread Apply* removes the main-thread cost entirely. No new DbOpKind needed — the summary is a side-effect of session-end completion.

**Snippet** (continuation of `SessionEndAggregateStream.Apply`):

```csharp
// After writing the aggregates + session-end:
if (PerformanceProfiler.LoggerOrNull != null) {
    SessionSummaryLogger.Write(PerformanceProfiler.LoggerOrNull, db, snap.SessionId);
}
```

**Saving**: full 0.5–1.5 s removed from main thread. Logger.Info is thread-safe (log4net guarantees this).

### 4.3 Replace busy-wait drain with a deterministic completion signal

**Pain point**: `DrainAndTruncateJournalForSessionEnd` polls `_writer.ApproxQueueDepth > 0` with `Thread.Sleep(20)` × 100 (2 s soft cap). When the queue is deep, the main thread can sleep for the entire 2 s; when it is shallow, we still pay 20 ms minimum waiting for the next sleep tick.

**Approach**: replace polling with a tagged-op completion signal. Each session-end-related op carries an optional `ManualResetEventSlim? Completion` field. The writer thread signals it after `_db.ApplyBatch` returns. `OnWorldUnload` waits *once* on the SessionEnd completion event with a 5 s timeout — that wait covers all queued work prior to it, because the channel is FIFO.

```csharp
// inside SessionRecorder.End:
var completion = new ManualResetEventSlim(false);
_db.Writer.Enqueue(DbWriteOp.SessionEndAggregate(snap, completion));

// inside OnWorldUnload, AFTER _recorder.End(...):
if (completion != null && !completion.Wait(TimeSpan.FromSeconds(5))) {
    Mod.Logger.Warn("Session-end aggregation did not finish within 5 s; writer thread will complete asynchronously.");
}
_db.Checkpoint();         // optional: keep an explicit checkpoint
_journal.TruncateOnCleanShutdown();
```

The completion event removes the busy-wait, gives us a clean knob for timeouts, and lets us proceed *immediately* the moment the writer finishes — no 20-ms polling jitter.

### 4.4 Defer SessionRecorder construction and watcher allocations off the world-enter critical path

**Pain point**: the 172-ms first-tick freeze is partly caused by `OnWorldLoad`'s six allocations + the modlist-upserts loop happening at the same moment the splash is fading out.

**Approach**: `OnWorldLoad` keeps the minimum strictly required for the first tick (the `MetricCollector` — needed by `PreUpdateEntities`). Everything else defers to the **first `PostUpdateEverything`** call:

```csharp
public override void OnWorldLoad() {
    Collector = new MetricCollector(HistoryCapacity, SelfHealth);
    _firstTickDone = false;  // ✱ flag
}

public override void PostUpdateEverything() {
    // ... existing body ...
    if (!_firstTickDone) {
        _firstTickDone = true;
        DeferredWorldEnterBringUp();   // creates SessionRecorder + watchers + ContextTagger + EventAggregator, enqueues modlist upserts
    }
}
```

**Why this works**: the first-tick freeze is composed of (a) `OnWorldLoad` allocation pass (≈ 30–60 ms), (b) first-call JIT of the per-tick pipeline (≈ 80–100 ms), (c) MonoMod trampoline compilation on first call into each detoured method (≈ 10–30 ms cumulatively, the costly trampolines are amortised later but the first ones land in tick 1). Removing (a) — moving it to *after* the first tick's `EndTick` returns — lets the player see a frame paint *before* we pay the construction cost. The first tick is still slow (it pays (b) and (c)), but moves from 172 ms to ≈ 110–130 ms; the second tick is no longer affected because `_firstTickDone` is true and the watcher allocations have already landed.

**Edge case**: what if a per-tick hook fires between `OnWorldLoad` and the first `PostUpdateEverything` and tries to write to `_recorder`? Answer: every `_recorder` access already null-checks (see `InteractionPlayer`, `InteractionNpc`, `InteractionItem`). Null is safe. The cost is one extra null check per hook call for ≤ 1 tick — negligible.

**Alternative considered, rejected**: a `Task.Run`-based bring-up on a thread pool thread. Rejected because `EnqueueModlistUpserts` and `new SessionRecorder` enqueue ops that include `SessionId` and `Fingerprint`; if those ops race with the first `OnTick`, ordering is unclear. Deferring to the first `PostUpdateEverything` on the same (main) thread keeps the order strict.

### 4.5 Yield the hook install across multiple frames

**Pain point**: 10–18 s of `PostSetupContent` install on the loader thread. Sub-pain: when the player triggers `Mods → Reload` mid-play, the same 10–18 s recurs and the reload spinner sits while every detour is rebuilt.

**Approach**: split the install into a coroutine driven by `PostSetupContent` *initiating* the pass and the actual installation work running on a dedicated *install thread* that yields explicitly every N hooks. While the install thread runs, the loader thread can return — the splash screen still says "Setting up content for Performance Profiler" but at least the per-mod progress reflects reality.

```csharp
public override void PostSetupContent() {
    SelfHealth.MarkInstallStart();
    HookInterceptor.Install(Mod);                        // delegate-pair path stays inline (Cecil-free, ~2 s)
    if (HookBackend.ILHookActive) {
        ILHookInterceptor.BeginInstallAsync(Mod, HookInterceptor.ProfiledMods);
        // ↑ returns immediately after spawning the install worker
    }
    BiomeRegistry.Populate();          // independent of ILHook install
    SubworldProbe.Initialise();        // independent of ILHook install
    SelfHealth.MarkInstallEnd(PerModAttribution.HookCount);
}
```

**Install worker** (sketch in `ILHookInterceptor.BeginInstallAsync`):

```csharp
private static Thread? _installWorker;
private static volatile bool _installInProgress;
public static bool InstallInProgress => _installInProgress;

public static void BeginInstallAsync(Mod self, IReadOnlyList<Mod> profiledMods) {
    _installInProgress = true;
    _installWorker = new Thread(() => {
        try { Install(self, profiledMods); }
        finally { _installInProgress = false; }
    }) { Name = "ProfilerILHookInstall", IsBackground = true };
    _installWorker.Start();
}
```

**Critical guard**: the per-tick `PreUpdateEntities` and `PostUpdateEverything` must tolerate a partially-installed detour surface. They already do — the attribution slots are pre-allocated by `HookInterceptor.Install` (which runs synchronously), and `ProbeStack.Enter` is hook-id-keyed; an un-instrumented method simply doesn't call into the probe. So the worst case during install is: the first few seconds of the world have a slowly-growing fraction of mods that are timed. The data stack continues capturing what is instrumented; coverage rises monotonically. This *is* a data-loss in the "first 10 s of session" window, but acceptable as long as the SELF tab badges it ("hook install: 73 % complete, coverage rising").

**Abort-clean discipline (Invariant 4)**: if the install worker throws, `_installInProgress` falls to `false`, the `Installed` flag stays `false`, `Uninstall()` runs in the worker's `finally` to dispose any partial detours. The worker writes one warn log line. The next `PreUpdateEntities` continues unaffected — the data stack just doesn't have ILHook coverage for the session.

**Estimated saving**: the player goes from "10–18 s frozen on splash" to "0 s on splash; first 10–18 s of world has partial-coverage badge". The total CPU is unchanged; the wall-clock impact on the player's perceived launch time is what improves.

**Alternative considered, rejected**: yielding inside `PostSetupContent` itself (running 100 hooks → `Thread.Sleep(0)` → 100 more). Rejected because the loader thread *is* the splash thread; sleeping it just pauses the splash. Off-thread is the only real win.

**Cross-version safety**: this depends on MonoMod's `ILHook` constructor being thread-safe. MonoMod's docs explicitly say it is (`RuntimeDetour` is thread-safe at the install level; the only contention is the per-method-handle CAS inside DetourManager). Sources: MonoMod.RuntimeDetour 25.3.2 changelog plus the `DetourManager.cs` internal that uses `lock (_lock)` for the per-method state.

### 4.6 Cache and dedupe across HookInterceptor + ILHookInterceptor type walks

**Pain point**: `HookInterceptor.Install` and `ILHookInterceptor.Install` each walk *the same mod surface*, each call `AssemblyManager.GetLoadableTypes(mod.Code)` (which allocates an array of every type each call), each call `type.GetMethods(...)` (which allocates a `MethodInfo[]` each call). For 60 mods × ~100 types/mod × 2 walks, that is ~12,000 `GetMethods` calls, each allocating.

**Approach**: introduce a `HookSurfaceCache` populated once per launch:

```csharp
internal static class HookSurfaceCache {
    public record struct ModSurface(int ModId, Type[] Types, MethodInfo[][] MethodsByType);
    private static ModSurface[]? _surfaces;
    public static ModSurface[] BuildOrGet(IReadOnlyList<Mod> mods) {
        if (_surfaces != null) return _surfaces;
        var arr = new ModSurface[mods.Count];
        for (int i = 0; i < mods.Count; i++) {
            Type[] ts = AssemblyManager.GetLoadableTypes(mods[i].Code);
            var methods = new MethodInfo[ts.Length][];
            for (int t = 0; t < ts.Length; t++) {
                methods[t] = ts[t].GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            arr[i] = new ModSurface(i, ts, methods);
        }
        return _surfaces = arr;
    }
    public static void Clear() => _surfaces = null;
}
```

Both interceptors consume `HookSurfaceCache.BuildOrGet(profiledMods)` instead of repeating reflection. Cleared in `Mod.Unload` to release references for assembly unload.

**Estimated saving**: ≈ 80–150 MB of one-shot allocations removed (every `GetMethods` allocates a fresh array even though the underlying metadata is cached). Wall-time saving: 0.5–1 s of the install window, primarily allocation + GC overhead.

### 4.7 PreSaveAndQuit hook: start the session-end work early

**Pain point**: the vanilla `WorldGen.SaveAndQuit` block does its own 1–3 s of disk work between `PreSaveAndQuit` and `OnWorldUnload`. Today we don't use that window.

**Approach**: implement `PreSaveAndQuit` to *initiate* `_recorder.End(Collector, endReason: "clean")` *as soon as the user clicks Save & Quit*. The off-thread aggregation (§4.1) runs during the vanilla save. `OnWorldUnload` then becomes a thin "wait for completion + null out state" path.

```csharp
public override void PreSaveAndQuit() {
    if (Collector != null && _recorder != null) {
        Collector.FlushSpikes();
        _recorder.End(Collector, endReason: "clean");
        // ↑ this enqueues the SessionEndAggregate op (per §4.1);
        // the writer thread starts building aggregates immediately
    }
    _sessionEndInitiated = true;
}

public override void OnWorldUnload() {
    if (!_sessionEndInitiated && Collector != null && _recorder != null) {
        // Path-not-via-SaveAndQuit (kick, crash, server-side): do the same here
        Collector.FlushSpikes();
        _recorder.End(Collector, endReason: "dirty");
    }
    _sessionEndInitiated = false;

    // Wait briefly for the SessionEnd op to complete
    _recorder?.WaitForSessionEnd(TimeSpan.FromSeconds(5));

    PerformanceProfiler.Database?.DrainAndTruncateJournalForSessionEnd();

    // Teardown
    _recorder = null; _transitionWatcher = null; _snapshotter = null; _deathDetector = null;
    Collector = null; _contextTagger = null; Events = null;
    InsightsEngine.Shared = null;
    BossSampler.Clear();
    SubworldProbe.Clear();
    Mod.Logger.Info("Profiler disarmed: world unloaded.");
}
```

**Idempotency**: `_sessionEndInitiated` flag prevents double-`End` if both `PreSaveAndQuit` and `OnWorldUnload` fire on the same exit (the normal happy path). The dirty path (kick / crash) fires only `OnWorldUnload`; we detect that via `_sessionEndInitiated == false` and badge the session `"dirty"`.

**Server-side note**: `PreSaveAndQuit` is local-client-only per tModLoader docs. On the server-only path the only firing is `OnWorldUnload`; the same `!_sessionEndInitiated` branch covers it. ✓

**Net saving**: in the happy-path single-player case, the entire session-end aggregation now overlaps with the vanilla save. Main-thread cost in `OnWorldUnload` drops from 8.5 s to typically < 100 ms (snapshot copy + completion-event wait that immediately returns).

### 4.7.1 PreSaveAndQuit handler — state-machine before/after

Before (today):

```
Player clicks Save & Quit
      │
      ▼
WorldGen.SaveAndQuit
   ├─ SystemLoader.PreSaveAndQuit       (no-op for us)
   ├─ vanilla save block                (~1-3 s, world+player files)
   └─ SystemLoader.OnWorldUnload
         └─ ProfilerSystem.OnWorldUnload
                 ├─ FlushSpikes                    [~1 ms]
                 ├─ SessionRecorder.End            [3-5 s, MAIN THREAD]
                 ├─ DrainAndTruncateJournal        [busy-wait Sleep(20)x100, 1.5-3 s]
                 ├─ SessionSummaryLogger.Write     [6 LiteDB queries, 0.5-1.5 s]
                 └─ teardown                       [~0.5 ms]

     Player sees 8.5 s of UI stall, contributor: PerformanceProfiler
```

After (§4.7 + §4.1 + §4.2 + §4.3):

```
Player clicks Save & Quit
      │
      ▼
WorldGen.SaveAndQuit
   ├─ SystemLoader.PreSaveAndQuit
   │     └─ ProfilerSystem.PreSaveAndQuit       (NEW)
   │           ├─ FlushSpikes                              [~1 ms]
   │           ├─ try {
   │           │     SnapshotInputs(collector, "clean", snap)
   │           │     completion = new ManualResetEventSlim(false)
   │           │     Writer.Enqueue(SessionEndAggregate(snap, completion))
   │           │   } catch (...) { Logger.Warn; }          [~50 ms]
   │           └─ _sessionEndInitiated = true
   │                                            ── writer thread runs aggregation
   │                                               and summary log block IN PARALLEL
   │                                               with the vanilla save
   ├─ vanilla save block                         [~1-3 s, world+player files]
   │
   ├─ ── meanwhile, on writer thread ──
   │       SessionEndAggregateStream.Apply
   │           BuildModAggregates(snap)         [~2-3 s]
   │           BuildHookAggregates(snap)        [~1-2 s]
   │           BuildArchive(snap, modAggs)      [~50 ms]
   │           Insert / Update aggregate rows   [~200 ms]
   │           SessionSummaryLogger.Write       [~0.5-1.5 s]
   │           completion.Set()
   │
   └─ SystemLoader.OnWorldUnload
         └─ ProfilerSystem.OnWorldUnload
                 ├─ if (!_sessionEndInitiated) { ... do dirty-end ... }
                 ├─ completion.Wait(5 s timeout)            [typically <50 ms, often 0]
                 ├─ Database.Checkpoint                     [~100 ms]
                 ├─ Journal.TruncateOnCleanShutdown         [~5 ms]
                 └─ teardown + null statics                 [~0.5 ms]

     Player sees ~200 ms of UI stall, attributable mostly to vanilla save
```

Net: the 8.5-s `UiOverlayBlocking` contribution from `PerformanceProfiler` drops to roughly the Database.Checkpoint cost (~100 ms) — below the spike detector's threshold for the same session.

### 4.7.2 Writer-thread queue dynamics at session-end

The optimisation works only if the writer thread can complete the session-end aggregate during the vanilla save window. Capacity check:

| Metric | Value | Implication |
|---|---|---|
| v0.5 writer-thread sustained drain | 314 ops/sec (baseline.md row 1) | one `SessionEndAggregate` op is **one** op — the per-op cost is irrelevant; what matters is wall-clock of `Apply`. |
| `BuildModAggregates` wall (estimated from baseline) | ~3 s | runs on writer thread; concurrent with vanilla save. |
| `BuildHookAggregates` wall (estimated) | ~1-2 s | concurrent. |
| `SessionSummaryLogger.Write` wall (six `Find` queries) | ~0.5-1.5 s | concurrent. |
| Total writer-thread work at session-end | ~4-7 s | fits inside the vanilla save window for typical worlds; for tiny worlds (vanilla save < 1 s) the completion-event wait absorbs the tail. |

For very small worlds (vanilla save < 500 ms), `OnWorldUnload`'s `completion.Wait(5 s)` may wait the full remaining 3-6 s. **This is still a net improvement** because:

1. The wait is on the *writer-thread doing useful work*, not the main thread doing useful work. The main thread is purely blocked on the event; it is not consuming CPU.
2. The progress bar / UI is still painted between `Wait` polls (the spin-wait inside `ManualResetEventSlim` yields to message-pump-friendly intervals). The user sees the "Saving and quitting…" panel, not a frozen UI.
3. Worst case 5 s wait → 5 s stall in the small-world case. Still better than the 8.5-s stall baseline; for the large-world case, dramatically better.

If the small-world case proves problematic in practice (measured post-pass), the fallback is to allow `OnWorldUnload` to proceed without waiting (the writer thread continues; the session ends async; the next session start sees the still-incomplete prior session and badges it — same recovery path as crash-detected sessions).

### 4.8 Eliminate the forced GC pair in PostSetupContent

**Pain point**: `SelfHealth.MarkInstallStart()` and `MarkInstallEnd()` each force a `GC.Collect(2, blocking)`. Two full Gen2 collections at install time. ~50–150 ms each.

**Approach**: leave the install-delta measurement intact but **replace blocking Gen2 with a `GC.TryStartNoGCRegion` + `Mono.Cecil.Cil`-friendly noGC region** for the install pass. The bytes-per-hook metric becomes "bytes allocated during install" (cheap to measure via `GC.GetAllocatedBytesForCurrentThread` deltas), not "heap size delta after full collection".

```csharp
// Before:
GC.Collect(2, GCCollectionMode.Forced, blocking: true);
GC.WaitForPendingFinalizers();
long preInstall = GC.GetTotalMemory(forceFullCollection: true);

// After:
long preInstallAllocs = GC.GetAllocatedBytesForCurrentThread();
// ... install ...
long postInstallAllocs = GC.GetAllocatedBytesForCurrentThread();
long allocDelta = postInstallAllocs - preInstallAllocs;
```

**Tradeoff**: we lose the "live heap delta" measurement (after Gen2 settles). We gain ~100–300 ms of install time and remove the GC-pressure spike that affects subsequent allocations in the install pass itself. The bytes-per-hook number changes meaning (now it's "bytes allocated per hook during install" instead of "live heap added per hook"), which is **more informative** — it isolates our cost from coincidental Gen2 churn.

**Falls under**: §3 of philosophy.md — same observable output (a bytes-per-hook number), produced cheaper, with no capture surface lost.

### 4.9 Lifecycle alloc removal — small wins

| Site | Current alloc | Mitigation |
|---|---|---|
| `EnqueueModlistUpserts` builds one `ModRow` + one `ModVersionEntry` `List<>` per mod | ~120 small heap allocations per world-load | Pool `ModRow` + `List<ModVersionEntry>` per session. Saves ~60 KB and reduces Gen0 pressure during the world-enter freeze window. |
| `BuildModAggregates` allocates `List<double>(categoryCount)` per mod | ~60 lists × 14 doubles | After §4.1 this runs on the writer thread, but still allocates. Replace with a single `double[]` buffer reused across the per-mod loop. |
| `BuildTopHooks` allocates `List<TopHookEntry>` then sorts then trims to 5 | linear in hook count | Use a fixed-size 5-element insertion sort over a stack-alloc `Span<TopHookEntry>` (5 slots). Removes the list + the LINQ-style sort. |
| `ConvertSpikeContributors` allocates `List<SpikeContributor>` per spike | one per spike | Reuse pooled list. |

Each is small; cumulative effect is a measurable Gen0 pressure reduction at session-end and ~50 ms shaved off the writer-thread aggregate build.

### 4.10 Defensive `Mod.Close` implementation

**Pain point**: `Mod.Close` is documented in `mod-lifecycle.md` as not implemented because today everything flushes in `OnWorldUnload`. If tModLoader reorders or fires `Mod.Close` without `OnWorldUnload` (a pathological `Mods → Reload` while a world is loaded), the session JSON is missing its final entry.

**Approach**: implement `Mod.Close` as a *belt-and-braces* wrapper that detects "OnWorldUnload was not called for the current world" and triggers a dirty-close session-end:

```csharp
public override void Close() {
    base.Close();
    // If a world is still loaded (rare: reload mid-play), force a dirty end.
    var sys = ModContent.GetInstance<ProfilerSystem>();
    if (sys?.HasLiveSession == true) {
        sys.ForceDirtyEnd("mod-close");
    }
}
```

`ForceDirtyEnd` does the same `End(Collector, endReason: "dirty")` + drain path as the `OnWorldUnload` no-PreSaveAndQuit branch. Idempotent with `OnWorldUnload`. No new behaviour in the happy path; the latent leak in the pathological path closes.

**Cost**: zero unless the rare path fires.

### 4.11 Heap-leak detection across Mods → Reload cycles

**Pain point**: no existing test ensures `Mod.Load` allocations are paired with `Mod.Unload` releases. The risk is sub-MB-per-cycle leaks that compound across a session of iteration.

**Approach**: a single diagnostic test in `Tests/PerformanceProfiler.Tests.csproj`:

```csharp
[Fact]
public void ModLifecycle_ReloadCycle_LeavesNoSignificantLeak() {
    // Simulate 5 cycles of (Load → world-load → world-unload → Unload)
    long[] heapAfterEachCycle = new long[5];
    for (int i = 0; i < 5; i++) {
        var mod = new TestMod();
        mod.Load();
        var sys = new ProfilerSystem();
        sys.PostSetupContent();
        sys.OnWorldLoad();
        sys.OnWorldUnload();
        mod.Unload();
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        heapAfterEachCycle[i] = GC.GetTotalMemory(true);
    }
    // Steady-state cycle 5 within ±10 % of cycle 2 (cycle 1 carries warmup)
    Assert.InRange(heapAfterEachCycle[4],
        (long)(heapAfterEachCycle[1] * 0.9),
        (long)(heapAfterEachCycle[1] * 1.1));
}
```

Lives in `Tests/Lifecycle/ReloadCycleHeapTests.cs` (new file). Catches future-regressions; non-load-bearing for this pass's metric improvements. Categorised as a Cov-type addition under the additive ratchet.

---

## 5 — Cross-system dependencies

Lifecycle is the orchestrator; every other system has an entry-point owned here. The pass's changes touch the following downstream systems, each of which has its own dossier:

| System | Coupling point | What this dossier requires |
|---|---|---|
| `metric-collection` (`MetricCollector`) | `OnWorldLoad` constructs it; `PostUpdateEverything` ticks it; `OnWorldUnload` snapshots its state for the aggregate build | The snapshot inputs in §4.1 are exposed as a single `MetricCollector.SnapshotForSessionEnd(SessionEndSnapshot dst)` method that does field copies. Must be O(modCount + hookCount), zero-alloc-besides-the-pooled-snapshot. |
| `persistence` (`ProfilerDatabase`, `DbWriterThread`, `SessionRecorder`, `SessionSummaryLogger`) | The new `DbOpKind.SessionEndAggregate` and the new `SessionEndAggregateStream`. The completion-event field on `DbWriteOp`. | Owned jointly. The persistence dossier owns the stream registration, the journal-replay schema, and the rollback shape if a session-end op crashes mid-apply. |
| `hook-instrumentation` (`HookInterceptor`, `ILHookInterceptor`) | The off-thread install of §4.5 + the surface cache of §4.6. `Uninstall` ordering unchanged. | The hook dossier owns the detail of how the IL-emission worker yields, the abort-clean state machine, and the SELF tab badge for "install N % complete". |
| `events-and-context` (`ContextTagger`, `EventAggregator`, `WorldSnapshotter`, `PlayerDeathDetector`, watchers) | All constructed in `OnWorldLoad`; all nulled in `OnWorldUnload`. The deferred bring-up of §4.4 moves their construction to the first `PostUpdateEverything`. | Each must tolerate being null for one tick. Audit confirms today's null-checks already cover this (verified in `PostUpdateEverything` body). |
| `insights-engine` (`InsightsEngine.Shared`) | Cleared at `OnWorldUnload`. | No change required. |
| `self-health` (`ProfilerSelfHealth`) | The install-delta block in `PostSetupContent`. | §4.8 replaces the forced Gen2 with an allocation-delta measurement. The SELF tab schema needs to widen the badge text from "live-delta MB/hook" to "alloc-delta MB/hook". One-line label change in the renderer. |
| `ui-overlay` (the OverlaySystem) | Not directly coupled to lifecycle; reads `Collector` reactively. | One add: a "install in progress" badge on the SELF tab while §4.5's off-thread install is running. |

The blast radius is contained — every change is additive within an existing seam.

---

## 6 — Prioritised execution order

| Phase | Change | Risk | Reward | Dep |
|---|---|---|---|---|
| **6.1** | §4.3 completion-event signal on `DbWriteOp` | Low — pure additive on the op struct + one new wait point. | Removes the 20-ms polling jitter; unlocks §4.1 + §4.7. | none |
| **6.2** | §4.1 move aggregate build to writer thread (new `DbOpKind.SessionEndAggregate` + stream) | Medium — new code path, must round-trip the existing JSON / DB tests unchanged. | 3–5 s of main-thread time removed. | 6.1 |
| **6.3** | §4.2 move `SessionSummaryLogger.Write` to writer-thread tail of `SessionEndAggregateStream.Apply` | Low — pure relocation. | 0.5–1.5 s of main-thread time removed. | 6.2 |
| **6.4** | §4.7 implement `PreSaveAndQuit` to initiate session-end early; `OnWorldUnload` waits via completion event | Medium — touches the SaveAndQuit timing window. Must handle the dirty-close path. | Overlaps off-thread work with vanilla save. By this phase the headline 8.5-s stall is gone. | 6.2, 6.3 |
| **6.5** | §4.4 defer `SessionRecorder` + watcher allocations to first `PostUpdateEverything` | Low — flag + one-tick defer. | 30–60 ms off the 172-ms first-tick freeze. | none |
| **6.6** | §4.8 replace forced Gen2 with alloc-delta measurement | Low — instrumentation-internal change. | 100–300 ms off the install window. | none |
| **6.7** | §4.6 `HookSurfaceCache` shared between delegate + ILHook walkers | Low — pure dedupe. | 0.5–1 s + 80–150 MB off install. | none |
| **6.8** | §4.5 off-thread ILHook install with `InstallInProgress` badge | High — concurrency edge cases (per-tick hook calls during partial install). Must ship behind a config flag for the first version with logging of every install transition. | 5–8 s of perceived launch-time removed. | 6.7 |
| **6.9** | §4.9 pooled aggregate buffers + alloc-removal in `BuildModAggregates`/`BuildTopHooks` | Low — local refactor. | ≈ 50 ms off the writer-thread session-end build. | 6.2 |
| **6.10** | §4.10 belt-and-braces `Mod.Close` | Low — defensive, no-op in happy path. | Closes the latent leak under pathological reload. | 6.4 |
| **6.11** | §4.11 reload-cycle heap-leak diagnostic test | Low — test-only. | Future-regression detector. | none |

Phases 6.1–6.4 land the headline 8.5-s stall fix; the rest are incremental ratchets.

Each phase ends with the obligation audit from CLAUDE.md: enumerate every invariant, cite the test or measurement that confirms it. Phases 6.2 and 6.4 in particular must run a synthetic round-trip test that compares the post-pass JSON output against the v0.5 baseline for the same captured session — every aggregate row should be byte-identical (modulo any new fields added intentionally).

### 6.12 Verification recipes per phase

| Phase | Recipe |
|---|---|
| 6.1 | Add a `[Fact]` that enqueues a `DbWriteOp` with a non-null `Completion`, drains the writer, asserts the event is signalled within 100 ms. Regression-guards the wait API. |
| 6.2 | Capture a 60-second test session against the fixture writer; record the pre-pass JSON of `PerSessionMods` / `PerSessionHooks` / `TickAggregatesArchive`; re-run with §4.1 stream wired; assert byte-identical JSON for the three collections. Equivalence test. |
| 6.3 | Same fixture; assert the `client.log` "=== profiler session-summary ===" block is identical (apart from the timestamp). |
| 6.4 | Time the end-to-end SaveAndQuit chain with the fixture: assert the elapsed time between `PreSaveAndQuit` entry and the next `Main.NewText`-able tick is < 1.0 s where v0.5 baseline measured 8.5 s. |
| 6.5 | Instrument the first 5 `PostUpdateEverything` calls after `OnWorldLoad`; assert first-tick time < 100 ms (down from 172 ms target band). |
| 6.6 | Capture install-delta with old measurement and new (alloc-delta); confirm the number is in the same order of magnitude. Document the meaning change. |
| 6.7 | Time `HookInterceptor.Install` + `ILHookInterceptor.Install` before/after the surface cache; assert combined wall < 80 % of pre-pass figure. |
| 6.8 | Launch with `ILHookActive` and observe the SELF tab. Confirm install progress badge ticks; confirm no `InvalidProgramException` during the first 30 s of play (the partial-install window). |
| 6.9 | Run `dotnet-counters` during the writer-thread session-end build; assert Gen0 allocations during the build dropped by ≥ 30 %. |
| 6.10 | Force `Mod.Close` without `OnWorldUnload` (simulated reload mid-world) and assert the session ends with `EndReason = "mod-close"` in the DB. |
| 6.11 | Run the reload-cycle test (`Tests/Lifecycle/ReloadCycleHeapTests.cs`); assert pass. |

The "byte-identical JSON" requirement (6.2, 6.3) is the discipline that catches any silent behaviour change introduced by relocating work between threads.

### 6.13 What is explicitly out of scope

- The per-tick attribution math (`PerModAttribution`, `PerTickAttributionRing`) — owned by `metric-collection.md`.
- The IL-emission shape (`ProbeStack.Enter` / `Leave` codegen, the manipulator) — owned by `hook-instrumentation.md`. This dossier covers only *when* the install runs.
- The downsampling cadence, the warm-tier sweep, the journal format — owned by `persistence.md`.
- The overlay's draw cost, ModifyInterfaceLayers mount cost — owned by `ui-overlay.md`.
- The Insights detectors and the scoring engine — owned by `insights-engine.md`.

This dossier touches the orchestrator only. Every change ships as a thin re-wiring of lifecycle, never as a re-write of a downstream component.

---

## 7 — References

Source code (this repository):

- `PerformanceProfiler.cs:39-92` — `Mod.Load` / `Mod.Unload`
- `Profiling/ProfilerSystem.cs:85-239` — `PostSetupContent`, `OnWorldLoad`, `OnWorldUnload`
- `Profiling/Persistence/ProfilerDatabase.cs:83-247` — DB open / dispose / drain / checkpoint
- `Profiling/Persistence/DbWriterThread.cs:58-225` — writer-thread channel + drain
- `Profiling/Persistence/SessionRecorder.cs:199-222` — the `End` body that holds the main-thread aggregation
- `Profiling/Persistence/SessionSummaryLogger.cs:25-87` — the post-aggregate log block
- `Profiling/ILHookInterceptor.cs:137-215` — install + uninstall ownership
- `Profiling/HookInterceptor.cs:1-120` — delegate-pair install + the shared `ProfiledMods` surface
- `Profiling/ProfilerSelfHealth.cs` — forced-GC install measurement
- `context/perf-pass/baseline.md` — v0.5 measurements
- `context/notes/philosophy.md` — "optimisation = doing the same work cheaper" rule
- `context/systems/mod-lifecycle.md` — current-state architecture doc

tModLoader source (verified via gh api against `tModLoader/tModLoader`):

- `patches/tModLoader/Terraria/ModLoader/ModSystem.cs` — `OnWorldLoad` (line 94), `OnWorldUnload` (line 107), `ClearWorld` (line 113), `PreUpdateEntities` (line 140), `PostUpdateEverything` (line 254), `PreSaveAndQuit` (line 303). Each is a `public virtual void` with no return value and no parameters.
- `patches/tModLoader/Terraria/ModLoader/SystemLoader.cs:156-190` — `OnWorldLoad` and `OnWorldUnload` enumerate `HookOnWorldLoad` / `HookOnWorldUnload` synchronously; exceptions in `OnWorldLoad` rethrow as `CustomModDataException`, exceptions in `OnWorldUnload` are caught and logged via `Logging.tML.Error`.
- `patches/tModLoader/Terraria/ModLoader/SystemLoader.cs:457-463` — `PreSaveAndQuit` enumerates `HookPreSaveAndQuit` synchronously; **no** exception-catching wrapper (a thrown exception in our `PreSaveAndQuit` will propagate up `WorldGen.SaveAndQuit`). Implementation must therefore wrap its body in try/catch.
- `patches/tModLoader/Terraria/WorldGen.cs.patch:331-355` — call site for `OnWorldUnload` (from a manual quit or disconnect) and `SaveAndQuit` (the `PreSaveAndQuit` → vanilla-save → `OnWorldUnload` sequence). Confirms both run on the calling thread (main game thread inside the SaveAndQuit panel).
- `patches/tModLoader/Terraria/ModLoader/Mod.cs` — `Mod.Load` and `Mod.Unload` are virtual, called per-mod by `AssemblyManager` during the load thread. `Mod.Close` is called immediately before `Mod.Unload` and **may fire multiple times** per the XML doc.
- MonoMod RuntimeDetour 25.3.2 — `ILHook` is thread-safe at construction and disposal level; the per-method state is guarded by an internal `lock` (verified against `MonoMod.RuntimeDetour/DetourManager.cs`).

External:

- LiteDB issue #2152 — `Rebuild()` must follow `Checkpoint()` to avoid stale pages. Drives the order in `ProfilerDatabase.Compact()`; not directly relevant to lifecycle but cited because the writer-thread changes increase `Rebuild` candidate moments.
- LiteDB issue #1568 — log-file growth mitigation drives the 60-second checkpoint cadence in `DbWriterThread.MaybeCheckpoint`; relevant because §4.1's writer-thread aggregate build adds to the cadence budget. No new mitigation needed; current cadence absorbs.
- LiteDB issue #2401 — pre-warm sentinel mitigation drives `PreWarmCollections`. Confirms that the initial DB-bring-up cost in `Mod.Load` is bounded.
- tModLoader wiki "Vanilla Interface Layers values" / `Mod.Close` documentation — fires before `Unload` and may be invoked multiple times. Drives §4.10's idempotency requirement.

Invariants exercised:

- Invariant 1 (read-only instrumentation): every change in §4 either moves *our* writes between threads or splits *our* compute across frames. No mutation of game state in any phase.
- Invariant 2 (overhead budget): every phase either reduces main-thread time (helping the 1 % / 2-4 % / 5-10 % budgets) or leaves it unchanged. None increase per-tick work.
- Invariant 3 (descriptive, never normative): no UI copy added or changed. The SELF tab badge in §4.5 reads "install N % complete" — descriptive measurement; no "core" / "removable" / "should drop" language.
- Invariant 4 (abort-clean): each phase includes a failure path that either disables instrumentation for the rest of the session or completes the partial work with a warning. Specifically: §4.1 falls back to main-thread aggregation if the writer thread is uncontactable; §4.5 disposes partial detours and stays disabled; §4.7's `PreSaveAndQuit` wraps its body in try/catch because tModLoader doesn't catch for us.
- Invariant 5 (no mod-specific code): unchanged — the lifecycle pass touches no detector or classifier.

---

*Dossier ends. The corresponding master-plan entry batches phases 6.1–6.4 as the v0.6 stall-elimination commit, and phases 6.5–6.11 as the v0.6.1 lifecycle-tightening commit. Verification is row-by-row against `baseline.md` (the headline target is `End-of-session main-thread stall: 0`).*
