#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string CssMods = @"
/* ============== Mod table with cascading tree + sparkline + alloc ====== */
.panel-wide { grid-area: mods; }

.modtable-head, .modrow {
  display: grid;
  grid-template-columns: 2em minmax(0, 1.4fr) minmax(0, 2.2fr) 4.5em 4.5em 4.5em 5em;
  align-items: center; gap: 0.5rem;
  padding: 0.25rem 0.9rem;
  font-family: var(--mono); font-size: 0.88rem;
}
.modtable-head {
  background: var(--header);
  border-bottom: 1px solid var(--border-soft);
  padding-top: 0.4rem; padding-bottom: 0.4rem;
}
.modtable-head .mh {
  font-family: var(--ui); font-size: 0.7rem; color: var(--muted);
  text-transform: uppercase; letter-spacing: 0.07em;
}
.modtable-head .mh.rank, .modtable-head .mh.num { text-align: right; }
.modtable-head .mh.bar { text-align: left; padding-left: 0.4rem; }
.modtable-head .mh.trend { text-align: center; }
.modtable-head .mh [data-explain] { cursor: help; }

.modtable {
  display: flex; flex-direction: column;
  max-height: 32rem; overflow-y: auto;
  padding-bottom: 0.4rem;
}
.modrow {
  border-bottom: 1px solid var(--border-soft);
  transition: background 0.12s, box-shadow 0.12s;
  cursor: pointer;
  user-select: none;
}
.modrow:hover {
  background: var(--hover);
  box-shadow: inset 2px 0 0 var(--accent);
}
.modrow:hover .twirl { color: var(--accent); }
/* Mod name keeps text-select on so users can copy mod names if needed,
   but the click handler on the parent .modrow still fires because the
   parent has the listener and child clicks bubble up. */
.modrow .modname { user-select: text; }
.modrow .rank { color: var(--dim); text-align: right; font-size: 0.78rem; }
.modrow .name {
  display: flex; align-items: center; gap: 0.4em; min-width: 0;
}
.modrow .name .twirl {
  color: var(--muted); font-size: 0.8em; transition: transform 0.15s;
  width: 0.8em; text-align: center;
}
.modrow.open .name .twirl { transform: rotate(90deg); }
.modrow .name .modname {
  min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
  color: var(--text); cursor: pointer;
}
.modrow .name .modname:hover { color: var(--accent); }
.modrow.is-top .name .modname { color: var(--text-bright); font-weight: 500; }
.modrow.outlier { background: rgba(245, 179, 66, 0.04); border-left: 2px solid var(--amber); }
.modrow .bar {
  height: 0.6rem; background: var(--surface);
  border-radius: 1px; overflow: hidden; position: relative;
}
.modrow .bar > span {
  display: block; height: 100%;
  background: var(--cpu); transition: width 0.3s ease-out;
}
.modrow.outlier .bar > span { background: var(--amber); }
.modrow.severe .bar > span { background: var(--danger); }
.modrow .spark { height: 0.9rem; min-width: 0; }
.modrow .spark svg { width: 100%; height: 100%; display: block; }
.modrow .ms, .modrow .alloc {
  text-align: right; color: var(--text); font-family: var(--mono);
}
.modrow .ms .u, .modrow .alloc .u {
  color: var(--muted); margin-left: 0.18em; font-size: 0.85em;
}

/* Cascading detail rows (category + hook level) */
.mod-tree {
  background: rgba(0, 0, 0, 0.18);
  border-bottom: 1px solid var(--border-soft);
  padding: 0.3rem 0;
}
.cat-row, .hook-row {
  display: grid;
  grid-template-columns: 2em minmax(0, 1.4fr) minmax(0, 2.2fr) 4.5em 4.5em 4.5em 5em;
  align-items: center; gap: 0.5rem;
  padding: 0.18rem 0.9rem;
  font-family: var(--mono); font-size: 0.82rem;
}
.cat-row {
  color: var(--accent); border-left: 2px solid var(--accent-line); margin-left: 1.4rem;
  cursor: pointer; transition: background 0.12s, border-left-color 0.12s;
  user-select: none;
}
.cat-row:hover {
  background: var(--hover);
  border-left-color: var(--accent);
  box-shadow: inset 2px 0 0 var(--accent);
}
.cat-row .twirl { color: var(--muted); font-size: 0.8em; width: 0.8em; text-align: center; transition: transform 0.15s, color 0.12s; }
.cat-row:hover .twirl { color: var(--accent); }
.cat-row.open .twirl { transform: rotate(90deg); color: var(--accent); }
.cat-row .name { display: flex; gap: 0.4em; align-items: center; min-width: 0; }
.hook-row { color: var(--muted); margin-left: 3rem; font-size: 0.78rem; cursor: default; }
.hook-row:hover { background: var(--hover); color: var(--text); }
.hook-row .name { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; user-select: text; }
.hook-row .bar > span { background: var(--accent); }
";
}
