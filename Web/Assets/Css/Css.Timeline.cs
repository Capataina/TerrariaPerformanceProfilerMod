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
    // Timeline tab styles — readable-vocabulary layer.
    //
    // Quiet, disciplined shapes only: a per-minute bar strip, time-placed
    // chips, time-scaled swimlane bars with splitBar waterfalls, a split-bar
    // + dtable attendance roll-up, death cards with labelled event chips,
    // and a chronicle block list. No metaphor glyphs, no waveform, no
    // treemap, no film-strip texture. The shared component vocabulary
    // (.split-bar, .dtable, .chip, perf tints, .statline) carries the load.
    private const string CssTimeline = @"
/* =================================================== TIMELINE TAB */
.tl-shell {
  display: flex; flex-direction: column;
  gap: 0.55rem;
  padding: 0.55rem 0.9rem 1rem;
  --tl-lane-h: 36px;
}

/* ---- Family filter bar -------------------------------------------- */
.tl-filterbar {
  display: flex; flex-wrap: wrap; gap: 4px;
}
.tl-filterbar .tl-filter-btn {
  font-family: var(--mono); font-size: 0.72rem;
  padding: 0.18rem 0.6rem;
  background: var(--surface);
  color: var(--muted);
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  cursor: pointer;
}
.tl-filterbar .tl-filter-btn:hover { color: var(--text); border-color: var(--accent-soft); }
.tl-filterbar .tl-filter-btn.active {
  background: var(--accent-soft);
  color: var(--accent);
  border-color: var(--accent-line);
}

/* ---- T4 heatstrip — per-minute vertical bar strip ----------------- */
/* A modest fixed-height activity strip: one thin fixed-width bar per minute,
   not a stretched slab. Bars are left-aligned and the strip scrolls
   horizontally once the session outgrows the panel width, so each minute
   keeps a legible width instead of being squeezed to a sliver. */
.tl-heatstrip {
  height: 68px;
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  background: var(--panel-2);
  overflow-x: auto; overflow-y: hidden;
  padding: 6px 8px;
}
.tl-heatstrip .hs-strip {
  display: flex; align-items: flex-end;
  gap: 2px;
  height: 100%;
  min-width: 100%;
  width: max-content;
}
.tl-heatstrip .hs-col {
  flex: 0 0 auto;
  width: 5px;
  height: 100%;
  position: relative;
  display: flex; flex-direction: column;
  align-items: center; justify-content: flex-end;
  cursor: default;
}
.tl-heatstrip .hs-bar {
  display: block;
  width: 100%;
  min-height: 2px;
  border-radius: 1px 1px 0 0;
  background: currentColor;
  opacity: 0.85;
}
.tl-heatstrip .hs-col:hover .hs-bar { opacity: 1; }
/* Spike / stall presence as a small dot above the bar, never by inflating
   the bar height — the bar encodes avgFrameMs alone. */
.tl-heatstrip .hs-mark {
  position: absolute; left: 50%; top: 1px;
  transform: translateX(-50%);
  width: 4px; height: 4px; border-radius: 50%;
  z-index: 1;
}
.tl-heatstrip .hs-mark.spike { background: var(--orange); }
.tl-heatstrip .hs-mark.stall { background: var(--danger); }

/* ---- T3 transition track — time-placed labelled chips ------------- */
.tl-transitions {
  position: relative;
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  background: var(--panel-2);
  padding: 0.3rem 0.5rem;
  min-height: 2rem;
}
.tl-transitions .tx-track {
  position: relative;
  min-height: 1.4rem;
}
.tl-transitions .tx-rail {
  position: absolute; left: 0; right: 0; top: 50%;
  height: 0; border-top: 1px dashed var(--border-soft);
  pointer-events: none;
}
.tl-transitions .tx-chip {
  position: absolute; top: 50%;
  transform: translate(-50%, -50%);
  z-index: 1;
  /* Floor the token width so a closely-timed transition chip stays a
     readable label rather than shrinking toward its text. */
  min-width: 2rem;
  justify-content: center;
}
.tl-transitions .tx-chip .tx-kind {
  color: var(--muted);
  text-transform: lowercase;
  margin-right: 0.2em;
}

/* ---- T1+T2 swimlanes ----------------------------------------------- */
.tl-gantt {
  display: flex; flex-direction: column;
  gap: 4px;
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  padding: 4px;
  background: var(--panel-2);
}
.tl-laneRow { display: block; }
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
  /* Floor the bar width so a short visit stays legible and clickable rather
     than collapsing to a 1-2px sliver. Position stays time-accurate; only
     the rendered width is floored. */
  min-width: 1.6rem;
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
.tl-segment .tl-waterfall {
  position: absolute; left: 0; right: 0; bottom: 0;
}
.tl-segment .tl-waterfall .split-bar {
  border-radius: 0;
  height: 5px;
  background: rgba(0,0,0,0.25);
}
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
.tl-detail .det-mods .ms { text-align: right; color: var(--accent); }

/* ---- T5 attendance — split bar + table ----------------------------- */
.tl-attendance .tm-totals {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0 1rem;
  margin-bottom: 0.5rem;
}
.tl-attendance .tm-totals .statline { display: flex; }
.tl-attendance .tm-bar { margin-bottom: 0.6rem; }
.tl-attendance .tm-table { margin-top: 0.2rem; }
.tl-attendance .tm-table td.l .dot {
  display: inline-block; width: 0.55rem; height: 0.55rem;
  border-radius: 1px; margin-right: 0.4em; vertical-align: middle;
}

/* ---- T6 death cards ------------------------------------------------- */
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
  margin-bottom: 0.4rem;
}
.tl-deaths .tl-death .head .k { color: var(--muted); margin-right: 0.3em; }
.tl-deaths .tl-death .ev-row {
  display: flex; flex-wrap: wrap; gap: 0.3rem;
}
.tl-deaths .tl-death .ev-chip {
  /* Keep every event token legible regardless of how short its label is. */
  min-width: 1.6rem;
  justify-content: center;
}
.tl-deaths .tl-death .ev-chip .ev-off {
  color: var(--muted);
  font-size: 0.66rem;
  margin-right: 0.1em;
}
.tl-deaths .tl-death .ev-chip .ev-mod {
  color: var(--muted);
  font-size: 0.66rem;
  margin-left: 0.25em;
  opacity: 0.85;
}

/* ---- T7 chronicle — chronological blocks --------------------------- */
.tl-chronicle {
  border: 1px solid var(--border-soft);
  border-radius: 3px;
  background: var(--panel-2);
  padding: 0.45rem 0.5rem;
  max-height: 320px;
  overflow-y: auto;
}
.tl-chronicle .cr-list {
  display: flex; flex-direction: column;
  gap: 2px;
}
.tl-chronicle .cr-block {
  padding: 0.35rem 0.55rem;
  border-left: 3px solid var(--accent);
  background: var(--panel);
  font-family: var(--mono); font-size: 0.74rem;
  color: var(--text);
}
.tl-chronicle .cr-meta {
  display: flex; gap: 0.6rem; align-items: baseline;
  margin-bottom: 0.1rem;
}
.tl-chronicle .cr-time {
  color: var(--muted);
  font-size: 0.66rem;
  letter-spacing: 0.02em;
}
.tl-chronicle .cr-kind {
  color: var(--accent);
  font-size: 0.68rem;
  text-transform: lowercase;
  letter-spacing: 0.04em;
}
.tl-chronicle .cr-text {
  color: var(--text-bright);
  font-size: 0.74rem;
  line-height: 1.3;
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
  .tl-attendance .tm-totals { grid-template-columns: 1fr; }
}
";
}
