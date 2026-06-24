# Insights engine rework — status (the eight-wave consolidation)

Executed 2026-06-24 against `context/plans/insights-engine.md`. The interpretation
layer is now a top-level `Insights/` module: producers register, consumers look up,
nobody re-derives — the same consolidation v0.10–v0.12 did for data, one layer up.
Mod version `0.18.1 → 0.19.0`.

## What landed (commit-by-commit)

| Wave | Commit | What |
|---|---|---|
| 1a | `87e0601` | `git mv Data/Detectors/Insights → Insights/` (top-level peer); namespace `Data.Detectors.Insights → Insights`. Byte-identical. |
| 1b | `ef7413c` | `Insights/Shared/` primitives (ModMetrics, Shares, ModNames); killed the duplication census. Byte-identical. |
| 2 | `15d71d8` | I-series interpreted stats (Observatory, Dormant, CrossCutting, EngagementCost, ModInteraction) + InsightsStat → `Insights/Publish/`. |
| 0 | `5a78ca9` | `InsightRecord → Insight`; shape-tagged `Magnitude` (Deviation/Share/Rate/Scaling/Headroom/Distribution); process-level `SubjectRef` (Session/Runtime/Machine); store gaps G5 (comparer race) + G6 (StableKey collision) closed; frozen `IReferenceFrame`/`IDriver`/`IInsightInput` contracts. |
| 4 | `6e8ffd3` | **The Flute fix.** Usage axis now active-use ticks (held/worn/in-biome), not creation counts. New `ItemsHeldTicks`/`ArmorEquippedTicks` per-tick counters; `ModMetrics.UsageWeight` reads them; `CreationWeight` keeps the old signal. Dashboard dormant table reworked. Breaking. |
| 3 | `6f2feca` | `ContextBaseline` reference frame (per-context per-mod Welford, fed 1 Hz off-thread, capped) + `Stats` (CohensD, Welch's t). Un-gated `ContextConditionalCost` with candidate-gating + Bonferroni. |
| 5 | `6346d31` + follow-on | Families D (`FrameHeadroom`), E (`CostConcentration`), C (`FrameJitter`) detectors + render templates. 3 of 5 families; **B not built** (see below). |
| 6 | `7a80228` | `CrossSessionStore` — per-context baselines persist across sessions, fingerprint-keyed (closes G3: confidence can reach High, LifetimeData truthful). Round-trip tested. |
| 7 | (this) | SessionSummaryLogger logs the engine's top insights (dual-surface); version bump; this note. |

## Verification boundary (important)

- **Compile-verified mod-wide** via `dotnet msbuild` (0 `error CS`) — see `context/notes/compile-gate.md`.
- **Unit-verified** via `dotnet test` — 104 passing: the Shared primitives, the Wave 0
  store/subject changes, the Wave 3 statistics + accumulator, the Wave 5 Pareto maths,
  and the Wave 6 LiteDB round-trip.
- **RUNTIME-UNVERIFIED** (needs an in-game Build + Reload; the `.tmod` lock blocks a
  packaged run here):
  - the per-tick HeldItem/armour sampling actually populating the new counters;
  - the new detectors emitting sensible insights against a live 1 Hz stream;
  - the new render strings + the reworked dormant table on the dashboard;
  - the cross-game-restart seed/save (does session 2 load session 1's baseline).
  The mechanisms under each are unit-tested and the runtime calls are guarded so a
  failure degrades the feature, never crashes a run (Invariants 1/4).

## What remains (honest)

- **Wave 5 family B (Temporal) not built:** leak/warmup/drift needs the
  heap/session-age/entity-count Drivers (deferred from Wave 4) to control for workload
  — building it without that control is the temporal-confound the plan forbids
  ("heap up AT CONSTANT entity count = a leak; heap up WITH entity count = progression").
  Family C is now in (FrameJitter — the CV/stutter half; bimodality/recovery, which
  need the full frame-time distribution not just median+MAD, remain).
- **Still-gated detectors:** ContextCorrelatedSpike (needs a transition-timed stream),
  SustainedCostShift / NewContributor (need per-tick per-mod history), HookFrequencyTail
  (needs per-hook call counts), LoadoutCombinationCost (cross-session loadout aggregation),
  and cross-mod chains. The Wave 6 persistence is half their substrate.
- **Frozen contracts with no implementer yet:** `IReferenceFrame`, `IDriver`,
  `IInsightInput` — their single-frame implementers land with the Family B/C detectors
  that consume them (via an adapter, to avoid Profiling→Insights layering inversion).
- **Doc drift:** `context/systems/insights-engine.md` still describes the engine at
  `Data/Detectors/Insights/` and pre-rework. A full `upkeep-context` pass is the right
  tool — this rework is large enough to warrant it rather than hand-patching.
