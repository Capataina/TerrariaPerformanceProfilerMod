#nullable enable

namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// File-name constants shared by the persistence layer. Lives in its own
/// file so tests can pick it up without dragging in <see cref="ProfilerPaths"/>'s
/// Terraria dependency.
/// </summary>
public static class PersistenceFileNames
{
    public const string Db        = "profiler.litedb";
    public const string Journal   = "profiler.events.log";
    public const string BackupPrefix = "profiler.litedb.bak-";
    public const string BrokenPrefix = "profiler.litedb.broken-";
    public const string LegacySessions = "Sessions";
}
