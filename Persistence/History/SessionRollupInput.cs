#nullable enable

using System;
using System.Collections.Generic;
using LiteDB;

namespace PerformanceProfiler.Persistence.History;

/// <summary>
/// What one finished session contributes to the cross-session rollup, per mod. Built
/// once at session-end from the per-session aggregates + the spike/stall tallies +
/// the live usage snapshot, then handed to the writer thread as a single
/// <c>DbWriteOp.RollupFold</c> payload. The fold (<see cref="RollupFold"/>) reads this
/// against the existing rollup rows and updates them in place.
///
/// <para>
/// A plain System.Text.Json-serialisable DTO (public properties only — the journal's
/// serialiser has <c>IncludeFields=false</c>) so it round-trips through journal replay:
/// the fold is replayed from this exact payload, and the ring-dedup on
/// <see cref="SessionId"/> keeps replay idempotent.
/// </para>
/// </summary>
public sealed class SessionRollupInput
{
    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public ObjectId? WorldId { get; set; }

    /// <summary>The modlist fingerprint this session ran under (the per-modlist dimension).</summary>
    public string Fingerprint { get; set; } = string.Empty;

    public DateTime EndedUtc { get; set; }
    public long TicksObserved { get; set; }

    public List<ModSessionContribution> Mods { get; set; } = new();
}

/// <summary>
/// One mod's contribution to a single session — the per-(session, mod) summary the fold
/// folds into both rollup levels. Identity is <see cref="InternalName"/> (Mod.Name),
/// never a load-order index.
/// </summary>
public sealed class ModSessionContribution
{
    public string InternalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    /// <summary>Per-session average frame cost contribution (ms).</summary>
    public double CostMs { get; set; }

    /// <summary>Per-session average allocation (bytes).</summary>
    public double AllocBytes { get; set; }

    /// <summary>Per-session weighted usage/engagement count (0 when no engagement signal —
    /// e.g. backfilled historical sessions, which never captured usage).</summary>
    public double EngagementScore { get; set; }

    /// <summary>Spike windows this mod was a top contributor to, this session.</summary>
    public int SpikeContributions { get; set; }

    /// <summary>Stall clusters this mod dominated, this session.</summary>
    public int StallContributions { get; set; }

    /// <summary>True when the mod did measurable work this session (cost above the active floor).</summary>
    public bool WasActive { get; set; }
}
