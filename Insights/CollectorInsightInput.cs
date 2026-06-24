#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Stats;

namespace PerformanceProfiler.Insights;

/// <summary>
/// Adapts a <see cref="MetricCollector"/> to <see cref="IInsightInput"/>. Lives in
/// the Insights module (which already depends on Profiling) so the dependency points
/// the right way — Profiling never has to know about the interpretation layer. This
/// is the seam that keeps drivers and reference frames testable against a synthetic
/// <see cref="IInsightInput"/> while production code feeds them a real collector.
/// </summary>
public sealed class CollectorInsightInput : IInsightInput
{
    private readonly MetricCollector _c;

    public CollectorInsightInput(MetricCollector collector) => _c = collector;

    public IReadOnlyList<double> PerModCategoryAverageMs => _c.PerModCategoryAverageMs;
    public IReadOnlyList<double> PerHookAverageMs => _c.PerHookAverageMs;
    public IReadOnlyList<double>? PerModCategoryAverageBytes => _c.PerModCategoryAverageBytes;
    public bool TracksAllocations => _c.TracksAllocations;
    public Baseline Baseline => _c.Baseline;

    public int LatestNpcCount => _c.History.Count > 0 ? _c.History[_c.History.Count - 1].NpcCount : 0;
    public int LatestProjectileCount => _c.History.Count > 0 ? _c.History[_c.History.Count - 1].ProjectileCount : 0;
    public long ManagedHeapBytes => _c.SelfHealth.ProcessManagedHeapBytes;
    public long SessionTicks => _c.History.Count;
}
