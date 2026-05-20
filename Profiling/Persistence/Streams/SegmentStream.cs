#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Profiling.Persistence.Streams;

/// <summary>
/// Stream owning the <c>segments</c> collection. One row per closed segment
/// (biome visit, weather event, boss fight, invasion, etc). Indexed on
/// SessionId for retrospective queries and on (Family, Key) for "show me
/// every Jungle visit I've ever had" lifetime aggregates.
/// </summary>
internal sealed class SegmentStream : IPersistenceStream
{
    public string Name => "segments";

    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.Segment };

    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        db.Segments.Upsert((SegmentRow)op.Payload);
    }

    public DbWriteOp? Reconstruct(JournalLine line)
        => line.Kind == nameof(DbOpKind.Segment)
            ? DbWriteOp.Segment(StreamJson.Deserialize<SegmentRow>(line.Payload))
            : null;

    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.Segments.EnsureIndex(x => x.SessionId);
        db.Segments.EnsureIndex(x => x.Family);
        db.Segments.EnsureIndex(x => x.StartUnixMs);
    }
}
