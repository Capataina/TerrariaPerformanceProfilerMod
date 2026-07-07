#nullable enable

using System;
using System.Collections.Generic;
using LiteDB;
using PerformanceProfiler.Persistence.Records;

namespace PerformanceProfiler.Persistence.Report;

/// <summary>
/// Everything the HTML session report renders, assembled once from the store
/// (atlas S17). Pure data — the writer templates it without further queries,
/// so writer tests run against synthetic instances.
/// </summary>
public sealed class SessionReportData
{
    public DateTime StartedUtc;
    public DateTime EndedUtc;
    public long DurationMs;
    public string ProfilerVersion = string.Empty;
    public string ModlistFingerprint = string.Empty;
    public List<string> ModVersions = new();
    public long TicksObserved;

    // KPIs (real-cadence since 0.28; the archive is the session's summary row).
    public double AvgFrameMs;
    public double MedianFrameMs;
    public double MaxFrameMs;
    public int SpikeCount;
    public int StallCount;

    /// <summary>Per-minute frame health for the ribbon: (minuteIndex, avgMs, worstMs).</summary>
    public List<(int Minute, double AvgMs, double WorstMs)> Minutes = new();

    /// <summary>Per-mod cost rows from the archive, descending by average.</summary>
    public List<(string Name, double AvgMs, double PeakMs, double TotalBytes)> PerMod = new();

    /// <summary>Real stalls (suspends/world-loads excluded — X3 honesty carried into the artefact).</summary>
    public List<(long UnixMs, double DurationMs, string Cause, string Severity)> Stalls = new();

    /// <summary>Wall time excluded from the stall list as pauses.</summary>
    public double PausedMs;
    public int PauseCount;

    /// <summary>Worst spikes, descending.</summary>
    public List<(long WorstTick, double WorstFrameMs, double BaselineMs)> Spikes = new();

    /// <summary>Boss/event segments: (family, name, durationMs, avgFrameMs, spikeCount).</summary>
    public List<(byte Family, string Name, long DurationMs, double AvgFrameMs, int SpikeCount)> Segments = new();

    /// <summary>Session-end insight feed snapshot: (shortText, confidence, scope).</summary>
    public List<(string Text, string Confidence, string Scope)> Insights = new();
}

/// <summary>
/// Assembles <see cref="SessionReportData"/> from the LiteDB store for one
/// session. LiteDB-only (Terraria-free, test-linked); every predicate hoists
/// its captured values per the C1 rule.
/// </summary>
public static class SessionReportReader
{
    /// <summary>
    /// Read the report data for <paramref name="sessionId"/>, or null when the
    /// session or its archive row is missing (an unended/crash-cut session has
    /// no archive and gets no report).
    /// </summary>
    public static SessionReportData? Read(ProfilerDatabase db, ObjectId sessionId)
    {
        SessionRow? session = db.Sessions.FindById(sessionId);
        if (session == null) return null;
        TickAggregateArchive? archive = db.TickAggregatesArchive.FindOne(a => a.SessionId == sessionId);
        if (archive == null) return null;

        var data = new SessionReportData
        {
            StartedUtc = DateTime.SpecifyKind(session.StartedUtc, DateTimeKind.Utc),
            EndedUtc = DateTime.SpecifyKind(session.EndedUtc ?? session.StartedUtc, DateTimeKind.Utc),
            DurationMs = session.DurationMs,
            ProfilerVersion = session.ProfilerVersion,
            ModlistFingerprint = session.ModlistFingerprint,
            ModVersions = session.ModVersions ?? new List<string>(),
            TicksObserved = archive.TicksObserved,
            AvgFrameMs = archive.AvgFrameMs,
            MedianFrameMs = archive.MedianFrameMs,
            MaxFrameMs = archive.MaxFrameMs,
            SpikeCount = (int)archive.SpikeCount,
            StallCount = (int)archive.StallCount,
        };

        // Ribbon: warm rows folded to minutes (same maths as the dashboard's
        // DB-branch heatmap; warm rows are real-cadence since 0.28).
        long minuteKey = -1;
        int ticksInMinute = 0;
        double sumMs = 0d, worstMs = 0d;
        foreach (TickAggregateWarm row in db.TickAggregatesWarm.Find(x => x.SessionId == sessionId))
        {
            long minute = row.SecondIndex / 60L;
            if (minute != minuteKey)
            {
                if (minuteKey >= 0)
                {
                    data.Minutes.Add(((int)minuteKey, ticksInMinute > 0 ? sumMs / ticksInMinute : 0d, worstMs));
                }
                minuteKey = minute;
                ticksInMinute = 0;
                sumMs = 0d;
                worstMs = 0d;
            }
            ticksInMinute++;
            sumMs += row.AvgFrameMs;
            if (row.P95FrameMs > worstMs) worstMs = row.P95FrameMs;
        }
        if (minuteKey >= 0)
        {
            data.Minutes.Add(((int)minuteKey, ticksInMinute > 0 ? sumMs / ticksInMinute : 0d, worstMs));
        }

        if (archive.PerMod != null)
        {
            foreach (var pm in archive.PerMod)
            {
                data.PerMod.Add((pm.Name, pm.AvgMs, pm.PeakMs, pm.TotalBytes));
            }
            data.PerMod.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));
        }

        // Stalls, cause-split (the X3 rule travels into the artefact).
        foreach (StallEventRow s in db.Stalls.Find(x => x.SessionId == sessionId))
        {
            bool isPause = s.Cause is "ProcessSuspended" or "WorldLoad";
            if (isPause)
            {
                data.PausedMs += s.DurationMs;
                data.PauseCount++;
            }
            else
            {
                data.Stalls.Add((s.UnixMs, s.DurationMs, s.Cause, s.Severity));
            }
        }
        data.Stalls.Sort((a, b) => b.DurationMs.CompareTo(a.DurationMs));
        if (data.Stalls.Count > 12) data.Stalls.RemoveRange(12, data.Stalls.Count - 12);

        foreach (SpikeWindowRow w in db.SpikeWindows.Find(x => x.SessionId == sessionId))
        {
            data.Spikes.Add((w.WorstTick, w.WorstFrameMs, w.BaselineMs));
        }
        data.Spikes.Sort((a, b) => b.WorstFrameMs.CompareTo(a.WorstFrameMs));
        if (data.Spikes.Count > 12) data.Spikes.RemoveRange(12, data.Spikes.Count - 12);

        foreach (SegmentRow seg in db.Segments.Find(x => x.SessionId == sessionId))
        {
            double avg = seg.Ticks > 0 ? seg.TotalFrameMs / seg.Ticks : 0d;
            data.Segments.Add((seg.Family, seg.Name, seg.DurationMs, avg, seg.SpikeCount));
        }

        foreach (InsightRow ins in db.Insights.Find(x => x.SessionId == sessionId))
        {
            data.Insights.Add((ins.RenderedShort, ins.Confidence, ins.EvidenceScope));
        }

        return data;
    }
}
