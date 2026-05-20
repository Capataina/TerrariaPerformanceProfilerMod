# Test Harness

*Maturity: working · Stability: stable.*

## Scope / Purpose

A non-shipping xUnit test project that pins the pure-logic surfaces the 2026-05-20 audit identified as load-bearing: insights ranking, insights confidence promotion, and ring-buffer wrap-around. It does **not** exercise any code that touches tModLoader, the game runtime, or any IL emission — those are tested manually via the in-game build cycle.

The discipline is "every pure-logic regression that would break behaviour silently gets a test before the production code is allowed to change shape."

## Boundaries / Ownership

Files: `Tests/PerformanceProfiler.Tests.csproj`, `Tests/RankingScorerTests.cs`, `Tests/InsightStoreTests.cs`, `Tests/RingBufferTests.cs`.

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
  </ItemGroup>
  <ItemGroup>
    <Compile Include="**\*.cs" Exclude="bin\**;obj\**" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="..\Profiling\RingBuffer.cs"                     Link="Source\RingBuffer.cs" />
    <Compile Include="..\Profiling\Insights\InsightRecord.cs"         Link="Source\Insights\InsightRecord.cs" />
    <Compile Include="..\Profiling\Insights\InsightStore.cs"          Link="Source\Insights\InsightStore.cs" />
    <Compile Include="..\Profiling\Insights\RankingScorer.cs"         Link="Source\Insights\RankingScorer.cs" />
  </ItemGroup>
</Project>
```

Key choices:

- `EnableDefaultCompileItems = false` so the parent `.cs` files don't auto-include from the parent directory.
- `Compile Include` with `Link="Source\..."` pulls the pure-logic files into the test project as if they lived there, but **without** copying them. The link path keeps the IDE's solution explorer tidy.
- **No `<ProjectReference>` to the mod project.** A ProjectReference would drag tModLoader assemblies into the test runner. The Compile-Include shape keeps the runner clean.

### Main mod project exclusion

`PerformanceProfiler.csproj` carries (per `git diff` against the pre-audit revision):

```xml
<Compile Remove="Tests\**\*.cs" />
```

Without this, the main mod build would pick up the `*Tests.cs` files (xUnit references would break the `.tmod` build).

### `.tmod` package exclusion

`build.txt` carries `buildIgnore=Tests/*` (added in commit `14fac59`). The `.tmod` packager skips the `Tests/` folder so the shipped Workshop artefact has zero test bytes.

### Adding a new test fixture

Per the docstring comment in `Tests/PerformanceProfiler.Tests.csproj`:

1. Add the test file under `Tests/`. The `Compile Include="**\*.cs"` glob picks it up automatically.
2. **If** the test needs a production source file not yet linked, add a `Compile Include="..\Profiling\X.cs" Link="Source\X.cs"` entry.
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

10/10 passing in ~16 ms as of commit `14fac59`; `BaselineTests.cs` added in `cdbe762` brings the total higher (re-run `dotnet test` for the current count).

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

- **The "no tModLoader references in linked files" rule is not enforced.** A future addition could accidentally pull `using Terraria.ModLoader;` into a Link'd file; the test runner would then fail to compile. There is no lint or hook today; the docstring is the only protection.
- **`build.txt`'s `buildIgnore` is the only thing keeping `Tests/*` out of the `.tmod`.** If a contributor edits `build.txt` and drops the entry, the next package would carry test bytes (and the test framework references) into the Workshop release. The `.csproj` `<Compile Remove>` would still keep the test `.cs` out of the mod DLL, so the runtime damage is bounded to size bloat.
- **The link path duplicates each file's namespace in two compilation units.** This is fine in practice (the production assembly and the test assembly are separate), but it means a code-mod tool that operates on both might double-edit. Today the only such tool is `git`, which handles it correctly.

## Partial / In Progress

- **`SessionLogWriter` schema snapshot test** is deferred from the 2026-05-20 audit. It would link `SessionReportBuilder` (once that split lands) and assert the v4 JSON shape against a fixture.

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

- `notes/conventions.md` — convention #14 on commit-message tagging (`CHA round N:` prefix).
- `plans/code-health-audit/build-and-tests.md` — audit finding that drove the harness creation.
- `systems/insights-engine.md` — the subsystem most-pinned by tests today.
- `systems/metric-collection.md` — `RingBuffer<TickFrame>` is the consumer of `RingBuffer<T>`.
