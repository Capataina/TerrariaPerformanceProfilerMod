#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.History;
using PerformanceProfiler.Persistence.Lifecycle;
using PerformanceProfiler.Persistence.Records;
using PerformanceProfiler.Web.Server;

namespace PerformanceProfiler.Web;

internal static partial class DashboardRouter
{
    // ----------------------------------------------------------------------
    // /api/history?mod=<internalName> — one mod's cross-session story (DB rework
    // wave 5): the lifetime rollup + the recency ring (a per-session trend) + the
    // per-modlist breakdown. Powers the Observatory drawer's lifetime view.
    // ----------------------------------------------------------------------
    private static string BuildHistory(HttpRequest req)
    {
        ProfilerDatabase? db = PerformanceProfiler.Database;
        string mod = ParseQueryValueRaw(req.RawTarget, "mod");
        if (db == null || string.IsNullOrEmpty(mod))
            return JsonSerializer.Serialize(new { found = false }, JsonOpts);

        try
        {
            ModHistory? h = new HistoryStore(db).GetModHistory(mod, lastN: ModLifetimeRollupRow.RingCapacity);
            if (h == null) return JsonSerializer.Serialize(new { found = false, mod }, JsonOpts);

            var ring = new List<object>(h.RecentRing.Count);
            foreach (SessionRingEntry e in h.RecentRing)
            {
                ring.Add(new
                {
                    endedUtc = e.EndedUtc,
                    fingerprint = e.Fingerprint,
                    version = e.Version,
                    costMs = e.CostMs,
                    allocBytes = e.AllocBytes,
                    engagement = e.EngagementScore,
                    spikes = e.SpikeContributions,
                    stalls = e.StallContributions,
                    active = e.WasActive,
                });
            }
            var byModlist = new List<object>(h.ByModlist.Count);
            foreach (ModModlistRollupRow m in h.ByModlist)
            {
                byModlist.Add(new
                {
                    fingerprint = m.Fingerprint,
                    sessions = m.SessionCount,
                    activeSessions = m.ActiveSessionCount,
                    avgCostMs = m.Cost.Mean,
                    avgEngagement = m.Engagement.Mean,
                    spikes = m.TotalSpikeContributions,
                    stalls = m.TotalStallContributions,
                });
            }

            return JsonSerializer.Serialize(new
            {
                found = true,
                mod = h.InternalName,
                displayName = h.DisplayName,
                lastVersion = h.LastVersion,
                sessionCount = h.SessionCount,
                activeSessionCount = h.ActiveSessionCount,
                lifetimeAvgCostMs = h.LifetimeAvgCostMs,
                lifetimeAvgEngagement = h.LifetimeAvgEngagement,
                totalSpikeContributions = h.TotalSpikeContributions,
                totalStallContributions = h.TotalStallContributions,
                firstSeenUtc = h.FirstSeenUtc,
                lastSeenUtc = h.LastSeenUtc,
                ring,
                byModlist,
            }, JsonOpts);
        }
        catch
        {
            return JsonSerializer.Serialize(new { found = false, mod }, JsonOpts);
        }
    }

    // ----------------------------------------------------------------------
    // /api/data-health — the store's own health + the current modlist-change diff
    // (DB rework waves 3+5). The player surface for "what's tracked" and the reset
    // control's context; the modlist-change here is the player half of the
    // dual-surface signal whose agent half is a client.log line at world load.
    // ----------------------------------------------------------------------
    private static string BuildDataHealth()
    {
        ProfilerDatabase? db = PerformanceProfiler.Database;
        if (db == null)
            return JsonSerializer.Serialize(new { available = false }, JsonOpts);

        try
        {
            var store = new HistoryStore(db);
            DataHealthView v = store.DataHealth();

            // Current modlist-change vs the previous ended session.
            string[] currentRoster = HookInterceptor.ProfiledModNames;
            List<string>? prevRoster = null;
            IReadOnlyList<RecentSessionView> recent = store.RecentSessions(1);
            if (recent.Count > 0)
            {
                // C1-class fix (second instance): an indexer+member access inside a
                // LiteDB predicate (recent[0].Fingerprint) makes the LINQ translator
                // resolve recent[0] as a document path and throw "TargetException:
                // Object does not match target type" — here it failed /api/data-health
                // on every dashboard poll. Hoist to a captured string constant.
                string prevFingerprint = recent[0].Fingerprint;
                ModlistRow? prevList = db.Modlists.FindOne(x => x.Fingerprint == prevFingerprint);
                if (prevList != null)
                {
                    prevRoster = new List<string>(prevList.Mods.Count);
                    foreach (ModEntry me in prevList.Mods) prevRoster.Add(me.Name);
                }
            }
            ModlistChange change = ModlistChange.Diff(currentRoster, prevRoster);

            return JsonSerializer.Serialize(new
            {
                available = true,
                worldLoaded = currentRoster.Length > 0,
                currentModCount = currentRoster.Length,
                sessionCount = v.SessionCount,
                endedSessionCount = v.EndedSessionCount,
                substantialSessionCount = v.SubstantialSessionCount,
                modlistCount = v.ModlistCount,
                modsTracked = v.ModsTracked,
                dbFileSizeBytes = v.DbFileSizeBytes,
                lastSessionEndedUtc = v.LastSessionEndedUtc,
                modlistChanged = change.Changed,
                modlistHadPrior = change.HadPrior,
                modsAdded = change.Added,
                modsRemoved = change.Removed,
            }, JsonOpts);
        }
        catch
        {
            return JsonSerializer.Serialize(new { available = false }, JsonOpts);
        }
    }

    /// <summary>Raw (case-preserving) query value — mod internal names are case-sensitive,
    /// unlike the lower-cased <see cref="ParseQueryValue"/> used for the reset scope.</summary>
    private static string ParseQueryValueRaw(string rawTarget, string key)
    {
        string needle = key + "=";
        int i = rawTarget.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0) return "";
        string s = rawTarget.Substring(i + needle.Length);
        int amp = s.IndexOf('&');
        if (amp >= 0) s = s.Substring(0, amp);
        return Uri.UnescapeDataString(s.Trim());
    }
}
