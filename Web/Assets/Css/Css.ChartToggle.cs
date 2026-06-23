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
    // The hover tooltip for the session heatmap cells (the only consumer of a
    // data-tip attribute). The former .chart-toggle / .segctl-style toggle CSS
    // that also lived here was retired when the frame-chart and memory-basis
    // toggles moved onto the shared segmented() component (.segctl).
    private const string CssChartToggle = @"
.hm-cell[data-tip]:hover::before {
  content: attr(data-tip);
  position: absolute;
  bottom: calc(100% + 6px); left: 50%; transform: translateX(-50%);
  background: var(--popover); border: 1px solid var(--accent-line); border-radius: 3px;
  padding: 0.3em 0.55em; font-family: var(--mono); font-size: 0.7rem;
  color: var(--text); white-space: nowrap; z-index: 10;
  pointer-events: none;
}
";
}
