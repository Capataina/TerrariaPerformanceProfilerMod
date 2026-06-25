#nullable enable

using System;
using System.Linq;
using System.Text;
using log4net;
using LiteDB;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Persistence;

/// <summary>
/// Writes a session-end summary block to <c>client.log</c> via
/// <see cref="Mod.Logger"/>. Called from <c>ProfilerSystem.OnWorldUnload</c>
/// after the recorder has finished its final flush.
///
/// <para>
/// The summary is one multi-line <c>Info</c> call. A future log-only
/// inspection (the workflow this session needed) can grep for
/// <c>session-summary</c> and see the whole story without opening the DB
/// or running the live overlay.
/// </para>
/// </summary>
internal static class SessionSummaryLogger
{
    public static void Write(ILog log, ProfilerDatabase db, ObjectId sessionId,
        System.Collections.Generic.IReadOnlyList<string>? engineInsights = null)
    {
        try
        {
            var s = db.Sessions.FindById(sessionId);
            if (s == null) return;

            int spikes = db.SpikeWindows.Find(x => x.SessionId == sessionId).Count();
            int stalls = db.Stalls.Find(x => x.SessionId == sessionId).Count();
            int clusters = db.StallClusters.Find(x => x.SessionId == sessionId).Count();
            int deaths = db.PlayerDeaths.Find(x => x.SessionId == sessionId).Count();
            int transitions = db.ContextTransitions.Find(x => x.SessionId == sessionId).Count();
            int snapshots = db.WorldSnapshots.Find(x => x.SessionId == sessionId).Count();

            var archive = db.TickAggregatesArchive.FindOne(x => x.SessionId == sessionId);
            var worstCluster = db.StallClusters
                .Find(x => x.SessionId == sessionId)
                .OrderByDescending(x => x.WorstDurationMs)
                .FirstOrDefault();
            var worstSpike = db.SpikeWindows
                .Find(x => x.SessionId == sessionId)
                .OrderByDescending(x => x.WorstFrameMs)
                .FirstOrDefault();
            var topMods = db.PerSessionMods
                .Find(x => x.SessionId == sessionId)
                .OrderByDescending(x => x.TotalMs)
                .Take(3)
                .ToList();

            var sb = new StringBuilder(512);
            sb.AppendLine("=== profiler session-summary ===");
            sb.AppendLine($"  session {sessionId}");
            sb.AppendLine($"  started   {s.StartedUtc:HH:mm:ss}  ended {s.EndedUtc?.ToString("HH:mm:ss") ?? "(open)"}");
            sb.AppendLine($"  ticks     {s.TicksObserved}  duration {s.DurationMs} ms  mode {s.Mode}  end {s.EndReason}");
            if (archive != null)
            {
                sb.AppendLine($"  frames    avg {archive.AvgFrameMs:F2}ms  max {archive.MaxFrameMs:F1}ms  spikes {archive.SpikeCount}  stalls {archive.StallCount}");
            }
            sb.AppendLine($"  events    spikes={spikes}  stalls={stalls}  clusters={clusters}  deaths={deaths}  transitions={transitions}  snapshots={snapshots}");
            if (worstSpike != null)
            {
                string topName = (worstSpike.TopContributors != null && worstSpike.TopContributors.Count > 0)
                    ? worstSpike.TopContributors[0].Name + " " + worstSpike.TopContributors[0].Ms.ToString("F1") + "ms"
                    : "(no attribution)";
                sb.AppendLine($"  worst spike   tick {worstSpike.WorstTick}  {worstSpike.WorstFrameMs:F0}ms  top {topName}");
            }
            if (worstCluster != null && worstCluster.StallCount > 0)
            {
                sb.AppendLine($"  worst cluster tick {worstCluster.StartTick}-{worstCluster.EndTick}  {worstCluster.StallCount} stalls  worst {worstCluster.WorstDurationMs:F0}ms  cause {worstCluster.DominantCause}  contributor {worstCluster.DominantContributorName}");
            }
            for (int i = 0; i < topMods.Count; i++)
            {
                var m = topMods[i];
                sb.AppendLine($"  top mod {i + 1}    {m.ModInternalName}  avg {m.AvgMs:F2}ms/t  peak {m.PeakMs:F1}ms  total {m.TotalMs:F0}ms");
            }
            // Dual-surface observability: the engine's top insights, rendered by the
            // caller (which holds the live engine + game-state mod names), so the agent
            // reading client.log sees the same interpretation the player sees on the
            // dashboard. Sourced from the engine directly rather than re-derived here.
            if (engineInsights != null && engineInsights.Count > 0)
            {
                for (int i = 0; i < engineInsights.Count; i++)
                {
                    sb.AppendLine($"  insight {i + 1}    {engineInsights[i]}");
                }
            }
            sb.AppendLine("=== end session-summary ===");
            log.Info(sb.ToString());
        }
        catch (Exception ex)
        {
            log.Warn($"SessionSummaryLogger.Write failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
