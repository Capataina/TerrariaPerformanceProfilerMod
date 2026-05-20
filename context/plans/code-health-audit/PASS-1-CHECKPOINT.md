# Code Health Audit — Pass 1 Checkpoint

**Date:** 2026-05-20  
**Scope:** full repository worktree, excluding generated build output and `.claude/worktrees/` duplicate agent worktrees.  
**Policy:** production source remains read-only; this audit writes only diagnostic tests when justified and `context/plans/code-health-audit/` artefacts.

## Current Project Shape

| Signal | Evidence | Consequence |
|---|---|---|
| Stack | C# / .NET 8 tModLoader mod; `PerformanceProfiler.csproj` imports `..\tModLoader.targets`; nullable enabled. | Helper scripts for Python/Rust graph analysis do not cover the primary language; C# fallback scans are required. |
| Product state | README says Milestone 1 Lite-mode MVP in progress; code already contains hook attribution, per-tick metric collection, overlay tabs, spike/allocation surfaces, and untracked Insights work. | Audit evaluates current implementation reality, not the older scaffold wording. |
| Hard invariants | Read-only instrumentation, measured overhead budget, descriptive UI copy, abort-clean host-drift behaviour. | Findings that would change gameplay, add hot-path allocation, editorialise insight text, or weaken abort-clean behaviour are out of scope. |
| Worktree state | `git status --short` currently reports only `?? context/plans/`; `git ls-files 'Profiling/Insights/**' 'UI/Overlay/Tabs/InsightsTab.cs'` confirms Insights files are tracked. | Audit-added artefacts are confined to the plan folder; Insights is tracked production source and remains read-only. |
| Context notes | `context/_Overview.md`, `context/integration-map.md`, and `context/notes/decisions.md` record the API-first, clone-on-wall, MonoMod On-hook attribution decision. | Do not re-litigate IL-edit-only architecture; current hook strategy is On-hook first, IL reserved for cases On-hooks cannot reach. |

## Test And Build Baseline

| Check | Command | Result | Interpretation |
|---|---|---|---|
| Audit test detector | `bash /Users/atacanercetinkaya/.config/opencode/skills/code-health-audit/scripts/test_baseline.sh /Users/atacanercetinkaya/Library/Application Support/Terraria/tModLoader/ModSources/PerformanceProfiler` | No recognised stack marker for Cargo / pyproject / requirements / package / go. | Known Issue: no detectable automated test infrastructure. For C# this means the helper missed the stack; no `dotnet test` project exists either. |
| tModLoader build baseline | `dotnet msbuild` | DLL compilation succeeded, then `.tmod` packaging failed with TML003 because `/Users/atacanercetinkaya/Library/Application Support/Terraria/tModLoader/Mods/PerformanceProfiler.tmod` is locked by a running tModLoader/mod instance. | Source compile is healthy enough to produce `bin/Debug/net8.0/PerformanceProfiler.dll`; package baseline is blocked by environment, not by a compile error. Close/disable the mod before rerunning. |

## Mandatory Script Outputs

### `file_size_scan.py`

The script is language-agnostic, so it found C# files, but it also counted duplicate files under `.claude/worktrees/`. Production findings below use the non-worktree C# fallback table.

```text
# File-size scan (Pass 1 broad-sweep)

_Root: `/Users/atacanercetinkaya/Library/Application Support/Terraria/tModLoader/ModSources/PerformanceProfiler`_
_Total source files scanned: 160_
_Files at-or-over language threshold: 15_

Top production rows after excluding `.claude/worktrees/`:
1. `Profiling/HookInterceptor.cs` — 1424 lines — C# threshold 550 — OVER
2. `Profiling/SessionLogWriter.cs` — 642 lines — C# threshold 550 — OVER
3. `Profiling/ILHookInterceptor.cs` — 585 lines — C# threshold 550 — OVER
4. `UI/Overlay/Tabs/OverviewTab.cs` — 480 lines — top decile — ok by threshold, candidate by relative size
5. `UI/Overlay/Tabs/TreeTab.cs` — 441 lines — ok
6. `Profiling/MetricCollector.cs` — 410 lines — ok
7. `Profiling/PerModAttribution.cs` — 406 lines — ok
8. `UI/Overlay/OverlayPanel.cs` — 391 lines — ok
9. `Profiling/SpikeDetector.cs` — 359 lines — ok
10. `Profiling/ModImpactScorer.cs` — 321 lines — ok
```

### C# Fallback Line Count

Command: `rg --files -g '*.cs' -g '!bin/**' -g '!obj/**' -g '!.claude/**' | xargs wc -l`

```text
1424 Profiling/HookInterceptor.cs
 642 Profiling/SessionLogWriter.cs
 585 Profiling/ILHookInterceptor.cs
 480 UI/Overlay/Tabs/OverviewTab.cs
 441 UI/Overlay/Tabs/TreeTab.cs
 410 Profiling/MetricCollector.cs
 406 Profiling/PerModAttribution.cs
 391 UI/Overlay/OverlayPanel.cs
 359 Profiling/SpikeDetector.cs
 321 Profiling/ModImpactScorer.cs
 253 Profiling/PerTickAttributionRing.cs
 191 Profiling/ProbeStack.cs
 185 Profiling/Insights/InsightRenderer.cs
 179 Profiling/Insights/InsightStore.cs
 164 Profiling/ProfilerSystem.cs
 150 UI/Overlay/Tabs/SpikesTab.cs
 149 Profiling/Insights/InsightRecord.cs
 133 Profiling/Insights/Detectors/GatedDetectors.cs
 129 UI/Overlay/Tabs/InsightsTab.cs
 120 Profiling/RingBuffer.cs
 113 UI/Overlay/OverlayState.cs
 109 UI/ProfilerOverlaySystem.cs
 106 Profiling/HookBackend.cs
 105 Profiling/Insights/InsightsEngine.cs
 104 Profiling/Insights/Detectors/PeakContributorToSpikeDetector.cs
 103 Profiling/Insights/RankingScorer.cs
 101 UI/ProfilerTheme.cs
  96 Profiling/Insights/Detectors/HotHookDominanceDetector.cs
  95 Profiling/Insights/Detectors/AllocationBurstDetector.cs
  89 UI/Overlay/IOverlayTab.cs
  85 UI/Overlay/OverlayDraw.cs
  83 Profiling/Insights/Detectors/FreeRemovalCandidateDetector.cs
  68 UI/Overlay/OverlayLayout.cs
  64 PerformanceProfiler.cs
  64 Profiling/Insights/IInsightDetector.cs
  57 UI/Overlay/TabRegistry.cs
  55 Profiling/TickFrame.cs
  43 Profiling/PerModSample.cs
  34 UI/ProfilerOverlay.cs
8686 total
```

### Python/Rust/TS Helper Coverage

| Script | Output | C# fallback disposition |
|---|---|---|
| `modularisation_candidates.py` | No Python or Rust source files found. | Use C# threshold/top-decile line-count list above. |
| `import_graph.py --top 30` | Files considered: 0. Edges: 0. | Use manual C# call-chain and public-surface scan during Pass 2. |
| `hotspot_intersect.py --top 25` | Files: 0. | Use file size, git churn, runtime criticality, and context risks instead. |

## Recent Churn Signal

Command: `git log --since=90.days.ago --name-only --pretty=format: -- '*.cs' | sort | uniq -c | sort -nr`

| File | 90-day touches | Interpretation |
|---|---:|---|
| `UI/ProfilerOverlay.cs` | 15 | Legacy UI wrapper has been actively replaced by modular overlay files; check for drift/dead wrapper surface. |
| `Profiling/MetricCollector.cs` | 10 | Core metric hot path is both critical and changing. |
| `Profiling/HookInterceptor.cs` | 8 | Largest file and central instrumentation surface. |
| `Profiling/ProfilerSystem.cs` | 6 | Lifecycle owner; check teardown and dual-surface observability. |
| `Profiling/PerModAttribution.cs` | 5 | Hot-path attribution storage; check allocation and layout. |
| `Profiling/ILHookInterceptor.cs` | 5 | Large fallback/alternate backend; check current role vs decisions. |

The raw churn command includes a blank-line count row from commit separators; it has no file meaning and is ignored.

## Comment / Marker Scan

Command: `Grep TODO|FIXME|HACK|WORKAROUND|TEMPORARY|DEPRECATED|throw new NotImplemented|catch (...) {}` in `*.cs`.

| Result | Interpretation |
|---|---|
| Six `TODO` comments in `Profiling/Insights/Detectors/GatedDetectors.cs`; all point to gated sibling data streams (`Events`, `LiteDB`, allocation deltas, per-hook distributions). | Not immediate rot by itself because the Insights work is untracked/in-progress and the comments cite exact plan dependencies. Pass 2 should verify these gates render honestly and do not produce misleading player claims. |

## Modularisation Candidate List

Candidate rule for C#: files exceeding 550 lines OR top decile by line count. There are 39 non-worktree C# files, so top decile = top 4 files.

| Candidate | Lines | Qualifying reason | Pass-2 verdict required |
|---|---:|---|---|
| `Profiling/HookInterceptor.cs` | 1424 | Exceeds C# threshold and top decile. | yes |
| `Profiling/SessionLogWriter.cs` | 642 | Exceeds C# threshold and top decile. | yes |
| `Profiling/ILHookInterceptor.cs` | 585 | Exceeds C# threshold and top decile. | yes |
| `UI/Overlay/Tabs/OverviewTab.cs` | 480 | Top decile by line count. | yes |

## Pass-2 System Prioritisation

| Priority | System | Files | Why this is substantive | Pass-2 focus |
|---:|---|---|---|---|
| 1 | Hook instrumentation and attribution | `Profiling/HookInterceptor.cs`, `Profiling/ILHookInterceptor.cs`, `Profiling/HookBackend.cs`, `Profiling/ProbeStack.cs`, `Profiling/PerModAttribution.cs`, `Profiling/MetricCollector.cs`, `Profiling/PerTickAttributionRing.cs`, `Profiling/SpikeDetector.cs` | Largest, hottest, and highest-risk system; touches Invariants 1, 2, and 4. | Hot-path allocation, data layout, backend duplication, abort-clean paths, teardown ownership, hook coverage accounting. |
| 2 | Overlay UI and impact scoring | `UI/Overlay/**`, `Profiling/ModImpactScorer.cs` | Main player surface; carries honesty contract and dual-surface observability. | Repeated drawing/allocation patterns, tab modularity, neutral wording, stale legacy wrapper surface. |
| 3 | Persistence/session logging | `Profiling/SessionLogWriter.cs` | Large file, current JSON path, likely to be superseded by LiteDB plan but still current code. | JSON writer modularity, repeated serialisation patterns, file I/O lifetime, schema drift, build/run observability. |
| 4 | Insights engine | `Profiling/Insights/**`, `UI/Overlay/Tabs/InsightsTab.cs` | Untracked current production source implementing a future-facing subsystem with honesty-contract risk. | Gated detector behaviour, string neutrality, allocation patterns, testability of pure ranking/rendering logic. |
| 5 | Build and test infrastructure | `PerformanceProfiler.csproj`, `build.txt`, repository test surface | No detectable tests and package build currently blocked by running tModLoader lock. | Known Issue finding; diagnostic-test feasibility; minimal non-production test path if it can resolve uncertainty without bloating the repo. |

## Known Issues Surfaced In Pass 1

| Issue | Evidence | Initial category |
|---|---|---|
| No automated test project is present. | `test_baseline.sh` found no recognised stack; `Glob **/*Test*.cs` from prior scan found no test files; `PerformanceProfiler.csproj` is a mod project only. | Test Coverage Gaps / Known Issues |
| Package build is environment-blocked while tModLoader has the `.tmod` locked. | `dotnet msbuild` compiled the DLL then failed with TML003 and `System.IO.IOException` on `Mods/PerformanceProfiler.tmod`. | Known Issues and Active Risks |
| README/context milestone drift exists. | README still has M0/scaffold badge and a malformed milestones table row while `context/notes/decisions.md` says M0 was dropped and code reality is M1/M2 work. | Documentation Rot / Configuration Drift candidate |
| Helper scripts under-cover C#. | `import_graph.py` and `hotspot_intersect.py` reported zero files despite 39 C# files. | Audit limitation; fallback recorded in evidence map. |

## Pass-1 Exit Criteria

| Criterion | Status | Evidence |
|---|---|---|
| Context read | done | README, `context/_Overview.md`, `context/integration-map.md`, `context/notes/decisions.md`, active plan notes. |
| Broad codebase scan | done | 39 C# files / 8,686 lines; file-size table above. |
| Test/build baseline | done, with blocker | `test_baseline.sh` no tests; `dotnet msbuild` DLL compile succeeded, package failed due file lock. |
| Modularisation candidates fixed | done | Four candidates listed above. |
| Pass-2 systems fixed | done | Five systems listed above. |
