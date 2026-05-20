#nullable enable

using System;
using System.Collections.Generic;
using LiteDB;

namespace PerformanceProfiler.Profiling.Persistence.Records;

/// <summary>One row per distinct sorted modlist; deduped on <see cref="Fingerprint"/>.</summary>
public sealed class ModlistRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public string Fingerprint { get; set; } = string.Empty;
    public string FingerprintAlg { get; set; } = "sha256-of-sorted-id-name-version-v1";
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public int SessionCount { get; set; }
    public List<ModEntry> Mods { get; set; } = new();
}

public sealed class ModEntry
{
    public int ModId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}
