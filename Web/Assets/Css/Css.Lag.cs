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
    private const string CssLag = @"
/* =================================================== LAG TAB (dashboard) */
.lag-shell {
  display: flex; flex-direction: column; gap: 0.7rem;
  padding: 0.7rem 0.9rem 1rem;
}

/* ----- KPI strip ----- */
.lag-kpi {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 0.5rem;
}
.lag-kpi-cell {
  background: linear-gradient(180deg, var(--panel) 0%, var(--panel-2) 100%);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.55rem 0.75rem;
  display: flex; flex-direction: column; gap: 0.15rem;
}
.lag-kpi-cell .label {
  font-family: var(--ui); font-size: 0.65rem; letter-spacing: 0.08em;
  text-transform: uppercase; color: var(--muted);
}
.lag-kpi-cell .value {
  font-family: var(--mono); font-size: 1.2rem; color: var(--text-bright);
}
.lag-kpi-cell .unit {
  font-family: var(--mono); font-size: 0.75rem; color: var(--muted);
  margin-left: 0.25em;
}

/* ----- Middle: heatmap + galaxy ----- */
.lag-mid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.7rem;
}
.lag-heatmap, .lag-clusters,
.lag-density, .lag-gc, .lag-causality, .lag-rhythm {
  background: linear-gradient(180deg, var(--panel) 0%, var(--panel-2) 100%);
  border: 1px solid var(--border-soft);
  border-radius: 4px;
  padding: 0.7rem 0.9rem;
}
.lag-section-h {
  font-family: var(--ui); font-size: 0.7rem; color: var(--muted);
  text-transform: uppercase; letter-spacing: 0.08em;
  margin-bottom: 0.5rem;
}

/* ----- Hex-grid heatmap ----- */
.lag-hexgrid {
  width: 100%;
  height: auto;
  display: block;
}
.lag-hexgrid .hex-empty {
  fill: var(--panel-2);
  stroke: var(--border-soft);
  stroke-width: 0.75;
}
.lag-hexgrid .hex-head {
  font-family: var(--ui); font-size: 9px;
  fill: var(--muted);
  text-transform: uppercase; letter-spacing: 0.05em;
}
.lag-hexgrid .hex-row {
  font-family: var(--ui); font-size: 9px;
  fill: var(--muted);
  text-transform: uppercase; letter-spacing: 0.05em;
}
.lag-hexgrid .hex-count {
  font-family: var(--mono); font-size: 9px;
  fill: var(--text-bright);
  pointer-events: none;
}

/* ----- Galaxy ----- */
.lag-galaxy {
  width: 100%; height: auto; display: block;
  background: var(--surface);
  border: 1px solid var(--border-soft);
  border-radius: 3px;
}
.lag-galaxy .galaxy-band {
  fill: rgba(255, 255, 255, 0.015);
}
.lag-galaxy .galaxy-axis {
  font-family: var(--ui); font-size: 9px;
  fill: var(--muted);
  text-transform: uppercase; letter-spacing: 0.06em;
}
.lag-galaxy .galaxy-axis-y {
  font-family: var(--ui); font-size: 9px;
  fill: var(--muted);
  letter-spacing: 0.05em;
}
.lag-galaxy .galaxy-node {
  opacity: 0.85;
  stroke: rgba(0,0,0,0.4);
  stroke-width: 0.6;
  cursor: pointer;
  transition: opacity 120ms ease, stroke-width 120ms ease;
}
.lag-galaxy .galaxy-node:hover {
  opacity: 1;
  stroke: var(--text-bright);
  stroke-width: 1.2;
}
.lag-galaxy .galaxy-node.sel {
  stroke: var(--text-bright);
  stroke-width: 1.6;
}
.lag-galaxy .galaxy-ring {
  fill: none;
  stroke-width: 1.4;
  opacity: 0.9;
  pointer-events: none;
}
.galaxy-details {
  margin-top: 0.4rem;
  background: var(--surface);
  border: 1px solid var(--border-soft);
  border-left: 3px solid var(--accent);
  border-radius: 3px;
  padding: 0.45rem 0.65rem;
  display: flex; flex-direction: column; gap: 0.3rem;
}
.galaxy-details .gd-top {
  display: flex; gap: 0.5em; align-items: baseline; flex-wrap: wrap;
  font-family: var(--mono); font-size: 0.78rem; color: var(--text-bright);
}
.galaxy-details .gd-cause {
  font-family: var(--ui); font-size: 0.62rem; font-weight: 700;
  letter-spacing: 0.08em; text-transform: uppercase;
  color: var(--accent);
  padding: 0.1em 0.4em;
  background: var(--accent-soft);
  border-radius: 2px;
}
.galaxy-details .gd-fp { color: var(--text); }
.galaxy-details .gd-sub { color: var(--muted); font-size: 0.72rem; }
.galaxy-details .gd-stats {
  display: flex; gap: 1em; flex-wrap: wrap;
  font-family: var(--mono); font-size: 0.74rem; color: var(--muted);
}
.galaxy-details .gd-stats b { color: var(--text-bright); font-weight: 600; }
.galaxy-hint {
  margin-top: 0.35rem;
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
}

/* ----- Per-segment lag swarm ----- */
.lag-swarm {
  width: 100%; height: auto; display: block;
  background: var(--surface);
  border: 1px solid var(--border-soft);
  border-radius: 3px;
}
.lag-swarm .swarm-bg {
  fill: rgba(255, 255, 255, 0.02);
}
.lag-swarm .swarm-bg.hot {
  fill: rgba(214, 99, 86, 0.06);
}
.lag-swarm .swarm-label {
  font-family: var(--mono); font-size: 10px;
  fill: var(--text);
}
.lag-swarm .swarm-val {
  font-family: var(--mono); font-size: 10px;
  fill: var(--muted);
}
.lag-swarm .swarm-dot.spike {
  fill: var(--spike);
  opacity: 0.85;
}
.lag-swarm .swarm-dot.stall {
  fill: var(--stall);
  opacity: 0.95;
}
.swarm-legend {
  margin-top: 0.35rem;
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  display: flex; gap: 1em; align-items: center;
}
.swarm-legend .dot {
  display: inline-block;
  width: 8px; height: 8px; border-radius: 50%;
  margin-right: 0.3em; vertical-align: middle;
}
.swarm-legend .dot.spike { background: var(--spike); }
.swarm-legend .dot.stall { background: var(--stall); }
.lag-density-baseline {
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  margin-bottom: 0.4rem;
}

/* ----- GC tide ----- */
.lag-gc-grid {
  display: grid;
  grid-template-columns: 12rem 1fr;
  gap: 0.7rem;
}
.lag-gc-counters {
  display: grid; grid-template-columns: 1fr auto; gap: 0.25rem 0.6rem;
  font-family: var(--mono); font-size: 0.76rem;
  align-content: start;
}
.lag-gc-counters .k { color: var(--muted); font-size: 0.65rem; text-transform: uppercase; letter-spacing: 0.06em; }
.lag-gc-counters .v { color: var(--text-bright); text-align: right; }
.gc-tide {
  width: 100%; height: auto; display: block;
  background: var(--surface);
  border: 1px solid var(--border-soft);
  border-radius: 3px;
}
.gc-tide .gc-baseline {
  stroke: var(--border-soft);
  stroke-width: 1;
}
.gc-tide .gc-break {
  fill: var(--stall);
  opacity: 0.85;
}
.gc-tide .gc-wind-l {
  font-family: var(--ui); font-size: 9px;
  fill: var(--muted);
  letter-spacing: 0.05em;
}
.gc-tide .gc-wind-v {
  font-family: var(--mono); font-size: 9px;
  fill: var(--text);
}
.gc-tide .gc-axis {
  font-family: var(--mono); font-size: 9px;
  fill: var(--muted);
}

/* ----- Causality (Sankey cards) ----- */
.lag-causality-list {
  display: flex; flex-direction: column; gap: 0.4rem;
  max-height: 22rem; overflow-y: auto;
}
.lag-chain-sankey {
  background: var(--surface);
  border: 1px solid var(--border-soft);
  border-left: 3px solid var(--gc);
  border-radius: 3px;
  padding: 0.3rem 0.4rem;
}
.lag-chain-sankey svg {
  width: 100%; height: auto; display: block;
}
.lag-chain-sankey .sk-gc-bar {
  fill: var(--gc);
  opacity: 0.85;
}
.lag-chain-sankey .sk-freed-bar {
  fill: #4f9d6a;
  opacity: 0.85;
}
.lag-chain-sankey .sk-freed-flow {
  fill: #4f9d6a;
  opacity: 0.22;
}
.lag-chain-sankey .sk-gc-label {
  font-family: var(--ui); font-size: 9px;
  fill: var(--muted);
  text-transform: uppercase; letter-spacing: 0.06em;
}
.lag-chain-sankey .sk-side-l {
  font-family: var(--mono); font-size: 10px;
  fill: var(--text);
}
.lag-chain-sankey .sk-label-in {
  font-family: var(--mono); font-size: 9px;
  fill: rgba(0, 0, 0, 0.72);
  pointer-events: none;
}

/* ----- Rhythm polar ----- */
.lag-polar-shell {
  display: grid;
  grid-template-columns: minmax(280px, 360px) 1fr;
  gap: 0.7rem;
  align-items: center;
}
.lag-polar {
  width: 100%; max-width: 360px; height: auto; display: block;
  background: var(--surface);
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  margin: 0 auto;
}
.lag-polar .polar-ring {
  fill: none;
  stroke: var(--border-soft);
  stroke-width: 0.6;
  stroke-dasharray: 2 3;
}
.lag-polar .polar-core {
  fill: rgba(255,255,255,0.025);
  stroke: var(--border-soft);
  stroke-width: 0.6;
}
.lag-polar .polar-tick {
  font-family: var(--mono); font-size: 9px;
  fill: var(--muted);
}
.lag-polar .polar-bar {
  fill: var(--accent);
  opacity: 0.78;
}
.lag-polar .polar-c1 {
  font-family: var(--mono); font-size: 1.05rem;
  fill: var(--text-bright);
}
.lag-polar .polar-c2 {
  font-family: var(--ui); font-size: 9px;
  fill: var(--muted);
  text-transform: uppercase; letter-spacing: 0.08em;
}
.lag-rhythm-clusters {
  display: flex; flex-direction: column; gap: 0.3rem;
}
.lag-rhythm-cluster {
  display: grid;
  grid-template-columns: auto auto 1fr auto;
  gap: 0.5rem; align-items: center;
  font-family: var(--mono); font-size: 0.75rem;
}
.lag-rhythm-cluster .swatch {
  width: 10px; height: 10px; border-radius: 2px;
  display: inline-block;
}
.lag-rhythm-cluster .centre { color: var(--text-bright); }
.lag-rhythm-cluster .note { color: var(--muted); font-size: 0.7rem; }
.lag-rhythm-cluster .share { color: var(--muted); font-size: 0.7rem; }

.lag-empty {
  font-family: var(--mono); font-size: 0.78rem; color: var(--muted);
  padding: 0.4rem 0;
}
";
}
