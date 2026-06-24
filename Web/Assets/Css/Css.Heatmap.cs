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
    private const string CssHeatmap = @"
/* =================================================== SESSION HEATMAP */
.heatmap-panel { grid-area: heatmap; }
.heatmap-wrap {
  padding: 0.7rem 0.95rem 0.95rem;
  display: flex; flex-direction: column; gap: 0.5rem;
}
.heatmap-grid {
  display: grid;
  /* Columns adapt to available width, fluid above the minimum. The Y axis is
     implicit (1 row): each cell is one minute of play. The min is generous so a
     short session (8 tiles) reads as chunky data the legend defers to, rather
     than a thin strip the full 6-item key out-sizes. */
  grid-template-columns: repeat(auto-fill, minmax(1.7rem, 1fr));
  grid-auto-rows: 1.7rem;
  gap: 3px;
}
.hm-cell {
  border-radius: 2px;
  background: var(--surface);
  position: relative;
  cursor: pointer;
  transition: transform 0.08s, box-shadow 0.08s;
}
.hm-cell:hover { transform: scale(1.18); z-index: 2; box-shadow: 0 0 0 1px var(--accent); }
/* Performance gradient by frame-time bucket */
.hm-cell.p0 { background: var(--perf-0); }
.hm-cell.p1 { background: var(--perf-1); }
.hm-cell.p2 { background: var(--perf-2); }
.hm-cell.p3 { background: var(--perf-3); }
.hm-cell.p4 { background: var(--perf-4); }
/* Within-band shade: --hm-t is 0 at the band's cool edge, 1 at its hot edge (on a
   sqrt curve, so the cool fifth where smooth sessions live gets usable resolution).
   A faster minute lifts toward 1.3 brightness, a slower one drops toward 0.7, with
   the band midpoint neutral — a wide-enough swing that an all-smooth session's real
   ms differences read as minute-to-minute texture instead of a flat block. The
   colour still ENCODES exact ms within the band, never decorates. */
.hm-cell { filter: brightness(calc(1.3 - 0.6 * var(--hm-t, 0.5))); }
.hm-cell:hover { filter: none; }
/* State overlay — boss fight cells get a red glow halo around them */
.hm-cell.boss::after {
  content: ''; position: absolute; inset: -2px;
  border: 1px solid var(--danger);
  border-radius: 3px; pointer-events: none;
  box-shadow: 0 0 6px rgba(247, 118, 142, 0.45);
}
/* A compact inline key, deliberately lighter than the tiles so the data leads:
   smaller swatches + dim text + tight spacing keep the 6-item legend from
   out-weighing the heatmap it explains. */
.heatmap-legend {
  display: flex; flex-wrap: wrap; gap: 0.2em 0.85em;
  font-family: var(--mono); font-size: 0.66rem;
  color: var(--dim);
}
.heatmap-legend .lg-sw {
  display: inline-block; width: 0.6em; height: 0.6em;
  border-radius: 2px; margin-right: 0.3em; vertical-align: middle;
}
.heatmap-legend .lg-boss {
  border: 1px solid var(--danger); background: var(--surface);
  box-shadow: 0 0 4px rgba(247, 118, 142, 0.45);
}
";
}
