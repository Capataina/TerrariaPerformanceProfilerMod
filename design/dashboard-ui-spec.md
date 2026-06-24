# Dashboard UI Spec — how the Performance Profiler dashboard looks today

> **Purpose.** A living description of the *visual* dashboard so we can iterate on the look together. This is the "as-built" snapshot; mark proposed changes inline and we work them off this baseline.
>
> **Status:** grounded against `build.txt 0.18.1`, source read on 2026-06-24, and six rendered screenshots (`design/dashboard-shots/tab-*.png`) captured the same day.
>
> **How this was grounded (not guessed):** the dashboard is generated entirely from C# verbatim-string assets in `Web/Assets/` (`Css.*.cs`, `Js.*.cs`, `IndexHtml.*.cs`). The preview harness (`tools/preview/render.py --tabs`) reconstructs and screenshots the real page with no build and no running game. Palette/type values below are read from `Css.Palette.cs`; layout/panels are read off the renders. **Where the renders can't settle something (live-only interaction states), it is flagged `⟂ confirm live`.**

---

## 0. Reading caveats (what the screenshots are and aren't)

| Dimension | The renders | Real in-game |
|---|---|---|
| Data | cached fixture data from a prior live capture (real-ish bars/charts) | live, 2–4 Hz polling |
| Window | 1600px wide, scroll **unlocked** into one tall printout | real window height, bounded scroll regions engage |
| Interaction | static; first item auto-selected | hover, selection, drawer slide, scroll-at-extreme, paused-game detection |
| Empty panels | "Allocation→GC causality" and "Hook distribution" show empty states (no live game) | populate during play |
| Insights usage | usage shares read ~0 | **known bug** — usage measures content *created* not *used* (deferred to insights rework); not a UI fault |

---

## 1. The design language

The governing idea, straight from `Css.Palette.cs`: **the chrome is pure monochrome neutral grey; the only colour on screen is data.** One signature accent (near-white) marks active/selected/focus. Everything coloured is an *encoding* — perf severity, a per-mod series, a state hue — never decoration.

### 1.1 Palette (OKLCH, dark, zero-chroma chrome)

**Chrome (greys, zero chroma):**

| Token | OKLCH | Role |
|---|---|---|
| `--bg-deep` | `0.120 0 0` | page base (body) |
| `--background` | `0.145 0 0` | app base |
| `--header` | `0.170 0 0` | top bar |
| `--panel-2` | `0.175 0 0` | nested panel |
| `--card` / `--panel` | `0.205 0 0` | **panel surface** |
| `--surface` / `--popover` | `0.235 0 0` | drawer / tooltip |
| `--secondary` / `--border` | `0.269 0 0` | raised control / hard lines |
| `--foreground` (text) | `0.985 0 0` | body text |
| `--muted` | `0.610 0 0` | labels / secondary text |
| `--dim` | `0.430 0 0` | faintest text |
| `--primary` / `--accent` | `0.922 0 0` | **the one signal accent (near-white)** |
| `--accent-soft` | `white / 0.09` | selected-row wash |
| `--accent-line` | `white / 0.22` | focus / hairline |

Note: there is **no blue in the chrome**. Selection is a near-white wash, not a colour. (Memory's RAM bars look bluish in the render — that's the categorical data palette, not chrome.)

**Data-viz (the only colour) — perf ramp, chroma rises toward the alarm end:**

| Token | OKLCH | Reads as |
|---|---|---|
| `--perf-0` / `--good` / `--cpu` | `0.72 0.10 150` | healthy green |
| `--perf-1` | `0.74 0.10 120` | green-yellow |
| `--perf-2` / `--amber` | `0.76 0.12 90` | amber |
| `--perf-3` / `--orange` / `--spike` | `0.70 0.14 50` | orange |
| `--perf-4` / `--danger` / `--stall` | `0.64 0.17 25` | red |

Plus a categorical per-mod family (`MOD_COLORS` in `Js.Helpers`): **12 hues locked at L 0.72 / C 0.11**, stepped evenly round the wheel, so the series read as one muted cohesive family on grey rather than a clashing rainbow. Series accents: `--alloc`/`--gc` purple `290`, plus `magenta 350`, `purple 300`, `cyan 215`, `good-bar` teal `185`.

### 1.2 Typography & metrics

- **UI:** `Inter` (`--ui`), 14px base, line-height 1.45.
- **Mono:** `JetBrains Mono` (`--mono`) — every number, every panel eyebrow label, every metric.
- **Panel headers** are an uppercase, letter-spaced, muted mono *eyebrow* (e.g. `FRAME TIME · LAST 30S`), with an optional right-aligned control or hint on the same line.
- **Radius:** `5px`, one scale everywhere. **Density is the feature** — thin 1px borders, tight padding, tabular numerics.

### 1.3 Component vocabulary (the v0.17 shadcn-style library)

Every pane composes these primitives (`Js.Components.cs` / `Js.Charts.cs` / `Css.Components.cs` / `Css.Charts.cs`); content is the only per-pane variable.

| Component | Looks like |
|---|---|
| `panel()` | bordered `--card` surface; eyebrow header + one padded body slot |
| `scrollRegion` | bounded inner-scroll area (poll-stable; doesn't reset on refresh) |
| `statTile` / `statGrid` | KPI tile: big mono value + label + optional sparkline + severity tint (good/warn/bad) |
| `gauge()` | radial arc (180°/270°), tokenised green→amber→red bands, centre value + sublabel |
| `sparkline()` | tiny inline trend line |
| `lineChart()` | line + area + threshold rules + spike markers (shared scale) |
| `barChart()` | vertical column histogram **or** horizontal rows |
| `donut()` | ring/pie + legend (nested rings for stacked) |
| `heatmapMatrix()` | 2D cell grid, magnitude-as-luminance |
| `splitBar()` + `legend()` | single proportion bar split into segments + key |
| `row()` / `rowList()` | the one hover + selection model (reserved tinted left bar) |
| `cellBar()` | inline magnitude bar inside a table cell |
| `twirl()` | expand chevron (cascading tree) |
| `segmented()` | segmented toggle (ms/fps, cur/avg/max, filter bars) |
| `drawer` | slide-in detail panel (mod card, Memory breakdown), Esc-dismissed |
| `.dtable` | the data-table system (sortable, sticky header) |
| `emptyState()` | canonical muted, centred "no data yet" placeholder |

---

## 2. The global shell (every tab)

The window is a fixed-height app; the document never scrolls (only the content region does — a deliberate fix so the top bar can't be shunted off-screen).

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ TOP BAR (--header)                                                             │
│  ⚡ Performance Profiler  v0.18.1  ◦ <status pill>     TICK #23,029  FRAME 4.73ms │
│                                                       AVG/30s 3.14ms  GC 0.00ms │
│                                                                  BACKEND ILHook │
├──────────────────────────────────────────────────────────────────────────────┤
│ TAB STRIP   Summary · Timeline · Lag · Insights · Self · Memory     (keys 1–6) │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                                │
│  CONTENT (scrolls internally) — the active tab's panels                        │
│                                                                                │
├──────────────────────────────────────────────────────────────────────────────┤
│ FOOTER  polling /api · 500ms · 1–6 to switch tabs · 13:19:50   6 fps · 11 poll │
└──────────────────────────────────────────────────────────────────────────────┘
```

- **Top bar — left:** wordmark + version chip + a status pill that reflects connection state: `live`, `game paused [window unfocused]`, or `reading from db · <when>`.
- **Top bar — right:** a mono metrics readout — `TICK`, `FRAME` (ms), `AVG/30s`, `GC` (ms), `BACKEND` (ILHook / delegate).
- **Tab strip:** six text tabs, active one near-white with an underline; inactive muted. Keyboard `1`–`6`.
- **Footer:** left = poll cadence + tab hint + clock; right = a self-perf readout (browser fps · polls · DOM nodes).
- **Overlays (`⟂ confirm live`, hidden in renders):** a full-screen *disconnected* state and a *no-world* state (the latter carries the focus-pause workaround copy).

---

## 3. The six tabs

### 3.1 Summary — `dashboard-shots/tab-summary.png`
Mission control; the tab you live in. Merges the README's conceptual *Now* + *Mods*.

1. **KPI strip** — tiles: FPS (60), FRAME TIME (11.9 ms), entities, LAG SPIKES (0), STALLS, CYCLES (14) — each big mono value + sparkline + sublabel, severity-tinted.
2. **FRAME TIME · LAST 30S** — `lineChart`: line + area, 60fps/median threshold rules, orange spike markers, `ms/fps` segmented toggle. Beside it **IMPACT SHARE** — `donut` (29% centre) + per-mod legend.
3. **SESSION TREND** — row of `sparkline`s + a session **minute heatmap** (the green calendar strip).
4. **NOW PLAYING** (current segments: Run #, biome/boss, cost) + **RECENT EVENTS** (`rowList` feed: spikes, segment closes, deaths).
5. **MODS · CASCADING TREE** — the big mod → category → hook expandable tree. `cellBar`/`splitBar` cost bars in the perf ramp, `cur/avg/max` sort (`segmented`), search box, outliers highlighted gold. Categories: Systems, World, Players, Projectiles, Items, etc.

### 3.2 Timeline — `dashboard-shots/tab-timeline.png`
Every closed segment of the session.

1. **Filter bar** (`segmented`: biome / weather / boss / invasion …).
2. **Heat strip** + **transition track** (time-placed chips marking context changes).
3. **SEGMENT DETAIL** (select a segment) + **ATTENDANCE** panel (right).
4. **Segment swimlanes** — per-segment rows with mod/item attendance chips (Gantt-like).
5. **CHRONICLE** — a film-strip / log list of runs at the bottom.

### 3.3 Lag — `dashboard-shots/tab-lag.png`
Spikes and stalls (the README's *Spikes*, expanded).

1. **KPI strip** — SPIKES (57), stall total, LONGEST STALL (4.66s).
2. **CAUSE × CONTEXT** — rows per cause (Spike / MainThreadFreeze / LongFreeze) × severity / count / ms / top-mod, with horizontal perf-ramp bars + per-mod labels.
3. **PER-SEGMENT LAG DENSITY** — table with normalised-density bars + spike counts.
4. **GC PRESSURE** — `lineChart` area with a dashed peak rule (`12.14 GB peak`).
5. **ALLOCATION → GC CAUSALITY** — empty when no GC cycle observed.
6. **LAG RHYTHM** — `barChart` histogram + a bucket table (`0.20s–0.45s …` count, top mod).

### 3.4 Insights — `dashboard-shots/tab-insights.png`
Interpretation layer (the engine being consolidated in the planned rework).

1. **Ring strip** — `gauge` rings (id / state / impact) in green/amber.
2. **DORMANT CONTENT** — ranked table (mod, share %, used, dormant).
3. **MOD OBSERVATORY** — master `rowList` (per-mod rows + composition bars + `cpu/usage/name` sort + search) ▸ **MOD DETAIL** panel (cpu share / smoothed ms / average ms / usage share + biome/loadout cells + footprint).
4. **CROSS-CUTTING CONSTELLATION** — 3-column relationship view.
5. **ENGAGEMENT VS COST** (`.dtable`: usage / cpu / ratio + verdict badges like *balanced* / *over-cost*) + **MOD-PAIR CORRELATION** (`.dtable`: mod A, mod B, r, samples + green correlation bars).
> ⚠ Usage-share columns read ~0 here due to the known *content-created-not-used* attribution bug; this is a data bug, not a layout one.

### 3.5 Memory — `dashboard-shots/tab-memory.png`
Per-mod RAM footprint (the tab the canonical doc predates).

1. **KPI band** — managed heap (12.32 GB), working set / native (4.79 GB), per-hook (30 KB), hooks installed (62,203).
2. **Hero RAM split-strip** — one `splitBar` of per-mod RAM share + the OKLCH categorical legend underneath.
3. **PER-MOD table** — each row: dominant RAM-magnitude `cellBar` (leads the row, length tracks size) + value + a thin composition bar + share %. (CalamityMod 2.83 GB, PerformanceProfiler 1.44 GB, ThoriumMod 884 MB …).
4. **BREAKDOWN drawer** (selected mod) — managed/native split tall bar + a `statGrid` (heap allocations 50.6%, 65.7 KB/s …).

### 3.6 Self — `dashboard-shots/tab-self.png`
The profiler measuring its own cost (the honesty contract made visible). Clearest render.

1. **PROFILER HEALTH** — `gauge` (`0.82× healthy`) + tiles: SEVERITY (healthy), BYTES/HOOK (29.7 KB), VS BASELINE (0.82×), HOOKS INSTALLED (62,203).
2. **INSTALL FOOTPRINT** (install delta 1801 MB, bytes/hook 29.7 KB, hook count 62,203, vs 36 KB baseline + bar) + **PROCESS CONTEXT** (working set, managed heap, managed share 99% + managed/native `splitBar`).
3. **ATTRIBUTION BACKEND** — backend ILHook, installs yes.
4. **HOOK DISTRIBUTION · TOP 12 MODS BY HOOK COUNT** — empty until a mod is expanded on Summary (lazy `/api/hooks`).

---

## 4. Visual / UX bug register

Grounded in a full-resolution pass (real-viewport renders + per-panel crops, 2026-06-24). Severity: **P1** = unreadable / overlap / broken or wrong; **P2** = dead space / clarity; **P3** = polish. Status: `open` until fixed + re-rendered. Evidence shots in `design/dashboard-shots/`.

### P1 — overlap / unreadable / wrong

| ID | Tab · location | What's wrong | Fix direction | Status |
|---|---|---|---|---|
| B-01 | Insights · Cross-cutting signals | The three sub-tables (hook-dominance / contributor-spike / allocation-burst) **overlap horizontally**; the middle column's mod names render on top of the left column's bars. Genuinely unreadable. (`crosscut-overlap-bug.png`) | `.cc-sections` floor widened to `min(100%, 28rem)` + `min-width:0` + static (non-sticky) sub-table headers so each class table owns its track and never spills | **fixed** (preview ✓) |
| B-02 | Insights · Cross-cutting headers | Class title + count run together: `HOTHOOKDOMINANCE`, `PEAKCONTRIBUTORTOSPIKE`, `ALLOCATIONBURST` | **Real cause** was PascalCase signal-class identifiers rendered raw then uppercased by CSS (not overlap — my first fix was wrong). Fix: `humanizeLabel()` splits camelCase → `HOT HOOK DOMINANCE` | **fixed** (preview ✓) |
| B-03 | Timeline · Transition track | Transition chips clipped and bunched at the right edge (`change (open)` cut off) | Contain the track (overflow), edge-anchor near-100% chips (the v0.18.1 fix doesn't cover this case) | open |
| B-04 | Timeline · Biome swimlane | Block labels truncated **and** the `+%` share label overlaps the block text | Reserve label space / move share to the right / tooltip on hover | open |
| B-05 | Insights · Engagement-vs-cost + Dormant | `USAGE SHARE` is `0.0%` for every mod, so `TILT` badges almost everything `cost-heavy` — the verdicts are misleading. Root cause: the content-created-not-used attribution bug | Badge "needs active-use data" / suppress the verdict until the insights rework lands | open |
| B-06 | Insights · Mod detail | Literal `&amp;` shows in `HEADLINE COST &AMP; ENGAGEMENT` (HTML entity double-escaped) | Pass a plain `&` (sectionHeader already escapes); now renders `&` | **fixed** (preview ✓) |

### P2 — dead space / clarity

| ID | Tab · location | What's wrong | Fix direction | Status |
|---|---|---|---|---|
| B-07 | Memory · Breakdown | 3 "profiler instrumentation" blocks (`hook scaffolding`, `installed hooks`, `allocation rate`) flushed left, ~72% dead space right *(reported example)* | R1 root fix: `.stat-grid` `auto-fill` → `auto-fit` so a few tiles fill the row | **fixed** (preview ✓) |
| B-08 | Self · Profiler-health header | Gauge + 4 chips occupy the left half; entire right half empty | Same R1 fix (`auto-fit`); the 4 tiles now stretch across the panel beside the gauge | **fixed** (preview ✓) |
| B-09 | Self · whole tab | Content stops at ~25% height; ~75% empty black | Rebalance: 2-up panels, pull hook-distribution up, larger gauge | open |
| B-10 | Self · Attribution-backend | 2 key-value rows flushed left, right half empty | Grid fix (B-07 class) | open |
| B-11 | Self · Hook-distribution | Empty until you switch to *Summary* and expand a mod (cross-tab dependency) | Load on Self-tab activation, or self-contained empty CTA | open |
| B-12 | Memory · KPI band | 4 tiles then a large gap to the `include native` toggle pinned far right | Distribute tiles / move toggle into the panel header | open |
| B-13 | Insights · Mod detail vs observatory | Detail panel shorter than the observatory list beside it, dead space bottom-right | Let detail not reserve full height, or fill with roster-vs-usage | open |
| B-14 | Summary · Impact donut | Donut small, sits top-left of its panel, dead space right + below | Enlarge donut or move legend right of it | open |
| B-15 | Summary · Now-playing vs events | Now-playing (2 cards) much shorter than the events feed, void underneath | Equal-height or let the short panel not reserve height | open |
| B-16 | Timeline · Heat strip | Strip occupies ~10% width, huge dead space right | Stretch to panel width or shrink the panel | open |
| B-17 | Timeline · empty lanes | Boss / Invasion / Subworld lanes hold full-height rows while empty | Collapse empty lanes (or compact "no X this session" row) | open |
| B-18 | Lag · Cause×context | Bars flat grey (no severity colour); header `SINGLE CONTEXT · —` is cryptic | Colour bars by perf ramp; clarify the header | open |
| B-19 | Lag · Fingerprint clusters | `CONTEXT` column is `—` on every row (dead column eating width) | Hide the column when always empty, or populate it | open |
| B-20 | Lag · Selected cluster | Shows raw internal id `Spike\|m6\|—\|\|h0` | Render a human label, drop the raw id | open |

### P3 — polish

| ID | Tab · location | What's wrong | Status |
|---|---|---|---|
| B-21 | Summary · Mods tree | Category rows show `N hooks` while mod rows show ms/avg/alloc (inconsistent right columns); cost-bar track leaves a gap before the numbers | open |
| B-22 | Summary · KPI strip | Tile widths/spacing uneven; flat `gc`/`spikes` sparklines read as empty lines | open |
| B-23 | Self · Process context | Managed/native split bar is ~99% one colour (native sliver invisible) | open |
| B-24 | Timeline · Chronicle | `biome —` em-dash; long runs of repeated `Zombie` chips | open |

### Idle-empty — verify against a `--live` session before treating as bugs

These read empty because the synthetic dataset had no boss, no GC cycle, and no expanded mod. Confirm with a live capture (`render.py --live` with a boss fight / GC) before changing anything: Timeline attendance all-zero + empty mod table; Lag allocation→GC-causality empty; Timeline boss/invasion lanes empty.

### Recurring root causes (fix once, clear many)

- **R1 — flushed-left stat/key-value blocks** (B-07, B-08, B-10, and Memory/Self generally): one shared grid rule in `Css.Components`/`statGrid` fills the width everywhere.
- **R2 — panels reserve full height when empty** (B-11, B-13, B-15, B-17): empty/short states should not hold full panel height.
- **R3 — composite multi-column panels free-flow instead of using a grid** (B-01, B-02): the cross-cutting panel is the worst case.

### Interactivity & polish (QoL pass — requested)

| ID | Item | State |
|---|---|---|
| Q-01 | Summary impact donut: click a slice **or** legend row → opens that mod's card. Reusable `data-mod` + `.slice.hit`/`.lg.hit` affordance; `openModCard` is the shared hook | **done** (preview ✓) |
| Q-02 | Mod card: coloured hero header (mod series colour + CPU-share headline) so the card opens on its key number | **done** (preview ✓) |
| Q-03 | Extend click-to-card to **every** mod surface (cross-cutting / engagement / dormant / mod-pair rows, observatory, memory legend) — the `openModCard` pattern is now in place | open |
| Q-04 | Deeper mod-card restyle (typography/capitalisation direction TBD) | open — needs direction |
| Q-05 | Lag selected-cluster: style the clicked detail, drop the raw id `Spike\|m6\|—\|\|h0` (B-19/B-20) | open |
| Q-06 | Timeline: the multi-colour mini-bars at the bottom of swimlane blocks read as noise; label `+%` overlaps the block text (B-04) | open |
| Q-07 | Dead-space rebalance across tabs — Self underfill (B-09), empty lanes (B-17), now-playing (B-15), etc. (R2) | open |

---

## 5. Interactive offline preview

For instant visual iteration there is now a clickable, self-contained replica:

- **`design/dashboard-preview.html`** — open it directly in a browser (no build, no game, no server). It embeds the **real** dashboard CSS/JS (the actual component library) and feeds it **synthetic data** through a `fetch` shim, so all six tabs, hover, selection, drawers and charts behave exactly like the live dashboard. This is the surface to eyeball a change on.
- **Regenerate after any `Web/Assets/*.cs` edit:** `python3 tools/preview/build_preview_html.py`. It re-extracts the current assets and re-inlines them, so the preview always reflects the latest UI. Without this it goes stale the moment a stylesheet changes.
- **Synthetic dataset:** `tools/preview/fixtures/*.json` (a captured-then-trimmed snapshot of all 29 endpoints). Refresh it from a real session with `render.py --live` (game open), then delete the fixtures folder and re-run the builder.

---

*Static screenshots (this doc's images): `python3 tools/preview/render.py --tabs` (empty/fixture) or `--real --live` with the game open (live data, real viewport). Interactive preview: `python3 tools/preview/build_preview_html.py` → `design/dashboard-preview.html`.*
