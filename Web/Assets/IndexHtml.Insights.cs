#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Insights pane: mount points only. A combined overview pane (insight summary
    // 2/3 | cross-cutting signals 1/3, split by a gentle vertical divider) over a
    // kanban board (one column per insight family). A slide-in drawer (the shared
    // .drawer component) shows a clicked card's mod context — what the mod adds +
    // its cost/usage + every insight about it. Every surface is component-rendered
    // in Js.Insights.cs; renderInsights() fills #ins-overview + #ins-kanban, and
    // openInsightDetail() fills #ins-drawer on a card click.
    private const string HtmlInsights = @"<!-- ========================================================= INSIGHTS -->
    <section class=""tab-pane"" data-pane=""insights"">
      <div class=""ins-shell"">
        <div id=""ins-overview""></div>
        <div id=""ins-kanban""></div>
      </div>

      <!-- card-click mod context drawer (shared .drawer component) -->
      <aside id=""ins-drawer"" class=""drawer hidden"">
        <header class=""drawer-h"">
          <span class=""dr-close"" id=""ins-dr-close"" title=""close"">&times;</span>
          <span class=""dr-title"" id=""ins-dr-title"">mod</span>
          <span class=""dr-meta"" id=""ins-dr-meta""></span>
        </header>
        <div class=""drawer-body"" id=""ins-dr-body""></div>
      </aside>
    </section>

    ";
}
