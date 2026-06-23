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
    private const string CssMods = @"
/* ====== Mod table: a rowList of row()s + a static aligned header ======= */
/* The headline / category / hook rows are all the shared .row (grid columns
   passed inline by the renderer, matching the header below). This layer only
   carries the header, the scroll bound, the unit subscripts, and the tree
   indent + colouring — everything else (grid, hover, outlier bar, twirl) is
   inherited from the component .row / .twirl. */
.panel-wide { grid-area: mods; }

.modtable-head {
  display: grid;
  grid-template-columns: 2em minmax(0, 1.4fr) minmax(0, 2.2fr) 4.5em 4.5em 4.5em 5em;
  align-items: center; gap: 0.5rem;
  padding: 0.4rem 0.6rem;
  /* Match the row's reserved 2px left bar so the header columns stay aligned. */
  border-left: 2px solid transparent;
  background: var(--header);
  border-bottom: 1px solid var(--border-soft);
}
.modtable-head .mh {
  font-family: var(--ui); font-size: 0.7rem; color: var(--muted);
  text-transform: uppercase; letter-spacing: 0.07em;
}
.modtable-head .mh.rank, .modtable-head .mh.num { text-align: right; }
.modtable-head .mh.bar { text-align: left; }
.modtable-head .mh.trend { text-align: center; }
.modtable-head .mh [data-explain] { cursor: help; }

.modtable-scroll { max-height: 32rem; overflow-y: auto; padding-bottom: 0.4rem; }

/* Headline mod row: name cell (twirl + selectable name) + right-aligned nums. */
.modrow .nm-cell { display: flex; align-items: center; gap: 0.4em; min-width: 0; }
.modrow .modname {
  min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
  color: var(--text); cursor: pointer; user-select: text;
}
.modrow .modname:hover { color: var(--accent); }
.mt-num { text-align: right; color: var(--text); font-family: var(--mono); }
.mt-num.muted { color: var(--muted); }
.mt-num .u { color: var(--muted); margin-left: 0.18em; font-size: 0.85em; }
.mt-spark { height: 0.9rem; min-width: 0; }
.mt-spark .spark-svg { height: 100%; }

/* Cascading detail rows (category + hook level). */
.mod-tree {
  background: rgba(0, 0, 0, 0.18);
  border-bottom: 1px solid var(--border-soft);
  padding: 0.3rem 0;
}
.cat-row { margin-left: 1.4rem; color: var(--accent); font-size: 0.82rem; }
.cat-row .nm { color: var(--accent); display: flex; gap: 0.4em; align-items: center; min-width: 0; }
.hook-row { margin-left: 3rem; color: var(--muted); font-size: 0.78rem; }
.hook-row .nm { user-select: text; }
";
}
