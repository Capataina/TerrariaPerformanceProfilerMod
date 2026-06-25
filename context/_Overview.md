# Performance Profiler — Context Folder

> Repository implementation memory. Read this first.

## What this folder is

The maintained working memory for the Performance Profiler mod. A reader (engineer or agent) working only from `context/` should be able to understand the whole project: what every subsystem owns, where it plugs into tModLoader, what the open risks are, what was tried before, and where in the source tree to start looking when extending a feature.

This is **not** a milestone log, not a changelog, and not a research archive. Each topic has one canonical home; per-subsystem reality lives in `systems/*.md`, per-API plug-in detail lives in `tmodloader/*.md`, and the cross-component map lives in `integration/integration-map.md`.

**Where to start in the source tree.** The mod has four production trees: `Data/` (the calculation pipeline — every number lives here), `Profiling/` (the measurement engine + DB infrastructure), `Web/` (the browser dashboard — the live player surface), and `UI/` (the archived in-game overlay, kept on disk but not in the player path). The root `PerformanceProfiler.cs` opens the LiteDB store and binds the dashboard server; `Profiling/ProfilerSystem.cs` drives the per-tick loop.

## Folder shape (rationale)

```
context/
├── _Overview.md             ← you are here: entry point + folder map
├── architecture.html        Interactive architecture explorer (open in a browser)
├── arch/                     Editable arch source: data.js (window.ARCH) + vendored shells
├── notes.md                 Index of notes/
├── _staleness-report.md     Per-file verdicts from the last upkeep run
├── .context-lint.json       Project-local lint aliases (see note below)
│
├── systems/                 One file per stable subsystem (canonical reality)
│   data-pipeline, hook-instrumentation, metric-collection,
│   spike-detection, allocation-tracking, events-and-context,
│   insights-engine, persistence, web-dashboard, overlay (archived),
│   test-harness, dashboard-audit-harness, mod-lifecycle
│
├── tmodloader/              Per-API reference: what tModLoader exposes
│                            AND how each of our subsystems plugs in
│   hook-surface, monomod-detours, lifecycle-and-loop, ui-system,
│   mod-identity, engagement-surfaces, ilhook-migration-research
│
├── integration/             Cross-cutting maps
│   integration-map.md       Per-component plug-in points + status model
│
├── notes/                   Topical inbox (decisions + conventions + posture + future work)
│   decisions, conventions, philosophy, ui-overhaul-plan,
│   future-unified-data-interface, future-html-report,
│   future-settings-design, future-insights-rework, compile-gate,
│   insights-rework-status, modlist-pre-upgrade-2026-06-22
│
├── perf-pass/               v0.5→v0.6 performance-research record
│   baseline, deferred, verification
│
├── plans/                   Forward-looking plan files + audit receipt
│   ui-component-library, extensive-testing-infrastructure,
│   insights-engine, install-ram-optimisation, code-health-audit/
│
└── pages/                   Per-page UI audit dossiers (one per dashboard tab),
                             maintained by the tools/testing L8 audit harness
```

`.context-lint.json` aliases `_Overview.md` into the architecture lint slot (it is the entry-point file alongside the structural map) and treats `tmodloader/` + `integration/` as reference-style content. The project uses the bundled **`architecture.html`** arch pipeline (open it in a browser); `arch/data.js` (`window.ARCH`) is the editable source and the five sibling files under `arch/` are vendored shells, regenerated wholesale by the pipeline — do not hand-edit them. The legacy markdown `architecture.md` was retired into this explorer in the 2026-06-25 upkeep pass.

**Why this shape.** The pre-implementation reconnaissance done in 2026-05-19 lived as a flat `tmodloader-*.md` set plus a single `integration-map.md`. After the 2026-05-20 implementation burst landed eleven distinct subsystems, that shape no longer scaled: there was no canonical home for "how does our insights engine actually work" and the per-API slices grew "how we plug in" sections piecemeal. The folder split separates three concerns:

- **What tModLoader exposes** (`tmodloader/`) — the API surface, stable across implementation churn.
- **How our subsystems are built** (`systems/`) — the implementation reality, changes with our code.
- **How the pieces connect** (`integration/`) — the cross-cutting map.

When a new feature lands, the canonical home is the relevant `systems/*.md`. When a new tModLoader API gets used, the relevant `tmodloader/*.md` gains a "How we plug in" line. When the connection between two subsystems changes, `integration/integration-map.md` updates. Three places to look, three places to write.

## Reading order for a new session

1. **`README.md`** at the repo root — directional intent (the dashboard-first pivot, the six conceptual views, overhead budgets, roadmap).
2. **`context/architecture.html`** — the interactive architecture explorer (top-down map: the production trees, the dependency graph, the data-flow trace, the failure invariants). Editable source is `context/arch/data.js`.
3. **`context/notes.md`** + **`context/notes/decisions.md`** + **`context/notes/philosophy.md`** — what was decided, why, and the posture behind it.
4. **`context/systems/data-pipeline.md`** — the calculation locus everything else reads through, then the `systems/*.md` file matching the work area + the `tmodloader/*.md` slice it cites.
5. **`context/plans/code-health-audit/index.md`** if the work touches anything in the audit's implementation receipt.

## The five Project Invariants (`README.md` and `CLAUDE.md`)

Inviolable. A change that breaks one is wrong regardless of how clean it looks.

1. **Read-only instrumentation.** The mod measures; it never changes game behaviour, save data, world state, or another mod's state.
2. **Overhead is a budget, not an aspiration.** Lite < 1%, Standard 2–4%, Deep 5–10%. The per-tick hot path is zero-allocation.
3. **The honesty contract.** Descriptive, never normative. No mod is "core" or "removable". Every insight badges its data strength (`ThisSession` / `LifetimeData` / `NeedsPersistence`) and its confidence (`Preliminary` / `Low` / `Medium` / `High`) independently.
4. **Abort-clean on host drift.** If a loader signature the Hook Interceptor depends on no longer matches, the mod disables instrumentation and reports it; it never proceeds against internals it cannot verify.
5. **No mod-specific code.** Every detector, tracker, classifier, insight, and event listener operates on generic surfaces tModLoader / vanilla Terraria exposes (`SpawnSource`, `PlayerDeathReason`, the buff arrays, the equipment slots, biome bits) — never on a named mod's identifier, namespace, type, hook, or content id. Read the interaction shape, not the mod identity. The posture is in `notes/philosophy.md`.

## Current state (build.txt 0.12)

The mod is past its dashboard-first pivot and its data-pipeline consolidation. The shape a reader should hold:

- **Dashboard-first.** The player surface is a browser dashboard served by a loopback HTTP server inside the mod (`Web/`); F9 opens the default browser. The in-game overlay (`UI/`) is archived on disk for a possible Steam-Deck revival, not compiled into the player path. Canonical: `systems/web-dashboard.md`, `systems/overlay.md`.
- **The `Data/` pipeline is the calculation locus.** Every number the mod produces lives in a `Data/` stage (collector → aggregator → stat → detector → stream) and is looked up by stable name through `DataRegistry.Shared`. v0.11 physically moved every stream-shaped class out of `Profiling/` into `Data/`; the measurement engine, the DB layer, and the `Events/` support structs stayed in `Profiling/`. Routers and exporters format snapshots, they never derive numbers. `ProfilerSystem.Collector` is `internal`. Canonical: `systems/data-pipeline.md`.
- **v0.12 tab rework.** Timeline / Lag / Insights are multi-panel dashboards built on 3 foundation streams (F1 `ModRosterScanner`, F2 `PerModUsageAggregator`, F3 `PerModCostTimeSeriesAggregator`) plus 17 tab streams, all behind the frozen `Data/Contracts/RolloutContracts.cs`. The dashboard ships five SPA tabs (Summary, Timeline, Lag, Insights, Self); the README's six conceptual views merge Now + Mods into Summary.
- **Persistence is LiteDB.** The legacy JSON `SessionLogWriter` was deleted in v0.3; persistence is a single LiteDB file + NDJSON redo journal + rotating backups, driven by one writer thread the game thread never touches. `SessionRecorder` orchestrates the `Data/Streams/*` writers. Canonical: `systems/persistence.md`.
- **Two hook backends, ILHook default.** `HookInterceptor` (delegate, ~71.6% signature-matched) and `ILHookInterceptor` (IL, ~100%, the default) coexist; `HookBackend.Mode` chooses. Canonical: `systems/hook-instrumentation.md`.

The full decision history (what landed in each version and why, the v0.6 perf pass, the v0.10 audit, the v0.12 parallelised rework) is the canonical record in `notes/decisions.md`, newest first. The v0.5→v0.6 performance research lives in `perf-pass/`. The code-health audit's implementation receipt is in `plans/code-health-audit/index.md`.

## Notes for future sessions

- The per-slice `tmodloader/*.md` docs cite tModLoader members by **fully-qualified name**, never line number — stable across tModLoader updates.
- The folder was reorganised on 2026-05-20 (flat `tmodloader-*.md` + `integration-map.md` moved via `git mv` so renames stay visible). The "design pitch wording" correction — per-mod attribution comes from the profiler's own `MethodBase.DeclaringType.Assembly → Mod.Code` reflection, not from a tModLoader ownership table — remains accurate.
- Known source-comment drifts (not behaviour) to fix at the next code touch: three `PerformanceProfiler.cs` comments + the `Load()` log say "F10" while the keybind is F9 ("OpenDashboard"); the KpiStat/KpiCalculator doc-comments name `ProfilerSystem.Load` as the registration site while the live site is `PerformanceProfiler.RegisterDataPipeline`; the committed `Tests/*.csproj` `Compile Include` globs still point at pre-v0.11 `Profiling/` paths for files that moved to `Data/` (see `systems/test-harness.md`).
