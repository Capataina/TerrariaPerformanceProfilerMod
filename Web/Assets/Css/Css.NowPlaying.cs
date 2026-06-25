#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string CssNowPlaying = @"
/* =================================================== NOW PLAYING ENRICHED */
/* Per-row detail for the shared row() the now-playing list composes from: a
   full-height family swatch (data colour set inline), a two-line name block,
   and a right-aligned meta column. The row chrome itself is the shared .row. */
.now-swatch { height: 100%; min-height: 1.6em; border-radius: 1px; }
.now-name { display: flex; flex-direction: column; gap: 0.1em; min-width: 0; }
.now-name .now-top { font-family: var(--ui); color: var(--text); font-size: 0.92rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.now-name .now-top .chip {
  font-size: 0.65rem; margin-right: 0.4em;
  text-transform: uppercase; letter-spacing: 0.06em;
}
.now-name .now-sub { font-family: var(--mono); color: var(--muted); font-size: 0.75rem; }
.now-meta { font-family: var(--mono); font-size: 0.78rem; text-align: right; line-height: 1.3; }
.now-meta .now-mod { color: var(--accent); font-weight: 500; }

/* Muted closing line under the open-segment rows. Earns the panel's lower edge
   so a short list no longer reads as a tall empty void; descriptive, not a CTA. */
.now-foot {
  font-family: var(--mono); font-size: 0.72rem; color: var(--dim);
  padding: 0.5rem 0.2rem 0.2rem; border-top: 1px solid var(--border-soft);
  margin-top: 0.35rem; line-height: 1.35;
}
";
}
