# Code Health Audit — Obligation Evidence Map

## Run Metadata

| Field | Value |
|---|---|
| Project | Performance Profiler |
| Repo root | `/Users/atacanercetinkaya/Library/Application Support/Terraria/tModLoader/ModSources/PerformanceProfiler` |
| Started | 2026-05-20 |
| Production source edit policy | Production source stayed untouched; audit wrote plan files only. Diagnostic tests were not written because the repo has no non-shipping C# test harness and adding `.cs` tests inside the mod source would risk packaging them into the `.tmod` unless production build metadata changed. |

## Front-Loaded External Research

| Obligation | Query | Retrieval tool | Source URL | Result | Notes |
|---|---|---|---|---|---|
| Pre-Pass-1 external research | `code health audit patterns for C# tModLoader mod` | `webfetch` search-results URL, because no literal `WebSearch` tool is available in this runtime | `https://www.bing.com/search?q=code+health+audit+patterns+for+C%23+tModLoader+mod` | done | Search results were low-signal; Pass-2 rows use direct primary/source-adjacent URLs. |

## Research Mode Distribution

| Mode | Count | Evidence |
|---|---:|---|
| Mode 1 — domain pattern lookup | 2 | Insights statistics/confidence research; .NET test-platform research. |
| Mode 2 — specific-technique evaluation | 2 | Overlay string allocation research; persistence atomic-write research. |
| Mode 3 — known-anti-pattern check | 1 | tModLoader MonoModHooks hook-install/IL-hook failure surface research. |

## System Evidence Rows

| System | Substantive? | Research obligation | Diagnostic-test obligation | Data-layout decision | Modularisation verdicts | Findings / potential issues | Status |
|---|---|---|---|---|---|---|---|
| Hook instrumentation and attribution | yes | Query: `tModLoader MonoModHooks Add Modify hook teardown instrumentation failure modes`; Source: `https://docs.tmodloader.net/docs/stable/class_mono_mod_hooks.html`; Mode 3 anti-pattern check. Source confirms `MonoModHooks.Add`, `Modify`, and hook dump surfaces, matching the abort-clean/read-only instrumentation risk surface. | Reasoned omission: no diagnostic test written. Static evidence is high for dead helpers and counter-selection drift; fault-injecting partial ILHook install requires a tModLoader host/runtime hook surface the audit cannot safely create without production changes. Potential failure-path items are in `potential-issues.md`. | Applied. Hot-path delegate and IL paths reviewed for allocation/counter surfaces; data-layout findings limited to avoiding per-draw/list-view allocations, not instrumentation arrays. | `Profiling/HookInterceptor.cs`: `split-recommended`; `Profiling/ILHookInterceptor.cs`: `leave-as-is`. | 4 certain findings in `hook-instrumentation.md`; 3 potential issues in `potential-issues.md`. | done |
| Overlay UI and impact scoring | yes | Query: `C# Substring allocation hot draw path AsSpan`; Source: `https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1846`; Mode 2 specific-technique evaluation. Source states `Substring` allocates a new heap string and many short-lived hot-path strings create GC pressure. | Reasoned omission: no diagnostic test written. The allocation is directly visible in `OverlayDraw.Truncate`; runtime magnitude would need in-game allocation profiling. Adding a C# UI test harness is blocked by the no non-shipping test project constraint. | Applied. Overlay draw paths and cached row-building paths reviewed; finding focuses on moving truncation work out of per-frame row draw. | `UI/Overlay/Tabs/OverviewTab.cs`: `leave-as-is`. | 2 certain findings in `overlay-ui.md`. | done |
| Persistence/session logging | yes | Query: `File.WriteAllText truncates overwritten files File.Replace atomic replacement .NET`; Sources: `https://learn.microsoft.com/en-us/dotnet/api/system.io.file.writealltext?view=net-8.0`, `https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace?view=net-8.0`; Mode 2 specific-technique evaluation. Sources state `WriteAllText` truncates/overwrites existing files and `File.Replace` replaces destination contents from another file. | Reasoned omission: no diagnostic test written. The useful tests require an extracted atomic-writer/report-builder seam or a safe separate test project. Current code hard-codes paths and the audit cannot edit production code to inject a file-system seam. | Applied. I/O paths, anonymous schema construction, pruning, and report array allocations were reviewed. Data-layout finding is structural: report shaping should be pure/testable, while hot tick path remains outside this writer. | `Profiling/SessionLogWriter.cs`: `split-recommended`. | 4 certain findings in `persistence-session-logging.md`; 1 potential issue in `potential-issues.md`. | done |
| Insights engine | yes | Query: `multiple comparison corrections false discovery rate Benjamini Hochberg analytics insight confidence`; Source: `https://www.statsig.com/blog/multiple-comparison-corrections-in-a-b`; Mode 1 domain pattern lookup. Source explains false positives under multiple tests and BH/FDR as a control method, matching the insights plan's confidence/flood-control risk. | Reasoned omission: no diagnostic test written. Pure unit tests would be valuable for scoring and confidence promotion, but the repo currently has no non-shipping C# test harness. Code evidence is high enough to issue the findings; tests are recommended as part of the build/test finding. | Applied. Pure scoring/store paths reviewed for allocation and ranking semantics. The key risks are semantics/honesty, not cache layout. | No Pass-1 modularisation candidate. | 4 certain findings in `insights-engine.md`; 1 potential issue in `potential-issues.md`. | done |
| Build and test infrastructure | yes | Query: `.NET 8 dotnet test test project VSTest Microsoft Testing Platform`; Source: `https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test`; Mode 1 domain pattern lookup. Source documents `dotnet test` as the .NET test runner surface and the VSTest/Microsoft.Testing.Platform modes. | Reasoned omission: no diagnostic test written because the finding is the absence of a safe test harness. `test_baseline.sh` found no recognised stack, `Glob` found no `*Test*.cs`, and `dotnet msbuild` packaging was environment-blocked by a locked `.tmod`. | Not applicable. Build/test harness has no data-layout surface beyond excluding tests from `.tmod` packaging. | No Pass-1 modularisation candidate. | 2 certain findings in `build-and-tests.md`. | done |

## Tool Obligation Rows

| Obligation | Evidence | Status |
|---|---|---|
| `file_size_scan.py` broad sweep | `python3 /Users/atacanercetinkaya/.config/opencode/skills/code-health-audit/scripts/file_size_scan.py <repo>` scanned 160 source files and flagged 15 over-threshold rows, including duplicate `.claude/worktrees` copies. Production interpretation lives in `PASS-1-CHECKPOINT.md`. | done |
| Test baseline | `bash /Users/atacanercetinkaya/.config/opencode/skills/code-health-audit/scripts/test_baseline.sh <repo>` found no recognised test stack; direct `dotnet msbuild` compiled `PerformanceProfiler.dll` but `.tmod` packaging failed due file lock. | done |
| Modularisation candidate enumeration | Python/Rust helper reported no Python/Rust; C# fallback line count fixed four candidates in `PASS-1-CHECKPOINT.md`: `HookInterceptor.cs`, `SessionLogWriter.cs`, `ILHookInterceptor.cs`, `OverviewTab.cs`. Verdicts are in `PASS-2-SYSTEMS-AUDITED.md`. | done |
| Import graph / hotspot scripts or C# fallback | `import_graph.py` and `hotspot_intersect.py` returned zero files because helper coverage is Python/Rust; C# fallback used manual call-chain, public-surface, line-count, churn, and criticality analysis during Pass 2. | done |
| Orphan detection | `python3 /Users/atacanercetinkaya/.config/opencode/skills/code-health-audit/scripts/orphans.py <repo>` ran. Output: `_No orphan candidates detected._`; caveat that the script is Python/Rust-oriented and C# dead-code proof used `Grep` symbol-reference checks. | done |
| Evidence-map lint | `python3 /Users/atacanercetinkaya/.config/opencode/skills/code-health-audit/scripts/evidence_map_lint.py <audit>/obligation-evidence-map.md` returned clean: 5 rows inspected; research modes `[1, 2, 3]`. | done |
| Finalize audit receipt | `python3 /Users/atacanercetinkaya/.config/opencode/skills/code-health-audit/scripts/finalize_audit.py <audit>` emitted the required receipt; the verbatim receipt is in `index.md`. | done |
