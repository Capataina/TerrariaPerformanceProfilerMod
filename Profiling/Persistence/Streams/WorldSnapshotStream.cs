#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Profiling.Persistence.Streams;

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
