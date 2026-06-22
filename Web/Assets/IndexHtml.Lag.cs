#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Lag pane containers. Each surface is a full-width panel stacked top to
    // bottom; the reshaped readable surfaces (rectangular heatmap, sortable
    // tables, split-bar cards, horizontal histogram) all read better at full
    // width than in the old side-by-side mid grid. Ids are unchanged so the
    // Js.Lag renderers keep targeting the same nodes.
    private const string HtmlLag = @"<!-- ============================================================== LAG -->
    <section class=""tab-pane"" data-pane=""lag"">
      <div class=""lag-shell"">
        <div class=""lag-kpi"" id=""lag-kpi""></div>
        <div class=""lag-heatmap"" id=""lag-heatmap""></div>
        <div class=""lag-clusters"" id=""lag-clusters""></div>
        <div class=""lag-density"" id=""lag-density""></div>
        <div class=""lag-gc"" id=""lag-gc""></div>
        <div class=""lag-causality"" id=""lag-causality""></div>
        <div class=""lag-rhythm"" id=""lag-rhythm""></div>
      </div>
    </section>

    ";
}
