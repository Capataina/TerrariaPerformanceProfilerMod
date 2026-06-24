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

/* Per-segment density cell: the magnitude-scaled split bar with its spike/stall
   + events-per-minute caption centred beneath it, so the count and the geometry
   it labels share a vertical axis instead of the caption floating left. */
.lag-density-cell { display: flex; flex-direction: column; gap: 0.15rem; min-width: 9rem; }
.lag-density-cell .cap { font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  text-align: center; }

/* GC pressure: stat block beside the heap line chart. */
.lag-gc-body { display: grid; grid-template-columns: minmax(13rem, 16rem) 1fr; gap: 0.9rem; align-items: start; }
@media (max-width: 760px) { .lag-gc-body { grid-template-columns: 1fr; } }

/* Allocation -> gc -> freed: when no stall has been observed the body holds a
   single 'no gc stalls observed' line. The canonical .empty reserves 4.5rem of
   breathing room, which reads as a tall hollow panel for one centred line here,
   so the causality empty-state is tightened to size to its own content. */
#lag-causality-body > .empty { min-height: 0; padding: 0.75rem 1rem; }

/* Lag rhythm: interval histogram beside the cluster table. The two halves
   carry different row counts (a coarse ~handful-bucket histogram on the left, a
   taller cluster table on the right), so they are stretched to a shared height
   and the shorter side's bar group is centred within it. That closes the dead
   space that opened under whichever half is shorter, in either direction,
   without forcing artificial inter-row gaps. */
.lag-rhythm-body { display: grid; grid-template-columns: minmax(14rem, 20rem) 1fr; gap: 0.9rem; align-items: stretch; }
.lag-rhythm-body > #lag-rhythm-hist { display: flex; flex-direction: column; }
.lag-rhythm-body > #lag-rhythm-hist > .bar-rows { flex: 1 1 auto; justify-content: center; }
@media (max-width: 760px) { .lag-rhythm-body { grid-template-columns: 1fr; align-items: start; } }
";
}
