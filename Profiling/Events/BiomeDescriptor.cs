#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Profiling.Events;

/// <summary>
/// One entry in <see cref="BiomeRegistry"/>: a stable id (the bit index in
/// <see cref="EventContext.Biomes"/>), the player-facing display name, the
/// canonical full name used as the dictionary key in session reports, and
/// the source mod (null for vanilla).
/// </summary>
internal readonly record struct BiomeDescriptor(int Id, string DisplayName, string FullName, string? ModName);
