#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Timeline tab — Gantt-style swimlane dashboard, component-library layer.
    //
    // Layout (top → bottom):
    //   tl-filterbar      family filter — segmented() control, JS-filled (all / per-family)
    //   tl-heatstrip      per-minute activity bar strip (T4) — barChart() vertical strip
    //   tl-transitions    time-placed labelled transition chips (T3) — bespoke time-domain
    //   tl-gantt          5 stacked lanes — Biome, Weather, Boss, Invasion, Subworld (T1+T2)
    //   tl-bottom         detail panel + attendance panel (T5) — shared .panel chrome
    //   tl-deaths         one card per death, labelled event chips (T6)
    //   tl-chronicle      panel with a scroll-region of timestamped blocks (T7)
    //
    // The detail / attendance / chronicle panels keep their chrome STATIC here
    // and expose an inner scroll body the renderers fill via setHTML(), so the
    // 2.5s poll preserves scroll position instead of snapping to the top (the
    // same scroll-stability contract the Self/Lag panes use). The gantt lanes
    // are absolutely positioned over the session time domain; each lane is
    // wrapped in a tl-laneRow so the family filter can hide whole rows, and the
    // inner tl-lane keeps data-family for its CSS label.
    private const string HtmlTimeline = @"<!-- ========================================================= TIMELINE -->
    <section class=""tab-pane"" data-pane=""timeline"">
      <div class=""tl-shell"">
        <div class=""tl-filterbar"" id=""tl-filterbar""></div>
        <div class=""tl-heatstrip"" id=""tl-heatstrip""></div>
        <div class=""tl-transitions"" id=""tl-transitions""></div>
        <div class=""tl-gantt"">
          <div class=""tl-laneRow"" id=""tl-laneRow-biome""><div class=""tl-lane"" data-family=""Biome"" id=""tl-lane-biome""></div></div>
          <div class=""tl-laneRow"" id=""tl-laneRow-weather""><div class=""tl-lane"" data-family=""Weather"" id=""tl-lane-weather""></div></div>
          <div class=""tl-laneRow"" id=""tl-laneRow-boss""><div class=""tl-lane"" data-family=""Boss"" id=""tl-lane-boss""></div></div>
          <div class=""tl-laneRow"" id=""tl-laneRow-invasion""><div class=""tl-lane"" data-family=""Invasion"" id=""tl-lane-invasion""></div></div>
          <div class=""tl-laneRow"" id=""tl-laneRow-subworld""><div class=""tl-lane"" data-family=""Subworld"" id=""tl-lane-subworld""></div></div>
        </div>
        <div class=""tl-bottom"">

          <!-- Selected-segment drill (statlines + per-mod dtable, JS-filled) -->
          <div class=""panel tl-detail"">
            <header class=""panel-h"">
              <span class=""panel-title"">segment detail</span>
              <span class=""panel-sub"" data-explain=""segment-detail"">click a swimlane block to drill</span>
            </header>
            <div class=""panel-body scroll-region"" id=""tl-detail-body"" style=""max-height:220px""></div>
          </div>

          <!-- Biome attendance roll-up (split bar + dtable, JS-filled) -->
          <div class=""panel tl-attendance"">
            <header class=""panel-h"">
              <span class=""panel-title"">attendance</span>
              <span class=""panel-sub"" data-explain=""attendance"">biome-tick share by mod</span>
            </header>
            <div class=""panel-body scroll-region"" id=""tl-attendance-body"" style=""max-height:220px""></div>
          </div>

        </div>
        <div class=""tl-deaths"" id=""tl-deaths""></div>

        <!-- Session chronicle (timestamped block list, JS-filled scroll body) -->
        <div class=""panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">chronicle</span>
            <span class=""panel-sub"" data-explain=""chronicle"">session events in order</span>
          </header>
          <div class=""panel-body flush scroll-region"" id=""tl-chronicle-scroll"" style=""max-height:320px""></div>
        </div>
      </div>
    </section>

    ";
}
