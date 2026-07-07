#nullable enable

using System;
using System.Text.Json;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Lifecycle;
using PerformanceProfiler.Web.Server;

namespace PerformanceProfiler.Web;

internal static partial class DashboardRouter
{
    // ----------------------------------------------------------------------
    // /api/reset?scope=everything|modlist — the player-initiated reset control
    // (DB rework wave 3, decision E). The ONLY endpoint that mutates the store.
    // A query-param GET on this loopback single-consumer server, gated by the
    // dashboard's confirm dialog; there is no link to it and no external reach,
    // so it cannot be hit by prefetch or navigation. Never a forced reset.
    // ----------------------------------------------------------------------
    private static string BuildReset(HttpRequest req)
    {
        string scope = ParseQueryValue(req.RawTarget, "scope");
        ProfilerDatabase? db = PerformanceProfiler.Database;
        if (db == null)
            return JsonSerializer.Serialize(new { ok = false, error = "no database open" }, JsonOpts);

        void Log(string m, Exception? e)
        {
            if (e == null) PerformanceProfiler.LoggerOrNull?.Info(m);
            else PerformanceProfiler.LoggerOrNull?.Warn($"{m}: {e.GetType().Name}: {e.Message}");
        }

        ResetReport report;
        switch (scope)
        {
            case "everything":
                report = StoreReset.Everything(db, Log);
                break;
            case "modlist":
                report = StoreReset.ForgetModlist(db, ModlistFingerprint.Compute(), Log);
                break;
            case "rebuild-rollup":
                // Non-destructive: recompute the lifetime rollup from raw sessions with the
                // current (fixed) fold. Corrects contaminated cross-session means without a wipe.
                report = StoreReset.RebuildRollup(db, Log);
                break;
            default:
                return JsonSerializer.Serialize(
                    new { ok = false, error = "unknown scope; use scope=everything, scope=modlist, or scope=rebuild-rollup" }, JsonOpts);
        }

        return JsonSerializer.Serialize(new
        {
            ok = report.Ok,
            scope = report.Scope,
            sessionsCleared = report.SessionsCleared,
            collectionsCleared = report.CollectionsCleared,
            error = report.Error,
        }, JsonOpts);
    }

    /// <summary>Pulls a single query value out of the raw request target (no body parsing in
    /// this server). Returns "" when absent. Lower-cased + trimmed.</summary>
    private static string ParseQueryValue(string rawTarget, string key)
    {
        string needle = key + "=";
        int i = rawTarget.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0) return "";
        string s = rawTarget.Substring(i + needle.Length);
        int amp = s.IndexOf('&');
        if (amp >= 0) s = s.Substring(0, amp);
        return s.Trim().ToLowerInvariant();
    }
}
