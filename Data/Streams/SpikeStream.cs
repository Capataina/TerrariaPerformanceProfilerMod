#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling.Persistence.Records;

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

/// <summary>Stream owning <c>spikeWindows</c>.</summary>
internal sealed class SpikeStream : IPersistenceStream
{
    public string Name => "spikeWindows";

    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.Spike };

    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        db.SpikeWindows.Upsert((SpikeWindowRow)op.Payload);
    }

    public DbWriteOp? Reconstruct(JournalLine line)
        => line.Kind == nameof(DbOpKind.Spike)
            ? DbWriteOp.Spike(StreamJson.Deserialize<SpikeWindowRow>(line.Payload))
            : null;

    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.SpikeWindows.EnsureIndex(x => x.SessionId);
        db.SpikeWindows.EnsureIndex(x => x.WorstFrameMs);
    }
}
