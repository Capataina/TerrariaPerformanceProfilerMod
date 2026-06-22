#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Lag pane containers. Each surface is a full-width panel stacked top to
    // bottom; the reshaped readable surfaces (rectangular heatmap, sortable
    // tables, split-bar cards, horizontal histogram) all read better at full
    // width than in the old side-by-side mid grid.
    //
    // Scroll-stability contract: the panels that contain a scrollable list
    // (clusters, density, causality, rhythm) keep their section header AND
    // their scroll container STATIC in this markup. The poll-driven renderers
    // only replace the inner content of the scroll container via setHTML(),
    // which preserves scrollTop/Left — so scrolling a long list and waiting
    // for the 3s poll no longer snaps it back to the top. The non-scrolling
    // panels (kpi, heatmap, gc) keep a single empty body the renderer owns.
    private const string HtmlLag = @"<!-- ============================================================== LAG -->
    <section class=""tab-pane"" data-pane=""lag"">
      <div class=""lag-shell"">
        <div class=""lag-kpi"" id=""lag-kpi""></div>

        <div class=""lag-heatmap"">
          <div class=""lag-section-h"">cause &times; context &middot; events</div>
          <div id=""lag-heatmap-body""></div>
        </div>

        <div class=""lag-clusters"">
          <div class=""lag-section-h"">fingerprint clusters</div>
          <div class=""lag-table-wrap"" id=""lag-clusters-body""></div>
          <div id=""lag-clusters-detail""></div>
        </div>

        <div class=""lag-density"">
          <div class=""lag-section-h"">per-segment lag density</div>
          <div id=""lag-density-caption""></div>
          <div class=""lag-table-wrap"" id=""lag-density-body""></div>
          <div id=""lag-density-legend""></div>
        </div>

        <div class=""lag-gc"">
          <div class=""lag-section-h"">gc pressure</div>
          <div id=""lag-gc-body""></div>
        </div>

        <div class=""lag-causality"">
          <div class=""lag-section-h"">allocation &rarr; gc &rarr; freed</div>
          <div class=""lag-causality-list"" id=""lag-causality-body""></div>
        </div>

        <div class=""lag-rhythm"">
          <div class=""lag-section-h"">lag rhythm</div>
          <div class=""lag-rhythm-grid"">
            <div class=""lag-hist"" id=""lag-rhythm-hist""></div>
            <div class=""lag-table-wrap"" id=""lag-rhythm-clusters""></div>
          </div>
        </div>
      </div>
    </section>

    ";
}
