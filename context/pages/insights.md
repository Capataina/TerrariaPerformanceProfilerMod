---
page: insights
label: Insights
last_audit: 2026-06-24T17:30:47
scenario: full
open_findings: P2×1 · P3×1
---

# Insights — page dossier

> Auto-maintained by the dashboard testing suite (`tools/testing`). Findings accumulate across audit runs and update in place by stable id. The **Notes** section is hand-owned and preserved across runs.

## Page at a glance
- **Panes discovered (last run):** modlist overview, dormant content, per-mod observatory, mod detail, cross-cutting signals, engagement vs cost, mod-pair cost correlation
- **Scenario audited:** `full`
- **Open findings:** P2×1 · P3×1

## Open findings
### [P2] Roster-composition legend is a lightness ramp used as a category key · `id:7de79b`
- **Category:** colour-encoding
- **Where:** MOD DETAIL / default state
- **What:** In ROSTER COMPOSITION the eight category swatches (items, npcs, buffs, projectiles, mounts, accessories, biomes, bosses) are drawn as one sequential grey ramp from near-white to dark. Two problems: (1) the 'items' swatch is near-white, the single brightest mark on the panel, which in the monochrome chrome reads as a highlight rather than a neutral key and sits in the chrome's reserved near-white; (2) the dim end (accessories, biomes, bosses) collapses into near-identical dark greys that are barely separable from each other or from the panel background, so the swatches stop functioning as distinct category identifiers. A magnitude ramp is encoding a nominal/categorical dimension.
- **Suggested fix:** Render the swatches as distinct-but-equal greys (categorical, not a brightness ramp): a small set of mid-tone neutrals at similar lightness so none is near-white and none vanishes into the background, or pair each category with a short text/shape token rather than relying on swatch lightness. Reserve the lightness ramp for magnitude, keep it off the category key.
- **First seen:** 2026-06-24T17:30:47 · **Last seen:** 2026-06-24T17:30:47

### [P3] KPI strip still mixes three gauge shapes (dot / ring / arc-pill) · `id:799f20`
- **Category:** consistency
- **Where:** MODLIST OVERVIEW / default state
- **What:** The chromatic mismatch is fixed: all four KPIs now share one monochrome-grey palette (no green/amber/pink), the strip has a title (MODLIST OVERVIEW), and it renders at full height (the prior sliver-clip is gone). What remains is shape inconsistency: MODS LOADED is a tiny filled dot, ACTIVE and UNDER 5% USAGE are full ring-progress gauges, and DORMANT is a small solid partial-arc pill. Four sibling KPIs of the same kind read as three component types rather than one family.
- **Suggested fix:** Settle on one mini-gauge shape for all four (e.g. ring-progress for each, MODS LOADED as a full ring, DORMANT as a low-fill ring) so the strip reads as one component family. Colour is already consistent; this is the remaining shape inconsistency.
- **First seen:** 2026-06-24T17:30:47 · **Last seen:** 2026-06-24T17:30:47

## Not seen last run
_Reported by an earlier run but not re-flagged latest — fixed, or simply not re-surfaced. Confirm before deleting._

### [P1] Top panel clipped to an unusable sliver · `id:4364ab`  _(not seen last run)_
- **Category:** layout-alignment
- **Where:** page / whole state
- **What:** In the whole-tab view, directly under the tab bar there is a panel crushed to roughly a 40px-tall sliver: only fragments are visible ('0.3...' top-right, a 'ThoriumMod' row, '9.1...' and '0.06% / 0.6ms / 0.20 scope'). Its header and most of its body are cut off, and the well-rendered KPI ring strip (MODS LOADED / ACTIVE / DORMANT / UNDER 5%) seen in the panel crop does not appear in the whole view at all. A panel is being squeezed to near-zero height between the tab bar and CROSS-CUTTING SIGNALS.
- **Suggested fix:** Give the top strip its full natural height (it needs ~110px for the KPI rings as the crop shows) and stop the flex/grid from collapsing it; ensure the KPI ring strip and this dormant-preview row each render at full height rather than one starving the other.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Selection feedback is barely perceptible · `id:86fff1`  _(not seen last run)_
- **Category:** affordance
- **Where:** PER-MOD OBSERVATORY / selected state
- **What:** The --selected screenshot is essentially indistinguishable from default: row 1 (CalamityMod) carries only a faint tinted left bar, and that same faint bar is present in the default state too, so the row reads as permanently pre-selected and clicking gives almost no visible confirmation. The selection model (reserved tinted left bar) is too low-contrast to register against the near-black row.
- **Suggested fix:** Strengthen the selected-row treatment: a clearly brighter left accent bar plus a subtle row-background lift, and ensure the default (unselected) state has no left bar so selection is an actual visual change.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Relationship data shown as a table, not a scatter · `id:61328c`  _(not seen last run)_
- **Category:** chart-fit
- **Where:** ENGAGEMENT VS COST / default state
- **What:** The panel is literally named 'engagement vs cost' (usage share vs cpu share) — a two-variable relationship — but it is rendered as a sorted table with usage-share, cpu-share, roster and a tilt badge. The relationship between the two axes (the whole point of the panel) is invisible; the reader cannot see clusters of cost-heavy vs usage-heavy mods. The design-bar reference explicitly expects this to be a scatter.
- **Suggested fix:** Render as a scatter: usage share on one axis, cpu share on the other, point per mod, optional roster-count as radius (bubble) and the tilt category as the point hue. Keep the table as a secondary/expandable view if precise numbers are wanted.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Share-of-class bars are solid white and all max-length · `id:9dafb3`  _(not seen last run)_
- **Category:** colour-encoding
- **Where:** CROSS-CUTTING SIGNALS / default state
- **What:** In all three leader tables (HOT HOOK DOMINANCE, PEAK CONTRIBUTOR TO SPIKE, ALLOCATION BURST) the SHARE OF CLASS bars render as solid full-strength white, and because every appearance value is 1 the bars are all the same full width. Solid white is the brightest thing in an otherwise monochrome-grey chrome, drawing the eye to a column that currently carries no differentiating information, and white bars sit outside the app's 'data gets colour, chrome stays grey' rule.
- **Suggested fix:** Encode the bars with the perf ramp or a sequential neutral keyed to actual share, and scale bar length to the real share value so equal values look equal and larger shares look larger; drop the pure-white fill.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] Three different gauge treatments in one KPI strip · `id:bce212`  _(not seen last run)_
- **Category:** consistency
- **Where:** page / default state
- **What:** Within the KPI strip the four stats are drawn inconsistently: MODS LOADED uses a tiny dot, ACTIVE uses a green ring, DORMANT uses a solid pink/magenta vertical pill, and UNDER 5% USAGE uses an amber ring. Two rings, one dot, one solid bar for four sibling KPIs of the same kind.
- **Suggested fix:** Use one gauge vocabulary for all four (e.g. ring-progress for each, or a consistent mini-gauge), so the four KPIs read as one component family.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P2] KPI strip has no panel title · `id:dbe41f`  _(not seen last run)_
- **Category:** consistency
- **Where:** page / default state
- **What:** The top KPI ring strip (MODS LOADED / ACTIVE / DORMANT / UNDER 5% USAGE) carries no panel header, unlike every other panel on the tab (CROSS-CUTTING SIGNALS, ENGAGEMENT VS COST, etc.) which all have an uppercase title. It reads as an orphaned strip.
- **Suggested fix:** Add a panel title consistent with the others (e.g. 'MODLIST OVERVIEW' or 'ROSTER AT A GLANCE') so the strip is a labelled panel, not a floating row.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Pink DORMANT marker is a lone categorical hue · `id:6c61bb`  _(not seen last run)_
- **Category:** colour-encoding
- **Where:** page / default state
- **What:** The DORMANT KPI uses a solid pink/magenta pill while ACTIVE (green) and UNDER 5% (amber) sit on the perf ramp. Pink is a categorical hue used decoratively for a single magnitude stat, off the green-amber-red ramp the other two ride.
- **Suggested fix:** Either bring DORMANT onto the same ramp/treatment as its siblings or use a neutral fill; reserve hue for the per-mod categorical series, not a single KPI.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Headline ms value sits visually quieter than the rank number · `id:bee176`  _(not seen last run)_
- **Category:** hierarchy
- **Where:** PER-MOD OBSERVATORY / default state
- **What:** Each row's headline metric (e.g. '0.79 ms') is right-aligned and similar in weight to the metadata line, while the large left-margin rank number ('1', '2') is the boldest element. The most important number per row (the cost) is not the most prominent thing in the row.
- **Suggested fix:** Increase the weight/size of the ms value (and de-emphasise or shrink the rank index) so the cost is the first thing the eye lands on in each row.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Zero-engagement bars are near-invisible · `id:3b667b`  _(not seen last run)_
- **Category:** readability
- **Where:** DORMANT CONTENT / default state
- **What:** For the many 0.0% rows the engagement bar is an empty track rendered as a faint grey line barely distinguishable from the row background; a reader cannot tell a bar is even there until a non-zero value (VitalityMod 13.1%, Overheal 33.3% in the scrolled crop) gives it a green fill.
- **Suggested fix:** Lift the empty-track contrast slightly or add a '0.0%' visual cue (a hairline at origin) so zero-engagement rows still read as 'bar present, empty' rather than as a blank cell.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

### [P3] Magnitude bars indistinguishable across the top rows · `id:c198b1`  _(not seen last run)_
- **Category:** readability
- **Where:** MOD-PAIR COST CORRELATION / default state
- **What:** Every listed r is between 0.993 and 1.000, so the green magnitude bars are all visually full and near-identical; the bar adds no discrimination over the numeric r column for this data. (Synthetic data, so not flagging the values — only that the encoding does not separate the rows.)
- **Suggested fix:** Scale the magnitude bar to fill the visible r range (e.g. baseline at the lowest shown r, or a non-linear scale near 1.0) so small differences in strong correlations remain visible, or drop the bar when it duplicates the number.
- **First seen:** 2026-06-24T16:13:05 · **Last seen:** 2026-06-24T16:13:05

## Notes
_Hand-written notes about this page live here and survive audit re-runs._

<!-- PP-AUDIT-DATA
{
 "tab": "insights",
 "findings": [
  {
   "id": "4364ab",
   "severity": "P1",
   "category": "layout-alignment",
   "title": "Top panel clipped to an unusable sliver",
   "panel": "page",
   "state": "whole",
   "what": "In the whole-tab view, directly under the tab bar there is a panel crushed to roughly a 40px-tall sliver: only fragments are visible ('0.3...' top-right, a 'ThoriumMod' row, '9.1...' and '0.06% / 0.6ms / 0.20 scope'). Its header and most of its body are cut off, and the well-rendered KPI ring strip (MODS LOADED / ACTIVE / DORMANT / UNDER 5%) seen in the panel crop does not appear in the whole view at all. A panel is being squeezed to near-zero height between the tab bar and CROSS-CUTTING SIGNALS.",
   "fix": "Give the top strip its full natural height (it needs ~110px for the KPI rings as the crop shows) and stop the flex/grid from collapsing it; ensure the KPI ring strip and this dormant-preview row each render at full height rather than one starving the other.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "61328c",
   "severity": "P2",
   "category": "chart-fit",
   "title": "Relationship data shown as a table, not a scatter",
   "panel": "ENGAGEMENT VS COST",
   "state": "default",
   "what": "The panel is literally named 'engagement vs cost' (usage share vs cpu share) \u2014 a two-variable relationship \u2014 but it is rendered as a sorted table with usage-share, cpu-share, roster and a tilt badge. The relationship between the two axes (the whole point of the panel) is invisible; the reader cannot see clusters of cost-heavy vs usage-heavy mods. The design-bar reference explicitly expects this to be a scatter.",
   "fix": "Render as a scatter: usage share on one axis, cpu share on the other, point per mod, optional roster-count as radius (bubble) and the tilt category as the point hue. Keep the table as a secondary/expandable view if precise numbers are wanted.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "7de79b",
   "severity": "P2",
   "category": "colour-encoding",
   "title": "Roster-composition legend is a lightness ramp used as a category key",
   "panel": "MOD DETAIL",
   "state": "default",
   "what": "In ROSTER COMPOSITION the eight category swatches (items, npcs, buffs, projectiles, mounts, accessories, biomes, bosses) are drawn as one sequential grey ramp from near-white to dark. Two problems: (1) the 'items' swatch is near-white, the single brightest mark on the panel, which in the monochrome chrome reads as a highlight rather than a neutral key and sits in the chrome's reserved near-white; (2) the dim end (accessories, biomes, bosses) collapses into near-identical dark greys that are barely separable from each other or from the panel background, so the swatches stop functioning as distinct category identifiers. A magnitude ramp is encoding a nominal/categorical dimension.",
   "fix": "Render the swatches as distinct-but-equal greys (categorical, not a brightness ramp): a small set of mid-tone neutrals at similar lightness so none is near-white and none vanishes into the background, or pair each category with a short text/shape token rather than relying on swatch lightness. Reserve the lightness ramp for magnitude, keep it off the category key.",
   "first_seen": "2026-06-24T17:30:47",
   "state_seen": "2026-06-24T17:30:47"
  },
  {
   "id": "86fff1",
   "severity": "P2",
   "category": "affordance",
   "title": "Selection feedback is barely perceptible",
   "panel": "PER-MOD OBSERVATORY",
   "state": "selected",
   "what": "The --selected screenshot is essentially indistinguishable from default: row 1 (CalamityMod) carries only a faint tinted left bar, and that same faint bar is present in the default state too, so the row reads as permanently pre-selected and clicking gives almost no visible confirmation. The selection model (reserved tinted left bar) is too low-contrast to register against the near-black row.",
   "fix": "Strengthen the selected-row treatment: a clearly brighter left accent bar plus a subtle row-background lift, and ensure the default (unselected) state has no left bar so selection is an actual visual change.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "9dafb3",
   "severity": "P2",
   "category": "colour-encoding",
   "title": "Share-of-class bars are solid white and all max-length",
   "panel": "CROSS-CUTTING SIGNALS",
   "state": "default",
   "what": "In all three leader tables (HOT HOOK DOMINANCE, PEAK CONTRIBUTOR TO SPIKE, ALLOCATION BURST) the SHARE OF CLASS bars render as solid full-strength white, and because every appearance value is 1 the bars are all the same full width. Solid white is the brightest thing in an otherwise monochrome-grey chrome, drawing the eye to a column that currently carries no differentiating information, and white bars sit outside the app's 'data gets colour, chrome stays grey' rule.",
   "fix": "Encode the bars with the perf ramp or a sequential neutral keyed to actual share, and scale bar length to the real share value so equal values look equal and larger shares look larger; drop the pure-white fill.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "bce212",
   "severity": "P2",
   "category": "consistency",
   "title": "Three different gauge treatments in one KPI strip",
   "panel": "page",
   "state": "default",
   "what": "Within the KPI strip the four stats are drawn inconsistently: MODS LOADED uses a tiny dot, ACTIVE uses a green ring, DORMANT uses a solid pink/magenta vertical pill, and UNDER 5% USAGE uses an amber ring. Two rings, one dot, one solid bar for four sibling KPIs of the same kind.",
   "fix": "Use one gauge vocabulary for all four (e.g. ring-progress for each, or a consistent mini-gauge), so the four KPIs read as one component family.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "dbe41f",
   "severity": "P2",
   "category": "consistency",
   "title": "KPI strip has no panel title",
   "panel": "page",
   "state": "default",
   "what": "The top KPI ring strip (MODS LOADED / ACTIVE / DORMANT / UNDER 5% USAGE) carries no panel header, unlike every other panel on the tab (CROSS-CUTTING SIGNALS, ENGAGEMENT VS COST, etc.) which all have an uppercase title. It reads as an orphaned strip.",
   "fix": "Add a panel title consistent with the others (e.g. 'MODLIST OVERVIEW' or 'ROSTER AT A GLANCE') so the strip is a labelled panel, not a floating row.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "3b667b",
   "severity": "P3",
   "category": "readability",
   "title": "Zero-engagement bars are near-invisible",
   "panel": "DORMANT CONTENT",
   "state": "default",
   "what": "For the many 0.0% rows the engagement bar is an empty track rendered as a faint grey line barely distinguishable from the row background; a reader cannot tell a bar is even there until a non-zero value (VitalityMod 13.1%, Overheal 33.3% in the scrolled crop) gives it a green fill.",
   "fix": "Lift the empty-track contrast slightly or add a '0.0%' visual cue (a hairline at origin) so zero-engagement rows still read as 'bar present, empty' rather than as a blank cell.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "6c61bb",
   "severity": "P3",
   "category": "colour-encoding",
   "title": "Pink DORMANT marker is a lone categorical hue",
   "panel": "page",
   "state": "default",
   "what": "The DORMANT KPI uses a solid pink/magenta pill while ACTIVE (green) and UNDER 5% (amber) sit on the perf ramp. Pink is a categorical hue used decoratively for a single magnitude stat, off the green-amber-red ramp the other two ride.",
   "fix": "Either bring DORMANT onto the same ramp/treatment as its siblings or use a neutral fill; reserve hue for the per-mod categorical series, not a single KPI.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "799f20",
   "severity": "P3",
   "category": "consistency",
   "title": "KPI strip still mixes three gauge shapes (dot / ring / arc-pill)",
   "panel": "MODLIST OVERVIEW",
   "state": "default",
   "what": "The chromatic mismatch is fixed: all four KPIs now share one monochrome-grey palette (no green/amber/pink), the strip has a title (MODLIST OVERVIEW), and it renders at full height (the prior sliver-clip is gone). What remains is shape inconsistency: MODS LOADED is a tiny filled dot, ACTIVE and UNDER 5% USAGE are full ring-progress gauges, and DORMANT is a small solid partial-arc pill. Four sibling KPIs of the same kind read as three component types rather than one family.",
   "fix": "Settle on one mini-gauge shape for all four (e.g. ring-progress for each, MODS LOADED as a full ring, DORMANT as a low-fill ring) so the strip reads as one component family. Colour is already consistent; this is the remaining shape inconsistency.",
   "first_seen": "2026-06-24T17:30:47",
   "state_seen": "2026-06-24T17:30:47"
  },
  {
   "id": "bee176",
   "severity": "P3",
   "category": "hierarchy",
   "title": "Headline ms value sits visually quieter than the rank number",
   "panel": "PER-MOD OBSERVATORY",
   "state": "default",
   "what": "Each row's headline metric (e.g. '0.79 ms') is right-aligned and similar in weight to the metadata line, while the large left-margin rank number ('1', '2') is the boldest element. The most important number per row (the cost) is not the most prominent thing in the row.",
   "fix": "Increase the weight/size of the ms value (and de-emphasise or shrink the rank index) so the cost is the first thing the eye lands on in each row.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  },
  {
   "id": "c198b1",
   "severity": "P3",
   "category": "readability",
   "title": "Magnitude bars indistinguishable across the top rows",
   "panel": "MOD-PAIR COST CORRELATION",
   "state": "default",
   "what": "Every listed r is between 0.993 and 1.000, so the green magnitude bars are all visually full and near-identical; the bar adds no discrimination over the numeric r column for this data. (Synthetic data, so not flagging the values \u2014 only that the encoding does not separate the rows.)",
   "fix": "Scale the magnitude bar to fill the visible r range (e.g. baseline at the lowest shown r, or a non-linear scale near 1.0) so small differences in strong correlations remain visible, or drop the bar when it duplicates the number.",
   "first_seen": "2026-06-24T16:13:05",
   "state_seen": "2026-06-24T16:13:05"
  }
 ]
}
PP-AUDIT-DATA -->
