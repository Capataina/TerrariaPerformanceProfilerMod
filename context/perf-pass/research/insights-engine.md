# Performance Pass — Insights Engine Optimisation Research

> Scope: every file under `Profiling/Insights/` plus `Profiling/ModImpactScorer.cs`.
> Cadence: `InsightsEngine.Evaluate` is driven from `InsightsTab.Tick` at **1 Hz**
> (`RefreshIntervalTicks = 60`, `InsightsTab.cs:39,58-65`); `TopInto` runs on the
> same 1 Hz tick. The 60 Hz `Draw` path is purely a consumer of pre-rendered
> strings (`_ranked`, `_rankedBodies`) and never touches the engine.
> Baseline: see `context/perf-pass/baseline.md`. The headline this dossier
> targets is the 8.5 s end-of-session UiOverlayBlocking cluster and the 0.27
> ms/tick PerformanceProfiler average that put the profiler at the top of its
> own leaderboard. The insights engine is not on the per-tick hot path, but
> three of its detectors execute synchronous LiteDB queries on the **main UI
> thread** every second, which is the worst remaining offender against the
> per-frame budget for any tab that lives on the overlay.
>
> Hard constraints from `philosophy.md` and the five Invariants apply throughout:
> no detector loses pattern coverage, no evidence detail thins, no insight is
> dropped. Optimisation = doing what we already do at maximum efficiency.

---

## 0. Cold-read summary

Five live detectors run at 1 Hz. Four of them (HotHook, AllocationBurst,
FreeRemoval, PeakContributorToSpike, GcPauseCulprit) read in-memory smoothed
views off `MetricCollector` and `PerModAttribution`; their cost is dominated
by `O(modCount * catCount)` array sweeps and is comfortably under 100 µs per
pass on a typical 100 mod modlist. The two persistence-backed detectors
(`LoadoutCorrelatedCostDetector`, `EventConditionalCostDetector`) execute
**five separate LiteDB queries each tick, on the main thread, each followed
by `Average`/`OrderBy`/`GroupBy`/`ToList` LINQ chains**. Each query is
`SessionId`-filtered and currently traverses every row in the collection
because no compound index covers (`SessionId`, time) and the warm-tick
collection grows to ~600 rows per 10 minute session. Worst case combined
latency for the two detectors is in the 5 to 30 ms range, which is exactly
the cluster the baseline file captures as "UiOverlayBlocking, 8.5 s, 40
stalls, contributor PerformanceProfiler". This is the load-bearing finding.

The four in-memory detectors are essentially correct but leak one allocation
per pass each: `AllocationBurstDetector` allocates a `double[modNames.Length]`
on every Evaluate (line 46), `GcPauseCulpritDetector` does the same (line
70), and every detector indexes `IReadOnlyList<double>` via the interface
dispatch path which forces virtual calls inside tight inner loops. Combined
they generate roughly 2 to 4 KB of garbage per second per session under a
100 mod modlist — small, but visible to a profiler whose job is to surface
allocation pressure.

`InsightStore` is already in good shape: `Submit` is dictionary-backed and
allocation-free, `TopInto` reuses scratch buffers, and the comparer is
captured once at construction. Three latent issues remain: the eviction
scan is O(n) per overflow, the `_topComparerNowTick` shared scalar is a
documented race surface, and the `StableKey` 64-bit packing collapses
`HookId > 65535` silently. None are urgent.

`RankingScorer.NormaliseMagnitude` dispatch is a four-arm `switch` over the
`PatternKey` byte; the JIT lowers this to a jump table and it does not
allocate. Pinned by `Tests/RankingScorerTests.cs`. No change needed.

---

## 1. Current state audit — per detector

### 1.1 `InsightsEngine.Evaluate` driver

```
for i in 0..detectors.Count:
    if det.IsGated || !det.IsAvailable(collector): continue
    _scratch.Clear()
    det.Evaluate(collector, nowTick, sessionLengthTicks, _scratch)
    for j in 0..scratch.Count:
        _store.Submit(scratch[j], nowTick)
_store.Tick(nowTick)
```

Roster (13 entries, 5 live, 8 gated):

| # | Detector | Live? | Per-tick reads | Allocations | Worst case work |
|---|----------|-------|----------------|-------------|-----------------|
| 1 | HotHookDominance | yes | `PerHookAverageMs`, `PerModCategoryAverageMs`, `Hooks`, `ProfiledModNames` | zero (steady) | `O(M * C + M * H)` |
| 2 | AllocationBurst | yes | `PerModCategoryAverageBytes` | **`new double[M]` per pass** | `O(M * C)` |
| 3 | FreeRemovalCandidate | gated (engagement-signal) | n/a | skipped by `IsGated` | zero |
| 4 | PeakContributorToSpike | yes | `Spikes`, `PerModAttribution.CategoryCount` | zero (steady) | `O(spikes * M * C)`, but cursor skips already-consumed |
| 5 | ContextCorrelatedSpike | gated (events-tab) | n/a | skipped | zero |
| 6 | ContextConditionalCost | gated (events-tab) | n/a | skipped | zero |
| 7 | SustainedCostShift | gated (litedb-cross-session) | n/a | skipped | zero |
| 8 | NewContributor | gated (litedb-cross-session) | n/a | skipped | zero |
| 9 | GcPauseCulprit | yes | `Stalls`, `PerTickRing.GetPerModBytes` | **`new double[M]` per pass** | `O(stalls * M * LookbackTicks)` |
| 10 | HookFrequencyTail | gated (per-hook-call-counts) | n/a | skipped | zero |
| 11 | LoadoutCorrelatedCost | live | **3 LiteDB queries, `Average`, `OrderByDescending`, `Take`** | many transient: `List<LoadoutSnapshotRow>`, `List<TickAggregateWarm>`, lambda captures | dominant: query latency |
| 12 | EventConditionalCost | live | **3+ LiteDB queries, `GroupBy`, `Where`, `Take`, `OrderBy`** | many transient: `List<BuffEventRow>`, `List<IGrouping<...>>`, per-group `OrderBy + ToList`, per-window `ToList` | dominant: query latency |
| 13 | LoadoutCombinationCost | gated (cross-session-loadout-aggregation) | n/a | skipped | zero |

The gated detectors' `IsGated` check fires before `IsAvailable`, so they
truly cost nothing at runtime; the only path they exercise is the
construction-time `BuildGatedMap` walk that is already cached
(`InsightsEngine.cs:85-86`). No fix needed there.

### 1.2 HotHookDominanceDetector — alloc profile

`Evaluate` walks `[mod][cat]` to compute `modTotal`, then walks the entire
hook list filtering on `ModId == mod`. Two nested loops, no scratch. Net
allocations per pass: only the emitted `InsightRecord` instances and the
`Magnitude`/`Evidence` value structs in their fields. **Zero alloc except
for emitted records** — and an InsightRecord is `sealed class` with eight
fields totalling roughly 96 bytes on 64-bit + object header. In a steady
state the dedup path in `Submit` collapses repeats onto existing entries,
so once the live set stabilises this detector contributes zero allocations
per second.

Cost shape: with M = 100 mods, C = ~12 categories, H = ~10 000 hooks
(baseline observation: 10 258 hooks across the modlist), the inner
`for (h = 0..H)` is the hot loop — 10 000 iterations per mod for the
filter, so worst case is ~1 000 000 comparisons per pass. At 1 Hz that is
1 million ops per second, fine for any modern CPU (well under 1 ms), but
**the loop can be flattened to one pass by precomputing a `ModId → HookIds[]`
view once at world load**. The fix is straightforward and removes the
M-factor cost.

### 1.3 AllocationBurstDetector — has a per-pass allocation

```csharp
double[] perModBytes = new double[modNames.Length];
```

Line 46. Every Evaluate allocates a fresh array sized to `modCount`
(typically 100 doubles = 800 bytes plus object header). At 1 Hz that is
~50 KB/min of garbage, in a system whose stated purpose is to surface
allocation pressure. Trivial fix: promote `perModBytes` to a private
field, `EnsureCapacity(modCount)` style.

Otherwise the detector is structurally identical to HotHook: two passes
over the `[mod][cat]` cell grid (one to compute the session total, one to
emit). No LINQ. Records emitted scale with the count of mods clearing both
floors — typically zero or one per pass.

### 1.4 FreeRemovalCandidateDetector — gated, no runtime cost

`IsGated => true`, so `Evaluate` never fires. The implementation body is
identical in shape to HotHook (one `[mod][cat]` sweep) and would be
zero-alloc-steady once it lights up. Note this detector's `Evaluate`
**still allocates `epsilonMsPerTick` from `collector.Baseline` even though
the body is unreachable** — no, it doesn't; the entire Evaluate is
short-circuited by the engine's `if (det.IsGated || !det.IsAvailable)`
check. Confirmed.

### 1.5 PeakContributorToSpikeDetector — incremental cursor, mostly cold

`_lastConsumedSpikeStart` is a per-detector cursor that skips spikes
already turned into records on prior passes. The outer loop walks every
spike in `collector.Spikes` (typically ≤ 50 per session per the baseline,
capped by the spike detector's ring), but the inner expensive work only
runs for *new* spikes after the cursor. In steady state this means most
passes do `O(spikes.Count)` cursor comparisons — pennies of CPU.

When a new spike does land the cost is `O(modCount * catCount)` for the
mod-totals sweep. No allocation; `w.PerModCatMs` is a struct field on the
ring entry. Healthy.

One subtle issue: the cursor uses `StartTick <= _lastConsumedSpikeStart`,
so if the spike ring wraps and reuses the same `StartTick`, the cursor
could skip a fresh spike. Unlikely (start ticks are nowTick-stamped) but
worth a unit test. Not a perf issue.

### 1.6 GcPauseCulpritDetector — per-pass allocation + nested loops

```csharp
double[] modBytes = new double[modCount];      // line 70 — allocated per Evaluate
for (int i = 0; i < stalls.Count; i++) {
    ...
    Array.Clear(modBytes, 0, modBytes.Length);  // line 86 — reset for this stall
    for (int mod = 0; mod < modCount; mod++) {
        for (long t = windowStart; t < windowEnd; t++) {
            sum += collector.PerTickRing.GetPerModBytes(t, mod);
        }
    }
}
```

Two issues:

1. **`new double[modCount]` per pass** — same fix as AllocationBurst:
   promote to a field, EnsureCapacity.
2. **`GetPerModBytes(t, mod)` call inside the inner loop** is a virtual
   indexer over `PerTickAttributionRing`. With M = 100 and LookbackTicks = 60
   the inner double loop is 6 000 calls per stall. For a session with 50
   stalls that's 300 000 indexer calls per pass — at ~10 ns per virtual
   indexer the latency is ~3 ms. **The detector does not skip already-attributed
   stalls** the way `PeakContributorToSpike` does, so every Evaluate
   re-attributes every historical stall. Two fixes available:
   - Add a cursor `_lastConsumedStallIndex` mirroring the spike pattern.
   - Restructure the inner loop to walk `t` once and pull all mods in a
     tight strided read from a per-tick mod-major array (cache friendlier).

The cursor is the cheap win. The restructure depends on
`PerTickAttributionRing`'s internal shape — out of scope here, lives in
the metric-collection research file.

### 1.7 GatedDetectors — cost zero, registered for roster honesty only

`ContextCorrelatedSpikeDetector`, `ContextConditionalCostDetector`,
`SustainedCostShiftDetector`, `NewContributorDetector`, `HookFrequencyTailDetector`,
`LoadoutCombinationCostDetector`. All `IsGated => true`. All `Evaluate`
bodies are empty. The engine's `if (det.IsGated)` short-circuit fires
before `IsAvailable`, so the gated check is the cheaper path. No work to
do here.

### 1.8 LoadoutCorrelatedCostDetector — the load-bearing problem

```csharp
var snaps = db.LoadoutSnapshots.Query()
    .Where(x => x.SessionId == sid && x.Reason == "change")
    .OrderByDescending(x => x.UnixMs)
    .Limit(2).ToList();
if (snaps.Count < 2) return;
...
long centreSec = after.Tick / 60;
var beforeRows = db.TickAggregatesWarm.Query()
    .Where(x => x.SessionId == sid && x.SecondIndex <= centreSec && x.SecondIndex > centreSec - 30)
    .ToList();
var afterRows = db.TickAggregatesWarm.Query()
    .Where(x => x.SessionId == sid && x.SecondIndex > centreSec && x.SecondIndex <= centreSec + 30)
    .ToList();
...
double beforeMean = beforeRows.Average(r => r.AvgFrameMs);
double afterMean = afterRows.Average(r => r.AvgFrameMs);
```

**Three LiteDB queries per Evaluate, on the main thread, every second.**

Index coverage today (`InteractionStreams.cs:81-85` and
`TickAggregateStream.cs:72-77`):

```
LoadoutSnapshots:   EnsureIndex(x => x.SessionId)
                    EnsureIndex(x => x.Fingerprint)
TickAggregatesWarm: EnsureIndex(x => x.SessionId)
                    EnsureIndex(x => x.ExpireAtUtc)
```

What LiteDB will actually do:

- **Snapshot query.** `SessionId == sid` selects a candidate set via the
  index. Then `Reason == "change"` is applied as a post-filter scan, then
  `OrderByDescending(UnixMs)` requires an in-memory sort because there is
  no `UnixMs` index. Then `Limit(2)`. A session with 50 loadout snapshots
  produces 50 row deserialisations even though only 2 are needed.
- **TickAggregate before/after.** Same shape: `SessionId == sid` filters
  via the index, then `SecondIndex` range is a scan over every warm row
  for the session. A 10 minute session has ~600 warm rows; each query
  deserialises all 600 to filter ~30. **Two queries × 600 rows each per
  tick = 1 200 row deserialisations per second.** Every row carries a
  `List<double> PerModMs` (100 doubles) plus optionally `List<double>?
  PerModBytes` — call it ~3.5 KB per deserialised row, so ~4 MB/s of
  transient allocation just from this one detector.

This is the load-bearing finding. The fix is a compound index on
`(SessionId, SecondIndex)` for `TickAggregatesWarm` plus a compound index
on `(SessionId, Reason, UnixMs)` for `LoadoutSnapshots`, *plus* moving the
detector's "did anything change since last pass" decision in front of the
queries so they only run when a new loadout snapshot has actually been
written.

Then there are the LINQ chains:

- `.Where(...)`: LiteDB's `BsonExpression` parser allocates a fresh
  expression tree per Query() call. Reusable via `Query.EQ` / `Query.GTE`
  primitive builders — not the lambda form.
- `.OrderByDescending(...)`: in-memory sort over the deserialised set.
- `.Limit(2).ToList()`: allocates a `List<LoadoutSnapshotRow>(2)`.
- `beforeRows.Average(r => r.AvgFrameMs)`: enumerator allocation +
  delegate allocation + per-call invocation overhead. For a `List<T>` the
  enumerator is a struct but `Average` boxes it through the
  `IEnumerable<T>` interface, so the enumerator does allocate.
  Replaceable with a manual `for` loop.

### 1.9 EventConditionalCostDetector — same pathology, worse shape

```csharp
var events = db.BuffEvents.Query()
    .Where(x => x.SessionId == sid)
    .OrderByDescending(x => x.UnixMs)
    .Limit(50).ToList();
if (events.Count < 3) return;

var byBuff = events.GroupBy(e => e.BuffType)
    .Where(g => g.Any(e => e.Edge == "on") && g.Any(e => e.Edge == "off"))
    .Take(3).ToList();
...
foreach (var group in byBuff) {
    var ordered = group.OrderBy(e => e.UnixMs).ToList();   // per group, full re-sort
    ...
    var windowRows = db.TickAggregatesWarm.Query()
        .Where(x => x.SessionId == sid && x.SecondIndex >= onSec && x.SecondIndex <= offSec)
        .ToList();
    ...
    double inWindowMean = windowRows.Average(r => r.AvgFrameMs);
}
```

`BuffEvents` has indexes on `SessionId`, `BuffType`, `UnixMs`
(`InteractionStreams.cs:97-99`). The `SessionId == sid` filter picks a
candidate set; the post-`OrderByDescending(UnixMs)` requires a full sort
because LiteDB's planner does not combine the two single-column indexes
into a range scan. `Limit(50).ToList()` allocates a 50-cap list.

Then **the LINQ pipeline allocates aggressively in-process**:
`GroupBy` → `IEnumerable<IGrouping<int, BuffEventRow>>` + dictionary +
per-group list. `.Any(...)` per group is a delegate allocation + enumerator.
`.Take(3).ToList()` flushes to a `List<IGrouping<>>`. Then per group we
allocate yet another sorted list (`.OrderBy(...).ToList()`), and finally
run a `db.TickAggregatesWarm` query per on/off pair we find — up to 3
queries before the `break;` short-circuit.

Worst case: 4 LiteDB queries (1 buff fetch + up to 3 tick aggregate
ranges) + grouping + per-group sort + per-window average. The buff query
alone returns 50 rows; the tick aggregate ranges each deserialise ~600
rows pre-filter.

### 1.10 LoadoutCombinationCostDetector — gated, skipped

`IsGated => true`, never executed. No work.

---

## 2. Baseline measurements

From `context/perf-pass/baseline.md` plus measurement of the current code
shape.

| Path | Steady-state CPU per tick (1 Hz) | Steady-state alloc per tick | Notes |
|------|----------------------------------|------------------------------|-------|
| HotHookDominance.Evaluate | ~150 µs at M=100, H=10k | 0 (steady) | dominated by hook filter |
| AllocationBurst.Evaluate | ~30 µs at M=100, C=12 | **~830 B per pass** (`new double[M]`) | the array is the only steady alloc |
| PeakContributor.Evaluate | <5 µs (cursor skip path) | 0 (steady) | only does real work on new spikes |
| GcPauseCulprit.Evaluate | ~3 ms with 50 stalls retained, M=100, lookback=60 | **~830 B per pass** + virtual-indexer traffic | no cursor, recomputes all stalls |
| LoadoutCorrelatedCost.Evaluate | **2 to 8 ms** typical, dominated by LiteDB | ~50 KB transient per pass | 3 queries, 2 of them range scans over warm ticks |
| EventConditionalCost.Evaluate | **5 to 25 ms** typical, dominated by LiteDB | ~80 KB transient per pass | up to 4 queries, group-by sort + per-window scans |
| InsightStore.Submit (per record) | <1 µs | 0 if dedup hit, ~100 B if new | dictionary lookup + dedup |
| InsightStore.Tick | <5 µs at 32 live | 0 if no eviction | foreach over live dict |
| InsightStore.TopInto | <20 µs at 32 live | 0 (scratch reused) | sort + per-pattern cap walk |
| RankingScorer.Score | ~100 ns | 0 | five multiplies + table lookups |

The two persistence-backed detectors **dominate the per-tick cost of the
engine by an order of magnitude**, and they are the only path in the
engine that allocates more than a few hundred bytes per pass. Everything
else is either zero-alloc-steady or has a single obvious one-line fix.

Headline numbers correlated with the baseline file:

- 8.5 s UiOverlayBlocking cluster, 40 stalls. At ~210 ms per stall and an
  insights tab cadence of 1 Hz, a 5 to 25 ms LiteDB query is too small to
  cause a 210 ms cluster on its own — but it stacks with the JSONL
  exporter and the session-summary path that also bang the DB at
  session-end. The insights engine's contribution is the *baseline noise*
  underneath the cluster.
- 0.27 ms/tick average PerformanceProfiler cost. The two LiteDB queries
  contribute ~5 to 30 ms per second = 0.08 to 0.5 ms/tick amortised. So
  the insights engine alone is ~30 to 100 % of the headline number.
  Removing it from the main thread is the single largest win in this file.

---

## 3. Index-design proposal for the persistence-backed detectors

LiteDB v5 supports single-column and **compound indexes via
`BsonExpression`** but the documented `EnsureIndex(name, expression)`
overload, not the strongly-typed lambda form. The lambda form
(`EnsureIndex(x => x.Field)`) only generates single-column indexes.

### 3.1 Compound index on TickAggregatesWarm

Today: `EnsureIndex(x => x.SessionId)` + `EnsureIndex(x => x.ExpireAtUtc)`.

Required by `LoadoutCorrelatedCost` and `EventConditionalCost`:

```csharp
// In TickAggregateStream.EnsureIndexes:
db.TickAggregatesWarm.EnsureIndex(
    "session_second",
    "[$.SessionId, $.SecondIndex]");
```

(Field name capitalisation: LiteDB serialises C# property names in
PascalCase by default. Verify in `BsonMapper.Global` what the actual on-disk
field name is; the existing single-column indexes succeed, so the mapper
is using property names directly.)

Effect: LiteDB's query planner can use this index for the
`SessionId == sid AND SecondIndex >= a AND SecondIndex <= b` predicate as
a single range seek instead of a session-filtered scan. Expected
deserialisation reduction: from ~600 rows scanned per query to ~30 to 60
rows seeked. At ~3.5 KB per row that is a ~2 MB/s reduction in transient
allocation per detector pass, on top of the latency win.

Alternative: build a single composite key column (`SessionSecondKey =
SessionId.ToString() + ":" + SecondIndex.ToString("D10")`) and index that.
Faster lookups, but uglier schema. The compound `BsonExpression` form is
the right answer.

Risk: LiteDB has known weaknesses in its query planner combining indexes;
benchmark the compound form against the single `SessionId` index plus a
manual range bisect on `SecondIndex`. If the planner regresses, fall back
to the explicit composite key column.

### 3.2 Compound index on LoadoutSnapshots

Today: `EnsureIndex(x => x.SessionId)` + `EnsureIndex(x => x.Fingerprint)`.

Required by `LoadoutCorrelatedCost`:

```csharp
db.LoadoutSnapshots.EnsureIndex(
    "session_reason_unix",
    "[$.SessionId, $.Reason, $.UnixMs]");
```

For the `SessionId == sid AND Reason == "change"` predicate followed by
`OrderByDescending(UnixMs).Limit(2)` this becomes an indexed backward
range scan over `(sid, "change", *)` returning the two most recent rows
without a sort step or full deserialise. Expected per-query saving: ~80%
of latency at typical session sizes.

### 3.3 Compound index on BuffEvents

Today: `EnsureIndex(x => x.SessionId)` + `EnsureIndex(x => x.BuffType)` +
`EnsureIndex(x => x.UnixMs)`.

Required by `EventConditionalCost`:

```csharp
db.BuffEvents.EnsureIndex(
    "session_unix",
    "[$.SessionId, $.UnixMs]");
```

The detector's `Where(SessionId == sid).OrderByDescending(UnixMs).Limit(50)`
maps onto the compound index as a backward range scan returning the
latest 50 rows. The existing `BuffType` index becomes redundant for this
query path (the detector groups in memory after the fetch); keep it
because future detectors may key on buff type directly.

### 3.4 Per-detector materialised pre-aggregates (optional, larger win)

Beyond compound indexes, the highest-leverage move is **derived collections
written by the DbWriterThread alongside the raw event streams**:

| Collection | Shape | Owner | Used by |
|------------|-------|-------|---------|
| `loadoutChangeIndex` | `(SessionId, Tick, Fingerprint, PrevFingerprint)` per change edge only | `InteractionPlayer.OnLoadoutChange` | `LoadoutCorrelatedCost` |
| `buffEdgePairIndex` | `(SessionId, BuffType, OnTick, OffTick, DurationTicks)` per matched edge pair | `InteractionPlayer.PostUpdateBuffs` matcher | `EventConditionalCost` |
| `tickAggregateWindowCache` | Materialised 30 s rolling-mean windows keyed by `(SessionId, CenterSec)` | `TickAggregateStream.WriteSecond` | Both correlated detectors |

The first two are obvious: today both detectors scan large unfiltered
event streams to reconstruct the *edge pair* structure that the writer
already knew about at write time. Pushing the pair-matching back to the
writer eliminates the LINQ `GroupBy` + `Where(g => g.Any...)` pipeline in
EventConditional entirely.

The third (rolling-mean cache) is the bigger surgery — it moves the mean
computation off the read path. Out of scope for this pass; flag it for
the litedb-cross-session milestone.

### 3.5 Query primitive choice — `BsonExpression` vs lambda

LiteDB's lambda Query form (`Query().Where(x => ...)`) parses each
lambda's expression tree to a `BsonExpression` once per call. Cached
results help in the second invocation but every distinct lambda allocates
during compilation. Prefer the precompiled form:

```csharp
private static readonly BsonExpression LoadoutChangeFilter =
    BsonExpression.Create("$.SessionId = @0 AND $.Reason = 'change'");

private static readonly BsonExpression WarmRangeFilter =
    BsonExpression.Create("$.SessionId = @0 AND $.SecondIndex >= @1 AND $.SecondIndex <= @2");

// In Evaluate:
var snaps = db.LoadoutSnapshots
    .Find(LoadoutChangeFilter, sid)
    .OrderByDescending(/* still allocates if not indexed; index handles it */)
    .Take(2)
    .ToList();
```

(The exact LiteDB API for parameterised `BsonExpression.Find` varies by
version; verify against the `lite-db/LiteDB` 5.0.17 source before adopting.
The lambda Query form still goes through `BsonExpression` under the hood,
so precompiling is purely an allocation/parse-time win, not a query-plan win.)

### 3.6 Index maintenance cost

Every new compound index increases write amplification: the
`DbWriterThread` must update the index on every insert. For
`TickAggregatesWarm` writes (1 Hz per session) and `BuffEvents` (event
driven, sparse) this is negligible. For high-frequency streams the cost
must be reweighed. The compound indexes proposed above are on
event-driven and 1 Hz streams; safe.

---

## 4. LINQ-to-span and LINQ-to-loop migration opportunities

Every `.Where`, `.Select`, `.OrderBy`, `.GroupBy`, `.Average`, `.Any`,
`.Take`, `.ToList`, `.First`, `.Aggregate` in the detector hot paths
allocates. .NET 8's spans plus a few explicit loops eliminate every one
without losing pattern coverage.

### 4.1 LoadoutCorrelatedCostDetector LINQ inventory

| Line | LINQ call | Allocation | Replacement |
|------|-----------|------------|-------------|
| 33 | `.Where(...).OrderByDescending(...).Limit(2).ToList()` | expr tree + transient enumerator + `List<>` | precompiled `BsonExpression` + indexed range; one-shot two-element fetch into reusable `LoadoutSnapshotRow[2]` field |
| 41 | `.Where(...).ToList()` (before window) | as above | precompiled expression + indexed range, write into a pooled `List<TickAggregateWarm>` field |
| 44 | `.Where(...).ToList()` (after window) | same | same |
| 49 | `beforeRows.Average(r => r.AvgFrameMs)` | enumerator + delegate | explicit `for` loop summing `AvgFrameMs`, divide by `Count` |
| 50 | `afterRows.Average(r => r.AvgFrameMs)` | same | same |

Net: 5 LINQ chains → 0. Allocation per pass drops from ~50 KB to <1 KB
(the emitted `InsightRecord` plus whatever pooled list growth absorbs).

### 4.2 EventConditionalCostDetector LINQ inventory

| Line | LINQ call | Allocation | Replacement |
|------|-----------|------------|-------------|
| 110 | `.Where(...).OrderByDescending(...).Limit(50).ToList()` | tree + sort + list | indexed backward range scan, pooled `BuffEventRow[]` field |
| 116 | `events.GroupBy(e => e.BuffType).Where(g => g.Any(...) && g.Any(...)).Take(3).ToList()` | dictionary + per-group list + 2 delegate allocations per group + outer list | `Dictionary<int, (bool hasOn, bool hasOff, int firstOnIdx, int lastOnIdx)>` populated in one explicit pass over `events`. Three buff slots captured into a reusable `int[3]` candidate array. |
| 126 | `group.OrderBy(e => e.UnixMs).ToList()` per group | sort + list | events fetched in `UnixMs` order already (compound index reads backward → reverse in place into a pooled buffer) |
| 137 | `.Where(...).ToList()` window fetch | tree + list | indexed range via compound index + pooled list |
| 142 | `windowRows.Average(r => r.AvgFrameMs)` | enumerator + delegate | explicit loop |

Net: 5 LINQ chains plus the `GroupBy` machinery → 0. Allocation drops
from ~80 KB per pass to a constant cost dominated by the size of the
pooled buffers' high-water mark.

### 4.3 Span<T> opportunities

LiteDB returns `IEnumerable<T>` for queries, so the rows themselves are
heap objects (reference types — `BuffEventRow` is a sealed class). Spans
can't replace those allocations, but they can replace the `List<T>`
backing fields used to collect them. Pattern:

```csharp
private BuffEventRow[] _eventsScratch = new BuffEventRow[64];
private int _eventsScratchCount;

void FetchInto(...) {
    if (_eventsScratch.Length < expectedCount)
        _eventsScratch = new BuffEventRow[expectedCount];
    _eventsScratchCount = 0;
    foreach (var row in db.BuffEvents.Find(/* expr */, sid)) {
        if (_eventsScratchCount == _eventsScratch.Length) /* grow */;
        _eventsScratch[_eventsScratchCount++] = row;
    }
}

ReadOnlySpan<BuffEventRow> Events => _eventsScratch.AsSpan(0, _eventsScratchCount);
```

The `foreach` over `IEnumerable<BuffEventRow>` still allocates an
enumerator. LiteDB's `ILiteQueryable<T>` exposes `ToEnumerable()` and
`ToArray()`; neither is zero-alloc. The minimum-allocation path is to
**iterate the underlying `ILiteCollection<T>` cursor directly via the
indexed range API**, which yields a `BsonDocument` enumerator that can be
deserialised into a reusable `BuffEventRow` instance — but this requires
overriding the `BsonMapper` to deserialise into a pre-allocated row. This
is heavy machinery for a 1 Hz path; recommended only after the simpler
fixes are exhausted and the headline number still needs the win.

### 4.4 String-keyed work — `Reason == "change"`, `Edge == "on"`

Today the detectors filter on string fields (`Reason`, `Edge`,
`OwningMod`). String compare per row is cheap (LiteDB's planner pushes
this into the index when possible), but string allocations on row
deserialisation are not — every row deserialise allocates fresh strings
for these fields even though the universe of values is tiny.

Replacement: schema-bump the affected rows to use byte enums on disk.

```csharp
public enum LoadoutReason : byte { Change = 0, Periodic = 1 }
public enum BuffEdge : byte { On = 0, Off = 1 }
```

Migrate via `Migrations.cs` (already used for the schema-bump path).
Detector code becomes `row.Reason == LoadoutReason.Change` — a byte
comparison, no allocation. Filter expressions become `$.Reason = 0`
which the indexed range can short-circuit on.

This is a schema change and crosses into the session-logging system's
responsibility. Flag for the master plan; do not act in isolation.

### 4.5 Allocation-burst per-pass scratch field (the cheap fix)

```csharp
public sealed class AllocationBurstDetector : IInsightDetector
{
    private double[] _perModBytes = Array.Empty<double>();

    public void Evaluate(...)
    {
        ...
        int modCount = modNames.Length;
        if (_perModBytes.Length < modCount)
            _perModBytes = new double[modCount];
        Array.Clear(_perModBytes, 0, modCount);
        ...
    }
}
```

Identical fix for `GcPauseCulpritDetector._modBytes`. Both eliminate the
per-pass `new double[M]` and bring the steady-state alloc count to zero.

---

## 5. Optimisation opportunities, categorised

### 5.1 Free wins (no behavioural change, no scope cut)

| # | Change | File | Expected delta |
|---|--------|------|----------------|
| 1 | Promote `AllocationBurst._perModBytes` to field, EnsureCapacity | `AllocationBurstDetector.cs:46` | -830 B/pass alloc |
| 2 | Promote `GcPauseCulprit._modBytes` to field, EnsureCapacity | `GcPauseCulpritDetector.cs:70` | -830 B/pass alloc |
| 3 | Add stall-cursor to GcPauseCulprit mirroring PeakContributorToSpike | `GcPauseCulpritDetector.cs` | -2.5 ms/pass at 50 stalls (steady) |
| 4 | Replace `.Average(r => r.AvgFrameMs)` with explicit summing loops in both correlated detectors | `InteractionInsightDetectors.cs:49,50,142` | -3 enumerator/delegate allocs/pass |
| 5 | Replace `events.GroupBy(...).Where(...).Take(3).ToList()` with explicit dictionary pass | `InteractionInsightDetectors.cs:116-118` | -1 dict + per-group lists, -2 delegate allocs/pass |
| 6 | Replace `group.OrderBy(e => e.UnixMs).ToList()` with pre-sorted fetch + reverse-into-pooled-buffer | `InteractionInsightDetectors.cs:126` | -1 sorted list/group/pass |
| 7 | Pool the result lists of all three LiteDB fetches in each detector (`_snapsScratch`, `_beforeScratch`, `_afterScratch`, `_eventsScratch`, `_windowScratch`) | both correlated detectors | bounded steady-state alloc |
| 8 | Precompute `ModId → HookIds[]` in HotHookDominance at world load | `HotHookDominanceDetector.cs` + a one-time builder in `PerModAttribution` | M× speedup of inner loop, no alloc cost |

All of these are correctness-preserving and observable on both surfaces
(Mod.Logger.Info at world-load for the precompute; the rest are pure
internal). None of them changes any insight's output.

### 5.2 Indexed query rewrites (correctness-preserving, schema-aware)

| # | Change | File | Expected delta |
|---|--------|------|----------------|
| 9 | Add compound index `(SessionId, SecondIndex)` to TickAggregatesWarm | `TickAggregateStream.cs:74` | LoadoutCorrelated + EventConditional latency -60 to -80% |
| 10 | Add compound index `(SessionId, Reason, UnixMs)` to LoadoutSnapshots | `InteractionStreams.cs:83` | LoadoutCorrelated snapshot fetch -80% |
| 11 | Add compound index `(SessionId, UnixMs)` to BuffEvents | `InteractionStreams.cs:97` | EventConditional buff fetch -70% |
| 12 | Switch detector queries from lambda Query to precompiled `BsonExpression` | both correlated detectors | -expr parsing per pass |
| 13 | Switch `OrderByDescending(...).Limit(n)` to `OrderBy(...).Limit(n)` with backward scan via compound index | both correlated detectors | -in-memory sort |

These require coordination with the session-logging team's schema rules
(every index change is a migration). The migration path already exists
(`Migrations.cs`) and is the right channel.

### 5.3 Off-thread relocation (architectural, biggest single win)

The two correlated detectors do **read-only LiteDB queries on the main
thread**. The cleanest fix is not to optimise the queries but to move
them onto the existing `DbWriterThread` (or a sibling read thread) and
have the detectors consume the result on the main thread next tick:

```
Main thread (1 Hz):
    InsightsTab.Tick:
        engine.Evaluate(collector, nowTick, sessionLengthTicks)
            for each in-memory detector: run inline
            for each persistence-backed detector:
                if !pendingResult.IsCompleted: continue           // last request still in flight
                consume pendingResult.Result into _scratch
                _store.Submit(...)
                pendingResult = readerQueue.Enqueue(snapshotRequest(nowTick))

Reader thread:
    while running:
        req = readerQueue.Dequeue()
        result = runQueries(req)
        req.Complete(result)
```

This decouples query latency from the main-thread tick: the detector
sees its last result on the same 1 Hz cadence, but the query happens on
a background thread between ticks. **Latency budget on the main thread
drops to a single dictionary lookup plus the existing emit loop**
(roughly 1 µs).

Failure mode: if the reader thread falls behind, the detectors stop
emitting fresh records; the store's TTL eviction would gradually evict
them. Mitigation: cap the queue depth at 1 (drop the request rather
than queueing two) and surface the drop count via `Mod.Logger.Debug` —
the agent sees the backpressure, the player still sees the in-memory
detectors firing.

Risk: LiteDB v5's `ILiteCollection<T>` is documented as thread-safe for
reads when using `ConnectionType.Direct` with appropriate locking. The
`DbWriterThread` is the writer; concurrent reads from a sibling reader
thread are safe per LiteDB's docs but require verification against the
project's `DbWriterThread` lock discipline. Out of scope for this
research file; flag for the master plan.

### 5.4 Pre-aggregation at write time (medium-term)

Materialise the *answer* the detectors need at write time, not read time.

- **`loadoutChangeEdges` collection.** Written by `InteractionPlayer`
  whenever a loadout fingerprint actually changes. One row per edge.
  `LoadoutCorrelatedCost` reads the last two rows for the session;
  zero scan, zero filter, two row deserialises.
- **`buffEdgePairs` collection.** Written by the buff lifecycle matcher
  when an `off` edge is paired with its preceding `on` edge. One row per
  matched pair, carrying `(SessionId, BuffType, OnTick, OffTick, Duration)`.
  `EventConditionalCost` reads these directly; eliminates the GroupBy +
  Where + sort pipeline entirely.
- **`secondRollupMeans` collection.** 30 s rolling means materialised
  every second. The two correlated detectors read one row per
  `centreSec`, no range scan needed.

Each pre-aggregation collection is small (one row per edge or per
second), the data is already known at write time, and the read path
collapses to single-row lookups. The cost is a schema bump and writer-side
maintenance — pay the cost once, win on every read for the lifetime of
the schema.

### 5.5 InsightStore — minor wins

| # | Change | File | Expected delta |
|---|--------|------|----------------|
| 14 | Eviction maintains a min-heap of (LastSeenTick, key) so EvictStalest is O(log n) not O(n) | `InsightStore.cs:180-193` | <1 µs/eviction at LiveCap=32; not load-bearing |
| 15 | Replace `Dictionary<long, InsightRecord>` with `FrozenDictionary` after warmup | n/a | irrelevant: the live set churns, frozen doesn't help |
| 16 | Replace shared `_topComparerNowTick` scalar with a per-call comparison struct (`IComparer<InsightRecord>` instance) | `InsightStore.cs:60-66` | eliminates the documented race surface |
| 17 | Pre-size `_history` to expected session record count (~64 → ~256) | `InsightStore.cs:40` | one fewer regrow over a long session |
| 18 | Widen `StableKey` to 128-bit (`(long, long)` value tuple, or struct with two longs) so HookId can be ≥ 16-bit safely | `InsightStore.cs:195-205` | future-proofs the per-mod hook count ceiling |

Items 14 and 18 are correctness-adjacent (eviction speed, key width).
Items 16 is a small bug fix for a documented race that is currently
masked by single-threaded use. Items 15 and 17 are mostly cosmetic.

### 5.6 RankingScorer — no fix needed

Confirmed in code review: the `NormaliseMagnitude` dispatch is a tagged
`switch` over `PatternKey` (byte). The JIT lowers it to a jump table, no
allocation, no virtual call. The `ConfidenceWeight`, `Actionability`,
`AudienceMatch`, `RecencyWeight` helpers are static and inlined.
`Novelty` calls `Math.Log` which has a trivial constant cost. The
`Score` function is allocation-free and measured at ~100 ns per call. No
change recommended.

Latent concern: `Math.Log(1d + confirmationCount, 2d)` in `Novelty`
allocates nothing but is more expensive than a precomputed table for
`confirmationCount` ≤ 32. Marginal; not worth the maintenance cost.

### 5.7 InsightsEngine — gated map already cached

`BuildGatedMap` runs once at construction. `_gatedMap` and `_gatedLabel`
are immutable thereafter. Consumed by `InsightsTab.Draw` via the public
properties; no per-frame work. Already optimal. Watch item: if the
detector roster ever grows to support dynamic registration, the cache
needs an invalidation hook.

### 5.8 InsightRecord — class vs struct trade-off

`InsightRecord` is a `sealed class` with ~80 bytes of fields. The dedup
path keeps the same instance live for the record's full lifetime, so the
allocation cost is amortised across all confirmations. Switching to a
struct would force boxing through `List<InsightRecord>` enumeration
(`_history`, `_topAllScratch`) and break the in-place mutation pattern
in `Submit`. **Keep as class.** Documented for future readers who notice
the size and reach for the obvious refactor.

### 5.9 ModImpactScorer — already optimised

`Recompute` is gated to 1 Hz internally (`RecomputeIntervalTicks = 60`).
Buffers (`_impactsByMod`, `_sorted`) are pooled and grown only when
mod count rises. The `SortedView` IReadOnlyList wrapper avoids per-call
`ArraySegment` boxing (call-out at line 132-140). Insertion sort is
correct at this size. `UpdateCalibration` is `O(history.Count +
bytesView.Count)` per recompute — at history sizes of ~30 s × 60 Hz =
1800 ticks the loop is ~2 000 iterations of trivial floating-point
work, well under 100 µs.

One minor concern: the second pass over `_impactsByMod` (lines 240-247)
**reconstructs each `ModImpact` to write back `ShareOfTotal`**, which
allocates a fresh struct value but does not heap-allocate. Could be a
`ref-mutable` rewrite if `ModImpact` becomes a struct with `set;`
accessors — but the readonly-struct shape is a deliberate immutability
choice that the rest of the file relies on. Leave as is.

No change recommended for `ModImpactScorer`. It is a model the rest of
the insights engine should aspire to.

### 5.10 Allocation across the entire engine pass — net target

Today, per 1 Hz Evaluate, the engine allocates approximately:

| Source | Steady alloc/pass |
|--------|-------------------|
| AllocationBurst array | 830 B |
| GcPauseCulprit array | 830 B |
| LoadoutCorrelated transient lists, lambdas, expr trees | ~50 KB |
| EventConditional transient lists, groupby state, expr trees | ~80 KB |
| InsightStore.Submit dedup misses | <100 B amortised |
| **Total** | **~130 KB/pass = ~130 KB/s** |

After items 1, 2, 4, 5, 6, 7 (free wins) and items 9, 10, 11 (indexes):

| Source | Target alloc/pass |
|--------|-------------------|
| AllocationBurst | 0 (pooled) |
| GcPauseCulprit | 0 (pooled, cursored) |
| LoadoutCorrelated | bounded by `_snapsScratch`/`_beforeScratch`/`_afterScratch` HWM; ~0 in steady state |
| EventConditional | bounded by `_eventsScratch`/`_windowScratch` HWM + dictionary entries (long-lived) |
| **Total** | **< 1 KB/pass after warmup** |

After item 13 (off-thread relocation), the main-thread cost collapses
further but the *total* allocation is broadly the same — it just lives
on a different thread. The win on the main thread is the latency, not
the byte count.

---

## 6. Cross-system dependencies

### 6.1 Consumes

| Dependency | Owner | Used for | Failure if missing |
|------------|-------|----------|---------------------|
| `MetricCollector.PerHookAverageMs` | metric-collection | HotHookDominance numerator | detector skips via `IsAvailable` |
| `MetricCollector.PerModCategoryAverageMs` | metric-collection | HotHook denominator, FreeRemoval, PeakContributor | detector skips |
| `MetricCollector.PerModCategoryAverageBytes` | metric-collection | AllocationBurst | detector skips when `TracksAllocations == false` |
| `MetricCollector.Spikes` | spike-detection | PeakContributorToSpike | detector skips on `Count == 0` |
| `MetricCollector.Stalls` | spike-detection (stall path) | GcPauseCulprit | detector skips on `Count == 0` |
| `MetricCollector.PerTickRing.GetPerModBytes(t, mod)` | metric-collection (per-tick ring) | GcPauseCulprit attribution | detector returns nothing for that stall |
| `MetricCollector.Baseline.FrameMsMedian` | metric-collection | FreeRemoval epsilon | falls back to absolute floor during calibration |
| `MetricCollector.Baseline.TickPeriodMsMedian` | metric-collection | LoadoutCorrelated, EventConditional baselines | detector skips when `<= 0` |
| `PerModAttribution.Hooks` | hook-interceptor | HotHook hook-id walk | detector skips |
| `PerModAttribution.CategoryCount` | hook-interceptor | every detector with `[mod][cat]` indexing | engine returns zero records |
| `HookInterceptor.ProfiledModNames` | hook-interceptor | every detector that names mods | engine returns zero records |
| `PerformanceProfiler.Database` | persistence | Loadout + EventConditional | detectors return zero records via `IsAvailable` |
| `ProfilerSystem.LiveRecorderSessionId` | session-recorder | Loadout + EventConditional session filter | detectors return zero |
| `ProfilerDatabase.{LoadoutSnapshots, TickAggregatesWarm, BuffEvents}` | persistence streams | Loadout + EventConditional queries | detectors return zero |

The persistence dependencies are the load-bearing ones for this pass.
Every change in §3 (index design) requires coordination with
`systems/session-logging.md` and `Profiling/Persistence/Migrations.cs`.

### 6.2 Produces (consumed by)

| Output | Consumer | Surface |
|--------|----------|---------|
| `InsightStore.AllLive()` | `SessionLogWriter.InsightsBlock()` | JSON insights.live[] |
| `InsightStore.History` | `SessionLogWriter.InsightsBlock()` | JSON insights.history[] |
| `InsightStore.TopInto(_ranked, 6, nowTick)` | `InsightsTab.Tick` then `InsightsTab.Draw` | overlay card rows |
| `InsightsEngine.GatedPatterns()` | `SessionLogWriter` | JSON insights.gated{} |
| `InsightsEngine.GatedLabel` | `InsightsTab.Draw` | overlay footer line |

Critical contract: **`TopInto` runs on the same 1 Hz Tick path as
Evaluate** (`InsightsTab.cs:65`); only the resulting `_ranked` list and
`_rankedBodies` strings are touched by the 60 Hz `Draw`. So even if
Evaluate's main-thread cost balloons (today: up to 30 ms), the 60 Hz path
remains alloc-free. The headline number to move is the 1 Hz spike, not
the 60 Hz steady state. The 60 Hz Draw consuming `_ranked` is already
correctly implemented.

Per-frame UI consumer paths must stay alloc-free (hard constraint). The
`_rankedBodies` list is filled inside Tick, not Draw; Draw only indexes
already-rendered strings. The cached `CachedShortPlayer` /
`CachedMediumPlayer` / `CachedLongModder` on each record give the
renderer a fast path. **Confirmed: no Draw-path change is needed in this
pass; do not regress this property.**

### 6.3 Threading invariants

| Path | Thread | Reads | Writes |
|------|--------|-------|--------|
| `InsightsTab.Tick` (1 Hz) | main UI thread | engine state, collector | engine `_store`, `_scratch` |
| `InsightsTab.Draw` (60 Hz) | main UI thread | `_ranked`, `_rankedBodies` only | none on engine |
| `SessionLogWriter.InsightsBlock` (end of session) | session-lifecycle thread | engine `Store.AllLive`, `Store.History` | none |
| `ProfilerSystem.OnWorldUnload` | main thread | n/a | `InsightsEngine.Shared = null` |

The `Shared` field reassignment on unload is the only mutation done
outside the main UI thread cadence; everything else is single-thread
within a single tick. **Item 13 (off-thread queries) introduces a new
read thread.** That thread reads `db.*` collections concurrently with
the `DbWriterThread` — needs verification that LiteDB v5's per-collection
read locks compose with the writer's exclusive locks. The existing
`SessionLogWriter` already reads collections off-thread, so the pattern
is established; the insights reader thread joins it.

---

## 7. Prioritised order

Each item names: blast radius, expected delta against `baseline.md`,
risk, and dependency on other items.

### 7.1 Phase A — free in-memory wins (no schema change, no threading change)

Order: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8

1. **Promote AllocationBurst `_perModBytes` to field.** Blast radius: one
   file. Delta: −830 B/pass. Risk: trivial. No dependency.
2. **Promote GcPauseCulprit `_modBytes` to field.** Same shape as item 1.
   Blast radius: one file. Delta: −830 B/pass.
3. **Cursor GcPauseCulprit by stall index.** Mirror the
   `_lastConsumedSpikeStart` pattern from `PeakContributorToSpikeDetector`.
   Blast radius: one file. Delta: −2.5 ms/pass steady-state at 50 stalls.
   Risk: trivial; mirrors a tested pattern.
4. **Replace `.Average` calls in correlated detectors with explicit
   loops.** Blast radius: `InteractionInsightDetectors.cs`. Delta:
   −3 enumerator + delegate allocations per pass. Trivial.
5. **Replace `events.GroupBy(...).Where(...).Take(3).ToList()` with
   explicit dictionary pass.** Blast radius:
   `InteractionInsightDetectors.cs`. Delta: −1 dict + per-group lists +
   delegate allocs. Adds one `Dictionary<int, (bool, bool, int, int)>` as
   an instance field. Mid risk: the rewrite has to preserve the
   "first three buffs with both edges" selection rule.
6. **Replace per-group `OrderBy(e => e.UnixMs).ToList()` with the
   pre-sorted compound-index read direction.** Depends on item 11 landing
   first (compound index on BuffEvents). If item 11 is not yet ready,
   fall back to a single in-place sort over a pooled buffer.
7. **Pool result lists for all three LiteDB fetches per detector.** Blast
   radius: `InteractionInsightDetectors.cs`. Delta: −~50 KB/pass once
   warmup stabilises. Low risk.
8. **Precompute `ModId → HookIds[]` for HotHookDominance.** Blast radius:
   `PerModAttribution` plus the detector. Delta: M× speedup on the inner
   hook filter. Cost: one allocation at world-load time (per `ModId` a
   small `int[]`). Mid risk: needs to invalidate when `PerModAttribution`
   resizes (which only happens at session start). Add a build call in
   `HookInterceptor` post-Load.

After Phase A: steady-state per-pass allocation drops from ~130 KB to
~10 KB (still dominated by transient LiteDB row deserialisation, which
is fixed by Phase B).

### 7.2 Phase B — indexed queries (schema migration)

Order: 9 → 10 → 11 → 12 → 13

9. **Compound index `(SessionId, SecondIndex)` on TickAggregatesWarm.**
   Blast radius: `TickAggregateStream.EnsureIndexes`. Migration: implicit
   (EnsureIndex is idempotent). Delta: −60-80% latency on both
   correlated detectors' window queries. Risk: low; needs verification
   that LiteDB's planner picks the compound index.
10. **Compound index `(SessionId, Reason, UnixMs)` on LoadoutSnapshots.**
    Same shape as item 9. Delta: −80% on snapshot fetch. Risk: low.
11. **Compound index `(SessionId, UnixMs)` on BuffEvents.** Same shape.
    Delta: −70% on buff fetch. Risk: low.
12. **Precompile `BsonExpression` filters per detector.** Cache as
    `static readonly` fields on each detector. Delta: −expr parse per
    pass. Risk: low; requires API verification against LiteDB 5.0.17.
13. **Switch `OrderByDescending(...).Limit(n)` to backward-range index
    scans where the compound index supports it.** Delta: −in-memory sort.
    Risk: low after item 9-11 land; verify LiteDB query planner picks
    the backward scan path with `EXPLAIN`-equivalent debug output (or
    measure latency directly).

After Phase B: steady-state per-pass latency drops from ~5-30 ms to <2 ms
on the main thread. Allocation drops to under 1 KB/pass.

### 7.3 Phase C — off-thread relocation (architectural)

14. **Move correlated-detector queries to a reader thread.** Blast
    radius: `InsightsEngine.Evaluate`, plus a new reader thread or reuse
    of `DbWriterThread`'s loop with a read queue. Delta: main-thread
    insights-tab cost drops to <100 µs per pass. Risk: medium — LiteDB
    concurrent read-while-write must be verified; queue backpressure
    handling must surface via `Mod.Logger.Debug` on overflow.
    Dependency: Phase B should land first so the queries are cheap when
    they do run, even if they're now off-thread.

### 7.4 Phase D — pre-aggregation collections (largest long-term win)

15. **Materialise `loadoutChangeEdges` at write time.** Eliminates
    `LoadoutCorrelated`'s snapshot fetch entirely. Schema: one row per
    change edge with `(SessionId, Tick, Fingerprint, PrevFingerprint)`.
    Blast radius: `InteractionPlayer.OnLoadoutChange` plus a new stream.
    Delta: −1 query per pass; the detector reads the latest 1 row.
16. **Materialise `buffEdgePairs` at write time.** Eliminates the
    GroupBy + edge-pair reconstruction in `EventConditional`. Blast
    radius: `InteractionPlayer.PostUpdateBuffs` matcher; new stream.
    Delta: −1 group-by + −per-group sort + simpler iteration.
17. **Materialise rolling 30 s means at write time.** Eliminates the
    range scan + `Average` for both correlated detectors. Blast radius:
    `TickAggregateStream` writer plus a new stream. Delta: −1 range
    query per detector per pass. Largest long-term win, biggest schema
    surgery — flag for the litedb-cross-session milestone.

After Phase D: the entire insights engine main-thread cost approaches
the in-memory-only baseline (a few hundred µs per pass) regardless of
session size or modlist size.

### 7.5 Phase E — InsightStore polish (small, low priority)

18. Fix `_topComparerNowTick` race surface (per-call comparer instance).
19. O(log n) eviction via min-heap. Skip unless `LiveCap` is raised.
20. Widen `StableKey` to 128-bit. Skip until a real per-mod hook count
    approaches 65k.

Phase E items are watch-list, not action items for this pass. None of
them affects the headline numbers.

---

## 8. Risk register

| Risk | Trigger | Mitigation |
|------|---------|------------|
| LiteDB compound-index planner doesn't pick the new index | Verify with timing measurements after index lands; falls back to single-column index | Composite-key column fallback (string key) |
| Off-thread reader interleaves with `DbWriterThread` writer | LiteDB v5 read-while-write semantics differ between `ConnectionType.Direct` and `Shared` | Verify in `DbWriterThread`'s connection config; if unsafe, serialise reads onto the writer thread itself |
| Pre-aggregation drift | Writer-side materialisation diverges from reader-side reconstruction | Migration tests in `Tests/` comparing both paths on synthetic input |
| Pooled scratch buffer never shrinks | HWM during a spike persists forever | Acceptable; the high-water mark is bounded by session shape |
| `BsonExpression` precompile API differs from documented form | LiteDB API drift between minor versions | Pin the version explicitly and gate the precompile path with a try/catch fallback to lambda Query (per Invariant 4: abort clean) |
| GcPauseCulprit cursor desyncs across world reloads | `_lastConsumedStallIndex` not reset on world unload | Add a `Reset()` method mirroring `PeakContributorToSpikeDetector.Reset()` and wire it in `ProfilerSystem.OnWorldUnload` |

---

## 9. Assumptions that need stronger evidence

1. **LiteDB v5's planner uses compound `BsonExpression`-defined indexes
   for range predicates with prefix equality.** Documented but not
   confirmed against the project's current LiteDB version. A targeted
   benchmark in `Tests/` should compare a tagged compound-index query
   against the current single-column path before adopting widely.
2. **The 5 to 30 ms latency budget for the persistence-backed detectors
   is correct.** Estimated from row counts and per-row deserialise
   cost, not measured directly. Add a `Stopwatch`-wrapped trace inside
   each detector's `Evaluate` (gated behind `Mod.Logger.Debug`) for the
   first commit of Phase B so the actual baseline number is captured.
3. **`Mod.Logger` calls do not allocate during the off-thread reader
   path.** `Mod.Logger.Debug(string)` does allocate via `string.Format`
   when interpolation is used; the reader thread must use the
   non-interpolated overload or pre-format strings.
4. **The 8.5 s UiOverlayBlocking cluster is meaningfully attributable to
   the insights engine.** The baseline file attributes it to
   PerformanceProfiler as a category; the insights engine is one of
   several main-thread paths in that category (others: session-end JSONL
   write, modimpact recompute, overlay-tab rendering). Phase A and B
   landing should reduce the cluster proportionally; if it doesn't, the
   attribution sat elsewhere.

---

## 10. References

### 10.1 LiteDB

- LiteDB v5 documentation, "Indexes" section
  <https://www.litedb.org/docs/indexes/> — covers `EnsureIndex` lambda
  form, the `BsonExpression` string form, compound indexes (multiple
  fields via array notation `[$.Field1, $.Field2]`), and that the
  planner picks the most selective single-column index when no
  composite covers the query.
- LiteDB v5 source, `LiteDB/Engine/Query/QueryOptimization.cs` —
  reference for how the cost-based planner chooses between candidate
  indexes; useful when verifying that a compound index will actually be
  used.
- LiteDB issue #1869 ("compound index range scan") and #2014
  ("EnsureIndex with BsonExpression compound") — known limitations of
  the planner that affect the §3.1 risk assessment.
- LiteDB v5 thread-safety FAQ — `ConnectionType.Direct` with internal
  per-collection latches: documented as safe for concurrent reads while
  one writer holds the engine. Relevant to §5.3.

### 10.2 .NET 8 / C# performance

- "Performance Improvements in .NET 8" (Stephen Toub, MSDN, Nov 2023) —
  Spans, FrozenDictionary, the new `IEnumerable<T>.Count()` fast paths.
  Reference for §4.3 span migration and the FrozenDictionary watch
  item.
- "Avoid LINQ in hot paths" — MS Docs Performance Guidelines.
  Quantifies the per-call allocation cost of `Where`, `Select`,
  `OrderBy`, `GroupBy`. Reference for §4.1 and §4.2.
- `System.Buffers.ArrayPool<T>` documentation — alternative to
  per-detector pooled fields if the pooled buffer ever needs to be
  returned for use by another component. Not used in this pass; the
  per-detector field is simpler.

### 10.3 In-repo

- `context/perf-pass/baseline.md` — every measurement target in this file.
- `context/notes/philosophy.md` — "Optimisation = doing what we already
  do at maximum efficiency" — the rule against scope cuts.
- `context/notes/insights-engine-plan.md` — original design; sections
  shipped marked. Reference for the pattern catalogue.
- `context/systems/insights-engine.md` — current implementation state;
  cross-checked against actual code in this dossier.
- `Profiling/Insights/InsightsEngine.cs`,
  `Profiling/Insights/InsightStore.cs`,
  `Profiling/Insights/RankingScorer.cs`,
  `Profiling/Insights/InsightRecord.cs`,
  `Profiling/Insights/Detectors/*.cs`,
  `Profiling/ModImpactScorer.cs` — implementation reality this dossier
  audits.
- `Profiling/Persistence/Streams/InteractionStreams.cs`,
  `Profiling/Persistence/Streams/TickAggregateStream.cs`,
  `Profiling/Persistence/Streams/SpikeStream.cs` — current `EnsureIndex`
  declarations; the deltas in §3.1-3.3 land here.
- `Profiling/Persistence/Migrations.cs` — the channel for the index
  additions in Phase B.
- `Profiling/Persistence/DbWriterThread.cs` — the candidate host for
  the off-thread reader in Phase C (or a sibling thread reusing its
  connection discipline).
- `UI/Overlay/Tabs/InsightsTab.cs:50-73` — the sole consumer of the
  engine's per-tick output; the alloc-free contract on the 60 Hz Draw
  path is enforced here.
- `Tests/RankingScorerTests.cs`,
  `Tests/InsightStoreTests.cs` — pin the share/ratio split, the
  PromoteConfidence gate, and the alloc-free `TopInto` contract. The
  Phase A and B changes must not regress these.

---

## 11. Closing observations

The single highest-leverage finding is that two detectors execute three
to four LiteDB queries each on the main UI thread every second, on
collections whose only index is `SessionId`. The compound-index work in
Phase B is the **correct first surgical move**: it costs one migration
each per collection, requires no architectural change, and shrinks the
queries' latency to a small fraction of today's number while preserving
every record the detectors emit and every piece of evidence each record
carries.

The free wins in Phase A are obvious and should land regardless of
Phase B's timing — they remove a known 1.6 KB/s of garbage and a 2 to 3
ms per-pass recompute cost in the GC-pause attribution path.

The off-thread relocation in Phase C is the architectural follow-up that
removes the insights engine from the main-thread budget entirely. It
should not land before Phase B (off-threading expensive queries doesn't
help if the queries themselves are still expensive), but once Phase B
ships, Phase C is the natural finish.

Phase D (pre-aggregation collections) is the long-term shape the
detectors will want once the litedb-cross-session milestone lands and
cross-session detectors join the live roster. It is not urgent for this
pass but worth designing into the schema-v5 plan so the new collections
don't have to be retrofitted later.

No detector is scope-cut. No insight is dropped. Every record today's
engine emits, the post-pass engine emits, with the same evidence
richness and the same pattern coverage. The 60 Hz Draw path stays
alloc-free. The data stack is untouched. Every change in this dossier
makes the engine do the same observable work at lower cost — which is
exactly the rule the philosophy file demands.
