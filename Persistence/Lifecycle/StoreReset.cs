#nullable enable

using System;
using System.Collections.Generic;
using LiteDB;

namespace PerformanceProfiler.Persistence.Lifecycle;

/// <summary>Outcome of a reset, surfaced to the dashboard + client.log so the destructive
/// action is observable on both examiners (Dual-Surface).</summary>
public sealed class ResetReport
{
    public string Scope { get; set; } = "";
    public bool Ok { get; set; }
    public int SessionsCleared { get; set; }
    public int CollectionsCleared { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// The player-initiated reset (DB rework wave 3, decision E) — the ONLY path that deletes
/// the player's profiler data, and always their explicit choice (a confirmed top-bar
/// button, never a forced reset). Two scopes:
///
/// <list type="bullet">
/// <item><b>Everything</b> — drop every collection; the store starts fresh.</item>
/// <item><b>Forget this modlist</b> — delete the current stack's sessions + their event
/// rows + the per-stack rollup / baseline / modlist metadata, but KEEP each mod's GLOBAL
/// lifetime rollup. That is the spine made literal: per-mod cross-modlist history survives
/// a forget; only the playthrough on this stack is dropped.</item>
/// </list>
///
/// <para>Operates through the public collections, relying on LiteDB's internal locking for
/// thread-safety against the writer thread. A straggler write landing during a reset is
/// acceptable for a destructive op (the user is wiping). Corruption recovery is separate
/// and stays quarantine-not-delete — recovering already-lost data is not resetting good
/// data.</para>
/// </summary>
public static class StoreReset
{
    /// <summary>Every collection that carries a per-session <c>SessionId</c>, so a forgotten
    /// modlist's sessions take their event rows with them.</summary>
    private static IEnumerable<Action<ProfilerDatabase, ObjectId>> SessionScopedDeletes()
    {
        yield return (db, id) => db.PerSessionMods.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.PerSessionHooks.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.SpikeWindows.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.Stalls.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.StallClusters.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.ContextTransitions.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.TickAggregatesWarm.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.TickAggregatesCold.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.TickAggregatesArchive.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.Insights.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.PlayerDeaths.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.WorldSnapshots.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.DamageTaken.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.DamageDealt.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.NpcSpawns.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.ItemCreations.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.LoadoutSnapshots.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.BuffEvents.DeleteMany(x => x.SessionId == id);
        yield return (db, id) => db.Segments.DeleteMany(x => x.SessionId == id);
    }

    /// <summary>Wipes the entire store. The global per-mod history goes too — this is the
    /// "start over completely" escape hatch.</summary>
    public static ResetReport Everything(ProfilerDatabase db, Action<string, Exception?>? log = null)
    {
        var report = new ResetReport { Scope = "everything" };
        try
        {
            report.CollectionsCleared = db.DropAllUserData();
            report.Ok = true;
            log?.Invoke("Store reset: everything cleared by the player.", null);
        }
        catch (Exception ex)
        {
            report.Ok = false;
            report.Error = ex.Message;
            log?.Invoke("Store reset (everything) failed", ex);
        }
        return report;
    }

    /// <summary>Forgets the current modlist's playthrough while preserving per-mod lifetime
    /// history (the spine). Deletes the stack's sessions + event rows + per-stack rollup /
    /// baseline / modlist-metadata rows; leaves ModLifetimeRollups intact.</summary>
    public static ResetReport ForgetModlist(ProfilerDatabase db, string fingerprint, Action<string, Exception?>? log = null)
    {
        var report = new ResetReport { Scope = "modlist" };
        if (string.IsNullOrEmpty(fingerprint)) { report.Error = "no current modlist fingerprint"; return report; }
        try
        {
            var ids = new List<ObjectId>();
            foreach (var s in db.Sessions.Find(x => x.ModlistFingerprint == fingerprint)) ids.Add(s.Id);

            foreach (ObjectId id in ids)
                foreach (var del in SessionScopedDeletes())
                    try { del(db, id); } catch (Exception ex) { log?.Invoke($"Forget-modlist: a session-scoped delete failed for {id}", ex); }

            report.SessionsCleared = db.Sessions.DeleteMany(x => x.ModlistFingerprint == fingerprint);
            db.Modlists.DeleteMany(x => x.Fingerprint == fingerprint);
            db.Mods.DeleteMany(x => x.ModlistFingerprint == fingerprint);
            db.ContextBaselines.DeleteMany(x => x.Fingerprint == fingerprint);
            db.ModModlistRollups.DeleteMany(x => x.Fingerprint == fingerprint);
            // ModLifetimeRollups deliberately preserved: per-mod lifetime history survives a
            // modlist forget (the spine). The forgotten stack's ring entries remain as part
            // of each mod's own lifetime record, independent of the deleted session rows.

            report.Ok = true;
            log?.Invoke($"Store reset: forgot modlist {fingerprint} ({report.SessionsCleared} sessions); per-mod lifetime history retained.", null);
        }
        catch (Exception ex)
        {
            report.Ok = false;
            report.Error = ex.Message;
            log?.Invoke("Store reset (forget modlist) failed", ex);
        }
        return report;
    }
}
