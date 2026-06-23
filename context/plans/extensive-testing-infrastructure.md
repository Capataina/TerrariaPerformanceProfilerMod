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
> Status: **PROPOSED — not started.** This is a directional strategy doc, kept
> current as each axis advances. Some axes already have a foothold (see the
> state column); the load-bearing new work is the interaction/layout axis (L4).
>
> Date opened: 2026-06-23. Mod version at open: `0.18.1`.

---

## Why this exists

Two pressures created this plan:

1. **A concrete gap.** The dashboard preview harness (`tools/preview/render.py`)
   is **static-only** — it drives Chrome in one-shot `--headless=new
   --screenshot=` mode, so the only thing that comes back out is a flat PNG. It
   cannot scroll, hover, select, or measure an element. A whole class of bugs we
   keep paying for in-game round-trips (text leaking past a sticky header, a
   chip positioned off-screen, two regions overlapping, a scroll region that
   does not cap) is **invisible to a pixel dump** and only shows up after build +
   reload + re-enter world. Those bugs are not subtle in principle; they are
   subtle *to our current instrument*.

2. **Evolving needs.** The insights rework will reshape the **attribution layer**
   (how "usage" is measured — see `context/notes/future-insights-rework.md`).
   That stresses a completely different axis than dashboard layout: pure-logic
   correctness on synthetic input. If "testing" means only "the screenshot
   harness", that work has no home. The strategy has to name every axis so each
   change lands on the right one.

The generic frame below is the answer to both: a map of testing axes, what each
proves, where it stands today, and the next concrete increment for each.

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

Each row is independent. A change picks the axis it stresses and grows that
layer. The insights rework, for instance, lands mostly on **L1** (new attribution
math) and **L6** (new fixtures + a reshaped `/api` contract), and *benefits from*
**L4** (interaction tests on the reworked Insights tab) without forcing any of the
other layers to change.

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
- **Multiple fixtures for the hard cases.** Empty state, one mod, ~30 mods
  (legend/colour stress), a lag-heavy session. Each is a fixture L3/L4 can target.

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

1. **L4 first** (highest leverage now): it closes the bug class that is actively
   costing in-game round-trips, and it unlocks L5 nearly for free.
2. **L6 alongside L4** (it is the prerequisite for L4 being *honest* — freeze at
   least one golden fixture before writing assertions).
3. **L1 expansion with the insights rework** (it arrives when that work does; the
   foothold already exists, so it is growth, not a new build).
4. **L2 / L5 / L7** as the work that stresses them arrives.

> Open question to settle when L4 starts: do we keep the tall-printout screenshot
> path and *add* Playwright beside it, or migrate fully? Current lean: keep both
> — the printout is a good human eyeball, Playwright is the assertion engine. They
> are not redundant; they answer different questions.

---

## References

- `tools/preview/render.py` — the current harness (L3); the file L4 extends.
- `Tests/` (`PerformanceProfiler.Tests.csproj`, xUnit/net8.0) — the L1 foothold.
- `context/notes/future-insights-rework.md` — the change that will drive L1/L6 growth.
- `context/plans/ui-component-library.md` — the component library whose invariants L4 would assert (one Row hover/selection model, one ScrollRegion cap contract, etc.).
- Memory `preview-harness-static-only` — the recorded limitation this plan lifts.
- Research (2026-06-23): [Playwright Python — Locator](https://playwright.dev/python/docs/api/class-locator), [Page](https://playwright.dev/python/docs/api/class-page); [Playwright vs Puppeteer vs Selenium 2026](https://use-apify.com/blog/playwright-vs-puppeteer-vs-selenium-2026); [Browserbase — why Playwright](https://www.browserbase.com/blog/recommending-playwright).
