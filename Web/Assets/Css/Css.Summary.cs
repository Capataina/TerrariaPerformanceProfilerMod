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
    private const string CssSummary = @"
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
";
}
