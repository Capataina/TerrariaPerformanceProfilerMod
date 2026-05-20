# Code Health Audit — Pass 2 Systems Audited

**Date:** 2026-05-20  
**Scope:** systems fixed in `PASS-1-CHECKPOINT.md`  
**Production source policy:** no production source edits were made.

## System Completion Ledger

| System | Research evidence | Tests written | Findings count | Confidence |
|---|---|---|---:|---|
| Hook instrumentation and attribution | Query: `tModLoader MonoModHooks Add Modify hook teardown instrumentation failure modes`; URL: `https://docs.tmodloader.net/docs/stable/class_mono_mod_hooks.html`; mode 3 anti-pattern check. | None. Reasoned omission: runtime hook fault-injection requires a tModLoader host and production seams the audit cannot add. Static dead-code and counter-drift evidence is direct. | 4 certain, 3 potential | High for dead helpers/category drift/context drift; medium for failure-path potentials. |
| Overlay UI and impact scoring | Query: `C# Substring allocation hot draw path AsSpan`; URL: `https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1846`; mode 2 specific-technique evaluation. | None. Reasoned omission: allocation is directly visible; runtime size needs in-game profiling, not a repository test. | 2 certain | High for contract drift and allocation source; runtime magnitude unmeasured. |
| Persistence/session logging | Query: `File.WriteAllText truncates overwritten files File.Replace atomic replacement .NET`; URLs: `https://learn.microsoft.com/en-us/dotnet/api/system.io.file.writealltext?view=net-8.0`, `https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace?view=net-8.0`; mode 2 specific-technique evaluation. | None. Reasoned omission: useful tests require a non-shipping C# harness or production seams; current risk is proven by code path plus BCL docs. | 4 certain, 1 potential | High for current overwrite/no-boundary shape; medium for failure blast radius because runtime tModLoader exception handling was not exercised. |
| Insights engine | Query: `multiple comparison corrections false discovery rate Benjamini Hochberg analytics insight confidence`; URL: `https://www.statsig.com/blog/multiple-comparison-corrections-in-a-b`; mode 1 domain pattern lookup. | None. Reasoned omission: pure unit tests are recommended but blocked by missing non-shipping harness. Code evidence is direct enough for current findings. | 4 certain, 1 potential | High for scoring/confidence/badge/comment drift; medium for shipped-surface severity because the game was not run. |
| Build and test infrastructure | Query: `.NET 8 dotnet test test project VSTest Microsoft Testing Platform`; URL: `https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test`; mode 1 domain pattern lookup. | None. The absence of a safe test project is the finding. | 2 certain | High for no test project; high for package-lock blocker as current environment state. |

## Modularisation Verdicts

| Candidate | Verdict | Justification |
|---|---|---|
| `Profiling/HookInterceptor.cs` | `split-recommended` | 1,424 lines mix mod discovery, delegate fallback signature matching, coverage accounting, public delegate wrappers, and now-dead legacy hook-name helpers; splitting along existing responsibility seams reduces navigation and deletion risk without changing instrumentation semantics. |
| `Profiling/SessionLogWriter.cs` | `split-recommended` | 642 lines combine lifecycle/path ownership, pruning, anonymous JSON schema shaping, ranking, coverage projection, spike projection, hashing, and file writes; keeping the lifecycle writer while extracting pure report/schema construction is behaviour-preserving and makes tests possible. |
| `Profiling/ILHookInterceptor.cs` | `leave-as-is` | 585 lines are large but cohesive around one delicate ILHook lifecycle: discovery, closed-generic filtering, dedupe, manipulator install, and teardown; splitting now would scatter one abort-clean algorithm without a clear free seam. |
| `UI/Overlay/Tabs/OverviewTab.cs` | `leave-as-is` | 480 lines are a cohesive single-tab widget implementation; the real free wins sit in shared chrome/draw helpers, not in splitting Overview-specific state and paint code. |

## Data Layout / Memory Access Decisions

| System | Decision |
|---|---|
| Hook instrumentation | Hot instrumentation arrays are preallocated and slot-indexed. No data-layout rewrite recommended as a certain finding. Avoidable allocation was found in the spike window view exposed to UI/session consumers, recorded in `hook-instrumentation.md`. |
| Overlay UI | Per-row drawing should not allocate truncated strings. Finding recorded in `overlay-ui.md`. |
| Persistence/session logging | Report writing is cold relative to per-tick instrumentation. The main layout/testability issue is anonymous schema construction mixed with file I/O, recorded in `persistence-session-logging.md`. |
| Insights engine | Store/scorer use reusable buffers in the hot tab path. Findings are semantics/honesty issues rather than layout issues. |
| Build and test infrastructure | Not applicable. |

## Certain-Set Non-Regression Note

No observation was downgraded from certain finding to potential issue to avoid proof work. Potential issues are separated because their resolution requires runtime fault injection, gameplay timing, external public-API intent, or a product decision about whether a half-landed surface is considered shipped.
