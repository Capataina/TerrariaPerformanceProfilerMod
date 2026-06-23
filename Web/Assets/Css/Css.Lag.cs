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
    // Lag tab: pure layout only. Every visual (KPI tiles, cause x context
    // heatmap, sortable cluster/density tables, GC heap line, causality split
    // bars, rhythm histogram) now comes from the shared component library, so
    // this file holds just the two-column layout grids the GC and rhythm
    // surfaces need. The old bespoke chart/cell classes (.lag-kpi*, .lag-shell,
    // .lag-section-h, .lag-caption, .lag-table-wrap, .lag-causebars, .lag-topmod,
    // .lag-detail/.ld-*, .lag-spikestall, .lag-gc-*/.gc-*, .lag-causality-list/
    // .lag-chain*, .lag-rhythm-grid/.lag-hist*) were retired onto components.
    private const string CssLag = @"
/* =================================================== LAG TAB */
.lag-layout { display: flex; flex-direction: column; gap: 0.75rem; }

/* GC pressure: stat block beside the heap line chart. */
.lag-gc-body { display: grid; grid-template-columns: minmax(13rem, 16rem) 1fr; gap: 0.9rem; align-items: start; }
@media (max-width: 760px) { .lag-gc-body { grid-template-columns: 1fr; } }

/* Lag rhythm: interval histogram beside the cluster table. */
.lag-rhythm-body { display: grid; grid-template-columns: minmax(14rem, 20rem) 1fr; gap: 0.9rem; align-items: start; }
@media (max-width: 760px) { .lag-rhythm-body { grid-template-columns: 1fr; } }
";
}
