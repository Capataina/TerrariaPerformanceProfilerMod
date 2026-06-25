# Code Health Audit

**Date:** 2026-06-25
**Scope:** full repository — 6 system clusters (hook instrumentation, metric collection + detectors, data pipeline + segments, insights engine, web server + persistence, web UI assets + cross-cutting).
**Status:** active (findings ready to implement)
**Supersedes:** the 2026-06-22 audit in this folder. That run's headline findings have since landed — `MarkInstallEnd` forces a Gen2 + `TrimRetainedScaffolding` reclaims scaffolding (RAM 3.7→1.0 GB), the test csproj is repaired (108/108 pass), and the 18 CS0105 duplicate-usings are gone (this audit re-verified **zero** duplicate-usings across all 302 `.cs`). This is a fresh full sweep against current v0.22 reality.

## Summary

A fresh repository-wide audit by 6 parallel cluster deep-dives produced **88 findings** (0 critical · 13 high · 30 medium · 45 low) plus 8 potential issues. The compile gate is clean (0 `error CS`) and the pure-logic suite is healthy (108/108). The structural finding: this codebase's large files are **genuinely cohesive** — all 11 production modularisation candidates are `leave-as-is`, 2 archived ones `not-applicable`, 0 splits. The wins are elsewhere: hot-path **devirtualisation + caching** (the per-call `Stopwatch.Frequency` division; the per-tick `IReadOnlyList<double>` interface dispatch over a plain `double[]`), **dead-code removal** (write-only coverage surface, dead `_periodMadHist`/`CurrentDepth`/`Reset()`/`StallDetectorRef`, 10 dead usings in the server DTOs), a per-poll **full-DOM-rebuild gate** (`renderIfChanged` built but adopted nowhere), and a small set of **correctness fixes** (a `405` reason-phrase, an unclamped Pearson `r`, an unguarded negative `FreedBytes`, and — the one behaviour-changing fix — the `RunningStat.Without` catastrophic-cancellation guard that protects the honesty contract). Production source was not edited by the audit; all verification is read + grep + research + (for UI) the L4 harness.

## What I Did Not Do

| Obligation | Status | Evidence |
|------------|--------|----------|
| Pre-Pass-1 front-loaded WebSearch | done | "code health audit patterns for C# .NET 8 game mod zero-allocation hot path"; row 1 of `obligation-evidence-map.md` |
| Mandatory reference loading (all 10) | done | all `code-health-audit/references/*.md` read in load order before Pass 1 |
| Pass-1 checkpoint before Pass 2 | done | `PASS-1-CHECKPOINT.md` |
| Test/build baseline captured | done | `dotnet msbuild` → 0 `error CS`; `dotnet test` → **108 pass / 0 fail / 22 s** (no Known-Issues from baseline) |
| Research per substantive system | done | 6 cluster rows in `obligation-evidence-map.md`, each query + URL + mode |
| Research-mode variety (≥3) | done | modes 1, 2, 3 all represented (`evidence_map_lint.py` → `[1, 2, 3]`) |
| Diagnostic tests where moderate→high | partial (reasoned) | most findings high-confidence from grep/read; cluster-1 files tModLoader-dependent (not linkable); equivalence + correctness pins land **with their fixes** in implementation. Per-cluster reasoned omissions in the map. |
| Modularisation candidate list (Pass 1) | done | 13 candidates in `PASS-1-CHECKPOINT.md` §4 |
| Per-file modularisation verdict | done | 13/13 in `PASS-2-SYSTEMS-AUDITED.md` (11 leave-as-is, 2 not-applicable, 0 out-of-scope) |
| Confidence upgrade attempted before moderate/low | done | equivalence claims grounded in ULP/identical-by-construction reasoning; the High correctness finding research-grounded |
| Pass-2 systems-audited checkpoint | done | `PASS-2-SYSTEMS-AUDITED.md` |
| Potential-issues sweep (Pass 2.5) | done | `potential-issues.md` — 8 entries, each with locations + observation + reasoning + investigation + why-not-certain |
| Certain-set non-regression | done | no finding demoted to dodge proof; see `PASS-2-SYSTEMS-AUDITED.md` final section |
| Data Layout / Memory Access per system | done | per-cluster applicability table in `PASS-2-SYSTEMS-AUDITED.md` |
| Production source not modified | done | `git status` — only `context/plans/code-health-audit/**` touched by the audit |
| Scripts: Py/Rust vs C# fallback | done | `file_size_scan.py` used; rest fallback (`find`/`grep`/`git`/`dotnet`), recorded in the map's language-coverage note; `evidence_map_lint.py` + `finalize_audit.py` run normally |
| Evidence-map lint exit 0 | done | `evidence_map_lint.py` → clean, 7 rows, modes [1,2,3] |

## Findings Overview

| File | Cluster | Critical | High | Medium | Low | Total |
|------|---------|----------|------|--------|-----|-------|
| [hook-instrumentation.md](hook-instrumentation.md) | Hook instrumentation + self-health | 0 | 4 | 6 | 4 | 14 |
| [metric-collection.md](metric-collection.md) | Metric collection + detectors | 0 | 1 | 6 | 5 | 12 |
| [data-pipeline.md](data-pipeline.md) | Data pipeline + segments | 0 | 3 | 2 | 16 | 21 |
| [insights.md](insights.md) | Insights engine | 0 | 1 | 5 | 7 | 13 |
| [web-and-persistence.md](web-and-persistence.md) | Web server + persistence | 0 | 0 | 4 | 5 | 9 |
| [web-ui-and-crosscutting.md](web-ui-and-crosscutting.md) | Web UI + cross-cutting | 0 | 4 | 7 | 8 | 19 |
| **Total certain** | | **0** | **13** | **30** | **45** | **88** |
| [potential-issues.md](potential-issues.md) | Suspicions (separate bar) | — | — | — | — | 8 |

(Per-cluster severity splits are as reported by each cluster; exact per-finding severity is in each file.)

## Priority Actions

1. **[HIGH · free]** Cache the `Stopwatch.Frequency` reciprocal in the harvest hot path (3–5 divisions/tick → multiply) — [metric-collection.md](metric-collection.md).
2. **[HIGH · free]** Devirtualise the per-tick `IReadOnlyList<double>` fold via an `internal double[]` accessor on `MetricCollector` (also fixes `PerModCostTimeSeriesAggregator`) — [data-pipeline.md](data-pipeline.md).
3. **[HIGH · free]** Adopt `renderIfChanged` on the heavy poll panels (Lag/Insights/Memory) — kill the per-poll full-DOM rebuild — [web-ui-and-crosscutting.md](web-ui-and-crosscutting.md).
4. **[HIGH · free]** Remove the write-only delegate-path coverage surface + dead `ProbeStack.CurrentDepth` / `ProfilerSelfHealth.Reset()` / `MetricCollector.StallDetectorRef` — [hook-instrumentation.md](hook-instrumentation.md), [metric-collection.md](metric-collection.md).
5. **[HIGH · free]** Delete the dead `Baseline._periodMadHist` field — [data-pipeline.md](data-pipeline.md).
6. **[correctness · behaviour-changing]** Guard `RunningStat.Without` against catastrophic cancellation (protects the honesty contract — NOT a free upgrade; changes p-values for the better) — [insights.md](insights.md), [potential-issues.md](potential-issues.md) #3.
7. **[MED · free]** `ModOwnerCache.FromEntitySource` per-call `Substring` alloc + its false memoise docstring — [hook-instrumentation.md](hook-instrumentation.md).
8. **[MED · free]** `405` reason-phrase (`HTTP/1.1 405 OK` → `Method Not Allowed`); `EventJournal.AppendBatch` double-buffer; 10 dead usings in the server DTOs — [web-and-persistence.md](web-and-persistence.md).
9. **[MED · free]** Clamp Pearson `r` to [-1,1]; guard negative `FreedBytes` — [data-pipeline.md](data-pipeline.md).
10. **[MED · free]** Fix `DataRegistry`/`KpiStat` doc-comment registration site (`ProfilerSystem.Load` → `RegisterDataPipeline`); extract a `dtable()`/`sortableHead()` helper — [web-ui-and-crosscutting.md](web-ui-and-crosscutting.md).

## By Category

- **Dead Code Removal:** ~9 (write-only coverage surface, `_periodMadHist`, `CurrentDepth`, `Reset()`, `StallDetectorRef`, 10 server-DTO usings, dead `PeakContributorToSpikeDetector.Reset()`).
- **Performance / Data Layout:** ~12 (reciprocal cache, interface-dispatch devirt, `SumAll` fusion, `_catCount` cache, `EventJournal` double-buffer, per-poll DOM rebuild, per-render Map allocs).
- **Known Issues / Active Risks:** ~6 (`Without` cancellation, `405` phrase, negative `FreedBytes`, unfed `insights` collection, divergent lag sentinel filter).
- **Pattern Extraction:** ~4 (`dtable`/`sortableHead`, detector skeletons, Publish-stat boilerplate).
- **Documentation Rot:** ~6 (registration-site doc-comments, false memoise docstring, `NormaliseMagnitude` docstring, `_Overview` F10 note now stale, EMA-named-Median).
- **Inconsistent Patterns:** ~5 (router idioms, OKLCH-token sRGB drift, lag sentinel divergence).
- **API Surface / Complexity / Triage / Test-Coverage:** the remainder.
- **Disproved / verified-clean (recorded, not findings):** `WeatherSources.All` enumerator-boxing (concrete array); `ModOwnerCache.ForItem` (interned); router inline-math (none); abort-clean + read-only + zero-alloc-enqueue intact; honesty contract clean; monochrome-chrome clean; **zero duplicate-usings repo-wide**.

## Diagnostic tests

Audit phase wrote no new test files — see the map's per-cluster reasoned omissions (cluster-1 files are tModLoader-dependent and not linkable into the pure-logic suite; equivalence + correctness pins are written **with their fixes** in implementation, where they exercise the changed code and pass; UI is verified via the L4 Playwright harness, not xUnit). The 108-test suite is the regression ground for every implemented fix.

## Audit Termination Receipt

> The receipt's "Counts" line is `finalize_audit.py`'s coarse regex heuristic (it scans for marker strings); the authoritative counts are **88 certain findings + 8 potential issues**, and **13 modularisation verdicts** (11 leave-as-is + 2 not-applicable), per `PASS-2-SYSTEMS-AUDITED.md`. The load-bearing content is the verbatim lint result proving the evidence map passed at the terminal moment.

```
# Audit Termination Receipt — generated by finalize_audit.py

_Generated: 2026-06-25T10:20:20Z_
_Audit folder: `/Users/atacanercetinkaya/Library/Application Support/Terraria/tModLoader/ModSources/PerformanceProfiler/context/plans/code-health-audit`_

## Lint

- Command: `python3 scripts/evidence_map_lint.py /Users/atacanercetinkaya/Library/Application Support/Terraria/tModLoader/ModSources/PerformanceProfiler/context/plans/code-health-audit/obligation-evidence-map.md`
- Exit code: 0
- Output (verbatim):

# Evidence map lint: clean

Rows inspected: 7
Research modes detected: [1, 2, 3]

## Counts

- Certain findings: 92
- Potential issues: 8
- Modularisation verdicts: split-recommended=0, leave-as-is=3, not-applicable=1

## Audit folder contents

- PASS-1-CHECKPOINT.md
- PASS-2-SYSTEMS-AUDITED.md
- data-pipeline.md
- hook-instrumentation.md
- index.md
- insights.md
- metric-collection.md
- obligation-evidence-map.md
- potential-issues.md
- web-and-persistence.md
- web-ui-and-crosscutting.md
```
