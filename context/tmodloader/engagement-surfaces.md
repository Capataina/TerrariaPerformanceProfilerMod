# tModLoader Integration Surface — Engagement & Context Surfaces

> Source: tModLoader.xml (tModLoader 1.4.4, build ~#5089). Serves components: Context Tagger (4), Encounter Detector (5), engagement instrumentation, Metric Collector frame stats (2).

## Summary

The engagement and context slice is **mostly buildable on the documented public API**: tModLoader exposes `Global*` and `Mod*` hook classes whose virtual methods (`GlobalNPC.OnKill`, `GlobalItem.UseItem`, `GlobalItem.Shoot`, `ModBiome.OnEnter`/`OnLeave`, `GlobalNPC.OnSpawn`) are exactly the event-driven taps an engagement tracker needs, and they are clean `[public-API]` surfaces. The poll-driven side is weaker on *documentation* but not on *reality*: the biome zone booleans (`ZoneJungle`, `ZoneSnow`, ...), the per-tick frame counters, the active-entity arrays and `active` flags are all genuine public vanilla fields on `Terraria.Player`/`Terraria.NPC`/`Terraria.Main` — they simply carry no XML doc comment, so this file cannot quote a summary for them and they are tagged `[partial]` pending a decompiler/IDE confirmation of exact names. The two real gaps are **GC timing** (no managed-runtime hook in the tModLoader API at all — must come from `System.GC` in the BCL) and **a frame-time / FPS field** (vanilla `Main.fpsCount`/`Main.frameRate` are not in the XML; the profiler should derive frame time from its own `Stopwatch` across an update boundary rather than trust an undocumented field). Read-only safety is excellent across the slice: every hook can be a pure observer, and every poll target is a field the profiler only ever reads.

## The surface

| Fully-qualified member | Kind | Hook or poll | What it does / why the profiler cares |
|---|---|---|---|
| `Terraria.ModLoader.GlobalNPC.OnKill(Terraria.NPC)` | M | Hook | Fires when any NPC dies. Engagement: `npcsKilled`; with `NPC.boss` true, `bossesFought` win-side. **Single-player / server only.** |
| `Terraria.ModLoader.GlobalNPC.OnHitByPlayer` | M | Hook | NPC hit by a player (melee/direct). Engagement: confirms player-attributed damage on this NPC. |
| `Terraria.ModLoader.GlobalNPC.OnHitByProjectile(Terraria.NPC,Terraria.Projectile,Terraria.NPC.HitInfo,System.Int32)` | M | Hook | NPC hit by a projectile. Engagement: weapon/projectile use confirmed. Runs on whoever owns the projectile (client or server). |
| `Terraria.ModLoader.GlobalNPC.OnSpawn(Terraria.NPC,Terraria.DataStructures.IEntitySource)` | M | Hook | Fires when any NPC spawns. Encounter Detector: boss-spawn trigger (filter on `NPC.boss`). **Single-player / server only.** |
| `Terraria.ModLoader.GlobalNPC.HitEffect(Terraria.NPC,Terraria.NPC.HitInfo)` | M | Hook | Client-side hit effect tap; usable as a client-visible "NPC took damage" signal where `OnHitByPlayer` is server-gated. |
| `Terraria.ModLoader.GlobalItem.UseItem(Terraria.Item,Terraria.Player)` | M | Hook | Item used (consumable, tool, swing). Engagement: `itemsUsed`. Return value controls `ApplyItemTime` — the profiler must return the upstream/default value, never force it. |
| `Terraria.ModLoader.GlobalItem.OnConsumeItem(Terraria.Item,Terraria.Player)` | M | Hook | An item stack was actually decremented. Engagement: precise "consumed" count, distinct from "swung". |
| `Terraria.ModLoader.GlobalItem.OnConsumeAmmo(Terraria.Item,Terraria.Item,Terraria.Player)` | M | Hook | Ammo consumed by a weapon. Engagement: ranged-weapon fire confirmation. |
| `Terraria.ModLoader.GlobalItem.Shoot(Terraria.Item,Terraria.Player,Terraria.DataStructures.EntitySource_ItemUse_WithAmmo,Microsoft.Xna.Framework.Vector2,Microsoft.Xna.Framework.Vector2,System.Int32,System.Int32,System.Single)` | M | Hook | Item spawned a projectile. Engagement: `weaponsFired`. **Local client only.** Return upstream value — `false` would suppress vanilla shooting. |
| `Terraria.ModLoader.GlobalItem.CanUseItem(Terraria.Item,Terraria.Player)` | M | Hook | Pre-use gate. Avoid as an engagement tap: fires speculatively and can be polled by other mods even when the item is never used. |
| `Terraria.ModLoader.ModPlayer.UpdateEquips` | M | Hook (per-tick) | Runs every tick while equips are processed. Usable as the per-tick poll site for the accessory scan, but the scan itself is poll-driven (see below). |
| `Terraria.ModLoader.ModPlayer.PostUpdateEquips` | M | Hook (per-tick) | Post-equip per-tick tap; safer scan point than `UpdateEquips` (equip state settled). |
| `Terraria.ModLoader.ModPlayer.OnHitNPCWithItem(Terraria.Item,Terraria.NPC,Terraria.NPC.HitInfo,System.Int32)` | M | Hook | Local player landed a melee/item hit. Client-side, player-attributed — complements server-gated `GlobalNPC.OnHitByPlayer`. |
| `Terraria.ModLoader.ModPlayer.OnHitNPCWithProj(Terraria.Projectile,Terraria.NPC,Terraria.NPC.HitInfo,System.Int32)` | M | Hook | Local player's projectile hit an NPC. Client-side, player-attributed. |
| `Terraria.ModLoader.ModPlayer.OnEnterWorld` | M | Hook | World-entry per-player tap. Session start / Context Tagger init. |
| `Terraria.ModLoader.ModBiome.OnEnter(Terraria.Player)` | M | Hook | Player entered a modded biome. Encounter Detector: biome-change trigger; engagement `biomesEntered`. |
| `Terraria.ModLoader.ModBiome.OnLeave(Terraria.Player)` | M | Hook | Player left a modded biome. Closes the biome dwell window. |
| `Terraria.ModLoader.ModBiome.OnInBiome(Terraria.Player)` | M | Hook (per-tick while active) | Fires each tick the player is in the biome. Usable for dwell accumulation; per-tick, so keep the body trivial. |
| `Terraria.ModLoader.ModBiome.IsBiomeActive(Terraria.Player)` | M | Poll | Returns true if the player is in the biome. Read-only query for the Context Tagger. |
| `Terraria.Player.InModBiome(Terraria.ModLoader.ModBiome)` | M | Poll | Per-instance "is the player in this ModBiome" query. **Throws `IndexOutOfRangeException` on failure** — must be guarded. |
| `Terraria.Player.CurrentSceneEffect` | P | Poll | The player's active scene-effect aggregate; a read-only snapshot of biome/scene context per tick. |
| `Terraria.Player.ZoneForest` / `ZonePurity` / `ZoneNormalCaverns` / `ZoneNormalUnderground` / `ZoneNormalSpace` / `ZoneOverworldHeight` / `ZoneDirtLayerHeight` / `ZoneRockLayerHeight` / `ZoneSkyHeight` / `ZoneUnderworldHeight` | P | Poll | The documented subset of vanilla biome zone properties. Read each tick for the Context Tagger biome tag. |
| `Terraria.NPC.boss` | F | Poll | True if the NPC is a boss. Filter for `OnSpawn`/`OnKill` boss detection. Read-only. |
| `Terraria.NPC.FullName` | P | Poll | Display name of an NPC — boss-fight label for the Encounter Detector. |
| `Terraria.NPC.realLife` | F | Poll | Multi-segment-boss "true body" index. Needed to de-dupe segmented bosses (e.g. worms) into one encounter. |
| `Terraria.NPC.netID` / `Terraria.NPC.type` | F | Poll | NPC identity for boss-class attribution. |
| `Terraria.NPC.AnyNPCs(System.Int32)` | M | Poll | "Is an NPC of this type alive" — cheap boss-presence check. |
| `Terraria.NPC.CountNPCS(System.Int32)` / `Terraria.NPC.FindFirstNPC(System.Int32)` | M | Poll | NPC-population helpers; locate the active boss for the encounter window. |
| `Terraria.Main.bloodMoon` | F | Poll | Blood-moon event flag. Encounter Detector event trigger. |
| `Terraria.Main.eclipse` | F | Poll | Solar-eclipse event flag. |
| `Terraria.Main.pumpkinMoon` / `Terraria.Main.snowMoon` | F | Poll | Pumpkin/Frost-moon event flags. |
| `Terraria.Main.invasionType` | F | Poll | Active invasion id (0 = none). Event detection: goblins, pirates, martians, etc. |
| `Terraria.Main.hardMode` | F | Poll | World hardmode state — context tag for the session. |
| `Terraria.Main.dayTime` / `Terraria.Main.time` / `Terraria.Main.moonPhase` | F | Poll | Day/night phase context for the Context Tagger. |
| `Terraria.Main.GameUpdateCount` | P | Poll | Game-update counter since world load (updates even while paused, not on menus). Drives modulo-timed sampling without a separate counter. |
| `Terraria.Main.worldEventUpdates` | F | Poll | World-event update counter; secondary event-activity signal. |
| `Terraria.Main.projectile` | F | Poll | The projectile array. Iterate the `active` flag to derive `projectileCount` for `TickFrame`. |
| `Terraria.Main.npc` | F | Poll | The NPC array. Source of `npcCount` for `TickFrame`. |
| `Terraria.Main.item` | F | Poll | The dropped-item array. Available context, not in the `TickFrame` spec. |
| `Terraria.ModLoader.ModSystem.PostUpdateEverything` | M | Hook (per-tick) | "Last hook in an update, all clients + server." The natural close-of-tick site to stamp the `TickFrame` and read frame stats. |
| `Terraria.ModLoader.ModSystem.PostUpdateNPCs` / `PostUpdateProjectiles` / `PostUpdateDusts` | M | Hook (per-tick) | Post-subsystem-update taps; entity counts are final by the time these run. |
| `Terraria.ModLoader.ModSystem.OnWorldLoad` / `OnWorldUnload` | M | Hook | Session boundary. Allocate the ring buffer / install detours on load; tear down on unload (Invariant 4 lifecycle). |
| `Terraria.ModLoader.Mod.Logger` | P | — | Per-mod `log4net` logger. Agent-surface channel for lifecycle/encounter events (never per-tick — Invariant 2). |

## Plug-in points

### Engagement events (event-driven hooks)

- `Terraria.ModLoader.GlobalNPC.OnKill(Terraria.NPC)` — **hook**, `[public-API]`. Primary `npcsKilled` tap; gates `bossesFought` via `NPC.boss`. Constraint: documented "called in single player or on the server only" — on a multiplayer **client** it does not fire. v1 is single-player (README "Public-mod quality bar"), so this is acceptable now; multiplayer engagement counting needs a client-visible substitute (`HitEffect`, or a death-detected-by-poll fallback). Record this constraint for the Milestone 0.C coverage spike.
- `Terraria.ModLoader.GlobalNPC.OnHitByPlayer` — **hook**, `[public-API]`. Player-attributed melee/direct hit.
- `Terraria.ModLoader.GlobalNPC.OnHitByProjectile(Terraria.NPC,Terraria.Projectile,Terraria.NPC.HitInfo,System.Int32)` — **hook**, `[public-API]`. Runs on the projectile owner (client or server), so it is client-visible for the local player's own projectiles — a better multiplayer-safe hit signal than `OnKill`.
- `Terraria.ModLoader.GlobalItem.UseItem(Terraria.Item,Terraria.Player)` — **hook**, `[public-API]`. `itemsUsed`. The return value drives `ApplyItemTime`; the profiler must return the value it received (or `null`/default for "no opinion"), never force `true`/`false` — that would be a write to game behaviour (Invariant 1).
- `Terraria.ModLoader.GlobalItem.OnConsumeItem(Terraria.Item,Terraria.Player)` — **hook**, `[public-API]`. Stack-decrement confirmation; distinguishes "consumed" from "swung".
- `Terraria.ModLoader.GlobalItem.OnConsumeAmmo(Terraria.Item,Terraria.Item,Terraria.Player)` — **hook**, `[public-API]`. Ranged-fire confirmation.
- `Terraria.ModLoader.GlobalItem.Shoot(...)` — **hook**, `[public-API]`. `weaponsFired`. Documented "local client only" and "return false to prevent vanilla's shooting code" — the profiler returns the upstream value unchanged.
- `Terraria.ModLoader.ModPlayer.OnHitNPCWithItem(...)` / `OnHitNPCWithProj(...)` — **hooks**, `[public-API]`. Client-side, local-player-attributed hit taps; the multiplayer-safe complement to the server-gated `GlobalNPC.OnHitByPlayer`.
- `Terraria.ModLoader.GlobalItem.CanUseItem(...)` — **hook**, `[public-API]`, **not recommended as an engagement tap** — it is a speculative gate, not a use event.

Equip / accessory engagement (`accessoriesEquipped`, `classesUsed`, `petsEquipped`) has **no clean event hook** — there is no documented `OnEquipAccessory` / `OnPetEquipped` hook. It is therefore **poll-driven**:

- `Terraria.ModLoader.ModPlayer.PostUpdateEquips` / `UpdateEquips` — **hooks**, `[public-API]` — used only as the *per-tick site* from which to run a poll, not as the engagement event itself.
- `Terraria.Player.armor`, `Terraria.Player.dye`, `Terraria.Player.miscEquips`, `Terraria.Player.miscDyes`, `Terraria.Player.inventory` — **fields**, `[partial]` (public vanilla fields; documented in XML as fields but with no summary). The accessory/pet/mount scan reads these arrays; per-mod attribution comes from `ModItem`/`Item.ModItem` identity on each slot. `Player.miscEquips` is the conventional pet/mount/light-pet slot array — exact slot indices `NEEDS DECOMPILER VERIFICATION`.
- `Terraria.ModLoader.AccessorySlotLoader` and `Terraria.ModLoader.ModAccessorySlot` — **types**, `[public-API]` for the documented members (`ModSlotCheck`, `ModdedIsItemSlotUnlockedAndUsable`, the `FunctionalItem`/`DyeItem` properties). These cover *modded* accessory slots added by other mods. Reading every modded slot's equipped item to attribute `accessoriesEquipped` across mods is feasible but the enumeration entry point — the per-player modded-slot store, conventionally `ModAccessorySlotPlayer` — **is not present in the XML** and is tagged `[needs-internals]` / `NEEDS DECOMPILER VERIFICATION`.

Buffs/pets/mounts: `Terraria.Player.buffType` / `buffTime` — **fields**, `[partial]` — give the active-buff scan (pet/light-pet/mount buffs surface here). `ModMount` is documented (`SetMount`, `UpdateEffects`) but those are author-side hooks on the *mount's own* class, not a global "any mount equipped" event — mount engagement is poll-driven off the player's mount state.

### Biome detection

- `Terraria.ModLoader.ModBiome.OnEnter(Terraria.Player)` / `OnLeave(Terraria.Player)` — **hooks**, `[public-API]`. Clean enter/leave events for **modded** biomes — directly feed `biomesEntered` and the Encounter Detector biome-change trigger.
- `Terraria.ModLoader.ModBiome.OnInBiome(Terraria.Player)` — **hook**, `[public-API]`, per-tick while active — dwell accumulation; keep the body trivial (Invariant 2).
- `Terraria.ModLoader.ModBiome.IsBiomeActive(Terraria.Player)` / `Terraria.Player.InModBiome(Terraria.ModLoader.ModBiome)` — **poll**, `[public-API]`. Read-only "in this modded biome" queries. `InModBiome` throws `IndexOutOfRangeException` on a bad biome reference — guard it.
- `Terraria.Player.CurrentSceneEffect` — **poll**, `[public-API]`. Per-tick scene-effect snapshot.
- **Vanilla biome zones** — `Terraria.Player.ZoneForest`, `ZonePurity`, `ZoneNormalCaverns`, `ZoneNormalUnderground`, `ZoneNormalSpace`, `ZoneOverworldHeight`, `ZoneDirtLayerHeight`, `ZoneRockLayerHeight`, `ZoneSkyHeight`, `ZoneUnderworldHeight` — **poll**, `[public-API]` (these ten *are* documented). The Context Tagger reads them each tick.
  - The remaining vanilla zone booleans the profiler also needs — `ZoneJungle`, `ZoneSnow`, `ZoneCorrupt`, `ZoneCrimson`, `ZoneHallow`, `ZoneDesert`, `ZoneBeach`, `ZoneJungle`, `ZoneDungeon`, `ZoneGlowshroom`, `ZoneMeteor`, `ZoneGraveyard`, `ZoneSandstorm`, etc. — are **public `bool` fields on `Terraria.Player`** but **carry no XML doc comment, so they do not appear in tModLoader.xml**. Tag `[partial]`: usable from code, but exact field names `NEEDS DECOMPILER VERIFICATION` (or confirm against the in-IDE assembly metadata). Do not guess the spelling of any zone field — verify the full set before the Context Tagger implementation.

### Boss / event detection

- `Terraria.ModLoader.GlobalNPC.OnSpawn(Terraria.NPC,Terraria.DataStructures.IEntitySource)` — **hook**, `[public-API]`. Boss-spawn trigger for the Encounter Detector — filter on `NPC.boss`. **Single-player / server only** (same caveat as `OnKill`).
- `Terraria.NPC.boss` — **poll**, `[public-API]` field. Boss classification.
- `Terraria.NPC.realLife` — **poll**, `[partial]` (documented field, no summary). De-dupes multi-segment bosses into one encounter.
- `Terraria.NPC.FullName` / `Terraria.NPC.netID` / `Terraria.NPC.type` — **poll**, `[public-API]` (`FullName`) / `[partial]` (`netID`, `type`). Boss identity / label.
- `Terraria.NPC.AnyNPCs(System.Int32)` / `CountNPCS(System.Int32)` / `FindFirstNPC(System.Int32)` — **poll**, `[public-API]`. Boss-presence and locate helpers — a robust poll-driven fallback to detect a boss fight even where `OnSpawn` did not fire (multiplayer client).
- Event flags on `Terraria.Main` — `bloodMoon`, `eclipse`, `pumpkinMoon`, `snowMoon` (**poll**, `[public-API]` documented fields) and `invasionType` (**poll**, `[public-API]`). The Encounter Detector polls these each tick (or via `PostUpdateEverything`) to open event windows. Edge-triggering (false→true) is the profiler's own logic.
- World context — `Terraria.Main.hardMode`, `dayTime`, `time`, `moonPhase` — **poll**, `[public-API]`.
- **Boss-fight outcome** (won / died / fled — needed for the retrospective ledger) has no single hook. It is derived: `OnKill` on a boss = won; `ModPlayer.OnHitByNPC` + player death (`ModPlayer.UpdateDead` / `OnRespawn`) during an open boss window = died; boss despawn with the window still open = fled. This is Encounter-Detector logic over `[public-API]` hooks, not a single API.

### Per-tick frame statistics (for `TickFrame`)

The `TickFrame` struct needs `frameTimeMs`, `gcTimeMs`, `projectileCount`, `npcCount`, `dustCount`.

- **`projectileCount`** — iterate `Terraria.Main.projectile` (**field**, `[public-API]`) counting entries whose `active` flag is set. `Projectile.active` is a public field but **not in the XML** — `[partial]`, name effectively certain but `NEEDS DECOMPILER VERIFICATION`.
- **`npcCount`** — iterate `Terraria.Main.npc` (**field**, `[public-API]`) counting `active`. Same `[partial]` caveat on `NPC.active`.
- **`dustCount`** — needs the dust array. `Terraria.Main.dust` is **not present in the XML at all** — `[needs-internals]` for documentation purposes, though it is a known public field. `NEEDS DECOMPILER VERIFICATION` for the exact field name and the `Dust.active` flag. The active-dust count may be cheaper to read from an internal counter than to iterate ~6000 slots — verify whether one exists.
- **`frameTimeMs`** — **no documented field.** Vanilla `Main.fpsCount` / `Main.frameRate` do not appear in the XML. Recommendation: **do not depend on an undocumented vanilla field** — the profiler already owns a `Stopwatch` (per the README overhead model); measure tick wall-time between two `PostUpdateEverything` calls (or `PreUpdateEntities` → `PostUpdateEverything`). This is `[public-API]` via the `ModSystem` hooks plus the BCL `Stopwatch`, and it is the more honest number for a profiler anyway.
- **`gcTimeMs`** — **no tModLoader API exists for this.** GC timing is not in `Terraria.*` or `Terraria.ModLoader.*`. It must come from the .NET BCL: `System.GC.GetTotalAllocatedBytes()` (alloc delta per tick), `System.GC.CollectionCount(int generation)` (collections-since deltas), and `System.GC.GetTotalPauseDuration()` (cumulative GC pause time, .NET 8 — subtract across ticks for per-tick `gcTimeMs`). Tag `[public-API]` against the **.NET 8 BCL**, not tModLoader. The README's Lite/Standard/Deep budget tiers should gate how often these are sampled.
- **`Terraria.Main.GameUpdateCount`** — **poll**, `[public-API]`. The modulo-timer source for sampled (sub-every-tick) capture in Lite mode.
- Close-of-tick stamp site: `Terraria.ModLoader.ModSystem.PostUpdateEverything` — **hook**, `[public-API]`, documented as the last hook in an update.

## Invariant checks

**Invariant 1 — read-only.** The slice is read-safe by construction:

- Every poll target (`NPC.boss`, `Main.bloodMoon`, `Player.ZoneForest`, the entity arrays, `GameUpdateCount`, BCL `GC` counters) is **read** only. The profiler must never assign to any of them. `GC.GetTotalAllocatedBytes`/`CollectionCount`/`GetTotalPauseDuration` are pure observers — no `GC.Collect()` call anywhere, which would itself be a behaviour change and a budget violation.
- Every engagement hook is overridden as a **pure observer**: increment a counter, return. The three return-value hooks are the only trap: `GlobalItem.UseItem` (controls `ApplyItemTime`), `GlobalItem.Shoot` (controls vanilla shooting), `GlobalItem.CanUseItem` (gate). The profiler must return the **upstream/default value unchanged** (in tModLoader's chained-hook model, the documented default). Returning a hard `true`/`false` would alter game behaviour and break Invariant 1. This must be a reviewed line in every such override.
- `Terraria.Player.InModBiome` throws `IndexOutOfRangeException` on a bad reference — a guarded `try`/skip keeps a Context Tagger fault from propagating, consistent with Invariant 4 (abort-clean).

**Invariant 2 — per-tick read cost.** The poll set is the hot path; cost notes:

- Cheap, safe per tick: the scalar field reads (`Main.bloodMoon`, `hardMode`, `dayTime`, `GameUpdateCount`, `NPC.boss`, the ten `Zone*` properties). Single field loads, zero allocation.
- Costlier: counting active entities iterates arrays — `Main.projectile` (~1000 slots), `Main.npc` (~200), `Main.dust` (~6000). Counting dust by full iteration is the most expensive single read in the slice; prefer an internal active-dust counter if one exists (verify), and consider sampling these counts at the Lite-mode modulo rate rather than every tick.
- `GC.GetTotalPauseDuration()` / `GetTotalAllocatedBytes()` are cheap but non-zero; sample-gate them by mode tier.
- `ModBiome.OnInBiome` runs per-tick *per active modded biome* — keep its body to a counter increment.
- All counters must be pre-allocated fields (per the `PerModSample[]` model); no per-tick `new`, no boxing of the `int`/`long`/`float` counts. The engagement hooks fire far less than 60 Hz so they are not the budget concern; the per-tick poll is.

## Coverage verdict

**Context Tagger (4):** ~85% on documented public API. Modded biomes (`ModBiome.IsBiomeActive`, `OnEnter`/`OnLeave`, `Player.InModBiome`, `CurrentSceneEffect`) and event/world flags (`Main.bloodMoon`/`eclipse`/`invasionType`/`hardMode`/`dayTime`) are fully `[public-API]`. The gap is the **full set of vanilla `Zone*` booleans** — ten are documented, the rest (`ZoneJungle`, `ZoneSnow`, `ZoneCorrupt`, `ZoneDungeon`, ...) are real public fields absent from the XML: `[partial]`, names to be confirmed once, then trivially usable.

**Encounter Detector (5):** ~80% on documented public API. Boss-spawn (`GlobalNPC.OnSpawn` + `NPC.boss`), biome-change (`ModBiome.OnEnter`/`OnLeave`), and event-start (Main flags) triggers are all there. Two caveats: (a) `OnSpawn`/`OnKill` are single-player/server-only, so the multiplayer path needs the poll-driven `NPC.AnyNPCs`/`CountNPCS` fallback — fine for v1 single-player; (b) **fight outcome** (won/died/fled) is derived Encounter-Detector logic over public hooks, not a single API. No internals needed.

**Engagement tracking:** ~75% on documented public API. Kills, hits, item use, ammo, weapon fire are all clean `[public-API]` hooks. The weak area is **equip/accessory/pet/mount/class engagement**, which is entirely **poll-driven over `[partial]` vanilla fields** (`Player.armor`, `miscEquips`, `buffType`) and, for cross-mod modded accessory slots, a `[needs-internals]` per-player slot store. `accessoriesEquipped`/`petsEquipped`/`classesUsed` are buildable but need a decompiler pass to nail the exact field/slot layout before implementation.

**Metric Collector frame stats (2):** ~60% on documented tModLoader API, ~90% if the .NET 8 BCL is counted. The tick-stamp site (`ModSystem.PostUpdateEverything`) and the entity arrays are `[public-API]`. `frameTimeMs` should be **profiler-owned** (`Stopwatch` across an update boundary) rather than read from an undocumented vanilla field — better engineering anyway. `gcTimeMs` has **no tModLoader API** and must come from `System.GC` (.NET 8): `[public-API]` against the BCL. `dustCount` needs the undocumented `Main.dust` array — `[needs-internals]` for docs, `NEEDS DECOMPILER VERIFICATION` in practice.

**Overall:** the event-driven engagement-and-encounter half is largely buildable on the documented public tModLoader API today. The poll-driven half (frame stats, biome zone booleans, equip scans) leans on real public vanilla fields that the XML simply does not document — none of it needs *private* internals except the cross-mod modded-accessory-slot enumeration. **One decompiler/IDE pass to confirm the undocumented-but-public vanilla field names unblocks the entire poll-driven surface.** This should be folded into the Milestone 0.C engagement-hook-coverage spike.

## Open questions / NEEDS DECOMPILER VERIFICATION

1. **Full vanilla `Zone*` field set** — only ten `Zone*` *properties* are in the XML. The boolean fields the Context Tagger needs (`ZoneJungle`, `ZoneSnow`, `ZoneCorrupt`, `ZoneCrimson`, `ZoneHallow`, `ZoneDesert`, `ZoneBeach`, `ZoneDungeon`, `ZoneGlowshroom`, `ZoneMeteor`, `ZoneGraveyard`, `ZoneSandstorm`, and others) are public on `Terraria.Player` but undocumented — `NEEDS DECOMPILER VERIFICATION` for the exact names and the complete list. Do not hard-code guessed spellings.
2. **`Main.dust` array + `Dust.active`** — `Main.dust` is absent from the XML. Confirm the field name, the array length constant, and whether an internal active-dust counter exists (to avoid iterating ~6000 slots every tick — Invariant 2). `NEEDS DECOMPILER VERIFICATION`.
3. **`active` flags** — `Projectile.active` / `NPC.active` are public but undocumented. Confirm exact names before the count loops. `NEEDS DECOMPILER VERIFICATION` (low risk, names are well-known).
4. **`Main.maxProjectiles` / `maxNPCsServer` / array-length constants** — not in the XML. Needed to bound the count loops correctly. `NEEDS DECOMPILER VERIFICATION`.
5. **Cross-mod modded accessory slots** — `AccessorySlotLoader`/`ModAccessorySlot` are documented, but the **per-player modded-slot store** (the object holding each player's equipped modded-accessory items, conventionally `ModAccessorySlotPlayer`) is not in the XML. Required to attribute `accessoriesEquipped` across other mods. `[needs-internals]` / `NEEDS DECOMPILER VERIFICATION`.
6. **`Player.miscEquips` slot layout** — the field exists; the index meaning of each slot (pet / light pet / mount / hook / minecart) is undocumented. Needed for `petsEquipped`. `NEEDS DECOMPILER VERIFICATION`.
7. **Multiplayer engagement** — `GlobalNPC.OnKill` and `GlobalNPC.OnSpawn` are single-player/server-only. v1 is single-player so this is not a blocker, but the v2 multiplayer path needs a client-visible substitute (`HitEffect`, `OnHitByProjectile` for owned projectiles, or poll-driven death detection). Open design question, recorded for the Milestone 0.C spike.
8. **`GC.GetTotalPauseDuration()` semantics** — confirm this .NET 8 API reports a monotonic cumulative pause time suitable for per-tick differencing, and that it is not Server-GC-mode dependent in tModLoader's runtime configuration. Verify against the .NET 8 BCL, not tModLoader.
9. **Hook return-value default** — for `GlobalItem.UseItem`/`Shoot`/`CanUseItem`, confirm the exact value a pure-observer override must return to be a true no-op in tModLoader's chained-hook dispatch (XML says "returns true by default" for `Shoot`; verify `UseItem`/`CanUseItem` and whether returning the parameter-passed value is the correct passthrough). Read-only correctness depends on this.

---

## How we plug in (post-implementation status, 2026-05-20)

The 2026-05-19 analysis identified seven open `NEEDS DECOMPILER VERIFICATION` items, most of them about undocumented-but-public vanilla fields (`Zone*`, `Main.dust`, `Projectile.active`, `NPC.active`). The 2026-05-20 implementation **resolves them via reflection at population time** rather than hard-coding field names.

### Context tagging

The `Profiling/Events/` subsystem (canonical home: `systems/events-and-context.md`) snapshots per-tick game state into `EventContext` values that travel inside every `TickFrame.Context`.

The snapshot reads:

| Surface | How we resolve | Reference |
|---------|---------------|-----------|
| Vanilla biome zones (`ZoneJungle`, `ZoneSnow`, `ZoneCorrupt`, ...) | `BiomeRegistry.Populate` reflects over `typeof(Terraria.Player)`'s `bool ZoneX` fields at `PostSetupContent`. Missing fields are simply absent — abort-clean per Invariant 4. | `Profiling/Events/BiomeRegistry.cs` |
| Modded biomes | `BiomeRegistry.Populate` enumerates modded biomes via tModLoader content reflection. Per-biome `IsBiomeActive(Main.LocalPlayer)` probe stored. | `Profiling/Events/BiomeRegistry.cs` |
| Active boss | `BossSampler.Current()` iterates `Main.npc[]`, filters `npc.active && npc.boss`, deduplicates segmented bosses via `NPC.realLife`. | `Profiling/Events/BossSampler.cs` |
| Event flags (`Main.bloodMoon`, `Main.eclipse`, `Main.pumpkinMoon`, `Main.snowMoon`, `Main.invasionType`) | Direct field reads each tick. | `Profiling/Events/ContextTagger.cs` |
| Subworld | `SubworldProbe.CurrentId()` reflects over `SubworldLibrary.SubworldSystem.Current`. Optional; `Available = false` if SubworldLibrary is missing. | `Profiling/Events/SubworldProbe.cs` |

`Main.GameUpdateCount` and the entity arrays (`Main.npc`, `Main.projectile`, `Main.dust`) are accessed directly via `CountActive(...)` helpers in `ProfilerSystem`. The "name effectively certain" `NPC.active` / `Projectile.active` / `Dust.active` of the 2026-05-19 analysis are confirmed working since M1.

### Frame statistics

Resolution of the 2026-05-19 analysis's three frame-stat gaps:

| Need | Resolution |
|------|-----------|
| `frameTimeMs` | `MetricCollector` owns its own `Stopwatch.GetTimestamp()` reads at `BeginTick` and `EndTick`. No dependency on undocumented vanilla fields. |
| `gcTimeMs` | `GC.GetAllocatedBytesForCurrentThread()` is read at `BeginTick` and `EndTick`; `TickFrame.AllocBytes = exit - entry`. GC pause time is not currently captured. |
| `dustCount` | `CountActive(Main.dust)` iterates the ~6000-slot array. Acceptable for M1 (a few microseconds per tick); flagged for future optimisation if overhead measurement ever requires it. |

### Engagement hooks (deferred)

The 2026-05-19 analysis described `GlobalNPC.OnKill` / `OnHitByPlayer` / `OnHitByProjectile`, `GlobalItem.UseItem` / `Shoot` / `OnConsumeItem`, `ModBiome.OnEnter` / `OnLeave` / `OnInBiome` as the event-driven engagement taps.

**None of these are currently instrumented as engagement events.** The mod's current scope is per-tick CPU + allocation attribution; engagement counting (`npcsKilled`, `itemsUsed`, `weaponsFired`) is a separate feature surface that has not been built. The hooks would be instrumented through the same `HookInterceptor` / `ILHookInterceptor` mechanism, but the *counting* (vs *timing*) layer is missing.

When this work resumes:

- `GlobalNPC.OnKill` / `OnSpawn` are single-player/server only. v1 is single-player; multiplayer falls back to poll-driven `NPC.AnyNPCs` per the 2026-05-19 analysis.
- Return-value hooks (`UseItem`, `Shoot`, `CanUseItem`) must return the upstream value unchanged. Convention #3 (try/finally) covers timing; engagement counting must not mutate return values either.

### Canonical home

`systems/events-and-context.md` carries the implementation reality including `ContextTagger.Snapshot`, `EventAggregator.Accumulate`, `BiomeRegistry`, `BossSampler`, and the optional `SubworldProbe`.
