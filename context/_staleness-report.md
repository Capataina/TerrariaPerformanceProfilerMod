# Staleness Report

Snapshot from the 2026-05-20 upkeep-context pass. Overwritten on each upkeep run.

Files walked: 28 `.md` under `context/` at start of run; 12 of those moved via `git mv` into the new folder shape; new files created to fill coverage gaps.

## Per-file verdicts (pre-run)

| File (pre-move) | Verdict | Evidence |
|------|---------|----------|
| `context/_Overview.md` | stale | Pre-implementation reconnaissance dated 2026-05-19 with six-agent feasibility framing. Described the codebase before ILHook landed, before the audit, before the test harness. Rewritten as the post-implementation entry point. |
| `context/integration-map.md` | stale | Same vintage; talks about M0 spikes that the 2026-05-19 decisions log dropped. Component tier model mismatches reality where ILHook is the default backend. Moved to `integration/integration-map.md` and refreshed. |
| `context/notes/decisions.md` | needs-updating | Only carried the 2026-05-19 session entry. Missing the 2026-05-20 decisions (ILHook default, HookCoverageVersion=3, EvidenceScope split, atomic writes, schema v4 insights block, pattern-aware ranking, PValueAdjusted promotion gate, dual-surface JSON share). Refreshed in place. |
| `context/tmodloader-hook-surface.md` | needs-updating | Describes the hook surface accurately; lacked a "How we plug in" section. Moved to `tmodloader/hook-surface.md`; "How we plug in" section appended. |
| `context/tmodloader-monomod-detours.md` | needs-updating | Called `MonoModHooks.Add/Modify` return type a `NEEDS DECOMPILER VERIFICATION` gap; reality: we now use `new ILHook(...)` directly because `MonoModHooks.Modify` returns void (confirmed at `Profiling/ILHookInterceptor.cs:36-41`). Moved to `tmodloader/monomod-detours.md`; "How `ILHookInterceptor` actually wires" section appended. |
| `context/tmodloader-lifecycle-and-loop.md` | needs-updating | Save-path uncertainty marked `NEEDS DECOMPILER VERIFICATION`; reality: `SessionLogWriter.SessionDirectory()` resolves the path. Atomic-write hardening + try/catch self-disable not reflected. Moved to `tmodloader/lifecycle-and-loop.md`; "How we plug in" section appended. |
| `context/tmodloader-mod-identity.md` | needs-updating | Enumeration gap marked unresolved; reality: `ModLoader.Mods` is enumerated directly in `HookInterceptor.Install` (line 296). Moved to `tmodloader/mod-identity.md`; resolution status appended. |
| `context/tmodloader-ui-system.md` | needs-updating | UI now has the full tab system (`IOverlayTab`, `TabRegistry.Visible`, five tabs). The slice predated this; "How we plug in" section appended after the move. |
| `context/tmodloader-engagement-surfaces.md` | needs-updating | `ContextTagger`, `BiomeRegistry`, `EventAggregator`, `SubworldProbe` now exist as a full subsystem. Moved to `tmodloader/engagement-surfaces.md`; "How we plug in" section appended. |
| `context/ILHook-migration-plan.md` | preserved | Migration plan; the work shipped but the research record stays useful. Moved to `tmodloader/ilhook-migration-research.md` as a reference artefact. |
| `context/notes/events-tab-plan.md` | preserved | Implemented; status header added. |
| `context/notes/future-html-report.md` | up-to-date | Forward-looking; still relevant; no implementation has touched its scope. |
| `context/notes/future-settings-design.md` | up-to-date | Forward-looking; still relevant. |
| `context/notes/ilhook-migration-plan.md` | preserved | Implemented; status header added. |
| `context/notes/insights-engine-plan.md` | preserved | Largely shipped (four detectors live, six gated). Status header added. |
| `context/notes/litedb-migration-plan.md` | up-to-date | Forward-looking; not started; still relevant. |
| `context/notes/overview-tab-plan.md` | preserved | Shipped; status header added. |
| `context/notes/spikes-and-allocations-plan.md` | preserved | Shipped; status header added. |
| `context/plans/code-health-audit/index.md` | preserved | Active audit log with full implementation receipt for the 2026-05-20 pass. All 16 certain findings and 6 potential issues classified done / deferred / acknowledged. Two findings explicitly deferred (SessionLogWriter split + schema snapshot test). |
| `context/plans/code-health-audit/PASS-1-CHECKPOINT.md` | preserved | Pass-1 checkpoint of the audit; historical record of the candidate list before deep-dive. |
| `context/plans/code-health-audit/PASS-2-SYSTEMS-AUDITED.md` | preserved | Pass-2 checkpoint; verdicts on modularisation candidates. |
| `context/plans/code-health-audit/build-and-tests.md` | preserved | Audit deep-dive for build / test infrastructure. The test-harness finding is now shipped (`Tests/`). |
| `context/plans/code-health-audit/hook-instrumentation.md` | preserved | Audit deep-dive for hook instrumentation. All findings shipped in round 1 (`77a99d2`). |
| `context/plans/code-health-audit/insights-engine.md` | preserved | Audit deep-dive for insights engine. All findings shipped in round 2 (`aa914ce`). |
| `context/plans/code-health-audit/obligation-evidence-map.md` | preserved | Audit obligation tracking; passed evidence-map lint. |
| `context/plans/code-health-audit/overlay-ui.md` | preserved | Audit deep-dive for overlay UI. All findings shipped in round 2 (`aa914ce`). |
| `context/plans/code-health-audit/persistence-session-logging.md` | preserved | Audit deep-dive for session logging. Atomic writes + self-disable shipped in round 1; the file's own modularisation finding + schema snapshot test are the two deferred items. |
| `context/plans/code-health-audit/potential-issues.md` | preserved | Audit's potential-issue sweep. All 6 issues resolved across rounds 1 and 2 (see `index.md`'s implementation receipt). |

## Coverage gap report (subsystems lacking a canonical home pre-run)

Each entry has been filled by a new `systems/*.md` file in this upkeep run.

| Repository area | New file | Why it deserves a file |
|-----------------|----------|------------------------|
| `Profiling/HookInterceptor.cs` + `ILHookInterceptor.cs` + `HookCategoryRouter.cs` + `HookCoverageView.cs` + `HookBackend.cs` + `ProbeStack.cs` | `systems/hook-instrumentation.md` | Two backends, shared router, shared coverage view, ~1700 lines. The audit dedicates a whole file to it. |
| `Profiling/MetricCollector.cs` + `PerModAttribution.cs` + `PerModSample.cs` + `PerTickAttributionRing.cs` + `RingBuffer.cs` + `TickFrame.cs` | `systems/metric-collection.md` | Per-tick measurement core. |
| `Profiling/SpikeDetector.cs` + ring buffers + per-tick attribution ring | `systems/spike-detection.md` | Audit + plan; cross-cuts overlay (SpikesTab). |
| `ProbeStack.EnterCpuAlloc/LeaveCpuAlloc` + alloc columns | `systems/allocation-tracking.md` | Distinct subsystem with its own IL emission shape and UI surface. |
| `Profiling/Insights/*` | `systems/insights-engine.md` | Largest single subsystem, with two surfaces. |
| `Profiling/SessionLogWriter.cs` + `ProfilerSystem` write wrapping | `systems/session-logging.md` | High-severity audit area; v4 schema; atomic writes; self-disable. |
| `UI/Overlay/*` + `UI/Overlay/Tabs/*` + `UI/ProfilerOverlay*.cs` | `systems/overlay.md` | The whole tab framework + five tab implementations. |
| `Profiling/Events/*` | `systems/events-and-context.md` | A whole subsystem fed by engagement-surfaces and feeding EventsTab. |
| `Tests/PerformanceProfiler.Tests.csproj` + three fixtures | `systems/test-harness.md` | Non-shipping subsystem with its own csproj and build-time isolation. |
| `PerformanceProfiler.cs` + `ProfilerSystem.cs` lifecycle wiring | `systems/mod-lifecycle.md` | The orchestrator. |

## Post-run file inventory

```
context/
├── _Overview.md                              (rewritten)
├── _staleness-report.md                      (this file)
├── architecture.md                           (new)
├── notes.md                                  (new)
├── integration/
│   └── integration-map.md                    (moved + refreshed)
├── tmodloader/
│   ├── hook-surface.md                       (moved + expanded)
│   ├── monomod-detours.md                    (moved + expanded)
│   ├── lifecycle-and-loop.md                 (moved + expanded)
│   ├── ui-system.md                          (moved + expanded)
│   ├── mod-identity.md                       (moved + status appended)
│   ├── engagement-surfaces.md                (moved + expanded)
│   └── ilhook-migration-research.md          (moved from root)
├── systems/
│   ├── hook-instrumentation.md               (new)
│   ├── metric-collection.md                  (new)
│   ├── spike-detection.md                    (new)
│   ├── allocation-tracking.md                (new)
│   ├── insights-engine.md                    (new)
│   ├── session-logging.md                    (new)
│   ├── overlay.md                            (new)
│   ├── events-and-context.md                 (new)
│   ├── test-harness.md                       (new)
│   └── mod-lifecycle.md                      (new)
├── notes/
│   ├── decisions.md                          (refreshed)
│   ├── conventions.md                        (new)
│   ├── future-html-report.md                 (preserved)
│   ├── future-settings-design.md             (preserved)
│   ├── litedb-migration-plan.md              (preserved)
│   ├── events-tab-plan.md                    (status header)
│   ├── insights-engine-plan.md               (status header)
│   ├── overview-tab-plan.md                  (status header)
│   ├── spikes-and-allocations-plan.md        (status header)
│   └── ilhook-migration-plan.md              (status header)
└── plans/code-health-audit/                  (preserved)
```
