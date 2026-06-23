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
    private const string CssKpis = @"
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
.kpi-spark .spark-svg { height: 100%; }
";
}
