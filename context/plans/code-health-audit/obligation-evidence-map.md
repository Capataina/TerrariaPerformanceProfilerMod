# Obligation Evidence Map — Code Health Audit 2026-07-07 (post-rework focused sweep)

**Target:** PerformanceProfiler — tModLoader 1.4.4 / .NET 8 C# mod, v0.28.0.
**Supersedes** the 2026-06-25 map. That audit's 6-cluster findings files (`hook-instrumentation.md`,
`metric-collection.md`, `data-pipeline.md`, `insights.md`, `web-and-persistence.md`,
`web-ui-and-crosscutting.md`) are PRESERVED as a prior backlog and are not re-verified here.

**Focus (this run):** the hot-path code reworked this session (the A/B/C/D fixes across
`Profiling/`, `Data/Aggregators`, `Data/Detectors`, and the cross-session `Persistence/`+`Web/`
path). This is a targeted post-rework verification sweep: did the measurement-honesty + RAM +
correctness rework leave dead code, orphans, unused members, or per-tick allocations?

## Research-mode distribution (≥3 required)

| Mode | Meaning | Count |
|------|---------|-------|
| 1 | Domain pattern lookup | 1 |
| 2 | Specific-technique evaluation | 1 |
| 3 | Known-anti-pattern / tradeoff | 1 |

Three modes represented → variety requirement met (3 WebSearch calls this run, on the systems deep-dived).

## Language-coverage note (script fallback)

C# is outside `scripts/*.py` (Py/Rust) coverage. `file_size_scan.py` is language-agnostic →
**used** (Pass-1 candidate list). `modularisation_candidates.py` / `import_graph.py` /
`hotspot_intersect.py` / `orphans.py` → **fallback**: `file_size_scan.py` for sizes, `Grep` for
call-site / dead-code sweeps, compiler warnings (CS0169/0414/0219) for definitive unused-member
detection. `test_baseline.sh` (Cargo/pyproject/npm/go only) → **fallback**: `dotnet test` +
`dotnet msbuild`. `evidence_map_lint.py` + `finalize_audit.py` language-agnostic → run normally.

## Rows

| System | Research obligation | Diagnostic-test obligation | Findings emitted | Reasoned omissions |
|--------|--------------------|-----------------------------|------------------|--------------------|
| **Front-loaded (pre-Pass-1)** | Query: "subnormal denormal float flush-to-zero performance penalty hot loop C# .NET managed"; Source: <https://arxiv.org/pdf/1506.03997>; Mode 2 (technique eval). Validated the C2 denormal-flush fix (denormals cost 20+ cycles on x86; .NET exposes no hardware FTZ, so explicit detect-and-flush is the correct managed remedy). | n/a (front-load) | — | — |
| **1. Metric Collector hot path** (`MetricCollector.cs`, `ProbeStack.cs`, `PerModAttribution.cs`) | Query: "exponential moving average vs sliding window mean memory tradeoff streaming metrics"; Source: <https://nestedsoftware.com/2018/04/04/exponential-moving-average-on-streaming-data-4hhl.24876.html>; Mode 3 (known-anti-pattern check: the unbounded Θ(w) sliding-window memory is the anti-pattern B1 removed by switching to an O(1) EMA). Confirms B1's EMA-over-windowed choice is standard streaming practice. | No new test. Confidence HIGH from tooling: `dotnet msbuild` → 0 CS0169/0414/0219 unused-member warnings (definitive dead-code check); grep → every per-hook property (`PerHookMs`/`PerHookAverageMs`/bytes) still consumed by 7 files; `UpdateRollingAverage`/`historyCapacity` still used by the per-mod path; hot path has no per-tick heap alloc (only stack `Span`/`Vector` structs); existing 170-test suite green. Reasoned omission per detection-strategies §7 (confidence already high; tooling is definitive). | 3 (F1, F2, F3) → findings.md | None |
| **2. Hook install / RAM** (`ILHookInterceptor.cs`, `ProfilerSelfHealth.cs`) | Query: "tModLoader MonoMod IL hook per-method instrumentation performance overhead best practices"; Source: <https://github.com/tModLoader/tModLoader/wiki/Patching-Other-Mods-Using-MonoMod>; Mode 1 (domain pattern). Confirms two IL hooks on the same method risk incompatibility → SourceCloneIl retention IS required for re-chain safety (the B4 non-removal rationale). | No new unit test — the B4 reclaim diagnostic ships in production (`TrimRetainedScaffolding` logs actual MB freed); it is the measurement. Reasoned omission (measurement shipped as a runtime log). | 1 (F4) → findings.md | None |
| **3. Cross-session persistence** (`ProfilerSystem` session-start eval, `Web/DashboardRouter.History.cs`, `HistoryStore`) | No separate WebSearch — the C1 failure mode (LiteDB LINQ-to-BsonExpression choking on an indexer-in-predicate) was traced from the live crash stack, stronger than literature. Reasoned omission below. | No new test — both C1-class instances were runtime-confirmed by the live stacks naming the exact lines (`ProfilerSystem.cs:334`, `History.cs:112`); a repo-wide sweep of `.Find/.FindOne/.Delete` predicates found no third instance. Reasoned omission (runtime stack + exhaustive grep already high). | 1 (F5) → findings.md | Research: reasoned omission — root cause came from the live stack, not external research. |
| **All other systems** (~90 files: `UI/` archived + not compiled, `Web` renderers, `Insights` detectors, `Localization`, `tools/`) | Not performed | Not written | 0 | Outside the reworked-hot-path mandate; the 2026-06-25 audit covered these and the session deep-read them today. Modularisation candidates among them still get per-file verdicts (F6) per the evaluation floor. |
