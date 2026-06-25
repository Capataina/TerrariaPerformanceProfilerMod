# Obligation Evidence Map — Code Health Audit 2026-06-25

**Target:** PerformanceProfiler — tModLoader 1.4.4 / .NET 8 C# mod, v0.22.0.
**Supersedes** the 2026-06-22 map (that audit's hook-RAM + broken-build + dup-using findings have since landed; this is a fresh full sweep).

> Live ledger. One row per Pass-2 cluster + the front-loaded pre-Pass-1 search. The `index.md` "What I Did Not Do" section is the project-level summary; the two agree.

## Research-mode distribution (≥3 required)

| Mode | Meaning | Count |
|------|---------|-------|
| 1 | Domain pattern lookup | 5 |
| 2 | Specific-technique evaluation | 5 |
| 3 | Known-anti-pattern check | 6 |

All three modes well-represented → variety requirement met. (16 WebSearch calls across the front-load + 6 clusters.)

## Language-coverage note (script fallback)

C# is outside `scripts/*.py` (Py/Rust) coverage. `file_size_scan.py` is language-agnostic → **used** (Pass-1 candidate list). `modularisation_candidates.py` / `import_graph.py` / `hotspot_intersect.py` / `orphans.py` → **fallback**: `file_size_scan.py` for sizes, `Grep` for call-site / dead-code / duplicate-using sweeps, `git log` for churn. `test_baseline.sh` (Cargo/pyproject/npm/go only) → **fallback**: `dotnet test` + `dotnet msbuild`. `evidence_map_lint.py` + `finalize_audit.py` are language-agnostic → run normally. Recorded as the reasoned omission of the script-invocation path.

## Diagnostic-test posture (audit phase)

Most findings are **high-confidence from reading + exhaustive grep** (dead code with zero call sites, doc-rot verified against code, devirtualisation semantically-identical-by-construction, duplicate-using = zero). Two structural constraints make audit-phase test-writing largely a reasoned omission:
1. **Cluster 1 (hook instrumentation) files are all tModLoader-dependent** → not linkable into the pure-logic `Tests/` csproj without a production-source extraction (out of scope). Findings rest on grep + decompilation-grade reasoning.
2. **Equivalence-claim findings** (reciprocal-multiply, loop-fusion, Pearson clamp, `Without`-cancellation fix) are best pinned by a test written **alongside the fix** in the implementation phase, where the test exercises the changed code and passes — rather than a failing test in the audit phase. The implementation phase writes these verifying tests + re-runs the 108-test suite.

This is the floor's "confidence already high without the test" + "test lands with the fix" disposition, recorded per-row below — not a silent skip.

## Rows

| System | Research obligation | Diagnostic-test obligation | Findings | Reasoned omissions |
|--------|--------------------|-----------------------------|----------|--------------------|
| **Front-loaded (pre-Pass-1)** | Query: "code health audit patterns for C# .NET 8 game mod zero-allocation hot path performance"; Source: <https://medium.com/@anderson.buenogod/7-hidden-allocations-in-c-that-quietly-hurt-performance-fea3074cdd43>, <https://www.stevejgordon.co.uk/dotnet-performance-optimisations-dont-have-to-be-complex>; Mode 1 (domain pattern) | n/a (pre-pass) | — | — |
| **1. Hook instrumentation + self-health** (`HookInterceptor`, `ILHookInterceptor`, `ProbeStack`, `HookSurfaceCache`, `ProfilerSelfHealth`, `ModOwnerCache`) | Q: "Stopwatch.GetTimestamp overhead hot path" (Mode 1 domain pattern, MS Learn); "long switch on Type reference-equality dictionary dispatch" (Mode 1, gist); "C# write-only property never read IDE0052" (Mode 3 anti-pattern, MS Learn); "GC.GetAllocatedBytesForCurrentThread reliability per-thread" (Mode 2 specific technique, MS Learn) | All findings HIGH-confidence from grep + reading; cluster files are tModLoader-dependent → not unit-test-linkable without a production extraction (out of scope). Reasoned omission. | 14 → [hook-instrumentation.md](hook-instrumentation.md) | Tests not written: files tModLoader-dependent (not linkable); dead-code/doc-rot findings grep-confirmed (already high). |
| **2. Metric collection + detectors** (`MetricCollector`, `RingBuffer`, `StallDetector`, `SpikeDetector`, `PerModAttribution`, `PerTickAttributionRing`, `Baseline`) | Q: "median+MAD streaming outlier ring-buffer allocation-free" (M2, mdpi.com / aakinshin.net); "C# allocation anti-patterns per-frame hot loop foreach boxing" (M3, nede.dev / criteo / infoq) | Equivalence findings (reciprocal-multiply, fused-sum) verified by ULP reasoning (already high); the two pins land **with the fix** in implementation. `PerModAttribution`/`Baseline` ARE linked. | 12 → [metric-collection.md](metric-collection.md) | Equivalence tests deferred to land with their fixes (implementation phase). |
| **3. Data pipeline + segments** (`DataRegistry`, `RolloutContracts`, `SegmentDetector`, collectors, aggregators, stats) | Q: "C# zero-alloc hot path foreach List<T> struct enumerator boxing" (M3, andrewlock/nede.dev); "Pearson correlation incremental numerical stability sum-of-products vs Welford" (M2, johndcook/amytabb) | Devirtualisation findings identical-by-construction (already high); Pearson-clamp + SegmentPromoter pins land with fixes. `SegmentPromoter` not currently linked. | 21 → [data-pipeline.md](data-pipeline.md) | 3 disproved non-findings recorded in-file (WeatherSources.All / ModOwnerCache). Pins deferred to fixes. |
| **4. Insights engine** (`Insights/*` — engine, 16 detectors, reference frames, drivers, publish stats) | Q: "Welford parallel variance Chan subtract delete numerical stability catastrophic cancellation" (M2, Wikipedia / Schubert SSDBM18); "Bonferroni number-of-tests dependent multiple-comparison pitfalls" (M3, StatSig / arXiv); "Welch t normal-approximation small-sample inflated false-positive skewed" (M1, Springer 2024) | `RunningStat.Without` cancellation (High correctness) pin lands **with the fix** (asserts post-fix stable variance). `Insight`/`InsightStore`/`RankingScorer`/`Stats` ARE linked. | 13 → [insights.md](insights.md) | Without-cancellation test written with its fix (avoids a failing-test baseline); confirmed no-producer scaffold by grep (already high). |
| **5. Web server + persistence** (`DashboardHttpServer`, `DashboardRouter.*`, `SessionRecorder`, `ProfilerDatabase`, `DbWriterThread`, streams) | Q: "System.Text.Json anonymous-object per-request allocation reflection caching" (M3, MS Learn); "LiteDB single writer thread InsertBulk checkpoint WAL" (M2, LiteDB issues); "raw TcpListener Socket.Poll SelectRead Available zero closed" (M1, MS Learn / dotnet runtime) | Router/abort-clean/zero-alloc all verified-clean by reading (already high). Persistence round-trip + wire-shape golden tests flagged; the journal round-trip pin lands with any serialisation touch. `Tests/Persistence/` linked. | 9 → [web-and-persistence.md](web-and-persistence.md) | source-gen finding NOT free (anonymous types) → flagged not mandated. Tests deferred to fixes. |
| **6. Web UI assets + cross-cutting** (`Js.*`, `Css.*`, dead-code / dup-using / doc-rot / dependency sweeps) | Q: "JS innerHTML full DOM rebuild per poll reflow vs diffing dashboard" (M3, gomakethings / dev.to); "SVG chart string-concatenation innerHTML performance" (M1, oreilly / echarts) | JS has no xUnit surface (verbatim C# strings) → UI findings verified via the **L4 Playwright harness + preview render** post-implementation, not xUnit. Reasoned omission. | 19 → [web-ui-and-crosscutting.md](web-ui-and-crosscutting.md) | duplicate-using sweep = **0 files** (clean negative). UI verified via L4 not xUnit. |
