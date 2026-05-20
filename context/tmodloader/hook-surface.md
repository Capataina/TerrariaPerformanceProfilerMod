# tModLoader Integration Surface — Per-Tick Hook Surface

> Source: tModLoader.xml (tModLoader 1.4.4, build ~#5089). Serves components: Hook Interceptor (1), Per-Tick Metric Collector (2).

## Summary

tModLoader's per-tick modding API splits cleanly into two layers. The **public layer** is the set of overridable hook methods on `ModSystem`, `ModPlayer`, `GlobalNPC`, `GlobalProjectile`, `GlobalItem` and `PlayerDrawLayer` — fully documented in `tModLoader.xml`, easy to enumerate, but a profiler can only detour them *on its own subclass*, not on every loaded mod's subclass. The **internal layer** is the per-loader dispatch loop (`SystemLoader`, `NPCLoader`, `PlayerLoader`, `ProjectileLoader`, plus `HookList`) that iterates every mod's implementations; this is where per-mod cost attribution actually becomes measurable, and it is almost entirely **absent from the XML** — only `ItemLoader` exposes its dispatch methods with doc comments, the other four loaders do not. The decisive consequence: the Hook Interceptor cannot be built from the public hook surface alone for Lite mode; it must ILHook internal `*Loader` dispatch methods obtained by reflection, and that reflection target set needs decompiler verification and abort-clean signature guards.

## The surface

All members below are per-tick or per-draw-frame and would attribute CPU to the mod implementing them. Frequency is for the **client** (the profiler's deployment target; v1 is single-player per README). "Per tick" = once per 60 Hz game update; "per draw" = once per rendered frame, which can differ from tick rate under frame-skip. Entity hooks marked "per entity per tick" run once for *every active instance* — the dominant cost multiplier on a heavy modlist.

### ModSystem — once per tick / per draw frame

| Fully-qualified member | Kind | Frequency | What it does / why the profiler cares |
|---|---|---|---|
| `M:Terraria.ModLoader.ModSystem.PreUpdateEntities` | hook (override) | 1×/tick (full-update frames only) | First world-update hook. Summary explicitly notes it and every later hook fire only on full-update frames — a natural "tick boundary" marker for the Ring Buffer. |
| `M:Terraria.ModLoader.ModSystem.PreUpdatePlayers` / `PostUpdatePlayers` | hook | 1×/tick | Brackets the player-update phase. |
| `M:Terraria.ModLoader.ModSystem.PreUpdateNPCs` / `PostUpdateNPCs` | hook | 1×/tick | Brackets the NPC-update phase. |
| `M:Terraria.ModLoader.ModSystem.PreUpdateGores` / `PostUpdateGores` | hook | 1×/tick | Brackets gore updates. |
| `M:Terraria.ModLoader.ModSystem.PreUpdateProjectiles` / `PostUpdateProjectiles` | hook | 1×/tick | Brackets the projectile-update phase. |
| `M:Terraria.ModLoader.ModSystem.PreUpdateItems` / `PostUpdateItems` | hook | 1×/tick | Brackets world-item updates. |
| `M:Terraria.ModLoader.ModSystem.PreUpdateDusts` / `PostUpdateDusts` | hook | 1×/tick | Brackets dust updates. |
| `M:Terraria.ModLoader.ModSystem.PreUpdateInvasions` / `PostUpdateInvasions` | hook | 1×/tick (SP/server only) | Invasion update phase. |
| `M:Terraria.ModLoader.ModSystem.PreUpdateTime` / `PostUpdateTime` | hook | 1×/tick | Time-of-day update. |
| `M:Terraria.ModLoader.ModSystem.PreUpdateWorld` / `PostUpdateWorld` | hook | 1×/tick (SP/server only) | General world simulation; named cost driver per the README's biome/world attribution. |
| `M:Terraria.ModLoader.ModSystem.PostUpdateEverything` | hook | 1×/tick | Explicitly "the last hook in an update" — the canonical place to close the per-tick `TickFrame` and commit it to the Ring Buffer. |
| `M:Terraria.ModLoader.ModSystem.PostUpdateInput` | hook | 1×/tick | After input poll. |
| `M:Terraria.ModLoader.ModSystem.UpdateUI(Microsoft.Xna.Framework.GameTime)` | hook | 1×/update | "Ran every update", for `UserInterface.Update`. Profiler's *own* UI Renderer also lives here. |
| `M:Terraria.ModLoader.ModSystem.ModifyInterfaceLayers(System.Collections.Generic.List{Terraria.UI.GameInterfaceLayer})` | hook | 1×/draw frame | Mods inject `GameInterfaceLayer`s here; the profiler's F9 overlay registers its own layer here. |
| `M:Terraria.ModLoader.ModSystem.PostDrawTiles` | hook | 1×/draw frame | After tile draw. |
| `M:Terraria.ModLoader.ModSystem.PostDrawInterface(Microsoft.Xna.Framework.Graphics.SpriteBatch)` | hook | 1×/draw frame | Legacy interface draw (XML flags it deprecated in favour of `ModifyInterfaceLayers`). |
| `M:Terraria.ModLoader.ModSystem.PostDrawFullscreenMap(System.String@)` | hook | 1×/draw frame while map open | Fullscreen-map custom draw. |
| `M:Terraria.ModLoader.ModSystem.PreDrawMapIconOverlay(...)` | hook | 1×/draw frame | Map-icon overlay phase. |
| `M:Terraria.ModLoader.ModSystem.ModifyScreenPosition` / `ModifyTransformMatrix` | hook | 1×/draw frame | Camera/transform; cheap but per-frame. |
| `M:Terraria.ModLoader.ModSystem.ModifySunLightColor` / `ModifyLightingBrightness` / `ModifyTimeRate` | hook | 1×/tick or per-frame | Lighting/time modifiers; per-frame cost. |
| `M:Terraria.ModLoader.ModSystem.ResetNearbyTileEffects` | hook | 1×/tick | Tile-effect reset. |

### ModPlayer — once per tick per local player (1× client, single-player)

| Fully-qualified member | Kind | Frequency | What it does / why the profiler cares |
|---|---|---|---|
| `M:Terraria.ModLoader.ModPlayer.PreUpdate` | hook | 1×/tick/player | "Beginning of every tick update for this player." |
| `M:Terraria.ModLoader.ModPlayer.PostUpdate` | hook | 1×/tick/player | "Very end of `Player.Update`." |
| `M:Terraria.ModLoader.ModPlayer.ResetEffects` | hook | 1×/tick/player | Per-tick field reset; nearly every content mod implements this — high aggregate cost. |
| `M:Terraria.ModLoader.ModPlayer.UpdateDead` | hook | 1×/tick/player when dead | Replaces `ResetEffects` while dead. |
| `M:Terraria.ModLoader.ModPlayer.PreUpdateBuffs` / `PostUpdateBuffs` | hook | 1×/tick/player | Buff-update phase. |
| `M:Terraria.ModLoader.ModPlayer.UpdateEquips` | hook | 1×/tick/player | After accessory update. |
| `M:Terraria.ModLoader.ModPlayer.PostUpdateEquips` | hook | 1×/tick/player | After equipment/armour-set update — heavy accessory mods concentrate here. |
| `M:Terraria.ModLoader.ModPlayer.PostUpdateMiscEffects` | hook | 1×/tick/player | Misc effect phase. |
| `M:Terraria.ModLoader.ModPlayer.PreUpdateMovement` | hook | 1×/tick/player | Before velocity → position. |
| `M:Terraria.ModLoader.ModPlayer.PostUpdateRunSpeeds` | hook | 1×/tick/player | After speed modifiers. |
| `M:Terraria.ModLoader.ModPlayer.UpdateLifeRegen` / `UpdateBadLifeRegen` | hook | 1×/tick/player | Life-regen / DoT computation. |
| `M:Terraria.ModLoader.ModPlayer.NaturalLifeRegen(System.Single@)` | hook | 1×/tick/player | Regen-power multiplier. |
| `M:Terraria.ModLoader.ModPlayer.SetControls` | hook | 1×/tick/player (local only) | Control-input modification. |
| `M:Terraria.ModLoader.ModPlayer.ProcessTriggers(Terraria.GameInput.TriggersSet)` | hook | 1×/tick/player (local only) | Keybind polling — gameplay frames only. |
| `M:Terraria.ModLoader.ModPlayer.FrameEffects` | hook | 1×/draw frame/player | Visual armour/accessory framing + dust. |
| `M:Terraria.ModLoader.ModPlayer.UpdateDyes` | hook | multiple/frame | XML: "called in `Player.UpdateDyes()`, including selection screen." |
| `M:Terraria.ModLoader.ModPlayer.DrawEffects` / `ModifyDrawInfo` / `HideDrawLayers` | hook | 1×+/draw frame/player | Draw-pipeline hooks; can fire multiple times per frame for afterimages (see `PlayerDrawLayer.Draw`). |

### GlobalNPC / GlobalProjectile — once per entity per tick (the cost multiplier)

| Fully-qualified member | Kind | Frequency | What it does / why the profiler cares |
|---|---|---|---|
| `M:Terraria.ModLoader.GlobalNPC.PreAI(Terraria.NPC)` | hook | per active NPC per tick | Runs before vanilla AI for *every* NPC; `false` skips vanilla AI + `AI`. |
| `M:Terraria.ModLoader.GlobalNPC.AI(Terraria.NPC)` | hook | per active NPC per tick | Main per-NPC AI hook; only if `PreAI` returned true. |
| `M:Terraria.ModLoader.GlobalNPC.PostAI(Terraria.NPC)` | hook | per active NPC per tick | Always runs. AI trio is a primary boss-fight cost driver (README's Cryogen example). |
| `M:Terraria.ModLoader.GlobalNPC.UpdateLifeRegen(Terraria.NPC,System.Int32@)` | hook | per active NPC per tick | NPC DoT/regen. |
| `M:Terraria.ModLoader.GlobalNPC.ResetEffects(Terraria.NPC)` | hook | per active NPC per tick | Per-NPC field reset. |
| `M:Terraria.ModLoader.GlobalNPC.FindFrame(Terraria.NPC,System.Int32)` | hook | per visible NPC per draw frame | Animation framing. |
| `M:Terraria.ModLoader.GlobalNPC.PreDraw` / `PostDraw` / `DrawEffects` | hook | per visible NPC per draw frame | NPC draw pipeline; afterimage/visual cost. |
| `M:Terraria.ModLoader.GlobalProjectile.PreAI(Terraria.Projectile)` | hook | per active projectile per tick | Before vanilla projectile AI. With 84,222 projectiles/session in the README mockup, this is the highest-frequency hook class. |
| `M:Terraria.ModLoader.GlobalProjectile.AI(Terraria.Projectile)` | hook | per active projectile per tick | Main per-projectile AI hook. |
| `M:Terraria.ModLoader.GlobalProjectile.PostAI(Terraria.Projectile)` | hook | per active projectile per tick | Always runs. |
| `M:Terraria.ModLoader.GlobalProjectile.ShouldUpdatePosition(Terraria.Projectile)` | hook | per active projectile per tick | Position-update gate. |
| `M:Terraria.ModLoader.GlobalProjectile.PreDraw` / `PostDraw` / `PreDrawExtras` | hook | per visible projectile per draw frame | Projectile draw pipeline. |
| `M:Terraria.ModLoader.GlobalProjectile.Colliding` / `CanHitNPC` / `CanHitPlayer` | hook | per projectile per tick (conditional) | Collision phase; fires when the projectile is in a collision check. |

### GlobalItem — per-item-ish hooks

| Fully-qualified member | Kind | Frequency | What it does / why the profiler cares |
|---|---|---|---|
| `M:Terraria.ModLoader.GlobalItem.UpdateInventory(Terraria.Item,Terraria.Player)` | hook | per inventory slot per tick | Runs for every item in the player's inventory each tick (~50+ slots). |
| `M:Terraria.ModLoader.GlobalItem.Update(Terraria.Item,System.Single@,System.Single@)` | hook | per world item per tick | World-item movement; skipped while grabbed. |
| `M:Terraria.ModLoader.GlobalItem.PostUpdate(Terraria.Item)` | hook | per world item per tick | Always runs for world items (light, ageing). |
| `M:Terraria.ModLoader.GlobalItem.HoldItem(Terraria.Item,Terraria.Player)` | hook | 1×/tick/player | Held-item effects. |
| `M:Terraria.ModLoader.GlobalItem.UpdateInfoAccessory(Terraria.Item,Terraria.Player)` | hook | per info-accessory slot per tick | Info-accessory phase. |
| `M:Terraria.ModLoader.GlobalItem.UpdateAccessory(Terraria.Item,Terraria.Player,System.Boolean)` | hook | per equipped accessory per tick | Accessory effects — the README's Dormant-cost "300+ accessories" surface. |
| `M:Terraria.ModLoader.GlobalItem.UpdateEquip(Terraria.Item,Terraria.Player)` | hook | per equipped item per tick | Armour/accessory stat effects. |

### Draw layers

| Fully-qualified member | Kind | Frequency | What it does / why the profiler cares |
|---|---|---|---|
| `M:Terraria.ModLoader.PlayerDrawLayer.Draw(Terraria.DataStructures.PlayerDrawSet@)` | hook (override) | 1×+/draw frame/player | XML: "called multiple times a frame if a player afterimage is being drawn." Per-layer player-render cost. |
| `M:Terraria.ModLoader.PlayerDrawLayer.GetDefaultVisibility(...)` | hook | per layer per frame | Visibility resolution. |
| `M:Terraria.ModLoader.PlayerDrawLayerLoader.GetDrawLayers(...)` | dispatch | 1×/draw frame/player | Builds the ordered draw-layer list; XML explicitly warns "not threadsafe". |

## Plug-in points

Each point is how the **Hook Interceptor** would obtain a detour target and how the **Metric Collector** would attribute the timing.

1. **`MonoModHooks.Modify(System.Reflection.MethodBase, MonoMod.Cil.ILContext.Manipulator)`** — `[public-API]`. The IL-detour primitive. Takes a `MethodBase`, so it can hook *any* method the profiler can reach by reflection, public or `internal`. This is the load-bearing public API for the entire Hook Interceptor: it does not restrict targets to documented members. Per-tick implication: the IL manipulation happens once at install time (world-load), not per tick — zero hot-path cost from installation itself. The injected timing IL is the per-tick cost and must be measured against the budget.

2. **`MonoModHooks.Add(System.Reflection.MethodBase, System.Delegate)`** — `[public-API]`. The On-hook (detour) primitive — wraps a method with a delegate that receives an `orig` callback. Usable for per-`(GlobalType, hookMethod)` Standard/Deep detours where the profiler wraps a specific mod's hook override. Per-tick implication: one delegate invocation + `orig` call per wrapped method per tick; heavier than an inlined IL `Stopwatch` read, hence README scopes it to Standard/Deep only.

3. **Internal `*Loader` per-loader dispatch methods (e.g. `SystemLoader.PostUpdateEverything`, `NPCLoader.NPCAI`, `PlayerLoader.PreUpdate`, `ProjectileLoader.ProjectileAI`)** — `[needs-internals]`. These are the methods that contain the `foreach` over every mod's hook implementations. ILHooking *one* of these per hook name and timing the loop body per-iteration is exactly the README's Lite-mode "foreach-level aggregation" technique. **Only `ItemLoader`'s dispatch methods appear in `tModLoader.xml`** (`ItemLoader.UpdateInventory`, `ItemLoader.PostUpdate`, `ItemLoader.UpdateEquip`, etc., each documented as "Calls ModItem.X and all GlobalItem.X hooks"). `SystemLoader`, `NPCLoader`, `PlayerLoader`, `ProjectileLoader` expose only stray utility members (`NPCLoader.GetNPC`, `SystemLoader.EnsureResizeArraysAttributeStaticCtorsRun`) — their per-tick dispatch loops are not documented and must be located by decompiler. Per-tick implication: this is the entire Lite-mode hot path; the injected per-iteration `Stopwatch` delta is the budgeted overhead.

4. **`HookList` / `GlobalHookList` (the dispatch enumerator)** — `[needs-internals]`. The README explicitly names `HookList.Enumerate` as the foreach the profiler times. `HookList` does **not appear anywhere in `tModLoader.xml`** — zero members. Whether the Lite-mode detour target is the `*Loader.<Hook>` method body or `HookList.Enumerate`/its enumerator cannot be decided from the public docs. Per-tick implication: defines where exactly the timing IL is injected; unresolved until decompiled.

5. **`ModSystem` update/draw overrides (`PostUpdateEverything`, `PreUpdateEntities`, `UpdateUI`, `ModifyInterfaceLayers`, ...)** — `[public-API]`. The profiler overrides these *on its own `ModSystem` subclass* for its own lifecycle: `PreUpdateEntities` to open a `TickFrame`, `PostUpdateEverything` to close and ring-buffer it, `UpdateUI`/`ModifyInterfaceLayers` to drive the F9 overlay. Fully public and documented. Per-tick implication: the profiler's own per-tick bookkeeping cost — counts against its overhead budget and must itself be measured (the README's "profiles itself" requirement). This point does **not** give per-mod attribution of *other* mods; it only frames the profiler's own tick.

6. **`Global*` per-entity hooks as Standard/Deep detour targets** — `[partial]`. For per-`(GlobalType, hookMethod)` attribution, the profiler enumerates loaded mods' `GlobalNPC`/`GlobalProjectile`/`GlobalItem` subclasses (reachable via tModLoader's mod-content reflection) and detours each subclass's overridden `AI`/`PostAI`/`UpdateInventory`/etc. The hook *contract* (names, signatures, semantics) is fully public; obtaining the concrete `MethodInfo` for *another mod's* override and confirming the dispatch still routes through it requires reflection plus internals knowledge. Per-tick implication: one detour per `(mod, hookMethod)` pair, each adding a delegate frame per entity per tick — the Standard/Deep budget tier.

7. **`Mod.Logger` (`P:Terraria.ModLoader.Mod.Logger`)** — `[public-API]`. Not a per-tick hook, but the agent-surface obligation from CLAUDE.md. Used at install/teardown and encounter boundaries only — never per tick (Invariant 2).

## The public-hook vs internal-dispatch boundary

This is the central feasibility question for the Hook Interceptor. The boundary sits **exactly between point 5 and point 3 above**.

```
  ┌─────────────────────── ONE GAME TICK ────────────────────────────┐
  │  Terraria.Main.Update                                             │
  │     │                                                             │
  │     ▼                                                             │
  │  SystemLoader.PostUpdateEverything()      ◄── INTERNAL dispatch    │
  │     │   foreach (var system in HookList)  ◄── the loop to time     │
  │     │   {                                                         │
  │     │       system.PostUpdateEverything() ◄── PUBLIC override      │
  │     │   }                                     (one mod's code)     │
  │     ▼                                                             │
  │  NPCLoader.NPCAI(npc)                     ◄── INTERNAL dispatch    │
  │     │   foreach (var g in HookList)                                │
  │     │       g.AI(npc)                     ◄── PUBLIC override      │
  └───────────────────────────────────────────────────────────────────┘
```

**The public surface ends at the overridable method.** `ModSystem.PostUpdateEverything`, `GlobalNPC.AI`, `ModPlayer.PreUpdate` are `abstract`/`virtual` members the profiler can *override on its own subclass*. Overriding a method affects only the instance of the class doing the overriding. A profiler's `ModSystem` subclass overriding `PostUpdateEverything` measures **the profiler's own** `PostUpdateEverything`, and nothing else. There is no public API to "override `GlobalNPC.AI` on Calamity's `GlobalNPC`."

**Per-mod attribution requires intercepting the dispatch loop.** To time *Calamity's* `GlobalNPC.AI`, the profiler must measure the call site — the `foreach` body inside `NPCLoader.NPCAI` (or inside the `HookList` enumerator) — and read `MethodBase.DeclaringType.Assembly` on the delegate being invoked to credit the correct mod (the README's per-mod identity mechanism). That call site is **internal** tModLoader code.

Two viable strategies, and which one the profiler can build determines feasibility:

| Strategy | Detour target | Attribution mechanism | Coverage in XML | Tag |
|---|---|---|---|---|
| **A — ILHook the loader dispatch method** (Lite mode) | `*Loader.<HookName>` method body (e.g. `NPCLoader.NPCAI`) | Inject `Stopwatch` IL around the per-iteration call; map iteration → mod assembly | `ItemLoader.*` documented; `SystemLoader`/`NPCLoader`/`PlayerLoader`/`ProjectileLoader` **not** documented | `[needs-internals]` |
| **B — Detour each mod's `Global*` override** (Standard/Deep) | The concrete `MethodInfo` of another mod's `GlobalNPC.AI` etc., resolved by reflection | `MonoModHooks.Add` wraps it; `DeclaringType.Assembly` is the mod | Hook *contract* public; concrete target resolution needs reflection over internal content registries | `[partial]` |

**Verdict on the boundary.** Lite mode as specified in the README — "time each `HookList.Enumerate` foreach body once" — **cannot be built on the documented public API**. It depends on `*Loader` dispatch internals that are mostly absent from `tModLoader.xml` and on `HookList`, which is entirely absent. `MonoModHooks.Modify` (public) is the *tool*, but the *targets* it must be pointed at are internal. Strategy B (Standard/Deep) is closer to the public surface — the hook names and signatures are documented — but still needs reflection over tModLoader's internal mod-content registries to enumerate other mods' `Global*` instances, so it is `[partial]`, not clean public API. The single most important finding: **the Hook Interceptor is fundamentally an internals-dependent component; the public API supplies the detour primitive and the hook contract, but not the dispatch-loop targets that make per-mod attribution possible.**

## Invariant checks

**Invariant 1 — read-only.** IL detours that only inject `Stopwatch.GetTimestamp()` reads, accumulate into pre-allocated structs, and call `orig` unconditionally are read-only: they observe the dispatch loop without altering control flow, arguments, or return values. The risk surface is a *buggy* manipulator that drops the `orig` call or mutates a `ref`/`out` argument (several per-tick hooks pass `@`-by-ref: `NaturalLifeRegen(System.Single@)`, `GlobalNPC.UpdateLifeRegen(...,System.Int32@)`, `PostDrawFullscreenMap(System.String@)`). The Hook Interceptor's IL manipulators must be reviewed to guarantee they never touch those ref cells and always re-emit the original call. Read-only is achievable but is a property of the *manipulator implementation*, not free from the detour mechanism.

**Invariant 2 — overhead budget / zero hot-path allocation.** The hot path is the injected timing IL running per dispatch-loop iteration (Lite) or per wrapped call (Standard/Deep). Allocation hazards to forbid in the manipulator: no boxing of timing values, no per-call `Stopwatch` *object* (`Stopwatch` is a class — use `Stopwatch.GetTimestamp()` static long reads, not `new Stopwatch()` or `.Start()`), no closure capture in hook delegates, no LINQ over per-entity collections. `PerModSample[]` must be pre-allocated and indexed by mod ID, never grown per tick. The per-entity hooks (`GlobalNPC.AI`, `GlobalProjectile.AI`) multiply any per-call cost by entity count — at 84k projectiles/session even a 30 ns delegate frame is measurable, which is exactly why the README restricts per-`(GlobalType,hookMethod)` detours to Standard/Deep and uses loop-body aggregation for Lite. `Mod.Logger` must never be called from any of these hooks. Every change to the injected IL is an unmeasured-hot-path-change risk and must be benchmarked on a real modlist before "done" (the Milestone 0.A spike).

**Invariant 4 — abort-clean on host drift.** This is the sharpest concern for this slice. The Lite-mode detour targets (`*Loader` dispatch methods, `HookList`) are tModLoader-internal, perf-tuned, and explicitly described in the project brief as changing across updates. `MonoModHooks.Modify` takes a `MethodBase` the profiler resolves by reflection — if a loader method is renamed, has its signature changed, or has its dispatch loop restructured (e.g. `HookList` replaced, `foreach` unrolled, dispatch inlined), the reflection lookup returns null or the IL manipulator's pattern-match against the method body fails. The Hook Interceptor **must**: (a) resolve every internal target by reflection inside a guarded block, (b) verify the IL shape it depends on before injecting (the `foreach`/call pattern it expects), (c) if any target is missing or mismatched, disable instrumentation entirely, set the mode to `Off`, and report via `Mod.Logger.Warn` + the overlay — never inject against an unverified body. The public hooks in points 5 and 6 are stable API and safe; the internal targets in points 3 and 4 are the abort-clean surface.

## Coverage verdict

| Component | Buildable on documented public API alone? | Detail |
|---|---|---|
| **Hook Interceptor (1) — Lite mode** | **No** | Depends on ILHooking `*Loader` dispatch methods / `HookList.Enumerate`. `ItemLoader.*` dispatch is documented; `SystemLoader`/`NPCLoader`/`PlayerLoader`/`ProjectileLoader` dispatch and all of `HookList` are not. The detour *primitive* (`MonoModHooks.Modify`) is public; the *targets* are internal. `[needs-internals]` |
| **Hook Interceptor (1) — Standard/Deep mode** | **Partial** | Hook names/signatures fully documented; `MonoModHooks.Add` is public. Enumerating *other mods'* `Global*` instances to detour needs reflection over internal content registries. `[partial]` |
| **Hook Interceptor (1) — own-tick framing** | **Yes** | Overriding `ModSystem.PostUpdateEverything`/`PreUpdateEntities`/`UpdateUI` on the profiler's own subclass is clean public API. Gives tick boundaries and self-profiling, not per-mod attribution. `[public-API]` |
| **Per-Tick Metric Collector (2)** | **Yes (logic) / depends-on-(1) (data feed)** | The collector is pure logic: pre-allocated `PerModSample[]`, `Stopwatch.GetTimestamp()` deltas, GC-allocation deltas via `GC.GetAllocatedBytesForCurrentThread()` (BCL, not tML). Fully buildable and unit-testable against synthetic samples per the CLAUDE.md testability standard. It only *consumes* the timing events the Hook Interceptor produces, so its real-world correctness is gated on component (1)'s internal detours. `[public-API]` for the component itself. |

**Bottom line:** roughly **one-third** of the Hook Interceptor (its own-tick framing and the `MonoModHooks` primitive) and **all** of the Metric Collector's pure logic are buildable on the documented public API. The decisive remaining two-thirds — actually attributing cost to *other* mods — is internals-dependent and is the correct focus of the Milestone 0.A feasibility spike. None of this contradicts the README; the README already commits to ILHooking `*Loader` method bodies, and this analysis confirms that commitment is *necessary*, not optional.

## Open questions / NEEDS DECOMPILER VERIFICATION

1. **`NEEDS DECOMPILER VERIFICATION` — exact `*Loader` dispatch method names and signatures.** `SystemLoader`, `NPCLoader`, `PlayerLoader`, `ProjectileLoader` per-tick dispatch methods are absent from `tModLoader.xml`. The names `NPCLoader.NPCAI`, `PlayerLoader.PreUpdate`, `SystemLoader.PostUpdateEverything` used above are *plausible inferences from `ItemLoader`'s documented naming pattern* — they must be confirmed against the decompiled assembly before any reflection lookup is written.

2. **`NEEDS DECOMPILER VERIFICATION` — `HookList` / `GlobalHookList` structure.** Zero members in the XML. Whether Lite-mode timing IL is injected into `*Loader.<Hook>` bodies or into a `HookList.Enumerate`/enumerator, and whether `HookList` is generic, is unknown from public docs. The README assumes `HookList.Enumerate` exists; this needs confirming.

3. **`NEEDS DECOMPILER VERIFICATION` — per-mod identity at the dispatch site.** The README credits cost via `MethodBase.DeclaringType.Assembly`. Whether the dispatch loop iterates over delegates, over `Global*` instances, or over a struct array — and therefore what the IL manipulator can read at the call site to identify the owning mod — is internal and unverified.

4. **Access modifiers.** XML doc summaries do not state `public`/`internal`. `MonoModHooks.Modify` accepts any `MethodBase`, so reflection can reach `internal` targets regardless — but whether the `*Loader` types are `public` (affecting how cleanly they are referenced) needs confirming.

5. **Tick vs draw-frame frequency under frame-skip.** XML confirms update hooks fire only on full-update frames (`PreUpdateEntities` summary) but does not quantify the relationship between update ticks and draw frames under `Main.FrameSkipMode`. The Metric Collector must distinguish "per tick" from "per draw" cost; the exact frame-skip behaviour needs runtime verification, not just docs.

6. **Multiple `Draw` calls per frame.** `PlayerDrawLayer.Draw` "will be called multiple times a frame if a player afterimage is being drawn." The Metric Collector must not assume one draw-hook call per frame; the afterimage multiplier needs runtime measurement (relevant to the README's afterimage-cost example).

7. **`GlobalItem.UpdateInfoAccessory` XML defect.** The XML entry for `GlobalItem.UpdateInfoAccessory` carries the *summary text of `UpdateInventory`* (an upstream doc copy-paste bug). The hook exists and is per-tick per info-accessory; its real semantics should be confirmed from `ItemLoader` / `ExampleMod`, not from the mismatched summary.

---

## How we plug in (post-implementation status, 2026-05-20)

The 2026-05-19 verdict on this slice was "the Hook Interceptor is fundamentally an internals-dependent component; the public API supplies the detour primitive and the hook contract, but not the dispatch-loop targets that make per-mod attribution possible." The 2026-05-20 implementation took a different route that **resolves the verdict**: instead of detouring the internal `*Loader.<HookName>` dispatch bodies, we detour each mod's `Mod*` / `Global*` override on its own type.

### What we actually do

Both backends share the same Install loop:

1. Enumerate `ModLoader.Mods` (public, see `tmodloader/mod-identity.md`'s post-implementation note).
2. For each mod, walk its types via `AssemblyManager.GetLoadableTypes(Mod.Code)` (`[public-API]`).
3. For each non-abstract type, resolve the category via `HookCategoryRouter.ResolveCategory(type)` — returns one of the seven categories (Systems / Players / Npcs / Projectiles / Items / World / Buffs) or -1.
4. For each method on that type where `method.GetBaseDefinition() != method` (i.e. an actual override of a tModLoader virtual), install a detour.

The detour primitive depends on the backend:

- **Delegate backend (`HookInterceptor`)** — matches the method's signature against a hand-written set of ~30 delegate-pair shapes and installs a `MonoModHooks.Add` On-hook. Coverage ~71.6% on a real modlist (a real `7314 / 10220` measurement on an 18-mod stack).
- **IL backend (`ILHookInterceptor`)** — uses `new MonoMod.RuntimeDetour.ILHook(target, manipulator, applyByDefault: true)` with a signature-agnostic manipulator that wraps the body in `try { ProbeStack.Enter(hookId); /* body via retLocal */ } finally { ProbeStack.Leave(); }`. Coverage ~100%.

### Why we don't ILHook the internal `*Loader` dispatch

The 2026-05-19 analysis (above) called the internal dispatch the load-bearing target. We chose the alternative — detouring each `Mod*`/`Global*` override on its own type — for three reasons:

1. **No internals dependency for the timing IL.** The `Mod*`/`Global*` method bodies are user-mod code; their signatures are the public hook contract documented in `tModLoader.xml`. The IL we wrap is the mod's own body, not tModLoader's dispatch loop. Invariant 4 is satisfied without IL-shape verification of internal targets.
2. **One detour per override, not per dispatch.** The per-iteration foreach-body approach the 2026-05-19 analysis described would have given lower per-tick overhead (one detour per loader-method, not per override) but at the cost of fragility on tModLoader updates. We chose explicit-per-override coverage instead.
3. **Per-mod identity is direct.** Every detoured method has `MethodBase.DeclaringType.Assembly == Mod.Code`, so attribution is one dictionary lookup at install time, zero per-tick reflection.

### Coverage tri-state (delegate backend only)

`HookInterceptor.TryHookSupportedOverride` (`Profiling/HookInterceptor.cs:386-394`) returns three outcomes:

| Outcome | Counter advanced | Semantics |
|---------|------------------|-----------|
| `Installed` | measured++ + total++ | Detour installed |
| `UnsupportedSignature` | total++ + histogram | Signature not in the supported set — coverage debt |
| `InstallFailed` | total++ + `InstallFailures++` | MonoMod runtime error |

`HookCoverageVersion = 3` (`Profiling/HookInterceptor.cs:221`) is bumped any time the accounting changes shape; the session-log identity hash folds it in so old reports prune automatically.

The IL backend has its own counters (`_measuredHookCounts` / `_totalHookCounts`) that mirror the delegate path's; the active backend's counters are surfaced via `HookCoverageView` to the overlay PROFILER HEALTH strip, the TreeTab badge, and the session JSON `coverage` block.

### Canonical home

`systems/hook-instrumentation.md` carries the implementation reality, including the closed-generic inheritance pass, the JIT shared-body trap mitigation, and the abort-clean install behaviour.
