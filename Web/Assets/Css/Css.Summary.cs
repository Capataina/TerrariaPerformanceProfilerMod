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
    private const string CssSummary = @"
/* ========================================== SUMMARY: mission-control grid */
.grid-summary {
  display: grid;
  grid-template-areas:
    'kpi    kpi     kpi'
    'chart  chart   donut'
    'trends trends  donut'
    'heatmap heatmap heatmap'
    'flow   flow    flow'
    'now    events  events'
    'mods   mods    mods';
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) minmax(0, 1fr);
  grid-template-rows: auto minmax(170px, 1fr) auto auto auto auto auto;
  gap: 0.75rem;
}

@media (max-width: 1100px) {
  .grid-summary {
    grid-template-areas:
      'kpi    kpi'
      'chart  chart'
      'donut  trends'
      'heatmap heatmap'
      'flow   flow'
      'now    events'
      'mods   mods';
    grid-template-columns: 1fr 1fr;
  }
}
@media (max-width: 700px) {
  .grid-summary {
    grid-template-areas: 'kpi' 'chart' 'donut' 'trends' 'heatmap' 'flow' 'now' 'events' 'mods';
    grid-template-columns: 1fr;
  }
}

/* ====== Cost-flow sankey ====== */
.flow-wrap { padding: 0.35rem 0.7rem 0.6rem; }
.flow-wrap .chart-sankey { width: 100%; max-height: 17rem; }

/* ====== Frame chart hero ====== */
.panel-hero .chart-wrap {
  flex: 1 1 auto; min-height: 0;
  position: relative;
  display: flex; flex-direction: column;
  padding: 0.4rem 0.9rem 0.5rem;
  gap: 0.3rem;
}
.chart { width: 100%; flex: 1 1 auto; display: flex; flex-direction: column; min-height: 130px; position: relative; }
.chart .chart-svg { flex: 1 1 auto; min-height: 0; height: 100%; }

/* Reference-rule key. The 60fps + median rules are drawn as unlabelled dashed
   lines (their SVG text would sit on the trace); this compact inset names them
   in the top-right gutter, clear of the plotting area. */
.chart-keys {
  position: absolute; top: 0; right: 0;
  display: flex; flex-direction: column; align-items: flex-end; gap: 0.1rem;
  font-family: var(--mono); font-size: 0.62rem; color: var(--dim);
  pointer-events: none; line-height: 1.2;
}
.chart-key { display: inline-flex; align-items: center; gap: 0.32em; white-space: nowrap; }
.chart-key .ck-rule {
  display: inline-block; width: 0.9em; height: 0;
  border-top-width: 1px; border-top-style: dashed; opacity: 0.7;
}

/* Centered note overlaying a panel body that has no live trace (db mode).
   Fills its positioned parent and centres the canonical .empty inside it; the
   live SVG/rows sit behind it and are blanked, so nothing peeks through. */
.panel-empty {
  position: absolute; inset: 0;
  display: flex; align-items: center; justify-content: center;
}

/* ====== Donut chart ====== */
/* Host for the shared donut() component; the chart owns its own .chart-center.
   This layer only sizes + centres it and bounds the legend's scroll. */
.donut-wrap {
  padding: 0.6rem;
  display: flex; align-items: center; justify-content: center;
  flex: 0 0 auto;
}
.donut-wrap .chart-donut { width: 100%; max-width: 170px; }
.donut-legend {
  padding: 0 0.9rem 0.7rem;
  flex: 1 1 auto; min-height: 0; overflow-y: auto;
}

/* ====== Sparkline trend rows ====== */
.trends { position: relative; padding: 0.4rem 0.6rem 0.5rem; display: flex; flex-direction: column; gap: 0.25rem; }
.trend-rows { display: flex; flex-direction: column; gap: 0.25rem; }
.trend-row {
  display: grid; grid-template-columns: 3rem minmax(0, 1fr);
  align-items: center; gap: 0.5rem;
}
.tr-k { font-family: var(--mono); font-size: 0.78rem; color: var(--muted); }
.tr-spark { width: 100%; height: 1.8rem; display: block; }
.tr-spark .spark-svg { height: 100%; }

/* ====== Now playing (rowList of row()) ====== */
/* The panel hugs its content instead of stretching to match the taller events
   panel beside it (the grid row defaults to align stretch, which is what left
   ~70% of this panel as dead void with only two open segments). align-self:start
   lets it size to the rows + footer; the footer closes the list off cleanly. */
.grid-summary > [style*='grid-area: now'] { align-self: start; }

/* Scroll container; each segment is a shared row(). The per-row detail (family
   swatch + two-line name block + meta) is styled in Css.NowPlaying. The list
   hugs its content (flex: 0 1 auto); a long list still scrolls within max-height. */
.now-scroll { overflow-y: auto; flex: 0 1 auto; min-height: 0; max-height: 22rem; }

/* ====== Events feed (rowList of row()) ====== */
.events-scroll { overflow-y: auto; flex: 1 1 auto; min-height: 100px; }
.ev-row { align-items: baseline; }
/* The kind glyph is the row's scan anchor — it is the one element coloured per
   event kind, so we size it up a touch to lead the eye down the list rather than
   leaving every row the same weight. The timestamp recedes (already dim) so the
   glyph + text carry the row and the relative time stays a secondary figure. */
.ev-glyph { text-align: center; color: var(--muted); font-size: 0.92rem; }
.ev-when  { color: var(--dim); font-size: 0.72rem; opacity: 0.85; }
.ev-row[data-kind='boss-kill'] .ev-glyph { color: var(--good); }
.ev-row[data-kind='death']     .ev-glyph { color: var(--danger); }
.ev-row[data-kind='spike']     .ev-glyph { color: var(--amber); }
.ev-row[data-kind='stall']     .ev-glyph { color: var(--danger); }
.ev-row[data-kind='segment']   .ev-glyph { color: var(--muted); }
";
}
