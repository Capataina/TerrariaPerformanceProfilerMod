# Staleness Report

Snapshot from the 2026-06-25 upkeep-context pass (the v0.13 → v0.22 reconciliation). Overwritten on each upkeep run.

The prior context was reconciled to **v0.12** on 2026-06-22. Everything in the **v0.13 → v0.22 arc landed afterwards**, almost all on 2026-06-24: the insights engine was consolidated out of `Data/Detectors/Insights/` into a new **top-level `Insights/` module** (5 families, ~18 detectors, ReferenceFrames / Drivers / CrossSession architecture); the dashboard gained a **sixth tab (Memory)** and four **new chart encodings** (bubble scatter, sankey, waffle, per-mod cost-stream) on top of a **shadcn-neutral OKLCH component library**; and a new off-game **L4/L6/L8 testing harness** landed under `tools/testing/`. Scope evidence: `git diff 4bcb632..HEAD` = 193 files, +19174 / −2964; `Insights/` exists at top level and `Data/Detectors/Insights/` is gone.

Files walked: 56 `.md` files under `context/` (excluding this report). The `plans/code-health-audit/` tree (8 files) is owned by the `code-health-audit` agent and is not adjudicated here.

## Per-file verdicts (this run)

### Cross-cutting docs (orchestrator-owned)

| File | Verdict | Evidence / action |
|------|---------|-------------------|
| `architecture.md` | stale → **retired into `architecture.html`** | Was stamped "as of v0.12" (insights at `Data/Detectors/Insights/`, five tabs, no harness). Per the user's request this pass, the markdown form was retired and the architecture is now the bundled interactive **`architecture.html`** explorer (editable source `arch/data.js`). The stale content was not patched — it was superseded; the current architecture lives in `arch/data.js`, lint-gated. |
| `architecture.html` + `arch/data.js` | up-to-date (generated) | Regenerated this pass against `scan_repo` + `git log` + the updated `systems/*.md`. `arch_lint` 0 errors/warnings/TODOs; `arch_verify` exit 0. 9 subsystem nodes; 12 relationships, 3 critical paths, 5 failures, 20-step dataFlow. 5 noise nodes (`bin_Debug`/`bin_Release`/`obj`/`design`/`Localization`) deleted, recorded in `_meta.deleted_node_ids`. |
| `arch/{index.html,styles.css,graph.js,app.js,features.js}` | preserved | Vendored shells, stamped from `skills/upkeep-context/scripts/_templates/arch/`; regenerated wholesale by `arch_shell.py` on every orchestrate run — never hand-edited. |
| `_Overview.md` | needs-updating → fixed | "Current state (build.txt 0.12)"; "five SPA tabs"; insights described as part of `Data/`. Folder map already lists `pages/` + the new notes (from the 8dc1dfe / d2323f9 wrap-up). build.txt is now 0.22.0. |
| `notes.md` | needs-updating → fixed | Index lists 8 of the 11 files in `notes/`; missing `compile-gate`, `future-insights-rework`, `insights-rework-status` (present on disk, listed in `_Overview` folder map but not in this index). |
| `integration/integration-map.md` | needs-updating → fixed | Status model stamped through v0.12; "5-tab SPA" (line 11); insights-engine canonical-home references point at the pre-move location; testing harness row absent. |

### Heavy system files (delegated, verified against source)

| File | Verdict | Evidence / action |
|------|---------|-------------------|
| `systems/insights-engine.md` | stale → rewritten (WS-Insights) | Whole subsystem at `Data/Detectors/Insights/` (now top-level `Insights/`); roster "four live / six gated" of 10 patterns (now ~18 detectors across 5 families); no ReferenceFrames / Drivers / CrossSession / Publish-stat architecture; no `Shared/` primitives. Largest single rewrite. |
| `systems/web-dashboard.md` | needs-updating → fixed (WS-Web) | "The shipped SPA has **five tabs**" — `pages/_index.md` shows **six** (adds **Memory**); "28 `/api/*`" — contract coverage shows 29; the "Planned: shadcn component library" is now BUILT (v0.17); four new chart encodings (scatter/sankey/waffle/cost-stream) unrecorded; CSS/JS fragment counts drifted (new `Js.Charts` module). |
| `systems/test-harness.md` | needs-updating → fixed (WS-Testing) | Covers only the L1 xUnit project; the new `tools/testing` L4/L6/L8 harness (44 files) is uncovered (see coverage gap). The csproj `Compile Include` drift note re-verified against current `Tests/*.csproj`. |

### Minor-drift system files (delegated verify; edit only where drifted)

| File | Verdict | Evidence / action |
|------|---------|-------------------|
| `systems/mod-lifecycle.md` | verify (WS-MinorDrift) | `Profiling/ProfilerSystem.cs` + `PerformanceProfiler.cs` touched in the arc; confirm the Insights/ registration + lifecycle wiring still matches after the engine move. |
| `systems/data-pipeline.md` | verify (WS-MinorDrift) | Insights consolidated *out* of `Data/Detectors/`; confirm the data-pipeline doc no longer implies insights live under `Data/`. Small `Data/Stats` + `Data/Contracts` + `Data/Aggregators` changes. |
| `systems/persistence.md` | verify (WS-MinorDrift) | `Profiling/Persistence/` had 3 changes; confirm `InsightStream` / `SessionRecorder` references still hold after the engine move. |
| `systems/metric-collection.md` | verify (WS-MinorDrift) | `Data/Stats` (2) touched; spot-check accessor references. |
| `systems/events-and-context.md` | verify (WS-MinorDrift) | No direct source change flagged; confirm cross-refs to insights still resolve. |
| `systems/spike-detection.md` | verify (WS-MinorDrift) | `PeakContributorToSpikeDetector` moved into `Insights/Detectors/`; confirm the cross-ref. |
| `systems/allocation-tracking.md` | verify (WS-MinorDrift) | `AllocationBurstDetector` moved into `Insights/Detectors/`; confirm the cross-ref. |
| `systems/hook-instrumentation.md` | verify (WS-MinorDrift) | No direct source change in the arc; confirm no drift. |
| `systems/overlay.md` | verify (WS-MinorDrift) | `UI/Overlay` (2) + `UI/ProfilerTheme.cs` (1) touched; archived, expect cosmetic only. |

### Plans (hygiene)

| File | Verdict | Evidence / action |
|------|---------|-------------------|
| `plans/insights-engine.md` | needs-updating → status fixed | Status reads "PROPOSED — not started" but the top-level `Insights/` module it specifies now exists (5 families, ~18 detectors). Mark executed / reconcile against shipped reality. |
| `plans/ui-component-library.md` | up-to-date | Already "EXECUTED (2026-06-23) — pending in-game confirmation"; six tabs incl. Memory named. The in-game gate is still open (loader-locked); preserve. |
| `plans/extensive-testing-infrastructure.md` | needs-updating → status fixed | The L4/L6/L8 axes it specifies are now BUILT under `tools/testing/` (commit fe2d57b + follow-ups). Tick the landed axes; note L2/L5/L7 still open. |
| `plans/install-ram-optimisation.md` | verify (WS-MinorDrift/hygiene) | RAM-footprint optimisation plan; confirm status against current `ProfilerSelfHealth` + hook scaffolding. |
| `plans/code-health-audit/index.md`, `plans/code-health-audit/PASS-1-CHECKPOINT.md`, `plans/code-health-audit/PASS-2-SYSTEMS-AUDITED.md`, `plans/code-health-audit/build-and-tests.md`, `plans/code-health-audit/cross-cutting.md`, `plans/code-health-audit/hook-install-ram.md`, `plans/code-health-audit/obligation-evidence-map.md`, `plans/code-health-audit/potential-issues.md` | not adjudicated | Prior `code-health-audit` receipts; out of scope by coordination rule. NOTE: a fresh `code-health-audit` run follows this upkeep pass (per the session goal) and will produce new plan files here. |

### Notes

| File | Verdict | Evidence / action |
|------|---------|-------------------|
| `notes/decisions.md` | needs-updating → v0.13–v0.22 entry added | Self-framed "current through v0.12"; the arc (insights consolidation, component library, four charts, testing harness, Memory tab) has no decision entry. |
| `notes/conventions.md` | verify → extend if new conventions found | The component library + chart vocabulary may have introduced unmarked conventions (chart-component contract, OKLCH token usage, monochrome-chrome rule). Convention-capture pass. |
| `notes/insights-rework-status.md` | needs-updating → mark shipped | Tracks the insights rework now executed (v0.19); reconcile to shipped + add to `notes.md` index. |
| `notes/future-insights-rework.md` | needs-updating → implemented banner | The rework it sketched is built; mark implemented / point at `systems/insights-engine.md`. Add to `notes.md` index. |
| `notes/compile-gate.md` | up-to-date | The off-game compile-gate convention; current. Add to `notes.md` index. |
| `notes/philosophy.md` | preserved | Posture / five invariants; no code drift. |
| `notes/future-html-report.md` | preserved | Forward-looking; not started. |
| `notes/future-settings-design.md` | preserved | Forward-looking; not started. |
| `notes/future-unified-data-interface.md` | preserved | Historical why-record for the `Data/` pipeline. |
| `notes/ui-overhaul-plan.md` | preserved | Historical design record (archived overlay). |
| `notes/modlist-pre-upgrade-2026-06-22.md` | preserved | Reference snapshot of the recovered play stack. |

### Pages (auto-maintained by `tools/testing`)

| File | Verdict | Evidence / action |
|------|---------|-------------------|
| `pages/_index.md`, `pages/summary.md`, `pages/timeline.md`, `pages/lag.md`, `pages/insights.md`, `pages/self.md`, `pages/memory.md` | up-to-date | Regenerated by the L8 harness; `pages/_index.md` stamps "Last audit: 2026-06-24T17:30:47, Tabs discovered: 6". Owned by the harness; not hand-edited. |

### Reference + research (source-stable)

| File | Verdict | Evidence / action |
|------|---------|-------------------|
| `tmodloader/hook-surface.md`, `tmodloader/monomod-detours.md`, `tmodloader/mod-identity.md`, `tmodloader/engagement-surfaces.md` | preserved | Per-API references cited by fully-qualified member name; stable across our file moves; the tModLoader surface they document was untouched by the arc. |
| `tmodloader/lifecycle-and-loop.md`, `tmodloader/ui-system.md` | preserved | "How we plug in" content was reconciled in the 2026-06-22 pass; the lifecycle wiring is unchanged by the v0.13–v0.22 arc (confirmed via WS-MinorDrift cross-check on mod-lifecycle). |
| `tmodloader/ilhook-migration-research.md` | preserved | Shipped research record. |
| `perf-pass/baseline.md`, `perf-pass/deferred.md`, `perf-pass/verification.md` | preserved | v0.5→v0.6 performance-research record. |
| `_staleness-report.md` | this file | Overwritten. |

## Coverage gap report (subsystems lacking a canonical home pre-run)

| Repository area | Inferred system name | Proposed filename | Why it deserves a file |
|-----------------|----------------------|-------------------|------------------------|
| `tools/testing/` (44 files: `audit.py`, `pp_testing/{driver,capture,layout,scenarios,pages,harness,site}.py`, `rubric.md`, `design-bar.md`) | dashboard audit harness (L4/L6/L8) | `systems/dashboard-audit-harness.md` | Real complexity: a self-describing Playwright harness that DOM-discovers tabs/panes/endpoints, generates L6 fixtures, asserts L4 layout invariants, and fans out an L8 vision-agent audit per tab writing the `pages/` dossiers. Distinct from `systems/test-harness.md` (L1 xUnit pure-logic). Created this run by WS-Testing. |

`tools/preview/` (the offline source-render, 25 files under `design/` + `tools/preview/`) is dev tooling already referenced from `web-dashboard.md` and the README; it is a render utility, not a subsystem warranting its own canonical file. Noted, not filed.

After this run, every production source tree (`Data/`, `Profiling/`, `Web/`, `Insights/`, `UI/`) plus the two off-game tool harnesses (`Tests/` L1, `tools/testing/` L4/L6/L8) has canonical coverage. Source roots inspected: the top-level `Data/`, `Profiling/`, `Web/`, `Insights/`, `UI/`, `Tests/`, `tools/` trees + the root entry-point file.
