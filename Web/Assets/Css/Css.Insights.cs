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

/* KPI strip — mini ring gauges -------------------------------------- */
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

/* I2 dust-shelf visualisation --------------------------------------- */
.ins-dormant .dust-shelf {
  display: block; width: 100%; height: auto;
  margin: 0.4rem 0 0.6rem;
}
.ins-dormant .dust-shelf .shelf-line {
  stroke: var(--border); stroke-width: 1.5;
}
.ins-dormant .dust-shelf .plaque {
  fill: var(--surface); stroke: var(--border); stroke-width: 0.75;
}
.ins-dormant .dust-shelf .dust-edge {
  fill: #8a8170; opacity: 0.5;
}
.ins-dormant .dust-shelf .pl-nm {
  fill: var(--text); font-family: var(--mono); font-size: 10px;
}
.ins-dormant .dust-shelf .pl-sub {
  fill: var(--muted); font-family: var(--mono); font-size: 8.5px;
}
.ins-dormant .dust-shelf .dust-plaque:hover .plaque {
  stroke: var(--accent); stroke-width: 1;
}
.ins-dormant .dust-empty {
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
.ins-obs-card .body .micro {
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  margin-top: 0.15rem;
}
.ins-obs-card .body .bar {
  height: 4px; background: var(--border-soft); border-radius: 2px; margin-top: 0.3rem;
  overflow: hidden;
}
.ins-obs-card .body .bar > span { display: block; height: 100%; background: var(--good); }
/* DNA strand microvis ----------------------------------------------- */
.ins-obs-card .body .dna {
  position: relative;
  height: 5px;
  margin-top: 0.28rem;
  background: var(--border-soft);
  border-radius: 1px;
  overflow: hidden;
}
.ins-obs-card .body .dna.empty {
  background: repeating-linear-gradient(45deg,
    var(--border-soft) 0 3px,
    transparent 3px 6px);
  opacity: 0.6;
}
.ins-obs-card .body .dna .dna-tick {
  position: absolute;
  top: 0; bottom: 0;
  border-right: 1px solid var(--panel);
}
.ins-obs-card .body .dna .dna-tick:last-child { border-right: none; }
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
.ins-cross .cc-empty {
  color: var(--dim); font-size: 0.82rem; padding: 0.4rem 0;
}

/* I5 constellation -------------------------------------------------- */
.ins-cross .cc-constellation {
  display: block;
  width: 100%;
  height: auto;
  margin-top: 0.3rem;
}
.ins-cross .cc-constellation .cc-spine {
  stroke: var(--border); stroke-width: 1; stroke-dasharray: 2 3;
}
.ins-cross .cc-constellation .cc-edge {
  stroke: var(--accent);
  fill: none;
}
.ins-cross .cc-constellation .cc-sig circle {
  fill: var(--panel-2); stroke: var(--accent); stroke-width: 1.5;
}
.ins-cross .cc-constellation .cc-sig-lbl {
  fill: var(--text); font-family: var(--mono); font-size: 10.5px;
  letter-spacing: 0.04em;
}
.ins-cross .cc-constellation .cc-star circle {
  fill: var(--text-bright); fill-opacity: 0.85;
  stroke: var(--accent-line); stroke-width: 0.5;
}
.ins-cross .cc-constellation .cc-star:hover circle {
  fill: var(--accent); fill-opacity: 1;
}
.ins-cross .cc-constellation .cc-star-lbl {
  fill: var(--muted); font-family: var(--mono); font-size: 9px;
  pointer-events: none;
}

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
.ins-scatter .dot { fill-opacity: 0.7; stroke: var(--text); stroke-opacity: 0.4; stroke-width: 0.6; }
.ins-scatter .dot-label { fill: var(--muted); font-family: var(--mono); font-size: 9px; pointer-events: none; }
.ins-scatter .dot-trail {
  stroke: var(--accent); stroke-width: 1.2; stroke-linecap: round;
  fill: none; pointer-events: none;
}

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
