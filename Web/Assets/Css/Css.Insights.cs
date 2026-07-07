#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Insights tab: layout for the interpretive page. A combined overview pane
    // (summary | cross-cutting, vertical divider) over a kanban board (columns
    // cut along a chosen axis) with clickable cards, plus the mod-context
    // drawer's insight list. Every visual inside is component-rendered (donut /
    // statGrid / splitBar / badge / cellBar); this is layout + the
    // card/column/drawer chrome only.
    private const string CssInsights = @"
/* =================================================== INSIGHTS */
.ins-shell {
  display: flex; flex-direction: column; gap: 0.6rem;
  padding: 0.6rem 0.9rem 1rem;
}

/* ----- Overview pane: summary (2/3) | cross-cutting (1/3) ----- */
.ins-ov-grid { display: grid; grid-template-columns: 2fr 1fr; gap: 0; align-items: stretch; }
.ins-ov-left  { padding-right: 1.1rem; min-width: 0; }
.ins-ov-right { padding-left: 1.1rem; border-left: 1px solid var(--border); min-width: 0; }
@media (max-width: 900px) {
  .ins-ov-grid { grid-template-columns: 1fr; }
  .ins-ov-left { padding-right: 0; }
  .ins-ov-right { border-left: 0; border-top: 1px solid var(--border); padding-left: 0; padding-top: 0.8rem; margin-top: 0.5rem; }
}
.ins-sum-layout { display: grid; grid-template-columns: minmax(0, 1.25fr) minmax(0, 1fr); gap: 1rem; align-items: center; }
@media (max-width: 680px) { .ins-sum-layout { grid-template-columns: 1fr; } }
.ins-sum-chart { display: flex; flex-direction: column; align-items: center; gap: 0.4rem; }
.ins-sum-chart .chart-donut-wrap { max-width: 10rem; }
.cc-stack { display: flex; flex-direction: column; gap: 0.85rem; }
.cc-class .split-bar { margin-top: 0.1rem; }
.cc-class .bar-legend { margin-top: 0.3rem; }

/* ----- Kanban board ----- */
/* Small uppercase label before each header control (group / show). */
.kan-lbl { font-family: var(--ui); font-size: 0.6rem; font-weight: 600; text-transform: uppercase;
  letter-spacing: 0.08em; color: var(--dim); align-self: center; margin-left: 0.2rem; }

/* The board: columns flex to FILL the width (so 2-3 columns never leave dead
   space) but hold a min so a busy session scrolls horizontally instead of
   crushing them. align-items:start lets short columns stay short. */
.kanban { display: flex; gap: 0.7rem; overflow-x: auto; padding: 0.15rem 0 0.4rem; align-items: flex-start; }
.kan-col {
  flex: 1 1 16rem; min-width: 15rem; max-width: 26rem;
  background: var(--panel-2); border: 1px solid var(--border-soft);
  border-radius: 7px; overflow: hidden; display: flex; flex-direction: column;
}
/* I4: all-weak columns start folded to their header (count chip carries the
   size); clicking the header unfolds. Keeps the LOW flood off the fold. */
.kan-col.kan-collapsed { flex: 0 0 auto; min-width: 11rem; }
.kan-col.kan-collapsed .kan-col-body { display: none; }
.kan-col.kan-collapsed .kan-col-h { cursor: pointer; opacity: 0.75; }
.kan-col.kan-collapsed .kan-col-h:hover { opacity: 1; }
.kan-col-h {
  display: flex; align-items: center; justify-content: space-between; gap: 0.5rem;
  padding: 0.5rem 0.7rem; border-bottom: 1px solid var(--border);
  border-top: 2px solid var(--accent, var(--border-soft));
}
.kan-col-t {
  font-family: var(--ui); font-size: 0.74rem; font-weight: 600;
  text-transform: uppercase; letter-spacing: 0.05em; color: var(--text);
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap; min-width: 0;
}
.kan-count {
  font-family: var(--mono); font-size: 0.7rem; color: var(--text-bright);
  background: var(--surface); padding: 0.05rem 0.5rem; border-radius: 10px; flex: none;
}
.kan-col-body {
  display: flex; flex-direction: column; gap: 0.5rem; padding: 0.55rem;
  max-height: 36rem; overflow-y: auto; overscroll-behavior: contain;
}

/* Card: a confidence-tinted left edge + a soft shadow gives the lift-off-the-
   column 'card' feel; on hover it rises and the shadow deepens. The card sits
   on --card (lighter than the column's --panel-2) for depth. */
.kan-card {
  border: 1px solid var(--border-soft); border-left: 3px solid var(--edge, var(--muted));
  border-radius: 5px; background: var(--card); padding: 0.55rem 0.6rem;
  display: flex; flex-direction: column; gap: 0.45rem; cursor: pointer;
  box-shadow: 0 1px 2px rgba(0,0,0,0.28);
  transition: transform 0.1s ease-out, box-shadow 0.12s, background 0.12s, border-color 0.12s;
}
.kan-card:hover { background: var(--hover); box-shadow: 0 4px 12px rgba(0,0,0,0.42);
  transform: translateY(-1px); border-color: var(--border); }
.kc-top { display: flex; align-items: center; justify-content: space-between; gap: 0.4rem; }
.kc-mod { min-width: 0; flex: 0 1 auto; overflow: hidden; }
.kc-text { font-family: var(--ui); font-size: 0.82rem; color: var(--text); line-height: 1.4; }
.kc-text::first-letter { text-transform: uppercase; }

/* Aggregate-card contributor breakdown: the named mods' share of the whole. The
   split-bar segments are the only colour on the card (each a per-mod hue); the
   legend and roster line stay muted chrome. Tightened from the component defaults
   so the block reads as a compact footnote under the finding, not a second hero. */
.kc-contrib { display: flex; flex-direction: column; gap: 0.3rem; }
.kc-contrib .bar-legend { margin-top: 0; font-size: 0.66rem; gap: 0.1rem 0.55rem; }
.kc-contrib .bar-legend .sw { width: 0.5rem; height: 0.5rem; }
.kc-roster { font-family: var(--mono); font-size: 0.62rem; color: var(--dim); letter-spacing: 0.02em; }

.kc-foot { display: flex; align-items: center; gap: 0.5rem; }
.kc-strength { flex: 1; min-width: 3rem; }

/* ----- Mod-context drawer: the insight list inside it ----- */
.dr-insights { display: flex; flex-direction: column; gap: 0.45rem; }
.dr-insight {
  border-left: 3px solid var(--muted); background: var(--secondary);
  border-radius: 0 4px 4px 0; padding: 0.4rem 0.55rem;
  display: flex; flex-direction: column; gap: 0.25rem;
}
.dr-ins-badges { display: flex; gap: 0.3rem; flex-wrap: wrap; }
.dr-ins-text { font-family: var(--ui); font-size: 0.8rem; color: var(--text); line-height: 1.35; }
.dr-ins-text::first-letter { text-transform: uppercase; }
";
}
