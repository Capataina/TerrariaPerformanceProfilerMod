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
    private const string CssSelf = @"
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
";
}
