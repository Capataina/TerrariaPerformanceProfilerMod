# Decisions

Resolved decisions from working sessions, newest first. Project-internal
record; the README is the directional summary.

## 2026-05-19 — Milestone 1 + 2 build session

**Repository published.** The project is now a public GitHub repo,
`Capataina/TerrariaPerformanceProfilerMod` (MIT licence), and is listed in
the profile README under Active Projects.

**Milestone 0 dropped.** The feasibility-spike phase is removed. Every M0
spike was premised on a 94-mod stack the dev machine cannot load, so the
spikes had nothing to measure. Milestones now run from M1, and overhead is
validated on whatever modlist the dev machine can actually run.

**API-first, clone-on-wall.** Build on tModLoader's public API first; when a
genuine wall is hit, read the tModLoader source from GitHub (via `gh`)
rather than guessing. The wall was reached at per-mod attribution — the
source was read and the approach confirmed against it.

**Per-mod attribution uses MonoMod On-hooks, never IL edits.** Confirmed
from the tModLoader source: `ModLoader.Mods` is public, `MonoModHooks.Add`
On-hooks auto-remove on mod unload, and every `Mod*`/`Global*` instance
carries its owning `Mod`. An On-hook wraps a method and cannot corrupt it,
so a fault is wrong numbers, never a crash (Invariants 1 and 4). IL editing
(`MonoModHooks.Modify`) is reserved for cases On-hooks cannot reach.

**Attribution is split by hook category.** Cost is accumulated per mod and
per category (Systems / Players / NPCs / Projectiles), so the overlay tree
folds a mod row open into a per-category breakdown.

**First-cut hook scope: parameterless instance hooks only.** The interceptor
hooks the void-signature per-tick hooks (`ModSystem`/`ModPlayer` update
hooks, `ModNPC`/`ModProjectile` AI) — one delegate shape, lowest risk. The
per-entity `GlobalNPC`/`GlobalProjectile` hooks, which carry a parameter,
are a planned follow-up to widen coverage.
