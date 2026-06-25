#nullable enable

using LiteDB;

namespace PerformanceProfiler.Persistence.Records;

/// <summary>
/// One persisted per-(context, mod) cost distribution. Identity is the stable
/// <see cref="InternalName"/> (+ <see cref="ModVersion"/>), so a mod's per-context
/// baseline carries forward across modlist edits instead of resetting; the
/// <see cref="Fingerprint"/> is retained as an analysis dimension (which stack the
/// distribution was gathered on, for cross-modpack comparison), not as the merge key.
/// The Welford components (count, mean, M2) round-trip the running stat exactly, so a
/// prior session's distribution resumes accumulating rather than being recomputed.
/// The global per-mod stat persists under Dim=0, Key=0.
/// </summary>
public sealed class ContextBaselineRow
{
    public ObjectId Id { get; set; } = ObjectId.Empty;

    /// <summary>The stable cross-session key: the mod's internal name (Mod.Name). Empty on
    /// legacy rows written before the identity rework — those are matched by
    /// <see cref="Fingerprint"/> + <see cref="ModId"/> as a fallback.</summary>
    public string InternalName { get; set; } = "";

    /// <summary>The mod's version when this distribution was gathered, so a version boundary
    /// in a baseline is flagged rather than silently pooled (Invariant 3).</summary>
    public string ModVersion { get; set; } = "";

    /// <summary>Machine + modlist fingerprint (ModlistFingerprint.Compute) — an analysis
    /// dimension (which stack), no longer the merge partition.</summary>
    public string Fingerprint { get; set; } = "";

    /// <summary>Context dimension (0 = the session-wide global stat; 1+ = hardmode/boss/invasion/subworld).</summary>
    public byte Dim { get; set; }

    /// <summary>Context value within the dimension (e.g. an invasion id); 0 for the global stat.</summary>
    public int Key { get; set; }

    /// <summary>The session-local load-order index at write time — informational; not a key.</summary>
    public int ModId { get; set; }

    public long Count { get; set; }
    public double Mean { get; set; }
    public double M2 { get; set; }
}
