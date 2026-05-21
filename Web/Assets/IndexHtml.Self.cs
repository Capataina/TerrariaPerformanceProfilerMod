#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string HtmlSelf = @"<!-- ============================================================= SELF -->
    <section class=""tab-pane"" data-pane=""self"">
      <div class=""self-layout"">

        <!-- Hero: profiler-health gauge + headline metric -->
        <div class=""panel self-hero"">
          <header class=""panel-h"">
            <span class=""panel-title"">profiler health</span>
            <span class=""panel-sub"" data-explain=""self-severity"">bytes-per-hook ratio · honest budget signal</span>
          </header>
          <div class=""hero-body"">
            <div class=""gauge"" id=""self-gauge"">
              <svg viewBox=""0 0 100 60"" preserveAspectRatio=""xMidYMid meet""></svg>
            </div>
            <div class=""hero-stats"">
              <div class=""hero-stat""><span class=""k"">severity</span><span class=""v"" id=""hero-sev"">—</span></div>
              <div class=""hero-stat""><span class=""k"">bytes/hook</span><span class=""v"" id=""hero-bph"">—</span></div>
              <div class=""hero-stat""><span class=""k"">vs baseline</span><span class=""v"" id=""hero-ratio"">—</span></div>
              <div class=""hero-stat""><span class=""k"">hooks installed</span><span class=""v"" id=""hero-hooks"">—</span></div>
            </div>
          </div>
        </div>

        <!-- Install footprint card with visual delta bar -->
        <div class=""panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">install footprint</span>
            <span class=""panel-sub"" data-explain=""install-delta"">heap delta over baseline</span>
          </header>
          <div class=""self-body"" id=""self-install""></div>
          <div class=""footprint-bar"" id=""footprint-bar""></div>
        </div>

        <!-- Process context with managed-vs-native split bar -->
        <div class=""panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">process context</span>
            <span class=""panel-sub"" data-explain=""process-context"">managed heap vs total working set</span>
          </header>
          <div class=""self-body"" id=""self-process""></div>
          <div class=""split-bar"" id=""self-split""></div>
        </div>

        <!-- Backend mode card -->
        <div class=""panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">attribution backend</span>
            <span class=""panel-sub"" data-explain=""backend"">how we instrument hooks</span>
          </header>
          <div class=""self-body"" id=""self-backend""></div>
        </div>

        <!-- Hook distribution per mod (mini chart) -->
        <div class=""panel panel-wide"">
          <header class=""panel-h"">
            <span class=""panel-title"">hook distribution · top 12 mods by hook count</span>
            <span class=""panel-sub"" id=""hookdist-sub"">—</span>
          </header>
          <div class=""hookdist"" id=""self-hookdist""></div>
        </div>

      </div>
    </section>

  ";
}
