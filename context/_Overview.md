# Performance Profiler — Context Folder

> Repository implementation memory. Read this first.

## What this folder is

The maintained working memory for the Performance Profiler mod. A reader (engineer or agent) working only from `context/` should be able to understand the whole project: what every subsystem owns, where it plugs into tModLoader, what the open risks are, what was tried before, and where in the source tree to start looking when extending a feature.

This is **not** a milestone log, not a changelog, and not a research archive. Each topic has one canonical home; per-subsystem reality lives in `systems/*.md`, per-API plug-in detail lives in `tmodloader/*.md`, and the cross-component map lives in `integration/integration-map.md`.

## Folder shape (rationale)

```
context/
├── _Overview.md             ← you are here: entry point + folder map
├── architecture.md          Top-down structural map of the mod
├── notes.md                 Index of notes/
├── _staleness-report.md     Per-file verdicts from the last upkeep run
│
├── systems/                 One file per stable subsystem (canonical reality)
│   data-pipeline (v0.10), hook-instrumentation, metric-collection,
│   spike-detection, allocation-tracking, insights-engine, persistence,
│   overlay, events-and-context, test-harness, mod-lifecycle
│
├── tmodloader/              Per-API reference: what tModLoader exposes
│                            AND how each of our subsystems plugs in
│   hook-surface, monomod-detours, lifecycle-and-loop, ui-system,
│   mod-identity, engagement-surfaces, ilhook-migration-research
│
├── integration/             Cross-cutting maps
│   integration-map.md       Per-component plug-in points + tier model
│
├── notes/                   Topical inbox (decisions, conventions, future work)
│   decisions, conventions, future-html-report, future-settings-design,
│   litedb-migration-plan, plus annotated historical research plans
│
└── plans/code-health-audit/ 2026-05-20 audit with full implementation receipt
```

**Why this shape.** The pre-implementation reconnaissance done in 2026-05-19 lived as a flat `tmodloader-*.md` set plus a single `integration-map.md`. After the 2026-05-20 implementation burst landed eleven distinct subsystems, that shape no longer scaled: there was no canonical home for "how does our insights engine actually work" and the per-API slices grew "how we plug in" sections piecemeal. The folder split separates three concerns:

- **What tModLoader exposes** (`tmodloader/`) — the API surface, stable across implementation churn.
- **How our subsystems are built** (`systems/`) — the implementation reality, changes with our code.
- **How the pieces connect** (`integration/`) — the cross-cutting map.

When a new feature lands, the canonical home is the relevant `systems/*.md`. When a new tModLoader API gets used, the relevant `tmodloader/*.md` gains a "How we plug in" line. When the connection between two subsystems changes, `integration/integration-map.md` updates. Three places to look, three places to write.

## Reading order for a new session

1. **`README.md`** at the repo root — directional intent (six views, overhead budgets, milestones).
2. **`context/architecture.md`** — top-down structural map.
3. **`context/notes.md`** + **`context/notes/decisions.md`** — what was decided and why.
4. The `systems/*.md` file matching the work area, plus the `tmodloader/*.md` slice it cites.
5. **`context/plans/code-health-audit/index.md`** if the work touches anything in the audit's implementation receipt.

## The four Project Invariants (`README.md` and `CLAUDE.md`)

Inviolable. A change that breaks one is wrong regardless of how clean it looks.

1. **Read-only instrumentation.** The mod measures; it never changes game behaviour, save data, world state, or another mod's state.
2. **Overhead is a budget, not an aspiration.** Lite < 1%, Standard 2–4%, Deep 5–10%. The per-tick hot path is zero-allocation.
3. **The honesty contract.** Descriptive, never normative. No mod is "core" or "removable". Every insight badges its data strength (`ThisSession` / `LifetimeData` / `NeedsPersistence`) and its confidence (`Preliminary` / `Low` / `Medium` / `High`) independently.
4. **Abort-clean on host drift.** If a loader signature the Hook Interceptor depends on no longer matches, the mod disables instrumentation and reports it; it never proceeds against internals it cannot verify.

## What changed since the last upkeep (2026-05-19 → 2026-05-20)

The 2026-05-19 folder was the pre-implementation reconnaissance done before the Hook Interceptor architecture landed. Between then and 2026-05-20, the implementation burst landed:

- Both hook backends (delegate-pair `HookInterceptor` and IL `ILHookInterceptor`), the shared `HookCategoryRouter`, and the backend-aware `HookCoverageView`. Coverage tri-state install outcomes + `HookCoverageVersion = 3`.
- The full overlay tab system (`IOverlayTab`, `TabRegistry`, five concrete tabs) with `IsAvailable` enforcement and 1 Hz truncation caches.
- The insights engine (four live + six gated detectors, store with p-value-gated promotion, pattern-aware ranking, `EvidenceScope` enum, schema v4 JSON parity via `InsightsEngine.Shared`).
- Spike detection, per-tick attribution ring, allocation tracking with IL-emitted CPU+alloc variants.
- The events-and-context subsystem (`ContextTagger`, `BiomeRegistry`, `BossSampler`, `EventAggregator`, optional `SubworldProbe`).
- Persistence hardening: atomic temp-file + `File.Replace` writes, narrowed prune pattern, `SessionLogFailureException` self-disable, `FlushSpikes` at world unload, ILHook outer-catch `Uninstall()`.
- Non-shipping xUnit test harness, three fixtures, build-time isolation from the `.tmod` package.

The full 2026-05-20 code-health audit and its implementation receipt are in `plans/code-health-audit/index.md`.

## 2026-05-21 — v0.12 tab rework: Timeline / Lag / Insights end-to-end

Multi-wave rework taking the three secondary tabs from flat ledger sheets to multi-panel Palantir-style dashboards. **21 substantive additions** plus 3 foundations plus a creative-visualisation patch on each tab.

- **Foundations** (`F1` `ModRosterScanner`, `F2` `PerModUsageAggregator`, `F3` `PerModCostTimeSeriesAggregator`) under `Data/Collectors/` and `Data/Aggregators/`. Every per-mod observation in the rework reads through one of these via the registry.
- **Timeline** (T1–T7): per-segment mod-attribution waterfall, lifetime delta badges, context-transition overlay track, session activity heatstrip, per-mod biome/invasion/boss attendance, 30s pre-death replay strips, factual session chronicle.
- **Lag** (L1–L7): fingerprint clustering, cause×context heatmap, GC pressure narrative, per-segment lag density, attribution-confidence visualisation, allocation→GC causality chain, lag rhythm/periodicity detection.
- **Insights** (I1–I7): per-mod observatory cards composing roster + usage + cost, dormant content surface, per-mod attendance breakdown, loadout influence trace, cross-cutting signal aggregation, engagement-vs-cost scatter, mod interaction correlation matrix.
- **Visualisation patch** layered creative visuals on top of the functional layer — narrative ribbons, sunburst attendance, lag galaxies, GC tide charts, allocation Sankeys, polar rhythm plots, DNA-strand mod cards, dust-shelf dormant rows, chord-diagram interaction matrix.

Locked-snapshot-contracts pattern (`Data/Contracts/RolloutContracts.cs`) let downstream agents compile against types whose implementations didn't yet exist, enabling Waves 1/2/3 to overlap. 14 background agents total. Full data-layer doc in `systems/data-pipeline.md`; v0.12 entry in `notes/decisions.md`.

## 2026-05-21 — v0.10 unified data pipeline + audit follow-up

Two structural shifts landed:

- **Unified `Data/` pipeline.** Every named, typed stream the mod produces now lives in `Data/` and is registered with `DataRegistry.Shared` at mod load. Consumers (the dashboard router, the future Mod.Call API, the future session-report exporter) read via `Lookup<TSnapshot>(name).CurrentSnapshot()` instead of reaching into named subsystems. Policy: *if it produces a number it lives in `Data/`; if it consumes a number it asks the registry.* `ProfilerSystem.Collector` is now `internal`. Canonical reality in `systems/data-pipeline.md`.
- **Multi-agent code-health audit.** Five parallel subagents audited Data/, Profiling/ core, Persistence+Insights, Web/, UI/. The critical slice landed in two follow-up commits: Invariant 2/3 fixes (`SegmentDetector.ComputeBiomeComposite` memoisation, dashboard wording de-normativisation), data-race fixes (`DashboardRouter.BuildNow` migrated to pipeline snapshots, `DataRegistry.Register/DisposeAll` lock atomicity), correctness fixes (`BoolIndex.EnsureCapacity` infinite loop, `PlayerDeathDetector` short-cast truncation, `ContextTransitionWatcher` weather-flag identity, `TickDownsampler._max` eviction, `ModlistStream` replay-idempotency, insights detector confidence honesty). Full session record in `notes/decisions.md` under the 2026-05-21 entry; deferred-items list is at the bottom of that entry.

## Notes for future sessions

- The per-slice `tmodloader/*.md` docs cite tModLoader members by **fully-qualified name**, never line number — stable across tModLoader updates.
- This folder was reorganised on 2026-05-20 as part of the post-implementation upkeep. The old flat `tmodloader-*.md` and `integration-map.md` files were moved via `git mv` so renames stay visible in the log. The "design pitch wording" correction (per-mod attribution comes from the profiler's own `MethodBase.DeclaringType.Assembly → Mod.Code` reflection, not from a tModLoader ownership table) was first surfaced in the 2026-05-19 recon and remains accurate.
