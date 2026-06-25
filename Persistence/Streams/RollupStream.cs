#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Persistence.Records;
using PerformanceProfiler.Persistence.History;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Collectors;
namespace PerformanceProfiler.Persistence.Streams;

/// <summary>
/// Stream owning the cross-session rollup collections (<c>modLifetimeRollups</c> +
/// <c>modModlistRollups</c>). It is the one place a session is folded into the lifetime
/// history, and it runs on the writer thread — the sole owner of DB access — so the
/// read-fold-upsert is naturally serialised with every other write (no cross-thread
/// race on the rollup rows).
///
/// <para>
/// The read-fold-upsert lives in <see cref="RollupApplier"/> (shared with the one-time
/// backfill so both fold identically); this stream is just its writer-thread entry point
/// + journal reconstruction + index declaration. Per-mod gating on the global ring is
/// the dedup marker, so a replayed op cannot double-count.
/// </para>
/// </summary>
internal sealed class RollupStream : IPersistenceStream
{
    public string Name => "rollups";

    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.RollupFold };

    public void Apply(in DbWriteOp op, ProfilerDatabase db)
        => RollupApplier.Apply(db, (SessionRollupInput)op.Payload);

    public DbWriteOp? Reconstruct(JournalLine line)
        => line.Kind == nameof(DbOpKind.RollupFold)
            ? DbWriteOp.RollupFold(StreamJson.Deserialize<SessionRollupInput>(line.Payload))
            : null;

    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.ModLifetimeRollups.EnsureIndex(x => x.InternalName);
        db.ModModlistRollups.EnsureIndex(x => x.InternalName);
        db.ModModlistRollups.EnsureIndex(x => x.Fingerprint);
    }
}
