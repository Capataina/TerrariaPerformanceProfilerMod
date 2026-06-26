#nullable enable

using System;
using System.Collections.Generic;
using LiteDB;
using PerformanceProfiler.Persistence.Records;

namespace PerformanceProfiler.Persistence.History;

/// <summary>
/// The full cross-session story of one mod: its lifetime rollup, the recent session
/// ring (newest first), and its per-modlist breakdown. What the Observatory drawer's
/// lifetime view and the per-mod cross-session detectors read.
/// </summary>
public sealed class ModHistory
{
    public string InternalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string LastVersion { get; set; } = string.Empty;

    public int SessionCount { get; set; }
    public int ActiveSessionCount { get; set; }
    public double LifetimeAvgCostMs { get; set; }
    public double LifetimeAvgEngagement { get; set; }
    public long TotalSpikeContributions { get; set; }
    public long TotalStallContributions { get; set; }
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    /// <summary>The mod's recent per-session summaries, newest first, capped at the requested window.</summary>
    public IReadOnlyList<SessionRingEntry> RecentRing { get; set; } = Array.Empty<SessionRingEntry>();

    /// <summary>One entry per stack this mod has been in — the cross-modpack breakdown.</summary>
    public IReadOnlyList<ModModlistRollupRow> ByModlist { get; set; } = Array.Empty<ModModlistRollupRow>();
}

/// <summary>
/// A mod's behaviour aggregated over a recency window (its last-N ring entries) — the
/// primitive the cross-session detectors rank and threshold over. Window-relative, so
/// "last 3 sessions" and "last 5 sessions" are different <see cref="ModWindowStats"/>
/// from the same rollup.
/// </summary>
public sealed class ModWindowStats
{
    public string InternalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string LastVersion { get; set; } = string.Empty;

    /// <summary>Ring entries inside the window (≤ requested N, ≤ what the mod actually has).</summary>
    public int SessionsInWindow { get; set; }

    /// <summary>Of those, how many the mod did measurable work in.</summary>
    public int ActiveSessions { get; set; }

    public double AvgCostMs { get; set; }
    public double AvgAllocBytes { get; set; }
    public double AvgEngagement { get; set; }
    public long SpikeContributions { get; set; }
    public long StallContributions { get; set; }
    public DateTime LastSeenUtc { get; set; }

    /// <summary>Fraction of the window the mod was active; ~0 is the "unused" signal.</summary>
    public double ActiveFraction => SessionsInWindow > 0 ? (double)ActiveSessions / SessionsInWindow : 0d;

    /// <summary>Cost per unit engagement — high with low engagement is the "costly despite low
    /// usage" signal. PositiveInfinity when there is cost but no engagement signal at all.</summary>
    public double CostPerEngagement =>
        AvgEngagement > 1e-9 ? AvgCostMs / AvgEngagement : (AvgCostMs > 0d ? double.PositiveInfinity : 0d);
}

/// <summary>A lightweight view of one persisted session, for the dashboard's "last N sessions" lens.</summary>
public sealed class RecentSessionView
{
    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public string Mode { get; set; } = string.Empty;
    public long TicksObserved { get; set; }
}

/// <summary>
/// The store's own health, for the data-health view + the reset control's context: how
/// much history is tracked and how big the file is, so the "this session" vs "lifetime
/// data" badges are legible.
/// </summary>
public sealed class DataHealthView
{
    public int SessionCount { get; set; }
    public int EndedSessionCount { get; set; }

    /// <summary>Of the ended sessions, how many cleared <see cref="RollupFold.MinSessionTicks"/>
    /// (~30 s of simulation) and so feed the lifetime averages — the thin remainder are
    /// world-load windows excluded from the rollup. Lets the player see how trustworthy a
    /// lifetime number is: how much tracked history is substantial vs too short to count.</summary>
    public int SubstantialSessionCount { get; set; }

    public int ModlistCount { get; set; }
    public int ModsTracked { get; set; }
    public long DbFileSizeBytes { get; set; }
    public DateTime? LastSessionEndedUtc { get; set; }
}
