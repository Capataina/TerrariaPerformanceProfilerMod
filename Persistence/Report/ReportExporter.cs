#nullable enable

using System;
using System.IO;
using LiteDB;

namespace PerformanceProfiler.Persistence.Report;

/// <summary>
/// The IO wrapper around the pure reader + writer (S17): assembles a
/// session's report and saves it under <c>reports/</c> beside the store.
/// Every caller (dashboard button, chat command, auto-export) funnels here.
/// </summary>
public static class ReportExporter
{
    /// <summary>
    /// Export one session's report. Returns the written path, or null when the
    /// session has no archive row yet (unended / crash-cut).
    /// </summary>
    public static string? ExportSession(ProfilerDatabase db, ObjectId sessionId)
    {
        SessionReportData? data = SessionReportReader.Read(db, sessionId);
        if (data == null) return null;

        string html = HtmlReportWriter.Render(data);
        string dir = Path.Combine(db.Root, "reports");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir,
            $"session-{data.StartedUtc:yyyyMMdd-HHmm}-{sessionId.ToString()[..8]}.html");
        File.WriteAllText(file, html);
        return file;
    }
}
