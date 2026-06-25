#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using PerformanceProfiler.Persistence.Records;

namespace PerformanceProfiler.Persistence.History;

/// <summary>
/// The one-time rebuild of the cross-session rollup from the existing session history
/// (DB rework wave 1b). Without it, a player upgrading to the history layer would start
/// from an empty rollup and see no cross-session insight until they had played several
/// fresh sessions; the backfill folds every already-recorded session so the history is
/// rich from the first open.
///
/// <para>
/// <b>Why direct apply is safe here.</b> The backfill runs at DB open (mod load), which
/// always completes before the player loads a world — so it cannot contend with the
/// writer thread's live session-end fold on the rollup collections (there is none yet).
/// It therefore folds directly via <see cref="RollupApplier"/> rather than enqueueing,
/// keeping it self-contained. The rollup is derived data, fully rebuildable from the
/// source collections, so bypassing the journal costs nothing: a crash mid-backfill just
/// leaves the run-once marker unset (or partial) and is harmless.
/// </para>
///
/// <para>
/// Backfilled sessions fold <b>0 engagement</b> (the per-session usage signal was never
/// persisted historically — only live sessions from wave 1a onward capture it) and an
/// empty version (the version timeline is a roadmap concern, not a core question). Cost,
/// allocation, spike, stall, and active/idle are all reconstructed from the persisted
/// per-session aggregates + spike/stall rows.
/// </para>
/// </summary>
public static class RollupBackfill
{
    /// <summary>
    /// Folds every ended session into the rollup, oldest-first (so each mod's bounded ring
    /// ends holding its newest entries). Idempotent against the ring window via
    /// <see cref="RollupApplier"/>'s dedup; the caller gates it to run once (rollup empty +
    /// marker unset). Fully guarded — a failure leaves whatever was folded and logs.
    /// </summary>
    public static int Run(ProfilerDatabase db, Action<string, Exception?>? log)
    {
        int folded = 0, total = 0;
        try
        {
            List<SessionRow> ended = db.Sessions.Find(x => x.EndedUtc != null)
                .OrderBy(s => s.EndedUtc)
                .ToList();
            total = ended.Count;

            foreach (SessionRow s in ended)
            {
                SessionRollupInput input = BuildFromSession(db, s);
                if (input.Mods.Count == 0) continue;
                RollupApplier.Apply(db, input);
                folded++;
            }
            log?.Invoke($"Rollup backfill: folded {folded}/{total} ended sessions into the cross-session history.", null);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Rollup backfill failed after {folded}/{total} sessions (partial history retained)", ex);
        }
        return folded;
    }

    private static SessionRollupInput BuildFromSession(ProfilerDatabase db, SessionRow s)
    {
        var input = new SessionRollupInput
        {
            SessionId = s.Id,
            WorldId = s.WorldId,
            Fingerprint = s.ModlistFingerprint,
            EndedUtc = s.EndedUtc ?? s.StartedUtc,
            TicksObserved = s.TicksObserved,
        };

        // Spike contributions per mod: count each window the mod was a top contributor to.
        var spikeByName = new Dictionary<string, int>();
        foreach (SpikeWindowRow w in db.SpikeWindows.Find(x => x.SessionId == s.Id))
        {
            for (int i = 0; i < w.TopContributors.Count; i++)
            {
                string name = w.TopContributors[i].Name;
                if (!string.IsNullOrEmpty(name))
                    spikeByName[name] = (spikeByName.TryGetValue(name, out int n) ? n : 0) + 1;
            }
        }

        // Stall dominance per mod: count each cluster the mod dominated.
        var stallByName = new Dictionary<string, int>();
        foreach (StallClusterRow cl in db.StallClusters.Find(x => x.SessionId == s.Id))
        {
            string name = cl.DominantContributorName;
            if (!string.IsNullOrEmpty(name))
                stallByName[name] = (stallByName.TryGetValue(name, out int n) ? n : 0) + 1;
        }

        foreach (PerSessionModAggregate a in db.PerSessionMods.Find(x => x.SessionId == s.Id))
        {
            string name = a.ModInternalName;
            if (string.IsNullOrEmpty(name)) continue;

            input.Mods.Add(new ModSessionContribution
            {
                InternalName = name,
                DisplayName = name,
                Version = string.Empty, // not recoverable per-session historically
                CostMs = a.AvgMs,
                AllocBytes = a.AvgBytes,
                EngagementScore = 0d,   // usage was not persisted before wave 1a
                SpikeContributions = spikeByName.TryGetValue(name, out int sc) ? sc : 0,
                StallContributions = stallByName.TryGetValue(name, out int st) ? st : 0,
                WasActive = a.AvgMs > RollupFold.ActiveCostFloorMs,
            });
        }

        return input;
    }
}
