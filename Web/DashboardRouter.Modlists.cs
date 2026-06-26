#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
using PerformanceProfiler.Web.Server;

namespace PerformanceProfiler.Web;

internal static partial class DashboardRouter
{
    // ----------------------------------------------------------------------
    // /api/modlist-history — the roster-evolution matrix. Every distinct modlist
    // the player has run is one column (time-ordered, oldest first); the union of
    // every mod ever seen is the rows. The matrix is the per-(mod, list) version
    // string — "" means absent from that roster — so the client derives the three
    // cell states (present / absent / version-changed) and the add/remove block
    // edges itself. Read-only over the persisted `modlists` collection (Invariant
    // 1); any failure returns {available:false} rather than throwing (Invariant 4).
    // ----------------------------------------------------------------------
    private static string BuildModlistHistory()
    {
        ProfilerDatabase? db = PerformanceProfiler.Database;
        if (db == null)
            return JsonSerializer.Serialize(new { available = false }, JsonOpts);

        try
        {
            // Columns of the matrix: every roster, oldest first (a timeline).
            var rows = new List<ModlistRow>(db.Modlists.FindAll());
            if (rows.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    available = true,
                    modlists = Array.Empty<object>(),
                    mods = Array.Empty<object>(),
                    versions = Array.Empty<object>(),
                }, JsonOpts);
            }
            rows.Sort((a, b) => a.FirstSeenUtc.CompareTo(b.FirstSeenUtc));

            // Union of every mod name ever seen + how many rosters it appears in
            // and where it first appears — both drive a deterministic row order so
            // the same store always renders the same matrix (reproducibility).
            var presentCount = new Dictionary<string, int>();
            var firstSeenIdx = new Dictionary<string, int>();
            // Per-list name -> version lookup, built once for the cell fill below.
            var listMaps = new List<Dictionary<string, string>>(rows.Count);
            for (int li = 0; li < rows.Count; li++)
            {
                var map = new Dictionary<string, string>(rows[li].Mods.Count);
                foreach (ModEntry me in rows[li].Mods)
                {
                    presentCount.TryGetValue(me.Name, out int c);
                    presentCount[me.Name] = c + 1;
                    if (!firstSeenIdx.ContainsKey(me.Name)) firstSeenIdx[me.Name] = li;
                    map[me.Name] = me.Version ?? string.Empty;
                }
                listMaps.Add(map);
            }

            // Row order: present in the most rosters first (the stable core floats
            // to the top), then earliest appearance, then name — fully ordinal so
            // it is identical run to run.
            var names = new List<string>(presentCount.Keys);
            names.Sort((a, b) =>
            {
                int byCount = presentCount[b].CompareTo(presentCount[a]);
                if (byCount != 0) return byCount;
                int byFirst = firstSeenIdx[a].CompareTo(firstSeenIdx[b]);
                if (byFirst != 0) return byFirst;
                return string.CompareOrdinal(a, b);
            });

            var modlists = new List<object>(rows.Count);
            foreach (ModlistRow row in rows)
            {
                string fp = row.Fingerprint ?? string.Empty;
                modlists.Add(new
                {
                    fingerprint = fp,
                    shortFp = fp.Length > 8 ? fp.Substring(0, 8) : fp,
                    firstSeenUtc = row.FirstSeenUtc,
                    lastSeenUtc = row.LastSeenUtc,
                    sessionCount = row.SessionCount,
                    modCount = row.Mods.Count,
                });
            }

            var mods = new List<object>(names.Count);
            var versions = new List<object>(names.Count);     // versions[modIndex] = string[listCount]
            foreach (string name in names)
            {
                mods.Add(new { name, presentCount = presentCount[name] });
                var vrow = new string[rows.Count];
                for (int li = 0; li < rows.Count; li++)
                    vrow[li] = listMaps[li].TryGetValue(name, out string? v) ? v : string.Empty;
                versions.Add(vrow);
            }

            return JsonSerializer.Serialize(new
            {
                available = true,
                modlists,
                mods,
                versions,
            }, JsonOpts);
        }
        catch
        {
            return JsonSerializer.Serialize(new { available = false }, JsonOpts);
        }
    }
}
