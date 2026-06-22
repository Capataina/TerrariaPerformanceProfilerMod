# Code Health Audit — Pass 1 Checkpoint

**Date:** 2026-06-22
**Scope:** full repository, with the deepest Pass-2 dive on the **hook-install RAM path** (the live investigation's priority), plus a repository-wide sweep for dead code, build/test health, duplicate-using lint, documentation rot, and modularisation.
**Supersedes:** the 2026-05-20/21 audit in this folder (regeneration against current code).

---

## 1. Stack and architecture

| Property | Value |
|----------|-------|
| Language / runtime | C# on .NET 8 (tModLoader 1.4.4 pinned) |
| Build | `dotnet msbuild` (mod), `dotnet test Tests/…` (pure-logic harness) |
| Key deps | `MonoMod.RuntimeDetour 25.3.2`, `MonoMod.Utils 25.0.10` (transitive via tML), `LiteDB 5.0.21` (bundled) |
| Manifest version | `build.txt` → `version = 0.12` |
| Compiled source | `Profiling/` (78 files, 10,496 LOC), `Data/` (90 files, 14,120 LOC), `Web/` (52 files, 8,436 LOC) |
| Archived (NOT compiled) | `UI/` (29 files, 6,069 LOC) — every file `#if false`-guarded since v0.9.0; intentionally retained per README + 2026-05-21 decision |

The mod measures per-mod CPU/allocation cost by IL-injecting timing probes into every
mod hook override. Two backends exist; the **ILHook backend is the production default**
(`HookBackend.Mode = ILHook`). The delegate backend (`HookInterceptor.cs`) is a dormant
archived fallback.

## 2. Systems identified (by Pass-2 priority)

| Priority | System | Files | Why prioritised |
|----------|--------|-------|-----------------|
| **1 (deepest)** | Hook-install RAM path | `ILHookInterceptor.cs` (575), `HookBackend.cs` (115), `ProfilerSelfHealth.cs` (258), `ProbeStack.cs` (205), `HookSurfaceCache.cs` (82), `ProfilerSystem.PostSetupContent` | The live investigation: ~9 GB install delta on a 100-mod / 152,310-hook stack; tML attributes ~8.2 GB to the mod. Suspected MonoMod Cecil/DMD retention + a self-health measurement gap. |
| 2 | Self-health measurement | `ProfilerSelfHealth.cs` | `MarkInstallEnd` samples `GetTotalMemory(false)` with no collection → conflates retained live state with uncollected transient install garbage. |
| 3 | Build / test infrastructure | `Tests/PerformanceProfiler.Tests.csproj` + linked sources | Baseline check (below) found the test suite does not build. |
| 4 | Duplicate-using lint | `Data/Streams/*`, `Data/Aggregators/Segments/*` | CS0105 warnings; mechanical free cleanup. |
| 5 | Documentation rot | `conventions.md`, `HookBackend.cs`, `README.md` | Comments/docs contradicting current code. |

The Data/, Web/, Profiling/persistence, and insights systems were audited by the
2026-05-20/21 multi-agent pass (~73 BUG-class findings, critical slice landed). This run
does **not** re-audit those at depth; it focuses on the priority RAM area and the
repository-wide hygiene/build/doc sweep, which the prior pass did not fully cover (the
prior pass declared the test harness non-existent — it exists but is broken).

## 3. Test-suite baseline (Pass 1 step 5) — FAILING

Command: `dotnet test Tests/PerformanceProfiler.Tests.csproj`

Result: **BUILD FAILS — does not compile.** 7× `CS2001 Source file could not be found`
reported (compiler stops early); a full path check shows **all 24 `Compile Include`
paths in the test csproj are stale**. The pure-logic source files moved from `Profiling/`
to `Data/` during the v0.10/v0.12 reorganisation, but the test project's `<Compile Include>`
list was never updated.

```
CS2001  Profiling/PerModSample.cs        (now Data/Aggregators/PerModSample.cs)
CS2001  Profiling/PerModAttribution.cs   (now Data/Aggregators/PerModAttribution.cs)
CS2001  Profiling/Baseline.cs            (now Data/Stats/Baseline.cs)
CS2001  Profiling/StallDetector.cs       (now Data/Detectors/StallDetector.cs)
CS2001  Profiling/Insights/InsightStore.cs    (now Data/Detectors/Insights/InsightStore.cs)
CS2001  Profiling/Insights/RankingScorer.cs   (now Data/Detectors/Insights/RankingScorer.cs)
CS2001  Profiling/Insights/InsightRecord.cs   (now Data/Detectors/Insights/InsightRecord.cs)
… and 17 more Compile Include paths that also no longer resolve.
```

**This is recorded immediately as a Known Issues finding** (F4, `build-and-tests.md`):
no working test suite means the audit's recommendations cannot lean on the existing suite
as a regression net, and the decisions log's repeated "63/63 passing" claims are no longer
true against the current tree.

The diagnostic test this audit writes (`Tests/HookInstallRetentionDiagnostics.cs`) is
therefore added with a self-contained build path that does not depend on the broken
`Compile Include` list (it links only stable, still-existing pure-logic files / no mod
sources), so the diagnostic runs even while the main test list is broken.

## 4. Broad-sweep file-size scan (modularisation candidate list)

C# threshold = 550 LOC (SKILL.md table). Candidates = files ≥ 550 LOC **OR** top decile.
Top decile of 298 compiled `.cs` files ≈ top 30 files. Archived `UI/` files are listed but
flagged not-applicable (not compiled).

### Compiled files ≥ 550 LOC (hard threshold)

| File | LOC | Qualifying reason |
|------|-----|-------------------|
| `Profiling/HookInterceptor.cs` | 1227 | ≥550 + top-decile — but dormant archived backend |
| `Data/Streams/SessionRecorder.cs` | 726 | ≥550 + top-decile |
| `Profiling/ProfilerSystem.cs` | 699 | ≥550 + top-decile |
| `Profiling/MetricCollector.cs` | 586 | ≥550 + top-decile |
| `Profiling/ILHookInterceptor.cs` | 575 | ≥550 + top-decile (priority system) |
| `Data/Detectors/StallDetector.cs` | 574 | ≥550 + top-decile |
| `Data/Aggregators/Segments/SegmentDetector.cs` | 551 | ≥550 + top-decile |
| `Web/Assets/Js/Js.Lag.cs` | 738 | ≥550 — generated-asset (JS-in-C#-string) |
| `Web/Assets/Js/Js.Insights.cs` | 702 | ≥550 — generated-asset |
| `Web/Assets/Js/Js.Timeline.cs` | 698 | ≥550 — generated-asset |

### Archived UI files ≥ 550 LOC (not compiled, `#if false`)

| File | LOC | Qualifying reason |
|------|-----|-------------------|
| `UI/Overlay/Tabs/OverviewTab.cs` | 786 | ≥550 but archived |
| `UI/Overlay/OverlayPanel.cs` | 679 | ≥550 but archived |

### Top-decile compiled files in the 400–550 band (also candidates)

| File | LOC |
|------|-----|
| `Data/Contracts/RolloutContracts.cs` | 544 |
| `Profiling/Persistence/ProfilerDatabase.cs` | 493 |
| `Profiling/Persistence/Interactions/InteractionPlayer.cs` | 493 |
| `UI/Overlay/Tabs/TreeTab.cs` | 474 (archived) |
| `Web/Assets/Css/Css.Timeline.cs` | 413 (generated-asset) |
| `Data/Aggregators/PerModAttribution.cs` | 413 |
| `Web/DashboardRouter.Timeline.cs` | 411 |

Per-file verdicts are recorded in `PASS-2-SYSTEMS-AUDITED.md` (Modularisation evaluation
floor). Summary: most are cohesive single-responsibility files or generated assets;
`HookInterceptor.cs` (1227, dormant) is the one genuine split candidate but is archived
fallback, so `leave-as-is` with a note rather than `split-recommended`.

## 5. Known issues already surfaced from context / baseline

1. **Test suite does not build** (§3) → F4.
2. **README claims ~36 KB/hook; live investigation reports ~60 KB/hook; code baseline pins 36 KB** — drift between the public claim, the code constant, and reality → F3.
3. **`_installedHooks` retention as process-lifetime dead weight** — already flagged a "watch item" in `context/systems/hook-instrumentation.md:211` and "Mono.Cecil retained state is the suspected dominant cost" in `decisions.md:201`; the prior pass did not quantify or root-cause it. This audit resolves it with decompiled evidence → F1.

## 6. Pass-2 prioritisation rationale

The hook-install RAM path is ordered first and given the deepest treatment because (a) it
is the live investigation's explicit priority, (b) the cost (~8.2 GB, more than every
other mod combined) dwarfs every other finding's magnitude, and (c) it had a standing
unresolved hypothesis ("Mono.Cecil retained state") that direct decompilation of the
shipped binaries can settle definitively. Build/test health is ordered next because a
broken suite undermines the safety footing of every other recommendation. The remaining
sweep items are mechanical / documentation and are batched into `cross-cutting.md`.
