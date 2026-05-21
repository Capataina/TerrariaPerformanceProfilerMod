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

/// <summary>Stream owning <c>worldSnapshots</c>.</summary>
internal sealed class WorldSnapshotStream : IPersistenceStream
{
    public string Name => "worldSnapshots";

    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.WorldSnapshot };

    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        db.WorldSnapshots.Upsert((WorldSnapshotRow)op.Payload);
    }

    public DbWriteOp? Reconstruct(JournalLine line)
        => line.Kind == nameof(DbOpKind.WorldSnapshot)
            ? DbWriteOp.WorldSnapshot(StreamJson.Deserialize<WorldSnapshotRow>(line.Payload))
            : null;

    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.WorldSnapshots.EnsureIndex(x => x.SessionId);
        db.WorldSnapshots.EnsureIndex(x => x.UnixMs);
    }
}
