#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Persistence.History;
using PerformanceProfiler.Persistence.Records;

namespace PerformanceProfiler.Insights.CrossSession;

/// <summary>
/// "X has done no measurable work in your last N sessions" (the user's first motivating
/// question). A pure observation over the recency ring — every recent session idle —
/// badged LifetimeData and confidence-scaled by how many sessions back the silence runs.
/// Descriptive only: it never says "remove it" (Invariant 3).
/// </summary>
public sealed class UnusedAcrossSessionsDetector : ICrossSessionDetector
{
    public const int Window = 3;
    public PatternKey Pattern => PatternKey.UnusedAcrossSessions;
    public Audience DefaultAudience => Audience.Player;

    public void Evaluate(CrossSessionInput input, List<Insight> emit)
    {
        foreach (ModHistory h in input.CurrentStack)
        {
            int sessions = CrossSessionMath.SessionsInWindow(h.RecentRing, Window);
            if (sessions < 3) continue;                                  // need ≥3 sessions of evidence
            if (CrossSessionMath.ActiveCount(h.RecentRing, Window) > 0) continue; // any activity disqualifies
            int modId = input.ModIdOf(h.InternalName);
            if (modId < 0) continue;

            emit.Add(new Insight
            {
                Pattern = Pattern,
                Subject = SubjectRef.ForMod(modId),
                Magnitude = new Magnitude { Shape = MagnitudeShape.Deviation, Count = sessions, ObservedMs = 0d, BaselineMs = 0d },
                Evidence = new Evidence { SampleN = sessions, BaselineN = 0, PValue = 1d, EffectSize = 0d, PValueAdjusted = 1d, Baseline = BaselineKind.None },
                Confidence = CrossSessionMath.Conf(sessions, Confidence.High),
                Scope = EvidenceScope.LifetimeData,
                Audience = DefaultAudience,
            });
        }
    }
}

/// <summary>
/// "X was a top spike contributor across your last N sessions" (the user's second
/// question). Sums each current-stack mod's spike-window contributions over the recency
/// window and surfaces the heaviest. Attribution, not a hypothesis test, so confidence is
/// capped at Medium.
/// </summary>
public sealed class LifetimeSpikeContributorDetector : ICrossSessionDetector
{
    public const int Window = 5;
    public const long MinSpikes = 3;
    public const int TopK = 3;
    public PatternKey Pattern => PatternKey.LifetimeSpikeContributor;
    public Audience DefaultAudience => Audience.Player;

    public void Evaluate(CrossSessionInput input, List<Insight> emit)
    {
        var ranked = new List<(ModHistory h, long spikes, int sessions)>();
        foreach (ModHistory h in input.CurrentStack)
        {
            int sessions = CrossSessionMath.SessionsInWindow(h.RecentRing, Window);
            if (sessions < 3) continue;
            long spikes = CrossSessionMath.SumSpikes(h.RecentRing, Window);
            if (spikes >= MinSpikes) ranked.Add((h, spikes, sessions));
        }
        ranked.Sort((a, b) => b.spikes.CompareTo(a.spikes));

        for (int i = 0; i < ranked.Count && i < TopK; i++)
        {
            (ModHistory h, long spikes, int sessions) = ranked[i];
            int modId = input.ModIdOf(h.InternalName);
            if (modId < 0) continue;

            emit.Add(new Insight
            {
                Pattern = Pattern,
                Subject = SubjectRef.ForMod(modId),
                Magnitude = new Magnitude { Shape = MagnitudeShape.Deviation, Count = (int)Math.Min(int.MaxValue, spikes), ObservedMs = 0d, BaselineMs = 0d },
                Evidence = new Evidence { SampleN = sessions, BaselineN = 0, PValue = 1d, EffectSize = spikes, PValueAdjusted = 1d, Baseline = BaselineKind.None },
                Confidence = CrossSessionMath.Conf(sessions, Confidence.Medium),
                Scope = EvidenceScope.LifetimeData,
                Audience = DefaultAudience,
            });
        }
    }
}

/// <summary>
/// "X is among your costliest mods despite low engagement, across your last N sessions"
/// (the user's third question). Ranks current-stack mods by windowed average cost and by
/// windowed average engagement; a mod in the top cost quartile AND the bottom engagement
/// quartile is surfaced. Requires a real engagement signal in the window (otherwise the
/// usage axis is undefined and nothing is emitted — honest absence). Interpretive ranking,
/// so confidence caps at Medium.
/// </summary>
public sealed class CostlyDespiteLowUsageDetector : ICrossSessionDetector
{
    public const int Window = 4;
    public PatternKey Pattern => PatternKey.CostlyDespiteLowUsage;
    public Audience DefaultAudience => Audience.Player;

    public void Evaluate(CrossSessionInput input, List<Insight> emit)
    {
        var rows = new List<(ModHistory h, double cost, double eng, int sessions)>();
        double totalEng = 0d;
        foreach (ModHistory h in input.CurrentStack)
        {
            int sessions = CrossSessionMath.SessionsInWindow(h.RecentRing, Window);
            if (sessions < 3) continue;
            double cost = CrossSessionMath.AvgCost(h.RecentRing, Window);
            if (cost <= RollupActiveFloor) continue;             // ignore idle mods
            double eng = CrossSessionMath.AvgEngagement(h.RecentRing, Window);
            totalEng += eng;
            rows.Add((h, cost, eng, sessions));
        }
        // No usage signal anywhere → the "despite low usage" axis is undefined; emit nothing.
        if (rows.Count < 4 || totalEng <= 0d) return;

        int quartile = Math.Max(1, rows.Count / 4);

        var byCostDesc = new List<int>();
        for (int i = 0; i < rows.Count; i++) byCostDesc.Add(i);
        byCostDesc.Sort((a, b) => rows[b].cost.CompareTo(rows[a].cost));
        var topCost = new HashSet<int>();
        for (int i = 0; i < quartile && i < byCostDesc.Count; i++) topCost.Add(byCostDesc[i]);

        var byEngAsc = new List<int>();
        for (int i = 0; i < rows.Count; i++) byEngAsc.Add(i);
        byEngAsc.Sort((a, b) => rows[a].eng.CompareTo(rows[b].eng));
        var lowUse = new HashSet<int>();
        for (int i = 0; i < quartile && i < byEngAsc.Count; i++) lowUse.Add(byEngAsc[i]);

        foreach (int idx in topCost)
        {
            if (!lowUse.Contains(idx)) continue;
            (ModHistory h, double cost, double eng, int sessions) = rows[idx];
            int modId = input.ModIdOf(h.InternalName);
            if (modId < 0) continue;

            emit.Add(new Insight
            {
                Pattern = Pattern,
                Subject = SubjectRef.ForMod(modId),
                Magnitude = new Magnitude { Shape = MagnitudeShape.Deviation, ObservedMs = cost, BaselineMs = eng, Count = sessions },
                Evidence = new Evidence { SampleN = sessions, BaselineN = rows.Count, PValue = 1d, EffectSize = cost, PValueAdjusted = 1d, Baseline = BaselineKind.None },
                Confidence = CrossSessionMath.Conf(sessions, Confidence.Medium),
                Scope = EvidenceScope.LifetimeData,
                Audience = DefaultAudience,
            });
        }
    }

    // Mirrors RollupFold.ActiveCostFloorMs; a local copy keeps this detector pure of the
    // Persistence fold logic while using the same idle threshold.
    private const double RollupActiveFloor = 0.0005d;
}

/// <summary>
/// Cross-modpack (decision C): "X costs Nx more in one of your modlists than another".
/// Reads a mod's per-modlist rollup rows; when it has been in ≥2 well-sampled stacks and
/// its average cost diverges past a ratio, surfaces the measured divergence. Computable
/// from the mod's own breakdown alone (no cross-mod ranking), so it is exact; states the
/// ratio, never a recommendation.
/// </summary>
public sealed class CrossModpackCostDivergenceDetector : ICrossSessionDetector
{
    public const int MinSessionsPerStack = 2;
    public const double DivergenceRatio = 2.0;
    public PatternKey Pattern => PatternKey.CrossModpackCostDivergence;
    public Audience DefaultAudience => Audience.Player;

    public void Evaluate(CrossSessionInput input, List<Insight> emit)
    {
        foreach (ModHistory h in input.CurrentStack)
        {
            double minCost = double.MaxValue, maxCost = 0d;
            int qualifyingStacks = 0, minSessions = int.MaxValue;
            foreach (ModModlistRollupRow m in h.ByModlist)
            {
                if (m.SessionCount < MinSessionsPerStack || m.Cost.Count == 0) continue;
                qualifyingStacks++;
                if (m.SessionCount < minSessions) minSessions = m.SessionCount;
                double c = m.Cost.Mean;
                if (c < minCost) minCost = c;
                if (c > maxCost) maxCost = c;
            }
            if (qualifyingStacks < 2 || minCost <= 1e-9) continue; // decision C: ≥2 well-sampled stacks

            double ratio = maxCost / minCost;
            if (ratio < DivergenceRatio) continue;

            int modId = input.ModIdOf(h.InternalName);
            if (modId < 0) continue;

            emit.Add(new Insight
            {
                Pattern = Pattern,
                Subject = SubjectRef.ForMod(modId),
                Magnitude = new Magnitude { Shape = MagnitudeShape.Deviation, ObservedMs = maxCost, BaselineMs = minCost, RatioOrDelta = ratio, Count = qualifyingStacks },
                Evidence = new Evidence { SampleN = minSessions, BaselineN = qualifyingStacks, PValue = 1d, EffectSize = ratio, PValueAdjusted = 1d, Baseline = BaselineKind.None },
                Confidence = CrossSessionMath.Conf(minSessions, Confidence.Medium),
                Scope = EvidenceScope.LifetimeData,
                Audience = DefaultAudience,
            });
        }
    }
}
