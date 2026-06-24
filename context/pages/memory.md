---
page: memory
label: Memory
last_audit: 2026-06-24T17:30:47
scenario: full
open_findings: P2×2 · P3×1
---

# Memory — page dossier

> Auto-maintained by the dashboard testing suite (`tools/testing`). Findings accumulate across audit runs and update in place by stable id. The **Notes** section is hand-owned and preserved across runs.

## Page at a glance
- **Panes discovered (last run):** memory · where it goes, per mod, breakdown
- **Scenario audited:** `full`
- **Open findings:** P2×2 · P3×1

## Open findings
### [P2] Row selection drives the breakdown but leaves no styling on the source row · `id:bde476`
- **Category:** affordance
- **Where:** per mod / selected state
- **What:** Selecting a per-mod row now populates the BREAKDOWN panel (02-breakdown.png shows CalamityMod fully expanded: code 17.3 MB / textures 1.33 GB / managed 703.2 MB plus the instrumentation cards), so the detail half of the selection model is wired. But the source row itself carries no selection feedback: 01-per-mod--selected.png is byte-identical (same MD5) to 01-per-mod--hover.png and differs from the default crop only by a one-row scroll, and a leftmost-column pixel scan finds zero rows with any tinted left bar or row fill. A user who clicks a row sees the breakdown change far below but gets no confirmation on the row they clicked and cannot tell which row is driving the panel. Half-applied selection model: detail responds, source row does not.
- **Suggested fix:** Apply the shared row selection model to the clicked row (reserved tinted left bar + subtle row fill) so the active row and the breakdown it drives are visibly linked. The breakdown population already works; only the source-row half is missing.
- **First seen:** 2026-06-24T17:30:47 · **Last seen:** 2026-06-24T17:30:47

### [P2] Split-strip slices still show no on-strip selection state and do not drive the breakdown · `id:e8c184`
- **Category:** affordance
- **Where:** memory · where it goes / selected state
- **What:** The header advertises 'slices sorted by size · click one for its breakdown', but no slice ever marks itself active. 00-memory-where-it-goes--selected.png differs from default only in that the 'profiler overhead' segmented toggle is engaged (a view-mode switch, not a slice selection); every slice stays full-brightness with no outline or dim. In _after-click.png the strip renders all slices at full brightness while the BREAKDOWN below sits on its empty 'Select a mod slice or row' prompt, so a strip interaction neither marks a slice nor populates the detail panel. The affordance is promised in copy but the strip produces no selection feedback and no breakdown drive.
- **Suggested fix:** On slice click, mark the active slice (outline / brightness lift / dim the rest) and route it into the same breakdown channel the per-mod row uses (which already renders correctly), so strip selection and detail agree.
- **First seen:** 2026-06-24T17:30:47 · **Last seen:** 2026-06-24T17:30:47

### [P3] No hover feedback on either table rows or strip slices · `id:b50702`
- **Category:** affordance
- **Where:** page / hover state
- **What:** Both --hover captures are byte-identical (same MD5) to their non-hover counterparts: 01-per-mod--hover.png == 01-per-mod--selected.png, and 00-memory-where-it-goes--hover.png == the default strip. Hovering a per-mod row or a strip slice produces no row-fill, no left-bar, no slice lift, no pre-click cue. Both surfaces are clickable (rows drive the breakdown) but give no signal they respond, so a first-time user gets no hint either surface is interactive until they click. This is the hover half of the shared interaction model, absent on this tab.
- **Suggested fix:** Add the shared hover treatment to rows (subtle row-fill / left-edge cue on pointer-over) and to strip slices (brightness lift on hover), so both surfaces advertise clickability before selection, consistent with the component library.
- **First seen:** 2026-06-24T17:30:47 · **Last seen:** 2026-06-24T17:30:47

## Not seen last run
_Reported by an earlier run but not re-flagged latest — fixed, or simply not re-surfaced. Confirm before deleting._

### [P2] No visible selection feedback on the per-mod row · `id:10d09f`  _(not seen last run)_
- **Category:** affordance
- **Where:** per mod / selected state
- **What:** The --selected capture of the per-mod table is visually indistinguishable from the default capture: no row carries a tinted left bar, highlight, or active styling, and the BREAKDOWN drawer below stays on its empty 'select a mod slice or row for its breakdown' prompt. Either the selection produced no visible row feedback or the click did not register; from the screenshots a user gets no confirmation that a row is selected.
- **Suggested fix:** Apply the shared row selection model (reserved tinted left bar + subtle row fill) on click and populate the BREAKDOWN drawer, so the selected row and its drawer state agree.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] ~29-row table has no sort or search control · `id:7d3b0d`  _(not seen last run)_
- **Category:** affordance
- **Where:** per mod / default state
- **What:** The per-mod table runs to ~29 rows (scrolls well past the visible 15) yet its header (MOD / RAM / FOOTPRINT / HOOKS / ALLOC/S) carries no sort carets, no clickable-column affordance, and there is no search/filter box. Rows are pre-sorted by RAM descending but the user cannot re-sort by HOOKS or ALLOC/S, nor jump to a mod by name. The rubric flags any list over ~12 rows with no search/sort/filter.
- **Suggested fix:** Add sort affordances to the numeric column headers (click-to-sort caret, active-column highlight) and a small search/filter input in the PER MOD panel header so a 29-mod roster is navigable without scrolling.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Split-strip slice click yields no visible selection state · `id:f77d4c`  _(not seen last run)_
- **Category:** affordance
- **Where:** memory · where it goes / selected state
- **What:** The panel header promises 'click one for its breakdown' and 'slices sorted by size · click one for its breakdown', but the --selected capture of the split-strip is identical to its default: no slice is outlined, dimmed, or otherwise marked as active, and the BREAKDOWN drawer stays empty. The affordance is advertised in copy but produces no on-strip feedback.
- **Suggested fix:** On slice click, mark the active slice (outline / brightness lift / dim the rest) and drive the BREAKDOWN drawer, matching the selection model used elsewhere.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] FOOTPRINT mini stacked-bar has no legend or key · `id:ca9618`  _(not seen last run)_
- **Category:** chart-fit
- **Where:** per mod / default state
- **What:** The FOOTPRINT column renders a two-segment stacked bar per row (a coloured lead segment, orange on most rows, green on SilkyUIFramework / Daybreak / ImproveGame, then a lilac segment). Nothing on the panel explains what the two segments encode or why the lead colour switches between orange and green. The column is a chart with no legend, so it reads as decoration rather than data.
- **Suggested fix:** Add a one-line legend or column sub-label naming the two footprint segments (e.g. 'managed | native', 'code | data') and key the lead-segment colour switch, or collapse to a single-metric bar if the split is not meaningful.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Lilac used for both a per-mod hue and the FOOTPRINT segment fill · `id:ef6bce`  _(not seen last run)_
- **Category:** colour-encoding
- **Where:** per mod / default state
- **What:** The lilac/purple that fills the second FOOTPRINT segment on every row is the same hue carrying CalamityMod's categorical identity in the split-strip and in CalamityMod's own RAM bar. One colour is doing two jobs across the panel: a per-mod category in the strip and a generic footprint-segment fill in the table. The rubric flags the same colour meaning two things.
- **Suggested fix:** Give the FOOTPRINT segments their own neutral or dedicated encoding hues distinct from the per-mod categorical palette, so lilac is not simultaneously 'CalamityMod' and 'footprint segment B'.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Ghosted alloc/s value duplicated inside the RAM bar lane · `id:85d266`  _(not seen last run)_
- **Category:** readability
- **Where:** per mod / default state
- **What:** A faint grey value is left-aligned at the start of the RAM-bar track on every row (e.g. '65.7 KB' on CalamityMod, '467 B' on PerformanceProfiler, '577 B' on BossChecklist). That number is the same figure shown again in the right-hand ALLOC/S column, so each row prints alloc/s twice. The left copy sits under no header, inside the RAM lane, and at a glance reads as a second RAM figure competing with the bold right-aligned RAM value.
- **Suggested fix:** Drop the faint left-aligned alloc/s echo from the RAM lane; alloc/s is already its own labelled right-hand column. If an in-lane value is wanted, label it and make it the bar's own metric, not a duplicate of a different column.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Empty-state prompt is the panel's only content and reads as dead space · `id:b0aa89`  _(not seen last run)_
- **Category:** hierarchy
- **Where:** breakdown / default state
- **What:** The BREAKDOWN panel's default state is a single centred line of muted text ('select a mod slice or row for its breakdown') in a wide, otherwise empty band. The empty state is honest but minimal: no hint of what the breakdown will contain (per-category split, hook list, alloc trend), so the panel reads as unfinished space rather than a primed slot.
- **Suggested fix:** Keep the prompt but add a faint preview of the breakdown's structure (greyed category labels / a skeleton split-bar) or a one-line description of what selecting a mod reveals, so the empty band signals intent rather than emptiness.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Very short RAM bars for small mods are near-invisible against the track · `id:92d35e`  _(not seen last run)_
- **Category:** readability
- **Where:** per mod / default state
- **What:** For mods below ~10 MB (Combinations, CalamityModMusic, SilkyUIFramework, Daybreak, Nitrate, BSWLmod) the RAM bar collapses to a 1-2 px sliver that barely separates from the dark track, so the magnitude encoding carries no information at the low end while the numeric value still does. The bar stops being readable exactly where the long tail of the roster lives.
- **Suggested fix:** Apply a minimum-width floor or a log/perceptual scale to the RAM bar so small-but-nonzero values remain visible, or drop the bar below a threshold and lean on the numeric value alone.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

## Notes
_Hand-written notes about this page live here and survive audit re-runs._

<!-- PP-AUDIT-DATA
{
 "tab": "memory",
 "findings": [
  {
   "id": "10d09f",
   "severity": "P2",
   "category": "affordance",
   "title": "No visible selection feedback on the per-mod row",
   "panel": "per mod",
   "state": "selected",
   "what": "The --selected capture of the per-mod table is visually indistinguishable from the default capture: no row carries a tinted left bar, highlight, or active styling, and the BREAKDOWN drawer below stays on its empty 'select a mod slice or row for its breakdown' prompt. Either the selection produced no visible row feedback or the click did not register; from the screenshots a user gets no confirmation that a row is selected.",
   "fix": "Apply the shared row selection model (reserved tinted left bar + subtle row fill) on click and populate the BREAKDOWN drawer, so the selected row and its drawer state agree.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "7d3b0d",
   "severity": "P2",
   "category": "affordance",
   "title": "~29-row table has no sort or search control",
   "panel": "per mod",
   "state": "default",
   "what": "The per-mod table runs to ~29 rows (scrolls well past the visible 15) yet its header (MOD / RAM / FOOTPRINT / HOOKS / ALLOC/S) carries no sort carets, no clickable-column affordance, and there is no search/filter box. Rows are pre-sorted by RAM descending but the user cannot re-sort by HOOKS or ALLOC/S, nor jump to a mod by name. The rubric flags any list over ~12 rows with no search/sort/filter.",
   "fix": "Add sort affordances to the numeric column headers (click-to-sort caret, active-column highlight) and a small search/filter input in the PER MOD panel header so a 29-mod roster is navigable without scrolling.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "85d266",
   "severity": "P2",
   "category": "readability",
   "title": "Ghosted alloc/s value duplicated inside the RAM bar lane",
   "panel": "per mod",
   "state": "default",
   "what": "A faint grey value is left-aligned at the start of the RAM-bar track on every row (e.g. '65.7 KB' on CalamityMod, '467 B' on PerformanceProfiler, '577 B' on BossChecklist). That number is the same figure shown again in the right-hand ALLOC/S column, so each row prints alloc/s twice. The left copy sits under no header, inside the RAM lane, and at a glance reads as a second RAM figure competing with the bold right-aligned RAM value.",
   "fix": "Drop the faint left-aligned alloc/s echo from the RAM lane; alloc/s is already its own labelled right-hand column. If an in-lane value is wanted, label it and make it the bar's own metric, not a duplicate of a different column.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "bde476",
   "severity": "P2",
   "category": "affordance",
   "title": "Row selection drives the breakdown but leaves no styling on the source row",
   "panel": "per mod",
   "state": "selected",
   "what": "Selecting a per-mod row now populates the BREAKDOWN panel (02-breakdown.png shows CalamityMod fully expanded: code 17.3 MB / textures 1.33 GB / managed 703.2 MB plus the instrumentation cards), so the detail half of the selection model is wired. But the source row itself carries no selection feedback: 01-per-mod--selected.png is byte-identical (same MD5) to 01-per-mod--hover.png and differs from the default crop only by a one-row scroll, and a leftmost-column pixel scan finds zero rows with any tinted left bar or row fill. A user who clicks a row sees the breakdown change far below but gets no confirmation on the row they clicked and cannot tell which row is driving the panel. Half-applied selection model: detail responds, source row does not.",
   "fix": "Apply the shared row selection model to the clicked row (reserved tinted left bar + subtle row fill) so the active row and the breakdown it drives are visibly linked. The breakdown population already works; only the source-row half is missing.",
   "first_seen": "2026-06-24T17:30:47",
   "state_seen": "2026-06-24T17:30:47"
  },
  {
   "id": "ca9618",
   "severity": "P2",
   "category": "chart-fit",
   "title": "FOOTPRINT mini stacked-bar has no legend or key",
   "panel": "per mod",
   "state": "default",
   "what": "The FOOTPRINT column renders a two-segment stacked bar per row (a coloured lead segment, orange on most rows, green on SilkyUIFramework / Daybreak / ImproveGame, then a lilac segment). Nothing on the panel explains what the two segments encode or why the lead colour switches between orange and green. The column is a chart with no legend, so it reads as decoration rather than data.",
   "fix": "Add a one-line legend or column sub-label naming the two footprint segments (e.g. 'managed | native', 'code | data') and key the lead-segment colour switch, or collapse to a single-metric bar if the split is not meaningful.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "e8c184",
   "severity": "P2",
   "category": "affordance",
   "title": "Split-strip slices still show no on-strip selection state and do not drive the breakdown",
   "panel": "memory \u00b7 where it goes",
   "state": "selected",
   "what": "The header advertises 'slices sorted by size \u00b7 click one for its breakdown', but no slice ever marks itself active. 00-memory-where-it-goes--selected.png differs from default only in that the 'profiler overhead' segmented toggle is engaged (a view-mode switch, not a slice selection); every slice stays full-brightness with no outline or dim. In _after-click.png the strip renders all slices at full brightness while the BREAKDOWN below sits on its empty 'Select a mod slice or row' prompt, so a strip interaction neither marks a slice nor populates the detail panel. The affordance is promised in copy but the strip produces no selection feedback and no breakdown drive.",
   "fix": "On slice click, mark the active slice (outline / brightness lift / dim the rest) and route it into the same breakdown channel the per-mod row uses (which already renders correctly), so strip selection and detail agree.",
   "first_seen": "2026-06-24T17:30:47",
   "state_seen": "2026-06-24T17:30:47"
  },
  {
   "id": "ef6bce",
   "severity": "P2",
   "category": "colour-encoding",
   "title": "Lilac used for both a per-mod hue and the FOOTPRINT segment fill",
   "panel": "per mod",
   "state": "default",
   "what": "The lilac/purple that fills the second FOOTPRINT segment on every row is the same hue carrying CalamityMod's categorical identity in the split-strip and in CalamityMod's own RAM bar. One colour is doing two jobs across the panel: a per-mod category in the strip and a generic footprint-segment fill in the table. The rubric flags the same colour meaning two things.",
   "fix": "Give the FOOTPRINT segments their own neutral or dedicated encoding hues distinct from the per-mod categorical palette, so lilac is not simultaneously 'CalamityMod' and 'footprint segment B'.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "f77d4c",
   "severity": "P2",
   "category": "affordance",
   "title": "Split-strip slice click yields no visible selection state",
   "panel": "memory \u00b7 where it goes",
   "state": "selected",
   "what": "The panel header promises 'click one for its breakdown' and 'slices sorted by size \u00b7 click one for its breakdown', but the --selected capture of the split-strip is identical to its default: no slice is outlined, dimmed, or otherwise marked as active, and the BREAKDOWN drawer stays empty. The affordance is advertised in copy but produces no on-strip feedback.",
   "fix": "On slice click, mark the active slice (outline / brightness lift / dim the rest) and drive the BREAKDOWN drawer, matching the selection model used elsewhere.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "92d35e",
   "severity": "P3",
   "category": "readability",
   "title": "Very short RAM bars for small mods are near-invisible against the track",
   "panel": "per mod",
   "state": "default",
   "what": "For mods below ~10 MB (Combinations, CalamityModMusic, SilkyUIFramework, Daybreak, Nitrate, BSWLmod) the RAM bar collapses to a 1-2 px sliver that barely separates from the dark track, so the magnitude encoding carries no information at the low end while the numeric value still does. The bar stops being readable exactly where the long tail of the roster lives.",
   "fix": "Apply a minimum-width floor or a log/perceptual scale to the RAM bar so small-but-nonzero values remain visible, or drop the bar below a threshold and lean on the numeric value alone.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "b0aa89",
   "severity": "P3",
   "category": "hierarchy",
   "title": "Empty-state prompt is the panel's only content and reads as dead space",
   "panel": "breakdown",
   "state": "default",
   "what": "The BREAKDOWN panel's default state is a single centred line of muted text ('select a mod slice or row for its breakdown') in a wide, otherwise empty band. The empty state is honest but minimal: no hint of what the breakdown will contain (per-category split, hook list, alloc trend), so the panel reads as unfinished space rather than a primed slot.",
   "fix": "Keep the prompt but add a faint preview of the breakdown's structure (greyed category labels / a skeleton split-bar) or a one-line description of what selecting a mod reveals, so the empty band signals intent rather than emptiness.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "b50702",
   "severity": "P3",
   "category": "affordance",
   "title": "No hover feedback on either table rows or strip slices",
   "panel": "page",
   "state": "hover",
   "what": "Both --hover captures are byte-identical (same MD5) to their non-hover counterparts: 01-per-mod--hover.png == 01-per-mod--selected.png, and 00-memory-where-it-goes--hover.png == the default strip. Hovering a per-mod row or a strip slice produces no row-fill, no left-bar, no slice lift, no pre-click cue. Both surfaces are clickable (rows drive the breakdown) but give no signal they respond, so a first-time user gets no hint either surface is interactive until they click. This is the hover half of the shared interaction model, absent on this tab.",
   "fix": "Add the shared hover treatment to rows (subtle row-fill / left-edge cue on pointer-over) and to strip slices (brightness lift on hover), so both surfaces advertise clickability before selection, consistent with the component library.",
   "first_seen": "2026-06-24T17:30:47",
   "state_seen": "2026-06-24T17:30:47"
  }
 ]
}
PP-AUDIT-DATA -->
