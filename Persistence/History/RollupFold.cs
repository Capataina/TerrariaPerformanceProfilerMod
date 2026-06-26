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

    /// <summary>The minimum ticks a session must observe before its per-mod cost / alloc /
    /// engagement averages are allowed into the lifetime distributions (~30 s at 60 tps).
    /// A world-load window of only a few hundred ticks (345 and 390 observed in play-tests,
    /// ~6 s of simulation) divides one-time JIT and asset-load cost by a tiny tick
    /// denominator, producing absurd per-tick averages (one mod read 121 ms/tick across a
    /// 390-tick window) that drag every mod's lifetime mean upward in lockstep when folded
    /// equal-weight. The threshold sits above those load windows yet below a genuine short
    /// session (the real 2951 and 3814-tick sessions, ~1 min, still fold).</summary>
    public const long MinSessionTicks = 1800;

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

        // Only substantial sessions fold into the cost / alloc / engagement distributions,
        // and they fold tick-weighted so a long session dominates the lifetime average in
        // proportion to how long it was actually played (see MinSessionTicks). A thin
        // load-window session is excluded entirely from the three means, so row.Cost.Count
        // (substantial sessions folded) can fall below row.SessionCount (all sessions
        // present); that divergence is intended, not a bug.
        if (session.TicksObserved >= MinSessionTicks)
        {
            row.Cost.FoldSampleWeighted(c.CostMs, session.TicksObserved);
            row.Alloc.FoldSampleWeighted(c.AllocBytes, session.TicksObserved);
            row.Engagement.FoldSampleWeighted(c.EngagementScore, session.TicksObserved);
        }

        // Spike and stall contributions are event counts, not divided-by-ticks averages, so a
        // thin session's memberships are real and always count; and the presence record
        // (SessionCount plus the ring entry below) is what "unused in your last N sessions"
        // reads, so a thin session must still register as a session the mod was present for.
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

        // Same substance gate as FoldGlobal: only sessions of real length fold their cost /
        // alloc / engagement (tick-weighted) into this stack's distributions; thin load
        // windows still register as a session present (SessionCount) and keep their spike /
        // stall event tallies.
        if (session.TicksObserved >= MinSessionTicks)
        {
            row.Cost.FoldSampleWeighted(c.CostMs, session.TicksObserved);
            row.Alloc.FoldSampleWeighted(c.AllocBytes, session.TicksObserved);
            row.Engagement.FoldSampleWeighted(c.EngagementScore, session.TicksObserved);
        }
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
