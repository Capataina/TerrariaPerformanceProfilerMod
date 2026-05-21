#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string HtmlLag = @"<!-- ============================================================== LAG -->
    <section class=""tab-pane"" data-pane=""lag"">
      <div class=""lag-shell"">
        <div class=""lag-kpi"" id=""lag-kpi""></div>
        <div class=""lag-mid"">
          <div class=""lag-heatmap"" id=""lag-heatmap""></div>
          <div class=""lag-clusters"" id=""lag-clusters""></div>
        </div>
        <div class=""lag-density"" id=""lag-density""></div>
        <div class=""lag-gc"" id=""lag-gc""></div>
        <div class=""lag-causality"" id=""lag-causality""></div>
        <div class=""lag-rhythm"" id=""lag-rhythm""></div>
      </div>
    </section>

    ";
}
