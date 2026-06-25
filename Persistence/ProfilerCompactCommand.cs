#nullable enable

using System;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Persistence;

/// <summary>
/// Manual compaction trigger. Runs LiteDB <c>Checkpoint</c> then <c>Rebuild</c>
/// — the equivalent of SQLite's <c>VACUUM</c>. Reclaims free pages from
/// deleted rows (especially warm-tier expirations) so a long-running player's
/// DB doesn't drift toward unbounded size.
///
/// Always safe to invoke between sessions; the command refuses to run while
/// a world is loaded because <c>Rebuild()</c> is not concurrency-safe with
/// the writer thread's burst writes.
/// </summary>
public class ProfilerCompactCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat;
    public override string Command => "profiler-compact";
    public override string Usage => "/profiler-compact";
    public override string Description => "Compact the Performance Profiler database (LiteDB Rebuild).";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        var db = PerformanceProfiler.Database;
        if (db == null)
        {
            caller.Reply("Profiler DB is not open this session; nothing to compact.");
            return;
        }

        if (Terraria.Main.gameMenu == false)
        {
            caller.Reply("Compact must be run from the main menu, not from inside a world.");
            return;
        }

        long before = db.DbFileSize;
        try
        {
            long pagesRebuilt = db.Compact();
            long after = db.DbFileSize;
            caller.Reply($"Compacted. Rebuilt {pagesRebuilt} pages. {before / 1024} KB -> {after / 1024} KB.");
            Mod.Logger.Info($"profiler-compact: {before} -> {after} bytes ({pagesRebuilt} pages rebuilt).");
        }
        catch (Exception ex)
        {
            caller.Reply($"Compact failed: {ex.GetType().Name}: {ex.Message}");
            Mod.Logger.Warn($"profiler-compact failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
