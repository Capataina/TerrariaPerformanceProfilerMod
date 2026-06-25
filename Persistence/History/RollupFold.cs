#nullable enable

using System;
using LiteDB;
using PerformanceProfiler.Persistence.Records;

namespace PerformanceProfiler.Persistence.History;

/// <summary>
/// The pure session-end fold: folds one <see cref="SessionRollupInput"/> into the
/// two-level rollup (the global <see cref="ModLifetimeRollupRow"/> + the per-modlist
/// <see cref="ModModlistRollupRow"/>). No I/O, no game-thread state, no Terraria types —
/// the writer thread reads the existing rows, calls these methods, writes them back, so
/// the maths is unit-testable against synthetic inputs (the L1 axis).
///
/// <para>
/// <b>Replay idempotency.</b> A fold is not naturally idempotent (folding a session
/// twice double-counts). Journal replay re-runs the <c>RollupFold</c> op, so before
/// folding the writer thread asks <see cref="AlreadyFolded"/>: if the session is already
/// in the global row's ring, the whole fold is skipped. The ring window
/// (<see cref="ModLifetimeRollupRow.RingCapacity"/>) comfortably covers the un-checkpointed
/// replay tail, so this is exact for replay. The one-time backfill is marker-guarded
/// separately (it folds sessions older than the ring once, oldest-first).
/// </para>
/// </summary>
public static class RollupFold
{
    /// <summary>Per-session average frame-cost contribution below this counts as "not active"
    /// (loaded but idle) — ~0.5 µs, the renderer's sub-tick noise floor order.</summary>
    public const double ActiveCostFloorMs = 0.0005d;

    /// <summary>True when this session is already represented in the row's ring, so a replayed
    /// or duplicate fold must be skipped to avoid double-counting.</summary>
    public static bool AlreadyFolded(ModLifetimeRollupRow row, ObjectId sessionId)
    {
        for (int i = 0; i < row.Ring.Count; i++)
        {
            if (row.Ring[i].SessionId == sessionId) return true;
        }
        return false;
    }

    /// <summary>
    /// Folds one mod's session contribution into its global lifetime row (mutated in
    /// place). The caller has already created a fresh row for a first-seen mod and
    /// checked <see cref="AlreadyFolded"/>.
    /// </summary>
    public static void FoldGlobal(ModLifetimeRollupRow row, SessionRollupInput session, ModSessionContribution c)
    {
        if (string.IsNullOrEmpty(row.InternalName)) row.InternalName = c.InternalName;
        if (!string.IsNullOrEmpty(c.DisplayName)) row.DisplayName = c.DisplayName;
        else if (string.IsNullOrEmpty(row.DisplayName)) row.DisplayName = c.InternalName;
        row.LastVersion = c.Version;

        if (row.SessionCount == 0 || session.EndedUtc < row.FirstSeenUtc) row.FirstSeenUtc = session.EndedUtc;
        if (session.EndedUtc > row.LastSeenUtc) row.LastSeenUtc = session.EndedUtc;

        row.SessionCount++;
        if (c.WasActive) row.ActiveSessionCount++;
        row.Cost.FoldSample(c.CostMs);
        row.Alloc.FoldSample(c.AllocBytes);
        row.Engagement.FoldSample(c.EngagementScore);
        row.TotalSpikeContributions += c.SpikeContributions;
        row.TotalStallContributions += c.StallContributions;

        row.Ring.Add(new SessionRingEntry
        {
            SessionId = session.SessionId,
            WorldId = session.WorldId,
            Fingerprint = session.Fingerprint,
            Version = c.Version,
            EndedUtc = session.EndedUtc,
            TicksObserved = session.TicksObserved,
            CostMs = c.CostMs,
            AllocBytes = c.AllocBytes,
            EngagementScore = c.EngagementScore,
            SpikeContributions = c.SpikeContributions,
            StallContributions = c.StallContributions,
            WasActive = c.WasActive,
        });
        TrimRing(row);
    }

    /// <summary>Folds one mod's session contribution into its per-modlist row (mutated in place).</summary>
    public static void FoldModlist(ModModlistRollupRow row, SessionRollupInput session, ModSessionContribution c)
    {
        if (string.IsNullOrEmpty(row.InternalName)) row.InternalName = c.InternalName;
        if (string.IsNullOrEmpty(row.Fingerprint)) row.Fingerprint = session.Fingerprint;

        if (row.SessionCount == 0 || session.EndedUtc < row.FirstSeenUtc) row.FirstSeenUtc = session.EndedUtc;
        if (session.EndedUtc > row.LastSeenUtc) row.LastSeenUtc = session.EndedUtc;

        row.SessionCount++;
        if (c.WasActive) row.ActiveSessionCount++;
        row.Cost.FoldSample(c.CostMs);
        row.Alloc.FoldSample(c.AllocBytes);
        row.Engagement.FoldSample(c.EngagementScore);
        row.TotalSpikeContributions += c.SpikeContributions;
        row.TotalStallContributions += c.StallContributions;
    }

    /// <summary>Trims the ring to its capacity, keeping the newest entries (drops from the front).</summary>
    public static void TrimRing(ModLifetimeRollupRow row)
    {
        int over = row.Ring.Count - ModLifetimeRollupRow.RingCapacity;
        if (over > 0) row.Ring.RemoveRange(0, over);
    }
}
