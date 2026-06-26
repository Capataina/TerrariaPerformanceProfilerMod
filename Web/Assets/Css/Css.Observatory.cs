#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Observatory tab: layout + the per-mod card/detail internals. Every visual
    // is component-rendered from the shared library; this holds the responsive
    // grids that arrange the panels and the small card-internal classes
    // (.obs-body / .obs-ms / .det-*) the observatory list + detail compose.
    private const string CssObservatory = @"
/* =================================================== OBSERVATORY */
.obs-shell {
  display: flex; flex-direction: column; gap: 0.6rem;
  padding: 0.6rem 0.9rem 1rem;
}

/* Top strip: composition waffle | headline stat tiles. */
.obs-top {
  display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1.35fr);
  gap: 0.6rem; align-items: start;
}
@media (max-width: 900px) { .obs-top { grid-template-columns: 1fr; } }

/* Main row (the hero): per-mod master | detail. The list + detail each own
   their height via the explicit max-height on their inner scroll regions, so
   the grid sizes to content; align-items:start keeps the shorter column from
   stretching to match the taller one. */
.obs-main {
  display: grid; grid-template-columns: minmax(0, 1.45fr) minmax(0, 1fr);
  gap: 0.6rem; align-items: start;
}
@media (max-width: 900px) { .obs-main { grid-template-columns: 1fr; } }

/* Analysis pair: engagement scatter | correlation chord. */
.obs-analysis {
  display: grid; grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.6rem; align-items: start;
}
@media (max-width: 900px) { .obs-analysis { grid-template-columns: 1fr; } }

/* Modlist-composition waffle: bound the cell grid so cells stay small squares. */
.wf-wrap { max-width: 20rem; margin: 0.1rem auto 0; }
.wf-key { margin-top: 0.55rem; }

/* Dormant usage cell: split bar + right-aligned percentage on one row. */
.obs-usage { display: flex; align-items: center; gap: 0.5rem; min-width: 8rem; }
.obs-usage .split-bar { flex: 1; background: var(--surface-2); box-shadow: inset 1px 0 0 var(--border); }
.obs-pct { color: var(--muted); flex: none; min-width: 3rem; text-align: right; }

/* Observatory card internals (the middle cell of each row()): name, usage
   micro-stats, the cpu cost bar, and the quieted roster composition bar. */
.obs-body { min-width: 0; }
.obs-micro {
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  margin-top: 0.2rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.obs-cost { margin-top: 0.3rem; }
.obs-comp { margin-top: 0.28rem; opacity: 0.55; }
/* The ms cost is the headline number of each row: larger, brighter, heavier,
   the unit kept quiet, so it leads the eye over the rank index. */
.obs-ms {
  font-family: var(--mono); font-size: 1rem; font-weight: 600;
  color: var(--text-bright); text-align: right; font-variant-numeric: tabular-nums;
}
.obs-ms .u { font-size: 0.62rem; font-weight: 400; color: var(--dim); margin-left: 0.15rem; }

/* Detail pane: the scroll-region is flush, so the body insets here. */
.det-pad { padding: 0.7rem 0.85rem; display: flex; flex-direction: column; gap: 0.2rem; }
.det-head { margin-bottom: 0.4rem; }
.det-title { font-family: var(--ui); font-size: 1.05rem; color: var(--text); }
.det-roster { font-family: var(--mono); font-size: 0.74rem; color: var(--muted); margin-top: 0.1rem; }

/* Engagement-vs-cost bubble scatter: centre the plot, legend beneath. */
#scatter-body { display: flex; flex-direction: column; align-items: center; }
#scatter-body .chart-scatter { max-width: 30rem; }
.sc-foot { margin-top: 0.3rem; }

/* Plain-English caption above a chart. */
.ins-caption { font-size: 0.74rem; color: var(--dim); line-height: 1.35; margin-bottom: 0.45rem; }

/* Correlation chord: centre it, cap its width, table beneath for exact r.
   The two name columns share a half-width panel, so cap them tighter than the
   global td.l 20rem cap; the ellipsis then engages and the title= carries the
   full pair name. */
.corr-chord { display: flex; justify-content: center; margin-bottom: 0.5rem; }
.corr-chord .chart-chord { max-width: 19rem; }
#obs-corr .dtable td.l, #obs-corr .dtable th.l { max-width: 8rem; }

/* Roster-evolution matrix: union of every mod (rows) × every distinct modlist
   the player has run (columns, oldest -> newest). A monochrome fill = present,
   empty = absent, a single accent ring = the version changed from the prior
   roster. The matrix can be wider than its panel, so its scroll region scrolls
   on both axes and the mod-name column + header row stay pinned as it scrolls. */
#obs-roster #roster-scroll { overflow: auto; }
.rmx {
  display: grid; gap: 2px; font-family: var(--mono);
  padding: 0.2rem 0.4rem 0.4rem; min-width: max-content;
}
/* Corner is pinned on both axes (it overlaps the sticky row + column). */
.rmx-corner {
  position: sticky; left: 0; top: 0; z-index: 3; background: var(--panel);
  color: var(--dim); font-size: 0.66rem; display: flex; align-items: flex-end;
  padding: 0.2rem 0.45rem;
}
/* Column headers pin to the top: a stack index, the first-seen date, the short fp. */
.rmx-col {
  position: sticky; top: 0; z-index: 2; background: var(--panel);
  display: flex; flex-direction: column; align-items: center; gap: 0.04rem;
  padding: 0.25rem 0.2rem; text-align: center;
}
.rmx-col .rmx-cn { color: var(--muted); font-size: 0.68rem; }
.rmx-col .rmx-cd { color: var(--text); font-size: 0.66rem; }
.rmx-col .rmx-cf { color: var(--dim); font-size: 0.58rem; }
/* Mod name pins to the left. */
.rmx-name {
  position: sticky; left: 0; z-index: 1; background: var(--panel);
  color: var(--muted); font-size: 0.72rem; text-align: right; align-self: center;
  padding: 0.2rem 0.5rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}
.rmx-cell {
  display: flex; align-items: center; justify-content: center; min-height: 1.6rem;
  border-radius: 2px; font-size: 0.64rem; color: var(--text-bright);
  font-variant-numeric: tabular-nums; padding: 0 0.2rem;
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.rmx-cell.absent { background: var(--surface); color: var(--dim); }
/* The one accent: a bright ring + accent text flags the version bump. */
.rmx-cell.changed { box-shadow: inset 0 0 0 1px var(--accent-line); color: var(--accent); }
.rmx-key { margin: 0.45rem 0.4rem 0.1rem; }
";
}
