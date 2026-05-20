#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Profiling.Persistence.Streams;

/// <summary>
/// Stream owning the three tick-aggregate tiers: <c>tickAggregatesWarm</c>
/// (1Hz), <c>tickAggregatesCold</c> (1/min), <c>tickAggregatesArchive</c>
/// (1/session). Three kinds share one stream because they share a write
/// shape (find-by-natural-key → insert / update).
/// </summary>
internal sealed class TickAggregateStream : IPersistenceStream
{
    public string Name => "tickAggregates";

    public IReadOnlyList<DbOpKind> Kinds { get; } = new[]
    {
        DbOpKind.WarmAggregate,
        DbOpKind.ColdAggregate,
        DbOpKind.ArchiveAggregate,
    };

    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        switch (op.Kind)
        {
            case DbOpKind.WarmAggregate:
            {
                var row = (TickAggregateWarm)op.Payload;
                var existing = db.TickAggregatesWarm.FindOne(x =>
                    x.SessionId == row.SessionId && x.SecondIndex == row.SecondIndex);
                if (existing == null) db.TickAggregatesWarm.Insert(row);
                else { row.Id = existing.Id; db.TickAggregatesWarm.Update(row); }
                break;
            }
            case DbOpKind.ColdAggregate:
            {
                var row = (TickAggregateCold)op.Payload;
                var existing = db.TickAggregatesCold.FindOne(x =>
                    x.SessionId == row.SessionId && x.MinuteIndex == row.MinuteIndex);
                if (existing == null) db.TickAggregatesCold.Insert(row);
                else { row.Id = existing.Id; db.TickAggregatesCold.Update(row); }
                break;
            }
            case DbOpKind.ArchiveAggregate:
            {
                var row = (TickAggregateArchive)op.Payload;
                var existing = db.TickAggregatesArchive.FindOne(x => x.SessionId == row.SessionId);
                if (existing == null) db.TickAggregatesArchive.Insert(row);
                else { row.Id = existing.Id; db.TickAggregatesArchive.Update(row); }
                break;
            }
        }
    }

    public DbWriteOp? Reconstruct(JournalLine line)
    {
        return line.Kind switch
        {
            nameof(DbOpKind.WarmAggregate)
                => DbWriteOp.WarmAggregate(StreamJson.Deserialize<TickAggregateWarm>(line.Payload)),
            nameof(DbOpKind.ColdAggregate)
                => DbWriteOp.ColdAggregate(StreamJson.Deserialize<TickAggregateCold>(line.Payload)),
            nameof(DbOpKind.ArchiveAggregate)
                => DbWriteOp.ArchiveAggregate(StreamJson.Deserialize<TickAggregateArchive>(line.Payload)),
            _ => null,
        };
    }

    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.TickAggregatesWarm.EnsureIndex(x => x.SessionId);
        db.TickAggregatesWarm.EnsureIndex(x => x.ExpireAtUtc);
        db.TickAggregatesCold.EnsureIndex(x => x.SessionId);
        db.TickAggregatesArchive.EnsureIndex(x => x.SessionId, unique: true);
    }
}
