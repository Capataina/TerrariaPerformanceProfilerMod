# Build & Test Infrastructure — Code Health Findings

**Systems covered:** `Tests/PerformanceProfiler.Tests.csproj` and its linked pure-logic sources.
**Finding count:** 2 certain (F4 high, F5 medium).

> The Pass-1 baseline check found the pure-logic test suite **does not build**. Every
> `<Compile Include>` path in the test csproj is stale — the linked sources moved from
> `Profiling/` to `Data/` during the v0.10/v0.12 reorganisation and the test project was
> never updated. This undermines the safety footing of every audit recommendation and
> falsifies the decisions log's repeated "N/N passing" claims against the current tree.

---

## F4 — Pure-logic test suite does not compile (all 24 linked-source paths stale) {#f4}

- [ ] Repair the `<Compile Include>` paths in `Tests/PerformanceProfiler.Tests.csproj` so they point at the post-reorganisation file locations (see F5 for the path map), restoring the test suite to a buildable, runnable state.

**Category:** Known Issues and Active Risks
**Severity:** High
**Effort:** Small
**Behavioural Impact:** None on the shipped mod — the test project is non-shipping (`buildIgnore = Tests\*`, and `PerformanceProfiler.csproj` removes `Tests/**`). The impact is entirely on the project's verification capability.

**Location:**
- `Tests/PerformanceProfiler.Tests.csproj` — the `<ItemGroup>` of `<Compile Include="..\Profiling\…" Link="…">` entries (24 entries, all stale).

**Current State:**
`dotnet test Tests/PerformanceProfiler.Tests.csproj` fails at compile with `CS2001
Source file could not be found`. The compiler reports 7 of them before stopping; a full
path check shows **all 24** `<Compile Include>` paths no longer resolve. During the
v0.10 unified-data-pipeline and v0.12 tab-rework reorganisations, the pure-logic sources
were moved out of `Profiling/` into `Data/` (`StallDetector` → `Data/Detectors/`,
`Baseline` → `Data/Stats/`, `PerModAttribution`/`PerModSample` → `Data/Aggregators/`,
the Insights trio → `Data/Detectors/Insights/`), but the test project's linked-source
list still points at the old `Profiling/…` paths. The test fixtures themselves
(`StallDetectorTests.cs`, `BaselineTests.cs`, `RankingScorerTests.cs`, etc.) are intact;
only the linked production sources they exercise are unreachable.

The decisions log states "63/63 passing" (2026-05-21) and earlier "44/44", "52/52",
"54/54" — none of which can be true now, because the suite has not compiled since the
reorganisation moved the files.

**Proposed Change:**
Update each `<Compile Include>` path to the file's current location (full map in F5).
The `Link="Source\…"` attributes can stay as-is (they only control the in-project virtual
folder). No fixture code changes; no production-source changes. Verify with `dotnet test
Tests/PerformanceProfiler.Tests.csproj` returning a green run.

Note: the audit's own diagnostic (`Tests/HookInstallRetentionDiagnostics.cs`) was added
with a **separate self-contained csproj** (`Tests/Diagnostics/HookInstallRetentionDiagnostics.csproj`)
precisely so it runs while this main project is broken. Once F4 is fixed, that file can be
folded into the main project's Compile list and the stub csproj deleted.

**Justification:**
Direct evidence — the baseline `dotnet test` run is the proof (CS2001 ×7 reported, 24/24
paths confirmed missing via a `find` existence check during Pass 1). This is exactly the
"broken or missing test infrastructure is itself a finding" case the skill's Pass-1
baseline step exists to catch.

**Expected Benefit:**
Restores the regression net the whole codebase's safety leans on. Re-validates the
pure-logic detectors (StallDetector classification, Baseline histogram, RankingScorer
normalisation, InsightStore promotion, RingBuffer, Pools, BoolIndex, Time) that the
decisions log claims are tested but currently are not. Makes "N/N passing" claims true
again.

**Impact Assessment:**
None on shipped behaviour — the test project never ships and is excluded from both the
`dotnet msbuild` mod build and the in-game build. Fixing the paths only affects whether
`dotnet test` compiles and runs.

---

## F5 — Stale-to-current path map for the test project's linked sources {#f5}

- [ ] Apply this exact path map when repairing F4.

**Category:** Known Issues and Active Risks
**Severity:** Medium
**Effort:** Trivial
**Behavioural Impact:** None (same as F4 — non-shipping test project).

**Location:**
- `Tests/PerformanceProfiler.Tests.csproj`.

**Current State / Proposed Change:**
The audit resolved the current location of each linked source via `find Profiling Data
-name '<file>.cs'`. Files that **moved** (path must change):

| Stale `<Compile Include>` path | Current location |
|--------------------------------|------------------|
| `..\Profiling\PerModSample.cs` | `..\Data\Aggregators\PerModSample.cs` |
| `..\Profiling\PerModAttribution.cs` | `..\Data\Aggregators\PerModAttribution.cs` |
| `..\Profiling\Baseline.cs` | `..\Data\Stats\Baseline.cs` |
| `..\Profiling\StallDetector.cs` | `..\Data\Detectors\StallDetector.cs` |
| `..\Profiling\Insights\InsightRecord.cs` | `..\Data\Detectors\Insights\InsightRecord.cs` |
| `..\Profiling\Insights\InsightStore.cs` | `..\Data\Detectors\Insights\InsightStore.cs` |
| `..\Profiling\Insights\RankingScorer.cs` | `..\Data\Detectors\Insights\RankingScorer.cs` |

Files that **did not move** (path still valid — verify each still resolves when repairing):
`..\Profiling\RingBuffer.cs`, `..\Profiling\TickFrame.cs`, `..\Profiling\Time.cs`,
`..\Profiling\EnumStringTable.cs`, `..\Profiling\Events\BiomeBitset.cs`,
`..\Profiling\Events\WeatherFlags.cs`, `..\Profiling\Events\InvasionId.cs`,
`..\Profiling\Events\GameMode.cs`, `..\Profiling\Events\BossSlotArray.cs`,
`..\Profiling\Events\EventContext.cs`, `..\Profiling\Pools\IPoolReset.cs`,
`..\Profiling\Pools\RowPool.cs`, `..\Profiling\Pools\ListPool.cs`,
`..\Profiling\Util\BoolIndex.cs`.

Also present in the csproj are glob includes (`..\Profiling\Persistence\*.cs`,
`..\Profiling\Persistence\Records\*.cs`, `..\Profiling\Persistence\Streams\*.cs`) — these
must be re-checked, because some persistence files moved (e.g. `SessionRecorder.cs` is now
`Data/Streams/SessionRecorder.cs`, and the `Profiling/Persistence/Streams/` folder no
longer exists). The repairing engineer should re-run a `find` to confirm each glob still
matches before committing.

**Justification:**
Direct evidence — every "current location" cell was produced by `find Profiling Data
-name '<file>.cs'` during Pass 1; every "stale path" cell was confirmed non-resolving by
a per-path existence check.

**Expected Benefit:**
Turns F4 from "the paths are wrong" into a mechanical, copy-pasteable repair with no
re-derivation needed.

**Impact Assessment:**
None — documentation of a mechanical fix to a non-shipping project.

---

## Resolution (2026-06-22) — F4 was deeper than a path swap

Applying the F5 map was necessary but not sufficient. Two further entanglements
the v0.11 move introduced surfaced only at compile time:

1. **Blanket `Data.*` using-headers.** Nearly every lifted source file gained a
   uniform header importing `Data.Collectors`, `Data.Streams`, and
   `Data.Aggregators.Segments` (inert in those files — no type from them is
   referenced). The test project does not compile the Collectors (Terraria-coupled
   via `MetricCollector`) or the Segments detector/store, so those header usings
   failed with CS0234 across the whole lifted set.
2. **`ProfilerDatabase` → `StreamRegistry` coupling.** `ProfilerDatabase` now
   references `StreamRegistry`, which moved to `Data/Streams/`, so that folder had
   to be linked (it was not in the original csproj's Profiling-only globs).

**Fix applied:** corrected the F5 paths; re-linked `..\Data\Streams\*.cs` (minus
`SessionRecorder`, which is Terraria-coupled); added `Tests/_TestNamespaceStubs.cs`
declaring empty `Data.Collectors` / `Data.Aggregators.Segments` namespaces so the
inert header usings resolve without pulling Terraria; updated the fixtures' usings
for the moved namespaces (`Profiling.Insights` → `Data.Detectors.Insights`, etc.);
folded the diagnostic into the main project and deleted the `Tests/Diagnostics`
stub. A `BsonMapper.Global` race between the two persistence test classes (xUnit
parallelises by default) was fixed test-only with
`[assembly: CollectionBehavior(DisableTestParallelization = true)]`.

**Result:** `dotnet test` → **69 passed, 0 failed**. The 4 transient benchmark
failures were the global-mapper race, not a production bug (the round-trip test and
in-game DB both open cleanly).
