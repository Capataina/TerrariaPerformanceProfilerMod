# The visual-quality bar — design knowledge the audit carries

A reviewer with only the rubric catches *bugs*; a reviewer with *taste* also raises
the *ceiling* — "this works, but it should be a richer encoding." Read this before
reviewing so the audit raises quality, not just stops regression. It is also the
standard the `frontend-design` skill builds against, so creation and verification
share one bar.

This is how we answer the standing complaint that the dashboard is "bars everywhere,
with one pie chart as the most interesting thing."

---

## The chart vocabulary to grow into

The data the profiler already collects supports a far richer vocabulary than today's
bars + one donut. When you see a flat bar (or a bare table) where the data shape wants
something better, flag it with the suggested encoding. A non-exhaustive map of chart
type → data we already have that fits it:

| Encoding | Fits our data |
|---|---|
| **Radial gauge / ring progress** | Self-tab overhead vs budget; the FrameHeadroom insight; a mod's share of total |
| **Multi-ring / nested donut** | Per-category cost within per-mod within total (the impact donut, one level deeper) |
| **Area / line + gradient + threshold rules** | Frame-time trace, GC pressure, per-mod cost over time, the 60 fps reference line |
| **Sankey / flow** | Cross-mod chains (A's projectile → B's status → C's accessory); cost flowing category → mod |
| **Heatmap** | Cause × context lag matrix, time-of-day activity, per-mod-per-segment density |
| **Bubble / scatter** | Engagement-vs-cost (already a scatter; a third dimension as radius) |
| **Dot-matrix / waffle** | Modlist composition (active vs dormant as a unit grid, not just a number) |
| **Timeline / swimlane gantt** | The segment timeline; boss / biome / event spans |
| **KPI card + trend sparkline** | Every headline number (avg fps, worst frame) with its own micro-trend |
| **Small multiples** | One mini-chart per mod for at-a-glance comparison across a roster |

The discipline is **not** "use every chart." It is **match the chart to the data
shape**, and stop defaulting to a bar when the shape wants something else. A part-to-
whole that is a row of bars instead of a stacked bar or donut, a distribution shown as
a sorted list instead of a histogram, a relationship shown as two columns instead of a
scatter — those are the findings.

## Do's and don'ts the audit enforces

**Do**
- Rank relevantly (sort by the thing that matters, biggest-first).
- Target a benchmark / reference line (the 60 fps line, a lifetime baseline, a budget).
- Support easy comparison (shared axes, small multiples, aligned baselines).
- Build a visual hierarchy — the headline number is the most prominent thing.
- Write descriptive titles that state what the panel answers.
- Prefer a sequential ramp for magnitude; reserve hue for category.

**Don't**
- 3D anything; dual-Y axes; gridline clutter; more than ~4 series on one chart.
- Truncated / misleading axes; a baseline that is not zero where zero is the anchor.
- Colour-as-decoration (the chrome is monochrome on purpose — only data gets colour).
- Too many decimals; illegible micro-text; a rainbow where a ramp belongs.

## The house style (what "consistent with this app" means)

- **Monochrome chrome, colourful data.** Surfaces, borders, and text are neutral grey
  on near-black (shadcn "neutral", OKLCH). The *only* colour on screen is data: the
  per-mod categorical hues (L=0.72 / C=0.11, evenly stepped) and the perf ramp
  (green → amber → red, chroma rising toward the alarm end). Colour in the chrome is a
  bug.
- **One component vocabulary.** Panels, scroll-regions, rows (one hover + selection
  model with a reserved tinted left bar), stat tiles, split bars, legends, segmented
  controls, gauges, line/bar/donut/heatmap charts — all from the shared library. Two
  different treatments of the same concept is a consistency finding.
- **Descriptive, never prescriptive copy** (Invariant 3). "CalamityMod costs 0.78
  ms/t across 9 boss fights", never "CalamityMod is the problem / should be removed".

## Teaching taste, not just rules — the reference set

A rubric is a floor; design quality needs a reference to pattern-match against:

- `design/renders/` — external example renders (radial / gauge / area / sankey work
  done well). When you flag "this bar should be a gauge", point at the closest
  reference so the fix has a concrete target, not a vague "make it nicer".
- `design/dashboard-ui-spec.md` — the as-built visual spec with its own P1/P2/P3
  issue list; the human-authored counterpart to this rubric and a seed for it.
- `design/dashboard-shots/` — curated per-tab reference screenshots.

The throughline: L8 does not just stop the UI getting *worse* (regression) — it is the
mechanism for making it steadily *better*, at agent speed instead of human-review
speed. On a project whose whole value is presenting measurement well, that is the
highest-leverage axis in the map.
