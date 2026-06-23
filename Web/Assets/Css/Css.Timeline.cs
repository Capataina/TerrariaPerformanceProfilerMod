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
.tl-heatstrip .bar-strip { gap: 2px; }
.tl-heatstrip .bar-col { width: 5px; }

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
}
.tl-transitions .tx-track {
  position: relative;
  min-height: 1.4rem;
}
.tl-transitions .tx-rail {
  position: absolute; left: 0; right: 0; top: 50%;
  height: 0; border-top: 1px dashed var(--border-soft);
  pointer-events: none;
}
.tl-transitions .tx-chip {
  position: absolute; top: 50%;
  transform: translate(-50%, -50%);
  z-index: 1;
  /* Floor the token width so a closely-timed transition chip stays a
     readable label rather than shrinking toward its text. */
  min-width: 2rem;
  justify-content: center;
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
.tl-segment {
  position: absolute;
  top: 18px; bottom: 4px;
  background: var(--good);
  border: 1px solid var(--border);
  border-radius: 2px;
  cursor: pointer;
  overflow: hidden;
  /* Floor the bar width so a short visit stays legible and clickable rather
     than collapsing to a 1-2px sliver. Position stays time-accurate; only
     the rendered width is floored. */
  min-width: 1.6rem;
  color: var(--text-bright);
  font-family: var(--mono); font-size: 0.7rem;
}
/* Family fill = the family hue at low alpha over the lane; border = the solid
   hue. colour-mix keeps the fill on the same token as the border so the two
   can never drift, and no raw rgba() leaks in. */
.tl-segment[data-family='Biome']    { background: color-mix(in oklch, var(--good)   55%, transparent); border-color: var(--good); }
.tl-segment[data-family='Weather']  { background: color-mix(in oklch, var(--amber)  55%, transparent); border-color: var(--amber); }
.tl-segment[data-family='Boss']     { background: color-mix(in oklch, var(--danger) 65%, transparent); border-color: var(--danger); }
.tl-segment[data-family='Invasion'] { background: color-mix(in oklch, var(--orange) 60%, transparent); border-color: var(--orange); }
.tl-segment[data-family='Subworld'] { background: color-mix(in oklch, var(--purple) 60%, transparent); border-color: var(--purple); }
.tl-segment .lbl {
  position: absolute; left: 4px; right: 4px; top: 1px;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  font-size: 0.7rem; line-height: 1.2;
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

/* ---- T7 chronicle — block layout INSIDE the shared row() ------------- */
/* row() supplies the chrome (hover + reserved left bar); these only stack the
   per-line meta + text within the row's single cell. */
.cr-block .cr-cell { display: flex; flex-direction: column; gap: 0.1rem; min-width: 0; }
.cr-block .cr-meta { display: flex; gap: 0.5rem; align-items: center; }
.cr-block .cr-time { font-size: 0.66rem; letter-spacing: 0.02em; }
.cr-block .cr-text { color: var(--text-bright); font-size: 0.74rem; line-height: 1.3; }

@media (max-width: 900px) {
  .tl-bottom { grid-template-columns: 1fr; }
}
";
}
