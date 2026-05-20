#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Profiling.Persistence.Streams;

/// <summary>
/// Stream owning the <c>sessions</c> collection. Handles
/// <see cref="DbOpKind.SessionStart"/> and <see cref="DbOpKind.SessionEnd"/>.
/// </summary>
internal sealed class SessionStream : IPersistenceStream
{
    public string Name => "sessions";

    public IReadOnlyList<DbOpKind> Kinds { get; } = new[]
    {
        DbOpKind.SessionStart,
        DbOpKind.SessionEnd,
    };

    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        switch (op.Kind)
        {
            case DbOpKind.SessionStart:
            {
                var row = (SessionRow)op.Payload;
                db.Sessions.Upsert(row);
                break;
            }
            case DbOpKind.SessionEnd:
            {
                var existing = db.Sessions.FindById(op.SessionId);
                if (existing != null)
                {
                    existing.EndedUtc = DateTime.UtcNow;
                    existing.DurationMs = op.DurationMs;
                    existing.TicksObserved = op.TicksObserved;
                    existing.EndReason = string.IsNullOrEmpty(op.EndReason) ? "clean" : op.EndReason;
                    existing.Incomplete = false;
                    db.Sessions.Update(existing);
                }
                break;
            }
        }
    }

    public DbWriteOp? Reconstruct(JournalLine line)
    {
        return line.Kind switch
        {
            nameof(DbOpKind.SessionStart)
                => DbWriteOp.SessionStart(StreamJson.Deserialize<SessionRow>(line.Payload)),
            nameof(DbOpKind.SessionEnd)
                => DbWriteOp.SessionEnd(
                    new LiteDB.ObjectId(line.SessionId),
                    line.EndReason,
                    line.DurationMs,
                    line.TicksObserved),
            _ => null,
        };
    }

    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.Sessions.EnsureIndex(x => x.StartedUtc);
        db.Sessions.EnsureIndex(x => x.ModlistFingerprint);
    }
}
