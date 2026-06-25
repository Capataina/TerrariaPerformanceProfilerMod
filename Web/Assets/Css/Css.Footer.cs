#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string CssFooter = @"
/* =================================================== FOOTER */
.footstrip {
  display: flex; align-items: center; gap: 1.2rem;
  padding: 0.4rem 1.2rem;
  background: var(--header);
  border-top: 1px solid var(--border);
  font-family: var(--mono); font-size: 0.72rem;
  color: var(--dim);
}
.footstrip .foot-spacer { flex: 1; }
";
}
