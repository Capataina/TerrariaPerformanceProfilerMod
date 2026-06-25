#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Insights tab: layout for the interpretive feed page. Holds the summary
    // infographic grid, the feed | sidebar main grid, the finding-card chrome,
    // and the cross-cutting stacked-bar stack. Every visual inside is
    // component-rendered (radialBars / donut / statGrid / splitBar / badge); this
    // is layout + the card frame only.
    private const string CssInsights = @"
/* =================================================== INSIGHTS */
.ins-shell {
  display: flex; flex-direction: column; gap: 0.6rem;
  padding: 0.6rem 0.9rem 1rem;
}

/* Summary infographic row: radial bars | multi-level donut | headline tiles.
   align-items:center so the two charts and the tile block sit on one baseline. */
.ins-summary-grid {
  display: grid; grid-template-columns: minmax(0, 0.95fr) minmax(0, 0.85fr) minmax(0, 1.1fr);
  gap: 1rem 1.2rem; align-items: center;
}
@media (max-width: 900px) { .ins-summary-grid { grid-template-columns: 1fr; } }
.iss-cell { display: flex; flex-direction: column; align-items: center; gap: 0.4rem; min-width: 0; }
.iss-h {
  font-family: var(--ui); font-size: 0.7rem; font-weight: 600; text-transform: uppercase;
  letter-spacing: 0.07em; color: var(--muted); align-self: flex-start;
}
.iss-cell .chart-radial, .iss-cell .chart-donut-wrap { max-width: 12.5rem; }
.iss-cell .bar-legend { justify-content: center; }
.iss-cell .stat-grid { width: 100%; }

/* Main row: the feed (hero) | cross-cutting (sidebar). Each owns its height via
   its inner scroll region; align-items:start keeps the shorter column honest. */
.ins-main {
  display: grid; grid-template-columns: minmax(0, 1.55fr) minmax(0, 1fr);
  gap: 0.6rem; align-items: start;
}
@media (max-width: 1000px) { .ins-main { grid-template-columns: 1fr; } }

/* ----- The feed: a column of finding cards ----- */
.feed { display: flex; flex-direction: column; gap: 0.5rem; padding: 0.6rem 0.7rem; }
/* Each card: a confidence-tinted left edge + framed block. The sentence is the
   hero; the head + foot carry the metadata (family eyebrow, badges, mod, pattern,
   strength). */
.ins-card {
  border: 1px solid var(--border-soft); border-left: 3px solid var(--muted);
  border-radius: 4px; background: var(--secondary);
  padding: 0.5rem 0.7rem; display: flex; flex-direction: column; gap: 0.35rem;
  transition: background 0.12s;
}
.ins-card:hover { background: var(--hover); }
.ic-head { display: flex; align-items: center; justify-content: space-between; gap: 0.6rem; }
.ic-fam {
  font-family: var(--ui); font-size: 0.62rem; font-weight: 600; text-transform: uppercase;
  letter-spacing: 0.08em; color: var(--dim); white-space: nowrap;
}
.ic-badges { display: flex; gap: 0.3rem; flex: none; }
.ic-text { font-family: var(--ui); font-size: 0.86rem; color: var(--text); line-height: 1.4; }
.ic-text::first-letter { text-transform: uppercase; }
.ic-foot { display: flex; align-items: center; gap: 0.6rem; }
.ic-pat { font-family: var(--mono); font-size: 0.68rem; color: var(--muted); white-space: nowrap; }
.ic-bar { flex: 1; min-width: 3rem; max-width: 8rem; margin-left: auto; }

/* ----- Cross-cutting sidebar: one stacked bar per signal class ----- */
.cc-stack { display: flex; flex-direction: column; gap: 0.95rem; padding: 0.6rem 0.8rem; }
.cc-class .split-bar { margin-top: 0.1rem; }
.cc-class .bar-legend { margin-top: 0.35rem; }
";
}
