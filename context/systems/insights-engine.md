# Insights Engine
*Maturity: comprehensive · Stability: maturing — 16 detectors registered (13 live, 3 gated) across five families; the reference-frame + cross-session substrate is in place and the dashboard surface is live; the per-insight LiteDB persistence path is scaffolded but unfed.*

## Scope / Purpose

The insights engine is the **interpretation layer** over the per-tick measurement pipeline. Where the `Data/` pipeline answers "what did each mod cost?", the engine answers "is this worth noticing, against what comparison, and how strong is the claim?". It reads smoothed/aggregated pipeline outputs (never collection internals), runs a roster of detectors, deduplicates and ranks their findings, and exposes them on the browser dashboard.

**The spine law (the structural invariant of the whole module).** No insight is ever an absolute magnitude. Every insight is the deviation of a signal from its comparable baseline for that signal, on this machine, expressed as an effect size. The interface that makes this expressible is `IReferenceFrame` (`Insights/Contracts/IReferenceFrame.cs:8-22`): a frame answers "what is normal for this signal in this reference context" with a centre (`Expected`) and a spread (`Dispersion`) so a detector reports a deviation, not a raw number. The law is enforced by construction in the Family-A/B detectors (which read a reference frame) and softly elsewhere; the structural-fact patterns (`CostConcentration`, `FrameHeadroom`, `FrameJitter`) report shares against an explicit ceiling rather than a hypothesis test.

The honesty contract (Invariant 3) governs every output: each record carries a `Confidence` (statistical strength), an `EvidenceScope` (this-session / lifetime / needs-persistence), a `BaselineKind` (what the comparison was against), and a `Magnitude` whose meaningful fields are keyed off a `MagnitudeShape`. The renderer is slot-filling only — `Insights/InsightRenderer.cs:1-5` carries a hard banned-vocabulary header (`"caused by"`, `"must remove"`, `"core mod"`, `"removable"`, `"bad mod"`).

## Boundaries / Ownership

The engine is a **top-level `Insights/` module** (it was consolidated out of `Data/Detectors/Insights/` across the v0.13→v0.22 arc; that path no longer exists). Layout:

```
Insights/
├── InsightsEngine.cs         roster + Evaluate pass + reference-frame substrate + Shared singleton
├── InsightStore.cs           live/history store: dedup, TTL eviction, confidence promotion, ranking
├── RankingScorer.cs          stateless 6-component weighted score
├── InsightRenderer.cs        slot-filling templates (banned-vocab enforced by inspection)
├── Insight.cs                Insight record + all enums (PatternKey, Confidence, EvidenceScope,
│                             Audience, BaselineKind, SubjectKind, SubjectRef, MagnitudeShape, Magnitude, Evidence)
├── IInsightDetector.cs       one detector per pattern; Pattern / IsAvailable / IsGated / GatedOn / Evaluate
├── CollectorInsightInput.cs  adapts MetricCollector → IInsightInput (the testability seam)
├── Contracts/                IReferenceFrame, IInsightInput, IDriver — pure-logic interfaces (L1 test axis)
├── ReferenceFrames/          ContextBaseline (Family A), TemporalBaseline (Family B), CrossSessionStore (durability)
├── Drivers/                  Drivers.cs — EntityCountDriver, SessionAgeDriver, HeapDriver
├── Shared/                   RunningStat + Stats (Welford/Welch/Cohen), ModNames, Shares, ModMetrics
├── Detectors/                16 detector classes (13 live, 3 gated) — see roster below
└── Publish/                  7 pipeline-facing stats that compose the dashboard Insights tab
```

Owns: the detector roster; the per-context (`ContextBaseline`) and early/late (`TemporalBaseline`) reference frames; the entity/age/heap drivers; the live + history store with TTL eviction and pattern-aware ranking; confidence promotion gated by `PValueAdjusted`; cross-session baseline persistence keyed by the modlist fingerprint; the slot-filling renderer; and the seven `Publish/` interpretation stats.

Does not own:
- The metric data the detectors read — `systems/metric-collection.md`, `systems/data-pipeline.md`, `systems/spike-detection.md`.
- The persisted **session JSON / LiteDB** plumbing — `systems/persistence.md` (the `contextBaselines` collection IS fed via `CrossSessionStore`; the per-insight `insights` collection is scaffolded but unfed — see Partial / In Progress).
- The insights **UI**. The live surface is the browser dashboard (`systems/web-dashboard.md`, `/api/insights` + the five `Publish/`-backed endpoints). The in-game `InsightsTab` is archived under `UI/Overlay/Tabs/InsightsTab.cs`.

## Current Implemented Reality

### Detector roster mapped to the five families

The README's five families map onto the roster as follows. State is read from each detector's `IsGated` / `IsAvailable` (`InsightsEngine.Evaluate` at `Insights/InsightsEngine.cs:217-227` skips any detector that is gated or not-available this pass). All 16 are registered in the constructor (`Insights/InsightsEngine.cs:100-138`).

| Family | Detector | Pattern | State | Comparison / reference frame |
|--------|----------|---------|-------|------------------------------|
| **Deviation** (cost vs own baseline) | `HotHookDominanceDetector` | `HotHookDominance` (3) | **live** | hook share of mod's session cost; `SessionMean` |
| | `AllocationBurstDetector` | `AllocationBurst` (4) | **live** (Standard/Deep only — needs alloc tracking) | mod share of session alloc throughput |
| | `FreeRemovalCandidateDetector` | `FreeRemovalCandidate` (7) | **gated** (`engagement-signal`) | cost vs 5% of baseline frame; `NeedsPersistence` |
| | `PeakContributorToSpikeDetector` | `PeakContributorToSpike` (9) | **live** | top-mod share of a spike's per-mod snapshot |
| | `GcPauseCulpritDetector` | `GcPauseCulprit` (5) | **live** (needs alloc + stalls) | top-mod alloc share in 60-tick pre-GC window |
| **Temporal** (later vs earlier; controls for workload) | `SustainedCostShiftDetector` | `SustainedCostShift` (6) | **live** (needs `TemporalBaseline.IsReady`) | early vs late per-mod cost; `SessionFirstHalf` |
| | `NewContributorDetector` | `NewContributor` (8) | **live** (needs `TemporalBaseline.IsReady`) | idle-early → active-late per-mod; `SessionFirstHalf` |
| | `HeapLeakDetector` | `HeapLeak` (20) | **live** (needs `TemporalBaseline.IsReady`) | late vs early heap, **controlling for entity ratio** |
| **Distribution** (frame-time shape) | `FrameJitterDetector` | `FrameJitter` (19) | **live** (needs calibrated baseline) | robust CV = MAD/median; `None` |
| **Headroom** (budget remaining) | `FrameHeadroomDetector` | `FrameHeadroom` (17) | **live** (needs calibrated baseline) | median frame vs 16.67 ms 60 fps ceiling; `None` |
| **Structure** (cross-mod relationships) | `CostConcentrationDetector` | `CostConcentration` (18) | **live** | Pareto count of mods carrying ≥70% of cost; `None` |
| | `ContextConditionalCostDetector` | `ContextConditionalCost` (2) | **live** (needs `ContextBaseline`) | in-context vs out-of-context per-mod cost; `ComparableContexts` |
| | `ContextCorrelatedSpikeDetector` | `ContextCorrelatedSpike` (1) | **live** (needs `ContextBaseline`) | context spike-share vs dwell-share; `ComparableContexts` |
| **Segment / loadout** (own family in practice, README folds them into Structure/Temporal) | `SegmentOutlierDetector` | `SegmentOutlier` (14) | **live** (needs `SegmentStore`) | a segment vs lifetime avg for its (family,key); `LifetimeData` |
| | `SegmentTopModDetector` | `SegmentTopMod` (15) | **live** (needs `SegmentStore`) | mod's #1-rank frequency across a segment class; `LifetimeData` |
| | `SegmentDeathCorrelationDetector` | `SegmentDeathCorrelation` (16) | **live** (needs `SegmentStore`) | death-containing vs clean segment ms/t; `ComparableContexts` |
| | `LoadoutCorrelatedCostDetector` | `LoadoutCorrelatedCost` (11) | **live** (needs `Database`) | cost before vs after a loadout change |
| | `EventConditionalCostDetector` | `EventConditionalCost` (12) | **live** (needs `Database`) | in-buff-window cost vs session baseline |
| | `LoadoutCombinationCostDetector` | `LoadoutCombinationCost` (13) | **gated** (`cross-session-loadout-aggregation`) | synergy claim; emits nothing |
| | `HookFrequencyTailDetector` | `HookFrequencyTail` (10) | **gated** (`per-hook-call-counts`) | per-hook p99/median tail; emits nothing |

Three detectors are **gated** (`IsGated == true`): `FreeRemovalCandidate` (`engagement-signal`), `LoadoutCombinationCost` (`cross-session-loadout-aggregation`), `HookFrequencyTail` (`per-hook-call-counts`). The remaining 13 are live, several with an `IsAvailable` precondition that holds only once its input lands (a calibrated baseline, a ready `TemporalBaseline`, allocation tracking, a `SegmentStore`, or a `Database`).

Note `GatedDetectors.cs` is now almost entirely a set of "moved to its own file" comments (`Insights/Detectors/GatedDetectors.cs:23-38`); the only class still defined there is `HookFrequencyTailDetector`. Gated detectors are registered only so `GatedPatterns()` / `GatedLabel` can honestly report the coverage gap.

### Pattern keys (stable numeric values — never reorder)

```
ContextCorrelatedSpike=1  ContextConditionalCost=2  HotHookDominance=3  AllocationBurst=4
GcPauseCulprit=5  SustainedCostShift=6  FreeRemovalCandidate=7  NewContributor=8
PeakContributorToSpike=9  HookFrequencyTail=10  LoadoutCorrelatedCost=11
EventConditionalCost=12  LoadoutCombinationCost=13  SegmentOutlier=14  SegmentTopMod=15
SegmentDeathCorrelation=16  FrameHeadroom=17  CostConcentration=18  FrameJitter=19  HeapLeak=20
```

Stable across schema bumps (`Insights/Insight.cs:22-48`). The `CrossCuttingSignalStat` emits `PatternKey.ToString()` as its `SignalClass`, so the enum-name stability is load-bearing for the dashboard.

### The reference-frame substrate (the big v0.22 addition)

The engine carries two reference frames, both fed once per `Evaluate` (1 Hz, off-thread) so they add **no per-tick cost** (Invariant 2). `UpdateContextBaseline` runs first in every pass (`Insights/InsightsEngine.cs:215, 240-283`) so detectors compare against an up-to-date baseline.

**`ContextBaseline` (Family A — `Insights/ReferenceFrames/ContextBaseline.cs`).** Per game-context, per mod, the running distribution of that mod's cost while the context was active, plus the global per-mod distribution and per-context spike/dwell counts. Bounded by construction: `MaxBuckets = 16` contexts × modCount `RunningStat`s (24 B each ≈ 58 KB for a 150-mod stack); the least-sampled bucket evicts and `Evictions` surfaces the drop. Context buckets are derived **only from vanilla surfaces** (Invariant 5): hardmode, any-boss-present, the active vanilla invasion, the subworld (`DimHardmode=1`, `DimBoss=2`, `DimInvasion=3`, `DimSubworld=4` at `Insights/InsightsEngine.cs:287-290`). A bucket is an opaque `long` (`MakeBucket(dim, value)`); the frame never interprets game state, keeping it pure logic. `TryConditional` returns the in-context distribution and its complement (`global.Without(inContext)`) only when both sides clear `MinSamplesForTest = 30` — the first p-hacking guard.

**`TemporalBaseline` (Family B — `Insights/ReferenceFrames/TemporalBaseline.cs`).** Splits the session into a frozen EARLY window (first `EarlySamples = 120` 1 Hz samples ≈ 2 min) and a LATE window, accumulating heap, entity count, and per-mod cost in each via `RunningStat`. `IsReady` once both windows clear `MinPerSide = 60`. It carries the entity-count distribution alongside heap precisely so `HeapLeakDetector` can **control for the temporal confound**: heap up at constant entity count is a leak; heap up with entity count is progression.

**Drivers (`Insights/Drivers/Drivers.cs`).** `EntityCountDriver` (NPCs + projectiles), `SessionAgeDriver` (ticks/3600), `HeapDriver` (managed-heap MB). They sample an `IInsightInput` (pure logic). The engine holds static `_heapDriver` / `_entityDriver` instances and feeds the `TemporalBaseline` their samples each pass (`Insights/InsightsEngine.cs:280-282`). `IDriver` exists so Family E (Scaling) can regress cost against a driver and Family B can control for one.

**Statistical guards (`Insights/Shared/Stats.cs`).** `RunningStat` is Welford-online (O(1)/sample, no stored points) with `Merge` (Chan's parallel algorithm) and `Without` (the reverse, recovering a complement). `Stats.CohensD` is the pooled effect size; `Stats.WelchTTestP` is the two-sided Welch t-test (normal-approximated via the Abramowitz–Stegun erf). Every multi-comparison detector applies **Bonferroni correction** by the number of tests actually run that pass (`pAdjusted = min(1, p · testsRun)` in `ContextConditionalCost`, `ContextCorrelatedSpike`, `SustainedCostShift`, `NewContributor`), so sweeping every (context, mod) pair cannot manufacture significance. The two-line guard is: candidate-gate by real co-occurrence + a large effect (Cohen's d ≥ 0.8) before any test, then correct the p-value.

**Cross-session persistence (`Insights/ReferenceFrames/CrossSessionStore.cs`).** Persists the per-context per-mod baselines to the LiteDB `contextBaselines` collection (`ContextBaselineRow`), keyed by the **machine/modlist fingerprint** (`ModlistFingerprint.Compute()`), so a baseline only ever combines runs of the same stack. `Load` seeds a fresh `ContextBaseline` from prior rows (marking it `WasSeeded`, which lets `ContextConditionalCostDetector` badge `LifetimeData` instead of `ThisSession`). `Save` replaces the fingerprint's rows when the frame was seeded (it already holds the lifetime total) and otherwise merges via the Welford `Merge` so a prior baseline is never silently overwritten. This is the durability layer that lets confidence climb past Low.

### `Confidence` promotion gated by `PValueAdjusted`

`Preliminary=0 → Low=1 → Medium=2 → High=3`. Promotion (`InsightStore.PromoteConfidence`, `Insights/InsightStore.cs:231-240`):

```csharp
if (confirmationCount >= 4 && pAdjusted <= 0.05) return Confidence.High;
if (confirmationCount >= 3 && pAdjusted <= 0.10) return Confidence.Medium;
if (confirmationCount >= 2) return Confidence.Low;
return Confidence.Preliminary;
```

A record with `PValueAdjusted = 1` (detector ran no hypothesis test) can never reach Medium by repetition alone. The Family-A/B detectors (`ContextConditionalCost`, `ContextCorrelatedSpike`, `SustainedCostShift`, `NewContributor`, `HeapLeak`) emit real corrected p-values, so they can now climb past Low once confirmed — the gap the older doc flagged ("no live detector reaches Medium") is closed for the statistical detectors. The share/structural patterns (`HotHookDominance`, `AllocationBurst`, `PeakContributorToSpike`, `GcPauseCulprit`, `CostConcentration`, `FrameHeadroom`, `FrameJitter`, segment patterns) deliberately stay at `PValueAdjusted = 1` — they are descriptive observations, not hypothesis tests, and the honesty contract keeps them at Low/Preliminary.

### `EvidenceScope`, `Audience`, `BaselineKind`, `MagnitudeShape`

- **`EvidenceScope`** (`Insights/Insight.cs:73-78`): `ThisSession=0` / `LifetimeData=1` / `NeedsPersistence=2`. Set per-record, orthogonal to `Confidence`. `ContextConditionalCost` flips to `LifetimeData` when its frame `WasSeeded`; the segment detectors emit `LifetimeData`; `FreeRemovalCandidate` emits `NeedsPersistence`.
- **`Audience`** (`Player=0` / `Modder=1` / `Both=2`): selected at detect time as `DefaultAudience`; demoted ×0.5 in the scorer for `Modder`.
- **`BaselineKind`** (`Insights/Insight.cs:92-101`): `SessionMean`, `RollingFiveMinute`, `PreContext`, `ComparableContexts`, `SessionFirstHalf`, `PerModRollingMean`, `None`. Rendered as a "compared to …" clause (`InsightRenderer.BaselineClause`) so a reader can argue with the comparison.
- **`MagnitudeShape`** (`Insights/Insight.cs:166-180`): `Deviation` / `Share` / `Rate` / `Scaling` / `Headroom` / `Distribution`. The `Magnitude` struct (`Insights/Insight.cs:191-229`) carries shape-specific field blocks so a Temporal/Distribution/Headroom insight carries its honest number instead of being squeezed into a ratio. `Scaling` (Family E regression) is declared but no detector currently emits it.

### Pattern-aware ranking

`RankingScorer.Score` (`Insights/RankingScorer.cs:43-58`) is the weighted sum `0.30·magnitude + 0.25·confidence + 0.15·recency + 0.15·actionability + 0.10·novelty + 0.05·audience`. `NormaliseMagnitude` splits regimes via `IsSharePattern` (`Insights/RankingScorer.cs:86-101`): share patterns (`HotHookDominance`, `AllocationBurst`, `PeakContributorToSpike`, `GcPauseCulprit`, `CostConcentration`, `FrameHeadroom`, `FrameJitter`) store a `[0,1]` fraction and pass through `ClampUnit`; ratio patterns use the soft-knee curve at 10× (`1×→0`, `2×→~0.11`, `5×→~0.44`, `10×+→1`). The set has grown since the original doc to cover the Wave-5 structural patterns. Pinned by `Tests/RankingScorerTests.cs`.

### `InsightStore` lifecycle

`Submit` dedups on a full-width `InsightKey(Pattern, Kind, ModId, HookId, ContextKey, ContextDim)` value-equality key (`Insights/InsightStore.cs:217-221`) — the prior 64-bit-packed key collided once a mod exceeded 65k hooks (gap G6, now closed). A matching live entry refreshes magnitude/evidence/audience in place, increments `ConfirmationCount`, re-runs `PromoteConfidence`, and invalidates the rendering cache. New entries evict the stalest live record past `LiveCap = 32`. `Tick(nowTick)` evicts to history past `DefaultTtlTicks = 60·60·5` (≈5 min). `TopInto` ranks under a `_topGate` lock with a Sort-local comparison tick (gap G5: the prior shared `_topComparerNowTick` scalar raced the HTTP exporter against the eval thread — **now fixed**, the lock + local closure replace it). `PerPatternCap = 2`. Allocation-free past warmup; pinned by `Tests/InsightStoreTests.cs`.

### Shared singleton and lifecycle wiring

`InsightsEngine.Shared` is a `Volatile`-read/write static (`Insights/InsightsEngine.cs:47-52`); `GetOrCreateShared()` uses `Interlocked.CompareExchange` so two concurrent first-callers see the same instance (`Insights/InsightsEngine.cs:61-68`) — the prior `??=` could race two `new InsightsEngine()` allocations and orphan one (**now fixed**). `ProfilerSystem` drives the whole lifecycle:

- **World-load seed** (`Profiling/ProfilerSystem.cs:288-291`): `CrossSessionStore.Load(...).SeedContextBaseline(...)` seeds this session's frame from the lifetime total for this fingerprint.
- **Off-thread evaluation** (`Profiling/ProfilerSystem.cs:517-543`): `PostUpdateEverything` spawns `engine.Evaluate(...)` on the thread pool, latched by `_insightsEvalInflight` (an `Interlocked.CompareExchange` single-slot guard) so a pass can never overlap itself; a thrown `Evaluate` is caught and logged via `Mod.Logger.Warn` (dual-surface), and the engine drops that pass.
- **World-unload save + clear** (`Profiling/ProfilerSystem.cs:361-447`): the baseline + fingerprint are captured *before* `InsightsEngine.Shared = null`, then `CrossSessionStore.Save(...)` persists the lifetime total on a background path, so the save survives the singleton teardown that drops session state.

## Key Interfaces / Data Flow

```
ProfilerSystem.PostUpdateEverything (~1 Hz, off-thread, _insightsEvalInflight latch):
    engine = InsightsEngine.GetOrCreateShared()
    engine.Evaluate(collector, latestTick, historyDepth):
        UpdateContextBaseline(collector)              // feed ContextBaseline + TemporalBaseline (1 Hz)
        for each detector in _detectors:
            if det.IsGated or !det.IsAvailable(collector): skip
            det.Evaluate(collector, nowTick, sessionLengthTicks, _scratch)
            for each rec in _scratch: _store.Submit(rec, nowTick)
        _store.Tick(nowTick)                          // TTL eviction

Dashboard read (Web/DashboardRouter.Insights.cs):
    /api/insights         → InsightsStat.CurrentSnapshot() → engine.Store.AllLive()
                            → InsightRenderer.Render(rec, Player, Short|Medium) + Confidence/Scope badges
    /api/mod-observatory  → ModObservatoryStat       (I1+I3+I4: roster + usage + CPU + loadout)
    /api/dormant          → DormantSurfaceStat        (I2)
    /api/cross-cutting    → CrossCuttingSignalStat    (I5: groups Store.AllLive() by PatternKey)
    /api/engagement-cost  → EngagementCostScatterStat (I6)
    /api/mod-interaction  → ModInteractionAggregator  (I7: Pearson matrix over per-mod cost series)

World-load:   CrossSessionStore.Load(contextBaselines, fingerprint, modCount) → SeedContextBaseline
World-unload: CrossSessionStore.Save(contextBaselines, fingerprint, capturedBaseline)
```

The seven `Publish/` stats register through `DataRegistry` (`IDataStat` / `IDataAggregator`, `Cadence = OnDemand`, computed on dashboard pull). `InsightsStat`, `CrossCuttingSignalStat` read `InsightsEngine.Shared` directly; the rest compose foundation streams (roster F1, usage F2, `HookCpuSnapshot`, `ModCostTimeSeries`) via `DataRegistry.Lookup`, never direct refs. All read paths take snapshots (`AllLive()` enumerates the live dict; `TopInto` sorts under lock); only `Evaluate` writes.

## Implemented Outputs / Artifacts

| Surface | Source |
|---------|--------|
| `/api/insights` ranked record rows (shortText/mediumText + Confidence + Scope + pattern badges) | `InsightsStat` → `Store.AllLive()` → `InsightRenderer.Render` |
| `/api/cross-cutting` pattern-grouped mod leaderboards | `CrossCuttingSignalStat` (groups `Store.AllLive()` by `PatternKey`) |
| `/api/mod-observatory`, `/api/dormant`, `/api/engagement-cost`, `/api/mod-interaction` | the `Publish/` composite stats (I1–I7) |
| `GatedLabel` ("gated detectors waiting on: …") | `BuildGatedLabel` (cached at construction) |
| Persisted `contextBaselines` rows (Welford components per fingerprint/dim/key/mod) | `CrossSessionStore.Save` → LiteDB |
| Agent surface | `Mod.Logger.Warn` on a failed `Evaluate` pass; the JSON-lines session files (pipeline-level) |

## Known Issues / Active Risks

- **Most descriptive detectors emit `PValueAdjusted = 1` by design.** The share/structural/segment patterns run no hypothesis test, so they sit at Low/Preliminary forever. This is correct under the honesty contract (untested observations stay weak), but it means the dashboard's typical row is Low-confidence; the palette must keep Low readable, not de-emphasised. The statistical detectors (context + temporal families + heap-leak) DO compute corrected p-values and can reach Medium/High.
- **`ContextBaseline` spike attribution is a 1 Hz approximation** (`Insights/ReferenceFrames/ContextBaseline.cs:75-92`): a spike is credited to the context live at the pass that first sees it — exact unless the context changed within that ~1 s window. `ContextCorrelatedSpikeDetector` inherits this; its copy is honest about it.
- **`ContextBaseline` bucket eviction is silent past the cap.** `Evictions` is exposed but nothing currently logs it; a >16-context session drops its least-sampled bucket without an agent-surface warning.
- **`UpdateContextBaseline` allocates one `CollectorInsightInput` per pass** (`Insights/InsightsEngine.cs:281`). At 1 Hz off-thread this is negligible against the budget, but it is a small per-pass heap allocation in a profiler designed to measure exactly that.
- **`Scaling` magnitude shape (Family E regression) is declared but unemitted.** `MagnitudeShape.Scaling` and the `Slope`/`Intercept`/`CliffAt` fields exist; no detector produces a cost-vs-driver regression yet, so `IDriver`'s scaling use is latent.

## Partial / In Progress

- **The per-insight LiteDB `insights` collection is scaffolded but unfed.** The collection (`ProfilerDatabase.Insights`), the row (`InsightRow` with `PatternKey`/`Confidence`/`EvidenceScope`), the stream (`Data/Streams/InsightStream.cs`), the op kind (`DbOpKind.Insight`, `DbWriteOp.Insight`), and the indexes all exist — but **no producer enqueues a `DbWriteOp.Insight`** (verified: the only reference to `DbWriteOp.Insight(` is the stream's own `Reconstruct`). The live insight feed reaches the dashboard purely in-memory via `InsightsSnapshot`/`Store.AllLive()`; nothing currently persists individual insight records or `GatedPatterns()` to LiteDB. The cross-session persistence that IS live is `contextBaselines` (the reference-frame substrate), not per-insight rows.
- **`FreeRemovalCandidateDetector` stays gated** on `engagement-signal`. Its `Evaluate` is fully implemented (relative-epsilon cheap-cost detection, `Scope = NeedsPersistence`) and runs against the day the gate clears, but `Evaluate` is never called while `IsGated` is true.
- **`LoadoutCombinationCostDetector` and `HookFrequencyTailDetector`** are registered stubs awaiting `cross-session-loadout-aggregation` and `per-hook-call-counts` respectively; both `Evaluate` to nothing.

## Planned / Missing / Likely Changes

- **Wire the `insights` LiteDB persistence path.** A producer that snapshots `Store.AllLive()` / `Store.History` / `GatedPatterns()` into `InsightRow`s at session boundaries would make the scaffolding live and give the engine a true `LifetimeData` insight history (distinct from the baseline history that already persists).
- **A Family-E (Scaling) detector** that regresses per-mod cost against `EntityCountDriver` and reports `Slope`/`Intercept`/`CliffAt` — the only declared family with no live detector.
- **An agent-surface log for `ContextBaseline.Evictions`** so >16-context sessions report the dropped bucket rather than truncating silently.
- **The two remaining gates** (`engagement-signal`, `per-hook-call-counts`) open `FreeRemovalCandidate` and `HookFrequencyTail`.

## Durable Notes / Discarded Approaches

- **`RankingScorer.NormaliseMagnitude` once collapsed every share through the ratio curve.** A share value of 0.42 was read as a 0.42× ratio (below baseline 1.0) and clamped to 0, so a 40% and a 90% contributor ranked identically — the strongest live signal erased. Fixed by the `IsSharePattern` split (`Insights/RankingScorer.cs:77-101`). Re-verified: still true and still the design; the share set has since grown to include the Wave-5 structural patterns. Pinned by `Tests/RankingScorerTests.cs`.
- **`InsightStore.PromoteConfidence` did not gate on `PValueAdjusted`.** A `PValueAdjusted = 1` record could reach Medium by re-firing three times — a direct honesty-contract violation. The `pAdjusted ≤ 0.10` (Medium) / `≤ 0.05` (High) clauses are the fix (`Insights/InsightStore.cs:231-240`). Re-verified: still in force. Pinned by `Tests/InsightStoreTests.cs`.
- **`InsightsEngine.Shared` race.** The plain `??=` form could allocate two engines and orphan one; replaced with `Volatile` + `Interlocked.CompareExchange` (`Insights/InsightsEngine.cs:48-68`). The dual-surface motivation survives: both the dashboard and the persistence/recorder paths read the same store. Re-verified: accurate and hardened.
- **`InsightStore` dedup-key collision (gap G6).** The prior key packed mod/hook/context ids into a 64-bit `long` with 16-bit slots, colliding past 65k hooks per mod. Replaced by the full-width value-equality `InsightKey` record struct that also folds in `SubjectKind`, so a `Session` subject and a `Mod` subject with the same `-1` ids never alias (`Insights/InsightStore.cs:209-221`). Re-verified: closed.
- **`TopInto` shared-comparison-tick race (gap G5).** The prior `_topComparerNowTick` scalar field let a second concurrent `TopInto` caller read a torn tick. Replaced by a `_topGate` lock with a Sort-local closure capture (`Insights/InsightStore.cs:150-179`). Re-verified: closed — this supersedes the "watch item" the old doc carried.
- **The Flute-reads-zero usage bug (Wave 4).** `ModMetrics.UsageWeight` was once `ItemsCreated + NpcsSpawned + …` — content *created or encountered*, which read a wielded-but-not-crafted weapon as unused and collapsed a craft-nothing session to 0% usage. It is now `ItemsHeldTicks + AccessoryEquippedTicks + ArmorEquippedTicks + TicksInOwnedBiomes` — active-use ticks (`Insights/Shared/ModMetrics.cs:43-54`). The creation count survives as the distinct `CreationWeight`. `DormantSurfaceStat` correspondingly switched from a used/roster fraction to a normalised active-use intensity (`Insights/Publish/DormantSurfaceStat.cs:55-58`).
- **`SegmentDeathCorrelationDetector` emit-time confidence was silently overwritten.** An earlier version set a confidence tier at emit; `Submit` re-derives it via `PromoteConfidence`, so the emit value was dead. Detectors now uniformly emit `Confidence.Preliminary` and let the store promote (`Insights/Detectors/SegmentDeathCorrelationDetector.cs:117-121`). Re-verified across the roster: every detector emits `Preliminary`.
- **Gated-map rebuilt per frame.** `BuildGatedMap` once ran inside the in-game tab's `Draw`, allocating a `Dictionary` + `List` 60×/sec; now cached once at construction (`Insights/InsightsEngine.cs:140-141`). Re-verified: still cached.
- **LINQ chains in the loadout/event detectors.** `LoadoutCorrelatedCost` / `EventConditionalCost` replaced ~30–50 KB/pass of LINQ-iterator + `List<T>` garbage with explicit foreach loops + field-cached scratch (`Insights/Detectors/InteractionInsightDetectors.cs:26-28, 137-141`) — the profiler must not generate the garbage it measures. The same zero-alloc discipline produced the field-cached `_perModBytesScratch` / `_modBytesScratch` in `AllocationBurst` / `GcPauseCulprit`.

## Obsolete / No Longer Relevant

- **`Data/Detectors/Insights/` no longer exists.** The whole subsystem was consolidated into the top-level `Insights/` module during the v0.13→v0.22 arc. Any reference to `Data/Detectors/Insights/`, `InsightRecord.cs`, or the "four live / six gated across 10 pattern keys" roster describes a retired layout.
- **The "no live detector reaches Medium/High" risk is half-retired.** It still holds for the descriptive share/structural patterns by design, but the statistical context/temporal/heap detectors now emit corrected p-values and can climb.
- **The `_topComparerNowTick` watch item is closed** (gap G5 fix above).

## Cross-references

- `systems/web-dashboard.md` — the live surface: `/api/insights` (back-compat, `InsightsStat`) + the five `Publish/`-backed endpoints (`/api/mod-observatory`, `/api/dormant`, `/api/cross-cutting`, `/api/engagement-cost`, `/api/mod-interaction`). The in-game `InsightsTab` is archived under `UI/Overlay/Tabs/`.
- `systems/persistence.md` — LiteDB: the **fed** `contextBaselines` collection (`CrossSessionStore` + `ContextBaselineRow`, keyed by modlist fingerprint) and the **scaffolded-but-unfed** `insights` collection (`InsightRow` / `InsightStream` / `DbOpKind.Insight`).
- `systems/data-pipeline.md` — the smoothed/aggregated snapshots the detectors read via `MetricCollector` / `IInsightInput` and the foundation streams the `Publish/` stats compose (roster, usage, `HookCpuSnapshot`, `ModCostTimeSeries`).
- `systems/metric-collection.md` — what `det.Evaluate(collector, …)` and `CollectorInsightInput` read.
- `systems/spike-detection.md` — `PeakContributorToSpikeDetector` reads `collector.Spikes`; `ContextBaseline.ObserveSpikes` attributes spikes to contexts; `GcPauseCulpritDetector` reads `collector.Stalls`.
- `Tests/RankingScorerTests.cs`, `Tests/InsightStoreTests.cs`, `Tests/Insights/` (`SharedPrimitivesTests`, `TemporalBaselineTests`, `CrossSessionStoreTests`, `ReferenceFrameTests`) — pin the audit findings and the reference-frame maths.

## The 2026-07-07 detectors (0.30.0–0.32.0)

- **SustainedSlowness (PatternKey 25)** — the level detector paired with the
  variance set: fires when RealtimeSpeed < 90% held ≥ 30s; copy: "game time is
  advancing at 51% of real-time speed and has been for 4m 12s…", naming the
  costliest mods WHILE slowed (never a cause — draw attribution arrived only
  with S01). Pure core (`SustainedSlownessCore`) is test-linked.
- **FrameHeadroom reworked (X1).** Reads `UpdateWindowEmaMs` (the baseline
  median is real-cadence now and pins at ~16.67 under vsync — useless for
  headroom) and emits ONLY at `RealtimeSpeedNow ≥ 0.98`
  (`RealtimeSpeed.FullSpeedGate`). Mutually exclusive with SustainedSlowness
  by construction. Copy names the uncovered surface (draw cost).
- **DrawBoundMod (PatternKey 26, S01)** — ≥1 ms/t total AND ≥60% draw share,
  top-3 by cost: "X spends 72% of its 7.4 ms/t in the draw phase — draw cost
  shows as render load, not game speed." Silent when phase lanes are off.
  Pure core (`DrawBoundModCore`) is test-linked.
- Registration surface for a new detector: `PatternKey` enum → engine list →
  `InsightRenderer` switch + template → `RankingScorer.IsSharePattern` (when
  the magnitude is a [0,1] share).
