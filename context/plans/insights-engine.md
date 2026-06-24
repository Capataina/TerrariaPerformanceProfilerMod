# Plan — The Insights Engine (one module, the interpretation layer)

> Goal: do for **interpretation** what v0.10 to v0.12 did for **data**. The data
> pipeline consolidated every number into one place (`DataRegistry.Shared`):
> producers register, consumers look up by name, nobody re-derives. This plan
> does the same for insights. Today interpretation is scattered across
> `Data/Detectors/Insights/`, `Data/Stats/`, `Data/Aggregators/`, and inline in
> the dashboard router, with the same formulas duplicated up to eight times.
> Consolidate all of it into a single top-level **`Insights/`** module: it
> consumes processed pipeline snapshots, produces a canonical `Insight`, and
> publishes back through the same registry so every consumer (dashboard, session
> log, retrospective, future overlay) reads interpretation the one way it reads
> data.
>
> This is **not** a flat mirror of the data pipeline. Data is a simpler workflow;
> insights earn richer internal modularisation (reference frames, drivers,
> detectors-by-family, shared primitives, rendering). The discipline borrowed
> from the pipeline is the *single home and single export seam*, not the flat
> folder shape. The relativity law generalised in this plan ("relative to a
> reference frame, never absolute") is the spine, and the five insight families
> are the taxonomy the module is built to express.
>
> Status: **PROPOSED — not started.** Directional, kept current as waves land.
> This is consolidate-and-generalise, not greenfield: the engine core already
> exists and is sound; the work is pulling the scattered interpretation in,
> generalising the substrate, and lighting up the detectors that were designed
> but starved.
>
> Date opened: 2026-06-24. Mod version at open: `0.18.1`.

---

## Why this exists

Two forces, one shape.

**1. The duplication is real and measurable.** The same interpretation logic is
re-implemented across the stats, because each "insight" stat was hand-coded
independently. This is the exact bespoke-drift disease the component library
fought on the UI side, one layer down in the logic.

| Duplicated logic | Where it recurs | Canonical home it wants |
|---|---|---|
| Usage weight `ItemsCreated + NpcsSpawned + BossesFought + BuffsApplied (+InvasionsFought)` | `DormantSurfaceStat.cs:68`, `ModObservatoryStat.cs:110`, `EngagementCostScatterStat.cs:85`, `PerModUsageAggregator.cs:298` (variants disagree on InvasionsFought) | `Insights/Shared/ModMetrics.UsageWeight()` |
| Roster size `Items + NPCs + Buffs + Projectiles + Mounts + Accessories + Invasions + Bosses` | `DormantSurfaceStat.cs:65`, `EngagementCostScatterStat.cs:101` | `ModMetrics.RosterSize()` |
| Share-of-total `total > 0 ? value/total : 0` | 8+ sites across `ModObservatoryStat`, `EngagementCostScatterStat`, `DormantSurfaceStat`, `AllocationCausalityStat`, `PerSegmentLagDensityStat`, `KpiCalculator` | `Shares.SafeShare()` |
| Sort + top-N truncate | 8 sites (Sort/RemoveRange and LINQ variants) | `Shares.TopN()` |
| Per-mod category fold `for m: baseIdx = m*catCount; for c: sum` | `ModObservatoryStat.cs:72`, `EngagementCostScatterStat.cs:51`, `DashboardRouter.Mods.cs:88`, `DashboardRouter.Memory.cs:74` | `ModMetrics.PerModFold()` |
| ModId bounds check (two styles) | `ModObservatoryStat`, `EngagementCostScatterStat`, lag aggregators, router | `ModNames.SafeName()` |

The Flute-reads-zero bug from `context/notes/future-insights-rework.md` is a
symptom of this: the usage formula is wrong, and it is wrong in *three places at
once*, so it cannot be fixed once. Consolidation is the precondition for fixing
it correctly.

**2. The interpretation is scattered, and the gated detectors are starved.**
14 of 27 `/api` endpoints are interpretation (relative judgements, rankings,
deviations, correlations), but the logic lives in `Data/Stats` and
`Data/Aggregators` next to raw collectors, not in the engine. Meanwhile the
engine itself has **6 gated detectors that emit nothing** (`ContextCorrelatedSpike`,
`ContextConditionalCost`, `GcPauseCulprit`, `SustainedCostShift`, `NewContributor`,
`HookFrequencyTail`), each waiting on a substrate (conditional baselines,
cross-session data) this plan builds. The machine is half-built and half-scattered.

The data pipeline already proved this exact consolidation works (its own migration
scars are documented: `DashboardRouter.BuildHeatmap` once held inline aggregation
math; extracting it to `HeatmapAggregator` was "the canonical kill-the-inline-math
step"). This plan repeats that move for interpretation.

---

## The spine: the relativity law (generalised)

> **No insight is ever an absolute magnitude. Every insight is the deviation of a
> signal from the comparable baseline for that signal, on this machine, expressed
> as an effect size. What varies between families is the reference frame, not the
> relativity.**

This kills the two failure modes that define a dishonest profiler: "Calamity
always uses more RAM" (true, therefore not an insight; the relative version asks
whether it is high *for itself* or *more than the sum of its parts*), and "a 20fps
machine flags everything" (a weak machine has a high baseline, so machine strength
cancels in the ratio). Every family below preserves this law; they differ only in
what they compare against.

---

## What an insight is (the canonical type)

Already typed today as `InsightRecord` (a good design: `Magnitude`, `Evidence`,
three orthogonal honesty axes `Confidence` / `EvidenceScope` / `BaselineKind`,
`Audience`). Two generalisations are needed to carry all five families:

- **`Magnitude` becomes shape-tagged.** Today it is deviation-shaped
  (`baseline`, `observed`, `ratio`, `bytes`). A trend needs a *rate*, a scaling
  law needs *slope + intercept + valid range*, headroom needs *ceiling +
  remaining*, a distribution shape needs *percentiles / modality / recovery-time*.
  Key the meaningful fields off the family/`PatternKey`.
- **`SubjectRef` gains process-level subjects.** Today it points at a mod / hook /
  context. Family B insights (restart, warmup, leak) are about *the session / the
  runtime / the machine*, which are not mods. Add those subject kinds.

The three honesty axes are preserved exactly, because they are the honesty
contract made structural:

| Axis | Values | What it lets the reader argue with |
|---|---|---|
| `Confidence` | Preliminary → Low → Medium → High | the statistical strength (gated on `ConfirmationCount` AND `PValueAdjusted`; repetition alone never promotes a `pAdjusted=1` record past Low) |
| `EvidenceScope` | ThisSession / LifetimeData / NeedsPersistence | the breadth of data behind it |
| `BaselineKind` | SessionMean / RollingFiveMinute / PreContext / ComparableContexts / SessionFirstHalf / PerModRollingMean / None | *what it compared against* |

`RankingScorer`'s pattern-aware magnitude (the `IsSharePattern` split that stops a
42% contributor being treated as a 0.42x ratio and clamped to zero) stays and
extends to the new shapes.

---

## The five families (the taxonomy)

Each family is defined by its reference frame. (Full worked examples and
honesty caveats are in the design conversation; this is the index.)

| Family | Reference frame | Terraria-concrete example | Net-new? |
|---|---|---|---|
| **A — Deviation from a comparable situation** | the same context, normally | "boss fights during a Blood Moon ran 34% above your boss-fight baseline" | partly exists (segment-outlier, the gated context detectors) |
| **B — Behaviour over time** | your own earlier session | "heap is 3.2x its load value at constant workload; a restart resets it" (the restart insight, leak, warmup, change-point, rank-change) | mostly net-new |
| **C — Shape of the distribution** | what well-behaved looks like | "your lag is stutter (many tiny stalls), not slowdown"; "spikes cascade into 2s drops" | net-new (LagRhythm is a seed) |
| **D — Relative to a ceiling** | the limit | "you sustain 60fps with ~3ms of budget free"; "14 of 32GB used" (serves the 99-mod plan) | net-new |
| **E — Structure across signals** | the rest of the system | "3 mods are 70% of cost (a lever)"; "frame time scales at ~0.04ms/projectile, cliff at ~400"; **cross-mod chains** | partly exists (ModInteraction Pearson) |

Cross-mod chains ("Mod A's projectile triggered Mod B's status applied by Mod C's
accessory", the research-gated mockup feature) is **one Structure detector**, not a
standalone feature. The engine eats it.

---

## Architecture: the single `Insights/` module

### Two orthogonal boundaries (do not conflate them)

- **Folder boundary** (code organisation): all interpretation logic lives in one
  top-level `Insights/` module. This is your ask.
- **Registry boundary** (runtime export seam): the module still *publishes through
  `DataRegistry.Shared`*, the same single seam the dashboard already uses. The
  pipeline policy "consumers ask the registry" is preserved; only the *home of the
  logic* is consolidated.

So `Insights/` owns the logic; its outputs register as `IDataStat<...Snapshot>`
into the existing registry. One folder for code, one registry for export.

### Placement decision

Promote the engine from `Data/Detectors/Insights/` to a **top-level `Insights/`**
peer of `Profiling/` / `Data/` / `Web/` / `UI/`. This matches the architecture's
own dependency diagram (which already draws the insights engine as its own box
downstream of `Data/`) and the honest layering: `Insights/` is the
*second-derivative* layer, consuming `Data/` numbers and feeding `Web/`
presentation. Alternative considered: keep it under `Data/Insights/`. Rejected
because the pipeline's cardinal rule is "if it produces a *number* it lives in
`Data/`", and insights produce *interpretations*, not numbers; a peer folder
states that distinction structurally. Cost: a namespace move
(`PerformanceProfiler.Data.Detectors.Insights` → `PerformanceProfiler.Insights`)
with a known blast radius (`InsightsEngine.Shared` call sites, `InsightsStat`).

### Target internal shape (rich modularisation, one folder)

```
Insights/                          the interpretation layer (top-level peer)
├── Insight.cs                     canonical record (generalises InsightRecord)
├── InsightsEngine.cs             orchestrator: detector roster + off-thread eval
├── InsightStore.cs              dedup, TTL, confidence promotion, live/history
│
├── Contracts/                    seams, frozen first (Wave 0; contract-decoupling)
│   ├── IInsightInput.cs          the processed-pipeline view detectors may read
│   ├── IInsightDetector.cs      detector contract (exists; moves here)
│   ├── IReferenceFrame.cs       "the comparable baseline for this observation"
│   ├── IDriver.cs               a measurable dimension to slice / regress by
│   └── Snapshots.cs             published output snapshot types (the /api shapes)
│
├── ReferenceFrames/              the relativity substrate (Addition 1 generalised)
│   ├── SessionBaseline.cs       wraps existing Baseline (histogram median/MAD)
│   ├── ContextBaseline.cs       per-context distributions (Family A)
│   ├── TemporalBaseline.cs      session-start / rolling-past (Family B)
│   ├── DistributionFrame.cs     percentiles / modality / recovery (Family C)
│   ├── CeilingFrame.cs          frame-budget / RAM ceiling (Family D)
│   └── CrossSessionStore.cs     persisted, machine-fingerprinted (Addition 5)
│
├── Drivers/                      the dimension registry (Addition 3 generalised)
│   ├── ActiveUseDriver.cs       held item / armour / accessory (FIXES usage bug)
│   ├── EntityCountDriver.cs     NPC / projectile / dust (already in TickFrame)
│   ├── SessionAgeDriver.cs      time since load (Family B)
│   └── HeapDriver.cs            managed heap size (Family B leak)
│
├── Detectors/                    organised BY FAMILY
│   ├── Deviation/                A (conditional cost, segment outlier, spike)
│   ├── Temporal/                 B (drift, leak, warmup, change-point, rank)
│   ├── Distribution/             C (jitter, bimodal, recovery, rhythm)
│   ├── Headroom/                 D (frame / RAM headroom)
│   └── Structure/                E (concentration, coupling, lead-lag, scaling, cross-mod-chains)
│
├── Shared/                       deduplicated primitives (KILLS the census)
│   ├── ModMetrics.cs             UsageWeight(), RosterSize(), PerModFold()
│   ├── Shares.cs                 SafeShare(), Percentage(), TopN()
│   └── ModNames.cs               SafeName()
│
├── Rendering/                    the honesty choke point (single place)
│   ├── InsightRenderer.cs        slot-fill, banned vocab, BaselineKind clauses
│   └── RankingScorer.cs          pattern-aware magnitude + weighted score
│
└── Publish/                      output adapters into DataRegistry
    ├── InsightsStat.cs           the live ranked feed ("insights")
    └── (migrated I1-I7 views, now fed by the engine, not re-deriving)
```

### Input contract (what the module consumes)

Detectors read **only processed pipeline outputs**, never collection internals.
This is already true and excellent: every in-scope detector reads smoothed
accessors (`PerModCategoryAverageMs`, `PerHookAverageMs`, `Baseline`,
`SpikeWindow.PerModCatMs`, `SegmentStore` aggregates) or structured LiteDB rows.
The contract formalises this: the module depends on a small set of registry
snapshots (`HookCpuSnapshot`, `FrameTimeSnapshot`, `AllocationSnapshot`,
`ModRosterSnapshot`, `ModUsageSnapshot`, segment + lag snapshots) plus the
`internal` `MetricCollector` accessors, and nothing else. This keeps detectors
unit-testable against synthetic input (the L1 axis of the testing-infrastructure
plan).

### Output contract (how the module publishes)

`detector → InsightStore → InsightsStat (IDataStat<InsightsSnapshot>) →
DataRegistry.Shared → DashboardRouter.BuildInsights → /api/insights`, with a
`Live` set and a `History` set. Every other consumer uses the same path:

| Consumer | Today | After |
|---|---|---|
| Dashboard insights tab | reads 5 separate stat snapshots + the engine | reads engine-published snapshots |
| Dashboard I1-I7 panels | re-derive in `Data/Stats` | engine views, no re-derivation |
| `SessionSummaryLogger` (client.log) | re-derives top-N rankings independently (`:49-87`) | reads the engine |
| Session JSON `insights.live/history` | `Store.AllLive()` / `Store.History` | unchanged |
| Future overlay / retrospective | n/a (archived) | reads the engine |

### The boundary that stays OUT

Raw-faithful views are **not** insights and stay in `Data/` + `Web/`: `/api/now`,
`/api/mods`, `/api/hooks`, `/api/memory`, the full per-mod cost tree. An insight
is *selective and relative*; a raw view is *faithful and complete*. Collapsing
"show me everything" into the engine would force it to either hide mods (breaks the
faithful-view contract) or spam non-insights. Dependency-graph metadata (a mod's
declared `modReferences`) may *annotate* an insight but is not produced by the
engine (not measured, not relative).

---

## The five additions (generalised), mapped to the module

These were detailed in design; here they are as module components.

1. **Reference-Frame Provider** (`ReferenceFrames/`). Generalises the existing
   single session `Baseline` into a provider that hands back the right comparison:
   context, session-start, rolling-past, distribution-shape, ceiling, or
   cross-session. The keystone: it is what makes Families B/C/D expressible and
   what un-gates `ContextConditionalCost` / `ContextCorrelatedSpike`.
2. **Relationship Detector** (`Detectors/Structure/`). Co-occurrence is one
   relation; also trend (vs time), scaling (vs driver), lead-lag, coupling,
   change-point. Cross-mod chains live here.
3. **Driver / Dimension Registry** (`Drivers/`). Active-use is one driver (and
   fixes the Flute bug via `Player.HeldItem` + the existing hit hooks + armour
   worn, all generic surfaces per Invariant 5). Also entity counts (already in
   `TickFrame`), session age, heap size. Lets a leak be told from "you built more
   stuff".
4. **Super-additivity Detector** (`Detectors/Structure/`). Loadout pairs are one
   case; the general "joint effect exceeds the sum" applies to any two drivers or
   conditions. Activates `LoadoutCombinationCost` (today emits nothing).
5. **Durability & Fairness Layer** (`ReferenceFrames/CrossSessionStore.cs`).
   Cross-session persistence + machine/modlist fingerprint. Wraps *every* family.
   Fixes gap G3 (today no live detector reaches Medium because `PValueAdjusted=1`
   and there is no lifetime data).

---

## Migration sequence (waves, contract-decoupling)

Each wave is committable and output-compatible (one exception, flagged). Uses the
frozen-contract pattern so waves overlap, exactly as the v0.12 expansion did.

| Wave | What lands | Behaviour change? | Risk |
|---|---|---|---|
| **0 — Freeze contracts** | `Insight`, `IInsightInput`, `IReferenceFrame`, `IDriver`, output `Snapshots`, `Shared` primitive signatures | none | low; pure declarations downstream compiles against |
| **1 — Module + dedup** | create top-level `Insights/`, move engine in (namespace change), extract `Shared` primitives, rewire all duplicating call sites | none (byte-identical output; tests guard) | namespace blast radius (`InsightsEngine.Shared`, `InsightsStat`) |
| **2 — Relocate interpretation** | move the 5 interpreted stats + `ModInteractionAggregator` logic into `Insights/`, re-expressed to consume the engine + `Shared`; `/api` shapes unchanged | none | 14 endpoints must stay output-compatible; snapshot tests before/after |
| **3 — Reference frames** | build `ReferenceFrames/`; un-gate the context detectors | additive (new insights appear) | overhead of per-context distributions (Invariant 2; Lite stays session-baseline only) |
| **4 — Drivers + active-use** | `Drivers/`, fix the usage axis (the deferred note), reshape usage-derived `/api` fields | **yes — usage fields change meaning** | the one breaking contract change; coordinate the dashboard JS in the same wave |
| **5 — Light up families** | implement Temporal/Distribution/Headroom/Structure detectors; activate the 6 gated detectors; cross-mod chains | additive | combinatorial explosion (candidate-gating, below) |
| **6 — Cross-session + fingerprint** | `CrossSessionStore`, machine/modlist fingerprint, persisted baselines | additive (confidence can now reach High; `LifetimeData` becomes truthful) | new LiteDB schema (schema-version bump, an L1/L6 test surface) |
| **7 — Reroute consumers** | `SessionSummaryLogger`, retrospective (and overlay if revived) consume the engine | none | verify client.log + session JSON agree with the dashboard |

Wave 1 is the highest-leverage low-risk step: it kills the duplication census and
establishes the home without changing a single output. Wave 4 is the only breaking
change and must be atomic with its dashboard consumer.

---

## Invariants and constraints threaded through

- **Invariant 2 (overhead budget).** Evaluation stays off-thread at 1Hz (every 60
  ticks, `Interlocked`-guarded) as today. The net-new per-tick cost is the
  reference-frame and driver sampling; it is bounded (few active contexts at once)
  and **mode-gated**: Lite = session-baseline + no active-use sampling; conditional
  baselines and drivers belong to Standard/Deep. Any per-tick addition is measured
  against the budget before it is "done".
- **Invariant 3 (honesty contract).** `Rendering/` is the *single* choke point:
  slot-fill only, banned vocabulary, every insight carries its three badges and
  its `BaselineKind` clause. All five families route through it, so editorial creep
  has exactly one place to be caught. The restart insight is observation-plus-
  mechanism, never a command.
- **Invariant 5 (no mod-specific code).** Drivers and detectors read generic
  surfaces only: held item via `ModOwnerCache`, never by name; weather flags,
  biome bits, boss presence, accessory slots. Modded weather/events/invasions are
  not surfaces tML exposes, so insights condition on vanilla context only; that is
  an honest UI statement, not a hidden gap.
- **Testability (ties to `extensive-testing-infrastructure.md`).** Detectors,
  reference frames, drivers, and `Shared` primitives are pure logic over declared
  inputs → the L1 axis. The frozen output snapshots + golden fixtures → the L6
  axis. The rework is the testing plan's first real customer.
- **Dual-surface observability.** Every family surfaces on the dashboard (player)
  AND in the session JSON / client.log (agent), through the same engine, so the two
  examiners cannot disagree about what fired.

---

## Risks and assumptions

- **Combinatorial explosion / p-hacking** (Families A/E super-additivity). Testing
  every condition pair guarantees false positives. Hard rule: **candidate-gating
  by real co-occurrence count before any statistical test**; the `InsightStore`
  p-value adjustment is the second line, not the first.
- **GC attribution is a hard limit, not a TODO.** Collections are global-only;
  allocation is syntactic (credited to the hook frame, not the alloc site). Every
  memory-pressure insight says "allocation rate, correlated", never "GC, caused".
- **Temporal confound.** "Performance degrading" may be progression (more map,
  more NPCs), not a leak. Family B must control for workload via the driver
  registry (heap up *at constant entity count*), else it is dishonest.
- **Migration blast radius.** 14 interpreted endpoints and the session JSON must
  stay output-compatible through Waves 1-3 and 5-7; Wave 4 is the sole intentional
  break. Snapshot/golden-fixture tests guard each wave.
- **Known engine gaps to fix in-flight** (from the vault Insights Engine doc):
  G3 `PValueAdjusted=1` on all live detectors (Wave 6 lifetime data unblocks
  Medium/High), G5 `_topComparerNowTick` shared-scalar race in `TopInto`
  (concurrent callers), G6 `StableKey` 16-bit slot collision above 65k hookIds per
  mod. The consolidation is the moment to close these.
- **Assumption needing evidence.** That the number of *simultaneously active*
  context buckets stays small in a 99-mod stack (modded biomes could inflate it).
  Mitigation: cap tracked buckets to top-K by sample count, fold the tail into
  "other", and `log()` the cap.

---

## References

- **Engine core (code):** `Data/Detectors/Insights/` — `InsightsEngine.cs`,
  `InsightRecord.cs`, `InsightStore.cs`, `InsightRenderer.cs`, `RankingScorer.cs`,
  `IInsightDetector.cs`, `Detectors/`. The files Wave 1 relocates.
- **The export seam (code):** `Data/DataRegistry.cs`, `Data/IDataStream.cs`,
  `Data/Contracts/RolloutContracts.cs`, `Web/DashboardRouter*.cs`,
  `PerformanceProfiler.cs:127-180` (`RegisterDataPipeline`).
- **Duplication evidence (code):** `Data/Stats/DormantSurfaceStat.cs`,
  `ModObservatoryStat.cs`, `EngagementCostScatterStat.cs`,
  `CrossCuttingSignalStat.cs`, `Data/Aggregators/ModInteractionAggregator.cs`,
  `Profiling/Persistence/SessionSummaryLogger.cs`.
- **The usage bug:** `context/notes/future-insights-rework.md` (Wave 4 fixes it).
- **Design source of truth (vault):** `Projects/Performance Profiler/Systems/Insights Engine.md`
  (honesty axes, ranking, store lifecycle, gaps G3-G6) and `Systems/Data Pipeline.md`
  (the consolidation template), via `gh api repos/Capataina/LifeOS/...`.
- **Sibling plans:** `context/plans/extensive-testing-infrastructure.md` (L1/L6
  exercise this rework), `context/plans/ui-component-library.md` (the same
  anti-drift move on the UI layer; insights are its logic-layer analogue).
- **Design mockup:** `design/Mockups.html` (cross-mod chains, dormant cost,
  event-triggered attribution, session retrospective — all subsumed as detectors).
- **Memories:** `wave-based-agent-parallelisation`, `contract-decoupling-pattern`
  (the Wave-0-freeze mechanics this migration reuses).
