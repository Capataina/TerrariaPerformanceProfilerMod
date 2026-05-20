#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Profiling.Persistence.Streams;

/// <summary>Stream owning <c>insights</c>.</summary>
internal sealed class InsightStream : IPersistenceStream
{
    public string Name => "insights";

    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.Insight };

    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        db.Insights.Upsert((InsightRow)op.Payload);
    }

    public DbWriteOp? Reconstruct(JournalLine line)
        => line.Kind == nameof(DbOpKind.Insight)
            ? DbWriteOp.Insight(StreamJson.Deserialize<InsightRow>(line.Payload))
            : null;

    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.Insights.EnsureIndex(x => x.SessionId);
        db.Insights.EnsureIndex(x => x.PatternKey);
    }
}
