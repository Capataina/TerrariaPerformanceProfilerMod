#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string HtmlInsights = @"<!-- ========================================================= INSIGHTS -->
    <section class=""tab-pane"" data-pane=""insights"">
      <div class=""panel"">
        <header class=""panel-h"">
          <span class=""panel-title"">live insights</span>
          <span class=""panel-sub"" id=""insights-sub"" data-explain=""insights"">—</span>
        </header>
        <div class=""insights"" id=""insightslist""></div>
      </div>
    </section>

    ";
}
