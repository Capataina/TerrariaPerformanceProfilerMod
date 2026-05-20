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

- The `Mod.Load` info log (proof the mod loaded).
- The `Mod.Unload` ILHook teardown call.
- The `ProfilerPlayer.OnEnterWorld` chat announcement of the F9 hotkey.
- The `ProfilerPlayer.ProcessTriggers` F9-poll → toggle dispatch.
- `PostSetupContent`: backend install + biome registry populate + subworld probe init.
- `OnWorldLoad`: collector allocation + session log creation (with try/catch) + context tagger init.
- `OnWorldUnload`: spike flush + session log finalisation (with try/catch) + context teardown + insights engine clear.
- `PreUpdateEntities`: `Collector.BeginTick()`.
- `PostUpdateEverything`: `Collector.EndTick(...)` + session log tick (with try/catch + self-disable) + context tagger snapshot.

Does not own:

- Implementation logic of any subsystem.
- The `ModifyInterfaceLayers` mount or `UpdateUI` pump — those live on `ProfilerOverlaySystem`, a separate `ModSystem` in `UI/`.
- Any per-tick attribution arithmetic — `PerModAttribution.Add` is called from inside the interceptors, not from here.

## Current Implemented Reality

### `Mod.Load` and `Mod.Unload`

```csharp
public override void Load() {
    Logger.Info($"Performance Profiler loaded (backend: {HookBackend.Mode}).");
}

public override void Unload() {
    ILHookInterceptor.Uninstall();
}
```

`Load` is intentionally minimal: the mod's content is autoloaded automatically and tModLoader's content-load phase runs before `PostSetupContent`, where the heavyweight setup actually happens.

`Unload` calls `ILHookInterceptor.Uninstall()` to dispose every installed `ILHook` **before** tModLoader unloads our assembly. Without this, the IL patches on other mods' methods would still call into `ProbeStack`, which lives in our assembly that is about to disappear — `InvalidProgramException` on the next tick. The delegate-pair backend does not need a teardown because tModLoader auto-removes its `MonoModHooks.Add` detours per-assembly.

### `ModSystem.PostSetupContent`

Runs after every mod's content is set up. Order matters:

```csharp
public override void PostSetupContent() {
    HookInterceptor.Install(Mod);      // always; does mod enumeration + PerModAttribution.Configure
    if (HookBackend.ILHookActive)
        ILHookInterceptor.Install(Mod, HookInterceptor.ProfiledMods);
    BiomeRegistry.Populate();
    SubworldProbe.Initialise();
    Mod.Logger.Info($"events context: {...}");
}
```

Why this order:

1. `HookInterceptor.Install` always runs first — it does the `ModLoader.Mods` enumeration and `PerModAttribution.Configure(modCount, backendCount, allocTracking)` that the ILHook path reuses (`Profiling/ProfilerSystem.cs:64-66`).
2. `ILHookInterceptor.Install` re-uses `HookInterceptor.ProfiledMods` so both backends instrument the same modlist with consistent ids.
3. `BiomeRegistry.Populate` and `SubworldProbe.Initialise` run after both interceptors are stable — the context layer reads tModLoader content registries that are guaranteed populated by PostSetupContent.

### `ModSystem.OnWorldLoad`

```csharp
public override void OnWorldLoad() {
    Collector = new MetricCollector(HistoryCapacity);   // 30s × 60Hz = 1800 frames
    try {
        _sessionLog = SessionLogWriter.Create();
    } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) {
        _sessionLog = null;
        Mod.Logger.Warn($"Session log disabled for this world ...");
    }
    _contextTagger = new ContextTagger();
    _contextTagger.Reset();
    Events = new EventAggregator();
    Mod.Logger.Info($"Profiler armed: {HistoryCapacity}-tick rolling history allocated.");
}
```

Critical properties:

- Session log creation is wrapped in try/catch. A permissions or IO failure leaves `_sessionLog = null` and the rest of the lifecycle continues. Metric collection runs regardless.
- The collector is allocated **before** the session log; if the session log creation throws, the collector is still alive and the player still gets the overlay.

### `ModSystem.OnWorldUnload`

```csharp
public override void OnWorldUnload() {
    Collector?.FlushSpikes();   // close any open spike window first
    if (Collector != null && _sessionLog != null) {
        try { _sessionLog.End(Collector); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) {
            Mod.Logger.Warn($"Session log end-write failed ...");
        }
    }
    _sessionLog?.Dispose();
    _sessionLog = null;
    Collector = null;
    _contextTagger = null;
    Events = null;
    InsightsEngine.Shared = null;
    BossSampler.Clear();
    SubworldProbe.Clear();
    Mod.Logger.Info("Profiler disarmed: world unloaded.");
}
```

Critical properties:

- `FlushSpikes` runs **before** `_sessionLog.End` so any open spike window lands in the final JSON.
- `InsightsEngine.Shared = null` is the load-bearing clear that prevents records from leaking across sessions.
- `BossSampler.Clear()` and `SubworldProbe.Clear()` reset their state so the next world starts fresh.

### `ModSystem.PreUpdateEntities` and `PostUpdateEverything`

```csharp
public override void PreUpdateEntities() {
    Collector?.BeginTick();
}

public override void PostUpdateEverything() {
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

    if (_sessionLog != null) {
        try { _sessionLog.Tick(collector); }
        catch (SessionLogFailureException ex) {
            Mod.Logger.Warn(...);
            _sessionLog = null;   // self-disable for the world
        }
    }

    if (tagger != null && events != null && collector.History.Count > 0) {
        long tickIndex = (long)Main.GameUpdateCount;
        tagger.Snapshot(tickIndex);
        double frameMs = collector.History[collector.History.Count - 1].FrameTimeMs;
        events.Accumulate(in tagger.Current, frameMs);
    }
}
```

Critical properties:

- `Collector?.BeginTick()` is null-safe; before `OnWorldLoad` runs (impossible in practice, but defensive) the call is a no-op.
- `collector.TickOpen` guards against `PostUpdateEverything` running without `PreUpdateEntities` having fired — partial-frame protection.
- The `[backend-compare]` log line only fires when divergence crosses the trigger threshold; consumed-and-reset semantics avoid spamming `client.log`.
- The session log call is wrapped in `try/catch (SessionLogFailureException)` for the self-disable path. Other exceptions propagate.
- Context tagger snapshot runs **after** `EndTick` so it stamps the just-closed `TickFrame.Context`.

### `ProfilerPlayer.OnEnterWorld`

Fires after the player has actually entered the world (not at `OnWorldLoad`, which fires mid-load when chat is wiped):

```csharp
public override void OnEnterWorld() {
    Main.NewText("Performance Profiler ready. Press F9 for the overlay.", 255, 220, 100);
    Mod.Logger.Info("OnEnterWorld fired; overlay hotkey announced.");
}
```

The comment in the source explicitly documents the OnWorldLoad-chat-clear bug.

### `ProfilerPlayer.ProcessTriggers`

```csharp
public override void ProcessTriggers(TriggersSet triggersSet) {
    ModKeybind? toggle = ProfilerOverlaySystem.ToggleKeybind;
    if (toggle != null && toggle.JustPressed) {
        ModContent.GetInstance<ProfilerOverlaySystem>().ToggleVisibility();
    }
}
```

`ProcessTriggers` runs only during gameplay on the local client (per tModLoader's docs); the F9 toggle is correctly scoped.

### Entity counting helpers

`CountActive(NPC[])`, `CountActive(Projectile[])`, `CountActive(Dust[])` — three nearly-identical loops iterating `entities[i].active`. The `Dust[]` variant carries a comment noting it scans ~6000 slots per tick and is acceptable for M1 with a watch flag.

## Key Interfaces / Data Flow

```
Mod.Load
   └─ Logger.Info (backend mode)

PostSetupContent (ModSystem)
   ├─ HookInterceptor.Install(Mod)
   │     └─ enumerate ModLoader.Mods
   │     └─ PerModAttribution.Configure(...)
   ├─ ILHookInterceptor.Install(Mod, ProfiledMods)    [if HookBackend.ILHookActive]
   ├─ BiomeRegistry.Populate()
   └─ SubworldProbe.Initialise()

OnWorldLoad (ModSystem)
   ├─ Collector = new MetricCollector(1800)
   ├─ try SessionLogWriter.Create() catch IO → null
   ├─ _contextTagger = new ContextTagger()
   └─ Events = new EventAggregator()

OnEnterWorld (ModPlayer, local)
   └─ Main.NewText("Press F9 …")

per gameplay tick:
   ProcessTriggers (ModPlayer)
      └─ if F9.JustPressed: ProfilerOverlaySystem.ToggleVisibility()

   PreUpdateEntities (ModSystem)
      └─ Collector?.BeginTick()

   [tModLoader dispatches every hook]

   PostUpdateEverything (ModSystem)
      ├─ Collector.EndTick(tickIndex, counts)
      ├─ ConsumeDivergenceLogTrigger → [backend-compare] log line
      ├─ try _sessionLog?.Tick(Collector)
      │     catch SessionLogFailureException: _sessionLog = null + Logger.Warn
      └─ _contextTagger.Snapshot(tickIndex); Events.Accumulate(tagger.Current, frameMs)

OnWorldUnload (ModSystem)
   ├─ Collector?.FlushSpikes()
   ├─ try _sessionLog?.End(Collector) catch IO
   ├─ _sessionLog?.Dispose(); _sessionLog = null
   ├─ Collector = null; _contextTagger = null; Events = null
   ├─ InsightsEngine.Shared = null
   ├─ BossSampler.Clear(); SubworldProbe.Clear()
   └─ Logger.Info("Profiler disarmed")

Mod.Unload
   └─ ILHookInterceptor.Uninstall()   [explicit, before assembly unload]
```

## Implemented Outputs / Artifacts

| Surface | Source |
|---------|--------|
| `client.log` lifecycle lines | `Mod.Logger.Info` at install / world load / world unload |
| `[backend-compare]` log line | `PostUpdateEverything` after `ConsumeDivergenceLogTrigger` |
| Chat: "Performance Profiler ready. Press F9 …" | `ProfilerPlayer.OnEnterWorld` |
| F9 toggle | `ProfilerPlayer.ProcessTriggers` |

## Known Issues / Active Risks

- **No `PreSaveAndQuit` handler.** Today the only clean-close trigger is `OnWorldUnload`, which fires on every world transition. `PreSaveAndQuit` is the documented "user chose to save and exit" signal — the session JSON could distinguish clean-close vs dirty-close more explicitly. Not a correctness bug; today the JSON simply does not carry this distinction.
- **`Mod.Close` is not implemented.** `Close` is called before `Unload` and may fire multiple times. Today everything we need to flush is flushed in `OnWorldUnload`; a player triggering a `Mods → Reload` mid-session would go through `Mod.Close` → `Mod.Unload` and the session log's final write happens at `OnWorldUnload`, which runs **before** `Mod.Close`/`Unload` in tModLoader's order. So in practice no flush is lost — but if a tModLoader change ever reorders that, the session JSON could be missing its final entry.
- **`ProfilerOverlaySystem.ToggleKeybind` is read by name via the namespace import.** If `ProfilerOverlaySystem` moved namespaces, the `using PerformanceProfiler.UI;` at the top of `PerformanceProfiler.cs` would need to update. Today it is colocated.

## Partial / In Progress

Nothing in progress.

## Planned / Missing / Likely Changes

- **`PreSaveAndQuit` handler** to badge clean-close in the session JSON. Low priority.
- **Settings UI integration.** Once the future settings tab lands, `PostSetupContent` may read player-saved settings to choose `HookBackend.Mode` and `AllocationTracking` instead of using build-time constants.

## Durable Notes / Discarded Approaches

- **`Main.NewText` was originally in `OnWorldLoad`.** Doesn't work: tModLoader clears chat during the load-to-in-game transition, so the message is wiped before the player sees it. Moved to `ProfilerPlayer.OnEnterWorld` (documented as a code comment in `PerformanceProfiler.cs`).
- **`ILHookInterceptor.Uninstall()` was originally in `OnWorldUnload`.** Wrong scope: `OnWorldUnload` fires on every world transition, but the IL detours are process-scoped (they patch other mods' methods, which exist as long as those mods are loaded). Moved to `Mod.Unload` so disposal happens only when the mod actually unloads.
- **`BiomeRegistry.Populate` and `SubworldProbe.Initialise` were originally in `Mod.Load`.** Too early — modded biomes are registered during the content-load phase that runs between `Mod.Load` and `PostSetupContent`. Moved to `PostSetupContent` so every mod's biomes are visible.

## Obsolete / No Longer Relevant

Nothing.

## Cross-references

- `tmodloader/lifecycle-and-loop.md` — the tModLoader lifecycle this subsystem hooks into.
- `systems/hook-instrumentation.md` — what `HookInterceptor.Install` and `ILHookInterceptor.Install` actually do.
- `systems/metric-collection.md` — what `BeginTick` / `EndTick` drive.
- `systems/session-logging.md` — the try/catch wrapping detailed here.
- `systems/events-and-context.md` — what `_contextTagger.Snapshot` and `Events.Accumulate` consume.
- `systems/insights-engine.md` — `InsightsEngine.Shared = null` clear.
