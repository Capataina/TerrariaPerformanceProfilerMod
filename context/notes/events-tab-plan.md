# Events / Encounters Tab — Implementation Plan

> Scope: add a top-level **Events** tab to the F9 profiler overlay that correlates per-tick CPU cost with concurrent game state — biomes, weather, world events, invasions, active bosses, subworlds. The goal is to answer questions of the form *"is my average tick worse in the Jungle?"*, *"is Blood Moon worse than a regular night?"*, *"which boss fight is the most expensive?"* without imposing a hardcoded list of vanilla state. Honours all four Project Invariants. Targets tModLoader 1.4.4 on .NET 8.

The plan is sized and shaped to mirror `context/ILHook-migration-plan.md`: an evidence ledger, a viability verdict, a model, a step-by-step implementation sequence, an honest risk register, and a testing strategy. Anything an implementer would otherwise have to research again has been pinned in §0.

---

## 0. Research evidence ledger

Every API surface this plan depends on was verified by reflection against the live tModLoader install at `/Users/atacanercetinkaya/Library/Application Support/Steam/steamapps/common/tModLoader/tModLoader.dll` (tModLoader 1.4.4.9), or against the SubworldLibrary 1.4.4 source on GitHub (we do not assume the library is installed).

| Claim | Evidence |
|---|---|
| `Terraria.Player` exposes **38** instance properties named `ZoneXxxxx` returning `bool` — they are **C# properties**, not fields, backed by `BitsByte zone1..zone5` packing | Reflection over `Player`: 0 fields starting with `Zone`, but `GetProperties(Public\|Instance\|DeclaredOnly).Where(p => p.Name.StartsWith("Zone") && p.PropertyType == typeof(bool)).Count() == 38`. Enumerated names: `ZoneDungeon, ZoneCorrupt, ZoneHallow, ZoneMeteor, ZoneJungle, ZoneSnow, ZoneCrimson, ZoneWaterCandle, ZonePeaceCandle, ZoneTowerSolar, ZoneTowerVortex, ZoneTowerNebula, ZoneTowerStardust, ZoneDesert, ZoneGlowshroom, ZoneUndergroundDesert, ZoneSkyHeight, ZoneOverworldHeight, ZoneDirtLayerHeight, ZoneRockLayerHeight, ZoneUnderworldHeight, ZoneBeach, ZoneRain, ZoneSandstorm, ZoneOldOneArmy, ZoneGranite, ZoneMarble, ZoneHive, ZoneGemCave, ZoneLihzhardTemple, ZoneGraveyard, ZoneShadowCandle, ZoneShimmer, ZonePurity, ZoneForest, ZoneNormalCaverns, ZoneNormalUnderground, ZoneNormalSpace`. Each has a public getter; we only ever invoke the getter. |
| Modded biomes are exposed through `Player.InModBiome(ModBiome)` (instance, returns `bool`) and the parameterless `InModBiome<T>()` generic; backing storage is `Player.modBiomeFlags : System.Collections.BitArray` (nonpublic instance field) | Reflection over `Player` — both `InModBiome` overloads present; `modBiomeFlags` enumerable as a nonpublic instance field. |
| Every loaded modded biome is enumerable via `ModContent.GetContent<ModBiome>() : IEnumerable<ModBiome>`. Each `ModBiome` exposes `.Type` (int, the dynamic biome id), `.Name`, `.FullName`, `.Mod`, `.DisplayName` (`LocalizedText`), and `IsBiomeActive(Player) : bool` | Reflection: `ModContent.GetContent<T>` is `IEnumerable<T> GetContent<T>()` static. `ModBiome` inherits `ModType` which carries `Mod`, `Name`, `FullName`. `ModBiome.Type` is a property on `ModBiome` (overrides `ModSceneEffect.Type`). |
| `Main` static surface for weather/events/world: `public static bool bloodMoon, eclipse, slimeRain, pumpkinMoon, snowMoon, raining, dayTime, hardMode, xMas, halloween, forceXMasForToday, forceHalloweenForToday, drunkWorld, getGoodWorld, tenthAnniversaryWorld, dontStarveWorld, notTheBeesWorld, remixWorld, noTrapsWorld, zenithWorld, afterPartyOfDoom`; `public static int invasionType, GameMode, moonPhase`; `public static double worldSurface, dayRate`; `public static string worldName`; `public static Player[] player`; `public static int myPlayer`; `public static Player LocalPlayer { get; }` (property; getter returns `player[myPlayer]`) | Reflection over `Main` enumerating public static fields and properties. Verified shape on the live install. |
| Difficulty derived through `Main.GameModeInfo : Terraria.DataStructures.GameModeData` with `IsExpertMode, IsMasterMode, IsJourneyMode` bool properties and `Id` int | Reflection. The vanilla Classic case is `!IsExpertMode && !IsMasterMode && !IsJourneyMode`. |
| `Sandstorm.Happening`, `DD2Event.Ongoing`, `LanternNight.GenuineLanterns \|\| LanternNight.ManualLanterns`, `BirthdayParty.GenuineParty \|\| BirthdayParty.ManualParty` are the canonical event flags in `Terraria.GameContent.Events.*` | Reflection over each type. `Sandstorm.Happening : bool`, `DD2Event.Ongoing : bool`, `LanternNight.GenuineLanterns / .ManualLanterns : bool`, `BirthdayParty.GenuineParty / .ManualParty : bool`. |
| `Main.invasionType` is the vanilla invasion id; `InvasionID` constants live in `Terraria.ID.InvasionID` (`Goblins=1, FrostLegion=2, PirateInvasion=3, MartianMadness=4`); `Main.pumpkinMoon` / `snowMoon` are *not* invasions, they are moon-event flags | Reflection: `InvasionID.PirateInvasion = 3 (Int16)`, `CachedInvasions = 3`. Confirmed alongside `Main.invasionType : Int32`. |
| `NPC` exposes `bool active`, `bool boss`, `int type`, `int netID`, `int whoAmI`, `int realLife`, `string FullName`, `string GivenOrTypeName`, `string TypeName`, `ModNPC ModNPC`. `realLife` points to the `whoAmI` of the boss "head" NPC for multi-segment fights (Eater of Worlds, Wall of Flesh, etc); it is `-1` for non-segmented or head NPCs | Reflection. `realLife : Int32` is public instance; `boss : Boolean` public instance. Convention documented in Terraria source — `realLife == -1` on the head, equal to `head.whoAmI` on the segments. |
| `NPCID.Sets.ShouldBeCountedAsBoss : bool[]` indexed by NPC type is the canonical "is this a boss for progression purposes" set — vanilla bosses set it, well-behaved modded bosses set it in their `ModNPC.SetStaticDefaults` | Reflection: `Terraria.ID.NPCID+Sets.ShouldBeCountedAsBoss : Boolean[]`. |
| Friendly NPC display names come from `Lang.GetNPCName(int netID) : LocalizedText`; `.Value : string` produces the localised final name | Reflection: `Terraria.Lang.GetNPCName(System.Int32 netID) -> LocalizedText`; `LocalizedText.Value : String`. |
| Lunar pillar NPC type ids are vanilla constants — `NPCID.LunarTowerSolar=517, LunarTowerVortex=422, LunarTowerNebula=507, LunarTowerStardust=493` | Reflection over `Terraria.ID.NPCID` literal constants. |
| `Main.worldName : string` and `Main.ActiveWorldFileData` expose the current world's name/seed/difficulty; `WorldGen.currentWorldSeed : string` is the seed string | Reflection. `Main.worldName : String` (public static field). `Main.ActiveWorldFileData` is a public static property; its concrete `WorldFileData` type holds the seed and difficulty mirror. |
| `Mod.Logger : log4net.ILog` is the runtime log surface | Reflection. |
| `ModSystem` virtual hook list includes `PreUpdateEntities, PostUpdateEverything, OnWorldLoad, OnWorldUnload, PreUpdateWorld, PostUpdateWorld, PreUpdateInvasions, PostUpdateInvasions, PreSaveAndQuit` — **no dedicated `OnBiomeEnter` / `OnBossSpawn` hooks exist**; transition detection must come from state diff against the previous tick | Reflection over `Terraria.ModLoader.ModSystem` virtual methods. No `OnBiomeEnter`, `OnBossSpawn`, `OnEventStart` hooks declared. |
| `SubworldLibrary.SubworldSystem` exposes `public static Subworld Current { get; }`, `public static bool AnyActive()`, `public static bool IsActive(string id)`, `public static bool IsActive<T>() where T : Subworld`, `public static bool AnyActive(Mod)`, `public static bool AnyActive<T>() where T : Mod`. Namespace is `SubworldLibrary`. We probe by `Type.GetType("SubworldLibrary.SubworldSystem, SubworldLibrary", throwOnError: false)` and bind the static `AnyActive()` `MethodInfo` plus the `Current` `PropertyInfo` once at world-load; both stay null when SWL is not loaded | Source at `https://github.com/jjohnsnaill/SubworldLibrary/blob/master/SubworldSystem.cs`, verified verbatim API. SWL is not added as a hard dependency in `build.txt`. |

### One non-evidence finding worth stating

The user's mental model of biome-name-display mods was that they "solve dynamic vanilla biome detection with no hardcoded list". Reality, verified against the most-installed example **Biome Titles** (`github.com/d-Dice/BTitles-1.4.3`): the mod **hardcodes** each `if (player.ZoneJungle) return "Jungle";` check inside `BiomeChecker` / `MiniBiomeChecker`. There is no published mod we found that enumerates Zone* properties reflectively. So the technique we use — reflection over `Player.GetProperties()` filtered by `StartsWith("Zone") && PropertyType == typeof(bool)` — is not borrowed; it is our own application of the same standard reflection pattern used elsewhere in the profiler (see `HookInterceptor`'s `AssemblyManager.GetLoadableTypes` walk). The technique is sound and we know exactly the 38 properties it discovers today; the gain is that a future tML release adding `ZoneAetherShimmerExtra` to `Player` is captured for free.

---

## 1. Viability verdict

**Doable. Recommended. The achievable share of fully-dynamic discovery is ~90% of the surface that matters; the residual 10% is enumerable and well-bounded.**

The Events tab consumes the same per-tick rhythm the profiler already drives (`PreUpdateEntities` / `PostUpdateEverything`). The sole new runtime cost is reading a small, fixed set of state slots per tick and a slightly larger one on a lower cadence. Aggregation is per-dimension — never a Cartesian product — so the bucket count grows with the union of states the player visits in a session, not their multiplication.

### What is fully dynamic

| Dimension | Mechanism | Bucket count (representative session) |
|---|---|---|
| Vanilla biomes | Reflect over `Player.GetProperties()` → cache 38 `(name, getter delegate)` pairs at install; each tick invoke the cached delegate against `Main.LocalPlayer` | ≤ 38 |
| Modded biomes | `ModContent.GetContent<ModBiome>()` once at install, cache `(FullName, ModBiome ref)` pairs; per tick call `player.InModBiome(biome)` | one per loaded modded biome (1.4.4 modlists: typically 0–60) |
| Active bosses | Scan `Main.npc[]`; an NPC qualifies if `active && (boss \|\| NPCID.Sets.ShouldBeCountedAsBoss[type])`; collapse multi-segment by `realLife != -1 ? Main.npc[realLife] : self`; friendly name via `Lang.GetNPCName(netID).Value` | one per boss fought (≤ 30 per long session) |
| Lunar pillars | Same scan, special-case the four `LunarTower*` NPC type ids | up to 4 |
| Subworld | Reflection probe of `SubworldLibrary.SubworldSystem.Current` (optional dependency) | 0 or 1 |

### What is semi-dynamic — fixed *set* of vanilla flags, but the *names* come from the field names themselves

| Dimension | Mechanism | Bucket count |
|---|---|---|
| Time / weather / world events / moon events | Read a curated set of `Main`/`Sandstorm`/`DD2Event`/`LanternNight`/`BirthdayParty` static booleans by name — but the *list of names* is derived from a small declarative table inside the Context Tagger, not scattered across the call sites. Adding a new vanilla weather flag is a one-line addition | ≤ 15 |
| Difficulty | Read `Main.GameModeInfo.IsExpertMode / IsMasterMode / IsJourneyMode` | 4 |
| Hardmode | `Main.hardMode` | 2 |
| Vanilla invasions | `Main.invasionType` integer, mapped to a small `InvasionID` switch (4 vanilla invasions) | ≤ 4 |

### The honest residual

| Dimension | Why not dynamic | Plan |
|---|---|---|
| Modded "events" not piggybacking on vanilla flags | tML 1.4.4 has no `ModEvent` registration API; modded events store their own static `bool Ongoing` somewhere only that mod knows | Documented gap. A per-mod opt-in `Mod.Call` API is sketched in §13 for later; *not* shipped in v1. |
| In-fight boss phases | Phase is an internal `ai[]` slot interpretation specific to each boss | Out of scope. Documented. |
| Modded invasions | tModLoader has no `ModInvasion` enumeration as of 1.4.4 | Documented gap; vanilla invasions cover ≥99% of modlists in practice. |

The viability conclusion: **the user's "dynamic everywhere it's possible" demand is honourable on biomes (38 vanilla + N modded), on bosses (one scan handles vanilla and modded uniformly), and on subworlds (one reflection probe). It is not honourable on modded events / phases / modded invasions; those become a per-mod opt-in surface or a `this session` blind spot, explicitly badged.**

### Risks the plan must address

| Risk | Trigger | Mitigation |
|---|---|---|
| **R1. Per-tick reflection cost.** Calling `PropertyInfo.GetValue(player)` 38 times per tick is ~3µs each (≈ 100µs total) — comparable to a small mod's whole frame budget | Always-on Lite mode | Cache each Zone getter as `Func<Player, bool>` via `Delegate.CreateDelegate` once at install; per-tick read is then a direct virtual call. Measured ~30ns/call. See §7. |
| **R2. ModBiome.IsBiomeActive can be arbitrarily expensive** (some content mods recompute tile counts inside it) | Standard or Deep modes if we called it per tick per modded biome | Read the cheap `BitArray` `modBiomeFlags` *directly* (tModLoader sets this for us inside `BiomeLoader.UpdateBiomes`), keyed by `ModBiome.Type`. Costs one `BitArray.Get(int)` per modded biome — ~10ns. See §3.5. |
| **R3. `Main.npc[]` is 200 slots.** A full scan per tick reads 200 NPC structs and 200 `.active` bools. | Lite mode boss tracking | Already proven affordable: `ProfilerSystem.CountActive(Main.npc)` runs every tick today. Reuse the same pattern; add a `boss \|\| ShouldBeCountedAsBoss[type]` filter inside the existing loop. No new cost. |
| **R4. Multi-segment double-counting.** Eater of Worlds is ~70 segments each with `boss = true`; Wall of Flesh is head + eye + eye | Any boss with `realLife != -1` | Collapse: at scan time, if `realLife != -1`, attribute to `Main.npc[realLife].type` instead of `npc.type`. Verified against the EoW IL pattern in `Terraria.NPC.realLife` field semantics. |
| **R5. Multiple bosses live simultaneously** (twins, lunar pillars, modded duos) | Plantera + a worm summoned, Twins, lunar phase | Each unique `type` after the realLife collapse contributes; one tick can populate multiple boss buckets in parallel. Matches the orthogonal-dimensions model. |
| **R6. SubworldLibrary not installed.** Naive `using SubworldLibrary;` would force a hard dependency | Standard modlist | Reflection probe only; nothing imported. The probe `Type.GetType(...)` returns null when SWL is not loaded; binding code stays null and skipped. |
| **R7. Cross-dimensional drill-down explodes storage.** "Cost in Jungle AND Blood Moon" requires a joint bucket | If we naively store every observed (biome, weather, …) tuple | Per-dimension storage by default. Joint-dimension drill is computed *on demand* by re-walking the ring buffer's last 30 seconds, which is bounded at 1 800 frames. See §8. |
| **R8. UI tab strip is new surface area.** Today the overlay header has 30S AVG / LIVE toggles; we are adding navigation, not toggling | Tab strip placement | The strip lives *below* the header on a new row, leaving the existing toggles alone. New tabs are `LIVE` (existing tree, renamed) and `EVENTS`. Future tabs (Boss Fights, Hot Moments) slot in as further entries without changing the strip's shape. See §9. |
| **R9. Bucket pruning early in session.** First 30 seconds have one bucket each for ~5 dimensions → mostly empty | Cold start | Render buckets *as they accumulate ≥ 1 second of dwell*. Below 1s, hide from the list but keep tracking. Threshold tuned in §9. |
| **R10. Vanilla update changes a `Zone*` property name.** `ZoneNormalCaverns` was added in 1.4.4; future versions could rename or add | tModLoader update | The reflection enumeration is the mitigation — new property names appear automatically. Removed names disappear and the matching bucket simply stops accumulating. We log a diff on install (compared against a baseline list of the 38 known names) so a vanilla schema change is visible in `client.log`. |

None of these are blockers. The biggest practical risk is R1 — Lite-mode tick overhead — and the mitigation (delegate-cached getters) is mechanical.

---

## 2. Use-case shape

The feature must support these queries directly on the Events tab, ranked by how often they motivate a player to open the profiler:

| Query | Bucket dimension | Aggregator |
|---|---|---|
| "Which biome am I getting lag spikes in?" | Biome | Per-bucket spike count (frames > 2× session mean) + peak frame ms |
| "Is Blood Moon worse than a regular night?" | Weather (BloodMoon = on/off) | Avg frame ms in BloodMoon bucket vs avg in the implicit "everything else" bucket |
| "Which boss fight is the most expensive?" | ActiveBoss | Avg + peak frame ms over the bucket's lifetime |
| "Is my Jungle worse than Forest?" | Biome | Avg ms ranked across biome buckets |
| "I died at 87 ms — what was going on?" | Cross-dimensional | All buckets active during the spike tick, computed by re-walking the ring buffer |
| "Which mod is the worst Blood Moon offender?" | Weather × Mod | Per-bucket per-mod CPU; same model as `_perModSmoothedMs` but keyed by bucket |

Every other use case in this list reduces to either *rank one dimension's buckets by an aggregate* or *re-walk the ring buffer once for a specific tick's context*. There is no use case that genuinely requires storing the Cartesian product of dimensions in real time.

---

## 3. The Context model

### 3.1 The per-tick context, as a struct

```csharp
internal struct TickContext
{
    public long TickIndex;

    // ---- Time / weather / world events ----------------------------------
    public WeatherFlags Weather;       // [Flags] bitset of Day, BloodMoon, Eclipse, SlimeRain, PumpkinMoon, SnowMoon, Sandstorm, Party, LanternNight, Raining, Halloween, Christmas
    public bool        Hardmode;
    public GameMode    Mode;           // Classic, Expert, Master, Journey
    public InvasionId  VanillaInvasion;// None, Goblins, FrostLegion, Pirates, Martians, OldOnesArmy

    // ---- Biome ----------------------------------------------------------
    // BitArray with one bit per registered biome: 38 vanilla + N modded.
    // Allocated once at install with length = BiomeRegistry.Count.
    public BiomeBitset Biomes;

    // ---- Active bosses --------------------------------------------------
    // Pre-sized array of length BossSlots (= 8 — covers worst case Twins +
    // pillars + a worm + a duo). Each slot holds the NPC type after the
    // realLife collapse, or 0 for "no boss in this slot". Zero allocations
    // per tick.
    public BossSlotArray Bosses;

    // ---- Subworld (optional) --------------------------------------------
    public int SubworldKey;            // 0 = none / SWL not loaded;
                                       // otherwise a small int from a Dictionary<string,int>
                                       // keyed by SubworldSystem.Current.FullName
}

[Flags]
internal enum WeatherFlags : ushort
{
    None = 0,
    DayTime = 1 << 0,
    BloodMoon = 1 << 1,
    Eclipse = 1 << 2,
    SlimeRain = 1 << 3,
    PumpkinMoon = 1 << 4,
    SnowMoon = 1 << 5,
    Sandstorm = 1 << 6,
    BirthdayParty = 1 << 7,
    LanternNight = 1 << 8,
    Raining = 1 << 9,
    Halloween = 1 << 10,
    Christmas = 1 << 11,
}

internal enum GameMode : byte { Classic, Expert, Master, Journey }
internal enum InvasionId : byte { None = 0, Goblins = 1, FrostLegion = 2, Pirates = 3, Martians = 4, OldOnesArmy = 5 }
```

`TickContext` is a value type held inline in `TickFrame` (see §6). It contains no managed references — `BiomeBitset` and `BossSlotArray` are fixed-size structs wrapping `ulong[]` and `short[8]` respectively. A tick can be copied, hashed, and compared without allocation.

### 3.2 Why bitsets rather than per-biome bools

A `BitArray` (or our equivalent `BiomeBitset`) is the same shape `Player.modBiomeFlags` already uses — we are not inventing a representation, we are matching the one already in the runtime. Cost: `(NumBiomes + 63) / 64` ulongs per tick ≈ 16 bytes for a 100-biome registry. Read cost: O(1) `Get(int)`.

### 3.3 The Context Tagger

A new component, `ContextTagger`, owns the per-tick read of `Main` and `Main.LocalPlayer` into a `TickContext`. It does so on two cadences:

| Field | Cadence | Why |
|---|---|---|
| `Bosses` (Main.npc scan) | every tick | Bosses can spawn and despawn on a single tick; we already scan `Main.npc` for the active-count number. |
| `Weather` (Main.bloodMoon etc) | every 6 ticks (10 Hz) | These flip on human timescales (boss spawn, moon transition); per-tick read is wasted. |
| `Biomes` (Zone properties + `modBiomeFlags`) | every 6 ticks (10 Hz) | `Player.UpdateBiomes` itself runs at game-time cadence and updates these via tile counts; sub-tick precision is meaningless. |
| `Hardmode`, `Mode`, `SubworldKey` | every 60 ticks (1 Hz) | Effectively constant across a session. |
| `VanillaInvasion` | every 6 ticks | Same cadence as weather. |

Between reads, the previous tick's values are reused (cheap struct copy). The cadence is a single `_tickPhase++` integer mod 6/60 inside the tagger — no per-flag scheduling, no branch prediction churn.

Concretely, per tick:

| Cost line | Lite-mode budget |
|---|---|
| Boss scan (200 npcs × 2 bools × pred-friendly) | ~5 µs |
| Weather/event read (12 statics + struct write) every 6th tick | amortised ~0.5 µs |
| Biome read (38 cached delegate calls + modBiomeFlags clone) every 6th tick | amortised ~3 µs |
| Subworld probe (one method call) every 60th tick | amortised ~0.01 µs |
| Total amortised | **~ 8.5 µs/tick** |

At 60 fps that is ~0.05 % of frame budget. Lite mode's < 1 % budget is comfortable.

### 3.4 The dynamic registry, populated at install

```csharp
internal static class BiomeRegistry
{
    // One entry per discovered biome — vanilla and modded.
    public static IReadOnlyList<BiomeDescriptor> Biomes => _biomes;

    private static readonly List<BiomeDescriptor> _biomes = new();
    private static readonly Func<Player, bool>[] _vanillaGetters;      // index = biome id for id < VanillaCount
    private static readonly int[] _modBiomeFlagIndex;                  // index = biome id for id >= VanillaCount → bit position in Player.modBiomeFlags

    public static int VanillaCount { get; private set; }
    public static int Count => _biomes.Count;

    public static void Populate()
    {
        // 1. Vanilla — reflect over Player Zone* properties.
        Type playerType = typeof(Player);
        var vanillaProps = playerType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.Name.StartsWith("Zone", StringComparison.Ordinal)
                        && p.PropertyType == typeof(bool)
                        && p.GetMethod != null)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

        VanillaCount = vanillaProps.Length;
        var getters = new Func<Player, bool>[VanillaCount];
        for (int i = 0; i < VanillaCount; i++)
        {
            PropertyInfo prop = vanillaProps[i];
            // Bind the getter once; per-tick reads are then a direct delegate
            // invocation, not a reflective property fetch.
            getters[i] = (Func<Player, bool>)Delegate.CreateDelegate(
                typeof(Func<Player, bool>), prop.GetMethod!);
            _biomes.Add(new BiomeDescriptor(
                id: i,
                displayName: HumanReadable(prop.Name),  // "ZoneJungle" -> "Jungle"
                fullName: $"Vanilla:{prop.Name}",
                modName: null));
        }
        typeof(BiomeRegistry).GetField(nameof(_vanillaGetters), ...)!.SetValue(null, getters); // see note

        // 2. Modded — enumerate ModBiomes; each Type maps to a bit index
        //    in Player.modBiomeFlags.
        ModBiome[] modBiomes = ModContent.GetContent<ModBiome>().ToArray();
        var bitIndex = new int[modBiomes.Length];
        for (int i = 0; i < modBiomes.Length; i++)
        {
            ModBiome mb = modBiomes[i];
            int id = VanillaCount + i;
            bitIndex[i] = mb.Type;  // The bit slot inside Player.modBiomeFlags.
            _biomes.Add(new BiomeDescriptor(
                id: id,
                displayName: mb.DisplayName?.Value ?? mb.Name,
                fullName: mb.FullName,
                modName: mb.Mod.Name));
        }
        typeof(BiomeRegistry).GetField(nameof(_modBiomeFlagIndex), ...)!.SetValue(null, bitIndex);
    }

    private static string HumanReadable(string zoneName)
    {
        // "ZoneJungle" -> "Jungle"; "ZoneUndergroundDesert" -> "Underground Desert"
        string trimmed = zoneName.Substring("Zone".Length);
        return Regex.Replace(trimmed, @"(?<=[a-z])(?=[A-Z])", " ");
    }
}

internal readonly record struct BiomeDescriptor(int Id, string DisplayName, string FullName, string? ModName);
```

The static-field "set the read-only field once at install" pattern matches `PerModAttribution`'s shape today (see `Configure(modCount)`); the boring `private static readonly` is upgraded to `private static <type> _x = Array.Empty<>()` and assigned in `Populate`.

### 3.5 Per-tick biome read — concrete code

```csharp
public static void ReadInto(Player player, ref BiomeBitset dest)
{
    // Vanilla — cached delegate invocation.
    Func<Player, bool>[] getters = BiomeRegistry._vanillaGetters;
    for (int i = 0; i < getters.Length; i++)
    {
        if (getters[i](player)) dest.Set(i);
        else                    dest.Clear(i);
    }

    // Modded — read Player.modBiomeFlags directly. This is the same array
    // BiomeLoader.UpdateBiomes(player) populated this tick.
    BitArray flags = player.modBiomeFlags;   // nonpublic; bind once via reflection at install.
    int[] map = BiomeRegistry._modBiomeFlagIndex;
    int offset = BiomeRegistry.VanillaCount;
    for (int i = 0; i < map.Length; i++)
    {
        if (map[i] < flags.Length && flags[map[i]]) dest.Set(offset + i);
        else                                        dest.Clear(offset + i);
    }
}
```

`player.modBiomeFlags` is a nonpublic instance field — we resolve it once via reflection at install and bind it to a `Func<Player, BitArray>` delegate, mirroring the cached-getter pattern.

### 3.6 Boss scan — concrete code

```csharp
public static int ReadBossesInto(NPC[] npcs, Span<short> dest)
{
    int slot = 0;
    bool[] countsAsBoss = NPCID.Sets.ShouldBeCountedAsBoss;
    for (int i = 0; i < npcs.Length && slot < dest.Length; i++)
    {
        NPC npc = npcs[i];
        if (!npc.active) continue;

        // Collapse multi-segment bosses to their head.
        int headWhoAmI = npc.realLife >= 0 ? npc.realLife : i;
        if (headWhoAmI != i) continue;   // only the head contributes — segments fold into it

        int type = npc.type;
        if (!npc.boss && (type >= countsAsBoss.Length || !countsAsBoss[type]))
            continue;

        // Deduplicate: a tick can scan the same head twice only if two
        // distinct slots claim realLife to one another — defensive, normally
        // skipped.
        bool already = false;
        for (int k = 0; k < slot; k++) if (dest[k] == type) { already = true; break; }
        if (already) continue;

        dest[slot++] = (short)type;
    }
    return slot;
}
```

`BossSlots = 8`. Worst observed real session (Twins + pillars phase) populates 5. The 8 cap is intentional — even if a chaotic modded encounter exceeds it, the tagger truncates and logs a one-shot warning via `Mod.Logger.Warn`; no allocation, no overflow.

Friendly names are resolved lazily, only when a boss bucket is first opened (§5), via `Lang.GetNPCName(type).Value`. Bucket lookup is on the `(type)` int, not on the string.

---

## 4. Vanilla dynamic discovery research

### 4.1 Biomes — *fully dynamic, evidence-backed*

The 38-count came from reflection at the head of this document. The reflection technique is what `BiomeRegistry.Populate` does. A future tModLoader update that introduces a 39th `Zone*` property requires zero code change.

### 4.2 Weather / events — *semi-dynamic, declared once*

There is no central tModLoader API enumerating weather flags. The flags are scattered across:

```
Main.bloodMoon         Main.eclipse           Main.slimeRain         Main.pumpkinMoon
Main.snowMoon          Main.raining           Main.dayTime           Main.hardMode
Main.xMas              Main.halloween         Sandstorm.Happening    DD2Event.Ongoing
LanternNight.GenuineLanterns || .ManualLanterns
BirthdayParty.GenuineParty || .ManualParty
```

A reflection sweep would catch `Main` boolean fields but not `Sandstorm.Happening` (different type, different namespace). A purely-reflection design has to *also* know which types to scan, and that list is the same hardcoded list we are trying to avoid. The honest tradeoff: ship one small declarative table that lists `(WeatherFlag, () => bool)`, exposed as a single file the implementer edits when a new vanilla event drops.

```csharp
internal static class WeatherSources
{
    public static readonly (WeatherFlags Flag, Func<bool> Read)[] All = new (WeatherFlags, Func<bool>)[]
    {
        (WeatherFlags.DayTime,        () => Main.dayTime),
        (WeatherFlags.BloodMoon,      () => Main.bloodMoon),
        (WeatherFlags.Eclipse,        () => Main.eclipse),
        (WeatherFlags.SlimeRain,      () => Main.slimeRain),
        (WeatherFlags.PumpkinMoon,    () => Main.pumpkinMoon),
        (WeatherFlags.SnowMoon,       () => Main.snowMoon),
        (WeatherFlags.Sandstorm,      () => Sandstorm.Happening),
        (WeatherFlags.BirthdayParty,  () => BirthdayParty.GenuineParty || BirthdayParty.ManualParty),
        (WeatherFlags.LanternNight,   () => LanternNight.GenuineLanterns || LanternNight.ManualLanterns),
        (WeatherFlags.Raining,        () => Main.raining),
        (WeatherFlags.Halloween,      () => Main.halloween),
        (WeatherFlags.Christmas,      () => Main.xMas),
    };
}
```

12 lines. A new vanilla event adds one row. This is honest: the *Events* tab is not a reflection magic show; it is a measurement plane that uses reflection wherever reflection is sound and a declarative table wherever it isn't. The cost of being dishonest about that asymmetry is bigger than 12 lines of plumbing.

### 4.3 Bosses — *fully dynamic, evidence-backed*

`Main.npc[]` plus `npc.boss \|\| NPCID.Sets.ShouldBeCountedAsBoss[type]` plus `realLife` collapse covers vanilla and modded bosses uniformly. `Lang.GetNPCName(npc.type).Value` gives the localised display string. No hardcoded list.

### 4.4 Invasions — *vanilla = 4 ids, no modded enumeration in 1.4.4*

`Main.invasionType : int` is the live id (0 = none). Mapping to a friendly name via a 5-entry switch (`InvasionID.Goblins → "Goblin Army"`, etc.) is finite and stable. Old-Ones-Army is tracked separately via `DD2Event.Ongoing`. No tML API enumerates modded invasions today.

### 4.5 Subworld — *reflection probe, optional dependency*

```csharp
internal static class SubworldProbe
{
    private static PropertyInfo? _currentProp;
    private static MethodInfo? _anyActive;
    private static PropertyInfo? _fullName;   // on Subworld type
    private static readonly Dictionary<string, int> _keys = new();

    public static void Initialise()
    {
        Type? swSystem = Type.GetType("SubworldLibrary.SubworldSystem, SubworldLibrary", throwOnError: false);
        if (swSystem == null) return;
        _currentProp = swSystem.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
        _anyActive = swSystem.GetMethod("AnyActive", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
        Type? subworld = Type.GetType("SubworldLibrary.Subworld, SubworldLibrary", throwOnError: false);
        _fullName = subworld?.GetProperty("FullName", BindingFlags.Public | BindingFlags.Instance);
    }

    public static int Read()
    {
        if (_anyActive == null) return 0;
        bool any = (bool)_anyActive.Invoke(null, null)!;
        if (!any) return 0;
        object? current = _currentProp!.GetValue(null);
        if (current == null) return 0;
        string? full = _fullName!.GetValue(current) as string;
        if (full == null) return 0;
        if (!_keys.TryGetValue(full, out int key))
        {
            key = _keys.Count + 1;     // 0 reserved for "no subworld"
            _keys[full] = key;
        }
        return key;
    }
}
```

Cadence is 60 ticks (§3.3) so the reflective `Invoke` cost is negligible.

---

## 5. Modded dynamic discovery research

| Surface | Mechanism | Cost shape |
|---|---|---|
| `ModBiome` | `ModContent.GetContent<ModBiome>()` once at install; per-tick read via `Player.modBiomeFlags` bitset | One bit per modded biome, ~1 ns per biome per tick |
| `ModNPC` boss | Folded into the same `Main.npc[]` scan as vanilla bosses; no separate enumeration needed | None — already paid by the active-count scan |
| Modded invasions | None — no `ModInvasion` API exists in 1.4.4 | Documented gap |
| Modded subworlds | Via the SubworldLibrary reflection probe — works for *any* mod that uses SWL | One reflective call per minute |
| Modded events (custom Ongoing flags) | None — no public API | Documented gap; per-mod `Mod.Call` opt-in proposed for v2 |

The Mod.Call surface (sketched for v2 in §13) is the lever for closing the modded-events gap without coupling the profiler to any specific mod.

---

## 6. The honest gaps

The Events tab does **not** capture:

| Gap | Why | Impact | Plan |
|---|---|---|---|
| Modded "event" flags (Calamity's worldfire, Thorium's Strange Tides, FargosSouls's Eternity timers) | No registration API in tML 1.4.4; each mod's `Ongoing : bool` is a private static somewhere only that mod knows | A heavy modded event runs invisibly; its cost lands in whatever biome / weather buckets are also active at the time, blurring the attribution | Documented as `unattributed: <duration>` rows in the session log; offer the v2 Mod.Call opt-in (§13). |
| In-fight phase transitions | Phases live in `npc.ai[0..3]` and are interpreted differently per boss | Phase 2 of Cryogen reads as the same Cryogen bucket as phase 1 | Out of scope; an aspirational v3 feature with a per-boss adapter registry. |
| Sub-tick "moments" (e.g. "while parrying", "while channelling") | Not exposed as bool state; would require AI-loop instrumentation | Not addressed | Documented; not a goal of this milestone. |
| Lunar Apocalypse as a single phase | No `Main.LunarApocalypseIsUp` flag exists; we infer it from the four LunarTower NPC types being active | A pillar-by-pillar bucket sequence rather than one merged "Lunar Events" bucket | Acceptable; the per-pillar attribution is *better* than a merged bucket, because the four pillars cost very different amounts. The retrospective text can synthesise the union if a player asks for "the lunar event". |
| Modded biomes whose `IsBiomeActive` is expensive | Some mods compute tile counts inside it; reading every tick would amplify their cost | We use `modBiomeFlags`, set by tML inside `BiomeLoader.UpdateBiomes`, so we never call `IsBiomeActive` ourselves | No impact; mitigated by design. |
| Engagement-weighted bucket scoring | Out of scope for this tab; engagement is the Dormant tab's job (README §"The Dormant cost surface") | The Events tab is cost-only; engagement attribution is a separate plane | Acceptable; cross-linked in the UI (§9.6). |

These are surfaced honestly in the tab's UI footer: `* events not surfaced through a vanilla flag (modded worldfire etc.) are not attributed; ask the mod author to add a profiler hint`.

---

## 7. Sampling cadence and overhead

Cadence summary (already established §3.3, restated as a single ledger):

| Read | Frequency | Per-tick budget | Why this frequency |
|---|---|---|---|
| Boss scan | Every tick | ~5 µs | Bosses spawn/despawn on a single tick; the existing entity-count scan already pays this cost — we add ~2 bool checks per slot |
| Weather flags | Every 6 ticks (10 Hz) | ~3 µs/read → 0.5 µs amortised | These change at human pace; sub-tick precision is meaningless |
| Biome bitset | Every 6 ticks | ~3 µs/read → 0.5 µs amortised | `BiomeLoader.UpdateBiomes` runs at game-tick cadence; our 10 Hz follows it |
| Subworld probe | Every 60 ticks (1 Hz) | ~0.5 µs/read → 0.01 µs amortised | Effectively constant per session |
| Aggregator commit | Every tick | ~1 µs | One increment per active dimension bucket; ~5 increments typical |
| **Total amortised** | | **≈ 7 µs per tick (0.04 % at 60 fps)** | |

The 1 % Lite-mode budget has room for this and the rest of the Lite-mode instrumentation simultaneously.

Verification gate before Milestone-1 sign-off: run the in-game smoke test from `ILHook-migration-plan.md` §12e with the Events tagger enabled and disabled in alternating runs; the divergence must be ≤ 0.2 % of frame time at p50.

---

## 8. Bucket aggregation data model

### 8.1 Per-dimension storage

The pivotal design decision. Each context dimension owns its own bucket dictionary; one tick contributes to all relevant dimensions in parallel.

```
                                +----------------+
                                |  TickContext   |
                                +-------+--------+
                                        |
              +----------+----------+---+---+----------+----------+
              |          |          |       |          |          |
              v          v          v       v          v          v
        +---------+ +---------+ +-------+ +-------+ +--------+ +--------+
        | Biome   | | Weather | | Mode  | | Bosses| |Invasion| |Subworld|
        | buckets | | buckets | | bucket| |bucket | |bucket  | |bucket  |
        +---------+ +---------+ +-------+ +-------+ +--------+ +--------+
```

Each tick advances every dimension's currently-active bucket by 1 frame worth of ms.

```csharp
internal sealed class BucketAggregator
{
    public readonly int DimensionId;
    public readonly string Dimension;       // "Biome" / "Weather" / ...

    // Dimension-local key → bucket. Keys are dimension-specific ints
    // (biome id, weather flag bit, boss type, invasion id, subworld key).
    private readonly Dictionary<int, BucketStats> _buckets = new();

    public IReadOnlyDictionary<int, BucketStats> Buckets => _buckets;

    public void Accumulate(int key, double frameMs, bool isSpike)
    {
        if (!_buckets.TryGetValue(key, out BucketStats? b))
        {
            b = new BucketStats();
            _buckets[key] = b;
        }
        b.Add(frameMs, isSpike);
    }
}

internal sealed class BucketStats
{
    public long Ticks;
    public double SumMs;
    public double SumSqMs;
    public double PeakMs;
    public int SpikeCount;            // frames where frameMs > 2× session running mean
    public long FirstSeenTick;
    public long LastSeenTick;

    public void Add(double frameMs, bool isSpike)
    {
        if (Ticks == 0) FirstSeenTick = LastSeenTick;  // set by caller before Add
        Ticks++;
        SumMs += frameMs;
        SumSqMs += frameMs * frameMs;
        if (frameMs > PeakMs) PeakMs = frameMs;
        if (isSpike) SpikeCount++;
    }

    public double AvgMs => Ticks == 0 ? 0 : SumMs / Ticks;
    public double StdDevMs
    {
        get
        {
            if (Ticks < 2) return 0;
            double mean = AvgMs;
            double var = (SumSqMs / Ticks) - (mean * mean);
            return var < 0 ? 0 : Math.Sqrt(var);
        }
    }
    public double DwellSec => Ticks / 60.0;  // tModLoader's 60 Hz; not wall clock
}
```

### 8.2 Per-bucket per-mod attribution (the drill-down)

A row in the Events tab can be expanded to show *which mods were expensive during that bucket's lifetime*. This requires per-bucket per-mod stats — analogous to `PerModAttribution._modTickTicks` but keyed by bucket as well as by mod.

```csharp
internal sealed class BucketStats
{
    // ... fields above ...

    // Allocated lazily on first per-mod accumulation, sized to ModCount.
    private double[]? _perModMs;

    public IReadOnlyList<double>? PerModMs => _perModMs;

    public void AddPerMod(ReadOnlySpan<double> tickPerModMs)
    {
        if (_perModMs == null) _perModMs = new double[tickPerModMs.Length];
        for (int i = 0; i < _perModMs.Length; i++) _perModMs[i] += tickPerModMs[i];
    }
}
```

Storage cost: `ModCount * 8 bytes` per bucket *only when expanded*. For a 94-mod modlist that's ~750 bytes per bucket. A session with 60 buckets total carries ~45 KB of per-bucket per-mod data — negligible.

If we wanted *every* bucket pre-loaded with per-mod data, we'd hit ~750 B × N buckets. For Lite mode (default), per-mod accumulation runs **only for the currently-open bucket per dimension**, and back-fills lazily when the player clicks to expand a historical bucket (the data is in the ring buffer — re-walk and re-attribute). For Standard mode, always-on per-bucket per-mod accumulation. See §10 for the mode-tied feature matrix.

### 8.3 Cross-dimensional drill-down

"Cost in Jungle AND during Blood Moon" is computed on demand, not stored. The mechanism: walk the ring buffer's `TickFrame[]` (already a 30-second window), filter ticks whose `TickContext.Biomes.IsSet(JungleId) && (Context.Weather & WeatherFlags.BloodMoon) != 0`, sum their `FrameTimeMs`. Bounded at 1 800 frames; runs in microseconds.

For *historical* cross-dimensional queries (beyond the ring buffer), the session-log JSONL is the source — each per-tick entry carries its `TickContext`, and the post-session retrospective re-walks the file.

### 8.4 Storage cost ledger

| What | Per-session worst case | Why bounded |
|---|---|---|
| Biome buckets | 38 vanilla + N modded entries × ~50 B each | Set by registry |
| Weather buckets | 12 entries × ~50 B | Set by `WeatherSources` table |
| Boss buckets | ≤ 30 entries × ~50 B | A long session sees ≤ 30 distinct bosses |
| Subworld buckets | ≤ 5 × ~50 B | Few mods have many subworlds |
| Per-bucket per-mod arrays | 60 buckets × 94 mods × 8 B ≈ 45 KB | Drill-down only |
| **Total** | **< 100 KB** | |

There is no scenario where bucket explosion is a real risk under this per-dimension design.

---

## 9. UI / Tab integration

### 9.1 Where the tab strip lives

The current overlay header carries the title plus the `30S AVG` / `LIVE` toggles. Both belong to the current view (whether the per-mod numbers are averaged or live). Adding view-switching to the header is wrong: it conflates the *view selector* with the *per-view options*.

**New layout**: tab strip is a 22 px row inserted between the header and the stat block.

```
 ┌─ PERFORMANCE PROFILER ───────────────────────────────────── 30S AVG ▾  LIVE ▾ ─┐
 │ [ LIVE ]  [ EVENTS ]  [ BOSSES ]  [ HOT MOMENTS ] [ DORMANT ]                    │  ← new 22px strip
 ├──────────────────────────────────────────────────────────────────────────────────┤
 │ tick 18.4 ms    avg 30s 17.9 ms    gc 0.4 ms    npc 47  proj 132  dust 1422       │
 │ ...                                                                              │
```

The strip is drawn by `OverlayPanel.DrawTabStrip(...)`, takes panel-local Y range `[HeaderHeight, HeaderHeight + TabHeight)`. Active tab is rendered with the `Accent` colour and a 2 px underline, inactive tabs with `TextMuted`. Hover highlights match the per-mod row hover (`RowHover`).

Constants:

```csharp
private const float TabHeight    = 22f;
private const float TabPadding   = 12f;
private const float TabSpacing   = 4f;
// Each tab's measured width is recomputed every frame from its label using
// FontAssets.MouseText.Value.MeasureString(label).X * scale. No fixed widths;
// adding a new tab is a one-line _tabs[] addition.
```

The existing `30S AVG` and `LIVE` toggles stay in the header on the right; their meaning is now "options for the active tab" rather than "view selectors". Both the LIVE and EVENTS views read those flags identically — they're orthogonal to which tab is active.

### 9.2 The Events tab body

Above the per-row list is a one-line header echoing the *current* live context — what dimension buckets are open right now — so the user can see what feeds the rows without scrolling.

```
 ┌─ EVENTS ─────────────────────────────────────────────────────────────────────┐
 │ now active   Forest · Day · Hardmode · Master · Pirates (32%)                  │
 ├──────────────────────────────────────────────────────────────────────────────┤
 │ dimension  bucket               dwell    avg ms   peak   spikes              │
 │ ──────────────────────────────────────────────────────────────────────────── │
 │ ◆ Biome    Jungle               14:32   22.1 ms  87.3   12 ●     [ expand ]  │
 │   Biome    Forest               18:01   17.4 ms  41.0    3                   │
 │   Biome    Underground          02:14   19.8 ms  62.7    5                   │
 │   Weather  Day                  29:12   18.0 ms  87.3   18                   │
 │   Weather  Blood Moon ◆ active  00:42   24.6 ms  61.0    4 ●                 │
 │ ◆ Boss     Skeletron Prime      00:00   --       --      --                  │
 │   Boss     King Slime           00:54   25.3 ms  44.0    2                   │
 │   Invasion Pirates ◆ active     00:31   22.8 ms  53.0    3 ●                 │
 │   ... 14 more (scroll)                                                       │
 │ ──────────────────────────────────────────────────────────────────────────── │
 │ * modded events without a vanilla flag are not attributed                    │
 └──────────────────────────────────────────────────────────────────────────────┘
```

Notes on the layout:

- **`◆ active` marker** distinguishes "happening now" buckets so the player can see what the current cost is buying.
- **Per-dimension grouping** is implicit (the dimension column repeats) rather than visually nested — keeps each row independently sortable.
- **Drill-down**: clicking a row expands it inline, showing top mods *for that bucket*. Visual mirrors the per-mod tree expansion (`+` / `−` glyph, indented sub-rows).

### 9.3 Drill-down (expanded row)

```
 │ ◆ Biome    Jungle               14:32   22.1 ms  87.3   12 ●     [ -      ]  │
 │     mods that cost the most in this bucket                                   │
 │     Calamity Mod                                       7.4 ms ███████        │
 │     Spirit Reforged                                    2.9 ms ██             │
 │     Fargo's Souls Mod                                  1.8 ms █              │
 │     ... 17 more                                                              │
```

The per-mod numbers are read from `BucketStats.PerModMs[modId]`. If the bucket was never expanded before, the panel back-fills from the ring buffer on first expansion (Lite mode only — Standard mode keeps per-mod accumulation always on).

### 9.4 Colour grading

Same green→amber→red gradient as the per-mod tree, but scaled against *the session baseline average frame ms*, not against the bucket's own max. A bucket whose avg is ≤ 1.0 × session mean is green; 1.0–1.5 × is amber; > 1.5 × is red. This is the *honest* contract: a bucket isn't "expensive" because it's the worst of the buckets, it's expensive because it deviates from typical session behaviour.

### 9.5 Pruning

A bucket with < 60 ticks (1 second of dwell) is not rendered as a row, but its data is still accumulated. This prevents the first frames of a session from flashing 15 nearly-empty rows. Once it crosses the threshold it appears with its full history.

### 9.6 Cross-link to Dormant tab

A bucket marked `◆ active` and contributing > 5 % of frame time, *whose top mod by per-bucket cost matches a mod the Dormant tab has zero engagement on*, shows a faint `→ Dormant` arrow in the right margin. Clicking switches to the Dormant tab pre-filtered to that mod. This is the only cross-tab link in v1; everything else stays self-contained.

### 9.7 What stays out of v1

Not in this tab's first version:

- Editing or muting buckets.
- Pinning a bucket to compare against another.
- Per-bucket spike timeline visualisation.
- A separate **Boss Fights** tab (the shape is similar enough that the boss-dimension rows already cover the workflow; promote to a dedicated tab in v2 if duration sorting and outcome tracking become first-class).

### 9.8 What the user explicitly does NOT want

**This is not a compact status bar.** Earlier brainstorming sketched a single-line "Currently in Jungle · Blood Moon · King Slime" badge on the existing overlay. That shape is not what the user is asking for. The Events tab is a *first-class view* with real per-bucket aggregation, drill-down, and per-tick re-walks for cross-dimensional queries. A future implementer should not shortcut to the status-line version even if it "looks similar" — it is a different feature.

---

## 10. Mode-tied feature matrix

| Feature | Lite (default) | Standard | Deep |
|---|---|---|---|
| Per-dimension bucket aggregation | ✓ | ✓ | ✓ |
| Boss scan cadence | every tick | every tick | every tick |
| Weather/biome sampling cadence | 10 Hz | 60 Hz | 60 Hz |
| Per-bucket per-mod attribution | **lazy** (on expand) | always-on for live buckets | always-on, all buckets |
| Per-tick context written to JSONL | header only | every tick, batched | every tick, plus `npc.ai[0..3]` for active bosses |
| Cross-dimensional re-walk | last 30 s only | last 30 s + on-demand session-log re-walk | always-on session-log re-walk into a separate index |
| Bucket count budget | unbounded but pruned | unbounded | unbounded |

The Lite-mode numbers stay inside the < 1 % budget. Standard adds the per-bucket per-mod arrays — ~45 KB total — and the higher sampling cadence (8 µs → 50 µs amortised per tick, still < 0.3 %). Deep mode is the diagnostic tier and is allowed to spend up to 10 %.

---

## 11. Session log integration

The existing `SessionLogWriter` writes a single periodic JSON report (`current-session.json`) and a final summary on world exit. Extend the schema rather than fork it.

### 11.1 New top-level field `events`

```json
{
  "schema": 3,
  "identity": "<hash>",
  "state": "final",
  "session": { ... existing ... },
  "mods": [ ... existing ... ],
  "coverage": { ... existing ... },
  "timeline": [ ... existing ... ],
  "spikes": [ ... existing ... ],
  "events": {
    "registry": {
      "biomes": [
        { "id": 0, "displayName": "Beach",  "fullName": "Vanilla:ZoneBeach", "mod": null },
        { "id": 17, "displayName": "Underground Jungle", "fullName": "Vanilla:ZoneJungle", "mod": null },
        { "id": 39, "displayName": "Astral Infection", "fullName": "CalamityMod/AstralInfection", "mod": "CalamityMod" }
      ],
      "weather":   [ "DayTime", "BloodMoon", "Eclipse", ... ],
      "invasions": [ "Goblins", "FrostLegion", "Pirates", "Martians", "OldOnesArmy" ],
      "subworlds": [ "TerraScience:OreCave", "Cascade:CosmicCave" ]
    },
    "buckets": [
      { "dim": "Biome",    "key": "Vanilla:ZoneJungle",      "dwellTicks": 52320, "avgMs": 22.1, "peakMs": 87.3, "stdDevMs": 3.4, "spikes": 12 },
      { "dim": "Weather",  "key": "BloodMoon",               "dwellTicks": 2520,  "avgMs": 24.6, "peakMs": 61.0, "stdDevMs": 4.1, "spikes": 4 },
      { "dim": "Boss",     "key": "NPCID:35:KingSlime",      "dwellTicks": 3240,  "avgMs": 25.3, "peakMs": 44.0, "stdDevMs": 5.2, "spikes": 2 }
    ],
    "transitions": [
      { "tick": 1234, "dim": "Biome",   "from": "Vanilla:ZoneForest",  "to": "Vanilla:ZoneJungle" },
      { "tick": 2580, "dim": "Weather", "added": ["BloodMoon"],        "removed": [] },
      { "tick": 9012, "dim": "Boss",    "added": ["NPCID:35"],         "removed": [] },
      { "tick": 9874, "dim": "Boss",    "added": [],                   "removed": ["NPCID:35"] }
    ]
  }
}
```

### 11.2 Bump `SchemaVersion` from 2 → 3

`SessionLogWriter.SchemaVersion` controls the identity hash (line 562: `ComputeIdentity` includes `schema={SchemaVersion}`); bumping it invalidates old session files via `PruneIncompatibleLogs`. This is the existing migration mechanism — no new code, just a constant bump.

### 11.3 Per-tick context (optional, Deep mode only)

Lite and Standard write only transition events. Deep mode writes a separate compressed line per tick with the full `TickContext`. The cost is real (~80 bytes/tick × 60 fps × 60 s = ~280 KB/minute) but bounded; Deep mode is the diagnostic tier.

### 11.4 Where the data goes

Same path as today: `<AppData>/Terraria/tModLoader/PerformanceProfiler/Sessions/<identity>-<stamp>.json`. The `events` block adds < 50 KB to a representative session file.

---

## 12. Step-by-step implementation sequence

Discovery passes 1–3, execution passes 4–13. Each step lists files, verification, and risk.

| # | Action | Files | Verify | Risk |
|---|---|---|---|---|
| **1** | Read `README.md`, `Profiling/TickFrame.cs`, `Profiling/MetricCollector.cs`, `Profiling/ProfilerSystem.cs`, `Profiling/SessionLogWriter.cs`, `UI/ProfilerOverlay.cs` cover-to-cover; enumerate the existing public symbols this plan extends (`TickFrame`, `MetricCollector.History`, `ProfilerOverlay.OverlayPanel`, `SessionLogWriter.SchemaVersion`). | read-only | Confirm symbols match this plan's references | **Low** — read-only |
| **2** | Grep for any existing `Encounter`, `Biome`, `Boss` symbol; the `TickFrame` doc-comment promises Context fields will arrive when their owners are built — confirm no half-built type exists | grep | Zero hits beyond the doc-comment promise in `TickFrame.cs` | **Low** |
| **3** | Run the reflection probe (the one used to populate §0) against the local tModLoader install and dump current Zone* property names; this becomes the baseline for the install-time diff log (§7 R10) | `tools/probe.cs` or a Bash one-shot — not committed | Baseline list matches §0's 38 entries | **Low** |
| **4** | Add `Profiling/Context/` folder with: `TickContext.cs` (the struct), `WeatherFlags.cs`, `GameMode.cs`, `InvasionId.cs`, `BiomeBitset.cs`, `BossSlotArray.cs`, `BiomeDescriptor.cs`. All pure data; no tModLoader dependency. | new files | `dotnet msbuild` succeeds; nothing else references them yet | **Low** — isolated |
| **5** | Add `Profiling/Context/BiomeRegistry.cs`, `Profiling/Context/WeatherSources.cs`, `Profiling/Context/SubworldProbe.cs`, `Profiling/Context/ContextTagger.cs`. Wire `BiomeRegistry.Populate()` and `SubworldProbe.Initialise()` into `HookInterceptor.Install(Mod)` *after* per-mod enumeration (so all `ModBiome`s are loaded). Log a one-line summary to `Mod.Logger.Info` describing the discovered counts (`"context: 38 vanilla biomes, 42 modded biomes, 12 weather flags, subworld=true"`). | new files; `Profiling/HookInterceptor.cs` 1-line addition | `dotnet msbuild` succeeds; in-game, log line appears at world-load and lists realistic counts | **Medium** — the modBiomeFlags reflection-binding is the one fragile spot; verify with a 2-mod test modlist before a 90-mod test |
| **6** | Add `Profiling/TickContext` field to `TickFrame.cs`. Initialise per tick via `ContextTagger.Snapshot(ref frame.Context)`. Existing serialisation paths ignore the new field (they walk explicit field names); no breakage. | `TickFrame.cs`, `MetricCollector.cs` (call the snapshot in `EndTick`) | `dotnet msbuild` succeeds; the existing overlay still renders identical numbers; `ContextTagger.Snapshot` measured at ≤ 10 µs/tick on a 90-mod modlist | **Medium** — this is the per-tick cost gate; do not proceed if it exceeds 20 µs |
| **7** | Add `Profiling/EventAggregator.cs` housing the per-dimension `BucketAggregator` instances and the `Accumulate(TickContext, double frameMs)` entry point. Wire from `MetricCollector.EndTick` after the existing per-mod harvest. Lite mode skips per-bucket per-mod accumulation. | new file; `MetricCollector.cs` 5-line addition | `dotnet msbuild` succeeds; in-game, after 30 s in Forest the Forest bucket has ~1 800 ticks and a sensible avg ms | **Medium** — wrong aggregator wiring shows up as zero ticks or wildly wrong avgs |
| **8** | Bump `SessionLogWriter.SchemaVersion` from 2 → 3. Extend the report object to include the `events` block per §11. Add `EventAggregator.SnapshotRegistry()` and `EventAggregator.SnapshotBuckets()` helpers returning the JSON-friendly shapes. Transition recording: `EventAggregator` tracks the previous `TickContext` and emits a transition row on each diff. | `SessionLogWriter.cs`, `EventAggregator.cs` | `dotnet msbuild` succeeds; existing session-file readers ignore unknown blocks; the `events.buckets` block matches the in-game observations from step 7 | **Medium** — JSON shape mistakes are visible immediately on file inspection |
| **9** | Add tab strip to `UI/ProfilerOverlay.cs`. Refactor the existing draw logic into a `Tab` enum (`Live`, `Events`) and a `DrawSelf` dispatch on the active tab. The current per-mod tree becomes `Tab.Live`'s body. Header toggles stay in place. | `UI/ProfilerOverlay.cs` (substantial refactor) | `dotnet msbuild` succeeds; in-game, two tabs visible, `LIVE` renders the current view unchanged, `EVENTS` shows an empty placeholder | **High** — biggest single change in the UI surface; restrict refactor to extracting `DrawLiveBody` from `DrawSelf` without moving the per-mod logic |
| **10** | Implement `DrawEventsBody` per §9: now-active line, sortable rows, hover, drill-down expansion, `◆ active` marker, colour grading against session-mean. Reuse `ProfilerTheme` colours and `DrawBar` for visual consistency. | `UI/ProfilerOverlay.cs` (~250 lines added) | `dotnet msbuild` succeeds; in-game, every bucket the aggregator holds appears as a row; expand/collapse works; rows hover-highlight consistently | **Medium** — visual polish iterations expected |
| **11** | Add bucket-pruning thresholds and the `◆ active` runtime marker. Add cross-dimensional re-walk for the "currently-spiking tick" context display in the now-active line. | `UI/ProfilerOverlay.cs` | In-game, the now-active line updates within 100 ms of context changes; rows below 60 ticks dwell hide | **Low** |
| **12** | Write `context/notes/events-tab.md` capturing the implementation decisions, the reflection-probe baseline, and the modBiomeFlags-direct-read rationale. | `context/notes/` | User reviews and accepts | **Low** — documentation |
| **13** | Commit at logical checkpoints: (a) context types + registry + tagger (steps 4-6), (b) aggregator + session-log schema (steps 7-8), (c) tab strip refactor (step 9), (d) events tab body + polish (steps 10-11), (e) context note (step 12). | git | Each commit builds and runs in-game; per-mod tree is visually unchanged after every commit until step 9 lands | **Low** — discipline |

---

## 13. Future opt-in: per-mod event Mod.Call

The honest gap in §6 is modded events. The closure is a small `Mod.Call` surface a mod can opt into:

```csharp
// Inside any mod's Mod.Call handler:
case "ReportEventStart":
    return PerformanceProfiler.EventBus.ReportStart(
        args: (string)args[1],   // event name, e.g. "CalamityMod:Worldfire"
        category: (string)args[2]); // "Weather" | "World" | "Encounter"
case "ReportEventEnd":
    return PerformanceProfiler.EventBus.ReportEnd((string)args[1]);
```

Lifetime of the event is bounded by paired Start/End calls; the profiler synthesises a dimension bucket the same way it does for vanilla `Sandstorm.Happening`. Documented in the profiler's `description.txt` and on the Workshop page; not a v1 commit.

This is the right escape valve: it does not require the profiler to know about each mod, and it does not require any mod to depend on the profiler (the `Mod.Call` is optional and degrades silently if the profiler is not loaded).

---

## 14. Risk register

Risks specific to this feature, beyond what §1 already enumerated:

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| **R-A** | A future tModLoader release changes the `modBiomeFlags` field name or visibility | Low | Medium — Modded biome detection silently goes to zero | The reflection binding logs `Logger.Warn` if `modBiomeFlags` is null on install; the profiler falls back to calling `Player.InModBiome(biome)` per modded biome at the cost of per-tick overhead, and surfaces the degradation as a `coverage` row in the session log |
| **R-B** | A modded biome's `ModBiome.Type` is reused or recycled across the session (e.g. mod reload) | Negligible | Negligible | `ModBiome.Type` is stable for a session; a `Mods → Reload` triggers our `Unload`, which clears `BiomeRegistry`. |
| **R-C** | The boss scan's `realLife` collapse misclassifies a non-segmented modded boss whose `ModNPC` sets `realLife` for non-standard reasons | Very low | Low | `realLife != -1` is the documented multi-segment idiom; misuse is a mod bug, not ours. We log a one-shot warn the first time we collapse a non-`boss` NPC into a head. |
| **R-D** | `Lang.GetNPCName(type).Value` returns a placeholder for an unloaded NPC | Negligible | Cosmetic | Falls through to `npc.FullName` then to `"NPCID:" + type`. Bucket key is the int anyway. |
| **R-E** | A modded subworld changes its `FullName` mid-session | Negligible | Cosmetic | We key on FullName at first observation; subsequent visits hit the cached key. A rename mid-session would create a second bucket — acceptable. |
| **R-F** | `ContextTagger.Snapshot` runs on the main thread and can be paused by debugger / GC pause, distorting "dwell" if measured in wall-clock seconds | Low | Low | Dwell is measured in *ticks*, not wall-clock — a paused frame doesn't count as bucket dwell. |
| **R-G** | The tab strip refactor in step 9 introduces a regression in the existing per-mod tree | Medium | Medium | Step-9 acceptance criterion: the per-mod tree is *visually identical* to before, screenshot-comparable. Restrict the refactor to `DrawSelf` → `DrawLiveBody` extraction; no logic moves. |
| **R-H** | Player has not loaded yet when `ContextTagger.Snapshot` first runs (e.g. between `Mod.Load` and `OnWorldLoad`) | Medium | Crash if not handled | Snapshot starts only inside `MetricCollector.EndTick`, which itself runs only between `BeginTick`/`EndTick`, which run only between `OnWorldLoad`/`OnWorldUnload`. By construction `Main.LocalPlayer` is non-null. Defensive null check still added. |

None of these change the viability verdict.

---

## 15. Testing strategy

Four layers, matching the four Project Invariants plus the dual-surface observability contract.

### 15a. Reflection-probe regression

**Hypothesis:** the reflection enumeration discovers exactly 38 vanilla biome properties on the install used by this plan; deviation indicates a tML schema change worth investigating.

**Steps:**

1. At install, log `context: vanilla biomes = N` with the comma-separated property names (debug level).
2. On every load: compare against a committed baseline list (`Profiling/Context/VanillaBiomeBaseline.cs`, 38 strings).
3. If the set differs, log `Logger.Warn` with the diff.

**Pass criterion:** baseline matches; warns are absent on tModLoader 1.4.4.9; the warning fires correctly on a synthetic test (rename a Zone property in a local tML build) and does not crash.

### 15b. Bucket-correctness regression

**Hypothesis:** for a 60-second session held entirely in one Zone with no events, exactly that bucket and the `DayTime` bucket should accumulate ~3 600 ticks each; every other bucket should hold 0.

**Steps:**

1. Spawn in Forest at noon; stand still 60 seconds; exit.
2. Read `current-session.json`'s `events.buckets`.
3. Assert: `Biome=Forest.dwellTicks ∈ [3500, 3700]`; `Weather=DayTime.dwellTicks` similarly; `Weather=BloodMoon.dwellTicks == 0`; no boss buckets; no invasion buckets.

**Pass criterion:** all assertions hold.

### 15c. Multi-dimensional accumulation

**Hypothesis:** standing in Snow during Blood Moon advances both the `Snow` and `BloodMoon` buckets in lockstep.

**Steps:**

1. World with Blood Moon forced on; spawn in Snow; stand still 30 seconds.
2. Read session log; compute `|Snow.dwellTicks - BloodMoon.dwellTicks| / max(Snow.dwellTicks, BloodMoon.dwellTicks)`.

**Pass criterion:** ≤ 0.01 — i.e. less than 1 % drift between the two buckets. Drift > 1 % implies the tagger missed a tick somewhere (different cadence buckets accumulating against the wrong context snapshot).

### 15d. Multi-segment boss collapse

**Hypothesis:** an Eater of Worlds fight (head + 69 segments) produces exactly one boss bucket keyed on the head NPC type, not 70.

**Steps:**

1. Summon Eater of Worlds (Worm Food in Corruption); fight to death.
2. Read session log.
3. Assert `events.buckets.where(dim == "Boss").count == 1` and key matches `NPCID:13` (Eater of Worlds Head).

**Pass criterion:** exactly one boss bucket per fight.

### 15e. Cadence and overhead

**Hypothesis:** with Lite mode and the Events tab enabled, total `MetricCollector.EndTick` cost ≤ 1.2 × what it costs with the tab disabled.

**Steps:**

1. Build a measurement scaffold: `Stopwatch` around `MetricCollector.EndTick`, sum into a static counter, log every 600 ticks.
2. Two runs of 60 seconds at identical gameplay (entering a fixed world, standing still in Forest):
   - Run A: with `ContextTagger.Snapshot` short-circuited to a no-op.
   - Run B: with full tagger active.
3. Compare the per-tick averages.

**Pass criterion:** Run B's average ≤ 1.2 × Run A's average. Larger ratios trigger investigation of the boss scan or the modded biome bit read.

### 15f. Subworld probe with and without SubworldLibrary

**Hypothesis:** the probe binds when SWL is loaded, returns null when it isn't, and never throws.

**Steps:**

1. Without SWL: launch, enter world, exit; confirm `Logger.Info` reports `subworld=false` and no exceptions.
2. With SWL loaded (one of the few mods that opts into it): launch, enter world, exit; confirm `subworld=true` and the registry includes at least the main-world key.

**Pass criterion:** no exception in either run; the boolean logged matches reality.

### 15g. In-game smoke test (per CLAUDE.md operating loop)

A 10-minute mixed session covering: Forest → Underground Jungle → into a Blood Moon → into King Slime → into Snow → into Goblin Invasion. Acceptance:

- All six dimensions populate appropriate buckets.
- The Events tab renders without flicker.
- Bucket rows hover-highlight consistently with the per-mod tree.
- `current-session.json` is well-formed JSON and includes the `events` block.
- `client.log` shows the `context: ... biomes ...` install line and no `Warn`/`Error` from `ContextTagger` or `EventAggregator`.

### 15h. Failure-mode triage

| Symptom | Likely cause | First check |
|---|---|---|
| Every `events.buckets[*].dwellTicks` is 0 | `EventAggregator.Accumulate` not called from `MetricCollector.EndTick` | Add a debug counter in `Accumulate` and log every 60 ticks |
| Modded biome buckets all show `dwellTicks=0` even when the player is in one | `Player.modBiomeFlags` binding failed | Inspect `Logger.Warn` at install; fall back to `Player.InModBiome` per biome |
| Boss bucket has 70 distinct keys for one Eater of Worlds fight | `realLife` collapse skipped | Verify `headWhoAmI != i` guard in `ReadBossesInto` |
| Per-tick overhead spikes to 100 µs+ | A modded biome's `IsBiomeActive` is being called (fell back from the bitset path) | Inspect install log for the fallback warning; identify the offending mod |
| `current-session.json` missing `events` block | `SchemaVersion` not bumped | Check `SessionLogWriter.SchemaVersion = 3` |
| Tab strip overlaps the stat block | `RowsTopOffset` not adjusted | `RowsTopOffset += TabHeight` in `OverlayPanel` |

---

## 16. What does and does not change in the codebase

| File | Change |
|---|---|
| `Profiling/Context/TickContext.cs` | **New.** Per-tick context struct |
| `Profiling/Context/WeatherFlags.cs` | **New.** `[Flags]` enum |
| `Profiling/Context/GameMode.cs` | **New.** Enum |
| `Profiling/Context/InvasionId.cs` | **New.** Enum |
| `Profiling/Context/BiomeBitset.cs` | **New.** Fixed-size bitset struct |
| `Profiling/Context/BossSlotArray.cs` | **New.** Fixed-size `short[8]` wrapper struct |
| `Profiling/Context/BiomeDescriptor.cs` | **New.** Record struct |
| `Profiling/Context/BiomeRegistry.cs` | **New.** Reflection-driven biome enumeration |
| `Profiling/Context/WeatherSources.cs` | **New.** Declarative table of vanilla weather flag readers |
| `Profiling/Context/SubworldProbe.cs` | **New.** Reflection probe for SubworldLibrary |
| `Profiling/Context/ContextTagger.cs` | **New.** Per-tick snapshot |
| `Profiling/Context/VanillaBiomeBaseline.cs` | **New.** Committed baseline list (38 strings) for the diff log |
| `Profiling/EventAggregator.cs` | **New.** Per-dimension bucket aggregation |
| `Profiling/EventBus.cs` | **New (stub).** Forward-compat surface for §13 Mod.Call; empty in v1 |
| `Profiling/TickFrame.cs` | **Add** `public TickContext Context;` field |
| `Profiling/MetricCollector.cs` | **Add** `EventAggregator` field; call `ContextTagger.Snapshot` in `EndTick` and `EventAggregator.Accumulate(frame.Context, frame.FrameTimeMs)` after the per-mod harvest |
| `Profiling/HookInterceptor.cs` | **Add** one line in `Install`: `BiomeRegistry.Populate(); SubworldProbe.Initialise();` after the mod enumeration. Log the discovered counts |
| `Profiling/ProfilerSystem.cs` | **No change** beyond what `MetricCollector` already exposes |
| `Profiling/SessionLogWriter.cs` | **Bump** `SchemaVersion` to 3. **Add** `events` block per §11; helpers consult `EventAggregator.SnapshotRegistry()` and `SnapshotBuckets()`. **Add** transition stream tracker that diffs `prev`/`current` `TickContext` and appends rows |
| `UI/ProfilerOverlay.cs` | **Refactor** `DrawSelf` to a tab dispatcher; **add** `DrawTabStrip`, `DrawEventsBody`, the now-active line, drill-down expansion. Reuse `ProfilerTheme` |
| `UI/ProfilerTheme.cs` | **No change** |
| `PerformanceProfiler.cs` | **No change** |
| `PerformanceProfiler.csproj` | **No change**. No new dependencies |
| `build.txt` | **No change**. SubworldLibrary remains an optional reflection-probed dependency |
| `context/notes/events-tab.md` | **New** capture of the implementation decisions after the work lands |
| `context/_Overview.md` | **Recommended edit** noting the new Context Tagger / EventAggregator components and the schema-3 bump. Confirm with user before editing |

---

## 17. Rollback plan

The Events tab is additive: every file in the §16 "New" list can be deleted, every "Add" can be reverted, the `SchemaVersion` reverted to 2, and the project returns to its pre-feature state.

Two checkpoints make rollback safe:

- **Step 6 / 7 boundary.** If overhead measurements exceed budget after step 7 (the aggregator wiring), `git revert` the step-7 commit; the context machinery stays installed but no buckets accumulate. The codebase is in a "tagger runs, aggregator silent" state — safe and inexpensive.
- **Step 9 boundary.** If the tab-strip refactor regresses the per-mod tree, `git revert` step 9 only; the Live view returns to its pre-refactor form. Steps 10–11 are stacked on top of 9, so a revert here is a feature-pause, not a bug-fix; the events backend stays in place and the data is still being written to session logs.

There is no on-disk state outside the existing session-log directory; pruning happens via the existing `PruneIncompatibleLogs` on schema mismatch.

---

## Honest summary

The Events tab is mostly a measurement problem and only secondarily a UI problem. The measurement side resolves into:

- 38 cached `Func<Player, bool>` getters for vanilla biomes (reflected once).
- One direct read of `Player.modBiomeFlags` for modded biomes.
- A 12-row declarative table for weather/event flags (the one honest hardcode in the design, scoped tightly).
- An extension of the existing `Main.npc[]` scan with a `realLife` collapse for bosses.
- A reflection probe for SubworldLibrary that costs nothing when SWL isn't loaded.

The aggregation side is per-dimension `BucketAggregator` instances — small, bounded, and parallel — with cross-dimensional queries computed on demand from the ring buffer and the session log. No Cartesian-product bucket explosion is possible by construction.

The UI side is a tab strip below the header plus a sortable row list, reusing every primitive (`DrawBar`, `ProfilerTheme`, the hover/expand pattern) that already exists for the per-mod tree.

The largest honest uncertainty is the per-tick overhead of the boss scan once it adds the `ShouldBeCountedAsBoss` check. The mitigation (step 6's measurement gate) is mechanical: if it costs more than 20 µs per tick we do not proceed to step 7. The plan is calibrated so that doesn't happen on the modlists we expect; if a modlist breaks the assumption, the failure is visible at step 6 before any UI work has begun.

The user's "dynamic everywhere possible" demand is met for the dimensions where it can be met honestly (biomes, bosses, subworlds), and the residual hardcoded surface (the weather/event table, the invasion id switch) is small, declarative, and traceable to specific tModLoader 1.4.4 API limits documented in §0. The plan does not pretend the residual is zero, and it does not pretend the biome-name-display mods have already solved this; the reflection technique is our own application of a standard pattern, evidence-grounded in §0, with the costs and risks named.
