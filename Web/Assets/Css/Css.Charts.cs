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
/* Clickable donut slices + legend rows (wired to open the mod card). */
.chart-donut .slice.hit { cursor: pointer; transition: opacity 0.12s; }
.chart-donut .slice.hit:hover { opacity: 0.8; }
.bar-legend .lg.hit { cursor: pointer; border-radius: 2px; transition: color 0.12s; }
.bar-legend .lg.hit:hover { color: var(--text-bright); }

/* ===== Gauge ====================================================== */
.chart-gauge { display: block; max-width: 100%; height: auto; }
.gauge-track { fill: none; stroke: var(--surface); }
.gauge-val { fill: none; }
/* dominant-baseline:central so the value/sub are vertically CENTRED on their y
   anchor instead of sitting on the baseline (which pushed the number up into the
   arc stroke and read as off-centre). */
.gauge-text { font-family: var(--mono); fill: var(--text-bright); text-anchor: middle; dominant-baseline: central; }
.gauge-sub  { font-family: var(--ui); fill: var(--muted); text-anchor: middle; dominant-baseline: central; }
/* Optional reference tick on a gauge arc (e.g. an overhead budget threshold). */
.gauge-ref { stroke: var(--text-bright); stroke-width: 1.5; opacity: 0.85; }

/* ===== Scatter / bubble =========================================== */
.chart-scatter { display: block; width: 100%; height: auto; }
.chart-scatter .sc-axis { stroke: var(--border); stroke-width: 1; }
.chart-scatter .sc-grid { stroke: var(--border-soft); stroke-width: 1; stroke-dasharray: 2 3; opacity: 0.6; }
.chart-scatter .sc-diag { stroke: var(--muted); stroke-width: 1; stroke-dasharray: 3 3; opacity: 0.5; }
.chart-scatter .sc-dot { opacity: 0.78; stroke: var(--bg-page); stroke-width: 0.6; }
.chart-scatter .sc-dot.hit { cursor: pointer; transition: opacity 0.12s; }
.chart-scatter .sc-dot.hit:hover { opacity: 1; stroke: var(--accent); stroke-width: 1.2; }
.chart-scatter .sc-lbl { fill: var(--muted); font-family: var(--ui); font-size: 9px; }
.chart-scatter .sc-tick { fill: var(--dim); font-family: var(--mono); font-size: 8.5px; }

/* ===== Waffle: unit-grid composition ============================== */
.chart-waffle { display: grid; gap: 2px; }
.chart-waffle .wf-cell { aspect-ratio: 1; border-radius: 1px; }

/* ===== Stacked-area stream ======================================== */
.chart-stream { display: block; width: 100%; height: 100%; }
/* Lower band opacity reads as a soft, layered gradient (modern/sleek) rather
   than hard saturated blocks; the perf-ramp hue still encodes impact. */
.chart-stream .st-band { stroke: none; fill-opacity: 0.68; }
.chart-stream .st-band:hover { fill-opacity: 0.92; }
/* The total envelope + its sample markers (drawn over the stack) — monochrome,
   a near-white line so the total reads as one bright contour over the grey stack. */
.chart-stream .st-line { fill: none; stroke: oklch(0.92 0 0); stroke-width: 1.3; opacity: 0.7; }
.chart-stream .st-dot { fill: var(--bg-deep); stroke: oklch(0.92 0 0); stroke-width: 1.3; }

/* ===== Sankey: two-layer flow ===================================== */
.chart-sankey { display: block; width: 100%; height: auto; }
.chart-sankey .sk-link { opacity: 0.3; transition: opacity 0.12s; }
.chart-sankey .sk-link:hover { opacity: 0.6; }
.chart-sankey .sk-node { rx: 1; }
.chart-sankey .sk-lbl { fill: var(--text); font-family: var(--mono); font-size: 8.5px; }

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

/* ===== Chord: circular pairwise relationships ===================== */
.chart-chord { display: block; max-width: 100%; height: auto; }
.chart-chord .cd-arc { stroke: var(--bg-page); stroke-width: 0.5; }
.chart-chord .cd-link { opacity: 0.34; transition: opacity 0.12s; }
.chart-chord .cd-link:hover { opacity: 0.62; }

/* ===== Bar chart: horizontal rows (histogram) =================== */
.bar-rows { display: flex; flex-direction: column; gap: 0.2rem; }
.bar-row { display: grid; grid-template-columns: minmax(3.5rem, auto) 1fr minmax(2.5rem, auto);
  gap: 0.5rem; align-items: center; font-family: var(--mono); font-size: 0.78rem; }
.bar-row .lbl { color: var(--muted); text-align: right; white-space: nowrap; }
.bar-row .val { color: var(--text); text-align: right; font-variant-numeric: tabular-nums; }
";
}
