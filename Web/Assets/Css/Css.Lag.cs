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

/* ----- Middle: heatmap + cluster cards ----- */
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

/* ----- Heatmap (cause × context grid) ----- */
.lag-heatmap-grid {
  display: grid;
  gap: 1px;
  background: var(--border-soft);
  border: 1px solid var(--border-soft);
}
.lag-heatmap-cell {
  background: var(--panel-2);
  padding: 0.35rem 0.4rem;
  font-family: var(--mono); font-size: 0.72rem;
  color: var(--text);
  min-height: 1.6rem;
  display: flex; align-items: center; justify-content: center;
}
.lag-heatmap-cell.head {
  background: var(--surface);
  color: var(--muted);
  font-size: 0.65rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}
.lag-heatmap-cell.row-head {
  justify-content: flex-start;
}
.lag-heatmap-cell .cnt { color: var(--text-bright); }
.lag-heatmap-cell .ms  { color: var(--muted); font-size: 0.65rem; margin-left: 0.3em; }

/* ----- Cluster cards (L1 + L5 confidence bar) ----- */
.lag-cluster-list {
  display: flex; flex-direction: column; gap: 0.4rem;
  max-height: 24rem; overflow-y: auto;
}
.lag-cluster {
  background: var(--surface);
  border: 1px solid var(--border-soft);
  border-left: 3px solid var(--accent);
  border-radius: 3px;
  padding: 0.55rem 0.7rem;
  display: flex; flex-direction: column; gap: 0.35rem;
}
.lag-cluster .title {
  font-family: var(--mono); font-size: 0.85rem; color: var(--text-bright);
  display: flex; gap: 0.5em; align-items: baseline;
}
.lag-cluster .cause {
  font-family: var(--ui); font-size: 0.62rem; font-weight: 700;
  letter-spacing: 0.08em; text-transform: uppercase;
  color: var(--accent);
  padding: 0.1em 0.4em;
  background: var(--accent-soft);
  border-radius: 2px;
}
.lag-cluster .sub {
  font-family: var(--mono); font-size: 0.72rem; color: var(--muted);
}
.lag-cluster .stats {
  display: grid; grid-template-columns: repeat(3, 1fr); gap: 0.3rem 0.6rem;
  font-family: var(--mono); font-size: 0.75rem;
}
.lag-cluster .stats .k { color: var(--muted); font-size: 0.62rem; text-transform: uppercase; letter-spacing: 0.06em; }
.lag-cluster .stats .v { color: var(--text); }
.lag-cluster .confidence-bar {
  height: 4px; width: 100%;
  background: var(--border-soft);
  border-radius: 2px;
  overflow: hidden;
  display: flex;
}
.lag-cluster .confidence-bar > span {
  display: block; height: 100%;
}
.lag-cluster .confidence-legend {
  font-family: var(--mono); font-size: 0.65rem; color: var(--muted);
  display: flex; justify-content: space-between;
}

/* ----- Per-segment lag density rows ----- */
.lag-density-rows {
  display: flex; flex-direction: column; gap: 0.3rem;
}
.lag-density-row {
  display: grid;
  grid-template-columns: minmax(8rem, 1.4fr) 1fr auto;
  gap: 0.6rem;
  align-items: center;
  font-family: var(--mono); font-size: 0.78rem;
}
.lag-density-row .nm { color: var(--text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.lag-density-row .bar-wrap {
  position: relative;
  height: 10px;
  background: var(--border-soft);
  border-radius: 2px;
  overflow: hidden;
}
.lag-density-row .bar {
  height: 100%;
  background: var(--spike);
}
.lag-density-row .bar.hot { background: var(--stall); }
.lag-density-row .vl { color: var(--muted); font-size: 0.72rem; }
.lag-density-baseline {
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  margin-bottom: 0.4rem;
}

/* ----- GC panel ----- */
.lag-gc-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.7rem;
}
.lag-gc-counters {
  display: grid; grid-template-columns: repeat(2, 1fr); gap: 0.35rem 0.7rem;
  font-family: var(--mono); font-size: 0.78rem;
}
.lag-gc-counters .k { color: var(--muted); font-size: 0.65rem; text-transform: uppercase; letter-spacing: 0.06em; }
.lag-gc-counters .v { color: var(--text-bright); }
.lag-gc-spark {
  position: relative;
  height: 4rem;
  background: var(--surface);
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  display: flex; align-items: flex-end;
  padding: 2px;
  gap: 1px;
}
.lag-gc-spark .b {
  flex: 1 1 0;
  background: var(--gc);
  min-height: 1px;
}

/* ----- Causality cards ----- */
.lag-causality-list {
  display: flex; flex-direction: column; gap: 0.4rem;
  max-height: 22rem; overflow-y: auto;
}
.lag-chain {
  background: var(--surface);
  border: 1px solid var(--border-soft);
  border-left: 3px solid var(--gc);
  border-radius: 3px;
  padding: 0.5rem 0.7rem;
  display: flex; flex-direction: column; gap: 0.3rem;
}
.lag-chain .head {
  font-family: var(--mono); font-size: 0.8rem; color: var(--text-bright);
  display: flex; gap: 0.6em; align-items: baseline; flex-wrap: wrap;
}
.lag-chain .kind {
  font-family: var(--ui); font-size: 0.6rem; font-weight: 700;
  letter-spacing: 0.08em; text-transform: uppercase;
  color: var(--gc);
  padding: 0.1em 0.4em; background: rgba(110, 93, 150, 0.12);
  border-radius: 2px;
}
.lag-chain .contribs {
  display: flex; flex-direction: column; gap: 0.2rem;
}
.lag-chain .contrib-row {
  display: grid;
  grid-template-columns: minmax(8rem, 1.2fr) 1fr auto;
  gap: 0.5rem; align-items: center;
  font-family: var(--mono); font-size: 0.75rem;
}
.lag-chain .contrib-row .nm { color: var(--text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.lag-chain .contrib-row .bar-wrap {
  height: 6px; background: var(--border-soft); border-radius: 2px; overflow: hidden;
}
.lag-chain .contrib-row .bar { height: 100%; background: var(--alloc); }
.lag-chain .contrib-row .vl { color: var(--muted); font-size: 0.7rem; }

/* ----- Rhythm histogram ----- */
.lag-rhythm-hist {
  display: flex; align-items: flex-end;
  gap: 1px;
  height: 5rem;
  padding: 2px;
  background: var(--surface);
  border: 1px solid var(--border-soft);
  border-radius: 3px;
}
.lag-rhythm-hist .b {
  flex: 1 1 0;
  background: var(--accent);
  min-height: 1px;
  opacity: 0.75;
}
.lag-rhythm-axis {
  display: flex; justify-content: space-between;
  font-family: var(--mono); font-size: 0.65rem; color: var(--muted);
  margin-top: 0.25rem;
}
.lag-rhythm-clusters {
  margin-top: 0.5rem;
  display: flex; flex-direction: column; gap: 0.25rem;
}
.lag-rhythm-cluster {
  display: grid;
  grid-template-columns: auto 1fr auto;
  gap: 0.5rem; align-items: center;
  font-family: var(--mono); font-size: 0.75rem;
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
