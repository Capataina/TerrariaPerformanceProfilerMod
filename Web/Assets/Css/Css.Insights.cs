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
/* The KPI strip is a fixed-height header row, not a flex casualty. Without an
   explicit flex basis the column's other children (the tall observatory + lower
   grids) starved it down to a ~40px sliver where only fragments of the rings
   showed. flex:none + a min-height keep the four 64px ring gauges + their label
   rows at full natural height under the tab bar. */
.ins-shell > #ins-kpi { flex: 0 0 auto; min-height: 92px; }

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
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.6rem;
  align-items: start;
}
/* Cross-cutting holds its own auto-fit section grid, so it spans the full row;
   engagement + correlation share the row below, each ~half width so their
   tables fit without clipping. (A previous auto-fit minmax(320px) made 5 narrow
   columns and the wide tables clipped their right-hand columns.) */
.ins-lower > #ins-cross { grid-column: 1 / -1; }
@media (max-width: 900px) { .ins-lower { grid-template-columns: 1fr; } }

/* Cross-cutting packs one ranked table per signal class into a responsive grid
   inside its panel body. The track floor is wide enough (28rem) that it always
   clears a class table's min-content, so a table never spills into its
   neighbour; min-width:0 lets the track shrink to that floor and min() collapses
   to a single column on narrow viewports. The sub-tables are short and not in a
   scroll region, so their headers are static (no sticky overlap), and overflow
   clipping is the last-resort guard against a pathological long name. */
.cc-sections {
  display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 28rem), 1fr));
  gap: 0.8rem 0.9rem;
  align-items: start;
}
/* The grid gap owns the spacing here, so the shared vertical-flow rule
   (.section-block + .section-block margin-top) must be cancelled: left on, it
   pushes columns 2..n down by that margin and the column TOPS no longer align
   (the recurring first-column-higher-than-the-rest bug). */
.cc-sections > .section-block { min-width: 0; overflow: hidden; margin-top: 0; }
.cc-sections .dtable thead th { position: static; }
.cc-sections .section-h > span:first-child {
  min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
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
/* Zero-engagement rows render no green segment, so the bar is just its track. A
   faint surface track on the near-black row read as a blank cell. Lift the track
   contrast and seat a hairline at the origin so a 0.0% row still reads as 'bar
   present, empty' rather than 'nothing here'. */
.ins-usage .split-bar {
  background: var(--surface-2);
  box-shadow: inset 1px 0 0 var(--border);
}

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
/* The ms cost is the headline number of each row, so it leads the eye over the
   rank index: a larger, brighter, heavier value with the unit kept quiet. */
.obs-ms {
  font-family: var(--mono); font-size: 1rem; font-weight: 600;
  color: var(--text-bright); text-align: right;
  font-variant-numeric: tabular-nums;
}
.obs-ms .u { font-size: 0.62rem; font-weight: 400; color: var(--dim); margin-left: 0.15rem; }

/* Detail pane: the scroll-region is flush, so the body insets here. */
.det-pad { padding: 0.7rem 0.85rem; display: flex; flex-direction: column; gap: 0.2rem; }
.det-head { margin-bottom: 0.4rem; }
.det-title { font-family: var(--ui); font-size: 1.05rem; color: var(--text); }
.det-roster { font-family: var(--mono); font-size: 0.74rem; color: var(--muted); margin-top: 0.1rem; }

/* Plain-English caption above the correlation table. */
.ins-caption {
  font-size: 0.74rem; color: var(--dim); line-height: 1.35; margin-bottom: 0.45rem;
}

/* Mod-pair correlation has TWO name columns (mod A x mod B) sharing a half-width
   panel, so the shared 20rem td.l cap is too generous — two long names would
   still overflow. Cap each name column tighter here; the ellipsis (shared td.l
   rule) then engages and the title= tooltip carries the full pair name. */
#ins-matrix .dtable td.l, #ins-matrix .dtable th.l { max-width: 9rem; }

/* Engagement-vs-cost bubble scatter: centre the plot, legend beneath. */
#scatter-body { display: flex; flex-direction: column; align-items: center; }
#scatter-body .chart-scatter { max-width: 30rem; }
.sc-foot { margin-top: 0.3rem; }

/* Modlist-composition waffle: bound the cell grid so cells stay small squares. */
.wf-wrap { max-width: 20rem; margin: 0.1rem auto 0; }
.wf-key { margin-top: 0.55rem; }
";
}
