# Notes

Index of `notes/`. Each entry is one bullet pointing at the file that owns the topic. Detail lives in the file, not here.

## Active

- [decisions](notes/decisions.md) — resolved decisions from working sessions, newest first; the project-internal decision record.
- [conventions](notes/conventions.md) — repository-wide coding and structural conventions surfaced from the 2026-05-20 upkeep convention-capture pass.

## Forward-looking (designs not yet implemented)

- [litedb-migration-plan](notes/litedb-migration-plan.md) — research-backed plan for the eventual lifetime-data persistence layer; gates the `LifetimeData` and `NeedsPersistence` EvidenceScope branches.
- [future-html-report](notes/future-html-report.md) — sketch for the post-session HTML report (separate from the session JSON).
- [future-settings-design](notes/future-settings-design.md) — sketch for player-facing settings UI.

## Historical research plans (shipped — kept for context)

These plans were the design briefs for features that have since landed. They stay in `notes/` as the historical record of what was being built and why. Each carries a status header indicating which sections shipped.

- [ilhook-migration-plan](notes/ilhook-migration-plan.md) — the IL backend migration plan. Shipped in commits `3eccf89` through `bb95091` and refined through the 2026-05-20 audit rounds.
- [overview-tab-plan](notes/overview-tab-plan.md) — the Overview tab + composite-impact scorer. Shipped (`693847f`, `a376f6a`).
- [events-tab-plan](notes/events-tab-plan.md) — the Events tab + context aggregation. Shipped (`18d19de`, `1062866`, then `bb95091` merge).
- [insights-engine-plan](notes/insights-engine-plan.md) — the insights engine. Four detectors live, six gated. Shipped (`e6a1020`) and refined through audit rounds.
- [spikes-and-allocations-plan](notes/spikes-and-allocations-plan.md) — spike detection + per-mod allocation tracking. Shipped (`08dd5eb`, `45baf02`, `f32d33d`).
