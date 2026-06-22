#nullable enable

using PerformanceProfiler.Data.Detectors.Insights;
using Xunit;

namespace PerformanceProfiler.Tests;

/// <summary>
/// Pins the two ranking behaviours the code-health audit identified as
/// drift risks: share patterns must contribute meaningfully to magnitude in
/// [0,1], and ratio patterns must still respect the soft-knee curve. If
/// either regime regresses, the live overlay ranks records by a metric that
/// has lost the signal.
/// </summary>
public class RankingScorerTests
{
    // The store TTL the scorer is normally called with; any positive value
    // works for these tests (recency is not what we're pinning).
    private const long TtlTicks = 60L * 60L * 5L;

    [Fact]
    public void SharePattern_FractionalShare_RanksAboveSmallerShare()
    {
        InsightRecord big = ShareRecord(PatternKey.HotHookDominance, ratioOrDelta: 0.90);
        InsightRecord small = ShareRecord(PatternKey.HotHookDominance, ratioOrDelta: 0.40);

        double scoreBig = RankingScorer.Score(big, nowTick: 100, ttlTicks: TtlTicks);
        double scoreSmall = RankingScorer.Score(small, nowTick: 100, ttlTicks: TtlTicks);

        Assert.True(scoreBig > scoreSmall,
            $"Expected 90% share to outrank 40% share but got big={scoreBig} small={scoreSmall}");
    }

    [Fact]
    public void RatioPattern_TenXRatio_ScoresHigherThanTwoXRatio()
    {
        InsightRecord ten = RatioRecord(PatternKey.SustainedCostShift, ratioOrDelta: 10.0);
        InsightRecord two = RatioRecord(PatternKey.SustainedCostShift, ratioOrDelta: 2.0);

        double scoreTen = RankingScorer.Score(ten, nowTick: 100, ttlTicks: TtlTicks);
        double scoreTwo = RankingScorer.Score(two, nowTick: 100, ttlTicks: TtlTicks);

        Assert.True(scoreTen > scoreTwo,
            $"Expected 10× ratio to outrank 2× ratio but got ten={scoreTen} two={scoreTwo}");
    }

    [Fact]
    public void RatioPattern_BelowOne_ScoresZeroOnMagnitude()
    {
        // The ratio curve clamps anything <= 1 to 0; a sub-baseline measurement
        // is not a positive signal.
        InsightRecord weak = RatioRecord(PatternKey.SustainedCostShift, ratioOrDelta: 0.5);

        // Only the magnitude component should be zeroed; the others (confidence,
        // recency, actionability, novelty, audience) still contribute. So we
        // bound by comparing against the same record at exactly 1.0×.
        InsightRecord baseline = RatioRecord(PatternKey.SustainedCostShift, ratioOrDelta: 1.0);

        double scoreWeak = RankingScorer.Score(weak, nowTick: 100, ttlTicks: TtlTicks);
        double scoreBaseline = RankingScorer.Score(baseline, nowTick: 100, ttlTicks: TtlTicks);

        Assert.Equal(scoreWeak, scoreBaseline);
    }

    private static InsightRecord ShareRecord(PatternKey pattern, double ratioOrDelta) => new InsightRecord
    {
        Pattern = pattern,
        Subject = SubjectRef.ForMod(0),
        Magnitude = new Magnitude { RatioOrDelta = ratioOrDelta },
        Evidence = new Evidence { PValueAdjusted = 1d },
        Confidence = Confidence.Preliminary,
        Audience = Audience.Player,
        LastSeenTick = 100,
        ConfirmationCount = 1,
    };

    private static InsightRecord RatioRecord(PatternKey pattern, double ratioOrDelta) =>
        ShareRecord(pattern, ratioOrDelta);
}
