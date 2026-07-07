#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Timeline tab styles — component-library layer.
    //
    // Most surfaces now compose from the shared vocabulary (panel chrome,
    // .dtable, .chip, .statline, split bars, barChart, emptyState) and carry no
    // CSS here. What remains are the three genuinely bespoke TIME-DOMAIN layouts
    // that have no generic equivalent: the time-placed transition track and the
    // absolute-positioned swimlane gantt. Each is kept but fully tokenised —
    // every colour is a var(--…), no raw rgba()/hex. The per-minute heat strip
    // migrated onto barChart() (vertical strip, scrollx, spike/stall marks) and
    // so needs only a height-bounded container.
    private const string CssTimeline = @"
/* =================================================== TIMELINE TAB */
.tl-shell {
  display: flex; flex-direction: column;
  gap: 0.55rem;
  padding: 0.55rem 0.9rem 1rem;
  --tl-lane-h: 36px;
}

/* ---- T4 heat strip — height-bounded shell for a barChart vertical strip --- */
/* The per-minute bars themselves are barChart()'s .bar-strip/.bar-col; this
   only gives them a modest fixed height to grow into and the panel-2 surface. */
.tl-heatstrip {
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  background: var(--panel-2);
  padding: 6px 8px;
  /* Column layout: the bar strip keeps its own fixed height and the marker /
     ramp legend sits beneath it inside the same panel (the page markup is a
     single div, so the legend is rendered into this container, not a sibling). */
  display: flex; flex-direction: column; gap: 5px;
}
/* The bar strip keeps the original fixed height so a flat-but-busy session still
   reads as variation; the legend below is intrinsic-height. */
.tl-heatstrip .hs-strip { height: 56px; }

/* ---- Heat strip legend: marker key + cost-ramp reference --------------- */
/* Names the spike/stall marker dots (previously unlabelled 'red dots') and
   anchors the perf-ramp fill to its min..max ms/t so the colour ramp is
   readable rather than a bare gradient. Pure chrome — no data colour here
   except the swatch dots and the ramp swatch, which mirror the data encoding. */
.tl-heatstrip .hs-legend {
  display: flex; align-items: center; flex-wrap: wrap; gap: 0.35rem 0.8rem;
  font-family: var(--mono); font-size: 0.62rem; color: var(--muted);
}
.tl-heatstrip .hs-ramp { display: inline-flex; align-items: center; gap: 0.3rem; }
.tl-heatstrip .hs-ramp-label { font-variant-numeric: tabular-nums; }
/* Ramp swatch mirrors the bar fill ramp (healthy -> busy) so the reader maps the
   min..max labels onto the same green->amber gradient the bars use. */
.tl-heatstrip .hs-ramp-bar {
  width: 3rem; height: 0.5rem; border-radius: 2px;
  background: linear-gradient(to right, var(--perf-0), var(--perf-2), var(--perf-3));
}
.tl-heatstrip .hs-key { display: inline-flex; align-items: center; gap: 0.3rem; }
/* The marker dots are absolutely positioned over a bar in the strip; in the
   legend they sit inline as a static swatch, so reset the shared positioning. */
.tl-heatstrip .hs-key .bar-mark {
  position: static; display: inline-block; transform: none;
  width: 5px; height: 5px;
}
/* Let the per-minute columns flex-grow to fill the panel so a short session
   spans the full strip instead of stranding it in the left corner. The inline
   width barChart() sets becomes a floor (min-width) the !important override
   relaxes; once the floor is hit on a long session, scrollx takes over. */
.tl-heatstrip .bar-strip { gap: 2px; }
.tl-heatstrip .bar-col {
  flex: 1 1 6px !important;
  width: auto !important;
  min-width: 6px;
}

/* ---- T3 transition track — time-placed labelled chips (BESPOKE) -------- */
/* Genuine time-domain layout: chips are absolutely positioned at their tick's
   fraction across the session window. No generic component places by time. */
.tl-transitions {
  position: relative;
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  background: var(--panel-2);
  padding: 0.3rem 0.5rem;
  min-height: 2rem;
  /* Clip absolutely-positioned chips to the track so a chip near the right edge
     can never leak off the panel (and off the screen). */
  overflow: hidden;
}
.tl-transitions .tx-track {
  /* Two stacked chip bands: closely-timed transitions alternate onto the lo/hi
     band so they sit above/below each other instead of overprinting. A small
     side inset keeps an edge-anchored chip clear of the clip boundary so the
     right-most label is never sheared by the panel edge. */
  position: relative;
  min-height: 3.6rem;
  margin: 0 0.35rem;
}
.tl-transitions .tx-rail {
  position: absolute; left: 0; right: 0; top: 50%;
  height: 0; border-top: 1px dashed var(--border-soft);
  pointer-events: none;
}
.tl-transitions .tx-chip {
  position: absolute;
  z-index: 1;
  /* Floor the token width so a closely-timed transition chip stays a readable
     label, and cap it so a long ""from -> to"" label can't dominate the track.
     The per-chip horizontal transform (edge-anchoring) is set inline; the
     vertical band (tx-lo / tx-hi) is set by class below. */
  min-width: 2rem;
  max-width: 12rem;
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
  justify-content: flex-start;
}
/* Bands are separated by a full chip-height plus a gap so a chip on the lo band
   never overprints one on the hi band even when both labels run wide. */
.tl-transitions .tx-chip.tx-lo { top: calc(50% + 0.5rem); }
.tl-transitions .tx-chip.tx-hi { top: calc(50% - 1.9rem); }
/* '+N earlier' overflow token: pinned to the left edge, neutral, above the bands. */
.tl-transitions .tx-chip.tx-more {
  left: 0; top: 50%; transform: translateY(-50%);
  min-width: 0; z-index: 2;
  color: var(--muted);
}
/* Ongoing-segment edge (audit T4): an open segment honestly spans start->now;
   the faded right edge + 'live' tag make full-width read as ONGOING rather
   than clipped at the panel bound. */
.tl-segment.open {
  border-top-right-radius: 0; border-bottom-right-radius: 0;
  -webkit-mask-image: linear-gradient(90deg, #000 88%, transparent 100%);
  mask-image: linear-gradient(90deg, #000 88%, transparent 100%);
}
.tl-segment .tl-open-tag {
  margin-left: 0.35em; color: var(--muted);
  font-size: 0.85em; letter-spacing: 0.03em;
}

/* Degenerate-window fallback (audit T3): when the session is too young for a
   time domain, chips flow left-to-right as a plain list — static position,
   natural wrap, same chrome. */
.tl-transitions .tx-track-flow {
  display: flex; flex-wrap: wrap; gap: 0.4rem;
  align-items: center; min-height: 3.6rem;
}
.tl-transitions .tx-chip.tx-flow {
  position: static; transform: none;
}
/* Transition chips carry no colour encoding (see Js.Timeline transitionKindWord):
   the word, arrow and kind all read in neutral chrome so the perf-ramp green
   keeps its single meaning. */
.tl-transitions .tx-chip { color: var(--text); }
.tl-transitions .tx-chip .tx-kind {
  color: var(--muted);
  text-transform: lowercase;
  margin-right: 0.2em;
}

/* ---- T1+T2 swimlanes — absolute-positioned gantt (BESPOKE) ------------- */
/* Genuine time-scaled gantt: each segment bar is positioned + sized by its
   start/end fraction across the session window, stacked into per-family lanes.
   No generic component does absolute time placement; kept and tokenised. */
.tl-gantt {
  display: flex; flex-direction: column;
  gap: 4px;
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  padding: 4px;
  background: var(--panel-2);
}
.tl-laneRow { display: block; }
.tl-lane {
  position: relative;
  height: var(--tl-lane-h);
  background: var(--panel);
  border-radius: 2px;
  overflow: hidden;
}
.tl-lane::before {
  content: attr(data-family);
  position: absolute; left: 6px; top: 2px;
  font-family: var(--mono); font-size: 0.65rem;
  color: var(--muted);
  pointer-events: none;
  z-index: 1;
}
/* Idle copy for a lane with no segments. Anchored near the lane START (just
   past the family label in ::before), not the right edge: with the gantt now
   scaled to the segment-containing span, a right-pinned label read as a
   marooned straggler floating over dead space. Sitting it near the start keeps
   a blank Boss/Invasion/Subworld row legible as legitimately empty, not broken. */
.tl-lane-empty {
  position: absolute; left: 8px; top: 50%; transform: translateY(-50%);
  /* Clear the family label that ::before draws at the lane's top-left. */
  padding-top: 0.8rem;
  font-family: var(--mono); font-size: 0.65rem;
  color: var(--dim);
  pointer-events: none;
}
.tl-segment {
  position: absolute;
  top: 18px; bottom: 4px;
  /* Fill encodes COST INTENSITY (perf ramp), not family. --seg-fill is set
     inline per block from perfColor(avgFrameMs / 16.6 ms); the lane already
     separates families, so the colour is freed to say 'which segments were
     expensive'. A muted neutral floor covers the no-cost (open) case. */
  --seg-fill: var(--surface-2);
  background: var(--seg-fill);
  border: 1px solid var(--border);
  border-radius: 2px;
  cursor: pointer;
  overflow: hidden;
  /* Floor the bar width so a short visit stays legible and clickable rather
     than collapsing to a 1-2px sliver. Position stays time-accurate; only
     the rendered width is floored. */
  min-width: 1.6rem;
  font-family: var(--mono); font-size: 0.7rem;
}
.tl-segment .lbl {
  position: absolute; left: 4px; right: 4px; top: 0;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  /* Cap the label band to the glyph box and centre its line so descenders on a
     short lane (e.g. the Weather 'Day' block) are not sheared at the bottom.
     line-height equals the band height, so the single line sits fully inside. */
  height: 0.92rem; line-height: 0.92rem;
  font-size: 0.7rem;
  /* Label reads on any fill: light text over a localised scrim. The shadow is
     a tight dark halo so the glyphs survive the bright (red) end of the ramp;
     no rainbow gradient sits behind the text any more. */
  color: oklch(0.985 0 0);
  text-shadow: 0 1px 2px oklch(0.16 0 0 / 0.95), 0 0 2px oklch(0.16 0 0 / 0.9);
  padding: 0 3px;
  /* A FLAT readable scrim (was a fade-to-transparent that left the lower glyphs
     unprotected over a bright fill / the per-mod waterfall), and z-index above
     the waterfall so the rainbow split bar is never painted over the text. A
     label over data always gets a guaranteed-contrast backing, never a scrim the
     data bleeds through. */
  background: oklch(0.16 0 0 / 0.66);
  border-radius: 0 0 3px 0;
  z-index: 2;
}
.tl-segment .tl-waterfall {
  position: absolute; left: 0; right: 0; bottom: 0;
  z-index: 1;
}
.tl-segment .tl-waterfall .split-bar {
  border-radius: 0;
  height: 5px;
  background: color-mix(in oklch, var(--bg-deep) 25%, transparent);
}
.tl-segment .badge {
  position: absolute; right: 3px; bottom: 5px;
  font-family: var(--mono); font-size: 0.62rem;
  padding: 0 4px; border-radius: 2px;
  background: color-mix(in oklch, var(--bg-deep) 45%, transparent); color: var(--text-bright);
}
.tl-segment.outlier { box-shadow: 0 0 0 1px var(--accent) inset; }
.tl-segment.selected {
  outline: 2px solid var(--accent);
  outline-offset: -1px;
  z-index: 2;
}

/* ---- Bottom row: detail + attendance panels side by side -------------- */
/* Both panes are now standard .panel chrome (rendered by panel()); this is
   only the two-up responsive grid that holds them. */
.tl-bottom {
  display: grid;
  grid-template-columns: minmax(260px, 1fr) minmax(260px, 1.2fr);
  gap: 0.55rem;
  align-items: start;
}
.tl-attendance .tm-totals { margin-bottom: 0.5rem; }
.tl-attendance .tm-bar { margin: 0.2rem 0 0.6rem; }
/* Muted idle line shown in place of the per-mod table when only vanilla biome
   ticks were captured, so the panel never shows a header over an empty body. */
.tl-attendance .tm-idle {
  font-family: var(--mono); font-size: 0.74rem;
  color: var(--dim); padding: 0.3rem 0;
}
/* Coloured swatch in the attendance .dtable mod column. */
.tl-attendance .tm-table td.l .dot {
  display: inline-block; width: 0.55rem; height: 0.55rem;
  border-radius: 1px; margin-right: 0.4em; vertical-align: middle;
}

/* ---- T6 death cards — labelled event chip rows ------------------------ */
/* Per-death card built from a sectionBlock body; the chrome is the shared
   .section-block + a danger left-accent, the event row is .chip tokens. */
.tl-deaths { display: flex; flex-direction: column; gap: 0.4rem; }
.tl-death {
  border: 1px solid var(--border-soft);
  border-left: 3px solid var(--danger);
  border-radius: 3px;
  background: var(--panel-2);
  padding: 0.45rem 0.6rem;
}
.tl-death .head {
  display: flex; flex-wrap: wrap; gap: 0.5rem 1rem;
  font-family: var(--mono); font-size: 0.78rem;
  color: var(--text);
  margin-bottom: 0.4rem;
}
.tl-death .head .k { color: var(--muted); margin-right: 0.3em; }
.tl-death .ev-row { display: flex; flex-wrap: wrap; gap: 0.3rem; }
.tl-death .ev-chip {
  /* Keep every event token legible regardless of how short its label is. */
  min-width: 1.6rem;
  justify-content: center;
}
.tl-death .ev-chip .ev-off { color: var(--muted); font-size: 0.66rem; margin-right: 0.1em; }
.tl-death .ev-chip .ev-mod { color: var(--muted); font-size: 0.66rem; margin-left: 0.25em; opacity: 0.85; }

/* ---- T7 chronicle — one-line rows INSIDE the shared row() ------------ */
/* row() supplies the chrome (hover + reserved left bar); this lays the kind
   chip, timestamp and text on ONE dense baseline so a long session shows more
   of its arc per screen. The timestamp sits in a fixed-width gutter so the
   times align into a scannable column. */
/* Row-tracking hover for the chronicle log: a faint background wash so the
   cursor's current line stands out while scanning a long session, WITHOUT the
   accent left-bar of .row.clickable (these rows are not selectable, so they must
   not signal clickability). Subtle by design — it aids tracking, not selection. */
.cr-block { transition: background 0.12s; }
.cr-block:hover { background: var(--hover); }
.cr-block .cr-cell {
  display: flex; align-items: baseline; gap: 0.5rem; min-width: 0;
}
.cr-block .cr-cell .chip { flex: none; align-self: center; }
.cr-block .cr-time {
  flex: none; width: 4.5rem;
  font-size: 0.66rem; letter-spacing: 0.02em;
  font-variant-numeric: tabular-nums;
}
.cr-block .cr-text {
  flex: 1 1 auto; min-width: 0;
  color: var(--text-bright); font-size: 0.74rem; line-height: 1.3;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}

/* Sticky type filter pinned to the top of the chronicle scroll body — stays
   visible while the log scrolls, giving the long list a per-type filter. */
.cr-filter {
  position: sticky; top: 0; z-index: 3;
  padding: 0.3rem 0.1rem 0.4rem;
  margin-bottom: 0.1rem;
  background: var(--panel);
  border-bottom: 1px solid var(--border-soft);
}

@media (max-width: 900px) {
  .tl-bottom { grid-template-columns: 1fr; }
}
";
}
