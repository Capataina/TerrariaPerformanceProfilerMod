---
page: lag
label: Lag
last_audit: 2026-06-24T17:30:47
scenario: full
open_findings: P2×1 · P3×3
---

# Lag — page dossier

> Auto-maintained by the dashboard testing suite (`tools/testing`). Findings accumulate across audit runs and update in place by stable id. The **Notes** section is hand-owned and preserved across runs.

## Page at a glance
- **Panes discovered (last run):** session lag, cause × context, fingerprint clusters, per-segment lag density, gc pressure, allocation → gc → freed, lag rhythm
- **Scenario audited:** `full`
- **Open findings:** P2×1 · P3×3

## Open findings
### [P2] Histogram half under-fills its column, halves do not end together · `id:514733`
- **Category:** layout-alignment
- **Where:** LAG RHYTHM / default state
- **What:** The prior dead-space inverted rather than closed. The left INTERVAL DISTRIBUTION histogram is now only 6 binned, legible buckets (0.12-16.12s .. 176.19-192.20s) and they sit clustered around the vertical middle of the left column, leaving a clear empty band above them (roughly the top third, between the 'time between repeated events' sub-header and the first bar) and a smaller gap below. The right RHYTHM CLUSTERS table runs ~13 rows edge-to-edge down the full panel height, so the two halves terminate at very different vertical extents and the left column reads as half-empty.
- **Suggested fix:** Top-align the histogram bars so they start just under the sub-header, or give the histogram taller rows / more vertical air so its 6 bars span the same height the 13-row cluster table occupies, so the two sub-panels end together and the left column is not visibly under-filled.
- **First seen:** 2026-06-24T17:30:47 · **Last seen:** 2026-06-24T17:30:47

### [P3] CONTEXT column still all dashes in the cluster table · `id:147553`
- **Category:** affordance
- **Where:** FINGERPRINT CLUSTERS / default state
- **What:** The CONTEXT column persists across the whole cluster table with a muted '—' chip on every row (identical in default, hover, scrolled, and after-click states). It carries zero information in this dataset yet consumes a full column between CAUSE and EVENTS. Note the selected-cluster detail strip DID get the human relabel and a demoted raw id, but the empty column was not dropped from the table itself.
- **Suggested fix:** Collapse or hide the CONTEXT column when every visible row is empty, mirroring the logic that produced the cleaned detail strip, or surface the context the cluster actually carries. An all-dash column is dead width the eye still has to skip past.
- **First seen:** 2026-06-24T17:30:47 · **Last seen:** 2026-06-24T17:30:47

### [P3] Panel title still sells a 2D matrix the body does not render · `id:83bfa7`
- **Category:** chart-fit
- **Where:** CAUSE × CONTEXT / default state
- **What:** The inner body is much improved: it has been honestly relabelled 'LAG EVENTS BY CAUSE' with a '—' marker for the single-context degenerate case, the panel is now tightened to its three rows (no large empty region), and the bars scale by count (Spike 50, Main thread freeze 5, Long frame 2). But the outer panel title still reads 'CAUSE × CONTEXT' with subtitle 'events by cause and surrounding context', promising a 2D cross-tab the body does not deliver when only one context exists. The inner heading tells the truth while the outer frame still oversells.
- **Suggested fix:** Make the outer panel title/subtitle reflect the single-context case the same way the inner heading already does (e.g. 'Lag events by cause'), or render the true cause×context heatmap when more than one context is present.
- **First seen:** 2026-06-24T17:30:47 · **Last seen:** 2026-06-24T17:30:47

### [P3] GC peak label crowds and nearly clips the right edge · `id:fd568d`
- **Category:** layout-alignment
- **Where:** GC PRESSURE / default state
- **What:** The prior collision with the dashed rule is resolved (the label now sits above the line), but the '12.14 GB peak' annotation still runs hard against the plot's right edge: the final letters of 'peak' are right up against the right border with almost no gutter, so the label terminates at (and visually nearly clips against) the edge.
- **Suggested fix:** Nudge the label left so 'peak' clears the right edge with a comfortable margin, or right-anchor it so the text ends before the border rather than touching it.
- **First seen:** 2026-06-24T17:30:47 · **Last seen:** 2026-06-24T17:30:47

## Not seen last run
_Reported by an earlier run but not re-flagged latest — fixed, or simply not re-surfaced. Confirm before deleting._

### [P1] Duplicate VS BASE column · `id:3e5d3f`  _(not seen last run)_
- **Category:** consistency
- **Where:** PER-SEGMENT LAG DENSITY / default state
- **What:** The table has two columns headed 'VS BASE ▾' carrying identical data: the second column (right of SEGMENT) reads 25.13×, 22.07×, 13.40×... and the rightmost column reads the same 25.13×, 22.07×, 13.40× for every row. Same header, same values, two columns.
- **Suggested fix:** Drop one of the two VS BASE columns. Keep a single VS-BASE column on the right and reuse the freed left column for something the table lacks (e.g. raw event count, or remove it and widen the bar).
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Density bars are always full-width · `id:3feb6c`  _(not seen last run)_
- **Category:** chart-fit
- **Where:** PER-SEGMENT LAG DENSITY / default state
- **What:** Every SPIKES/STALLS bar spans the full column regardless of magnitude. Run #14 (12/0, 86.2 events/min, 25.13×) and Run #3 (0/1, 6.9/min, 2.00×) draw the same full-width bar; only the spike/stall colour split varies. For a panel titled 'density' the bar encodes the spike-vs-stall ratio but not the relative density between rows, so the most important comparison (which segment is worst) is carried only by the numeric columns.
- **Suggested fix:** Scale total bar length to events/min (or VS BASE) so denser segments read longer, and keep the orange/red split inside that length for spike-vs-stall ratio. Then the bar answers 'which segment is worst' at a glance.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Titled as a cause×context matrix, renders as a flat bar list · `id:4e0cc3`  _(not seen last run)_
- **Category:** chart-fit
- **Where:** CAUSE × CONTEXT / default state
- **What:** Header and subtitle promise a 2D view ('CAUSE × CONTEXT', 'events by cause and surrounding context'), but the body shows 'SINGLE CONTEXT · —' and a one-dimensional ranked bar list (Spike 50, MainThreadFreeze 5, LongFrame 2). The context axis collapses to a single '—', so the cross-tab the title sells is absent.
- **Suggested fix:** When only one context exists, retitle/relabel the panel to what it actually shows (e.g. 'lag events by cause') rather than presenting a matrix frame with a degenerate single-context row; or render the true cause×context heatmap when more than one context is present.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Two unaligned row systems read as one confused table · `id:bd8222`  _(not seen last run)_
- **Category:** hierarchy
- **Where:** LAG RHYTHM / default state
- **What:** The dense left histogram (~60 rows, count beside each bar) and the right interval table (~16 rows: INTERVAL / EVENTS / TOP MOD / OF SESSION) sit side by side with no visual link and different row counts, so it is unclear whether a left bar maps to a right row. The eye cannot tell if these are one dataset or two.
- **Suggested fix:** Make the two halves share a row grid (left bar aligns with its right-table row) or visually separate them into clearly distinct sub-panels with their own headers, so the relationship is explicit.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Right-side table fills only the top third, large dead space below · `id:0a4677`  _(not seen last run)_
- **Category:** layout-alignment
- **Where:** LAG RHYTHM / default state
- **What:** The left histogram column runs the full panel height (~60 interval buckets, e.g. 0.12s–2.84s and beyond), but the right-side interval table + 'OF SESSION' share column stops after ~13 rows, leaving the bottom two-thirds of the right half empty against the still-running left histogram.
- **Suggested fix:** Either align the right table to the same vertical extent as the histogram (one row per visible bucket), or cap the histogram to the same ~13 buckets the table shows so the two halves end together and the dead space closes.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Raw cluster id shown to the player · `id:3814e2`  _(not seen last run)_
- **Category:** readability
- **Where:** FINGERPRINT CLUSTERS / selected state
- **What:** The selected-cluster detail strip surfaces a cryptic raw id 'Spike|m0|—||h0' as the cluster label. That is an internal fingerprint key, not player-readable, and it is the most prominent label in the selected detail.
- **Suggested fix:** Render a human description for the selected cluster (e.g. 'Spike · CalamityMod · no context') and keep the raw id as a small secondary/monospace tag if needed for debugging.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Histogram micro-bars and interval labels too small to read · `id:d85ae4`  _(not seen last run)_
- **Category:** readability
- **Where:** LAG RHYTHM / default state
- **What:** The left histogram is a dense stack of ~60 thin bars with tiny interval labels (0.12s, 0.13s, 0.15s, 0.18s...) and single-digit counts beside them at the real render size. Adjacent bars and labels are near-indistinguishable; there is no scanning path to the modal interval.
- **Suggested fix:** Bin the intervals into fewer, taller buckets, increase row height/label size, and highlight the modal bucket so the periodicity the panel is about is legible at a glance.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] CONTEXT column is all dashes · `id:9e3804`  _(not seen last run)_
- **Category:** affordance
- **Where:** FINGERPRINT CLUSTERS / default state
- **What:** The CONTEXT column shows a '—' chip for every row across the cluster table, carrying no information in this dataset while occupying a full column.
- **Suggested fix:** Collapse or hide the CONTEXT column when every row is empty, or fill it with the context the cluster carries; an all-dash column is dead width.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Three rows in a panel sized for many · `id:7a2ecd`  _(not seen last run)_
- **Category:** layout-alignment
- **Where:** CAUSE × CONTEXT / default state
- **What:** The panel reserves full-width height but holds only three bar rows (Spike / MainThreadFreeze / LongFrame), leaving the lower half empty.
- **Suggested fix:** Tighten the panel height to its three rows, or fold this into a neighbouring panel, so it does not read as an unfinished/empty region.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Bar caption not centred on its bar · `id:7f3328`  _(not seen last run)_
- **Category:** layout-alignment
- **Where:** PER-SEGMENT LAG DENSITY / default state
- **What:** The 'N / M' spike/stall counts sit below each bar, left-aligned to the bar start rather than centred under the bar, so they float against the left edge while the bar extends well past them.
- **Suggested fix:** Centre the count caption under the bar, or right-align it to the bar end, so the value and the geometry it labels share an axis.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Peak label crowds the chart's top-right edge · `id:a7225d`  _(not seen last run)_
- **Category:** layout-alignment
- **Where:** GC PRESSURE / default state
- **What:** The '12.14 GB peak' annotation sits hard against the dashed reference line and the right edge of the plot, with the label baseline very close to the dashed rule so the two nearly touch.
- **Suggested fix:** Nudge the peak label down/left a few px off the dashed line and away from the right edge so it reads as an annotation on the line rather than colliding with it.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

## Notes
The duplicate VS-BASE column (P1) is the priority fix; the density-bar encoding rework
(scale bar length to events/min) should land in the same pass since both touch the
per-segment-lag-density table.

<!-- PP-AUDIT-DATA
{
 "tab": "lag",
 "findings": [
  {
   "id": "3e5d3f",
   "severity": "P1",
   "category": "consistency",
   "title": "Duplicate VS BASE column",
   "panel": "PER-SEGMENT LAG DENSITY",
   "state": "default",
   "what": "The table has two columns headed 'VS BASE \u25be' carrying identical data: the second column (right of SEGMENT) reads 25.13\u00d7, 22.07\u00d7, 13.40\u00d7... and the rightmost column reads the same 25.13\u00d7, 22.07\u00d7, 13.40\u00d7 for every row. Same header, same values, two columns.",
   "fix": "Drop one of the two VS BASE columns. Keep a single VS-BASE column on the right and reuse the freed left column for something the table lacks (e.g. raw event count, or remove it and widen the bar).",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "0a4677",
   "severity": "P2",
   "category": "layout-alignment",
   "title": "Right-side table fills only the top third, large dead space below",
   "panel": "LAG RHYTHM",
   "state": "default",
   "what": "The left histogram column runs the full panel height (~60 interval buckets, e.g. 0.12s\u20132.84s and beyond), but the right-side interval table + 'OF SESSION' share column stops after ~13 rows, leaving the bottom two-thirds of the right half empty against the still-running left histogram.",
   "fix": "Either align the right table to the same vertical extent as the histogram (one row per visible bucket), or cap the histogram to the same ~13 buckets the table shows so the two halves end together and the dead space closes.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "3814e2",
   "severity": "P2",
   "category": "readability",
   "title": "Raw cluster id shown to the player",
   "panel": "FINGERPRINT CLUSTERS",
   "state": "selected",
   "what": "The selected-cluster detail strip surfaces a cryptic raw id 'Spike|m0|\u2014||h0' as the cluster label. That is an internal fingerprint key, not player-readable, and it is the most prominent label in the selected detail.",
   "fix": "Render a human description for the selected cluster (e.g. 'Spike \u00b7 CalamityMod \u00b7 no context') and keep the raw id as a small secondary/monospace tag if needed for debugging.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "3feb6c",
   "severity": "P2",
   "category": "chart-fit",
   "title": "Density bars are always full-width",
   "panel": "PER-SEGMENT LAG DENSITY",
   "state": "default",
   "what": "Every SPIKES/STALLS bar spans the full column regardless of magnitude. Run #14 (12/0, 86.2 events/min, 25.13\u00d7) and Run #3 (0/1, 6.9/min, 2.00\u00d7) draw the same full-width bar; only the spike/stall colour split varies. For a panel titled 'density' the bar encodes the spike-vs-stall ratio but not the relative density between rows, so the most important comparison (which segment is worst) is carried only by the numeric columns.",
   "fix": "Scale total bar length to events/min (or VS BASE) so denser segments read longer, and keep the orange/red split inside that length for spike-vs-stall ratio. Then the bar answers 'which segment is worst' at a glance.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "4e0cc3",
   "severity": "P2",
   "category": "chart-fit",
   "title": "Titled as a cause\u00d7context matrix, renders as a flat bar list",
   "panel": "CAUSE \u00d7 CONTEXT",
   "state": "default",
   "what": "Header and subtitle promise a 2D view ('CAUSE \u00d7 CONTEXT', 'events by cause and surrounding context'), but the body shows 'SINGLE CONTEXT \u00b7 \u2014' and a one-dimensional ranked bar list (Spike 50, MainThreadFreeze 5, LongFrame 2). The context axis collapses to a single '\u2014', so the cross-tab the title sells is absent.",
   "fix": "When only one context exists, retitle/relabel the panel to what it actually shows (e.g. 'lag events by cause') rather than presenting a matrix frame with a degenerate single-context row; or render the true cause\u00d7context heatmap when more than one context is present.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "514733",
   "severity": "P2",
   "category": "layout-alignment",
   "title": "Histogram half under-fills its column, halves do not end together",
   "panel": "LAG RHYTHM",
   "state": "default",
   "what": "The prior dead-space inverted rather than closed. The left INTERVAL DISTRIBUTION histogram is now only 6 binned, legible buckets (0.12-16.12s .. 176.19-192.20s) and they sit clustered around the vertical middle of the left column, leaving a clear empty band above them (roughly the top third, between the 'time between repeated events' sub-header and the first bar) and a smaller gap below. The right RHYTHM CLUSTERS table runs ~13 rows edge-to-edge down the full panel height, so the two halves terminate at very different vertical extents and the left column reads as half-empty.",
   "fix": "Top-align the histogram bars so they start just under the sub-header, or give the histogram taller rows / more vertical air so its 6 bars span the same height the 13-row cluster table occupies, so the two sub-panels end together and the left column is not visibly under-filled.",
   "first_seen": "2026-06-24T17:30:47",
   "state_seen": "2026-06-24T17:30:47"
  },
  {
   "id": "bd8222",
   "severity": "P2",
   "category": "hierarchy",
   "title": "Two unaligned row systems read as one confused table",
   "panel": "LAG RHYTHM",
   "state": "default",
   "what": "The dense left histogram (~60 rows, count beside each bar) and the right interval table (~16 rows: INTERVAL / EVENTS / TOP MOD / OF SESSION) sit side by side with no visual link and different row counts, so it is unclear whether a left bar maps to a right row. The eye cannot tell if these are one dataset or two.",
   "fix": "Make the two halves share a row grid (left bar aligns with its right-table row) or visually separate them into clearly distinct sub-panels with their own headers, so the relationship is explicit.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "d85ae4",
   "severity": "P2",
   "category": "readability",
   "title": "Histogram micro-bars and interval labels too small to read",
   "panel": "LAG RHYTHM",
   "state": "default",
   "what": "The left histogram is a dense stack of ~60 thin bars with tiny interval labels (0.12s, 0.13s, 0.15s, 0.18s...) and single-digit counts beside them at the real render size. Adjacent bars and labels are near-indistinguishable; there is no scanning path to the modal interval.",
   "fix": "Bin the intervals into fewer, taller buckets, increase row height/label size, and highlight the modal bucket so the periodicity the panel is about is legible at a glance.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "147553",
   "severity": "P3",
   "category": "affordance",
   "title": "CONTEXT column still all dashes in the cluster table",
   "panel": "FINGERPRINT CLUSTERS",
   "state": "default",
   "what": "The CONTEXT column persists across the whole cluster table with a muted '\u2014' chip on every row (identical in default, hover, scrolled, and after-click states). It carries zero information in this dataset yet consumes a full column between CAUSE and EVENTS. Note the selected-cluster detail strip DID get the human relabel and a demoted raw id, but the empty column was not dropped from the table itself.",
   "fix": "Collapse or hide the CONTEXT column when every visible row is empty, mirroring the logic that produced the cleaned detail strip, or surface the context the cluster actually carries. An all-dash column is dead width the eye still has to skip past.",
   "first_seen": "2026-06-24T17:30:47",
   "state_seen": "2026-06-24T17:30:47"
  },
  {
   "id": "7a2ecd",
   "severity": "P3",
   "category": "layout-alignment",
   "title": "Three rows in a panel sized for many",
   "panel": "CAUSE \u00d7 CONTEXT",
   "state": "default",
   "what": "The panel reserves full-width height but holds only three bar rows (Spike / MainThreadFreeze / LongFrame), leaving the lower half empty.",
   "fix": "Tighten the panel height to its three rows, or fold this into a neighbouring panel, so it does not read as an unfinished/empty region.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "7f3328",
   "severity": "P3",
   "category": "layout-alignment",
   "title": "Bar caption not centred on its bar",
   "panel": "PER-SEGMENT LAG DENSITY",
   "state": "default",
   "what": "The 'N / M' spike/stall counts sit below each bar, left-aligned to the bar start rather than centred under the bar, so they float against the left edge while the bar extends well past them.",
   "fix": "Centre the count caption under the bar, or right-align it to the bar end, so the value and the geometry it labels share an axis.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "83bfa7",
   "severity": "P3",
   "category": "chart-fit",
   "title": "Panel title still sells a 2D matrix the body does not render",
   "panel": "CAUSE \u00d7 CONTEXT",
   "state": "default",
   "what": "The inner body is much improved: it has been honestly relabelled 'LAG EVENTS BY CAUSE' with a '\u2014' marker for the single-context degenerate case, the panel is now tightened to its three rows (no large empty region), and the bars scale by count (Spike 50, Main thread freeze 5, Long frame 2). But the outer panel title still reads 'CAUSE \u00d7 CONTEXT' with subtitle 'events by cause and surrounding context', promising a 2D cross-tab the body does not deliver when only one context exists. The inner heading tells the truth while the outer frame still oversells.",
   "fix": "Make the outer panel title/subtitle reflect the single-context case the same way the inner heading already does (e.g. 'Lag events by cause'), or render the true cause\u00d7context heatmap when more than one context is present.",
   "first_seen": "2026-06-24T17:30:47",
   "state_seen": "2026-06-24T17:30:47"
  },
  {
   "id": "9e3804",
   "severity": "P3",
   "category": "affordance",
   "title": "CONTEXT column is all dashes",
   "panel": "FINGERPRINT CLUSTERS",
   "state": "default",
   "what": "The CONTEXT column shows a '\u2014' chip for every row across the cluster table, carrying no information in this dataset while occupying a full column.",
   "fix": "Collapse or hide the CONTEXT column when every row is empty, or fill it with the context the cluster carries; an all-dash column is dead width.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "a7225d",
   "severity": "P3",
   "category": "layout-alignment",
   "title": "Peak label crowds the chart's top-right edge",
   "panel": "GC PRESSURE",
   "state": "default",
   "what": "The '12.14 GB peak' annotation sits hard against the dashed reference line and the right edge of the plot, with the label baseline very close to the dashed rule so the two nearly touch.",
   "fix": "Nudge the peak label down/left a few px off the dashed line and away from the right edge so it reads as an annotation on the line rather than colliding with it.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "fd568d",
   "severity": "P3",
   "category": "layout-alignment",
   "title": "GC peak label crowds and nearly clips the right edge",
   "panel": "GC PRESSURE",
   "state": "default",
   "what": "The prior collision with the dashed rule is resolved (the label now sits above the line), but the '12.14 GB peak' annotation still runs hard against the plot's right edge: the final letters of 'peak' are right up against the right border with almost no gutter, so the label terminates at (and visually nearly clips against) the edge.",
   "fix": "Nudge the label left so 'peak' clears the right edge with a comfortable margin, or right-anchor it so the text ends before the border rather than touching it.",
   "first_seen": "2026-06-24T17:30:47",
   "state_seen": "2026-06-24T17:30:47"
  }
 ]
}
PP-AUDIT-DATA -->
