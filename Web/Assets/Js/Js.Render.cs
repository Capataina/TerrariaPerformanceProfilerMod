#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
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
