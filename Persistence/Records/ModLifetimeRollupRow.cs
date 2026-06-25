#nullable enable

using System;
using System.Collections.Generic;
using LiteDB;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
namespace PerformanceProfiler.Persistence.Records;

/// <summary>
/// The cross-session analog of <see cref="PerSessionModAggregate"/>: one permanent row
/// per mod (<b>keyed on the stable <see cref="InternalName"/></b>, never the
/// session-local ModId index), holding that mod's whole lifetime across every modlist
/// it has ever been in. This is the global level of the two-level rollup (the
/// per-(mod, modlist) level lives in <see cref="ModModlistRollupRow"/>).
///
/// <para>
/// It is what makes "unused in your last 3 sessions" / "most spike contributions over
/// 5 sessions" / "costly despite low usage over 4" cheap: the Welford blocks bound the
/// read cost (O(1) per metric, no raw re-scan) and the bounded <see cref="Ring"/>
/// carries the last <see cref="RingCapacity"/> per-session summaries so a recency
/// window ("last N") and a version timeline are both recoverable without touching the
/// per-session collections. Maintained once per session at the session-end fold
/// (off the hot path — Invariant 2). Permanent and tiny, so retention never deletes it
/// (decision D); the player's reset control is the only thing that clears it.
/// </para>
///
/// <para>
/// Identity is <see cref="Mod.Name"/> — a generic loader surface, never a hard-coded
/// mod string (Invariant 5). A mod that renames itself splits its own history; that
/// rare edge is surfaced by the version timeline, not silently merged.
/// </para>
/// </summary>
public sealed class ModLifetimeRollupRow
{
    /// <summary>How many recent per-session summaries the ring retains — enough to answer
    /// "last N sessions" for the windows the detectors use (3 / 5 / 10) with headroom.</summary>
    public const int RingCapacity = 30;

    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    /// <summary>The stable cross-session key: the mod's internal name (Mod.Name).</summary>
    public string InternalName { get; set; } = string.Empty;

    /// <summary>Last-seen friendly name, for display only — never a key.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Last-seen version string, for the "current version" badge; the per-session
    /// versions live in the <see cref="Ring"/> so a version boundary stays detectable.</summary>
    public string LastVersion { get; set; } = string.Empty;

    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }

    /// <summary>Total sessions this mod was present for (whether active or idle).</summary>
    public int SessionCount { get; set; }

    /// <summary>Sessions in which the mod did measurable work (cost above the active floor).
    /// The complement (SessionCount − ActiveSessionCount) is its idle-but-loaded tally.</summary>
    public int ActiveSessionCount { get; set; }

    /// <summary>Lifetime distribution of the mod's per-session average frame cost (ms).</summary>
    public WelfordStat Cost { get; set; } = new();

    /// <summary>Lifetime distribution of the mod's per-session average allocation (bytes).</summary>
    public WelfordStat Alloc { get; set; } = new();

    /// <summary>Lifetime distribution of the mod's per-session engagement score (0 when the
    /// session had no engagement signal for it; see the fold for how the score is derived).</summary>
    public WelfordStat Engagement { get; set; } = new();

    /// <summary>Lifetime count of spike windows this mod appeared in as a top contributor.</summary>
    public long TotalSpikeContributions { get; set; }

    /// <summary>Lifetime count of stall clusters this mod dominated.</summary>
    public long TotalStallContributions { get; set; }

    /// <summary>The bounded ring of recent per-session summaries (newest last), capped at
    /// <see cref="RingCapacity"/>. The recency-window and version-timeline substrate.</summary>
    public List<SessionRingEntry> Ring { get; set; } = new();
}

/// <summary>
/// One per-session summary of a mod's behaviour, embedded in
/// <see cref="ModLifetimeRollupRow.Ring"/>. Small and flat so a long ring stays cheap;
/// it carries exactly what the cross-session detectors read off the recency window
/// (was-it-active, spike count, cost, version, which stack) without re-opening the
/// per-session collections.
/// </summary>
public sealed class SessionRingEntry
{
    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public ObjectId? WorldId { get; set; }

    /// <summary>The modlist fingerprint this session ran under — the per-modlist dimension
    /// (decision: fingerprint is a tag/analysis dimension here, never the identity).</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>The mod's version during this session, so a version boundary in the ring is
    /// a first-class detectable event (feeds regression tracking on the roadmap).</summary>
    public string Version { get; set; } = string.Empty;

    public DateTime EndedUtc { get; set; }
    public long TicksObserved { get; set; }

    public double CostMs { get; set; }
    public double AllocBytes { get; set; }
    public double EngagementScore { get; set; }
    public int SpikeContributions { get; set; }
    public int StallContributions { get; set; }

    /// <summary>True when the mod did measurable work this session; the "unused in last N"
    /// detector reads a run of false values across the recent ring.</summary>
    public bool WasActive { get; set; }
}
