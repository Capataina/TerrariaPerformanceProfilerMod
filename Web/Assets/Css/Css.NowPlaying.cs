#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string CssNowPlaying = @"
/* =================================================== NOW PLAYING ENRICHED */
.now-seg.rich {
  grid-template-columns: 0.32rem minmax(0, 1.4fr) auto;
  padding: 0.45rem 0.55rem;
  gap: 0.55rem;
}
.now-seg.rich .swatch { height: 100%; min-height: 1.6em; }
.now-seg.rich .name-block { display: flex; flex-direction: column; gap: 0.1em; min-width: 0; }
.now-seg.rich .name-block .top { font-family: var(--ui); color: var(--text); font-size: 0.92rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.now-seg.rich .name-block .top .family-tag {
  font-family: var(--mono); font-size: 0.65rem;
  color: var(--muted); margin-right: 0.4em;
  padding: 0.02em 0.4em; background: var(--surface); border-radius: 2px;
  text-transform: uppercase; letter-spacing: 0.06em;
}
.now-seg.rich .name-block .sub { font-family: var(--mono); color: var(--muted); font-size: 0.75rem; }
.now-seg.rich .meta { font-family: var(--mono); font-size: 0.78rem; text-align: right; line-height: 1.3; }
.now-seg.rich .meta .mod { color: var(--accent); font-weight: 500; }

/* =================================================== HEATMAP TOOLTIP */";
}
