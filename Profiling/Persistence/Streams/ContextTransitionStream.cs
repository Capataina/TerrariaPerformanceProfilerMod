#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Profiling.Persistence.Streams;

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
