#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string HtmlInsights = @"<!-- ========================================================= INSIGHTS -->
    <section class=""tab-pane"" data-pane=""insights"">
      <div class=""ins-shell"">
        <div class=""ins-kpi"" id=""ins-kpi""></div>
        <div class=""ins-mid"">
          <div class=""ins-observatory"">
            <div class=""ins-dormant"" id=""ins-dormant""></div>
            <div class=""ins-obs-list"" id=""ins-obs-list""></div>
          </div>
          <aside class=""ins-detail"" id=""ins-detail""></aside>
        </div>
        <div class=""ins-cross"" id=""ins-cross""></div>
        <div class=""ins-scatter"" id=""ins-scatter""></div>
        <div class=""ins-matrix"" id=""ins-matrix""></div>
      </div>
    </section>

    ";
}
