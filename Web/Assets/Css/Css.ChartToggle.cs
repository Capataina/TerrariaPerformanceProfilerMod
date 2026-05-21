#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string CssChartToggle = @"
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
";
}
