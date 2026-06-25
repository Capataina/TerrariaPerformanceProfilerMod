#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string JsRender = @"
// ====== SUMMARY =======================================================
function renderSummary() {
  renderKpiStrip();
  renderModStream();
  renderFrameChart();
  renderDonut();
  renderCostFlow();
  renderTrendSparklines();
  renderHeatmap();
  renderNowPlaying();
  renderNowEvents();
  renderSummaryMods();
}
";
}
