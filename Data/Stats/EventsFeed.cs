#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Data.Aggregators.Segments;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// One row in the dashboard's "recent events" feed. Mirrors what used
/// to be assembled in JavaScript on every poll by merging the segments
/// and spikes endpoints client-side.
/// </summary>
public struct FeedEvent
{
    /// <summary>"boss-kill" / "death" / "spike" / "stall" / "segment".</summary>
    public string Kind;
    /// <summary>One-line human-readable summary.</summary>
    public string Text;
    /// <summary>Wall-clock UTC ms; the dashboard formats it as "Ns ago".</summary>
    public long UnixMs;
    /// <summary>Game tick the event fired on, when known.</summary>
    public long TickIndex;
}

/// <summary>
/// Builds the merged "recent events" feed from the collector and segment
/// store. Pure logic; allocates one list per call (the feed only fires
/// from <c>/api/events</c> at ~1.5 Hz, so per-call allocation is fine).
///
/// <para>
/// Previously this merge happened in JS — but the merged shape is
/// fundamentally a "fact about the session," not a presentation choice,
/// so it belongs on the C# side. The dashboard now reads a pre-merged
/// list instead of two raw arrays.
/// </para>
/// </summary>
public static class EventsFeed
{
    /// <summary>
    /// Build the merged feed. Returns most-recent-first, capped at
    /// <paramref name="max"/> entries.
    /// </summary>
    public static List<FeedEvent> Build(MetricCollector? collector, SegmentStore? segments, int max = 16)
    {
        var list = new List<FeedEvent>(max * 2);
        long nowUnix = Time.UnixMsNow();
        long nowTick = collector != null && collector.History.Count > 0
            ? collector.History[collector.History.Count - 1].TickIndex
            : 0L;

        // Closed segments → one event per segment, kind decided by drama:
        // death > boss-kill > spike > generic close.
        if (segments != null)
        {
            foreach (Segment s in segments.Recent)
            {
                string kind;
                string text;
                if (s.DeathCount > 0)         { kind = "death"; text = $"died in {s.Name}"; }
                else if (s.BossKillCount > 0) { kind = "boss-kill"; text = $"{s.Name} · victory · {FormatDuration(s.DurationMs)}"; }
                else if (s.SpikeCount > 0)    { kind = "spike"; text = $"{s.Name} closed with {s.SpikeCount} spike(s)"; }
                else                          { kind = "segment"; text = $"{s.Name} ended · {FormatDuration(s.DurationMs)}"; }
                list.Add(new FeedEvent { Kind = kind, Text = text, UnixMs = s.EndUnixMs, TickIndex = s.EndTick });
            }
        }

        // Detected spike windows → one entry per window. Top contributor
        // is the line's headline so the player sees the cost owner at a
        // glance without expanding the Lag tab.
        if (collector != null)
        {
            string[] modNames = HookInterceptor.ProfiledModNames;
            int categoryCount = PerModAttribution.CategoryCount;
            foreach (var w in collector.Spikes)
            {
                string topName = "(unattributed)";
                double topMs = 0d;
                if (w.PerModCatMs != null && w.PerModCatMs.Length > 0)
                {
                    int modCount = w.PerModCatMs.Length / categoryCount;
                    int bestMod = -1;
                    for (int m = 0; m < modCount; m++)
                    {
                        double sum = 0d;
                        int baseIdx = m * categoryCount;
                        for (int cat = 0; cat < categoryCount; cat++) sum += w.PerModCatMs[baseIdx + cat];
                        if (sum > topMs) { topMs = sum; bestMod = m; }
                    }
                    if (bestMod >= 0 && bestMod < modNames.Length) topName = modNames[bestMod];
                }
                // Approximate wall-clock: now minus ((nowTick - worstTick) * ~16.6 ms).
                long unix = nowUnix - System.Math.Max(0L, (nowTick - w.WorstTick) * 1000L / 60L);
                list.Add(new FeedEvent
                {
                    Kind = "spike",
                    Text = $"spike {w.WorstFrameMs:F1}ms · top {topName} {topMs:F2} ms",
                    UnixMs = unix,
                    TickIndex = w.WorstTick,
                });
            }

            // Stall events → cause + duration line. Stalls are rarer and
            // bigger than spikes, so they always make the cut.
            foreach (var st in collector.Stalls)
            {
                long unix = nowUnix - System.Math.Max(0L, (nowTick - st.StartTickIndex) * 1000L / 60L);
                list.Add(new FeedEvent
                {
                    Kind = "stall",
                    Text = $"stall {st.TickPeriodMs:F0}ms · {st.Cause}",
                    UnixMs = unix,
                    TickIndex = st.StartTickIndex,
                });
            }
        }

        list.Sort((a, b) => b.UnixMs.CompareTo(a.UnixMs));
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
        return list;
    }

    private static string FormatDuration(long ms)
    {
        if (ms < 1000L) return ms + "ms";
        long s = ms / 1000L;
        if (s < 60L) return s + "s";
        long m = s / 60L;
        return m + "m " + (s % 60L).ToString("00") + "s";
    }
}
