# Code Health Audit — Pass 2 Systems Audited

**Date:** 2026-06-22
**Supersedes:** the 2026-05-20/21 Pass-2 snapshot in this folder.

Static snapshot of the final Pass-2 state. The live ledger is
`obligation-evidence-map.md` (which this agrees with).

## Per-system audit record

| System | Research (query / URL / mode) | Tests written (path / assertion / result) | Findings | Confidence |
|--------|-------------------------------|--------------------------------------------|----------|-----------|
| Hook-install RAM path | "MonoMod RuntimeDetour ILHook DynamicMethodDefinition DMDCecilGenerator retains ModuleDefinition memory after apply" / github.com/MonoMod/MonoMod ILHook.cs + MonoMod.Common DynamicMethodDefinition.cs / mode 3 | **Decompiled shipped binaries (ground truth):** `MonoMod.RuntimeDetour 25.3.2` DetourManager → `/tmp/dm.cs` (SourceCloneIl never disposed L346/643-663; LastContext MakeReadOnly not disposed L679; CleanILContexts supersession gate L695). `MonoMod.Utils 25.0.10` DynamicMethodDefinition → `/tmp/dmd.cs` (Module L59, Dispose L636). Plus `Tests/HookInstallRetentionDiagnostics.cs` (split-measurement methodology). | F1 (high), F2 (high), F3 (medium), P-1 | High (F1/F2 backed by decompiled binaries + passing test) |
| Self-health measurement | "GC.GetTotalMemory forceFullCollection false vs true measuring retained managed heap after transient allocation burst" / learn.microsoft.com GC.GetTotalMemory / mode 2 | `Tests/HookInstallRetentionDiagnostics.cs` — `ForcedCollection_IsRepeatable` (0 KB drift between two forced samples, PASS); `ForcedCollection_GivesStableRetainedMeasurement` (false=24.3 MB vs true=32.2 MB on 16 MB rooted set → 7.9 MB methodology spread, PASS). | F2 (high) | High |
| Build / test infrastructure | "dotnet test CS2001 source file could not be found Compile Include stale path after refactor" / learn.microsoft.com CS2001 / mode 3 | `dotnet test Tests/PerformanceProfiler.Tests.csproj` → build fails, 7× CS2001, 24/24 Compile Include paths confirmed stale (existence check). The baseline run IS the diagnostic. | F4 (high), F5 (medium) | High |
| Duplicate-using lint | reasoned omission (mechanical lint cleanup) | `grep -nE '^using ' \| sort \| uniq -d` over all `.cs` → 18 files, exact line pairs confirmed. | F6 (low) | High |
| Documentation rot | reasoned omission (doc-vs-code reading) | `grep -rn '_TempAllocBench'` (1 hit = the comment itself); `grep -rn 'AggressiveInlining'` (present, contradicts conventions.md #13); README/code/reality KB-per-hook triangulated. | F7 (low), F8 (low) | High |

## Data Layout / Memory Access Patterns applicability (per system)

| System | Decision |
|--------|----------|
| Hook-install RAM path | **Analysis performed** — this is the audit's primary Data-Layout finding (F1: per-hook retained Cecil object graphs, two method bodies per hooked method, retained for process lifetime). |
| Self-health measurement | Not applicable — measurement code, no hot loop / no layout-sensitive access pattern. Recorded. |
| Build / test infrastructure | Not applicable — csproj configuration, no runtime data layout. Recorded. |
| Duplicate-using lint | Not applicable — compiler directives. Recorded. |
| Documentation rot | Not applicable — notes/comments. Recorded. |

## Modularisation evaluation floor — per-candidate verdicts

C# threshold = 550 LOC; candidates = ≥550 LOC OR top decile (~top 30 of 298 compiled files).

| File | LOC | Verdict | Justification |
|------|-----|---------|---------------|
| `Profiling/HookInterceptor.cs` | 1227 | leave-as-is | Dormant **archived fallback** backend (delegate path; `HookBackend.Mode = ILHook` makes it inactive). The file is a self-contained backend behind a build-time switch — splitting an inactive fallback adds churn for no live-code benefit. If reactivated it would be a split candidate, but that is a future decision. |
| `Data/Streams/SessionRecorder.cs` | 726 | leave-as-is | Cohesive single responsibility (per-session recording: tick downsample + event drain + session end). The v0.6 perf pass already split the writer concern into `DbWriterThread`/`TickDownsampler`; what remains is one orchestrating recorder. Splitting further would create the constant cross-import coupling the §3 "What NOT to Flag" rule warns against. |
| `Profiling/ProfilerSystem.cs` | 699 | leave-as-is | The `ModSystem` lifecycle seam — by convention #10 this IS the single place per-world/per-tick lifecycle lives. Its length is the breadth of tModLoader lifecycle callbacks it must implement (PostSetupContent, OnWorldLoad/Unload, PreSaveAndQuit, PostUpdateEverything), each a distinct required override. Splitting would fragment the lifecycle contract across files. |
| `Profiling/MetricCollector.cs` | 586 | leave-as-is | Hot-path engine; comprehensive but internally cohesive (frame timing + baseline + GC/alloc reads + spike/stall feed). Already the subject of the v0.6 incremental-baseline refactor. Per §3 a hot-path engine that is internally well-structured despite size is not a split target. |
| `Profiling/ILHookInterceptor.cs` | 575 | leave-as-is | The IL manipulator is a single tightly-coupled algorithm (install walk + Cecil IL transform); the transform and the walk share the closed-generic/dedup invariants. Splitting the manipulator from the walk would scatter the JIT-shared-body safety logic that must stay together (see hook-instrumentation.md:208). Priority system — read in depth; not over-broad, just intrinsically detailed. |
| `Data/Detectors/StallDetector.cs` | 574 | leave-as-is | Single classifier (wall-vs-CPU gap + cause attribution); the classifier's branches are one cohesive decision tree. Has dedicated tests (once F4 is fixed). §3 pattern-matching/dispatch function that is long but each case is simple and independent. |
| `Data/Aggregators/Segments/SegmentDetector.cs` | 551 | leave-as-is | State machine (open/close segments across biome/weather/boss/death dimensions). §3 explicitly exempts "a comprehensive state machine that is internally well-structured despite size". The 2026-05-21 decision deliberately chose NOT to split it (`decisions.md:254`). |
| `Web/Assets/Js/Js.Lag.cs` (738), `Js.Insights.cs` (702), `Js.Timeline.cs` (698), `Css.Timeline.cs` (413) | — | not-applicable | **Generated-asset files**: JavaScript/CSS embedded as C# string literals, served by the dashboard. The length is the bundled web asset, not C# logic. §3 "generated table" exemption. Already the product of the v0.11 split from a monolithic bundle. |
| `Data/Contracts/RolloutContracts.cs` | 544 | leave-as-is | Frozen snapshot-contract types (the v0.12 contract-decoupling file). Deliberately one file so the contract surface is locked in one place (memory note: contract-decoupling-pattern). Splitting would defeat its single-source-of-truth purpose. |
| `Profiling/Persistence/ProfilerDatabase.cs` | 493 | leave-as-is | Already split in v0.3 (605→431, then grown to 493); owns only cross-cutting DB concerns (open/recovery/schema/journal/backup). §3 internally coherent. |
| `Profiling/Persistence/Interactions/InteractionPlayer.cs` | 493 | leave-as-is | One ModPlayer hook surface (OnHurt/OnHitNPC*/PostUpdateBuffs/PostUpdateEquips); each method is a distinct hook the class must own together (shared loadout-fingerprint state). |
| `Data/Aggregators/PerModAttribution.cs` | 413 | leave-as-is | Hot-path per-mod accumulator; IL-emit metadata references it by name (`decisions.md:254` notes renaming/restructuring would ripple through the detour IL stream). Internally cohesive. |
| `UI/Overlay/Tabs/OverviewTab.cs` (786), `OverlayPanel.cs` (679), `TreeTab.cs` (474), other `UI/**` | — | not-applicable | **Archived, `#if false`-guarded, not compiled** since v0.9.0; intentionally retained per README + 2026-05-21 decision (`decisions.md:271`). Splitting a non-compiled archive is meaningless. |

**Summary:** 0 split-recommended, 13 leave-as-is, generated/archived groups not-applicable. No
candidate received the forbidden "out of scope for this round's focus" verdict — every
candidate has a substantive per-file justification grounded in §3.

## Certain-set non-regression

No finding was demoted from certain to potential to dodge a proof obligation. P-1 is a
genuinely-uncertain structural option (needs MonoMod upstream cooperation or risky
reflection — the no-in-repo-test-possible case), not a downgraded certain finding. F1's
certain core (the retention root cause) stays in `hook-install-ram.md`; only the
not-free structural reclaim option is filed as potential.
