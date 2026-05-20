# UI Overhaul Plan — Performance Profiler

**Status:** draft, awaiting Caner's pass before implementation
**Owner:** Claude (UI lead)
**Date:** 2026-05-20
**Sequencing:** runs before the LiteDB migration; perf-of-the-profiler work follows both

## 1. Why now

The current overlay is a functional skeleton. It carries the data correctly but the presentation has accumulated debt:

| Problem | Where it shows up | Severity |
|---|---|---|
| Hardcoded absolute thresholds | OverviewTab cost bands: `green <1 ms, amber 1-4 ms, red >4 ms` | High — violates the relativity principle the rest of the codebase now honours |
| Hidden-low filter on Overview | `_hideLowImpact` filter is on by default; the tab silently omits half the modlist | High — user complaint: "summary should rank everything like the tree tab" |
| Small text, sharp layout | `0.6-0.8f` text scale everywhere, fixed 640 px width, 1 px hard borders | Medium — hard to read at high DPI |
| Information overlap | Hook rows, sub-rows, scrolls all sized for cramped vertical space | Medium |
| No charts / visual comparison | Everything is text + linear progress bars; no donut, no histogram, no sparkline | Medium — comparison is the core job of a profiler UI |
| Sparse colour vocabulary | Three accent colours, two text shades, one heat ramp not used consistently | Low/Medium |
| Tab order frozen at compile time | Reordering requires a code edit; users can't pin their preferred default | Low |

The fix isn't paint — it's a coherent visual system that subsequent features (LiteDB-backed lifetime views, per-call graphs) can extend without re-litigating typography every time.

## 2. Design principles

These guide every individual decision below. If a proposed UI element doesn't satisfy them, it doesn't ship.

1. **Information-dense but readable.** The mod's value is data. We add visual weight (whitespace, larger fonts) only where it earns its keep. Concretely: row heights grow from 18 px to 22 px; row count visible at a glance stays roughly the same on a larger panel.

2. **Severity-driven colour, never decorative.** Every colour change in the UI encodes a measurement. Same heat ramp (green → amber → red) used for impact, coverage, severity, self-health — one mental model, one palette. No green text "because green is calm".

3. **Relative thresholds throughout.** Cost bands are derived from `Baseline.FrameMsMedian`, not hardcoded ms. A "red" mod on your machine and a "red" mod on a 25 fps player's machine mean the same thing — "this mod owns a disproportionate share of your frame".

4. **Visualisations where they help comparison, text where it helps detail.** Donut for "share of total", sparkline for "trend over 30 s", histogram for "distribution of frame times". The textual table stays — that's where you read exact numbers.

5. **Resizable, persistent.** Panel size is a user preference, persisted across sessions. Default is bigger than today; corner-drag resize within sane bounds.

6. **Modern surface treatment via tiered surfaces, not rounded corners.** One elevated surface tier (cards) plus subtle gradient highlights on actives; corners stay sharp because rounded edges would require shipping a texture asset or hand-rolling polygon math, neither of which earns its keep when the same visual hierarchy can be achieved with surface contrast. Decided 2026-05-20.

7. **The overlay can never be the cause of player lag.** Every UI decision has a hot-path cost; we ship within the existing per-frame budget. New chart drawing batches geometry, runs at 1 Hz refresh like the existing tabs.

## 3. Layout architecture

### 3.1 Panel sizing

The overlay supports two presentation modes, switched at runtime via a header toggle:

- **Default mode (1120 px wide)** — the "look at it while standing in your base" view. Big text, full charts, generous spacing. Designed for inspection sessions, not active play. The text grows with the panel: H1 at 1.0× scale (up from 0.82× pre-overhaul), H2 at 0.86×, row text at 0.78×. Bars are 14 px tall on mods.

- **Compact mode (720 px wide)** — the "turn it on and walk around with it" view. Roughly today's overlay size, denser typography, smaller charts. Used during boss fights or active combat where the overlay needs to coexist with gameplay.

```
Default:   PanelWidth = 1120f   (at-rest inspection)
Compact:   PanelWidth = 720f    (active gameplay)
Resize:    [640, 1600]           (user can override either default via corner drag)
Height:    tab-determined, capped at screen.Height - 80
Persist:   mode + width per-user in ModConfig (Phase 7)
```

Corner-drag handle in the bottom-right; tracks like the existing drag-header. Min/max chosen so even ultra-wide users keep the panel manageable. The mode toggle is a header pill (next to LIVE/PAUSED); flipping it snaps to that mode's default size unless the user has manually resized within the current session.

### 3.2 Chrome anatomy

```
┌─────────────────────────────────────────────────────────────────┐
│  ▎ PERFORMANCE PROFILER                  [NOW / 30s] [LIVE]    │ <- 32 px header
├─────────────────────────────────────────────────────────────────┤
│  [SUMMARY] [TREE] [LAG] [EVENTS] [INSIGHTS]    ⊙ CPU MEM BOTH  │ <- 28 px tab strip
├─────────────────────────────────────────────────────────────────┤
│  ┌─ this tick ─────┐  ┌─ frame 30s ──┐  ┌─ gc ──┐  ┌─ tick# ──┐│ <- stat cards
│  │ 1.25 ms         │  │ 1.32 ms      │  │ 0 ms  │  │ 7,201    ││    (raised
│  └─────────────────┘  └──────────────┘  └───────┘  └──────────┘│     surfaces)
│                                                                  │
│  ┌─ profiler health ────────────────────────────────────────┐  │
│  │ hooks 10,250/10,250 100%  ●●●●●●●●●●  backend ilhook    │  │
│  │ self  527 MB · 52 KB/hook · 16% of game  [Concerning]   │  │
│  └──────────────────────────────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ... tab content area ...                                       │
│                                                                  │
└──────────────────────────────────────────────────────────────⤡─┘ <- resize grip
```

The stat block and PROFILER HEALTH move INTO the chrome as raised card surfaces. They're informational rather than tab-specific so they live above the tab fold.

### 3.3 New layout constants

```
PanelWidthDefault     = 880f    (was 640f)
HeaderHeight          = 32f     (was 28f)
TabStripHeight        = 28f     (was 22f)
StatCardHeight        = 48f     (new — three lines of label/value, raised surface)
StatCardGap           = 6f
ProfilerHealthHeight  = 48f     (was implicit ~22 px crammed under stats)
ChromeHeight          = HeaderHeight + TabStripHeight + StatCardHeight + ProfilerHealthHeight + paddings
RowHeight             = 22f     (was 18f)
SubRowHeight          = 20f     (was 16f)
HookRowHeight         = 18f     (was 14f)
```

Result: more vertical real estate per row (text scale increases proportionally), but the chrome occupies more pixels too. Net effect is fewer rows visible on a screen — solved by the panel growing wider, and by users resizing height upward when they want a deeper view.

## 4. Component library

These are the new primitives. Each lives in `UI/Overlay/Components/`. Every tab consumes them.

### 4.1 `ProfilerCard`

A titled raised surface. Title strip on top in panel-fill, body in elevated-fill, sharp corners with a 1 px border. Built from the existing `FillRect` + `DrawBorder` helpers — no new primitives, no asset. Used for stat blocks, PROFILER HEALTH, sections within tabs.

```csharp
ProfilerCard.Begin(sb, area, "this tick", titleColor);
//   ... body content drawn into card body region ...
ProfilerCard.End(sb);
```

### 4.2 `HeatBar`

Horizontal bar where the fill colour comes from `ProfilerTheme.CostColor(fraction)` and the fill width comes from `value / max`. Used for every mod/hook cost row.

```csharp
HeatBar.Draw(sb, area, value, max, height: 10);   // colour derived
```

### 4.3 `Sparkline`

Mini line chart over a fixed-length series. Two implementations:
- **Filled area** (frame time over 30 s)
- **Bar series** (per-mod cost over time)

Drawn as primitive-batch line strips (cheap; ~30 vertices per sparkline). Refresh-cadence-gated so we recompute the polyline only at 1 Hz.

```csharp
Sparkline.DrawFilled(sb, area, valuesRing, fillColor, lineColor);
```

### 4.4 `DonutChart`

Pie chart with hole. Sectors drawn as triangle fans via `GraphicsDevice.DrawUserPrimitives`. Centre hole reserved for a hero stat (total ms / top contributor / etc).

Slice colour from a deterministic palette keyed off `modId` (stable across sessions — same mod always gets the same hue) but de-saturated so the heat-ramp foreground stays visible against it.

```csharp
DonutChart.Draw(sb, gd, centre, outerR, innerR, slices, centreStat, centreLabel);
```

Slice limit: top 8 by value, ninth slice is "others" lumping the tail. Hover highlights the slice and shows the mod name; click expands its detail in the side panel.

### 4.5 `Pill`

Already exists as `Toggle`. Modernised: optional leading dot for status, two-row variant for "label / value" pairs, slightly larger hit-target. Sharp corners, same hit-test API.

### 4.6 `StatBlock`

Label/value pair with consistent hierarchy. Title in muted small caps, value in primary larger, optional unit suffix in muted, optional delta indicator (↑/↓ + arrow colour).

```csharp
StatBlock.Draw(sb, position, "this tick", "1.25 ms", deltaPercent: -8.3);
```

### 4.7 `SeverityBadge`

Pill variant that takes a severity enum and renders the appropriate colour + label. One implementation handles spike severity, stall severity, confidence, evidence scope, self-health severity — the colour mapping table lives in one place.

### 4.8 `TimelineStrip`

A horizontal time axis showing where events landed in the session. Tick marks for boundaries, dots for events, hover shows event detail. Used at the top of the LAG tab to give "when did things happen" context above the row list.

## 5. Colour palette

### 5.1 Surfaces (three tiers)

```
Background       #0a0e14   (deepest — outside the panel)
Panel            #0d1117   (default panel fill)
Surface          #11161f   (header strip — same as today)
SurfaceElevated  #161b22   (NEW — for cards and raised regions)
SurfaceHover     #1a2030   (NEW — hover state for clickable rows)
Border           #1f2329   (today)
BorderActive     #2a3645   (NEW — for cards that are "selected")
```

### 5.2 Text (three tiers, today)

```
Text       #c5c8ce   primary
TextMuted  #6e7480   secondary
TextDim    #4d525d   captions / faint detail
```

Already adequate. Used at consistent scale tiers (see §6).

### 5.3 Heat ramp (5 stops, used by `CostColor`)

```
Good       #95d4a3   green   — <0.5× the band's upper bound
Healthy    #b8d479   lime    — NEW intermediate (0.5–1.0× band)
Amber      #f5b342   amber   — band straddle
Hot        #e88a3c   orange  — NEW (1.0–1.5× band)
Danger     #f47174   red     — far above band
```

Smooth interpolation across the 5 stops in `CostColor`. The two new intermediates fix the harsh transitions at 0.5 (green→amber) and 1.0 (amber→red) that read as "OK / OK / SUDDENLY BAD" in the current overlay.

### 5.4 Accent / status (specialised)

```
Accent      #79c0ff   blue   — active toggle, selected tab, headings
Dormant     #b389e3   purple — engagement-low signal (future, when LiteDB)
Modder      #92c5b6   teal   — modder-targeted insights badge
```

### 5.5 Mod palette (for donut / sparkline slice colours)

A 12-colour palette of muted, mid-saturation hues that visually separate without screaming. Deterministically assigned by `modId mod 12`. Order chosen to maximise pairwise distance (alternating warm/cool):

```
#5b9bd5  #f4a460  #7ec494  #e07e9b  #b095d4  #d4b463
#7ab8d4  #d47272  #94c46a  #b884ce  #cca37e  #82b6c9
```

Heat-ramp foreground (the actual cost colouring) renders over these so the slice colour conveys identity, not severity.

## 6. Typography

Three scale tiers. Bordered text (existing helper) at consistent sizes:

```
H1  scale 0.92f   panel title, tab headers
H2  scale 0.78f   section titles, stat block values
Row scale 0.72f   primary row text (mod name, hook name)
Body scale 0.62f  secondary row text (annotations, units, captions)
```

Up from the current `0.55-0.82` mishmash. Consistency makes scanning easier than maximising info-density per pixel.

## 7. Per-tab redesigns

### 7.1 SUMMARY tab (was OVERVIEW)

Renamed because "Summary" is what players say. The default landing tab; designed to answer "what do I have, and what's hurting me the most across CPU, allocation, and spike contribution" in a single glance.

**This is explicitly a multi-dimensional view**, not a CPU-only ranking. The mod's value proposition is "performance is more than ms" — the SUMMARY tab is where that becomes visible. `ModImpactScorer` already produces `ModImpact` records carrying `CpuMs`, `SpikeMs`, `AllocMsEq` (via the self-calibrated GC heuristic), and a `Composite` score that fuses them. The previous OverviewTab consumed this data but rendered it as a single linear leaderboard. The redesign surfaces the multi-dimensional structure that's already in the data.

```
┌──────────────────────────────────────────────────────────────────────┐
│  ┌─ impact share ───────────┐  ┌─ top contributors ──────────────┐  │
│  │                          │  │                                  │  │
│  │      ●●●●●●●             │  │ ▰▰▰▰▰▰▰▰▰▰  TBoR        45.0   │  │
│  │    ●  TBoR  ●            │  │ ░cpu 32 │ alloc 8KB │ spk 3    │  │
│  │   ●  39%   ●             │  │ ▰▰▰▰▰▱▱▱▱▱  Fargo's     22.0   │  │
│  │   ●  45.0  ●             │  │ ░cpu 15 │ alloc 12KB│ spk 0    │  │
│  │    ●       ●             │  │ ▰▰▰▱▱▱▱▱▱▱  Thorium     12.0   │  │
│  │      ●●●●●●●             │  │ ░cpu 11 │ alloc 2KB │ spk 1    │  │
│  │                          │  │                                  │  │
│  │  Slices = COMPOSITE %    │  │                                  │  │
│  │  Hue = dominant axis:    │  │                                  │  │
│  │  ● cpu ● alloc ● spike   │  │                                  │  │
│  └──────────────────────────┘  └──────────────────────────────────┘  │
│  ┌─ session trend (last 30s) ─────────────────────────────────────┐  │
│  │ cpu    /\/\___/\__/\_____/\__/\______/\/\______/\_             │  │
│  │ alloc  ___/\____/\\__________/\___________/\____               │  │
│  │ spike  │           │       │           │                       │  │
│  └────────────────────────────────────────────────────────────────┘  │
│  ┌─ all mods ─────────────────────────────  [sort: composite ▾]  ┐  │
│  │ 1  ▰▰▰▰▰▰▰▰▰▰  TheBindingOfRarria       45.0  cpu+alloc+spk │  │
│  │    ░░░░░░░░░ cpu 32│░░░░░░░ alloc 8KB│░░░ spk 3              │  │
│  │ 2  ▰▰▰▰▰▱▱▱▱▱  Verdant                   8.2  cpu            │  │
│  │    ░░░░░░ cpu 7│░ alloc <1KB│— spk 0                          │  │
│  │ ... every single mod, no hidden-low filter ...                │  │
│  └────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────┘
```

Four regions:

1. **Donut chart (top-left).** Slice size = **mod's share of total COMPOSITE impact**, not CPU. Centre stat: top contributor's name + composite percentage + absolute composite value. Top 8 slices + "others" lumping the tail. **Slice hue encodes the dominant component** for that mod — blue for CPU-dominant, purple for alloc-dominant, amber for spike-dominant. The legend lives below the donut. This is the visual identity of the tab: a glance tells you not just who's heavy, but WHY they're heavy.

2. **Top contributors strip (top-right).** Top 5 mods by current sort (defaults to composite). Each entry is two lines:
   - Line 1: heat-coloured composite bar + mod name + composite value
   - Line 2: three small component indicators (cpu ms / alloc bytes / spike count) in muted text
   
   This makes the component breakdown legible at the top of the tab without requiring a click-to-expand.

3. **Session trend (full width).** **Three stacked sparklines** sharing an x-axis:
   - CPU frame time (filled, heat-coloured by value)
   - Allocation rate (filled, separate y-axis)
   - Spike-tick markers (vertical lines at spike positions)
   
   A glance shows "where did spikes line up with allocation bursts" — the prerequisite for "the LiteDB version will tell you it was a GC trigger".

4. **All-mods ranking (bottom, primary area).** Every loaded mod, no filter. Two lines per row:
   - Line 1: heat composite bar + name + composite value + "cpu+alloc+spike" tags showing which axes contribute meaningfully (>10% share)
   - Line 2: three component mini-bars + their values
   
   Sort dropdown drives the ranking ("composite / cpu / spike / alloc / alloc rate / coverage"). Click row → expand inline (future: side panel).

### Changes from current OverviewTab

| Out | In |
|---|---|
| Hardcoded `<1 ms / 1–4 ms / >4 ms` impact bands | Bands derived from `Baseline.FrameMsMedian × {0.5, 1.0, 1.5}` |
| `_hideLowImpact = true` default | Filter pill exists but defaults OFF — every mod always shown |
| Single linear leaderboard rendered from already-multi-dimensional `ModImpact` data | Multi-dimensional surface: donut hue + component sub-bars expose the cpu/alloc/spike breakdown the scorer already computes |
| CPU-only donut spec (my first draft of this plan) | Composite donut with dominant-component hue encoding |
| Single frame sparkline | Stacked cpu + alloc + spike-marker sparklines on a synchronised axis |
| `ImpactSortMode` chips at top | Sort dropdown with the same modes |

### 7.2 TREE tab

Stays structurally the same — it's already the right shape for hierarchical drill-down. Visual refresh only:

- Row heights bump to 22 px / 20 px / 18 px
- Mod-name column uses `RowScale` typography
- Each row's value column gets a `HeatBar` instead of a thin gradient bar
- Coverage badge becomes a `SeverityBadge` pill
- Expanded category row gets a subtle elevated background (`SurfaceElevated`)
- Hover state on rows (currently absent) — `SurfaceHover` background, cursor coupling

### 7.3 LAG tab (was SPIKES)

Already unified spikes + stalls in the last batch. Visual polish:

- Add `TimelineStrip` at top showing event positions across the session
- Larger rows with `SeverityBadge` for spike severity / stall severity
- Cause icon (●▲) becomes a small inline pill with cause-coloured background
- Mini-sparkline next to spike rows showing the frame-time profile around the spike (5 ticks before + spike + 5 ticks after)
- Hover/click → side panel with full event detail (the existing data, just surfaced richer)

### 7.4 EVENTS tab

The events tab is the cleanest of the current set — minimal changes. Just typography and row heights to match the rest. Future: per-bucket sparkline showing how each event surface (boss type / biome / weather) trends.

### 7.5 INSIGHTS tab

Card-per-insight layout instead of dense rows. Each card:

```
┌────────────────────────────────────────────────────────────────┐
│  Hot Hook Dominance                  [Low]  [this session]    │
│  TheBindingOfRarria                                            │
│  ArtifactSetPlayer.PostUpdateEquips() is 84% of category cost  │
│  ─────────────────────────────────────────────────────────────│
│  evidence: 0.840 share · 12 confirmations · pAdj 1.000        │
└────────────────────────────────────────────────────────────────┘
```

- Larger heading (pattern name)
- Confidence + EvidenceScope as side-by-side badges
- Subject (mod / hook) prominent below
- Body string on its own line (more room → less truncation)
- Evidence row in muted text

The current InsightsTab cramming everything onto two lines per row is the worst readability issue in the existing UI.

### 7.6 New tab: SELF

A dedicated tab for the profiler's own diagnostics. Rationale: PROFILER HEALTH in the chrome is a glance; users who care about our own impact (modder audience) want detail. Contents:

- Current self-health (resident, install delta, bytes-per-hook, fraction of game, severity)
- Sparkline of our heap usage over the session
- Hook-per-mod table (which mod's content we hooked the most, for memory-burn diagnosis)
- "compare with current modlist" — would the next-larger Calamity install push us over X MB? Pure interactive projection.

This tab earns its place by being the player-facing version of the "memory burn" sub-project's data. Implementing it forces us to expose the data correctly; the sub-project's perf work then surfaces immediately.

## 8. Quality-of-life additions

- **Pin tab.** Right-click a tab → "set as default". Persisted; the overlay opens to that tab next session.
- **Compact mode toggle.** Header pill; collapses row heights back to current sizes, shrinks chrome cards. For users who want the dense view.
- **Snapshot view.** A button in the LIVE/PAUSED toggle: "snapshot to clipboard" copies the current tab's data as a markdown table. Cheap to implement; instantly useful for sharing modlist screenshots that include the actual numbers.
- **Search box.** New row above the All-Mods ranking; live filters by name match. Persists last-search across F9 toggles.
- **Settings cog.** Top-right of the chrome opens a small settings panel: panel size, compact mode, default tab, sort defaults, future ModConfig values.

## 9. Implementation phases

The work splits into roughly seven commits, each independently buildable. Each phase delivers a complete, working UI — there's never a half-broken intermediate state.

| Phase | Commits | What lands |
|---|---|---|
| 0 | 1 | New layout constants, `ProfilerTheme` palette additions (5-stop ramp, surface tiers, mod palette). Existing UI works unchanged but reads from the new constants. Sanity check: nothing visually different yet. |
| 1 | 1 | Component library — `RoundedSurface`, `ProfilerCard`, `HeatBar`, `Pill` modernised, `StatBlock`, `SeverityBadge`. Drawn but not yet adopted by tabs. Tests for hit-testing where relevant. |
| 2 | 1 | Chrome rewrite — header / tab strip / stat cards / PROFILER HEALTH card. Tab content still uses the old rendering inside the new chrome. |
| 3 | 1 | SUMMARY tab redesign with donut, top-contributors, frame sparkline, all-mods ranking. Hardcoded bands removed, hidden-low filter defaults OFF. |
| 4 | 1 | TREE + EVENTS visual refresh (no structural change). |
| 5 | 1 | LAG (SPIKES) tab — timeline strip, severity badges, optional mini-sparkline. |
| 6 | 1 | INSIGHTS card layout + new SELF tab. |
| 7 | 1 | Resizable panel + ModConfig for size / default tab / compact mode. Snapshot-to-clipboard. |

QoL items (search box, settings cog, pin-default-tab) land alongside Phase 7 as they share the persistence machinery.

Total: roughly 8 commits over the work. Estimated 1500–2500 LOC across UI/Overlay/.

## 10. Risks and open questions

1. **DonutChart performance.** `DrawUserPrimitives` per slice per frame is fine at 60 Hz with 8 slices — that's ~1500 triangles/sec, MonoGame eats this. But we should 1 Hz-cadence the geometry rebuild (only the vertex positions; the draw call itself is always fresh). Confirm with the profiler's own measurement after Phase 3.

3. **Resizable panel + persisted size.** Adds the first `ModConfig` to the mod. Trivial in tModLoader but it's a milestone we should be deliberate about (does our config get its own JSON file? where is it surfaced in the mods menu?).

4. **Caner pin-default-tab semantics.** Per-mod or per-modlist? I'd default to global, but worth a confirmation.

5. **Compact mode interaction.** If compact mode is on, do the new surfaces still render with rounded corners + cards, just smaller? Or does it revert to the current flat style? I lean "smaller + still modern", but the cheaper implementation is "revert".

6. **Self tab placement.** Add it as the rightmost tab so it doesn't displace anyone's default. Hide it behind a "modder mode" config toggle? Or always visible since we already surface PROFILER HEALTH in the chrome? I lean always-visible.

## 11. What's NOT in this overhaul

Deliberate omissions, listed so they don't get sneaked in:

- **No new data collection.** UI work only. The detectors, baselines, attribution stay exactly as they are.
- **No LiteDB integration.** That's the next milestone; this UI prepares for it (e.g. the SELF tab's "compare with bigger modlist" projection assumes per-session-stored data, but doesn't require it yet — works on `bytesPerHook` extrapolation alone).
- **No accessibility audit (colour-blind palette etc).** Worth doing as a follow-up; out of scope for this commit-train.
- **No localisation pass.** Strings stay in English; hjson localisation is an existing pipeline that picks up new strings on its own.
- **No mobile / controller support.** Mouse-driven only.

---

## 12. Decisions log

| Date | Question | Decision |
|---|---|---|
| 2026-05-20 | Rounded corners? | **No.** Sharp corners; visual hierarchy comes from surface tiers + heat ramp + tiered typography. Texture-asset and procedural-polygon approaches both rejected as cost-not-worth-it. |
| 2026-05-20 | Pin-default-tab scope | Global. One preference across all modlists. |
| 2026-05-20 | Compact mode behaviour | Keep cards just smaller, modern style stays. Cheaper revert-to-flat path discarded. |
| 2026-05-20 | Default mode sizing | Default is **bigger than the original 880 px proposal — 1120 px wide**. The framing flipped: default = "look at it while standing in the base" (generous, info-rich), compact = "walk around with it during boss fights" (HUD-style, 720 px). Default isn't the "moderate middle"; it's the "spend time with it" mode. |
| 2026-05-20 | SELF tab visibility | Always visible. PROFILER HEALTH in the chrome is the glance, SELF tab is the detail. |
| 2026-05-20 | OVERVIEW → SUMMARY rename | Yes. |
| 2026-05-20 | SUMMARY hidden-low filter default | Off. Show every mod always; filter pill exists, defaults off. |

**Next step:** Phase 0 (layout constants + theme palette additions, zero visible change).
