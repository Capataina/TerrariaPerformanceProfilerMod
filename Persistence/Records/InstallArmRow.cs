#nullable enable

using System;
using LiteDB;

namespace PerformanceProfiler.Persistence.Records;

/// <summary>
/// One hook-install measurement (S04 memory guard): written once per
/// interceptor install — i.e. once per mod-(re)load — at PostSetupContent.
/// The reload-stack comparator reads the rows sharing a <see cref="ProcessKey"/>:
/// tModLoader's Reload Mods keeps the PROCESS and swaps assembly-load
/// contexts, so install residue that the old ALC pins shows up here as the
/// install delta STAIRCASING at constant hook count — the exact signature the
/// 2026-07-07 live session recorded (1.82 → 2.46 GB, 30 → 40.5 KB/hook, same
/// 62,203 hooks, two reloads).
/// </summary>
public sealed class InstallArmRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();

    /// <summary>Identity of the OS process this arm happened in: "pid:startTicksUtc".</summary>
    public string ProcessKey { get; set; } = string.Empty;

    /// <summary>1-based install count within the process (1 = first load, 2+ = reloads).</summary>
    public int ArmIndex { get; set; }

    public DateTime ArmedUtc { get; set; }
    public long InstallDeltaBytes { get; set; }
    public long BytesPerHook { get; set; }
    public int HookCount { get; set; }
    public string ProfilerVersion { get; set; } = string.Empty;
}
