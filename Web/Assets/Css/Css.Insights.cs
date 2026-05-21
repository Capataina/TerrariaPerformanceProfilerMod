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
    private const string CssInsights = @"
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
";
}
