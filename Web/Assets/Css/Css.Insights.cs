#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Insights tab: layout for the interpretive page. A combined overview pane
    // (summary | cross-cutting, vertical divider) over a kanban board (column per
    // family) with clickable cards, plus the mod-context drawer's insight list.
    // Every visual inside is component-rendered (donut / statGrid / splitBar /
    // badge / cellBar); this is layout + the card/column/drawer chrome only.
    private const string CssInsights = @"
/* =================================================== INSIGHTS */
.ins-shell {
  display: flex; flex-direction: column; gap: 0.6rem;
  padding: 0.6rem 0.9rem 1rem;
}

/* ----- Overview pane: summary (2/3) | cross-cutting (1/3) ----- */
/* One panel, two halves split by a gentle vertical rule. */
.ins-ov-grid { display: grid; grid-template-columns: 2fr 1fr; gap: 0; align-items: stretch; }
.ins-ov-left  { padding-right: 1.1rem; min-width: 0; }
.ins-ov-right { padding-left: 1.1rem; border-left: 1px solid var(--border); min-width: 0; }
@media (max-width: 900px) {
  .ins-ov-grid { grid-template-columns: 1fr; }
  .ins-ov-left { padding-right: 0; }
  .ins-ov-right { border-left: 0; border-top: 1px solid var(--border); padding-left: 0; padding-top: 0.8rem; margin-top: 0.5rem; }
}

/* Cleaned summary: headline tiles | confidence donut. */
.ins-sum-layout { display: grid; grid-template-columns: minmax(0, 1.25fr) minmax(0, 1fr); gap: 1rem; align-items: center; }
@media (max-width: 680px) { .ins-sum-layout { grid-template-columns: 1fr; } }
.ins-sum-chart { display: flex; flex-direction: column; align-items: center; gap: 0.4rem; }
.ins-sum-chart .chart-donut-wrap { max-width: 10rem; }

/* Cross-cutting stack: one stacked bar per signal class. */
.cc-stack { display: flex; flex-direction: column; gap: 0.85rem; }
.cc-class .split-bar { margin-top: 0.1rem; }
.cc-class .bar-legend { margin-top: 0.3rem; }

/* ----- Kanban board: a column per insight family ----- */
.kanban { display: flex; gap: 0.6rem; overflow-x: auto; padding-bottom: 0.3rem; align-items: flex-start; }
.kan-col {
  flex: 0 0 17.5rem; max-width: 17.5rem; min-width: 0;
  background: var(--panel-2); border: 1px solid var(--border-soft); border-radius: 5px;
  display: flex; flex-direction: column;
}
.kan-col-h {
  display: flex; align-items: center; justify-content: space-between; gap: 0.5rem;
  padding: 0.5rem 0.7rem; border-bottom: 1px solid var(--border);
  font-family: var(--ui); font-size: 0.72rem; font-weight: 600;
  text-transform: uppercase; letter-spacing: 0.06em; color: var(--text);
}
.kan-count {
  font-family: var(--mono); font-size: 0.7rem; color: var(--muted);
  background: var(--surface); padding: 0.05rem 0.45rem; border-radius: 10px;
}
.kan-col-body { display: flex; flex-direction: column; gap: 0.45rem; padding: 0.5rem;
  max-height: 34rem; overflow-y: auto; overscroll-behavior: contain; }

/* Kanban card: confidence-tinted edge; the finding sentence is the hero; badges +
   mod chip + pattern + strength bar carry the metadata. Clickable -> drawer. */
.kan-card {
  border: 1px solid var(--border-soft); border-left: 3px solid var(--muted);
  border-radius: 4px; background: var(--secondary); padding: 0.45rem 0.55rem;
  display: flex; flex-direction: column; gap: 0.3rem; cursor: pointer;
  transition: background 0.12s, border-color 0.12s;
}
.kan-card:hover { background: var(--hover); }
.kc-badges { display: flex; gap: 0.3rem; flex-wrap: wrap; }
.kc-text { font-family: var(--ui); font-size: 0.8rem; color: var(--text); line-height: 1.35; }
.kc-text::first-letter { text-transform: uppercase; }
.kc-foot { display: flex; align-items: center; gap: 0.4rem; flex-wrap: wrap; }
.kc-pat { font-family: var(--mono); font-size: 0.64rem; color: var(--muted); white-space: nowrap; }

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
