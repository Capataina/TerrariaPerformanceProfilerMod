#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string HtmlSummary = @"<!-- ========================================================== SUMMARY -->
    <section class=""tab-pane active"" data-pane=""summary"">

      <div class=""grid-summary"">

        <!-- KPI strip: each card has a hero number + 3 sub-stats + sparkline -->
        <div class=""kpi-strip"">
          <div class=""kpi"" data-explain=""kpi-fps"">
            <div class=""kpi-head"">
              <span class=""k"">avg fps</span>
              <span class=""kpi-tag"" id=""kpi-fps-tag"">—</span>
            </div>
            <div class=""kpi-hero""><span class=""v"" id=""kpi-fps-v"">—</span><span class=""v-suffix"">/ 60</span></div>
            <div class=""kpi-subs"" id=""kpi-fps-subs""></div>
            <div class=""kpi-spark"" id=""kpi-fps-spark""></div>
          </div>
          <div class=""kpi"" data-explain=""kpi-worst"">
            <div class=""kpi-head"">
              <span class=""k"">worst frame</span>
              <span class=""kpi-tag"" id=""kpi-worst-tag"">—</span>
            </div>
            <div class=""kpi-hero""><span class=""v"" id=""kpi-worst-v"">—</span><span class=""v-suffix"">ms</span></div>
            <div class=""kpi-subs"" id=""kpi-worst-subs""></div>
            <div class=""kpi-spark"" id=""kpi-worst-spark""></div>
          </div>
          <div class=""kpi"" data-explain=""kpi-spikes"">
            <div class=""kpi-head"">
              <span class=""k"">lag spikes</span>
              <span class=""kpi-tag"" id=""kpi-spikes-tag"">—</span>
            </div>
            <div class=""kpi-hero""><span class=""v"" id=""kpi-spikes-v"">—</span><span class=""v-suffix"" id=""kpi-spikes-suffix"">in 30s</span></div>
            <div class=""kpi-subs"" id=""kpi-spikes-subs""></div>
            <div class=""kpi-spark"" id=""kpi-spikes-spark""></div>
          </div>
          <div class=""kpi"" data-explain=""kpi-stalls"">
            <div class=""kpi-head"">
              <span class=""k"">stalls</span>
              <span class=""kpi-tag"" id=""kpi-stalls-tag"">—</span>
            </div>
            <div class=""kpi-hero""><span class=""v"" id=""kpi-stalls-v"">—</span><span class=""v-suffix"">session</span></div>
            <div class=""kpi-subs"" id=""kpi-stalls-subs""></div>
            <div class=""kpi-spark"" id=""kpi-stalls-spark""></div>
          </div>
        </div>

        <!-- Frame chart hero -->
        <div class=""panel panel-hero"" style=""grid-area: chart;"">
          <header class=""panel-h"">
            <span class=""panel-title"" id=""chart-title"">frame time · last 30s</span>
            <span class=""panel-sub"" id=""chart-sub"">—</span>
            <span class=""panel-actions"" id=""chart-mode""></span>
          </header>
          <div class=""chart-wrap"">
            <div class=""chart"" id=""frame-chart"" aria-hidden=""true""></div>
            <div class=""panel-empty hidden"" id=""chart-empty""></div>
          </div>
        </div>

        <!-- Impact share donut -->
        <div class=""panel"" style=""grid-area: donut;"">
          <header class=""panel-h"">
            <span class=""panel-title"">impact share</span>
            <span class=""panel-sub"" id=""donut-sub"">—</span>
          </header>
          <div class=""donut-wrap"" id=""donut-svg""></div>
          <div class=""donut-legend"" id=""donut-legend""></div>
        </div>

        <!-- 3-row session-trend sparklines -->
        <div class=""panel"" style=""grid-area: trends;"">
          <header class=""panel-h"">
            <span class=""panel-title"" id=""trends-title"">session trend · last 30s</span>
            <span class=""panel-sub"" data-explain=""sparklines"">frame · alloc · spikes</span>
          </header>
          <div class=""trends"">
            <div class=""trend-rows"" id=""trend-rows"">
              <div class=""trend-row""><span class=""tr-k"" data-explain=""spark-frame"">frame</span><div class=""tr-spark"" id=""spark-frame""></div></div>
              <div class=""trend-row""><span class=""tr-k"" data-explain=""spark-gc"">gc</span><div class=""tr-spark"" id=""spark-alloc""></div></div>
              <div class=""trend-row""><span class=""tr-k"" data-explain=""spark-spike"">spikes</span><div class=""tr-spark"" id=""spark-spike""></div></div>
            </div>
            <div class=""panel-empty hidden"" id=""trends-empty""></div>
          </div>
        </div>

        <!-- Session timeframe heatmap -->
        <div class=""panel heatmap-panel"">
          <header class=""panel-h"">
            <span class=""panel-title"">session timeframe · minute by minute</span>
            <span class=""panel-sub"" id=""heatmap-sub"" data-explain=""heatmap"">—</span>
          </header>
          <div class=""heatmap-wrap"">
            <div class=""heatmap-grid"" id=""heatmap-grid""></div>
            <div class=""heatmap-legend"">
              <span><span class=""lg-sw"" style=""background: var(--perf-0)""></span>smooth (≤17ms)</span>
              <span><span class=""lg-sw"" style=""background: var(--perf-1)""></span>17–25ms</span>
              <span><span class=""lg-sw"" style=""background: var(--perf-2)""></span>25–40ms</span>
              <span><span class=""lg-sw"" style=""background: var(--perf-3)""></span>40–60ms</span>
              <span><span class=""lg-sw"" style=""background: var(--perf-4)""></span>&gt;60ms · spikes</span>
              <span><span class=""lg-sw lg-boss""></span>boss fight</span>
            </div>
          </div>
        </div>

        <!-- CPU cost flow: category -> top mods (sankey) -->
        <div class=""panel"" style=""grid-area: flow;"">
          <header class=""panel-h"">
            <span class=""panel-title"">where the cpu goes · category → mod</span>
            <span class=""panel-sub"" id=""flow-sub"">—</span>
          </header>
          <div class=""flow-wrap"" id=""cost-flow""></div>
        </div>

        <!-- Now playing segments -->
        <div class=""panel"" style=""grid-area: now;"">
          <header class=""panel-h"">
            <span class=""panel-title"">now playing</span>
            <span class=""panel-sub"" id=""now-sub"">0 open</span>
          </header>
          <div class=""now-scroll"" id=""nowlist""></div>
        </div>

        <!-- Events feed -->
        <div class=""panel"" style=""grid-area: events;"">
          <header class=""panel-h"">
            <span class=""panel-title"">recent events</span>
            <span class=""panel-sub"">last 12</span>
          </header>
          <div class=""events-scroll"" id=""nowevents""></div>
        </div>

        <!-- Full mod ranking with tree expansion + mod cards -->
        <div class=""panel panel-wide"" style=""grid-area: mods;"">
          <header class=""panel-h"">
            <span class=""panel-title"">mods · cascading tree</span>
            <span class=""panel-actions"">
              <input type=""search"" id=""mod-filter"" placeholder=""search mods…"" class=""filter-input"" />
              <button id=""mods-collapse-all"" class=""mini-btn"" title=""collapse every expanded mod + category"">collapse all</button>
              <span id=""mods-sort""></span>
            </span>
          </header>
          <div class=""modtable-head"">
            <span class=""mh rank"">#</span>
            <span class=""mh name"">mod</span>
            <span class=""mh bar"">cost</span>
            <span class=""mh trend"">30s</span>
            <span class=""mh num""><span data-explain=""tick-frame"">now</span></span>
            <span class=""mh num""><span data-explain=""tick-avg"">avg</span></span>
            <span class=""mh num"" id=""mh-alloc""><span data-explain=""alloc"">alloc</span></span>
          </div>
          <div class=""modtable-scroll"" id=""modtable""></div>
        </div>

      </div>
    </section>

    ";
}
