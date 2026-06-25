# Insights rework — known correctness bugs (RESOLVED in the rework)

> **Status: RESOLVED (v0.19.0, 2026-06-24).** Bug 1 below (the Flute "usage = content
> created, not content used" root cause) was fixed in **Wave 4** of the insights
> consolidation: the usage axis now reads active-use ticks (held / worn / in-biome) via
> the new `ItemsHeldTicks` / `ArmorEquippedTicks` per-tick counters, not `OnCreated`
> counts. See `notes/insights-rework-status.md` for the commit-by-commit receipt and
> `systems/insights-engine.md` for the current implementation. This note is kept as the
> why-record (the root-cause investigation the rework started from), not a live to-do.

Captured 2026-06-23 from live in-game testing (v0.18.1). The Insights *rendering*
was migrated onto the component library; the *data/attribution layer* behind it
had correctness bugs that the rework fixed. Recorded so the rework started from the
root cause, not a re-investigation.

## Bug 1 — "usage" measures content CREATED, not content USED (root cause)

**Symptom (observed live):** main weapon in-game was Thorium's *Flute* (being
held/swung), yet the dormant + observatory surfaces showed Thorium at **0 items
used**, and engagement-vs-cost showed **every mod at 0.0% usage share**.

**Root cause:** the "used" / "usage" axis is computed as content *created + spawned*,
not *used*:

- `Data/Stats/DormantSurfaceStat.cs:68` — `used = ItemsCreated + NpcsSpawned + …`
- `Data/Stats/ModObservatoryStat.cs:110` — `w = ItemsCreated + NpcsSpawned + BossesFought + …`
- `Data/Stats/EngagementCostScatterStat.cs:85` — `used = ItemsCreated + NpcsSpawned + …`

`ItemsCreated` (`Data/Aggregators/PerModUsageAggregator.cs:58,137`) only increments
on the **`OnCreated`** item hook (`Profiling/Persistence/Interactions/InteractionItem.cs:46`
— recipe craft / init / buy / journey-duplicate). **Holding, swinging, or using an
item you already own fires nothing.** So a weapon wielded but not crafted this
session never counts → 0 "used" for its mod, and the whole usage share collapses to
0% when nothing was crafted.

This single cause explains both the Flute=0 symptom and the all-0.0% usage shares.

**Secondary:** the creation tracking is itself partial — `InteractionItem`'s own
comment (lines 36–40) notes v0.5 only wired `OnCreated`, so item pickups and
world-drops were already silently missed.

**What IS tracked as real use (keep):** `accessoryEquippedTicks`,
`ticksInOwnedBiomes` (per-tick held/worn surfaces). Items are the gap — they use a
creation event where a use signal is needed.

## Direction for the rework (not prescriptive on implementation)

Measure active USE on generic surfaces (Invariant 5 — no mod-specific code;
Invariant 3 — descriptive copy):

- held item per tick via `Player.HeldItem` (the wielded weapon/tool)
- an item use / swing hook for active use
- armour / accessory worn (accessory equipped-ticks already exist; extend to armour)

Then "used / roster" and "usage share" answer the real question ("is this mod's
content in active use") rather than "did you craft something this session". Keep the
creation counts too — they are a distinct, legitimate signal (what got made), just
not a proxy for usage.

## Status

Deferred to the planned Insights rework. The UI/component-library work (v0.17–0.18.1)
is independent and complete; this is purely the attribution layer feeding it.
