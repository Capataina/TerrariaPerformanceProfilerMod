#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string CssLag = @"
/* =================================================== LAG TAB (merged feed) */
.lagfeed {
  padding: 0.45rem 0.9rem 1rem;
  display: flex; flex-direction: column; gap: 0.45rem;
}
.lag-card {
  background: linear-gradient(180deg, var(--panel) 0%, var(--panel-2) 100%);
  border: 1px solid var(--border-soft);
  border-left: 3px solid var(--spike);
  border-radius: 4px;
  cursor: pointer;
  transition: background 0.12s, border-color 0.12s, transform 0.08s;
  user-select: none;
}
.lag-card.stall { border-left-color: var(--stall); }
.lag-card.warming { opacity: 0.55; border-left-color: var(--dim); }
.lag-card:hover {
  background: var(--hover);
  border-color: var(--border);
}
.lag-head {
  display: grid;
  grid-template-columns: 2rem 1fr auto;
  gap: 0.7rem;
  align-items: center;
  padding: 0.7rem 0.9rem;
}
.lag-glyph {
  font-size: 1.2rem;
  text-align: center;
  color: var(--spike);
  width: 2rem;
}
.lag-card.stall .lag-glyph { color: var(--stall); }
.lag-main { min-width: 0; }
.lag-title {
  font-family: var(--mono); font-size: 0.95rem; color: var(--text-bright);
  display: flex; align-items: baseline; gap: 0.5em;
}
.lag-kind {
  font-family: var(--ui); font-size: 0.65rem; font-weight: 700;
  letter-spacing: 0.1em; color: var(--spike);
  padding: 0.1em 0.45em; background: rgba(201, 127, 60, 0.12);
  border-radius: 2px;
}
.lag-kind.danger { color: var(--stall); background: rgba(185, 78, 88, 0.12); }
.lag-sub {
  font-family: var(--mono); font-size: 0.78rem; color: var(--muted);
  margin-top: 0.2rem;
}
.lag-chevron {
  font-family: var(--mono); color: var(--dim);
  transition: color 0.12s, transform 0.15s;
  font-size: 0.85rem;
}
.lag-card:hover .lag-chevron { color: var(--accent); }
.lag-detail {
  padding: 0.4rem 0.9rem 0.9rem;
  border-top: 1px solid var(--border-soft);
  display: flex; flex-direction: column; gap: 0.5rem;
}
.lag-detail.hidden { display: none; }
.lag-detail-h {
  font-family: var(--ui); font-size: 0.7rem; color: var(--muted);
  text-transform: uppercase; letter-spacing: 0.07em;
}
.lag-contribs {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 0.25rem 1.2rem;
}
.lag-c {
  display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 0.5em;
  font-family: var(--mono); font-size: 0.85rem;
}
.lag-c .nm { color: var(--text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.lag-c .vl { color: var(--muted); }
";
}
