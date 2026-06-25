# Plan — Extensive testing infrastructure

> Goal: treat testing as a **layered strategy across independent axes**, not a
> single tool. Each axis proves a different class of correctness, grows on its
> own schedule, and fails in its own way. The point of writing it this way is
> durability: as the mod evolves (the insights rework next, then whatever
> follows), our testing needs shift *per axis* rather than all at once, and a
> layered map lets us grow the layer the change actually stresses without
> re-architecting the rest.
>
> The throughline is the project's own **Testability** standard and **dual-surface
> observability**: pure logic must be provable on synthetic input without a
> running game, and every runtime feature must be observable on both the player
> surface (UI) and the agent surface (`client.log` + JSON-lines). Testing
> infrastructure is how we *exercise* both surfaces off a real game where we can.
>
> Status: **PARTIALLY EXECUTED (2026-06-24).** The load-bearing axes are now BUILT
> under `tools/testing/` (commit `fe2d57b` + the fix-wave follow-ups): **L4**
> (deterministic Playwright layout/interaction invariants), **L6** (generative
> fixtures from the discovered `/api` contract), and **L8** (the agent-driven UI/UX
> audit — capture → fan-out review → synthesize → per-page dossiers under
> `context/pages/`). The harness is self-describing (tabs/panes/endpoints discovered
> from the DOM/JS), so it scales with the app. **L1** (xUnit pure-logic) predates
> this plan. Canonical reality: `systems/dashboard-audit-harness.md` (L4/L6/L8) and
> `systems/test-harness.md` (L1). **Still open:** L2 (hot-path overhead budget), L5
> (visual regression — falls out of an L8 re-run diff), L7 (in-game runtime). This
> doc is kept as the directional axis-map the remaining layers grow against.
>
> Date opened: 2026-06-23. L4/L6/L8 executed: 2026-06-24. Mod version at open: `0.18.1`.
> Expanded: 2026-06-24 (`0.19.0`) with the agent-driven UI/UX audit axis (L8), the
> visual-quality bar the audit measures against, and the synthetic-data
> completeness discipline that makes both honest.

---

## Why this exists

Three pressures created this plan:

1. **A concrete gap.** The dashboard preview harness (`tools/preview/render.py`)
   is **static-only** — it drives Chrome in one-shot `--headless=new
   --screenshot=` mode, so the only thing that comes back out is a flat PNG. It
   cannot scroll, hover, select, or measure an element. A whole class of bugs we
   keep paying for in-game round-trips (text leaking past a sticky header, a
   chip positioned off-screen, two regions overlapping, a scroll region that
   does not cap) is **invisible to a pixel dump** and only shows up after build +
   reload + re-enter world. Those bugs are not subtle in principle; they are
   subtle *to our current instrument*.

2. **Evolving needs.** The insights rework reshaped the **attribution layer**
   (how "usage" is measured — see `context/notes/future-insights-rework.md`) and
   added a whole new ranked-insight feed. That stresses a completely different
   axis than dashboard layout: pure-logic correctness on synthetic input. If
   "testing" means only "the screenshot harness", that work has no home. The
   strategy has to name every axis so each change lands on the right one.

3. **The UI is the product, and only one human can currently see it.** The
   dashboard is how every measurement reaches the player; *data we cannot present
   well is data that does not matter.* Yet today the only instrument that can
   judge the things that decide whether the UI is good — is this readable, is it
   aligned, is the colour encoding sane, is a control missing, is this the right
   chart for the data shape — is **Caner's own eyes**. That makes one human the
   bottleneck on every visual bug and caps iteration at human-review speed: we can
   squash *some* bugs, never *all* of them, and never the subjective-but-real
   "this works but it looks amateur" class. The fix is to give the **agent** the
   ability to see and judge the UI — drive the real dashboard, screenshot every
   surface, and fan out vision-capable agents to flag every problem in parallel —
   turning a days-long manual review into a single orchestrated pass. This is the
   axis (L8) with the largest human-time payoff in the whole map, measured not in
   minutes but in days and weeks per UI iteration.

The generic frame below is the answer to all three: a map of testing axes, what
each proves, where it stands today, and the next concrete increment for each.

---

## The axes (the map)

| # | Axis | What it proves | Surface / tool | Current state |
|---|---|---|---|---|
| **L1** | Pure-logic unit | Detection, ranking, attribution, schema/compaction math is correct on every synthetic input, edge cases included | C# **xUnit** (`Tests/`, net8.0) | **Exists** — RingBuffer, Pools, StallDetector/Classifier, RankingScorer, InsightStore, persistence round-trip. Grows most with the insights rework. |
| **L2** | Overhead / budget | The per-tick hot path stays inside Invariant 2 (Lite <1%, Standard 2–4%, Deep 5–10%) and stays zero-allocation | C# benchmark tests | **Partial** — persistence benchmark exists; hot-path alloc/timing assertions do not. |
| **L3** | Dashboard render | The CSS/JS produces the right layout from given data, reflecting **un-built `.cs` edits** | `render.py` headless screenshot | **Exists, static-only** — the ceiling this plan lifts. |
| **L4** | Interaction & layout invariants | Scroll, hover, selection, drawers, overflow, overlap, sticky headers hold *under control*, deterministically | **Playwright (Python)** layered onto the existing harness | **Proposed** — the load-bearing new work. |
| **L5** | Visual regression | No *unintended* visual drift across edits (a baseline changed only where we meant it to) | Screenshot baselines + diff (builds on L4's browser handle) | **Future** — cheap once L4 exists. |
| **L6** | Data-contract / fixtures | The `/api` JSON shapes both surfaces consume are stable, and a frozen fixture set feeds L3/L4 **reproducibly** | Shared captured-JSON fixtures + schema checks | **Partial** — `render.py capture_data()` curls live data; not frozen, not schema-checked. |
| **L7** | In-game integration smoke | End-to-end on the real game, on **both** surfaces (player overlay + `client.log` + JSON-lines) | Manual build+reload, plus log/JSON assertions | **Manual** — the irreducible final check; partially automatable on the agent surface. |
| **L8** | Agent-driven UI/UX audit | The rendered UI is **readable, aligned, complete, consistent, and well-encoded** — the judgement-call quality bar a bounding-box assertion cannot reach, including "wrong chart for the data shape" and "this needs a control" | Screenshot-every-surface harness → fan-out of vision agents against a shared design rubric → fix-list → rerun | **Proposed** — the headline new build; the largest human-time saver in the map. |

Each row is independent. A change picks the axis it stresses and grows that
layer. The insights rework, for instance, landed mostly on **L1** (new attribution
math) and **L6** (new fixtures + a reshaped `/api` contract), *benefits from*
**L4** (interaction tests on the reworked Insights tab), and is *judged* by **L8**
(does the new insight feed actually read well), without forcing any of the other
layers to change.

**L4 vs L8 — the load-bearing distinction.** They both drive the real browser, but
they catch opposite halves of "is the UI good": **L4 proves the deterministic
invariants** a machine can assert (nothing overflows, no box sits above its sticky
header, a drawer moves off-screen on close) — true/false, no judgement. **L8 catches
the judgement class** a machine cannot reduce to an assertion (is this legible at
this size, is the colour doing work or just decorating, is a 30-row list missing a
filter, is a bar chart the wrong encoding for a part-to-whole). L4 is a lint; L8 is a
design review. You need both, and L8 is where the human time actually goes today.

---

## L4 — the interaction & layout axis (the new build)

This is the axis the current harness cannot reach, and the one worth detailing,
because it is where today's investigation landed.

### What's actually capping us

`render.py` today: `spawn Chrome → run injected preview-boot.js → one PNG →
kill Chrome`. There is **no live handle** to the page. We already fake a sliver
of interaction (the boot script calls `switchTab()` and clicks the first row to
populate detail panes), but it fires blind and only pixels come back. The
static-only ceiling is a property of the **one-shot CLI screenshot mode**, not of
headless browsers. Swap the driver and the ceiling lifts.

### What "control it like a user" decomposes into

| Capability | Reachable? | Mechanism |
|---|---|---|
| Scroll a region; assert sticky header holds / no leak | ✅ | `mouse.wheel()` / `scroll_into_view_if_needed()` then assert |
| Element sized / placed correctly | ✅ | `bounding_box()` → `{x,y,width,height}`; `getComputedStyle` via `evaluate` |
| Two elements overlap / text leaks past header | ✅ | compare bounding boxes (the **exact class** of the dormant-leak + transition-chip bugs) |
| Element within viewport / horizontal overflow | ✅ | box vs viewport; `scrollWidth > clientWidth` |
| Hover / selection / focus / drawer-open states | ✅ | drive the interaction, *then* screenshot |
| Visual regression across edits | ✅ | screenshot diff (this is L5, built on the same handle) |

### Tooling decision: Playwright (Python)

Researched 2026-06-23; consensus is unambiguous.

| Tool | Verdict for us |
|---|---|
| **Playwright** ✅ | Default for new projects. WebSocket/CDP transport (fastest), auto-waiting (waits for elements to be visible/stable/not-obscured), trace viewer (frame-by-frame + DOM snapshots), `bounding_box()`, scroll helpers, `to_have_screenshot()`. **First-class Python bindings** → slots straight into `render.py`. |
| Puppeteer | Chrome-only, Node/JS only — a language split from our Python harness for no gain. |
| Selenium | WebDriver/HTTP overhead, slowest, legacy. |
| Cypress | Its own JS test-runner universe; overkill for a loopback dashboard. |

The Python binding is decisive: we **keep `extract()`** (the un-built-`.cs`
reflection that is the harness's whole reason to exist) and the loopback serve
untouched, and replace **only** the `shoot()` step with a Playwright session we
hold open and poke.

### Cost / honest catch

- **Intentional dependency, but local:** `pip install playwright` + `playwright
  install chromium`. No cloud, no paid service, no extra server (we already serve
  on loopback), no JS framework, nothing that touches the self-contained `.tmod`.
- **The one real design decision is determinism, and it lives in L6** (below), not
  in the tooling: interaction assertions against `--live` data are
  non-deterministic because the mod's data shifts tick to tick.
- **Minor:** headless vs headed rendering differs sub-pixel (fonts mainly).
  Irrelevant for layout/overlap assertions; only matters if L5 sets tight diff
  thresholds.

### First cut (when this axis is picked up)

1. Keep `extract()` + serve; add a Playwright-driven page alongside the current
   `shoot()` rather than ripping it out (the tall-printout screenshot stays
   useful for eyeballing).
2. Add a `--assert` mode with a handful of **layout invariants** that encode the
   bug classes we keep hitting:
   - no element wider than its container (no horizontal overflow),
   - no row's box above its sticky header's box,
   - sticky header survives a scroll to the extreme,
   - selected/hover state actually changes the row's computed style,
   - drawer open/close moves the drawer box on/off-screen as expected.
3. Run it against a **frozen fixture** (L6), not live data, so a failure means a
   real regression, not a data shift.

---

## L8 — the autonomous UI/UX audit loop (the headline build)

The axis that takes the human out of the inner loop of "look at the dashboard,
find everything wrong with it." A bounding-box assertion (L4) cannot tell you a
panel is ugly, a number is buried, a list needs a filter, or a bar chart is the
wrong encoding. A vision-capable agent can. L8 is the orchestration that points a
fleet of those agents at every surface of the dashboard and collects every problem
in a single pass.

### The loop

```
capture → review (fan-out) → synthesize → fix (batch) → rerun → converge
```

1. **Capture** — drive the dashboard (the L4 browser handle against the L6
   synthetic data) and screenshot *every* surface: each tab whole, then each pane
   within each tab cropped individually, in each meaningful state (default, hover,
   selected, scrolled-to-extreme, drawer-open, empty, dense), at real viewport
   sizes. Write them to a **clean-slate** directory that is wiped and remade every
   run (e.g. `design/audit/`), so screenshots never pile up and a run is always the
   current truth, never a mix of old and new.
2. **Review (fan-out)** — spawn **one agent per tab**, in parallel (a 6-tab
   dashboard is 6 agents, each owning its tab's whole-shot plus every pane crop).
   Each reads its screenshots against the shared rubric (below) and emits structured
   findings: `{ surface, location, severity, category, what's wrong, suggested fix }`.
   The parallel fan-out is the whole point and the thing OpenDesign cannot do — it
   is a separate platform with no subagent orchestration, so reviewing six tabs
   means six serial human-driven passes there, versus one orchestrated pass here.
3. **Synthesize** — collect every agent's findings into one ranked fix-list:
   dedupe (a global issue flagged on many panes collapses to one), rank by severity,
   group by the file that fixes each.
4. **Fix (batch)** — work the list top-down, one coherent batch at a time.
5. **Rerun → converge** — re-run capture+review on the fixed build, confirm the
   flagged issues cleared, and that nothing new broke (this is also where **L5**
   visual-regression lives — the rerun's screenshot diff shows exactly what moved).
   Loop until the list is empty or only `P3`/`wontfix` remains.

### Why agents, not a bigger assertion script

The deterministic half of "is the UI right" is L4's job and stays there (faster,
cheaper, a hard gate). L8 exists for the half that does **not** reduce to a boolean:
taste, completeness, hierarchy, encoding-fit, readability-at-size. Those are
judgement calls a vision model makes where a `bounding_box()` compare cannot. The
division is clean and worth stating as a rule: **if a finding can be written as an
assertion, it belongs in L4; if it needs an eye, it belongs in L8.**

### The genericisation contract (what keeps it alive as the app grows)

The harness must **discover** structure, never hardcode it, so it survives every
new tab, pane, insight, and tracked metric:

- **Panes are discovered from the DOM,** not a hardcoded "6 tabs, 7 panes". The
  capture walks the rendered page (every tab in the tab bar, every panel within
  each) and shoots whatever is there. Add a tab tomorrow and it is audited with no
  harness change.
- **The rubric is principle-based,** not pane-specific: "a long list needs a
  filter" applies to any list that ever exists. New surfaces inherit the whole
  rubric for free.
- **The synthetic data is generative** (L6): it fills *every* slot the `/api`
  contract defines, so a new field or endpoint is populated and therefore audited
  automatically.
- **The agent count scales with the page,** one per discovered tab; the
  orchestration reads the tab list and fans out to match.

Get those four right and L8 never needs re-architecting as the mod grows — exactly
the durability this whole plan exists for.

### The audit rubric (what every review agent applies)

The shared checklist that turns "looks off" into a structured, fixable finding.
Principle-based, so it covers surfaces that do not exist yet. Grouped by what it
catches; each finding carries a severity (**P1** broken / unreadable / wrong · **P2**
dead-space / clarity · **P3** polish) so the fix-list ranks itself.

**Layout & alignment**
- Elements overlapping; text leaking past a sticky header (L4 catches the gross
  case, the eye catches the near-misses under the threshold).
- Horizontal overflow / content wider than its container; a chip or label clipped
  at an edge.
- **Vertical baseline misalignment** — labels that should share a line sitting a
  few px apart, a value not centred against its bar, a number floating above its
  caption. The most common "amateur" tell, and invisible to a per-element assertion.
- Inconsistent spacing: gutters / padding / gaps that vary where they should be
  uniform; a ragged or broken grid.
- Dead space: a panel mostly empty with its content clustered in one corner.

**Readability**
- Text too small at the real render size; low contrast (muted-on-muted, a value
  that vanishes into its background); truncation with no ellipsis; a wall of
  same-weight text with no scanning path.

**Colour & encoding**
- Clashing or muddy palette; the same colour meaning two things across panes, or
  two colours meaning one thing.
- **Decoration vs encoding (project rule):** colour must *encode* (severity, a
  per-mod series, a state), never decorate — flag any colour carrying no meaning.
- Colourblind-unsafe pairs (red/green as the only distinction).

**Affordance completeness**
- A long list with no **search / sort / filter**.
- A missing or cramped **empty state**; no idle / loading / "no data yet" copy.
- No **hover / selection / focus feedback** on something interactive; something
  that looks clickable and isn't (or the reverse); a drawer with no clear
  open/close affordance.

**Chart appropriateness (the Chart.Guide discipline)**
- **Wrong chart for the data shape.** Match encoding to question: part-to-whole →
  donut / stacked / treemap; distribution → histogram / box / violin; relationship
  → scatter / heatmap; over time → line / area / sparkline; flow → sankey; ranking →
  sorted bars; geospatial → map. A bar used for everything is the current smell.
- Missing context (a value with no baseline / reference line / comparison);
  misleading axes (truncated or dual-Y), 3D, gridline clutter, too many series, a
  rainbow where a sequential ramp belongs.

**Information hierarchy**
- The most important number is not the most prominent thing in its panel; no visual
  path (everything the same weight, the eye has nowhere to land first).

**Consistency (the component-library discipline)**
- The same concept rendered differently across panes (two "selected" styles, two
  empty-state treatments) — the drift the component library exists to kill, now
  caught visually.

**Honesty (Invariant 3)**
- Normative vocabulary in player copy ("remove", "core", "bad mod") instead of
  descriptive; an insight shown without its data-strength / confidence / baseline
  badge.

---

## The visual-quality bar — design knowledge the agents carry

A reviewer with only the rubric catches *bugs*; a reviewer with *taste* also raises
the *ceiling* — "this works, but it should be a richer encoding." L8's agents need
both, so the audit is paired with a design-knowledge layer they read before
reviewing (and that the design/build agents read before *creating*). This is also
how we answer the standing complaint that the app is "bars everywhere, with one pie
chart as the most interesting thing."

### The chart vocabulary to grow into

The data we collect supports a far richer vocabulary than today's bars + donut, and
the audit should flag where a flat bar leaves a better encoding on the table. A
non-exhaustive map of chart type → data we already have that fits it:

| Encoding | Fits our data |
|---|---|
| **Radial gauge / ring progress** | Self-tab overhead vs budget; the FrameHeadroom insight; a mod's share of total |
| **Multi-ring / nested donut** | Per-category cost within per-mod within total (the impact donut, one level deeper) |
| **Area / line with gradient + threshold rules** | Frame-time trace, GC pressure, per-mod cost over time, the 60 fps reference line |
| **Sankey / flow** | **Cross-mod chains** (A's projectile → B's status → C's accessory — the exact feature still waiting for a home), cost flowing category → mod |
| **Heatmap** | Cause × context lag matrix, time-of-day activity, per-mod-per-segment density |
| **Bubble / scatter** | Engagement-vs-cost (already a scatter; a third dimension as radius) |
| **Dot-matrix / waffle** | Modlist composition (active vs dormant as a unit grid, not just a number) |
| **Timeline / swimlane gantt** | The segment timeline; boss / biome / event spans |
| **KPI card + trend sparkline** | Every headline number (avg fps, worst frame) with its own micro-trend — the modern-dashboard idiom |
| **Small multiples** | One mini-chart per mod for at-a-glance comparison across a roster |

The discipline is not "use every chart" — it is **match the chart to the data
shape**, and stop defaulting to a bar when the shape wants something else.

### Do's and don'ts the agents enforce

- **Do:** rank relevantly, target a benchmark / reference, support easy comparison,
  build a visual hierarchy, write descriptive titles, prefer a sequential ramp for
  magnitude.
- **Don't:** 3D anything, dual-Y axes, gridline clutter, more than ~4 series on one
  chart, truncated / misleading axes, colour-as-decoration, too-many-decimals,
  illegible micro-text.

### Teaching the agents taste, not just rules

A rubric is a floor; design quality needs a reference the agents pattern-match
against. Two moves:
- **A curated design-reference set** committed beside the rubric: examples of the
  chart types above done well (polished radial / gauge / area / sankey work — the
  external references in `design/renders/` are a seed), so a review agent can say
  "this bar should look like *that* gauge" with a concrete target, not a vague "make
  it nicer."
- **Lean on the `frontend-design` skill** (already installed) as the creative
  direction when *building* a new surface, with L8 as the *verification* layer that
  judges the result. Design-skill creates, L8 audits, the rubric + reference set is
  the shared standard between them.

The throughline: L8 does not just stop the UI getting *worse* (regression) — it is
the mechanism for making it steadily *better*, at agent speed instead of
human-review speed. On a project whose whole value is presenting measurement well,
that is the highest-leverage axis in the map.

---

## L6 — the fixture / data-contract axis (what makes L3/L4 honest)

This is the seam that makes everything above reproducible, and the seam the
insights rework will reshape.

- **Freeze fixtures.** `capture_data()` already curls every `/api` endpoint in
  db-read mode. Promote a chosen capture to a **committed golden fixture set**
  (a known modlist, a known session) so L3 screenshots and L4 assertions run
  against fixed input. Today they run against whatever the game last wrote.
- **Schema-check the contract.** The `/api` JSON shapes are consumed by **both**
  surfaces (dashboard + agent). A lightweight schema check (shape, required
  keys, types) catches contract drift before it silently breaks a pane or an
  agent reader. The insights rework *changes* this contract (new usage fields),
  so the fixture set + schema is exactly what tells us what downstream moved.
- **Fill every slot (the audit's prerequisite).** An empty or half-populated pane
  *hides* bugs — you cannot judge the alignment or readability of a panel showing
  "no data yet". So the audit's primary fixture must be **generative and complete**:
  it populates *every* field the `/api` contract defines, at realistic magnitudes
  and shapes, so every pane, legend, table, chart, badge, and the new ranked-insight
  feed renders fully during a capture. The generator reads the contract (the frozen
  schema) rather than a hand-written blob, so a new field or endpoint is filled
  automatically — the same genericisation that keeps L8 alive. This is the next
  increment for `build_preview_html.py`: graduate it from "snapshot whatever the
  game last wrote" to a **fill-everything synthetic scenario** that exercises every
  affordance, generated from the contract.
- **Multiple fixtures for the hard cases.** Beyond the fill-everything baseline:
  empty state, one mod, ~30 mods (legend / colour stress), a lag-heavy session, a
  leak-heavy session (exercises the HeapLeak insight), an every-insight-firing
  session (exercises the full insight feed), and **edge-case values** that break
  layout — very long mod names (truncation / ellipsis), huge and tiny numbers
  (formatting), zero / negative. Each is a fixture L3 / L4 / L8 can target, and each
  is a state the audit should screenshot.

---

## L7 — the irreducible in-game check

No harness removes the final gate: **build + reload + enter world**, then read
both surfaces. What *can* be partially automated is the **agent surface** —
after a manual run, assert on `client.log` (expected lifecycle/encounter lines,
zero `No orig delegate` collisions, abort-clean messages absent) and on the
JSON-lines session files (schema, compaction, round-trip). The player surface
(overlay, F9, retrospective card) stays a human check. The discipline is to keep
this layer **thin** — it is the slowest loop, so push everything that *can* move
down into L1/L3/L4/L6 down there, and reserve L7 for what genuinely needs the
real runtime.

---

## How the axes relate to the invariants

Testing infrastructure is in service of the five invariants, not separate from them:

- **Invariant 2 (overhead is a budget)** → **L2** is its enforcement. An
  unmeasured hot-path change is an incomplete change; L2 is where the measurement
  becomes a gate rather than a manual habit.
- **Invariant 3 (honesty contract)** → an L1/L6 check can assert insight strings
  carry a data-strength badge and never use normative vocabulary, catching
  editorial creep mechanically.
- **Invariant 4 (abort-clean on host drift)** → **L7** asserts the abort-clean
  path actually fires and reports when a loader signature is forced to mismatch.
- **Invariant 5 (no mod-specific code)** → a cheap repo-wide L1-adjacent lint
  can flag a named-mod string literal in detector/attribution code.

These are noted as *where the invariants would be enforced*, not committed scope;
each is picked up when its axis is.

---

## Sequencing (suggested, not fixed)

The axes are independent, so order follows need rather than dependency. A
sensible default given current state:

1. **L4 + L6 together first** (the substrate): the live browser handle (L4) and
   the fill-everything synthetic fixture (L6) are the two things L8 stands on.
   Neither is large, and L4 closes the deterministic-bug class on its own.
2. **L8 immediately on top** (the headline payoff): it *reuses* L4's capture and
   L6's data and adds only the clean-slate screenshot sweep, the per-tab agent
   fan-out, the rubric, and the curated design-reference set. This is where the
   days-and-weeks of human review time get recovered, so it is the highest-leverage
   build the moment its substrate exists. L5 (visual regression) falls out of L8's
   rerun step nearly for free.
3. **L1 — largely landed with the insights rework** (108 tests as of `0.19.0`);
   grows further only as new pure logic arrives.
4. **L2 / L7** as the work that stresses them arrives (L2 the moment a hot-path
   change needs its budget gated; L7 stays thin by design).

The single biggest unlock is sequencing L4+L6 *as the means to L8*, not as ends in
themselves: the invariant assertions are useful, but the agent audit is what changes
the economics of UI work on this project.

> Open question to settle when L4 starts: do we keep the tall-printout screenshot
> path and *add* Playwright beside it, or migrate fully? Current lean: keep both
> — the printout is a good human eyeball, Playwright is the assertion engine. They
> are not redundant; they answer different questions.

---

## References

- `tools/preview/render.py` — the current static harness (L3); the file L4 extends.
- `tools/preview/build_preview_html.py` — the offline interactive preview (the page
  L4 drives and L8 screenshots); L6's "fill every slot" increment lands here.
- `Tests/` (`PerformanceProfiler.Tests.csproj`, xUnit/net8.0) — the L1 foothold (108
  tests as of `0.19.0`).
- `design/dashboard-ui-spec.md` — the as-built visual spec + its P1/P2/P3 issue list;
  the human-authored counterpart to the L8 rubric, and a seed for it.
- `design/dashboard-shots/`, `design/renders/` — example renders; the seed of the
  curated design-reference set the L8 agents pattern-match against.
- The `frontend-design` skill — the creative-direction layer for *building* UI;
  L8 is its verification counterpart.
- `context/notes/future-insights-rework.md` — drove the L1/L6 growth (now landed).
- `context/plans/ui-component-library.md` — the component library whose invariants L4
  asserts and whose consistency L8 audits (one Row hover/selection model, one
  ScrollRegion cap contract, etc.).
- The Chart.Guide chart-selection canon (match chart to data shape; the do's/don'ts)
  — the basis of L8's chart-appropriateness rubric.
- Memory `preview-harness-static-only` — the recorded limitation this plan lifts.
- Research (2026-06-23): [Playwright Python — Locator](https://playwright.dev/python/docs/api/class-locator), [Page](https://playwright.dev/python/docs/api/class-page); [Playwright vs Puppeteer vs Selenium 2026](https://use-apify.com/blog/playwright-vs-puppeteer-vs-selenium-2026); [Browserbase — why Playwright](https://www.browserbase.com/blog/recommending-playwright).
