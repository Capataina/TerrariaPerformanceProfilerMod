#nullable enable

using System;
using System.IO;
using Terraria;

namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// Cross-platform path resolution for the profiler's persistence layer.
/// Mirrors the layout described in §6.1 of the migration plan.
///
/// Resolves under tModLoader's own SavePath (the same root the previous
/// JSON writer used via <c>Main.SavePath</c>), keeping the data near the
/// player's save files regardless of OS — macOS Library, Windows
/// Documents\My Games, Linux .local/share.
/// </summary>
internal static class ProfilerPaths
{
    private const string FolderName = "PerformanceProfiler";

    public const string DbFileName       = PersistenceFileNames.Db;
    public const string JournalFileName  = PersistenceFileNames.Journal;
    public const string BackupPrefix     = PersistenceFileNames.BackupPrefix;
    public const string BrokenPrefix     = PersistenceFileNames.BrokenPrefix;
    public const string LegacySessions   = PersistenceFileNames.LegacySessions;

    /// <summary>The persistence root for this install. Created on demand.</summary>
    public static string Root()
    {
        string parent = Main.SavePath ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(parent, FolderName);
    }

    public static string DbPath()       => Path.Combine(Root(), DbFileName);
    public static string JournalPath()  => Path.Combine(Root(), JournalFileName);
    public static string BackupPath(int n) => Path.Combine(Root(), BackupPrefix + n.ToString());

    public static void EnsureDirectory()
    {
        string root = Root();
        if (!Directory.Exists(root)) Directory.CreateDirectory(root);
    }
}
