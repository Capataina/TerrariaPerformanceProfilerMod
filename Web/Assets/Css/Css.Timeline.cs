#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string CssTimeline = @"
/* =================================================== TIMELINE TAB */
.timeline {
  padding: 0.45rem 0.9rem 1rem;
  display: flex; flex-direction: column; gap: 0.45rem;
}
.tl-seg {
  background: linear-gradient(180deg, var(--panel) 0%, var(--panel-2) 100%);
  border: 1px solid var(--border-soft);
  border-left: 3px solid var(--good);
  border-radius: 4px;
  padding: 0.7rem 0.9rem;
  cursor: pointer;
  transition: background 0.12s, border-color 0.12s;
  user-select: none;
}
.tl-seg:hover { background: var(--hover); border-color: var(--border); }
.tl-seg-main {
  display: grid;
  grid-template-columns: minmax(0, 1.6fr) 5em 6em 5em 5em minmax(0, 1.4fr);
  gap: 0.6rem 1.1rem; align-items: center;
  font-family: var(--mono); font-size: 0.9rem;
}
.tl-seg-main .name {
  min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
  color: var(--text-bright); font-weight: 500;
}
.tl-seg-main .dur { text-align: right; color: var(--text); font-family: var(--mono); }
.tl-seg-main .mspt { text-align: right; color: var(--accent); font-family: var(--mono); font-weight: 500; }
.tl-seg-main .badge { text-align: right; color: var(--muted); font-size: 0.78rem; }
.tl-seg-main .chips { display: flex; gap: 0.5em; justify-content: flex-end; font-size: 0.78rem; }
.tl-seg-main .chips .chip.death { color: var(--danger); }
.tl-seg-main .chips .chip.spike { color: var(--amber); }
.tl-seg-main .chips .chip.stall { color: var(--danger); }
.tl-seg-main .chips .chip.boss  { color: var(--good); }
.tl-seg-main .topmods {
  font-size: 0.78rem; color: var(--muted);
  display: flex; gap: 0.9em; flex-wrap: wrap; justify-content: flex-end;
}
.tl-seg[data-family='Boss']         { border-left-color: var(--danger); }
.tl-seg[data-family='Invasion']     { border-left-color: var(--danger); }
.tl-seg[data-family='Weather']      { border-left-color: var(--amber); }
.tl-seg[data-family='Hardmode']     { border-left-color: var(--amber); }
.tl-seg[data-family='Subworld']     { border-left-color: var(--amber); }
.tl-seg[data-family='Combat']       { border-left-color: var(--spike); }
.tl-seg[data-family='DeathBracket'] { border-left-color: var(--muted); }
.tl-seg[data-family='UserBookmark'] { border-left-color: var(--accent); }
.tl-seg.promoted { background: rgba(73, 120, 168, 0.05); }
.tl-seg-detail {
  margin-top: 0.6rem; padding-top: 0.6rem;
  border-top: 1px dashed var(--border-soft);
  display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 0.4rem 1rem;
  font-family: var(--mono); font-size: 0.82rem;
  color: var(--muted);
}
.tl-seg-detail .det-row { display: grid; grid-template-columns: 1fr auto; gap: 0.3em; }
.tl-seg-detail .det-row .v { color: var(--text); }
.tl-seg-detail.hidden { display: none; }

@media (max-width: 900px) {
  .tl-seg-main {
    grid-template-columns: minmax(0, 1.4fr) 5em 5em minmax(0, 1fr);
    grid-template-rows: auto auto;
  }
  .tl-seg-main .topmods { grid-column: 1 / -1; justify-content: flex-start; }
}
";
}
