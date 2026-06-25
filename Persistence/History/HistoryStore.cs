#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using PerformanceProfiler.Persistence.Records;

namespace PerformanceProfiler.Persistence.History;

/// <summary>
/// The cross-session read layer — the consuming half the plan adds. A read-model over
/// the two-level rollup (parallel to <see cref="DbReadModel"/>, but cross-session and
/// mod-membership-scoped), and the single home every cross-session consumer reads
/// through: the dashboard's lifetime views, the cross-session detectors, and the chat
/// commands all stop hand-rolling per-session <c>.Find</c> loops.
///
/// <para>
/// An instance over a <see cref="ProfilerDatabase"/> (not a static singleton like
/// DbReadModel) so it is unit-testable against a synthetic store. Every method is fully
/// guarded (Invariant 4): a query or shape failure returns empty and the surface stays
/// in its honest empty state. Read-only (Invariant 1).
/// </para>
///
/// <para>
/// <b>Scope is mod-membership overlap, structurally.</b> A per-mod query reads that
/// mod's rollup ring, which by construction contains only the sessions the mod was
/// present for — so removed mods are absent from a current-stack query and present mods
/// continue uninterrupted across modlist edits, with no reset. The caller supplies the
/// scope set (the live roster, or null = every tracked mod); the store does not reach
/// into the loader, keeping it decoupled and testable.
/// </para>
/// </summary>
public sealed class HistoryStore
{
    private readonly ProfilerDatabase _db;

    public HistoryStore(ProfilerDatabase db) => _db = db;

    /// <summary>
    /// The full lifetime story of one mod: its global rollup, the newest-first recency
    /// ring (capped at <paramref name="lastN"/>), and its per-modlist breakdown. Null if
    /// the mod has no rollup yet or the store is unavailable.
    /// </summary>
    public ModHistory? GetModHistory(string internalName, int lastN)
    {
        if (string.IsNullOrEmpty(internalName)) return null;
        try
        {
            ModLifetimeRollupRow? g = _db.ModLifetimeRollups.FindOne(x => x.InternalName == internalName);
            if (g == null) return null;

            List<SessionRingEntry> ring = g.Ring
                .OrderByDescending(e => e.EndedUtc)
                .Take(lastN > 0 ? lastN : g.Ring.Count)
                .ToList();

            List<ModModlistRollupRow> byModlist =
                _db.ModModlistRollups.Find(x => x.InternalName == internalName).ToList();

            return new ModHistory
            {
                InternalName = g.InternalName,
                DisplayName = string.IsNullOrEmpty(g.DisplayName) ? g.InternalName : g.DisplayName,
                LastVersion = g.LastVersion,
                SessionCount = g.SessionCount,
                ActiveSessionCount = g.ActiveSessionCount,
                LifetimeAvgCostMs = g.Cost.Mean,
                LifetimeAvgEngagement = g.Engagement.Mean,
                TotalSpikeContributions = g.TotalSpikeContributions,
                TotalStallContributions = g.TotalStallContributions,
                FirstSeenUtc = g.FirstSeenUtc,
                LastSeenUtc = g.LastSeenUtc,
                RecentRing = ring,
                ByModlist = byModlist,
            };
        }
        catch { return null; }
    }

    /// <summary>
    /// Per-mod aggregates over each mod's last-<paramref name="lastN"/> ring window — the
    /// rank/threshold primitive the cross-session detectors share. <paramref name="scopeMods"/>
    /// restricts to a set of internal names (the live roster, for current-modlist scope);
    /// null includes every tracked mod (all-time scope). Empty on any failure.
    /// </summary>
    public IReadOnlyList<ModWindowStats> WindowedStats(int lastN, IReadOnlyCollection<string>? scopeMods)
    {
        var result = new List<ModWindowStats>();
        try
        {
            HashSet<string>? scope = scopeMods != null ? new HashSet<string>(scopeMods) : null;
            IEnumerable<ModLifetimeRollupRow> rows = scope == null
                ? _db.ModLifetimeRollups.FindAll()
                : _db.ModLifetimeRollups.Find(x => true).Where(r => scope.Contains(r.InternalName));

            foreach (ModLifetimeRollupRow g in rows)
            {
                List<SessionRingEntry> window = g.Ring
                    .OrderByDescending(e => e.EndedUtc)
                    .Take(lastN > 0 ? lastN : g.Ring.Count)
                    .ToList();
                if (window.Count == 0) continue;

                int active = 0;
                double cost = 0d, alloc = 0d, eng = 0d;
                long spikes = 0L, stalls = 0L;
                foreach (SessionRingEntry e in window)
                {
                    if (e.WasActive) active++;
                    cost += e.CostMs;
                    alloc += e.AllocBytes;
                    eng += e.EngagementScore;
                    spikes += e.SpikeContributions;
                    stalls += e.StallContributions;
                }
                int n = window.Count;

                result.Add(new ModWindowStats
                {
                    InternalName = g.InternalName,
                    DisplayName = string.IsNullOrEmpty(g.DisplayName) ? g.InternalName : g.DisplayName,
                    LastVersion = g.LastVersion,
                    SessionsInWindow = n,
                    ActiveSessions = active,
                    AvgCostMs = cost / n,
                    AvgAllocBytes = alloc / n,
                    AvgEngagement = eng / n,
                    SpikeContributions = spikes,
                    StallContributions = stalls,
                    LastSeenUtc = window[0].EndedUtc,
                });
            }
        }
        catch { return result; }
        return result;
    }

    /// <summary>The last <paramref name="n"/> ended sessions, newest first. Empty on failure.</summary>
    public IReadOnlyList<RecentSessionView> RecentSessions(int n)
    {
        try
        {
            return _db.Sessions.Find(x => x.EndedUtc != null)
                .OrderByDescending(s => s.EndedUtc)
                .Take(n > 0 ? n : int.MaxValue)
                .Select(s => new RecentSessionView
                {
                    SessionId = s.Id,
                    StartedUtc = s.StartedUtc,
                    EndedUtc = s.EndedUtc,
                    Fingerprint = s.ModlistFingerprint,
                    DurationMs = s.DurationMs,
                    Mode = s.Mode,
                    TicksObserved = s.TicksObserved,
                })
                .ToList();
        }
        catch { return Array.Empty<RecentSessionView>(); }
    }

    /// <summary>Store-health counts for the data-health view + the reset control's context.</summary>
    public DataHealthView DataHealth()
    {
        var v = new DataHealthView();
        try
        {
            v.SessionCount = _db.Sessions.Count();
            DateTime? lastEnded = null;
            int ended = 0;
            foreach (SessionRow s in _db.Sessions.Find(x => x.EndedUtc != null))
            {
                ended++;
                if (s.EndedUtc != null && (lastEnded == null || s.EndedUtc > lastEnded)) lastEnded = s.EndedUtc;
            }
            v.EndedSessionCount = ended;
            v.LastSessionEndedUtc = lastEnded;
            v.ModlistCount = _db.Modlists.Count();
            v.ModsTracked = _db.ModLifetimeRollups.Count();
            v.DbFileSizeBytes = _db.DbFileSize;
        }
        catch { /* honest partial counts; Invariant 4 */ }
        return v;
    }
}
