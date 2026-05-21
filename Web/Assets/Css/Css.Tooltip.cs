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
    private const string CssTooltip = @"
/* =================================================== TOOLTIP */
.tooltip {
  position: fixed; z-index: 100;
  max-width: 22rem;
  background: #11161fee;
  border: 1px solid var(--accent-line);
  border-radius: 4px;
  padding: 0.6rem 0.8rem;
  font-family: var(--ui); font-size: 0.82rem;
  color: var(--text);
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4);
  pointer-events: none;
  transition: opacity 0.1s;
}
.tooltip strong { color: var(--accent); }
.tooltip .tip-title {
  font-weight: 600; color: var(--text-bright); margin-bottom: 0.3em;
  display: block;
}
.tooltip code {
  background: rgba(255,255,255,0.06); padding: 0.05em 0.3em;
  border-radius: 2px; font-family: var(--mono); font-size: 0.85em;
}
";
}
