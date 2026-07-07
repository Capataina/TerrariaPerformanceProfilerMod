#nullable enable

using System;
using System.IO;
using System.Text.Json;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Report;

namespace PerformanceProfiler.Web;

internal static partial class DashboardRouter
{
    // ----------------------------------------------------------------------
    // /api/export-report — write the self-contained HTML session report
    // (atlas S17) for the most recent COMPLETED session and return its path.
    // The live session has no archive row yet, so mid-session exports serve
    // the previous session — the response says which.
    // ----------------------------------------------------------------------
    private static string BuildExportReport()
    {
        try
        {
            ProfilerDatabase? db = PerformanceProfiler.Database;
            if (db == null)
            {
                return JsonSerializer.Serialize(new { ok = false, error = "profiler database is not open" }, JsonOpts);
            }

            var last = DbReadModel.GetLastSession();
            if (last == null)
            {
                return JsonSerializer.Serialize(new { ok = false, error = "no completed session in the store yet" }, JsonOpts);
            }

            string? path = ReportExporter.ExportSession(db, last.Archive.SessionId);
            if (path == null)
            {
                return JsonSerializer.Serialize(new { ok = false, error = "session data incomplete; report not written" }, JsonOpts);
            }

            return JsonSerializer.Serialize(new
            {
                ok = true,
                path,
                sessionEndedUtc = last.EndedUtc.ToString("yyyy-MM-dd HH:mm"),
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { ok = false, error = $"{ex.GetType().Name}: {ex.Message}" }, JsonOpts);
        }
    }
}
