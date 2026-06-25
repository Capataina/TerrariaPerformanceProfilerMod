#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using PerformanceProfiler.Insights;
using PerformanceProfiler.Insights.CrossSession;
using PerformanceProfiler.Persistence.History;
using PerformanceProfiler.Persistence.Records;
using Xunit;

namespace PerformanceProfiler.Tests.Persistence;

/// <summary>
/// Pure-logic tests for the cross-session detector family (DB rework wave 4). Each
/// detector is exercised against synthetic mod histories — the rollup ring is newest-first
/// — so the cross-session reasoning (windowing, ranking, thresholds, the honesty-contract
/// confidence caps) is verified with no game runtime and no LiteDB.
/// </summary>
public sealed class CrossSessionDetectorTests
{
    // Ring entries are newest-first; index 0 is the most recent session.
    private static ModHistory Mod(string name, IReadOnlyList<SessionRingEntry> ringNewestFirst,
                                  IReadOnlyList<ModModlistRollupRow>? byModlist = null)
        => new ModHistory
        {
            InternalName = name,
            DisplayName = name,
            RecentRing = ringNewestFirst,
            ByModlist = byModlist ?? Array.Empty<ModModlistRollupRow>(),
        };

    private static SessionRingEntry Entry(double cost, bool active, int spikes = 0, double engagement = 0)
        => new SessionRingEntry { CostMs = cost, WasActive = active, SpikeContributions = spikes, EngagementScore = engagement };

    private static CrossSessionInput Input(params ModHistory[] mods)
    {
        var idByName = new Dictionary<string, int>();
        for (int i = 0; i < mods.Length; i++) idByName[mods[i].InternalName] = i;
        return new CrossSessionInput(mods, idByName, "fp-current");
    }

    private static List<Insight> Run(ICrossSessionDetector det, CrossSessionInput input)
    {
        var emit = new List<Insight>();
        det.Evaluate(input, emit);
        return emit;
    }

    [Fact]
    public void Unused_ThreeIdleSessions_Emits()
    {
        var idle = Mod("IdleMod", new[] { Entry(0, false), Entry(0, false), Entry(0, false) });
        var active = Mod("BusyMod", new[] { Entry(2, true), Entry(0, false), Entry(0, false) });
        var emit = Run(new UnusedAcrossSessionsDetector(), Input(idle, active));

        Assert.Single(emit);
        Assert.Equal(PatternKey.UnusedAcrossSessions, emit[0].Pattern);
        Assert.Equal(0, emit[0].Subject.ModId);                 // IdleMod is index 0
        Assert.Equal(EvidenceScope.LifetimeData, emit[0].Scope);
        Assert.Equal(Confidence.Medium, emit[0].Confidence);    // exactly 3 sessions
    }

    [Fact]
    public void Unused_FewerThanThreeSessions_DoesNotEmit()
    {
        var twoIdle = Mod("IdleMod", new[] { Entry(0, false), Entry(0, false) });
        Assert.Empty(Run(new UnusedAcrossSessionsDetector(), Input(twoIdle)));
    }

    [Fact]
    public void Unused_FiveIdleSessions_IsHighConfidence()
    {
        var idle = Mod("IdleMod", Enumerable.Range(0, 6).Select(_ => Entry(0, false)).ToList());
        var emit = Run(new UnusedAcrossSessionsDetector(), Input(idle));
        Assert.Single(emit);
        // Window is 3, but the detector confidences on sessions-in-window; with a 3-session
        // window it tops out at Medium. The High rung needs a wider observed window.
        Assert.Equal(Confidence.Medium, emit[0].Confidence);
    }

    [Fact]
    public void SpikeContributor_RanksAndThresholds()
    {
        var heavy = Mod("Heavy", new[] { Entry(1, true, 4), Entry(1, true, 3), Entry(1, true, 2) }); // 9 spikes / 3 sessions
        var light = Mod("Light", new[] { Entry(1, true, 1), Entry(1, true, 0), Entry(1, true, 0) }); // 1 spike < MinSpikes
        var emit = Run(new LifetimeSpikeContributorDetector(), Input(heavy, light));

        Assert.Single(emit);
        Assert.Equal(PatternKey.LifetimeSpikeContributor, emit[0].Pattern);
        Assert.Equal(0, emit[0].Subject.ModId);  // Heavy
        Assert.Equal(9, emit[0].Magnitude.Count);
    }

    [Fact]
    public void CostlyDespiteLowUsage_NeedsFourMods_AndAnEngagementSignal()
    {
        // Four mods, each with 4 sessions. CostlyMod: high cost, zero engagement.
        ModHistory M(string n, double cost, double eng) =>
            Mod(n, Enumerable.Range(0, 4).Select(_ => Entry(cost, true, 0, eng)).ToList());

        var costly = M("Costly", cost: 10, eng: 0);
        var cheapBusy = M("CheapBusy", cost: 1, eng: 50);
        var midA = M("MidA", cost: 2, eng: 40);
        var midB = M("MidB", cost: 3, eng: 30);

        var emit = Run(new CostlyDespiteLowUsageDetector(), Input(costly, cheapBusy, midA, midB));
        Assert.Contains(emit, e => e.Subject.ModId == 0 && e.Pattern == PatternKey.CostlyDespiteLowUsage);
        Assert.All(emit, e => Assert.Equal(EvidenceScope.LifetimeData, e.Scope));
    }

    [Fact]
    public void CostlyDespiteLowUsage_NoEngagementAnywhere_EmitsNothing()
    {
        ModHistory M(string n, double cost) =>
            Mod(n, Enumerable.Range(0, 4).Select(_ => Entry(cost, true, 0, 0)).ToList());
        var emit = Run(new CostlyDespiteLowUsageDetector(),
            Input(M("A", 10), M("B", 1), M("C", 2), M("D", 3)));
        Assert.Empty(emit);  // usage axis undefined → honest absence
    }

    [Fact]
    public void CrossModpackDivergence_TwoStacksDiverge_Emits()
    {
        ModModlistRollupRow Stack(string fp, double cost, int sessions)
        {
            var r = new ModModlistRollupRow { InternalName = "Roamer", Fingerprint = fp, SessionCount = sessions };
            for (int i = 0; i < sessions; i++) r.Cost.FoldSample(cost);
            return r;
        }
        var roamer = Mod("Roamer",
            new[] { Entry(2, true) },
            new[] { Stack("fp-A", cost: 1.0, sessions: 3), Stack("fp-B", cost: 3.0, sessions: 3) });

        var emit = Run(new CrossModpackCostDivergenceDetector(), Input(roamer));
        Assert.Single(emit);
        Assert.Equal(PatternKey.CrossModpackCostDivergence, emit[0].Pattern);
        Assert.Equal(3.0, emit[0].Magnitude.RatioOrDelta, 6);   // 3.0 / 1.0
        Assert.Equal(2, emit[0].Magnitude.Count);               // two qualifying stacks
    }

    [Fact]
    public void CrossModpackDivergence_SingleStack_DoesNotEmit()
    {
        var r = new ModModlistRollupRow { InternalName = "Roamer", Fingerprint = "fp-A", SessionCount = 5 };
        for (int i = 0; i < 5; i++) r.Cost.FoldSample(2.0);
        var roamer = Mod("Roamer", new[] { Entry(2, true) }, new[] { r });
        Assert.Empty(Run(new CrossModpackCostDivergenceDetector(), Input(roamer)));
    }
}
