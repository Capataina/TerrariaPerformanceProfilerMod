#nullable enable

using System.Collections.Generic;
using LiteDB;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Profiling.Persistence.Streams;

/// <summary>
/// Stream owning the per-session per-mod and per-hook aggregate batches.
/// Both kinds use a wipe-and-insert idempotency pattern (delete all rows
/// for the session, then InsertBulk) — cheap because the row counts are
/// bounded (mods: ~100s, hooks: ~1000s).
/// </summary>
internal sealed class PerSessionAggregateStream : IPersistenceStream
{
    public string Name => "perSessionAggregates";

    public IReadOnlyList<DbOpKind> Kinds { get; } = new[]
    {
        DbOpKind.PerSessionModAggregateBatch,
        DbOpKind.PerSessionHookAggregateBatch,
    };

    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        ObjectId sid = op.SessionId;     // copy out so the LINQ lambdas don't capture `in op`
        switch (op.Kind)
        {
            case DbOpKind.PerSessionModAggregateBatch:
            {
                var rows = (List<PerSessionModAggregate>)op.Payload;
                if (rows.Count == 0) return;
                db.PerSessionMods.DeleteMany(x => x.SessionId == sid);
                db.PerSessionMods.InsertBulk(rows);
                break;
            }
            case DbOpKind.PerSessionHookAggregateBatch:
            {
                var rows = (List<PerSessionHookAggregate>)op.Payload;
                if (rows.Count == 0) return;
                db.PerSessionHooks.DeleteMany(x => x.SessionId == sid);
                db.PerSessionHooks.InsertBulk(rows);
                break;
            }
        }
    }

    public DbWriteOp? Reconstruct(JournalLine line)
    {
        ObjectId sid = string.IsNullOrEmpty(line.SessionId) ? ObjectId.Empty : new ObjectId(line.SessionId);
        return line.Kind switch
        {
            nameof(DbOpKind.PerSessionModAggregateBatch)
                => DbWriteOp.ModAggregateBatch(sid, StreamJson.Deserialize<List<PerSessionModAggregate>>(line.Payload)),
            nameof(DbOpKind.PerSessionHookAggregateBatch)
                => DbWriteOp.HookAggregateBatch(sid, StreamJson.Deserialize<List<PerSessionHookAggregate>>(line.Payload)),
            _ => null,
        };
    }

    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.PerSessionMods.EnsureIndex(x => x.SessionId);
        db.PerSessionMods.EnsureIndex(x => x.ModInternalName);
        db.PerSessionHooks.EnsureIndex(x => x.SessionId);
    }
}
