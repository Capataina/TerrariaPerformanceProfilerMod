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
    // Lag tab CSS — readable-vocabulary rebuild.
    //
    // The bespoke per-surface chart CSS (hex grid, galaxy circles + rings,
    // beeswarm dots, gradient tide + wind arrows, inline sankey bands, polar
    // rings/wedges/bars) is gone: every Lag surface now composes the shared
    // components (.rheat / .dtable / .split-bar / .chip / .statline), whose
    // styling lives in Css.Components. What remains here is tab-local layout
    // and the few small wrappers the surfaces need (detail strip, top-mod
    // cell, spike/stall cell, GC two-column grid, chain card, histogram row).
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

/* ----- Shared panel chrome for every Lag surface ----- */
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
.lag-caption, .lag-hint {
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  margin-bottom: 0.4rem;
}
.lag-hint { margin-top: 0.4rem; margin-bottom: 0; }
.lag-empty {
  font-family: var(--mono); font-size: 0.78rem; color: var(--muted);
  padding: 0.4rem 0;
}
.lag-table-wrap { max-height: 24rem; overflow-y: auto; }
.lag-bar-legend { margin-top: 0.1rem; }

/* ----- Cause × context heatmap (.rheat local sizing) ----- */
.lag-rheat { align-content: start; }

/* Single-context fallback: ranked horizontal bar list (one row per cause).
   Used when there is only one distinct context, where a 1-column heatmap
   would be pointless. Mirrors the rhythm histogram row layout. */
.lag-causebars { display: flex; flex-direction: column; gap: 0.25rem; }
.lag-causebar-row {
  display: grid;
  grid-template-columns: minmax(7rem, 12rem) 1fr 3rem;
  gap: 0.5rem; align-items: center;
  font-family: var(--mono); font-size: 0.74rem;
}
.lag-causebar-row .lbl { color: var(--text); overflow: hidden;
  text-overflow: ellipsis; white-space: nowrap; }
.lag-causebar-row .bar { display: block; }
.lag-causebar-row .val { color: var(--text-bright); text-align: right;
  font-variant-numeric: tabular-nums; }

/* ----- Fingerprint cluster table cells ----- */
/* Top-mod cell: name + share over a thin split bar coloured by modColor. */
.lag-topmod { display: flex; flex-direction: column; gap: 0.12rem; min-width: 9rem; }
.lag-topmod .nm { font-size: 0.78rem; }
.lag-topmod .sh { color: var(--muted); font-size: 0.68rem; }

/* Selected-fingerprint detail strip. */
.lag-detail {
  margin-top: 0.5rem;
  background: var(--surface);
  border: 1px solid var(--border-soft);
  border-left: 3px solid var(--accent);
  border-radius: 3px;
  padding: 0.45rem 0.65rem;
  display: flex; flex-direction: column; gap: 0.3rem;
}
.lag-detail .ld-top {
  display: flex; gap: 0.5em; align-items: baseline; flex-wrap: wrap;
  font-family: var(--mono); font-size: 0.78rem; color: var(--text-bright);
}
.lag-detail .ld-cause {
  font-family: var(--ui); font-size: 0.62rem; font-weight: 700;
  letter-spacing: 0.08em; text-transform: uppercase;
  color: var(--accent);
  padding: 0.1em 0.4em;
  background: var(--accent-soft);
  border-radius: 2px;
}
/* Raw fingerprint id is now a dimmed secondary token (the cause leads). */
.lag-detail .ld-fp { color: var(--dim); font-size: 0.7rem; }
.lag-detail .ld-sub { color: var(--muted); font-size: 0.72rem; }
.lag-detail .ld-stats {
  display: flex; gap: 1em; flex-wrap: wrap;
  font-family: var(--mono); font-size: 0.74rem; color: var(--muted);
}
.lag-detail .ld-stats b { color: var(--text-bright); font-weight: 600; }

/* ----- Per-segment density: spike/stall cell ----- */
.lag-spikestall { display: flex; flex-direction: column; gap: 0.15rem; min-width: 8rem; }
.lag-spikestall .cnt { font-family: var(--mono); font-size: 0.7rem; color: var(--muted); }
.lag-spikestall .cnt .spk { color: var(--spike); }
.lag-spikestall .cnt .stl { color: var(--stall); }

/* ----- GC pressure: stat block + heap chart ----- */
.lag-gc-grid {
  display: grid;
  grid-template-columns: minmax(13rem, 16rem) 1fr;
  gap: 0.8rem;
  align-items: start;
}
.lag-gc-stats { display: flex; flex-direction: column; }
.lag-gc-chart {
  background: var(--surface);
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  padding: 0.2rem;
}
.gc-heap { width: 100%; height: auto; display: block; }
.gc-heap .gc-baseline { stroke: var(--border-soft); stroke-width: 1; }
.gc-heap .gc-area { fill: var(--accent-soft); stroke: none; }
.gc-heap .gc-line { fill: none; stroke: var(--accent); stroke-width: 1.2; }
.gc-heap .gc-peak { fill: var(--accent); }
.gc-heap .gc-peak-l { font-family: var(--mono); font-size: 9px; fill: var(--text-bright); }
.gc-heap .gc-axis { font-family: var(--mono); font-size: 9px; fill: var(--muted); }

/* ----- Allocation → GC → freed chain cards ----- */
.lag-causality-list {
  display: flex; flex-direction: column; gap: 0.5rem;
  max-height: 24rem; overflow-y: auto;
}
.lag-chain {
  background: var(--surface);
  border: 1px solid var(--border-soft);
  border-left: 3px solid var(--gc);
  border-radius: 3px;
  padding: 0.5rem 0.6rem;
  display: flex; flex-direction: column; gap: 0.35rem;
}
.lag-chain-stats {
  display: flex; flex-wrap: wrap; gap: 0.25rem 1.1rem;
  font-family: var(--mono); font-size: 0.74rem;
}
.lag-chain-stats .st { display: inline-flex; gap: 0.35rem; align-items: baseline; }
.lag-chain-stats .k { color: var(--muted); font-size: 0.66rem;
  text-transform: uppercase; letter-spacing: 0.05em; }
.lag-chain-stats .v { color: var(--text-bright); }

/* ----- Lag rhythm: horizontal histogram + cluster table ----- */
.lag-rhythm-grid {
  display: grid;
  grid-template-columns: minmax(14rem, 20rem) 1fr;
  gap: 0.8rem;
  align-items: start;
}
.lag-hist { display: flex; flex-direction: column; gap: 0.2rem; }
.lag-hist-row {
  display: grid;
  grid-template-columns: 3.5rem 1fr 3rem;
  gap: 0.5rem; align-items: center;
  font-family: var(--mono); font-size: 0.74rem;
}
.lag-hist-row .lbl { color: var(--muted); text-align: right; }
.lag-hist-row .bar { display: block; }
.lag-hist-row .val { color: var(--text-bright); text-align: right;
  font-variant-numeric: tabular-nums; }
";
}
