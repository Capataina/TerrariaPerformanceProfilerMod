#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Lag pane scaffolding, component-rendered. Each surface is a full-width
    // .panel; every body is filled by the renderer from the shared component
    // library (statGrid, heatmapMatrix/barChart, .dtable, lineChart, splitBar,
    // sectionBlock). The bespoke .lag-* chrome was retired onto components.
    //
    // Scroll-stability contract: the panels with a scrollable list (clusters,
    // density, causality, rhythm) keep their header AND their .scroll-region
    // container STATIC here. The poll-driven renderers replace only the inner
    // content of the scroll container via setHTML(), which preserves
    // scrollTop/Left so the 3s poll never snaps a scrolled list back to the top.
    private const string HtmlLag = @"<!-- ============================================================== LAG -->
    <section class=""tab-pane"" data-pane=""lag"">
      <div class=""lag-layout"">

        <!-- KPI strip: events / session lag / worst p95 / load factor -->
        <div class=""panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">session lag</span>
            <span class=""panel-sub"" data-explain=""lag-kpi"">aggregate lag signal this session</span>
          </header>
          <div class=""panel-body"" id=""lag-kpi""></div>
        </div>

        <!-- Cause x context heatmap (collapses to a ranked bar list <=1 context) -->
        <div class=""panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">cause &times; context</span>
            <span class=""panel-sub"" data-explain=""lag-heatmap"">events by cause and surrounding context</span>
          </header>
          <div class=""panel-body"" id=""lag-heatmap-body""></div>
        </div>

        <!-- Fingerprint clusters: sortable table + selected-row detail strip -->
        <div class=""panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">fingerprint clusters</span>
            <span class=""panel-sub"" data-explain=""lag-clusters"">recurring lag signatures, sortable</span>
          </header>
          <div class=""panel-body"">
            <div class=""scroll-region"" id=""lag-clusters-body"" style=""max-height:24rem""></div>
            <div id=""lag-clusters-detail"" style=""margin-top:0.5rem""></div>
          </div>
        </div>

        <!-- Per-segment lag density -->
        <div class=""panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">per-segment lag density</span>
            <span class=""panel-sub"" id=""lag-density-sub"">&mdash;</span>
          </header>
          <div class=""panel-body"">
            <div class=""scroll-region"" id=""lag-density-body"" style=""max-height:24rem""></div>
            <div id=""lag-density-legend"" style=""margin-top:0.4rem""></div>
          </div>
        </div>

        <!-- GC pressure: heap line + counter stat block -->
        <div class=""panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">gc pressure</span>
            <span class=""panel-sub"" data-explain=""lag-gc"">managed heap over time &middot; collection counters</span>
          </header>
          <div class=""panel-body"" id=""lag-gc-body""></div>
        </div>

        <!-- Allocation -> GC -> freed causality chains -->
        <div class=""panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">allocation &rarr; gc &rarr; freed</span>
            <span class=""panel-sub"" data-explain=""lag-causality"">who allocated into each gc stall window</span>
          </header>
          <div class=""panel-body"">
            <div class=""scroll-region"" id=""lag-causality-body"" style=""max-height:24rem""></div>
          </div>
        </div>

        <!-- Lag rhythm: interval histogram + cluster table -->
        <div class=""panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">lag rhythm</span>
            <span class=""panel-sub"" data-explain=""lag-rhythm"">periodicity of repeated lag events</span>
          </header>
          <div class=""panel-body"">
            <div class=""lag-rhythm-body"">
              <div id=""lag-rhythm-hist""></div>
              <div class=""scroll-region"" id=""lag-rhythm-clusters"" style=""max-height:24rem""></div>
            </div>
          </div>
        </div>

      </div>
    </section>

    ";
}
