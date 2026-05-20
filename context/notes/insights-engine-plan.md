# Insights Engine — Implementation Plan

> **Status (2026-05-20): SHIPPED — preserved as historical research record.** Engine, store, scorer, renderer, four live detectors (HotHookDominance, AllocationBurst, FreeRemovalCandidate, PeakContributorToSpike) and six gated detector stubs shipped in e6a1020 and refined through audit rounds aa914ce/14fac59. Real p-value computations for live detectors and emit paths for gated detectors remain pending (see systems/insights-engine.md).
>
> Read the system files for current reality; this plan is the design brief that shipped, kept for the rationale.


> Scope: build the **Insights Engine**, a subsystem that turns the profiler's three data streams — per-tick `TickContext` (biome / weather / boss / invasion / subworld), per-spike attribution records, and per-mod CPU + allocation samples — into structured `InsightRecord`s and renders them as natural-language statements aimed at both players and mod authors. Honours all four Project Invariants. Targets tModLoader 1.4.4 on .NET 8. Sized and shaped to mirror `context/ILHook-migration-plan.md` and the sibling `context/notes/events-tab-plan.md`: evidence ledger, viability verdict, model, catalog, statistical foundations, pipeline, ranking, NLG, UI, JSONL integration, step-by-step sequence, risk register, honest gaps, worked example, mock.

The user's verbatim shape of the problem is:

> *"walking into the jungle biome during blood moon made x mod spike by y amount and go up to z ms"*

The engine has to deliver that sentence from raw data without hardcoding "if biome == Jungle". Modders are explicitly part of the audience: a useless insight is *"Mod X is slow during Blood Moon"*; a useful one is *"Mod X's GlobalNPC.AI rose from 0.5 ms baseline to 28 ms in the 2 ticks after Blood Moon started; primary contributor NpcId 12 (Zombie) AI"* — precise enough to drive a bug fix.

---

## 0. Research evidence ledger

| Claim | Evidence |
|---|---|
| **Welford's online algorithm** computes mean and variance in a single pass, allocation-free, numerically stable; recurrence is `delta = x - mean; mean += delta / n; M2 += delta * (x - mean); var = M2 / (n - 1)` | Wikipedia *Algorithms for calculating variance*; Embedded Related, "Ten Little Algorithms, Part 3: Welford's Method"; Knuth TAOCP Vol 2 §4.2.2. Standard since 1962; idiomatic in streaming systems. https://en.wikipedia.org/wiki/Algorithms_for_calculating_variance |
| **Median Absolute Deviation (MAD)** is the median of `|x_i - median(x)|`; robust to outliers because it inherits the breakdown point of the median (50 %), unlike stddev (0 %). Threshold `k * MAD` with `k ∈ [3, 5]` is the standard outlier rule. For approximate-normal data, `1.4826 * MAD` ≈ stddev | https://en.wikipedia.org/wiki/Median_absolute_deviation; Eureka Statistics, "Using the Median Absolute Deviation to Find Outliers"; Aakinshin, "DoubleMAD outlier detector". Direct relevance: tick-time distributions are heavy-tailed because of GC pauses and occasional content-mod spikes, so stddev over-weights the very outliers we are trying to flag. |
| **CUSUM** sums z-standardised residuals `S_n = max(0, S_{n-1} + (x_n - μ) / σ - k)` and fires a change-point when `S_n > h`. Online, parameter-free in the sense that `k` (slack) and `h` (threshold) are the only knobs; `k = 0.5` and `h = 4–5` are textbook defaults | giobbu/CUSUM (GitHub), Towards Data Science *Probabilistic CUSUM for change point detection*, Sarem Seitz blog. Direct relevance: the **SUSTAINED COST SHIFT** detector ("Hardmode permanently shifted Mod X's baseline") is exactly a mean-shift change-point problem. |
| **Mann-Whitney U test** is the non-parametric analogue of the two-sample t-test; valid for small samples (`n ≥ 5` per group) and ordinal / continuous data; does not assume normality or equal variances; tests stochastic dominance, not means | StatMate *When to Use T-Test vs Mann-Whitney U Test*; PMC11841899 *Optimal two-stage group sequential designs based on Mann-Whitney-Wilcoxon test*. Relevance: comparing "tick times during Blood Moon" vs "tick times outside Blood Moon" with n on the order of hundreds of ticks per bucket but heavily non-normal — Mann-Whitney is the safe default. |
| **Welch's t-test** is the right parametric fallback when variances differ but data is approximately normal; uses Satterthwaite degrees of freedom | StatMate; ResearchGate Q&A. Relevance: edge cases where bucket sizes are large enough (≥ 200 ticks) and the heavy tails have been trimmed (top/bottom 5 %) that the CLT carries the mean. |
| **Cliff's delta** = `(P(X > Y) - P(X < Y))` over all pairs (X ∈ group A, Y ∈ group B); range `[-1, +1]`; non-parametric effect size; interpreted as negligible `< 0.147`, small `[0.147, 0.33)`, medium `[0.33, 0.474)`, large `≥ 0.474` | MetricGate *Cliff's Delta Effect Size Calculator*; Romano et al. (2006). Relevance: matched to Mann-Whitney; gives the "how big is the effect" number the natural-language template fills into "Mod X is N× more expensive in Blood Moon". |
| **Cohen's d** = `(μ_A - μ_B) / s_pooled`; parametric companion to the t-test; cut-offs small 0.2, medium 0.5, large 0.8 | Standard. Used only when Welch's test was applicable. |
| **Benjamini-Hochberg FDR control**: sort p-values ascending, find the largest k with `p_(k) ≤ (k / m) * α`; reject all p-values ≤ that. Controls expected false-discovery proportion at α, less conservative than Bonferroni (`p ≤ α / m`) | MCP Analytics *Benjamini-Hochberg Procedure Explained*; arXiv 1406.7117 *A Complete Review of Controlling the FDR*; Statsig docs. Relevance: with 50 mods × 10 contexts × 5 detectors = 2500 tests per session, Bonferroni demands `p ≤ 0.00002`, which kills sensitivity. FDR at `α = 0.10` allows ~5× more discoveries while keeping the expected false-positive proportion bounded. |
| **Honeycomb BubbleUp** picks a region in a heatmap and computes, for every dimension and value, how over- or under-represented that value is in the selection vs the baseline — a contrast / lift score, not a hypothesis test | Honeycomb docs *Identify Outliers*; honeycomb.io blog *Debugging Just Got Faster*. Relevance: the **PEAK-CONTRIBUTOR-TO-SPIKE** detector is conceptually a tiny BubbleUp — pick a spike tick, ask "which mod's deviation from its own rolling baseline best explains the spike". |
| **Datadog Watchdog / anomaly monitor** decomposes a metric into trend, seasonality (hourly / daily / weekly), and residual; flags points outside a learned band on the residual | Datadog docs *Anomaly Monitor*. Relevance: we do **not** have weekly seasonality, but we *do* have intra-session non-stationarity (early-session vs late-session). Time-windowed baselines, not session-wide baselines, are the analogous mitigation. |
| **PerfView's "Investigations"** doctrine (Vance Morrison, Perf@Scale 2014): the analyst's job is grouping and folding — collapse the trace until each row is *one decision*. The tool's value is automatic grouping/folding suggestions, not the raw stack | atscaleconference.com *The keys to actionable perf investigations*; PerfView GitHub. Relevance: insights are pre-folded explanations. An insight is the answer to "if I had to look at one row, what should it say". |
| **Tableau Explain Data** uses Bayesian models — predict the mark from in-view dimensions; then add candidate explanatory dimensions one at a time and score by (variance explained − complexity penalty) | help.tableau.com *How Explain Data Works*; PRNewswire. Relevance: the ranking step (§7) borrows the *complexity-penalised improvement* idea — an insight with one variable is preferred to an insight with three at equal explanatory power. |
| **Google Lighthouse "Opportunities / Diagnostics"** split: Opportunities are *actionable* (with a "potential savings" magnitude); Diagnostics are *informational* (without). The user can rank by potential savings without sorting by raw score | developer.chrome.com *Lighthouse performance scoring*. Relevance: the actionable / informational split maps onto the player vs modder audience cleanly. A FREE-REMOVAL CANDIDATE is an Opportunity; a CONTEXT-CONDITIONAL COST is a Diagnostic. |
| **Brendan Gregg's USE Method** structures observability around Utilisation, Saturation, Errors per resource; latency heat maps surface long-tail behaviour invisible to averages | brendangregg.com *The USE Method*, *Heat Maps*. Relevance: per-mod cost is utilisation; per-mod allocation burst is saturation; per-mod ILHook failure is errors. The catalog covers all three. |
| **Template-based NLG with slot filling** is the standard, reliable approach for analytics narratives — explicit templates with named placeholders, populated from structured records. Used by Yellowfin, Tableau, Looker, Quill, Automated Insights | YellowfinBI *What Is Natural Language Generation*; deepgram.com *Natural Language Generation*. Relevance: we explicitly reject LLM-based NLG (latency, unpredictability, hallucination risk against Invariant 3). Templates with slot filling are deterministic and reviewable. |
| **`System.Diagnostics.Stopwatch.GetTimestamp()`** is the per-tick clock already in use; `Stopwatch.Frequency` is the ticks-per-second constant for unit conversion | `Profiling/MetricCollector.cs:179`. The Insights Engine reuses these; it does not introduce a new clock. |
| **`PerModAttribution.HookCount`** and **`HookDescriptor (ModId, CategoryId, DisplayName)`** are the existing hook-identity surface | `Profiling/PerModAttribution.cs:74,77`. Insights at hook granularity address `HookDescriptor.DisplayName`; no schema change required. |
| **`TickContext`** struct from `context/notes/events-tab-plan.md §3.1` carries `WeatherFlags`, `BiomeBitset`, `BossSlotArray`, `InvasionId`, `Hardmode`, `Mode`, `SubworldKey`. Allocation-free, comparable, hashable. | Sibling plan §3.1. The Insights Engine treats `TickContext` as the orthogonal-dimensions vector. |
| **`SessionLogWriter`** writes one current report and one final report per session; bumps `SchemaVersion` via `ComputeIdentity`'s `schema={SchemaVersion}` to invalidate old files | `Profiling/SessionLogWriter.cs:24,33,558+`. Insights Engine adds blocks under a new top-level `insights` field and bumps `SchemaVersion` 3 → 4. |

### Sibling-plan status

| Plan | Path | Status (verified) | What this plan assumes of it |
|---|---|---|---|
| Events tab | `context/notes/events-tab-plan.md` | **Exists.** 1003 lines. | Provides `TickContext`, `ContextTagger`, `EventAggregator`, per-bucket aggregation, transition stream. Insights consume these as inputs. |
| Spikes + allocations | `context/notes/spikes-and-allocations-plan.md` | **Exists.** | Insights consume two outputs by *shape*: (i) a `SpikeRecord` stream `(tick, frameMs, perModDeviationMs[])` and (ii) per-mod allocation deltas per tick `(tick, allocBytes[modId])`. This plan defines those shapes in §3.4 so the sibling plan has a fixed contract to honour. |
| Overview tab | `context/notes/overview-tab-plan.md` | **Exists.** | Insights and Overview are siblings — both consume the same data; Overview *ranks*, Insights *narrate*. This plan keeps the engine renderer-agnostic so an Overview that wants ranked rows reads `InsightStore.Top(...)` directly. |

### One non-evidence finding worth stating

The user reads "dynamically aggregate and design an insight" as *the engine discovers correlations from data* — not *the engine picks from a hand-written list of templates by pattern matching*. That phrasing pins the design: detectors are stateless functions over windowed data; the catalog (§4) is a list of **detector classes**, not a list of **finished sentences**. Each detector produces an `InsightRecord` with a `PatternKey`, and the renderer (§8) picks the template for that `PatternKey`. The set of templates is hardcoded; the *content* of every insight is data-driven. This is the same shape as Lighthouse's "Opportunities" (a fixed set of audit kinds, with per-page measured magnitudes) and the opposite of a chat-LLM "tell me what's wrong" approach.

---

## 1. Viability verdict

**Doable. Recommended. Five classes of insight are statistically well-supported on the data we will have; two more are speculative and explicitly badged as such; one (true counterfactuals) is deferred to a later milestone.**

The signal we have at the end of Milestones 2–3 is enough to honestly say a lot, but only if the statistical machinery is right. Three failure modes dominate if it is not:

1. **False-positive flood.** Naively running a t-test per `(mod, context, pattern)` triple at α = 0.05 across a 60-minute session produces dozens to hundreds of bogus insights. FDR correction is non-negotiable.
2. **Effect-size blindness.** A statistically significant 0.001 ms difference is true and useless. Every insight needs a hard effect-size floor *in addition to* a confidence floor.
3. **Mod-author harm.** Insights are shareable. A wrong-but-confident sentence about Mod X gets pasted in Discord and reflects badly on a stranger. Every rendered insight ships with a confidence badge and a link to the supporting evidence.

### What is well-supported

| Insight class | Why supported | Catalog entry |
|---|---|---|
| Context-conditional cost shift | Per-tick `TickContext` + per-mod CPU samples gives `(mod_ms \| context = C)` distributions directly | CONTEXT-CONDITIONAL COST, CONTEXT-CORRELATED SPIKE |
| Hot-hook dominance inside a context | `PerHookMs` array per tick + `HookDescriptor.ModId` lets us compute, per bucket, each hook's share of its owning mod's total | HOT-HOOK DOMINANCE |
| Sustained baseline shift after an event | CUSUM on per-mod ms with the event tick as a candidate change point | SUSTAINED COST SHIFT |
| Spike attribution | For each spike tick, the per-mod deviation from the per-mod rolling mean gives a ranked culprit list (BubbleUp shape) | PEAK-CONTRIBUTOR-TO-SPIKE |
| New-contributor detection | Compare per-mod mean over the first half-session to the second half — Mann-Whitney plus Cliff's delta | NEW-CONTRIBUTOR |

### What is speculative — must be explicitly badged "preliminary" or "this session"

| Insight class | Why speculative | Catalog entry |
|---|---|---|
| Free-removal candidate | Requires both cost AND engagement signal. Engagement attribution is a separate engineering surface (README "engagement" section) not yet implemented. Until engagement lands, "cost is negligible" alone is too weak. | FREE-REMOVAL CANDIDATE — *gated on engagement landing* |
| GC-pause culprit | Per-tick allocation attribution from the sibling spikes-and-allocations plan is the gating input. We can detect the pause (GcTimeMs jumps) but cannot honestly name the culprit without per-mod allocation deltas in the K ticks before. | GC-PAUSE CULPRIT — *gated on sibling plan* |

### What we explicitly do not solve

| Out of scope | Reason | Where covered |
|---|---|---|
| True counterfactual ("if you removed Mod X, FPS would be Y") | Requires a model of mod cost composition; mods are not independent (shared engine surfaces, shared NPC slots) | §13 honest gaps |
| Synergy detection ("X and Y together are worse than X+Y individually") | Requires counterfactual simulation; observational data cannot identify it without strong assumptions | §13 honest gaps |
| Source-line attribution ("Mod X is slow at MyHelper.cs:42") | MonoMod gives method-level granularity; line-level requires symbols and stack-walk we don't capture | §13 honest gaps |
| Cross-machine comparison | Per-session local data only; no shared corpus by Invariant policy (no telemetry, README "no telemetry") | §13 honest gaps |
| Modded-event correlation (events not piggybacking on vanilla flags) | Sibling plan's §13 sketches a `Mod.Call` opt-in. Until that lands, modded events are invisible. | Out of scope v1 |

### Risks the plan must address

| # | Risk | Trigger | Mitigation |
|---|---|---|---|
| **R1** | False-positive flood | Multi-comparison without correction | BH-FDR at α = 0.10 across all detectors' p-values per tick batch; per-insight effect-size floor (Cliff's delta ≥ 0.33) on top of p-value gate |
| **R2** | Compute cost blows the overhead budget | Naive O(mods × contexts × patterns × ticks) | Streaming Welford + MAD per (mod, context) pair updated in `EndTick`; full statistical tests run only at 1 Hz; expensive tests (Mann-Whitney) run only on detectors that already passed an effect-size pre-filter; end-of-session pass is the only batch O(N²)-ish work |
| **R3** | Cherry-picking bias (loudest pattern hides others) | Single detector with too much surface | Catalog covers cost, hot-hook share, allocation, change-point, frequency, hook-count tail — six orthogonal lenses (USE-method-shaped) |
| **R4** | Mod-author harm from a wrong insight | Shareable insight repeated downstream | Confidence badge on every render; "preliminary" tag for low-sample; supporting-evidence panel renders the raw counts; the renderer never says "caused by", always "correlated with" unless a mechanism is named |
| **R5** | Non-stationary baselines | Player behaviour changes within a session (early exploration → mid-session boss fight → late-session farming) | Time-windowed baselines: per-mod rolling mean over last 5 minutes is the comparison for "currently anomalous"; session-wide mean is the comparison for "over the whole session" — and only the latter goes into the end-of-session report. Detector output marks which baseline was used. |
| **R6** | Heavy-tailed tick distributions | GC pauses, occasional content-mod blowups make stddev wildly over-state spread | Robust statistics (median + MAD) for spike detection and outlier flagging; mean/stddev only used after trimmed-mean preprocessing (drop top/bottom 5 %) |
| **R7** | Autocorrelation in tick times | A 200 ms freeze persists across ~10 ticks; treating those as 10 independent observations is wrong | Down-sample to one observation per "regime" for hypothesis testing — collapse consecutive ticks with no context transition into block-medians before feeding the test; n_effective for the test is the block count, not the tick count |
| **R8** | Insight churn (same insight flickers on/off) | Detector thresholds straddled by noisy data | Hysteresis on the InsightStore: an insight needs `EnterThreshold` evidence to appear and only `LeaveThreshold < EnterThreshold` to remain; first-seen tick and last-confirmed tick tracked separately |
| **R9** | Insight spam (60 detectors firing at once on a complex session) | Top-N rendering with no cap | Hard cap of 8 live insights in the overlay; a SurfacingBudget per detector class (max 2 from any one detector type at a time); rotation if more candidates fit the score threshold |
| **R10** | Detector adds per-tick cost itself | Lite-mode overhead budget | All per-tick work is amortised Welford updates: 2 adds, 1 mul, 1 sub per (mod, context-bucket) pair. Hypothesis tests run at 1 Hz on the harvested aggregates; an end-of-session pass is the only time we touch raw ticks |
| **R11** | Player-vs-modder framing leak | A modder-facing diagnostic phrased like a player insight, or vice versa | Two render paths (`InsightRender.Short / Medium / Long` × `Audience.Player / Modder`). Renderer chooses by panel context (the overlay is `Player`, the JSONL export is `Modder` by default but contains both). |
| **R12** | LLM creep | Future contributor sees "natural-language" and reaches for an LLM | This document plus a `// SLOT FILLING ONLY — DO NOT INTRODUCE LLM` comment at the top of `InsightRenderer.cs` |

None block the design. R1, R2, R10 are the load-bearing ones; the catalog and pipeline are shaped around them.

### Honest uncertainties

- **Sample size in practice.** A short play session (30 minutes) into a fresh world produces ~108 000 ticks. That sounds like a lot but with realistic context-bucket dwell (most buckets get a few hundred to a few thousand ticks), per-mod-per-context tests will sit at n=200..2000 per group. Mann-Whitney is happy there; t-tests are happy there; effect-size estimates are noisy at the low end. The minimum-sample floor in §5 reflects this honestly.
- **GC pauses as both signal and noise.** A spike is sometimes the GC, not the mod. We can identify GC-attributable spikes (`TickFrame.GcTimeMs > threshold`) and *exclude* them from "Mod X spiked" insights; they generate their own GC-PAUSE CULPRIT line instead. This depends on the sibling plan landing.
- **The "currently active" insight set vs the "end-of-session" one.** Live insights chase the last 30 seconds; the report insights span the session. The grammar (§3) is the same, only the time window differs. This means an insight can appear live and then either confirm (also surfaces in the report) or fail to confirm (drops out, fairly). Players see the live ones in the overlay, the report ones in the session card — we should be explicit that *the live and final sets are not guaranteed to agree*.

---

## 2. Use-case shape

Every insight pattern below maps onto one of three audience queries. We design around the queries, not the patterns.

| Query | Audience | Surface | Example sentence |
|---|---|---|---|
| **"Why is this session laggy?"** | Player | Live overlay INSIGHTS tab; session card | "Calamity costs 3.4× more during Blood Moon than in normal night — averaging 8.1 ms vs 2.4 ms baseline." |
| **"What's going on right now?"** | Player | Live toast + INSIGHTS tab top row | "Just entered Underground Jungle — Spirit Mod's NPC.AI rose from 0.6 ms baseline to 4.1 ms." |
| **"Where is my mod misbehaving?"** | Modder | JSONL export; session-end markdown report; per-mod retrospective card | "Hook AccursedTrident.AI accounts for 78 % of the mod's CPU during boss fights (n = 4 fights, 14 800 ticks). Top contributing NPC types in those ticks: 35, 220, 414." |

Every other query reduces to one of these. The engine produces structured records; the renderer chooses surface and tone per audience.

---

## 3. The insight grammar

Before any detector is written, the engine has a single data shape that every detector produces. The renderer's job is the inverse — turn the record into the right sentence for the audience.

### 3.1 The `InsightRecord`

```csharp
public enum PatternKey : byte
{
    ContextCorrelatedSpike = 1,
    ContextConditionalCost = 2,
    HotHookDominance       = 3,
    AllocationBurst        = 4,
    GcPauseCulprit         = 5,
    SustainedCostShift     = 6,
    FreeRemovalCandidate   = 7,
    NewContributor         = 8,
    PeakContributorToSpike = 9,
    HookFrequencyTail      = 10, // modder-facing: "this hook fires X×/tick, p99 of which is Y ms"
}

public enum Confidence : byte { Preliminary = 0, Low = 1, Medium = 2, High = 3 }
public enum Audience  : byte { Player = 0, Modder = 1, Both = 2 }
public enum BaselineKind : byte { SessionMean = 0, RollingFiveMinute = 1, PreContext = 2, ComparableContexts = 3 }

public readonly struct SubjectRef
{
    public readonly int    ModId;        // -1 if global / cross-mod
    public readonly int    HookId;       // -1 if mod-level, not hook-level
    public readonly int    ContextKey;   // dim-encoded; -1 if not context-scoped (see EventAggregator's bucket keys)
    public readonly byte   ContextDim;   // 0=Biome 1=Weather 2=Boss 3=Invasion 4=Subworld 5=Composite

    public SubjectRef(int modId, int hookId, int contextKey, byte contextDim)
    { ModId = modId; HookId = hookId; ContextKey = contextKey; ContextDim = contextDim; }
}

public struct Magnitude
{
    public double BaselineMs;   // the comparison value (e.g. session-mean ms)
    public double ObservedMs;   // the new value (e.g. context-bucket-mean ms)
    public double RatioOrDelta; // observed / baseline OR (observed - baseline) — pattern-specific
    public long   AllocBytes;   // 0 if not allocation-related
    public int    Count;        // sample size that produced this number
}

public struct Evidence
{
    public int       SampleN;        // n for the primary group
    public int       BaselineN;      // n for the baseline group (0 if not applicable)
    public double    PValue;         // 1.0 if not tested
    public double    EffectSize;     // Cliff's delta OR Cohen's d, signed; 0 if not applicable
    public double    PValueAdjusted; // BH-FDR-adjusted; equals PValue * m_eff if not yet adjusted
    public long      FirstTickIndex; // tick where the supporting window starts
    public long      LastTickIndex;  // tick where the supporting window ends
    public BaselineKind Baseline;
}

public sealed class InsightRecord
{
    public PatternKey Pattern;
    public SubjectRef Subject;
    public Magnitude  Magnitude;
    public Evidence   Evidence;
    public Confidence Confidence;
    public Audience   Audience;

    // Filled by the InsightStore on dedup; firstSeenTick is the tick of the
    // detector firing that created this record, lastSeenTick is the latest
    // confirmation.
    public long FirstSeenTick;
    public long LastSeenTick;
    public int  ConfirmationCount;

    // Renderer cache — populated lazily, never serialised.
    public string? CachedShortPlayer;
    public string? CachedMediumPlayer;
    public string? CachedLongModder;
}
```

### 3.2 What each field means and why

| Field | Role | Why it lives on the record (not the renderer) |
|---|---|---|
| `PatternKey` | Selects the template family | Templates are version-controlled per pattern; freezing this in the record makes the JSONL export self-describing |
| `Subject` | Identifies *what* the insight is about | Lets the renderer link to the per-mod card or per-hook drill-down without re-deriving |
| `Magnitude` | Concrete numbers — baseline, observed, ratio, allocation | The renderer's slot-filling reads from here; nothing is computed at render time |
| `Evidence` | Statistics that justify the claim | The "supporting evidence" panel reads from here; the modder export dumps it verbatim |
| `Confidence` | High / Medium / Low / Preliminary | Drives the badge colour and the wording strength ("strongly suggests" vs "possibly indicates" vs "preliminary observation") |
| `Audience` | Player / Modder / Both | Some insights only make sense to one audience; HookFrequencyTail is Modder-only |
| `FirstSeenTick` / `LastSeenTick` / `ConfirmationCount` | InsightStore bookkeeping | Lets the renderer say "first observed 2 minutes ago, last confirmed 4 seconds ago, seen 18 times" |

Every detector produces `InsightRecord`s of one `PatternKey`. No detector ever invents a slot the template can't accommodate. The contract is rigid by design.

### 3.3 Renderings — three densities, two audiences

```
PatternKey.ContextCorrelatedSpike
  Audience=Player, Short:
    "Calamity spiked 8.1× during Blood Moon (peak 18.7 ms, baseline 2.3 ms)."
  Audience=Player, Medium:
    "Walking into Blood Moon raised Calamity's per-tick cost from a baseline of
     2.3 ms to a peak of 18.7 ms within 14 ticks. Observed 3× this session;
     each entry consistently triggers the rise."
  Audience=Modder, Long:
    "Pattern: CONTEXT_CORRELATED_SPIKE
     Subject: Calamity Mod (modId=3)
     Context: Weather=BloodMoon
     Baseline: per-mod rolling mean over last 5 minutes (n=18000 ticks, 2.31 ms)
     Observed: peak per-mod ms within 60 ticks of context entry (n=180 ticks, 18.7 ms)
     Effect: Cliff's delta = 0.87 (large), p=0.0001 (BH-adjusted q=0.003)
     Top contributing hooks during the observed window:
       1. Calamity.GlobalNPC.AI               68 %   12.7 ms/tick
       2. Calamity.Projectile.PostAI          18 %    3.4 ms/tick
       3. Calamity.ModSystem.PostUpdateEverything  8 %  1.5 ms/tick
     Observation count: 3 distinct entries into BloodMoon, all triggered the rise.
     Tick range of supporting window: 12340..14820 (session-relative)."
```

The templates are explicit. Every slot is named. The Modder Long rendering reads from `Magnitude` + `Evidence` + a child query into `PerHookMs` for the same window.

### 3.4 Sibling-plan contracts the renderer assumes

The Insights Engine cannot ship before the inputs it consumes exist. The contracts:

```csharp
// Provided by sibling Events plan (already drafted).
public interface IEventStream
{
    ref readonly TickContext Current { get; }
    bool HasTransitionsSince(long lastTick, out ReadOnlySpan<ContextTransition> transitions);
    IReadOnlyList<BucketStats> Buckets(byte dim);
    BucketStats BucketFor(byte dim, int key);
}

// Provided by sibling Spikes plan (not yet drafted) — this is the shape we
// commit to here.
public interface ISpikeStream
{
    // Spike record: tick + frame ms + the per-mod deviation contribution.
    // PerModDeviation[modId] = (this-tick mod ms) - (mod's rolling mean ms).
    void DrainNewSpikes(List<SpikeRecord> into);
}
public struct SpikeRecord
{
    public long   TickIndex;
    public double FrameMs;
    public double GcMs;
    public double[] PerModDeviationMs; // length == PerModAttribution.ModCount
}

// Provided by sibling Spikes plan: per-tick allocation deltas, harvested same
// rhythm as PerModAttribution.HarvestInto.
public interface IAllocationStream
{
    void HarvestInto(long[] perModAllocBytes); // length == PerModAttribution.ModCount
}
```

If `ISpikeStream` and `IAllocationStream` are not yet implemented, the Insights Engine boots with stub implementations that return empty data; detectors that need them produce zero records. No crash, degraded coverage. The JSONL export marks affected patterns as `"gatedOn": "spikes-stream"` so the agent reader sees the gating.

---

## 4. The insight catalog

Each entry is a self-contained detector. Trigger condition is in data terms; statistical test is named; minimum sample is given; worked example uses realistic numbers from a 30-minute session; template is the rendering. The order is the recommended implementation order.

### 4.1 CONTEXT-CORRELATED SPIKE

**Audience:** Player + Modder. The user's literal example.

**Trigger:** within K ticks (K=60, ≈1 second) of a context transition `C_before → C_after`, the per-mod ms exceeds `baseline + spike_threshold`. The baseline is the rolling 5-minute per-mod mean *before* the transition. Spike threshold = `3 × MAD_per_mod` (robust against GC noise).

**Test:** Mann-Whitney U on (per-mod ms during the K post-transition ticks) vs (per-mod ms during a same-length window pre-transition), with Cliff's delta as the effect size.

**Minimum sample:**
- ≥ 3 distinct transitions into the context this session (so we can show "every time you enter, it spikes")
- ≥ 60 ticks per side for the test
- Cliff's delta ≥ 0.33 (medium effect)
- q-value ≤ 0.10 after BH correction

**Worked example:** Player walks into Underground Jungle (transition tick 14200, 14210). Pre-window ticks 14140..14199 (60 ticks): Spirit Mod ms median 0.6, MAD 0.15. Post-window 14210..14269: Spirit Mod ms median 4.1, MAD 0.8. Cliff's delta ≈ 0.94, p ≈ 0.0001. Two earlier entries this session showed the same pattern.

**Template:**

```
Short Player:
  "Entering {biome} consistently spikes {mod} ({ratio:×1.0}, peak {peakMs:F1} ms)."

Medium Player:
  "Walking into {biome} raises {mod}'s tick cost from {baselineMs:F1} ms baseline
   to a peak of {peakMs:F1} ms within {kTicks} ticks. Observed {n} times this
   session; every entry triggered the spike."

Long Modder:
  "[CONTEXT_CORRELATED_SPIKE] {mod} on entering Biome={biomeFullName}
   Baseline (per-mod rolling 5-min, n={baselineN}): median {baselineMs:F2} ms,
   MAD {baselineMad:F2}.
   Observed (post-transition window, K={kTicks}, n={observedN}): median
   {observedMs:F2} ms, peak {peakMs:F2} ms.
   Cliff's delta {cliffsDelta:F2} ({effectLabel}), Mann-Whitney p={p:F4},
   BH-adjusted q={qAdj:F4}.
   Transition count this session: {transitionN}.
   Top contributing hooks during the observed window:
   {hookTable}"
```

### 4.2 CONTEXT-CONDITIONAL COST

**Audience:** Player + Modder.

**Trigger:** for a (mod, context-bucket) pair where dwellTicks ≥ 600 (10 seconds at 60 fps) and a comparison group exists (other buckets in the same dimension OR the implicit "everything else" group), the per-mod ms in the bucket differs from the comparison.

**Test:** Mann-Whitney U with block-median pre-processing (R7 mitigation) on (mod ms in bucket) vs (mod ms outside bucket). Cliff's delta as effect size.

**Minimum sample:**
- dwellTicks ≥ 600 in the bucket
- dwellTicks ≥ 600 outside the bucket (a session that never leaves Forest has no comparison — drop the test)
- Cliff's delta ≥ 0.33
- q-value ≤ 0.10

**Worked example:** Calamity in Blood Moon vs not. n_in = 2520, n_out = 86400, median_in = 8.0 ms, median_out = 2.35 ms. Cliff's delta = 0.78. q ≈ 0.0005.

**Template:**

```
Short Player:
  "{mod} costs {ratio:×F1}× more in {context} ({observedMs:F1} ms vs {baselineMs:F1} ms baseline)."

Medium Player:
  "Across {observedDwell} ticks in {context} this session, {mod} averaged
   {observedMs:F1} ms — {ratio:F1}× its baseline of {baselineMs:F1} ms in
   non-{context} time. {effectLabel} effect."

Long Modder:
  "[CONTEXT_CONDITIONAL_COST] {mod} in {contextDim}={contextFullName}
   In-bucket (n={observedN}, dwell={observedDwellTicks} ticks): median
   {observedMs:F2} ms, MAD {observedMad:F2}.
   Out-of-bucket (n={baselineN}): median {baselineMs:F2} ms, MAD {baselineMad:F2}.
   Block-medians used for the test (block size = transition-bounded segment),
   n_eff_in={effIn}, n_eff_out={effOut}.
   Cliff's delta {cliffsDelta:F2} ({effectLabel}), p={p:F4}, q={qAdj:F4}."
```

### 4.3 HOT-HOOK DOMINANCE

**Audience:** Modder primarily, Player surface as a click-through.

**Trigger:** within a (mod, context-bucket) pair where in-bucket dwellTicks ≥ 600, one hook accounts for ≥ 60 % of the mod's cumulative cost in that bucket.

**Test:** Binomial-tail test on the per-hook share, conservative — the null is "uniform across the mod's K hooks", p-value is the right-tail probability of observing the dominance under uniform. Effect size: the share itself, with cut-offs at 0.6, 0.75, 0.9.

**Minimum sample:**
- ≥ 600 ticks in the bucket
- Share ≥ 0.6
- p ≤ 0.05 (this test is loose because the alternative — uniformity — is rarely the true null; the gate is the share, not the p-value)

**Worked example:** Calamity in BossFight=Cryogen, 1800 ticks. Mod total ms = 12.1 ms/tick avg. SnowstormCallback().AI hook averages 9.5 ms/tick (78 %). Share = 0.78. Template highlights the hook name and the share, plus the call frequency (calls/sec, from per-hook accounting) and per-call mean ms.

**Template:**

```
Short Player (only if rendered live, otherwise this is Modder-only):
  "{hookDisplay} is {sharePct:P0} of {mod}'s cost during {context}."

Long Modder:
  "[HOT_HOOK_DOMINANCE] {mod} in {contextDim}={contextFullName}
   Hook: {hookDisplay} (hookId={hookId})
   Share of {mod}'s cumulative cost in bucket: {sharePct:P1}
     {hookMs:F2} ms / {modTotalMs:F2} ms.
   Bucket dwell: {dwellTicks} ticks ({dwellSeconds:F0} s).
   Calls per tick (mean): {callsPerTick:F1}, per-call mean: {perCallMs:F3} ms.
   Binomial-tail p={p:F4} against uniform-share null."
```

### 4.4 ALLOCATION BURST

**Audience:** Modder primarily; Player surface as a click-through from a GC-pause toast.

**Trigger:** *(gated on sibling spikes-and-allocations plan)*. Within a (mod, context-bucket) pair, the per-mod allocation bytes per tick exceeds `baseline_alloc + 3 × MAD_alloc`, sustained over ≥ 30 ticks.

**Test:** Mann-Whitney on per-mod allocBytes (in-bucket) vs (out-of-bucket). Effect: ratio of medians (`obs/base`), interpretive cut-offs 2×, 5×, 10×.

**Minimum sample:**
- ≥ 60 ticks in the burst window with `allocBytes > 0`
- Ratio ≥ 2.0
- q-value ≤ 0.10

**Worked example:** During King Slime fight, Fargo's Souls allocates 280 KB/tick (median) over 1200 ticks vs 18 KB/tick baseline. Ratio = 15.6×.

**Template:**

```
Short Player (rare; usually under a click-through):
  "{mod} allocates {ratio:×F0}× more during {context} ({observedBytes:K0} KB/tick)."

Long Modder:
  "[ALLOCATION_BURST] {mod} in {contextDim}={contextFullName}
   In-bucket median allocations: {observedKb:F1} KB/tick
   Out-of-bucket median:          {baselineKb:F1} KB/tick
   Ratio: {ratio:×F1}, sustained over {sustainTicks} ticks.
   Hooks most likely responsible (by call frequency × per-call alloc):
   {hookTable}"
```

### 4.5 GC-PAUSE CULPRIT

**Audience:** Modder; Player surface as a brief explanation under a GC-spike toast.

**Trigger:** *(gated on sibling allocations stream)*. For each tick where `TickFrame.GcTimeMs > 5 ms`, look at the K=60 ticks preceding. Rank mods by `Σ allocBytes / Σ totalAllocBytes` in that window; surface the top 3.

**Test:** No formal test; this is *attribution* under the model "the mod that allocated most is the most likely culprit". The renderer says "preceded by" not "caused by". Confidence is downgraded to Low automatically.

**Minimum sample:**
- ≥ 3 GC pauses in the session with this same top-mod set
- Top mod's share ≥ 30 % of allocs in the window

**Worked example:** 7 GC pauses this session (mean 8.4 ms). In the 60-tick windows before each, Calamity averaged 62 % of allocations.

**Template:**

```
Short Player (under a toast):
  "Most GC pauses this session were preceded by Calamity allocating heavily ({sharePct:P0} of allocs)."

Long Modder:
  "[GC_PAUSE_CULPRIT] preliminary
   GC pause count: {pauseN} (mean {pauseMs:F1} ms each)
   In the 60 ticks preceding each pause, allocation share was:
   {modTable}
   No causal claim. Confidence Low because allocation source != GC trigger in
   general; report compounds when {topMod} also dominates ALLOCATION_BURST
   in the same buckets."
```

### 4.6 SUSTAINED COST SHIFT

**Audience:** Player + Modder.

**Trigger:** Online CUSUM on per-mod ms detects a positive change point at tick T; the post-T mean exceeds the pre-T mean by a Cohen's d ≥ 0.5 (or equivalent on the trimmed-mean path); the shift persists ≥ 600 ticks after T.

**Test:** CUSUM with `k = 0.5 σ` (sensitivity) and `h = 4 σ` (alarm). When alarm fires at T, compare (pre-window: last 5 min) vs (post-window: next 5 min or session end) with Welch's t-test (trimmed) and Cohen's d.

**Minimum sample:**
- ≥ 300 ticks pre-T and ≥ 300 ticks post-T
- Cohen's d ≥ 0.5
- q-value ≤ 0.10

**Worked example:** Entering Hardmode at tick 32400. Mod Y's pre-mean ms = 1.4, σ = 0.3. Post-mean ms = 2.8, σ = 0.4. d = 3.9 (huge). CUSUM alarms within 200 ticks of the transition.

**Template:**

```
Short Player:
  "{mod}'s baseline cost rose from {preMs:F1} ms to {postMs:F1} ms after {triggerEvent}."

Medium Player:
  "Around the time of {triggerEvent}, {mod}'s typical per-tick cost shifted
   permanently — from {preMs:F1} ms baseline ({preDuration}) to {postMs:F1} ms
   ({postDuration}). The shift has held since."

Long Modder:
  "[SUSTAINED_COST_SHIFT] {mod}
   Change point detected at tick {changeTick} (session-relative).
   Nearest context transition within ±60 ticks: {triggerEvent} (offset {offsetTicks}).
   Pre-window ({preDurationTicks} ticks): mean {preMs:F2} ms, σ {preStd:F2}.
   Post-window ({postDurationTicks} ticks): mean {postMs:F2} ms, σ {postStd:F2}.
   Welch's t-test (5%-trimmed): t={t:F2}, p={p:F4}, q={qAdj:F4}.
   Cohen's d = {cohensD:F2} ({effectLabel})."
```

### 4.7 NEW-CONTRIBUTOR

**Audience:** Player + Modder. "This mod just got expensive recently."

**Trigger:** Split the session in half (or sliding two-window split): a mod whose late-half mean exceeds early-half mean with Cliff's delta ≥ 0.33 AND no SUSTAINED_COST_SHIFT was already flagged at the boundary.

**Test:** Mann-Whitney on (early-half ms) vs (late-half ms), same block-median preprocessing.

**Minimum sample:**
- Session length ≥ 10 minutes
- ≥ 300 ticks per half
- Cliff's delta ≥ 0.33
- q-value ≤ 0.10

**Worked example:** Mod Z idle for the first half (avg 0.2 ms), suddenly 1.5 ms in the second half. No obvious context transition responsible. Surfaces because the player has installed Mod Z but rarely uses its content; once they did, it shows up.

**Template:**

```
Short Player:
  "{mod} became {ratio:×F1}× more expensive in the second half of the session."

Long Modder:
  "[NEW_CONTRIBUTOR] {mod}
   Session split at tick {splitTick}.
   Early half (n={earlyN}): median {earlyMs:F2} ms, MAD {earlyMad:F2}.
   Late half  (n={lateN}): median {lateMs:F2} ms,  MAD {lateMad:F2}.
   Cliff's delta {cliffsDelta:F2} ({effectLabel}), p={p:F4}, q={qAdj:F4}.
   No SUSTAINED_COST_SHIFT was flagged near the boundary; the rise is gradual
   rather than abrupt. Suggested investigation: did the player begin using
   {mod}'s content somewhere mid-session (boss fight, new biome explored,
   new accessory equipped)?"
```

### 4.8 PEAK-CONTRIBUTOR-TO-SPIKE

**Audience:** Player + Modder. Per-spike attribution.

**Trigger:** for every spike (a tick where `FrameMs > rolling_median + 3 × rolling_MAD`), the engine ranks the per-mod deviations from each mod's own rolling mean. The mod with the largest share is named.

**Test:** No formal hypothesis test here — this is BubbleUp-shaped attribution. The "p-value" surrogate is the share itself: top mod's share ≥ 60 % is High confidence; 40–60 % is Medium; below 40 % is Low (or "no dominant culprit, multi-mod spike").

**Minimum sample:**
- 1 spike is enough to emit a record, but `ConfirmationCount` builds across repeated spikes
- Top-mod share ≥ 40 % (below this, emit a "no dominant culprit" insight)

**Worked example:** Spike of 87 ms at tick 21340. Calamity deviation: +52 ms (60 %). Fargo's Souls: +18 ms (21 %). Other mods: 17 % combined. Top hook in Calamity during that tick: SnowstormCallback at 41 ms.

**Template:**

```
Short Player:
  "The {peakMs:F0} ms spike at {timestamp} was {sharePct:P0} {mod} ({hookDisplay})."

Long Modder:
  "[PEAK_CONTRIBUTOR_TO_SPIKE] spike at tick {spikeTick} (frame {frameMs:F1} ms)
   Top contributors by per-mod deviation:
     1. {mod1} +{dev1Ms:F1} ms ({share1:P0})
     2. {mod2} +{dev2Ms:F1} ms ({share2:P0})
     3. {mod3} +{dev3Ms:F1} ms ({share3:P0})
   Top hooks in {mod1} during this tick:
     1. {hook1Display}  {hook1Ms:F1} ms
     2. {hook2Display}  {hook2Ms:F1} ms
   Concurrent context: {contextSummary}."
```

### 4.9 FREE-REMOVAL CANDIDATE *(gated on engagement signal)*

**Audience:** Player primarily.

**Trigger:** mod cost ≤ ε (default 0.1 ms/tick avg) over a session ≥ 30 minutes AND engagement signal is zero (no items used, no NPCs killed credited to this mod, no biome of this mod entered, etc).

**Test:** No statistical test required — both conditions are deterministic over the session.

**Minimum sample:** Session ≥ 30 minutes; otherwise the absence of engagement is uninformative.

**Worked example:** Mod W's avg cost = 0.04 ms/tick, total session cost 0.7 % of frame time. Engagement: 0 items used, 0 mod NPCs killed, no Mod-W biome touched. Render hedged: "Mod W contributed 0.7 % of frame time and you didn't interact with its content *this session*. Consider whether it's pulling its weight for your playstyle."

**Template:**

```
Short Player:
  "{mod} cost {totalPct:P1} of frame time this session and you didn't use its content."

Medium Player:
  "Over {sessionDuration}, {mod} contributed {totalPct:P1} of total frame time
   (avg {avgMs:F2} ms/tick) and showed zero engagement signal: no items used,
   no mod NPCs killed, no mod biome entered. This is a 'this session' window —
   it may differ across playthroughs."

(Modder rendering: not produced — this is a player-facing insight only.)
```

Until engagement signal is wired, this detector is **disabled** in code (not commented out — a `if (!EngagementSignal.IsAvailable) return;` guard). Easier to enable later than to debug a half-fed detector.

### 4.10 HOOK-FREQUENCY TAIL

**Audience:** Modder only.

**Trigger:** for a hook with mean calls/tick ≥ 50, the 99th percentile of per-call ms is ≥ 5× the median per-call ms.

**Test:** No formal test; this is a tail-shape descriptor. We log it because it's the kind of thing a mod author actively wants to know — "most of the time this hook is fine, but the tail is fat".

**Minimum sample:**
- ≥ 5000 calls observed for the hook this session

**Worked example:** ProjectileLoader hook of Mod V. Mean 1200 calls/tick. Median per-call = 0.001 ms. p99 per-call = 0.012 ms. Ratio = 12×.

**Template:**

```
Modder only (no player rendering):
  "[HOOK_FREQUENCY_TAIL] {mod}.{hookDisplay}
   Calls per tick (mean): {callsPerTick:F0}
   Per-call ms: median {medianMs:F4}, p95 {p95Ms:F4}, p99 {p99Ms:F4}
   Tail ratio (p99/median): {tailRatio:F1}×.
   Most calls are negligible; the tail is wide. Likely worth profiling the
   per-call path for input shapes that hit the slow case."
```

### 4.11 Cross-detector composition

Some insights are interesting because they co-fire. The engine does not invent new templates from compositions; it leaves cross-references as data on the record:

```csharp
public List<long>? RelatedInsightIds;  // ids of other live insights involving the same Subject
```

The renderer can choose to add a "see also" footer to the medium/long forms if `RelatedInsightIds` is non-empty. Concrete example: CONTEXT_CONDITIONAL_COST + HOT_HOOK_DOMINANCE on the same (mod, context) pair tells the player both "Mod X is expensive in Blood Moon" and "the hook responsible is GlobalNPC.AI". The user clicks the first; the second appears beneath as supporting structure.

---

## 5. Statistical foundations

The statistics are the centre of gravity of this plan. Get them wrong and the engine generates plausible falsehoods at scale.

### 5.1 Online streaming primitives

Three pieces are updated every tick per `(mod × context-bucket)` pair:

#### 5.1.1 Welford for mean / variance

```csharp
public struct Welford
{
    public long N;
    public double Mean;
    public double M2;

    public void Add(double x)
    {
        N++;
        double delta = x - Mean;
        Mean += delta / N;
        double delta2 = x - Mean;
        M2 += delta * delta2;
    }

    public double Variance      => N > 1 ? M2 / (N - 1) : 0d;
    public double StandardDev   => System.Math.Sqrt(Variance);
}
```

Allocation-free, O(1) per add, numerically stable. Used for everything that needs a mean and a variance. https://en.wikipedia.org/wiki/Algorithms_for_calculating_variance

#### 5.1.2 Streaming median + MAD via P² algorithm or a fixed-window sketch

Exact streaming median is O(log N) with a two-heap structure but allocates on insert. For our case (per-tick updates of dozens of (mod × context) pairs), we use the **P² quantile estimator** — five marker positions, O(1) per add, no allocation; approximates any single quantile (p50 for median, p99 for tail-percentile). For MAD we run a second P² over `|x - p50_estimate|`. Error is bounded; the literature gives ~1 % relative error after ~1000 samples.

The alternative — keep a fixed-size reservoir of K=2000 recent samples and recompute the median on demand — is simpler but allocates the reservoir per (mod × context). 2000 × 8 bytes × (200 buckets) = 3.2 MB. Acceptable but the P² version is cheaper at steady state. **Decision**: ship P² in Lite; offer the reservoir as Standard for the few buckets the player actively drills into.

#### 5.1.3 CUSUM for change-point

```csharp
public struct Cusum
{
    public double S;            // accumulated z-deviation
    public double K;            // slack (typical 0.5)
    public double H;            // alarm threshold (typical 4.0)
    public long   AlarmTick;    // -1 if no alarm; else tick at which threshold was crossed

    public void Update(double x, double mean, double stddev, long tick)
    {
        if (stddev <= 1e-9) { S = 0; return; }
        double z = (x - mean) / stddev;
        S = System.Math.Max(0d, S + z - K);
        if (S > H && AlarmTick < 0) AlarmTick = tick;
    }
    public void Reset() { S = 0; AlarmTick = -1; }
}
```

When `AlarmTick` flips from `-1` to a real tick, the SUSTAINED-COST-SHIFT detector kicks in: it pauses CUSUM, waits another 600 ticks of data, then runs the post-window vs pre-window Welch's test. If the test fails (no real shift), CUSUM is reset; if it passes, the insight emits and CUSUM stays reset for the rest of the session for this mod.

### 5.2 Hypothesis testing — which test, when

| Situation | Test | Why |
|---|---|---|
| Two groups, small (n < 30), non-normal or unknown distribution | Mann-Whitney U | No normality assumption; rank-based; robust to outliers |
| Two groups, large (n ≥ 200), heavy-tailed (typical tick distributions) | Mann-Whitney U on block-medians | The CLT would carry the mean, but block-median preprocessing addresses autocorrelation more honestly than n_effective adjustment |
| Two groups, large, approximately normal after 5 %-trimming | Welch's t-test on trimmed data | Stronger when the distribution genuinely is light-tailed (rare here) |
| One group's distribution vs uniform | Binomial-tail | HOT_HOOK_DOMINANCE — share against uniform null |

Default is **Mann-Whitney with block-median preprocessing**. Welch's t-test on trimmed data is a fallback when n is huge and the trim looks well-behaved.

### 5.3 Effect-size floors

A p-value alone is meaningless at large n — a 0.001 ms difference becomes "significant" with enough samples. Effect-size cut-offs are the actionability gate.

| Effect size | Floor for surfacing | Source |
|---|---|---|
| Cliff's delta | ≥ 0.33 (medium) | Romano et al. 2006 |
| Cohen's d | ≥ 0.5 (medium) | Cohen 1988 |
| Share-of-cost (HOT_HOOK_DOMINANCE) | ≥ 0.60 | engineering judgement |
| Ratio of medians (CONTEXT_CONDITIONAL_COST, ALLOCATION_BURST) | ≥ 2.0× | engineering judgement |
| Per-mod-deviation share (PEAK_CONTRIBUTOR_TO_SPIKE) | ≥ 0.40 (else "no dominant culprit") | engineering judgement |

Insights below the floor are suppressed even if the p-value is microscopic. This is the gate that stops "Mod Y is 1.001× more expensive in Jungle, p=1e-200".

### 5.4 Multiple-comparison correction

Every detector pass produces a batch of (test → p-value) pairs. Before any insight is surfaced, the batch is BH-corrected at α = 0.10:

```
1. Collect all p-values from the pass: P = [p_1, p_2, ..., p_m]
2. Sort ascending: p_(1) ≤ p_(2) ≤ ... ≤ p_(m)
3. Find the largest k such that p_(k) ≤ (k / m) * α
4. Reject (i.e. flag as significant) all hypotheses with p ≤ p_(k)
5. Adjusted q-value for hypothesis i: min over j ≥ i of (p_(j) * m / j)
6. The insight carries the adjusted q in Evidence.PValueAdjusted
```

This is the standard step-up procedure. Implementation is one sort and one pass. https://en.wikipedia.org/wiki/False_discovery_rate

α = 0.10 instead of 0.05 because (a) the consequence of a false positive is "show a softly-worded insight" not "publish a paper" and (b) the effect-size floor is already filtering aggressively. The product of `q ≤ 0.10` and `delta ≥ 0.33` is sharply selective.

### 5.5 Baselines — what we compare to

Every detector says explicitly what its baseline is.

| Baseline | Used by | Why |
|---|---|---|
| `RollingFiveMinute` (last 18000 ticks of the *same* mod) | CONTEXT_CORRELATED_SPIKE | "Is this mod doing something different from what it was doing a minute ago" |
| `PreContext` (last K ticks before the transition) | CONTEXT_CORRELATED_SPIKE secondary | "Specifically, did the transition cause it" |
| `SessionMean` (session-wide mean of the same mod) | CONTEXT_CONDITIONAL_COST end-of-session | "Across the whole session, is this context more expensive than average" |
| `ComparableContexts` (other buckets in the same dimension) | CONTEXT_CONDITIONAL_COST live | "Is Jungle worse than other biomes, not just worse than the session mean" |
| `SessionFirstHalf` | NEW_CONTRIBUTOR | "Is the mod doing more now than at the start of the session" |

The choice is **per-detector and named in the record**. The renderer's medium/long forms include the baseline name. This is the honesty contract operationalised: the user can always see what the comparison is.

### 5.6 Sample-size floors — concrete table

| Detector | Min n per group | Min observations | Min duration |
|---|---|---|---|
| CONTEXT_CORRELATED_SPIKE | 60 ticks | 3 transitions | — |
| CONTEXT_CONDITIONAL_COST | 600 ticks in-bucket, 600 out | — | 20 s in-bucket dwell |
| HOT_HOOK_DOMINANCE | 600 ticks in-bucket | — | 20 s in-bucket dwell |
| ALLOCATION_BURST | 60 ticks sustained | — | 1 s sustain |
| GC_PAUSE_CULPRIT | 60 ticks pre-pause window | 3 GC pauses ≥ 5 ms | — |
| SUSTAINED_COST_SHIFT | 300 pre, 300 post | CUSUM alarm | 10 min session |
| NEW_CONTRIBUTOR | 300 per half | — | 10 min session |
| PEAK_CONTRIBUTOR_TO_SPIKE | — | 1 spike (more = higher confirmation) | — |
| HOOK_FREQUENCY_TAIL | — | 5000 calls | — |
| FREE_REMOVAL_CANDIDATE | — | session-wide engagement signal | 30 min session |

Below the floor, the detector silently does nothing. No record, no JSON entry. The agent reader sees "no insight from this detector" rather than "insight with a `note: low confidence`".

### 5.7 Why simple thresholds aren't enough — three failure modes the math addresses

1. **Heavy-tailed distributions inflate variance.** A single 200 ms GC pause inside a 5-minute window of mostly 1 ms ticks pushes the stddev to 4–5 ms; a 3σ rule then ignores anything below 12–15 ms and lets the next spike of similar magnitude pass as "normal". MAD with k=3 catches the real spike because the median didn't move. (See https://aakinshin.net/posts/harrell-davis-double-mad-outlier-detector/ for the long version.)
2. **Autocorrelation overstates n.** A 200 ms freeze spans ~10 ticks. Treating them as 10 independent samples claims n=10 worth of evidence when there is n=1 of evidence ("one freeze happened"). Block-median preprocessing — collapse each context-stable segment to its median — gives the test the right n. The block size is the number of segments between context transitions, not the tick count.
3. **Non-stationarity invalidates session-wide baselines mid-session.** "Mod X is 3× more expensive than baseline" computed against the first 5 minutes of the session is meaningless 90 minutes later if the player has entered Hardmode in between. Rolling 5-minute baseline for live insights; SessionMean only used at end-of-session when the session is bounded.

---

## 6. Insight pipeline architecture

### 6.1 Components and ownership

```
┌───────────────────────────────────────────────────────────────────────────┐
│  Insights Engine                                                          │
│                                                                           │
│   per tick (60 Hz):                                                       │
│     ┌───────────────────────┐    ┌───────────────────────────┐            │
│     │ StreamingStats        │◀───│ MetricCollector.EndTick   │            │
│     │ (Welford + P² + CUSUM)│    │  ⤷ harvested per-mod ms    │            │
│     └──────────┬────────────┘    │  ⤷ per-hook ms             │            │
│                │                 │  ⤷ TickContext             │            │
│                │                 │  ⤷ FrameMs, GcMs            │            │
│                │                 └───────────────────────────┘            │
│                ▼                                                          │
│   at 1 Hz / on-trigger:                                                   │
│     ┌────────────────────────────────────────────────────────────┐        │
│     │ DetectorScheduler                                          │        │
│     │   ⤷ per-tick:        no detectors                           │        │
│     │   ⤷ per second:       CONTEXT_CONDITIONAL_COST,            │        │
│     │                       HOT_HOOK_DOMINANCE,                  │        │
│     │                       SUSTAINED_COST_SHIFT (CUSUM polled), │        │
│     │                       NEW_CONTRIBUTOR (every 30 s)         │        │
│     │   ⤷ on transition:    CONTEXT_CORRELATED_SPIKE             │        │
│     │   ⤷ on spike:         PEAK_CONTRIBUTOR_TO_SPIKE            │        │
│     │   ⤷ on alloc burst:   ALLOCATION_BURST                     │        │
│     │   ⤷ on GC pause:      GC_PAUSE_CULPRIT                     │        │
│     └───────────────────────┬────────────────────────────────────┘        │
│                             ▼                                             │
│     ┌────────────────────────────────────────────────────────────┐        │
│     │ BH-FDR correction batch (per-pass)                         │        │
│     └───────────────────────┬────────────────────────────────────┘        │
│                             ▼                                             │
│     ┌────────────────────────────────────────────────────────────┐        │
│     │ InsightStore   (live, dedup, TTL, hysteresis)              │        │
│     │   ⤷ per-pattern surfacing budget                            │        │
│     │   ⤷ first-seen / last-seen / confirmation-count             │        │
│     └─────┬───────────────────────────────────┬──────────────────┘        │
│           ▼                                   ▼                           │
│   ┌────────────────────────┐    ┌──────────────────────────────┐          │
│   │ InsightRenderer        │    │ InsightExporter (JSONL)      │          │
│   │ (templates, slot-fill) │    │ (active, history, final)     │          │
│   └────────────────────────┘    └──────────────────────────────┘          │
│           ▲                                                               │
│           │                                                               │
│   ┌───────┴────────────────────────────────────────────────────┐          │
│   │ End-of-session pass: full catalog over the bounded session │          │
│   │ window. Output → InsightStore (terminal state)             │          │
│   └────────────────────────────────────────────────────────────┘          │
└───────────────────────────────────────────────────────────────────────────┘
```

### 6.2 Cadence — what runs per-tick, what runs less

| Work | Cadence | Justification |
|---|---|---|
| `StreamingStats.Update(per-mod ms, per-mod alloc, TickContext)` | per tick (60 Hz) | These are the streaming primitives; the whole engine depends on them being up to date. 6 floats updated per (mod × context bucket); typical session has ≤ 200 active pairs. ~1 µs/tick total. |
| `DetectorScheduler.Tick(now)` | per tick | Just a dispatcher; checks the per-detector cadence and on-trigger flags. ~50 ns/tick. |
| CONTEXT_CONDITIONAL_COST, HOT_HOOK_DOMINANCE detectors | 1 Hz | These read aggregated stats, not raw ticks. Cost per pass: O(mods × active-buckets) Mann-Whitney tests ≈ a few hundred operations. ~200 µs spread over 60 ticks → 3 µs/tick amortised. |
| SUSTAINED_COST_SHIFT (CUSUM poll) | per tick (cheap; just check the AlarmTick flag) + run the Welch's test once when the alarm fires | The alarm is an O(1) check; the test fires at most once per (mod × session). |
| NEW_CONTRIBUTOR | every 30 s | Split-session test; needs ≥10 min of data anyway. |
| CONTEXT_CORRELATED_SPIKE | on context transition (drained from `IEventStream.HasTransitionsSince`) | Up to a few transitions per minute typically. |
| PEAK_CONTRIBUTOR_TO_SPIKE | on spike (from `ISpikeStream.DrainNewSpikes`) | Cheap — just a sort of the per-mod deviation array. |
| ALLOCATION_BURST | 1 Hz, but skipped if no alloc-stream input | Same shape as CONTEXT_CONDITIONAL_COST but on allocBytes. |
| GC_PAUSE_CULPRIT | on GC pause | Requires the K=60 pre-pause alloc snapshots; alloc-stream maintains a rolling buffer. |
| HOOK_FREQUENCY_TAIL | end-of-session only | Tail-shape is most meaningful over the full bounded sample. |
| FREE_REMOVAL_CANDIDATE | end-of-session only | Requires the session to be bounded to evaluate "did the player not use this". |
| Full catalog re-run (end-of-session batch) | once on world-unload | This is where the final insight set is computed against the full session window. |

Per-tick amortised total: < 10 µs. That stays inside Lite's < 1 % budget. The expensive work (Mann-Whitney on n=600+ samples) runs at 1 Hz against the streaming summaries, not the raw samples — *Welford's mean and variance are sufficient statistics* for the comparisons we care about, and the rank tests run on a small block-median collapse rather than the full tick stream.

### 6.3 Detector contract

Every detector is a stateless function `Evaluate(WindowData, StreamingStats) -> InsightRecord?`. No detector mutates state outside its own optional history (e.g. PEAK_CONTRIBUTOR_TO_SPIKE keeps a counter of how often the same `(spikeContext, mod)` pair fires). Detectors are unit-testable against synthetic input — pure logic, no tModLoader dependency.

```csharp
public interface IInsightDetector
{
    PatternKey Pattern { get; }
    Audience   DefaultAudience { get; }
    DetectorCadence Cadence { get; }    // PerTick / OnceASecond / OnTransition / OnSpike / EndOfSession

    void Evaluate(in DetectorContext ctx, List<InsightRecord> emit);
}

public readonly struct DetectorContext
{
    public readonly long NowTick;
    public readonly StreamingStatsView Stats;
    public readonly IEventStream Events;
    public readonly ISpikeStream Spikes;
    public readonly IAllocationStream Allocs;
    public readonly TransitionBatch RecentTransitions;
    public readonly SpikeBatch RecentSpikes;
    public readonly PerModAttributionView Attribution;  // hook-level read-only access
    public readonly long SessionLengthTicks;
}
```

### 6.4 The InsightStore — live, deduplicated, TTL-tracked

```csharp
public sealed class InsightStore
{
    private readonly Dictionary<long, InsightRecord> _live; // key = stable-id derived from (Pattern, Subject)
    private readonly List<long> _liveOrder;                 // insertion order, used for FIFO eviction
    private readonly Dictionary<PatternKey, int> _surfacingBudget;

    private const int LiveCap = 32;             // total live insights kept; surface only top-8 in UI
    private const int PerPatternCap = 4;        // no more than 4 of one kind at once
    private const int TtlTicks = 60 * 60 * 5;   // 5 minutes — re-confirmation required after this

    public void Submit(InsightRecord rec, long nowTick) { ... }
    public IReadOnlyList<InsightRecord> Top(int n, long nowTick) { ... }   // ranked, see §7
    public IReadOnlyList<InsightRecord> AllLive(long nowTick) { ... }
    public IReadOnlyList<InsightRecord> EndOfSession() { ... }             // populated only at world-unload
}
```

Stable id is `Hash(Pattern, Subject.ModId, Subject.HookId, Subject.ContextKey, Subject.ContextDim)` — same insight resurfacing updates the same record rather than creating a new one.

Hysteresis:

- A detector firing once → `Confidence = Preliminary`, `ConfirmationCount = 1`, surfaced but badged.
- Detector firing a second time within `TtlTicks` for the same Subject → `Confidence = Low`, `ConfirmationCount = 2`.
- Third → `Confidence = Medium`. Fourth+ with q ≤ 0.05 → `Confidence = High`.
- No detector firing for `TtlTicks` → record expires from live but is retained in `_history` for the session-end pass.

### 6.5 End-of-session pass

When `Mod.Unload` (or `OnWorldUnload`) fires, the engine:

1. Drains all per-pattern detectors with `Cadence == EndOfSession` over the full session window.
2. Re-runs all live detectors *once* against the full session (not the rolling window) so the final report uses the strongest possible sample size.
3. BH-FDR-corrects across the full final batch.
4. Writes the result to `InsightStore.EndOfSession()` for the JSONL `insights_final[]` field and the markdown report.

This is the only place we do the full-session-window comparisons. The cost is bounded — single pass, runs once.

---

## 7. Ranking and surfacing

A 60-minute session can produce 30–50 records that pass the floors. The overlay surfaces 8. Ranking decides which 8.

### 7.1 Scoring formula

```
score(rec) =  w_mag * normalise(magnitude)                  // larger effect wins
           +  w_conf * confidence_weight(rec.Confidence)    // High > Medium > Low > Preliminary
           +  w_rec * recency_weight(now - rec.LastSeenTick) // fresher wins
           +  w_act * actionability(rec.Pattern)             // clear culprit > vague correlation
           +  w_nov * novelty(rec.ConfirmationCount)         // new beats already-shown-10-times
           +  w_aud * audience_match(rec.Audience, surface)  // overlay = Player; export = Modder
```

Weights (initial; tunable):

| Coefficient | Default | Rationale |
|---|---|---|
| `w_mag` | 0.30 | Magnitude is the why-the-user-cares signal |
| `w_conf` | 0.25 | Confidence is the why-trust-it signal |
| `w_rec` | 0.15 | Recency keeps live overlay fresh |
| `w_act` | 0.15 | Actionable insights ranked above descriptive ones |
| `w_nov` | 0.10 | Penalise re-showing the same insight forever |
| `w_aud` | 0.05 | Light steering by surface |

`actionability(pattern)`:
- PEAK_CONTRIBUTOR_TO_SPIKE, HOT_HOOK_DOMINANCE, FREE_REMOVAL_CANDIDATE → 1.0 (clear "do X")
- CONTEXT_CORRELATED_SPIKE, ALLOCATION_BURST → 0.8 (clear what / when, less clear what to do)
- CONTEXT_CONDITIONAL_COST, NEW_CONTRIBUTOR → 0.6 (descriptive)
- SUSTAINED_COST_SHIFT, GC_PAUSE_CULPRIT → 0.5
- HOOK_FREQUENCY_TAIL → 0.4 (informational only)

`novelty(confirmationCount)` = `1 / (1 + log2(1 + confirmationCount))`. First appearance = 1.0; after 7 confirmations ≈ 0.33.

`recency_weight(age_ticks)` = `max(0.2, 1 - age_ticks / TtlTicks)`. Linear decay to a floor of 0.2 at TTL; expires from live entirely after TTL.

### 7.2 Surfacing budget and rotation

- **Hard cap:** 8 live insights surfaced in the overlay at any one time.
- **Per-pattern cap:** at most 2 of any one PatternKey at once. A user with three CONTEXT_CONDITIONAL_COST insights firing should see the top 2 of that kind plus the top-other-pattern, not a wall of context-cost rows.
- **Rotation:** every 30 seconds, recompute the score and re-rank. Insights below the top-8 cutoff swap in if scores are within 5 % of the current bottom. Hard hysteresis on the cutoff prevents thrash.

### 7.3 End-of-session full catalog

The end-of-session report has **no cap**. Every insight that passed its floors over the full session window is included. Markdown export groups by audience (Player section first, Modder section second), within each by pattern, within each by score descending.

---

## 8. Natural-language generation

### 8.1 Template-based, slot-filling, deterministic. No LLM.

Every rendering comes from a registered template indexed by `(PatternKey, Audience, Density)`. The template is plain interpolation with named slots; the renderer fills slots from the `InsightRecord`. Failure mode = compile-time mismatch between template slot names and record fields, caught by a unit test (`InsightTemplateTests.AllTemplatesRenderForEveryPattern()`).

```csharp
public static class InsightRenderer
{
    public static string Render(InsightRecord rec, Audience audience, Density density)
    {
        TemplateKey key = new TemplateKey(rec.Pattern, audience, density);
        if (!_templates.TryGetValue(key, out InsightTemplate? t))
        {
            // Fall back: Player + Short is always defined. Modder + Long is always defined.
            t = _templates[FallbackKey(rec.Pattern, audience)];
        }
        return t.Render(rec);   // string.Create with span-buffer; no allocation beyond the final string
    }
}
```

The template DSL is the same `{name:format}` shape as `string.Format` — boring on purpose. C# 10's interpolated string handlers give us span-buffer rendering with zero intermediate allocations.

### 8.2 Number formatting — consistent across the engine

| Quantity | Format | Example |
|---|---|---|
| Time in ms | 2 sig figs, `F1` for ≥1 ms, `F2` for <1 ms, `F3` for <0.1 ms | `8.1 ms`, `0.42 ms`, `0.012 ms` |
| Ratio (multiplier) | `×F1` if ≥2, `×F2` if <2 | `8.1×`, `1.34×` |
| Percentage | `P0` if ≥10 %, `P1` otherwise | `78 %`, `2.4 %` |
| Bytes | `K0` (KB), `M1` (MB), `G2` (GB) | `420 KB`, `12.7 MB` |
| Calls per tick | `F0` if ≥10, `F1` otherwise | `1200`, `4.7` |
| Tick index | thousands separator | `12,400` |
| Confidence | label | "High", "Medium", "Low", "preliminary" |
| Effect size | label only in short, value+label in long | "large" / "Cliff's delta 0.78 (large)" |

Formatters are gathered in `InsightFormatting.cs` as extension methods so templates can do `{baselineMs:Ms}` and `{ratio:Mult}` rather than each template re-deriving the format string. Less risk of one template formatting "8.1 ms" while another shows "8.0998000".

### 8.3 Tone and Invariant 3 compliance

Every rendered string is run through a "tone sanity" review at template-add time, but the engine also enforces structural rules:

| Rule | Violation example | Compliant example |
|---|---|---|
| No "caused by", "must", "should remove" | "Calamity caused the spike" | "Calamity was the largest contributor (60 %)" |
| Always cite the measurement | "Mod X is heavy in Blood Moon" | "Mod X averaged 8.1 ms in Blood Moon vs 2.4 ms baseline" |
| Always badge the time window | "This mod is unused" | "You haven't used this mod's content **this session**." |
| No mod ranking by removal value | "Top removal candidates: X, Y, Z" | "Mods that cost frame time with zero engagement signal this session: X (1.2 %), Y (0.9 %)..." |
| Hedge correlations | "Blood Moon makes Mod X slow" | "Mod X is observed to cost more during Blood Moon (3.4× baseline)" |

A simple regex check at template-registration time enforces the banned-vocab list ("caused by", "must remove", "core mod", "removable", "bad mod"). Unit test `InsightTemplateTests.NoBannedVocabAcrossTemplates()`.

### 8.4 Comparison framing — canonical per pattern

Each insight's medium and long forms always declare what the comparison is. Players can argue with a number; they can't argue with "compared to the last 5 minutes, this is what changed". The renderer fills the comparison clause from `Evidence.Baseline`:

- `SessionMean` → "compared to this session's average"
- `RollingFiveMinute` → "compared to the last 5 minutes"
- `PreContext` → "compared to the moments before"
- `ComparableContexts` → "compared to other biomes / other weather / other bosses"

---

## 9. UI integration sketch

The Events tab plan (§9) already established the tab-strip pattern (`LIVE`, `EVENTS`, future tabs). Insights is the next tab.

### 9.1 The INSIGHTS tab

```
 ┌─ INSIGHTS · 8 of 14 live · top 8 by score ────────────────  audience: PLAYER ▾ ─┐
 │                                                                                  │
 │ ▾ CONTEXT_CORRELATED_SPIKE      Calamity · Blood Moon                  High      │
 │     Walking into Blood Moon raises Calamity from 2.3 ms to 18.7 ms (8.1×)        │
 │     observed 3× this session · last confirmed 14 s ago         [ show evidence ] │
 │                                                                                  │
 │   PEAK_CONTRIBUTOR_TO_SPIKE     Calamity SnowstormCallback             High      │
 │     The 87 ms spike at 02:14 was 78 % Calamity (top hook SnowstormCallback)      │
 │                                                                                  │
 │   CONTEXT_CONDITIONAL_COST      Spirit Mod · Underground Jungle        Medium    │
 │     Spirit Mod averages 4.1 ms in Underground Jungle vs 0.9 ms baseline (4.5×)   │
 │                                                                                  │
 │   HOT_HOOK_DOMINANCE            Calamity · BossFight=Cryogen           Medium    │
 │     SnowstormCallback is 78 % of Calamity's cost during the Cryogen fight        │
 │                                                                                  │
 │   SUSTAINED_COST_SHIFT          Fargo's Souls · since Hardmode entry   Medium    │
 │     Fargo's Souls baseline rose from 0.6 ms → 1.4 ms after Hardmode (held 47 m)  │
 │                                                                                  │
 │   NEW_CONTRIBUTOR               Mod Z · second half of session         Low       │
 │     Mod Z became 7.3× more expensive in the last 14 minutes                      │
 │                                                                                  │
 │ ░ FREE_REMOVAL_CANDIDATE        Mod W                                  Low       │
 │     Mod W cost 0.7 % of frame time with zero engagement this session             │
 │                                                                                  │
 │ ░ GC_PAUSE_CULPRIT              Calamity                            Preliminary  │
 │     Most GC pauses this session preceded by Calamity allocating heavily (62 %)   │
 │                                                                                  │
 │ ─────────────────────────────────────────────────────────────────────────────── │
 │ 6 more (not surfaced because score below cutoff)              [ show all 14 ]   │
 │ end-of-session report will include the full catalog                              │
 └──────────────────────────────────────────────────────────────────────────────────┘
```

Conventions:

- **Confidence badge** on the right (High / Medium / Low / Preliminary), colour-coded green / amber / yellow / grey.
- **Per-row hover** highlights and shows a brief tooltip with the baseline name.
- **Click-through** ([ show evidence ]) opens an inline panel beneath the row showing the medium template form, the n's, the p-value, the q-value, the effect size, and a sparkline of the supporting window.
- **Audience dropdown** in the header — `PLAYER` (default) or `MODDER` switches the rendering for the same set of records.
- **Newly-fired insights** flash for 2 seconds with the `Accent` colour.
- **`░` shading** distinguishes Preliminary insights.

### 9.2 Inline toast for new high-confidence insights

When a new insight fires with `Confidence ≥ High` outside the active tab, a brief toast appears in the top-right of the overlay:

```
 ┌─ NEW INSIGHT ────────────────────────────────────────────────┐
 │ Calamity spiked 8.1× entering Blood Moon                     │
 │ peak 18.7 ms · click for details                             │
 └──────────────────────────────────────────────────────────────┘
```

Throttled: at most one toast every 30 seconds. The toast is dismissable; click-through navigates to the INSIGHTS tab pre-scrolled to the relevant row.

### 9.3 Per-mod retrospective card

The card view is a downstream UI feature; this plan only specifies the data shape. The card reads from `InsightStore.AllLive() ∪ InsightStore.EndOfSession()` and filters by `Subject.ModId`. The renderer's medium/long forms slot directly into the card. No new template — the card is a *filter*, not a *new template family*.

The card spec belongs in a separate plan (`context/notes/retrospective-cards-plan.md`, future). What this plan needs from it: the contract that the card consumes `InsightRecord`s by `Subject.ModId` and renders them via `InsightRenderer.Render(rec, Audience.Player, Density.Medium)`. That contract is fully met by §3 and §8.

### 9.4 End-of-session report

A markdown file `<AppData>/.../PerformanceProfiler/Sessions/<identity>-<stamp>.md` produced alongside the JSONL file. Structure:

```markdown
# Performance Profiler — Session Report
**Started:** 2026-05-20 18:42:11 UTC · **Duration:** 1h 47m · **Modlist:** <fingerprint>

## What the session was like
<one-paragraph summary derived from the top 3 Player insights, slot-filled>

## For the player
### Top observations
<Player-audience insights, medium density, ranked>

### Did-not-engage candidates *(this session only)*
<FREE_REMOVAL_CANDIDATE block, badged>

## For modders
<Modder-audience insights, long density, grouped by mod>

## Raw data
- Session JSONL: <path>
- Insight count: <n> live + <m> end-of-session
- Modlist: <fingerprint hash>
```

The markdown is screenshot-shareable (README's stated goal) and machine-readable enough for an external script to ingest.

### 9.5 Visual mock — INSIGHTS tab with full UI chrome

```
 F9 ┌─ PERFORMANCE PROFILER ──────────────────────────  MODE: STANDARD ▾  2.3 % overhead ─┐
    │ [ LIVE ]  [ EVENTS ]  [ INSIGHTS ]  [ BOSSES ]  [ HOT MOMENTS ] [ DORMANT ]            │
    ├──────────────────────────────────────────────────────────────────────────────────────┤
    │ 8 of 14 live · sort score · audience PLAYER ▾                                          │
    │                                                                                        │
    │ ▾ CONTEXT_CORRELATED_SPIKE   Calamity · Blood Moon                          ●● High    │
    │   Walking into Blood Moon raises Calamity from 2.3 ms → 18.7 ms (8.1×)                 │
    │   ○○○ confirmed 3× this session · last 14 s ago                                        │
    │                                                                                        │
    │   PEAK_CONTRIBUTOR_TO_SPIKE  Calamity SnowstormCallback                     ●● High    │
    │   The 87 ms spike at 02:14 was 78 % Calamity (top hook SnowstormCallback)              │
    │                                                                                        │
    │   CONTEXT_CONDITIONAL_COST   Spirit Mod · Underground Jungle                ●  Medium  │
    │   Spirit Mod averages 4.1 ms in Underground Jungle vs 0.9 ms baseline (4.5×)           │
    │                                                                                        │
    │   HOT_HOOK_DOMINANCE         Calamity · BossFight=Cryogen                    ●  Medium  │
    │   SnowstormCallback is 78 % of Calamity's cost during the Cryogen fight                │
    │                                                                                        │
    │   SUSTAINED_COST_SHIFT       Fargo's Souls · since Hardmode entry            ●  Medium  │
    │   Fargo's Souls baseline rose from 0.6 → 1.4 ms after Hardmode (held 47 m)              │
    │                                                                                        │
    │   NEW_CONTRIBUTOR            Mod Z · second half of session                  ○  Low     │
    │   Mod Z became 7.3× more expensive in the last 14 minutes                              │
    │                                                                                        │
    │ ░ FREE_REMOVAL_CANDIDATE     Mod W                                            ○  Low     │
    │   Mod W cost 0.7 % of frame time with zero engagement this session                     │
    │                                                                                        │
    │ ░ GC_PAUSE_CULPRIT           Calamity                                         ◐ Prelim  │
    │   Most GC pauses this session preceded by Calamity allocating heavily (62 %)            │
    │ ─────────────────────────────────────────────────────────────────────────────────────── │
    │ 6 more not surfaced (score below cutoff)                          [ show all 14 ▼ ]    │
    │ a full report writes at save-and-exit                                                   │
    └────────────────────────────────────────────────────────────────────────────────────────┘
```

Colours reuse `ProfilerTheme`: `Accent` for high-confidence badges, `Warn` for medium, `TextMuted` for low/preliminary. The `░` shading is `RowDim`. No new theme colours.

---

## 10. Session log integration

### 10.1 New top-level field `insights`

The JSONL writer (`SessionLogWriter.cs`) gains an `insights` block. Bump `SchemaVersion` 3 → 4 (the events plan bumps 2 → 3; this plan stacks on top).

```json
{
  "schema": 4,
  "identity": "<hash>",
  "state": "final",
  "session": { ... existing ... },
  "mods": [ ... existing ... ],
  "coverage": { ... existing ... },
  "timeline": [ ... existing ... ],
  "spikes": [ ... existing ... ],
  "events": { ... from events plan ... },
  "insights": {
    "active":  [ /* currently surfaced records — refreshed per write */ ],
    "history": [ /* every record ever surfaced this session, with first/last ticks */ ],
    "final":   [ /* end-of-session full catalog, populated only in final state */ ],
    "gated":   { "spikes-stream": ["GC_PAUSE_CULPRIT","ALLOCATION_BURST"],
                 "engagement-signal": ["FREE_REMOVAL_CANDIDATE"] }
  }
}
```

Each record is the full `InsightRecord` shape:

```json
{
  "pattern": "CONTEXT_CORRELATED_SPIKE",
  "subject": { "modId": 3, "modName": "CalamityMod",
               "hookId": -1, "hookDisplay": null,
               "contextDim": "Weather", "contextKey": "BloodMoon" },
  "magnitude": { "baselineMs": 2.31, "observedMs": 8.05, "peakMs": 18.7,
                 "ratio": 8.1, "allocBytes": 0, "count": 180 },
  "evidence": { "sampleN": 180, "baselineN": 18000,
                "pValue": 0.0001, "pValueAdjusted": 0.003,
                "effectSize": 0.87, "effectKind": "cliffsDelta",
                "firstTickIndex": 12340, "lastTickIndex": 14820,
                "baselineKind": "RollingFiveMinute" },
  "confidence": "High",
  "audience": "Both",
  "firstSeenTick": 12340, "lastSeenTick": 14820,
  "confirmationCount": 3,
  "renderings": {
    "shortPlayer": "Calamity spiked 8.1× during Blood Moon (peak 18.7 ms, baseline 2.3 ms).",
    "mediumPlayer": "Walking into Blood Moon raised Calamity ...",
    "longModder":   "[CONTEXT_CORRELATED_SPIKE] Calamity ..."
  }
}
```

Renderings are embedded so agents reading the JSONL never have to re-run the renderer. This is important for the dual-surface contract — the agent surface gets the same words the player saw, byte-for-byte.

### 10.2 Where the data goes

Same directory as the events block: `<AppData>/Terraria/tModLoader/PerformanceProfiler/Sessions/`. The `insights` block adds ~10–50 KB to a typical session file; the markdown report is a separate file ~5–20 KB.

### 10.3 Gated detectors

The `gated` field is the honest disclosure mechanism. If the sibling spikes-and-allocations plan hasn't landed, four patterns return empty; the JSONL records this so a downstream reader sees `"GC_PAUSE_CULPRIT": gated_on=spikes-stream` rather than `"GC_PAUSE_CULPRIT": no insights found`.

---

## 11. Step-by-step implementation sequence

Discovery passes 1–3, execution 4–14. Each step lists files, verification, risk. Sized to match the events plan's cadence.

| # | Action | Files | Verify | Risk |
|---|---|---|---|---|
| **1** | Read `README.md`, `Profiling/MetricCollector.cs`, `Profiling/TickFrame.cs`, `Profiling/PerModAttribution.cs`, `Profiling/SessionLogWriter.cs`, `UI/ProfilerOverlay.cs`, `context/notes/events-tab-plan.md` cover-to-cover. Confirm symbol availability for the contracts in §3.4. | read-only | Each symbol referenced in §3.4 exists or has a documented gate | **Low** |
| **2** | Audit existing per-mod / per-hook accessors on `MetricCollector`. Identify what the engine reads (per-mod ms, per-hook ms, TickContext, FrameMs, GcMs) and confirm each is exposed publicly. | grep | Each access is a public read; no internal-only fields blocking | **Low** |
| **3** | Decide P² vs reservoir for the streaming median. Prototype both in a unit-test sketch (`tests/StreamingMedianBench.cs`, not committed) using synthetic data. Pick whichever is cheaper on a 60 Hz update for 200 buckets. | scratch | Decision recorded in `context/notes/insights-engine-decisions.md` | **Low** |
| **4** | Add `Profiling/Insights/` folder with the pure-data types: `PatternKey.cs`, `Confidence.cs`, `Audience.cs`, `BaselineKind.cs`, `SubjectRef.cs`, `Magnitude.cs`, `Evidence.cs`, `InsightRecord.cs`. All public, no tModLoader dependency. | new files | `dotnet msbuild` succeeds; nothing else references them yet | **Low** |
| **5** | Add `Profiling/Insights/Streaming/` with `Welford.cs`, `P2Quantile.cs`, `Cusum.cs`, `StreamingStats.cs`. Unit tests against synthetic streams (normal, lognormal, heavy-tailed with spikes). Verify Welford within 1e-9 of full-pass, P² within 1 % of exact median after 2000 samples. | new files | Unit tests pass | **Medium** — P² has fiddly boundary cases; rely on the standard 5-marker reference impl |
| **6** | Add `Profiling/Insights/StreamingStatsCore.cs` — the engine's per-tick update entry point, called from `MetricCollector.EndTick` after the existing harvest. Reads per-mod ms, per-hook ms, per-mod alloc (if available), TickContext. Updates streaming primitives for each (mod, active-context-bucket). Idle pairs short-circuit. | new + `MetricCollector.cs` 5-line addition | In-game, one 60-second session populates Welford state for every (mod × active-bucket). Inspect via a debug F-key dump if needed | **Medium** — wrong wiring shows up as zero in stats; add a debug counter to verify |
| **7** | Add `Profiling/Insights/Statistics/` with `MannWhitneyU.cs`, `WelchT.cs`, `CliffsDelta.cs`, `CohensD.cs`, `BinomialTail.cs`, `BhFdr.cs`. Each is pure logic, unit-tested against known reference outputs (e.g. Mann-Whitney from R's `wilcox.test`). | new files | Unit tests pass against canned reference | **Medium** — implementations are textbook but easy to introduce off-by-one rank errors; double-check against a small R/Python reference run |
| **8** | Implement detectors 4.1 (CONTEXT_CORRELATED_SPIKE), 4.2 (CONTEXT_CONDITIONAL_COST), 4.3 (HOT_HOOK_DOMINANCE), 4.6 (SUSTAINED_COST_SHIFT), 4.7 (NEW_CONTRIBUTOR), 4.8 (PEAK_CONTRIBUTOR_TO_SPIKE). Implement detector contract (§6.3). Add `DetectorScheduler.cs` and wire to MetricCollector.EndTick (per-tick cheap part) and a 1 Hz timer (heavy part). | new files | In-game session shows non-empty `InsightStore.AllLive()` after 60 seconds of varied play; debug dump confirms each detector firing at least once during a representative session | **High** — first insights appearing is the validation gate. Logic errors here propagate into rendered insights. Unit-test each detector against synthetic windows before in-game test |
| **9** | Add `Profiling/Insights/InsightStore.cs` with dedup, TTL, hysteresis, surfacing budget (§6.4). Add `RankingScorer.cs` (§7). Hard-cap live to 32, surfaced to 8. | new file | In-game session shows stable live set; toggling MODE doesn't churn the list | **Medium** |
| **10** | Add `Profiling/Insights/Rendering/` with `InsightTemplate.cs`, `InsightRenderer.cs`, `InsightFormatting.cs`, and one template file per pattern. Unit test: `AllTemplatesRenderForEveryPattern()` and `NoBannedVocabAcrossTemplates()`. | new files | Unit tests pass; render the full live set to console at session end and eyeball | **Medium** — templates are where Invariant 3 lives; the unit test enforces it |
| **11** | Add INSIGHTS tab to `UI/ProfilerOverlay.cs`. Refactor: extend the tab enum from `{Live, Events}` to `{Live, Events, Insights}` (the events plan's refactor in its step 9 sets the precedent). Implement `DrawInsightsBody` with the layout from §9.1. | `UI/ProfilerOverlay.cs` (~200 lines) | In-game, third tab visible; rows render with correct truncation; hover and click-through work | **High** — UI surface is where small bugs become obvious; restrict the change to drawing only |
| **12** | Add `Profiling/Insights/InsightExporter.cs`. Bump `SessionLogWriter.SchemaVersion` 3 → 4. Add `insights` block per §10. Generate the markdown report at session-end (§9.4). | `SessionLogWriter.cs`, new file | After a 60-second session, the JSONL file contains a well-formed `insights` block and the markdown file renders correctly | **Medium** |
| **13** | End-of-session pass: invoke the full detector catalog over the bounded session window in `Mod.Unload` / `OnWorldUnload`. Populate `InsightStore.EndOfSession()`. Drain into the JSONL `final` block and the markdown. | new dispatcher in `Profiling/Insights/EndOfSessionPass.cs` | After a 5-minute session covering varied gameplay, the `final` list is richer than `active` (because session-wide n is now large enough for more detectors to clear floors) | **Medium** |
| **14** | Add toast (§9.2) for newly-firing high-confidence insights. Throttled to once per 30 s. | `UI/ProfilerOverlay.cs` | In-game, simulate a context transition and confirm the toast appears, dismisses, and click-routes correctly | **Low** |
| **15** | Write `context/notes/insights-engine.md` capturing the final design decisions, the template list, and the gated-detector status. | `context/notes/` | User reviews and accepts | **Low** |
| **16** | Commit at logical checkpoints: (a) types + streaming primitives (steps 4-6), (b) detectors + statistics (steps 7-8), (c) store + ranking + renderer (steps 9-10), (d) UI tab (step 11), (e) JSONL + markdown + EOS (steps 12-13), (f) toast + capture (steps 14-15). | git | Each commit builds, runs, and is in-game observable | **Low** |

The whole effort sits between the events plan's step 13 (events plan done) and the milestone-4 closure. Estimated wall time on a focused stretch: a couple of weeks of evenings.

---

## 12. Honest risk register — additional risks specific to this engine

(Re-summarises §1 risks plus the new ones surfaced during the design.)

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| **R-A** | A detector emits a confidently-wrong insight that gets shared | Medium | High (mod-author harm) | Confidence badge mandatory; "preliminary" tag for low-sample; supporting-evidence panel; tone rules + banned-vocab unit test; user can hover-disable a detector |
| **R-B** | Statistical machinery is correct but inputs are biased (the spikes stream over-reports certain mods) | Low | Medium | Detectors take input streams via interface; sibling-plan implementations are individually testable; the engine never trusts an input shape without validating the contract |
| **R-C** | Per-tick streaming-stats cost grows linearly with active-bucket count | Medium | Low | Per-tick update is O(active mods × active buckets) ≈ O(200) per tick; measured in step 6. Cap on tracked (mod × bucket) pairs at 500 with eviction by least-recently-updated if exceeded |
| **R-D** | A future contributor adds a new detector that violates Invariant 3 | Medium | Low (caught by tests) | `NoBannedVocabAcrossTemplates()` unit test catches it at PR time |
| **R-E** | Templates drift from `InsightRecord` schema after a record change | Low | Medium (NRE at render) | `AllTemplatesRenderForEveryPattern()` unit test catches missing/extra slot names |
| **R-F** | The end-of-session pass runs into a heavy modlist's `Mod.Unload` ordering and gets disposed mid-write | Medium | Low (lose a JSONL final block) | Run the EOS pass before `HookInterceptor.Uninstall` in `Mod.Unload` ordering; the events plan already established this idiom |
| **R-G** | A modder uses a wrong-but-confident insight, fixes the wrong thing, and the issue is misdiagnosed | Medium | Medium | Modder-audience long form always cites raw n, p, q, effect size — *the modder can replicate the test before acting* |
| **R-H** | The user disagrees with an insight (their own playstyle context not visible to the engine) | High | Low | Per-row "hide" / "downvote" not in v1; for v1, the workaround is the audience badge — the insight says "this session" and is explicitly transient |
| **R-I** | An insight's NLG template hardcodes a vanilla biome name | Low (caught by code review) | Medium | Templates only slot-fill from `InsightRecord`; biome / mod / hook names come from the registry, never the template |
| **R-J** | The full catalog re-run at end-of-session is slow on a long session | Low | Low (UX hitch on save-and-exit) | The pass is bounded — O(detectors × pairs); profile in step 13; if it exceeds 100 ms move to a Task before the unload finalises |
| **R-K** | A detector's hard cap on confirmation count silently caps a runaway pattern | Low | Negligible | `ConfirmationCount` is `int`, capped at `int.MaxValue` — no practical limit |
| **R-L** | Renderer cache (cached strings on the record) goes stale when ranking re-runs | Low | Low | Cache fields are cleared whenever `RankingScorer` mutates the record; alternatively, cache is keyed by `(Confidence, ConfirmationCount)` so any change invalidates |

None block the design.

---

## 13. Honest gaps — what this engine explicitly does NOT do

This section is the trust-building part. Caner asks for an engine; honesty about its limits is what makes the output ship-quality.

| Gap | Why deferred / not solvable here | Workaround / future surface |
|---|---|---|
| **True counterfactuals** ("if you removed Mod X, FPS would be Y") | Requires a model of mod cost composition; mods share engine surfaces (NPC slots, projectile slots), so removing one shifts the cost of others rather than cleanly subtracting. Observational data alone cannot identify the counterfactual without strong assumptions (linearity, independence) that we know are false. | A weak heuristic could estimate "removing Mod X would have saved ~Y ms on a session like this one, assuming independent costs" with a fat warning. v2. |
| **Source-line attribution** ("Mod X's slow function is in `MyHelper.cs:42`") | MonoMod gives method-level granularity; the profiler does not capture stack frames or symbols at sample time | Out of scope. Modder export already names the method; the mod author has the source. |
| **Synergy detection** ("X and Y cost more together than X+Y individually") | Requires counterfactual simulation; observational data cannot distinguish synergy from confounding | Out of scope v1. Sketched in §1. |
| **Cross-machine variance** ("your hardware is slow") | Each session is its own baseline; no shared corpus by Invariant policy (no telemetry) | Out of scope by design. README's "no telemetry" rules this out. |
| **Cross-session insights** ("over your last 10 sessions, Mod X has trended up") | Lifetime rollup is README Milestone 3+; this plan operates on one session | Future surface. When lifetime rollup lands, a CROSS_SESSION_TREND detector pattern can be added — same shape, longer time window. |
| **Modded-event correlation for events not piggybacking on vanilla flags** | Sibling plan's `Mod.Call` API for `ReportEventStart/ReportEventEnd` is the path; not v1 of either plan | Same `Mod.Call` opt-in surface |
| **Engagement-signal-driven insights** | README's engagement vector (items used, NPCs killed, etc) is a separate engineering surface; not yet built | FREE_REMOVAL_CANDIDATE is gated on it landing |
| **Per-mod-config recommendations** ("set Calamity ▸ Visual Effects ▸ Afterimages = false") | Requires a curated config-knowledge dataset (README Milestone 4 sub-bullet); not part of this engine's scope | Future; the rendered insight could *link* to a config snippet supplied by a separate dataset |

Every gap is the deliberate answer to a real question. The engine's value is what it does competently, not the breadth of claims it makes.

---

## 14. Calibration — small worked example end-to-end

Player starts a Hardmode session at tick 0. Forest, daytime, no events.

**Tick range 0–12000 (3 min 20 s)**: Forest, day. Calamity averages 0.6 ms/tick (median); MAD 0.15.

**Tick 12000–12005**: Blood Moon flips on (`Main.bloodMoon = true`). `IEventStream.HasTransitionsSince(11999)` returns `[(tick=12000, dim=Weather, added=[BloodMoon])]`.

**Tick 12005–12100** (the K=60-tick observation window after the transition): Calamity ms rises sharply. Per-tick samples: 1.2, 4.8, 8.1, 12.0, 14.2, 16.8, 18.7 (peak), 17.4, 15.9, 14.0, … settling around 8.0 ms median.

### Flow through the pipeline

```
[ tick 12005 ]  StreamingStats.Update(modId=Calamity, frameMs=1.2, contextHash=BloodMoonOn)
                  ⤷ Welford(Calamity, BloodMoon).Add(1.2)
                  ⤷ P2Quantile(Calamity, BloodMoon).Update(1.2)
                  ⤷ Cusum(Calamity, BloodMoon).Update(1.2, mean=0.62, sd=0.15) → S=3.1, no alarm
                  ⤷ Welford(Calamity, GLOBAL).Add(1.2)                  // session-wide

[ tick 12012 ]  PEAK_CONTRIBUTOR_TO_SPIKE fires: 18.7 ms tick, Calamity dev = +18.1 ms ≈ 96 %
                  ⤷ emit InsightRecord(Pattern=PEAK_CONTRIBUTOR_TO_SPIKE, ConfirmationCount=1, Confidence=Preliminary)

[ tick 12060 ]  1 Hz scheduler tick. DetectorScheduler runs:
                  ⤷ CONTEXT_CORRELATED_SPIKE.Evaluate(Calamity, BloodMoonOn) →
                       pre-window:  ticks 11940..11999 (60 ticks), median 0.6, MAD 0.15
                       post-window: ticks 12000..12059 (60 ticks), median 8.0, peak 18.7
                       Mann-Whitney U: U=14, p≈0.0001
                       Cliff's delta: 0.95 (large)
                       Floor: ✓ Cliff's delta ≥ 0.33 ✓ q ≤ 0.10 (single test, q=p≈0.0001 < 0.10)
                       Min sample: 1 transition this session (≥ 3 required for High) → Confidence=Preliminary
                       → emit InsightRecord(...).

[ tick 12060 ]  InsightStore.Submit(rec1, nowTick=12060):
                  ⤷ key = Hash(CONTEXT_CORRELATED_SPIKE, modId=3, hookId=-1,
                               contextDim=Weather, contextKey=BloodMoon)
                  ⤷ live[key] = rec1; FirstSeenTick = LastSeenTick = 12060; ConfirmationCount = 1

[ tick 14000 ]  Player leaves Blood Moon. CUSUM(Calamity, BloodMoon) is closed.

[ tick 18000 ]  Player re-enters Blood Moon. CONTEXT_CORRELATED_SPIKE re-fires.
                  ⤷ InsightStore.Submit updates existing record: ConfirmationCount=2, Confidence=Low.

[ tick 24000 ]  Third Blood Moon entry. CONTEXT_CORRELATED_SPIKE fires again.
                  ⤷ ConfirmationCount=3, Confidence=Medium.
                  ⤷ q-adjusted across the 9 tests this pass = 0.003.
                  ⤷ q ≤ 0.05 ✓ + ConfirmationCount ≥ 3 ✓ → Confidence promoted to High.
```

### Rendered output at tick 24800

| Audience / Density | Output |
|---|---|
| Player / Short | `"Entering Blood Moon consistently spikes Calamity (8.1×, peak 18.7 ms)."` |
| Player / Medium | `"Walking into Blood Moon raises Calamity's per-tick cost from a 0.6 ms baseline to a peak of 18.7 ms within 60 ticks. Observed 3 times this session; every entry triggered the spike."` |
| Modder / Long | `"[CONTEXT_CORRELATED_SPIKE] Calamity on entering Weather=BloodMoon\nBaseline (per-mod rolling 5-min, n=18000): median 0.62 ms, MAD 0.15.\nObserved (post-transition K=60 window, n=180): median 8.05 ms, peak 18.7 ms.\nCliff's delta 0.95 (large), Mann-Whitney p=0.0001, BH-adjusted q=0.003.\nTransition count this session: 3.\nTop contributing hooks during the observed window:\n  1. Calamity.GlobalNPC.AI                  68 %  12.7 ms\n  2. Calamity.Projectile.PostAI             18 %   3.4 ms\n  3. Calamity.ModSystem.PostUpdateEverything 8 %   1.5 ms\nObservation count: 3 distinct entries into BloodMoon, all triggered the rise."` |

### Score at tick 24800

```
magnitude:    ratio 8.1× → normalised 0.85
confidence:   High → 1.0
recency:      LastSeenTick = 24800, now = 24800 → 1.0
actionability: CONTEXT_CORRELATED_SPIKE → 0.8
novelty:      ConfirmationCount = 3 → 1 / (1 + log2 4) = 0.33
audience:     Both → matches PLAYER overlay → 1.0

score = 0.30 * 0.85 + 0.25 * 1.0 + 0.15 * 1.0 + 0.15 * 0.8 + 0.10 * 0.33 + 0.05 * 1.0
      = 0.255 + 0.250 + 0.150 + 0.120 + 0.033 + 0.050
      = 0.858
```

Ranked #1 of 14 live insights. Surfaced top of the INSIGHTS tab; the new-Confidence-promotion (Medium → High at tick 24000) fired a one-shot toast.

---

## 15. Honest summary

The Insights Engine is a narrow, evidence-driven layer on top of the data the profiler already collects (or will collect, per the sibling plans). The core technical choices:

- **Streaming primitives only** (Welford, P², CUSUM) for per-tick work. Zero allocation, < 10 µs/tick amortised. Lite-mode-safe.
- **Hypothesis testing at 1 Hz**, not per-tick. Mann-Whitney as the default — small-n-friendly, non-parametric, distribution-agnostic.
- **Effect-size floors with statistical-significance gates**. Cliff's delta ≥ 0.33 + BH-FDR q ≤ 0.10 across all per-pass tests. Stops the multiple-comparison flood without hand-tuning thresholds per detector.
- **Hysteresis + confirmation count** so the same insight doesn't flicker.
- **Template-based NLG with slot filling**. Deterministic. Reviewable. Forbidden-vocab enforced by unit test.
- **Two surfaces** — live overlay (Player) + JSONL/markdown export (Modder). Same records, different renders.

The catalog covers six orthogonal lenses (context-cost, hot-hook, allocation, change-point, contributor-shift, spike-attribution) plus two modder-only descriptors (hook-frequency tail, GC-pause attribution) plus one engagement-gated player insight (free-removal candidate). Nothing covers everything; everything covers something honestly.

The honest gaps are large and named. The engine produces words; it does not produce certainty. Every insight cites the measurement that produced it; every confidence is operationalised; every comparison is named. A modder reading a high-confidence Modder-Long insight has enough to reproduce the test and act. A player reading a high-confidence Player-Medium insight has enough to know which mod and which context, and a starting point for their own pruning decision — *with evidence, not vibes*, as the README puts it.

The single largest remaining uncertainty is the sibling spikes-and-allocations plan. Four of the ten catalog entries are gated on it. The engine ships without them; the gated detectors stay dormant; the JSONL `insights.gated` block names the gap. When the sibling plan lands, the gated detectors enable with no other code change.

---

## Sources cited in §0 (inline)

- Honeycomb BubbleUp — https://www.honeycomb.io/blog/debugging-faster-enhancements-to-bubbleup ; https://docs.honeycomb.io/investigate/analyze/identify-outliers
- Welford / streaming variance — https://en.wikipedia.org/wiki/Algorithms_for_calculating_variance ; https://www.embeddedrelated.com/showarticle/785.php
- Benjamini-Hochberg FDR — https://mcpanalytics.ai/articles/benjamini-hochberg-procedure-practical-guide-for-data-driven-decisions ; https://arxiv.org/pdf/1406.7117
- Cliff's delta — https://metricgate.com/docs/cliffs-delta/ ; http://www.scielo.org.co/scielo.php?script=sci_arttext&pid=S1657-92672011000200018
- PerfView / Vance Morrison — https://atscaleconference.com/videos/the-keys-to-actionable-perf-investigations/ ; https://github.com/microsoft/perfview
- CUSUM — https://towardsdatascience.com/probabilistic-cusum-for-change-point-detection-121f793ab3a1/ ; https://github.com/giobbu/CUSUM
- Datadog anomaly detection — https://docs.datadoghq.com/monitors/types/anomaly/
- Tableau Explain Data — https://help.tableau.com/current/server/en-us/explain_data_explained.htm
- MAD — https://en.wikipedia.org/wiki/Median_absolute_deviation ; https://eurekastatistics.com/using-the-median-absolute-deviation-to-find-outliers/ ; https://aakinshin.net/posts/harrell-davis-double-mad-outlier-detector/
- Mann-Whitney / Welch — https://statmate.org/blog/t-test-vs-mann-whitney ; https://www.researchgate.net/post/Welch_T-test_vs_Mann-Whitney_U_test
- Lighthouse — https://developer.chrome.com/docs/lighthouse/performance/performance-scoring
- USE method / heat maps — https://www.brendangregg.com/usemethod.html ; https://www.brendangregg.com/heatmaps.html
- NLG slot filling — https://www.yellowfinbi.com/blog/what-is-natural-language-generation ; https://deepgram.com/ai-glossary/natural-language-generation
