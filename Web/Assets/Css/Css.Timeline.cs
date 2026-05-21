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

/* ---- T4 heatstrip — SVG seismograph waveform ----------------------- */
.tl-heatstrip {
  height: 60px;
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  background: var(--panel-2);
  overflow: hidden;
  padding: 0;
}
.tl-heatstrip .tl-seismo {
  display: block;
  width: 100%; height: 100%;
}
.tl-heatstrip .axis-mid {
  stroke: var(--border-soft);
  stroke-width: 1;
  stroke-dasharray: 2 3;
}
.tl-heatstrip .tape {
  fill: var(--accent);
  fill-opacity: 0.22;
}
.tl-heatstrip .edge {
  fill: none;
  stroke: var(--accent);
  stroke-width: 1.2;
  stroke-linejoin: round;
  vector-effect: non-scaling-stroke;
}
.tl-heatstrip .peak {
  fill: var(--orange);
  stroke: var(--orange);
  stroke-width: 0.5;
}
.tl-heatstrip .spike-dot { fill: var(--orange); fill-opacity: 0.85; }
.tl-heatstrip .stall-dot { fill: var(--danger); fill-opacity: 0.85; }
.tl-heatstrip .hot { fill: transparent; pointer-events: all; cursor: default; }

/* ---- T3 transition track — per-kind glyphs ------------------------- */
.tl-transitions {
  position: relative;
  height: var(--tl-tx-h);
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  background: var(--panel-2);
  overflow: hidden;
}
.tl-transitions .tl-tx-svg {
  display: block;
  width: 100%; height: 100%;
}
.tl-transitions .tx-rail {
  stroke: var(--border-soft);
  stroke-width: 1;
  stroke-dasharray: 2 4;
}
.tl-transitions .gx { stroke-width: 1; vector-effect: non-scaling-stroke; }
.tl-transitions .gx.weather   { fill: var(--amber);  stroke: var(--amber); }
.tl-transitions .gx.biome     { fill: var(--good);   stroke: var(--good); fill-opacity: 0.75; }
.tl-transitions .gx.hardmode  { fill: var(--danger); stroke: var(--danger); }
.tl-transitions .gx.invasion  { fill: var(--orange); stroke: var(--orange); fill-opacity: 0.85; }
.tl-transitions .gx.subworld .ring.outer { fill: none; stroke: var(--purple); stroke-width: 1.2; }
.tl-transitions .gx.subworld .ring.inner { fill: var(--purple); stroke: none; fill-opacity: 0.7; }
.tl-transitions .gx.generic   { fill: none; stroke: var(--accent); stroke-width: 1.5; }

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

/* ---- T5 attendance — nested treemap -------------------------------- */
.tl-attendance .tm-totals {
  display: flex; gap: 1rem; flex-wrap: wrap;
  margin-bottom: 0.4rem;
  font-size: 0.78rem; color: var(--muted);
}
.tl-attendance .tm-totals .v { color: var(--text-bright); }
.tl-attendance .tl-treemap {
  display: block;
  width: 100%;
  height: calc(100% - 2.2rem);
  min-height: 140px;
}
.tl-attendance .tm-cell rect { transition: fill-opacity 0.12s ease; }
.tl-attendance .tm-cell:hover rect { fill-opacity: 0.82; }
.tl-attendance .tm-lbl {
  font-family: var(--mono); font-size: 11px;
  fill: var(--text-bright);
  pointer-events: none;
}
.tl-attendance .tm-sub {
  font-family: var(--mono); font-size: 10px;
  fill: var(--text);
  fill-opacity: 0.78;
  pointer-events: none;
}
.tl-attendance .tm-vanilla-rect {
  fill: var(--panel);
  stroke: var(--border-soft);
  stroke-width: 1;
  stroke-dasharray: 3 3;
}
.tl-attendance .tm-vanilla .tm-lbl { fill: var(--muted); }
.tl-attendance .tm-vanilla .tm-sub { fill: var(--muted); }

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
.tl-deaths .tl-death .tl-death-svg {
  display: block;
  width: 100%;
  height: 32px;
  background: var(--panel);
  border: 1px solid var(--border-soft);
  border-radius: 2px;
}
.tl-deaths .tl-death .ax-rail {
  stroke: var(--border-soft);
  stroke-width: 1;
}
.tl-deaths .tl-death .ax-tick {
  stroke: var(--border);
  stroke-width: 1;
}
.tl-deaths .tl-death .ax-lbl {
  font-family: var(--mono); font-size: 7px;
  fill: var(--muted);
}
.tl-deaths .tl-death .ev { vector-effect: non-scaling-stroke; }
.tl-deaths .tl-death .ic-death rect { fill: var(--text-bright); stroke: none; }
.tl-deaths .tl-death .ic-death .hole { fill: var(--panel); }
.tl-deaths .tl-death .ic-death .jaw  { fill: var(--text-bright); }
.tl-deaths .tl-death .ic-damage line {
  stroke: var(--danger); stroke-width: 1.4; stroke-linecap: round;
}
.tl-deaths .tl-death .ic-spawn { fill: var(--amber); stroke: var(--amber); stroke-width: 0.5; }
.tl-deaths .tl-death .ic-item .cap    { fill: var(--cyan); }
.tl-deaths .tl-death .ic-item .bottle { fill: var(--cyan); fill-opacity: 0.55; stroke: var(--cyan); stroke-width: 0.8; }
.tl-deaths .tl-death .ic-buff-on  line,
.tl-deaths .tl-death .ic-buff-on  polyline { stroke: var(--good);  stroke-width: 1.3; fill: none; stroke-linecap: round; stroke-linejoin: round; }
.tl-deaths .tl-death .ic-buff-off line,
.tl-deaths .tl-death .ic-buff-off polyline { stroke: var(--muted); stroke-width: 1.3; fill: none; stroke-linecap: round; stroke-linejoin: round; }
.tl-deaths .tl-death .ic-generic { fill: var(--accent); }

/* ---- T7 chronicle — horizontal narrative film-strip ribbon --------- */
.tl-chronicle {
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  background: var(--panel-2);
  padding: 0.45rem 0.5rem;
  overflow-x: auto;
  overflow-y: hidden;
}
.tl-chronicle .cr-ribbon {
  display: flex;
  flex-direction: row;
  align-items: stretch;
  gap: 0;
  min-height: 92px;
}
.tl-chronicle .cr-block {
  flex: 0 0 auto;
  width: 12rem;
  padding: 0.4rem 0.55rem;
  border-left: 3px solid var(--accent);
  background: var(--panel);
  display: flex; flex-direction: column;
  gap: 0.15rem;
  font-family: var(--mono); font-size: 0.72rem;
  color: var(--text);
  position: relative;
  overflow: hidden;
}
.tl-chronicle .cr-block::after {
  /* sprocket-hole row — film-strip texture along the bottom edge */
  content: '';
  position: absolute;
  left: 0; right: 0; bottom: 0;
  height: 5px;
  background-image: radial-gradient(circle at 4px 50%, var(--panel-2) 1.5px, transparent 1.6px);
  background-size: 9px 5px;
  background-repeat: repeat-x;
  opacity: 0.7;
}
.tl-chronicle .cr-sep {
  flex: 0 0 6px;
  background:
    linear-gradient(to bottom,
      var(--panel-2) 0 6px,
      transparent 6px calc(100% - 6px),
      var(--panel-2) calc(100% - 6px) 100%);
  background-color: var(--panel);
  position: relative;
}
.tl-chronicle .cr-sep::before,
.tl-chronicle .cr-sep::after {
  content: ''; position: absolute;
  left: 1px; right: 1px;
  height: 3px;
  background: var(--border-soft);
}
.tl-chronicle .cr-sep::before { top: 1px; }
.tl-chronicle .cr-sep::after  { bottom: 1px; }

.tl-chronicle .cr-time {
  color: var(--muted);
  font-size: 0.66rem;
  letter-spacing: 0.02em;
}
.tl-chronicle .cr-kind {
  color: var(--accent);
  font-size: 0.7rem;
  text-transform: lowercase;
  letter-spacing: 0.04em;
}
.tl-chronicle .cr-text {
  color: var(--text-bright);
  font-size: 0.74rem;
  line-height: 1.25;
  overflow: hidden;
  display: -webkit-box;
  -webkit-line-clamp: 4;
  -webkit-box-orient: vertical;
}
.tl-chronicle .cr-block[data-kind='death']      { border-left-color: var(--danger); }
.tl-chronicle .cr-block[data-kind='death']      .cr-kind { color: var(--danger); }
.tl-chronicle .cr-block[data-kind='spike']      { border-left-color: var(--orange); }
.tl-chronicle .cr-block[data-kind='spike']      .cr-kind { color: var(--orange); }
.tl-chronicle .cr-block[data-kind='boss-start'],
.tl-chronicle .cr-block[data-kind='boss-end']   { border-left-color: var(--amber); }
.tl-chronicle .cr-block[data-kind='boss-start'] .cr-kind,
.tl-chronicle .cr-block[data-kind='boss-end']   .cr-kind { color: var(--amber); }
.tl-chronicle .cr-block[data-kind='weather']    { border-left-color: var(--cyan); }
.tl-chronicle .cr-block[data-kind='weather']    .cr-kind { color: var(--cyan); }
.tl-chronicle .cr-block[data-kind='transition'] { border-left-color: var(--good); }
.tl-chronicle .cr-block[data-kind='transition'] .cr-kind { color: var(--good); }
.tl-chronicle .cr-block[data-kind='summary']    { border-left-color: var(--purple); }
.tl-chronicle .cr-block[data-kind='summary']    .cr-kind { color: var(--purple); }
.tl-chronicle .cr-block:hover { background: var(--panel-2); }

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
