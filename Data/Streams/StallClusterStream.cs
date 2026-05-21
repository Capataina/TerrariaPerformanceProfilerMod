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

/// <summary>Stream owning <c>stallClusters</c>.</summary>
internal sealed class StallClusterStream : IPersistenceStream
{
    public string Name => "stallClusters";

    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.StallCluster };

    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        db.StallClusters.Upsert((StallClusterRow)op.Payload);
    }

    public DbWriteOp? Reconstruct(JournalLine line)
        => line.Kind == nameof(DbOpKind.StallCluster)
            ? DbWriteOp.StallCluster(StreamJson.Deserialize<StallClusterRow>(line.Payload))
            : null;

    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.StallClusters.EnsureIndex(x => x.SessionId);
        db.StallClusters.EnsureIndex(x => x.StartUnixMs);
    }
}
