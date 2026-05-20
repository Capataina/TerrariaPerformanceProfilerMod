# Insights Engine — Code Health Findings

**Systems covered:** `Profiling/Insights/**`, `UI/Overlay/Tabs/InsightsTab.cs`, session-log integration references  
**Finding count:** 4 findings (0 critical, 0 high, 3 medium, 1 low)

## Known Issues And Active Risks

### Score Share-Based Insight Magnitudes As Fractions, Not Ratios Above One
- [ ] Adjust insight ranking so share/effect-size magnitudes in `[0, 1]` still affect ordering

**Category:** Known Issues and Active Risks  
**Severity:** Medium  
**Effort:** Small  
**Behavioural Impact:** Possible (requires decision) — player-visible insight ordering changes to reflect current detector evidence.

**Location:**
- `Profiling/Insights/RankingScorer.cs:50-56` — values `<= 1` normalise to zero
- `Profiling/Insights/Detectors/HotHookDominanceDetector.cs:65-86` — stores hook share in `RatioOrDelta`
- `Profiling/Insights/Detectors/AllocationBurstDetector.cs:64-85` — stores allocation share in `RatioOrDelta`
- `Profiling/Insights/Detectors/PeakContributorToSpikeDetector.cs:67-91` — stores spike contributor share in `RatioOrDelta`

**Current State:**
The scorer interprets `RatioOrDelta` as a ratio where `1x` means no magnitude, but active detectors store fractional shares such as `0.40` or `0.90`. Those values all collapse to zero magnitude, so a 40% contributor and a 90% contributor are ranked as equal on the magnitude component.

**Proposed Change:**
Represent magnitude kind explicitly, or normalise by pattern: share/effect-size patterns map `[0,1]` directly to `[0,1]`, while true ratio patterns keep the existing soft knee above `1x`.

**Justification:**
The insight store’s purpose is to surface the most useful records first. Current scoring discards the key magnitude signal for the detectors that are actually live today.

**Expected Benefit:**
Top-N insights become more evidence-driven: larger hot-hook shares, allocation shares, and spike-contributor shares rank above smaller ones when other factors match.

**Impact Assessment:**
The set of emitted records does not change, but ordering can change. That is a player-visible correction, not a behaviour-neutral refactor.

### Prevent Untested Observations From Promoting To Medium Confidence By Repetition Alone
- [ ] Gate confidence promotion on evidence strength as well as confirmation count

**Category:** Known Issues and Active Risks  
**Severity:** Medium  
**Effort:** Small  
**Behavioural Impact:** Possible (requires decision) — confidence badges can become more conservative.

**Location:**
- `Profiling/Insights/InsightStore.cs:207-212` — three confirmations promote to `Medium` regardless of `pAdjusted`
- `Profiling/Insights/Detectors/HotHookDominanceDetector.cs:80-86` — emits `PValueAdjusted = 1d`
- `Profiling/Insights/Detectors/AllocationBurstDetector.cs:79-85` — emits `PValueAdjusted = 1d`
- `Profiling/Insights/Detectors/PeakContributorToSpikeDetector.cs:82-88` — emits `PValueAdjusted = 1d`

**Current State:**
The store promotes repeated records to `Medium` even when detectors explicitly mark that no hypothesis test ran (`PValueAdjusted = 1d`). Repetition is useful evidence of persistence, but it is not statistical support.

**Proposed Change:**
Make `PromoteConfidence` require a statistical/evidence threshold for `Medium` and `High`, or introduce a separate persistence badge so repeated untested observations stay honestly labelled as low/preliminary.

**Justification:**
The insights plan and external multiple-comparison research both treat false-positive control as load-bearing. A badge that looks stronger solely because the same untested record repeated risks violating the honesty contract.

**Expected Benefit:**
Player-facing confidence becomes harder to overstate, and mod-author-facing output remains defensible when screenshots or JSON reports are shared.

**Impact Assessment:**
The emitted insight content stays the same, but confidence labels can be lower. This is an intentional honesty correction.

### Separate Data-Strength Badges From Confidence Badges
- [ ] Add a player-visible data-strength badge (`this session`, `lifetime data`, `needs persistence`) distinct from statistical confidence

**Category:** Known Issues and Active Risks  
**Severity:** Medium  
**Effort:** Medium  
**Behavioural Impact:** Possible (requires decision) — player-visible row badges change.

**Location:**
- `README.md:193-196` — insight surfaces must show data-strength badges
- `Profiling/Insights/InsightRecord.cs:26-53` — has `Confidence` and `BaselineKind`, but no evidence-scope enum
- `UI/Overlay/Tabs/InsightsTab.cs:115-123` — badge text is only confidence

**Current State:**
Insights rows show `preliminary`/`Low`/`Medium`/`High`. They do not distinguish single-session evidence from lifetime data or persistence-gated evidence, even though the README makes that distinction part of the honesty contract.

**Proposed Change:**
Add an explicit evidence-scope/data-strength field to `InsightRecord` or derive one deterministically from the detector and persistence state. Render it alongside, not instead of, confidence.

**Justification:**
Confidence and data strength answer different questions. A highly repeatable observation inside one session is still weaker than lifetime data across multiple sessions, and the UI should make that visible.

**Expected Benefit:**
Brings the current Insights tab into alignment with the project’s player-trust contract and prevents single-session output from looking stronger than it is.

**Impact Assessment:**
Visible copy changes, but measurement data does not. Because the badge vocabulary is part of the README contract, this is a deliberate product-surface correction.

## Documentation Rot

### Correct The Gated Free-Removal Detector Comments
- [ ] Update `FreeRemovalCandidateDetector` comments so they match the engine’s gated-detector behaviour

**Category:** Documentation Rot  
**Severity:** Low  
**Effort:** Trivial  
**Behavioural Impact:** None

**Location:**
- `Profiling/Insights/Detectors/FreeRemovalCandidateDetector.cs:7-12`, `Profiling/Insights/Detectors/FreeRemovalCandidateDetector.cs:31-33`
- `Profiling/Insights/InsightsEngine.cs:74-80`

**Current State:**
The detector comment says records still emit and the renderer hedges, but `IsGated => true` and `InsightsEngine.Evaluate` skips gated detectors before calling `Evaluate`.

**Proposed Change:**
Rewrite the detector comment to state the actual contract: the detector is registered for roster/gate visibility but emits zero records until engagement data is wired.

**Justification:**
The code is conservative and safe; the problem is misleading documentation that can send future implementation work down the wrong path.

**Expected Benefit:**
Removes a false implementation hint from a sensitive honesty-contract detector.

**Impact Assessment:**
No runtime behaviour change. Comment-only cleanup.
