# v0.27.1 — cross-session data-quality + snapshot-context patch

EXECUTED 2026-06-26 (v0.27.1). Born from the first real play-test of the v0.27
cross-session layer: a live read of the on-disk `profiler.litedb` (10 sessions, 29 mods,
7 distinct modlist fingerprints) surfaced several defects, all in the new cross-session
surface — the live per-session numbers were always fine. Waves 0–2 (commits `a33d680`,
`0222bd1`, `803c902`) fixed them.

## What the play-test data showed (the diagnosis)

- **Thin-session contamination (the headline).** The rollup folded each session's per-mod
  average cost into a Welford stat with EQUAL weight regardless of length. World-load
  windows of ~345–390 ticks (≈6 s of sim) divide one-time JIT/asset-load cost by a tiny
  denominator → absurd per-mod averages (ImproveGame's ring held a 390-tick session at
  **121 ms/tick** beside real sessions at ~0.5–2 ms). Equal-weight pooling dragged the
  lifetime mean to 16.9 ms/tick. Every mod inflated in lockstep in those sessions.
- **"3 of 26" looked wrong but was right.** `CostConcentration` counts cost-CONTRIBUTING
  mods. On a 29-mod session, 3 mods were idle (0.000 ms), so 29 − 3 = 26. It read as a
  bug only because the card showed a bare count with no roster context, and the player had
  coincidentally just added exactly 3 mods. NOT cross-session bleed: the live `/api/insights`
  feed reads the live store + freshly-computed cross-session insights, never another
  session's persisted rows.
- **The profiler flagged itself.** It runs every tick with zero engagement by construction,
  so it always tripped `CostlyDespiteLowUsage`, `LifetimeSpikeContributor`, and
  `AllocationBurst` against the mods it measures.
- **`ProfilerVersion` was `0.0.0.0`** on every session — it read the assembly version (never
  stamped) instead of `Mod.Version`. Per-mod `LastVersion` was always correct.
- **Biome rendered as `biome:2022653656`** — the segment Key is a stable composite hash (for
  cross-session comparison) that `SegmentNameTable` can't reverse, and the `BiomeRegistry` is
  per-session. The detector already resolved `seg.Name` on the side but discarded it.
- **The profiler's "978 KB/tick" allocation is a measurement artifact, not an Invariant-2
  breach.** Near-constant ~978 KB across different sessions (vs 0.5 KB in the per-mod
  aggregate) = its off-tick overlay-render / persistence-flush bytes (it profiles itself),
  sampled into a per-tick rolling mean. The instrumentation hot path is verifiably zero-alloc.

## The fixes (executed)

- `RollupFold.MinSessionTicks = 1800` (≈30 s): a substance gate. Below it, a session's
  Cost/Alloc/Engagement are NOT folded into the lifetime distributions; the ring entry,
  SessionCount, ActiveSessionCount, and spike/stall totals stay unconditional (presence +
  event counts are still honest). `row.Cost.Count <= row.SessionCount` is the intended result.
- Tick-weighting: `WelfordStat` gained `WeightSum`/`WeightedSum` + `[BsonIgnore] WeightedMean`
  + `FoldSampleWeighted(value, ticks)`. The equal-weight `Mean`/`M2` stay (honest
  session-to-session variance); `WeightedMean` is the play-time-weighted lifetime average and
  is what the read layer (`HistoryStore`, `/api/history`) and the cross-session detectors now
  read. On the real ImproveGame ring: 16.9 → **~11.8** (WeightedMean) / ~5.3 (gated equal),
  the 121 ms artifact gone; the residual is the genuinely heavy 23k-tick session (real signal).
  `CrossSessionMath.AvgCost/AvgEngagement` and `WindowedStats` skip sub-threshold ring entries.
- `ProfilerVersion` now reads `Mod.Version` (so v0.27.1+ sessions record the real version —
  the substrate roadmap-F1 regression detection needs).
- `InsightConstants.SelfModInternalName = "PerformanceProfiler"` (legitimate self-id;
  Invariant 5 forbids hard-coding OTHER mods' names) excludes the profiler from the three
  detectors; `AllocationBurst` also drops it from the share DENOMINATOR.
- `HotHookDominance.ModTotalFloorMs` 0.05 → **0.5** (no more "100% of a 0.19 ms mod").
- The aggregate-insight contributor contract (`a33d680`): `Insight.Contributors` +
  `Magnitude.LoadedCount` + `InsightRow.Contributors/LoadedModCount/ActiveModCount`.
  `CostConcentration` names its top-N mods; the card reads "3 of 26 active mods carry 75% of
  cost: ImproveGame, CalamityMod, ThoriumMod (29 loaded, 3 idle)".
- Biome name threaded via `Insight.SubjectLabel` (renderer prefers it).

## Web surfaces added (wave 2)

- Insights: contributor split-bar + per-mod legend + "N loaded · M idle" line per aggregate card.
- Observatory: a **roster-evolution matrix** (`/api/modlist-history` → `DashboardRouter.Modlists.cs`),
  mods × modlists with version + change-accent + absent cells. Snapshot infra ALREADY existed —
  the `modlists` collection has snapshotted every roster (members + versions) since v0.27.
- Self: a session/roster banner (current count + diff since last session) + a thin-session
  badge ("9 of 13 substantial", `SubstantialSessionCount` on `DataHealthView` / `/api/data-health`).

## Open / deferred (flagged)

- **Runtime-pending.** The fix applies to NEW folds. The existing on-disk rollup still carries
  the old contaminated means until rebuilt (a reset + replay, or a re-backfill). The next
  play-test is the runtime verification — none of the v0.27/v0.27.1 cross-session behaviour is
  in-game-verified yet (off-game: 0 error CS, 170 tests, Playwright DOM check, zero console errors).
- `HotHookDominance` is NOT self-excluded (only the three detectors that actually flagged the
  profiler are). Cheap follow-up if its overlay hook ever dominates its own cost.
- Warm-up-tick exclusion at collection time was considered and not done — the gate achieves the
  same outcome more cheaply (the whole thin session is the artifact, not just its first ticks).
- Pre-existing em dashes in insight rendered copy (`InsightRenderer` BaselineClause) remain; the
  web layer uses `—` as its standard no-data placeholder (a convention, not this patch's concern).

See [[cross-session-history-layer]] for the v0.27 layer this patches.
