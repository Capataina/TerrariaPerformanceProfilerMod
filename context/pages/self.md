---
page: self
label: Self
last_audit: 2026-06-24T16:13:05
scenario: full
open_findings: P2×3 · P3×3
---

# Self — page dossier

> Auto-maintained by the dashboard testing suite (`tools/testing`). Findings accumulate across audit runs and update in place by stable id. The **Notes** section is hand-owned and preserved across runs.

## Page at a glance
- **Panes discovered (last run):** profiler health, install footprint, process context, attribution backend, hook distribution · top 12 mods by hook count
- **Scenario audited:** `full`
- **Open findings:** P2×3 · P3×3

## Open findings
### [P2] Gauge has no budget/reference mark · `id:d2a01d`
- **Category:** chart-fit
- **Where:** PROFILER HEALTH / default state
- **What:** The radial gauge shows a green->amber->red ramp arc with a bright-green fill from the left, but there is no tick, line, or label marking where the budget threshold sits on the arc. The value 0.82x means nothing against the arc without a visible 1.0x reference point and end-of-scale max. The reader cannot tell at a glance how close to the alarm zone the fill is.
- **Suggested fix:** Add a reference tick on the arc at the budget line (1.0x) and label the arc endpoints (e.g. 0 and the scale max), so the fill position reads as 'how far into budget' rather than a free-floating arc.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Ranking bars are monochrome, not per-mod hue · `id:7e2cdb`
- **Category:** colour-encoding
- **Where:** HOOK DISTRIBUTION · TOP 12 MODS BY HOOK COUNT / default state
- **What:** Every bar in the top-12 ranking is the same grey/white fill. The house style reserves per-mod categorical hue for exactly this kind of per-mod series, and these same mods (CalamityMod, ThoriumMod, etc.) are coloured elsewhere in the dashboard. Rendering them all monochrome here both loses the cross-panel mod-identity link and makes the panel read as the 'bars everywhere' smell the design bar warns against.
- **Suggested fix:** Tint each bar with that mod's categorical hue (the same L=0.72/C=0.11 series used elsewhere) so the ranking carries mod identity and stays consistent with other per-mod panels.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Centre value sits low-left, not centred in the gauge · `id:92150f`
- **Category:** layout-alignment
- **Where:** PROFILER HEALTH / default state
- **What:** The '0.82x' value and its 'healthy' caption are anchored at the lower-left of the ring rather than visually centred inside the gauge arc. The number's left edge overruns under the green fill stroke, so the digit and the arc geometry crowd each other and the value does not read as the gauge's centre label.
- **Suggested fix:** Centre the value/caption block horizontally and vertically within the gauge's bounding circle, with enough inset that the fill stroke never overlaps the glyphs.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] ms column rides along unsorted with no encoding · `id:c98e3f`
- **Category:** chart-fit
- **Where:** HOOK DISTRIBUTION · TOP 12 MODS BY HOOK COUNT / default state
- **What:** The right-hand 'ms' column varies independently of the hook-count bars the rows are sorted by (e.g. VitalityMod has 57 hooks but 0.02 ms, PerformanceProfiler has 5 hooks but 0.31 ms). It is a bare number column with no bar, sparkline, or visual cue, so the relationship between hook count and cost is invisible even though both values are present per row.
- **Suggested fix:** Give the ms value its own light encoding (a thin secondary bar or a dot on a shared mini-scale) so the reader can see where cost and hook count diverge, rather than parsing two number columns by eye.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Gauge column wider than its content needs · `id:3ce6fd`
- **Category:** hierarchy
- **Where:** PROFILER HEALTH / default state
- **What:** The gauge occupies the left ~25% of the panel but the arc and value cluster only fill the left half of that column, leaving a band of dead space between the gauge and the first stat tile (SEVERITY). The four stat tiles to the right are evenly sized and read fine; the imbalance is the gap around the gauge.
- **Suggested fix:** Tighten the gauge column width to the arc's actual footprint, or enlarge the gauge to fill its column, so the SEVERITY tile starts closer and the row reads as one band.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] managed/native legend swatches are near-identical · `id:4e280c`
- **Category:** readability
- **Where:** PROCESS CONTEXT / default state
- **What:** The bottom split bar is almost entirely one bright fill (managed, 98%) with a tiny native sliver, and the two legend swatches ('managed' / 'native') are both low-chroma greys that are hard to tell apart at render size. The 98% headline is also the same weight as the working-set/managed-heap rows above it, so the panel's key number does not lead.
- **Suggested fix:** Differentiate the two legend swatches clearly (the managed swatch matching the bar fill, native a distinctly darker neutral) and lift the 98% 'managed share' as the panel's headline metric.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

## Notes
_Hand-written notes about this page live here and survive audit re-runs._

<!-- PP-AUDIT-DATA
{
 "tab": "self",
 "findings": [
  {
   "id": "7e2cdb",
   "severity": "P2",
   "category": "colour-encoding",
   "title": "Ranking bars are monochrome, not per-mod hue",
   "panel": "HOOK DISTRIBUTION \u00b7 TOP 12 MODS BY HOOK COUNT",
   "state": "default",
   "what": "Every bar in the top-12 ranking is the same grey/white fill. The house style reserves per-mod categorical hue for exactly this kind of per-mod series, and these same mods (CalamityMod, ThoriumMod, etc.) are coloured elsewhere in the dashboard. Rendering them all monochrome here both loses the cross-panel mod-identity link and makes the panel read as the 'bars everywhere' smell the design bar warns against.",
   "fix": "Tint each bar with that mod's categorical hue (the same L=0.72/C=0.11 series used elsewhere) so the ranking carries mod identity and stays consistent with other per-mod panels.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "92150f",
   "severity": "P2",
   "category": "layout-alignment",
   "title": "Centre value sits low-left, not centred in the gauge",
   "panel": "PROFILER HEALTH",
   "state": "default",
   "what": "The '0.82x' value and its 'healthy' caption are anchored at the lower-left of the ring rather than visually centred inside the gauge arc. The number's left edge overruns under the green fill stroke, so the digit and the arc geometry crowd each other and the value does not read as the gauge's centre label.",
   "fix": "Centre the value/caption block horizontally and vertically within the gauge's bounding circle, with enough inset that the fill stroke never overlaps the glyphs.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "d2a01d",
   "severity": "P2",
   "category": "chart-fit",
   "title": "Gauge has no budget/reference mark",
   "panel": "PROFILER HEALTH",
   "state": "default",
   "what": "The radial gauge shows a green->amber->red ramp arc with a bright-green fill from the left, but there is no tick, line, or label marking where the budget threshold sits on the arc. The value 0.82x means nothing against the arc without a visible 1.0x reference point and end-of-scale max. The reader cannot tell at a glance how close to the alarm zone the fill is.",
   "fix": "Add a reference tick on the arc at the budget line (1.0x) and label the arc endpoints (e.g. 0 and the scale max), so the fill position reads as 'how far into budget' rather than a free-floating arc.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "3ce6fd",
   "severity": "P3",
   "category": "hierarchy",
   "title": "Gauge column wider than its content needs",
   "panel": "PROFILER HEALTH",
   "state": "default",
   "what": "The gauge occupies the left ~25% of the panel but the arc and value cluster only fill the left half of that column, leaving a band of dead space between the gauge and the first stat tile (SEVERITY). The four stat tiles to the right are evenly sized and read fine; the imbalance is the gap around the gauge.",
   "fix": "Tighten the gauge column width to the arc's actual footprint, or enlarge the gauge to fill its column, so the SEVERITY tile starts closer and the row reads as one band.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "4e280c",
   "severity": "P3",
   "category": "readability",
   "title": "managed/native legend swatches are near-identical",
   "panel": "PROCESS CONTEXT",
   "state": "default",
   "what": "The bottom split bar is almost entirely one bright fill (managed, 98%) with a tiny native sliver, and the two legend swatches ('managed' / 'native') are both low-chroma greys that are hard to tell apart at render size. The 98% headline is also the same weight as the working-set/managed-heap rows above it, so the panel's key number does not lead.",
   "fix": "Differentiate the two legend swatches clearly (the managed swatch matching the bar fill, native a distinctly darker neutral) and lift the 98% 'managed share' as the panel's headline metric.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "c98e3f",
   "severity": "P3",
   "category": "chart-fit",
   "title": "ms column rides along unsorted with no encoding",
   "panel": "HOOK DISTRIBUTION \u00b7 TOP 12 MODS BY HOOK COUNT",
   "state": "default",
   "what": "The right-hand 'ms' column varies independently of the hook-count bars the rows are sorted by (e.g. VitalityMod has 57 hooks but 0.02 ms, PerformanceProfiler has 5 hooks but 0.31 ms). It is a bare number column with no bar, sparkline, or visual cue, so the relationship between hook count and cost is invisible even though both values are present per row.",
   "fix": "Give the ms value its own light encoding (a thin secondary bar or a dot on a shared mini-scale) so the reader can see where cost and hook count diverge, rather than parsing two number columns by eye.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  }
 ]
}
PP-AUDIT-DATA -->
