# Notes

Index of `notes/`. Each entry is one bullet pointing at the file that owns the topic. Detail lives in the file, not here.

## Active

- [decisions](notes/decisions.md) — resolved decisions from working sessions, newest first; the project-internal decision record.
- [conventions](notes/conventions.md) — repository-wide coding and structural conventions not enforced by tooling, including the unified-`Data/`-pipeline rules.
- [compile-gate](notes/compile-gate.md) — how to verify the mod off-game: the pure-logic `dotnet test` gate and the full-mod `dotnet msbuild` Roslyn gate (`error CS` count; the `TML003` packaging lock is expected and ignored).
- [feature-atlas](notes/feature-atlas.md) — the 31-slot capability tracker (status matrix + briefs); the roadmap's canonical map. Updated per slot-status change.
- [philosophy](notes/philosophy.md) — the project posture the five Invariants come from: universal not bespoke, capture the chain not the consequence, data stack vs presentation stack, descriptive attribution.

## Active work areas (2026-07-07)

The honesty + feature mega-batch (0.28.1→0.35.0) landed; the standing gap is
RUNTIME verification (a Build + Reload playtest). Most-active docs:
`systems/metric-collection.md`, `systems/persistence.md`,
`systems/web-dashboard.md`, `notes/feature-atlas.md`.

## Forward-looking (designs not yet implemented)

- [future-html-report](notes/future-html-report.md) — sketch for the post-session HTML report (separate from the live dashboard and the LiteDB store).
- [future-settings-design](notes/future-settings-design.md) — sketch for the player-facing settings UI.

## Historical records (shipped — kept as the original framing / receipt)

- [insights-rework-status](notes/insights-rework-status.md) — the eight-wave consolidation of the interpretation layer into the top-level `Insights/` module (executed 2026-06-24, v0.18.1 → v0.19.0); commit-by-commit receipt + the honest "what remains" (HookFrequencyTail, LoadoutCombinationCost, cross-mod event chains, still gated). Canonical reality now lives in `systems/insights-engine.md`.
- [future-insights-rework](notes/future-insights-rework.md) — the correctness bugs (chiefly the Flute "usage = created not used" root cause) captured before the rework; **fixed in the rework's Wave 4** (usage is now active-use ticks). Kept as the why-record.
- [future-unified-data-interface](notes/future-unified-data-interface.md) — the original framing note for the `Data/` pipeline; implemented in v0.10–v0.11. Canonical reality now lives in `systems/data-pipeline.md`; this is the why-record.
- [ui-overhaul-plan](notes/ui-overhaul-plan.md) — the design brief for the in-game overlay overhaul. The overlay shipped, then was archived in v0.9.0 when the mod pivoted to the browser dashboard; kept as the historical design record.

## Reference data

- [modlist-pre-upgrade-2026-06-22](notes/modlist-pre-upgrade-2026-06-22.md) — the 99-mod play stack recovered before the machine upgrade, captured for re-subscribe. Distinct from the lean 18-mod benchmark stack the perf research measured against.
