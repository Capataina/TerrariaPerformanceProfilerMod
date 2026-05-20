#nullable enable

using System;
using System.Collections.Generic;
using LiteDB;

namespace PerformanceProfiler.Profiling.Persistence.Records;

/// <summary>
/// One row per <c>(modlistFingerprint, modInternalName)</c>. Tracks version
/// churn within a stable modlist across time so "did this mod's perf change
/// after the v1.2 update" is a queryable.
/// </summary>
public sealed class ModRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public string ModlistFingerprint { get; set; } = string.Empty;
    public string InternalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string VersionSeen { get; set; } = string.Empty;
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public List<ModVersionEntry> VersionHistory { get; set; } = new();
}

public sealed class ModVersionEntry
{
    public string Version { get; set; } = string.Empty;
    public DateTime FirstUtc { get; set; }
    public DateTime LastUtc { get; set; }
}
