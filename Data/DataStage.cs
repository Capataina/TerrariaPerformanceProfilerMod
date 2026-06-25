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
namespace PerformanceProfiler.Data;

/// <summary>
/// The six stages of the observability pipeline. Each stage has a single
/// responsibility (see <c>context/plans/unified-data-pipeline.md §3</c>):
/// collectors read raw values, aggregators organise them into groups,
/// stats derive numbers from aggregates, detectors emit pattern records,
/// streams persist to LiteDB, exporters serve to outside consumers.
///
/// <para>
/// Every <see cref="IDataStream"/> declares its stage. The registry uses
/// it for diagnostics and for future-iteration features (e.g. an exporter
/// that only consumes <see cref="Stat"/>s, never raw collectors).
/// </para>
/// </summary>
public enum DataStage : byte
{
    Collector,
    Aggregator,
    Stat,
    Detector,
    Stream,
    Exporter,
}
