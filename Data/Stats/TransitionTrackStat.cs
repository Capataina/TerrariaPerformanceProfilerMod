#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;

namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// Timeline T3 — projects <c>contextTransitions</c> rows for the current
/// session onto the swimlane time domain.
///
/// <para>
/// <see cref="ContextTransitionRow"/> only stores the game-tick index (no
/// wall-clock UnixMs), so we derive <see cref="TransitionEntry.UnixMs"/>
/// from <see cref="SessionRow.StartedUtc"/> + <c>tick × (1000/60)</c>. The
/// 60 Hz assumption is the same approximation used by
/// <c>HeatmapAggregator</c> for warm-tier ticks; it is good enough for the
/// timeline's swimlane projection, where tick-level precision is not
/// needed.
/// </para>
///
/// <para>
/// The row's <see cref="ContextTransitionRow.Type"/> field already encodes
/// the flag identity for weather flips ("weather:BloodMoon" etc.) and the
/// vanilla shape for biome / boss / hardmode / invasion edges, so we pass
/// it through verbatim — the contract is "Type/From/To as the watcher
/// emitted them".
/// </para>
/// </summary>
public sealed class TransitionTrackStat : IDataStat<TransitionTrackSnapshot>
{
    /// <summary>Cap on transitions returned per snapshot. Matches the swimlane's draw budget.</summary>
    public const int MaxTransitions = 200;

    public string Name => RolloutStreamNames.TransitionTrack;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public TransitionTrackSnapshot CurrentSnapshot()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        ProfilerDatabase? db = PerformanceProfiler.Database;
        ObjectId? sid = sys?.LiveRecorderSessionId;
        if (db == null || sid == null)
        {
            return TransitionTrackSnapshot.Empty;
        }

        long sessionStartUnixMs;
        try
        {
            SessionRow? row = db.Sessions.FindById(sid);
            if (row != null && row.StartedUtc.Year > 2000)
            {
                DateTime utc = DateTime.SpecifyKind(row.StartedUtc, DateTimeKind.Utc);
                sessionStartUnixMs = new DateTimeOffset(utc).ToUnixTimeMilliseconds();
            }
            else
            {
                sessionStartUnixMs = Time.UnixMsNow();
            }
        }
        catch
        {
            sessionStartUnixMs = Time.UnixMsNow();
        }

        List<TransitionEntry> list = new(MaxTransitions);
        try
        {
            // LiteDB does not expose .Take on Find directly; we project via
            // Find + linq with a hard cap. Sorting is by Tick ascending so the
            // swimlane reads chronologically.
            foreach (ContextTransitionRow row in db.ContextTransitions
                         .Find(t => t.SessionId == sid)
                         .OrderBy(t => t.Tick)
                         .Take(MaxTransitions))
            {
                // Tick → UnixMs at 60 Hz approximation, anchored to session start.
                long unixMs = sessionStartUnixMs + (row.Tick * 1000L) / 60L;
                list.Add(new TransitionEntry(
                    UnixMs: unixMs,
                    TickIndex: row.Tick,
                    Type: row.Type ?? string.Empty,
                    From: row.From ?? string.Empty,
                    To: row.To ?? string.Empty));
            }
        }
        catch
        {
            // Defensive: a torn DB read returns whatever we've already collected.
            // Invariant 1 (read-only) — never mutate to recover.
        }

        return new TransitionTrackSnapshot(worldLoaded: sys?.Collector != null, list);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
