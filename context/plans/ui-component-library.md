# Plan — Genericise the dashboard into a shadcn-style component library

> Goal: stop coding each pane individually. Build the dashboard out of a small
> set of **reusable, canonical UI components** so that every panel shares one
> implementation of spacing, scrolling, selection, hover, empty state, and
> charting, and the *only* thing that varies per surface is the **content**
> inside. When that holds, a whole class of bugs (leaking strips, scroll regions
> that do not scroll, mis-centred placeholders, inconsistent hover/selection
> colours, charts each pinned to their own axis) becomes structurally
> impossible, and when something does break it breaks in one place we already
> know how to look at, not in a pane-specific reimplementation.
>
> Adopt a **shadcn-rooted design language**: shadcn's semantic token system plus
> its OKLCH perceptually-grounded colour scheme, the cleanest modern baseline to
> build on.
>
> Status: **FLESHED OUT — evidence-based build brief, not yet started.** This
> file states the why, the full target component set, the project-specific
> idiom, the bugs the migration kills, and the build sequence. It is grounded in
> a line-by-line read of every Web/Assets file (see the duplication census).
>
> Date opened: 2026-06-22. Fleshed out: 2026-06-23. Mod version: `0.16.1`.

---

## Why this exists (the root cause)

Every recurring dashboard bug this project keeps re-finding has the **same**
shape: a surface was hand-coded with its own spacing / scroll / state / chart
styling, and that bespoke copy drifted from every other surface's bespoke copy.
The fix each time is local; the disease is structural. The 2026-06-22 visual
sweep is the evidence: five separate "bugs", one cause.

| Symptom (v0.16 sweep) | Bespoke cause | What a shared component would have done |
|---|---|---|
| Memory strip leaks past the right edge | The strip sat flush to a panel that carries no body padding; the table/header inset differently | A `Panel` with one padded body slot, every child inset identically |
| Observatory "selected" bar green; the table beside it selects blue | Each surface invented its own `.selected` / `.sel` rule with its own colour | One `selectable-row` treatment; "selected" is one token everywhere |
| Observatory list stretches forever (no scroll on 150 mods) | The card list relied on a parent height nothing actually bounded | A `ScrollRegion` that owns `max-height` + overflow as a contract |
| Top bar clips when content scrolls to the end | Document-level overscroll on a fixed-height grid the shell did not fully pin | An `AppShell` that owns the fixed/scroll split once |
| Cascade hover draws extra top/bottom edges; gold rows will not go blue | `box-shadow` left-bar in one place, `border-left` in another, `:hover` losing a specificity race | One `Row` with one hover/selection model and a reserved tinted left bar |

`Css.Coherence.cs` is the **embryo** of the fix: it already pulled nine
divergent empty-state classes onto one canonical `.empty`, then the selection
accent. The plan generalises that exact move from "empty states + selection" to
**every primitive the dashboard draws**.

> The mental model the user named: *"think of it like our very own shadcn
> component library"* — panels, tables, lists, drawers, chips, bars, and charts
> all come from one place; content is the only variable; a spacing or centring
> issue can then only mean the content is broken, and you know exactly where to
> look.

---

## The duplication census (the evidence)

A full read of `Web/Assets/` (20 CSS partials, 18 JS partials, 8 HTML
fragments) found the same handful of concepts re-implemented per tab. This is
the quantified case for the library. Counts are distinct implementations of one
concept, with representative file:line anchors.

| Concept | Impls | Where (representative anchors) |
|---|---:|---|
| **Panel chrome** (border + radius + panel-2 bg + header) | 7+ | `.panel` (Css.Panels, the canonical one) vs reinvented `.ins-dormant/.ins-cross/.ins-scatter/.ins-matrix` (Css.Insights), `.lag-heatmap/.lag-clusters/...` (Css.Lag:59), `.tl-detail/.tl-attendance/.tl-chronicle` (Css.Timeline:214), `.modtable` (Css.Mods:46), `.self-body` (Css.Self:43) |
| **Scroll region** (`max-height`/`flex:1` + `overflow-y:auto`) | ~12 | `.dor-scroll/.obs-scroll/.det-scroll/.sc-scroll/.mx-scroll` (Css.Insights), `.lag-table-wrap/.lag-causality-list` (Css.Lag), `.mem-table-wrap` (Css.Memory:34), `.modtable/.mc-body`, `.nowlist/.events` (Css.Summary), `.tl-chronicle` (Css.Timeline:296) |
| **Section header** (uppercase muted small label) | 7 | `.panel-h` (canonical), `.dor-h/.cc-h/.sc-h/.mx-h` (Css.Insights:81), `.lag-section-h` (Css.Lag:66), `.mem-sect-h` (Css.Memory:61), `.modtable-head` (Css.Mods:32), `.tl-detail h4` (Css.Timeline:223), `.mc-section h3` (Css.ModCard) |
| **Stat / label-value** | 6 | `.statline` (shared, canonical), `.mem-card`+`.mem-stat` (Css.Memory), `.mc-stat` (Css.ModCard), `.hero-stat`+`.self-row` (Css.Self), `.lag-chain-stats` (Css.Lag), `.kpi-sub` (Css.Kpis) |
| **KPI tile** | 3 | `.kpi` hero+tag+subs+spark (Css.Kpis), `.ins-kpi .tile` ring gauge (Css.Insights:35), `.lag-kpi-cell` label/value/unit (Css.Lag:39) |
| **Drawer / detail pane** | 4 | `.modcard` slide-in (Css.ModCard), `.mem-drawer` (Css.Memory:54), `.ins-detail` aside (Css.Insights:163), `.lag-detail` strip (Css.Lag:109), `.tl-detail` (Css.Timeline:223) |
| **Inline value bar** (`<span class=bar><span style=width>`) | 7+ | `.cellbar` (shared helper, **used by almost nobody**) vs `.modrow .bar` (Css.Mods:92), `.mc-cat-row .br` (Css.ModCard), `.footprint-bar`/`.hd-row .bar` (Css.Self:62/79), and the shared `cellBar()` proper (Insights/Lag/Memory/Timeline) |
| **Segmented control** | 2 | `.segctl` (Css.Panels:37; Memory basis toggle, Mods sort) vs `.chart-toggle` (Css.ChartToggle:29; Summary frame ms/fps) |
| **Sortable-header behaviour** | 3 | `sortableHead()` (Js.Insights:98), `lagSortTh()`+`lagApplySort()` (Js.Lag:70), inline sort in `renderSummaryModsForced()` (Js.Mods) |
| **Legend** | 3 | `.bar-legend` (shared) vs `.donut-legend` (Css.Summary:89), `.heatmap-legend` (Css.Heatmap:55) |
| **Line / area chart** | 2 | `renderFrameChart()` hand-rolled SVG (Js.Summary:62) vs GC chart via `seriesPaths()` (Js.Lag:433). Only the second uses the shared helper |
| **Sparkline** | 5 | `drawSpark()` (Js.Summary:266), spike-marker variant `drawSpikeMarkers()` (Js.Summary:280), `setKpi` spark (Js.Kpis:133), `renderModSparkInline()` (Js.Mods:127), and `seriesPaths()` itself |
| **Bar / column strip** | 2 | per-minute heat strip `renderTimelineHeatstrip()` (Js.Timeline:153, vertical) vs lag rhythm `lag-hist` (Js.Lag:563, horizontal) |
| **Gauge** | 2 | Self half-circle `renderSelfGauge()` (Js.Self:75, **hardcoded hex**) vs Insights ring `.ring` stroke-dasharray (Css.Insights:43) |
| **Heatmap** | 2 | categorical 2D `.rheat` (Css.Components:55, shared) vs calendar/minute `.hm-cell` grid (Css.Heatmap:24) |
| **Table system** | 2 | `.dtable` (shared, good) vs the bespoke 7-col grid tree `.modtable-head`/`.modrow`/`.mod-tree` (Css.Mods) which ignores `.dtable` entirely |

The reading also surfaced the one good seed we build the chart layer on:
`seriesPaths(values, opts)` + `niceScale(values)` in `Js.Components.cs:77-109`
already do auto-scaled line/area SVG correctly. Almost nothing uses them. That
is the gap: the right primitive exists, adoption never happened.

---

## What already exists and is good (keep, do not rebuild)

The library is mostly consolidation, not greenfield. These already work and are
the foundation the rest snaps onto:

- `emptyState()` + `.empty` (`Js.Components`, `Css.Coherence`) — **done**, the proof the pattern works.
- `.chip` (+ `.good/.warn/.bad/.cool`) — `Css.Components:68`.
- `.dtable` styling (sticky header, perf-tint `.t0..t4`, `tr.sel`) — `Css.Components:27`. Good; needs universal adoption + the sort behaviour folded in.
- `.split-bar` / `splitBar()` / `splitLegend()` — `Css.Components:15`, `Js.Components:26`. Good; needs the name collision fixed (see Bugs).
- `.cellbar` / `cellBar()` — exists, just underused.
- `seriesPaths()` / `niceScale()` / `setHTML()` — `Js.Components:67-109`. The chart core seed and the scroll-preserve contract.
- `Css.Panels` `.panel/.panel-h`, `.segctl`, `.mini-btn`, `.filter-input` — good primitives; `.panel` and `.segctl` just need to become the *only* implementations.

---

## The component idiom in THIS project (architecture)

The dashboard ships as C# verbatim-string constants concatenated into one SPA
(`DashboardAssets.cs`). There is **no asset pipeline and no JS framework**, and
there will not be one: it would break the single self-contained `.tmod`
promise. So "component" here does **not** mean a React/Vue component. It means a
fixed triple:

```
A component =  one CSS class block      (in a Web/Assets/Css/Css.*.cs partial)
            +  one JS render function   (returns an HTML string; in Js.*.cs)
            +  a documented `opts` contract (the inputs; data is the only variable)
```

This is exactly the shape the good existing helpers already have. The pattern to
replicate everywhere:

```js
// opts: { title, sub?, actions?:html, body:html, scroll?:bool, scrollId?:string }
function panel(opts) {
  const head = `<header class='panel-h'>
    <span class='panel-title'>${escapeHtml(opts.title)}</span>
    ${opts.sub ? `<span class='panel-sub'>${escapeHtml(opts.sub)}</span>` : ''}
    ${opts.actions ? `<div class='panel-actions'>${opts.actions}</div>` : ''}
  </header>`;
  const body = opts.scroll
    ? `<div class='scroll-region' id='${opts.scrollId}'>${opts.body}</div>`
    : `<div class='panel-body'>${opts.body}</div>`;
  return `<div class='panel'>${head}${body}</div>`;
}
```

Two existing runtime contracts every component must honour, because the
dashboard polls and re-renders live:

1. **Scroll preservation.** Any scroll region re-rendered on a poll must go
   through `setHTML(el, html)` (preserves `scrollTop/Left`), never raw
   `innerHTML`. The `ScrollRegion` component owns this so callers cannot forget.
2. **Signature caching.** The Timeline renderers already early-exit when their
   input signature is unchanged (`_tlSig`, `Js.Timeline:53`). Generalise this
   into the render path so components do not churn the DOM every 500 ms. A small
   `renderIfChanged(key, sig, fn)` helper formalises it.

Registration mechanics the builder must not miss (the easy-to-forget surface):
each new `Css.*` / `Js.*` partial must be added to the concat lists in
`DashboardAssets.cs` (`Css` after the shell + before per-tab; `Js` after
`JsHelpers`/`JsComponents`). Components live in a dedicated, growing
`Css.Components.*` / `Js.Components.*` set so the per-tab files only hold
content composition.

> The agent catalogues that fed this plan described contracts in React `props`
> shorthand. Read those as `opts`-object fields, not a framework. No JSX, no
> virtual DOM, no dependencies.

---

## Design tokens — adopt shadcn's scheme (OKLCH)

Move from the ad-hoc palette (`Css.Palette.cs`) to shadcn's **semantic token
layer**, backed by **OKLCH** (perceptually uniform: equal numeric steps look
like equal visual steps, which is the "rooted in science" part and why tints and
states stay legible). Two deliberately separate blocks:

```
/* UI semantic tokens (shadcn naming, dark-first) */
--background --foreground          (page / text)
--card       --card-foreground     (Panel surface / its text)
--popover    --popover-foreground  (Drawer / tooltip)
--primary    --primary-foreground  (the one signature accent — today's --accent)
--secondary  --muted --muted-foreground
--accent     --accent-foreground
--destructive                      (danger / stalls)
--border --input --ring            (lines, fields, focus)
--radius                           (one radius scale)

/* Data-viz ramp — a SEPARATE block, semantic encodings not UI chrome */
--perf-0..4   --cpu --alloc --spike --stall --gc   + a categorical series ramp
```

Current vars map cleanly: `--accent` → `--primary`, `--panel` → `--card`,
`--surface`/`--panel-2` → `--popover`/`--secondary`, `--border-soft`/`--border`
→ `--border`/`--input`. Land the new layer **aliased onto the old vars** so
nothing breaks on day one (the contract every component compiles against).

> Why keep the data ramp separate: semantic UI tokens and chart encodings are
> different concerns. A "primary" colour and a "this-mod's-series" colour should
> never be forced to be the same value. Charts read the ramp; chrome reads the
> semantic tokens.

> Honesty-contract note (Invariant 3): adopting shadcn is a visual/structural
> change only. No component may introduce normative copy; descriptive-not-
> prescriptive governs component *content* exactly as today.

---

## Component inventory (the full set)

Intentionally small (shadcn-sized), grouped in four tiers. "Replaces" cites the
bespoke copies the component retires; "State" is what exists today.

### Tier 1 — Layout primitives

| Component | Owns | Replaces (evidence) | State |
|---|---|---|---|
| `AppShell` | the fixed topbar / tabs / footer + the single content scroll region; overscroll containment | `Css.Shell` (works, not a declared primitive); fix hardcoded `brand-version v0.9` and footer "1-5" (now 6 tabs) | partial |
| `Panel` | border, radius, card bg, header (title / sub / actions), **one padded body slot** | the 7+ reinvented panel chromes in the census | `.panel` exists, adopted on only 2 of 6 tabs |
| `ScrollRegion` | `max-height`/`flex:1` + overflow + the `setHTML` scroll-preserve contract | the ~12 `.*-scroll`/`.*-wrap`/`.modtable`/`.nowlist` reinventions | reinvented everywhere |
| `SectionHeader` | the uppercase-muted label used inside panels and sub-sections | the 7 header reinventions (`.dor-h`, `.lag-section-h`, `.mem-sect-h`, `.modtable-head`, `.mc-section h3`, `.tl-detail h4`) | `.panel-h` canonical, rest reinvent |
| `Grid` helpers | the recurring responsive 2-column detail/bottom layouts | `.ins-mid`, `.tl-bottom`, `.lag-gc-grid`, `.lag-rhythm-grid`, `.self-layout`, `.hero-body` | per-pane |

### Tier 2 — Data-display primitives

| Component | Owns | Replaces (evidence) | State |
|---|---|---|---|
| `DataTable` | sticky header, **sortable cols (behaviour, not just style)**, perf-tint, one hover + selection model | `.dtable` styling is shared and good; fold in the 3 separate sort impls (`sortableHead`, `lagSortTh`, Mods inline) | style done, behaviour fragmented |
| `TreeTable` | `DataTable` + nested expandable rows + one `Twirl` chevron | the bespoke `.modtable`/`.mod-tree`/`.cat-row`/`.hook-row` (Css.Mods) | bespoke, ignores `.dtable` |
| `RowList` / `Row` | ranked clickable list, one hover + `selected` treatment, reserved tinted left bar | observatory cards, `.now-seg`, `.event`, `.hd-row`, `.lag-hist-row`, chronicle `.cr-block`, death cards | each rolled its own |
| `StatLine` / `StatTile` | label/value (inline) and label-over-value card (optional bar), one severity tint | `.statline` (keep) + `.mem-card`/`.mem-stat`/`.mc-stat`/`.hero-stat`/`.self-row`/`.lag-chain-stats`; dedupe the `.good/.warn/.bad` value modifiers | 6 impls |
| `KpiTile` (+ `KpiStrip`) | hero number + status tag + sub-stats + optional spark/gauge | `.kpi` (Css.Kpis), `.ins-kpi .tile`, `.lag-kpi-cell` | 3 impls |
| `Drawer` / `DetailPane` | slide-in overlay (Drawer) and inline aside (DetailPane), shared header + sectioned body | `.modcard` (slide-in), `.mem-drawer`, `.ins-detail`, `.lag-detail`, `.tl-detail` | 4 impls |
| `Chip` | small labelled token | `.chip` (keep); absorb `.family-tag` (NowPlaying), `.ld-cause` badge (Lag), `.tx-kind`, `.ev-chip` | done, needs adoption |
| `Legend` | swatch + label + value row, for any chart | `.bar-legend` (keep) + `.donut-legend` + `.heatmap-legend` | 3 impls |
| `SegmentedControl` (+ `FilterBar`) | single-select button group; `FilterBar` is the multi/gating sibling | `.segctl` + `.chart-toggle`; `FilterBar` from `.tl-filterbar` | 2 impls |
| `Callout` | bordered info box (info / warn / danger) | `.mc-callout` | 1 impl |
| `EmptyState` | the canonical muted/centred placeholder | **done** | done |
| `CellBar` | inline proportion bar for a table cell | `.cellbar` exists; retire the 7+ `.bar > span` reinventions | helper exists, underused |

### Tier 3 — Chart primitives (the deliberate new investment)

The user's explicit ask: generic bar / pie / split / stacked-pie and "cooler
infographics". Today there are 5 sparkline impls, 2 line-chart impls, 2 gauges,
2 heatmaps, 2 bar strips, all bespoke. Build **one chart module** on the
existing `seriesPaths`/`niceScale` core plus a tiny shared SVG frame helper
(`chartFrame(opts) -> {svgOpen, scaleX, scaleY, close}`), then express each
chart as a render function over it.

| Component | Shape | Replaces (evidence) | Notes / new capability |
|---|---|---|---|
| `Sparkline` | mini line, no axes | `drawSpark`, `drawSpikeMarkers`, `setKpi` spark, `renderModSparkInline` | one impl, optional marker overlay |
| `LineChart` / `AreaChart` | axis-scaled line + optional area, threshold/median rules, point markers | `renderFrameChart` (hand-rolled) + GC chart (`seriesPaths`) | consolidate onto `seriesPaths`; opts for rules + markers |
| `BarChart` | vertical **or** horizontal bars, tint mapping, marker dots | per-minute strip (`renderTimelineHeatstrip`, vertical) + lag rhythm (`lag-hist`, horizontal) | `Histogram` and `TimeStrip` are presets of this |
| `Donut` / `Pie` | arc segments, `innerRadius` (0 = pie), centre label, top-N + "rest" grouping | `renderDonut`/`donutSlice` (Js.Summary) | **`StackedPie`/nested rings = `rings: [segs, segs]` variant** (the asked-for stacked pie) |
| `Gauge` | radial ratio, coloured bands, configurable sweep (180 half / 270 / 360 ring), centre value | Self `renderSelfGauge` (hardcoded hex) + Insights `.ring` | one impl, tokenised colours |
| `Heatmap` | `Categorical` (2D matrix, `.rheat`) and `TimeGrid` (calendar/minute, `.hm-cell`); `Conditional` falls back to a ranked `BarChart` when one axis is degenerate | `.rheat` (shared) + `.hm-cell` grid + Lag's heatmap-or-barlist (Js.Lag:159) | fix the `.empty` collision here |
| `SplitBar` / `StackedBar` | stacked composition bar + `Legend` | `.split-bar`/`splitBar` (keep); fix name collision | the "split chart" primitive |
| `Swimlane` / `TimeLane` | time-scaled segment bars across labelled lanes + a transition annotation track | `renderTimelineSwimlanes` + transition track | distinctive infographic; a `TimeAxis` shared with `TimeStrip` |

### Cross-cutting interaction contract

The thing that keeps drifting, declared once and inherited everywhere:

- **One hover model**: subtle bg + a reserved transparent 2px left border tinted
  to the accent on hover. No `box-shadow` left bars, no extra top/bottom edges.
  (`.modrow` already got this right in the v0.16 fix; make it the Row default.)
- **One selection token**: the signature blue (`--primary`), via `tr.sel` /
  `.selected`. Coherence already unified the observatory green into this; finish
  it.
- **One focus-visible ring**: `--ring`, on every interactive element.

---

## Bugs and collisions the migration resolves

These are live latent bugs found during the read, each a direct consequence of
bespoke-per-pane styling. The library kills the class; list them so they are
fixed deliberately, not rediscovered.

| Bug | Evidence | Effect | Fixed by |
|---|---|---|---|
| `.split-bar` redefined by a tab file | `Css.Self.cs:57,62` redefines the shared `.split-bar` (margin + `height:0.65rem` + `border-radius:1px`); `CssSelf` concatenates after `CssComponents` | every split-bar dashboard-wide silently inherits Self's margin and shrunk height | `SplitBar` owns the class; Self composes it (rename Self's to `.footprint-bar` only) |
| `.panel-wide` defined twice | `Css.Mods.cs:19` (`grid-area:mods`) vs `Css.Self.cs:64` (`grid-column:1/-1`), both global | Self's rule overrides the Mods-panel column placement on the Summary grid | `Panel`/`Grid` own placement; retire both ad-hoc defs |
| `.empty` collides with heatmap cells | `Css.Coherence:23` `.empty` rule also matches `.hm-cell.empty` (`Css.Heatmap:41`) | panel empty-state padding (`1.1rem`, centred, mono) is injected into heatmap "empty" cells | `Heatmap` uses a scoped state class (e.g. `.is-empty`), not the panel `.empty` |
| Gauge bypasses tokens | `renderSelfGauge` hardcodes `#4f9d6a`/`#c97f3c`/`#b94e58` (Js.Self:75) | the one gauge that ignores the palette; will not track a token/theme change | `Gauge` reads `--perf-*` / band tokens |
| Duplicated value modifiers | `.good/.warn/.bad` redefined in `Css.ModCard`, `Css.Self`, `Css.Kpis` | three sources of truth for one severity tint | one `StatTile`/severity token |
| Stale chrome strings | `brand-version` hardcoded `v0.9` (`IndexHtml.Preamble:27`); footer "1-5 to switch tabs" with 6 tabs (`IndexHtml.Closing:10`) | dashboard shows the wrong version and wrong keymap hint | `AppShell` renders these from state |

---

## Migration approach (strangler, not big-bang)

The dashboard must stay working throughout. This mirrors the contract-decoupling
pattern used for the data-layer waves: freeze the contract, then move consumers
onto it one at a time.

1. **Tokens first.** Land the shadcn/OKLCH token layer aliased onto the current
   palette so nothing breaks. This is the contract every component compiles
   against.
2. **Reference trio on one pane.** Build `Panel` + `ScrollRegion` +
   `SectionHeader` (the highest bug-density primitives) and migrate **one** pane
   end-to-end as the worked reference. Recommend **Insights**, because it
   reinvents panel chrome and scroll regions the most (5 of each on one tab), so
   the win is largest and the pattern is proven against the hardest case.
3. **Chart core next.** Land `chartFrame` + `Sparkline` + `LineChart` +
   `BarChart` over the existing `seriesPaths`/`niceScale`, and convert the
   Summary frame chart and the Lag GC chart first (the two line charts), then
   the 5 sparklines. This retires the most duplicated JS.
4. **Fan out pane by pane**, retiring each bespoke CSS/JS block as its pane moves
   onto components. Blast-radius rule: deleting `.mem-strip` means the strip now
   comes from `SplitBar`; verify the *pane*, not just the diff.
5. **The coherence layer shrinks as it wins.** Every alias it holds
   (`.tl-empty`, `.lag-empty`, the selection override, the `.split-bar`
   max-width patch) disappears when its pane stops emitting the legacy class. A
   shrinking `Css.Coherence.cs` is the success metric.

Suggested fan-out order (most bespoke first, so the library hardens early):
**Insights → Lag → Timeline → Mods → Summary → Memory → Self.** (Memory and
Self already lean on `.panel`/`.split-bar`, so they convert cheaply at the end.)

---

## Verification

Stays dual-surface and uses the existing preview harness
(`tools/preview/render.py`) per pane:

- **Agent surface**: `render.py` extracts the un-built CSS/JS from the C#
  strings and screenshots each tab, so layout / colour / sort / chart shape are
  checkable without an in-game build.
- **Caveat (known, recorded in memory)**: the harness is **static-only**. Hover,
  selection, scroll-at-extreme, real-viewport `vh` caps, and the drawer slide
  need the live browser. This limitation is itself part of why these bugs slip
  through, and a reason to make interaction states component-owned so they are
  testable in one place. Split verification honestly: harness for layout,
  live in-game Build + Reload for interaction.
- **Per-component check**: because a component is one CSS block + one JS fn, each
  can be exercised against a fixture in isolation before any pane adopts it.

---

## Invariant compliance

- **Invariant 1 (read-only)** and **4 (abort-clean)**: N/A. This is browser UI;
  it touches no game state and no loader internals.
- **Invariant 2 (overhead budget / zero-alloc hot path)**: N/A to the budget.
  All charting is client-side JS in the browser, off the per-tick path. The
  server only serializes JSON it already builds. The signature-caching contract
  additionally reduces *browser* DOM churn, a UX win, not a hot-path concern.
- **Invariant 3 (honesty contract)**: binding. Components are visual/structural;
  no component introduces normative copy. The descriptive-not-prescriptive rule
  governs the *content* passed into a component exactly as today.
- **Invariant 5 (no mod-specific code)**: unaffected. `modColor(id)` hashes a
  generic mod id; no component keys off a named mod.

---

## Open decisions (resolve at build time)

- **Component file layout**: one growing `Css.Components.cs`/`Js.Components.cs`,
  or split into `Css.Components.Layout.cs` / `.Charts.cs` etc.? Lean to splitting
  once the set exceeds ~8 components, matching the existing per-concern partial
  convention.
- **`renderIfChanged` scope**: generalise the Timeline `_tlSig` pattern into the
  shared render path, or keep it per-renderer? Recommend generalising so new
  panes get poll-stability for free.
- **TreeTable vs DataTable**: keep the Mods cascade as a `TreeTable` variant, or
  flatten it into `DataTable` with an `expandable` row option? Decide when the
  Mods pane is reached in the fan-out.
