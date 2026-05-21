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
/* Palette: command-center / Palantir-style. Near-black backgrounds,
   restrained electric-blue accent, desaturated semantic colours so
   alerts pop without the whole UI feeling like a Christmas tree. */
:root {
  --bg-deep:      #07090c;
  --bg-page:      #0a0d12;
  --panel:        #0d1117;
  --panel-2:      #0a0d12;
  --surface:      #11161c;
  --surface-2:    #161b22;
  --header:       #0a0d12;
  --border:       #1b2028;
  --border-soft:  #14191f;
  --hover:        #161c24;

  --text:         #d6dae0;
  --text-bright:  #f0f3f8;
  --muted:        #6a727f;
  --dim:          #3d434c;

  /* Restrained, desaturated semantic accents */
  --good:         #4f9d6a;   /* muted green */
  --good-bar:     #4b9c8c;   /* dim teal */
  --amber:        #b88a25;   /* dim amber */
  --orange:       #c97f3c;   /* burnt orange */
  --danger:       #b94e58;   /* muted red */
  --magenta:      #8367a3;   /* dim magenta */
  --purple:       #6e5d96;   /* dim purple */
  --accent:       #4a9eff;   /* THE single signature electric blue */
  --cyan:         #4ab8c2;
  --accent-soft:  rgba(74, 158, 255, 0.08);
  --accent-line:  rgba(74, 158, 255, 0.30);

  /* Series colours — used everywhere a value has a single semantic axis */
  --cpu:          #4f9d6a;
  --alloc:        #6e5d96;
  --spike:        #c97f3c;
  --stall:        #b94e58;
  --gc:           #6e5d96;

  /* Performance gradient (good → bad), mod bars + heatmap cells */
  --perf-0: #4f9d6a;   /* healthy green */
  --perf-1: #6aa3a8;   /* teal */
  --perf-2: #b88a25;   /* amber */
  --perf-3: #c97f3c;   /* orange */
  --perf-4: #b94e58;   /* red */

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
.muted  { color: var(--muted); }

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
  box-shadow: 0 0 8px rgba(247, 118, 142, 0.55);
}
.live-dot.paused {
  background: var(--amber);
  box-shadow: 0 0 8px rgba(224, 175, 104, 0.55);
}
.live-dot.idle {
  background: var(--dim);
}
@keyframes pulse {
  0%, 100% { box-shadow: 0 0 8px rgba(79, 157, 106, 0.45); }
  50%      { box-shadow: 0 0 14px rgba(79, 157, 106, 0.85); }
}
.live-text { color: var(--muted); }

.topstats {
  display: flex; justify-content: flex-end;
  gap: 1.6rem; flex-wrap: wrap;
}
.topstat { display: flex; flex-direction: column; align-items: flex-end; line-height: 1.1; cursor: help; }
.topstat .k { font-family: var(--ui); font-size: 0.7rem; color: var(--muted); text-transform: uppercase; letter-spacing: 0.07em; }
.topstat .v { font-family: var(--mono); font-size: 1.05rem; color: var(--text-bright); font-weight: 500; transition: color 0.2s; }
.topstat .v.flash { animation: flash 0.4s ease-out; }
@keyframes flash {
  0%   { color: var(--accent); }
  100% { color: var(--text-bright); }
}

/* ============================================================== TABS */
.tabs {
  display: flex; gap: 0; padding: 0 1.2rem;
  background: var(--panel); border-bottom: 1px solid var(--border);
}
.tab {
  font-family: var(--ui); font-size: 0.85rem; font-weight: 500;
  background: transparent; border: 0; color: var(--muted);
  padding: 0.75rem 1.2rem 0.7rem;
  cursor: pointer; border-bottom: 2px solid transparent;
  letter-spacing: 0.03em; white-space: nowrap;
  display: flex; align-items: center; gap: 0.5em;
  transition: color 0.15s, border-color 0.15s, background 0.15s;
}
.tab:hover { color: var(--text); background: rgba(255, 255, 255, 0.02); }
.tab.active { color: var(--accent); border-bottom-color: var(--accent); }
.tab .ki {
  font-family: var(--mono); font-size: 0.65rem;
  padding: 0.1em 0.4em; border-radius: 2px;
  background: var(--surface); color: var(--dim);
  border: 1px solid var(--border-soft);
}
.tab.active .ki { color: var(--accent); border-color: var(--accent-line); background: var(--accent-soft); }

/* ============================================================== CONTENT */
.content { min-height: 0; overflow-y: auto; padding: 1rem 1.2rem 4rem; }
.tab-pane { display: none; }
.tab-pane.active { display: block; }

/* ============================================================== OVERLAYS */
.overlay-state {
  position: fixed; inset: 0;
  display: flex; align-items: center; justify-content: center;
  background: rgba(7, 9, 14, 0.92);
  z-index: 50; padding: 2rem;
}
.overlay-inner {
  max-width: 38rem;
  background: var(--panel);
  border: 1px dashed var(--border);
  border-radius: 6px;
  padding: 2rem 2.4rem; text-align: center;
}
.overlay-inner h2 {
  font-weight: 600; font-size: 1.3rem; margin: 0 0 0.5rem; color: var(--text-bright);
}
.overlay-inner p { color: var(--muted); margin: 0.6em 0; }
.overlay-inner p.hint {
  font-size: 0.85em; background: var(--surface); border: 1px solid var(--border-soft);
  border-radius: 3px; padding: 0.6em 0.9em; margin-top: 1.2em; color: var(--text);
}
#disconnected .overlay-inner { border-color: var(--danger); }

/* ============================================================== PANELS */
.panel {
  background: linear-gradient(180deg, var(--panel) 0%, var(--panel-2) 100%);
  border: 1px solid var(--border);
  border-radius: 5px;
  display: flex; flex-direction: column;
  min-width: 0; min-height: 0;
}
.panel-h {
  display: flex; align-items: baseline; justify-content: space-between;
  padding: 0.55rem 0.9rem;
  border-bottom: 1px solid var(--border-soft);
  flex: 0 0 auto; gap: 0.8rem;
}
.panel-title { font-family: var(--ui); font-size: 0.78rem; font-weight: 600; color: var(--muted); text-transform: uppercase; letter-spacing: 0.07em; }
.panel-sub   { font-family: var(--mono); font-size: 0.78rem; color: var(--dim); cursor: help; }
.panel-actions { display: flex; gap: 0.6rem; align-items: center; }

/* ====== Segmented controls ====== */
.segctl {
  display: inline-flex; border: 1px solid var(--border); border-radius: 3px;
  overflow: hidden; background: var(--header);
}
.segctl button {
  font: inherit; background: transparent; color: var(--muted);
  border: 0; border-right: 1px solid var(--border-soft);
  padding: 0.18em 0.7em; cursor: pointer; font-size: 0.78rem;
}
.segctl button:last-child { border-right: 0; }
.segctl button:hover { color: var(--text); }
.segctl button.active { background: var(--accent-soft); color: var(--accent); }

.filter-input {
  background: var(--header); color: var(--text);
  border: 1px solid var(--border); border-radius: 3px;
  padding: 0.18em 0.55em; font-family: var(--mono); font-size: 0.78rem;
  width: 12rem; max-width: 30%;
}
.filter-input:focus { outline: none; border-color: var(--accent-line); }

/* ========================================== SUMMARY: mission-control grid */
.grid-summary {
  display: grid;
  grid-template-areas:
    'kpi    kpi     kpi'
    'chart  chart   donut'
    'trends trends  donut'
    'heatmap heatmap heatmap'
    'now    events  events'
    'mods   mods    mods';
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) minmax(0, 1fr);
  grid-template-rows: auto minmax(170px, 1fr) auto auto auto auto;
  gap: 0.75rem;
}

@media (max-width: 1100px) {
  .grid-summary {
    grid-template-areas:
      'kpi    kpi'
      'chart  chart'
      'donut  trends'
      'heatmap heatmap'
      'now    events'
      'mods   mods';
    grid-template-columns: 1fr 1fr;
  }
}
@media (max-width: 700px) {
  .grid-summary {
    grid-template-areas: 'kpi' 'chart' 'donut' 'trends' 'heatmap' 'now' 'events' 'mods';
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
.chart { width: 100%; flex: 1 1 auto; display: block; min-height: 130px; }
.chart-axis {
  display: flex; justify-content: space-between;
  font-family: var(--mono); font-size: 0.7rem; color: var(--dim);
}

/* ====== Donut chart ====== */
.donut-wrap {
  position: relative; padding: 0.6rem;
  display: flex; align-items: center; justify-content: center;
  flex: 0 0 auto;
}
.donut { width: 100%; max-width: 170px; aspect-ratio: 1; display: block; }
.donut-center {
  position: absolute; left: 50%; top: 50%; transform: translate(-50%, -55%);
  text-align: center; pointer-events: none;
  font-family: var(--ui);
}
.donut-center .dc-pct { display: block; font-size: 1.6rem; font-weight: 600; color: var(--text-bright); line-height: 1.05; }
.donut-center .dc-name { display: block; font-size: 0.75rem; color: var(--text); margin-top: 0.1em; }
.donut-center .dc-ms { display: block; font-family: var(--mono); font-size: 0.7rem; color: var(--muted); }
.donut-legend {
  padding: 0 0.9rem 0.7rem;
  display: flex; flex-direction: column; gap: 0.1rem;
  font-family: var(--mono); font-size: 0.78rem;
  flex: 1 1 auto; min-height: 0; overflow-y: auto;
}
.donut-legend .leg {
  display: grid; grid-template-columns: 0.6em minmax(0, 1fr) auto;
  gap: 0.4em; align-items: center;
}
.donut-legend .leg .sw { height: 0.6em; }
.donut-legend .leg .nm { color: var(--text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.donut-legend .leg .pc { color: var(--muted); }

/* ====== Sparkline trend rows ====== */
.trends { padding: 0.4rem 0.6rem 0.5rem; display: flex; flex-direction: column; gap: 0.25rem; }
.trend-row {
  display: grid; grid-template-columns: 3rem minmax(0, 1fr);
  align-items: center; gap: 0.5rem;
}
.tr-k { font-family: var(--mono); font-size: 0.78rem; color: var(--muted); }
.tr-spark { width: 100%; height: 1.8rem; display: block; }

/* ====== Now playing ====== */
.nowlist {
  padding: 0.4rem 0.5rem 0.5rem;
  display: flex; flex-direction: column; gap: 0.2rem;
  overflow-y: auto; flex: 1 1 auto; min-height: 100px;
}
.nowlist .empty-line { color: var(--dim); padding: 0.4rem 0.4rem; font-style: italic; font-size: 0.85rem; }
.now-seg {
  display: grid; grid-template-columns: 0.25rem minmax(0, 1fr) auto;
  gap: 0.45rem; align-items: center;
  padding: 0.32rem 0.4rem; border-radius: 2px;
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

/* ====== Events feed ====== */
.events {
  padding: 0.45rem 0.5rem;
  display: flex; flex-direction: column; gap: 0.15rem;
  overflow-y: auto; flex: 1 1 auto; min-height: 100px;
}
.event {
  display: grid; grid-template-columns: 1.4em minmax(0, 1fr) auto;
  gap: 0.4rem; align-items: baseline;
  padding: 0.18rem 0.35rem; border-radius: 2px;
  font-family: var(--mono); font-size: 0.78rem;
  cursor: pointer;
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

/* =================================================== INSIGHTS */
.insights { padding: 0.45rem 0.9rem 1rem; display: flex; flex-direction: column; gap: 0.5rem; }
.insight {
  background: var(--panel-2);
  border: 1px solid var(--border-soft);
  border-left: 3px solid var(--good);
  border-radius: 3px;
  padding: 0.65rem 0.9rem;
  cursor: pointer;
}
.insight:hover { background: var(--hover); }
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

.insight-empty {
  padding: 2.5rem 1.5rem;
  text-align: center;
  background: linear-gradient(180deg, var(--panel) 0%, var(--panel-2) 100%);
  border: 1px dashed var(--border);
  border-radius: 5px;
  margin: 0.5rem;
}
.insight-empty h3 {
  font-family: var(--ui); font-weight: 600; font-size: 1.05rem;
  color: var(--text); margin: 0 0 0.6rem; letter-spacing: 0.02em;
}
.insight-empty p {
  color: var(--muted); font-size: 0.88rem; margin: 0.4rem auto;
  max-width: 48ch; line-height: 1.5;
}
.insight-empty p.muted { color: var(--dim); font-size: 0.82rem; }

/* =================================================== SELF TAB */
.self-layout {
  display: grid;
  grid-template-columns: minmax(0, 1.5fr) minmax(0, 1fr);
  gap: 0.75rem;
  grid-auto-flow: dense;
}
.self-hero { grid-column: 1 / -1; }
@media (max-width: 900px) { .self-layout { grid-template-columns: 1fr; } }

.hero-body {
  display: grid; grid-template-columns: 18rem 1fr; gap: 1rem;
  padding: 1rem 1.2rem; align-items: center;
}
.gauge { display: block; width: 100%; max-width: 16rem; }
.gauge svg { width: 100%; height: auto; }
.hero-stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 0.5rem 1rem; }
.hero-stat { display: flex; flex-direction: column; gap: 0.1rem; }
.hero-stat .k { font-family: var(--ui); font-size: 0.72rem; color: var(--muted); text-transform: uppercase; letter-spacing: 0.07em; }
.hero-stat .v { font-family: var(--mono); font-size: 1.2rem; color: var(--text-bright); }
.hero-stat .v.good { color: var(--good); }
.hero-stat .v.warn { color: var(--amber); }
.hero-stat .v.bad  { color: var(--danger); }
@media (max-width: 700px) { .hero-body { grid-template-columns: 1fr; } }

.self-body { padding: 0.6rem 0.9rem 0.5rem; }
.self-row {
  display: grid; grid-template-columns: 1fr auto;
  align-items: baseline; padding: 0.18rem 0;
  font-family: var(--mono); font-size: 0.88rem;
  border-bottom: 1px solid var(--border-soft);
}
.self-row:last-child { border-bottom: 0; }
.self-row .k { color: var(--muted); }
.self-row .v { color: var(--text); text-align: right; }
.self-row .v.good { color: var(--good); }
.self-row .v.warn { color: var(--amber); }
.self-row .v.bad  { color: var(--danger); }

.footprint-bar, .split-bar {
  margin: 0.4rem 0.9rem 0.9rem;
  height: 0.65rem; background: var(--surface);
  border-radius: 1px; display: flex; overflow: hidden;
}
.footprint-bar > span, .split-bar > span { display: block; height: 100%; }

.panel-wide { grid-column: 1 / -1; }
.hookdist {
  padding: 0.5rem 0.9rem 0.9rem;
  display: flex; flex-direction: column; gap: 0.25rem;
}
.hd-row {
  display: grid;
  grid-template-columns: 2em minmax(0, 1.4fr) minmax(0, 3fr) 7.5em 6em;
  gap: 0.6rem; align-items: center;
  font-family: var(--mono); font-size: 0.85rem;
  white-space: nowrap;
  padding: 0.18rem 0;
}
.hd-row .rk { color: var(--dim); text-align: right; font-size: 0.78rem; }
.hd-row .nm { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--text); }
.hd-row .bar { height: 0.55rem; background: var(--surface); border-radius: 1px; overflow: hidden; min-width: 0; }
.hd-row .bar > span { display: block; height: 100%; background: var(--accent); }
.hd-row .n { color: var(--text); text-align: right; white-space: nowrap; overflow: hidden; }
.hd-row .mb { color: var(--muted); text-align: right; font-size: 0.78rem; white-space: nowrap; overflow: hidden; }

/* =================================================== MOD CARD (slide-in) */
.modcard {
  position: fixed; top: 0; right: 0; bottom: 0;
  width: 28rem; max-width: 95vw;
  background: linear-gradient(180deg, var(--panel) 0%, var(--bg-page) 100%);
  border-left: 1px solid var(--border);
  z-index: 40; display: flex; flex-direction: column;
  transform: translateX(0); transition: transform 0.2s ease-out;
  box-shadow: -8px 0 24px rgba(0, 0, 0, 0.4);
}
.modcard.hidden { transform: translateX(100%); display: flex !important; pointer-events: none; }
.mc-h {
  display: grid; grid-template-columns: auto 1fr auto;
  gap: 0.8rem; align-items: center;
  padding: 0.9rem 1.1rem; border-bottom: 1px solid var(--border);
}
.mc-back {
  cursor: pointer; color: var(--muted); font-size: 1.5rem;
  width: 1.2em; height: 1.2em; display: flex; align-items: center; justify-content: center;
  border-radius: 3px; line-height: 1;
}
.mc-back:hover { color: var(--text); background: var(--hover); }
.mc-name { font-family: var(--ui); font-weight: 600; font-size: 1.1rem; color: var(--text-bright); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.mc-rank { font-family: var(--mono); font-size: 0.78rem; color: var(--muted); }
.mc-body { flex: 1; overflow-y: auto; padding: 1rem 1.1rem 2rem; }
.mc-section { margin-bottom: 1.2rem; }
.mc-section h3 {
  font-family: var(--ui); font-size: 0.75rem; font-weight: 600;
  color: var(--muted); text-transform: uppercase; letter-spacing: 0.07em;
  margin: 0 0 0.4rem;
}
.mc-stat-grid {
  display: grid; grid-template-columns: 1fr 1fr; gap: 0.5rem;
}
.mc-stat {
  background: var(--surface); border: 1px solid var(--border-soft);
  border-radius: 3px; padding: 0.55rem 0.7rem;
  display: flex; flex-direction: column; gap: 0.15rem;
}
.mc-stat .k { font-size: 0.7rem; color: var(--muted); text-transform: uppercase; letter-spacing: 0.06em; }
.mc-stat .v { font-family: var(--mono); font-size: 1.15rem; color: var(--text); }
.mc-stat .v.big { font-size: 1.5rem; color: var(--text-bright); }
.mc-stat .v.accent { color: var(--accent); }
.mc-stat .v.good { color: var(--good); }
.mc-stat .v.warn { color: var(--amber); }
.mc-stat .sub { font-size: 0.7rem; color: var(--dim); }

.mc-callout {
  background: rgba(121, 192, 255, 0.06);
  border: 1px solid var(--accent-line);
  border-radius: 3px;
  padding: 0.7rem 0.85rem; margin: 0.6rem 0;
  font-size: 0.88rem;
}
.mc-callout strong { color: var(--accent); }

.mc-catlist { display: flex; flex-direction: column; gap: 0.2rem; }
.mc-cat-row {
  display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 2fr) 5em;
  gap: 0.5rem; align-items: center;
  font-family: var(--mono); font-size: 0.85rem;
}
.mc-cat-row .nm { color: var(--text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.mc-cat-row .br { height: 0.55rem; background: var(--surface); border-radius: 1px; overflow: hidden; }
.mc-cat-row .br > span { display: block; height: 100%; background: var(--cpu); }
.mc-cat-row .vl { color: var(--muted); text-align: right; }

/* =================================================== TOOLTIP */
.tooltip {
  position: fixed; z-index: 100;
  max-width: 22rem;
  background: #11161fee;
  border: 1px solid var(--accent-line);
  border-radius: 4px;
  padding: 0.6rem 0.8rem;
  font-family: var(--ui); font-size: 0.82rem;
  color: var(--text);
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4);
  pointer-events: none;
  transition: opacity 0.1s;
}
.tooltip strong { color: var(--accent); }
.tooltip .tip-title {
  font-weight: 600; color: var(--text-bright); margin-bottom: 0.3em;
  display: block;
}
.tooltip code {
  background: rgba(255,255,255,0.06); padding: 0.05em 0.3em;
  border-radius: 2px; font-family: var(--mono); font-size: 0.85em;
}

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

/* =================================================== KPI STRIP */
.kpi-strip {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 0.75rem;
  grid-area: kpi;
}
.kpi {
  background: linear-gradient(180deg, var(--panel) 0%, var(--panel-2) 100%);
  border: 1px solid var(--border);
  border-radius: 5px;
  padding: 0.7rem 0.95rem 0.6rem;
  display: flex; flex-direction: column;
  gap: 0.4rem;
  position: relative;
  overflow: hidden;
  min-height: 7rem;
}
.kpi-head {
  display: flex; justify-content: space-between; align-items: center;
}
.kpi .k {
  font-family: var(--ui); font-size: 0.7rem;
  color: var(--muted); text-transform: uppercase; letter-spacing: 0.08em;
}
.kpi-tag {
  font-family: var(--mono); font-size: 0.65rem;
  padding: 0.1em 0.45em; border-radius: 2px;
  background: var(--surface); color: var(--muted);
  letter-spacing: 0.05em; text-transform: uppercase;
}
.kpi-tag.good { color: var(--good); background: rgba(79, 157, 106, 0.10); }
.kpi-tag.warn { color: var(--amber); background: rgba(184, 138, 37, 0.10); }
.kpi-tag.orange { color: var(--orange); background: rgba(201, 127, 60, 0.10); }
.kpi-tag.bad  { color: var(--danger); background: rgba(185, 78, 88, 0.10); }
.kpi-hero {
  display: flex; align-items: baseline; gap: 0.4em;
  line-height: 1;
}
.kpi .v {
  font-family: var(--mono); font-size: 2rem; font-weight: 500;
  color: var(--text-bright); line-height: 1;
}
.kpi .v.good { color: var(--good); }
.kpi .v.warn { color: var(--amber); }
.kpi .v.orange { color: var(--orange); }
.kpi .v.bad  { color: var(--danger); }
.kpi-hero .v-suffix {
  font-family: var(--mono); font-size: 0.82rem;
  color: var(--dim);
}
.kpi-subs {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.2rem 1rem;
  font-family: var(--mono); font-size: 0.74rem;
}
.kpi-sub {
  display: grid; grid-template-columns: 1fr auto; gap: 0.4em;
  align-items: baseline;
}
.kpi-sub .k {
  font-family: var(--mono); font-size: 0.7rem;
  color: var(--dim); text-transform: none; letter-spacing: 0;
}
.kpi-sub .v {
  font-family: var(--mono); font-size: 0.78rem;
  color: var(--text); font-weight: 400;
}
.kpi-spark {
  height: 1.2rem;
  display: block; width: 100%;
}

/* =================================================== SESSION HEATMAP */
.heatmap-panel { grid-area: heatmap; }
.heatmap-wrap {
  padding: 0.7rem 0.95rem 0.95rem;
  display: flex; flex-direction: column; gap: 0.5rem;
}
.heatmap-grid {
  display: grid;
  /* Columns adapt to available width: every cell is at least 12px wide,
     fluid above that. The Y axis is implicit (1 row), since per the user's
     intent each cell is one minute of play.  */
  grid-template-columns: repeat(auto-fill, minmax(1.1rem, 1fr));
  gap: 2px;
}
.hm-cell {
  aspect-ratio: 1;
  border-radius: 2px;
  background: var(--surface);
  position: relative;
  cursor: pointer;
  transition: transform 0.08s, box-shadow 0.08s;
}
.hm-cell:hover { transform: scale(1.18); z-index: 2; box-shadow: 0 0 0 1px var(--accent); }
.hm-cell.empty { background: var(--surface); opacity: 0.4; }
/* Performance gradient by frame-time bucket */
.hm-cell.p0 { background: var(--perf-0); }
.hm-cell.p1 { background: var(--perf-1); }
.hm-cell.p2 { background: var(--perf-2); }
.hm-cell.p3 { background: var(--perf-3); }
.hm-cell.p4 { background: var(--perf-4); }
/* State overlay — boss fight cells get a red glow halo around them */
.hm-cell.boss::after {
  content: ''; position: absolute; inset: -2px;
  border: 1px solid var(--danger);
  border-radius: 3px; pointer-events: none;
  box-shadow: 0 0 6px rgba(247, 118, 142, 0.45);
}
.heatmap-legend {
  display: flex; flex-wrap: wrap; gap: 0.4em 1.2em;
  font-family: var(--mono); font-size: 0.75rem;
  color: var(--muted);
}
.heatmap-legend .lg-sw {
  display: inline-block; width: 0.8em; height: 0.8em;
  border-radius: 2px; margin-right: 0.4em; vertical-align: middle;
}
.heatmap-legend .lg-boss {
  border: 1px solid var(--danger); background: var(--surface);
  box-shadow: 0 0 4px rgba(247, 118, 142, 0.45);
}

/* =================================================== NOW PLAYING ENRICHED */
.now-seg.rich {
  grid-template-columns: 0.32rem minmax(0, 1.4fr) auto;
  padding: 0.45rem 0.55rem;
  gap: 0.55rem;
}
.now-seg.rich .swatch { height: 100%; min-height: 1.6em; }
.now-seg.rich .name-block { display: flex; flex-direction: column; gap: 0.1em; min-width: 0; }
.now-seg.rich .name-block .top { font-family: var(--ui); color: var(--text); font-size: 0.92rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.now-seg.rich .name-block .top .family-tag {
  font-family: var(--mono); font-size: 0.65rem;
  color: var(--muted); margin-right: 0.4em;
  padding: 0.02em 0.4em; background: var(--surface); border-radius: 2px;
  text-transform: uppercase; letter-spacing: 0.06em;
}
.now-seg.rich .name-block .sub { font-family: var(--mono); color: var(--muted); font-size: 0.75rem; }
.now-seg.rich .meta { font-family: var(--mono); font-size: 0.78rem; text-align: right; line-height: 1.3; }
.now-seg.rich .meta .mod { color: var(--accent); font-weight: 500; }

/* =================================================== HEATMAP TOOLTIP */
.hm-cell[data-tip]:hover::before {
  content: attr(data-tip);
  position: absolute;
  bottom: calc(100% + 6px); left: 50%; transform: translateX(-50%);
  background: #1a1b26ee; border: 1px solid var(--accent-line); border-radius: 3px;
  padding: 0.3em 0.55em; font-family: var(--mono); font-size: 0.7rem;
  color: var(--text); white-space: nowrap; z-index: 10;
  pointer-events: none;
}

/* =================================================== FRAME CHART TOGGLE */
.chart-toggle {
  display: inline-flex; border: 1px solid var(--border); border-radius: 3px;
  overflow: hidden; background: var(--header);
  margin-left: auto;
}
.chart-toggle button {
  font: inherit; font-size: 0.7rem;
  background: transparent; color: var(--muted);
  border: 0; border-right: 1px solid var(--border-soft);
  padding: 0.18em 0.7em; cursor: pointer;
}
.chart-toggle button:last-child { border-right: 0; }
.chart-toggle button:hover { color: var(--text); }
.chart-toggle button.active { background: var(--accent-soft); color: var(--accent); }

::-webkit-scrollbar { width: 6px; height: 6px; }
::-webkit-scrollbar-track { background: transparent; }
::-webkit-scrollbar-thumb { background: var(--border); border-radius: 3px; }
::-webkit-scrollbar-thumb:hover { background: var(--surface-2); }
";
}
