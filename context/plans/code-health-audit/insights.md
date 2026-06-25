# Insights Engine — Code Health Findings

Cluster: the statistical interpretation layer (`Insights/`, 42 files, 5426 LOC). Audited
against `context/systems/insights-engine.md` + `context/notes/insights-rework-status.md`,
the running code, the `Tests/Insights/*` + `Tests/RankingScorerTests.cs` + `Tests/InsightStoreTests.cs`
suite, and external statistical research (see report). Every finding is FREE: identical
runtime behaviour, no new maintenance burden, full evidence chain. No production source was
edited. **13 findings** across 6 categories.

Severity legend: **High** (correctness / honesty-contract risk, or a stated contract that
does not hold) · **Medium** (latent bug, dead code, or a documented-but-wrong claim) · **Low**
(doc/comment rot, naming, micro-consistency).

---

## Known Issues

### Per-insight LiteDB `insights` collection has no producer (confirmed unfed scaffold)
- [ ] The `insights` LiteDB collection, its row, its op kind, and its stream all exist, but **nothing enqueues a `DbWriteOp.Insight`** — the live feed is in-memory only.
**Category:** Known Issues / Dead-end scaffold   **Severity:** Medium   **Effort:** free (documentation only — wiring the producer is a feature, explicitly out of scope)   **Behavioural Impact:** none today; the scaffolding compiles and ships inert.
**Location:** `Profiling/Persistence/DbWriteOp.cs:111` — `Insight(InsightRow)`; consumed only at `Data/Streams/InsightStream.cs:25,30`; collection at `Profiling/Persistence/ProfilerDatabase.cs:79`.
**Current State:** Verified by grep across all `*.cs`: the only references to `DbWriteOp.Insight(` are (1) its own factory in `DbWriteOp.cs` and (2) `InsightStream.Reconstruct` (the *reader* side, which rebuilds an op from a persisted JSONL line). No `Submit`/`Save`/recorder path ever calls `DbWriteOp.Insight(...)` to *write* a row. `InsightsStat.CurrentSnapshot()` (`Insights/Publish/InsightsStat.cs:52-57`) reads `eng.Store.AllLive()` straight from memory. The only LiteDB path that IS fed is `contextBaselines` via `CrossSessionStore` — that is the reference-frame substrate, not per-insight rows.
**Proposed Change:** None to code. This finding records the gap as a Known-Issue so the next reader does not assume `insights` is a live lifetime-history source. The system doc already flags it under "Partial / In Progress"; this confirms it against the current tree at audit time. (Building the producer is a feature and out of scope per the audit brief.)
**Justification:** A persisted collection with a deserialiser but no serialiser is a silent dead-end: a future agent wiring "insight history" could reasonably assume rows already flow and build a reader on an always-empty collection.
**Expected Benefit:** The scaffold's true state is documented; no one wastes time debugging an empty query.
**Impact Assessment:** Zero — documentation only.

### `PeakContributorToSpikeDetector.Reset()` is dead — never invoked
- [ ] `Reset()` exists with a docstring claiming an "end-of-session pass to drain every spike" uses it, but no call site exists anywhere in the tree.
**Category:** Known Issues / Dead code · unfulfilled contract   **Severity:** Medium   **Effort:** free
**Location:** `Insights/Detectors/PeakContributorToSpikeDetector.cs:109-113` — `Reset()`.
**Current State:** Grep for `.Reset()` across `Profiling/` finds only `Time.Reset()`, `_contextTagger.Reset()`, and `RowPool` — no `PeakContributorToSpikeDetector.Reset`. The detector also does not implement any reset interface the engine calls. The `_lastConsumedSpikeStart` cursor (`:42`) therefore monotonically advances for the life of the engine instance; it is correctly reset only by the world-unload teardown that drops the whole `InsightsEngine.Shared` (which allocates a fresh detector roster). The "end-of-session drain" the docstring promises does not happen — the final spikes before unload are consumed by the last live `Evaluate` pass like any other, not by a dedicated drain.
**Proposed Change:** Either (a) delete the `Reset()` method and its docstring, or (b) if an end-of-session drain is genuinely wanted, file it as a feature. Free action = delete the dead method + the misleading docstring. (Flagging only — no edit made.)
**Justification:** Dead public method with a docstring that asserts a behaviour that does not exist. The next reader trusts the docstring and assumes spikes are drained at session end; they are not.
**Expected Benefit:** Removes a false contract; one fewer "where is this called?" investigation.
**Impact Assessment:** Deleting it is behaviour-identical (it is never called). If kept, the docstring should be corrected to "reserved; no current caller".

---

## Active Risks (numerical / statistical)

### `RunningStat.Without` can silently floor catastrophic-cancellation M2 to 0
- [ ] The reverse-Chan M2 recovery subtracts two nearly-equal large doubles; when the in-context and out-of-context distributions are similar, floating-point cancellation can drive the recovered M2 negative, and the code floors it to 0 — producing a degenerate (zero-variance) complement that the downstream Welch test reads as "infinitely confident".
**Category:** Active Risks / Numerical stability   **Severity:** High   **Effort:** free (flag + diagnostic test; the floor itself is a reasonable guard, the *consequence* is the risk)
**Location:** `Insights/Shared/Stats.cs:67-78` — `RunningStat.Without`; consumed at `Insights/ReferenceFrames/ContextBaseline.cs:152` (`_global[modId].Without(inContext)`), feeding `ContextConditionalCostDetector.Evaluate` (`Insights/Detectors/ContextConditionalCostDetector.cs:80-94`).
**Current State:** `m2 = _m2 - subset._m2 - delta*delta*((double)n*subset.Count)/Count`. External research (Schubert, *Numerically Stable Parallel Computation of (Co-)Variance*, SSDBM18; Wikipedia *Algorithms for calculating variance*) confirms the parallel/incremental *subtraction* (deletion) case is the numerically dangerous one — the search explicitly returned "no specific information about deleting/subtracting samples … this requires additional numerical considerations". When `_m2` (global) and `subset._m2` (in-context) are close — which happens precisely when a mod's cost is *similar* in and out of context, the common no-signal case — the subtraction loses precision and `m2` can go slightly negative. The `if (m2 < 0d) m2 = 0d` floor (`:76`) then yields a complement with `Variance == 0`. `Stats.WelchTTestP` (`:110-120`) computes `denom = va + vb`; if the out-of-context variance is floored to 0 and the in-context variance is also tiny, `denom <= 1e-18` returns p=1 (safe). But the asymmetric case — in-context has real spread, out-of-context floored to 0 — gives a finite tiny `denom`, a large `t`, and a spuriously significant p-value. The candidate-gate (`inCtx.Mean > outCtx.Mean` + Cohen's d ≥ 0.8) catches *most* of this because a floored-variance complement also distorts CohensD's pooled SD, but the two guards share the same corrupted M2, so they do not independently cross-check.
**Proposed Change:** No code edit (the floor is defensible). FLAG a diagnostic equivalence test (see Diagnostic Tests below) that drives `Without` into the cancellation regime and asserts the resulting p-value is not spuriously < 0.05. If the test fails, the *fix* (a relative-epsilon guard that treats `m2 < relEps*_m2` as "degenerate, skip the test") is itself free, but that is a follow-on, not this finding.
**Justification:** This is the newest, most statistics-heavy code; the honesty contract (Invariant 3) rests on the p-value being defensible. A spurious sub-0.05 p can promote a record to Medium/High confidence (via `PromoteConfidence`), which is a direct honesty-contract violation, not just a cosmetic bug.
**Expected Benefit:** Confidence that the complement-recovery path cannot manufacture significance from floating-point noise — the exact failure the spine law is meant to prevent.
**Impact Assessment:** Test-only; no runtime change. The existing `ReferenceFrameTests.RunningStat_Without_RecoversTheComplement` (`Tests/Insights/ReferenceFrameTests.cs:32-50`) uses well-separated values (1-4 vs 10-15) and does NOT exercise the cancellation regime, so the risk is currently unpinned.

### Welch's test uses a normal (z) approximation, not the t-distribution; skewed per-mod cost with unequal n inflates Type I
- [ ] `WelchTTestP` normal-approximates the t-distribution and never applies the Welch–Satterthwaite degrees-of-freedom correction; the per-mod cost series is right-skewed (sub-ms with occasional spikes) and the in/out sample sizes are unequal — the exact regime the literature shows inflates the false-positive rate.
**Category:** Active Risks / Statistical correctness   **Severity:** Medium   **Effort:** free (flag + research-grounded note; a t/df fix is additive follow-on)
**Location:** `Insights/Shared/Stats.cs:110-120` — `WelchTTestP`; docstring at `:103-109` claims "accurate once both samples have a few dozen points".
**Current State:** The code computes `t = (a.Mean - b.Mean)/sqrt(va+vb)` then `return 2*(1 - NormalCdf(|t|))` — i.e. it treats the statistic as standard-normal. For the 1 Hz reference frames this is dozens-to-hundreds of samples, where z≈t for *normal* data. But external research (Springer *Statistical Papers* 2024; ResearchGate *Risks of defaulting to Welch's t-test with unequal sample sizes and skewed distributions*) is explicit: "when sample sizes are unequal and the data follow a heavily skewed distribution (like a Poisson with a low mean), Welch's t-test can develop an inflated false positive rate, around 0.078 at a 0.05 threshold". Per-tick mod cost IS heavily right-skewed and low-mean (mostly sub-0.1 ms with rare spikes), and `ContextConditionalCost`/`SustainedCostShift`/`NewContributor` all compare *unequal* in/out windows. So the docstring's "accurate" claim is too strong for this data's actual shape. The Bonferroni correction (below) partially offsets this by being conservative, but the two effects do not cleanly cancel.
**Proposed Change:** No code edit. (1) FLAG that the docstring overclaims: "accurate" should read "approximate; on right-skewed low-mean cost with unequal n the normal approximation can be slightly anti-conservative — partially offset by the Bonferroni step". (2) FLAG a diagnostic test that feeds skewed unequal-n distributions and checks the empirical Type-I rate. A real fix (Welch–Satterthwaite df + a t-CDF, or a non-parametric Mann–Whitney as the plan's original §4.4 intended) is a feature, out of scope.
**Justification:** The spine law and the confidence badges are only as honest as the p-value. A slightly inflated Type-I rate means a few records reach Medium/High that statistically should not — an honesty-contract pressure point in the most-trusted detectors.
**Expected Benefit:** The known limit of the approximation is documented where the next reader will see it (the docstring), instead of an unqualified "accurate".
**Impact Assessment:** Documentation/test only. No `WelchTTestP` callers change behaviour.

### Bonferroni denominator assumes independent tests; the per-(context,mod) sweep is dependent
- [ ] Each detector corrects by `testsRun` (the count of comparisons that pass the candidate gate this pass), but those tests are NOT independent — every bucket's out-of-context complement is derived from the same global per-mod series — so the effective number of independent tests is smaller than `testsRun`.
**Category:** Active Risks / Statistical correctness   **Severity:** Low (the error is in the *conservative* direction — honesty-safe)   **Effort:** free (documentation)
**Location:** `Insights/Detectors/ContextConditionalCostDetector.cs:81,103,110`; same pattern in `ContextCorrelatedSpikeDetector.cs:60,77,98`, `SustainedCostShiftDetector.cs:45,57,77`, `NewContributorDetector.cs:47,59,79`.
**Current State:** `pAdjusted = min(1, p * testsRun)`. External research (StatSig; arXiv 0907.2478; LibreTexts 6.1) confirms Bonferroni "implicitly assumes the test statistics are independent … when tests are dependent the effective k is lower than the total number of tests", making Bonferroni *over*-conservative (higher Type-II, lower power). Here `outOfContext = global.Without(inContext)`, so the boss-bucket test and the hardmode-bucket test for the same mod both lean on `_global[mod]` — they are positively correlated. The correction therefore over-penalises, suppressing some real signals. Direction matters: this errs toward *under*-claiming, which the honesty contract tolerates (untested-looking records stay at Low), unlike the Welch z-approximation above which errs the other way.
**Proposed Change:** No code edit. FLAG a one-line comment at each correction site noting the dependence makes the correction conservative (the design comment at `ContextConditionalCostDetector.cs:30-36` says Bonferroni "cannot manufacture significance" — true, but it omits that it can *suppress* real significance through dependence). A Holm or BH step (the research's named alternatives) would recover power but is a feature.
**Justification:** A future tuning pass that sees "we miss real cross-context cost shifts" should know the correction is conservative-by-dependence before reaching for a looser threshold.
**Expected Benefit:** The power cost is documented; nobody loosens the candidate gate (which would be the wrong lever) to compensate.
**Impact Assessment:** None — comment only.

### `NewContributorDetector` from-zero ratio sentinel (`99d`) leaks into the ranking magnitude
- [ ] When the early window mean is ~0, `RatioOrDelta` is set to a hardcoded `99d` ("~from-zero"); this is a ratio pattern, so `RankingScorer.RatioCurve` saturates it to 1.0 — every new-contributor record pins maximum magnitude regardless of how small the late cost actually is.
**Category:** Active Risks / Ranking correctness   **Severity:** Low   **Effort:** free (flag; the renderer never shows `99×`, so the player surface is unaffected)
**Location:** `Insights/Detectors/NewContributorDetector.cs:74` — `RatioOrDelta = c.early > 1e-9 ? c.late/c.early : 99d`; consumed by `RankingScorer.RatioCurve` (`Insights/RankingScorer.cs:104-109`).
**Current State:** `RatioCurve(99)` → `(99-1)/9 = 10.9 → clamp 1.0`. So a mod that climbed from idle to 0.1 ms/t and one that climbed to 5 ms/t both get magnitude 1.0 and rank identically on the magnitude component — the same class of "strongest signal erased" the share/ratio split (`Durable Notes` in the system doc) was created to fix, re-introduced here through the sentinel. The renderer (`RenderNewContributor`, `InsightRenderer.cs:316-324`) shows only `late` ms, never the ratio, so the *player copy* is honest; the distortion is purely in ranking order. The `ActiveMs = 0.10` gate (`:28`) bounds how trivial the late cost can be, limiting the blast radius.
**Proposed Change:** No code edit. FLAG that for the from-zero case the magnitude would rank more honestly on the late *level* (e.g. normalise `c.late` against the session frame budget) than on a sentinel ratio. A fix is additive (a `MagnitudeShape.Rate` already exists and carries the late level). Flagging only.
**Justification:** Same root cause as the documented share/ratio bug: a magnitude that does not monotonically track the signal it claims to rank by.
**Expected Benefit:** New-contributor records would rank by how much new cost actually appeared, not a constant.
**Impact Assessment:** Ranking-order only; no player-visible string changes. Not pinned by `RankingScorerTests` (which tests `SustainedCostShift`, not the from-zero `NewContributor` path).

---

## Inconsistent Patterns

### Doc/code drift: `IsSharePattern` set has grown but the method's own docstring lists only 3 of 7
- [ ] `RankingScorer.NormaliseMagnitude`'s docstring enumerates the share patterns as "`HotHookDominance`, `AllocationBurst`, `PeakContributorToSpike`" but `IsSharePattern` actually returns true for 7 patterns (those three plus `GcPauseCulprit`, `CostConcentration`, `FrameHeadroom`, `FrameJitter`).
**Category:** Inconsistent Patterns / Documentation rot   **Severity:** Low   **Effort:** free
**Location:** `Insights/RankingScorer.cs:60-83` (docstring) vs `:86-101` (`IsSharePattern` switch).
**Current State:** The docstring `<list>` at `:62-68` names three share patterns; the switch at `:86-101` returns `true` for seven and carries its own inline comments explaining the Wave-5 additions. So the inline switch comments are current and the method-level docstring is stale. The system doc (`insights-engine.md`, "Pattern-aware ranking") correctly lists all seven, so the *doc* is right and the *code docstring* is the laggard.
**Proposed Change:** FLAG: the `NormaliseMagnitude` docstring `<list>` should list all seven share patterns (or say "see `IsSharePattern`"). No behavioural edit.
**Justification:** A reader trusting the docstring would think `CostConcentration`/`FrameHeadroom`/`FrameJitter` go through the ratio curve (which would zero their `[0,1]` shares — the original bug), when in fact they are correctly routed as shares.
**Expected Benefit:** The most-bug-prone method in the scorer documents its actual behaviour.
**Impact Assessment:** None — comment only.

### `ContextCorrelatedSpike` renders a lift ratio via `Multiple()` but stores it as a non-share `RatioOrDelta`; the doc calls all of Family A "share against ceiling"
- [ ] `ContextCorrelatedSpike` stores `lift = spikeShare/dwellShare` (a ratio, can be ≫1) in `RatioOrDelta`, is correctly excluded from `IsSharePattern`, and renders as "1.8×" — but the system doc's "structural-fact patterns report shares against an explicit ceiling" framing reads as if it were a share pattern. Minor classification ambiguity, not a bug.
**Category:** Inconsistent Patterns / Naming-classification clarity   **Severity:** Low   **Effort:** free
**Location:** `Insights/Detectors/ContextCorrelatedSpikeDetector.cs:90` (`RatioOrDelta = c.lift`); `Insights/RankingScorer.cs:86-101` (excluded — correct); `Insights/InsightRenderer.cs:278` (`Multiple(c.lift)`).
**Current State:** Behaviour is consistent and correct: a lift of 3.0 goes through `RatioCurve` (ranks as a 3× ratio) and renders "3.0×". The only drift is conceptual — the spine-law prose in `insights-engine.md:8` groups `FrameJitter`/`FrameHeadroom`/`CostConcentration` as the share-against-ceiling patterns and is silent on where `ContextCorrelatedSpike`'s lift sits. It is a ratio (over-representation factor), handled as one.
**Proposed Change:** FLAG for the next `upkeep-context` pass: note that `ContextCorrelatedSpike`'s magnitude is a *lift ratio*, ranked via the ratio curve, not a share. No code change.
**Justification:** Prevents a future reader from "fixing" `ContextCorrelatedSpike` into `IsSharePattern` (which would clamp a legitimate 3× lift to … 3.0 clamped to 1.0 — losing the very over-representation the pattern measures).
**Expected Benefit:** The classification is unambiguous in the doc.
**Impact Assessment:** None — doc only.

---

## Pattern Extraction

### Four statistical detectors share an identical candidate-gate → Bonferroni → sort → emit skeleton
- [ ] `ContextConditionalCost`, `SustainedCostShift`, `NewContributor` (and structurally `ContextCorrelatedSpike`) repeat the same five-step body: clear scratch, sweep + count `testsRun`, candidate-gate (`mean` direction + Cohen's d ≥ 0.8), `adjust = max(1, testsRun)`, sort-by-effect, emit-top-K with `pAdjusted = min(1, p*adjust)`.
**Category:** Pattern Extraction / Duplicated detector scaffolding   **Severity:** Low (NOT free to extract — see assessment)   **Effort:** the dedup itself is non-trivial; flagging only
**Location:** `ContextConditionalCostDetector.cs:67-145`, `SustainedCostShiftDetector.cs:39-87`, `NewContributorDetector.cs:41-89`, `ContextCorrelatedSpikeDetector.cs:47-107`.
**Current State:** The three temporal/context two-sample detectors are near-identical except for (a) the candidate predicate (`inCtx.Mean > outCtx.Mean` vs `late.Mean > early.Mean` vs `early.Mean < IdleMs && late.Mean >= ActiveMs`), (b) the sort key (effect vs late-cost), and (c) the subject/baseline kind. The Bonferroni arithmetic `min(1, p * max(1, testsRun))` is copied verbatim at four sites. This is real duplication, but it is *structural*, not literal-copy-paste of a bug-prone block — each detector's predicate genuinely differs.
**Proposed Change:** FLAG only. A shared `TwoSampleSweep` helper (taking a candidate predicate delegate + a sort comparison + an emit factory) would centralise the Bonferroni arithmetic so a future correction (e.g. switching to Holm) happens once. BUT: extracting it introduces a delegate-per-candidate allocation risk on the off-thread path and an abstraction the project's standards explicitly defer ("extract on the third real consumer, not the imagined fourth"). There are exactly 3-4 consumers, so the seam is arguably earned — but the allocation discipline (Invariant 2, even at 1 Hz) makes a naive `Func<>`-based extraction a regression. **This is therefore NOT a free win** and is recorded as a watch-item, not a recommendation.
**Justification:** Centralising the Bonferroni arithmetic would make the "switch to Holm/BH" follow-on (referenced in the dependent-tests finding) a one-site change instead of four.
**Expected Benefit:** Future statistical-correction changes touch one site.
**Impact Assessment:** Not free; deferred. Extraction must be allocation-free (struct-based predicate, no closures) to respect Invariant 2 — that constraint is what disqualifies it as a simple cleanup.

### `Publish/` stats repeat the registry-lookup-or-empty + index-by-modId boilerplate
- [ ] `ModObservatoryStat`, `EngagementCostScatterStat`, `DormantSurfaceStat` each open with the same `DataRegistry.Shared.Lookup<X>(name)?.CurrentSnapshot() ?? X.Empty` triple-lookup and the same "build a `Dictionary<int, ModUsageEntry>` keyed by ModId then accumulate `UsageWeight`" loop.
**Category:** Pattern Extraction / Duplicated compose boilerplate   **Severity:** Low   **Effort:** flagging only (extraction is borderline-free)
**Location:** `Insights/Publish/ModObservatoryStat.cs:86-113`, `Insights/Publish/EngagementCostScatterStat.cs:63-85`, `Insights/Publish/DormantSurfaceStat.cs:43-67`.
**Current State:** The roster+usage lookup and the `usageById` index are built three times with the same shape. The shared `ModMetrics.UsageWeight`/`RosterSize`/`SafeShare` already centralise the *formulas* (the Wave-1 census win); what remains duplicated is the *snapshot plumbing* (lookup, null-coalesce-to-Empty, index-by-id). This is genuinely repeated and a small `(roster, usage)` fetch+index helper would be allocation-equivalent (the dictionaries are built either way).
**Proposed Change:** FLAG: a `Publish/` internal helper `TryGetRosterAndUsage(out roster, out usage, out usageById)` would remove ~12 lines × 3 sites. Behaviour-identical (same allocations). Borderline-free; recorded for the next refactor pass rather than asserted as a must-do.
**Justification:** Three real consumers — the project's own "extract on the third consumer" rule is satisfied.
**Expected Benefit:** One home for the snapshot-fetch plumbing; a fourth `Publish/` stat composes for free.
**Impact Assessment:** Low risk, behaviour-identical, but touches three files — surface it before editing rather than treating as a pass-through cleanup.

---

## Algorithm / Data Layout

### `ModInteractionAggregator` Pearson covariance recomputes the per-bucket deviations the variance pass already touched
- [ ] The variance pre-pass (`:147-160`) and the `Pearson` covariance loop (`:202-219`) each re-walk every bucket subtracting the mean; for the included N×N upper triangle this re-reads `series2d` `O(N² · bucketCount)` times with the mean-subtraction inlined, when the deviations could be cached once.
**Category:** Algorithm Optimisation / redundant recomputation   **Severity:** Low (already cached behind a 5 s recompute gate; off-thread; bounded N≤30)   **Effort:** flagging only
**Location:** `Insights/Publish/ModInteractionAggregator.cs:144-219` — variance pre-pass + `Pearson`.
**Current State:** Means and variances are pre-computed once (`:144-160`), but `Pearson` (`:202-219`) recomputes `(series[i,b]-mi)*(series[j,b]-mj)` for every pair, re-subtracting the means it could have cached as a deviation matrix. The doc's own cost note (`:18-23`) accepts "a few million multiply-adds per call … cached for 5 s". For N≤30 and an hour of seconds (~3600 buckets) that is ~1.6M ops/call behind a 5 s gate — negligible. A cached deviation matrix `dev[i,b] = series2d[i,b]-means[i]` would halve the subtractions but doubles the working set (another `double[N,bucketCount]`), which for N=30, bucketCount=3600 is ~864 KB — a real allocation against a memory-conscious profiler.
**Proposed Change:** FLAG only. The current form is the right trade-off (CPU is gated + off-thread; the deviation-matrix alternative trades a free 864 KB allocation for a saving nobody can feel). Recorded so a future "optimise the Pearson matrix" impulse sees the trade was considered and rejected.
**Justification:** Documents that the obvious "cache the deviations" optimisation was weighed against Invariant-2 memory discipline and the recompute gate, and is not worth it at the bounded N.
**Expected Benefit:** Prevents a well-meaning future allocation that helps nothing.
**Impact Assessment:** None — no change recommended.

### `UpdateContextBaseline` allocates one `CollectorInsightInput` per pass (1 Hz)
- [ ] Each `Evaluate` allocates `new CollectorInsightInput(collector)` to feed the temporal baseline; at 1 Hz off-thread this is negligible, but it is a small per-pass heap allocation in a profiler whose entire thesis is measuring allocations.
**Category:** Data Layout / Memory Access   **Severity:** Low   **Effort:** flagging only (the fix — a field-cached adapter — is free but additive)
**Location:** `Insights/InsightsEngine.cs:281` — `var input = new CollectorInsightInput(collector);`.
**Current State:** `CollectorInsightInput` (`Insights/CollectorInsightInput.cs:16-32`) wraps a single `MetricCollector` field and is otherwise stateless — it could be field-cached on the engine and reused every pass (the wrapped collector is the same instance for the session). The system doc already flags this under "Known Issues". One ~24-byte allocation per second off-thread is genuinely immaterial against the budget; the irony (a profiler allocating where it measures) is the only reason to note it. Compare: the detectors went to real lengths to field-cache `_perModBytesScratch`/`_modBytesScratch` (`AllocationBurst`/`GcPauseCulprit`) for exactly this reason, so the per-pass `CollectorInsightInput` is inconsistent with that established discipline.
**Proposed Change:** FLAG: field-cache the adapter (`_insightInput ??= new CollectorInsightInput(collector)`), matching the zero-alloc discipline the detectors already follow. Behaviour-identical. Free, but a touch to the hot-ish path — surface before editing.
**Justification:** Consistency with the codebase's own established zero-alloc-per-pass convention; removes the one self-contradictory allocation in the engine.
**Expected Benefit:** The 1 Hz pass becomes allocation-free for its input adapter, matching the detector scratch buffers.
**Impact Assessment:** Trivial; the collector reference is stable for the engine's lifetime so caching is safe (and a new engine is allocated per world-load, refreshing it).

---

## Diagnostic Tests Needed (flagged, NOT written — audit is read-only)

These resolve the numerical uncertainties above. All are L1 pure-logic tests (no running game),
addable to `Tests/Insights/`:

1. **`RunningStat.Without` cancellation-regime equivalence test** (High — pins finding #3).
   Build a `global` of ~200 near-identical large samples (e.g. all ≈ 1000.0 ± 0.001), subtract a
   `subset` of ~190 of them, assert the recovered complement's `Variance` matches a directly-built
   complement to a relative tolerance — and that `Stats.WelchTTestP(inContext, complement)` does
   NOT return < 0.05 when the two are statistically indistinguishable. The current
   `RunningStat_Without_RecoversTheComplement` uses well-separated small integers and cannot catch this.

2. **`WelchTTestP` Type-I-rate test on skewed unequal-n input** (Medium — pins finding #5).
   Draw both samples from the same low-mean right-skewed distribution (e.g. exponential-ish / Poisson
   proxy) with `nA=30, nB=200`, repeat ~2000 times, assert the empirical fraction of p<0.05 is not
   materially above 0.05 (the research predicts ~0.078 for the leaky regime). Documents the
   approximation's actual error rate rather than trusting the "accurate" docstring.

3. **`NewContributor` from-zero magnitude monotonicity test** (Low — pins finding #6).
   Two synthetic temporal baselines, both idle-early, one late=0.12 ms/t and one late=5 ms/t; assert
   the 5 ms record outranks the 0.12 ms record on `RankingScorer.Score`. Currently both pin to
   magnitude 1.0 via the `99d` sentinel, so this would fail today — confirming the finding.

4. **Bonferroni-conservatism characterisation test** (Low — informational, pins finding #7).
   Feed two correlated buckets (boss + hardmode often co-active) with the same real cost lift; assert
   `testsRun`-based correction suppresses a signal that a dependence-aware (Holm) step would retain —
   documents the power cost rather than asserting a fix.

---

## Modularisation Verdict

**No modularisation candidates in this cluster.** Largest `Insights/` file is `InsightRenderer.cs` at
390 LOC, well under the 550 threshold; the next are `ModObservatoryStat.cs` (328), `Insight.cs` (292),
`InteractionInsightDetectors.cs` (274), `ContextBaseline.cs` (227), `InsightStore.cs` (241). None are
top-decile repo-wide — the repo's largest is `HookInterceptor.cs` at 1227 and the 14th-largest is
`Js.Summary.cs` at 539, so the entire Insights cluster sits below the repo's top 14 files. The module
is already well-decomposed (one detector per file, one stat per file, the shared primitives factored
out in Wave 1).

---

## Invariant Compliance (spot-check)

- **Invariant 1 (read-only):** clean. Every detector and stat only *reads* collector accessors,
  reference frames, and LiteDB collections; nothing mutates game/world/other-mod state. `CrossSessionStore.Save`
  writes only the profiler's own `contextBaselines` collection.
- **Invariant 3 (descriptive, never normative):** clean. Spot-checked every `InsightRenderer` template
  (`:92-327`) against the banned-vocab header (`:1-5`): no "caused by"/"must remove"/"core mod"/"removable"/"bad mod".
  `HeapLeak` copy ("a restart resets it") and `GcPauseCulprit` (the docstring explicitly forbids "caused")
  stay observational. No new normative copy.
- **Invariant 5 (no mod-specific code):** clean. Grepped the detectors + frames for any named-mod string
  match: none. `ModOwnerCache.ForItem` (`ModObservatoryStat.cs:302`) resolves owner by generic surface,
  not by string-matching a mod id. Context buckets are vanilla-only (`DimHardmode/Boss/Invasion/Subworld`,
  `InsightsEngine.cs:287-290`). Example strings in docstrings ("Calamity", "Fargo's", "Verdant") are
  illustration only, never code paths.
- **Invariant 2 (off-thread eval, no pathological allocation):** one minor per-pass allocation flagged
  (`CollectorInsightInput`, finding above); detectors otherwise field-cache scratch. No per-tick path here
  (the engine runs at 1 Hz off-thread).
