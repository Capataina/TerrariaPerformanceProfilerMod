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

/// <summary>Stream owning <c>contextTransitions</c>.</summary>
internal sealed class ContextTransitionStream : IPersistenceStream
{
    public string Name => "contextTransitions";

    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.ContextTransition };

    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        db.ContextTransitions.Upsert((ContextTransitionRow)op.Payload);
    }

    public DbWriteOp? Reconstruct(JournalLine line)
        => line.Kind == nameof(DbOpKind.ContextTransition)
            ? DbWriteOp.ContextTransition(StreamJson.Deserialize<ContextTransitionRow>(line.Payload))
            : null;

    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.ContextTransitions.EnsureIndex(x => x.SessionId);
    }
}
