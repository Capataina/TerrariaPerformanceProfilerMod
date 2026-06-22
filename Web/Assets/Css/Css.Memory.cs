#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string CssMemory = @"
/* ===== Memory tab ==================================================== */
.mem-layout { display: flex; flex-direction: column; gap: 1rem; }

.mem-summary { display: flex; flex-wrap: wrap; gap: 1.5rem; padding: 0.2rem 0 0.9rem; }
.mem-stat { display: flex; flex-direction: column; gap: 0.1rem; }
.mem-stat .k { font-family: var(--ui); font-size: 0.65rem; text-transform: uppercase;
  letter-spacing: 0.07em; color: var(--muted); }
.mem-stat .v { font-family: var(--mono); font-size: 1.05rem; color: var(--text-bright);
  font-variant-numeric: tabular-nums; }

.mem-strip { height: 1.7rem; }
.mem-strip .mem-slice { cursor: pointer; transition: filter 0.12s, width 0.3s ease-out; }
.mem-strip .mem-slice:hover { filter: brightness(1.25); }
.mem-strip .mem-slice.sel { box-shadow: inset 0 0 0 2px var(--text-bright); }
.bar-legend .lg[data-mod] { cursor: pointer; }
.bar-legend .lg[data-mod]:hover { color: var(--text); }

.mem-table-wrap { max-height: 28rem; overflow-y: auto; }
/* RAM / scaffold cell: the figure plus a proportion bar (share of the visible
   total) so the dominant column reads as a quantity, not a bare number. */
.mem-table-wrap .mem-val { white-space: nowrap; }
.mem-table-wrap .mem-val .n { display: block; font-variant-numeric: tabular-nums; margin-bottom: 0.2rem; }
.mem-table-wrap .mem-val .cellbar { min-width: 3.5rem; }

.mem-drawer { min-height: 4rem; }
.mem-drawer-head { display: flex; justify-content: space-between; align-items: baseline;
  margin-bottom: 0.7rem; padding-bottom: 0.5rem; border-bottom: 1px solid var(--border-soft); }
.mem-drawer-name { font-family: var(--ui); font-weight: 600; color: var(--text-bright); font-size: 0.95rem; }
.mem-drawer-total { font-family: var(--mono); color: var(--accent); font-variant-numeric: tabular-nums; }
.mem-sect { margin: 0.9rem 0; }
.mem-sect:first-of-type { margin-top: 0.2rem; }
.mem-sect-h { font-family: var(--ui); font-size: 0.65rem; text-transform: uppercase;
  letter-spacing: 0.07em; color: var(--muted); margin-bottom: 0.45rem; }

/* Instrumentation stat cards: compact, capped width, label-over-value with a
   proper internal gap — replaces the full-width space-between .statline rows. */
.mem-card-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(9rem, 1fr));
  gap: 0.5rem; max-width: 30rem; }
.mem-card { background: var(--surface); border: 1px solid var(--border-soft);
  border-radius: 3px; padding: 0.5rem 0.65rem; display: flex; flex-direction: column; gap: 0.15rem; }
.mem-card .k { font-family: var(--ui); font-size: 0.62rem; text-transform: uppercase;
  letter-spacing: 0.06em; color: var(--muted); }
.mem-card .v { font-family: var(--mono); font-size: 1.05rem; color: var(--text-bright);
  font-variant-numeric: tabular-nums; }
.mem-card-bar { display: block; margin-top: 0.3rem; }
/* Card track shares the panel surface; lift it to the border tone so the
   unfilled portion stays visible against the card's own surface fill. */
.mem-card-bar .cellbar { background: var(--border-soft); }
";
}
