#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Persistence.Records;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Collectors;
namespace PerformanceProfiler.Persistence.Streams;

/// <summary>Stream owning <c>stallEvents</c>.</summary>
internal sealed class StallStream : IPersistenceStream
{
    public string Name => "stallEvents";

    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.Stall };

    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        db.Stalls.Upsert((StallEventRow)op.Payload);
    }

    public DbWriteOp? Reconstruct(JournalLine line)
        => line.Kind == nameof(DbOpKind.Stall)
            ? DbWriteOp.Stall(StreamJson.Deserialize<StallEventRow>(line.Payload))
            : null;

    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.Stalls.EnsureIndex(x => x.SessionId);
    }
}
