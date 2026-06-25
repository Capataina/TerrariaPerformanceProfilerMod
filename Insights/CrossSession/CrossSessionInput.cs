#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Persistence.History;
using PerformanceProfiler.Persistence.Records;

namespace PerformanceProfiler.Insights.CrossSession;

/// <summary>
/// The input a cross-session detector reads: the lifetime history of every mod in the
/// CURRENT stack (so a finding is always about a mod the player still has), plus the
/// name→ModId map (the current session's load-order index, for the Insight subject) and
/// the current modlist fingerprint. Built once at session start from the
/// <see cref="HistoryStore"/>; the detectors are pure over it (no game runtime), so the
/// cross-session reasoning is unit-testable against synthetic histories.
/// </summary>
public sealed class CrossSessionInput
{
    public IReadOnlyList<ModHistory> CurrentStack { get; }
    public IReadOnlyDictionary<string, int> ModIdByName { get; }
    public string CurrentFingerprint { get; }

    public CrossSessionInput(IReadOnlyList<ModHistory> currentStack,
                             IReadOnlyDictionary<string, int> modIdByName,
                             string currentFingerprint)
    {
        CurrentStack = currentStack;
        ModIdByName = modIdByName;
        CurrentFingerprint = currentFingerprint;
    }

    /// <summary>The current session's load-order index for a mod, or -1 if it is not in the live stack.</summary>
    public int ModIdOf(string internalName) => ModIdByName.TryGetValue(internalName, out int id) ? id : -1;
}

/// <summary>A cross-session insight detector: reads the persisted history, emits findings
/// badged <see cref="EvidenceScope.LifetimeData"/>. A separate contract from the live
/// <c>IInsightDetector</c> because it runs once per session over the rollup, not at 1 Hz
/// over the live world.</summary>
public interface ICrossSessionDetector
{
    PatternKey Pattern { get; }
    Audience DefaultAudience { get; }
    void Evaluate(CrossSessionInput input, List<Insight> emit);
}

/// <summary>
/// Pure windowing + confidence helpers shared by the cross-session detectors. The ring
/// is newest-first (as <see cref="HistoryStore.GetModHistory"/> returns it), so "the last
/// N sessions" is simply its first N entries.
/// </summary>
internal static class CrossSessionMath
{
    public static int SessionsInWindow(IReadOnlyList<SessionRingEntry> ring, int n)
        => n <= 0 ? ring.Count : Math.Min(n, ring.Count);

    public static int ActiveCount(IReadOnlyList<SessionRingEntry> ring, int n)
    {
        int lim = SessionsInWindow(ring, n), c = 0;
        for (int i = 0; i < lim; i++) if (ring[i].WasActive) c++;
        return c;
    }

    public static double AvgCost(IReadOnlyList<SessionRingEntry> ring, int n)
    {
        int lim = SessionsInWindow(ring, n);
        if (lim == 0) return 0d;
        double s = 0d;
        for (int i = 0; i < lim; i++) s += ring[i].CostMs;
        return s / lim;
    }

    public static double AvgEngagement(IReadOnlyList<SessionRingEntry> ring, int n)
    {
        int lim = SessionsInWindow(ring, n);
        if (lim == 0) return 0d;
        double s = 0d;
        for (int i = 0; i < lim; i++) s += ring[i].EngagementScore;
        return s / lim;
    }

    public static long SumSpikes(IReadOnlyList<SessionRingEntry> ring, int n)
    {
        int lim = SessionsInWindow(ring, n);
        long s = 0L;
        for (int i = 0; i < lim; i++) s += ring[i].SpikeContributions;
        return s;
    }

    /// <summary>Confidence from the depth of cross-session evidence (number of sessions), capped
    /// per detector so an interpretive ranking never claims the same strength as a direct
    /// multi-session observation. ≥5 → High, ≥3 → Medium, ≥2 → Low.</summary>
    public static Confidence Conf(int sessions, Confidence cap)
    {
        Confidence c = sessions >= 5 ? Confidence.High
                     : sessions >= 3 ? Confidence.Medium
                     : sessions >= 2 ? Confidence.Low
                     : Confidence.Preliminary;
        return c < cap ? c : cap;
    }
}
