# Plan — UI/UX Overhaul (S16 + S18 + S19 + S20)

> Slots: S16 gradient ribbon, S18 popup cards, S19 per-tab quality, S20 warming
> states. Bug ledger source: plans/ui-ux-audit.md (X1-X8 + per-tab rows, live
> capture 2026-07-07). Version target: 0.35.0.
> Protocol per the user's directive: **pass 1** — page-by-page enumeration of
> every improvement in chat; **pass 2** — implement all of it.

## The three new primitives (build once, apply everywhere)

### P-A · Warming/disabled state component (S20, X4)
One JS helper `renderPanelState(panel, {kind, minutesOfData, needed, reason})`
rendering the shared visual language: `warming` (session younger than the
surface's minimum), `empty-honest` ("no variance events — but see the
headline", for Lag), `disabled` ("off in config", pairs with S23). Every panel
declares its minimum-data threshold; no more confident verdicts from 3 minutes
(97% dormant, 100% usage, grey slabs).

### P-B · Session gradient ribbon (S16, the user's design)
The Timeline strip (and a slim variant above Summary) becomes the session's
per-minute heat gradient driven by the heatmap buckets (real cadence after
honesty-completion): colour = severity ramp (smooth → amber → red), full
session span, time axis with minute ticks + start/now labels, hover = minute
tooltip (avg/worst/spikes/boss), click = minute drill popup (P-C). Replaces
the single unreadable block + duplicate-number caption (T1/T2). Legend:
boss-segment underline strip beneath the ribbon.

### P-C · Popup card system (S18)
A modal card component (`Js.Cards.cs`): centred, dismissible (esc/backdrop),
stacked above the pane, one at a time; content renderers per card type:
- **Boss/segment report card** (flagship): duration, avg/worst frame during,
  per-mod top-5 during the segment, deaths, loot events, verdict line — the
  screenshot-able artefact.
- **Stall-cluster card**: cause, duration, GC anatomy, contributors, context.
- **Minute card**: the ribbon drill — that minute's frames, events, top movers.
- **Mod card**: reuses the existing drawer content in popup form where a
  drawer is disproportionate (e.g. from the report ribbon).
Drawers stay for list-adjacent browsing (Observatory, Insights); popups serve
moment-shaped things (the user's explicit ask: "some things can be a literal
popup card like bossfights").

## The audit ledger → implementation map

| Ledger | Fix wave | Note |
|---|---|---|
| X1, X2, X3 | honesty-completion plan | measurement, not UI |
| X4 warming states | P-A | |
| X5 null-vs-zero | shared `fmtMaybe()` formatter: measured-zero ⇒ `0`, absent ⇒ `—` + tooltip | one convention, all tables |
| X6 footstrip "1-6" | trivial fix, dynamic from tab count | |
| X7 | honesty-completion | |
| X8 reset-db wrap | topbar layout fix (flex row, controls right-aligned beside brand) + the export-report button joins it | |
| S1 grey slabs | P-A warming on cost stream (< 2 buckets) | |
| S3 corner numbers | axis labels "min/max ms" | |
| S5 minute panel | superseded by P-B ribbon on Summary too | |
| T1/T2 strip | P-B | |
| T3 clipped chip | events row becomes left-flowing chip list, ellipsis + tooltip, newest-first | |
| T4 biome bar width | fix open-segment width calc (span = elapsed share, not 100%) | |
| T5 empty lanes | collapse to one compact line each | |
| T6 "no deaths" floater | into a panel frame with the deaths list's empty state | |
| T7 dead space | pane bottom padding audit | |
| T8 join/join | distinct chronicle tags (join/open) | |
| L1/L2 | honesty headline + P-A empty-honest states | |
| O1 "0 items" reads as roster | sub-line labels "used"; hidden until any usage observed (P-A) | |
| O2 "100.0% usage" | denominator chip: "of 184 usage-ticks observed" + warming gate | |
| O3 active/dormant at min 3 | warming gate on the AT A GLANCE verdicts | |
| O4 | X5 formatter | |
| I2 truncated kanban titles | title attr tooltips + slightly wider min column | |
| I3 scroll affordance | fade-edge gradient + scroll hint chevron | |
| I4 LOW flood | confidence-first default sort; LOW columns collapsed behind a count chip | |
| SE1/SE2 | memory-guard plan surfaces | |
| M2 table cut | scroll container with sticky header (existing pattern) | |
| M3 legend truncation | min-width + ellipsis + title | |
| M5 | memory-guard | |

## Enhancement pass (pass-1 chat enumeration feeds this; expected shape)

Beyond fixes: motion (panel enter transitions, value-change flashes on KPI
numbers, ribbon minute pulse on live edge), colour language (one severity ramp
token set shared by ribbon/KPI tags/heatmap — today's ad-hoc greens/ambers
unify), intuitiveness (every panel title gains a one-line "what am I looking
at" subtitle on hover — the audit showed panes whose meaning wasn't legible),
new dataviz (Observatory split bars from S01; Lag deficit gauge; Self arm-
history mini-table). Pass 1 will enumerate per pane in chat before pass 2
implements; anything pass 1 surfaces beyond this list lands in the same wave.

## Test plan

- Every fixture scenario × every tab: screenshot + corrected-selector overflow
  sweep (zero unintended clips — T3/X8 become regression-proof).
- Warming matrix: `warming` scenario ⇒ every gated panel shows P-A state;
  `healthy60` at 20 min ⇒ none do.
- Popup cards: open/close/esc/backdrop via Playwright; boss card content
  asserts against the fixture's segment data.
- Ribbon: bucket → colour mapping unit-tested in JS-side contract (fixture
  minute values ⇒ expected classes); axis labels present.

## Acceptance

1. All 30 per-tab ledger rows either fixed (with the fixing commit named in
   the ledger) or explicitly deferred with reason.
2. The slow-mo fixture's dashboard contains zero false-calm surfaces (ties to
   honesty acceptance).
3. Boss popup card renders from fixture data and is screenshot-archived.
4. One severity ramp used by ribbon, tags, and heatmap (grep-checked tokens).
