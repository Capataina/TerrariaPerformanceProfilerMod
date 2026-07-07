# Test Harness

*Maturity: working · Stability: stable.*

## Scope / Purpose

A non-shipping xUnit test project that pins the pure-logic surfaces the audit rounds and the insights rework identified as load-bearing: insights ranking and confidence promotion, the `Insights/Shared` primitives (mod metrics, shares, names), the reference-frame substrate (stats, per-context baselines, temporal early/late baselines, the cross-session LiteDB round-trip), ring-buffer wrap-around, the baseline service, stall detection / classification, the time helper, the object pools, the `BoolIndex` helper, and the LiteDB persistence round-trip + benchmark. It does **not** exercise any code that touches tModLoader, the game runtime, or any IL emission — those are tested manually via the in-game build cycle.

It is the **L1 axis** of the layered testing strategy in `context/plans/extensive-testing-infrastructure.md`; the dashboard-driving L4/L6/L8 axis lives in `systems/dashboard-audit-harness.md`.

The discipline is "every pure-logic regression that would break behaviour silently gets a test before the production code is allowed to change shape."

## Boundaries / Ownership

Files: `Tests/PerformanceProfiler.Tests.csproj`; the support files `Tests/_TestNamespaceStubs.cs` (xUnit serial-execution config + empty namespace stubs) and `Tests/HookInstallRetentionDiagnostics.cs` (a pure System/xUnit diagnostic, no mod deps); and the fixtures `Tests/BaselineTests.cs`, `Tests/BoolIndexTests.cs`, `Tests/InsightStoreTests.cs`, `Tests/PoolsTests.cs`, `Tests/RankingScorerTests.cs`, `Tests/RingBufferTests.cs`, `Tests/StallClassifierTests.cs`, `Tests/StallDetectorTests.cs`, `Tests/TimeTests.cs`, `Tests/Insights/CrossSessionStoreTests.cs`, `Tests/Insights/ReferenceFrameTests.cs`, `Tests/Insights/SharedPrimitivesTests.cs`, `Tests/Insights/TemporalBaselineTests.cs`, `Tests/Persistence/PersistenceBenchmarkTests.cs`, `Tests/Persistence/PersistenceRoundTripTests.cs`.

Owns:

- The xUnit test project (separate SDK-style csproj).
- The `Compile Include + Link` mechanism that pulls pure-logic source files in without dragging tModLoader assemblies into the runner.
- The build-time isolation from the `.tmod` package.

Does not own:

- Production source. Tests reach in via `Link`; they never own the file.
- Game runtime tests. The mod is exercised manually via tModLoader's in-game build + reload.

## Current Implemented Reality

### Project shape (`Tests/PerformanceProfiler.Tests.csproj`)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>PerformanceProfiler.Tests</RootNamespace>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <!-- LiteDB from the mod's lib folder, so the persistence tests exercise
         the same DLL that ships inside the .tmod. -->
    <Reference Include="LiteDB"><HintPath>..\lib\LiteDB.dll</HintPath></Reference>
  </ItemGroup>
  <ItemGroup>
    <!-- Test files in this folder (the Compile Include="**\*.cs" glob; includes
         the folded-in HookInstallRetentionDiagnostics.cs — pure System/xUnit). -->
    <Compile Include="**\*.cs" Exclude="bin\**;obj\**" />
  </ItemGroup>
  <ItemGroup>
    <!-- Pure-logic source lifted in by Compile Include + Link. Each must have
         zero Terraria.ModLoader references. The Events bitsets/flags stayed in
         Profiling/Events/ and are dragged in because TickFrame carries an
         EventContext field. The stream-shaped classes moved Profiling/ -> Data/
         in v0.10/v0.11; the insights module moved to a top-level Insights/ in the
         v0.13-v0.22 rework. The paths below point at those CURRENT locations. -->
    <Compile Include="..\Profiling\RingBuffer.cs"            Link="Source\RingBuffer.cs" />
    <Compile Include="..\Profiling\TickFrame.cs"             Link="Source\TickFrame.cs" />
    <Compile Include="..\Profiling\Time.cs"                  Link="Source\Time.cs" />
    <Compile Include="..\Profiling\EnumStringTable.cs"       Link="Source\EnumStringTable.cs" />
    <Compile Include="..\Profiling\Pools\*.cs"               Link="Source\Pools\%(Filename)%(Extension)" />
    <Compile Include="..\Profiling\Util\BoolIndex.cs"        Link="Source\Util\BoolIndex.cs" />
    <Compile Include="..\Profiling\Events\*.cs"              Link="Source\Events\%(Filename)%(Extension)" />
    <!-- Moved to Data/ in v0.11. -->
    <Compile Include="..\Data\Aggregators\PerModSample.cs"      Link="Source\PerModSample.cs" />
    <Compile Include="..\Data\Aggregators\PerModAttribution.cs" Link="Source\PerModAttribution.cs" />
    <Compile Include="..\Data\Stats\Baseline.cs"               Link="Source\Baseline.cs" />
    <Compile Include="..\Data\Detectors\StallDetector.cs"      Link="Source\StallDetector.cs" />
    <Compile Include="..\Data\Contracts\RolloutContracts.cs"   Link="Source\Contracts\RolloutContracts.cs" />
    <!-- Top-level Insights/ module (the v0.13-v0.22 rework): the canonical
         Insight type + store + scorer, plus Shared primitives, reference frames,
         drivers, and the cross-session store. -->
    <Compile Include="..\Insights\Insight.cs"                  Link="Source\Insights\Insight.cs" />
    <Compile Include="..\Insights\InsightStore.cs"             Link="Source\Insights\InsightStore.cs" />
    <Compile Include="..\Insights\RankingScorer.cs"            Link="Source\Insights\RankingScorer.cs" />
    <Compile Include="..\Insights\Shared\*.cs"                 Link="Source\Insights\Shared\%(Filename)%(Extension)" />
    <Compile Include="..\Insights\ReferenceFrames\*.cs"        Link="Source\Insights\ReferenceFrames\%(Filename)%(Extension)" />
    <Compile Include="..\Insights\Contracts\*.cs"              Link="Source\Insights\Contracts\%(Filename)%(Extension)" />
    <Compile Include="..\Insights\Drivers\Drivers.cs"          Link="Source\Insights\Drivers\Drivers.cs" />
    <!-- Persistence sources reference LiteDB only; the per-collection stream
         classes moved to Data/Streams/ in v0.11. Lifted whole-folder with the
         game-runtime-touching files Compile-Removed (ProfilerPaths,
         SessionRecorder, DbReadModel, etc). -->
    <Compile Include="..\Profiling\Persistence\*.cs"           Link="..." />
    <Compile Include="..\Profiling\Persistence\Records\*.cs"   Link="..." />
    <Compile Include="..\Data\Streams\*.cs"                    Link="..." />
    <Compile Remove="..\Profiling\Persistence\ProfilerPaths.cs" /> <!-- + ~10 other game-touching removes -->
  </ItemGroup>
</Project>
```

(See the actual `Tests/PerformanceProfiler.Tests.csproj` for the full `Compile Remove` list — `ProfilerPaths`, `LegacyJsonImporter`, `ProfilerCompactCommand`, `ModlistFingerprint`, `DbReadModel`, `SessionRecorder`, `TickDownsampler`, `ContextTransitionWatcher`, `WorldSnapshotter`, `PlayerDeathDetector`, `SessionSummaryLogger`, the `Commands\*`/`Interactions\*` folders, and `ProfilerFocusProbe`.)

Key choices:

- `EnableDefaultCompileItems = false` so the parent `.cs` files don't auto-include from the parent directory.
- `Compile Include` with `Link="Source\..."` pulls the pure-logic files into the test project as if they lived there, but **without** copying them. The link path keeps the IDE's solution explorer tidy.
- **No `<ProjectReference>` to the mod project.** A ProjectReference would drag tModLoader assemblies into the test runner. The Compile-Include shape keeps the runner clean.
- A direct `<Reference>` to `..\lib\LiteDB.dll` (the same assembly the `.tmod` ships) so the persistence round-trip and benchmark fixtures exercise the production DB layer; the game-runtime-touching persistence files are `Compile Remove`d so only the LiteDB-only sources lift cleanly.

### Main mod project exclusion

`PerformanceProfiler.csproj` carries (per `git diff` against the pre-audit revision):

```xml
<Compile Remove="Tests\**\*.cs" />
```

Without this, the main mod build would pick up the `*Tests.cs` files (xUnit references would break the `.tmod` build).

### `.tmod` package exclusion

`build.txt`'s `buildIgnore` comma-list carries `Tests\*` (added in commit `14fac59`; it now also lists `tools\*`, `context\*`, `*.md`, etc.). The `.tmod` packager skips the `Tests/` folder so the shipped Workshop artefact has zero test bytes.

### Adding a new test fixture

Per the docstring comment in `Tests/PerformanceProfiler.Tests.csproj`:

1. Add the test file under `Tests/`. The `Compile Include="**\*.cs"` glob picks it up automatically.
2. **If** the test needs a production source file not yet linked, add a `Compile Include="..\<dir>\X.cs" Link="Source\X.cs"` entry — the pure-logic sources live under `Profiling/` (RingBuffer, TickFrame, Time, Pools, Events), `Data/` (PerModAttribution, Baseline, StallDetector, Streams, Contracts) after the v0.11 move, and the **top-level `Insights/`** module (Insight, InsightStore, RankingScorer, Shared/*, ReferenceFrames/*, Contracts/*, Drivers) after the v0.13-v0.22 rework.
3. **Verify** the linked file has zero `Terraria.ModLoader` references. Otherwise the runner will fail to load.
4. Run `dotnet test Tests/PerformanceProfiler.Tests.csproj`.

The "verify zero tModLoader references" step is the load-bearing one. The whole isolation strategy depends on it; a stray `using Terraria.ModLoader;` in a linked file would drag the runtime in.

### Current fixtures

| File | What it pins |
|------|--------------|
| `RankingScorerTests.cs` | A 90% share now outranks a 40% share (the audit's insights-engine #1 finding). The 10× ratio still beats the 2× ratio. Ratios below 1 collapse to zero magnitude (unchanged knee). |
| `InsightStoreTests.cs` | An untested record (`PValueAdjusted = 1`) never promotes past Low regardless of confirmation count. A tested record (`PValueAdjusted = 0.01`) reaches High at 4 confirmations. Submit dedup. |
| `RingBufferTests.cs` | Wrap-around semantics that the 30s history and 50-window spike retainer depend on. |
| `BaselineTests.cs` | Per-session baseline service used by the relative spike threshold (added in commit `cdbe762`, alongside the spike threshold refactor that derived from a shared baseline rather than a hard-coded 5 ms floor). |
| `StallDetectorTests.cs` | Stall-window detection over the per-tick stream. |
| `StallClassifierTests.cs` | Stall cause classification (the stall-attribution arsenal landed in v0.4). |
| `TimeTests.cs` | The `Stopwatch`-based `Time.UnixMsNow()` helper (v0.6 Phase α). |
| `PoolsTests.cs` | The `RowPool` / `ListPool` object pools — the per-tick zero-alloc contract. |
| `BoolIndexTests.cs` | The `BoolIndex` bitset helper. |
| `Persistence/PersistenceRoundTripTests.cs` | LiteDB write → read fidelity across the streams (writer thread + records + streams). |
| `Persistence/PersistenceBenchmarkTests.cs` | LiteDB write throughput / latency under the persistence layer. |
| `Insights/SharedPrimitivesTests.cs` | The Wave-1 `Insights/Shared/` primitives (`ModMetrics`, `Shares`, `ModNames`) — pure math over the `RolloutContracts` entry types. |
| `Insights/ReferenceFrameTests.cs` | The Wave-3 reference-frame substrate (`Stats`, `ContextBaseline` per-context accumulator). |
| `Insights/TemporalBaselineTests.cs` | The Wave-5 family-B early/late temporal baseline + driver contracts. |
| `Insights/CrossSessionStoreTests.cs` | The Wave-6 LiteDB round-trip of the per-context baselines. |
| `HookInstallRetentionDiagnostics.cs` | A diagnostic fixture (not a regression pin): proves the `ProfilerSelfHealth` hook-install RAM measurement methodology conflates retained state with uncollected transient garbage, using a synthetic allocate-then-release. Pure System/xUnit, no mod deps. |

Sixteen `.cs` files carry test methods (the 15 `*Tests.cs` fixtures above plus the `HookInstallRetentionDiagnostics.cs` diagnostic); a 17th, `_TestNamespaceStubs.cs`, carries no tests (it sets `DisableTestParallelization` and provides empty namespace stubs so the lifted persistence sources' header `using`s resolve in the runtime-free harness). The csproj docstring and `_TestNamespaceStubs.cs` describe the suite as **~70 tests, sub-second**; re-run `dotnet test` for the exact current pass count. The earlier "10/10 in ~16 ms" figure (commit `14fac59`) and the "108 tests as of `0.19.0`" figure in `extensive-testing-infrastructure.md` are both point-in-time snapshots — `dotnet test` is the live count.

## Key Interfaces / Data Flow

```
dotnet test Tests/PerformanceProfiler.Tests.csproj
   ↓
Microsoft.NET.Test.Sdk discovers xUnit fixtures
   ↓
xUnit runner instantiates each test class
   ↓
Test invokes production-source methods on Link'd files:
   - new RingBuffer<int>(...)
   - new InsightStore(...)
   - RankingScorer.Score(...)
   ↓
Asserts via xUnit's Assert.Equal / Assert.True / ...
```

The runner never starts Terraria, never loads `tModLoader.dll`, never touches a `Mod`. Pure logic only.

## Implemented Outputs / Artifacts

| Path | What |
|------|------|
| `Tests/bin/...` / `Tests/obj/...` | Build outputs (gitignored) |
| `dotnet test` stdout | xUnit test results |

## Known Issues / Active Risks

- **The csproj `Compile Include` link paths are CURRENT (re-verified v0.22.0).** An earlier revision of this doc flagged the linked entries as stale `..\Profiling\...` paths left behind by the v0.11 `Data/` move. That drift has since been repaired and the doc note was itself stale; the **current** `Tests/PerformanceProfiler.Tests.csproj` points every entry at its real location, and all resolve on disk (verified by `find`):
  - `..\Data\Aggregators\PerModSample.cs`, `..\Data\Aggregators\PerModAttribution.cs` (was `..\Profiling\`)
  - `..\Data\Stats\Baseline.cs`, `..\Data\Detectors\StallDetector.cs` (was `..\Profiling\`)
  - `..\Data\Streams\*.cs`, `..\Data\Contracts\RolloutContracts.cs`
  - `..\Insights\Insight.cs`, `..\Insights\InsightStore.cs`, `..\Insights\RankingScorer.cs`, and `..\Insights\Shared\*`, `..\Insights\ReferenceFrames\*`, `..\Insights\Contracts\*`, `..\Insights\Drivers\Drivers.cs` — the v0.13-v0.22 rework moved insights to a **top-level `Insights/`** module, NOT `Data/Detectors/Insights/` (that path does not exist; an earlier draft of this note guessed it). The old type name `InsightRecord` is now `Insight.cs`.
  The Events bitsets/flags + the `Time`/`Pools`/`BoolIndex`/`EnumStringTable`/`RingBuffer`/`TickFrame` helpers correctly stayed under `Profiling/`. The csproj has been changed many commits past `b2f023d` (e.g. through the Insights waves up to `398da95`), so the "last csproj change was `b2f023d`" claim was also stale. Net: `dotnet test` compiles against the current tree; no path repair outstanding.
- **The "no tModLoader references in linked files" rule is not enforced.** A future addition could accidentally pull `using Terraria.ModLoader;` into a Link'd file; the test runner would then fail to compile. There is no lint or hook today; the docstring is the only protection.
- **`build.txt`'s `buildIgnore` is the only thing keeping `Tests/*` out of the `.tmod`.** If a contributor edits `build.txt` and drops the entry, the next package would carry test bytes (and the test framework references) into the Workshop release. The `.csproj` `<Compile Remove>` would still keep the test `.cs` out of the mod DLL, so the runtime damage is bounded to size bloat.
- **The link path duplicates each file's namespace in two compilation units.** This is fine in practice (the production assembly and the test assembly are separate), but it means a code-mod tool that operates on both might double-edit. Today the only such tool is `git`, which handles it correctly.

## Partial / In Progress

- **Persisted-schema snapshot test** is deferred from the 2026-05-20 audit. The legacy JSON `SessionLogWriter` (whose schema the original deferral targeted) was deleted in v0.3; the equivalent test today would assert the LiteDB `SessionRecorder` / stream record shapes against a fixture. The `Persistence/PersistenceRoundTripTests.cs` fixture covers write/read fidelity but not a frozen-schema snapshot.

## Planned / Missing / Likely Changes

- **More fixtures as pure-logic regressions surface.** The convention is "one test fixture per finding the audit calls Medium or higher and that can be expressed in pure logic."
- **`MetricCollector` pure-logic shim.** If the per-mod arithmetic ever gets a regression worth pinning, a slim shim that excludes the per-tick timing read could be linked in.

## Durable Notes / Discarded Approaches

- **`ProjectReference` was the first attempt.** It dragged `Terraria.ModLoader.dll` and `FNA.dll` into the test runner, which then failed to find the OpenGL backend, which then crashed the runner. The Link-only shape is the workaround.
- **A separate Tests solution was considered.** Rejected: one `.csproj` is enough and the main mod project's `<Compile Remove>` keeps the test files out of the mod DLL.
- **The `Tests/` folder lives inside the mod source directory** rather than as a sibling. This keeps `git mv` operations clean and matches the README's "everything in one ModSources folder" mental model.

## Obsolete / No Longer Relevant

Nothing.

## Cross-references

- `systems/dashboard-audit-harness.md` — the **other** testing axis. This file (L1) proves pure-logic correctness (ranking, insight promotion, ring buffers, persistence math) on synthetic input with no browser and no game; the audit harness (L4/L6/L8) proves the dashboard's layout, interaction, and visual quality by driving the real page with Playwright, with no game and no `.cs` build. The two are independent and neither imports the other; together they are the project's testing surface short of the irreducible in-game L7 check. Both are `buildIgnore`'d (`Tests\*` / `tools\*`).
- `notes/conventions.md` — convention #14 on commit-message tagging (`CHA round N:` prefix).
- `plans/code-health-audit/build-and-tests.md` — audit finding that drove the harness creation.
- `systems/insights-engine.md` — the subsystem most-pinned by tests today.
- `systems/metric-collection.md` — `RingBuffer<TickFrame>` is the consumer of `RingBuffer<T>`.

## The 2026-07-07 rings (S27)

- **Ring 1 — scenario simulation (`Tests/Simulation/`).** `ScenarioRunner`
  drives the REAL classes (Baseline, StallDetector, SpikeDetector +
  PerTickAttributionRing, `KpiCalculator.ComputeCore`, HeatmapFold,
  RealtimeSpeed folds, the insight cores) with scripted sessions.
  MetricCollector cannot link (tML-transitive via SelfHealth), so the runner
  mirrors EndTick's documented contract (suspend guard, folds) with
  move-together pointer comments; `KpiCalculator` split into a pure core
  (linked) + `KpiCalculator.Live.cs` (collector overload, unlinked). Scenario
  library: healthy60, slowmo30 (the live session's exact shape), altTabbed
  (the 25/41/45s gaps), spiky, warming. The honesty battery pins the X1/X2/X3
  classes as permanent assertions.
- **Ring 2 — store round-trips (`StoreRoundTripPins`).** Real temp-file LiteDB
  exercising production predicate SHAPES (the C1 indexer class only bites
  against the live translation layer), the ModVersions BSON round-trip incl. a
  legacy pre-v2 document, and the X3 cause-split verified in-store.
- **Diagnostics benches**: PhaseLaneBench (best-of-5 numbers printed, loose
  ceiling asserted — tight timing pins flake); the forced-GC repeatability pin
  is min-of-3-pairs (order-independence after a live flake).
- **run_all.sh**: dotnet test + compile gate (loader-lock noise ignored) +
  harness assert, one command, loud skip if the venv is absent.
- **Suite count: 205.** The linked-source rule is the enforcement: a linked
  file gaining a Terraria using breaks the compile.
