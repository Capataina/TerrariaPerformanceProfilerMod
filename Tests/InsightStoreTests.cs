#nullable enable

using PerformanceProfiler.Insights;
using Xunit;

namespace PerformanceProfiler.Tests;

/// <summary>
/// Pins the confidence-promotion contract from the audit's insights finding:
/// a record with PValueAdjusted = 1d (the detector explicitly declares "no
/// hypothesis test was run") can never reach Medium on confirmations alone.
/// </summary>
public class InsightStoreTests
{
    [Fact]
    public void Repeated_UntestedRecord_NeverPromotesPastLow()
    {
        InsightStore store = new InsightStore();

        // Submit the same untested record many times. The store will mark each
        // submission as a confirmation; without the pAdjusted gate this would
        // promote to Medium and then High purely on repetition.
        for (int i = 0; i < 10; i++)
        {
            store.Submit(MakeUntestedRecord(), nowTick: 1000 + i);
        }

        Insight live = AssertSingleLive(store);
        Assert.Equal(10, live.ConfirmationCount);
        Assert.True(live.Confidence <= Confidence.Low,
            $"Untested record should not promote past Low, got {live.Confidence}");
    }

    [Fact]
    public void Repeated_TestedRecord_PromotesAtThreshold()
    {
        InsightStore store = new InsightStore();

        for (int i = 0; i < 4; i++)
        {
            store.Submit(MakeTestedRecord(), nowTick: 2000 + i);
        }

        Insight live = AssertSingleLive(store);
        Assert.Equal(4, live.ConfirmationCount);
        // 4 confirmations + pAdjusted <= 0.05 → High.
        Assert.Equal(Confidence.High, live.Confidence);
    }

    [Fact]
    public void Submit_DedupesOnPatternAndSubject()
    {
        InsightStore store = new InsightStore();
        store.Submit(MakeUntestedRecord(), nowTick: 100);
        store.Submit(MakeUntestedRecord(), nowTick: 101);
        store.Submit(MakeUntestedRecord(), nowTick: 102);

        Assert.Equal(1, store.LiveCount);
    }

    [Fact]
    public void Submit_DistinguishesSubjectsBySubjectKind()
    {
        // G6: a Mod subject and a Session subject share every id (all -1) and
        // differ only in Kind. The prior packed-long key dropped Kind and would
        // have collapsed these onto one entry; the full-width InsightKey keeps
        // them distinct.
        InsightStore store = new InsightStore();
        store.Submit(new Insight
        {
            Pattern = PatternKey.SustainedCostShift,
            Subject = SubjectRef.ForMod(-1),
            Evidence = new Evidence { PValueAdjusted = 1d },
        }, nowTick: 10);
        store.Submit(new Insight
        {
            Pattern = PatternKey.SustainedCostShift,
            Subject = SubjectRef.ForSession(),
            Evidence = new Evidence { PValueAdjusted = 1d },
        }, nowTick: 11);

        Assert.Equal(2, store.LiveCount);
    }

    [Fact]
    public void SubjectRef_Factories_SetTheRightKind()
    {
        Assert.Equal(SubjectKind.Mod, SubjectRef.ForMod(3).Kind);
        Assert.Equal(SubjectKind.Hook, SubjectRef.ForHook(3, 1).Kind);
        Assert.Equal(SubjectKind.Context, SubjectRef.ForContext(7, 2).Kind);
        Assert.Equal(SubjectKind.Session, SubjectRef.ForSession().Kind);
        Assert.Equal(SubjectKind.Runtime, SubjectRef.ForRuntime().Kind);
        Assert.Equal(SubjectKind.Machine, SubjectRef.ForMachine().Kind);
    }

    [Fact]
    public void Magnitude_DefaultShapeIsDeviation()
        => Assert.Equal(MagnitudeShape.Deviation, new Magnitude().Shape);

    private static Insight AssertSingleLive(InsightStore store)
    {
        Assert.Equal(1, store.LiveCount);
        foreach (Insight r in store.AllLive()) return r;
        throw new System.InvalidOperationException("unreachable");
    }

    private static Insight MakeUntestedRecord() => new Insight
    {
        Pattern = PatternKey.FreeRemovalCandidate,
        Subject = SubjectRef.ForMod(0),
        Magnitude = new Magnitude { ObservedMs = 0.01 },
        Evidence = new Evidence { PValueAdjusted = 1d }, // no test ran
        Audience = Audience.Player,
        Scope = EvidenceScope.NeedsPersistence,
    };

    private static Insight MakeTestedRecord() => new Insight
    {
        Pattern = PatternKey.HotHookDominance,
        Subject = SubjectRef.ForHook(0, 0),
        Magnitude = new Magnitude { RatioOrDelta = 0.85 },
        Evidence = new Evidence { PValueAdjusted = 0.01 }, // strong test
        Audience = Audience.Player,
        Scope = EvidenceScope.ThisSession,
    };
}
