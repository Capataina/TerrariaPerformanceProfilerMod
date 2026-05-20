# Project Philosophy

The general posture that shapes every design decision in this codebase. The
five Project Invariants in `CLAUDE.md` are the inviolable laws; this note is
the worldview those laws come from, so future sessions don't drift from the
posture even when applying the rules to new situations.

---

## The mod is universal, not bespoke

Performance Profiler measures **any modlist a player happens to be running**.
Not "modded Terraria with Calamity", not "a curated stack the author tested",
not "the popular mods of 2026". The literal universe of every combination of
every tModLoader mod that exists, has ever existed, or will exist before the
mod stops being maintained.

Two consequences follow:

- **Dynamic over enumerative.** Where vanilla tML exposes an enumeration
  surface (`ModContent.GetContent<ModBiome>()`, `IEntitySource`, the buff
  arrays, `Player.HurtInfo`, the equipment slots, `PlayerDeathReason`), the
  profiler hooks the enumeration, not a hand-coded list. If a mod adds 30
  biomes, the watcher learns about them on `PostSetupContent`. If a mod
  introduces a new damage source via `ModProjectile`, the damage tracker
  records it by its dynamic identity, not by string matching the mod's
  name.

- **The interaction shape, not the mod identity.** Recording "CheatSheet
  spawned an NPC" by string-matching `CheatSheet` is brittle: it works for
  one mod and breaks for every alternative spawning mod that exists or
  will exist (HEROsMod, FargosMutant, NPCSpawnAssistant, any unreleased
  successor). Recording "an NPC spawned via `EntitySource_DebugCommand`" is
  universal: every debug spawner ever written uses that source.

  Invariant 5 in `CLAUDE.md` enforces this. The philosophy is its source.

If a tML platform gap forces a single-mod path (the SubworldLibrary probe
is one such case), the code calls it out explicitly, scopes it as small as
possible, and degrades gracefully when the named mod isn't loaded.

---

## Data stack vs presentation/storage stack

The mod has two distinct stacks that get conflated unless we keep them
separate:

**The data stack.** Everything captured: ticks, allocations, per-mod CPU,
per-hook CPU, spikes, stalls, stall clusters, biome bits, weather flags,
invasions, hardmode, time-of-day, sub-worlds, boss presence + outcome,
player deaths, world snapshots, NPC spawn events, item creation events,
damage-taken events, damage-dealt events, buff lifecycle events, loadout
snapshots, world-event windows, anything else that's a generic-surface
interaction. The data stack is **how much of the game's behaviour the
profiler can observe**. More is always better here.

**The presentation/storage/optimisation stack.** Everything done with the
captured data: what gets written to disk vs kept in RAM, what gets shown
on which overlay tab, what gets surfaced as an insight, what gets compacted
on session-end, what gets thrown away after 24 hours, what gets shown
inline in `client.log`, what gets exported to a future HTML report. These
are downstream decisions about **how to spend our overhead budget and the
player's attention**, made independently of what we capture.

The discipline: **never let the presentation/storage stack constrain what
the data stack captures**. If a new tracker would generate 60-Hz events
during combat, the right answer is "capture it, downsample for storage,
display the aggregate" — not "don't capture it because storage is
expensive". Storage is a downstream design problem with known levers
(sampling, downsampling tiers, ring buffers, compaction); capture is a
one-way door (un-captured events are unrecoverable).

The corollary: **a dedicated optimisation pass is a planned milestone**,
not a constant pressure on feature work. Optimisation = doing what we
already do at maximum efficiency. It is not = doing less. When the time
comes we'll squeeze RAM, storage, allocations, and CPU; the feature set
stays intact.

---

## Profiling is descriptive, attribution is interaction-shaped

The honesty contract (Invariant 3) says no mod is "core" or "removable".
The interaction-tracking posture extends that to attribution:

- The profiler doesn't say **"Mod X is causing your lag"**. It says
  **"these hooks cost N ms, fired in this state, after these events"**
  and lets the player draw the conclusion.
- The profiler doesn't say **"this combo is bad"**. It says **"cost is
  measurably higher when loadout component A is equipped together with
  B"** and surfaces the correlation with its evidence.
- The profiler doesn't say **"the spawn menu is laggy"**. It says
  **"a sustained cluster of UI-thread stalls fires during this interval,
  with mod X's draw-side hooks as the dominant contributor"**.

Every insight is a *measurement plus its evidence*, not a verdict. The
player decides what to do about it.

This is why interaction tracking matters: a verdict-free system needs to
expose the relationships between game state and cost so the player can
read the chain themselves. "Hook X is expensive" is a verdict. "Hook X is
expensive *only while buff Y is active, which only happens after damage*"
is a measurement plus evidence — the player decides whether to drop the
mod, drop the buff source, or keep both knowing the cost.

---

## Capture the chain, not the consequence

Concrete example of the principle:

A player attacks an enemy. The game lags. Where is the lag?

A consequence-only profiler says "this hook took 30 ms". Useful but not
sufficient — the player still doesn't know what *combination of states*
made that hook expensive.

A chain-aware profiler captures:

- The held item at the moment of the swing.
- The projectile that spawned (if any), and its source item.
- The accessories worn and their `ModAccessorySlot` state.
- The armour set bonus active.
- The buffs on the player at swing time.
- The NPC type hit and the damage variant (melee / ranged / magic / summon).
- The `OnHitNPC` / `OnHitNPCWithItem` / `OnHitNPCWithProj` path that
  actually fired.
- The damage applied (raw / critical / DoT) and any reflexive damage on
  the player.

Each of those is a generic-surface hook. None of them requires
mod-specific code. The combination is the chain. The chain is the
explanation the player needs.

Same for the receiving side: when the player takes damage, capture the
`PlayerDeathReason` (NPC type / projectile type / item type / "Other"),
the active buffs, the loadout. When the player dies, the killer is
already in the row — the last damage-taken event before `dead = true`.

---

## What this implies for everything not built yet

When we add a new tracker, the test is:

1. Does it hook a **generic surface** vanilla / tML exposes? (Invariant 5)
2. Does it record the **interaction shape** — what happened, what was the
   state, who was involved — rather than a flattened "thing went wrong"?
3. Does it leave the **presentation / storage / display** decision to
   downstream code, not bake them into the capture path?
4. Is the per-tick cost measured against the overhead budget (Invariant 2)
   or queued to a writer thread the way the existing event streams are?

If all four pass, the tracker belongs in the data stack. We add it.

Storage and overhead are downstream concerns. We optimise them in a
dedicated milestone when the feature surface is settled.

---

## What this rules out — explicitly

- Hardcoded lists of mod names, mod ids, mod versions, namespace prefixes,
  or content names.
- Mod-specific case statements in any classifier, ranker, detector,
  insight renderer, or UI tab.
- Hand-curated "common combos" or "popular setups" tables.
- Lookup tables of "this hook is known-bad", "this mod is known-fine",
  etc.
- Capture decisions gated on storage or overhead concerns before a
  dedicated optimisation pass has measured them.

Anything that smells like the above is wrong by construction, regardless
of how convenient it would be for one playtest's diagnosis.

---

## How to think about future decisions

When in doubt about whether to capture X:

- If X is a **generic surface vanilla / tML exposes** and X is part of
  the player's interaction with the game world, capture it.
- If X requires **knowing a specific mod exists**, find the generic
  surface that mod (and every alternative mod) uses, and capture *that*.
- If you can't find a generic surface, file the gap in `decisions.md`
  and wait for a tML release that exposes it. Don't write a reflection
  workaround that name-matches a single mod.

When in doubt about whether to display Y:

- Display is downstream. Capture Y, route it to an aggregate or a
  collection, decide which tab / which insight / which log line surfaces
  it later. The data is there either way.
