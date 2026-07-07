# Plan — Honesty Completion (S06 + S07)

> Slots: atlas S06 (real-cadence honesty) + S07 (fingerprint robustness).
> Bugs closed: X1, X2, X3 (ui-ux-audit.md), H4 (session ledger), X7.
> Status: planned → in progress 2026-07-07. Version target: lands in 0.30.0.

## Why

0.28.1 made the KPI strip honest; the detector/insight/aggregator layer still
reads update-window `FrameTimeMs`. Live capture during a real 31-fps slow-motion
session caught the consequences on screen:

- **X1** — FRAME HEADROOM insight: "You sustain 60 fps with 8.4 ms of frame
  budget free" (16.7 − 8.3 compute = 8.4: the arithmetic receipt of reading the
  wrong field).
- **X2** — Lag tab: EVENTS 0 · SESSION LAG 0.00 ms at 2× frame budget. The lag
  model counts *variance* events only; uniform slowness produces zero events.
- **X3** — Summary stalls card: "biggest 122228ms" — an alt-tab counted as the
  session's headline stall.
- **X7** — Self tab: 10 "modlists seen" in 11 sessions; the fingerprint
  fractures on every dev build, so cross-modpack baselines never accumulate.

## Consumer audit (the X1/H4 surface, grep-verified 2026-07-07)

| Consumer | Reads | Verdict |
|---|---|---|
| `Data/Detectors/SpikeDetector.cs:170` | `frame.FrameTimeMs` | **repoint** → real cadence (spikes = what the player feels; per-mod attribution unchanged — it uses the tick's compute breakdown regardless) |
| `Data/Stats/Baseline.cs:240,367` | frame median/MAD buckets | **repoint** frame-ms histogram to real cadence ("3× your normal frame" must mean the player's real normal, consistent with the everything-relative decision). `TickPeriodMs*` fields are already real-cadence. |
| `Data/Aggregators/HeatmapAggregator.cs:221` | per-minute avg/worst | **repoint** — this feeds the minute-by-minute panel and the future gradient ribbon; it must show felt performance |
| `Data/Collectors/FrameTimeCollector.cs:93` | snapshot `frameMs` | **repoint** (topbar FRAME chip reads it) |
| `Insights/Detectors/FrameHeadroomDetector.cs` | baseline/KPI frame ms | **repoint + rephrase**: headroom must be computed from real cadence; when avg fps < 55 the detector must not fire at all |
| `Insights/Detectors/FrameJitterDetector.cs` | frame variance | **repoint**: cadence jitter is the felt jitter |
| `Profiling/ProfilerSystem.cs:747,761` | log narration lines | **repoint** (agent-surface honesty: `client.log` must tell the same story) |
| `UI/Overlay/*` (archived tree) | various | **leave** — archived, not compiled into the dashboard path |
| `KpiCalculator`, `DashboardRouter.Summary`, `TickDownsampler`, `SessionRecorder` | — | already repointed (0.28.x) |

`FrameTimeMs` itself stays on `TickFrame` — it is the *update-window compute*
metric, still the correct input for per-mod attribution context and the
self-overhead story. The rule after this plan: **player-facing frame numbers read
real cadence; attribution internals read compute time; every use is deliberate.**

## New measurement: the sustained-deficit signal (X2)

A new stat, `RealtimeSpeedStat` (Data/Stats/):

- `RealtimeSpeed` = clamp01(16.667 / EMA(realFrameMs)) — "the game runs at N% of
  real-time speed". EMA α=0.02 (~2s horizon at 60 UPS) + a 30s mean.
- `BudgetDeficitMsPerS` = max(0, EMA(realFrameMs) − 16.667) × ticksPerSecond.
- `TimeBelow90PctMs` session accumulator (time spent under 90% speed).
- Suspend/world-load guarded ticks excluded (they already fall back to compute
  time in the collector, so the EMA never sees pause gaps).

Surfaces:
- **Lag tab headline card** (replaces the "0 events = all clear" lie): "running
  at 51% real-time speed · 4m 12s below 90% this session · deficit 14 ms/s",
  with the existing empty panels below only claiming "no *variance* events".
- **kpi block**: `realtimeSpeed` (drives a Summary sub + the fps card tag:
  `slow-mo` when < 0.9).
- **New insight**: `SustainedSlownessDetector` (Insights/Detectors/) — fires when
  speed < 90% for ≥ 30s; copy names the top per-mod contributors descriptively
  ("while slowed, the costliest mods were X (Y ms/t) …"); THIS SESSION badge;
  never fires during warm-up.
- FrameHeadroom and SustainedSlowness are mutually exclusive by construction
  (headroom requires speed ≥ 0.98; slowness requires < 0.9).

## Cause-aware stall KPIs (X3)

`KpiCalculator` stall loop splits by `StallEvent.Cause`:
- headline (`stallCount`, `worstStallMs`, `avgStallMs`) counts only real in-app
  causes (MainThreadFreeze, MajorGc, MinorGc, UiOverlayBlocking, LongFrame,
  Unknown);
- new `pausedMs` + `pauseCount` aggregate ProcessSuspended + WorldLoad;
- Summary stalls card: sub-line "paused 2m 2s (excluded)" so the number's
  meaning is visible (Invariant 3: state what was excluded).

## Fingerprint robustness (X7)

`ModlistFingerprint.Compute` currently hashes name+version for every loaded mod
including the profiler → every dev build is a "new modlist". Change:
- fingerprint = ordered hash over the **InternalName set excluding
  PerformanceProfiler itself** (self-exclusion precedent: the rollup already
  self-excludes, 0.27.1);
- mod **versions move to session metadata** (`SessionRecord.ModVersions`),
  which becomes the enabling substrate for atlas S10 (update-regression);
- one final fracture happens when this ships (new fingerprint for the same
  list); the reset dialog's rebuild-rollup covers the rollup side, and the
  banner explains the roster change — acceptable one-time cost, called out in
  the commit.

## Test plan (feeds plans/e2e-testing.md)

- Synthetic slow-mo session (real 33ms / compute 4ms): asserts avgFps ≈ 30,
  heatmap worst ≈ real values, SpikeDetector fires on real-cadence spikes,
  FrameHeadroom does NOT fire, SustainedSlowness DOES, Lag headline shows
  speed ≈ 50%.
- Synthetic healthy session: headroom fires, slowness doesn't, speed ≈ 1.0.
- Synthetic suspend session: stall KPIs exclude the pause; pausedMs carries it.
- Fingerprint: same set ± version bump ⇒ same fingerprint; set change ⇒ new;
  profiler presence/absence ⇒ same.

## Acceptance

1. Zero `FrameTimeMs` reads outside the deliberate-list (grep gate in tests).
2. The X1 sentence can no longer be produced during slow-mo (test-pinned).
3. Lag tab headline present and truthful in the slow-mo fixture screenshot.
4. Stall headline excludes suspends (test-pinned).
5. 11-session dev history would have read "1 modlist seen" (unit-tested on
   synthetic fingerprint inputs).
