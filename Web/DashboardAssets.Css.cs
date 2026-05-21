#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    /// <summary>
    /// Dashboard CSS. All-relative layout (Grid + Flex). Designed for
    /// 1080p+ but reflows down to ~800px before things break. JetBrains
    /// Mono for tabular data, Inter for chrome.
    /// </summary>
    public const string Css = @"
:root {
  --bg-deep:      #07090e;
  --bg-page:      #0a0e14;
  --panel:        #0d1117;
  --panel-2:      #0e131b;
  --surface:      #161b22;
  --surface-2:    #1c2230;
  --header:       #11161f;
  --border:       #1f2329;
  --border-soft:  #161a21;
  --hover:        #1a2030;

  --text:         #c5c8ce;
  --text-bright:  #ffffff;
  --muted:        #6e7480;
  --dim:          #4a4f5a;

  --good:         #95d4a3;
  --good-bar:     #6ec07e;
  --amber:        #f5b342;
  --danger:       #f47174;
  --accent:       #79c0ff;
  --accent-soft:  rgba(121, 192, 255, 0.10);
  --accent-line:  rgba(121, 192, 255, 0.40);

  --cpu:          #6ec07e;
  --alloc:        #c39ad8;
  --spike:        #f5b342;

  --mono: 'JetBrains Mono', 'SFMono-Regular', 'Menlo', monospace;
  --ui:   'Inter', -apple-system, 'Segoe UI', sans-serif;
}

* { box-sizing: border-box; }

html, body {
  margin: 0; padding: 0;
  background: var(--bg-deep);
  color: var(--text);
  font-family: var(--ui);
  font-size: 14px;
  line-height: 1.45;
  height: 100vh;
  overflow: hidden;
}

.hidden { display: none !important; }

/* ============================================================== APP SHELL */
.app {
  display: grid;
  grid-template-rows: auto auto 1fr auto;
  height: 100vh;
  width: 100vw;
  background: linear-gradient(180deg, var(--bg-page) 0%, var(--bg-deep) 70%);
}

/* ============================================================== TOP BAR */
.topbar {
  display: grid;
  grid-template-columns: auto auto 1fr;
  gap: 1.4rem;
  align-items: center;
  padding: 0.7rem 1.2rem;
  background: linear-gradient(180deg, var(--header) 0%, var(--panel) 100%);
  border-bottom: 1px solid var(--border);
}
.brand { display: flex; align-items: center; gap: 0.7rem; min-width: 0; }
.brand-mark {
  width: 0.7rem; height: 1.2rem;
  background: linear-gradient(180deg, var(--accent) 0%, transparent 100%);
  display: inline-block;
}
.brand-name {
  font-family: var(--ui); font-weight: 700; font-size: 1.05rem;
  color: var(--text-bright); letter-spacing: 0.02em;
}
.brand-version {
  font-family: var(--mono); font-size: 0.75rem;
  color: var(--muted); padding: 0.05em 0.45em;
  background: var(--surface); border: 1px solid var(--border-soft);
  border-radius: 3px;
}

.live {
  display: flex; align-items: center; gap: 0.5em;
  font-family: var(--mono); font-size: 0.85rem;
}
.live-dot {
  width: 0.55em; height: 0.55em; border-radius: 50%;
  background: var(--muted);
  box-shadow: 0 0 0 0 transparent;
  transition: background 0.2s;
}
.live-dot.ok {
  background: var(--good);
  box-shadow: 0 0 8px rgba(149, 212, 163, 0.6);
  animation: pulse 1.6s ease-in-out infinite;
}
.live-dot.err {
  background: var(--danger);
  box-shadow: 0 0 8px rgba(244, 113, 116, 0.5);
}
@keyframes pulse {
  0%, 100% { box-shadow: 0 0 8px rgba(149, 212, 163, 0.6); }
  50%      { box-shadow: 0 0 14px rgba(149, 212, 163, 0.95); }
}
.live-text { color: var(--muted); }

.topstats {
  display: flex;
  justify-content: flex-end;
  gap: 1.6rem;
  flex-wrap: wrap;
}
.topstat { display: flex; flex-direction: column; align-items: flex-end; line-height: 1.1; }
.topstat .k {
  font-family: var(--ui); font-size: 0.7rem;
  color: var(--muted); text-transform: uppercase; letter-spacing: 0.07em;
}
.topstat .v {
  font-family: var(--mono); font-size: 1.05rem;
  color: var(--text-bright); font-weight: 500;
}

/* ============================================================== TABS */
.tabs {
  display: flex;
  gap: 0;
  padding: 0 1.2rem;
  background: var(--panel);
  border-bottom: 1px solid var(--border);
  overflow-x: auto;
}
.tabs::-webkit-scrollbar { display: none; }
.tab {
  font-family: var(--ui); font-size: 0.85rem; font-weight: 500;
  background: transparent; border: 0;
  color: var(--muted);
  padding: 0.75rem 1.2rem 0.7rem;
  cursor: pointer;
  border-bottom: 2px solid transparent;
  letter-spacing: 0.03em;
  white-space: nowrap;
  transition: color 0.15s, border-color 0.15s, background 0.15s;
}
.tab:hover { color: var(--text); background: rgba(255, 255, 255, 0.02); }
.tab.active {
  color: var(--accent);
  border-bottom-color: var(--accent);
}

/* ============================================================== CONTENT */
.content {
  min-height: 0;
  overflow-y: auto;
  padding: 1rem 1.2rem 1.4rem;
}
.tab-pane { display: none; height: 100%; }
.tab-pane.active { display: block; }

/* ============================================================== EMPTY STATE */
.empty {
  display: flex; align-items: center; justify-content: center;
  height: 100%;
  padding: 2rem;
}
.empty-inner {
  max-width: 38rem;
  background: var(--panel);
  border: 1px dashed var(--border);
  border-radius: 6px;
  padding: 2rem 2.4rem;
  text-align: center;
}
.empty-inner h2 {
  font-family: var(--ui); font-weight: 600; font-size: 1.3rem; margin: 0 0 0.5rem;
  color: var(--text-bright);
}
.empty-inner p { color: var(--muted); margin: 0.6em 0; }
.empty-inner p.hint {
  font-size: 0.85em;
  background: var(--surface); border: 1px solid var(--border-soft);
  border-radius: 3px; padding: 0.6em 0.9em;
  margin-top: 1.2em;
  color: var(--text);
}

/* ============================================================== PANELS */
.panel {
  background: linear-gradient(180deg, var(--panel) 0%, var(--panel-2) 100%);
  border: 1px solid var(--border);
  border-radius: 5px;
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}
.panel-h {
  display: flex; align-items: baseline; justify-content: space-between;
  padding: 0.55rem 0.9rem;
  border-bottom: 1px solid var(--border-soft);
  flex: 0 0 auto;
}
.panel-title {
  font-family: var(--ui); font-size: 0.78rem; font-weight: 600;
  color: var(--muted); text-transform: uppercase; letter-spacing: 0.07em;
}
.panel-sub {
  font-family: var(--mono); font-size: 0.78rem; color: var(--dim);
}

/* ========================================== NOW: mission-control grid */
.grid-now {
  display: grid;
  grid-template-areas:
    'chart chart events'
    'mods now events';
  grid-template-columns: minmax(0, 2fr) minmax(0, 1fr) minmax(0, 1fr);
  grid-template-rows: minmax(180px, 1fr) minmax(0, 2fr);
  gap: 0.75rem;
  height: 100%;
  min-height: 460px;
}

@media (max-width: 1100px) {
  .grid-now {
    grid-template-areas:
      'chart chart'
      'now mods'
      'events events';
    grid-template-columns: 1fr 1fr;
    grid-template-rows: minmax(180px, auto) auto auto;
  }
}
@media (max-width: 700px) {
  .grid-now {
    grid-template-areas: 'chart' 'now' 'mods' 'events';
    grid-template-columns: 1fr;
  }
}

/* ====== Frame chart hero ====== */
.panel-hero .chart-wrap {
  flex: 1 1 auto; min-height: 0;
  position: relative;
  display: flex; flex-direction: column;
  padding: 0.4rem 0.9rem 0.5rem;
  gap: 0.3rem;
}
.chart {
  width: 100%; flex: 1 1 auto;
  display: block;
  min-height: 100px;
}
.chart-axis {
  display: flex; justify-content: space-between;
  font-family: var(--mono); font-size: 0.7rem; color: var(--dim);
}

/* ====== Now playing list ====== */
.nowlist {
  padding: 0.4rem 0.5rem 0.5rem;
  display: flex; flex-direction: column; gap: 0.2rem;
  overflow-y: auto;
  flex: 1 1 auto; min-height: 0;
}
.nowlist .empty-line { color: var(--dim); padding: 0.4rem 0.4rem; font-style: italic; font-size: 0.85rem; }
.now-seg {
  display: grid;
  grid-template-columns: 0.25rem minmax(0, 1fr) auto;
  gap: 0.45rem;
  align-items: center;
  padding: 0.32rem 0.4rem;
  border-radius: 2px;
  font-family: var(--mono); font-size: 0.85rem;
}
.now-seg .swatch { height: 1rem; background: var(--good); border-radius: 1px; }
.now-seg .name { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--text); }
.now-seg .meta { color: var(--muted); font-size: 0.78rem; text-align: right; line-height: 1.2; }
.now-seg .meta .mod { color: var(--good); }
.now-seg[data-family='Boss']     .swatch { background: var(--danger); }
.now-seg[data-family='Invasion'] .swatch { background: var(--danger); }
.now-seg[data-family='Weather']  .swatch { background: var(--amber); }
.now-seg[data-family='Hardmode'] .swatch { background: var(--amber); }
.now-seg[data-family='Subworld'] .swatch { background: var(--amber); }
.now-seg[data-family='Combat']   .swatch { background: var(--spike); }
.now-seg[data-family='DeathBracket'] .swatch { background: var(--muted); }
.now-seg[data-family='UserBookmark'] .swatch { background: var(--accent); }

/* ====== Live mod list ====== */
.modlist {
  padding: 0.45rem 0.7rem;
  display: flex; flex-direction: column; gap: 0.25rem;
  overflow-y: auto;
  flex: 1 1 auto; min-height: 0;
}
.modrow {
  display: grid;
  grid-template-columns: 1.5em minmax(0, 0.9fr) minmax(0, 2.5fr) 4.5em;
  gap: 0.4rem;
  align-items: center;
  font-family: var(--mono); font-size: 0.85rem;
  padding: 0.05rem 0;
}
.modrow .rank { color: var(--dim); text-align: right; font-size: 0.78rem; }
.modrow .name { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.modrow .bars {
  display: flex; gap: 1px; height: 0.55rem;
  background: var(--surface);
  border-radius: 1px;
  overflow: hidden;
}
.modrow .bars .b { height: 100%; }
.modrow .bars .b.cpu { background: var(--cpu); }
.modrow .ms { font-family: var(--mono); text-align: right; color: var(--text); }
.modrow .ms .u { color: var(--muted); margin-left: 0.2em; }

/* ====== Events feed ====== */
.events {
  padding: 0.45rem 0.5rem;
  display: flex; flex-direction: column; gap: 0.15rem;
  overflow-y: auto;
  flex: 1 1 auto; min-height: 0;
}
.event {
  display: grid;
  grid-template-columns: 1.4em minmax(0, 1fr) auto;
  gap: 0.4rem;
  align-items: baseline;
  padding: 0.18rem 0.35rem;
  border-radius: 2px;
  font-family: var(--mono); font-size: 0.78rem;
}
.event:hover { background: var(--hover); }
.event .glyph { text-align: center; color: var(--muted); }
.event .what  { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--text); }
.event .when  { color: var(--dim); font-size: 0.72rem; }
.event[data-kind='boss-kill'] .glyph { color: var(--good); }
.event[data-kind='death']     .glyph { color: var(--danger); }
.event[data-kind='spike']     .glyph { color: var(--amber); }
.event[data-kind='stall']     .glyph { color: var(--danger); }
.event[data-kind='segment']   .glyph { color: var(--muted); }

/* =================================================== MODS TAB (full) */
.modtable {
  display: flex; flex-direction: column; gap: 0.2rem;
  padding: 0.5rem 0.9rem 0.9rem;
}
.modtable .modrow.big {
  grid-template-columns: 2em minmax(0, 1.2fr) minmax(0, 4fr) 5em 5em;
  font-size: 0.95rem;
  padding: 0.25rem 0;
}
.modtable .modrow.big .name { color: var(--text); }
.modtable .modrow.big .bars { height: 0.8rem; }
.modtable .modrow.big .name strong { color: var(--text-bright); font-weight: 600; }
.modtable .modrow.big.outlier { background: rgba(245, 179, 66, 0.06); border-left: 2px solid var(--amber); padding-left: 0.35rem; }
.modtable .modrow.big.top { background: rgba(149, 212, 163, 0.05); }

.segctl {
  display: inline-flex; border: 1px solid var(--border); border-radius: 3px; overflow: hidden;
  background: var(--header);
}
.segctl button {
  font: inherit; background: transparent; color: var(--muted);
  border: 0; border-right: 1px solid var(--border-soft);
  padding: 0.15em 0.7em; cursor: pointer;
}
.segctl button:last-child { border-right: 0; }
.segctl button.active { background: var(--accent-soft); color: var(--accent); }

/* =================================================== TIMELINE TAB */
.timeline { padding: 0.45rem 0.9rem 1rem; display: flex; flex-direction: column; gap: 0.4rem; }
.tl-seg {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-left: 3px solid var(--good);
  border-radius: 3px;
  padding: 0.55rem 0.85rem;
  display: grid;
  grid-template-columns: minmax(0, 1.4fr) 5em 5em 5em 5em minmax(0, 1.4fr);
  gap: 0.6rem 1rem;
  align-items: center;
  font-family: var(--mono); font-size: 0.85rem;
}
.tl-seg .name { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--text); }
.tl-seg .dur, .tl-seg .mspt { text-align: right; color: var(--text); }
.tl-seg .badge { text-align: right; color: var(--muted); font-size: 0.78rem; }
.tl-seg .chips { display: flex; gap: 0.45em; justify-content: flex-end; font-size: 0.78rem; }
.tl-seg .chips .chip.death { color: var(--danger); }
.tl-seg .chips .chip.spike { color: var(--amber); }
.tl-seg .chips .chip.stall { color: var(--danger); }
.tl-seg .chips .chip.boss  { color: var(--good); }
.tl-seg .topmods {
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

@media (max-width: 900px) {
  .tl-seg {
    grid-template-columns: minmax(0, 1.4fr) 5em 5em minmax(0, 1fr);
    grid-template-rows: auto auto;
  }
  .tl-seg .topmods { grid-column: 1 / -1; justify-content: flex-start; }
}

/* =================================================== SPIKES TAB */
.spikes { padding: 0.45rem 0.9rem 1rem; display: flex; flex-direction: column; gap: 0.5rem; }
.spike-row {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-left: 3px solid var(--amber);
  border-radius: 3px;
  padding: 0.6rem 0.85rem;
}
.spike-row .head {
  display: flex; justify-content: space-between; align-items: baseline; gap: 1rem;
  font-family: var(--mono); font-size: 0.95rem;
  margin-bottom: 0.4rem;
}
.spike-row .head .worst { color: var(--amber); font-weight: 700; }
.spike-row .head .baseline { color: var(--muted); font-size: 0.82rem; }
.spike-row .contribs {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 0.25rem 1rem;
  font-family: var(--mono); font-size: 0.85rem;
}
.spike-row .contribs .c {
  display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 0.5em;
}
.spike-row .contribs .c .name { color: var(--text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.spike-row .contribs .c .ms { color: var(--muted); }
.spike-row.warming { border-left-color: var(--muted); opacity: 0.7; }

/* =================================================== INSIGHTS TAB */
.insights { padding: 0.45rem 0.9rem 1rem; display: flex; flex-direction: column; gap: 0.5rem; }
.insight {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-left: 3px solid var(--good);
  border-radius: 3px;
  padding: 0.65rem 0.9rem;
}
.insight .head {
  display: flex; justify-content: space-between; align-items: baseline;
  font-family: var(--mono); font-size: 0.78rem; color: var(--muted);
  margin-bottom: 0.25rem;
}
.insight .head .pattern { color: var(--good); letter-spacing: 0.06em; text-transform: uppercase; }
.insight .head .conf { color: var(--muted); }
.insight .body { font-size: 0.95rem; color: var(--text); }
.insight .footer { font-size: 0.78rem; color: var(--dim); margin-top: 0.35rem; }
.insight.warn  { border-left-color: var(--amber); }
.insight.warn  .head .pattern { color: var(--amber); }
.insight.alert { border-left-color: var(--danger); }
.insight.alert .head .pattern { color: var(--danger); }

/* =================================================== SELF TAB */
.self-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 0.75rem;
}
.self-body { padding: 0.5rem 0.9rem 0.9rem; }
.self-row {
  display: grid; grid-template-columns: 1fr auto;
  align-items: baseline; padding: 0.18rem 0;
  font-family: var(--mono); font-size: 0.9rem;
  border-bottom: 1px solid var(--border-soft);
}
.self-row:last-child { border-bottom: 0; }
.self-row .k { color: var(--muted); }
.self-row .v { color: var(--text); text-align: right; }
.self-row .v.good { color: var(--good); }
.self-row .v.warn { color: var(--amber); }
.self-row .v.bad  { color: var(--danger); }

/* =================================================== FOOTER */
.footstrip {
  display: flex; align-items: center; gap: 1.2rem;
  padding: 0.4rem 1.2rem;
  background: var(--header);
  border-top: 1px solid var(--border);
  font-family: var(--mono); font-size: 0.72rem;
  color: var(--dim);
}
.footstrip .foot-spacer { flex: 1; }

/* small scrollbar */
::-webkit-scrollbar { width: 6px; height: 6px; }
::-webkit-scrollbar-track { background: transparent; }
::-webkit-scrollbar-thumb { background: var(--border); border-radius: 3px; }
::-webkit-scrollbar-thumb:hover { background: var(--surface-2); }
";
}
