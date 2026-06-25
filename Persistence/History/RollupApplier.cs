#nullable enable

using PerformanceProfiler.Persistence.Records;

namespace PerformanceProfiler.Persistence.History;

/// <summary>
/// The read-fold-upsert of one session into the two-level rollup, against a live
/// <see cref="ProfilerDatabase"/>. Shared by the two callers that fold sessions: the
/// <c>RollupStream</c> (live session-end, on the writer thread) and the one-time
/// <see cref="RollupBackfill"/> (existing history, at open) — so both apply identical
/// logic and the ring-dedup behaves the same on either path.
///
/// <para>
/// Per mod: load the global + per-modlist rows (or create fresh), gate on the global
/// ring (a session already folded is skipped — replay/dup idempotency), fold, upsert.
/// The global row is upserted last because its ring is the dedup marker.
/// </para>
/// </summary>
public static class RollupApplier
{
    public static void Apply(ProfilerDatabase db, SessionRollupInput input)
    {
        if (input.Mods.Count == 0) return;

        for (int i = 0; i < input.Mods.Count; i++)
        {
            ModSessionContribution c = input.Mods[i];
            if (string.IsNullOrEmpty(c.InternalName)) continue;

            ModLifetimeRollupRow global =
                db.ModLifetimeRollups.FindOne(x => x.InternalName == c.InternalName)
                ?? new ModLifetimeRollupRow { InternalName = c.InternalName };

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
}
