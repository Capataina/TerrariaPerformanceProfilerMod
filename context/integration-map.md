# Performance Profiler — tModLoader Integration Map

> Per-component plug-in points, milestone feasibility, and the workaround for every gap.
> Companion to `_Overview.md`; the per-slice `tmodloader-*.md` files hold the full member tables.

---

## Milestone feasibility on the public API

Can the README's milestones be built on tModLoader's public API alone? Verdict per milestone:

| Milestone | Scope | Verdict | Where the difficulty lives |
|---|---|---|---|
| **M0 — Feasibility spikes** | 0.A detour install cost @ 94 mods · 0.B JSON-lines write perf + crash safety · 0.C engagement-hook coverage | **Buildable on the API.** M0 is the probe phase by design. | 0.B/0.C are clean public API. 0.A installs detours through the public `MonoModHooks` API onto loader methods resolved by reflection — no source needed to *measure install cost*. |
| **M1 — Lite-mode MVP** | ILHook per-loader timing · per-mod CPU aggregate · single overlay panel (top-10 + 30 s rolling) · F9 toggle · gate < 1 % overhead | **Buildable on the API — but this is the hard milestone.** | Everything *around* the Hook Interceptor (Ring Buffer, aggregation, overlay shell, F9) is clean public API. The Hook Interceptor's ILHook timing is the project's real engineering risk: it needs the IL shape of tModLoader's internal `*Loader` dispatch loop. That shape is reachable **without a clone** — `MonoModHooks.Modify` hands the manipulator an `ILContext` (the live IL), and `MonoModHooks.DumpIL` writes it to `Logs/ILDumps/`. So we *observe* the IL through the API itself. A clone would make that IL easier to read as C#, but is a convenience, not a blocker. |
| **M2 — Tree + Standard mode** | foldable tree UI · Hot Path capture · per-mod icons · colour-gradient bars · per-`(GlobalType, hookMethod)` detours | **Buildable on the API.** | The widget tree is the public `UIElement` model; the distinctive visuals are custom `DrawSelf` drawing (standard FNA). Standard-mode per-`(GlobalType, hookMethod)` detours resolve other mods' `Global*` subclasses by reflecting their assemblies through the **public** `AssemblyManager.GetLoadableTypes`, then `MonoModHooks.Add` each. |
| M3 — Persistence + retrospective | JSON-lines storage · encounter detection · retrospective cards | Buildable. One reflection workaround: the writable data directory (`Main.SavePath`). |
| M4 — Insights engine | heuristic attribution rules · NL insight generation | Buildable — pure logic, no tModLoader API at all. |
| M5 — Public Workshop release | description, GIF, tutorial, repo | Not a code-API question. |

**Answer to "can we do M0, M1, M2 on the APIs": yes — all three.** M0 is exactly the right place to try the API and find any wall. M1 is buildable but is where the genuine difficulty concentrates (the Hook Interceptor IL work) — and the most likely moment we *choose* to clone for readability. M2 is buildable on the public widget API plus reflection over the public `GetLoadableTypes`.

**The likely "wall" is one specific thing:** writing the Lite-mode IL manipulator that wraps tModLoader's per-mod dispatch `foreach`. We can see that loop's IL via `DumpIL` at runtime, but reading 200 lines of raw CIL is slower than reading the equivalent C#. *That* is the point where cloning tModLoader (checked out to the installed commit) pays for itself. It is a Milestone 1 decision, not a Milestone 0 one.

---

## Component integration detail

Ordered by build tier (see `_Overview.md` for the tier model). `[public-API]` = documented & directly callable · `[doc-gap]` = public but undocumented, confirm the name from DLL metadata · `[reflection]` = non-public, reach via guarded reflection + abort-clean.

### Tier 1 — buildable today, no game, unit-testable

#### Component 3 — Ring Buffer
Pure data structure. Fixed-size circular buffer of `TickFrame` structs, allocated once, never grown.
- Lifecycle owner: `ModSystem.OnWorldLoad` (allocate) / `OnWorldUnload` (free) — `[public-API]`, a clean symmetric pair.
- The buffer itself is plain C# — write and test it now against synthetic `TickFrame`s.
- **No gap.**

#### Component 2 — Per-Tick Metric Collector
Pure logic that consumes the Hook Interceptor's timing events.
- Timing: `System.Diagnostics.Stopwatch.GetTimestamp()` — `[public-API]`, .NET BCL. Use the static `long` reads, never `new Stopwatch()` (Invariant 2).
- Allocation tracking: `GC.GetAllocatedBytesForCurrentThread()`, `GC.GetTotalPauseDuration()` (.NET 8) — `[public-API]`, BCL.
- `PerModSample[]` pre-allocated, indexed by mod ID.
- Frame boundary: open in `ModSystem.PreUpdateEntities`, commit in `PostUpdateEverything` — `[public-API]`. **Skip partial frames** (where `PreUpdateEntities` did not fire) — count them as "no tick sampled", not a 0 ms tick.
- **No gap.** The component's *logic* is fully buildable and unit-testable now; its real-world *data* depends on Component 1.

#### Component 8 — Insights Engine
Pure post-session rule logic over already-collected data. No tModLoader API at all. Fully buildable and unit-testable now against synthetic sessions.

#### Component 7 — Persistent Store (schema + serialisation half)
- JSON-lines schema, per-row `schema` field, serialisation via `System.Text.Json` — `[public-API]`, BCL. Buildable and testable now.
- Modlist-fingerprint hashing — pure logic, buildable now.
- *The directory acquisition is Tier 3 — see below.*

### Tier 2 — documented public API, needs the game to exercise

#### Lifecycle wiring
- `ModSystem.OnWorldLoad` / `OnWorldUnload` — ring-buffer alloc/free + detour install/teardown. `[public-API]`
- `ModSystem.PreSaveAndQuit` — clean-session-close signal; finalise + flush here. `[public-API]`
- `Mod.PostSetupContent` — build the `Assembly → ModId` map + modlist fingerprint here. `[public-API]`
- Per-tick sequence: `UpdateUI` → `PreUpdateEntities` → … → `PostUpdateEverything`. Full table in `tmodloader-lifecycle-and-loop.md`.

#### Component 6 — UI Renderer (the overlay shell)
- F9 keybind: `KeybindLoader.RegisterKeybind` → `ModKeybind.JustPressed`, polled in `ModPlayer.ProcessTriggers`. `[public-API]`
- Draw over live gameplay: insert a layer in `ModSystem.ModifyInterfaceLayers`; drive updates from `ModSystem.UpdateUI`. `[public-API]`
- Widget tree: the `Terraria.UI.UIElement` model (`Append`, `DrawSelf`, `LeftMouseDown`, `StyleDimension`). `[public-API]`
- Input suppression: set `Player.mouseInterface = true` when the cursor is over a panel. `[public-API]`
- **Avoid `IngameFancyUI`** — it is modal and locks the player out of gameplay, contradicting "Esc dismisses mid-fight, no modal traps".
- *Gaps: the custom-drawing substrate — Tier 3 below.*

#### Components 4 & 5 — Context Tagger + Encounter Detector
- Engagement hooks (all `[public-API]`): `GlobalNPC.OnKill` / `OnHitByProjectile`, `GlobalItem.UseItem` / `OnConsumeItem` / `OnConsumeAmmo` / `Shoot`, `ModPlayer.OnHitNPCWithItem` / `OnHitNPCWithProj`.
- Modded biomes: `ModBiome.OnEnter` / `OnLeave` / `OnInBiome`. `[public-API]`
- Boss/event triggers: `GlobalNPC.OnSpawn` + `NPC.boss`; `Main.bloodMoon` / `eclipse` / `invasionType`. `[public-API]`
- Win/died/fled outcome is our own derived logic over these hooks — not an API gap.
- ⚠ `GlobalNPC.OnKill` / `OnSpawn` are single-player/server only — fine for v1 (single-player); multiplayer needs a poll fallback (`NPC.AnyNPCs`). Recorded for the 0.C spike.
- ⚠ Return-value hooks (`UseItem`, `Shoot`) must return the upstream value unchanged — forcing `true`/`false` would breach Invariant 1.

### Tier 3 — one-time metadata-name confirmation, or a guarded-reflection workaround

| Need | Component | Gap type | Workaround |
|---|---|---|---|
| Vanilla `Zone*` field names (`ZoneSnow`, `ZoneCorrupt`, …) | 4 | `[doc-gap]` | Confirm names from `tModLoader.dll` metadata (IDE view or a reflection dump), then plain direct field reads. No runtime reflection needed once names are known. |
| `Main.dust` array, `Projectile.active` / `NPC.active` flags, array-length constants | 2, 4 | `[doc-gap]` | Same — confirm names from metadata, then direct reads. |
| UI draw substrate — `LegacyGameInterfaceLayer` ctor, `FontAssets`, the magic-pixel texture, `MeasureString` | 6 | `[doc-gap]` | Standard FNA drawing inside `UIElement.DrawSelf`; confirm exact handles from metadata + `ExampleMod`. Terraria ships **no monospace font** — the overlay's monospace look needs fixed-advance glyph layout, a real implementation-time task. |
| Writable data directory for the JSON-lines store | 7 | `[reflection]` | `Main.SavePath` via guarded reflection; **fall back** to the platform path (`%AppData%`/`Application Support` → `Terraria/tModLoader/`) if the field is absent. Abort-clean either way. |
| Loaded-mod enumeration for the `Assembly→ModId` map + fingerprint | 1, 2, 7 | `[reflection]` | `ModLoader.Mods` (historically a public `Mod[]`) via reflection if not directly visible; guarded, resolved once at `PostSetupContent`. If it fails, disable instrumentation and report (Invariant 4). |
| Per-mod author/homepage for the retrospective card | — | `[reflection]` | `build.txt` inside the `.tmod` via `Mod.File`; until confirmed, the card degrades gracefully (omit the `by <author>` line — never fabricate it, Invariant 3). |

### Tier 4 — the genuine spike (Milestone 0.A → Milestone 1)

#### Component 1 — Hook Interceptor (cross-mod attribution)
The one irreducible hard problem. The README's Lite mode = "ILHook the `*Loader.<HookName>` body, time the per-mod dispatch `foreach`".
- Detour primitive: `MonoModHooks.Modify(MethodBase, ILContext.Manipulator)` — `[public-API]`, accepts *any* `MethodBase`, public or internal.
- Targets: the internal `*Loader` dispatch methods + `HookList`. `ItemLoader`'s dispatch is documented; `SystemLoader`/`NPCLoader`/`PlayerLoader`/`ProjectileLoader` and all of `HookList` are not. `[reflection]`
- Per-mod identity at the call site: `MethodBase.DeclaringType.Assembly` matched against each `Mod.Code` — `[public-API]`, our own reflection (not a tModLoader ownership table — design wording to correct).
- **Workaround for the IL-shape unknown:** ILHook a loader method via `MonoModHooks.Modify`, and in the manipulator dump the `ILContext` with `MonoModHooks.DumpIL` (writes to `Logs/ILDumps/`). This *observes the real dispatch IL at runtime through the public API* — no clone, no decompiler. Write the timing manipulator against the observed shape.
- **Abort-clean (Invariant 4):** resolve every internal target by guarded reflection; verify the IL shape before injecting; on any mismatch, disable instrumentation, set mode `Off`, report on both surfaces. The internal targets are perf-tuned and *will* drift across tModLoader updates — this is the designed failure path, not an edge case.
- This is where, if anywhere, cloning tModLoader (at the installed commit) becomes worth it — to read the dispatch loop as C# instead of CIL. A Milestone 1 call.

---

## The one design-wording correction

The README/design say per-mod attribution "comes for free because tModLoader tracks per-assembly detour ownership through `MonoModHooks`." The public API does not expose any such ownership table. Attribution is genuinely free — but via the profiler's own `MethodBase.DeclaringType.Assembly → Mod.Code` reflection. Same outcome; correct the wording when the README/design is next touched.
