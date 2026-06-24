---
page: timeline
label: Timeline
last_audit: 2026-06-24T17:30:47
scenario: full
open_findings: P2×1
---

# Timeline — page dossier

> Auto-maintained by the dashboard testing suite (`tools/testing`). Findings accumulate across audit runs and update in place by stable id. The **Notes** section is hand-owned and preserved across runs.

## Page at a glance
- **Panes discovered (last run):** segment detail, attendance, chronicle
- **Scenario audited:** `full`
- **Open findings:** P2×1

## Open findings
### [P2] Swimlane gantt strands most of its horizontal width · `id:e2066b`
- **Category:** layout-alignment
- **Where:** page / whole state
- **What:** The Biome and Weather lanes hold their segment blocks in only the far-left ~12% of the lane; the rest of every lane out to the right edge is empty. Biome shows two short adjacent blocks (Forest..., Graveyard...) then dead space to the panel edge, Weather shows one short Day block then empty, and Boss/Invasion/Subworld are empty with their 'none this session' copy pinned hard against the far-right edge over an empty middle. The time axis runs well past the last captured segment instead of tiling the captured start->end range edge to edge, so the gantt reads as a cluster of thumbnails in a large empty rectangle rather than a timeline spanning the panel. The full-width frame-trace strip and the now-full-width heatstrip directly above make the stranded gantt look unfinished by comparison.
- **Suggested fix:** Scale the swimlane time axis to the captured session range so the blocks tile the full panel width (the last segment ends at the right edge), matching the heatstrip that was already stretched full-width. Centre the 'none this session' empty-state copy within each empty lane rather than right-aligning it, so empty lanes read as deliberately empty instead of right-edge stragglers floating over dead space.
- **First seen:** 2026-06-24T17:30:47 · **Last seen:** 2026-06-24T17:30:47

## Not seen last run
_Reported by an earlier run but not re-flagged latest — fixed, or simply not re-surfaced. Confirm before deleting._

### [P1] Swimlane fills are rainbow decoration, not encoding · `id:9610e1`  _(not seen last run)_
- **Category:** colour-encoding
- **Where:** page / whole state
- **What:** The Biome and Weather segment blocks are filled with a left-to-right multi-colour gradient that does not encode any ordered magnitude or single category. A rainbow across one block carries no readable meaning and violates the monochrome-chrome / colour-is-data rule; the perf ramp is a sequential green->amber->red, not a multi-hue band.
- **Suggested fix:** Replace the gradient with a single encoding: one categorical hue per biome/weather type (matching the per-mod hue system), OR a sequential perf-ramp fill keyed to the segment's cost. One block = one meaning. Reserve hue for category and the green->amber->red ramp for magnitude.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P1] Transition track text clipped and overlapping · `id:08d141`  _(not seen last run)_
- **Category:** layout-alignment
- **Where:** page / whole state
- **What:** The dashed transition/control track top-right reads 'change (open) -> c weather or bi biome Purity -> (off)'. Words are truncated mid-token ('c', 'bi') and the segments run into each other with no separation, so the track is unparseable as either a transition record or a control.
- **Suggested fix:** If this is a transition track, lay each transition out as a discrete, non-overlapping pill with full labels (or ellipsis with a hover/title for overflow). If it is a control row, move it out of the timeline data area into the chrome header and give each token its own padded segment. Do not let labels truncate mid-word.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P1] Swimlane block labels illegible on gradient fill · `id:1c63d4`  _(not seen last run)_
- **Category:** readability
- **Where:** page / whole state
- **What:** In the Biome lane the segment block 'Forest Overworld Height Purity vis...' and in the Weather lane the 'Day' block render dark text directly on a saturated multi-hue gradient (green/amber/red/blue, orange/cyan/red). The text has near-zero contrast against the brightest stops of the fill and is unreadable at render size.
- **Suggested fix:** Stop overlaying text on the data fill. Either put the label above/beside the block on neutral chrome, or give the block a single solid categorical hue with a high-contrast monochrome text layer (white on dark, or a tinted-left-bar row treatment) rather than a rainbow gradient behind the text.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Empty Boss/Invasion/Subworld lanes have no empty-state copy · `id:8e9910`  _(not seen last run)_
- **Category:** affordance
- **Where:** page / whole state
- **What:** The Boss, Invasion, and Subworld swimlane rows are completely blank but each still consumes a full lane height. There is no 'no boss fights this session' / 'no invasions' copy, so the rows read as unfinished rather than legitimately empty.
- **Suggested fix:** Add a muted, descriptive empty-state line inside each blank lane ('no boss segments yet', 'no invasions this session'), or collapse empty lanes to a thin labelled strip so they do not eat full-height dead space when there is nothing to show.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Solid grey share bar encodes nothing · `id:556500`  _(not seen last run)_
- **Category:** colour-encoding
- **Where:** ATTENDANCE / default state
- **What:** The horizontal share bar under the stats is a single flat grey fill with no segmentation. It looks like a data bar but conveys no proportion; the only breakdown ('vanilla 1 (100.0%)') is in the legend below with a grey swatch.
- **Suggested fix:** In the degenerate single-series state, drop the bar (it adds no information) and keep just the legend, or render the bar only once there are >=2 series to split. When populated, segment it with the per-mod categorical hues so the share bar actually encodes proportion.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] All-zero readout under a populated-looking header · `id:c70f5a`  _(not seen last run)_
- **Category:** hierarchy
- **Where:** ATTENDANCE / default state
- **What:** Every stat (biome ticks, modded, invasions, boss segments) reads 0, the share bar is a single solid grey block, and the table header (MOD / BIOME TICKS / SHARE / INVASIONS / BOSS SEGS) sits over no rows. The panel presents the full furniture of a populated table while carrying no data, which reads as broken rather than empty.
- **Suggested fix:** When attendance has no modded ticks, show an explicit empty state ('no biome-tick attribution captured this session') in place of the zero-grid and the headerless table, rather than rendering a complete-looking table shell over zeros.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Per-minute strip strands 85% dead space · `id:29cbac`  _(not seen last run)_
- **Category:** layout-alignment
- **Where:** page / whole state
- **What:** The activity strip (green bars with red markers) occupies only the far-left ~8% of a full-width panel; the remaining width is empty. The chart is given far more horizontal space than its data uses, leaving a large empty rectangle.
- **Suggested fix:** Either stretch the strip to span the full panel width (one bar per minute across the session), or shrink the panel to fit the data and reclaim the vertical space. A per-minute strip should read as a continuous timeline spanning the panel, not a thumbnail in the corner.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Long event log lacks filter by type · `id:38f03a`  _(not seen last run)_
- **Category:** affordance
- **Where:** CHRONICLE / default state
- **What:** The chronicle is a long, scrollable list mixing death/join/weather/transition events with no way to filter to one type. The same segment-type chips exist at the top of the page (all/biome/weather/boss/invasion/subworld) but the event log itself offers no type filter.
- **Suggested fix:** Add a lightweight type filter (segmented control or chip row) to the chronicle header so a user can isolate e.g. just deaths or just transitions in a long session, consistent with the filter affordance already used on the swimlane.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Two-line event rows waste vertical rhythm · `id:308baf`  _(not seen last run)_
- **Category:** chart-fit
- **Where:** CHRONICLE / default state
- **What:** Each chronicle entry spans two lines (badge + timestamp on line one, description on line two) with generous gaps, so only ~8 events fit in the viewport and a long session needs heavy scrolling. The timestamp and description could share a baseline.
- **Suggested fix:** Collapse each event to a single dense row: [badge] [timestamp] [description] on one baseline, biggest-first or chronological, so more of the session's arc is visible at once. Consider a left time-gutter so timestamps align in a column for vertical scanning.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Green 'transition' badge collides with perf-ramp green · `id:67f610`  _(not seen last run)_
- **Category:** colour-encoding
- **Where:** CHRONICLE / default state
- **What:** In the event log the 'transition' badge is outlined green while 'death' is red, 'join'/'weather' are neutral grey. Green is the 'good/within-budget' end of the perf ramp elsewhere in the app; reusing it for the neutral event-type 'transition' makes one hue mean two things across panes.
- **Suggested fix:** Use neutral grey badges for non-severity event types (transition, join, weather) and reserve coloured badges for genuine severity (death red). If event types need categorical tinting, draw it from the categorical hue set, not the perf-ramp green that signals 'good' elsewhere.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

## Notes
_Hand-written notes about this page live here and survive audit re-runs._

<!-- PP-AUDIT-DATA
{
 "tab": "timeline",
 "findings": [
  {
   "id": "08d141",
   "severity": "P1",
   "category": "layout-alignment",
   "title": "Transition track text clipped and overlapping",
   "panel": "page",
   "state": "whole",
   "what": "The dashed transition/control track top-right reads 'change (open) -> c weather or bi biome Purity -> (off)'. Words are truncated mid-token ('c', 'bi') and the segments run into each other with no separation, so the track is unparseable as either a transition record or a control.",
   "fix": "If this is a transition track, lay each transition out as a discrete, non-overlapping pill with full labels (or ellipsis with a hover/title for overflow). If it is a control row, move it out of the timeline data area into the chrome header and give each token its own padded segment. Do not let labels truncate mid-word.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "1c63d4",
   "severity": "P1",
   "category": "readability",
   "title": "Swimlane block labels illegible on gradient fill",
   "panel": "page",
   "state": "whole",
   "what": "In the Biome lane the segment block 'Forest Overworld Height Purity vis...' and in the Weather lane the 'Day' block render dark text directly on a saturated multi-hue gradient (green/amber/red/blue, orange/cyan/red). The text has near-zero contrast against the brightest stops of the fill and is unreadable at render size.",
   "fix": "Stop overlaying text on the data fill. Either put the label above/beside the block on neutral chrome, or give the block a single solid categorical hue with a high-contrast monochrome text layer (white on dark, or a tinted-left-bar row treatment) rather than a rainbow gradient behind the text.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "9610e1",
   "severity": "P1",
   "category": "colour-encoding",
   "title": "Swimlane fills are rainbow decoration, not encoding",
   "panel": "page",
   "state": "whole",
   "what": "The Biome and Weather segment blocks are filled with a left-to-right multi-colour gradient that does not encode any ordered magnitude or single category. A rainbow across one block carries no readable meaning and violates the monochrome-chrome / colour-is-data rule; the perf ramp is a sequential green->amber->red, not a multi-hue band.",
   "fix": "Replace the gradient with a single encoding: one categorical hue per biome/weather type (matching the per-mod hue system), OR a sequential perf-ramp fill keyed to the segment's cost. One block = one meaning. Reserve hue for category and the green->amber->red ramp for magnitude.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "29cbac",
   "severity": "P2",
   "category": "layout-alignment",
   "title": "Per-minute strip strands 85% dead space",
   "panel": "page",
   "state": "whole",
   "what": "The activity strip (green bars with red markers) occupies only the far-left ~8% of a full-width panel; the remaining width is empty. The chart is given far more horizontal space than its data uses, leaving a large empty rectangle.",
   "fix": "Either stretch the strip to span the full panel width (one bar per minute across the session), or shrink the panel to fit the data and reclaim the vertical space. A per-minute strip should read as a continuous timeline spanning the panel, not a thumbnail in the corner.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "556500",
   "severity": "P2",
   "category": "colour-encoding",
   "title": "Solid grey share bar encodes nothing",
   "panel": "ATTENDANCE",
   "state": "default",
   "what": "The horizontal share bar under the stats is a single flat grey fill with no segmentation. It looks like a data bar but conveys no proportion; the only breakdown ('vanilla 1 (100.0%)') is in the legend below with a grey swatch.",
   "fix": "In the degenerate single-series state, drop the bar (it adds no information) and keep just the legend, or render the bar only once there are >=2 series to split. When populated, segment it with the per-mod categorical hues so the share bar actually encodes proportion.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "8e9910",
   "severity": "P2",
   "category": "affordance",
   "title": "Empty Boss/Invasion/Subworld lanes have no empty-state copy",
   "panel": "page",
   "state": "whole",
   "what": "The Boss, Invasion, and Subworld swimlane rows are completely blank but each still consumes a full lane height. There is no 'no boss fights this session' / 'no invasions' copy, so the rows read as unfinished rather than legitimately empty.",
   "fix": "Add a muted, descriptive empty-state line inside each blank lane ('no boss segments yet', 'no invasions this session'), or collapse empty lanes to a thin labelled strip so they do not eat full-height dead space when there is nothing to show.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "c70f5a",
   "severity": "P2",
   "category": "hierarchy",
   "title": "All-zero readout under a populated-looking header",
   "panel": "ATTENDANCE",
   "state": "default",
   "what": "Every stat (biome ticks, modded, invasions, boss segments) reads 0, the share bar is a single solid grey block, and the table header (MOD / BIOME TICKS / SHARE / INVASIONS / BOSS SEGS) sits over no rows. The panel presents the full furniture of a populated table while carrying no data, which reads as broken rather than empty.",
   "fix": "When attendance has no modded ticks, show an explicit empty state ('no biome-tick attribution captured this session') in place of the zero-grid and the headerless table, rather than rendering a complete-looking table shell over zeros.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "e2066b",
   "severity": "P2",
   "category": "layout-alignment",
   "title": "Swimlane gantt strands most of its horizontal width",
   "panel": "page",
   "state": "whole",
   "what": "The Biome and Weather lanes hold their segment blocks in only the far-left ~12% of the lane; the rest of every lane out to the right edge is empty. Biome shows two short adjacent blocks (Forest..., Graveyard...) then dead space to the panel edge, Weather shows one short Day block then empty, and Boss/Invasion/Subworld are empty with their 'none this session' copy pinned hard against the far-right edge over an empty middle. The time axis runs well past the last captured segment instead of tiling the captured start->end range edge to edge, so the gantt reads as a cluster of thumbnails in a large empty rectangle rather than a timeline spanning the panel. The full-width frame-trace strip and the now-full-width heatstrip directly above make the stranded gantt look unfinished by comparison.",
   "fix": "Scale the swimlane time axis to the captured session range so the blocks tile the full panel width (the last segment ends at the right edge), matching the heatstrip that was already stretched full-width. Centre the 'none this session' empty-state copy within each empty lane rather than right-aligning it, so empty lanes read as deliberately empty instead of right-edge stragglers floating over dead space.",
   "first_seen": "2026-06-24T17:30:47",
   "state_seen": "2026-06-24T17:30:47"
  },
  {
   "id": "308baf",
   "severity": "P3",
   "category": "chart-fit",
   "title": "Two-line event rows waste vertical rhythm",
   "panel": "CHRONICLE",
   "state": "default",
   "what": "Each chronicle entry spans two lines (badge + timestamp on line one, description on line two) with generous gaps, so only ~8 events fit in the viewport and a long session needs heavy scrolling. The timestamp and description could share a baseline.",
   "fix": "Collapse each event to a single dense row: [badge] [timestamp] [description] on one baseline, biggest-first or chronological, so more of the session's arc is visible at once. Consider a left time-gutter so timestamps align in a column for vertical scanning.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "38f03a",
   "severity": "P3",
   "category": "affordance",
   "title": "Long event log lacks filter by type",
   "panel": "CHRONICLE",
   "state": "default",
   "what": "The chronicle is a long, scrollable list mixing death/join/weather/transition events with no way to filter to one type. The same segment-type chips exist at the top of the page (all/biome/weather/boss/invasion/subworld) but the event log itself offers no type filter.",
   "fix": "Add a lightweight type filter (segmented control or chip row) to the chronicle header so a user can isolate e.g. just deaths or just transitions in a long session, consistent with the filter affordance already used on the swimlane.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "67f610",
   "severity": "P3",
   "category": "colour-encoding",
   "title": "Green 'transition' badge collides with perf-ramp green",
   "panel": "CHRONICLE",
   "state": "default",
   "what": "In the event log the 'transition' badge is outlined green while 'death' is red, 'join'/'weather' are neutral grey. Green is the 'good/within-budget' end of the perf ramp elsewhere in the app; reusing it for the neutral event-type 'transition' makes one hue mean two things across panes.",
   "fix": "Use neutral grey badges for non-severity event types (transition, join, weather) and reserve coloured badges for genuine severity (death red). If event types need categorical tinting, draw it from the categorical hue set, not the perf-ramp green that signals 'good' elsewhere.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  }
 ]
}
PP-AUDIT-DATA -->
