#nullable enable

using LiteDB;

namespace PerformanceProfiler.Profiling.Persistence.Records;

/// <summary>
/// One persisted per-(context, mod) cost distribution, keyed by the machine /
/// modlist fingerprint so baselines only ever combine across sessions of the same
/// stack. The Welford components (count, mean, M2) round-trip the running stat
/// exactly, so a prior session's distribution resumes accumulating rather than
/// being recomputed. The global per-mod stat persists under Dim=0, Key=0.
/// </summary>
public sealed class ContextBaselineRow
{
    public ObjectId Id { get; set; } = ObjectId.Empty;

    /// <summary>Machine + modlist fingerprint (ModlistFingerprint.Compute); rows only merge within one.</summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>Context dimension (0 = the session-wide global stat; 1+ = hardmode/boss/invasion/subworld).</summary>
    public byte Dim { get; set; }

    /// <summary>Context value within the dimension (e.g. an invasion id); 0 for the global stat.</summary>
    public int Key { get; set; }

    public int ModId { get; set; }

    public long Count { get; set; }
    public double Mean { get; set; }
    public double M2 { get; set; }
}
