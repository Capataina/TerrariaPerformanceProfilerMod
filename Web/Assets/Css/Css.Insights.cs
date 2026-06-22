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
   (.split-bar, .dtable, .chip, .statline, .rheat, .cellbar). The rules
   below are layout + per-surface framing only; the components carry
   their own styling from Css.Components.cs. */
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
.ins-mid {
  display: grid; grid-template-columns: minmax(0, 1.4fr) minmax(0, 1fr);
  gap: 0.6rem;
  min-height: 320px;
}
.ins-observatory {
  display: flex; flex-direction: column; gap: 0.4rem;
  min-width: 0;
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
.ins-obs-list {
  background: var(--panel);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  overflow-y: auto;
  max-height: 520px;
  min-height: 240px;
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
.ins-detail {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.6rem 0.85rem;
  overflow-y: auto;
  max-height: 520px;
  min-height: 240px;
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

/* I7 matrix — top-pairs table + correlation heatmap ------------------ */
.ins-matrix {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.55rem 0.8rem;
}
.ins-matrix .mx-h { margin-bottom: 0.4rem; }
.ins-matrix .mx-pairs { margin-bottom: 0.7rem; }
.ins-matrix .mx-cell { width: 6rem; }
/* Signed correlation values: green positive, red negative. */
.ins-matrix .r-pos { color: var(--good); }
.ins-matrix .r-neg { color: var(--danger); }
.ins-matrix .mx-grid-h {
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  letter-spacing: 0.06em; text-transform: uppercase; margin-bottom: 0.3rem;
}
.ins-matrix .mx-scroll { overflow-x: auto; }
.ins-matrix .mx-rheat .rh-col {
  writing-mode: vertical-rl; transform: rotate(180deg);
  max-height: 70px; overflow: hidden; text-overflow: ellipsis;
}
.ins-matrix .mx-rheat .rh-row {
  max-width: 130px; overflow: hidden; text-overflow: ellipsis;
}
.ins-matrix .mx-rheat .rh-cell { min-width: 30px; min-height: 1.6rem; font-size: 0.68rem; }
.ins-matrix .mx-empty {
  color: var(--dim); font-size: 0.82rem; padding: 0.6rem 0; text-align: center;
}
.ins-matrix .mx-legend {
  margin-top: 0.5rem;
  display: flex; align-items: center; gap: 0.4rem;
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
}
.ins-matrix .mx-legend .swatch { width: 14px; height: 14px; border: 1px solid var(--border-soft); }
";
}
