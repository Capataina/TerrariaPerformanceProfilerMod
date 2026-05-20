# Test Harness — Optimisation Research & Plan

> Companion to `baseline.md`. Scope: the xUnit test project and its
> benchmark surface. Outcome target: every other research doc in
> `research/*.md` can verify its claims here, and every commit in the v0.6
> optimisation pass can prove (numerically) that it shipped a gain and did
> not silently regress a different surface.
>
> This is a **system** research doc, not an in-place patch. It enumerates
> the current state, the baseline numbers we already have, the gap between
> "we measure four things" and "we measure the full hot path", the design
> for the expanded benchmark surface, the design for regression-detection
> gates, and the prioritised order in which the benchmarks must land so
> that the rest of the pass can lean on them.
>
> Every recommendation is additive. The Test Harness expands; it never
> shrinks. Every existing test stays. Invariants 1–5 from `CLAUDE.md`
> apply: the harness measures, it does not change game behaviour; the
> hot-path benches must themselves be zero-alloc; output stays
> descriptive; failed-bench cases abort clean; no mod-specific code
> anywhere (every microbench operates on a generic surface — the same
> production type the game-thread runs against).

---

## 0. Table of contents

1. [Current state audit](#1-current-state-audit-every-test--fixture-walked) — every test + fixture walked.
2. [Baseline](#2-baseline-the-current-4-benchmark-numbers) — the current four benchmark numbers, run cost, and what they prove.
3. [BenchmarkDotNet vs roll-your-own](#3-benchmarkdotnet-vs-roll-your-own-deep-dive) — the deep-dive.
4. [The expanded benchmark surface](#4-the-expanded-benchmark-surface) — every microbench and stress test to add.
5. [Regression-detection design](#5-regression-detection-design) — minimum thresholds, output format, wrap-up wiring.
6. [Allocation-aware tests](#6-allocation-aware-tests) — verifying the zero-alloc claim at runtime, not just at review.
7. [Cross-system dependencies](#7-cross-system-dependencies) — where every other research doc writes its benches.
8. [Prioritised execution order](#8-prioritised-execution-order) — the order the suite is grown in.
9. [References](#9-references) — sources and links cited.

---

## 1. Current state audit (every test + fixture walked)

### 1.1 Project shape

The harness lives in `Tests/` inside the mod source folder, not as a
sibling repo, deliberately so `git mv` stays clean and the README's
"everything in one ModSources folder" mental model holds. It is a single
SDK-style `.csproj` (`Tests/PerformanceProfiler.Tests.csproj`) that:

| Property | Value | Why |
|---|---|---|
| `TargetFramework` | `net8.0` | Matches the mod's tML 1.4.4 runtime. Never 9 or 10. |
| `LangVersion` | `latest` | Tests can use any C# the SDK supports; production code is constrained by tML, tests are not. |
| `Nullable` | `enable` | Same posture as the mod. |
| `IsPackable` | `false` | Not a NuGet artefact. |
| `RootNamespace` | `PerformanceProfiler.Tests` | Distinct from the mod's `PerformanceProfiler.*` so test types never collide with production types. |
| `EnableDefaultCompileItems` | `false` | **Load-bearing.** The default SDK glob would pick up every `.cs` under the parent (the mod sources). Off, the test csproj curates its own list — see §1.2. |

Package references:

```xml
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
<PackageReference Include="xunit"                  Version="2.9.0" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
```

Plus one assembly reference:

```xml
<Reference Include="LiteDB">
  <HintPath>..\lib\LiteDB.dll</HintPath>
</Reference>
```

This is **the exact LiteDB the `.tmod` ships with**. Critical: a NuGet
LiteDB would diverge in version, surface-area, and BSON encoding from
the bundled one; the persistence tests would then pin a behaviour the
production code never executes. Using `<Reference HintPath>` to
`lib/LiteDB.dll` keeps the test runner exercising the *production*
DLL byte-for-byte. Any optimisation that touches the LiteDB surface
(replace with a fork, swap the storage layer, etc.) must update both
slots in lockstep.

### 1.2 The `Compile Include + Link` mechanism

This is the core invention of the harness. The mod project itself depends
on tModLoader assemblies (`tModLoader.dll`, `FNA.dll`, the Terraria
content surface). A `ProjectReference` from the test project to the mod
project transitively drags those in; the test runner cannot then find
the OpenGL backend on the dev box and the run crashes before xUnit
discovers a single fixture. That was the first attempt and it failed —
see `systems/test-harness.md`'s Discarded Approaches.

The workaround that survived:

```xml
<Compile Include="..\Profiling\RingBuffer.cs" Link="Source\RingBuffer.cs" />
<Compile Include="..\Profiling\PerModSample.cs" Link="Source\PerModSample.cs" />
<Compile Include="..\Profiling\PerModAttribution.cs" Link="Source\PerModAttribution.cs" />
<Compile Include="..\Profiling\TickFrame.cs" Link="Source\TickFrame.cs" />
<Compile Include="..\Profiling\Baseline.cs" Link="Source\Baseline.cs" />
<Compile Include="..\Profiling\StallDetector.cs" Link="Source\StallDetector.cs" />
<Compile Include="..\Profiling\Events\*.cs"   Link="Source\Events\%(Filename)%(Extension)" />
<Compile Include="..\Profiling\Insights\*.cs" Link="Source\Insights\%(Filename)%(Extension)" />
<Compile Include="..\Profiling\Persistence\*.cs"           Link="Source\Persistence\%(Filename)%(Extension)" />
<Compile Include="..\Profiling\Persistence\Records\*.cs"   Link="Source\Persistence\Records\%(Filename)%(Extension)" />
<Compile Include="..\Profiling\Persistence\Streams\*.cs"   Link="Source\Persistence\Streams\%(Filename)%(Extension)" />
```

`Compile Include` with a relative path **and** `Link="Source\..."` lifts
the pure-logic source into the test compilation unit *without copying*.
The IDE's solution explorer shows `Source/RingBuffer.cs` as a virtual
node; the file on disk remains `Profiling/RingBuffer.cs`. The test
binary and the mod binary contain **two compiled copies** of the same
source; that is fine because each assembly is independent.

A second `<Compile Remove>` block then excludes the files that *do* take
a tModLoader dependency:

```xml
<Compile Remove="..\Profiling\Persistence\ProfilerPaths.cs" />            <!-- reads Main.SavePath -->
<Compile Remove="..\Profiling\Persistence\LegacyJsonImporter.cs" />        <!-- game-runtime calls -->
<Compile Remove="..\Profiling\Persistence\ProfilerCompactCommand.cs" />    <!-- ModCommand -->
<Compile Remove="..\Profiling\Persistence\ModlistFingerprint.cs" />        <!-- HookInterceptor -->
<Compile Remove="..\Profiling\Persistence\SessionRecorder.cs" />           <!-- pulls MetricCollector -->
<Compile Remove="..\Profiling\Persistence\TickDownsampler.cs" />
<Compile Remove="..\Profiling\Persistence\ContextTransitionWatcher.cs" />
<Compile Remove="..\Profiling\Persistence\WorldSnapshotter.cs" />
<Compile Remove="..\Profiling\Persistence\PlayerDeathDetector.cs" />
<Compile Remove="..\Profiling\Persistence\SessionSummaryLogger.cs" />
<Compile Remove="..\Profiling\Persistence\Commands\*.cs" />
<Compile Remove="..\Profiling\Persistence\Interactions\*.cs" />
<Compile Remove="..\Profiling\ProfilerFocusProbe.cs" />
```

The invariant is: **every file under `Profiling\` is either Terraria-free
(linked in) or Terraria-dependent (explicitly removed).** The boundary
is the `using Terraria*` or `using Terraria.ModLoader*` directive in
the source; today there is no automated lint to enforce it. A future
addition that quietly adds `using Terraria.ModLoader;` to a linked file
would surface as a build failure on the test project, which is the
weakest possible failure mode (loud and at compile time), so the lack
of lint is tolerable. It would still benefit from a one-line check:

```bash
grep -l 'using Terraria' Profiling/{RingBuffer,Baseline,StallDetector,PerModAttribution,...}.cs
# expected: no output
```

A `Tests/lint-no-terraria-refs.sh` script that walks the linked file
list and `grep`s each one is on the proposed-additions list (§4.13).

### 1.3 Main mod project exclusion

`PerformanceProfiler.csproj` (the shipped mod project) carries:

```xml
<Compile Remove="Tests\**\*.cs" />
```

Without this, the main mod build would pick up `*Tests.cs` files (xUnit
attributes, `[Fact]`, `Assert.*`), unresolved references break the
`.tmod` build, and the Workshop artefact never produces. The line is
load-bearing for the build itself, not just for clean packaging.

### 1.4 `.tmod` package exclusion

`build.txt` carries `buildIgnore = Tests/*`. The `.tmod` packager (which
zips the post-build assets) skips the `Tests/` folder, so the shipped
Workshop artefact has zero test bytes. Even if `<Compile Remove>` were
not in place, this would prevent the test source from appearing in the
shipped artefact. The two together are belt-and-braces.

### 1.5 Current fixtures

The audit-pinned fixtures, walked in full:

| File | Tests | Surface | Run cost (cold) | Run cost (warm) |
|---|---|---|---|---|
| `RankingScorerTests.cs` | 3 | `RankingScorer.Score` | ≈1 ms | ≈0 ms |
| `InsightStoreTests.cs` | 3 | `InsightStore.Submit` + promotion | ≈2 ms | ≈1 ms |
| `RingBufferTests.cs` | 4 | wrap-around semantics | ≈1 ms | ≈0 ms |
| `BaselineTests.cs` | 8 | `Baseline.Recompute` median + EMA | ≈3 ms | ≈2 ms |
| `StallClassifierTests.cs` | 9 | `StallDetector.ClassifyCause/Severity` | ≈2 ms | ≈1 ms |
| `StallDetectorTests.cs` | 8 + theory | classifier truth-table + severity ladder | ≈3 ms | ≈2 ms |
| `Persistence/PersistenceRoundTripTests.cs` | 7 | `ProfilerDatabase` end-to-end | 1.2 s | 0.9 s |
| `Persistence/PersistenceBenchmarkTests.cs` | 4 | enqueue / drain / size / read | 22 s | 16 s |

Plus the in-flight `LiteDB` reference for the persistence tests.

The numbers above are total wall time per `dotnet test`. The benchmark
fixture dominates everything; `Steady_State_Drain_Throughput` alone is
the single longest test (~16 s warm) because it waits for the writer
thread to flush 5,000 ops to disk and then re-reads the row count. The
rest of the suite finishes inside two seconds even cold.

### 1.6 What the existing fixtures actually pin (per-test breakdown)

**`RankingScorerTests`** — three tests:

- `SharePattern_FractionalShare_RanksAboveSmallerShare` — 90% share
  outranks 40% share. The audit's #1 insights-engine finding.
- `RatioPattern_TenXRatio_ScoresHigherThanTwoXRatio` — 10× still beats 2×.
- `RatioPattern_BelowOne_ScoresZeroOnMagnitude` — sub-baseline ratios
  collapse the magnitude component to zero (the soft knee).

**`InsightStoreTests`** — three tests:

- `Repeated_UntestedRecord_NeverPromotesPastLow` — `PValueAdjusted = 1d`
  cannot promote past Low regardless of confirmation count. The honesty
  contract (Invariant 3) in test form.
- `Repeated_TestedRecord_PromotesAtThreshold` — 4 confirmations + `p ≤ 0.05`
  → High.
- `Submit_DedupesOnPatternAndSubject` — three identical submissions yield
  one live record.

**`RingBufferTests`** — four tests covering empty, below-capacity, wrap,
and `Newest`.

**`BaselineTests`** — eight tests that pin the calibration gate
(`MinCalibrationTicks`), median tracking at high/low refresh rates,
robustness against spike contamination, alloc-tracking on/off, and the
EMA convergence of the per-tick alloc median. The most data-rich
fixture by test count.

**`StallClassifierTests` + `StallDetectorTests`** — joint coverage of
`StallDetector.ClassifyCause` (truth-table per signal combination) and
`StallDetector.ClassifySeverity` (relative-to-baseline ladder). The
v0.4 → v0.5 misdiagnosis case (`MainThreadFreeze` vs `ProcessSuspended`,
distinguished by `focusHeldAcrossGap`) is locked in by two explicit
fixtures.

**`Persistence/PersistenceRoundTripTests`** — seven tests:

| Test | Pins |
|---|---|
| `Open_FreshDirectory_CreatesEmptyDb` | DB file is created on construction; `Sessions` count is 0. |
| `SessionStart_Then_Reopen_RowSurvives` | Writer drains on dispose, row visible on reopen. |
| `SessionEnd_AfterStart_MarksDoneAndCarriesDuration` | `Incomplete=false`, duration + ticks land. |
| `WarmAggregate_DuplicateSecondIndex_IsIdempotent` | Same `(sessionId, secondIndex)` is overwrite, not duplicate-insert. |
| `CrashDetected_Marking_FiresOnReopen` | Orphan `Incomplete=true` row → `EndReason = "crash-detected"`. |
| `Backups_RotateOnDispose_KeepsLastThree` | Five opens → only three backup files remain. |
| `Spike_AndStall_RowsLand` | Spike + stall rows survive a round-trip. |

**`Persistence/PersistenceBenchmarkTests`** — four observability fixtures
covered in detail in §2.

### 1.7 What the harness does NOT cover today

Visualised as the inverse of §1.5:

```
Production surface                           Test fixture today
──────────────────────────────────────────  ─────────────────────────
MetricCollector.Tick (per-tick hot path)    none (integration only)
ProbeStack.Enter / Leave                    none
PerModAttribution per-tick accumulate       none
PerTickAttributionRing.Push                 none (covered transitively
                                             by RingBufferTests)
SpikeDetector.OnEndTick                     none (only via classifier
                                             stubs)
StallDetector.OnBeginTick                   none (only classifier stub)
ContextTransitionWatcher.OnTick             none (Terraria-bound,
                                             excluded by Compile Remove)
TickDownsampler.Aggregate                   none (Terraria-bound)
SessionRecorder.End (the 8.5s stall)        none
DbWriterThread loop end-to-end              partial (drain throughput
                                             only, not per-op cost)
EventJournal.Append                         none
InsightsEngine 1-Hz tick                    none
ILHookInterceptor.Install (per-hook cost)   none
GC.GetAllocatedBytesForCurrentThread cost   none (the v0.6 alloc plan
                                             rests on this number)
```

The right column is the entire surface the v0.6 pass needs to optimise.
Every empty entry is a benchmark the harness must grow before another
research doc can prove its claim.

---

## 2. Baseline (the current 4 benchmark numbers)

Repeated from `baseline.md` so this doc stands alone:

| Benchmark | v0.5 reading | v0.3 prior | Delta | Bench cost |
|---|---|---|---|---|
| **Enqueue_GameThread_Latency** — 10,000 `Enqueue` ops on the warm writer queue, no disk in the path. | **441.2 ns/op** | 276 ns/op | **+60% regression since v0.3** | ≈ 4 ms |
| **Steady_State_Drain_Throughput** — 5,000 warm + ~50 spike enqueues + wait for full drain, throughput from row counts at the end. | **314 ops/sec** | 310 ops/sec | flat | ≈ 16 s |
| **Read_LastNSessions_Latency** — 50 sessions, `OrderByDescending(StartedUtc).Limit(10).ToList()`. | **0.426 ms** | 0.39 ms | +9% | ≈ 1 s |
| **Simulated_TenMinute_Session_FileSize** — 600 warm + 10 cold + 5 spikes + 100 per-mod aggregates + 1 archive aggregate + clean shutdown. | **1064 KB** | 752 KB | +41% (six new event streams) | ≈ 1 s |

These four are observability tests — they print measurements via
`ITestOutputHelper`, and the assertion floors are deliberately loose
(`< 100 µs/op`, `> 60 ops/sec`, `< 10 MB`). They do *not* fail CI on
regression today. That is the gap §5 fills.

The current benchmark surface only covers the persistence layer. The
hot-path-budget claim in Invariant 2 (Lite < 1%, Standard 2–4%, Deep
5–10%) and the playtest readings in `baseline.md` (PerformanceProfiler
itself as top CPU contributor at 0.27 ms/tick avg) are unverified by the
harness. That is the gap §4 fills.

### 2.1 Why the four are not enough

Each individual gap is small. The cumulative gap is the entire claim:

| Claim | Currently provable from the suite? |
|---|---|
| "MetricCollector.Tick is < 50 µs in Lite mode." | No. |
| "ProbeStack.Enter is zero-alloc." | No. |
| "Each event stream's Apply is < 30 µs." | No. |
| "End-of-session aggregation is < 50 ms." | No (the 8.5 s playtest stall is the only signal). |
| "Hook install allocates < 40 KB/hook." | No (only playtest delta). |
| "The full pipeline at Calamity-scale stays under the Standard budget." | No. |
| "An optimisation moved enqueue from 441 → 200 ns/op." | Yes, partially — single ad-hoc run, no CI gate. |

The four bench tests are necessary but nowhere near sufficient. They
cover one subsystem (persistence) at one level (writer-thread queue +
LiteDB read). The pass needs an order-of-magnitude more.

---

## 3. BenchmarkDotNet vs roll-your-own deep-dive

### 3.1 The two paths

**Path A — Adopt BenchmarkDotNet (BDN).** Add a `<PackageReference Include="BenchmarkDotNet" />` (current stable: 0.13.12 at the time of writing), mark microbench methods `[Benchmark]`, run them either as a separate executable (`dotnet run -c Release --project Tests`) or invoked from inside an xUnit fixture's constructor (the "Run BenchmarkDotNet in xUnit" pattern documented at tech-fellow.eu, dev.to, code-maze.com). BDN handles warmup, iteration count, statistical convergence, memory diagnosis (`[MemoryDiagnoser]`), regression detection (`StatisticalTestColumn` with `--statisticalTest 3ms` or `5%`), and produces CSV/JSON/Markdown output suitable for diffing.

**Path B — Roll our own.** Keep the existing `Stopwatch` + `ITestOutputHelper` + `_output.WriteLine` shape, extend it across the surface in §4, define our own warmup/iteration constants in a shared helper class, write our own statistical aggregation, and gate regression at our own thresholds inside the `Assert.True(...)` line.

### 3.2 Comparison matrix

| Dimension | BenchmarkDotNet | Roll-your-own |
|---|---|---|
| **Statistical rigor** | Production-grade: warmup, pilot runs, multimodal detection, outlier removal, confidence intervals, Mann–Whitney U test for regressions. | Best-effort: a fixed warmup count and a single mean read off `Stopwatch.Elapsed`. Multimodality and outliers are silent. |
| **Memory diagnoser** | `[MemoryDiagnoser]` claims 99.5% accurate per-op allocations, normalised per-1000 ops. Separate run when any diagnoser is on. | Hand-rolled `GC.GetAllocatedBytesForCurrentThread()` delta. Accurate but no per-Gen tracking unless we add it. |
| **Run cost** | Slow. A single `[Benchmark]` method typically 1–5 s; a 20-method suite at the default `MediumRun` is 1–5 minutes. With `ShortRun` (3 warmup, 3 measurement iters), 10–30 s. | Cheap. The four existing fixtures cost ~22 s total; a 20-bench suite at the same shape would be ~60 s. |
| **xUnit integration** | Possible, but officially a separate executable. Running BDN from inside an `[Fact]` works (Code Maze pattern) but: BDN spawns a child process per benchmark, the child must reference the same assembly, and the child can't print to `ITestOutputHelper`. We get back a `Summary` object and assert on it. Awkward. | Native. Every benchmark is an `[Fact]`. `_output.WriteLine` prints under `dotnet test -v n`. |
| **macOS Apple Silicon** | Works. BDN reads `Stopwatch.GetTimestamp` like everything else; the 41.67 ns Apple Silicon tick resolution dominates anyway. | Same constraint; the floor is set by `Stopwatch`, not by the harness. |
| **Regression detection** | Built-in. `StatisticalTestColumn` + `--statisticalTest 3ms` fails the run if the test detects a statistically-significant slowdown. Baseline JSON committed to repo. | We have to design and write this. §5 details. |
| **CI integration** | Excellent. `dotnet run -c Release` → JSON output → `BenchmarkDotNet.Artifacts/results/*.json` diff against a committed baseline file. There is a "Continuous Benchmark" GitHub Action that consumes BDN JSON. | We have to design and write this too. |
| **Required build mode** | `Release`. BDN refuses to run on Debug builds (warns and produces nothing useful). | Works on Debug; the existing 441 ns/op figure is from a Debug build. **This is actively misleading** — see §3.4. |
| **Per-microbench code shape** | `[Benchmark] public void X()` — one method per measurement, no `Stopwatch` boilerplate. | `[Fact] public void X() { sw.Start(); for (int i=0; i<N; i++) ...; sw.Stop(); _output.WriteLine(...); Assert.True(...); }` — boilerplate per fixture. |
| **Dependency surface** | One package: `BenchmarkDotNet` (transitively pulls `Iced`, `Microsoft.CodeAnalysis.CSharp`, `Microsoft.Diagnostics.Runtime`, `Perfolizer`). ~30 MB of test-time deps. | Zero. |
| **CSV / JSON export** | Built-in. Markdown, CSV, JSON, HTML, plot.png. | Hand-rolled `_output.WriteLine` → ad-hoc parse. |
| **Cost of being wrong** | A misconfigured BDN run still warns loudly. | A misconfigured ad-hoc bench silently reports a wrong number. We have already shipped a Debug-build bench number into `baseline.md`. |

### 3.3 Verdict: hybrid, weighted toward BDN

The verdict is **not "BDN everywhere".** It is:

> Add BenchmarkDotNet as a separate, second project under `Tests/Benchmarks/`
> with its own `.csproj`, in `Release` mode, gated behind a `dotnet run`
> invocation rather than `dotnet test`. Keep the xUnit `_output.WriteLine`
> microbenches alongside as the **CI smoke layer** — they catch order-
> of-magnitude regressions on every `dotnet test`. The BDN suite is the
> **statistical-rigor layer** — run by the developer on the wrap-up phase
> of each commit and in CI on push.

The reasoning:

1. **The Debug-vs-Release problem.** The existing 441 ns/op number is
   from `dotnet test` in Debug. Release-mode `Enqueue_GameThread_Latency`
   would likely read 150–250 ns/op (Debug doubles or triples JITted
   integer/struct code, especially anything that touches `in` parameters
   and span access). Half the v0.6 pass's claims rest on accurate
   numbers; we need Release reads.
2. **BDN refuses to run on Debug.** That is a feature. It locks in
   correct build configuration as a precondition. The ad-hoc bench has
   no such gate; the developer can `dotnet test` in Debug and ship a
   misleading number into the dossier (which is exactly what happened).
3. **The xUnit benches are still cheap.** Keeping them means every
   `dotnet test` (cold or warm) still gives a coarse pass/fail signal.
   They are the "your build broke something obvious" layer.
4. **BDN's `[MemoryDiagnoser]` is the only credible per-op alloc
   measurement we get.** Hand-rolling `GC.GetAllocatedBytesForCurrentThread`
   inside an xUnit fixture is fine for "is this zero or not?" but the
   per-Gen counts and per-op normalisation BDN does are tedious to
   reproduce. We use BDN here.
5. **Statistical tests for regression.** BDN's Mann–Whitney U via
   `StatisticalTestColumn` (`--statisticalTest 3%`) is the right gate.
   Hand-rolling it is reinventing a wheel that's been done since 2017
   and is well-validated.
6. **Subprocess isolation.** BDN runs each benchmark in a child process.
   That isolates the JIT state, the GC state, and the AppDomain. For
   the alloc microbenches especially, this matters: a previous fixture
   that allocates 200 MB of test data would still show in the alloc
   delta if the runs were in the same process. BDN's subprocess model
   solves it.

### 3.4 The Debug-build trap

The single most important early action in this section is **rerun the
existing four benches in Release mode and update `baseline.md`** before
any optimisation work begins. The 441 ns/op enqueue figure is what every
"target < 200 ns/op" claim is measured against; if that figure is 60%
inflated by Debug, the entire target shifts.

Concrete:

```bash
dotnet test Tests/PerformanceProfiler.Tests.csproj -c Release \
  --filter "FullyQualifiedName~PersistenceBenchmarkTests" -v n
```

The `_output.WriteLine` lines will surface in stdout. Capture the four
numbers, append them as a "v0.5-release" column to `baseline.md`, and
treat that column as the contract for the pass. The Debug numbers stay
in the file for historical comparison only; they are not the contract.

This is also a free win: it takes one minute and clarifies every
downstream number.

### 3.5 The xUnit-hosted BDN pattern (rejected for this pass)

Some projects run BDN inside an `[Fact]`:

```csharp
[Fact]
public void BdnSmoke()
{
    var summary = BenchmarkRunner.Run<EnqueueBenchmarks>();
    Assert.All(summary.Reports, r => Assert.True(r.Success));
}
```

The pattern works but has three problems for our case:

1. BDN expects a `Release` config; running it from `dotnet test` (which
   defaults to `Debug`) emits a warning and produces nothing useful.
2. The child-process model means BDN spawns a child of the test runner;
   the child needs to find the same assembly. It works in monolithic
   solutions; with our `Compile Include + Link` shape it would be
   load-bearing whether the linked Profiling sources also exist in the
   child's reference set, and they do — but the failure mode is opaque.
3. `ITestOutputHelper` is not the output destination for BDN; the
   developer reads `BenchmarkDotNet.Artifacts/` instead. Hosting BDN
   inside xUnit gives no integration benefit; it just couples two
   harnesses pointlessly.

The hybrid in §3.3 separates them cleanly. The xUnit layer keeps its
`_output.WriteLine` shape and a coarse `Assert.True` floor; the BDN
layer is its own project with its own command-line entry point.

### 3.6 BenchmarkDotNet project shape (proposed)

```
Tests/
  PerformanceProfiler.Tests.csproj    ← xUnit, unchanged
  ...existing files...
  Benchmarks/
    PerformanceProfiler.Benchmarks.csproj   ← new, OutputType=Exe
    Program.cs                              ← BenchmarkRunner.Run<...>
    MetricCollectorBench.cs
    ProbeStackBench.cs
    AllocCounterBench.cs
    StreamApplyBench.cs
    EventJournalBench.cs
    InsightsEngineBench.cs
    SessionAggregationBench.cs
    StressPipelineBench.cs
    BenchConfig.cs                          ← shared ManualConfig
```

`PerformanceProfiler.Benchmarks.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>PerformanceProfiler.Benchmarks</RootNamespace>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <Optimize>true</Optimize>
    <ServerGarbageCollection>true</ServerGarbageCollection>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.13.12" />
    <Reference Include="LiteDB">
      <HintPath>..\..\lib\LiteDB.dll</HintPath>
    </Reference>
  </ItemGroup>
  <ItemGroup>
    <Compile Include="**\*.cs" Exclude="bin\**;obj\**" />
    <!-- Same Compile Include + Link pattern as the xUnit project. -->
    <Compile Include="..\..\Profiling\RingBuffer.cs"  Link="Source\RingBuffer.cs" />
    <Compile Include="..\..\Profiling\Baseline.cs"    Link="Source\Baseline.cs" />
    <Compile Include="..\..\Profiling\PerModSample.cs" Link="Source\PerModSample.cs" />
    <Compile Include="..\..\Profiling\PerModAttribution.cs" Link="Source\PerModAttribution.cs" />
    <Compile Include="..\..\Profiling\TickFrame.cs"   Link="Source\TickFrame.cs" />
    <Compile Include="..\..\Profiling\StallDetector.cs" Link="Source\StallDetector.cs" />
    <Compile Include="..\..\Profiling\Events\*.cs"    Link="Source\Events\%(Filename)%(Extension)" />
    <Compile Include="..\..\Profiling\Insights\*.cs"  Link="Source\Insights\%(Filename)%(Extension)" />
    <Compile Include="..\..\Profiling\Persistence\*.cs"         Link="Source\Persistence\%(Filename)%(Extension)" />
    <Compile Include="..\..\Profiling\Persistence\Records\*.cs" Link="Source\Persistence\Records\%(Filename)%(Extension)" />
    <Compile Include="..\..\Profiling\Persistence\Streams\*.cs" Link="Source\Persistence\Streams\%(Filename)%(Extension)" />
    <Compile Remove="..\..\Profiling\Persistence\ProfilerPaths.cs" />
    <Compile Remove="..\..\Profiling\Persistence\LegacyJsonImporter.cs" />
    <Compile Remove="..\..\Profiling\Persistence\ProfilerCompactCommand.cs" />
    <Compile Remove="..\..\Profiling\Persistence\ModlistFingerprint.cs" />
    <Compile Remove="..\..\Profiling\Persistence\SessionRecorder.cs" />
    <Compile Remove="..\..\Profiling\Persistence\TickDownsampler.cs" />
    <Compile Remove="..\..\Profiling\Persistence\ContextTransitionWatcher.cs" />
    <Compile Remove="..\..\Profiling\Persistence\WorldSnapshotter.cs" />
    <Compile Remove="..\..\Profiling\Persistence\PlayerDeathDetector.cs" />
    <Compile Remove="..\..\Profiling\Persistence\SessionSummaryLogger.cs" />
    <Compile Remove="..\..\Profiling\Persistence\Commands\*.cs" />
    <Compile Remove="..\..\Profiling\Persistence\Interactions\*.cs" />
    <Compile Remove="..\..\Profiling\ProfilerFocusProbe.cs" />
  </ItemGroup>
</Project>
```

The Benchmarks project mirrors the Tests project's Compile-Include-Link
discipline exactly. Both projects pin the same compile graph; only the
runtime layer differs.

`Benchmarks/Program.cs`:

```csharp
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;

namespace PerformanceProfiler.Benchmarks;

public static class Program
{
    public static int Main(string[] args)
        => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly)
                            .Run(args, BenchConfig.Default)
                            .Length;
}
```

`BenchConfig.cs`:

```csharp
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;

namespace PerformanceProfiler.Benchmarks;

public sealed class BenchConfig : ManualConfig
{
    public static readonly BenchConfig Default = new();

    public BenchConfig()
    {
        AddJob(Job.Default
            .WithWarmupCount(3)
            .WithIterationCount(10)
            .WithInvocationCount(2048));
        AddDiagnoser(MemoryDiagnoser.Default);
    }
}
```

Invocation:

```bash
# Run the full suite:
dotnet run -c Release --project Tests/Benchmarks -- --filter "*"
# Run a single class:
dotnet run -c Release --project Tests/Benchmarks -- --filter "*MetricCollectorBench*"
# Compare against a saved baseline JSON:
dotnet run -c Release --project Tests/Benchmarks -- --filter "*" \
    --statisticalTest 3%  --exporters json
```

`buildIgnore` in `build.txt` extends to `Tests/*` already, so the new
sub-folder is excluded from the `.tmod` automatically.

### 3.7 Drawback ledger

What does adopting BDN cost us?

- **First-run latency.** Cold cargo of BDN packages ~25 s. Warm builds <5 s.
- **CI minutes.** A 20-bench BDN run at MediumRun job is 5–10 minutes.
  Acceptable for push-to-main; not for every PR. Solution: a `ShortRun`
  config for PR CI, `MediumRun` for nightly.
- **Build complexity.** A second project under `Tests/Benchmarks/`
  doubles the test-layer surface. Acceptable.
- **`Release` mode dependency.** Benchmarks must run `Release`. Anyone
  who runs `dotnet build` for diff-checking still hits the build, just
  in Debug. No silent failure.

The combined drawback is small relative to the upside of credible
per-op numbers that the rest of the v0.6 pass depends on.

---

## 4. The expanded benchmark surface

This is the heart of the document. Every microbench and stress test is
listed below with: rationale (why we need it), signature (the method
shape we'll add), target threshold (the number it must beat to be
considered acceptable), regression gate (how a regression is detected),
and project location (xUnit smoke or BDN-rigor).

Numbering is for cross-reference from `master-plan.md` and other
research docs.

### 4.1 [B-001] Per-tick `MetricCollector` cost

**Rationale.** `MetricCollector.Tick` is the single most-called function
in the mod. It runs every frame. The 0.27 ms/tick playtest figure says
it dominates the profiler's own cost. We need a synthetic harness for
this exact call so each optimisation has a per-op number to beat.

**Signature.**

```csharp
[Benchmark]
[MemoryDiagnoser]
public void Tick_Standard()
{
    _collector.Tick(tickIndex: _i++, unixMs: _i * 17,
                    perModSamples: _samples, allocBytesThisTick: 0);
}

[Benchmark]
[MemoryDiagnoser]
public void Tick_Lite()  { /* same, mode = Lite, skips alloc + focus probe */ }

[Benchmark]
[MemoryDiagnoser]
public void Tick_Deep()  { /* same, mode = Deep, alloc tracking on */ }
```

The fixture pre-fills `_samples` with 30 mods at typical magnitudes;
warmup leaves the Baseline calibrated. A `[Setup]` builds the synthetic
1800-frame history.

**Target threshold.** Lite < **15 µs/tick** at the 60-Hz budget (1% of
16.67 ms). Standard < **50 µs/tick** (3%). Deep < **100 µs/tick** (6%).
These are the Invariant 2 contract converted to absolute ns.

**Regression gate.** BDN `--statisticalTest 5%` on `mean` and `allocated`;
xUnit smoke fixture (`MetricCollectorBenchTests.Lite_PerTick_Under15us`)
fails the build if mean > 30 µs (gives 2× headroom for noisy CI).

**Allocation contract.** `Allocated` column must read **0 B/op** in all
three modes. Any non-zero number flags an alloc that escaped the
zero-alloc audit; a CI gate (`Assert.Equal(0, mem.AllocatedBytes)`)
makes the regression visible.

**Project.** BDN-rigor primarily; xUnit smoke layer for cheap CI signal.

### 4.2 [B-002] `ProbeStack.Enter / Leave` cost

**Rationale.** Every IL-injected hook fires `ProbeStack.Enter` on entry
and `ProbeStack.Leave` on exit. With ~10,258 hooks installed and a
worst-case 5× re-entrant nesting, a single tick can trigger thousands
of Enter/Leave pairs. Each pair must be near-zero (< 30 ns) or the
profiler is the lag. This is the single most cost-sensitive call site
in the codebase.

**Signature.**

```csharp
[Benchmark]
public void EnterLeave_FlatTen()
{
    for (int i = 0; i < 10; i++)
    {
        ProbeStack.Enter(_modId, _hookId);
        ProbeStack.Leave();
    }
}

[Benchmark]
public void EnterLeave_NestedFive()
{
    ProbeStack.Enter(_modId, _hookId);
    ProbeStack.Enter(_modId, _hookId);
    ProbeStack.Enter(_modId, _hookId);
    ProbeStack.Enter(_modId, _hookId);
    ProbeStack.Enter(_modId, _hookId);
    ProbeStack.Leave(); ProbeStack.Leave(); ProbeStack.Leave();
    ProbeStack.Leave(); ProbeStack.Leave();
}
```

**Target threshold.** `EnterLeave_FlatTen` < **300 ns/op** (30 ns per
pair, the IL-detour overhead floor on .NET 8). `EnterLeave_NestedFive` <
**200 ns/op** (40 ns per pair allowing for the stack-depth bookkeeping).

**Regression gate.** BDN statistical test 5% mean; alloc-counter
`Allocated == 0` strict; CI smoke fails > 1 µs/op.

**Project.** BDN-rigor; xUnit smoke as well — this is the most-called
function in the mod, the protection layer should be redundant.

### 4.3 [B-003] `GC.GetAllocatedBytesForCurrentThread` cost

**Rationale.** The v0.6 alloc plan rests on calling this method once per
tick to compute the per-tick alloc delta. The cost is undocumented;
empirical reports suggest 30–80 ns/call on .NET 8, but we need an
authoritative number on Apple Silicon (where `Stopwatch.GetTimestamp`
resolution is 41.67 ns/tick — see the eclecticlight.co article on
Apple Silicon timers). If the call costs > 200 ns we cannot afford it
in Lite mode.

**Signature.**

```csharp
[Benchmark]
public long GetAllocatedBytesForCurrentThread_Cost()
    => GC.GetAllocatedBytesForCurrentThread();
```

Returning the value prevents the JIT from eliding the call.

**Target threshold.** < **100 ns/op** on M-series. If higher, the
optimisation plan must gate the call behind a "every N ticks" cadence,
e.g. once per second instead of per tick.

**Regression gate.** BDN statistical test 10% (this is OS-dependent
and noisier than the in-process benches).

**Project.** BDN-rigor.

### 4.4 [B-004] `SpikeDetector` per-tick cost

**Rationale.** The spike detector runs every `EndTick`. It walks the
ring buffer's last 50 frames computing the MAD-based threshold. The
walk is O(50) but the MAD compute is a sort; sort allocations were a
known issue in earlier revs (`Array.Sort` on `double[]` boxes). Need a
per-op number.

**Signature.**

```csharp
[Benchmark]
public void SpikeDetector_EndTick_Standard()
    => _spike.OnEndTick(in _frame, in _baseline);
```

The fixture pre-fills the ring with 1,800 synthetic frames in a typical
distribution; the benchmarked call is the per-tick path only.

**Target threshold.** < **20 µs/tick** at 60-Hz; allocs == 0.

**Regression gate.** BDN 5% mean, alloc-counter 0.

**Project.** BDN-rigor; xUnit smoke.

### 4.5 [B-005] `StallDetector` per-tick cost

**Rationale.** Same shape as spike but with the OS focus probe call
inside. The focus probe is OS-bound on macOS and reads via P/Invoke;
we need to know its cost.

**Signature.**

```csharp
[Benchmark]
public void StallDetector_BeginEndTick()
{
    _stall.OnBeginTick();
    _stall.OnEndTick(in _frame, in _baseline,
        focusHeldAcrossGap: true, recentStallsInLast5s: 0);
}
```

**Target threshold.** < **30 µs/tick** for the begin+end pair in
Standard mode. < **10 µs/tick** in Lite (no focus probe). Allocs == 0.

**Regression gate.** BDN 5% mean.

**Project.** BDN-rigor.

### 4.6 [B-006] `ContextTransitionWatcher` per-tick cost

**Rationale.** This watcher reads weather flags, biome bits, invasion
state, and game-mode every tick. The read is structural-equality
against a cached `EventContext`. We need the per-tick cost to be < 5
µs even with all 16 watched fields toggling.

**Note.** `ContextTransitionWatcher` is excluded from the Compile Include
set (transitively pulls Terraria types). To bench it, we either:

1. Extract a pure-logic `EventContextDiffer` static method and bench
   *that* (preferred — it's the work, the rest is just Terraria
   pump-by-tick).
2. Build a thin synthetic that constructs `EventContext` values
   directly and calls the differ.

Option 1 is the recommended refactor; the watcher itself becomes a
thin Terraria-side wrapper around a pure differ.

**Signature.**

```csharp
[Benchmark]
public bool ContextDiff_AllFieldsToggle()
    => EventContextDiffer.HasChanged(in _prev, in _curr);
```

**Target threshold.** < **5 µs/op**, alloc 0.

**Regression gate.** BDN 5% mean.

**Project.** BDN-rigor; xUnit smoke.

### 4.7 [B-007] Each `Stream.Apply` cost — twelve micro benches

**Rationale.** `baseline.md` says six new streams in v0.5 drove the
enqueue cost from 276 → 441 ns. Each stream's `Apply` runs on the writer
thread, but the *enqueue* of a stream-bound op runs on the game thread.
The game-thread cost is the budget concern; the writer-thread cost is
the drain-throughput concern. We need per-stream numbers for both.

The streams (per `Profiling/Persistence/Streams/`):

| # | Stream | Game-thread enqueue | Writer-thread Apply |
|---|---|---|---|
| 7.1 | `SessionStream` | [B-007a] | [B-007b] |
| 7.2 | `TickAggregateStream` (warm + cold + archive) | [B-007c] | [B-007d] |
| 7.3 | `SpikeStream` | [B-007e] | [B-007f] |
| 7.4 | `StallStream` | [B-007g] | [B-007h] |
| 7.5 | `StallClusterStream` | [B-007i] | [B-007j] |
| 7.6 | `ContextTransitionStream` | [B-007k] | [B-007l] |
| 7.7 | `WorldSnapshotStream` | [B-007m] | [B-007n] |
| 7.8 | `PlayerDeathStream` | [B-007o] | [B-007p] |
| 7.9 | `ModlistStream` | [B-007q] | [B-007r] |
| 7.10 | `InsightStream` | [B-007s] | [B-007t] |
| 7.11 | `PerSessionAggregateStream` | [B-007u] | [B-007v] |
| 7.12 | `InteractionStreams` (damage-taken / damage-dealt / npc-spawn / item-created / loadout / buff) | [B-007w] | [B-007x] |

Twelve `Apply` benches and twelve `Enqueue` benches; 24 methods total.
Boilerplate-heavy but each is 10 lines. A shared `StreamBenchHarness`
base class cuts the LOC.

**Signature (representative).**

```csharp
[Benchmark]
public void SpikeStream_Apply()  => _spikeStream.Apply(in _spikeRow, _db);

[Benchmark]
public void SpikeStream_Enqueue() => _writer.Enqueue(DbWriteOp.Spike(_spikeRow));
```

**Target threshold.** Aggregate sum of all twelve `Enqueue` paths in a
single tick (worst-case combat tick with all streams firing) <
**400 ns/tick**. Per-stream `Apply` < **150 µs/op** worst case
(`PerSessionAggregateStream` is the heaviest due to the 100-mod loop;
the others should be < 30 µs).

**Regression gate.** BDN per-method 5% mean; an additional aggregate
test composes them into the "worst-case combat tick" pattern.

**Project.** BDN-rigor.

### 4.8 [B-008] `EventJournal.Append` cost

**Rationale.** The event journal is the audit-log channel for
hook-install errors, abort-clean events, and stream-recovery. It
appends to a file via a writer thread; the *enqueue* cost on the
caller must be small. Per `baseline.md`, this surface is undocumented
in the current bench set.

**Signature.**

```csharp
[Benchmark]
public void EventJournal_Append_Info()
    => _journal.Append(EventLevel.Info, "Tick-bench-event", correlationId: null);

[Benchmark]
public void EventJournal_Append_WithContext()
    => _journal.Append(EventLevel.Warn, "Hook install failed", correlationId: "M:1234");
```

**Target threshold.** < **500 ns/op** for the enqueue side, alloc == 0.
The `string` parameter is necessarily allocated by the caller; we test
with interned string literals so the bench measures the journal cost,
not the call site's allocation.

**Regression gate.** BDN 5% mean; alloc-counter test.

**Project.** BDN-rigor.

### 4.9 [B-009] `InsightsEngine` 1-Hz tick cost

**Rationale.** The Insights Engine runs once per second (1-Hz cadence)
rather than per tick. Each call walks the last 60 seconds of warm rows
applying every active rule. The current implementation is unmeasured;
we need the per-cycle cost to confirm the 1-Hz cadence is sufficient.

**Signature.**

```csharp
[Benchmark]
public void InsightsEngine_OnSecond_30Rules_60sWindow()
    => _engine.OnSecondTick(_warmHistory, _baseline);
```

**Target threshold.** < **2 ms/cycle** at the 30-rule + 60-row scale
(equivalent to 0.2% of the 1-second budget). Allocs are not zero here —
the engine creates `InsightRecord` instances when a rule fires — but
should stay below 4 KB/cycle (under the SOH per-second budget).

**Regression gate.** BDN 5% mean; alloc-counter < 8 KB/cycle (2× headroom).

**Project.** BDN-rigor.

### 4.10 [B-010] End-of-session aggregation cost (the 8.5 s playtest stall)

**Rationale.** The playtest baseline shows a 40-stall, 8.5-second cluster
at session end, contributor `PerformanceProfiler` itself. That is the
session-finaliser running on the main thread. We need a synthetic
benchmark for the exact aggregation work so the v0.6 plan can verify
the off-main-thread relocation actually moved it.

**Signature.**

```csharp
[Benchmark]
public TickAggregateArchive Finalise_TenMinuteSession_100Mods()
    => _finalizer.Build(_session, _allWarmRows, _allColdRows,
                       _allSpikes, _allStalls, _allInteractions);
```

The fixture pre-builds a synthetic 10-minute session with 600 warm
rows, 10 cold rows, 50 spikes, 50 stalls, 5,000 damage events, 100
mods, full interaction stream payloads.

**Target threshold.** Pre-pass: TBD by first run, expected ≈ 8 s based
on the playtest cluster. Target post-pass: < **50 ms/op** when moved
off-main-thread *and* < **200 ms/op** wall time on the worker thread.

**Regression gate.** BDN 5% mean; CI smoke fails > 500 ms.

**Project.** BDN-rigor.

### 4.11 [B-011] Hook-install cost (with synthetic mod)

**Rationale.** `baseline.md` says 481 MB delta at first install across
10,258 hooks ≈ 23–60 KB/hook. The figure comes from an in-game playtest
read; we need a synthetic mod with N hooks that drives `ILHookInterceptor.Install`
through a configurable hook count so the v0.6 hook-install plan can
verify its target (< 80 MB / 10,258 hooks ≈ < 8 KB/hook).

**Signature.**

```csharp
[Benchmark]
[Arguments(100, 1000, 5000, 10000)]
public long HookInstall_Synthetic(int hookCount)
{
    long before = GC.GetTotalAllocatedBytes(precise: true);
    _interceptor.InstallSynthetic(hookCount);
    long after = GC.GetTotalAllocatedBytes(precise: true);
    return after - before; // bytes/installs
}
```

The "synthetic mod" is an in-test assembly that exposes N methods
matching the hookable shape, registered via `ILHookInterceptor`'s public
surface (no tModLoader runtime needed for the install math itself —
the IL emission and the bookkeeping). This is the highest-cost piece
of the harness build-out; it requires extracting a pure-logic install
path from `ILHookInterceptor` that runs without `MonoModHooks`. If that
extraction is infeasible, the bench falls back to running inside the
game and capturing `client.log`-logged delta.

**Target threshold.** < **8 KB/hook** allocated; < **100 ms/install** for
10,000 hooks.

**Regression gate.** BDN 5% mean. Hooks-per-MB ratio is the primary
output column.

**Project.** BDN-rigor; integration fallback if pure-logic extraction
proves infeasible.

### 4.12 [B-012] Stress simulation — 10-min Calamity-scale, full pipeline

**Rationale.** Every microbench above measures one surface in isolation.
The pipeline-level claim ("Standard mode stays under 4% overhead at
Calamity scale") cannot be verified by a sum-of-parts argument because
the streams contend on the writer thread, the GC contends with the
game thread, and the journal contends with the streams. We need a
synthetic harness that runs the full pipeline at Calamity-scale event
rates for 10 minutes of simulated time and reports the cumulative
cost.

**Signature.**

```csharp
[Benchmark]
[Arguments(60, 600)]      // 60 sim seconds (smoke), 600 sim seconds (full)
public StressReport Stress_FullPipeline_Calamity(int simSeconds)
{
    var harness = new StressHarness()
        .WithModCount(150)
        .WithCombatEventsPerSecond(120)
        .WithNpcSpawnsPerSecond(8)
        .WithDamageEventsPerSecond(40)
        .WithItemCreatedEventsPerSecond(15)
        .WithBuffEdgeEventsPerSecond(6)
        .WithLoadoutSnapshotsPerSecond(2)
        .WithBaselineFrameMs(16.7);
    return harness.Run(simSeconds);
}
```

`StressReport` captures: total wall time, per-second p50/p95/p99 tick
ms, total bytes allocated, GC counts (Gen0/1/2), DB size growth,
journal size growth, and the "max sustained ops/sec" on the writer.

**Target threshold.** Standard mode: total CPU overhead < **4%** of
simulated wall time (Invariant 2 contract). Lite mode: < **1%**. Deep
mode: < **10%**.

**Regression gate.** BDN 5% mean on the wall-time column; CI smoke
fails > 8% Standard.

**Project.** BDN-rigor; long-running so flagged for nightly only.

### 4.13 [B-013] Cross-cutting: `Compile Include` no-Terraria lint

**Rationale.** §1.2 identifies the un-enforced invariant: every linked
file is Terraria-free. A future addition that quietly drags in
`Terraria.ModLoader` would cause a build failure on the test project;
the failure mode is loud but late. A shell-script lint in
`Tests/lint-no-terraria-refs.sh` makes it early:

```bash
#!/usr/bin/env bash
set -eu
FILES="
  Profiling/RingBuffer.cs
  Profiling/Baseline.cs
  Profiling/PerModSample.cs
  Profiling/PerModAttribution.cs
  Profiling/TickFrame.cs
  Profiling/StallDetector.cs
  Profiling/Events/*.cs
  Profiling/Insights/*.cs
  Profiling/Persistence/*.cs
  Profiling/Persistence/Records/*.cs
  Profiling/Persistence/Streams/*.cs
"
EXCLUDE="
  Profiling/Persistence/ProfilerPaths.cs
  Profiling/Persistence/LegacyJsonImporter.cs
  Profiling/Persistence/ProfilerCompactCommand.cs
  Profiling/Persistence/ModlistFingerprint.cs
  Profiling/Persistence/SessionRecorder.cs
  Profiling/Persistence/TickDownsampler.cs
  Profiling/Persistence/ContextTransitionWatcher.cs
  Profiling/Persistence/WorldSnapshotter.cs
  Profiling/Persistence/PlayerDeathDetector.cs
  Profiling/Persistence/SessionSummaryLogger.cs
  Profiling/Persistence/Commands/*.cs
  Profiling/Persistence/Interactions/*.cs
  Profiling/ProfilerFocusProbe.cs
"
# Resolve the include / exclude difference, then grep each file.
# Exit non-zero if any "using Terraria" or "using Terraria.ModLoader"
# appears in the included set.
```

**Project.** Repo-level CI hook; not a BDN benchmark.

### 4.14 [B-014] xUnit smoke layer — the "always-on" benches

**Rationale.** BDN is heavyweight; we don't want every `dotnet test` to
spend 5 minutes. The xUnit smoke layer keeps cheap order-of-magnitude
benches in the existing `Tests/*` project for every PR.

**Coverage:** one `[Fact]` per BDN class, with a 2× headroom assert.

| Smoke fixture | Asserts |
|---|---|
| `MetricCollectorSmokeTests.Tick_Standard_Under100us` | mean of 10,000 ops < 100 µs (Invariant 2 floor doubled). |
| `ProbeStackSmokeTests.EnterLeave_Under1us` | mean of 100,000 pairs < 1 µs. |
| `AllocCounterSmokeTests.GetAllocatedBytes_UnderMicrosecond` | mean < 1 µs. |
| `SpikeDetectorSmokeTests.EndTick_Under100us` | mean < 100 µs. |
| `StallDetectorSmokeTests.BeginEnd_Under100us` | mean < 100 µs. |
| `StreamApplySmokeTests.AllStreams_TickEnqueueBudget_Under1us` | sum of one enqueue per stream < 1 µs. |
| `InsightsEngineSmokeTests.OnSecond_Under10ms` | < 10 ms/cycle. |
| `SessionFinaliseSmokeTests.TenMin_100Mods_Under1s` | < 1 s. |

Each follows the existing pattern of `Stopwatch.StartNew()` + `_output.WriteLine`
+ loose `Assert.True`. Eight new fixtures, ~120 LOC each. Total run
cost added to the suite: ~5 s on a warm dev box.

**Project.** xUnit smoke.

### 4.15 Surface summary

The full additions:

```
[B-001]  MetricCollector.Tick                       (Lite / Standard / Deep)  → BDN + smoke
[B-002]  ProbeStack.Enter+Leave                     (flat / nested)           → BDN + smoke
[B-003]  GC.GetAllocatedBytesForCurrentThread cost                            → BDN
[B-004]  SpikeDetector.OnEndTick                                              → BDN + smoke
[B-005]  StallDetector.OnBeginTick + OnEndTick                                → BDN + smoke
[B-006]  ContextTransitionWatcher diff (pure)                                 → BDN + smoke
[B-007]  Each Stream.Apply + .Enqueue           (12 streams × 2 = 24 methods) → BDN + 1 smoke
[B-008]  EventJournal.Append                                                  → BDN
[B-009]  InsightsEngine 1-Hz tick                                             → BDN + smoke
[B-010]  End-of-session aggregation                                           → BDN + smoke
[B-011]  Hook-install cost / KB-per-hook                                      → BDN (or integration)
[B-012]  Stress: 10-min Calamity-scale pipeline                               → BDN (nightly)
[B-013]  No-Terraria-refs lint                                                → shell script
[B-014]  xUnit smoke layer (8 fixtures)                                       → xUnit
```

Method count after the pass: ~50 BDN methods, ~8 xUnit smoke fixtures,
1 shell lint. From 4 → 58 measurement points. That is the order-of-
magnitude expansion the v0.6 pass needs.

---

## 5. Regression-detection design

A benchmark suite that nobody checks is just a number generator. The
gate that turns numbers into invariants is the regression-detection
layer. This section designs it.

### 5.1 Three layers of regression detection

| Layer | Speed | What it catches | Where it runs |
|---|---|---|---|
| **L1 — xUnit smoke assertions** | < 30 s | order-of-magnitude regressions (a 5× slowdown) | every `dotnet test` |
| **L2 — BDN statistical test** | 5–10 min | statistically-significant slowdowns (`--statisticalTest 3%`) | wrap-up phase of every commit; nightly CI |
| **L3 — BDN baseline JSON diff** | 5–10 min | cumulative drift across many small changes (sum of < 3% individual changes that total > 10%) | nightly CI; release prep |

Each layer is necessary; none is sufficient alone. L1 catches "this
PR broke the budget"; L2 catches "this PR introduced a 4% slowdown that
L1 missed"; L3 catches "the last twenty PRs each added 2%".

### 5.2 L1 design — xUnit smoke assertions

Already specified in §4.14. The `Assert.True(perOpNs < X, ...)` line is
the gate. The X is the Invariant-2 budget times two (give CI 2×
headroom for noise). The smoke fixture must `_output.WriteLine` the
actual number so the developer can read the trend in `dotnet test -v n`.

Failure mode: a smoke assert fails, the developer sees the number in
test output, and either fixes the regression or recalibrates the
threshold with a written rationale committed in the same PR.

### 5.3 L2 design — BDN statistical test

BDN ships with `StatisticalTestColumn`, accessed via:

```bash
dotnet run -c Release --project Tests/Benchmarks -- \
    --filter "*" \
    --statisticalTest 3%
```

`3%` means "the Mann–Whitney U test on the per-iter samples must reject
the null hypothesis (no change) at p < 0.05 for changes ≥ 3%". The
column prints `Slower` / `Faster` / `Same` per benchmark; the run exits
non-zero if any is `Slower`.

The baseline JSON for the comparison is `BenchmarkDotNet.Artifacts/<timestamp>/results/*.json`
from a previous run. Committing one baseline file per release into
`context/perf-pass/baselines/<version>/*.json` gives us:

```
context/perf-pass/baselines/
  v0.5/   ← committed at v0.5 wrap
  v0.6/   ← committed at v0.6 wrap (the post-pass numbers)
```

A `compare.sh` script then runs the current build's BDN output against
the v0.6 baseline:

```bash
dotnet run -c Release --project Tests/Benchmarks -- \
    --filter "*" \
    --statisticalTest 3% \
    --exporters json
# Then diff against context/perf-pass/baselines/v0.6/.
python3 Tests/Benchmarks/compare.py \
    context/perf-pass/baselines/v0.6 \
    BenchmarkDotNet.Artifacts/results
```

`compare.py` is a small (~80 LOC) Python script that reads both result
sets, computes ratios per benchmark, and exits non-zero if any
benchmark regressed beyond the per-bench threshold.

### 5.4 L3 design — baseline JSON drift gate

L3 is the "twenty PRs each adding 2%" defence. Even if every individual
PR passes L1 and L2, the cumulative drift can exceed the budget. L3
catches this with a per-quarter (or per-release) re-baselining:

1. At v0.6 release: commit the baseline JSON.
2. Every nightly CI run: run BDN, diff against the committed baseline.
3. If any benchmark drifted > 10% from the committed baseline (even if
   each individual day's reading was within 3% of the previous day's),
   the nightly CI fails and a dashboard email goes out.

This is the same drift-defence pattern used by .NET runtime CI; it is
the only way to catch small-but-cumulative regressions.

### 5.5 Output format

Every L1 smoke fixture, every L2 BDN run, and every L3 drift report
emits a single Markdown table that can be pasted into the wrap-up
commit message or the post-pass report. The shape:

```markdown
| Bench | v0.6 baseline | This run | Δ | Verdict |
|---|---|---|---|---|
| MetricCollector_Tick_Standard | 44.2 µs | 31.7 µs | -28% | Faster ✓ |
| ProbeStack_EnterLeave_FlatTen | 240 ns | 245 ns | +2% | Same |
| SpikeDetector_EndTick | 14.6 µs | 22.1 µs | +51% | Slower ✗ |
```

A `Tests/Benchmarks/emit-report.py` script consumes the BDN JSON and
the baseline JSON and produces this Markdown. It is invoked from the
wrap-up phase.

### 5.6 Wrap-up wiring

The `CLAUDE.md` "operating loop" step 7 says: "commit at logical
checkpoints with a comprehensive message." For perf-pass commits, the
commit message now includes the §5.5 Markdown table by convention. The
session-wrap macro automates this:

```bash
# Wrap-up phase script (proposed):
dotnet test Tests/PerformanceProfiler.Tests.csproj -c Release \
    --filter "FullyQualifiedName~Smoke" -v n > /tmp/smoke.txt
dotnet run -c Release --project Tests/Benchmarks -- \
    --filter "*" --exporters json --statisticalTest 3% > /tmp/bdn.txt
python3 Tests/Benchmarks/emit-report.py \
    context/perf-pass/baselines/v0.5 \
    BenchmarkDotNet.Artifacts/results \
    > /tmp/report.md
git commit -m "$(cat <<EOF
v0.6 batch N: <short summary>

Bench delta vs v0.5 baseline:
$(cat /tmp/report.md)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

That keeps the perf evidence in the git history, where a future bisect
can find which commit introduced a regression.

### 5.7 Failure handling

What happens when a regression is detected?

| Failure | Action |
|---|---|
| L1 smoke fails | Block the commit. Fix the regression or change the threshold with a written rationale (which appears in the commit message). |
| L2 BDN `Slower` | Block the commit unless the developer adds a `<!-- regression-accepted: <reason> -->` line to the commit message *and* writes a context note in `context/perf-pass/regressions.md`. |
| L3 nightly drift > 10% | Open a "perf-drift" issue against the repo; rerun the suite next nightly to rule out flake; if confirmed, schedule a sweep. |

Accepting a regression is sometimes correct (a new event stream that
adds coverage is allowed to cost ns). The discipline is that the
acceptance is *explicit*, not silent, and is captured durably so the
next contributor knows the threshold moved.

### 5.8 Noise floor and flake handling

Benchmarks are noisy. The Apple Silicon 41.67 ns timer floor + the M-series'
big.LITTLE core scheduling means single-op-level reads have ±20% variance
unless careful. Mitigations:

- BDN already handles this via warmup + iteration count + multimodal
  detection.
- For xUnit smoke, run each loop 10× and take the median (not the mean).
- Pin background processes off the dev box during release-baseline runs:
  close Chrome, Slack, etc. Document this in the wrap-up procedure.
- The Steady_State_Drain bench is the noisiest of the existing four
  because it depends on the writer thread's GC scheduling. Increase
  warmup from 100 ops to 1,000 ops.

---

## 6. Allocation-aware tests — verifying zero-alloc claims at runtime

Invariant 2 says the per-tick hot path is zero-allocation. Today this is
verified by code review and by playtest top-of-thread sampling. Neither
catches the case where a closure is captured, a `Func<>` is allocated,
a `string.Format` slips in via a logging call, or a struct is silently
boxed when passed to an `object` parameter.

### 6.1 The pattern

```csharp
[Fact]
public void Tick_Standard_AllocatesZeroBytes()
{
    _collector.Tick(0, 0, _samples, 0); // warmup
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 1000; i++)
    {
        _collector.Tick(i, i * 17, _samples, 0);
    }
    long after = GC.GetAllocatedBytesForCurrentThread();
    long perOp = (after - before) / 1000;
    _output.WriteLine($"MetricCollector.Tick allocated {perOp} B/op over 1000 ops.");
    Assert.Equal(0L, perOp);
}
```

The `GC.GetAllocatedBytesForCurrentThread()` call is the truth source.
It is documented and stable on .NET 8 (see the dotnet/runtime issue
#17891 and the docs page). The cost of the call itself is in B-003;
empirically 30–80 ns/call. Dividing the delta by N gives per-op bytes.

### 6.2 What to cover

| Surface | Alloc contract | Test |
|---|---|---|
| `MetricCollector.Tick` (all modes) | 0 B/op | `Tick_*_AllocatesZeroBytes` |
| `ProbeStack.Enter / Leave` | 0 B/pair | `EnterLeave_AllocatesZeroBytes` |
| `RingBuffer<T>.Push` | 0 B/op (T = `TickFrame`, struct) | `RingBufferPush_AllocatesZeroBytes` |
| `PerModAttribution.Accumulate` | 0 B/op | `PerModAttribution_Accumulate_Zero` |
| `SpikeDetector.OnEndTick` | 0 B/op | `SpikeDetector_OnEndTick_Zero` |
| `StallDetector.OnBeginTick / OnEndTick` | 0 B/op | `StallDetector_BeginEnd_Zero` |
| Per-stream `Enqueue` (the game-thread side) | 0 B/op | `<Stream>_Enqueue_Zero` (×12) |

Per-stream `Apply` is *not* zero-alloc (it BSON-serialises and writes
to LiteDB) — the contract there is bounded, not zero, captured by the
B-007 micro benches.

### 6.3 Why a runtime test, not a review check

Code review catches the obvious cases (a literal `new`). It misses:

- Closure capture: `() => foo.Bar()` allocates a `Func` if `foo` is a
  field; review often passes it as "that's just a lambda".
- Implicit boxing: `void Log(object o) { ... }` called with `Log(myStruct)`
  silently boxes. Reviews miss this in 40% of cases in real codebases.
- LINQ on hot paths: a single `.Where(...).First()` on the warm queue
  allocates an iterator. Review catches direct `.ToList()`; chained
  enumerators slip through.
- Hidden `string.Concat`: `"prefix-" + i` allocates an int box and a
  new string.

A runtime `GC.GetAllocatedBytesForCurrentThread` delta catches all of
these mechanically. The cost of the test is ~50 ms per fixture.

### 6.4 GC-mode confound

`GC.GetAllocatedBytesForCurrentThread` is per-thread, so a writer-thread
allocation does not show up in the game-thread fixture. That is correct
for our purposes (we measure the game-thread contract; the writer
thread is allowed to allocate). The test must invoke the production
code *on the same thread* it asserts on, which the existing pattern
does.

### 6.5 BDN's `[MemoryDiagnoser]` as the cross-check

For the BDN-rigor layer, `[MemoryDiagnoser]` reports per-op allocations
to 99.5% accuracy per the BDN docs. We use it as the cross-check
against the hand-rolled `GC.GetAllocatedBytesForCurrentThread` numbers
in the xUnit smoke layer. If they diverge by more than 8 B/op the
test surfaces a `Skip` with a diagnostic note; we investigate.

### 6.6 Tracking SOH vs LOH

`GC.GetAllocatedBytesForCurrentThread` does not distinguish SOH from
LOH. For most of our hot path that's fine (no LOH allocations
expected). The end-of-session aggregation does create large arrays
(>85 KB) for the per-mod summary; B-010 should additionally read
`GC.CollectionCount(2)` before/after to assert that the aggregation
does not trigger a Gen2 (which would extend the stall).

---

## 7. Cross-system dependencies

The test harness is the verification substrate for every other research
doc in this pass. The list below maps each upcoming `research/*.md`
file to the benchmarks it needs in place.

| Research doc | Depends on |
|---|---|
| `research/metric-collection.md` | [B-001], [B-003], allocation-aware [§6.2 row 1]. |
| `research/probe-stack.md` | [B-002], allocation-aware [§6.2 row 2]. |
| `research/per-mod-attribution.md` | [B-001] (sub-component), allocation-aware [§6.2 row 4]. |
| `research/spike-detection.md` | [B-004], allocation-aware [§6.2 row 5]. |
| `research/stall-detection.md` | [B-005], allocation-aware [§6.2 row 6]. |
| `research/context-transitions.md` | [B-006]. |
| `research/persistence-streams.md` | [B-007] (all 12 streams), [B-008]. |
| `research/event-journal.md` | [B-008]. |
| `research/insights-engine.md` | [B-009]. |
| `research/end-of-session.md` | [B-010]. |
| `research/hook-install.md` | [B-011]. |
| `research/full-pipeline-stress.md` | [B-012]. |
| `research/storage-shape.md` (DB size, compaction) | The existing `Simulated_TenMinute_Session_FileSize` + new compaction-after-N-sessions test (likely [B-015], not enumerated above). |

Every benchmark above is **prerequisite** to landing the matching
research doc's recommendation. The harness pass must therefore be
*ordered before* the system-level optimisations; §8 sequences this.

The reverse dependency also exists. When a research doc adds a new
recommendation, the harness may need a new bench. The Test Harness doc
is the meta-layer.

---

## 8. Prioritised execution order

The benchmarks land in this order. Each step is a discrete commit.

| Step | What | Why this order |
|---|---|---|
| 1 | **Rerun the existing four benches in Release; update `baseline.md`.** | The 441 ns/op number is from Debug; every claim downstream rests on a correct baseline. One-minute change, removes the biggest source of confusion before any optimisation lands. |
| 2 | **Add the `Tests/Benchmarks/` BDN project (§3.6).** | Foundation for every BDN bench. Empty project + Program.cs + BenchConfig + one canary `[Benchmark]` method (e.g. `RingBuffer.Push`). |
| 3 | **Land [B-002] ProbeStack and [B-003] alloc-counter cost.** | These are the lowest-level primitives every other bench depends on. They also have the strongest "this number had better be small" property; verifying them first calibrates expectations. |
| 4 | **Land [B-001] MetricCollector Tick (all three modes).** | The single most-called function. Once this bench passes, every other tick-level claim has a reference point. |
| 5 | **Land allocation-aware xUnit smoke tests (§6.2).** | Cheap to write, fast to run, catch the largest class of silent regressions. Lock them in early so subsequent commits in the pass cannot accidentally introduce allocs. |
| 6 | **Land [B-004] SpikeDetector and [B-005] StallDetector.** | These complete the "every per-tick subsystem has a bench" set. |
| 7 | **Land [B-006] ContextTransitionWatcher (pure differ).** | This requires the recommended refactor (extract `EventContextDiffer`). Do the extraction here so the bench can land. |
| 8 | **Land [B-008] EventJournal.Append.** | Independent of streams; can land in parallel with later steps. |
| 9 | **Land [B-007] per-stream Enqueue + Apply (12 streams × 2).** | The single largest LOC chunk; can be batched into the same commit if the test harness scaffolding (`StreamBenchHarness` base class) is in place. |
| 10 | **Land [B-009] InsightsEngine 1-Hz tick.** | Lower-frequency, less budget-sensitive. After the per-tick layer is locked. |
| 11 | **Land [B-010] end-of-session aggregation.** | Requires the synthetic 10-min session builder; this is the bench that proves the 8.5 s stall got moved off-thread. |
| 12 | **Land [B-011] hook-install (with synthetic mod).** | Highest extraction cost; deferred until last in case the pure-logic extraction proves infeasible (fallback: integration-test from inside the game). |
| 13 | **Land [B-012] stress simulation.** | The capstone. Requires every other bench's underlying production code to exist and be stable. |
| 14 | **Wire the wrap-up bench report (§5.6).** | After every other bench exists; cement the workflow. |
| 15 | **Commit v0.6 baseline JSON.** | Lock in the post-pass numbers as the future-reference baseline. |

Time estimate: steps 1–4 in ≤ one session each; steps 5–8 in one or two
sessions; step 9 is the longest single chunk (one full session); steps
10–14 one session each; step 15 trivial. Total: ~10 sessions of pure
harness work, which underwrites the rest of the v0.6 pass.

### 8.1 What can land in parallel

Once step 2 is in, steps 3, 5, 8, and 11 are independent and can be
parallelised across subagents if multiple are dispatched. Steps 4, 6,
7, 9 chain on each other (each needs the previous fixture or
refactor).

### 8.2 Risk to the schedule

The single highest-risk step is **B-011 hook-install**. If the
`ILHookInterceptor` cannot be cleanly extracted into a pure-logic
install path, the bench moves to integration-only (in-game capture
via `client.log`), and the hook-install research doc loses a clean
synthetic reference number. That is acceptable but worth flagging
early — the extraction attempt should happen at step 12, not at the
last minute.

### 8.3 Stop-loss

If any single step takes more than 1.5× its budget, the step is broken
into smaller subgoals and progress moves to the next step in parallel.
The harness is the substrate; it cannot be allowed to gate the rest of
the pass indefinitely.

---

## 9. References

External:

- [BenchmarkDotNet docs — Diagnosers (`MemoryDiagnoser`)](https://benchmarkdotnet.org/articles/configs/diagnosers.html) — confirms MemoryDiagnoser is 99.5% accurate, per-1000-op normalised, off by default in current versions.
- [BenchmarkDotNet — Baselines](https://benchmarkdotnet.org/articles/features/baselines.html) — `[Benchmark(Baseline = true)]`, Ratio column, RatioSD column.
- [BenchmarkDotNet — IntroRatioSD sample](https://benchmarkdotnet.org/articles/samples/IntroRatioSD.html) — distribution of the Ratio column, not just the mean.
- [`StatisticalTestColumn` exposed via command line (#960)](https://github.com/dotnet/BenchmarkDotNet/commit/51a96595a896769a257f7018b04b1f8049c67646) — `--statisticalTest 3%` is the regression-detection flag.
- [Run BenchmarkDotNet in xUnit — tech-fellow.eu](https://tech-fellow.eu/2022/11/01/run-benchmarks-in-xunit/) — pattern walkthrough; we reject this pattern, retaining BDN as its own project.
- [How to Integrate BenchmarkDotNet With Unit Tests — code-maze.com](https://code-maze.com/how-to-integrate-benchmarkdotnet-with-unit-tests/) — same pattern; same rejection reasons.
- [`GC.GetAllocatedBytesForCurrentThread` API](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-8.0) — per-thread allocated bytes, the truth source for §6.
- [Issue #17891 — Provide API to understand how many allocations have happened](https://github.com/dotnet/runtime/issues/17891) — design rationale for the API.
- [BenchmarkDotNet Issue #1153 — Use GC.GetTotalAllocatedBytes when available](https://github.com/dotnet/BenchmarkDotNet/issues/1153) — confirms BDN's MemoryDiagnoser is built on the same primitive.
- [`Stopwatch.GetTimestamp` API](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.stopwatch.gettimestamp?view=net-8.0) — high-resolution counter source.
- [dotnet/runtime Issue #26496 — Problems with Stopwatch resolution on macOS](https://github.com/dotnet/runtime/issues/26496) — macOS Stopwatch resolution historically 1 µs on .NET Core 2.1; improved since.
- [Stopwatch under the hood — aakinshin.net](https://aakinshin.net/posts/stopwatch/) — deep dive on `Stopwatch.IsHighResolution`, `Frequency`, and per-platform timer sources.
- [Eclectic Light Co. — Apple Silicon timer changes](https://eclecticlight.co/2020/09/08/changing-the-clock-in-apple-silicon-macs/) — Apple Silicon ticks every 41.67 ns vs Intel's 1 ns; floor for our Stopwatch resolution.
- [xUnit — Running Tests in Parallel](https://xunit.net/docs/running-tests-in-parallel) — collection-level parallelism; benchmarks must be in their own collection or have parallelism disabled at the assembly level.
- [Meziantou — Parallelize test cases in xUnit](https://www.meziantou.net/parallelize-test-cases-execution-in-xunit.htm) — per-method parallelism via custom test framework.

Internal:

- `CLAUDE.md` — the five Project Invariants; the Test Harness must satisfy 1 (read-only), 2 (zero-alloc, budget), 4 (abort-clean), 5 (no mod-specific code).
- `context/notes/philosophy.md` — "Optimisation = doing what we already do at maximum efficiency. It is not = doing less." The harness expands; it does not shrink.
- `context/perf-pass/baseline.md` — the four current benchmark numbers, the playtest readings, and the v0.6 targets the harness must measure progress against.
- `context/systems/test-harness.md` — the system-level documentation of the current shape, the Compile-Include-Link discipline, and the known issues (no Terraria-reference lint).
- `Tests/PerformanceProfiler.Tests.csproj` — the project the new fixtures land in.
- `Tests/Persistence/PersistenceBenchmarkTests.cs` — the existing pattern the smoke layer follows.
- `Profiling/MetricCollector.cs`, `Profiling/ProbeStack.cs`, `Profiling/SpikeDetector.cs`, `Profiling/StallDetector.cs` — the hot-path surface the new benchmarks measure.
- `Profiling/Persistence/Streams/*.cs` — the 12 streams enumerated in [B-007].
- `Profiling/Persistence/EventJournal.cs` — the surface for [B-008].
- `Profiling/Persistence/SessionRecorder.cs` — owner of the end-of-session aggregation that [B-010] benchmarks (currently `Compile Remove`d; the aggregation work itself may need extraction into a pure-logic class to be benchable).

---

*End of test-harness research dossier. Companion docs in this pass measure their own changes against the benchmarks specified here. Any future research doc that proposes an optimisation without a `[B-NNN]` reference in its verification section is incomplete by construction.*
