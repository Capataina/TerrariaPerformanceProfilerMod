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

/// <summary>Stream owning <c>playerDeaths</c>.</summary>
internal sealed class PlayerDeathStream : IPersistenceStream
{
    public string Name => "playerDeaths";

    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.PlayerDeath };

    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        db.PlayerDeaths.Upsert((PlayerDeathRow)op.Payload);
    }

    public DbWriteOp? Reconstruct(JournalLine line)
        => line.Kind == nameof(DbOpKind.PlayerDeath)
            ? DbWriteOp.PlayerDeath(StreamJson.Deserialize<PlayerDeathRow>(line.Payload))
            : null;

    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.PlayerDeaths.EnsureIndex(x => x.SessionId);
        db.PlayerDeaths.EnsureIndex(x => x.UnixMs);
    }
}
