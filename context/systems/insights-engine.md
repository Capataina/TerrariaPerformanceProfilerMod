# Insights Engine

*Maturity: comprehensive · Stability: unstable — four detectors live, six gated; pattern coverage and audience tuning still evolving.*

## Scope / Purpose

The insights engine reads `MetricCollector` state and emits short-form diagnostic records the player or an agent can act on. It is the **second-derivative** layer over the per-tick measurement: where metric collection answers "what happened?", insights answer "is this worth noticing, and how strong is the claim?"

The honesty contract (Invariant 3) governs every output: every record carries a `Confidence` (statistical strength), an `EvidenceScope` (was the data this session, lifetime, or insufficient?), and a `Magnitude` whose units depend on the pattern. The renderer hedges template wording where the data dictates.

## Boundaries / Ownership

Files under `Data/Detectors/Insights/` (the whole subsystem moved out of `Profiling/Insights/` in v0.11): `InsightsEngine.cs`, `InsightStore.cs`, `InsightRecord.cs`, `RankingScorer.cs`, `InsightRenderer.cs`, `IInsightDetector.cs`, plus the live detectors in `Data/Detectors/Insights/Detectors/` (`HotHookDominanceDetector`, `AllocationBurstDetector`, `FreeRemovalCandidateDetector`, `PeakContributorToSpikeDetector`) and `GatedDetectors.cs` carrying six stub detectors.

Owns:

- The detector roster (four live, six gated).
- The live + history store with TTL eviction.
- Confidence promotion gated by `PValueAdjusted`.
- `EvidenceScope` classification per record.
- Pattern-aware magnitude normalisation for ranking.
- The shared singleton (`InsightsEngine.Shared`) consumed by both the dashboard insights surface and the `SessionRecorder` (via `Data/Streams/InsightStream.cs`).

Does not own:

- The metric data the detectors read — that belongs to `systems/metric-collection.md` and `systems/spike-detection.md`.
- The insights UI rendering — the live surface is the browser dashboard (`systems/web-dashboard.md`); the in-game InsightsTab is archived under `UI/`.
- The persisted schema — see `systems/persistence.md`.
- The lifetime-data persistence layer — LiteDB (`SessionRecorder`); see `systems/persistence.md`.

## Current Implemented Reality

### Detector roster

| # | PatternKey | Detector | State | Why |
|---|------------|----------|-------|-----|
| 1 | `HotHookDominance` | `HotHookDominanceDetector` | **live** | Reads `PerModAttribution`; data available today |
| 2 | `AllocationBurst` | `AllocationBurstDetector` | **live** | Reads alloc columns when tracking is on |
| 3 | `FreeRemovalCandidate` | `FreeRemovalCandidateDetector` | **live (gated emit)** | Registered, but its `Scope = NeedsPersistence` records emit only after the lifetime store lands |
| 9 | `PeakContributorToSpike` | `PeakContributorToSpikeDetector` | **live** | Reads `SpikeDetector.Windows` |
| | | | | |
| 1' | `ContextCorrelatedSpike` | `ContextCorrelatedSpikeDetector` (in `GatedDetectors.cs`) | gated on `events` | Needs `EventAggregator` + transition stream |
| 2 | `ContextConditionalCost` | `ContextConditionalCostDetector` | gated on `events` | Needs `EventAggregator.BucketStats` |
| 5 | `GcPauseCulprit` | `GcPauseCulpritDetector` | gated on `events` | Needs per-tick per-mod alloc deltas plus events |
| 6 | `SustainedCostShift` | `SustainedCostShiftDetector` | gated on `litedb` | Needs session-half slicing |
| 8 | `NewContributor` | `NewContributorDetector` | gated on `litedb` | Needs cross-session diff |
| 10 | `HookFrequencyTail` | `HookFrequencyTailDetector` | gated on `events` | Needs per-hook call counts |

Gated detectors are registered in the roster but `Evaluate` short-circuits on `det.IsGated` (`InsightsEngine.cs:128-130`). They contribute to the persisted `gated` map via `GatedPatterns()`.

### Pattern keys (stable numeric values)

```
ContextCorrelatedSpike = 1
ContextConditionalCost = 2
HotHookDominance       = 3
AllocationBurst        = 4
GcPauseCulprit         = 5
SustainedCostShift     = 6
FreeRemovalCandidate   = 7
NewContributor         = 8
PeakContributorToSpike = 9
HookFrequencyTail      = 10
```

Stable across schema bumps; never reorder (`InsightRecord.cs:8-24`).

### `Confidence` (statistical strength)

`Preliminary = 0` (first fire only) → `Low = 1` → `Medium = 2` → `High = 3`.

Promotion is gated by both `ConfirmationCount` and `PValueAdjusted` (`InsightStore.cs:215-224`):

```csharp
if (confirmationCount >= 4 && pAdjusted <= 0.05) return Confidence.High;
if (confirmationCount >= 3 && pAdjusted <= 0.10) return Confidence.Medium;
if (confirmationCount >= 2) return Confidence.Low;
return Confidence.Preliminary;
```

A record with `PValueAdjusted = 1` (detector explicitly declares "no hypothesis test ran") can never reach Medium by repetition alone — the honesty contract requires badges to be defensible independently of how often the same untested observation re-fires.

### `EvidenceScope` (data-source breadth)

Orthogonal to `Confidence` (`InsightRecord.cs:49-54`):

| Scope | Meaning |
|-------|---------|
| `ThisSession` | Every signal sourced from the current world only. Default for in-scope detectors. |
| `LifetimeData` | Record draws on prior sessions retained via the persistence layer (not yet wired). |
| `NeedsPersistence` | Detector has a real claim it cannot substantiate without persistence. `FreeRemovalCandidateDetector` sets this. Renders with a "needs persistence" hedge until LiteDB lands. |

The renderer (dashboard; the archived InsightsTab did the same) shows both badges side by side. A record can be statistically tight within a single session and still weaker than lifetime data accumulated across sessions; the two-badge design lets a reader argue with either dimension independently.

### `Audience` (Player / Modder / Both)

Selected at render time, never at detect time. Modder-only records are demoted in the scorer (`RankingScorer.AudienceMatch` returns 0.5 for Modder, 1.0 for Player/Both).

### `BaselineKind` (comparison vs what)

Records declare what they compare against (`InsightRecord.cs:68-76`):

```
SessionMean, RollingFiveMinute, PreContext, ComparableContexts,
SessionFirstHalf, PerModRollingMean, None
```

The honesty contract requires every rendered insight to declare its baseline so a reader can argue with the comparison itself, not just the number. The renderer surfaces this on the dashboard and in the persisted record.

### Pattern-aware ranking

`RankingScorer.Score` (`RankingScorer.cs:33-48`) is the weighted sum:

```
score = 0.30 magnitude + 0.25 confidence + 0.15 recency
      + 0.15 actionability + 0.10 novelty + 0.05 audience
```

`NormaliseMagnitude` splits regimes by `IsSharePattern`:

```csharp
private static bool IsSharePattern(PatternKey k) => k switch {
    PatternKey.HotHookDominance       => true,
    PatternKey.AllocationBurst        => true,
    PatternKey.PeakContributorToSpike => true,
    _                                 => false,
};
```

Share patterns store fractions in `[0,1]` and pass through `ClampUnit` unchanged. Ratio patterns use the soft-knee curve at 10× (`1× → 0`, `2× → ~0.11`, `5× → ~0.44`, `10×+ → 1`).

Before commit `aa914ce` every detector ran through the ratio curve, which collapsed every share value to a magnitude of zero. A 40% contributor and a 90% contributor ranked identically — the strongest live signal the in-scope detectors produce was being erased. Pinned by `Tests/RankingScorerTests.cs`.

### `InsightStore` lifecycle

```
Submit(record, nowTick):
    key = StableKey(pattern, subject)        // packs into 64-bit
    if _live.TryGetValue(key, existing):
        existing.Magnitude = record.Magnitude
        existing.Evidence  = record.Evidence
        existing.ConfirmationCount++
        existing.LastSeenTick = nowTick
        existing.Confidence = PromoteConfidence(existing.ConfirmationCount, existing.Evidence.PValueAdjusted)
        return
    if _live.Count >= LiveCap (32):
        EvictStalest(nowTick)
    record.FirstSeenTick = nowTick
    record.LastSeenTick  = nowTick
    record.ConfirmationCount = 1
    record.Confidence = PromoteConfidence(1, record.Evidence.PValueAdjusted)
    _live[key] = record
    _history.Add(record)

Tick(nowTick):
    for each kv in _live where nowTick - lastSeen > _ttlTicks:
        evict to history list

TopInto(destination, n, nowTick):
    _topAllScratch.Clear()
    add all _live values
    Sort by RankingScorer.Score(rec, nowTick, ttlTicks) desc, ties by lastSeen desc
    take top n respecting PerPatternCap (2)
```

`LiveCap = 32`, `PerPatternCap = 2`, `DefaultTtlTicks = 60 × 60 × 5` (≈5 minutes at 60 Hz). The comparer is captured once at construction; `_topComparerNowTick` is refreshed before each sort to avoid re-allocating the closure per call. `TopInto` is allocation-free past the initial warmup — pinned by `Tests/InsightStoreTests.cs`.

### Shared singleton

`InsightsEngine.Shared` is a public static field plus `GetOrCreateShared()` (`InsightsEngine.cs:33-39`). The dashboard insights surface and the persistence layer (`Data/Streams/InsightStream.cs`, written by `SessionRecorder`) both call `GetOrCreateShared()` to ensure they read from the same store. The archived in-game InsightsTab (under `UI/`) used the same singleton.

`ProfilerSystem.OnWorldUnload` explicitly sets `InsightsEngine.Shared = null` so the next world starts with an empty store. Without this, records would leak across sessions and the persisted per-session insights block would carry stale entries.

### Gated map cached at construction

`_gatedMap` and `_gatedLabel` are computed once in the `InsightsEngine` constructor (`InsightsEngine.cs:76-110`). The pre-fix shape rebuilt the map per frame inside InsightsTab's `Draw`, allocating a fresh `Dictionary` + `List` 60×/sec.

## Key Interfaces / Data Flow

```
ProfilerSystem.PostUpdateEverything (every 60 ticks, off-thread, gated on prior run):
    engine = InsightsEngine.GetOrCreateShared()
    engine.Evaluate(collector, latestTick, historyDepth)
        for each detector in _detectors:
            if det.IsGated or !det.IsAvailable(collector): skip
            det.Evaluate(collector, nowTick, sessionLengthTicks, _scratch)
            for each rec in _scratch:
                _store.Submit(rec, nowTick)
        _store.Tick(nowTick)   // TTL eviction

dashboard /api/insights + /api/cross-cutting read (InsightsStat etc):
    engine.Store.TopInto(_ranked, MaxRows, nowTick)
    bodies populated parallel to _ranked, rendered with Confidence + Scope badges

SessionRecorder insights persist (Data/Streams/InsightStream.cs):
    engine = InsightsEngine.GetOrCreateShared()
    live[] = Store.AllLive() persisted to the `insights` collection
    history[] = Store.History persisted
    gated = engine.GatedPatterns()
```

The same engine instance is touched from more than one thread in practice — the thread-pool `Evaluate` (scheduled from `PostUpdateEverything`) and the dashboard / recorder read paths. The read paths read; only `Evaluate` writes, and it is gated so it never overlaps itself (`_insightsEvalInflight`). The contention surface is small but real now that `Evaluate` runs off-thread rather than inline; the read paths use `AllLive()` / `TopInto` snapshots. If a second writer cadence ever lands, this needs revisiting.

## Implemented Outputs / Artifacts

| Surface | Source |
|---------|--------|
| Dashboard `/api/insights` ranked card rows (archived InsightsTab) | `Store.TopInto` |
| Dashboard "gated detectors waiting on: …" line | `_gatedLabel` |
| Persisted `insights.live[]` | `Store.AllLive()` via `InsightStream` |
| Persisted `insights.history[]` | `Store.History` via `InsightStream` |
| Persisted `insights.gated{}` | `GatedPatterns()` via `InsightStream` |

## Known Issues / Active Risks

- **`PValueAdjusted` defaults to 1 for the in-scope detectors.** None of the four live detectors currently runs a hypothesis test; they emit records with `Evidence.PValueAdjusted = 1` and rely on the magnitude + repetition signal. As a consequence, no live detector's records reach Medium or High confidence today. Acceptable — the honesty contract is "untested observations stay at Low/Preliminary" — but future detectors that compute real p-values should be wired through the same evidence struct, not a separate path. Downstream impact: the dashboard insights surface today shows mostly Low-confidence rows; the colour palette must continue to make Low rows readable rather than de-emphasised.
- **Gated detector emit is fully disabled.** `Evaluate` skips gated detectors entirely. The roster registration exists only so `GatedPatterns()` lists them in the persisted output. A future code reader could misread the `Data/Detectors/Insights/Detectors/GatedDetectors.cs` file as implementing the pattern; it does not. The docstring on each gated detector (`Evaluate` body) says "registered for roster / gate visibility but currently emits zero records" — keep this wording when adding new gated detectors.
- **`_topComparerNowTick` is shared scalar state.** If two callers ever invoke `TopInto` concurrently on the same store, the comparer reads whichever `nowTick` was written last. Today the sorting `TopInto` caller is the dashboard read path; the `SessionRecorder` insights persist uses `AllLive()` which does not sort. Watch item.
- **`StableKey` packs into 64 bits with 16-bit slots.** Collisions are mathematically possible if a single mod somehow had > 65k distinct hookIds (`InsightStore.cs:195-205`). Today the largest discovered hook counts are in the hundreds per mod, but if the per-`(mod, hookId)` space ever grows, the key shape needs widening.

## Partial / In Progress

- **`FreeRemovalCandidateDetector.Scope = NeedsPersistence`** is set, but the detector currently emits zero records because its internal gate (a stub condition until lifetime data exists) never clears. When the LiteDB layer lands, the gate opens and records will emit with `Scope = NeedsPersistence` first, then transition to `Scope = LifetimeData` once enough sessions accumulate.

## Planned / Missing / Likely Changes

- **Six gated detectors await the `events` and `litedb` gates** (see roster table above). Each is queued; the `events` gate is closer to opening because `EventAggregator` already accumulates per-dimension bucket stats — only the transition stream is missing.
- **Real p-value computations for in-scope detectors.** Today every record carries `PValueAdjusted = 1`. The rationale is captured in `notes/decisions.md`; not started.
- **Audience tuning per pattern.** Currently every pattern hard-codes `Audience.Player` or `Audience.Both`. The plan calls for audience-aware template strings; not implemented.

## Durable Notes / Discarded Approaches

- **Initial `RankingScorer.NormaliseMagnitude` collapsed every magnitude through the ratio curve.** Found in the 2026-05-20 audit (`plans/code-health-audit/insights-engine.md`). A share value of 0.42 was treated as a 0.42× ratio (below baseline 1.0) and got clamped to 0. So a 40% contributor and a 90% contributor ranked identically — the audit named this as the most-impactful insight-engine bug because it erased the strongest signal the in-scope detectors produce.
- **`InsightsEngine.Shared` did not exist before commit `aa914ce`.** The InsightsTab owned its own engine instance, so the SessionLogWriter (when it was added) saw an empty store and the JSON's insights block carried nothing. The singleton design is the dual-surface fix — both surfaces read from the same place.
- **`InsightStore.PromoteConfidence` did not gate on `PValueAdjusted` before commit `aa914ce`.** A record with `PValueAdjusted = 1` could reach Medium just by re-firing three times. The audit (`insights-engine.md`) flagged this as a direct honesty-contract violation: badges promoted on repetition alone are not defensible. The fix is the `pAdjusted <= 0.10` clause on Medium and `pAdjusted <= 0.05` on High. Pinned by `Tests/InsightStoreTests.cs`.
- **`FreeRemovalCandidateDetector` was misleadingly docstring'd to "fire with hedged copy".** Reality: `Evaluate` skips gated detectors entirely, so the detector emitted nothing. The docstring was rewritten (commit `aa914ce`) to: "registered for roster / gate visibility but currently emits zero records."
- **The gated-pattern map was rebuilt per frame.** `BuildGatedMap` ran inside InsightsTab's `Draw` allocating a `Dictionary` + `List` 60×/sec. Audit-flagged (overlay-ui findings) and fixed by caching at engine construction.

## Obsolete / No Longer Relevant

Nothing. The subsystem is roughly five weeks old and its discarded approaches all surface in the audit + this run.

## Cross-references

- `systems/web-dashboard.md` — the live insights surface (`/api/insights`, `/api/cross-cutting`); the in-game InsightsTab is archived under `UI/`.
- `systems/persistence.md` — the persisted `insights` collection (`InsightStream` + `SessionRecorder`) and the lifetime-data layer that opens the gated detectors.
- `systems/metric-collection.md` — what `det.Evaluate(collector, …)` reads.
- `systems/spike-detection.md` — `PeakContributorToSpikeDetector` reads `SpikeDetector.Windows`.
- `notes/decisions.md` — the original insights-engine design rationale (the per-feature plan note was folded in here).
- `Tests/RankingScorerTests.cs`, `Tests/InsightStoreTests.cs` — pin the audit findings.
- `plans/code-health-audit/insights-engine.md` — audit findings driving the current state.
