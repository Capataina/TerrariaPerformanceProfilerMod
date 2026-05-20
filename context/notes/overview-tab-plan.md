# Aggregated Overview Tab — Implementation Plan

> **Status (2026-05-20): SHIPPED — preserved as historical research record.** OverviewTab + ModImpactScorer shipped in 693847f and a376f6a. Truncation caches and per-frame allocation fixes landed in audit round aa914ce. Multi-tab framework refactor in 037f8d5. Canonical reality: systems/overlay.md.
>
> Read the system files for current reality; this plan is the design brief that shipped, kept for the rationale.


> Scope: introduce a multi-tab F9 overlay and make a new **Overview** tab the default landing surface. The Overview is the *aggregated* view: every loaded mod ranked by a single composite "performance impact" score that fuses CPU cost, spike contribution, and allocation pressure. Tab 1 = `OVERVIEW`, tab 2 = `TREE` (the existing btop-style per-mod tree), tab 3 = `EVENTS` (see `events-tab-plan.md`), tab 4 = `SPIKES` (see `spikes-and-allocations-plan.md` — does not exist yet; this plan designs against its expected outputs). Honours all four Project Invariants. Targets tModLoader 1.4.4 on .NET 8.
>
> The plan is sized and shaped to mirror `context/ILHook-migration-plan.md`: an evidence ledger, a viability verdict, a scoring methodology survey with a justified recommendation, step-by-step implementation sequencing, an honest risk register, an honest gaps section, and a testing strategy. The reader should be able to implement everything below without doing further external research.

---

## 0. Research evidence ledger

The plan rests on three evidence layers: (a) the live profiler's own data model, verified from source; (b) external profiler / MCDA literature, cited URL by URL; (c) .NET 8 GC pause behaviour, sourced from Microsoft Learn and the dotnet/runtime repo. Anything the implementer would otherwise need to re-research is pinned here.

### 0.1 Code surfaces the Overview tab consumes

| Claim | Evidence |
|---|---|
| `MetricCollector.PerModCategoryAverageMs : IReadOnlyList<double>` is the rolling 30 s average per-mod-per-category cost vector; layout `[modId * CategoryCount + categoryId]` | `Profiling/MetricCollector.cs:135-137` ("`Rolling 30-second per-mod/category average in milliseconds`"). |
| `MetricCollector.PerModCategoryMs` is the smoothed (EWMA, α = 0.06) live view | `Profiling/MetricCollector.cs:30`, `:130`. |
| `RingBuffer<TickFrame> MetricCollector.History` is a 30 s window of `TickFrame` records with `FrameTimeMs`, `GcTimeMs`, `TickIndex` and entity counts | `Profiling/MetricCollector.cs:122`, `Profiling/TickFrame.cs`. |
| `PerModAttribution.ModCount` and `CategoryCount = 7` give the dense grid dimensions; `PerModAttribution.CategoryNames` labels each column | `Profiling/PerModAttribution.cs:50`, `:67-68`. |
| `HookInterceptor.ProfiledModNames` exposes the ordered mod-name array used by the existing tree's `BuildSortedRows` | `UI/ProfilerOverlay.cs:646`. |
| `ProfilerTheme.CostColor(double fraction)` already returns the green/amber/red gradient used in the tree's cost bars | `UI/ProfilerTheme.cs:71`. |
| Existing overlay panel width = 640 px; header height = 28 px; mod tree starts at `RowsTopOffset = 172 f`; per-mod row height = 18 px; cost-bar width = 170 px; coverage badge column at `panelX + 592` | `UI/ProfilerOverlay.cs:61`, `:67`, `:74-78`, `:631`. |
| Header right-hand cluster currently holds `MetricToggle` ("30S AVG"/"NOW") and `PauseToggle` ("LIVE"/"PAUSED") at `localX ∈ [426, 596]`, `localY ∈ [6, 22]` | `UI/ProfilerOverlay.cs:66-71`, `:188-213`. |
| `OverlayPanel.LeftMouseDown` already partitions clicks into header / row regions via panel-local Y test against `HeaderHeight`; a tab strip inserted between header and rows will fit the same dispatch model | `UI/ProfilerOverlay.cs:116-143`. |

### 0.2 External profiler-ranking conventions

| Source | Claim used | URL |
|---|---|---|
| JetBrains dotTrace "Top Methods" view shows per-method *Own time*, *Total time*, *Calls*, sorted by Own time descending; secondary sort by Total time | "Hot Spots view" docs | <https://www.jetbrains.com/help/profiler/Performance_Analysis__Hot_Spots_View.html> |
| JetBrains dotMemory ranks objects by **Retained Size** (the bytes that would be reclaimed if the object were collected), not by Shallow Size | dotMemory "Dominators" doc | <https://www.jetbrains.com/help/dotmemory/Dominators_View.html> |
| Microsoft PerfView's "CPU stacks" presents per-frame inclusive ms and exclusive ms; the default sort is inclusive ms descending. Spikes are surfaced by "Sample Counts > X" filters, not by a separate metric | PerfView "Tutorial: CPU Investigation" | <https://github.com/microsoft/perfview/blob/main/documentation/Tutorial.md> |
| Visual Studio "Diagnostic Tools → CPU Usage" Top Functions ranks by self-CPU%, with Total-CPU% secondary; no composite with memory | docs.microsoft "CPU Usage diagnostic tool" | <https://learn.microsoft.com/en-us/visualstudio/profiling/cpu-usage> |
| chrome://tracing / Performance panel separates "Bottom-Up" (self time) from "Call Tree" (total time) and uses a discrete "long task" (> 50 ms) flag for spikes rather than mixing into the main rank | Chrome DevTools docs | <https://developer.chrome.com/docs/devtools/performance/reference#bottom-up> |
| BenchmarkDotNet result tables present Mean / Error / StdDev / Median / Allocated as **separate columns, never composited** — multi-metric ranking is left to the reader | BenchmarkDotNet "How it works" | <https://benchmarkdotnet.org/articles/overview.html> |
| btop's process list sorts by `CPU %` by default, with secondary visible columns for `MEM`, `Threads`; the column the user clicks becomes the sort key. There is no composite "process pain" metric | btop README | <https://github.com/aristocratos/btop#usage> |
| Linux `top` / glances behave the same — composite scoring is *not* a convention in OS-level process monitors. The user's instinct that "btop-style aggregation" exists in this space is therefore not a borrowed pattern; we are inventing it (carefully) | btop README, glances docs | same |

The honest takeaway: **none of the named profilers ship a single-number composite of CPU + spikes + allocations.** They show columns and let the human integrate. The user's ask — to *invent* a composite for the modded-Terraria case — is therefore a novel design choice. The justification must hold up on its own; we cannot point at dotTrace and say "they did it this way."

### 0.3 Multi-criteria decision analysis (MCDA)

| Source | Claim used | URL |
|---|---|---|
| TOPSIS (Hwang & Yoon, 1981) ranks alternatives by Euclidean distance from a positive-ideal solution vs a negative-ideal solution, after column-normalising each criterion. Robust to widely-differing units | Wikipedia overview | <https://en.wikipedia.org/wiki/TOPSIS> |
| Weighted-sum model (WSM) is the simplest aggregation: `score_i = Σ w_j × normalised_x_ij`. Sensitive to normalisation choice; fine when criteria are commensurable after normalisation | "Weighted sum model" | <https://en.wikipedia.org/wiki/Weighted_sum_model> |
| Weighted-product model (WPM) multiplies instead of sums: `score_i = Π normalised_x_ij^{w_j}`. Penalises mods that are extreme on any one axis; behaves like a soft AND | "Weighted product model" | <https://en.wikipedia.org/wiki/Weighted_product_model> |
| Analytic Hierarchy Process (AHP) elicits pairwise weights from a decision-maker via a consistency-checked matrix. Overkill for three criteria; cited for completeness | Saaty 1980, summary | <https://en.wikipedia.org/wiki/Analytic_hierarchy_process> |
| Min-max normalisation `x' = (x - min) / (max - min)` maps a metric to [0, 1] but is sensitive to outliers — one heavy mod compresses the rest to near-zero | "Feature scaling" | <https://en.wikipedia.org/wiki/Feature_scaling> |
| Robust scaling (`x' = (x - median) / IQR`) is the standard outlier-resistant alternative | scikit-learn `RobustScaler` docs | <https://scikit-learn.org/stable/modules/generated/sklearn.preprocessing.RobustScaler.html> |
| Z-score standardisation requires ≥ ~30 samples for the mean/stddev to stabilise; with N < 10 it is unreliable | textbook fact, summary | <https://en.wikipedia.org/wiki/Standard_score> |

### 0.4 .NET 8 GC pause ↔ allocation rate

The "convert allocation rate to ms-equivalent" question is the single hardest piece of evidence in this plan. The summary of what is known:

| Claim | Evidence |
|---|---|
| .NET 8 default is Server GC for workloads that opt in, Workstation GC otherwise; Terraria/tModLoader runs Workstation GC unless explicitly configured | dotnet/runtime "GC configuration" | <https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/workstation-server-gc> |
| Workstation GC Gen0 / Gen1 pause times are typically sub-millisecond on a modern CPU; Gen2 pauses (a "full GC") are the ones that show up as frame stutters and range from ~5 ms to ~50 ms depending on heap size | Microsoft "Background GC" / Maoni Stephens blog | <https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/background-gc>, <https://devblogs.microsoft.com/dotnet/server-gc-and-background-gc-in-net-core/> |
| Gen0 budget on Workstation GC is roughly 256 KB to a few MB depending on cache geometry; when allocations within a tick exceed it, a Gen0 collection is triggered | dotnet/runtime source comments; Maoni's "GC ETW events" article | <https://devblogs.microsoft.com/dotnet/dotnet-etw-trace-events-for-gc/> |
| `GC.GetTotalPauseDuration()` (added in .NET 7, refined in .NET 8) returns cumulative pause time process-wide; the profiler already uses it to credit the per-tick `GcTimeMs` | `MetricCollector.cs:293-296`; API doc | <https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalpauseduration> |
| There is **no publicly-documented closed-form mapping** from "this thread allocated N bytes" to "this contributed M ms of GC pause time later." The Gen0 trigger is global, and pause time attribution back to the allocator is an *open research problem* in the .NET GC literature | absence of evidence — searched `dotnet/runtime` issues for "attribution" + "pause" + "allocation rate", no formal mapping exists | <https://github.com/dotnet/runtime/issues?q=allocation+attribution+pause> |

**Consequence for the plan:** the allocation→ms conversion factor cannot be a physical constant. It is a *tunable heuristic* with a defensible default, documented as such in code and in UI. §5.3 commits to the heuristic and explains the calibration path.

### 0.5 Sibling-plan dependencies

| File | Status | What the Overview consumes from it |
|---|---|---|
| `context/notes/events-tab-plan.md` | **Exists** (verified at `context/notes/events-tab-plan.md`). Defines the tab strip as a 22 px row below the header, tabs left-to-right; the Events tab is the immediate sibling. | Tab-strip layout convention; we extend it with `OVERVIEW` as the leftmost (first) tab. The Events tab itself is independent. |
| `context/notes/spikes-and-allocations-plan.md` | **Exists.** | Two new per-mod metric vectors: a *spike-contribution score* and a *per-mod allocation rate (bytes/tick, smoothed)*. The Overview defines the **shape** it needs, the spikes plan defines the production. |

The Overview tab is designed defensively: if the spikes/allocations data is unavailable when the Overview lands, the tab degrades to "CPU-only impact" with both other component bars greyed out and a footer note: `spike + allocation tracking — pending Milestone X`. The composite formula collapses gracefully — see §4.

### 0.6 Interface contract with `spikes-and-allocations-plan.md`

The sibling plan **must** ship these two read-only data surfaces. The Overview reads, the sibling produces.

```csharp
// In Profiling/SpikeTracker.cs (sibling plan).
public sealed class SpikeTracker
{
    // Per-mod spike contribution score, smoothed over the same 30 s window
    // MetricCollector uses. Units: ms-equivalent of "spike pain" — the number
    // of ms over baseline this mod contributed during recent spikes, weighted
    // by how recent. Layout: indexed by modId, length = PerModAttribution.ModCount.
    public IReadOnlyList<double> PerModSpikeImpactMs { get; }
}

// In Profiling/AllocationTracker.cs (sibling plan).
public sealed class AllocationTracker
{
    // Per-mod allocation rate, smoothed bytes/tick over the same window.
    // Layout: indexed by modId, length = PerModAttribution.ModCount.
    public IReadOnlyList<long> PerModBytesPerTick { get; }
}
```

If those surfaces deviate from this shape (length differs, units differ), the Overview's `ModImpactScorer` falls back to "CPU-only" mode and logs a single `Warn` on world-load. The contract is explicit; drift surfaces immediately.

---

## 1. Viability verdict

**Doable. Recommended. Three real risks, each with a concrete mitigation.**

The Overview tab is a pure aggregation surface over data the profiler already produces (CPU) plus data the sibling plans will produce (spikes, allocations). The only *new* engineering is (a) a tab strip in `ProfilerOverlay`, (b) a `ModImpactScorer` that runs at low cadence (1–2 Hz), and (c) a re-layout of the Overview body. No new hot-path work is added. No per-tick allocation. No new IL-hook surface.

The scoring methodology is the part that needs care, not the wiring.

### Risks the plan must address

| Risk | Trigger | Mitigation |
|---|---|---|
| **R1. Composite misrank.** A composite that gives wrong rankings is worse than no composite — it hides a real culprit behind a misleading single number. | Any scoring choice. | The composite **always** sits next to its three components in the same row; clicking a column header re-sorts by that component alone. The composite is a *starting point*, not gospel. See §4 and §6. |
| **R2. Ranking volatility.** If the score changes 60 Hz the leaderboard reshuffles every frame, which is unreadable. | Per-frame recompute. | Composite computed at 1 Hz over the 30 s rolling-average inputs (already smoothed by `MetricCollector.PerModCategoryAverageMs`). Add rank-change hysteresis: rows reorder only when the composite delta exceeds a *visible-difference threshold* (default 3 %). See §7. |
| **R3. Conversion-factor error.** Alloc→ms and spike→ms heuristics can be wrong by an order of magnitude in either direction. | Any synthesis attempt. | Each conversion factor is a single, well-named `const double` with rationale in a comment. The factor is also surfaced in the overlay's "calibration" footer so a power user can verify it against measured GC events. A calibration step (§5.3) re-derives the factor empirically from the player's own session. |

### Honest uncertainties

- **The "right" weighting for the three components.** We pick a default (equal contribution after each is normalised to ms-equivalent), but this is an opinion, not a derived truth. The plan exposes both the composite *and* the components so the user can build their own intuition.
- **The pain-function shape.** Below ~0.5 ms a mod is invisible; above ~5 ms it is noticeable; above ~30 ms the frame is dropped. The plan picks a piecewise-linear pain function with thresholds drawn from the player-perception literature on input latency, not a physics model.
- **Calibrating allocation→ms on a per-machine basis.** A modern desktop with a 64 MB Gen0 can absorb 10× the allocation rate of an older one with 4 MB before triggering a Gen0 GC. The Overview can self-calibrate by observing the player's own `GcTimeMs` and total allocation rate, but the first ~60 s of any session has too little data for the calibration to converge. We accept "warming up" as a state.

---

## 2. The scoring problem stated precisely

### 2.1 What "performance impact" means

A mod's performance impact is a measure of how much that mod degrades the player's *experience of* the game. Player experience has three distinct components, each with a different psychoacoustic signature:

| Component | Felt as | Measured as | Already in the codebase? |
|---|---|---|---|
| **CPU baseline** | Lower average framerate ("the game feels slow") | Per-mod ms/tick, smoothed over 30 s | **Yes** — `MetricCollector.PerModCategoryAverageMs` |
| **Spike contribution** | Stutters, frame drops, "the game lags when X happens" | Per-mod contribution to frames that exceed a baseline budget (e.g. > 1.5 × session mean) | **No** — sibling plan |
| **Allocation pressure** | Periodic micro-freezes that arrive seemingly at random (Gen0/Gen1 GC), or rare long freezes (Gen2 GC) | Per-mod bytes-allocated/tick, smoothed | **No** — sibling plan |

These are not interchangeable currencies and the plan never pretends they are. A mod that spends 2 ms/tick consistently is different in kind from a mod that costs 0.2 ms/tick but produces one 50 ms spike every five seconds, which is different again from a mod that allocates 1 MB/s and triggers a Gen0 collection every minute.

The composite's job is to give the player a *first ordering* — a ranked list to start reading from the top of. The honest contract is: the composite says "look at this mod first", and the component bars beside it say "and here is why we said that."

### 2.2 The aggregator's inputs, formally

Let `M` be the set of profiled mods (size ~10–100 on a typical session). For each mod `m ∈ M`:

| Symbol | Source | Units | Cadence |
|---|---|---|---|
| `cpu(m)` | `PerModCategoryAverageMs` summed across categories for that mod | ms/tick (rolling 30 s) | 60 Hz produce, 1 Hz consume |
| `spike(m)` | `SpikeTracker.PerModSpikeImpactMs[m]` (sibling) | ms-equivalent of spike pain | sibling defines; assume ≥ 1 Hz |
| `alloc(m)` | `AllocationTracker.PerModBytesPerTick[m]` (sibling) | bytes/tick (rolling 30 s) | sibling defines |

The scorer reads these into a local triple `(c, s, a)` per mod and returns a triple `(composite, components, classification)`.

### 2.3 The aggregator's outputs

```csharp
public readonly struct ModImpact
{
    public readonly int ModId;
    public readonly double Composite;       // ms-equivalent total pain
    public readonly double CpuMs;           // raw c, in ms/tick
    public readonly double SpikeMs;         // raw s, in ms-equivalent
    public readonly double AllocMsEq;       // raw a converted to ms-equivalent
    public readonly double ShareOfTotal;    // Composite / Σ Composite, in [0,1]
    public readonly ImpactBand Band;        // Green / Amber / Red against absolute thresholds
}

public enum ImpactBand { Green, Amber, Red }
```

The composite is in ms-equivalent, so it composes additively with itself, has an intuitive meaning ("this mod costs roughly N ms per tick of player-felt pain"), and the colour bands are against **absolute** thresholds (Green < 1 ms, Amber 1–4 ms, Red > 4 ms) rather than relative ones. A green row is objectively fine, not "fine relative to this modlist", which is the property the user explicitly wants.

---

## 3. Scoring methodology — survey of options

Six candidates were considered. Each is evaluated against six properties: **explainable** (one sentence to a non-engineer), **degrades** (handles a missing input gracefully), **single-mod** (sensible when |M| = 1), **stable** (no reshuffling on small input perturbation), **absolute** (the number has a unit, not a rank), **cheap** (O(|M|) per refresh, no per-tick cost).

### 3.1 Frame-budget reduction (weighted ms-equivalent sum)

> "Each metric is converted to ms-equivalent on the same 16.67 ms frame budget; the composite is their sum."

`Composite(m) = c + s + α × (a / TickAllocBudget)`

where `TickAllocBudget` and `α` are calibrated so that an allocation rate equal to the budget contributes `α` ms-equivalent (default `α = 2.0`, `TickAllocBudget = 65_536` bytes/tick — see §5.3).

| Property | Verdict |
|---|---|
| Explainable | "Roughly how many ms of your 16.67 ms frame budget this mod costs you, all-in." ✅ |
| Degrades | If `s` or `a` is unavailable, drop that term — composite stays meaningful. ✅ |
| Single-mod | Works — value is absolute. ✅ |
| Stable | Inputs are all already smoothed (30 s windows). ✅ |
| Absolute | Yes — ms units throughout. ✅ |
| Cheap | One add, one multiply per mod. ✅ |

**Strengths:** intuitive output, unit-carrying, additive, decomposes trivially into components, plays nicely with absolute colour bands (Green < 1 ms etc).

**Weaknesses:** the allocation-conversion constant is a heuristic — if it is off by 5×, the alloc contribution is misweighted. Mitigation: the constant is visible in code, comment-justified, surfaced in the overlay footer, and recalibrable from observed GC pause history (§5.3).

### 3.2 Z-score / standardised sum

> "Each metric is z-scored across the modlist; the composite is the (weighted) sum of z-scores."

`Composite(m) = z_c(m) + z_s(m) + z_a(m)`

| Property | Verdict |
|---|---|
| Explainable | "How many standard deviations worse than average this mod is, summed across CPU, spikes, and allocations." ⚠ — defensible but not single-sentence intuitive. |
| Degrades | If a metric is missing, the z-score is undefined for everyone. Requires per-component fallback. ❌ |
| Single-mod | Fails — z-score of one sample is undefined (std = 0). ❌ |
| Stable | Inputs are smoothed but the mean/std shift as new mods come and go. ⚠ |
| Absolute | No — the number is purely relative. ❌ |
| Cheap | O(|M|) per refresh, but two passes (mean + std, then z). ✅ |

**Rejected as primary**, but useful as a secondary view to answer "which mod is the outlier in this modlist". Could be exposed as a `RELATIVE` toggle alongside `ABSOLUTE` (the frame-budget view). Out of v1 scope.

### 3.3 Percentile-rank sum

> "Each metric ranks each mod (1 = best, N = worst); composite = sum of percentile ranks."

| Property | Verdict |
|---|---|
| Explainable | "Roughly where this mod sits in the leaderboard, averaged across CPU, spikes, allocations." ⚠ |
| Degrades | Skip a metric and re-rank the other two. ✅ |
| Single-mod | Fails — rank is undefined for N = 1. ❌ |
| Stable | Very stable — ranks rarely shuffle on small perturbations. ✅ |
| Absolute | No — purely ordinal. ❌ |
| Cheap | O(|M| log |M|) for the sort, fine at |M| ≤ 200. ✅ |

**Rejected as primary** — loses absolute meaning, which is exactly what the player needs to decide "is this a real problem or noise?"

### 3.4 TOPSIS (multi-criteria decision analysis)

> "Each mod's score is its Euclidean distance from the *ideal-best* (zero on every axis) divided by the sum of distances to ideal-best and ideal-worst."

| Property | Verdict |
|---|---|
| Explainable | "Distance from the perfect mod, normalised against the worst mod." ⚠⚠ — not intuitive. |
| Degrades | Drop a column and recompute — works. ✅ |
| Single-mod | Fails — needs at least two points to define ideal-worst. ❌ |
| Stable | Stable but **noisy** in the long-tail: as a mid-ranked mod's number wobbles, its distance ratio wobbles too. ⚠ |
| Absolute | No — bounded [0, 1], score interpretation needs the modlist context. ❌ |
| Cheap | O(|M|) per refresh. ✅ |

**Rejected.** Real MCDA methodology, but the wrong tool for "tell me which mod costs me how many ms".

### 3.5 Pain-function composition

> "Each metric is run through a piecewise-linear *pain* function that is zero below a threshold and accelerates above it; the composite is the sum of pain values."

`pain(x; thresh, slope1, slope2) = 0 if x < thresh; (x - thresh) × slope1 if x < knee; (knee - thresh) × slope1 + (x - knee) × slope2 otherwise`

Defaults motivated by player-perception literature: CPU below 0.5 ms is invisible, above 5 ms it is noticeable; spike contribution accelerates above ~2 ms; allocation pressure begins to bite above ~32 KB/tick.

| Property | Verdict |
|---|---|
| Explainable | "How much each metric *hurts*, run through a 'noticeable threshold', and summed." ⚠ — close to intuitive. |
| Degrades | Skip a term, sum the rest. ✅ |
| Single-mod | Works — pain values are absolute. ✅ |
| Stable | Stable — smoothed inputs, piecewise-linear shape. ✅ |
| Absolute | Yes (pain units). ✅ |
| Cheap | O(|M|) per refresh, three function calls per mod. ✅ |

**Strong candidate.** Subjectively closer to "the right answer" because it captures the perceptual non-linearity: a mod that goes from 0.4 ms to 0.6 ms is still invisible; one that goes from 4 ms to 6 ms is a different beast. The downside is that "pain units" is a unit we invented, with no external referent. The user has to trust the threshold choices.

### 3.6 Spike-weighted ms-equivalent (recommended primary)

> "Express each component in ms-equivalent on the same frame budget. CPU is already ms. Spike is ms-of-spike-pain (sibling produces). Allocation is converted to ms via a calibrated GC-pause heuristic. Composite is their straight sum."

This is **3.1 (frame-budget reduction)** with concrete formulas for each conversion. It is the recommended path. See §4.

---

## 4. Recommendation with rationale

**Primary scoring method: weighted ms-equivalent sum (option 3.1 with concrete conversions).**

```
Composite(m) = w_cpu × CpuMs(m)
             + w_spike × SpikeMs(m)
             + w_alloc × AllocMsEq(m)

Defaults: w_cpu = 1.0, w_spike = 1.0, w_alloc = 1.0
```

Each weight defaults to 1.0 — we are not opinionated about which component is "worse" in the absence of player input. A power user can tune the weights in a config file (see §11), but the default is "equal weighting after each is in ms-equivalent units".

### 4.1 Why this method, against the six properties

| Property | Justification |
|---|---|
| Explainable | "Roughly how many ms of frame budget you lose to this mod, between baseline cost, spike contribution, and GC pressure." One sentence, no jargon. |
| Degrades | If `SpikeTracker` is unavailable, `SpikeMs = 0`. If `AllocationTracker` is unavailable, `AllocMsEq = 0`. The composite collapses to `CpuMs` and the UI greys out the missing bars. No conditional branches in the hot path, no division by zero, no NaN. |
| Single-mod | Sensible — the number is absolute. A modlist of one shows "Calamity: 7.8 ms" rather than "Calamity: rank 1 of 1". |
| Stable | All three inputs are smoothed over the same 30 s window; small per-tick perturbations cannot reshuffle the leaderboard. Hysteresis in the sort (§7) protects against the residual jitter. |
| Absolute | Same unit as the CPU column the user already trusts; colour bands are against absolute thresholds. |
| Cheap | Three multiplies, two adds per mod, run at 1 Hz. For |M| = 100 that is 500 ops/s — negligible. |

### 4.2 Why **store the components, not just the composite**

The single biggest failure mode of any composite is *misleading rank order*. The mitigation is to never hide the decomposition. Concretely:

1. Every Overview row shows the composite **and** all three component bars side by side.
2. Column headers `CPU` / `SPIKE` / `ALLOC` are clickable; clicking re-sorts the leaderboard by that component alone.
3. Hovering a row pops a tooltip with the exact formula evaluation: `7.84 ms = 3.10 (CPU) + 2.84 (SPIKE) + 1.90 (ALLOC × 2.0)`.

The composite is the *recommended starting sort*; the components are the *actual evidence*. This is the same shape BenchmarkDotNet uses (columns, not composites) — we add the composite as one more sortable column rather than replacing the columns.

### 4.3 Secondary view (out of v1 scope, documented)

A `RELATIVE` toggle alongside `ABSOLUTE` (default) that switches the composite from ms-equivalent (option 3.1) to z-score sum (option 3.2). Useful for "which mod stands out *on my particular modlist*" rather than "which mod is objectively expensive". Documented here so the future implementer does not feel they have to invent it.

---

## 5. Component normalisation

Each component arrives in a different unit. §5.1–§5.3 give the conversion to ms-equivalent in concrete form.

### 5.1 CPU normalisation

CPU is already in ms/tick from `MetricCollector.PerModCategoryAverageMs`. No transformation needed.

```csharp
double CpuMs(int modId)
{
    int catCount = PerModAttribution.CategoryCount;
    double sum = 0d;
    int baseIdx = modId * catCount;
    IReadOnlyList<double> src = _collector.PerModCategoryAverageMs;
    for (int c = 0; c < catCount; c++)
    {
        sum += src[baseIdx + c];
    }
    return sum;
}
```

This is the same summation `BuildSortedRows` already performs (`UI/ProfilerOverlay.cs:652-661`). Reuse, don't reinvent.

### 5.2 Spike normalisation

The sibling `spikes-and-allocations-plan.md` is responsible for producing `PerModSpikeImpactMs` already in ms-equivalent units. The Overview reads it as-is.

If that plan deviates and produces, say, `(spikeCount, p95SpikeSize)` raw tuples instead of an ms-equivalent vector, this plan recommends converting at the source via:

```
SpikeMs(m) = (p95SpikeSize(m) - baselineFrameMs) × (spikeCount(m) / windowTicks)
```

In words: "the average amount per tick by which this mod's spikes pushed the frame above baseline". This is the standard "excess area under the spike envelope" formulation used in latency monitoring (e.g. p95 latency contribution attribution in distributed tracing). The Overview tab should not re-implement this — it belongs in the sibling — but the formula is recorded here so the contract is concrete.

### 5.3 Allocation normalisation — the heuristic

This is the hard one. The plan commits to a heuristic with a documented derivation.

**The base heuristic:** the cost of allocating one byte, amortised across all the Gen0 GC pauses that allocation contributes to.

The total cost of all GC pauses in a 30 s window is observable: `Σ (TickFrame.GcTimeMs)` over the ring buffer. The total bytes allocated in the same window is observable: `Σ (PerModBytesPerTick[m]) × windowTicks` summed over all mods. The ratio gives the "ms of pause per byte allocated" on the player's actual machine, in their actual run, in real time:

```
gcMsPerByte = (Σ GcTimeMs in window) / (Σ allocBytes across mods in window)
```

Then:

```
AllocMsEq(m) = PerModBytesPerTick[m] × gcMsPerByte × ticksPerSecond
```

Units: `bytes/tick × ms/byte × ticks/s = ms/s`. To bring it onto the same per-tick basis as CPU, divide by 60 (ticks/second on Terraria's main update loop):

```
AllocMsEq(m) = PerModBytesPerTick[m] × gcMsPerByte
```

(The two `ticks` units cancel cleanly.)

**Why this is the right shape:**

1. **Self-calibrating.** The conversion factor is derived from the player's own session. If their hardware tolerates 10× the allocation rate before triggering GC, `gcMsPerByte` is 10× smaller, and the allocation contribution to the composite scales down accordingly.
2. **Attribution-honest.** The factor distributes total observed GC pause time across mods in proportion to their share of allocations. It cannot over-attribute or under-attribute in aggregate — `Σ AllocMsEq(m)` = total observed GC ms (modulo the per-tick conversion).
3. **Graceful degradation.** If `Σ GcTimeMs` is zero in the window (no GC happened), the factor is zero and the allocation contribution vanishes. The composite collapses to CPU + spikes. No NaN, no divide-by-zero — the code clamps with `Math.Max(totalBytes, 1)` in the denominator.

**Warm-up state.** During the first 30 s of a session, the window is partially filled. The plan handles this by computing `gcMsPerByte` from whatever samples exist (`samples = _history.Count`, mirroring `MetricCollector.UpdateRollingAverage`) and badging the Overview footer with `calibrating — N s of data` until the window is full. Below 5 s, the allocation column is greyed out entirely.

**Fallback constant.** If for any reason no GC events have been observed in the window (a quiet session, no allocations triggered Gen0), the plan uses a fallback constant `FALLBACK_GC_MS_PER_BYTE = 1e-7 ms/byte ≈ 0.1 ms per MB`. This number is drawn from the Microsoft Background GC blog post (link in §0.4): a "small" Gen0 collection on a modern CPU is ~0.5 ms and clears ~5 MB → ~0.1 ms/MB ≈ 1e-7 ms/byte. The constant lives at the top of `ModImpactScorer.cs` with a comment block explaining the derivation.

### 5.4 Honest framing

These conversions are **not physics**. They are heuristics designed to feel right and to be inspectable. The plan makes that explicit in three places:

1. In code: each constant has a comment block citing its source.
2. In the UI footer: `composite = cpu + spike + alloc · alloc-to-ms factor: 8.3e-8 ms/byte (auto-calibrated, 28s)`.
3. In the on-hover tooltip: the exact decomposition for that row.

The user always sees the recipe.

---

## 6. Edge cases and degenerate inputs

| Case | Behaviour | Why |
|---|---|---|
| Modlist with one mod (no relative baseline) | Composite = component sum, no comparison; the row's component bars scale to "this mod's own max", not relative to the leaderboard. | The composite is absolute (ms-equivalent), so it works for N = 1. |
| One mod dominates 10× the rest | Show absolute ms-equivalent **and** "share of total" %. The dominant mod's row has a full red bar; the rest get smaller but visible bars because each row's bar is scaled to the leaderboard max, but the *colour band* is against absolute thresholds. | A user looking at a 10× dominator should still see that the long tail exists; the share-% column tells them at a glance whether to look further. |
| High CPU, zero spikes, zero allocations | Composite = CpuMs. Band based on CpuMs absolute. Row reads "consistently slow". | The composite handles it correctly without special-casing. |
| Low CPU, many spikes | Composite is dominated by `SpikeMs`, even though `CpuMs` is small. The SPIKE component bar is full; CPU bar is short. The row reads "lurks then bites" — exactly the case the composite most needs to surface. | This is the use-case the design exists for. |
| Loaded but inactive mod (zero CPU, zero spikes, zero alloc) | Composite = 0, ranked at the bottom (or hidden by the "concerning mods" filter). | Don't penalise mods for being installed. The Dormant tab is where engagement-vs-cost analysis lives; the Overview is cost-only by design. |
| `SpikeTracker` not loaded (Milestone X not landed yet) | `SpikeMs = 0` for all mods; the SPIKE column header is greyed out and unsortable. Composite collapses to `CpuMs + AllocMsEq`. Footer note: `spike tracking pending`. | Graceful degradation, no conditional logic in the hot path. |
| `AllocationTracker` not loaded | Same as above for allocations. Footer: `allocation tracking pending`. | Graceful degradation. |
| Mod with NaN or infinity in an input (defensive — should never happen) | Clamp to zero before summing; log a single `Warn` per session per mod. | Defensive; never let a bad input poison the leaderboard. |
| First 5 s of a session | All bars greyed out; centred text `calibrating — N samples`. | Smoothing has not converged; an early ranking would mislead. |
| Tab switched to Overview during a save-and-quit | The Overview captures a final composite snapshot for the session log (§10) before the world unloads. | Preserves the "this session's top mods" data for the JSONL retrospective. |

---

## 7. The UI — tab strip

### 7.1 Where the strip lives

Layout, matching the convention established by `events-tab-plan.md` §9.1:

```
 ┌─ PERFORMANCE PROFILER ──────────────────────────────────── 30S AVG ▾  LIVE ▾ ─┐   header (28px)
 │ [ OVERVIEW ]  [ TREE ]  [ EVENTS ]  [ SPIKES ]                                  │   tab strip (22px)
 ├──────────────────────────────────────────────────────────────────────────────────┤
 │ tick stats / profiler health / tab body ...                                       │
```

Constants (added to `OverlayPanel`):

```csharp
private const float TabHeight  = 22f;
private const float TabPaddingX = 12f;
private const float TabSpacing  = 4f;
// RowsTopOffset and DividerOffset are bumped by TabHeight below.
private const float StatStartY      = 12f + TabHeight;   // was 12f
private const float HealthTopOffset = 100f + TabHeight;  // was 100f
private const float DividerOffset   = 148f + TabHeight;  // was 148f
private const float RowsTopOffset   = 172f + TabHeight;  // was 172f
```

The header's existing `30S AVG` / `LIVE` toggles stay where they are. They become "options for the active tab" rather than view selectors (matching the events-tab-plan convention). On the Overview tab, `30S AVG` switches the composite between the smoothed 30 s window (default) and the live EWMA; `LIVE` / `PAUSED` freezes the leaderboard.

### 7.2 The tab abstraction

A new file `UI/ProfilerTab.cs`:

```csharp
internal enum ProfilerTab
{
    Overview = 0,
    Tree = 1,
    Events = 2,
    Spikes = 3,
}

internal static class ProfilerTabLabels
{
    public static readonly string[] Names = { "OVERVIEW", "TREE", "EVENTS", "SPIKES" };
}
```

`OverlayPanel` gains:

```csharp
private ProfilerTab _activeTab = ProfilerTab.Overview;   // default landing
private static ProfilerTab _persistedTab = ProfilerTab.Overview; // survives F9 close/open within a session
```

`_persistedTab` is static so closing and reopening F9 returns to the last tab the user was on, matching the user's mental model ("F9 brings me back to where I was"). It resets to `Overview` on world unload — see §7.5.

### 7.3 Visual style

Pill style (rounded-rect background fill) is wrong — `ProfilerTheme` is angular and flat. The right shape is an **underline accent on the active tab**, matching how the header strip uses a 2 px accent bar (`ProfilerTheme.Accent` at `area.X, area.Y, 2, HeaderHeight`).

```
 [ OVERVIEW ]  [ TREE ]  [ EVENTS ]  [ SPIKES ]
 ▔▔▔▔▔▔▔▔▔▔▔
```

The active tab text is `ProfilerTheme.Accent` (light blue); inactive is `ProfilerTheme.TextMuted`; hover is `ProfilerTheme.Text` plus `ProfilerTheme.RowHover` fill behind the label. The 2 px underline uses `ProfilerTheme.Accent`.

### 7.4 Click dispatch

`OverlayPanel.LeftMouseDown` partitions panel-local Y as:

```
 0          .. HeaderHeight                       → existing header (toggles + drag)
 HeaderHeight .. HeaderHeight + TabHeight        → tab strip (new)
 HeaderHeight + TabHeight .. rest                → active tab's body
```

The new branch:

```csharp
if (localY > HeaderHeight && localY <= HeaderHeight + TabHeight)
{
    ProfilerTab? hit = HitTestTab(localX);
    if (hit.HasValue)
    {
        _activeTab = hit.Value;
        _persistedTab = hit.Value;
        _scrollOffset = 0;        // each tab starts at the top
        _expanded.Clear();        // tree-only state, safe to drop
        _expandedCats.Clear();
    }
    return;
}
```

Tab widths are measured per-frame using `FontAssets.MouseText.Value.MeasureString(label).X * 0.7f + 2 * TabPaddingX` so adding a new tab is a one-line `Names[]` addition.

### 7.5 Lifecycle

- F9 close/reopen within a session: tab persists via the static `_persistedTab`.
- World unload (`ProfilerOverlaySystem.OnWorldUnload`): `_persistedTab = ProfilerTab.Overview` so each new session starts on the front page.
- Mod reload (`Mod.Unload` → `Mod.Load`): static field resets naturally because the assembly is unloaded.

### 7.6 Keyboard shortcuts (optional, low-priority)

`Tab` (in-overlay) cycles forward, `Shift+Tab` cycles back. Wire through `OverlayPanel.Update` against `Main.keyState`. Documented but not required for v1 — the user can ship without it.

---

## 8. The Overview tab content

### 8.1 Visual mock

Full panel mock, 640 px wide, with the user's actual modlist scale (~18 mods shown):

```
 F9 ┌─ PERFORMANCE PROFILER ──────────────────────────────────── 30S AVG ▾  LIVE ▾ ─┐
    │ [ OVERVIEW ]  [ TREE ]  [ EVENTS ]  [ SPIKES ]                                 │
    │ ▔▔▔▔▔▔▔▔▔▔▔                                                                    │
    ├──────────────────────────────────────────────────────────────────────────────┤
    │  tick 23.4 ms     avg 30s 24.1 ms     uptime 02:17:43     90 mods             │
    │  gc 2.1 ms        npc 184  proj 412   alloc 184 KB/s                          │
    │  tick #490,820                                                                │
    │  ── PROFILER HEALTH ─────────────────────────────────────────────────────────  │
    │  hooks 8 432/8 432 (100%)            full 90 partial 0           backend: ilhook│
    │  ████████████████████████████████████████████████████████████████████████      │
    │  ── IMPACT LEADERBOARD   ·   sort by:  composite ▾ cpu  spike  alloc ────── │
    │  ░ filter: hide < 0.5 ms              colour:  green ≤1   amber ≤4   red >4 │
    │                                                                              │
    │  ▾ Calamity Mod              ████████████████████░░░░░░░░░░░░  7.84 ms  32%  │
    │      cpu   ████████░░░░░░░░░░░░ 3.10 ms                                      │
    │      spike ███████░░░░░░░░░░░░  2.84 ms                                      │
    │      alloc █████░░░░░░░░░░░░░░  1.90 ms  (228 KB/tick × 8.3e-8)              │
    │  ▾ Fargo's Souls Mod         ████████████░░░░░░░░░░░░░░░░░░░  4.21 ms  17%  │
    │      cpu   ██████░░░░░░░░░░░░░░ 2.10 ms                                      │
    │      spike ████░░░░░░░░░░░░░░░░ 1.45 ms                                      │
    │      alloc ██░░░░░░░░░░░░░░░░░░ 0.66 ms                                      │
    │  ▸ Spirit Reforged           ██████░░░░░░░░░░░░░░░░░░░░░░░░░  2.10 ms   9%  │
    │  ▸ Runeterran Accessories    █████░░░░░░░░░░░░░░░░░░░░░░░░░░  1.94 ms   8%  │
    │  ▸ Thorium Mod               ████░░░░░░░░░░░░░░░░░░░░░░░░░░░  1.62 ms   7%  │
    │  ▸ Magic Storage             ███░░░░░░░░░░░░░░░░░░░░░░░░░░░░  1.08 ms   4%  │
    │  ▸ Wing Slot                 █░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  0.62 ms   3%  │
    │  ▸ Recipe Browser            ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  0.32 ms   1%  │
    │  + 10 more hidden (< 0.5 ms threshold)                                       │
    │  ── alloc-to-ms factor: 8.3e-8 ms/byte (auto-calibrated, 28s)                 │
    └──────────────────────────────────────────────────────────────────────────────┘
```

Key features visible above:

- **Top stats block** is the same one the existing tree has, minus a stat tweak: the `tick #` line collapses to a smaller font row, and a new "90 mods" / "alloc 184 KB/s" pair surfaces session-wide context.
- **Profiler health bar** is unchanged (verbatim reuse of `DrawProfilerHealth`).
- **Impact leaderboard** is the new content. Each row shows mod name, composite bar, composite value in ms, and share-of-total %. Expand (`▾`) reveals the three component sub-bars with their own values and the allocation row's full decomposition `(228 KB/tick × 8.3e-8)`.
- **Filter chip** `hide < 0.5 ms` is the "concerning mods" filter — toggleable; default ON for any session past warm-up. The threshold is `MIN_INTERESTING_MS = 0.5`.
- **Sort chip** lets the user re-sort by component; default is `composite`. Sort headers persist across tab switches via a static.
- **Footer** surfaces the auto-calibrated allocation factor — the user can see and trust the conversion.

### 8.2 Row layout, in pixels

Each leaderboard row is 18 px tall when collapsed (matching the existing tree's `RowHeight = 18f`). The bar starts at `panelX + 256` and is 240 px wide (longer than the tree's 170 px because the Overview row is denser). Composite value at `panelX + 504`, share-% at `panelX + 568`.

When expanded, three 14 px sub-rows are inserted under the row, each with its own bar. Bar fills use `ProfilerTheme.CostColor(fraction)` against the row's max (not the leaderboard max — this keeps the sub-bar comparisons internally meaningful for that mod). The allocation sub-row gets an extra `(NNN KB/tick × X.Xe-Y)` annotation showing the heuristic in action.

### 8.3 Color grading — absolute thresholds

The user explicitly wants the colour to mean "objectively fine" rather than "fine relative to other mods". Thresholds:

```csharp
internal static class ImpactBands
{
    public const double GreenMaxMs = 1.0;   // < 1 ms ms-equivalent: invisible to the player
    public const double AmberMaxMs = 4.0;   // 1–4 ms: noticeable on a 60fps target
    // > 4 ms: actively painful (≥ 4/16.67 ≈ 24% of one frame)

    public static ImpactBand Classify(double composite)
        => composite < GreenMaxMs ? ImpactBand.Green
         : composite < AmberMaxMs ? ImpactBand.Amber
         : ImpactBand.Red;
}
```

These thresholds map to the existing `ProfilerTheme.Good / Amber / Danger` palette without any new colour additions.

### 8.4 Drill-into-Tree

Single-click on a row toggles expansion (the sub-bars). **Double-click** switches to the `TREE` tab pre-expanded on that mod. The existing `OverlayPanel._expanded` set is the destination — set it to contain only that mod ID, scroll the tree to the row, and switch tabs:

```csharp
private void DrillIntoTree(int modId)
{
    _activeTab = ProfilerTab.Tree;
    _persistedTab = ProfilerTab.Tree;
    _expanded.Clear();
    _expanded.Add(modId);
    _scrollOffset = ScrollOffsetForMod(modId);
}
```

`ScrollOffsetForMod` walks `_rows` (built by the existing tree code) and returns the offset that brings the mod into view. It is a five-line helper.

### 8.5 Sort controls

The sort header strip above the rows is a single 14 px-tall area with four labelled chips: `composite ▾`, `cpu`, `spike`, `alloc`. The `▾` glyph marks the active sort. Clicking a chip:

```csharp
private void SetSortMode(SortMode mode)
{
    if (_sortMode == mode) _sortDescending = !_sortDescending; // toggle direction
    else { _sortMode = mode; _sortDescending = true; }
    // Defer the actual sort until the next ApplyImpact() call.
    _impactDirty = true;
}
```

`SortMode` is a four-value enum (`Composite / Cpu / Spike / Alloc`). Default direction is descending (worst first) because that is the question the user is asking. Both `_sortMode` and `_sortDescending` persist via static fields so the choice survives F9 toggle.

### 8.6 "Concerning mods" filter

A toggle chip `hide < 0.5 ms` defaults to ON. When ON, rows with `composite < MIN_INTERESTING_MS` collapse into a `+ N more hidden (< 0.5 ms threshold)` summary row at the bottom of the leaderboard. Clicking the summary row expands it (sets the filter OFF, scrolls to keep the visible mods in place). This is symmetric with the existing tree's `+ N more` pattern at `UI/ProfilerOverlay.cs:539-541`.

---

## 9. Data flow

```
 ┌──────────────────────┐         ┌───────────────────┐
 │ MetricCollector       │         │ SpikeTracker      │
 │ PerModCategoryAverageMs│        │ PerModSpikeImpactMs│
 └──────────┬───────────┘         └──────────┬────────┘
            │                                │
            │     ┌────────────────────────┐ │
            │     │ AllocationTracker      │ │
            │     │ PerModBytesPerTick     │ │
            │     └─────────────┬──────────┘ │
            │                   │            │
            ▼                   ▼            ▼
       ┌─────────────────────────────────────────┐
       │ ModImpactScorer                          │
       │                                          │
       │   ComputeAll() at 1 Hz                   │
       │   - reads three vectors                  │
       │   - applies pain / conversion functions  │
       │   - emits ModImpact[] sorted by composite│
       └─────────────┬───────────────────────────┘
                     │
                     ▼
            ┌──────────────────┐
            │ OverlayPanel     │
            │ DrawOverviewBody │
            └──────────────────┘
```

### 9.1 The scorer

A new file `Profiling/ModImpactScorer.cs`:

```csharp
public sealed class ModImpactScorer
{
    private readonly MetricCollector _collector;
    private readonly SpikeTracker? _spikes;
    private readonly AllocationTracker? _alloc;

    private readonly ModImpact[] _impactsByMod;  // pre-allocated, indexed by modId
    private readonly ModImpact[] _sorted;        // pre-allocated, sorted view

    private double _gcMsPerByte;
    private long _lastComputeTick = -1L;
    private const long RecomputeIntervalTicks = 60;  // 1 Hz

    public ModImpactScorer(MetricCollector collector,
                           SpikeTracker? spikes,
                           AllocationTracker? alloc,
                           int modCount)
    {
        _collector = collector;
        _spikes = spikes;
        _alloc = alloc;
        _impactsByMod = new ModImpact[modCount];
        _sorted = new ModImpact[modCount];
    }

    public IReadOnlyList<ModImpact> Sorted => _sorted;
    public double GcMsPerByte => _gcMsPerByte;
    public bool   IsCalibrated => _collector.History.Count >= 30; // 0.5s warm-up

    public void MaybeRecompute(long currentTick, SortMode mode, bool descending)
    {
        if (currentTick - _lastComputeTick < RecomputeIntervalTicks) return;
        _lastComputeTick = currentTick;

        UpdateGcCalibration();
        ComputeImpacts();
        Sort(mode, descending);
    }

    // Implementation of UpdateGcCalibration, ComputeImpacts, Sort follow §5.3.
}
```

Pre-allocated `_impactsByMod` and `_sorted` arrays mean a recompute is allocation-free — important to be a polite citizen of the player's frame budget even though we run at 1 Hz.

### 9.2 Ownership

`ProfilerSystem` (existing `ModSystem`) owns the new scorer alongside the existing `MetricCollector`. The scorer's `MaybeRecompute` is called from `OverlayPanel.Update` when the Overview tab is active, gated by `_activeTab == ProfilerTab.Overview`. Off-tab, the scorer never runs — zero overhead.

### 9.3 Invalidation

The scorer invalidates and recomputes when:
- 60 ticks have elapsed since the last compute (1 Hz cadence).
- The user clicks a sort header (`_impactDirty = true`).
- The user toggles `30S AVG` ↔ `NOW` in the header (forces a recompute next frame).

Switching tabs does **not** invalidate — the cached `_sorted` is reused when the user returns to Overview.

---

## 10. Session log integration

The session retrospective (Milestone 3) is the natural home for the aggregated Overview as a frozen snapshot. The plan adds three new top-level JSONL fields to the session-end summary.

### 10.1 Schema additions

```json
{
  "schema": 4,
  "...": "...",
  "impactLeaderboard": [
    {
      "modId": 7,
      "modName": "Calamity Mod",
      "modVersion": "2.0.5",
      "composite": 7.84,
      "cpu": 3.10,
      "spike": 2.84,
      "allocBytesPerTick": 22800000,
      "allocMsEq": 1.90,
      "shareOfTotal": 0.321,
      "band": "Amber"
    }
    /* ... one entry per mod, sorted descending by composite ... */
  ],
  "impactCalibration": {
    "gcMsPerByte": 8.3e-8,
    "calibrationSamplesTicks": 1800,
    "fallbackUsed": false
  },
  "impactRankStability": {
    "rankChangesPerMinute": 0.4,
    "topFiveStable": true
  }
}
```

### 10.2 Rank-stability tracking

A small ring of the top-5 modIds is maintained over the session (one snapshot per minute, capacity 120 entries ≈ 2 h). `rankChangesPerMinute` is the average Levenshtein distance between consecutive minute snapshots, divided by minutes elapsed. `topFiveStable` is `true` iff the top-5 set (order-insensitive) has not changed in the last 10 minutes. This is the "was the leaderboard volatile or stable?" signal the user asked for.

### 10.3 Schema version bump

`SchemaVersion` bumps from whatever the current value is (verify in `SessionLogWriter`) to N+1, propagating through the session fingerprint hash exactly as `ILHook-migration-plan.md §8` describes for `HookCoverageVersion`. Old session files become hidden, not deleted.

### 10.4 Retrospective card hooks

The end-of-session retrospective card (the "screenshot-shareable" surface from README §"A session, end to end") gains a `COST PODIUM` section that is literally the top-3 entries of `impactLeaderboard`. The card layout already accommodates a 3-row podium; this plan does not change its visual shape, only its data source.

---

## 11. Step-by-step implementation sequence

Implementation runs in two passes per the project's task-decomposition discipline: a discovery pass (steps 1–2) and an execution pass (steps 3–12).

| # | Action | File(s) | Verify | Risk |
|---|---|---|---|---|
| **1** | Re-read `UI/ProfilerOverlay.cs` cover-to-cover. Enumerate every constant the tab-strip insertion will shift (`StatStartY`, `HealthTopOffset`, `DividerOffset`, `RowsTopOffset`, `CollapsedHeight`). Confirm no other file references those constants directly. | read-only | Scratch list of bumped constants matches §7.1. | **Low** — read-only. |
| **2** | Grep for every reference to `_activeTab` / "tab" in the existing repo to confirm no prior tab work has begun. Read `events-tab-plan.md §9` to confirm tab-strip pixel layout matches. | grep | Zero prior tab references; events plan's tab strip is at `[HeaderHeight, HeaderHeight + 22)` matching this plan. | **Low**. |
| **3** | Add `UI/ProfilerTab.cs` with the enum and labels per §7.2. No references yet. Compile. | new file | `dotnet msbuild` succeeds. | **Low** — isolated. |
| **4** | Add `Profiling/ModImpactScorer.cs` skeleton per §9.1, with CPU-only mode (no spike/alloc readers required). Pure logic; no UI hookup yet. Add `Profiling/ModImpact.cs` and `Profiling/SortMode.cs`. | new files | `dotnet msbuild` succeeds. Unit-testable shape: a method `ComputeImpacts(IReadOnlyList<double> categoryMs)` that returns a `ModImpact[]`. | **Low** — pure logic, unit-testable per CLAUDE.md. |
| **5** | Wire `ProfilerSystem` to construct the scorer on `PostSetupContent` after `HookInterceptor.Install`. Pass `null` for the spike and alloc trackers (they don't exist yet). | `Profiling/ProfilerSystem.cs` | `dotnet msbuild` succeeds; in-game, scorer exists but no UI reads it. | **Low**. |
| **6** | Refactor `OverlayPanel.DrawSelf` to extract `DrawTreeBody` from the current logic. The current draw becomes a dispatch on `_activeTab`. **Default `_activeTab` is still `ProfilerTab.Tree`** at this step — we are preserving today's behaviour. | `UI/ProfilerOverlay.cs` (refactor only, no new tabs visible) | In-game, the overlay looks identical to before the refactor. Screenshot-compare. | **High** — biggest single change in the UI surface; restrict refactor to extraction. |
| **7** | Add the tab strip drawing and click dispatch per §7. With only `OVERVIEW` and `TREE` enabled (the Events / Spikes tabs are placeholders). Clicking `OVERVIEW` shows a centred `(overview coming next)` placeholder. | `UI/ProfilerOverlay.cs` | In-game, two tabs visible. Tree tab unchanged. Overview tab shows placeholder. Click-to-switch works. F9 close/reopen returns to last tab. | **Medium** — pixel layout interactions with the existing toggles. |
| **8** | Implement `DrawOverviewBody` per §8. CPU-only mode: composite = CpuMs, spike and alloc bars greyed out with `pending` annotation. Filter, sort headers, expand/collapse, drill-into-tree wired. | `UI/ProfilerOverlay.cs` (~280 lines added) | In-game, Overview leaderboard renders with the CPU-only composite. Sorting works. Drill-into-tree works. | **Medium**. |
| **9** | Wire the calibration footer and warm-up greying. `gcMsPerByte` reads zero in CPU-only mode; the footer reads `allocation tracking: pending`. | `UI/ProfilerOverlay.cs` | In-game, footer is present and informative. | **Low**. |
| **10** | Switch the **default landing tab** to `ProfilerTab.Overview`. `_persistedTab = ProfilerTab.Overview`. F9 now opens to the Overview as the front page. | `UI/ProfilerOverlay.cs` (one-line change) | In-game, fresh F9 opens to Overview. | **Low**. |
| **11** | Add the session-log fields per §10. Bump `SchemaVersion`. Prior session files become hidden. | `Profiling/SessionLogWriter.cs` | In-game, end a session, inspect the JSONL. Confirm the new fields are present. | **Medium** — schema migration affects existing session view. |
| **12** | Once the sibling `spikes-and-allocations-plan.md` is implemented, swap the scorer's `null` arguments for the real trackers. The Overview's pending bars light up. | `Profiling/ProfilerSystem.cs`, `UI/ProfilerOverlay.cs` (small tweaks) | In-game, all three component bars now have data. The auto-calibration footer shows a real `gcMsPerByte`. | **Medium** — depends on the sibling plan landing. |
| **13** | Write `context/notes/overview-tab.md` capturing the implementation decisions, the scoring formula, and the calibration approach. | `context/notes/` | User reviews and accepts. | **Low** — documentation. |
| **14** | Commit at logical checkpoints: (a) tab abstraction + scorer skeleton (steps 3–5), (b) tab strip refactor (steps 6–7), (c) Overview body CPU-only (steps 8–10), (d) session log (step 11), (e) full integration with spikes/alloc (step 12), (f) context note (step 13). | git | Each commit builds and the existing tree is visually unchanged after every commit until step 10. | **Low** — discipline. |

Steps 1–11 are independently shippable — at any point in the sequence, the user can switch to the Overview tab and see a functional (if reduced) leaderboard. The sibling plan's outputs in step 12 are an additive enhancement, not a blocker.

---

## 12. Honest risk register

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| **R-1** | Ranking volatility despite smoothing — top-5 reshuffles every refresh | Medium | High (unreadable UI) | (a) 1 Hz refresh; (b) inputs already smoothed over 30 s; (c) hysteresis: a rank swap requires the composite delta to exceed 3 % of the leader's composite. The hysteresis state is one extra `double[]` (last shown composites) per redraw. |
| **R-2** | Composite misleads — a mod ranks low on composite but is actually the worst on the metric the user cares about | Medium | Medium | Always show all three component bars; clickable column headers. The composite is presented as "suggested starting sort", not "the answer". |
| **R-3** | `gcMsPerByte` calibration is wrong by an order of magnitude (e.g. session has unusual Gen2 GC behaviour) | Low–Medium | Medium | (a) Footer surfaces the constant; (b) fallback constant available if observed window is empty; (c) the constant is a public read-only on `ModImpactScorer` so tests can inject. |
| **R-4** | First-30s noisy scores mislead users | Medium | Low | "Calibrating — N s" badge; allocation column greyed below 5 s. |
| **R-5** | Per-tick overhead of the scorer | Low | Low | Recompute at 1 Hz; pre-allocated buffers; off-tab the scorer never runs. Measured overhead expected: < 0.01 % of frame budget. |
| **R-6** | Sibling plan doesn't ship in time | Medium | Low | CPU-only fallback is the default during the gap; the Overview is useful from step 10 onwards. |
| **R-7** | The tab-strip refactor (step 6) regresses the existing tree | Medium | High | Step 6 acceptance criterion: tree tab is visually identical to before, screenshot-comparable. Restrict refactor scope to `DrawSelf` → `DrawTreeBody` extraction. |
| **R-8** | Schema-version bump invalidates a user's prior session files | Certain | Low | Prior files are *hidden*, not deleted (matches the existing `HookCoverageVersion` policy from `ILHook-migration-plan.md §8`). |
| **R-9** | Hover-tooltip text overflows the panel width | Low | Low | Composite tooltip is one line at small font; panel width is 640 px; tested mock fits in ~480 px. |
| **R-10** | Sort header chips intercept clicks the user meant for a row | Low | Low | Chips live in a 14 px strip above the rows; the existing `HitTestRows` Y partition extends naturally. |

---

## 13. Honest gaps

The Overview tab **does not solve**:

| Question | Why not |
|---|---|
| "If I remove Mod X, my framerate will rise from Y to Z" | The mod's cost is its CPU + spike + alloc footprint, but some of that cost is *interaction* with other mods. Removing X may also reduce Y's cost (because Y stops processing X's items). A projection requires a counterfactual model that the profiler explicitly does not have. Out of scope. |
| "Mod A and Mod B together cost more than the sum of A + B" — mod-pair synergies | Full counterfactual analysis. The profiler measures the present state; it cannot rerun history with a different modlist. Documented as a Milestone 4+ research question. |
| Multiplayer / server-side impact ranking | Different runtime, different metrics (network frame time, sync cost), different scoring. v2 question. |
| Per-encounter Overview drilldown (e.g. "show me the leaderboard *during the Cryogen fight*") | Achievable by re-walking the ring buffer with an encounter filter, but interacts with the Events tab's encounter detection. Cross-tab feature, scoped to v2. |
| Engagement-weighted scoring (cost ÷ engagement, the Dormant tab's signal) | Different tab, different question. The Dormant tab is the right home for "this mod costs X with zero engagement"; the Overview is cost-only by design (matches the README distinction). |
| Lifetime composite trends ("Calamity's composite has trended up across sessions") | Requires persistent storage; lands when the session-log integration of §10 is wired into the cross-session view. Not in v1. |
| Mod-internal subsystem-level impact ("Calamity's afterimage system is the worst part") | The Tree tab is the right home — the Overview's drill-into-tree action is the bridge. |

These are surfaced in the tab's footer link cluster (`→ TREE for subsystem detail · → DORMANT for engagement-weighted scoring · → EVENTS for context-specific costs`) so the user always knows where the question they are asking has a home.

---

## 14. What does and does not change in the codebase

| File | Change |
|---|---|
| `UI/ProfilerTab.cs` | **New.** `ProfilerTab` enum and label table. |
| `UI/ProfilerOverlay.cs` | **Major refactor.** Tab strip insertion, `DrawSelf` dispatch, new `DrawOverviewBody`, new sort/filter state, new pixel constants per §7.1. Extract `DrawTreeBody` from existing draw logic. |
| `Profiling/ModImpactScorer.cs` | **New.** Composite scoring, GC calibration, sort dispatcher. ~250 lines. |
| `Profiling/ModImpact.cs` | **New.** Result struct. |
| `Profiling/SortMode.cs` | **New.** Enum + helper. |
| `Profiling/ProfilerSystem.cs` | **Add** scorer construction on `PostSetupContent`. |
| `Profiling/SessionLogWriter.cs` | **Extend** with the three new top-level fields per §10. Bump `SchemaVersion`. |
| `Profiling/MetricCollector.cs` | **No change.** The scorer is a consumer. |
| `Profiling/PerModAttribution.cs` | **No change.** |
| `Profiling/RingBuffer.cs`, `TickFrame.cs`, `PerModSample.cs` | **No change.** |
| `Profiling/HookInterceptor.cs`, `ILHookInterceptor.cs` | **No change.** |
| `Profiling/SpikeTracker.cs` | **Not in this plan.** Sibling plan (`spikes-and-allocations-plan.md`). |
| `Profiling/AllocationTracker.cs` | **Not in this plan.** Sibling. |
| `UI/ProfilerTheme.cs` | **No change.** Reuse all existing colours; absolute thresholds map to `Good`/`Amber`/`Danger`. |
| `UI/ProfilerOverlaySystem.cs` | **Add** a one-liner in `OnWorldUnload` to reset `_persistedTab` to `Overview`. |
| `PerformanceProfiler.cs` | **No change.** |
| `build.txt` | **No change.** |
| `context/_Overview.md` | **Recommended edit** after landing — describe the tab strip and the Overview as the front page. Confirm with user. |
| `context/notes/overview-tab.md` | **New note** after the change lands. Implementation capture. |

---

## 15. Testing strategy

Testing splits into four layers, matching the four invariants and the dual-surface observability contract.

### 15a. Scorer unit tests (pure logic)

The `ModImpactScorer` is pure logic; it takes vectors in, returns vectors out, no game runtime. Unit-testable per CLAUDE.md.

**Hypothesis:** the composite formula is symmetric, monotonic, and degrades gracefully on missing inputs.

**Tests:**
1. Two mods with identical inputs → identical composites; identical bands.
2. Doubling CPU on mod A → A's composite rises by exactly `w_cpu × cpuDelta`.
3. Setting `_spikes = null` → all `SpikeMs` are 0; composite = CpuMs + AllocMsEq.
4. Setting `_alloc = null` → all `AllocMsEq` are 0; composite = CpuMs + SpikeMs.
5. Single-mod list → sorted order is `[mod0]`; composite = its component sum.
6. NaN in input → clamped to 0; one `Warn` logged.
7. Empty history (no GC observed) → `gcMsPerByte = FALLBACK_GC_MS_PER_BYTE`.

**Pass:** all six tests pass; no allocations during `ComputeImpacts` (verify with `GC.GetAllocatedBytesForCurrentThread` deltas).

### 15b. Visual regression (the tree tab is unchanged)

**Hypothesis:** after step 6 (the refactor), the Tree tab is pixel-identical to before.

**Steps:** screenshot the overlay pre-refactor and post-refactor on the same world, same modlist, same paused tick. Compare side-by-side.

**Pass:** zero visual delta in the tree body. The tab strip is the only addition.

### 15c. Rank stability under perturbation

**Hypothesis:** small input perturbations (1–3 % wobble) do not reshuffle the top-5 leaderboard within a single recompute cycle.

**Steps:**
1. Capture a `ModImpact[]` snapshot during a paused session.
2. Perturb each mod's CPU by ± 2 % uniformly at random.
3. Recompute. Confirm the top-5 set is unchanged.
4. Repeat 100 times.

**Pass:** ≥ 95 of 100 runs preserve the top-5 set (order-insensitive). Hysteresis from §12 R-1 makes this stronger.

### 15d. Cadence and overhead

**Hypothesis:** the scorer's 1 Hz recompute does not measurably affect frame time.

**Steps:** with Overview tab active, capture 60 s of frame-time samples. Switch to Tree tab, capture 60 s. Compute mean and p99 frame times.

**Pass:** mean and p99 within 0.5 % between the two tabs.

### 15e. Calibration convergence

**Hypothesis:** within 30 s of session start, `gcMsPerByte` converges to within 20 % of the eventual long-session value.

**Steps:** log `gcMsPerByte` once per second over a 5-minute session. Compute the value at 30 s, 60 s, 5 min. Confirm 30 s value is within 20 % of 5 min value.

**Pass:** convergence within tolerance. Failures indicate either insufficient GC events in the warm-up window (acceptable, fallback handles it) or a calibration bug.

### 15f. In-game smoke test (per CLAUDE.md operating loop)

A 10-minute session on a Calamity + Thorium modlist with at least one boss fight. Acceptance:

- F9 opens to Overview as the front page.
- Leaderboard populates within 5 s, gradually refines as the 30 s window fills.
- Sort chips work; sort persists across F9 toggle.
- Filter chip toggles correctly.
- Double-click drills into Tree tab pre-expanded on that mod.
- Tab strip click-cycle hits all four tabs without flicker.
- Session-end JSONL contains the three new top-level fields with sensible values.
- `client.log` shows the scorer's construction line and no `Warn`/`Error`.
- Overhead measured against a Tree-tab-only baseline: < 1 % delta.

### 15g. Failure-mode triage

| Symptom | Likely cause | First check |
|---|---|---|
| Leaderboard reshuffles every second | Hysteresis not applied or threshold too low | `_hysteresisThreshold`; `_previousComposites` array contents |
| All allocation bars greyed out indefinitely | `AllocationTracker` not wired, or `Σ allocBytes` always zero | Probe `_alloc?.PerModBytesPerTick` non-null and non-zero |
| `gcMsPerByte` is `Infinity` or `NaN` | Divide-by-zero in calibration | Confirm `Math.Max(totalBytes, 1)` guard is in place |
| Composite ranks visibly wrong vs the visible component bars | Sort mode not honouring direction | `_sortDescending` flag; reverse comparison |
| Overview tab "hangs" the overlay | Scorer running on the UI thread synchronously every frame | Verify `MaybeRecompute` cadence guard `currentTick - _lastComputeTick < RecomputeIntervalTicks` |
| Pixel layout broken after tab strip | Constants in §7.1 not all bumped | Sweep `RowsTopOffset`, `DividerOffset`, `HealthTopOffset`, `StatStartY`, `CollapsedHeight` |

---

## 16. Rollback plan

The feature is additive. Steps 3–13 each leave the repo in a runnable state.

### Coexistence design

Up to step 9, `_activeTab` defaults to `ProfilerTab.Tree`. The Overview tab exists but is not the front page. If a regression surfaces in steps 6–9, the user can `git revert` the affected commit and the overlay returns to its pre-tab-strip form (one further revert needed for step 6).

### Minimal rollback (post-step 10)

If the Overview-as-front-page is a regression in player perception but the underlying mechanism is sound:
1. `git revert` step 10 — `_persistedTab` reverts to `ProfilerTab.Tree`.
2. Build, reload. F9 opens to Tree again; Overview is reachable via the tab strip.

### Catastrophic rollback

If the whole tab system is broken:
1. `git revert` the feature branch.
2. `SchemaVersion` returns to its pre-bump value (the bump is in step 11 → reverting step 11 alone reverts the schema).
3. Net effect: identical to never having shipped the feature.

There is no permanent state on disk that the Overview writes outside the JSONL schema, so rollback is always clean — same property `ILHook-migration-plan.md §11` notes.

---

## Honest summary

The Overview tab is mostly a UI problem with one hard sub-problem: a defensible composite that fuses three units with different psychoacoustic signatures (sustained CPU pain, spike pain, allocation pain). The plan commits to weighted ms-equivalent summation (option 3.1) as the primary method, with each component shown beside the composite, clickable column-sort, and absolute colour bands so a green row means "objectively fine" — exactly the contract the user asked for.

The hardest piece of evidence — the allocation→ms conversion — is honest about being a heuristic. The plan picks **self-calibration from observed GC pause events** rather than a hardcoded constant, falls back to a documented `1e-7 ms/byte` constant on quiet sessions, and surfaces the live conversion factor in the overlay footer so the user can always see and trust the recipe.

The tab strip extension lands cleanly over the events-tab-plan's already-designed strip; adding `OVERVIEW` is a one-line `Names[]` addition plus the dispatch wiring. Default landing tab becomes `OVERVIEW`; the existing per-mod tree becomes `TREE` and remains pixel-identical after the refactor (step-6 acceptance criterion).

The largest remaining honest uncertainty is the composite's *perceptual validity* — does ranking by `cpu + spike + alloc` actually match what a player feels as "this mod is the problem"? We cannot prove it without playtesting. The mitigation is the show-all-components / sort-per-column design: even if the composite is subtly miscalibrated, the user can always click `spike ▾` or `alloc ▾` and read the true ordering of that axis. The composite is the front door; the components are the truth.
