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
    // Insights tab: pure layout only. Every visual (KPI ring gauges, dormant
    // table, observatory card list, detail stat lines / tables, cross-cutting /
    // engagement / correlation tables) is component-rendered from the shared
    // library, so this file holds only the responsive grids that arrange the
    // panels. The old bespoke surfaces (.ins-kpi/.tile/.ring*, .ins-dormant
    // and its .dor-*, .ins-obs-list/.ins-obs-card/.rank/.body/.nm/.micro/.cost/
    // .comp/.ms, .ins-detail and its .det-*, .ins-cross/.cc-*, .ins-scatter/
    // .sc-*, .ins-matrix/.mx-*/.r-pos/.r-neg) were retired onto components.
    private const string CssInsights = @"
/* =================================================== INSIGHTS */
.ins-shell {
  display: flex; flex-direction: column; gap: 0.6rem;
  padding: 0.6rem 0.9rem 1rem;
}

/* Mid section: 2-column observatory | detail. Each column owns its height via
   the explicit max-height on its inner scroll regions (the dormant table, the
   observatory card list ~10 rows, the detail body), so the grid sizes to content
   and nothing overflows onto the lower row. align-items:start keeps the shorter
   column from stretching to match the taller one. (A previous max-height:68vh on
   the grid did NOT cap the grid rows — they grew to the full 29-row roster and
   spilled over the lower section.) */
.ins-mid {
  display: grid; grid-template-columns: minmax(0, 1.5fr) minmax(0, 1fr);
  gap: 0.6rem;
  align-items: start;
}
@media (max-width: 900px) { .ins-mid { grid-template-columns: 1fr; } }
/* Observatory column stacks the dormant panel over the card-list panel; the
   list panel grows to fill the remaining height (its .panel.fill + the
   scroll-region inside own the overflow) so the list reaches the bottom of
   the row instead of leaving dead space below short lists. */
.ins-observatory {
  display: flex; flex-direction: column; gap: 0.6rem;
  min-width: 0; min-height: 0;
}

/* Lower analytical row: cross-cutting | engagement | correlation. A responsive
   grid so the three surfaces pack across the full width instead of stacking
   full-width with empty right-hand strips. Cross-cutting is the widest (it
   holds its own auto-fit section grid) so it spans the full row; engagement
   and correlation are narrower tables sharing the next row. On narrow
   viewports the auto-fit collapses everything to a single column. */
.ins-lower {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 0.6rem;
  align-items: start;
}
.ins-lower > #ins-cross { grid-column: 1 / -1; }

/* Cross-cutting packs one ranked table per signal class into an auto-fit grid
   inside its panel body. */
.cc-sections {
  display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 0.6rem 0.9rem;
}

/* Width caps for the in-cell magnitude bars so a cellBar in a table column
   does not stretch the whole column. */
.ins-cell { width: 6rem; }

/* KPI strip: four full-ring gauges in an even grid inside the panel body.
   Each cell pairs the ring with a label + sub. Collapses to two columns on
   narrow viewports. */
.kpi-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 0.6rem; }
@media (max-width: 700px) { .kpi-grid { grid-template-columns: repeat(2, 1fr); } }
.kpi-cell { display: grid; grid-template-columns: 64px minmax(0, 1fr); gap: 0.55rem; align-items: center; }
.kpi-meta { display: flex; flex-direction: column; gap: 0.15rem; min-width: 0; }
.kpi-lbl {
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  letter-spacing: 0.08em; text-transform: uppercase;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}
.kpi-sub { font-family: var(--mono); font-size: 0.72rem; color: var(--dim); }

/* Fill-column panels (observatory list + detail) grow to the height of the
   mid row so their inner scroll regions engage. */
.ins-fillcol { min-height: 0; height: 100%; }

/* Dormant + engagement usage cell: split bar plus a right-aligned percentage,
   sharing one row inside the table cell. */
.ins-usage { display: flex; align-items: center; gap: 0.5rem; min-width: 8rem; }
.ins-usage .split-bar { flex: 1; }
.ins-pct { color: var(--muted); flex: none; min-width: 3rem; text-align: right; }

/* Observatory card internals (the middle cell of each row()). Stacks the mod
   name, the usage micro-stats, the cpu cost bar, and the (quieted) roster
   composition bar. The composition is a secondary signal: its bar is full
   width regardless of mod size, so it is dropped below the cpu/cost signals
   and lowered in opacity so it never leads. */
.obs-body { min-width: 0; }
.obs-micro {
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  margin-top: 0.2rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.obs-cost { margin-top: 0.3rem; }
.obs-comp { margin-top: 0.28rem; opacity: 0.55; }
.obs-ms {
  font-family: var(--mono); font-size: 0.82rem; color: var(--text); text-align: right;
}
.obs-ms .u { font-size: 0.65rem; color: var(--dim); margin-left: 0.15rem; }

/* Detail pane: the scroll-region is flush, so the body insets here. */
.det-pad { padding: 0.7rem 0.85rem; display: flex; flex-direction: column; gap: 0.2rem; }
.det-head { margin-bottom: 0.4rem; }
.det-title { font-family: var(--ui); font-size: 1.05rem; color: var(--text); }
.det-roster { font-family: var(--mono); font-size: 0.74rem; color: var(--muted); margin-top: 0.1rem; }

/* Plain-English caption above the correlation table. */
.ins-caption {
  font-size: 0.74rem; color: var(--dim); line-height: 1.35; margin-bottom: 0.45rem;
}
";
}
