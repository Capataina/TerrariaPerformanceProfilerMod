# Pass 1 Checkpoint — 2026-07-07 (post-rework focused sweep)

Supersedes the 2026-06-25 Pass-1 checkpoint. Prior cluster findings files preserved as backlog.

## Project

PerformanceProfiler — tModLoader 1.4.4 / .NET 8 C# mod, v0.28.0. 358 source files. The
pure-logic surface is the `Tests/` xUnit project (170 tests).

## Test-suite baseline

- Command: `dotnet test Tests/PerformanceProfiler.Tests.csproj` → **170 passed / 0 failed / 0 skipped** (22 s).
- Compile gate: `dotnet msbuild PerformanceProfiler.csproj` → **0 error CS**, **0 unused-member warnings** (CS0169/0414/0219/0168/0649).
- No pre-existing failures → no Known-Issues findings.

## Systems (Pass-2 prioritisation)

Scoped to the code reworked this session (the A–E fixes):
1. **Metric Collector hot path** — `MetricCollector.cs` (680 L), `ProbeStack.cs`, `PerModAttribution.cs`.
2. **Hook install / RAM** — `ILHookInterceptor.cs` (722 L), `ProfilerSelfHealth.cs`.
3. **Cross-session persistence** — `ProfilerSystem` session-start eval, `Web/DashboardRouter.History.cs`, `HistoryStore`, `StoreReset`.

The 2026-06-25 audit covered every other system; not re-walked here (reasoned omission in the map).

## Modularisation candidate list (evaluation floor — C# threshold 550 L)

`python3 scripts/file_size_scan.py .` → 18 files at/over threshold. C# candidates + verdicts
(`context/arch/*.js` are generated architecture-viewer assets → not-applicable; `UI/` is
ARCHIVED and not compiled → not-applicable):

| File | Lines | Verdict | Justification |
|------|-------|---------|---------------|
| `Profiling/HookInterceptor.cs` | 1222 | leave-as-is | Cohesive delegate-backend hook-install engine; splitting fragments the install pipeline. |
| `Profiling/ProfilerSystem.cs` | 911 | leave-as-is | The `ModSystem` lifecycle hub; size is the lifecycle surface. Splitting scatters ordering that must stay legible. |
| `Persistence/Streams/SessionRecorder.cs` | 817 | leave-as-is | Cohesive recorder that already delegates to streams + downsampler. |
| `Web/Assets/Js/Js.Timeline.cs` | 800 | leave-as-is | One-file-per-dashboard-tab is the established pattern (verbatim-JS asset). |
| `UI/Overlay/Tabs/OverviewTab.cs` | 786 | not-applicable | `UI/` ARCHIVED, not compiled (README repository-layout). |
| `Profiling/ILHookInterceptor.cs` | 722 | leave-as-is | The IL backend, parallel to HookInterceptor; cohesive single backend. |
| `Web/Assets/Js/Js.Lag.cs` | 687 | leave-as-is | Per-tab renderer asset. |
| `Profiling/MetricCollector.cs` | 680 | leave-as-is | Per-tick collector core; +74 L this session (self-overhead + EMA + denormal flush) but one cohesive tick pipeline; splitting risks the zero-alloc discipline. |
| `UI/Overlay/OverlayPanel.cs` | 679 | not-applicable | Archived (not compiled). |
| `Web/Assets/Js/Js.Observatory.cs` | 658 | leave-as-is | Per-tab renderer asset. |
| `Data/Detectors/StallDetector.cs` | 608 | leave-as-is | Detector + already-pure-static classifier; cohesive. |
| `Persistence/ProfilerDatabase.cs` | 573 | leave-as-is | DB facade (collection accessors + lifecycle); cohesive. |
| `Data/Contracts/RolloutContracts.cs` | 560 | leave-as-is | Deliberate single frozen-contracts file (contract-decoupling pattern). |
| `Data/Aggregators/Segments/SegmentDetector.cs` | 555 | leave-as-is | Cohesive segment detector, marginally over threshold. |

**No `split-recommended`** — matches the 2026-06-25 finding: these files are genuinely cohesive
and the 550-L C# threshold is conservative for a mature mod. Splitting fragments cohesion.
