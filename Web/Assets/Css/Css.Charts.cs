#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Chart vocabulary: the shared visual primitives every data surface draws
    // with, so no pane hand-rolls its own chart again. SVG charts (line / area /
    // donut / gauge) are responsive via viewBox; bar charts are CSS divs so they
    // stay crisp and reflow. Loaded after Css.Components (tokens + cellbar exist).
    private const string CssCharts = @"
/* ===== Generic SVG chart frame ===================================== */
.chart-svg { display: block; width: 100%; height: auto; }
.chart-line { fill: none; stroke-width: 1.4; }
.chart-area { stroke: none; opacity: 0.14; }
.chart-rule { stroke-dasharray: 3 3; stroke-width: 1; opacity: 0.5; }
.chart-mark { stroke-width: 1; opacity: 0.7; }
.chart-axis { display: flex; justify-content: space-between; font-family: var(--mono);
  font-size: 0.66rem; color: var(--dim); margin-top: 0.2rem; }
.chart-axis .lo { text-align: left; } .chart-axis .hi { text-align: right; }

/* ===== Sparkline =================================================== */
.spark-svg { display: block; width: 100%; height: 100%; }

/* ===== Donut / pie ================================================= */
.chart-donut-wrap { position: relative; display: flex; align-items: center; justify-content: center; }
.chart-donut { display: block; max-width: 100%; height: auto; }
.chart-center { position: absolute; inset: 0; display: flex; flex-direction: column;
  align-items: center; justify-content: center; text-align: center; pointer-events: none; }
.chart-center .cc-top { font-family: var(--mono); font-size: 1.3rem; color: var(--text-bright); line-height: 1; }
.chart-center .cc-mid { font-family: var(--ui); font-size: 0.72rem; color: var(--muted); margin-top: 0.15rem;
  max-width: 7rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.chart-center .cc-bot { font-family: var(--mono); font-size: 0.7rem; color: var(--dim); }

/* ===== Gauge ====================================================== */
.chart-gauge { display: block; max-width: 100%; height: auto; }
.gauge-track { fill: none; stroke: var(--surface); }
.gauge-val { fill: none; }
.gauge-text { font-family: var(--mono); fill: var(--text-bright); text-anchor: middle; }
.gauge-sub  { font-family: var(--ui); fill: var(--muted); text-anchor: middle; }

/* ===== Bar chart: vertical column strip ========================== */
.bar-strip { display: flex; align-items: flex-end; gap: 1px; height: 100%;
  width: max-content; min-width: 100%; }
.bar-strip.scrollx { overflow-x: auto; overflow-y: hidden; }
.bar-col { flex: 0 0 auto; display: flex; flex-direction: column; justify-content: flex-end;
  align-items: center; height: 100%; position: relative; }
.bar-col .bar-col-fill { width: 100%; min-height: 2px; border-radius: 1px 1px 0 0;
  background: var(--cpu); opacity: 0.85; transition: height 0.2s; }
.bar-col:hover .bar-col-fill { opacity: 1; }
.bar-col .bar-mark { position: absolute; top: 0; width: 4px; height: 4px; border-radius: 50%; }
.bar-col .bar-mark.spike { background: var(--spike); }
.bar-col .bar-mark.stall { background: var(--stall); transform: translateY(5px); }

/* ===== Bar chart: horizontal rows (histogram) =================== */
.bar-rows { display: flex; flex-direction: column; gap: 0.2rem; }
.bar-row { display: grid; grid-template-columns: minmax(3.5rem, auto) 1fr minmax(2.5rem, auto);
  gap: 0.5rem; align-items: center; font-family: var(--mono); font-size: 0.78rem; }
.bar-row .lbl { color: var(--muted); text-align: right; white-space: nowrap; }
.bar-row .val { color: var(--text); text-align: right; font-variant-numeric: tabular-nums; }
";
}
