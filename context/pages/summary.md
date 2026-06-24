---
page: summary
label: Summary
last_audit: 2026-06-24T17:30:47
scenario: full
open_findings: P2×1 · P3×2
---

# Summary — page dossier

> Auto-maintained by the dashboard testing suite (`tools/testing`). Findings accumulate across audit runs and update in place by stable id. The **Notes** section is hand-owned and preserved across runs.

## Page at a glance
- **Panes discovered (last run):** frame time · last 30s, impact share, session trend · last 30s, session timeframe · minute by minute, now playing, recent events, mods · cascading tree
- **Scenario audited:** `full`
- **Open findings:** P2×1 · P3×2

## Open findings
### [P2] Uniform-weight event wall with no filter/sort and a mismatched count label · `id:e9ec1d`
- **Category:** affordance
- **Where:** Recent Events / default state
- **What:** The list is ~16-18 rows of the same 'spike N.Nms · top <Mod> N.NN ms' idiom, every row the same weight with a leading orange spike-bolt marker, and there is no filter, sort, or grouping by mod. The leading spike magnitude (the number that matters) is the same size and colour as the trailing 'top <Mod> N.NN ms', so the eye has no scan anchor. The header reads 'last 12' yet clearly more than 12 rows render, so the cap label does not match the rows shown.
- **Suggested fix:** Make the leading spike magnitude the dominant element of each row (heavier weight / larger size, or aligned into its own column) so the list has a scan anchor, and add a per-mod filter or sort given the length. Reconcile the 'last 12' label with the actual row count (either honour the cap or correct the label).
- **First seen:** 2026-06-24T17:30:47 · **Last seen:** 2026-06-24T17:30:47

### [P3] Heatmap reads as a flat green strip at rest · `id:609d83`
- **Category:** chart-fit
- **Where:** Session Timeframe · Minute by Minute / default state
- **What:** Legend-dwarfs-data is fixed: the legend is now a single compact inline row of small swatches beneath the tiles. But all 8 minute tiles render the identical 'smooth' green with no within-band shading, so the heatmap conveys no minute-to-minute variation and still reads as a status strip rather than a magnitude encoding. The green→amber→red ramp it is built for is invisible this session.
- **Suggested fix:** Apply a subtle within-band lightness ramp by exact ms (lighter→darker green) so even an all-smooth session shows minute-to-minute texture at rest, keeping the tiles legible as a magnitude encoding without requiring hover.
- **First seen:** 2026-06-24T17:30:47 · **Last seen:** 2026-06-24T17:30:47

### [P3] Marginal-contribution copy states 'no measurable fps change' twice · `id:a3cf48`
- **Category:** readability
- **Where:** Impact Share / selected state
- **What:** The degenerate '60 fps vs 60 fps' is fixed and the prior run-on is trimmed. What remains: the sentence reads 'This mod adds 0.89 ms to the average frame, which is no measurable fps change — the frame already fits the 60 fps budget, so the 0.89 ms shows up as headroom rather than lost fps. Right now: 0.79 ms, no measurable fps change.' The live clause restates the average clause's conclusion ('no measurable fps change') verbatim, so the second occurrence reads as boilerplate rather than new information. Identical in the right-rail drawer and the impact-share--selected crop.
- **Suggested fix:** Drop the trailing 'no measurable fps change' from the live clause and render it as just the value, e.g. 'Right now: 0.79 ms.' Emit a distinct fps note for the live figure only when it falls in a genuinely different fps regime from the average.
- **First seen:** 2026-06-24T17:30:47 · **Last seen:** 2026-06-24T17:30:47

## Not seen last run
_Reported by an earlier run but not re-flagged latest — fixed, or simply not re-surfaced. Confirm before deleting._

### [P1] "60 fps and 60 fps" comparison degenerate · `id:fc683f`  _(not seen last run)_
- **Category:** readability
- **Where:** Impact Share / selected state
- **What:** In the mod-card detail (drawer and impact-share--selected), the marginal-contribution sentence reads "the difference between 60 fps and 60 fps on average, and 60 vs 60 live." Both sides of each comparison are identical, so the sentence is meaningless and reads as a broken template. A player sees a comparison that compares a value to itself.
- **Suggested fix:** Compute and show the two genuinely different fps figures (frame-time-with-mod vs frame-time-minus-this-mod's-marginal-cost). If the delta rounds to the same integer fps at the current frame time, drop the fps clause entirely and state the ms delta only, rather than emitting "60 vs 60".
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Long event list with no filter/search · `id:373939`  _(not seen last run)_
- **Category:** affordance
- **Where:** Recent Events / default state
- **What:** The recent-events list shows ~18 rows of "spike N.Nms · top <Mod> N.NN ms" with relative timestamps, all the same weight, and scrolls beyond the visible 12. There is no way to filter by mod, by spike magnitude, or to sort — at 18+ near-identical rows the eye has no scanning path and no way to find the events for one mod.
- **Suggested fix:** Add a lightweight filter (by mod and/or a magnitude threshold) and/or let the rows be grouped or sorted by mod. At minimum, emphasise the spike magnitude (the number that matters) typographically so the list has a scan anchor instead of a uniform wall.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Two rows then ~70% empty panel · `id:8fe9df`  _(not seen last run)_
- **Category:** hierarchy
- **Where:** Now Playing / default state
- **What:** The now-playing panel holds two encounter rows at the top and leaves roughly the bottom two-thirds as flat empty space. With only two open encounters the panel is enormously over-tall, creating a large dead void that breaks the page rhythm against the denser recent-events panel beside it.
- **Suggested fix:** Let the panel height hug its content (shrink-to-fit) with a sensible min-height, or fill the reserved space with a low-cost secondary encoding (a thin per-encounter cost sparkline or a "no further encounters open" muted footer line) so the height is earned.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Heatmap legend dwarfs the data · `id:377a6d`  _(not seen last run)_
- **Category:** layout-alignment
- **Where:** Session Timeframe · Minute by Minute / default state
- **What:** The panel shows 8 small green tiles on one row, then a full-width 6-item legend (smooth / 17-25ms / 25-40ms / 40-60ms / >60ms · spikes / boss fight) beneath. The legend occupies more visual weight and width than the actual heatmap, and five of its six categories never appear in the data — the legend out-sizes the chart it explains.
- **Suggested fix:** Make the tiles larger and the heatmap the dominant element; render the legend as a compact inline key (smaller swatches, tighter) below or to the side. Consider collapsing legend entries that have zero occurrences this session, or moving the full key behind a hover/tooltip so the data leads.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Overlaid axis labels collide with the trace · `id:b8a16b`  _(not seen last run)_
- **Category:** readability
- **Where:** Frame Time · Last 30s / default state
- **What:** The "60fps" reference label and the "median" baseline label are drawn directly over the chart area at the right edge, where the spike bars run through them, and "median" overlaps the trace itself. They read as floating, half-occluded text rather than clean axis annotations.
- **Suggested fix:** Move the reference labels out of the plotting area (into the right gutter or a small inset chip) so the dashed 60fps line and median line are annotated without text sitting on top of the data, mirroring how the 0.44 / 18.4 axis bounds already sit in the margin.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] All-green heatmap loses its ramp · `id:223baf`  _(not seen last run)_
- **Category:** chart-fit
- **Where:** Session Timeframe · Minute by Minute / default state
- **What:** Every minute tile renders the same "smooth" green, so the heatmap conveys no variation and the sequential green→amber→red ramp it is built for is invisible in this session. As a single-colour row it currently reads as a status strip, not a heatmap, which under-sells the encoding.
- **Suggested fix:** This is acceptable for an all-smooth session, but consider a subtle within-band shade (lighter→darker green by exact ms) so even an all-smooth run shows minute-to-minute texture, keeping the heatmap legible as a magnitude encoding rather than a flat block.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Two different bar idioms on one tab · `id:dbcd6e`  _(not seen last run)_
- **Category:** consistency
- **Where:** Mods · Cascading Tree / default state
- **What:** The cascading-tree cost column uses a single perf-ramp gradient bar per row (green→amber→red), while the drawer/selected category-breakdown uses flat solid-green bars, and the impact donut uses categorical hues. Three different bar/colour idioms appear within the Summary tab for cost magnitude, which slightly weakens the "one component vocabulary" promise.
- **Suggested fix:** Decide one magnitude-bar treatment (the perf ramp is the stronger encoding) and apply it consistently to the category-breakdown bars in the drawer, reserving categorical hue strictly for per-mod series in the donut/legend.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Expanded-row "loading…" with no skeleton · `id:d45aff`  _(not seen last run)_
- **Category:** layout-alignment
- **Where:** Mods · Cascading Tree / selected state
- **What:** When CalamityMod is expanded, the child area shows a bare left-aligned "loading…" string with no skeleton rows or spacing, sitting flush under the row. It reads as unstyled placeholder text rather than a designed loading state, and momentarily breaks the otherwise tidy row grid.
- **Suggested fix:** Render the expanded loading state as one or two muted skeleton child-rows (or a small inline spinner aligned to the child indent) so the in-flight state matches the component vocabulary used elsewhere.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

## Notes
_Hand-written notes about this page live here and survive audit re-runs._

<!-- PP-AUDIT-DATA
{
 "tab": "summary",
 "findings": [
  {
   "id": "fc683f",
   "severity": "P1",
   "category": "readability",
   "title": "\"60 fps and 60 fps\" comparison degenerate",
   "panel": "Impact Share",
   "state": "selected",
   "what": "In the mod-card detail (drawer and impact-share--selected), the marginal-contribution sentence reads \"the difference between 60 fps and 60 fps on average, and 60 vs 60 live.\" Both sides of each comparison are identical, so the sentence is meaningless and reads as a broken template. A player sees a comparison that compares a value to itself.",
   "fix": "Compute and show the two genuinely different fps figures (frame-time-with-mod vs frame-time-minus-this-mod's-marginal-cost). If the delta rounds to the same integer fps at the current frame time, drop the fps clause entirely and state the ms delta only, rather than emitting \"60 vs 60\".",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "373939",
   "severity": "P2",
   "category": "affordance",
   "title": "Long event list with no filter/search",
   "panel": "Recent Events",
   "state": "default",
   "what": "The recent-events list shows ~18 rows of \"spike N.Nms \u00b7 top <Mod> N.NN ms\" with relative timestamps, all the same weight, and scrolls beyond the visible 12. There is no way to filter by mod, by spike magnitude, or to sort \u2014 at 18+ near-identical rows the eye has no scanning path and no way to find the events for one mod.",
   "fix": "Add a lightweight filter (by mod and/or a magnitude threshold) and/or let the rows be grouped or sorted by mod. At minimum, emphasise the spike magnitude (the number that matters) typographically so the list has a scan anchor instead of a uniform wall.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "377a6d",
   "severity": "P2",
   "category": "layout-alignment",
   "title": "Heatmap legend dwarfs the data",
   "panel": "Session Timeframe \u00b7 Minute by Minute",
   "state": "default",
   "what": "The panel shows 8 small green tiles on one row, then a full-width 6-item legend (smooth / 17-25ms / 25-40ms / 40-60ms / >60ms \u00b7 spikes / boss fight) beneath. The legend occupies more visual weight and width than the actual heatmap, and five of its six categories never appear in the data \u2014 the legend out-sizes the chart it explains.",
   "fix": "Make the tiles larger and the heatmap the dominant element; render the legend as a compact inline key (smaller swatches, tighter) below or to the side. Consider collapsing legend entries that have zero occurrences this session, or moving the full key behind a hover/tooltip so the data leads.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "8fe9df",
   "severity": "P2",
   "category": "hierarchy",
   "title": "Two rows then ~70% empty panel",
   "panel": "Now Playing",
   "state": "default",
   "what": "The now-playing panel holds two encounter rows at the top and leaves roughly the bottom two-thirds as flat empty space. With only two open encounters the panel is enormously over-tall, creating a large dead void that breaks the page rhythm against the denser recent-events panel beside it.",
   "fix": "Let the panel height hug its content (shrink-to-fit) with a sensible min-height, or fill the reserved space with a low-cost secondary encoding (a thin per-encounter cost sparkline or a \"no further encounters open\" muted footer line) so the height is earned.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "b8a16b",
   "severity": "P2",
   "category": "readability",
   "title": "Overlaid axis labels collide with the trace",
   "panel": "Frame Time \u00b7 Last 30s",
   "state": "default",
   "what": "The \"60fps\" reference label and the \"median\" baseline label are drawn directly over the chart area at the right edge, where the spike bars run through them, and \"median\" overlaps the trace itself. They read as floating, half-occluded text rather than clean axis annotations.",
   "fix": "Move the reference labels out of the plotting area (into the right gutter or a small inset chip) so the dashed 60fps line and median line are annotated without text sitting on top of the data, mirroring how the 0.44 / 18.4 axis bounds already sit in the margin.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "e9ec1d",
   "severity": "P2",
   "category": "affordance",
   "title": "Uniform-weight event wall with no filter/sort and a mismatched count label",
   "panel": "Recent Events",
   "state": "default",
   "what": "The list is ~16-18 rows of the same 'spike N.Nms \u00b7 top <Mod> N.NN ms' idiom, every row the same weight with a leading orange spike-bolt marker, and there is no filter, sort, or grouping by mod. The leading spike magnitude (the number that matters) is the same size and colour as the trailing 'top <Mod> N.NN ms', so the eye has no scan anchor. The header reads 'last 12' yet clearly more than 12 rows render, so the cap label does not match the rows shown.",
   "fix": "Make the leading spike magnitude the dominant element of each row (heavier weight / larger size, or aligned into its own column) so the list has a scan anchor, and add a per-mod filter or sort given the length. Reconcile the 'last 12' label with the actual row count (either honour the cap or correct the label).",
   "first_seen": "2026-06-24T17:30:47",
   "state_seen": "2026-06-24T17:30:47"
  },
  {
   "id": "223baf",
   "severity": "P3",
   "category": "chart-fit",
   "title": "All-green heatmap loses its ramp",
   "panel": "Session Timeframe \u00b7 Minute by Minute",
   "state": "default",
   "what": "Every minute tile renders the same \"smooth\" green, so the heatmap conveys no variation and the sequential green\u2192amber\u2192red ramp it is built for is invisible in this session. As a single-colour row it currently reads as a status strip, not a heatmap, which under-sells the encoding.",
   "fix": "This is acceptable for an all-smooth session, but consider a subtle within-band shade (lighter\u2192darker green by exact ms) so even an all-smooth run shows minute-to-minute texture, keeping the heatmap legible as a magnitude encoding rather than a flat block.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "609d83",
   "severity": "P3",
   "category": "chart-fit",
   "title": "Heatmap reads as a flat green strip at rest",
   "panel": "Session Timeframe \u00b7 Minute by Minute",
   "state": "default",
   "what": "Legend-dwarfs-data is fixed: the legend is now a single compact inline row of small swatches beneath the tiles. But all 8 minute tiles render the identical 'smooth' green with no within-band shading, so the heatmap conveys no minute-to-minute variation and still reads as a status strip rather than a magnitude encoding. The green\u2192amber\u2192red ramp it is built for is invisible this session.",
   "fix": "Apply a subtle within-band lightness ramp by exact ms (lighter\u2192darker green) so even an all-smooth session shows minute-to-minute texture at rest, keeping the tiles legible as a magnitude encoding without requiring hover.",
   "first_seen": "2026-06-24T17:30:47",
   "state_seen": "2026-06-24T17:30:47"
  },
  {
   "id": "a3cf48",
   "severity": "P3",
   "category": "readability",
   "title": "Marginal-contribution copy states 'no measurable fps change' twice",
   "panel": "Impact Share",
   "state": "selected",
   "what": "The degenerate '60 fps vs 60 fps' is fixed and the prior run-on is trimmed. What remains: the sentence reads 'This mod adds 0.89 ms to the average frame, which is no measurable fps change \u2014 the frame already fits the 60 fps budget, so the 0.89 ms shows up as headroom rather than lost fps. Right now: 0.79 ms, no measurable fps change.' The live clause restates the average clause's conclusion ('no measurable fps change') verbatim, so the second occurrence reads as boilerplate rather than new information. Identical in the right-rail drawer and the impact-share--selected crop.",
   "fix": "Drop the trailing 'no measurable fps change' from the live clause and render it as just the value, e.g. 'Right now: 0.79 ms.' Emit a distinct fps note for the live figure only when it falls in a genuinely different fps regime from the average.",
   "first_seen": "2026-06-24T17:30:47",
   "state_seen": "2026-06-24T17:30:47"
  },
  {
   "id": "d45aff",
   "severity": "P3",
   "category": "layout-alignment",
   "title": "Expanded-row \"loading\u2026\" with no skeleton",
   "panel": "Mods \u00b7 Cascading Tree",
   "state": "selected",
   "what": "When CalamityMod is expanded, the child area shows a bare left-aligned \"loading\u2026\" string with no skeleton rows or spacing, sitting flush under the row. It reads as unstyled placeholder text rather than a designed loading state, and momentarily breaks the otherwise tidy row grid.",
   "fix": "Render the expanded loading state as one or two muted skeleton child-rows (or a small inline spinner aligned to the child indent) so the in-flight state matches the component vocabulary used elsewhere.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "dbcd6e",
   "severity": "P3",
   "category": "consistency",
   "title": "Two different bar idioms on one tab",
   "panel": "Mods \u00b7 Cascading Tree",
   "state": "default",
   "what": "The cascading-tree cost column uses a single perf-ramp gradient bar per row (green\u2192amber\u2192red), while the drawer/selected category-breakdown uses flat solid-green bars, and the impact donut uses categorical hues. Three different bar/colour idioms appear within the Summary tab for cost magnitude, which slightly weakens the \"one component vocabulary\" promise.",
   "fix": "Decide one magnitude-bar treatment (the perf ramp is the stronger encoding) and apply it consistently to the category-breakdown bars in the drawer, reserving categorical hue strictly for per-mod series in the donut/legend.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  }
 ]
}
PP-AUDIT-DATA -->
