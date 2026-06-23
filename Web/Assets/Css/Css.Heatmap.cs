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
  /* Columns adapt to available width: every cell is at least 12px wide,
     fluid above that. The Y axis is implicit (1 row), since per the user's
     intent each cell is one minute of play.  */
  grid-template-columns: repeat(auto-fill, minmax(1.1rem, 1fr));
  gap: 2px;
}
.hm-cell {
  aspect-ratio: 1;
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
/* State overlay — boss fight cells get a red glow halo around them */
.hm-cell.boss::after {
  content: ''; position: absolute; inset: -2px;
  border: 1px solid var(--danger);
  border-radius: 3px; pointer-events: none;
  box-shadow: 0 0 6px rgba(247, 118, 142, 0.45);
}
.heatmap-legend {
  display: flex; flex-wrap: wrap; gap: 0.4em 1.2em;
  font-family: var(--mono); font-size: 0.75rem;
  color: var(--muted);
}
.heatmap-legend .lg-sw {
  display: inline-block; width: 0.8em; height: 0.8em;
  border-radius: 2px; margin-right: 0.4em; vertical-align: middle;
}
.heatmap-legend .lg-boss {
  border: 1px solid var(--danger); background: var(--surface);
  box-shadow: 0 0 4px rgba(247, 118, 142, 0.45);
}
";
}
