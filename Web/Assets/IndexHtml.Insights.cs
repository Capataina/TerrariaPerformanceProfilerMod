#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Insights pane: mount points only. The interpretive layer — an infographic
    // summary row (findings-by-family radial bars + a pattern×confidence
    // multi-level donut + headline tiles) above the main row, which pairs the
    // ranked insight FEED (the engine's natural-language conclusions, the hero)
    // with the cross-cutting signal roll-up in the sidebar. Every surface is
    // component-rendered in Js.Insights.cs; the bespoke markup here is the two
    // responsive grids (CSS in Css.Insights.cs). renderInsights() fills these.
    private const string HtmlInsights = @"<!-- ========================================================= INSIGHTS -->
    <section class=""tab-pane"" data-pane=""insights"">
      <div class=""ins-shell"">
        <div id=""ins-summary""></div>
        <div class=""ins-main"">
          <div id=""ins-feed""></div>
          <div id=""ins-cross""></div>
        </div>
      </div>
    </section>

    ";
}
