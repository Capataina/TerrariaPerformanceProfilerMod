#nullable enable

using System;
using LiteDB;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Profiling.Persistence.Commands;

/// <summary>
/// Shared helpers for chat commands that read the profiler DB. Resolves
/// the current/latest session id, formats wall-clock + duration in a
/// chat-friendly way, and replies safely if the DB isn't open.
/// </summary>
internal static class QueryCommandBase
{
    /// <summary>
    /// Returns the DB if open, otherwise null. Caller is expected to
    /// reply with a friendly "DB not open" message and exit.
    /// </summary>
    public static ProfilerDatabase? GetDb(CommandCaller caller)
    {
        var db = PerformanceProfiler.Database;
        if (db == null)
            caller.Reply("Profiler DB is not open this session.");
        return db;
    }

    /// <summary>
    /// Resolve the session id the caller most likely wants: either the live
    /// session (if a world is loaded and the recorder is running) or the
    /// most recent session row in the DB.
    /// </summary>
    public static ObjectId? CurrentOrLatestSessionId(ProfilerDatabase db)
    {
        // Try the live recorder first (most recent context).
        var system = ModContent.GetInstance<ProfilerSystem>();
        if (system != null)
        {
            var live = system.LiveRecorderSessionId;
            if (live != null) return live;
        }

        var latest = db.Sessions.Query()
            .OrderByDescending(x => x.StartedUtc)
            .Limit(1)
            .FirstOrDefault();
        return latest?.Id;
    }

    public static string FormatDuration(double ms)
    {
        if (ms < 1000d) return $"{ms:F0} ms";
        double s = ms / 1000d;
        if (s < 60d) return $"{s:F1} s";
        double m = s / 60d;
        if (m < 60d) return $"{m:F1} min";
        return $"{m / 60d:F1} h";
    }

    public static int ParseCountArg(string[] args, int fallback)
    {
        if (args.Length > 0 && int.TryParse(args[0], out int n) && n > 0) return Math.Min(n, 50);
        return fallback;
    }

    /// <summary>
    /// Wrap a chat-command body so any unexpected throw is reported as a
    /// clean caller.Reply line + a single Warn in client.log, instead of
    /// tML's default "An error occurred running command X. See client.log
    /// for details" wrapper which is intimidating to players. v0.6.1
    /// hardening for the case where a v0.5 session row in the DB has
    /// long-name BSON fields that the v0.6 short-name mapper can't read
    /// — the query path silently returns empty/null and the chat command
    /// then NullRefs trying to format it. Now the user sees "no data" or
    /// a clear error instead.
    /// </summary>
    public static void SafeRun(CommandCaller caller, string commandName, Action body)
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            caller.Reply($"/{commandName}: {ex.GetType().Name}: {ex.Message}");
            PerformanceProfiler.LoggerOrNull?.Warn(
                $"chat-command /{commandName} threw: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
