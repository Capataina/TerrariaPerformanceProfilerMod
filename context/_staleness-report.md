# Staleness Report

Snapshot from the **2026-07-07 focused upkeep-context pass** (post the v0.27.1 → v0.28.0
measurement-honesty + RAM + correctness rework). Overwritten each run.

**Pass scope (honest):** this was a *focused* Upkeep targeting the files the v0.28.0 rework touched.
Rework-adjacent files carry evidence-backed verdicts. Far-field files (the `tmodloader/` reference
docs, unrelated `systems/`, `pages/`, older `notes/`, the `code-health-audit/` plan just written)
carry a lighter-touch verdict — checked for rework-relevance, not line-by-line re-verified against
all of current code. Two docs (`systems/persistence.md`, `systems/metric-collection.md`) carry
**pre-existing drift that predates this session** (fields/methods that no longer match code, e.g.
`persistence.md` predates the whole v0.27 cross-session rollup layer); the rework facts were added,
but a full historical reconciliation of those two is deferred (see WIDND).

## Per-file staleness table

| File | Verdict | Evidence |
|------|---------|----------|
| context/_Overview.md | up-to-date | orientation doc; unaffected by the rework's internals |
| context/notes.md | up-to-date | index of notes/; no new note file added this pass |
| context/notes/decisions.md | needs-updating→updated | prepended the 2026-07-07 measurement-honesty + RAM + correctness entry |
| context/notes/philosophy.md | up-to-date | "optimisation = same output cheaper" principle still holds (B1 followed it) |
| context/notes/conventions.md | up-to-date | conventions unchanged by the rework |
| context/notes/compile-gate.md | up-to-date | the 0-error-CS gate this session used matches it |
| context/notes/cross-session-history-layer.md | up-to-date | v0.27 layer doc; D1 rebuild-rollup references it, no contradiction |
| context/notes/0271-data-quality-and-snapshot-context.md | up-to-date | v0.27.1 record; D1 builds on it, doesn't contradict |
| context/notes/insights-rework-status.md | up-to-date | insights engine untouched this session |
| context/notes/modlist-pre-upgrade-2026-06-22.md | preserved | archival modlist snapshot |
| context/notes/future-html-report.md | up-to-date | still a future item |
| context/notes/future-insights-rework.md | up-to-date | future item |
| context/notes/future-settings-design.md | needs-updating | the per-feature-slider config direction (E1) belongs here eventually; noted in decisions.md this pass, not folded here — deferred |
| context/notes/future-unified-data-interface.md | up-to-date | future item |
| context/notes/ui-overhaul-plan.md | up-to-date | UI unchanged this session |
| context/systems/metric-collection.md | needs-updating→updated | added RealFrameTimeMs (A1), per-hook EMA (B1), self-overhead (A2/A3), denormal flush (C2); PRE-EXISTING drift (AllocBytes/SnapshotForTick don't match code) flagged, full reconciliation deferred |
| context/systems/persistence.md | needs-updating→updated | added the rebuild-rollup scope (D1) + C1/C3 fixes; PRE-EXISTING drift (predates the v0.27 rollup layer) flagged, full reconciliation deferred |
| context/systems/allocation-tracking.md | needs-updating | the per-mod-per-tick alloc-history line (131) + MEM/BOTH mode framing relate to B1/E1; not edited this pass — deferred (lower priority; alloc coverage itself unchanged) |
| context/systems/hook-instrumentation.md | up-to-date | the B4 scaffolding-trim + reclaim diagnostic extend existing behaviour; the doc's trim description still holds |
| context/systems/insights-engine.md | up-to-date | insights engine logic untouched (C1 was in the caller, ProfilerSystem, not the engine) |
| context/systems/data-pipeline.md | up-to-date | pipeline unchanged this session |
| context/systems/spike-detection.md | up-to-date | spike detector still keys off update-window FrameTimeMs (unchanged, documented in metric-collection.md) |
| context/systems/events-and-context.md | up-to-date | events unchanged |
| context/systems/mod-lifecycle.md | up-to-date | lifecycle unchanged |
| context/systems/web-dashboard.md | needs-updating | the Self tab gained harvest/probe lines + the reset dialog gained rebuild-rollup; not edited this pass — deferred (captured in metric-collection.md + persistence.md) |
| context/systems/dashboard-audit-harness.md | up-to-date | harness unchanged |
| context/systems/test-harness.md | up-to-date | 170-test suite unchanged in shape |
| context/systems/overlay.md | preserved | archived UI/ overlay |
| context/pages/_index.md | up-to-date | page index |
| context/pages/self.md | needs-updating | Self tab gained harvest/probe lines (A4); not edited this pass — deferred, captured in metric-collection.md |
| context/pages/summary.md | needs-updating | AvgFrameMs is now the real frame period (A1); not edited this pass — deferred, captured in metric-collection.md |
| context/pages/timeline.md | up-to-date | timeline unaffected |
| context/pages/lag.md | up-to-date | stall attribution refined (A5) but the lag page structure holds |
| context/pages/insights.md | up-to-date | insights page unaffected |
| context/pages/memory.md | up-to-date | memory tab unaffected |
| context/integration/integration-map.md | up-to-date | high-level integration map; rework was within-system |
| context/perf-pass/baseline.md | up-to-date | v0.5 baseline contract, historical; B4 supersedes its Cecil-dominance assumption (noted in deferred.md) |
| context/perf-pass/deferred.md | up-to-date | updated this session (E2): §2.8 marked executed + residual |
| context/perf-pass/verification.md | up-to-date | v0.6 verification record, historical |
| context/plans/install-ram-optimisation.md | needs-updating | B1 (1.8 GB history removal) + B4 (residual measured) are major RAM-plan progress; not edited this pass — deferred (captured in decisions.md + deferred.md) |
| context/plans/database-rework.md | up-to-date | v0.27 DB plan; D1 is a follow-on, no contradiction |
| context/plans/insights-engine.md | up-to-date | insights plan unaffected |
| context/plans/extensive-testing-infrastructure.md | up-to-date | testing plan unaffected |
| context/plans/ui-component-library.md | up-to-date | UI plan unaffected |
| context/plans/code-health-audit/index.md | up-to-date | regenerated this session (Phase 2) |
| context/plans/code-health-audit/findings.md | up-to-date | written this session |
| context/plans/code-health-audit/potential-issues.md | up-to-date | written this session |
| context/plans/code-health-audit/obligation-evidence-map.md | up-to-date | written this session |
| context/plans/code-health-audit/PASS-1-CHECKPOINT.md | up-to-date | written this session |
| context/plans/code-health-audit/PASS-2-SYSTEMS-AUDITED.md | up-to-date | written this session |
| context/plans/code-health-audit/hook-instrumentation.md | preserved | 2026-06-25 audit backlog, kept for reference |
| context/plans/code-health-audit/metric-collection.md | preserved | 2026-06-25 audit backlog |
| context/plans/code-health-audit/data-pipeline.md | preserved | 2026-06-25 audit backlog |
| context/plans/code-health-audit/insights.md | preserved | 2026-06-25 audit backlog |
| context/plans/code-health-audit/web-and-persistence.md | preserved | 2026-06-25 audit backlog |
| context/plans/code-health-audit/web-ui-and-crosscutting.md | preserved | 2026-06-25 audit backlog |
| context/tmodloader/hook-surface.md | up-to-date | tML reference; unaffected |
| context/tmodloader/ilhook-migration-research.md | up-to-date | reference |
| context/tmodloader/lifecycle-and-loop.md | up-to-date | the PreUpdateEntities/PostUpdateEverything anchors it documents are exactly what A1 relied on |
| context/tmodloader/mod-identity.md | up-to-date | reference |
| context/tmodloader/monomod-detours.md | up-to-date | reference; B4's SourceCloneIl rationale is consistent with it |
| context/tmodloader/engagement-surfaces.md | up-to-date | reference |
| context/tmodloader/ui-system.md | preserved | archived-UI reference |

## Coverage-gap report

No uncovered subsystems. Every top-level source area (`Profiling/`, `Data/`, `Persistence/`,
`Web/`, `Insights/`, `Localization/`, `tools/`) has a corresponding `systems/` file or is an
archived/tooling area with a note. Source roots inspected: the `systems/` set already spans hook
instrumentation, metric collection, allocation tracking, data pipeline, events, insights,
persistence, spike detection, web dashboard, mod lifecycle, overlay (archived), and the test/audit
harnesses.

## Deferred (captured in decisions.md, not folded into every owning doc this focused pass)

`pages/self.md`, `pages/summary.md`, `systems/web-dashboard.md`, `systems/allocation-tracking.md`,
`plans/install-ram-optimisation.md`, `notes/future-settings-design.md` each have a rework-relevant
update pending. They are non-contradictory today (the new facts live in `metric-collection.md`,
`persistence.md`, and `decisions.md`); folding the specifics into each is a follow-up upkeep pass.
