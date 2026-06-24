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

        InsightRecord live = AssertSingleLive(store);
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

        InsightRecord live = AssertSingleLive(store);
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

    private static InsightRecord AssertSingleLive(InsightStore store)
    {
        Assert.Equal(1, store.LiveCount);
        foreach (InsightRecord r in store.AllLive()) return r;
        throw new System.InvalidOperationException("unreachable");
    }

    private static InsightRecord MakeUntestedRecord() => new InsightRecord
    {
        Pattern = PatternKey.FreeRemovalCandidate,
        Subject = SubjectRef.ForMod(0),
        Magnitude = new Magnitude { ObservedMs = 0.01 },
        Evidence = new Evidence { PValueAdjusted = 1d }, // no test ran
        Audience = Audience.Player,
        Scope = EvidenceScope.NeedsPersistence,
    };

    private static InsightRecord MakeTestedRecord() => new InsightRecord
    {
        Pattern = PatternKey.HotHookDominance,
        Subject = SubjectRef.ForHook(0, 0),
        Magnitude = new Magnitude { RatioOrDelta = 0.85 },
        Evidence = new Evidence { PValueAdjusted = 0.01 }, // strong test
        Audience = Audience.Player,
        Scope = EvidenceScope.ThisSession,
    };
}
