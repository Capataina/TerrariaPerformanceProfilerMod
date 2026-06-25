# Data Pipeline & Segments — Code Health Findings

**Cluster:** Data pipeline + segments (the calculation locus). **Files audited:** `Data/DataRegistry.cs`, `Data/DataStage.cs`, `Data/IDataStream.cs`, `Data/TickContext.cs`, `Data/Contracts/RolloutContracts.cs`, all of `Data/Aggregators/` (incl. `Segments/`), all of `Data/Collectors/`, all of `Data/Stats/`, plus `Insights/Publish/ModInteractionAggregator.cs` (registered into the same registry) and the cross-checked support types (`MetricCollector.PerModCategoryRawMs`, `BiomeRegistry.Biomes`, `WeatherSources.All`, `ModOwnerCache.ForItem`, `BiomeBitset`).

**Finding count: 21** (4 hot-path, 2 correctness, 1 dead-code, 6 duplication, 1 numerical, 4 inconsistency, 3 doc/stale-comment). Plus a Data-Layout applicability decision and modularisation verdicts.

**Freeness convention used below:** a finding is FREE only if it is behaviour-identical, adds no new dependency, no new abstraction the project hasn't already sanctioned, and no public-surface (snapshot/contract) change. Findings that fail that test are marked **NOT-FREE (flag-only)** and recorded as observations, not actions — several genuinely interesting ones (a 60×-scaled denominator candidate, an `IsEmpty`-vs-`Empty` normalisation, the Pearson `r` clamp's blast radius) live there.

---

## Hot Path (Zero-Allocation / Virtual-Dispatch)

The per-tick path in this cluster runs through `ProfilerSystem.PostUpdateEverything` → the frozen `DataRegistry.PerTickCallbacks` for-loop (drives `PerModUsageAggregator.Capture` and `PerModCostTimeSeriesAggregator.Capture`) **and** the directly-driven `SegmentDetector.OnTick` (called at `ProfilerSystem.cs:623`) + `EventAggregator.Accumulate` + `ContextTagger.Snapshot`. Findings below are confirmed against the backing types, not inferred.

### Interface-indexer dispatch on the per-tick segment fold (`IReadOnlyList<double>`)
- [x] `SegmentDetector.OnTick` folds per-mod cost by indexing an `IReadOnlyList<double>` once per (mod × category × open-segment) every tick; the backing object is a plain `double[]` whose concrete type is hidden behind the interface, defeating devirtualisation and bounds-check elision. — IMPLEMENTED: added `internal double[] PerModCategoryRawMsArray => _perModRawMs;` to `MetricCollector`; `SegmentDetector.OnTick`'s parameter changed to `double[]`; `ProfilerSystem.cs:623` passes `collector.PerModCategoryRawMsArray`. (MetricCollector/SegmentDetector are tModLoader-linked → not pin-testable; equivalence is by-reasoning: same buffer, concrete-array indexing, identical values.)

**Category:** Data Layout / Memory Access (per-tick virtual dispatch)   **Severity:** High   **Effort:** Medium   **Behavioural Impact:** None (identical numerics)
**Location:** `Data/Aggregators/Segments/SegmentDetector.cs:255` — `OnTick()` inner fold; argument originates at `Profiling/ProfilerSystem.cs:623` (`collector.PerModCategoryRawMs`), typed `IReadOnlyList<double>` at `Profiling/MetricCollector.cs:220`.
**Current State:** `OnTick(..., IReadOnlyList<double> perModCategoryRawMs)` runs `for m in modCount { for c in categoryCount { rowSum += perModCategoryRawMs[baseIdx + c]; } }` for **every open segment** (`SegmentDetector.cs:249-258`). At ~50 mods × ~6 categories × N open segments, that is hundreds–thousands of `IReadOnlyList<double>.this[int]` interface-dispatch calls per tick at 60 Hz. `MetricCollector` exposes only the interface (`public IReadOnlyList<double> PerModCategoryRawMs => _perModRawMs;`, backing field `private readonly double[] _perModRawMs;` at line 45) — no array accessor exists.
**Proposed Change:** Add an `internal double[] PerModCategoryRawMsArray => _perModRawMs;` accessor on `MetricCollector` (same-assembly, the field is already a `double[]`), change `SegmentDetector.OnTick`'s parameter and `ProfilerSystem.cs:623` to pass/accept `double[]`, and index the concrete array. `double[]` indexing is non-virtual and the JIT elides bounds checks in the counted loop. Identical output.
**Justification:** Confirmed `IReadOnlyList<double>` over a `double[]` — the indexer cannot devirtualise through the interface. This is the highest-frequency dispatch site in the cluster (multiplied by open-segment count). Invariant 2 names "no boxing, zero-allocation" for the hot path; interface dispatch is the same class of avoidable hot-path cost the project already eliminated for the callback loop (static-delegate for-loop, `DataRegistry.cs:34-37`).
**Expected Benefit:** Removes the per-element interface dispatch on the single hottest fold in the cluster; restores bounds-check elision. No allocation either way (the element is a `double` by value — no boxing), so this is dispatch/JIT-quality, not GC.
**Impact Assessment:** Blast radius crosses into `Profiling/` (the new accessor on `MetricCollector` + the `ProfilerSystem.cs:623` call site). The accessor is purely additive and `internal`. **FREE** in behaviour; flag the cross-file touch. Measure against the overhead budget before declaring done (Invariant 2 makes the measurement part of "done").

### Same interface-indexer pattern in `PerModCostTimeSeriesAggregator.OnTick`
- [x] The F3 per-tick cost-bucket fold reads `c.PerModCategoryRawMs` as `IReadOnlyList<double>` and indexes it per element, despite the class doc claiming "no foreach over interfaces" — the indexer is still interface dispatch. — IMPLEMENTED: `PerModCostTimeSeriesAggregator.OnTick` now holds `double[] perCat = c.PerModCategoryRawMsArray` (reuses the accessor from the previous finding); `perCat.Count` → `perCat.Length`. Class doc corrected: "no interface indexing (the `double[]` is indexed directly so the JIT devirtualises and elides bounds checks)".

**Category:** Data Layout / Memory Access (per-tick virtual dispatch)   **Severity:** High   **Effort:** Medium   **Behavioural Impact:** None
**Location:** `Data/Aggregators/PerModCostTimeSeriesAggregator.cs:151,168,178` — `OnTick()`.
**Current State:** `IReadOnlyList<double> perCat = c.PerModCategoryRawMs;` (line 151) then `sum += perCat[baseIdx + catId];` (line 178) inside the per-mod/per-category fold. Doc-comment (lines 27-34) asserts the loop has "no LINQ, no foreach over interfaces, no allocations" — true for `foreach`, but the indexed access is still through `IReadOnlyList<double>`.
**Proposed Change:** Reuse the `MetricCollector.PerModCategoryRawMsArray` accessor from the previous finding; hold `double[] perCat` here too. The shape-drift guard (`endIdx > perCatLen`, line 173) works unchanged against `perCat.Length`.
**Justification:** Identical mechanism and backing type to the `SegmentDetector` finding; both fixes share the one new accessor, so this rides for free once that lands.
**Expected Benefit:** Same — devirtualised indexing on a second per-tick fold (≤3600-bucket ring, modCount×catCount per tick).
**Impact Assessment:** Same accessor, same `Profiling/` blast radius. **FREE** in behaviour. Correct the doc-comment to say "no interface indexing" once fixed (it currently overstates the guarantee).

### Interface-indexer dispatch on the per-tick biome attendance fold (`IReadOnlyList<BiomeDescriptor>`)
- [x] `PerModUsageAggregator.CaptureInstance` indexes `BiomeRegistry.Biomes` (an `IReadOnlyList<BiomeDescriptor>` over a `List<BiomeDescriptor>`) once per registered modded biome, every tick. — IMPLEMENTED: added `internal static List<BiomeDescriptor> BiomesList => _biomes;` to `BiomeRegistry`; `PerModUsageAggregator.CaptureInstance` now holds `List<BiomeDescriptor> biomes = BiomeRegistry.BiomesList`. Also fixed the lower-value rare-rebuild site `SegmentDetector.ComputeBiomeComposite` (holds `BiomesList` locally, indexes the concrete list) for consistency.

**Category:** Data Layout / Memory Access (per-tick virtual dispatch)   **Severity:** Medium   **Effort:** Medium   **Behavioural Impact:** None
**Location:** `Data/Aggregators/PerModUsageAggregator.cs:182,188` — `CaptureInstance()`; backing at `Profiling/Events/BiomeRegistry.cs:41,53` (`private static readonly List<BiomeDescriptor> _biomes; public static IReadOnlyList<BiomeDescriptor> Biomes => _biomes;`).
**Current State:** `IReadOnlyList<BiomeDescriptor> biomes = BiomeRegistry.Biomes;` then `string? owner = biomes[b].ModName;` per modded biome bit set this tick.
**Proposed Change:** Add `internal static List<BiomeDescriptor> BiomesList => _biomes;` (or a `BiomeDescriptor[]` accessor) on `BiomeRegistry` and index the concrete `List<T>`/array. `List<T>.this[int]` is non-virtual and devirtualisable; the array form additionally elides bounds checks.
**Justification:** Same dispatch class as the `double[]` findings. `BiomeDescriptor` is a `readonly record struct` returned by value, so no boxing — purely a dispatch/JIT-quality win. Frequency is lower (only iterates modded biomes the player currently stands in), hence Medium not High.
**Expected Benefit:** Devirtualised indexing on the per-tick biome fold.
**Impact Assessment:** Blast radius into `Profiling/Events/BiomeRegistry.cs` (additive `internal` accessor). The **same** finding applies to `SegmentDetector.ComputeBiomeComposite` at `SegmentDetector.cs:534` (`BiomeRegistry.Biomes[i].DisplayName`), but that site is on the *rare composite-rebuild* path (memoised behind `_cachedCompositeValid`, `SegmentDetector.cs:520`), not the steady-state per-tick path, so it is much lower value — fix it in the same pass for consistency, but it is not the motivation. **FREE** in behaviour.

### `ModOwnerCache.ForItem` on the per-tick loadout fold — VERIFIED CLEAN (recorded so it is not re-flagged)
- [ ] (no action) The per-equipped/held-item `ModOwnerCache.ForItem(it.type)` call on the per-tick path returns an interned/cached string, not a freshly-built one — no per-tick allocation.

**Category:** Algorithm & Performance (verified non-finding)   **Severity:** Info   **Effort:** n/a   **Behavioural Impact:** None
**Location:** `Data/Aggregators/PerModUsageAggregator.cs:254,274` calling `Profiling/ModOwnerCache.cs:37-42`.
**Current State:** `ForItem` early-returns the shared literal `"Terraria"` for vanilla types, else `_byTypeId.GetOrAdd((Kind.Item, itemType), static k => …)` over a `ConcurrentDictionary<(Kind,int), string>` (line 35) with a `static` factory lambda. The `(Kind,int)` key is a value tuple (no boxing on a generic-dictionary lookup), the `static` lambda captures nothing, and on the steady-state hit path `GetOrAdd` returns the cached reference with no allocation. The downstream `mn == "Terraria"` ordinal compare and `_modIdByName` dictionary lookup (`ModIdForName`, lines 125-129) are allocation-free given an interned input.
**Justification:** The earlier audit flagged this as a verification gap; reading `ModOwnerCache` closes it. The doc-comment's "no allocation (Invariant 2)" claim (line 268) holds. **No change.**

---

## Correctness

### `AllocationCausalityStat` ships a potentially-negative `FreedBytes`; its direct sibling already guards the identical subtraction
- [x] `freedBytes = HeapSizeBeforeBytes - HeapSizeAfterBytes` is emitted unguarded, so a GC where the heap is larger after than before (allocation during the collection window, or a sampling race) produces a negative `FreedBytes` on the UI. `GcPressureStat` computes the same subtraction and takes the absolute value. — IMPLEMENTED: `AllocationCausalityStat.cs:117` now `long freedBytes = Math.Abs(s.HeapSizeBeforeBytes - s.HeapSizeAfterBytes);` with a comment citing the `GcPressureStat.cs:88-90` sibling. (Implemented inline — the `StallMath` extraction was not done; that is a separate Pattern-Extraction finding left to its own pass.)

**Category:** Active Risks (correctness)   **Severity:** Medium   **Effort:** Trivial   **Behavioural Impact:** Output changes only in the currently-wrong negative case (a negative byte count becomes its magnitude)
**Location:** `Data/Stats/AllocationCausalityStat.cs:117` (`long freedBytes = s.HeapSizeBeforeBytes - s.HeapSizeAfterBytes;`) vs the guarded `Data/Stats/GcPressureStat.cs:88-90` (`long delta = …; if (delta < 0) delta = -delta; freedBytes += delta;`).
**Current State:** Unguarded subtraction flows straight into `AllocCausalityChain.FreedBytes` (`AllocationCausalityStat.cs:122`) and onto the L6 allocation-causality panel.
**Proposed Change:** `long freedBytes = Math.Abs(s.HeapSizeBeforeBytes - s.HeapSizeAfterBytes);` — matching the behaviour `GcPressureStat` already chose. (Best delivered via the `StallMath` extraction in the Pattern-Extraction section, which folds this fix in.)
**Justification:** `GcPressureStat` is the authority on the correct handling of this exact subtraction; the two siblings disagree and only the guarded one is right. This is descriptive correctness, not a normative-copy concern (Invariant 3 untouched).
**Expected Benefit:** No negative byte counts on the L6 panel.
**Impact Assessment:** Behaviour changes only where the current output is already wrong (negative). **FREE.**

### `DeathReplayStat.ResolvePrimaryBiome` always returns `string.Empty` with a dead `Main.LocalPlayer` read
- [x] Every path through `ResolvePrimaryBiome` returns `""`; the `Terraria.Main.LocalPlayer` read has no effect (result is only null-checked then discarded) and the surrounding try/catch guards nothing. — IMPLEMENTED: collapsed the body to `return string.Empty;` (removed the dead `Main.LocalPlayer` read + redundant try/catch); kept the method as the documented seam and folded the explanatory comment into the docstring. Output unchanged (`""` for every input).

**Category:** Dead Code / Complexity Hotspots   **Severity:** Low   **Effort:** Trivial   **Behavioural Impact:** None (already constant `""`)
**Location:** `Data/Stats/DeathReplayStat.cs:272-289`; caller at line 190 (`string primaryBiome = ResolvePrimaryBiome();`).
**Current State:** Documented future-refinement scaffold: reads `Main.LocalPlayer`, null-checks it, then returns `""` regardless; the `catch` also returns `""`.
**Proposed Change:** Collapse the body to `return string.Empty;` (keep the method as the documented seam, kill the misleading game-state read + redundant try/catch). Inlining `""` at the one call site is the more aggressive alternative but removes the documented seam, so the body-collapse is the lower-surprise option.
**Justification:** A guaranteed-constant method that reads game state implies it derives something it does not; removing the read makes the placeholder honest.
**Expected Benefit:** No behaviour change; removes a misleading runtime read on the OnDemand death-replay pull.
**Impact Assessment:** **FREE.** Keep the explanatory comment.

---

## Dead Code

### `Baseline._periodMadHist` is allocated and cleared but never sample-written or read
- [x] A 512-int (`HistogramBuckets`) histogram is allocated for the lifetime of the session and zeroed on `Reset`, but no code path ever fills or reads it — `ComputeMadFromHistory` reuses `_frameMadHist` for *both* the frame and period MAD branches. — IMPLEMENTED: deleted the `_periodMadHist` field (`Baseline.cs:76`) and its `Array.Clear` in `Reset`; rewrote the adjacent comment to describe three live histograms (`_frameHist`, `_periodHist`, the shared `_frameMadHist` scratch). Verified `ComputeMadFromHistory` uses `_frameMadHist` for both branches.

**Category:** Dead Code   **Severity:** Low   **Effort:** Trivial   **Behavioural Impact:** None
**Location:** `Data/Stats/Baseline.cs:76` (`private readonly int[] _periodMadHist = new int[HistogramBuckets];`), cleared at line 256; `_frameMadHist` is the one actually reused (lines 74, 325-326 `int[] hist = _frameMadHist;`).
**Current State:** Grep confirms `_periodMadHist` has exactly two references: its allocation (line 76) and its `Array.Clear` in `Reset` (line 256). No read, no sample write.
**Proposed Change:** Delete the field (line 76) and its `Array.Clear` (line 256). Update the adjacent comment at lines 71-72 ("Two histograms × two metrics = four total") to reflect three live histograms (`_frameHist`, `_periodHist`, the shared `_frameMadHist`).
**Justification:** Provably dead — allocated and zeroed for nothing. `Baseline.cs` is unit-test-linked (`Tests/PerformanceProfiler.Tests.csproj`), so the deletion is verifiable off-game (`dotnet test` + `dotnet msbuild` grep 'error CS').
**Expected Benefit:** Removes a 2 KB allocation held for the whole session and the dead clear; corrects the stale comment.
**Impact Assessment:** **FREE.** No consumer can reference a private field.

---

## Pattern Extraction (literal copy-paste → one helper)

### The GC-cause stall predicate + freed-bytes fold + pause-ms cast are duplicated across `GcPressureStat` and `AllocationCausalityStat`
- [ ] Three idioms — the "is this a GC stall" guard, the before/after-heap freed-bytes computation, and the `(long)s.TickPeriodMs` pause cast — recur in both GC-facing stats, with the freed-bytes copy in `AllocationCausalityStat` *missing* the abs guard the other has (the correctness finding above).

**Category:** Pattern Extraction   **Severity:** Medium   **Effort:** Low   **Behavioural Impact:** None (with the abs guard applied uniformly — see correctness finding)
**Location:** `Data/Stats/GcPressureStat.cs:81,88-90,86` and `Data/Stats/AllocationCausalityStat.cs:67,117,121`. Predicate `s.Cause != StallCause.MajorGc && s.Cause != StallCause.MinorGc` is byte-identical at `GcPressureStat.cs:81` and `AllocationCausalityStat.cs:67`.
**Current State:** Two files hand-roll the same three operations; one of the three has already drifted (the unguarded subtraction).
**Proposed Change:** A small `internal static class StallMath` (sibling to `Insights/Shared/Shares.cs` / `ModNames.cs`, or in `Data/Stats/`) with `static bool IsGcCause(StallCause c)`, `static long FreedBytes(in StallEvent s)` (returns `Math.Abs(before - after)`), `static long PauseMs(in StallEvent s)`. Three call sites collapse to the helpers; the correctness fix lands as a side-effect of the single `FreedBytes` definition.
**Justification:** This is the canonical 3+-instance extraction (predicate appears in 2 files, the freed-bytes/pause idioms across 2 files), and the duplication has *already* produced a divergence bug. A single home prevents recurrence.
**Expected Benefit:** Removes the duplication and structurally fixes the negative-`FreedBytes` divergence.
**Impact Assessment:** **FREE** — adds a tiny static helper of the same kind the project already sanctions (`Shares`, `ModNames`). No public-surface change.

### `DeathReplayStat`: two byte-identical `SourceKind` switches differing only in parameter type
- [ ] `ResolveDamageModId(DamageTakenRow)` and `ResolveDamageModIdFromContributor(DeathDamageContributor)` are identical `"npc"/"projectile"/"item"/_ => -1` switches over `.SourceKind`/`.SourceId`, differing only in which struct the two fields come from.

**Category:** Pattern Extraction   **Severity:** Low   **Effort:** Low   **Behavioural Impact:** None
**Location:** `Data/Stats/DeathReplayStat.cs:244-264`.
**Current State:** ~18 duplicated lines across the two methods.
**Proposed Change:** One private `static int ResolveDamageModId(string sourceKind, int sourceId, string[] modNames)` taking the two primitives; callers pass `(d.SourceKind, d.SourceId, …)` and `(top.SourceKind, top.SourceId, …)`.
**Justification:** Two literal copies of the same `SourceKind`→`ModOwnerCache.Kind` table. `"npc"/"projectile"/"item"` are generic interaction-shape strings (not mod identifiers), so Invariant 5 is untouched.
**Expected Benefit:** Removes the divergence risk between the two resolvers.
**Impact Assessment:** **FREE.** Single-file, private.

### `TopModFromSpike` is duplicated across `LagFingerprintAggregator` and `LagRhythmAggregator`
- [ ] The per-spike-window per-mod argmax fold (sum each mod's category cells, track the top) is implemented twice; the Rhythm copy is a strict subset (drops the `total`), and the Fingerprint copy carries an unused parameter.

**Category:** Pattern Extraction   **Severity:** Low   **Effort:** Low   **Behavioural Impact:** None
**Location:** `Data/Aggregators/LagFingerprintAggregator.cs:176-194` (returns `(topModId, topModMs, totalModMs)`) vs `Data/Aggregators/LagRhythmAggregator.cs:209-224` (returns `int topId`). Inner loop `for k in cats: sum += w.PerModCatMs[baseIdx + k]` and the `if (sum > topSum)` argmax are identical.
**Current State:** Two copies of the same attribution math; only one accumulates the running `total`.
**Proposed Change:** Extract one `static (int topId, double topSum, double total) TopModFromSpike(in SpikeWindow w)` into a shared `internal static class SpikeAttribution` beside the aggregators; Rhythm discards the unused tuple fields.
**Justification:** 3+-instance-spirit duplication (two copies of identical math that can drift). Both are OnDemand, so no hot-path concern. `w.PerModCatMs` is a `double[]` (direct indexing), so no interface-dispatch issue here.
**Expected Benefit:** One home for the per-spike argmax; removes divergence risk.
**Impact Assessment:** **FREE.** Folds in the unused-parameter finding below.

### `LagFingerprintAggregator.TopModFromSpike` unused `modNameCount` parameter
- [ ] `TopModFromSpike(in SpikeWindow w, int modNameCount)` never references `modNameCount`; the caller passes `modNames.Length` pointlessly.

**Category:** Dead Code   **Severity:** Trivial   **Effort:** Trivial   **Behavioural Impact:** None
**Location:** `Data/Aggregators/LagFingerprintAggregator.cs:177` (param), call at line 88 (`TopModFromSpike(in w, modNames.Length)`). Confirmed: body lines 178-194 do not read it.
**Proposed Change:** Drop the parameter and the argument. (Folds into the `SpikeAttribution` extraction.)
**Justification / Benefit / Impact:** Unused parameter; removal is **FREE**, single-file. Compiler-verifiable.

### `LagFingerprintAggregator` + `LagRhythmAggregator` hand-roll the name-or-"—" expression that `ModNames.SafeName` already provides
- [ ] Two aggregators inline `idx >= 0 && idx < modNames.Length ? modNames[idx] : "—"` while the newest sibling (`ModInteractionAggregator`) already routes through `ModNames.SafeName`, which has a 3-arg overload taking the out-of-range placeholder.

**Category:** Inconsistent Patterns / Pattern Extraction   **Severity:** Low   **Effort:** Trivial   **Behavioural Impact:** None
**Location:** `Data/Aggregators/LagFingerprintAggregator.cs:132-133` and `Data/Aggregators/LagRhythmAggregator.cs:179`; helper at `Insights/Shared/ModNames.cs:28-29` (`SafeName(int modId, IReadOnlyList<string> names, string outOfRange)`).
**Current State:** Three files, two idioms for the same bounds-checked resolution.
**Proposed Change:** Replace the inlined expressions with `ModNames.SafeName(id, modNames, "—")`. The 3-arg overload returns the caller's placeholder on out-of-range, so the `"—"` output is preserved exactly. (The base 2-arg overload returns `"mod-" + id` instead, so use the 3-arg form to match.)
**Justification:** `ModNames.SafeName` was created (per its own doc, lines 7-12) precisely to be the single home for this; two call sites predate it.
**Expected Benefit:** All three files share one resolver.
**Impact Assessment:** **FREE** — verified the `"—"` sentinel is preservable via the 3-arg overload.

### `EventsFeed`/spikes/stalls stat boilerplate header recurs across the OnDemand stats (NOT-FREE without an interface DIM)
- [ ] The `Cadence => OnDemand` / `Stage => Stat` / empty `Initialise/Reset/Dispose` / `CurrentSnapshotBoxed() => CurrentSnapshot()` block is byte-repeated across ~9 stat classes.

**Category:** Pattern Extraction   **Severity:** Low   **Effort:** Low   **Behavioural Impact:** None
**Location:** `CurrentSnapshotBoxed() => CurrentSnapshot();` at `KpiStat.cs:55`, `SpikesStat.cs:61`, `StallsStat.cs:55`, `SelfHealthStat.cs:100`, `EventsFeedStat.cs:72`, `PerModContextAttendanceStat.cs:95`, `TransitionTrackStat.cs:110`, `SegmentLifetimeStat.cs:71`, `SegmentModAttributionStat.cs:76` (9 identical one-liners). The wider Cadence/Stage/lifecycle block recurs in the same files.
**Current State:** `KpiStat.cs:31-35` documents the copy-paste as a *deliberate flat-template idiom* for new stats.
**Proposed Change:** Add a default interface method `object CurrentSnapshotBoxed() => CurrentSnapshot();` to `IDataStream<TSnapshot>` (.NET 8 supports DIMs) and delete the 9 overrides; OR an `abstract OnDemandStat<T>` base. The DIM removes the most-duplicated line with zero new type.
**Justification:** Genuine 9-instance literal duplication.
**Impact Assessment:** **NOT-FREE (flag-only).** The DIM touches `Data/IDataStream.cs` (outside this set's edit scope) and the base-class option both add an abstraction that *conflicts with the documented flat-template intent* at `KpiStat.cs:31-35`. Recommend confirming with the user before acting — it is the cleanest duplication target, but the project explicitly chose the flat form.

---

## Numerical Stability

### Pearson `r` returned unclamped — float rounding can push it past ±1
- [ ] The two-pass Pearson computes `cov` and the variances in separate loops; for near-perfectly-correlated series, float rounding can make `cov` marginally exceed `√(vi·vj)`, returning e.g. `1.0000000002`. Consumers assuming `r ∈ [-1,1]` (heatmap colour map, `Math.Acos`/`Math.Sqrt(1-r²)`) NaN or render out of range. — DEFERRED: the fix (`Math.Clamp(cov / denom, -1d, 1d)` at `ModInteractionAggregator.cs:218`) is correct and free, but the file lives under `Insights/Publish/` which is explicitly OUT of this agent's edit scope (the brief restricts edits to `Profiling/` + `Data/` and says "do NOT touch `Insights/`"). The Insights cluster owns it. Scope boundary > convenience — left for that pass.

**Category:** Active Risks (numerical)   **Severity:** Low   **Effort:** Trivial   **Behavioural Impact:** Output changes only in the currently-out-of-range float-escape case (clamps to the valid endpoint)
**Location:** `Insights/Publish/ModInteractionAggregator.cs:218` (`return cov / denom;`), guarded against zero-variance at lines 207/217 but not against the ±1 envelope.
**Current State:** The math body is otherwise correct and stable — proper two-pass centred form (`Σ(x-μ)(y-μ) / √(Σ(x-μ)²·Σ(y-μ)²)`), NOT the catastrophic-cancellation `sumSq - sum²/n` one-pass form. Only the final clamp is missing. The contract even declares `Pearson, // -1..+1` at `RolloutContracts.cs:507`.
**Proposed Change:** `return Math.Clamp(cov / denom, -1d, 1d);`
**Justification:** Industry-standard guard for two-pass correlation; the contract already promises the `[-1,1]` envelope the code does not enforce. (External research below confirms the two-pass centred form is the numerically stable choice — the only gap is the clamp.)
**Expected Benefit:** `r` can never escape `[-1,1]`; downstream `Acos`/`Sqrt` paths are NaN-safe.
**Impact Assessment:** **FREE** — only affects values already outside the declared range.

---

## Inconsistent Patterns

### Stream-name declaration: local `const string StreamName` vs `RolloutStreamNames.*`
- [ ] Five stats declare a local `public const string StreamName = "…"` and route `Name` through it; six+ stats reference the central `RolloutStreamNames` constants instead. Two idioms for the same registered-name concept.

**Category:** Inconsistent Patterns   **Severity:** Low   **Effort:** Low   **Behavioural Impact:** None
**Location:** Local-const: `KpiStat.cs:39`, `SpikesStat.cs:44`, `StallsStat.cs:38`, `SelfHealthStat.cs:73`, `EventsFeedStat.cs:50`. Central: `PerModContextAttendanceStat.cs:31`, `TransitionTrackStat.cs:43`, `SegmentLifetimeStat.cs:30`, `SegmentModAttributionStat.cs:33` (+ all v0.12 aggregators). Central table at `RolloutContracts.cs:531-560`.
**Current State:** `RolloutStreamNames` is the better pattern (single source of truth, mitigates the stringly-typed coupling risk the context doc flags under "Known Issues"). The five local-const files predate it.
**Proposed Change:** Migrate the five local `StreamName` consts into `RolloutStreamNames` and point `Name` at them.
**Impact Assessment:** **NOT-FREE (flag-only).** Those five `public const string StreamName` are public surface; consumers (registration in `PerformanceProfiler.RegisterDataPipeline`, dashboard lookups) may reference the const, not just the literal — `KpiStat.cs:34` documents `Lookup<KpiSnapshot>("kpi")` using the literal, but a grep of every consumer is required before moving the const. Blast radius beyond this cluster; confirm first.

### Empty-result representation: `IsEmpty` flag vs `Empty` sentinel
- [ ] `KpiSnapshot` signals emptiness with an `IsEmpty` boolean field; every other stat uses a `static readonly … Empty` sentinel. The *meaning* of "empty" also varies (a `WorldLoaded` flag + null list, vs an empty non-null array).

**Category:** Inconsistent Patterns   **Severity:** Low   **Effort:** Medium   **Behavioural Impact:** Changes struct semantics if normalised
**Location:** `KpiCalculator.cs:44` (`new KpiSnapshot { IsEmpty = true }`) vs `SpikesSnapshot.Empty`/`StallsSnapshot.Empty`/etc. The `RolloutContracts.cs` snapshots uniformly use `Empty` statics (e.g. lines 55, 92, 108).
**Current State:** KPI is the lone outlier on the emptiness idiom.
**Impact Assessment:** **NOT-FREE (flag-only).** Normalising `IsEmpty` → an `Empty` sentinel changes the snapshot struct's read semantics, which every KPI consumer relies on. This is the strongest *consistency* signal to escalate, but it is a public-shape change, not a free edit.

### `worldLoaded` derivation differs across stats — justified by differing early-returns (record so it is not "fixed")
- [ ] Some stats hardcode `worldLoaded: true` (reached only after an early `if (collector == null) return Empty;`); others compute `worldLoaded: sys?.Collector != null` because their early return keys off `db == null`, not the collector.

**Category:** Inconsistent Patterns (verified-justified)   **Severity:** Info   **Effort:** n/a   **Behavioural Impact:** None
**Location:** Hardcoded `true`: `GcPressureStat.cs:106`, `PerSegmentLagDensityStat.cs:104`, `AllocationCausalityStat.cs:57/129`, `SpikesStat.cs:58`, `StallsStat.cs:52`. Computed: `DeathReplayStat.cs:202`, `SessionChronicleStat.cs:181`, `SegmentLifetimeStat.cs:47/68`, `SegmentModAttributionStat.cs:50/73`.
**Justification:** The hardcoded-`true` sites have already proven `collector != null` via an early return; the computed sites reach their final constructor with a possibly-null collector. The inconsistency is *correct* — recorded so a future reader does not unify the computed ones into `true` and introduce a wrong value/null-deref. **No change.**

### `EventAggregator` and `ContextTagger` are per-tick but declare no `Cadence` contract
- [x] The two directly-driven per-tick classes (`EventAggregator.Accumulate`, `ContextTagger.Snapshot`) are plain `internal sealed class`es with no `IDataCollector`/`Cadence`; their hot-path nature lives only in XML comments, so the per-tick discipline rests on reviewer vigilance, not a declared contract. — IMPLEMENTED: added a "PER-TICK HOT PATH (Invariant 2) — no allocation, no interface dispatch" banner to the docstrings of both `EventAggregator.Accumulate` and `ContextTagger.Snapshot`, noting they are driven directly (not via the Cadence loop) so the banner is the contract.

**Category:** Inconsistent Patterns   **Severity:** Low   **Effort:** Trivial   **Behavioural Impact:** None
**Location:** `Data/Aggregators/EventAggregator.cs` (`Accumulate`, ~line 111), `Data/Collectors/ContextTagger.cs` (`Snapshot`, line 59).
**Proposed Change:** Add a one-line `// PER-TICK HOT PATH — no allocation, no interface dispatch` banner comment on `Accumulate` and `Snapshot`, making the contract visible at the call site (these are exactly the two files where a silent hot-path regression would hide).
**Justification:** A clarity-only addition; these classes are driven directly rather than through the registry's `Cadence`-gated loop, so they have no declared hot-path marker.
**Impact Assessment:** **FREE** (comment only). `foreach (var pair in WeatherSources.All)` at `SegmentDetector.cs:151` and `EventAggregator` is **clean** — `WeatherSources.All` is a `(WeatherFlags, Func<bool>)[]` array (verified `WeatherSources.cs:30`), so the `foreach` uses the array's struct enumerator with no boxing (a 16-byte tuple value-copy per iteration, harmless). The earlier suspicion that `.All` might be interface-typed is a **false positive** — recorded so it is not re-flagged.

---

## Documentation Rot (stale comments)

### `PerSegmentLagDensityStat` doc says "60 s clamp"; code clamps to a 1-second floor
- [x] The class doc-comment claims the baseline is "clamped to the elapsed minutes" when "fewer than ~60 s have elapsed", but the code is `Math.Max(elapsedMinutes, 1d/60d)` — a 1-second floor, not a 60-second clamp. — IMPLEMENTED: corrected `PerSegmentLagDensityStat`'s class doc to "the elapsed-minutes denominator is clamped to a 1-second floor (`Math.Max(elapsedMinutes, 1d/60d)`)".

**Category:** Documentation Rot   **Severity:** Trivial   **Effort:** Trivial   **Behavioural Impact:** None
**Location:** comment at `Data/Stats/PerSegmentLagDensityStat.cs:24-27` vs code at `:59`; identical `1d/60d` floor at `GcPressureStat.cs:94`.
**Proposed Change:** Correct the comment to "clamped to a 1-second floor so a young session doesn't divide by a near-zero elapsed time."
**Impact Assessment:** **FREE.**

### `SessionChronicleStat` doc says "four collections" then lists five
- [x] The class doc says it "Reads four DB collections" and then enumerates five (sessions, contextTransitions, segments, player deaths, spike windows). — IMPLEMENTED: `SessionChronicleStat` class doc "four" → "five".

**Category:** Documentation Rot   **Severity:** Trivial   **Effort:** Trivial   **Behavioural Impact:** None
**Location:** `Data/Stats/SessionChronicleStat.cs:21-22`.
**Proposed Change:** Change "four" → "five" (or recount precisely against the actual `db.*` reads in the method).
**Impact Assessment:** **FREE.**

### `Baseline.AllocBytesPerTickMedian` is named/documented "Median" but computed as an EMA
- [x] The public property and its XML doc say "Median per-tick allocation rate", but it is updated as an exponential moving average (`AllocEmaAlpha = 0.05`), so it is sensitive to recent spikes rather than robust like a true median. — IMPLEMENTED (doc-only, free slice): corrected `Baseline.AllocBytesPerTickMedian`'s XML doc to "EMA-smoothed mean per-tick allocation rate (α=0.05) ... not a robust median despite the historical name". The property RENAME is the separate not-free part (public-surface blast radius) — left unrenamed.

**Category:** Documentation Rot (misleading contract)   **Severity:** Low   **Effort:** Trivial (doc) / blast-radius (rename)   **Behavioural Impact:** None (doc fix)
**Location:** `Data/Stats/Baseline.cs:114` (doc + property) updated as EMA at line 181; the class comment at lines 96-99 admits the EMA.
**Proposed Change:** Fix the XML doc to "EMA-smoothed mean per-tick allocation rate (Î±=0.05)". A *rename* of the property to drop "Median" would be the fuller fix but is a public-surface blast-radius change (call sites elsewhere), so doc-only is the FREE slice.
**Impact Assessment:** Doc fix **FREE**; rename **NOT-FREE (flag-only)**.

---

## Data Layout / Memory Access — Applicability Decision (mandatory)

**Applicable, and it is where the highest-value findings in this cluster sit.** Two surfaces were assessed:

1. **The per-tick fold path** — APPLICABLE and actionable. The three interface-indexer findings in the Hot Path section (`SegmentDetector.OnTick`, `PerModCostTimeSeriesAggregator.OnTick`, `PerModUsageAggregator.CaptureInstance`) are all Data-Layout/Access wins: containers whose concrete backing is a plain `double[]` / `List<BiomeDescriptor>` are reached through `IReadOnlyList<T>`, defeating devirtualisation and bounds-check elision on the hottest folds. The fix is to expose the concrete backing via additive `internal` accessors. The rest of the per-tick path is already cache-friendly: `OpenSegment.PerModMs` is a pooled `double[]` indexed linearly (`SegmentDetector.cs:244-257`), `BiomeBitset` is a `ulong[]` walked word-at-a-time, the `PerModUsageAggregator._counters` use unsigned-compare bounds-check idioms, and `TickContext` is a `readonly ref struct` (stack-only, `EventContext` passed `in`).

2. **The snapshot-copy path** — APPLICABLE but already correct, no action. `PerModUsageAggregator.CurrentSnapshot` (count-then-exact-size), `PerModCostTimeSeriesAggregator.CurrentSnapshot` (exact-sized ring copy under brief lock, immutable-once-frozen buckets), and `GcPressureStat._heapMb.ToArray()` (documented immutability copy at ~0.33 Hz) are all correctly pre-sized single allocations on OnDemand cadence. No layout win available without a contract change.

No SoA/AoS restructuring is recommended — the parallel-array layout (`Segment.ModIds`/`ModMs`, `OpenSegment.PerModMs`) is already the data-oriented form and is frozen in the contracts.

---

## Modularisation Verdicts (required)

### `Data/Contracts/RolloutContracts.cs` (560 lines) — **LEAVE AS-IS**
This is a **frozen-contracts file by design** (header lines 8-29: "Locked in Wave 0 … so that downstream implementing agents all read from the same shape"). It is a flat catalogue of ~40 immutable `readonly record struct`/`readonly struct` snapshot signatures + the `RolloutStreamNames` constant table — zero logic, zero branching, no methods beyond positional records and `Empty` statics. Its 560 lines are *cohesion*, not complexity: the single-file location is exactly what gives downstream waves one place to compile against, and the context doc cites it as the mitigation for the stringly-typed-coupling risk. Splitting it by tab-family (Foundations/Timeline/Lag/Insights) would scatter the `RolloutStreamNames` table away from the types it keys and break the "one frozen shape file" contract for no readability gain (the `// ---` section banners already partition it visually). **Not a split candidate.**

### `Data/Aggregators/Segments/SegmentDetector.cs` (550 lines) — **LEAVE AS-IS (borderline; one optional seam)**
This is a genuine state machine with high *intrinsic* cohesion: the per-tick `OnTick` edge-sweep (open/close/fold) and the side-channel `OnSpike`/`OnStall`/`OnDeath`/`OnCombatHit`/`OpenBookmark`/`CloseBookmark` all mutate the same private `_open` dictionary, `_pool`, and the `_prev*` edge-detection state, under single-threaded game-thread ownership. The pieces are *not* independently testable without the shared state, which is the project's own modularisation test ("can you comment out one component and have the rest still work?") — here you cannot, because they are one machine. The class is already well-decomposed internally (`SweepOpen`/`SweepClose`/`OpenIfAbsent`/`CloseAndPublish`/`BuildSegment`/`Compose`/`ComputeBiomeComposite` are tight private helpers) and the heavy collaborators are *already extracted* (`SegmentStore`, `SegmentPromoter`, `SegmentNameTable`, `OpenSegment`, `Segment`). The one *optional* seam: `ComputeBiomeComposite` (lines 501-543, the FNV-1a hash + memoised name-rebuild) is the most self-contained pure-ish unit and could move to a `BiomeComposite` helper to make it unit-testable — but it reads `BiomeRegistry` static state and owns the `_cachedComposite*` fields, so the extraction is non-trivial and not free. **Recommend leave-as-is**; if a second consumer of biome-composite hashing ever appears, extract then (the project's "third real consumer" rule). The 550 lines are warranted by the state-machine cohesion.

---

## Flagged Diagnostic Tests (not written — per audit rules)

The test harness (`Tests/PerformanceProfiler.Tests.csproj`) already links `Baseline.cs`, `PerModAttribution.cs`, `StallDetector.cs`, `RolloutContracts.cs`, and the `Insights/Shared` math. The following pure-logic surfaces in this cluster are testable there and currently unguarded:

1. **`SegmentPromoter.Decide` promotion truth-table** — `Data/Aggregators/Segments/SegmentPromoter.cs` is pure logic (no game-state, explicitly "trivially unit-testable" per its doc, lines 22-25) but is **not linked** into the test csproj. Assert: bookmark/boss-kill/invasion/hardmode/subworld always promote; rare-weather set promotes; drama signals promote only for Boss/Combat/DeathBracket; the `lifetimeSampleCount >= 5 && avg > 1.5×lifetime` outlier gate. Target surface: link `SegmentPromoter.cs` (+ `Segment.cs`, `SegmentFamily.cs`, `WeatherFlags.cs`) and add `SegmentPromoterTests`. **Highest-value flagged test** — pure decision logic, zero runtime deps.

2. **Pearson clamp + zero-variance** — `Insights/Publish/ModInteractionAggregator.cs` correlation. Assert: identical series → `r == 1` (not `1.0000…2`); anti-correlated → `r == -1`; one constant series → `r == 0`; the clamp holds the envelope. The Pearson math is pure over `double[,]`; extract or test via the aggregator's compute path. Guards the numerical finding above.

3. **`Baseline` histogram median + MAD** — already linked. Assert the cumulative-count median (`Baseline.cs:302-313`) and `ComputeMadFromHistory` (reusing `_frameMadHist`) against known distributions; this also regression-guards the `_periodMadHist` deletion (the dead-field finding) by proving the period branch still computes correctly off the shared histogram.

4. **`SegmentDetector.Compose`/decompose round-trip** — the `(family << 56) | (uint)key` packing (`SegmentDetector.cs:488-491`) and its decode (`SweepClose` line 371-373). Assert pack→unpack identity across all `SegmentFamily` values and the full int key range, including negative keys (the `(uint)key` cast). Pure static; needs `SegmentDetector` to expose `Compose`/the decode as testable (currently private) — flag the visibility, do not change behaviour.

---

## External Research (obligation)

- **Query:** "C# zero allocation hot path foreach List<T> struct enumerator boxing IEnumerable pitfalls" — **mode:** keyword/broad. URL: https://andrewlock.net/making-foreach-on-an-ienumerable-allocation-free-using-reflection-and-dynamic-methods/ and https://nede.dev/blog/preventing-unnecessary-allocation-in-net-collections/. **Finding applied:** confirms that `foreach`/indexing over an *interface*-typed handle (`IEnumerable<T>`/`IReadOnlyList<T>`) prevents the compiler from using the concrete struct enumerator and blocks devirtualisation, whereas array/`List<T>` access via the concrete type is devirtualisable (and .NET 8+ guarded-devirt/object-stack-allocation does not see through an `IReadOnlyList<T>` field type). This is the basis of the three Hot-Path interface-indexer findings — and the reason `WeatherSources.All` (a concrete array) is *clean* while `MetricCollector.PerModCategoryRawMs` (interface-typed) is not.
- **Query:** "Pearson correlation incremental computation numerical stability sum of products vs Welford .NET" — **mode:** keyword/academic. URL: https://www.johndcook.com/blog/2008/09/26/comparing-three-methods-of-computing-standard-deviation/ and https://amytabb.com/til/2022/06/15/mean-variance-stability/. **Finding applied:** confirms the naive one-pass `Σx² − (Σx)²/n` form suffers catastrophic cancellation, while the two-pass centred `Σ(x−μ)²` / `Σ(x−μ)(y−μ)` form (exactly what `ModInteractionAggregator` and `LagRhythmAggregator` use) is the numerically stable choice. Validates that the only gap in the Pearson code is the missing `[-1,1]` clamp, not the formula itself.
