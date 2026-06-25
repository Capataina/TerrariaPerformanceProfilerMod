#nullable enable

using System;
using PerformanceProfiler.Persistence.Records;

namespace PerformanceProfiler.Persistence;

/// <summary>
/// Read-only view over the most recent COMPLETED session in the LiteDB store,
/// powering the dashboard's "reading from db" mode: when no world is live, the
/// Summary KPIs and the per-mod ranking fall back to the last session's
/// persisted <see cref="TickAggregateArchive"/> instead of showing empty.
///
/// <para>One archive row per session carries both the whole-session KPIs
/// (avg/median/p95/max frame, spike/stall counts) and the per-mod list, so a
/// single lookup serves both fallback endpoints.</para>
///
/// <para>Cached briefly — the store does not change while the game sits idle, so
/// re-querying on every ~500 ms poll would be wasteful. Fully guarded: any
/// failure or empty store yields null and the dashboard stays in its honest
/// no-world state. Read-only (Invariant 1): never writes the store.</para>
/// </summary>
public static class DbReadModel
{
    /// <summary>The most recent ended session and its archived summary.</summary>
    public sealed class LastSession
    {
        public DateTime StartedUtc;
        public DateTime EndedUtc;
        public TickAggregateArchive Archive = null!;
    }

    private static LastSession? _cached;
    private static DateTime _cachedAtUtc = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Most recent ended session + its archive, or null if none exist or the
    /// store is unavailable. Result is cached for <see cref="CacheTtl"/>.
    /// </summary>
    public static LastSession? GetLastSession()
    {
        DateTime now = DateTime.UtcNow;
        if (_cached != null && now - _cachedAtUtc < CacheTtl) return _cached;
        _cachedAtUtc = now;

        try
        {
            ProfilerDatabase? db = PerformanceProfiler.Database;
            if (db == null) { _cached = null; return null; }

            // Most recent session that actually ended (a crash-cut session has a
            // null EndedUtc and no archive row, so it is skipped here).
            SessionRow? best = null;
            foreach (SessionRow s in db.Sessions.Find(x => x.EndedUtc != null))
            {
                if (best == null || s.StartedUtc > best.StartedUtc) best = s;
            }
            if (best == null) { _cached = null; return null; }

            TickAggregateArchive? archive = db.TickAggregatesArchive.FindOne(a => a.SessionId == best.Id);
            if (archive == null) { _cached = null; return null; }

            _cached = new LastSession
            {
                StartedUtc = best.StartedUtc,
                EndedUtc = best.EndedUtc ?? best.StartedUtc,
                Archive = archive,
            };
            return _cached;
        }
        catch
        {
            // Abort-clean: a query/shape failure must never break the request
            // path; the dashboard simply stays in its no-world state.
            _cached = null;
            return null;
        }
    }
}
