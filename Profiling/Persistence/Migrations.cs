#nullable enable

using System;
using LiteDB;

namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// Schema migrations between LiteDB user-versions. Each step is idempotent
/// so a crashed migration retries safely. Today there is only version 1
/// (the migration target); the structure exists to absorb future schema
/// changes without rewriting the bring-up path.
/// </summary>
internal static class Migrations
{
    public static void Apply(LiteDatabase db, int fromVersion, int toVersion, Action<string, Exception?> log)
    {
        if (fromVersion >= toVersion) return;
        for (int v = fromVersion; v < toVersion; v++)
        {
            try
            {
                Step(db, v);
                log($"ProfilerDatabase: migrated user_version {v} -> {v + 1}", null);
            }
            catch (Exception ex)
            {
                log($"ProfilerDatabase: migration {v} -> {v + 1} failed", ex);
                throw;
            }
        }
    }

    private static void Step(LiteDatabase db, int from)
    {
        switch (from)
        {
            // case 1: MigrateV1ToV2(db); break;
            default:
                break;
        }
    }
}
