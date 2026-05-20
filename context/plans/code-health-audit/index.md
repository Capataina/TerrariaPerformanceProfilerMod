# Code Health Audit

**Date:** 2026-05-20  
**Scope:** full repository, with Pass-2 deep dives on hook instrumentation, overlay UI, persistence/session logging, insights engine, and build/test infrastructure  
**Status:** complete

## Summary

The audit found 16 certain findings and 6 potential issues. The highest-value work is to make the profiler's observability surfaces trustworthy: backend-aware coverage in session JSON, persistence failure isolation, a safe non-shipping test harness, and honesty fixes in the Insights ranking/badge path. Production source was not edited.

## What I Did Not Do

| Obligation | Status | Evidence |
|---|---|---|
| Pre-Pass-1 front-loaded external research | done | Query `code health audit patterns for C# tModLoader mod`; URL `https://www.bing.com/search?q=code+health+audit+patterns+for+C%23+tModLoader+mod`; recorded in `obligation-evidence-map.md`. |
| Mandatory reference loading | done | Read all ten `code-health-audit/references/*.md` files before writing findings. |
| Pass-1 checkpoint written before final output | done | `context/plans/code-health-audit/PASS-1-CHECKPOINT.md`. |
| Project test/build baseline captured | done | `test_baseline.sh` found no recognised test stack; `dotnet msbuild` compiled DLL then failed package step due locked `.tmod`; recorded in Pass 1. |
| Pre-existing test failures recorded | done | No test suite exists, so no failing tests could be enumerated; missing infrastructure is recorded in `build-and-tests.md`. |
| Research obligation per substantive system | done | Five system rows in `obligation-evidence-map.md`, each with query, URL, and mode. |
| Research mode variety | done | Modes 1, 2, and 3 are represented in `obligation-evidence-map.md`. |
| Diagnostic tests written where required | partial | No tests written. Reasoned omission: no safe non-shipping C# test harness exists; adding `.cs` tests inside this mod source risks `.tmod` packaging unless production build metadata changes. Findings are either high-confidence from direct code evidence or marked possible/potential where tests/runtime evidence are needed. |
| Modularisation candidate list enumerated | done | Four candidates listed in `PASS-1-CHECKPOINT.md`. |
| Every modularisation candidate has a verdict | done | `PASS-2-SYSTEMS-AUDITED.md` records four verdicts. |
| Confidence upgrade pathway attempted | done | Additional code reads, external docs, symbol searches, and potential-issue separation were used; no moderate/low certain finding relies on untested speculation without a reasoned omission. |
| Pass-2 systems-audited checkpoint written | done | `context/plans/code-health-audit/PASS-2-SYSTEMS-AUDITED.md`. |
| Potential-issues sweep ran | done | `context/plans/code-health-audit/potential-issues.md` contains 6 entries. |
| Certain-set non-regression check | done | `PASS-2-SYSTEMS-AUDITED.md` states no finding was moved to potential to avoid proof obligations. |
| Data Layout and Memory Access Patterns applied | done | Per-system data-layout decisions recorded in `PASS-2-SYSTEMS-AUDITED.md`; concrete allocation findings recorded in hook and overlay files. |
| Orphan detection | done | `orphans.py` ran and reported no orphan candidates; C# dead-code proof used `Grep` symbol checks. |
| Production source not modified | done | `git status --short` shows only `?? context/plans/`; `git diff --stat` produced no tracked production diff. |
| Evidence-map lint | done | `evidence_map_lint.py` returned clean with 5 rows inspected and research modes `[1, 2, 3]`. |
| Audit termination receipt | done | `finalize_audit.py` receipt pasted below. |

## Findings Overview

| File | System | Critical | High | Medium | Low | Total |
|---|---|---:|---:|---:|---:|---:|
| [hook-instrumentation.md](hook-instrumentation.md) | Hook instrumentation | 0 | 0 | 2 | 2 | 4 |
| [overlay-ui.md](overlay-ui.md) | Overlay UI | 0 | 0 | 2 | 0 | 2 |
| [persistence-session-logging.md](persistence-session-logging.md) | Persistence/session logging | 0 | 1 | 3 | 0 | 4 |
| [insights-engine.md](insights-engine.md) | Insights engine | 0 | 0 | 3 | 1 | 4 |
| [build-and-tests.md](build-and-tests.md) | Build/test infrastructure | 0 | 1 | 1 | 0 | 2 |
| **Total** |  | **0** | **2** | **11** | **3** | **16** |

## Priority Actions

1. **[HIGH]** Add a non-shipping C# test harness before more pure logic accumulates — [build-and-tests.md#create-a-non-shipping-c-test-harness-before-more-pure-logic-accumulates](build-and-tests.md#create-a-non-shipping-c-test-harness-before-more-pure-logic-accumulates)
2. **[HIGH]** Isolate session logging failures from world lifecycle — [persistence-session-logging.md#isolate-session-logging-failures-from-world-lifecycle](persistence-session-logging.md#isolate-session-logging-failures-from-world-lifecycle)
3. **[MEDIUM]** Use backend-aware coverage counters in session JSON — [hook-instrumentation.md#use-backend-aware-coverage-counters-in-session-json](hook-instrumentation.md#use-backend-aware-coverage-counters-in-session-json)
4. **[MEDIUM]** Score share-based insight magnitudes as fractions — [insights-engine.md#score-share-based-insight-magnitudes-as-fractions-not-ratios-above-one](insights-engine.md#score-share-based-insight-magnitudes-as-fractions-not-ratios-above-one)
5. **[MEDIUM]** Prevent untested observations from promoting to medium confidence — [insights-engine.md#prevent-untested-observations-from-promoting-to-medium-confidence-by-repetition-alone](insights-engine.md#prevent-untested-observations-from-promoting-to-medium-confidence-by-repetition-alone)
6. **[MEDIUM]** Move truncation allocations out of per-row draw paths — [overlay-ui.md#move-truncation-allocations-out-of-per-row-draw-paths](overlay-ui.md#move-truncation-allocations-out-of-per-row-draw-paths)

## By Category

| Category | Count | Main systems |
|---|---:|---|
| Known Issues and Active Risks | 8 | Session logging, insights, overlay contract, build baseline, coverage JSON |
| Test Coverage Gaps | 2 | Build/test infrastructure, session schema |
| Modularisation | 1 | `SessionLogWriter.cs` |
| Dead Code Removal | 1 | `HookInterceptor.cs` legacy helpers |
| Pattern Extraction | 1 | Backend category routing |
| Performance Improvement | 2 | Overlay truncation, spike window view wrapper |
| Documentation Rot | 1 | Gated free-removal detector comments |

## Potential Issues

See [potential-issues.md](potential-issues.md) for six follow-ups that need runtime evidence or a product decision before implementation.

## Implementation Receipt (2026-05-20, post-audit)

The audit was followed by an in-session implementation pass across three commits.
Status of every certain finding and every potential issue is recorded below;
the audit's own termination receipt follows unchanged.

### Certain findings — implementation status

| File | Finding | Status | Notes |
|---|---|---|---|
| hook-instrumentation | Remove legacy delegate hook-name arrays and helper methods | done | Round 1 — ~180 lines deleted; HookCoverageVersion bumped to 3. |
| hook-instrumentation | Use backend-aware coverage counters in session JSON | done | Round 1 — new `HookCoverageView`; overlay + TreeTab + SessionLogWriter all route through it. |
| hook-instrumentation | Share hook category routing between backends | done | Round 1 — new `HookCategoryRouter.ResolveCategory`. |
| hook-instrumentation | Cache the spike window view exposed to consumers | done | Round 1 — `SpikeDetector` allocates `_windowsView` at construction. |
| overlay-ui | Reconcile `IOverlayTab.IsAvailable` with tab chrome behaviour | done | Round 2 — enforced via `TabRegistry.Visible` + `ResolveActive`. |
| overlay-ui | Move truncation allocations out of per-row draw paths | done | Round 2 — `OverviewTab._truncatedNames` and `InsightsTab._rankedBodies` caches refilled at 1 Hz. |
| persistence-session-logging | Isolate session logging failures from world lifecycle | done | Round 1 — `ProfilerSystem` wraps `Create`/`Tick`/`End` in try/catch with `SessionLogFailureException` self-disable. |
| persistence-session-logging | Write session reports through a temp file and same-directory replace | done | Round 1 — new `AtomicWrite` using `File.Replace` (with `File.Move` first-write fallback). |
| persistence-session-logging | Split pure report construction from file I/O in `SessionLogWriter` | DEFERRED | Pure code-org refactor with zero behaviour change. Best landed once the new test harness can safety-net the relocation. |
| persistence-session-logging | Add schema snapshot coverage for agent-readable session reports | DEFERRED | Depends on the split above. Belongs in the follow-up commit that does the split. |
| insights-engine | Score share-based insight magnitudes as fractions, not ratios above one | done | Round 2 — `RankingScorer.NormaliseMagnitude` is now pattern-aware. |
| insights-engine | Prevent untested observations from promoting to medium confidence | done | Round 2 — `InsightStore.PromoteConfidence` gates Medium on `PValueAdjusted <= 0.10`. Pinned by `Tests/InsightStoreTests.cs`. |
| insights-engine | Separate data-strength badges from confidence badges | done | Round 2 — new `EvidenceScope` enum + field; rendered alongside Confidence in `InsightsTab`. |
| insights-engine | Correct the gated free-removal detector comments | done | Round 2 — docstring rewritten to state the actual gated-roster contract. |
| build-and-tests | Create a non-shipping C# test harness | done | Round 3 — `Tests/PerformanceProfiler.Tests.csproj` (xUnit). 10/10 tests pass; main mod build unaffected. |
| build-and-tests | Treat the current `.tmod` packaging failure as an environment blocker | acknowledged | tModLoader was open during every dev-side build; the C# DLL compiles cleanly, only `.tmod` packaging hits the file lock. Closing tML resolves it. |

### Potential issues — implementation status

| # | Issue | Status | Notes |
|---|---|---|---|
| 1 | Partial ILHook install failure may leave already-added hooks undisposed | done | Round 1 — `ILHookInterceptor.Install` outer catch now calls `Uninstall()`. |
| 2 | Delegate fallback install failures may be counted as measured hooks | done | Round 1 — `TryHookSupportedOverride` returns a tri-state `InstallOutcome`; install failures get their own counter. |
| 3 | Open spike windows may be missing from final session reports | done | Round 1 — `MetricCollector.FlushSpikes()` is called in `OnWorldUnload` before the final `End()`. |
| 4 | Public two-arg `PerModAttribution.Add` appears unused | done | Round 1 — overload removed. |
| 5 | Session log pruning may delete manual JSON artefacts | done | Round 1 — `PruneIncompatibleLogs` narrowed to the writer-owned filename shape via `LooksLikeOurReport`. |
| 6 | Insights have a player surface before an agent/session-log surface | done | Round 2 — `SessionLogWriter` schema v4 includes an `insights` block (live + history + gated). |

### Deferred for a follow-up commit

* persistence-session-logging modularisation finding (split `SessionLogWriter` into lifecycle/IO + pure `SessionReportBuilder`) — pure refactor, no behaviour change. Best landed alongside the schema-snapshot test the same finding implies, now that the test harness exists.

## Audit Termination Receipt

# Audit Termination Receipt — generated by finalize_audit.py

_Generated: 2026-05-20T00:55:46Z_
_Audit folder: `/Users/atacanercetinkaya/Library/Application Support/Terraria/tModLoader/ModSources/PerformanceProfiler/context/plans/code-health-audit`_

## Lint

- Command: `python3 scripts/evidence_map_lint.py /Users/atacanercetinkaya/Library/Application Support/Terraria/tModLoader/ModSources/PerformanceProfiler/context/plans/code-health-audit/obligation-evidence-map.md`
- Exit code: 0
- Output (verbatim):

```
# Evidence map lint: clean

_Checked: `/Users/atacanercetinkaya/Library/Application Support/Terraria/tModLoader/ModSources/PerformanceProfiler/context/plans/code-health-audit/obligation-evidence-map.md`_

Rows inspected: 5
Research modes detected: [1, 2, 3]
```

## Counts

- Certain findings: 16
- Potential issues: 6
- Modularisation verdicts: split-recommended=4, leave-as-is=4, not-applicable=0

## Audit folder contents

```
- PASS-1-CHECKPOINT.md
- PASS-2-SYSTEMS-AUDITED.md
- build-and-tests.md
- hook-instrumentation.md
- index.md
- insights-engine.md
- obligation-evidence-map.md
- overlay-ui.md
- persistence-session-logging.md
- potential-issues.md
```

_Paste this entire block verbatim into `index.md` under the section `## Audit Termination Receipt`. The Quality Checklist requires its presence; absence of this section is a checklist failure._
