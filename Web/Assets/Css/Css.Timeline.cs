#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Timeline tab styles — Wave 3 functional layer.
    //
    // The goal here is structural correctness, not visual flourish. Bars
    // are plain blocks. The heat strip is plain cells. The transition
    // track is plain diamonds. Wave 4 lays creative work on top of this
    // foundation; we want every data field reachable first.
    private const string CssTimeline = @"
/* =================================================== TIMELINE TAB */
.tl-shell {
  display: flex; flex-direction: column;
  gap: 0.55rem;
  padding: 0.55rem 0.9rem 1rem;
  --tl-row-h: 26px;
  --tl-lane-h: 36px;
  --tl-heat-h: 28px;
  --tl-tx-h: 26px;
}

/* ---- T4 heatstrip --------------------------------------------------- */
.tl-heatstrip {
  display: flex;
  height: var(--tl-heat-h);
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  background: var(--panel-2);
  overflow: hidden;
}
.tl-heatstrip .tl-heatcell {
  flex: 1 1 0;
  min-width: 4px;
  border-right: 1px solid var(--border-soft);
  position: relative;
  cursor: default;
}
.tl-heatstrip .tl-heatcell:last-child { border-right: 0; }
.tl-heatstrip .tl-heatcell .lab {
  position: absolute; inset: 0;
  display: flex; align-items: center; justify-content: center;
  font-family: var(--mono); font-size: 0.65rem; color: var(--muted);
  pointer-events: none;
}

/* ---- T3 transition track ------------------------------------------- */
.tl-transitions {
  position: relative;
  height: var(--tl-tx-h);
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  background: var(--panel-2);
}
.tl-transitions .tl-tx {
  position: absolute;
  top: 50%;
  width: 10px; height: 10px;
  margin-left: -5px; margin-top: -5px;
  background: var(--accent);
  transform: rotate(45deg);
  border: 1px solid var(--border);
  cursor: default;
}
.tl-transitions .tl-tx[data-type^='weather'] { background: var(--amber); }
.tl-transitions .tl-tx[data-type^='biome']   { background: var(--good); }
.tl-transitions .tl-tx[data-type='hardmode'] { background: var(--danger); }

/* ---- T1+T2 swimlanes ----------------------------------------------- */
.tl-gantt {
  display: flex; flex-direction: column;
  gap: 4px;
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  padding: 4px;
  background: var(--panel-2);
}
.tl-lane {
  position: relative;
  height: var(--tl-lane-h);
  background: var(--panel);
  border-radius: 2px;
  overflow: hidden;
}
.tl-lane::before {
  content: attr(data-family);
  position: absolute; left: 6px; top: 2px;
  font-family: var(--mono); font-size: 0.65rem;
  color: var(--muted);
  pointer-events: none;
  z-index: 1;
}
.tl-segment {
  position: absolute;
  top: 18px; bottom: 4px;
  background: var(--good);
  border: 1px solid var(--border);
  border-radius: 2px;
  cursor: pointer;
  overflow: hidden;
  min-width: 4px;
  color: var(--text-bright);
  font-family: var(--mono); font-size: 0.7rem;
}
.tl-segment[data-family='Biome']    { background: rgba( 79,157,106, 0.55); border-color: var(--good); }
.tl-segment[data-family='Weather']  { background: rgba(184,138, 37, 0.55); border-color: var(--amber); }
.tl-segment[data-family='Boss']     { background: rgba(185, 78, 88, 0.65); border-color: var(--danger); }
.tl-segment[data-family='Invasion'] { background: rgba(201,127, 60, 0.60); border-color: var(--orange); }
.tl-segment[data-family='Subworld'] { background: rgba(110, 93,150, 0.60); border-color: var(--purple); }
.tl-segment .lbl {
  position: absolute; left: 4px; right: 4px; top: 1px;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  font-size: 0.7rem; line-height: 1.2;
}
.tl-segment .waterfall {
  position: absolute; left: 0; right: 0; bottom: 0;
  height: 5px; display: flex;
  background: rgba(0,0,0,0.25);
}
.tl-segment .waterfall span { display: block; height: 100%; }
.tl-segment .badge {
  position: absolute; right: 3px; bottom: 5px;
  font-family: var(--mono); font-size: 0.62rem;
  padding: 0 4px; border-radius: 2px;
  background: rgba(0,0,0,0.45); color: var(--text-bright);
}
.tl-segment.outlier { box-shadow: 0 0 0 1px var(--accent) inset; }
.tl-segment.selected {
  outline: 2px solid var(--accent);
  outline-offset: -1px;
  z-index: 2;
}

/* ---- Bottom row: detail + attendance --------------------------------- */
.tl-bottom {
  display: grid;
  grid-template-columns: minmax(260px, 1fr) minmax(260px, 1.2fr);
  gap: 0.55rem;
  min-height: 180px;
}
.tl-detail, .tl-attendance {
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  background: var(--panel-2);
  padding: 0.55rem 0.7rem;
  font-family: var(--mono); font-size: 0.82rem;
  color: var(--text);
  overflow: auto;
}
.tl-detail h4, .tl-attendance h4 {
  margin: 0 0 0.4rem; font-family: var(--ui); font-size: 0.78rem;
  letter-spacing: 0.04em; text-transform: uppercase; color: var(--muted);
  font-weight: 500;
}
.tl-detail .det-row {
  display: grid; grid-template-columns: 8.5em 1fr;
  gap: 0.4em;
  padding: 0.12rem 0;
  border-bottom: 1px dashed var(--border-soft);
}
.tl-detail .det-row:last-child { border-bottom: 0; }
.tl-detail .det-row .k { color: var(--muted); }
.tl-detail .det-row .v { color: var(--text-bright); }
.tl-detail .det-mods {
  margin-top: 0.5rem;
  display: flex; flex-direction: column; gap: 2px;
}
.tl-detail .det-mods .row {
  display: grid; grid-template-columns: minmax(0, 1fr) 5em 4em;
  gap: 0.5em; align-items: center;
  font-size: 0.78rem;
}
.tl-detail .det-mods .name {
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}
.tl-detail .det-mods .bar {
  height: 6px; background: var(--accent-soft);
  border: 1px solid var(--accent-line); border-radius: 1px;
  position: relative;
}
.tl-detail .det-mods .bar > span {
  position: absolute; left: 0; top: 0; bottom: 0;
  background: var(--accent);
}
.tl-detail .det-mods .ms { text-align: right; color: var(--accent); }

/* ---- T5 attendance --------------------------------------------------- */
.tl-attendance .att-row {
  display: grid;
  grid-template-columns: minmax(0, 1.4fr) 5em 4em 4em;
  gap: 0.5em; align-items: center;
  padding: 0.15rem 0;
  border-bottom: 1px dashed var(--border-soft);
}
.tl-attendance .att-row:last-child { border-bottom: 0; }
.tl-attendance .att-row .name {
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  color: var(--text-bright);
}
.tl-attendance .att-row .biome-bar {
  height: 6px; background: var(--panel); border: 1px solid var(--border-soft);
  border-radius: 1px; position: relative; overflow: hidden;
}
.tl-attendance .att-row .biome-bar > span {
  position: absolute; left: 0; top: 0; bottom: 0; background: var(--good);
}
.tl-attendance .att-row .num {
  text-align: right; color: var(--text); font-family: var(--mono); font-size: 0.78rem;
}
.tl-attendance .att-totals {
  display: flex; gap: 1rem; flex-wrap: wrap;
  margin-bottom: 0.4rem;
  font-size: 0.78rem; color: var(--muted);
}
.tl-attendance .att-totals .v { color: var(--text-bright); }

/* ---- T6 death strips ------------------------------------------------- */
.tl-deaths {
  display: flex; flex-direction: column; gap: 0.4rem;
}
.tl-deaths .tl-death {
  border: 1px solid var(--border-soft);
  border-left: 3px solid var(--danger);
  border-radius: 3px;
  background: var(--panel-2);
  padding: 0.45rem 0.6rem;
}
.tl-deaths .tl-death .head {
  display: flex; flex-wrap: wrap; gap: 0.5rem 1rem;
  font-family: var(--mono); font-size: 0.78rem;
  color: var(--text);
  margin-bottom: 0.35rem;
}
.tl-deaths .tl-death .head .k { color: var(--muted); margin-right: 0.3em; }
.tl-deaths .tl-death .strip {
  position: relative;
  height: 22px;
  background: var(--panel);
  border: 1px solid var(--border-soft);
  border-radius: 2px;
  overflow: hidden;
}
.tl-deaths .tl-death .strip .ev {
  position: absolute; top: 2px; bottom: 2px;
  width: 6px; margin-left: -3px;
  border-radius: 1px;
}
.tl-deaths .tl-death .strip .ev[data-kind='buff-on']      { background: var(--good); }
.tl-deaths .tl-death .strip .ev[data-kind='buff-off']     { background: var(--muted); }
.tl-deaths .tl-death .strip .ev[data-kind='damage']       { background: var(--danger); }
.tl-deaths .tl-death .strip .ev[data-kind='npc-spawn']    { background: var(--amber); }
.tl-deaths .tl-death .strip .ev[data-kind='item-created'] { background: var(--cyan); }
.tl-deaths .tl-death .strip .ev[data-kind='death']        { background: var(--text-bright); width: 3px; }
.tl-deaths .tl-death .strip .axis {
  position: absolute; left: 0; right: 0; bottom: 0;
  height: 1px; background: var(--border);
}

/* ---- T7 chronicle ---------------------------------------------------- */
.tl-chronicle {
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  background: var(--panel-2);
  padding: 0.5rem 0.7rem;
  font-family: var(--mono); font-size: 0.8rem;
  max-height: 220px; overflow-y: auto;
  display: flex; flex-direction: column; gap: 2px;
}
.tl-chronicle .cl-row {
  display: grid;
  grid-template-columns: 5.5em 6em minmax(0, 1fr);
  gap: 0.6em;
  padding: 0.1rem 0;
  border-bottom: 1px dashed var(--border-soft);
}
.tl-chronicle .cl-row:last-child { border-bottom: 0; }
.tl-chronicle .cl-row .t { color: var(--muted); }
.tl-chronicle .cl-row .kind {
  color: var(--accent); text-transform: lowercase;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
}
.tl-chronicle .cl-row .txt { color: var(--text); }
.tl-chronicle .cl-row[data-kind='death']      .kind { color: var(--danger); }
.tl-chronicle .cl-row[data-kind='spike']      .kind { color: var(--orange); }
.tl-chronicle .cl-row[data-kind='boss-start'] .kind,
.tl-chronicle .cl-row[data-kind='boss-end']   .kind { color: var(--danger); }
.tl-chronicle .cl-row[data-kind='weather']    .kind { color: var(--amber); }
.tl-chronicle .cl-row[data-kind='transition'] .kind { color: var(--good); }

.tl-empty {
  padding: 0.5rem 0.7rem;
  color: var(--muted);
  font-family: var(--mono); font-size: 0.8rem;
}

@media (max-width: 900px) {
  .tl-bottom { grid-template-columns: 1fr; }
}
";
}
