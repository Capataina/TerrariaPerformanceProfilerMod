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
    private const string CssInsights = @"
/* =================================================== INSIGHTS */
/* Insights surfaces are built from the shared readable vocabulary
   (.split-bar, .dtable, .chip, .statline, .cellbar). The rules below are
   layout + per-surface framing only; the components carry their own
   styling from Css.Components.cs. Every scrollable surface keeps a stable
   inner scroll container (.dor-scroll, .obs-scroll, .det-scroll,
   .sc-scroll, .mx-scroll) so a poll re-render via setHTML preserves scroll
   position instead of snapping to the top. */
.ins-shell {
  display: flex; flex-direction: column; gap: 0.6rem;
  padding: 0.6rem 0.9rem 1rem;
}

/* KPI strip — mini ring gauges (kept; acceptable flair) ------------- */
.ins-kpi {
  display: grid; grid-template-columns: repeat(4, 1fr); gap: 0.45rem;
}
.ins-kpi .tile {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.5rem 0.6rem;
  display: grid; grid-template-columns: 60px minmax(0, 1fr); gap: 0.55rem;
  align-items: center;
}
.ins-kpi .tile .ring-wrap { width: 60px; height: 60px; }
.ins-kpi .tile .ring { width: 60px; height: 60px; display: block; }
.ins-kpi .tile .ring .track { fill: none; stroke: var(--border); stroke-width: 6; }
.ins-kpi .tile .ring .arc   { fill: none; stroke-width: 6; stroke-linecap: round;
  transition: stroke-dasharray 0.4s ease; }
.ins-kpi .tile .ring .ring-val {
  fill: var(--text-bright); font-family: var(--mono); font-size: 13px;
  letter-spacing: 0.02em;
}
.ins-kpi .tile .tile-body { display: flex; flex-direction: column; gap: 0.15rem; min-width: 0; }
.ins-kpi .tile .lbl {
  font-family: var(--mono); font-size: 0.7rem;
  color: var(--muted); letter-spacing: 0.08em; text-transform: uppercase;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}
.ins-kpi .tile .sub { font-size: 0.72rem; color: var(--dim); }

/* Mid section: 2-column observatory | detail ------------------------- */
/* Both columns stretch to the same height (align-items: stretch is the grid
   default) so the detail aside fills the column rather than leaving a gap
   below it. min-height grows the row to use the vertical space the cut
   matrix freed up. */
.ins-mid {
  display: grid; grid-template-columns: minmax(0, 1.5fr) minmax(0, 1fr);
  gap: 0.6rem;
  min-height: 460px;
}
.ins-observatory {
  display: flex; flex-direction: column; gap: 0.4rem;
  min-width: 0; min-height: 0;
}

/* Shared per-surface section header. */
.ins-dormant .dor-h .label,
.ins-cross .cc-h, .ins-scatter .sc-h, .ins-matrix .mx-h {
  font-family: var(--mono); font-size: 0.72rem; color: var(--muted);
  letter-spacing: 0.08em; text-transform: uppercase;
}

/* I2 dormant surface — sortable table -------------------------------- */
.ins-dormant {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.45rem 0.7rem;
}
.ins-dormant .dor-h {
  display: flex; justify-content: space-between; align-items: baseline;
  font-family: var(--mono); font-size: 0.78rem; color: var(--muted);
  margin-bottom: 0.3rem;
}
.ins-dormant .dor-h .label { color: var(--text); }
.ins-dormant .dor-scroll { max-height: 220px; overflow-y: auto; }
.ins-dormant .dor-usage { display: flex; align-items: center; gap: 0.5rem; min-width: 8rem; }
.ins-dormant .dor-usage .split-bar { flex: 1; }
.ins-dormant .dor-pct { color: var(--muted); flex: none; min-width: 3rem; text-align: right; }
.ins-dormant .dor-empty {
  color: var(--dim); font-size: 0.82rem; padding: 0.4rem 0; text-align: center;
}

/* I1 observatory card list ------------------------------------------ */
/* The .ins-obs-list wrapper is the stable element; the inner .obs-scroll
   carries overflow so a poll re-render via setHTML preserves scroll. The
   wrapper fills the remaining height of the observatory column (flex:1)
   and .obs-scroll fills the wrapper, so the list reaches the bottom of the
   mid row instead of leaving dead space below short lists. */
.ins-obs-list {
  background: var(--panel);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  flex: 1 1 auto;
  min-height: 240px;
  display: flex; flex-direction: column;
  overflow: hidden;
}
.ins-obs-list .obs-scroll {
  flex: 1 1 auto;
  overflow-y: auto;
  min-height: 0;
}
.ins-obs-card {
  display: grid;
  grid-template-columns: 22px minmax(0, 1fr) 90px;
  gap: 0.5rem;
  padding: 0.45rem 0.7rem;
  border-bottom: 1px solid var(--border-soft);
  cursor: pointer;
  align-items: center;
}
.ins-obs-card:hover { background: var(--hover); }
.ins-obs-card.selected { background: var(--hover); box-shadow: inset 3px 0 0 var(--good); }
.ins-obs-card .rank {
  font-family: var(--mono); font-size: 0.72rem; color: var(--dim); text-align: right;
}
.ins-obs-card .body { min-width: 0; }
.ins-obs-card .body .nm {
  font-family: var(--ui); font-size: 0.88rem; color: var(--text);
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.ins-obs-card .body .comp { margin-top: 0.28rem; }
.ins-obs-card .body .comp-empty {
  font-family: var(--mono); font-size: 0.7rem; color: var(--dim);
  font-style: italic; padding: 0.1rem 0;
}
.ins-obs-card .body .micro {
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  margin-top: 0.2rem;
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.ins-obs-card .body .cost { margin-top: 0.3rem; }
.ins-obs-card .ms {
  font-family: var(--mono); font-size: 0.82rem; color: var(--text); text-align: right;
}
.ins-obs-card .ms .u { font-size: 0.65rem; color: var(--dim); margin-left: 0.15rem; }

/* I1+I3+I4 detail pane ----------------------------------------------- */
/* The .ins-detail aside is the stable element and fills its grid column
   (stretch); the inner .det-scroll carries overflow so a poll re-render via
   setHTML preserves scroll while a mod's detail is open. */
.ins-detail {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.6rem 0.85rem;
  min-height: 240px;
  display: flex; flex-direction: column;
  overflow: hidden;
}
.ins-detail .det-scroll {
  flex: 1 1 auto;
  overflow-y: auto;
  min-height: 0;
  display: flex; flex-direction: column; gap: 0.6rem;
}
.ins-detail .empty {
  color: var(--dim); font-size: 0.85rem; padding: 1.5rem 0.5rem; text-align: center;
}
.ins-detail h4 {
  margin: 0 0 0.3rem; font-family: var(--mono); font-size: 0.72rem;
  color: var(--muted); letter-spacing: 0.08em; text-transform: uppercase;
}
.ins-detail .det-title {
  font-family: var(--ui); font-size: 1.05rem; color: var(--text);
  margin-bottom: 0.1rem;
}
.ins-detail .det-stats { display: flex; flex-direction: column; }

/* Lower analytical row: cross-cutting | engagement | correlation ------ */
/* A responsive grid so the three surfaces pack across the full width
   instead of stacking full-width with empty right-hand strips. Cross-cutting
   is the widest (it holds its own auto-fit section grid) so it spans two
   columns; engagement and correlation are narrow tables sharing the rest.
   On narrow viewports the auto-fit collapses them to a single column. */
.ins-lower {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 0.6rem;
  align-items: start;
}
.ins-lower .ins-cross { grid-column: 1 / -1; }

/* I5 cross-cutting — grouped section list ---------------------------- */
.ins-cross {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.55rem 0.8rem;
  display: flex; flex-direction: column; gap: 0.5rem;
}
.ins-cross .cc-h { margin-bottom: 0.15rem; }
.ins-cross .cc-sections {
  display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 0.6rem 0.9rem;
}
.ins-cross .cc-section {
  border: 1px solid var(--border-soft); border-radius: 4px;
  background: var(--panel); padding: 0.4rem 0.55rem;
}
.ins-cross .cc-cls {
  font-family: var(--mono); font-size: 0.82rem; color: var(--text);
  margin-bottom: 0.3rem;
}
.ins-cross .cc-cls .cc-cnt { color: var(--dim); font-size: 0.72rem; margin-left: 0.4rem; }
.ins-cross .cc-cell { width: 6rem; }
.ins-cross .cc-empty {
  color: var(--dim); font-size: 0.82rem; padding: 0.4rem 0;
}

/* I6 engagement vs cost — sortable table ----------------------------- */
.ins-scatter {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.55rem 0.8rem;
}
.ins-scatter .sc-h { margin-bottom: 0.4rem; }
.ins-scatter .sc-scroll { max-height: 360px; overflow-y: auto; }
.ins-scatter .sc-empty {
  color: var(--dim); font-size: 0.82rem; padding: 0.4rem 0;
}

/* I7 mod-pair correlation — top-coupled-pairs table ------------------ */
/* The full NxN Pearson grid was cut (dead space, twice misunderstood). Only
   the readable pairs table remains, under a plain-English caption. .mx-scroll
   is the stable scroll container so a poll preserves scroll position. */
.ins-matrix {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.55rem 0.8rem;
}
.ins-matrix .mx-h { margin-bottom: 0.4rem; }
.ins-matrix .mx-caption {
  font-size: 0.74rem; color: var(--dim); line-height: 1.35;
  margin-bottom: 0.45rem;
}
.ins-matrix .mx-scroll { max-height: 360px; overflow-y: auto; }
.ins-matrix .mx-cell { width: 6rem; }
/* Signed correlation values: green positive, red negative. */
.ins-matrix .r-pos { color: var(--good); }
.ins-matrix .r-neg { color: var(--danger); }
.ins-matrix .mx-empty {
  color: var(--dim); font-size: 0.82rem; padding: 0.6rem 0; text-align: center;
}
";
}
