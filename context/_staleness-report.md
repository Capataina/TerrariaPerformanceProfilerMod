# Staleness Report

Snapshot from the **2026-07-07 evening full upkeep** (post the 0.28.1→0.35.0
mega-batch; architecture artefact rebuilt from scratch per user direction).
Overwritten each run.

## Per-file staleness table

| File | Verdict | Evidence |
|------|---------|----------|
| context/_Overview.md | needs-updating→updated | state-at-close block added: v0.35.0, 205 tests, batch summary + atlas pointer |
| context/_staleness-report.md | up-to-date | this snapshot |
| context/arch/_merge-report.md | up-to-date | no batch-relevant claims |
| context/integration/integration-map.md | needs-updating→updated | 2026-07-07 integration points appended (phase flag, config gates, SelfHealth router read, session-end ordering) |
| context/notes.md | needs-updating→updated | atlas indexed; active-areas block refreshed to the batch |
| context/notes/0271-data-quality-and-snapshot-context.md | preserved | v0.27.1 record; fingerprint v2 builds on it |
| context/notes/compile-gate.md | up-to-date | gate used ~20x today as documented; .tmod packs with game closed |
| context/notes/conventions.md | needs-updating→updated | pure-core pattern, linked-source rule, verbatim-string escaping appended |
| context/notes/cross-session-history-layer.md | up-to-date | rollup contract untouched by the batch |
| context/notes/decisions.md | needs-updating→updated | mega-batch entry prepended (7 durable decisions, commit-linked) |
| context/notes/feature-atlas.md | needs-updating→updated | 11 slot statuses flipped with commit evidence |
| context/notes/future-html-report.md | needs-updating→updated | EXECUTED banner (ef74479) + design deltas |
| context/notes/future-insights-rework.md | preserved | resolved-record |
| context/notes/future-settings-design.md | needs-updating→updated | PARTIALLY EXECUTED banner (88f10f4): sliders shipped, presets rejected |
| context/notes/future-unified-data-interface.md | preserved | landed-record |
| context/notes/insights-rework-status.md | up-to-date | v0.19 receipt; new detectors documented in systems/insights-engine.md |
| context/notes/modlist-pre-upgrade-2026-06-22.md | preserved | archival modlist snapshot |
| context/notes/philosophy.md | up-to-date | 'same output cheaper' held through the batch |
| context/notes/ui-overhaul-plan.md | needs-updating→updated | SUPERSEDED banner pointing at plans/ui-overhaul.md + audit ledger |
| context/pages/_index.md | preserved | L8-harness dossier, harness-owned; regenerates on next audit.py synthesize (new panels will land then) |
| context/pages/insights.md | preserved | L8-harness dossier, harness-owned; regenerates on next audit.py synthesize (new panels will land then) |
| context/pages/lag.md | preserved | L8-harness dossier, harness-owned; regenerates on next audit.py synthesize (new panels will land then) |
| context/pages/memory.md | preserved | L8-harness dossier, harness-owned; regenerates on next audit.py synthesize (new panels will land then) |
| context/pages/self.md | preserved | L8-harness dossier, harness-owned; regenerates on next audit.py synthesize (new panels will land then) |
| context/pages/summary.md | preserved | L8-harness dossier, harness-owned; regenerates on next audit.py synthesize (new panels will land then) |
| context/pages/timeline.md | preserved | L8-harness dossier, harness-owned; regenerates on next audit.py synthesize (new panels will land then) |
| context/perf-pass/baseline.md | preserved | historical v0.5/0.6 record |
| context/perf-pass/deferred.md | preserved | historical v0.5/0.6 record |
| context/perf-pass/verification.md | preserved | historical v0.5/0.6 record |
| context/plans/code-health-audit/PASS-1-CHECKPOINT.md | preserved | 2026-07-07-morning audit artefacts + kept 06-25 backlog |
| context/plans/code-health-audit/PASS-2-SYSTEMS-AUDITED.md | preserved | 2026-07-07-morning audit artefacts + kept 06-25 backlog |
| context/plans/code-health-audit/data-pipeline.md | preserved | 2026-07-07-morning audit artefacts + kept 06-25 backlog |
| context/plans/code-health-audit/findings.md | preserved | 2026-07-07-morning audit artefacts + kept 06-25 backlog |
| context/plans/code-health-audit/hook-instrumentation.md | preserved | 2026-07-07-morning audit artefacts + kept 06-25 backlog |
| context/plans/code-health-audit/index.md | preserved | 2026-07-07-morning audit artefacts + kept 06-25 backlog |
| context/plans/code-health-audit/insights.md | preserved | 2026-07-07-morning audit artefacts + kept 06-25 backlog |
| context/plans/code-health-audit/metric-collection.md | preserved | 2026-07-07-morning audit artefacts + kept 06-25 backlog |
| context/plans/code-health-audit/obligation-evidence-map.md | preserved | 2026-07-07-morning audit artefacts + kept 06-25 backlog |
| context/plans/code-health-audit/potential-issues.md | preserved | 2026-07-07-morning audit artefacts + kept 06-25 backlog |
| context/plans/code-health-audit/web-and-persistence.md | preserved | 2026-07-07-morning audit artefacts + kept 06-25 backlog |
| context/plans/code-health-audit/web-ui-and-crosscutting.md | preserved | 2026-07-07-morning audit artefacts + kept 06-25 backlog |
| context/plans/database-rework.md | up-to-date | v0.27 record; InstallArms additive |
| context/plans/e2e-testing.md | needs-updating→updated | EXECUTED rings 1-2 banner; Ring-3 deferral |
| context/plans/extensive-testing-infrastructure.md | needs-updating | run_all + rings extend L1; cross-link deferred — captured in test-harness.md |
| context/plans/feature-settings.md | needs-updating→updated | EXECUTED banner 88f10f4; RetainHookScaffolding deferral |
| context/plans/honesty-completion.md | needs-updating→updated | EXECUTED banner 448f447; acceptance evidence |
| context/plans/html-session-report.md | needs-updating→updated | EXECUTED banner ef74479; static-render upgrade |
| context/plans/insights-engine.md | up-to-date | v0.19 record |
| context/plans/install-ram-optimisation.md | needs-updating | reload-stack detection (0f9e844) is plan progress; folding deferred — captured in persistence.md + decisions.md |
| context/plans/loop-anatomy.md | needs-updating→updated | EXECUTED banner 84409c1; measured numbers + deviations |
| context/plans/memory-guard.md | needs-updating→updated | EXECUTED banner 0f9e844; feed-insight deferral |
| context/plans/ui-component-library.md | up-to-date | v0.17 record |
| context/plans/ui-overhaul.md | needs-updating→updated | EXECUTED pass-2 banner fb2d061; U4 drop + carried ideas |
| context/plans/ui-ux-audit.md | needs-updating→updated | closure map appended: every X/S/T/L/O/I/SE/M row → commit or re-diagnosis |
| context/systems/allocation-tracking.md | needs-updating→updated | config gate + X5 convention appended |
| context/systems/dashboard-audit-harness.md | needs-updating→updated | selection-rule rebuild section appended |
| context/systems/data-pipeline.md | needs-updating→updated | new snapshot fields + pure pieces appended |
| context/systems/events-and-context.md | needs-updating→updated | real-cadence accumulate + chronicle kind appended |
| context/systems/hook-instrumentation.md | needs-updating→updated | phaseLanes Configure + backend config + arm rows appended |
| context/systems/insights-engine.md | needs-updating→updated | 3 detector additions + registration surface appended |
| context/systems/metric-collection.md | needs-updating→updated | honesty+anatomy layer section appended (repoints, RealtimeSpeed, phase lanes, config) |
| context/systems/mod-lifecycle.md | needs-updating→updated | config gates + session-end additions appended |
| context/systems/overlay.md | preserved | archived UI/ tree unchanged |
| context/systems/persistence.md | needs-updating→updated | fingerprint v2, InstallArms, Report module, H2 hardening appended |
| context/systems/spike-detection.md | needs-updating→updated | real-cadence trigger + sensitivity section appended |
| context/systems/test-harness.md | needs-updating→updated | Simulation rings + run_all + bench conventions appended |
| context/systems/web-dashboard.md | needs-updating→updated | 0.30.1-0.35.0 surface layer appended (cards, panelState, pollMs, per-tab) |
| context/tmodloader/engagement-surfaces.md | up-to-date | host-API reference; PostDrawInterface/ModConfig usage this batch consistent with it |
| context/tmodloader/hook-surface.md | up-to-date | host-API reference; PostDrawInterface/ModConfig usage this batch consistent with it |
| context/tmodloader/ilhook-migration-research.md | up-to-date | host-API reference; PostDrawInterface/ModConfig usage this batch consistent with it |
| context/tmodloader/lifecycle-and-loop.md | up-to-date | host-API reference; PostDrawInterface/ModConfig usage this batch consistent with it |
| context/tmodloader/mod-identity.md | up-to-date | host-API reference; PostDrawInterface/ModConfig usage this batch consistent with it |
| context/tmodloader/monomod-detours.md | up-to-date | host-API reference; PostDrawInterface/ModConfig usage this batch consistent with it |
| context/tmodloader/ui-system.md | up-to-date | host-API reference; PostDrawInterface/ModConfig usage this batch consistent with it |
| context/arch/data.js | up-to-date | REBUILT FROM SCRATCH this pass (user-directed): old-skill seed deleted, fresh seed + full agent fill, arch_lint 0/0/0, arch_verify PASSED |
| arch section: project | up-to-date | agent-filled this pass (stack/milestone/tests/tagline/purpose/overview/techStack) |
| arch section: nodes | up-to-date | 10 real subsystems filled; 5 junk seeds deleted (bin/obj/design/lib) recorded in _meta.deleted_node_ids |
| arch section: edges + relationships | up-to-date | 10 edges + 10 relationships (meets min(10,C(10,2))) with mechanisms + break semantics |
| arch section: dataFlow + failures + criticalPaths | up-to-date | the honest-frame chain traced end-to-end; 6 failure invariants; 3 critical paths with blast |
| arch section: coverage/notes/concept/glossary/decisions/risks/alerts/kpis/lineage/stateOwnership | up-to-date | agent-filled this pass from batch knowledge; lineage phases = 3 narrative cards |
| arch shells (index/styles/graph/app/features) | preserved | stamped from skills/upkeep-context/scripts/_templates/arch/ |
| context/architecture.html | up-to-date | bundled 594KB; arch_verify PASSED (no console errors, all critical DOM present) |

## Coverage-gap report

Two new source areas assessed, neither warranting a new file:
- `ProfilerConfig.cs` + `Localization/` (S23 settings) → owned by
  `systems/mod-lifecycle.md` (gates) + `systems/web-dashboard.md` (pollMs) +
  the feature-settings plan.
- `Persistence/Report/` → owned by `systems/persistence.md` §Report.

All 8 top-level code roots + tools/ have owning docs. No uncovered subsystems.

## Deferred this pass

`plans/install-ram-optimisation.md` and `plans/extensive-testing-infrastructure.md`
carry needs-updating verdicts with their facts captured centrally
(persistence.md / test-harness.md / decisions.md); folding the specifics in is
the next pass's work. `pages/*.md` dossiers regenerate via the harness
(`audit.py synthesize`), not hand-edits.
