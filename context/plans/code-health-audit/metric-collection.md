# Metric Collection & Detectors — Code Health Findings

**Cluster:** the zero-allocation per-tick frame engine + ring buffer + spike/stall detection.
**Files audited:** `Profiling/MetricCollector.cs` (586), `Profiling/RingBuffer.cs` (129), `Profiling/TickFrame.cs` (75), `Data/Detectors/StallDetector.cs` (594), `Data/Detectors/SpikeDetector.cs` (282), `Data/Aggregators/PerModAttribution.cs` (413), `Data/Aggregators/PerModSample.cs` (52), `Data/Aggregators/PerTickAttributionRing.cs` (287), `Data/Stats/Baseline.cs` (354).

**Finding count: 12** (1 high · 6 medium · 5 low), grouped by category, high→low within each.

Every finding below is FREE: identical observable behaviour, no new allocation, no new dependency, no new abstraction. Invariant 2 (zero-alloc hot path) and Invariant 1 (read-only) hold for each. Behavioural-impact column is honest: any non-`none` is called out and gated on a decision.

---

## Algorithm / Redundant Work

### Per-call `Stopwatch.Frequency` division on every harvest, every tick
- [x] `PerModAttribution.HarvestInto`/`HarvestHooksInto` recompute `1000d / Stopwatch.Frequency` per call; `MetricCollector.TimestampDeltaMs` recomputes it per tick — the project already proved the cached-reciprocal pattern in `Time.cs`. — IMPLEMENTED: `PerModAttribution.cs` static `readonly double TicksToMs` field; multiplied at `HarvestInto`/`HarvestHooksInto`. `MetricCollector.cs` static `readonly double TicksToMs` field; `TimestampDeltaMs` multiplies by it. Pin: `Tests/AuditPin_Metric_Reciprocal.cs`.
**Category:** Algorithm / Redundant Computation   **Severity:** high   **Effort:** small   **Behavioural Impact:** none
**Location:** `Data/Aggregators/PerModAttribution.cs:318` & `:348` — `HarvestInto()` / `HarvestHooksInto()`; `Profiling/MetricCollector.cs:574` — `TimestampDeltaMs()`
**Current State:** Both harvest methods open with `double ticksToMs = 1000d / Stopwatch.Frequency;`. `EndTick` calls `HarvestInto` + `HarvestHooksInto` (and, when alloc-tracking is on, two more harvest passes), plus `TimestampDeltaMs` once. That is a minimum of 3–5 reads of the `Stopwatch.Frequency` runtime property + 3–5 floating-point divisions per tick (60×/s). `Stopwatch.Frequency` is not a constant field — it is a property whose getter returns a static value but is not guaranteed by the JIT to be hoisted out of these separate call frames. `Profiling/Time.cs:46,62` already established the canonical fix in this exact codebase: cache `_ticksToMs = 1000.0 / Stopwatch.Frequency` once and re-anchor only on `Reset()`. The harvest path does not reuse it.
**Proposed Change:** Introduce a `private static readonly double TicksToMs = 1000d / Stopwatch.Frequency;` in `PerModAttribution` (and reuse for `TimestampDeltaMs`, or expose `Time.TicksToMs`). `Stopwatch.Frequency` is invariant for the life of the process on every platform tModLoader runs (it is the QPC / `mach_absolute_time` frequency, fixed at boot), so a process-lifetime cache is exactly correct and matches the established `Time.cs` convention. Multiply by the cached reciprocal instead of dividing.
**Justification:** A division is ~20–40 cycles vs a multiply ~3–5; the property read has its own call overhead. This is the single most-trafficked conversion in the harvest hot path and the codebase has already decided (in `Time.cs`) that the per-call division is worth eliminating. Conventions §5 ("`Stopwatch.GetTimestamp()` static reads") and the Invariant-2 zero-overhead posture both point the same way.
**Expected Benefit:** Removes 3–5 divisions + 3–5 property reads per tick (180–300/s) with byte-identical output (reciprocal-multiply vs divide differs only in the last ULP, far below the 0.25 ms half-bucket precision the baseline rounds to anyway). Aligns the harvest path with the `Time.cs` precedent.
**Impact Assessment:** None observable. Floating-point result differs at most in the last bit of the mantissa; every downstream consumer (smoothing EMA, histogram bucketing at 0.5 ms granularity) is orders of magnitude coarser. Diagnostic test recommended below to prove byte-equivalence on a fixed `Stopwatch.Frequency`.

### `Baseline.Recompute` re-reads the frame `EndTick` already holds as a local
- [x] `EndTick` builds `frame`, pushes it, then calls `Baseline.Recompute(history,…)` which immediately does `history[history.Count - 1]` — re-running the ring indexer's modulo-correction + a full `TickFrame` struct copy to fetch the frame already in hand. — IMPLEMENTED: added `Baseline.Recompute(history, in TickFrame newest, …)` overload (`Baseline.cs`) delegating to a shared `RecomputeCore` with a `hasNewest` flag; steady-state path feeds the in-hand frame to `OnFramePushed`, rebuild branch still reads `history`. `MetricCollector.EndTick` now passes `in frame`. Pin: `Tests/AuditPin_Baseline_FastPath.cs`.
**Category:** Algorithm / Redundant Computation   **Severity:** medium   **Effort:** small   **Behavioural Impact:** none
**Location:** `Data/Stats/Baseline.cs:158` — `Recompute()` → `TickFrame newest = history[history.Count - 1];`; called from `Profiling/MetricCollector.cs:458`
**Current State:** `EndTick` constructs `frame` (a multi-field struct: 2 longs + 2 doubles + 3 ints + an `EventContext`), pushes it by `in`, then hands `history` to `Baseline.Recompute`. On the steady-state path (`stateMatches == true`, `history.Count > 0`), `Recompute` re-fetches the newest frame via the indexer (`Baseline.cs:158`), which executes the `(uint)index >= (uint)_count` bounds check, the `_head - _count + index` arithmetic with the `< 0` correction (`RingBuffer.cs:91–106`), and copies the whole struct out by value — purely to read `FrameTimeMs` and `TimestampUnixMs` in `OnFramePushed`. `EndTick` still has the identical `frame` as a local.
**Proposed Change:** Add a `Recompute` overload (or an `OnFramePushed`-fed entry point) that takes the just-closed `in TickFrame frame` directly, so the steady-state path skips the indexer round-trip. Keep the existing `history`-based path for the resync/rebuild branch (test batch-push, post-`Reset` replay) where the frame-in-hand is unavailable. The `RebuildFromHistory` branch is unchanged.
**Justification:** The frame is already materialised one stack frame up; re-deriving it through the ring indexer is pure redundant work on the hottest method in the system. The resync branch genuinely needs `history`, so this is a targeted fast-path, not a refactor.
**Expected Benefit:** Eliminates one indexer call (bounds check + wrap arithmetic + full-struct copy) per tick on the steady-state path. Output identical — the same frame values flow into `OnFramePushed`.
**Impact Assessment:** None. The fast path receives the byte-identical frame; the slow (rebuild) path is untouched. Behaviour is unchanged for both the live game and the test harness.

---

## Data Layout / Memory Access

**Applicability decision (mandatory): APPLICABLE and high-value for this cluster.** This is the zero-alloc per-tick engine, so cache-friendliness of the per-tick arrays is exactly where Data-Layout findings pay off. The dominant per-tick structures are flat `double[]`/`long[]`/`float[]` grids indexed `[modId * CategoryCount + categoryId]` — already the correct SoA-style contiguous layout, not arrays-of-structs. The findings below are about *access order* and *struct width*, the two remaining levers, plus one alignment-consistency note. No layout finding proposes a new allocation.

### `PerTickAttributionRing.Push` re-reads `PerModAttribution.CategoryCount` and recomputes bases inside the hot per-tick loop
- [x] `Push` calls the `CategoryCount` property and recomputes `catBase`/`cell` index arithmetic per mod per category every tick; the property is a `.Length` read behind a static array, not a const. — IMPLEMENTED: added `private readonly int _catCount` to `PerTickAttributionRing` (set at construction beside `_modCount`, from the same `CategoryCount` the cat arrays are sized with); `Push`, `CopyLatestCategorySnapshot`, and `TryGetCategorySnapshot` now read the field.
**Category:** Data Layout / Memory Access · Redundant Computation   **Severity:** medium   **Effort:** trivial   **Behavioural Impact:** none
**Location:** `Data/Aggregators/PerTickAttributionRing.cs:141` (`int catCount = PerModAttribution.CategoryCount;`) and the inner loop `:152–178`
**Current State:** `catCount` is read once into a local at the top of `Push` (good), but the construction-time value is already known — the ring is sized with `PerModAttribution.CategoryCount` at `:105`. More importantly the inner double loop computes `int cell = catBase + c;` and indexes `_perModCatMs[byCatTickBase + cell]` — correct, contiguous, fine. The real micro-cost is that `CategoryCount` resolves to `CategoryNames.Length` (`PerModAttribution.cs:60`), a property dereference, on every `Push` even though the count is frozen at `Configure`. The ring already caches `_modCount`; it does not cache `_catCount`.
**Proposed Change:** Cache `_catCount` as a `readonly int` field at construction (it is fixed for the ring's life, set right beside `_modCount` at `:98`). Replace the per-`Push` `PerModAttribution.CategoryCount` read with the field. The same one-line cache applies in `CopyLatestCategorySnapshot` (`:225`) and `TryGetCategorySnapshot` (`:265`).
**Justification:** `_modCount` is already cached as a field for exactly this reason; `catCount` is its peer dimension and should be symmetric (Editing Discipline: match the neighbour). The category count cannot change after `Configure`, so a construction-time cache is correct.
**Expected Benefit:** Removes one static-property + `.Length` dereference per `Push`, per snapshot copy, and per lookup. Identical output. Brings the ring's two grid dimensions to the same caching treatment.
**Impact Assessment:** None. `CategoryCount` is immutable post-`Configure`; the ring is built after `Configure`. No behaviour change.

### `SumAll` is called twice per tick over the full per-mod grid; the harvest loop could fold the sum in
- [x] `EndTick` calls `SumAll(_perModRawMs)` (and `SumAll(_perModRawMsBackend1)` in Parallel) as a separate full pass over the cells array, immediately after the harvest+smoothing loop already walked the same array. — IMPLEMENTED: `total0` is now folded into the `_perModSmoothedMs` smoothing loop in `MetricCollector.EndTick` (ascending index, bit-identical to `SumAll`); the separate `SumAll(_perModRawMs)` call is deleted. The backend-1 `SumAll(_perModRawMsBackend1)` stays (no co-located pass to fuse). Pin: `Tests/AuditPin_Metric_FusedSum.cs`.
**Category:** Data Layout / Memory Access · Redundant Pass   **Severity:** medium   **Effort:** small   **Behavioural Impact:** none
**Location:** `Profiling/MetricCollector.cs:439` (`double total0 = SumAll(_perModRawMs);`) and `:445`; `SumAll` at `:508`
**Current State:** Lines 404–409 harvest `_perModRawMs` and run the smoothing EMA loop over all `cells` entries. Then line 439 runs `SumAll(_perModRawMs)` — a *second* full sequential pass over the identical array to accumulate the backend-0 total. The array is `ModCount * CategoryCount` doubles (126 for an 18-mod install, ~1400 for a 200-mod nightmare). On the second pass the data may already have been evicted from L1 if the intervening alloc-tracking harvest touched enough other arrays. The Parallel-mode branch (`:442`) harvests backend 1 into `_perModRawMsBackend1` and then `SumAll`s it too.
**Proposed Change:** Accumulate `total0` inside the existing smoothing loop at `:406–409` (one `total0 += _perModRawMs[i]` per iteration) rather than calling `SumAll` afterward. Same for backend 1: the harvest loop in `HarvestInto` could optionally return the sum, or the caller sums during a pass it already makes. The minimal free version: hoist the `total0` accumulation into the `:406` loop and delete the `:439` `SumAll` call.
**Justification:** Summing during the pass that already reads every cell is strictly fewer memory touches than a separate pass, with identical floating-point result (same addition order: ascending index). This is the classic "fuse two loops over the same array" win, and the array is the per-tick hot data.
**Expected Benefit:** Removes one full sequential pass over the per-mod grid per tick (two in Parallel mode). At 200 mods that is ~1400 fewer double-loads/tick (84k/s) plus better cache residency.
**Impact Assessment:** None *if the addition order is preserved* (ascending index, matching `SumAll`'s `for i` loop) so floating-point rounding is bit-identical. Flagged as `none` on that condition. A test asserting `fused total == SumAll(array)` for a randomised array pins it.

### `StallEvent` struct is wide (~140 bytes) and the 50-slot ring copies it whole on every `Push` and indexer read
- [ ] `StallEvent` inlines five `StallContributor` structs plus 13 scalar fields; `RingBuffer<StallEvent>` copies the entire struct by value on `Push` and on every `this[index]` read (including the per-stall `CountRecentStallsInWindow` loop).
**Category:** Data Layout / Memory Access   **Severity:** low   **Effort:** medium   **Behavioural Impact:** none (flagged)
**Location:** `Data/Detectors/StallDetector.cs:78–135` (`StallEvent`), `:377–385` (`CountRecentStallsInWindow` reads `_events[i]` per iteration)
**Current State:** `StallEvent` is 3 longs + 6 doubles + 3 ints + 2 enum bytes + 5× `StallContributor` (each `int`+`double` = 16 bytes) ≈ 136–144 bytes. `CountRecentStallsInWindow` is called inside `OnBeginTick`'s stall path and loops `for i in _events.Count`, each iteration doing `_events[i]` which copies the *whole* 140-byte struct out by value just to read `.StartTimestampUnixMs` (one long). This is off the per-tick steady path (only runs when a stall actually fires) so it is not Invariant-2 critical, but a stall storm (the UI-overlay-blocking case, 47 consecutive stalls) calls this 47×, each copying 50×140 bytes.
**Proposed Change:** This is genuinely on the fence for "free". The behaviour-identical micro-win is to add a `RingBuffer<T>` method that exposes a field by-ref / by-index without a full struct copy — but adding `ref readonly this[int]` to the generic ring is an API change touching the whole codebase (blast radius), so it is NOT free here. The *narrow* free version: in `CountRecentStallsInWindow`, the struct is read only for one long; there is no zero-touch way to read it without either a `ref` indexer (new API) or storing timestamps in a parallel `long[]` (new field/allocation). **Recommendation: leave as-is**; documented here as a known cost so a future "stall-storm overhead" measurement has a starting point. No edit proposed.
**Justification:** The only behaviour-preserving fixes require either a new generic-ring API (blast radius across every `RingBuffer<T>` user) or a parallel timestamp array (new allocation). Both violate the free constraint. Recording the cost is the correct audit action.
**Expected Benefit:** N/A — flagged, not actioned.
**Impact Assessment:** None (no edit). Surfaced for the potential-issues ledger.

---

## API Surface / Dead Code

### `MetricCollector.StallDetectorRef` internal getter has a misleading doc and no in-cluster consumer
- [x] The `internal StallDetector StallDetectorRef` property's XML doc describes the `Baseline` getter, not itself, and `grep` finds no reader of `StallDetectorRef` outside the type. — IMPLEMENTED: deleted the `StallDetectorRef` property from `MetricCollector.cs` (re-confirmed zero solution-wide consumers, including `Tests/`).
**Category:** API Surface / Dead Code · Documentation Rot   **Severity:** medium   **Effort:** trivial   **Behavioural Impact:** none
**Location:** `Profiling/MetricCollector.cs:304–311` — `StallDetectorRef`
**Current State:** The XML summary on `StallDetectorRef` reads "Shared baseline (median frame time, median tick period, allocation rate, calibration state) — kept here for the legacy `Baseline` getter further down…" — that is a copy-paste of the `Baseline` property's doc and describes the wrong member. The property exposes `_stallDetector`. A repo grep for `StallDetectorRef` returns only the declaration; no consumer reads it. It may have been a test seam since superseded by the public `Stalls` getter.
**Proposed Change:** Delete the property as dead code. A full-solution grep (`grep -rn StallDetectorRef . --include='*.cs'`, including `Tests/`) returned **zero** non-declaration consumers — it is dead solution-wide, not merely unused in this cluster. (Fallback if a reflection-based or string-keyed consumer ever surfaces: fix the misleading XML doc to describe the stall-detector accessor instead of the `Baseline` getter.)
**Justification:** A doc that describes a different member is worse than no doc — it actively misleads (Documentation Rot). Dead internal surface is maintenance weight. Both fixes are zero-behaviour.
**Expected Benefit:** Removes a misleading doc and (likely) one unused property, shrinking the type's surface.
**Impact Assessment:** None. If a hidden consumer surfaces, fall back to the doc-fix sub-case. Verify with `grep -rn StallDetectorRef` across the whole solution before deleting.

### `RingBuffer<T>.IsFull` and `.Oldest` appear to have no consumers in this cluster
- [x] `IsFull` and `Oldest` are part of the ring's public surface but the metric/detector cluster reads only `Push`, `this[]`, `Count`, `Capacity`, `Newest`, `Clear`. — IMPLEMENTED: removed both `RingBuffer<T>.IsFull` and `.Oldest` after re-confirming zero callers solution-wide (incl. `Tests/`; the only `Tests/` hits were a method name and a comment, not the members). Decision (b) (keep as complete-surface) was overridden by the brief's directive: truly zero callers repo-wide → remove.
**Category:** API Surface / Dead Code   **Severity:** low   **Effort:** trivial   **Behavioural Impact:** none
**Location:** `Profiling/RingBuffer.cs:64` (`IsFull`), `:117` (`Oldest`)
**Current State:** A full-solution grep (`grep -rn '\.IsFull' / '\.Oldest' . --include='*.cs'`, excluding the `RingBuffer.cs` declarations) returned **zero** consumers for both. `RingBuffer<T>` is a widely-reused generic primitive (frames, spike windows, stall events), so these are part of a coherent complete-surface for a general-purpose container — but no caller, anywhere in the solution or its tests, reads either.
**Proposed Change:** This is a genuine judgement call, deliberately left to the cross-cutting/owner pass rather than asserted here. Two defensible reads: (a) treat as dead public surface and delete both for a trivial free win; or (b) keep them as the deliberate complete-surface of a general-purpose ring (Engineering Standard: clear interfaces over speculative trimming — a reusable container legitimately exposes `IsFull`/`Oldest` even when current callers do not need them). I lean (b): the ring is a foundational primitive whose API completeness has documentation value, and the cost of two unused getters is near-zero. Recorded with the confirmed grep so the owner can decide.
**Justification:** The grep removes the uncertainty (confirmed unused), but "unused" on a foundational primitive's coherent surface is not the same as "should be removed" — unlike the `StallDetectorRef` case, where the member also carries a *wrong* doc and no design rationale. Recorded as a decision point, not a directive.
**Expected Benefit:** If deleted: two fewer unused getters. If kept: a complete, documented ring surface. Either is behaviour-neutral.
**Impact Assessment:** None either way. No edit made by this audit.

---

## Documentation Rot

### `BackendDivergence` doc/code drift vs the context dossier formula
- [ ] `metric-collection.md` documents `BackendDivergence = (BackendTotalMs1 - BackendTotalMs0)/max(BackendTotalMs0,1)`; the code guards with `< 1e-6` and divides by `baseline` (no `max(…,1)`), and the doc's `MetricCollector.cs:167` line reference is stale.
**Category:** Documentation Rot   **Severity:** low   **Effort:** trivial   **Behavioural Impact:** none
**Location:** code `Profiling/MetricCollector.cs:192–201` (`BackendDivergence`); doc `context/systems/metric-collection.md:98–104`
**Current State:** The dossier says the denominator is `max(BackendTotalMs0, 1)`; the code uses `if (baseline < 1e-6) return 0d; return (… ) / baseline;`. These are different guards (a 0.5 ms baseline divides by 0.5 in code, by 1 in the doc). The doc also cites `MetricCollector.cs:167` for the property, but it now lives at `:192`. The doc is in the `context/` tree, not production source, so editing it is outside the "never edit production" rule but is also outside this finding-file's write scope; flagged for the doc-owner.
**Proposed Change:** Update `context/systems/metric-collection.md` to match the implemented guard (`< 1e-6` epsilon, divide by the raw `baseline`) and fix the line reference. No code change — the code is correct; the doc drifted.
**Justification:** The dossier is the maintained "current reality" per the Source Hierarchy; a wrong formula there will mislead the next reader into "fixing" correct code. Source Hierarchy rule: code determines reality.
**Expected Benefit:** Doc matches code; the divergence formula and line anchor are trustworthy again.
**Impact Assessment:** None (doc-only). Listed so the upkeep-context pass corrects it; no production file touched.

### `TickFrame.ModSamples` is permanently `null`; doc implies a wired future that the layout already supersedes
- [ ] `EndTick` hard-codes `ModSamples = null` with the comment "a later memory-tuning step"; the per-mod data actually lives in `PerModAttribution`/`PerTickAttributionRing`, so the field is effectively vestigial.
**Category:** Documentation Rot · API Surface   **Severity:** low   **Effort:** trivial   **Behavioural Impact:** none (flagged)
**Location:** `Profiling/TickFrame.cs:58–64` (`ModSamples`), set `null` at `Profiling/MetricCollector.cs:395`
**Current State:** `TickFrame.ModSamples` is a `PerModSample[]?` that is always `null` — the comment at `MetricCollector.cs:395` says per-frame per-mod arrays are "a later memory-tuning step," and `TickFrame.cs:58` says it will be wired "a later milestone." Meanwhile the dossier (`metric-collection.md:40`) states plainly: "Per-mod totals are **not** in `TickFrame`. They live on `PerModAttribution`." The architecture chose the `PerTickAttributionRing` (float, 50% narrower) over per-frame `PerModSample[]` on the ring; `ModSamples` is the road not taken. `PerModSample` itself IS still used as a type elsewhere (it is linked in the test project), so it is not dead.
**Proposed Change:** **Leave the field as-is** (removing it is a struct-layout change that ripples to every `TickFrame` consumer and the persisted shape — not free), but the *comment/doc* should be corrected to state the field is reserved/unused and that per-mod data is carried by `PerTickAttributionRing`, so a future reader does not "complete" a feature the architecture has already routed around. The doc edit is in `context/`/source comments owned by the doc pass; flagged, not actioned here.
**Justification:** The forward-looking "will be wired later" comment contradicts the settled architecture (Editing Discipline: comments describe current desired state, not an abandoned plan). Removing the field is a blast-radius change, so only the comment is in scope, and that is a production-source edit this audit does not make.
**Expected Benefit:** Stops the comment from advertising a superseded plan; clarifies that the ring is the per-mod carrier.
**Impact Assessment:** None. No code or layout change proposed; flagged for the owner to correct the stale comment.

---

## Consistency / Clarity

### Two ring implementations with divergent wrap strategies and no cross-reference
- [ ] `RingBuffer<T>` wraps with a branch (`_head+1==len ? 0 : _head+1`); `PerTickAttributionRing` wraps with a power-of-two mask. The choice is deliberate but undocumented, inviting a future "unify them" mistake.
**Category:** Consistency / Clarity   **Severity:** low   **Effort:** trivial   **Behavioural Impact:** none
**Location:** `Profiling/RingBuffer.cs:74` (branch wrap) vs `Data/Aggregators/PerTickAttributionRing.cs:99–102,145–146` (mask wrap)
**Current State:** `RingBuffer<T>` is a generic value-type ring sized to the *exact* requested capacity (1800 for frames) and wraps via a conditional. `PerTickAttributionRing` rounds capacity *up* to a power of two so wrap is a single `& mask`. Both are correct; the mask trick only works because the second ring accepts the rounded-up capacity, whereas `RingBuffer<T>` must honour exact capacity (1800 frames = exactly 30 s; rounding to 2048 would change the observable retention window — so masking `RingBuffer<T>` is NOT a free change). Nothing in either file notes *why* they differ, so a future optimiser might try to mask `RingBuffer<T>` and silently break the 30 s contract, or try to make `PerTickAttributionRing` exact-capacity and lose the mask win.
**Proposed Change:** Add a one-line cross-reference comment in each file noting the deliberate divergence and the reason: `RingBuffer<T>` is exact-capacity (retention window is a hard contract, so no pow2 rounding), `PerTickAttributionRing` is pow2-rounded (retention is a soft floor, extra slots are free). This is a comment-only change to production source — flagged, not actioned by this audit (no edits), but it is the cheapest guard against the "unify the rings" footgun.
**Justification:** The divergence is load-bearing and non-obvious; the masking memory note from `notes/` (memory map) explains it for `PerTickAttributionRing` but `RingBuffer<T>` has no counterpart. Cross-referencing prevents a behaviour-breaking "consistency" refactor.
**Expected Benefit:** Future readers see the two rings are intentionally different and why; no behaviour change.
**Impact Assessment:** None (comment-only; flagged for the owner since this audit makes no edits).

### `StallDetector` carries four `ClassifyCause` overloads + two `ClassifySeverity` overloads as back-compat shims
- [ ] The classifier has a 4-arg, 5-arg, 6-arg, and 7-arg `ClassifyCause`, plus a 1-arg and 2-arg `ClassifySeverity`; the shorter ones exist for tests/back-compat and forward to the full overload with hard-coded defaults.
**Category:** Consistency / Clarity · API Surface   **Severity:** low   **Effort:** small   **Behavioural Impact:** none (flagged)
**Location:** `Data/Detectors/StallDetector.cs:429–471` (`ClassifyCause` ×4), `:529–541` (`ClassifySeverity` ×2)
**Current State:** Production calls the 7-arg `ClassifyCause` (`:360`) and 2-arg `ClassifySeverity` (`:361`). The shorter overloads (`:429`, `:446`, `:450`; `:540`) fan out to the full forms with defaults like `baselineMs: 16.67d` and `focusHeldAcrossGap: true`. The `StallClassifierTests.cs` file is the consumer of several shorter forms. This is a legitimate testability seam (pure static classifier tested across overloads), so it is not waste — but the count has grown to where a reader cannot tell at a glance which is the "real" entry point.
**Proposed Change:** **Leave the overloads** (they are an active test surface — removing them breaks `StallClassifierTests.cs`, which is not free), but a one-line XML note on the full 7-arg overload marking it "the production entry point; shorter overloads are test/back-compat shims that supply defaults" would clarify intent. Comment-only production edit — flagged, not actioned.
**Justification:** The overloads are exercised by tests (verified: `StallClassifierTests.cs` is in the test project), so they are not dead and must not be trimmed. The only free improvement is a clarity comment, which is a production edit outside this audit's write scope.
**Expected Benefit:** A reader immediately identifies the canonical classifier entry vs the shims.
**Impact Assessment:** None. No removal (would break tests); comment-only suggestion flagged.

---

## Modularisation Verdicts (required)

### `MetricCollector.cs` (586 lines) — **LEAVE AS-IS**
One cohesive responsibility: drive the per-tick lifecycle (`BeginTick`/`EndTick`) and own the parallel CPU+alloc smoothing/rolling/history arrays. The ~20 array fields look heavy but are five logical bands (raw/smoothed/average/history/rolling) × two surfaces (mod, hook) × two metrics (ms, bytes), each genuinely needed by the per-tick math; splitting them across helper types would force the hot `EndTick` loop to chase references across objects, working *against* Invariant 2's cache-locality goal. The class has no second consumer to extract an abstraction for (Engineering Standard: build the seam when the second consumer arrives). Verdict: cohesive hot-path engine; keep whole.

### `StallDetector.cs` (594 lines) — **LEAVE AS-IS**
The line count is dominated by (a) two well-documented enums (`StallCause`, `StallSeverity`) with per-member XML rationale, (b) the `StallEvent`/`StallContributor` data structs, and (c) the pure-static classifier overload family — all tightly coupled to the one detection algorithm and already pure-logic / unit-tested in isolation (`StallClassifierTests.cs`, `StallDetectorTests.cs`). The `StallEventsView` inner class is a 14-line allocation-free wrapper, correctly local. There is no independent sub-responsibility that a second caller needs; extracting the enums or the classifier into separate files would scatter one algorithm's contract across the tree for no consumer benefit. Verdict: single algorithm + its data + its tested classifier; keep whole.

---

## Flagged Diagnostic Tests (none written — flags only)

These are *feasible* because `RingBuffer`, `Baseline`, `StallDetector`, `PerModSample`, `PerModAttribution` are already linked into `Tests/PerformanceProfiler.Tests.csproj`. `PerTickAttributionRing`, `SpikeDetector`, and `MetricCollector` are **not** linked (they pull tModLoader-transitive deps or are not in the Compile list), so equivalence tests touching those would need a link addition first.

| # | Test name | Target surface | Asserts | Why |
|---|---|---|---|---|
| 1 | `HarvestInto_CachedReciprocal_ByteIdenticalToPerCallDivision` | `PerModAttribution` (linked) | For a randomised `long[]` of stopwatch ticks, `bits(value * cachedReciprocal) == bits(value * (1000d/Stopwatch.Frequency))` — or within 1 ULP if exact-equal fails | Pins finding #1 (cached `TicksToMs`) as behaviour-preserving before anyone edits the harvest. |
| 2 | `Baseline_FastPathFrame_EqualsHistoryPath` | `Baseline` (linked) | After pushing N frames, the median/MAD from a frame-fed `Recompute` overload equals the current `history`-fed `Recompute` | Pins finding #2 (skip the redundant indexer read) — proves the fast path and the rebuild path agree. |
| 3 | `SumAll_FusedIntoSmoothingLoop_EqualsSeparatePass` | (would need `MetricCollector` linked) | Fused `total0` accumulated in the smoothing loop == `SumAll(array)` for a randomised grid, bit-identical | Pins finding #5 (loop fusion) on floating-point order. Requires linking `MetricCollector` first. |
| 4 | `PerTickRing_CatCountField_MatchesPropertyEverywhere` | (would need `PerTickAttributionRing` linked) | Snapshot/lookup output identical whether `catCount` comes from the field or the property | Low value (the property is immutable post-Configure); only if finding #3 is contested. |

High-confidence-from-reading (no test needed): #4 (`StallDetectorRef` doc), #6/#7 (dead-surface greps), #8/#9 (doc rot), #10/#11/#12 (comment/consistency). These are inspection-resolvable.

---

## Potential-Issues Candidates (for the orchestrator's ledger, not findings)

These are NOT free code-health wins — they are correctness/robustness observations that need a decision, surfaced per the cross-cutting brief:

1. **`PerModAttribution` is process-static, single-instance.** Two `MetricCollector`s in one process (a test creating one while the game owns another, or a hypothetical multi-world) would share the same static accumulator and cross-contaminate. Today only one collector is live, so it is safe; the static shape (Conventions §2) is load-bearing for IL-emitted callers, so this is by-design, but it is a latent footgun worth the ledger. (Echoes the dossier's `Configure`-called-once note.)
2. **`Baseline.RebuildFromHistory` `stateMatches` heuristic** (`Baseline.cs:150–152`) assumes `history.Count == _frameCount` OR (full ring). A test path that pushes frames, calls `Recompute`, then `Clear`s history and pushes a *different* count that happens to collide could skip a needed rebuild. Narrow, test-only, but the equality-or-full check is not airtight against adversarial replay.
3. **`StallDetector` Process CPU read on the stall path** (`:328`) calls `_self.Refresh()` inside a `try/catch`; if `Refresh` is slow under sandboxing it adds latency to the very tick that already stalled. Off the steady path, but the stall path measuring itself is mildly self-referential. Acceptable; noted.
4. **`PerTickAttributionRing` pow2 rounding silently doubles category-snapshot memory** for non-pow2 `categorySnapshotTicks` (120 → 128 is cheap, but a value like 130 → 256 nearly doubles). Default (120→128) is fine; flagged so a future tuning knob change does not surprise the memory budget.

---

## Research Record (obligation)

| Mode | Query | Top source used | URL |
|---|---|---|---|
| 2 (specific algorithmic) | "median MAD streaming outlier detection ring buffer histogram allocation-free per-frame" | MDPI survey on streaming outlier detection — confirms the histogram-over-sliding-window median + 3·MAD threshold this codebase implements is the standard allocation-free streaming pattern; validates `Baseline`'s incremental-histogram design as best-practice, not a smell. | https://www.mdpi.com/2079-9292/13/16/3339 ; https://aakinshin.net/posts/harrell-davis-double-mad-outlier-detector/ |
| 3 (anti-pattern catalogue) | "common allocation anti-patterns C# per-frame hot loop boxing IReadOnlyList foreach enumerator" | nede.dev / Criteo / InfoQ — `foreach` over an `IReadOnlyList<T>` interface (vs a concrete struct-enumerator type) heap-allocates the `IEnumerator<T>`. Directly applicable: `EventsFeed.cs:82,112` does `foreach (var w in collector.Spikes)` / `collector.Stalls` over the `SpikeWindowsView`/`StallEventsView` `IReadOnlyList` wrappers (yield iterators → heap alloc per enumeration). **Not flagged as a cluster finding** because `EventsFeed` is an on-demand stat consumer, NOT the per-tick path, so Invariant 2 does not bite — recorded here so the cross-cutting/reader-side cluster can decide whether to add a struct-enumerator to the views. | https://nede.dev/blog/preventing-unnecessary-allocation-in-net-collections/ ; https://medium.com/criteo-engineering/memory-anti-patterns-in-c-7bb613d55cf0 ; https://www.infoq.com/articles/For-Each-Performance/ |

The research confirmed two things: (1) `Baseline`'s histogram-based streaming median+MAD is the textbook allocation-free approach, so no algorithmic finding there — the v0.6.1 incremental histogram is correct and well-chosen; (2) the only allocation anti-pattern that touches this cluster's *types* (foreach-over-interface boxing of the detector views) fires on the off-tick reader path, not the hot path, so it is a cross-cutting note rather than a cluster finding.
