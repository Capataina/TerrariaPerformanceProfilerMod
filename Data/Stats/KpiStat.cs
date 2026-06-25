#nullable enable

using Terraria.ModLoader;
using PerformanceProfiler.Profiling;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// The reference <see cref="IDataStat{TSnapshot}"/> implementation. Produces
/// a <see cref="KpiSnapshot"/> (avg fps, worst frame, lag spikes count,
/// stall count + a handful of sub-stats) on demand from the live
/// <see cref="MetricCollector"/> via the existing pure-logic
/// <see cref="KpiCalculator.Compute"/>.
///
/// <para>
/// The dashboard's <c>/api/now</c> response embeds this snapshot directly
/// instead of computing the numbers inline. Registered under the name
/// <c>"kpi"</c> at world-load. <see cref="DataStreamCadence.OnDemand"/> — no
/// per-tick driver state.
/// </para>
///
/// <para>
/// This file is the canonical template for new <c>Data/Stats/</c> entries:
/// implement <see cref="IDataStat{TSnapshot}"/>, register in
/// <c>PerformanceProfiler.RegisterDataPipeline</c> via <c>DataRegistry.Shared.Register(new KpiStat())</c>,
/// done. Consumers go through <c>DataRegistry.Shared.Lookup&lt;KpiSnapshot&gt;("kpi")</c>.
/// </para>
/// </summary>
public sealed class KpiStat : IDataStat<KpiSnapshot>
{
    public const string StreamName = "kpi";

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { /* no per-session state */ }
    public void Reset() { /* stateless */ }
    public void Dispose() { /* nothing to release */ }

    public KpiSnapshot CurrentSnapshot()
    {
        MetricCollector? c = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        return KpiCalculator.Compute(c);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
