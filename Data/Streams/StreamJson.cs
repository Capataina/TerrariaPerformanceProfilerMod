#nullable enable

using JsonSerializer = System.Text.Json.JsonSerializer;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Collectors;
namespace PerformanceProfiler.Data.Streams;

/// <summary>
/// Shared <see cref="JsonSerializer"/> options for stream reconstruction.
/// Internal to the streams namespace — every stream's
/// <see cref="IPersistenceStream.Reconstruct"/> uses the same shape so
/// serialised journal lines match the format <see cref="EventJournal"/>
/// produces.
/// </summary>
internal static class StreamJson
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = false,
        IncludeFields = false,
    };

    public static T Deserialize<T>(string json) where T : class
        => JsonSerializer.Deserialize<T>(json, Options)!;
}
