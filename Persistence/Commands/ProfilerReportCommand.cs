#nullable enable

using System;
using Terraria.ModLoader;
using PerformanceProfiler.Persistence.Report;

namespace PerformanceProfiler.Persistence.Commands;

/// <summary>
/// Chat trigger for the HTML session report (atlas S17): exports the most
/// recent COMPLETED session — mid-session that is the previous one, since the
/// live session has no archive row until it ends — and replies with the path.
/// </summary>
public class ProfilerReportCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat;
    public override string Command => "profiler-report";
    public override string Usage => "/profiler-report";
    public override string Description => "Write the shareable HTML report for the last completed session.";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        var db = PerformanceProfiler.Database;
        if (db == null)
        {
            caller.Reply("Profiler DB is not open this session; no report source.");
            return;
        }
        var last = DbReadModel.GetLastSession();
        if (last == null)
        {
            caller.Reply("No completed session in the store yet — finish a session first.");
            return;
        }
        try
        {
            string? path = ReportExporter.ExportSession(db, last.Archive.SessionId);
            caller.Reply(path != null
                ? $"Report written: {path}"
                : "Session data incomplete; report not written.");
        }
        catch (Exception ex)
        {
            caller.Reply($"Report failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
