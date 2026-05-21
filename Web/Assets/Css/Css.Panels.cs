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
    private const string CssPanels = @"
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

/* Lightweight singleton button (sits next to segctl groups in panel-actions).
   Used for collapse-all and similar one-shot actions where a single button
   would look odd inside a one-item segmented control. */
.mini-btn {
  font: inherit; font-size: 0.78rem;
  background: var(--header); color: var(--muted);
  border: 1px solid var(--border); border-radius: 3px;
  padding: 0.18em 0.7em; cursor: pointer;
  transition: color 0.12s, border-color 0.12s, background 0.12s;
}
.mini-btn:hover { color: var(--text); border-color: var(--accent-soft); }
.mini-btn:active { background: var(--accent-soft); color: var(--accent); }

.filter-input {
  background: var(--header); color: var(--text);
  border: 1px solid var(--border); border-radius: 3px;
  padding: 0.18em 0.55em; font-family: var(--mono); font-size: 0.78rem;
  width: 12rem; max-width: 30%;
}
.filter-input:focus { outline: none; border-color: var(--accent-line); }
";
}
