# Dashboard Audit Harness (L4 / L6 / L8)

*Maturity: comprehensive · Stability: stable — the genericisation contract and the three axes are settled; scenario transforms and L4 invariants grow as new dashboard surfaces arrive.*

## Scope / Purpose

A self-describing, off-game testing harness for the **browser dashboard** (`Web/`), living under `tools/testing/`. It is the second of the project's two testing axes: this file covers the **L4/L6/L8** browser-driving suite; `systems/test-harness.md` covers the **L1** xUnit pure-logic suite (`Tests/`). They are independent — see the cross-reference table at the foot.

It implements the load-bearing axes of `context/plans/extensive-testing-infrastructure.md` (`tools/testing/README.md:3-4`):

| Axis | What it proves | Entry point |
|---|---|---|
| **L6** | A complete, contract-driven fixture set feeds the others reproducibly | `audit.py gen` / `contract` |
| **L4** | Deterministic interaction & layout invariants hold (overflow, sticky headers, selection, drawers, alignment, dead-space) | `audit.py assert` |
| **L8** | The rendered UI is readable, aligned, complete, well-encoded — the judgement-call bar a machine cannot assert | `audit.py capture` → agent fan-out → `audit.py synthesize` |

Like `tools/preview/render.py`, the harness reads the dashboard's **verbatim-string C# assets directly** (`harness.py:32-44`), so it reflects un-built `.cs` edits: the loop is edit → run, no game and no build. The in-game Build + Reload remains the irreducible final check (L7); `tools/testing/README.md:18-20`.

## Boundaries / Ownership

Files (every committed line under `tools/testing/`; the `.venv` and Playwright browser are gitignored and recreated, see below):

| Concern | File |
|---|---|
| CLI: `doctor` / `contract` / `gen` / `assert` / `capture` / `synthesize` | `tools/testing/audit.py` |
| L8 shared audit checklist (read by every review agent) | `tools/testing/rubric.md` |
| L8 visual-quality bar + chart vocabulary (read by every review agent) | `tools/testing/design-bar.md` |
| Asset extraction (reuses `render.py`) + endpoint discovery + loopback serve | `tools/testing/pp_testing/harness.py` |
| L6 generative fixtures + contract drift | `tools/testing/pp_testing/scenarios.py` |
| Stand up a scenario site on loopback (shared by L4 + L8) | `tools/testing/pp_testing/site.py` |
| Playwright boot + generic DOM discovery (the L4/L8 substrate) | `tools/testing/pp_testing/driver.py` |
| L4 deterministic invariants | `tools/testing/pp_testing/layout.py` |
| L8 clean-slate screenshot sweep + manifest | `tools/testing/pp_testing/capture.py` |
| Durable per-page dossier upsert + index | `tools/testing/pp_testing/pages.py` |
| Playwright dependency pin | `tools/testing/requirements.txt` |

Owns:

- The browser-driving test surface for the dashboard (everything `render.py` cannot reach: scroll, hover, select, drawer, measure).
- The discovered `/api` contract and the four generative scenarios over it.
- The durable per-page dossiers it writes into `context/pages/` (the audit owns the **findings**; the human owns each dossier's **Notes** section).

Does not own:

- The dashboard source. The harness reads `Web/Assets/*.cs` through `render.py`'s extractor (`harness.py:25,32-44`); it never edits them.
- The asset-extraction parser itself. That is `tools/preview/render.py` (`collect_consts` / `concat_order` / the un-double-`@""` reader), imported as a sibling tool (`harness.py:20-25`).
- Pure-logic tests. That is the L1 xUnit project (`systems/test-harness.md`).

## Current Implemented Reality

### The genericisation contract — the spine

Nothing hardcodes the dashboard's shape. Every structural element is **discovered**, so a 7th tab (or new pane, panel, endpoint, poll fn) is audited with zero harness change (`README.md:12-16`, `__init__.py:3-7`, `driver.py:3-10`):

| Element | Discovery mechanism | Source |
|---|---|---|
| **Tabs** | `document.querySelectorAll('.tab[data-tab]')` → `dataset.tab` | `driver.py:29,47-50` |
| **Panes** | `.tab-pane[data-pane="<key>"]` | `driver.py:54,116-118` |
| **Panels** | `.panel` within the active pane | `driver.py:55-69,116` |
| **Endpoints** | regex `/api/([a-z0-9][a-z0-9-]*)` over the extracted JS, deduped + sorted, `"api"` discarded | `harness.py:47-57` |
| **Poll fns** | `Object.keys(window)` matching `/^poll/` and `typeof === 'function'` | `driver.py:28,40` |
| **Per-tab renderer** | `'render' + Key` looked up on `window` | `driver.py:42-43` |
| **Interactive targets** | union of known selectors (`a,button,[data-mod],.slice.hit,.tl-segment,…`) **plus anything with a pointer cursor**, keeping visible leaves in reading order | `driver.py:160-174` |
| **Non-panel sections** | the pane's content-root children that are not `.panel` and are taller than a control-bar sliver (the Timeline swimlane / heatstrip / transition track) | `driver.py:238-265` |

The interactive-target discovery is the load-bearing convention: by treating *any pointer-cursor element* as a click/hover target, bespoke widgets (swimlane blocks, donut slices, legend hits, segmented buttons) are driven without ever being named (`driver.py:160-174`, `capture.py:14-16`). SVG marks with no `.click()` method are driven via a synthetic bubbling `MouseEvent` (`driver.py:191-199`).

### L6 — generative fixtures (`scenarios.py`)

The "contract" is the committed fixture set `tools/preview/fixtures/*.json` (29 files as of v0.22), a real captured session that already renders every pane (`scenarios.py:1-11`, `harness.py:29`). A scenario is a **generic recursive transform** over whatever endpoints are discovered — never a per-endpoint or per-field hand-written blob (`scenarios.py:83-119`). Four scenarios (`scenarios.py:152-157`, selectable via `--scenario`):

| Scenario | Transform | Stresses |
|---|---|---|
| `full` (default) | the committed contract verbatim (`_full`) | the realistic dense baseline the audit screenshots |
| `edge-long-names` | every name/label-ish string < 40 chars replaced with an over-long Latin or CJK value (`_long_names`, keyed on `_NAME_KEY`) | truncation / ellipsis / horizontal overflow |
| `edge-extremes` | ~1 in 3 numeric leaves pushed to `1e12 / 1e-9 / 0 / -4242.4242 / 9.999… / 1234567.89` (`_extremes`) | number formatting, bar/axis layout |
| `empty` | every array emptied (`_empty_arrays`) | every empty state |

`build_scenario` guarantees an entry for **every discovered endpoint**; one with no committed fixture gets `{}` (its empty state renders) and is reported by `contract_report`, so the gap is visible, never silent (`scenarios.py:160-171`, `71-80`). Adding a scenario means adding one function to `SCENARIOS`; you never touch an endpoint list, because there isn't one to touch (`scenarios.py:21-22`).

`contract_report` (`scenarios.py:71-80`) classifies endpoints into `covered` / `fetched_no_fixture` (needs a `render.py --live` capture) / `fixture_no_fetch` (orphan), and emits a `schema_of` type descriptor per endpoint — the drift signal that catches a contract change before it silently breaks a pane or an agent reader.

### L4 — deterministic invariants (`layout.py`)

The true/false properties a machine can assert, encoding the bug classes the static screenshot harness kept missing. Every invariant iterates the **discovered** panes; none names a tab (`layout.py:1-11`). `TOL = 2.0px` slack for sub-pixel rounding (`layout.py:12`). Implemented invariants:

| Invariant | What it asserts | Source |
|---|---|---|
| `no-page-horizontal-scroll` | document `scrollWidth` does not exceed the viewport (page-level, once) | `layout.py:15-23` |
| `no-horizontal-content-overflow` | per-pane scroll-regions / tables / `.rowlist` whose content is clipped wider than their box (when `overflow-x` is hidden/clip/auto) | `layout.py:26-43` |
| `sticky-header-stays-pinned` | after scrolling a region to its extreme, the sticky header has not drifted > 24px below the region top | `layout.py:50-85` (`72-75`) |
| `sticky-header-opaque` | the sticky header's background alpha ≥ 0.99 — the **dormant-leak class is a paint property, not geometry**, so it checks the actual cause (rows bleeding through a translucent header) rather than raw box overlap; alpha parsed from rgba/oklch/lab/hsl | `layout.py:50-85` (`54-62,76-80`) |
| `row-items-top-aligned` | side-by-side children of a row-direction grid/flex share a top edge — the "first column sits higher than the rest" class; skips centred/end/baseline alignment, `.row`/`tr`, and very-different-height pairs (multi-row spans) as legitimate | `layout.py:90-125` |
| `no-label-overlap` | visible non-nested chips/badges/labels must not overlap > 35% of the smaller area — the transition-track / time-placed-chip collision class | `layout.py:129-148` |
| `panel-fills-its-height` | a tall (≥ 220px) panel body without a chart/scroll-region filler whose content fills < 42% of its height is dead space | `layout.py:153-174` |
| `selection-has-feedback` | clicking a row in a clickable list must change its class, background, **or** open a drawer — clicks the row and diffs computed style | `layout.py:189-224` |
| `drawer-hidden-when-closed` | the `#modcard` drawer is off-screen or carries the `hidden` class at rest (opened then closed per tab to exercise the path) | `layout.py:176-186`, `261-265` |

`assert` stands up a frozen scenario site, runs every invariant across every discovered tab, prints the tabs + panel count, and exits non-zero with a per-violation list on any failure (`audit.py:94-112`, `layout.py:227-272`). The L4/L8 split is the plan's rule: if a finding can be a boolean assertion it lives here and is gated; if it needs an eye it is L8's (`rubric.md:22-26`, `layout.py:8-10`).

### L8 — agent-driven UI/UX audit (capture → review → synthesize)

The axis that takes the human out of the inner loop of "look at the dashboard, find everything wrong." A bounding-box assertion cannot tell you a panel is ugly, a number is buried, a list needs a filter, or a bar chart is the wrong encoding; a vision agent can. The fan-out (one agent per tab, in parallel) is the capability the loop is worth its weight for (`README.md:62-112`, `capture.py:1-19`).

**1. `capture`** (`capture.py:32-174`) wipes and remakes `/tmp/pp-audit/shots/` each run and, per discovered tab, shoots into `<tab>/`:

- `_whole.png` — the whole tab at the real viewport (default 1500×950 @2x; `audit.py:165-169`, `capture.py:160-161`);
- each panel cropped (`NN-title.png`) in its `default` / `--scrolled` / `--hover` / `--selected` states (selection clicks the **second** target where present, since the first row is often already the default selection and shows no delta; `capture.py:60-83`);
- each **non-panel section** (`sec-<label>.png`) cropped and driven in its `--hover` / `--selected` states (`capture.py:94-122`);
- `_after-click.png` — the whole tab after clicking the first real data target **anywhere in the pane** (master → detail, so a click in one surface that fills a detail in another is captured; `capture.py:124-137`, `driver.py:322-342`);
- `_drawer.png` — the mod-card drawer once per tab that offers one (`capture.py:138-143`);
- `manifest.json` describing every shot, the per-tab `doc` to update, the rubric/design-bar paths, and the contract-coverage block (`capture.py:157-173`).

**2. Review (the fan-out).** For each tab in the manifest, one vision-capable agent reads `tools/testing/rubric.md` + `tools/testing/design-bar.md` (the shared standard), every screenshot under `/tmp/pp-audit/shots/<tab>/`, and the existing `context/pages/<tab>.md` (so it confirms/updates rather than re-reports), then writes `/tmp/pp-audit/findings/<tab>.json` as `{ findings: [{ severity, category, panel, state, title, what, fix }] }` (`README.md:90-104`). The agent count scales with the discovered tab list. `capture` prints the exact per-tab paths to hand each agent (`audit.py:130-135`).

**3. `synthesize`** (`audit.py:138-158`, `pages.py:150-188`) reads every `findings/<tab>.json` and folds it into the durable, evolving page dossiers (`context/pages/<tab>.md`) plus `context/pages/_index.md`. Each finding carries a **stable id** (`sha1(tab|category|panel|what[:80])[:6]`, `pages.py:30-33`), so a re-run updates it in place; one not re-flagged this run moves to a "Not seen last run" section rather than being deleted (a fix closes it, the next agent confirms it); the hand-written **Notes** section is preserved verbatim (`pages.py:36-50,90-99`). A new tab's dossier is created from a skeleton the first time it appears in a capture (`pages.py:11-13`).

### Rubric & design-bar (the L8 shared standard)

- `rubric.md` — principle-based checklist (layout/alignment, readability, colour/encoding, affordance, chart-appropriateness, hierarchy, consistency, **honesty**) with P1/P2/P3 severities and the finding JSON shape. Principle-based so a new tab inherits the whole rubric for free. The honesty section (Invariant 3: normative vocabulary in player copy, or a number shown without its data-strength badge) is always P1 (`rubric.md:92-98`).
- `design-bar.md` — the visual-quality ceiling: the chart vocabulary to grow into (radial gauge, nested donut, area+threshold, sankey, heatmap, scatter, waffle, swimlane, KPI+sparkline, small multiples), the do's/don'ts, and the house style (monochrome chrome / colourful data, one component vocabulary, descriptive-never-prescriptive copy). The same bar the `frontend-design` skill builds against, so creation and verification share one standard (`design-bar.md:6-8`).

## Key Interfaces / Data Flow

```
edit Web/Assets/*.cs  (no build)
   │
   ▼
harness.extract_assets()  ── reuses tools/preview/render.py ──►  (css, js, html)
   │                                                  harness.discover_endpoints(js) ─► /api contract
   ▼
scenarios.load_contract_fixtures()  ◄── tools/preview/fixtures/*.json (29)
scenarios.build_scenario(name, eps, fx)  ─►  {endpoint: obj}  (every discovered endpoint guaranteed)
   │
   ▼
site.stand_up(scenario)  ─►  loopback HTTP on 127.0.0.1:27299  (index.html + dashboard.css/js + api/*.json)
   │
   ▼
driver.Dashboard(url)  ── Playwright/chromium, booted (_BOOT_JS warms every tab's polls) ──┐
   │                                                                                        │
   ├──►  layout.run(dash)   L4: every invariant × every discovered tab  ─► PASS / violations (exit≠0)
   │
   └──►  capture.capture(dash, …)   L8: screenshot sweep  ─►  /tmp/pp-audit/shots/<tab>/*.png + manifest.json
                                                                   │
                              agent fan-out (1 / tab, reads rubric + design-bar + shots) ─► /tmp/pp-audit/findings/<tab>.json
                                                                   │
                                                          pages.synthesize(manifest, findings)
                                                                   ▼
                                                   context/pages/<tab>.md  +  context/pages/_index.md
```

Public CLI surface (`audit.py:8-17,161-181`), driven by the suite's own venv python:

```sh
A=tools/testing/.venv/bin/python; T=tools/testing/audit.py
$A $T doctor                        # deps + browser + extraction self-check
$A $T contract                      # discovered /api contract + fixture coverage/drift
$A $T gen --scenario edge-extremes  # write a scenario's fixtures to a dir to inspect
$A $T assert  [--scenario]          # L4: drive the dashboard, check invariants (exit≠0 on fail)
$A $T capture [--scenario]          # L8: clean-slate screenshot sweep + manifest
$A $T synthesize                    # fold agent findings into context/pages/<tab>.md
```

`--scenario` ∈ {`full`, `edge-long-names`, `edge-extremes`, `empty`}; `--port` (27299), `--width` (1500), `--height` (950) on `assert`/`capture` (`audit.py:165-169`).

## Implemented Outputs / Artifacts

| Path | Lifecycle | What |
|---|---|---|
| `/tmp/pp-audit/site/<scenario>/` | ephemeral | the assembled scenario site served on loopback (`site.py:19-24`) |
| `/tmp/pp-audit/shots/<tab>/*.png` + `manifest.json` | ephemeral, wiped each run | the L8 screenshot sweep (`capture.py:34-36`) |
| `/tmp/pp-audit/findings/<tab>.json` | ephemeral | per-tab agent findings (the review hand-off) |
| `/tmp/pp-audit/gen/<scenario>/*.json` | ephemeral | `gen` output for inspecting L6 fixtures (`audit.py:86`) |
| `context/pages/<tab>.md` | **durable, committed** | per-page dossier: discovered panes + accumulating findings (auto) + Notes (hand-owned) |
| `context/pages/_index.md` | **durable, committed** | audit index: tabs, panes, open-finding counts, contract coverage |

As of the last committed audit (`context/pages/_index.md`): **6 tabs** (Summary, Timeline, Lag, Insights, Self, Memory), **29 endpoints covered**, scenario `full`, viewport 1500×950@2x.

## Known Issues / Active Risks

- **The `.venv` + Playwright browser are gitignored and machine-local.** A fresh checkout has the suite source but not the runtime; `assert`/`capture` fail until the one-time setup recreates them (`README.md:24-39`, `.gitignore`). `audit.py doctor` is the guard — it self-checks playwright importability, the chromium binary under `.venv/ms-playwright`, and asset-extraction/discovery before a run (`audit.py:43-67`).
- **The harness imports `tools/preview/render.py` as a sibling tool** via `sys.path` injection (`harness.py:20-25`). If `render.py`'s extractor API (`collect_consts` / `concat_order` / `read` / `REPO`) changes, the harness breaks at extraction time, surfaced by `doctor`. This coupling is deliberate (the dashboard the suite tests is byte-identical to the one `render.py` produces) but undeclared beyond the import.
- **L4 invariants run against a frozen scenario, not live game data**, so a `FAIL` is a real regression, not a data shift (`layout.py:5-7`). The flip side: an invariant only catches what the chosen scenario's data shape exercises — a bug that only appears at a magnitude no scenario produces is invisible to L4 (mitigated by the edge scenarios, not eliminated).
- **L8 findings are model-judgement, not deterministic.** A re-run with a different vision agent can surface or drop a borderline P3. The stable-id + "Not seen last run" mechanism (`pages.py`) keeps that churn legible rather than silently rewriting the dossier, but the dossier is a living judgement record, not a fixed contract.

## Partial / In Progress

- **L5 (visual regression) is not built.** The plan notes it "falls out of L8's rerun step nearly for free" (`extensive-testing-infrastructure.md:79,205-208`), but there is no committed screenshot-baseline + diff today. The `capture` browser handle is the substrate it would build on.
- **`edge-extremes` / `empty` / `edge-long-names` are exercised but not yet wired into a committed assertion suite** beyond `assert --scenario`. Running L4 across all four scenarios is a manual invocation, not a single gate.

## Planned / Missing / Likely Changes

- **L2 (hot-path overhead budget)** — a different axis entirely; lives in the C# benchmark space, not this browser harness. The persistence benchmark exists in the L1 project; per-tick alloc/timing assertions do not (`extensive-testing-infrastructure.md:76`).
- **L7 (in-game integration smoke)** — the irreducible final check (build + reload + enter world, read both surfaces). Partially automatable on the agent surface (`client.log` + JSON-lines assertions) but deliberately kept thin; not part of this harness (`extensive-testing-infrastructure.md:81,399-409`).
- **More L4 invariants** as recurring layout-bug classes surface — the pattern is "one invariant per bug class the static harness kept missing," added so it does not have to be hand-found again (`layout.py:245-247`).

## Durable Notes / Discarded Approaches

- **Reusing `render.py`'s extractor over re-implementing the `@"..."` parser.** The harness deliberately imports `render.collect_consts` / `concat_order` rather than re-parsing the C# verbatim strings, so the dashboard it tests is byte-identical to the one `render.py` / `build_preview_html.py` produce (`harness.py:1-11`). Re-implementing would have created a second source of truth that could drift.
- **Playwright (Python) chosen over Puppeteer / Selenium / Cypress** (researched 2026-06-23): first-class Python bindings slot straight into the existing Python harness; auto-waiting, `bounding_box()`, scroll helpers, trace viewer. Puppeteer is Node-only (a language split for no gain); Selenium is slowest/legacy; Cypress is its own test-runner universe (`extensive-testing-infrastructure.md:128-141`).
- **The sticky-leak invariant checks paint, not geometry.** An earlier framing would have asserted "no row box above the sticky header box," but rows sliding under an *opaque* stacked header is normal and correct. The real bug is a *translucent* header letting rows bleed through, so the invariant checks the header's background alpha — the actual cause (`layout.py:46-49,76-80`).
- **Selection capture clicks the second target, not the first.** The first row is often already the default selection, so clicking it shows no delta and reads as "no selection feedback" when there is some; the second target makes the change visible (`capture.py:75-80`).

## Obsolete / No Longer Relevant

Nothing. The harness is current as of v0.22.0.

## Cross-references

- `systems/test-harness.md` — the **other** testing axis: the L1 xUnit pure-logic suite (`Tests/`). The two are independent: L1 proves detection/ranking/attribution/persistence math on synthetic input with no browser; L4/L6/L8 prove the dashboard's layout, interaction, and visual quality with no game. Neither imports the other.
- `context/plans/extensive-testing-infrastructure.md` — the layered-axes plan this harness implements (the L1–L8 map, the L4-vs-L8 distinction, the genericisation contract, the sequencing rationale).
- `systems/web-dashboard.md` — the subject under test: the `Web/` SPA, its `/api` endpoints, and the verbatim-string asset pipeline the harness extracts.
- `tools/preview/render.py` — the static-only L3 harness whose extractor this suite reuses and whose interaction ceiling it lifts (memory `preview-harness-static-only`).
- `context/pages/` — this harness's durable output: one dossier per discovered tab plus `_index.md`.
- `build.txt` `buildIgnore` carries `tools\*` (and `context\*`), so neither the harness nor its dossiers ship in the `.tmod`.

## 2026-07-07: the selection-feedback rule rebuilt on a live catch

The rule failed on correct behaviour: it clicked the FIRST clickable row,
which Observatory auto-selects at render — the one row whose state cannot
change. Repro (scratch Playwright + console capture) proved the product right
and the rule wrong. Rebuilt: pick the first NON-selected row, track it BY
INDEX across the click (a first rewrite that re-picked per-side compared
different rows and went 1→3 false failures — the recorded dead end), dispatch
a bubbling MouseEvent (`.click()` throws on `[data-mod]` SVG slices), and
recognise any visible `.drawer` or a popup card (the insights kanban opens
`#ins-drawer`, not `#modcard`). 38 panels PASS post-fix (`2f2fc1c`).
