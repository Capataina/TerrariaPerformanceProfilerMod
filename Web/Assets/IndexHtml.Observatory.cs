#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Observatory pane: mount points only. The descriptive per-mod attribution
    // layer (composition + headline stats up top, the per-mod master-detail as
    // the hero, the engagement-vs-cost scatter + correlation chord as the
    // analysis pair, the dormant table as the reference at the bottom). Every
    // surface is component-rendered in Js.Observatory.cs; the only bespoke markup
    // here is the responsive grids that arrange the panels (CSS in
    // Css.Observatory.cs). renderObservatory() fills these slots.
    private const string HtmlObservatory = @"<!-- ===================================================== OBSERVATORY -->
    <section class=""tab-pane"" data-pane=""observatory"">
      <div class=""obs-shell"">
        <div class=""obs-top"">
          <div id=""obs-composition""></div>
          <div id=""obs-stats""></div>
        </div>
        <div class=""obs-main"">
          <div id=""obs-list""></div>
          <div id=""obs-detail""></div>
        </div>
        <div class=""obs-analysis"">
          <div id=""obs-scatter""></div>
          <div id=""obs-corr""></div>
        </div>
        <div id=""obs-roster""></div>
        <div id=""obs-dormant""></div>
      </div>
    </section>

    ";
}
