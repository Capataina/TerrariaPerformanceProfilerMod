#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
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
  height: 68px;
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  background: var(--panel-2);
  padding: 6px 8px;
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
     band so they sit above/below each other instead of overprinting. */
  position: relative;
  min-height: 3rem;
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
.tl-transitions .tx-chip.tx-lo { top: calc(50% + 0.15rem); }
.tl-transitions .tx-chip.tx-hi { top: calc(50% - 1.45rem); }
/* '+N earlier' overflow token: pinned to the left edge, neutral, above the bands. */
.tl-transitions .tx-chip.tx-more {
  left: 0; top: 50%; transform: translateY(-50%);
  min-width: 0; z-index: 2;
  color: var(--muted);
}
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
/* Idle copy for a lane with no segments — sits to the right of the family label
   so a blank Boss/Invasion/Subworld row reads as legitimately empty, not broken. */
.tl-lane-empty {
  position: absolute; right: 8px; top: 50%; transform: translateY(-50%);
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
  position: absolute; left: 4px; right: 4px; top: 1px;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  font-size: 0.7rem; line-height: 1.2;
  /* Label reads on any fill: light text over a localised scrim. The shadow is
     a tight dark halo so the glyphs survive the bright (red) end of the ramp;
     no rainbow gradient sits behind the text any more. */
  color: oklch(0.985 0 0);
  text-shadow: 0 1px 2px oklch(0.16 0 0 / 0.95), 0 0 2px oklch(0.16 0 0 / 0.9);
  padding: 0 2px;
  background: linear-gradient(oklch(0.16 0 0 / 0.55), oklch(0.16 0 0 / 0));
  border-radius: 2px 2px 0 0;
}
.tl-segment .tl-waterfall {
  position: absolute; left: 0; right: 0; bottom: 0;
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
