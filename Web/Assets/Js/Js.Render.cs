#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string JsRender = @"
// ====== SUMMARY =======================================================
function renderSummary() {
  renderKpiStrip();
  renderFrameChart();
  renderDonut();
  renderTrendSparklines();
  renderHeatmap();
  renderNowPlaying();
  renderNowEvents();
  renderSummaryMods();
}
";
}
