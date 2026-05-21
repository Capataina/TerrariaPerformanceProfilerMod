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
.ins-shell {
  display: flex; flex-direction: column; gap: 0.6rem;
  padding: 0.6rem 0.9rem 1rem;
}

/* KPI strip ---------------------------------------------------------- */
.ins-kpi {
  display: grid; grid-template-columns: repeat(4, 1fr); gap: 0.45rem;
}
.ins-kpi .tile {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.55rem 0.75rem;
  display: flex; flex-direction: column; gap: 0.2rem;
}
.ins-kpi .tile .lbl {
  font-family: var(--mono); font-size: 0.7rem;
  color: var(--muted); letter-spacing: 0.08em; text-transform: uppercase;
}
.ins-kpi .tile .val {
  font-family: var(--mono); font-size: 1.25rem; color: var(--text);
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

/* I2 dormant strip --------------------------------------------------- */
.ins-dormant {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.45rem 0.7rem;
}
.ins-dormant .dor-h {
  display: flex; justify-content: space-between; align-items: baseline;
  font-family: var(--mono); font-size: 0.78rem; color: var(--muted);
  cursor: pointer;
}
.ins-dormant .dor-h .label { color: var(--text); letter-spacing: 0.05em; text-transform: uppercase; }
.ins-dormant .dor-body { margin-top: 0.45rem; display: none; }
.ins-dormant.open .dor-body { display: block; }
.ins-dormant .dor-row {
  display: grid; grid-template-columns: minmax(0,1fr) 90px 110px 90px;
  gap: 0.5rem; padding: 0.2rem 0;
  font-family: var(--mono); font-size: 0.78rem; color: var(--text);
  border-top: 1px dashed var(--border-soft);
}
.ins-dormant .dor-row .nm { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.ins-dormant .dor-row .v { color: var(--muted); text-align: right; }

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
.ins-obs-card .body .micro {
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  margin-top: 0.15rem;
}
.ins-obs-card .body .bar {
  height: 4px; background: var(--border-soft); border-radius: 2px; margin-top: 0.3rem;
  overflow: hidden;
}
.ins-obs-card .body .bar > span { display: block; height: 100%; background: var(--good); }
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
  margin: 0; font-family: var(--mono); font-size: 0.72rem;
  color: var(--muted); letter-spacing: 0.08em; text-transform: uppercase;
}
.ins-detail .det-title {
  font-family: var(--ui); font-size: 1.05rem; color: var(--text);
  margin-bottom: 0.1rem;
}
.ins-detail .det-stat-grid {
  display: grid; grid-template-columns: repeat(2, 1fr); gap: 0.35rem 0.6rem;
  font-family: var(--mono); font-size: 0.78rem;
}
.ins-detail .det-stat-grid .lbl { color: var(--muted); }
.ins-detail .det-stat-grid .val { color: var(--text); text-align: right; }
.ins-detail table.det-table {
  width: 100%; border-collapse: collapse;
  font-family: var(--mono); font-size: 0.76rem;
}
.ins-detail table.det-table td, .ins-detail table.det-table th {
  padding: 0.18rem 0.3rem; text-align: left; border-top: 1px dashed var(--border-soft);
}
.ins-detail table.det-table th { color: var(--muted); font-weight: normal; }
.ins-detail table.det-table td.num { text-align: right; color: var(--text); }
.ins-detail table.det-table td.muted { color: var(--dim); }

/* I5 cross-cutting --------------------------------------------------- */
.ins-cross {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.55rem 0.8rem;
  display: flex; flex-direction: column; gap: 0.35rem;
}
.ins-cross .cc-h {
  font-family: var(--mono); font-size: 0.72rem; color: var(--muted);
  letter-spacing: 0.08em; text-transform: uppercase;
  margin-bottom: 0.15rem;
}
.ins-cross .cc-row {
  display: grid;
  grid-template-columns: 200px minmax(0, 1fr);
  gap: 0.6rem;
  padding: 0.3rem 0;
  border-top: 1px dashed var(--border-soft);
  align-items: baseline;
}
.ins-cross .cc-row .cls {
  font-family: var(--mono); font-size: 0.82rem; color: var(--text);
}
.ins-cross .cc-row .leaders {
  font-family: var(--mono); font-size: 0.78rem; color: var(--muted);
  display: flex; flex-wrap: wrap; gap: 0.4rem 0.8rem;
}
.ins-cross .cc-row .leaders .ldr .nm { color: var(--text); }
.ins-cross .cc-row .leaders .ldr .cnt { color: var(--dim); margin-left: 0.25rem; }

/* I6 scatter --------------------------------------------------------- */
.ins-scatter {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.55rem 0.8rem;
}
.ins-scatter .sc-h {
  font-family: var(--mono); font-size: 0.72rem; color: var(--muted);
  letter-spacing: 0.08em; text-transform: uppercase; margin-bottom: 0.4rem;
}
.ins-scatter svg { width: 100%; height: 320px; display: block; }
.ins-scatter .axis { stroke: var(--border); stroke-width: 1; }
.ins-scatter .gridline { stroke: var(--border-soft); stroke-width: 1; stroke-dasharray: 3 3; }
.ins-scatter .axis-label, .ins-scatter .tick-label {
  fill: var(--muted); font-family: var(--mono); font-size: 10px;
}
.ins-scatter .quadrant-label {
  fill: var(--dim); font-family: var(--mono); font-size: 10px;
  letter-spacing: 0.06em; text-transform: uppercase;
}
.ins-scatter .dot { fill: var(--good); fill-opacity: 0.55; stroke: var(--text); stroke-opacity: 0.4; stroke-width: 0.6; }
.ins-scatter .dot-label { fill: var(--muted); font-family: var(--mono); font-size: 9px; pointer-events: none; }

/* I7 matrix ---------------------------------------------------------- */
.ins-matrix {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.55rem 0.8rem;
  overflow-x: auto;
}
.ins-matrix .mx-h {
  font-family: var(--mono); font-size: 0.72rem; color: var(--muted);
  letter-spacing: 0.08em; text-transform: uppercase; margin-bottom: 0.4rem;
}
.ins-matrix .mx-grid {
  display: grid; gap: 1px;
}
.ins-matrix .mx-grid .cell {
  width: 16px; height: 16px;
  background: var(--panel);
  cursor: default;
}
.ins-matrix .mx-grid .lbl-row {
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  height: 16px; line-height: 16px; padding-right: 0.4rem; text-align: right;
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.ins-matrix .mx-grid .lbl-col {
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  height: 60px; writing-mode: vertical-rl; transform: rotate(180deg);
  overflow: hidden; text-overflow: ellipsis;
}
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
