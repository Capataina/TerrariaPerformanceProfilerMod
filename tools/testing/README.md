# Dashboard testing suite (L4 / L6 / L8)

A layered, **self-describing** test harness for the browser dashboard. It implements
the load-bearing axes of `context/plans/extensive-testing-infrastructure.md`:

| Layer | What it proves | Entry point |
|---|---|---|
| **L6** | A complete, contract-driven fixture set feeds the others reproducibly | `audit.py gen` / `contract` |
| **L4** | Deterministic interaction & layout invariants hold (overflow, sticky headers, selection, drawers) | `audit.py assert` |
| **L8** | The rendered UI is readable, aligned, complete, well-encoded — the judgement-call bar a machine cannot assert | `audit.py capture` → agent fan-out → `audit.py synthesize` |

**Nothing here hardcodes the dashboard's shape.** The tab list is discovered from the
DOM (`.tab[data-tab]`), the endpoint list from the JS (`/api/<name>`), the panels from
`.panel` within each pane, the poll functions from `window`. Add a 7th tab, a new
panel, or a new endpoint and the whole suite picks it up with **no code change** — a
new tab even gets its own page dossier created automatically.

It reflects **un-built `.cs` edits** (it reads the verbatim-string assets directly, like
`render.py`), so the loop is edit → run, with no game and no build. The in-game Build +
Reload stays the final check (L7).

---

## Setup (one-time)

```sh
python3 -m venv tools/testing/.venv
tools/testing/.venv/bin/python -m pip install -r tools/testing/requirements.txt
PLAYWRIGHT_BROWSERS_PATH="$PWD/tools/testing/.venv/ms-playwright" \
  tools/testing/.venv/bin/python -m playwright install chromium
```

The browser lands inside `.venv/ms-playwright` (the driver points Playwright there by
default), so the suite is self-contained. The whole `tools/` tree is `buildIgnore`d, so
none of it ships in the `.tmod`.

```sh
tools/testing/.venv/bin/python tools/testing/audit.py doctor   # verify the install
```

---

## Commands

```sh
A=tools/testing/.venv/bin/python; T=tools/testing/audit.py

$A $T doctor                       # deps + browser + extraction self-check
$A $T contract                     # discovered /api contract + fixture coverage/drift
$A $T gen --scenario edge-extremes # write a scenario's fixtures somewhere to inspect
$A $T assert                       # L4: drive the dashboard, check invariants (exit≠0 on fail)
$A $T capture                      # L8: clean-slate screenshot sweep + manifest
$A $T synthesize                   # fold agent findings into context/pages/<tab>.md
```

`--scenario` (L6) is `full` (default), `edge-long-names`, `edge-extremes`, or `empty`.
A scenario is a generic transform over the discovered contract; add one by adding a
function to `pp_testing/scenarios.py::SCENARIOS` — you never edit an endpoint list.

---

## The L8 audit loop (capture → review → synthesize)

```
   capture            review (fan-out, 1 agent / tab)          synthesize
 ┌───────────┐      ┌──────────────────────────────┐      ┌──────────────────┐
 │ screenshot│      │ each agent reads rubric.md +  │      │ upsert findings  │
 │ every tab │ ───► │ design-bar.md + its shots +   │ ───► │ into evolving    │
 │ pane,state│      │ context/pages/<tab>.md,       │      │ context/pages/   │
 │ + manifest│      │ writes findings/<tab>.json    │      │ <tab>.md + index │
 └───────────┘      └──────────────────────────────┘      └──────────────────┘
```

1. **`capture`** drives the dashboard against the chosen scenario and writes, into the
   clean-slate `/tmp/pp-audit/shots/` (wiped each run):
   - every tab's `_whole.png`;
   - each panel cropped (`NN-title.png`) in its `default` / `--scrolled` / `--hover` /
     `--selected` states;
   - each **non-panel section** (`sec-<label>.png`, e.g. the Timeline swimlane /
     heatstrip / transition track) — bespoke blocks outside `.panel` chrome, cropped and
     driven in their `--hover` / `--selected` states too;
   - `_after-click.png` — the **whole tab after clicking the first data target anywhere
     in the pane** (master → detail), so a click in one surface that fills a detail in
     another is captured (clicking a swimlane block populates "segment detail", etc.);
   - each tab's `_drawer.png`;
   - and `manifest.json`.
   Interactivity is discovered generically (any element with a pointer cursor or a known
   interactive selector), so bespoke click targets are driven without being named.

2. **Review (the agent fan-out).** For each tab in the manifest, spawn one vision-
   capable agent. The fan-out is the capability OpenDesign lacks (no subagent
   orchestration), and the whole reason this axis is worth its weight. Each agent:
   - reads `tools/testing/rubric.md` + `tools/testing/design-bar.md` (the shared standard),
   - reads every screenshot under `/tmp/pp-audit/shots/<tab>/`,
   - reads the existing `context/pages/<tab>.md` (so it confirms/updates, not re-reports),
   - writes `/tmp/pp-audit/findings/<tab>.json` as:
     ```json
     { "findings": [
       { "severity": "P1|P2|P3", "category": "layout-alignment|readability|colour-encoding|affordance|chart-fit|hierarchy|consistency|honesty",
         "panel": "<panel title or 'page'>", "state": "default|scrolled|selected|whole|drawer",
         "title": "<short label>", "what": "<what is wrong, concretely>", "fix": "<suggested fix>" }
     ] }
     ```
   The agent count scales with the discovered tab list — six tabs today, seven tomorrow.

3. **`synthesize`** reads every `findings/<tab>.json` and folds it into the durable,
   evolving page dossiers (`context/pages/<tab>.md`) plus `context/pages/_index.md`.
   Each finding has a stable id, so a re-run updates it in place; one not re-flagged
   moves to "Not seen last run" rather than being deleted; the hand-written **Notes**
   section in each dossier is preserved verbatim.

`capture` prints the exact per-tab paths to hand each agent.

---

## Per-page dossiers (`context/pages/`)

One durable markdown file per discovered tab, accumulating that page's known bugs,
improvement ideas, discovered panes, and free-form notes across audit runs. The suite
owns the findings; you own the Notes. A new tab's dossier is created from a skeleton
the first time it appears in a capture — the "add a 7th page, change no code" contract.

## Files

```
tools/testing/
  audit.py            CLI (doctor / contract / gen / assert / capture / synthesize)
  rubric.md           L8 shared audit checklist (read by every review agent)
  design-bar.md       L8 visual-quality bar + chart vocabulary (read by every agent)
  requirements.txt    playwright
  pp_testing/
    harness.py        asset extraction (reuses render.py) + endpoint discovery + serve
    scenarios.py      L6 generative fixtures + contract drift
    site.py           stand up a scenario site on loopback
    driver.py         Playwright boot + generic DOM discovery
    layout.py         L4 deterministic invariants
    capture.py        L8 clean-slate screenshot sweep + manifest
    pages.py          durable per-page dossier upsert
```
