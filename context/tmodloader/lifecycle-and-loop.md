# tModLoader Integration Surface — Mod Lifecycle & Game Loop

> Source: tModLoader.xml (tModLoader 1.4.4, build ~#5089). Serves components: Metric Collector (2), Ring Buffer (3), Encounter Detector (5), Persistent Store (7).

## Summary

The mod-load and world-load lifecycle the profiler depends on is almost entirely covered by the documented public API: `Mod` and `ModSystem` give clean, well-paired enter/exit hooks for allocating the ring buffer at world entry and freeing it at world exit, and the per-tick `ModSystem` update hooks (`PreUpdateEntities` ... `PostUpdateEverything`) provide a deterministic frame boundary to drive sampling. Two real gaps exist: there is **no public API for a per-mod persistent storage directory** — `Mod.File` is the read-only `.tmod` archive, not a writable data folder, and `SavePath` does not appear in the XML at all — and **clean-vs-crash session-close detection** relies on the *absence* of a hook firing rather than a positive signal, so dirty-exit recovery has to be reconstructed from a sentinel-file pattern. Frame-number and game-time sources (`Main.GameUpdateCount`, `Main.time`) are public. Net verdict: components 2, 3 and 5 are fully buildable on the public API; component 7's directory acquisition is `[needs-internals]`.

## The surface

| Fully-qualified member | Kind | What it does / why the profiler cares |
|---|---|---|
| `Terraria.ModLoader.Mod.Load` | method (override) | Runs after this mod's content is autoloaded; no world exists yet. Mod-wide one-time setup. Content lookup tables NOT yet populated. Profiler use: register config, prepare static state — **not** ring-buffer allocation (no world). |
| `Terraria.ModLoader.Mod.PostSetupContent` | method (override) | Runs after *all* mods' content is set up (arrays resized, IDs populated). Profiler use: enumerate the loaded modlist, build the mod-id ↔ assembly map, compute the modlist fingerprint. |
| `Terraria.ModLoader.Mod.Unload` | method (override) | Called when the mod is unloaded; mods unload in reverse load order. Undo anything `Load` did that tML will not auto-handle. Profiler use: final teardown safety net. |
| `Terraria.ModLoader.Mod.Close` | method (override) | Called *before* `Unload`, possibly multiple times, whenever unload is imminent (update download, recompile). Release file handles / stop streams. Must call `base.Close()`. Profiler use: ensure the Persistent Store's file handles are closed before a reload. |
| `Terraria.ModLoader.Mod.Logger` | property | An `ILog` named for the mod — the agent-surface logging channel (writes to `client.log`). Used for all lifecycle/milestone/teardown logging per the Dual-Surface contract. |
| `Terraria.ModLoader.Mod.File` | property | The `TmodFile` for *this* mod — the packed read-only `.tmod` archive. **NOT a writable data directory.** Do not use for the Persistent Store. |
| `Terraria.ModLoader.Mod.Name` | property | The mod's identifying name (folder name by default). Part of the modlist-fingerprint tuple (`mod-name + version`). |
| `Terraria.ModLoader.Mod.Version` | property | This mod's version. Combined with `Name` for the fingerprint and for the abort-clean version gate. |
| `Terraria.ModLoader.Mod.TModLoaderVersion` | property | The tML version the mod was built against. Useful input to Invariant 4's host-drift check. |
| `Terraria.ModLoader.Mod.Side` | property | The `ModSide` controlling client/server sync. Profiler is client-side telemetry; relevant for v2 multiplayer scoping only. |
| `Terraria.ModLoader.ModSystem.OnModLoad` | method (override) | Called right after `Mod.Load()`; content is autoloaded. Per-`ModSystem` one-time setup. |
| `Terraria.ModLoader.ModSystem.PostSetupContent` | method (override) | Per-`ModSystem` equivalent of `Mod.PostSetupContent` — content fully set up. Good place for the modlist scan if kept in a dedicated system. |
| `Terraria.ModLoader.ModSystem.OnWorldLoad` | method (override) | **Called whenever a world is loaded, before `LoadWorldData`.** The primary world-entry hook. **Ring-buffer allocation + detour install belong here.** |
| `Terraria.ModLoader.ModSystem.PostWorldLoad` | method (override) | Called after `LoadWorldData`; world is ready to be entered, all modded world data loaded. Single-player/server only — **not on multiplayer clients.** Secondary entry hook; use only for work needing loaded world data. |
| `Terraria.ModLoader.ModSystem.OnWorldUnload` | method (override) | **Called whenever a world is unloaded.** The primary world-exit hook. **Ring-buffer free + detour teardown belong here.** |
| `Terraria.ModLoader.ModSystem.ClearWorld` | method (override) | Called when the world is cleared (before world-gen or load, single + multiplayer) **and also just before mods are unloaded.** Reset world-related data structures here. |
| `Terraria.ModLoader.ModSystem.PreSaveAndQuit` | method (override) | **Called when the "Save and Quit" button is pressed.** Local client only. The positive signal of a *clean* session close — finalise the open encounter and flush the Persistent Store here. |
| `Terraria.ModLoader.ModSystem.SaveWorldData(TagCompound)` | method (override) | Save custom per-world data into the world file. Profiler use: optional — could stamp a session sentinel into the world file, but JSON-lines under a data dir is the chosen path. |
| `Terraria.ModLoader.ModSystem.LoadWorldData(TagCompound)` | method (override) | Load custom per-world data. Write defensive code. Profiler use: read back any world-file sentinel if that route is taken. |
| `Terraria.ModLoader.ModSystem.UpdateUI(GameTime)` | method (override) | Ran every update; intended for calling `Update` on `UserInterface` classes. All clients. First per-tick `ModSystem` hook in the sequence (fires before `PreUpdateEntities`). |
| `Terraria.ModLoader.ModSystem.PreUpdateEntities` | method (override) | Runs after UI updates, **before any World/Player/NPC/Projectile/Tile update.** **Only fires on full-update frames** (skipped on partial updates when `Main.autoPause` or `Main.FrameSkipMode` cause a partial tick). The canonical "tick start" boundary for the profiler. |
| `Terraria.ModLoader.ModSystem.PreUpdatePlayers` | method (override) | Before players update. All clients + server. |
| `Terraria.ModLoader.ModSystem.PostUpdatePlayers` | method (override) | After players update. |
| `Terraria.ModLoader.ModSystem.PreUpdateNPCs` / `PostUpdateNPCs` | method (override) | Before/after NPCs update. All clients + server. |
| `Terraria.ModLoader.ModSystem.PreUpdateProjectiles` / `PostUpdateProjectiles` | method (override) | Before/after projectiles update. |
| `Terraria.ModLoader.ModSystem.PreUpdateItems` / `PostUpdateItems` | method (override) | Before/after items update. |
| `Terraria.ModLoader.ModSystem.PreUpdateDusts` / `PostUpdateDusts` | method (override) | Before/after dusts update. |
| `Terraria.ModLoader.ModSystem.PreUpdateGores` / `PostUpdateGores` | method (override) | Before/after gores update. |
| `Terraria.ModLoader.ModSystem.PreUpdateInvasions` / `PostUpdateInvasions` | method (override) | Before/after invasions update. Single-player/server only. |
| `Terraria.ModLoader.ModSystem.PreUpdateWorld` / `PostUpdateWorld` | method (override) | Before/after world (tile/star/etc.) update. Single-player/server only. |
| `Terraria.ModLoader.ModSystem.PreUpdateTime` / `PostUpdateTime` | method (override) | Before/after time update. All clients + server. |
| `Terraria.ModLoader.ModSystem.PostUpdateInput` | method (override) | After input keys are polled. All clients. Useful for the F9-toggle capture relative to other input consumers. |
| `Terraria.ModLoader.ModSystem.PostUpdateEverything` | method (override) | **The last hook in an update**, after networking. All clients + server. The canonical "tick end" boundary — commit the `TickFrame` to the ring buffer here. |
| `Terraria.ModLoader.ModPlayer.Initialize` | method (override) | Called when the player is loaded (player-select screen). Initialise per-player data structures. |
| `Terraria.ModLoader.ModPlayer.OnEnterWorld` | method (override) | **Called when the local player enters the world.** Local client only. Companion signal to `OnWorldLoad`; the README's iteration loop names this as the re-fire point. Useful for the first-launch tutorial overlay reset. |
| `Terraria.ModLoader.ModPlayer.PreUpdate` / `PostUpdate` | method (override) | Per-player tick hooks. Local, server, remote. Engagement-tracking surface (out of this slice's scope; relevant to Context Tagger). |
| `Terraria.ModLoader.ModPlayer.OnRespawn` | method (override) | Called when a player respawns. Encounter Detector signal candidate (death → respawn boundary). |
| `Terraria.ModLoader.ModPlayer.PlayerConnect` / `PlayerDisconnect` | method (override) | Remote-client connect/disconnect. v2 multiplayer only. |
| `Terraria.Main.GameUpdateCount` | property | **Counts game updates since the world was loaded.** Updates even while paused; does NOT update on the main menus. The frame-number / tick-index source for `TickFrame`. Resets per world-load — a natural session-relative frame counter. |
| `Terraria.Main.time` | field | In-game time since last day/night flip, advanced by `Main.dayRate` per tick. Context Tagger / retrospective input, not a frame timer. |
| `Terraria.Main.dayTime` | field | Day vs night flag. Context input only. |
| `Terraria.Main.gameMenu` | field | **True when in the main menus, false when in a world.** The cleanest public guard for "are we in a session" — gates whether per-tick sampling should run at all. |
| `Terraria.ModLoader.MonoModHooks.Add(MethodBase, Delegate)` | method (static) | Adds a runtime `On.`-style hook to a method. tML tracks per-assembly detour ownership through this. Hook Interceptor surface (component 1, out of this slice) — but it is the API the detour install/teardown in `OnWorldLoad`/`OnWorldUnload` will call into. |
| `Terraria.ModLoader.MonoModHooks.Modify(MethodBase, ILContext.Manipulator)` | method (static) | Adds an IL hook. Same ownership tracking. The ILHook route the README's Lite mode names. |
| `Terraria.ModLoader.ModLoader.TryGetMod(String, out Mod)` | method (static) | Safely resolves a `Mod` by name. Modlist enumeration / fingerprint input. |
| `Terraria.ModLoader.ModLoader.GetMod(String)` | method (static) | Resolves a `Mod` by name (throws if absent). Prefer `TryGetMod`. |
| `Terraria.ModLoader.ModLoader.HasMod(String)` | method (static) | Existence check for a mod by name. |

## The per-tick update sequence

The XML documents each hook's relative position with phrases like "before anything in the World gets updated" and "the last hook that happens in an update". Assembled, the canonical full-update tick the profiler sees is:

```
  ── frame boundary ─────────────────────────────────────────────
  1. input poll            → ModSystem.PostUpdateInput        (all clients)
  2. UI update             → ModSystem.UpdateUI(GameTime)     (all clients)
  ─ TICK START for the profiler ─
  3. pre-world             → ModSystem.PreUpdateEntities      (full-update frames only)
  4. time                  → ModSystem.PreUpdateTime / PostUpdateTime
  5. players               → ModSystem.PreUpdatePlayers / PostUpdatePlayers
  6. NPCs                  → ModSystem.PreUpdateNPCs / PostUpdateNPCs
  7. gores                 → ModSystem.PreUpdateGores / PostUpdateGores
  8. projectiles           → ModSystem.PreUpdateProjectiles / PostUpdateProjectiles
  9. items                 → ModSystem.PreUpdateItems / PostUpdateItems
 10. dusts                 → ModSystem.PreUpdateDusts / PostUpdateDusts
 11. world (tiles/stars)   → ModSystem.PreUpdateWorld / PostUpdateWorld   (SP/server)
 12. invasions             → ModSystem.PreUpdateInvasions / PostUpdateInvasions (SP/server)
 13. network update
  ─ TICK END for the profiler ─
 14. final hook            → ModSystem.PostUpdateEverything    (all clients)
  ── frame boundary ─────────────────────────────────────────────
```

Notes and honest limits:

- **The exact ordering of steps 4–12 is `[partial]`.** Each hook's summary states *its own* "before/after X" placement, but the XML does not publish one canonical ordered list. The sequence above is assembled from individual summaries (`PreUpdateEntities` "before anything in the World"; `PostUpdateEverything` "the last hook ... after the Network got updated") and matches vanilla `Main.Update`'s known order. Treat the player/NPC/projectile/item interleave as the best-supported reading, not a guarantee — `NEEDS DECOMPILER VERIFICATION` against `Terraria.Main.Update` if exact ordering ever becomes load-bearing.
- **`PreUpdateEntities` and every hook after it only fire on full-update frames.** When `Main.autoPause` is true or `Main.FrameSkipMode` is 0 or 2, the game may run a *partial* update (menus/animations only). The profiler must treat a frame where `PreUpdateEntities` did not fire as "no tick sampled", not "a 0 ms tick" — otherwise partial frames pollute frame-time aggregates.
- **`UpdateUI` and `PostUpdateInput` fire on every update**, including partial ones. Drive the overlay's own refresh from `UpdateUI`; drive sampling from the `PreUpdateEntities` → `PostUpdateEverything` pair.
- Recommended profiler frame model: open the `TickFrame` in `PreUpdateEntities`, stamp it with `Main.GameUpdateCount`, commit it to the ring buffer in `PostUpdateEverything`. One frame = one `PreUpdateEntities`/`PostUpdateEverything` pair.

## Resource lifecycle plug-in points

### 1. Ring-buffer allocation (world entry) — `[public-API]`

`Terraria.ModLoader.ModSystem.OnWorldLoad` — fires on every world load, before `LoadWorldData`. Allocate the fixed-size `TickFrame[]` ring buffer and the pre-allocated `PerModSample[]` here. This pairs exactly with `OnWorldUnload`, satisfying Invariant 4's "installed at world-load, torn down at world-unload" ownership rule. `PostWorldLoad` is an alternative but is single-player/server only and runs later than necessary — `OnWorldLoad` is the correct choice.

### 2. Ring-buffer free (world exit) — `[public-API]`

`Terraria.ModLoader.ModSystem.OnWorldUnload` — fires on every world unload. Null out the ring buffer and per-mod aggregators here; the buffer is allocated once and freed once, with `OnWorldLoad`/`OnWorldUnload` as the single owner pair. `ClearWorld` also fires "just before mods are unloaded" and on every world clear — it is a *belt-and-braces* secondary teardown trigger, not the primary one (it fires too often, including before world-gen).

### 3. Detour install (world entry) — `[public-API]` for the call site, hook targets out of slice

`Terraria.ModLoader.ModSystem.OnWorldLoad` — install MonoMod detours via `Terraria.ModLoader.MonoModHooks.Add` / `MonoModHooks.Modify` from the same hook that allocates the ring buffer, so install and allocation share a lifecycle. The *targets* of those detours (the `*Loader.<HookName>` method bodies) are component 1's concern and are `[needs-internals]` — see the Hook Interceptor slice. The plug-in point and the detour API itself are public.

### 4. Detour teardown (world exit) — `[partial]`

`Terraria.ModLoader.ModSystem.OnWorldUnload` is the correct teardown hook, but the **disposal mechanics are `[partial]`**. The XML documents `MonoModHooks.Add` and `MonoModHooks.Modify` but does **not** expose a returned `Hook`/`ILHook` disposable handle or an `Undo`/`Remove` method in the public surface. The standard MonoMod pattern is that `Add`/`Modify` return a `Hook`/`ILHook` whose `.Dispose()` removes the detour — but that return type is not in this XML. `NEEDS DECOMPILER VERIFICATION`: confirm the return type of `MonoModHooks.Add`/`Modify` and whether tML auto-undoes a mod's detours on `Mod.Unload`. If detours are auto-undone on unload, world-exit teardown without a world-reload is still the profiler's responsibility for the Off-mode transition. Hold the handles from install and dispose them in `OnWorldUnload`.

### 5. Session open — `[public-API]`

`Terraria.ModLoader.ModSystem.OnWorldLoad` opens the session (a session = `world-load → save-and-exit`). Stamp the session with `Main.GameUpdateCount` (resets to 0 at world load — a natural session frame origin) and the modlist fingerprint computed at `PostSetupContent`. The Encounter Detector opens its first encounter window (world-load is itself an encounter trigger per the README) from the same hook.

### 6. Session clean-close detection — `[public-API]`

`Terraria.ModLoader.ModSystem.PreSaveAndQuit` — the positive signal of a clean close, fired when "Save and Quit" is pressed (local client only). Finalise the open encounter, flush the Persistent Store, and **clear the dirty-exit sentinel** (see point 7) here. `OnWorldUnload` also fires on a clean exit but fires on *every* world transition; `PreSaveAndQuit` is the specific "user chose to leave cleanly" signal. Recommended: treat `PreSaveAndQuit` as the clean-finalise hook and `OnWorldUnload` as the unconditional resource-free hook — they are different jobs.

### 7. Crash / dirty-exit detection — `[partial]`, by construction

There is **no hook that fires on a crash** — that is the nature of a crash. Dirty-exit detection must be reconstructed from the *absence* of a clean signal:

- On `OnWorldLoad`, write a sentinel file (e.g. `.../PerformanceProfiler/sessions/<id>.inprogress`) and begin the JSON-lines session file.
- On `PreSaveAndQuit` (clean close), finalise the session file and delete the sentinel.
- On the *next* `Mod.Load` or `OnWorldLoad`, scan for any orphaned `.inprogress` sentinel: its presence means the previous session crashed. Rewrite that session's JSON-lines file with `incomplete: true` so it cannot poison aggregates.

This is `[partial]` because the API gives no positive crash hook — the recovery pattern is sound but is the profiler's own construction, not a tML-provided affordance. `Mod.Close` (called before `Unload`, possibly on update/recompile) is a *graceful-shutdown-imminent* signal and should also flush + clear the sentinel, but it does not cover a hard process crash.

### 8. Save-path acquisition — `[needs-internals]`

**This is the one genuine API gap in this slice.** The README targets `~/Library/Application Support/Terraria/tModLoader/PerformanceProfiler/` for the JSON-lines store. The public XML provides **no member for a writable per-mod data directory**:

- `Mod.File` is the `TmodFile` — the packed, read-only `.tmod` archive. Not writable, not a directory.
- `Mod.SourceFolder` exists but is the dev-time source directory (only meaningful when building from `ModSources/`), not a runtime data dir for Workshop-installed players.
- No `SavePath`, `SaveLocation`, `SaveDir`, or per-mod data-directory member appears anywhere in the XML (searched `T:`/`M:`/`P:`/`F:` across all namespaces).

`NEEDS DECOMPILER VERIFICATION`: the tML save root is internally `Terraria.Main.SavePath` (a static string, e.g. `.../Library/Application Support/Terraria/tModLoader`) — this field is **not in the public XML** and must be confirmed by decompiling `Terraria.Main`. Options for the Persistent Store, in order of preference:

1. Read the internal `Main.SavePath` via reflection (with an abort-clean guard if the field is missing — consistent with Invariant 4) and append `PerformanceProfiler/`.
2. Use the well-known platform path directly (`Environment.SpecialFolder.ApplicationData` → `Terraria/tModLoader/`), accepting that tML's `-savedirectory` launch override would be missed.
3. `ModConfig` (`Terraria.ModLoader.Config.ModConfig`) *does* persist to disk via tML — but it is config, not an append-only JSON-lines log, so it is unsuitable for the session store.

Recommendation: option 1 with a reflection probe at `Mod.Load`, failing clean to option 2, and logging which path was resolved to `client.log`. Resolve and verify this before Milestone 3 (Persistence) starts.

## Invariant checks

**Invariant 1 — Read-only.** Every hook in this slice is an *observation* point. `OnWorldLoad`, `OnWorldUnload`, `PreSaveAndQuit`, and the per-tick `Pre/PostUpdate*` hooks can all be overridden with bodies that only read `Main` state and write to the profiler's own ring buffer — no game/world/save mutation. The two write-capable hooks, `SaveWorldData` and `LoadWorldData`, should be **left unimplemented**: the profiler writes to its own JSON-lines files, not the world file, so it never touches another mod's or the game's save data. `MonoModHooks.Modify`/`Add` *can* mutate behaviour, but the Hook Interceptor's contract (component 1) is to install timing-only detours that call `orig()` unchanged — that discipline is enforced in that slice, not here.

**Invariant 2 — Overhead budget / zero-allocation hot path.** The hot path is the `PreUpdateEntities` → `PostUpdateEverything` pair. The `TickFrame` and `PerModSample[]` are allocated once in `OnWorldLoad` (point 1) and reused; committing a frame to the ring buffer is an index write, no allocation. **Do not call `Mod.Logger` from any per-tick hook** — logging is for load/world/encounter boundaries only (CLAUDE.md Dual-Surface rule). `Main.GameUpdateCount` is a property read (cheap, no alloc). The partial-frame guard (only sample when `PreUpdateEntities` fired) is itself a branch, not an allocation. The per-tick path stays allocation-free with these hooks as the boundaries.

**Invariant 4 — Clean ownership / abort-clean.** `OnWorldLoad`/`OnWorldUnload` form a single, symmetric owner pair for both the ring buffer and the detours — allocation and free, install and teardown, each in exactly one place. `Mod.Close` + `Mod.Unload` provide the mod-level teardown net. The detour-disposal-handle uncertainty (point 4) and the `Main.SavePath` reflection probe (point 8) are both places where a tML host-drift could break the profiler; both must fail clean — disable instrumentation, log to `client.log`, never proceed against an internal that no longer matches.

## Coverage verdict

| Component | Buildable on documented public API? | Gap |
|---|---|---|
| **2 — Per-Tick Metric Collector** | **Yes, fully.** | Frame boundaries (`PreUpdateEntities`/`PostUpdateEverything`), frame number (`Main.GameUpdateCount`), and the in-session guard (`Main.gameMenu`) are all public. The metric *values* (per-mod CPU) come from component 1's detours — out of this slice. |
| **3 — Ring Buffer** | **Yes, fully.** | `OnWorldLoad`/`OnWorldUnload` give a clean, paired once-allocate/once-free lifecycle entirely on the public API. |
| **5 — Encounter Detector** | **Yes, for the lifecycle triggers.** | World-load encounter open (`OnWorldLoad`), clean session close (`PreSaveAndQuit`), respawn boundary (`ModPlayer.OnRespawn`) are public. Boss-spawn and biome-change triggers are *content* signals belonging to the Context Tagger slice — not assessed here. |
| **7 — Persistent Store** | **Mostly — one blocking gap.** | The *when* (write on `PreSaveAndQuit`, flush on `Mod.Close`, recover on next `Mod.Load`) is fully public. The *where* — the writable per-mod data directory — is **`[needs-internals]`**: no public save-path member exists. Crash detection is a sentinel-file pattern of the profiler's own construction (`[partial]`). |

**Bottom line:** roughly 90% of this slice is buildable on the documented public API alone. The two items that are not — the `Main.SavePath` resolution for the Persistent Store, and the `MonoModHooks` disposal-handle return type for detour teardown — are both small, both decompiler-verifiable, and both have abort-clean fallbacks consistent with Invariant 4. Neither blocks Milestone 1 (Lite-mode MVP, in-memory only); both must be resolved before Milestone 3 (Persistence).

## Open questions / NEEDS DECOMPILER VERIFICATION

1. **`Main.SavePath` (or equivalent).** `[needs-internals]` Confirm the internal static field/property on `Terraria.Main` that holds the tML save root, its exact type and name, and whether it already includes the `tModLoader` segment. Confirm it honours the `-savedirectory` launch argument. This is the directory the Persistent Store appends `PerformanceProfiler/` to. Public XML has no save-path member at all.
2. **`MonoModHooks.Add` / `Modify` return type.** `[partial]` The XML documents the methods but not their return values. Confirm whether they return a `Hook`/`ILHook` (MonoMod's `RuntimeDetour`) disposable handle, and whether tML auto-disposes a mod's detours on `Mod.Unload`/world-unload. Detour teardown in `OnWorldUnload` depends on this.
3. **Exact ordering of `PreUpdate*`/`PostUpdate*` interleave.** `[partial]` Each hook documents its own relative position, but no single canonical ordered list is published. The sequence in this doc is assembled from individual summaries and matches known vanilla `Main.Update` order. Verify against `Terraria.Main.Update` only if exact step-4-to-12 ordering becomes load-bearing for attribution.
4. **Partial-update frame frequency.** `[public-API], behavioural` `PreUpdateEntities` is documented to skip on partial updates (`Main.autoPause`, `FrameSkipMode` 0/2). Confirm empirically how often partial frames occur during normal play so the "no tick sampled" path is exercised and does not silently drop a meaningful fraction of frames from aggregates.
5. **`Mod.Close` call multiplicity.** `[public-API]` The summary says `Close` "may be called multiple times before Unload." The Persistent Store flush in `Close` must therefore be idempotent — flushing an already-flushed/closed session must be a safe no-op.
6. **`GameUpdateCount` and the F9-overlay-while-paused case.** `[public-API]` `GameUpdateCount` "updates even while gameplay is paused." Confirm whether `PreUpdateEntities` fires while the game is paused via the in-game pause (vs `autoPause`); if it does, paused frames would be sampled as real ticks and should be tagged or excluded by the Context Tagger.

---

## How we plug in (post-implementation status, 2026-05-20)

The 2026-05-19 analysis flagged the save-path resolution and the detour disposal handle as the two remaining `NEEDS DECOMPILER VERIFICATION` items. Both are resolved.

### Save path

`ProfilerPaths.Root()` resolves the per-mod data folder under tModLoader's per-platform save dir (the LiteDB file, redo journal, and rotating backups live there). The `Main.SavePath` reflection probe described in the 2026-05-19 analysis was not strictly required because the platform path is reachable without it. (The deleted v0.2 JSON writer used `SessionLogWriter.SessionDirectory()`; that path is gone since v0.3.)

A `-savedirectory` launch override would be missed by the platform-path fallback, but is acceptable for the Workshop release. If a player surfaces a complaint, the fix is the `Main.SavePath` reflection probe with the abort-clean guard described in the 2026-05-19 analysis (still valid).

### Detour disposal

Resolved by sidestepping `MonoModHooks.Modify`. See `monomod-detours.md`'s post-implementation note; the IL backend constructs `new ILHook(...)` directly so disposal is `ILHook.Dispose()` on a reference we hold. The delegate backend uses `MonoModHooks.Add`, whose detours tModLoader auto-removes on mod unload — no explicit disposal needed.

### The actual lifecycle wiring

`ProfilerSystem : ModSystem` (`Profiling/ProfilerSystem.cs`) owns the world-scope half:

| Hook | What we do |
|------|-----------|
| `PostSetupContent` | `Time.Reset` + `LangNameCache.Populate` + `SelfHealth.MarkInstallStart` + `HookInterceptor.Install` + `ILHookInterceptor.Install` (if active) + `SelfHealth.MarkInstallEnd` + `BiomeRegistry.Populate` + `SubworldProbe.Initialise` + `ModRosterScanner.Scan` (F1) |
| `OnWorldLoad` | sets `_deferredInitPending = true` and returns (the heavy construction is deferred to the first `PostUpdateEverything` to avoid a world-enter freeze) |
| deferred init (first `PostUpdateEverything`) | new `MetricCollector(1800, SelfHealth)` + try-build `SessionRecorder` (LiteDB) + watchers (`ContextTransitionWatcher`, `WorldSnapshotter`, `PlayerDeathDetector`) + `ContextTagger` + `EventAggregator` + `SegmentDetector`/`SegmentStore` + `DataRegistry.Shared.InitialiseAll` + `Freeze` |
| `PreUpdateEntities` | `Collector?.BeginTick()` |
| `PostUpdateEverything` | `Collector.EndTick(...)` + divergence log + `_recorder?.OnTick(...)` (try/catch self-disable) + `_contextTagger.Snapshot` + `Events.Accumulate` + `SegmentDetector.OnTick` + drive `DataRegistry.PerTickCallbacks` + off-thread `InsightsEngine.Evaluate` (~every 60 ticks) |
| `PreSaveAndQuit` | kicks off the async session-end aggregation so it overlaps vanilla's save+backup chain |
| `OnWorldUnload` | idempotent session-end kickoff (if `PreSaveAndQuit` did not) + `SegmentDetector.CloseAllOnShutdown` + null everything + `InsightsEngine.Shared = null` + `BossSampler.Clear()` + `SubworldProbe.Clear()` + `DataRegistry.Shared.ResetAll()` |

`PerformanceProfiler : Mod` (`PerformanceProfiler.cs`) owns the mod-scope half:

| Hook | What we do |
|------|-----------|
| `Mod.Load` | `RegisterDataPipeline()` + open the LiteDB `Database` (degrade to null on failure) + `LegacyJsonImporter.RunOnceIfNeeded` + bind the loopback `Dashboard` HTTP server (degrade to null on bind failure) + `Logger.Info` (agent surface) |
| `Mod.Unload` | `ILHookInterceptor.Uninstall()` (explicit, before assembly unload — the load-bearing teardown) + `DataRegistry.Shared.DisposeAll()` + dispose `Dashboard` then `Database` (order matters: neither a stream nor the route handler may touch a half-disposed DB) |

`ProfilerPlayer : ModPlayer` (same file) owns the gameplay-input half:

| Hook | What we do |
|------|-----------|
| `OnEnterWorld` | `Main.NewText("Press F9 for the dashboard (URL)")` — chat is cleared during the world-load transition, so this must NOT be in `OnWorldLoad` |
| `ProcessTriggers` | poll `ProfilerOverlaySystem.DashboardKeybind.JustPressed` → launch the default browser at the loopback dashboard URL (`open`/`xdg-open`/shell). The keybind is registered as `"OpenDashboard"`. |

### IO failure self-disable

The `try/catch (IOException, UnauthorizedAccessException, SecurityException)` wrapping around `SessionRecorder` construction (deferred world-load init) and the per-tick `try/catch` at `PostUpdateEverything` around `SessionRecorder.OnTick` form the abort-clean envelope for the persistence subsystem. A failure on any of these paths sets `_recorder = null` for the rest of the world; metric collection and the live dashboard continue regardless. Invariant 4 satisfied. The legacy JSON `SessionLogWriter` it replaced was deleted in v0.3. See `systems/persistence.md` for the LiteDB design and crash-safety stack.

### Canonical home

`systems/mod-lifecycle.md` carries the implementation reality; `systems/persistence.md` carries the persistence half; `systems/web-dashboard.md` carries the browser surface.
