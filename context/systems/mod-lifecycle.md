# Mod Lifecycle

*Maturity: working · Stability: stable.*

## Scope / Purpose

The orchestrator. Drives `Mod.Load`, `Mod.Unload`, and `ModSystem.OnWorldLoad` / `OnWorldUnload` / `PreUpdateEntities` / `PostUpdateEverything`. Connects the static-class subsystems (interceptors, probes, attribution) to tModLoader's lifecycle by calling their `Install` / `Uninstall` / `BeginTick` / `EndTick` at the right moments.

Two classes carry this responsibility:

- `PerformanceProfiler : Mod` — top-level mod entry (`PerformanceProfiler.cs`).
- `ProfilerSystem : ModSystem` — per-world lifecycle (`Profiling/ProfilerSystem.cs`).

A third, `ProfilerPlayer : ModPlayer`, lives in the same file as the Mod class and owns the F9 keybind polling.

## Boundaries / Ownership

Files: `PerformanceProfiler.cs`, `Profiling/ProfilerSystem.cs`.

Owns:

- The `Mod.Load` info log + LiteDB `Database` open + `DashboardHttpServer` bind + `DataRegistry` stream registration.
- The `Mod.Unload` ILHook teardown + `DataRegistry.DisposeAll()` + Dashboard/Database dispose.
- The `ProfilerPlayer.OnEnterWorld` chat announcement of the F9 dashboard hotkey.
- The `ProfilerPlayer.ProcessTriggers` F9-poll → browser-open dispatch.
- `PostSetupContent`: backend install + biome registry populate + subworld probe init + mod-roster scan.
- `OnWorldLoad`: sets the deferred-init flag (the heavy construction runs on the first `PostUpdateEverything`).
- `RunDeferredWorldLoadInit` (first tick): collector allocation + `SessionRecorder` creation (with try/catch) + watchers + context tagger + event aggregator + segment engine + `DataRegistry.InitialiseAll(...)`.
- `PreSaveAndQuit`: kicks off the async session-end (parallel with vanilla's save chain).
- `OnWorldUnload`: idempotent session-end kickoff + segment flush + per-world teardown + insights engine clear + `DataRegistry.ResetAll()`.
- `PreUpdateEntities`: `Collector.BeginTick()`.
- `PostUpdateEverything`: deferred-init drive on the first tick, then `Collector.EndTick(...)` + recorder feed (with try/catch + self-disable) + context tagger snapshot + pipeline per-tick callbacks + segment engine.

Does not own:

- Implementation logic of any subsystem.
- The `ModifyInterfaceLayers` mount or `UpdateUI` pump — those live on `ProfilerOverlaySystem`, a separate `ModSystem` in `UI/`.
- Any per-tick attribution arithmetic — `PerModAttribution.Add` is called from inside the interceptors, not from here.

## Current Implemented Reality

### `Mod.Load` and `Mod.Unload`

```csharp
public override void Load() {
    LoggerOrNull = Logger;
    Logger.Info($"Performance Profiler loaded (backend: {HookBackend.Mode}).");
    RegisterDataPipeline();                 // register every IDataStream once
    try { Database = new ProfilerDatabase(ProfilerPaths.Root(), ...);   // LiteDB open
          LegacyJsonImporter.RunOnceIfNeeded(Database, Logger); }       // one-shot legacy import
    catch { Database = null; }               // degrade to no-persistence (Invariant 4)
    try { Dashboard = new DashboardHttpServer(route: DashboardRouter.Route, ...); } // loopback HTTP
    catch { Dashboard = null; }              // degrade to inert F9 keybind
}

public override void Unload() {
    ILHookInterceptor.Uninstall();
    DataRegistry.Shared.DisposeAll();        // before the DB dispose: streams may hold a DB ref
    Dashboard?.Dispose(); Dashboard = null;  // before the DB: route handler must not touch a half-disposed DB
    Database?.Dispose();  Database = null;
    LoggerOrNull = null;
}
```

`Load` now does real work: register the data pipeline, open the LiteDB-backed `Database`, run a one-shot import of any legacy JSON sessions, and bind the loopback `DashboardHttpServer`. Both the DB open and the dashboard bind are wrapped so a failure degrades to "no persistence" / "inert F9 keybind" rather than aborting the mod (Invariant 4 applied to the file system and the port range). The heavyweight hook install still happens later in `PostSetupContent`.

`Unload` calls `ILHookInterceptor.Uninstall()` to dispose every installed `ILHook` **before** tModLoader unloads our assembly. Without this, the IL patches on other mods' methods would still call into `ProbeStack`, which lives in our assembly that is about to disappear — `InvalidProgramException` on the next tick. The delegate-pair backend does not need a teardown because tModLoader auto-removes its `MonoModHooks.Add` detours per-assembly. The dispose order is load-bearing: `DataRegistry.DisposeAll()` and `Dashboard.Dispose()` both run **before** `Database.Dispose()` so neither a stream nor the route handler calls into a half-disposed DB.

### `ModSystem.PostSetupContent`

Runs after every mod's content is set up. Order matters:

```csharp
public override void PostSetupContent() {
    Time.Reset();                      // wall-clock origin (Stopwatch-based)
    LangNameCache.Populate();          // pre-resolve every Lang name into a flat string[]
    SelfHealth.MarkInstallStart();     // managed-heap baseline before our install pass
    HookInterceptor.Install(Mod);      // always; does mod enumeration + PerModAttribution.Configure
    if (HookBackend.ILHookActive)
        ILHookInterceptor.Install(Mod, HookInterceptor.ProfiledMods);
    SelfHealth.MarkInstallEnd(PerModAttribution.HookCount);   // install-delta = our hook-install cost
    BiomeRegistry.Populate();
    SubworldProbe.Initialise();
    Mod.Logger.Info($"events context: {...}");
    roster?.Scan();                    // v0.12 F1 install-time per-mod content roster scan
}
```

Why this order:

1. `HookInterceptor.Install` always runs first — it does the `ModLoader.Mods` enumeration and `PerModAttribution.Configure(modCount, backendCount, allocTracking)` that the ILHook path reuses.
2. `ILHookInterceptor.Install` re-uses `HookInterceptor.ProfiledMods` so both backends instrument the same modlist with consistent ids.
3. `BiomeRegistry.Populate`, `SubworldProbe.Initialise`, and the `ModRosterScanner.Scan` all run after both interceptors are stable — they read tModLoader content registries (and `BiomeRegistry`'s owner map) that are guaranteed populated by PostSetupContent.
4. `SelfHealth.MarkInstallStart/End` bracket the install pass so the heap delta attributable to our hooks is captured before the per-world collector exists; the collector picks up the same process-singleton `SelfHealth` at world load.

### `ModSystem.OnWorldLoad` and deferred init

`OnWorldLoad` itself is now a one-liner: it sets `_deferredInitPending = true` and returns. The heavy construction (which v0.5 ran inline here and measured at a 172 ms world-enter freeze) is deferred to the first `PostUpdateEverything` tick, where the cost lands during gameplay (allowed to spike per Invariant 2 budgets) instead of UI-blocking the world-enter.

```csharp
public override void OnWorldLoad() {
    _deferredInitPending = true;
}

// Runs on the first PostUpdateEverything after world-load.
private void RunDeferredWorldLoadInit() {
    Collector = new MetricCollector(HistoryCapacity, SelfHealth);   // 30s × 60Hz = 1800 frames
    try {
        if (PerformanceProfiler.Database != null)
            _recorder = new SessionRecorder(db, profilerVersion, tmlVersion: "1.4.4", mode, ...);
        else
            _recorder = null;   // DB failed to open: degraded session, no persistence
    } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) {
        _recorder = null;
        Mod.Logger.Warn($"Session recorder disabled for this world ...");
    }
    _transitionWatcher = _recorder != null ? new ContextTransitionWatcher() : null;
    _snapshotter       = _recorder != null ? new WorldSnapshotter()        : null;
    _deathDetector     = _recorder != null ? new PlayerDeathDetector()     : null;
    _contextTagger = new ContextTagger(); _contextTagger.Reset();
    Events = new EventAggregator();
    SegmentStore = new SegmentStore(PerformanceProfiler.Database, ...);   // live even without the recorder
    Segments = new SegmentDetector(_recorder?.SessionId ?? ObjectId.Empty, SegmentStore);
    DataRegistry.Shared.InitialiseAll(new SessionContext { ... });        // freeze per-tick callbacks
    Mod.Logger.Info($"Profiler armed: {HistoryCapacity}-tick rolling history allocated.");
}
```

Critical properties:

- The legacy JSON `SessionLogWriter` is gone (deleted in v0.3); persistence is now the LiteDB-backed `SessionRecorder`. The mod-wide `Database` is opened once at `Mod.Load`; the per-world `_recorder` is created here against it.
- `SessionRecorder` creation is wrapped in try/catch and is also skipped when `Database == null`. A failure leaves `_recorder = null` and the rest of the lifecycle continues — metric collection, the segment engine, and the live dashboard all run regardless (Invariant 4).
- The collector is allocated **before** the recorder; if recorder creation throws, the collector is still alive and the player still gets the dashboard.
- The segment engine and the `DataRegistry` pipeline live even when the recorder does not — closed segments still flow through the in-memory ring for the Timeline, only the DB write enqueue degrades to a no-op.

### `ModSystem.PreSaveAndQuit` and the async session-end

`PreSaveAndQuit` fires immediately before vanilla's world-save begins (and before `OnWorldUnload`). It kicks off the session-end aggregation **now** so the recorder's `End` runs in parallel with vanilla's 1-3s save+backup chain instead of after it. The work is idempotent — a `_preSaveEndKickedOff` latch means `OnWorldUnload` skips a second spawn if `PreSaveAndQuit` already ran.

```csharp
public override void PreSaveAndQuit() {
    try { KickOffSessionEndAsync(); _preSaveEndKickedOff = true; }
    catch (Exception ex) {           // PreSaveAndQuit is NOT wrapped by tML's SystemLoader catch;
        LoggerOrNull?.Warn(...);     // a throw here would abort the user's world save
    }
}

private void KickOffSessionEndAsync() {
    if (Collector == null || _recorder == null) return;
    Collector.FlushSpikes();         // close any open spike window before End()
    var (recorder, collector, db, logger) = (_recorder, Collector, Database, LoggerOrNull);
    _ = Task.Run(() => {
        recorder.End(collector, endReason: "clean");
        db?.DrainAndTruncateJournalForSessionEnd();
        SessionSummaryLogger.Write(logger, db, recorder.SessionId);
    });
}
```

### `ModSystem.OnWorldUnload`

```csharp
public override void OnWorldUnload() {
    if (!_preSaveEndKickedOff) KickOffSessionEndAsync();   // quit-via-menu / disconnect path
    _preSaveEndKickedOff = false;
    Segments?.CloseAllOnShutdown(tickIndex, unixMs);       // flush still-open segments to Timeline + DB
    _recorder = null; _transitionWatcher = null; _snapshotter = null; _deathDetector = null;
    Collector = null; _contextTagger = null; Events = null;
    Segments = null; SegmentStore = null;
    InsightsEngine.Shared = null;
    BossSampler.Clear();
    SubworldProbe.Clear();
    DataRegistry.Shared.ResetAll();   // discard every stream's per-session state (streams stay registered)
    Mod.Logger.Info("Profiler disarmed: world unloaded.");
}
```

Critical properties:

- `FlushSpikes` runs (inside `KickOffSessionEndAsync`) **before** `SessionRecorder.End` so any open spike window lands in the persisted session.
- The session-end kickoff is idempotent: `PreSaveAndQuit` normally does it, and `OnWorldUnload` only kicks off when the latch is unset (quit via title-screen menu, server disconnect).
- Still-open segments are closed via `CloseAllOnShutdown` so they reach the Timeline and DB before the detector is dropped.
- `InsightsEngine.Shared = null` is the load-bearing clear that prevents records from leaking across sessions.
- `BossSampler.Clear()` and `SubworldProbe.Clear()` reset their state so the next world starts fresh.
- `DataRegistry.Shared.ResetAll()` clears every registered stream's per-session buffers; the streams themselves stay registered for the next world load.

### `ModSystem.PreUpdateEntities` and `PostUpdateEverything`

```csharp
public override void PreUpdateEntities() {
    Collector?.BeginTick((long)Main.GameUpdateCount);
}

public override void PostUpdateEverything() {
    // First tick after world-load: run the deferred heavy construction, skip the per-tick path.
    if (_deferredInitPending) { _deferredInitPending = false; RunDeferredWorldLoadInit(); return; }

    MetricCollector? collector = Collector;
    if (collector == null || !collector.TickOpen) return;

    collector.EndTick(
        tickIndex: (long)Main.GameUpdateCount,
        npcCount: CountActive(Main.npc),
        projectileCount: CountActive(Main.projectile),
        dustCount: CountActive(Main.dust));

    if (collector.ConsumeDivergenceLogTrigger()) {
        // emit "[backend-compare] delegate=... ilhook=... Δ=..." log line
    }

    // InsightsEngine.Evaluate scheduled on the thread pool every 60 ticks, gated on the
    // previous run completing (it wedged the main loop for >1s when run inline — see notes/decisions.md).
    if (collector.History.Count % 60 == 0 && Interlocked.CompareExchange(ref _insightsEvalInflight, 1, 0) == 0)
        Task.Run(() => InsightsEngine.GetOrCreateShared().Evaluate(collector, latestTick, historyDepth));

    // Recorder feed: per-tick downsampling + spike/stall drain. Queue-only; game thread never blocks on disk.
    if (_recorder != null && collector.History.Count > 0) {
        try { _recorder.OnTick(collector.History.Newest, collector); }
        catch (Exception ex) { Mod.Logger.Warn(...); _recorder = null; _transitionWatcher = null; }  // self-disable
    }

    if (tagger != null && events != null && collector.History.Count > 0) {
        long tickIndex = (long)Main.GameUpdateCount;
        tagger.Snapshot(tickIndex);
        double frameMs = collector.History.Newest.FrameTimeMs;
        events.Accumulate(in tagger.Current, frameMs);

        // Drive every frozen PerTick pipeline callback with a stack-allocated TickContext.
        var cbs = DataRegistry.Shared.PerTickCallbacks;
        if (cbs.Length > 0) { var ctx = new TickContext(...); for (int i = 0; i < cbs.Length; i++) cbs[i](in ctx); }

        // Transitions, periodic snapshots, death edge → recorder (each null-guarded).
        _transitionWatcher?.OnSnapshot(in tagger.Current, frameMs, _recorder);
        _snapshotter?.OnTick(_recorder, in tagger.Current, ...);
        _deathDetector?.OnTick(_recorder, in tagger.Current);

        // Segment engine — same EventContext; folds new spike/stall/death edges into open segments.
        Segments?.OnTick(tickIndex, unixMs, in tagger.Current, frameMs, collector.PerModCategoryRawMs);
    }
}
```

Critical properties:

- The first `PostUpdateEverything` after world-load drives `RunDeferredWorldLoadInit` and returns; the per-tick path starts from tick 2.
- `Collector?.BeginTick(...)` is null-safe; before the deferred init runs the call is a no-op.
- `collector.TickOpen` guards against `PostUpdateEverything` running without `PreUpdateEntities` having fired — partial-frame protection.
- The `[backend-compare]` log line only fires when divergence crosses the trigger threshold; consumed-and-reset semantics avoid spamming `client.log`.
- The recorder feed is wrapped in try/catch with a self-disable (`_recorder = null`) on failure; the rest of the tick continues.
- `InsightsEngine.Evaluate` runs off-thread (thread-pool), gated on the previous run completing — running it inline wedged the main loop for over a second on a long-session playtest.
- Context tagger snapshot runs **after** `EndTick` so it stamps the just-closed `TickFrame.Context`; the pipeline callbacks and segment engine read against that same snapshot.

### `ProfilerPlayer.OnEnterWorld`

Fires after the player has actually entered the world (not at `OnWorldLoad`, which fires mid-load when chat is wiped):

```csharp
public override void OnEnterWorld() {
    string? url = PerformanceProfiler.Dashboard?.Url;
    if (url != null)
        Main.NewText($"Performance Profiler ready. Press F9 for the dashboard ({url}).", 180, 220, 255);
    else
        Main.NewText("Performance Profiler ready — dashboard server failed to start; see client.log.", 255, 180, 100);
    Mod.Logger.Info("OnEnterWorld fired; dashboard hotkey announced.");
}
```

v0.9.0 archived the in-game overlay; the chat hint now points at the browser dashboard URL (or a failure line when the server never bound). The comment in the source explicitly documents the OnWorldLoad-chat-clear bug.

### `ProfilerPlayer.ProcessTriggers`

```csharp
public override void ProcessTriggers(TriggersSet triggersSet) {
    ModKeybind? dashboard = ProfilerOverlaySystem.DashboardKeybind;
    if (dashboard != null && dashboard.JustPressed) {
        OpenDashboardInBrowser();   // platform-dispatched URL open: open / xdg-open / shell
    }
}
```

`ProcessTriggers` runs only during gameplay on the local client (per tModLoader's docs); the F9 dashboard keybind (registered as `"OpenDashboard"` in `UI/ProfilerOverlaySystem.cs`) is correctly scoped. `OpenDashboardInBrowser` dispatches on `RuntimeInformation` (`open` on macOS, `xdg-open` on Linux, shell on Windows) and falls back to printing the URL in chat if the launch fails.

### Entity counting helpers

`CountActive(NPC[])`, `CountActive(Projectile[])`, `CountActive(Dust[])` — three nearly-identical loops iterating `entities[i].active`. The `Dust[]` variant carries a comment noting it scans ~6000 slots per tick and is acceptable for M1 with a watch flag.

## Key Interfaces / Data Flow

```
Mod.Load
   ├─ Logger.Info (backend mode)
   ├─ RegisterDataPipeline()                 // every IDataStream registered once
   ├─ try Database = new ProfilerDatabase(...)  catch → null   // LiteDB open (degrade ok)
   │     └─ LegacyJsonImporter.RunOnceIfNeeded(...)            // one-shot legacy JSON import
   └─ try Dashboard = new DashboardHttpServer(...) catch → null  // loopback HTTP bind (degrade ok)

PostSetupContent (ModSystem)
   ├─ Time.Reset(); LangNameCache.Populate(); SelfHealth.MarkInstallStart()
   ├─ HookInterceptor.Install(Mod)
   │     └─ enumerate ModLoader.Mods
   │     └─ PerModAttribution.Configure(...)
   ├─ ILHookInterceptor.Install(Mod, ProfiledMods)    [if HookBackend.ILHookActive]
   ├─ SelfHealth.MarkInstallEnd(PerModAttribution.HookCount)
   ├─ BiomeRegistry.Populate()
   ├─ SubworldProbe.Initialise()
   └─ ModRosterScanner.Scan()                 // v0.12 F1 install-time roster

OnWorldLoad (ModSystem)
   └─ _deferredInitPending = true             // heavy construction deferred to first tick

OnEnterWorld (ModPlayer, local)
   └─ Main.NewText("Press F9 for the dashboard (URL) …")

per gameplay tick:
   ProcessTriggers (ModPlayer)
      └─ if F9.JustPressed: OpenDashboardInBrowser()   // platform-dispatched URL open

   PreUpdateEntities (ModSystem)
      └─ Collector?.BeginTick(GameUpdateCount)

   [tModLoader dispatches every hook]

   PostUpdateEverything (ModSystem)
      ├─ if _deferredInitPending: RunDeferredWorldLoadInit(); return
      │     ├─ Collector = new MetricCollector(1800, SelfHealth)
      │     ├─ try _recorder = new SessionRecorder(Database, ...) catch IO → null   (skip if Database == null)
      │     ├─ _transitionWatcher / _snapshotter / _deathDetector  (recorder-gated)
      │     ├─ _contextTagger = new ContextTagger(); Events = new EventAggregator()
      │     ├─ SegmentStore + SegmentDetector  (live even without recorder)
      │     └─ DataRegistry.Shared.InitialiseAll(SessionContext)   // freeze per-tick callbacks
      ├─ Collector.EndTick(tickIndex, counts)
      ├─ ConsumeDivergenceLogTrigger → [backend-compare] log line
      ├─ every 60 ticks (gated): Task.Run(InsightsEngine.Evaluate)   // off-thread
      ├─ try _recorder?.OnTick(latest, Collector)
      │     catch: _recorder = null + Logger.Warn                    // self-disable
      ├─ _contextTagger.Snapshot(tickIndex); Events.Accumulate(tagger.Current, frameMs)
      ├─ DataRegistry.Shared.PerTickCallbacks[i](in TickContext)     // frozen pipeline fan-out
      ├─ _transitionWatcher / _snapshotter / _deathDetector .On*(recorder, ...)
      └─ Segments?.OnTick(...) + OnSpike/OnStall/OnDeath edge diffs

PreSaveAndQuit (ModSystem)
   └─ KickOffSessionEndAsync(); _preSaveEndKickedOff = true
         └─ Collector.FlushSpikes(); Task.Run(() => recorder.End(...); db.DrainAndTruncate...; SessionSummaryLogger.Write(...))

OnWorldUnload (ModSystem)
   ├─ if !_preSaveEndKickedOff: KickOffSessionEndAsync()   // menu-quit / disconnect path
   ├─ Segments?.CloseAllOnShutdown(...)
   ├─ _recorder / watchers / Collector / _contextTagger / Events / Segments / SegmentStore = null
   ├─ InsightsEngine.Shared = null
   ├─ BossSampler.Clear(); SubworldProbe.Clear()
   ├─ DataRegistry.Shared.ResetAll()
   └─ Logger.Info("Profiler disarmed")

Mod.Unload
   ├─ ILHookInterceptor.Uninstall()           [explicit, before assembly unload]
   ├─ DataRegistry.Shared.DisposeAll()         [before DB dispose]
   ├─ Dashboard?.Dispose()                     [before DB dispose]
   └─ Database?.Dispose()
```

## Implemented Outputs / Artifacts

| Surface | Source |
|---------|--------|
| `client.log` lifecycle lines | `Mod.Logger.Info` at load / install / world load / world unload / session-end |
| `[backend-compare]` log line | `PostUpdateEverything` after `ConsumeDivergenceLogTrigger` |
| Chat: "Performance Profiler ready. Press F9 for the dashboard (URL)." | `ProfilerPlayer.OnEnterWorld` |
| F9 → browser dashboard open | `ProfilerPlayer.ProcessTriggers` → `OpenDashboardInBrowser` |
| LiteDB session record at session-end | `KickOffSessionEndAsync` → `SessionRecorder.End` |

## Known Issues / Active Risks

- **`Mod.Close` is not implemented.** `Close` is called before `Unload` and may fire multiple times. Today everything we need to flush is kicked off at `PreSaveAndQuit` / `OnWorldUnload`, both of which run **before** `Mod.Close`/`Unload` in tModLoader's order, so no flush is lost — but if a tModLoader change ever reordered that, a session could miss its final write.
- **`PreSaveAndQuit` runs outside tML's `SystemLoader` catch.** A throw inside `PreSaveAndQuit` would abort the user's world save, so the handler wraps `KickOffSessionEndAsync` in its own try/catch. Verified against the mod-lifecycle dossier §3.5; if a future tModLoader version starts wrapping this hook the defensive catch becomes redundant but harmless.
- **`ProfilerOverlaySystem.DashboardKeybind` is read by name via the namespace import.** If `ProfilerOverlaySystem` moved namespaces, the `using PerformanceProfiler.UI;` at the top of `PerformanceProfiler.cs` would need to update. Today it is colocated.

## Partial / In Progress

Nothing in progress.

## Planned / Missing / Likely Changes

- **Settings UI integration.** Once the future settings tab lands, `PostSetupContent` may read player-saved settings to choose `HookBackend.Mode` and `AllocationTracking` instead of using build-time constants.

## Durable Notes / Discarded Approaches

- **`Main.NewText` was originally in `OnWorldLoad`.** Doesn't work: tModLoader clears chat during the load-to-in-game transition, so the message is wiped before the player sees it. Moved to `ProfilerPlayer.OnEnterWorld`, where it announces the dashboard hotkey (documented as a code comment in `PerformanceProfiler.cs`).
- **`ILHookInterceptor.Uninstall()` was originally in `OnWorldUnload`.** Wrong scope: `OnWorldUnload` fires on every world transition, but the IL detours are process-scoped (they patch other mods' methods, which exist as long as those mods are loaded). Moved to `Mod.Unload` so disposal happens only when the mod actually unloads.
- **`BiomeRegistry.Populate` and `SubworldProbe.Initialise` were originally in `Mod.Load`.** Too early — modded biomes are registered during the content-load phase that runs between `Mod.Load` and `PostSetupContent`. Moved to `PostSetupContent` so every mod's biomes are visible.

## Obsolete / No Longer Relevant

Nothing.

## Cross-references

- `tmodloader/lifecycle-and-loop.md` — the tModLoader lifecycle this subsystem hooks into.
- `systems/hook-instrumentation.md` — what `HookInterceptor.Install` and `ILHookInterceptor.Install` actually do.
- `systems/metric-collection.md` — what `BeginTick` / `EndTick` drive.
- `systems/persistence.md` — the LiteDB `Database`, `SessionRecorder`, the writer thread, and the try/catch wrapping detailed here.
- `systems/web-dashboard.md` — the `DashboardHttpServer` bound at `Mod.Load` and the F9 browser-open.
- `systems/data-pipeline.md` — the `DataRegistry` register / initialise / reset / dispose lifecycle.
- `systems/events-and-context.md` — what `_contextTagger.Snapshot` and `Events.Accumulate` consume.
- `systems/insights-engine.md` — `InsightsEngine.Shared = null` clear.
