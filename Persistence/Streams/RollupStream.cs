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
/// The fold itself (<see cref="RollupFold"/>) is pure; this stream is just the I/O
/// shell around it: load each mod's two rows, gate on the ring (replay/dup idempotency),
/// fold, upsert. Per-mod gating on the global ring is the dedup marker; the per-modlist
/// row rides the same gate (a crash between the two upserts could miss one session from
/// one per-modlist row — negligible against the Welford distribution, and the global
/// level that powers the cross-session detectors stays exact).
/// </para>
/// </summary>
internal sealed class RollupStream : IPersistenceStream
{
    public string Name => "rollups";

    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.RollupFold };

    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        var input = (SessionRollupInput)op.Payload;
        if (input.Mods.Count == 0) return;

        for (int i = 0; i < input.Mods.Count; i++)
        {
            ModSessionContribution c = input.Mods[i];
            if (string.IsNullOrEmpty(c.InternalName)) continue;

            ModLifetimeRollupRow global =
                db.ModLifetimeRollups.FindOne(x => x.InternalName == c.InternalName)
                ?? new ModLifetimeRollupRow { InternalName = c.InternalName };

            // The ring is the dedup marker: a replayed or duplicate op for a session
            // already in this mod's ring is skipped, both levels.
            if (RollupFold.AlreadyFolded(global, input.SessionId)) continue;

            ModModlistRollupRow modlist =
                db.ModModlistRollups.FindOne(x => x.InternalName == c.InternalName && x.Fingerprint == input.Fingerprint)
                ?? new ModModlistRollupRow { InternalName = c.InternalName, Fingerprint = input.Fingerprint };

            RollupFold.FoldGlobal(global, input, c);
            RollupFold.FoldModlist(modlist, input, c);

            db.ModModlistRollups.Upsert(modlist);
            db.ModLifetimeRollups.Upsert(global); // global last: its ring is the dedup marker
        }
    }

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
