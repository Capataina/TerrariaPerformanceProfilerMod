# Obligation Evidence Map

**Audit date:** 2026-06-22 (hook-install RAM deep-dive + repository sweep)
**Target:** `PerformanceProfiler` — tModLoader 1.4.4 / .NET 8 mod (C#)

> Live verification ledger. One row per system audited in Pass 2, plus the
> front-loaded pre-Pass-1 research row. The "What I Did Not Do" section of
> `index.md` is the project-level summary of this per-system detail; the two
> must agree. This run supersedes the 2026-05-20/21 audit in this folder
> (plan-lifecycle regeneration against current code).

## Research-mode distribution

| Mode | Meaning | Count |
|------|---------|-------|
| 1 | Domain pattern lookup | 2 |
| 2 | Specific-technique evaluation | 2 |
| 3 | Known-anti-pattern check | 2 |

Three modes represented → variety requirement met.

## Language-coverage note (script fallback)

The bundled `scripts/*.py` cover Python and Rust. This project is **C#**, outside
that coverage. Per SKILL.md §"Language coverage", the fallback path was used and is
recorded here as a reasoned omission of the script-invocation path:

- **File-size scan / modularisation candidates:** `find … -name '*.cs' | xargs wc -l`
  + the C# 550-line threshold + top-decile rule, computed manually. Output pasted into
  `PASS-1-CHECKPOINT.md`.
- **Import graph / hotspot intersect:** `import_graph.py` / `hotspot_intersect.py` only
  parse Py/Rs imports → empty on C#. Substituted with `grep` over `using` directives +
  the per-commit file activity recorded in `context/notes/decisions.md` + the
  `context/perf-pass/baseline.md` hot-path inventory.
- **Test baseline:** `scripts/test_baseline.sh` detects Cargo/pyproject/package.json/go.mod,
  none present. Substituted with the real command `dotnet test
  Tests/PerformanceProfiler.Tests.csproj`.
- **Orphans:** `orphans.py` parses Py/Rs only. Substituted with manual `grep` call-site sweeps.
- **Evidence-map lint / finalize:** language-agnostic; run normally.

## Rows

| System | Research obligation | Diagnostic-test obligation | Findings emitted | Reasoned omissions |
|--------|--------------------|-----------------------------|------------------|--------------------|
| **Front-loaded (pre-Pass-1)** | Query: "code health audit patterns for C# .NET MonoMod IL hook instrumentation profiler memory"; Source: <https://learn.microsoft.com/en-us/dotnet/framework/unmanaged-api/profiling/profiling-overview>, <https://specterops.io/blog/2024/06/11/lateral-movement-with-the-net-profiler/>; Mode: 1 (domain pattern lookup) | n/a (front-load) | n/a | None |
| **Hook-install RAM path** (`ILHookInterceptor.cs`, `HookBackend.cs`, `ProfilerSystem.PostSetupContent`) | Query: "MonoMod RuntimeDetour ILHook DynamicMethodDefinition DMDCecilGenerator retains ModuleDefinition memory after apply"; Source: <https://github.com/MonoMod/MonoMod/blob/master/MonoMod.RuntimeDetour/ILHook.cs>, <https://github.com/MonoMod/MonoMod.Common/blob/master/Utils/DynamicMethodDefinition.cs>; Mode: 3 (known-anti-pattern check) | **Decompiled the shipped binaries — ground truth, stronger than any synthetic test.** `MonoMod.RuntimeDetour 25.3.2` `DetourManager` via `ilspycmd` → `/tmp/dm.cs`: `ManagedDetourState.SourceCloneIl` (a DMD) never disposed until `RemoveILHook` (fields L346; `UpdateEndOfChain` L643-663 re-clones it on every chain change; the per-method temp DMD `val` is disposed in `finally` L662 but `SourceCloneIl` is not); `ILHookEntry.LastContext` `MakeReadOnly()`'d not disposed (`InvokeManipulator` L666-681 line 679; `CleanILContexts` L683-705 disposes only the *superseded* context). `MonoMod.Utils 25.0.10` `DynamicMethodDefinition` → `/tmp/dmd.cs`: holds `ModuleDefinition Module` (L59) + `MethodDefinition Definition` (L57); `Dispose()` (L636) disposes the Module — but is never called on `SourceCloneIl` while the hook lives. Plus split-measurement diagnostic `Tests/HookInstallRetentionDiagnostics.cs`. | 4 → [hook-install-ram.md](hook-install-ram.md) (F1 retention, F2 measurement-gap, F3 KB/hook drift, plus P-1 in potential-issues) | None |
| **Self-health measurement** (`ProfilerSelfHealth.cs`) | Query: "GC.GetTotalMemory forceFullCollection false vs true measuring retained managed heap after transient allocation burst"; Source: <https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalmemory>; Mode: 2 (specific-technique evaluation) | `Tests/HookInstallRetentionDiagnostics.cs` — `GetTotalMemory(false)` after allocate-then-release burst over-reports vs `GetTotalMemory(true)`, demonstrating the conflation `MarkInstallEnd` suffers (samples with `forceFullCollection:false` and no preceding collection). Result captured in finding F2. | 1 → [hook-install-ram.md](hook-install-ram.md#f2) | None |
| **Build / test infrastructure** (`Tests/PerformanceProfiler.Tests.csproj`) | Query: "dotnet test CS2001 source file could not be found Compile Include stale path after refactor"; Source: <https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs2001>; Mode: 3 (known-anti-pattern check) | `dotnet test Tests/PerformanceProfiler.Tests.csproj` → **build fails, 7× CS2001 reported, all 24 `Compile Include` paths stale** (files moved `Profiling/`→`Data/`). The baseline run IS the diagnostic. Stale→true-path map computed via `find`. | 2 → [build-and-tests.md](build-and-tests.md) (F4 broken-build, F5 path-map) | None |
| **CS0105 duplicate-using sweep** (18 files: 14 `Data/Streams/`, 4 `Data/Aggregators/Segments/`) | Not performed — mechanical compiler-warning cleanup; the constant-table / lint tier per `detection-strategies.md` §"When Research Is Not Required". | `grep -nE '^using ' <file> \| sort \| uniq -d` over every `.cs` → 18 files with genuine in-file duplicate `using`; line numbers verified on 3 samples (SpikeStream L4+L9, OpenSegment L4+L7, SessionRecorder L7+L12). | 1 → [cross-cutting.md](cross-cutting.md#f6) | Research skipped: mechanical lint-class cleanup. |
| **Documentation rot sweep** (`conventions.md` #13, `HookBackend.cs`, README KB/hook) | Not performed — verifying a comment against code is direct reading, not a research question (§"When Research Is Not Required"). | `grep -rn '_TempAllocBench'` → only a doc-comment reference; no such symbol. `grep -rn 'AggressiveInlining'` → present in ProbeStack/PerModAttribution, contradicting conventions.md #13. README says ~36 KB/hook; code `BaselineBytesPerHook = 36 KB`; live investigation reports ~60 KB/hook. | 2 → [cross-cutting.md](cross-cutting.md#f7) (F7 stale conventions #13 + README drift, F8 phantom benchmark ref) | Research skipped: doc-vs-code verification is reading. |
| **Modularisation candidates** (12 compiled files ≥550 LOC + top-decile) | Not a per-system research target — handled by the Modularisation evaluation floor, verdicts in `PASS-2-SYSTEMS-AUDITED.md`. | n/a (structural verdicts, not tests) | Verdicts: see `PASS-2-SYSTEMS-AUDITED.md` (1 split-recommended, rest leave-as-is / not-applicable) | UI/ files not-applicable (archived `#if false`). |
