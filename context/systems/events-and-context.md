# Events and Context

*Maturity: working · Stability: unstable — the per-tick transition stream the gated context-correlated detectors need is still missing.*

## Scope / Purpose

The events-and-context subsystem snapshots per-tick game state (biome, boss, weather, invasion, subworld) into `EventContext` values that travel inside every `TickFrame.Context`, then aggregates those snapshots into per-dimension bucket statistics consumed by the EventsTab and (eventually) the gated context-correlated detectors.

This is the layer that answers "what was happening in the game when this cost was paid?" without which CPU attribution can only be by mod, not by situation.

## Boundaries / Ownership

The snapshotter and the aggregator moved into `Data/` in v0.11; the support data structures stayed in `Profiling/Events/`.

In `Data/`:

- `Data/Collectors/ContextTagger.cs` — per-tick snapshotter
- `Data/Aggregators/EventAggregator.cs` — per-dimension bucket aggregation

In `Profiling/Events/`:

- `EventContext.cs` — the per-tick value struct
- `BiomeRegistry.cs` — vanilla + modded biome enumeration
- `BiomeBitset.cs` / `BiomeDescriptor.cs` — packed biome representation
- `BossSampler.cs` / `BossSlotArray.cs` — boss identity + segmented-boss dedup
- `BucketStats.cs` — per-bucket rolling stats
- `SubworldProbe.cs` — optional SubworldLibrary reflection probe
- `WeatherFlags.cs` / `WeatherSources.cs` — weather state
- `GameMode.cs`, `InvasionId.cs` — enum types

Owns:

- Per-tick context snapshotting at `PostUpdateEverything`.
- Vanilla and modded biome enumeration.
- Boss identity with multi-segment dedup via `NPC.realLife`.
- Optional SubworldLibrary probe (resolved by reflection, abort-clean per Invariant 4).
- Per-dimension bucket aggregation feeding the EventsTab.

Does not own:

- The tick lifecycle — `ProfilerSystem` owns when `Snapshot()` and `Accumulate()` are called.
- The bucket UI — the live surface is the browser dashboard (`systems/web-dashboard.md`); the archived in-game EventsTab lived under `UI/`.
- The context-correlated detectors — gated, see `systems/insights-engine.md`.

## Current Implemented Reality

### `EventContext`

Per-tick value type (`Profiling/Events/EventContext.cs`). Fields include:

- Biome (packed via `BiomeBitset`).
- Active boss (`BossId`, via `BossSampler`).
- Weather flags (`WeatherFlags`).
- Vanilla invasion id (`InvasionId`).
- Subworld id (when `SubworldProbe` is available).
- Game mode.

Stamped into `TickFrame.Context` by `ContextTagger.Snapshot(tickIndex)` after `EndTick` has pushed the frame.

### `ContextTagger.Snapshot(tickIndex)`

Reads:

- `Player.ZoneJungle` / `ZoneSnow` / `ZoneCorrupt` / ... via `BiomeRegistry`'s reflection-resolved field map.
- `ModBiome.IsBiomeActive(Main.LocalPlayer)` for every registered modded biome.
- `BossSampler.Current()` — the active boss, deduping segmented bosses by `NPC.realLife`.
- `Main.bloodMoon`, `Main.eclipse`, `Main.pumpkinMoon`, `Main.snowMoon`, `Main.invasionType`.
- `SubworldProbe.CurrentId()` if `SubworldProbe.Available`.

Produces a fresh `EventContext` value into a writeable field; the next consumer (`EventAggregator`) reads it.

### `BiomeRegistry`

Populated once at `PostSetupContent` (`ProfilerSystem.PostSetupContent`). Two parts:

1. Vanilla biomes: resolved via reflection over `typeof(Terraria.Player)`'s `bool ZoneX` fields. Field names that match are added to the registry; missing or mistyped names are simply absent — abort-clean per Invariant 4.
2. Modded biomes: enumerated via `ModContent.GetContent<ModBiome>()` (or equivalent tModLoader content scan). Each gets an `IsBiomeActive` probe stored.

`BiomeRegistry.ModBiomeBindingOk` reports whether the modded-biome enumeration succeeded; logged by `ProfilerSystem.PostSetupContent` at info level.

### `BossSampler`

Iterates `Main.npc[]`, finds NPCs with `NPC.boss == true`, deduplicates multi-segment bosses by following `NPC.realLife` to the head segment, returns one `BossId` representing the active boss. `Clear()` is called from `OnWorldUnload`.

### `EventAggregator.Accumulate(in EventContext, frameMs)`

For each dimension (biome, boss, weather, invasion, subworld, gameMode):

- Look up (or insert) the bucket for the dimension's current value.
- Update `BucketStats`: tick count, sum-of-FrameTimeMs, sum-of-squares (for variance), min, max.

Per-tick allocation: zero. Buckets are inserted lazily (on first observation) but the insert is rare in steady state.

### EventsTab (archived overlay)

Part of the in-game overlay archived in v0.9.0 (lives under `UI/`, no longer instantiated); the live event surface is the browser dashboard (`systems/web-dashboard.md`). The rendering detail below is retained because it documents the bucket read pattern any renderer reuses.

Reads the per-dimension bucket maps and renders rows. Each row carries a NOW-context summary (the bucket the current tick belongs to) plus aggregate stats.

`_cachedNowSummary` is rebuilt at 1 Hz from `EventAggregator.Latest` via `ComputeNowActiveSummary`; the per-frame `Draw` just reads the cached string. Pre-fix this allocated a fresh `List<string>` + `string.Join` per Draw — flagged by the audit overlay-ui findings.

### `SubworldProbe`

Reflection probe for SubworldLibrary, a popular tModLoader add-on that adds parallel worlds. The probe:

1. Resolves the `SubworldLibrary.SubworldSystem` type by reflection at `Initialise()`.
2. Resolves the `Current` property (returns the active `Subworld` instance or null).
3. Stores delegates for `Available` and `CurrentId()`.

If the type or member is missing, `Available = false`; `CurrentId()` returns the no-subworld sentinel. Abort-clean per Invariant 4. The probe lives in `Initialise()` once at `PostSetupContent`, never per tick.

## Key Interfaces / Data Flow

```
PostSetupContent:
   BiomeRegistry.Populate()    // vanilla via reflection + modded via tModLoader content scan
   SubworldProbe.Initialise()  // optional reflection probe

OnWorldLoad:
   _contextTagger = new ContextTagger()
   _contextTagger.Reset()
   Events = new EventAggregator()

per tick (PostUpdateEverything):
   collector.EndTick(...)       // frame pushed to ring
   _contextTagger.Snapshot(tickIndex)  // EventContext written
   Events.Accumulate(in tagger.Current, frameMs)  // buckets updated

renderer read (archived EventsTab / dashboard, 1 Hz):
   _cachedNowSummary = ComputeNowActiveSummary(aggregator.Latest)
   BuildRows(aggregator.Buckets)  // snapshot rows into reusable list

OnWorldUnload:
   _contextTagger = null
   Events = null
   BossSampler.Clear()
   SubworldProbe.Clear()
```

## Implemented Outputs / Artifacts

| Surface | Source |
|---------|--------|
| `TickFrame.Context` | `ContextTagger.Snapshot` |
| Event dimension bucket rows (archived EventsTab / dashboard) | `EventAggregator.Buckets` |
| Event NOW summary (archived EventsTab / dashboard) | `_cachedNowSummary` |
| (Future) `ContextCorrelatedSpike` insight | gated; needs the transition stream |

## Known Issues / Active Risks

- **No per-tick transition stream yet.** The gated `ContextCorrelatedSpikeDetector` needs not just "what was the context" but "when did the context change" to correlate spikes with transitions. Today `EventAggregator` accumulates but does not stream transitions. Gated until added.
- **`BiomeRegistry.Populate` runs once at PostSetupContent.** If a mod registers biomes after that point (it shouldn't, but tModLoader is lax about this), the new biome would not appear in the registry. The `ModBiomeBindingOk` flag is the canary; a `false` value is logged.
- **`SubworldProbe` reflects against `SubworldLibrary.SubworldSystem`.** If SubworldLibrary refactors that type, the probe falls back to "no subworld" silently. Abort-clean per Invariant 4, but session JSONs from that release would carry no subworld attribution.
- **`BossSampler` iterates all NPCs every tick.** ~200-slot iteration with `npc.active && npc.boss` checks; cheap but non-zero. Acceptable today; if a future overhead measurement flags it, the `realLife` traversal can be cached across consecutive ticks where the boss has not changed.

## Partial / In Progress

- **Transition stream for `ContextCorrelatedSpikeDetector`.** Needed for the gated detector to ever fire. See `systems/insights-engine.md` for the gated-detector roster and `notes/decisions.md` for the events-correlation rationale.

## Planned / Missing / Likely Changes

- **More dimensions.** The current set (biome, boss, weather, invasion, subworld, gameMode) covers everything the README's six views care about. A future addition would be time-of-day (day vs night vs eclipse-overlap) as a separate dimension.
- **Per-context cost attribution beyond frame time.** Today bucket stats track frame time only. Per-mod cost per context would feed `ContextConditionalCostDetector`.

## Durable Notes / Discarded Approaches

- **`EventContext.VanillaInvasion.ToString()` boxed the enum.** Audit fix in commit `6537950`: replaced with a typed `InvasionShortName` switch helper. Same pattern as the `OverviewTab.Sorted` boxing fix; the take-away is that **enum `.ToString()` calls are not free** — they box.
- **`SubworldProbe` was originally a hard reference to SubworldLibrary.** Made optional via reflection in commit `e6a1020`-era work because requiring SubworldLibrary as a dependency would have shrunk the audience.
- **`BiomeRegistry` reflection over `Zone*` fields landed in 2026-05-19**; the zone-field set was confirmed against `tmodloader/engagement-surfaces.md` §"Vanilla biome zones" recommendations.

## Obsolete / No Longer Relevant

Nothing.

## Cross-references

- `tmodloader/engagement-surfaces.md` — the API surface (`ModBiome`, `NPC.boss`, `Main.bloodMoon` etc.) the snapshotter reads.
- `systems/metric-collection.md` — `TickFrame.Context` lives in the frame the collector pushes.
- `systems/insights-engine.md` — the gated detectors that will consume the transition stream.
- `systems/web-dashboard.md` — the live event surface (the in-game EventsTab is archived under `UI/`).
- `notes/decisions.md` — the events-context design rationale (the per-tab plan notes were folded in here).

## 2026-07-07 touches

Per-context frame accumulation (`events.Accumulate`) reads `RealFrameTimeMs` —
"Forest costs X ms/t" is player-facing (`448f447`). The chronicle's
"session opened" lines carry their own `session` kind instead of `join` (T8);
the JS chip row picks new kinds up via present-set filtering.
