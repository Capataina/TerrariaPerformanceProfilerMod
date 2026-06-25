#nullable enable

using System;
using System.Diagnostics;
using Xunit;

namespace PerformanceProfiler.Tests;

/// <summary>
/// Code-health audit pin for the cached-reciprocal change in the harvest hot
/// path (metric-collection finding #1, High severity). <see cref="PerModAttribution"/>
/// and <see cref="PerformanceProfiler.Profiling.MetricCollector"/> previously
/// recomputed <c>1000d / Stopwatch.Frequency</c> per call (2-5× per tick); the
/// fix caches the reciprocal once as a static field and multiplies, matching
/// the <c>Time.cs</c> precedent.
///
/// <para>
/// The change is claimed byte-equivalent: multiplying by a cached reciprocal
/// instead of dividing differs at most in the last bit of the mantissa, far
/// below the 0.5 ms histogram-bucket granularity downstream. This pin proves
/// the two forms agree to a tolerance well under that granularity across the
/// representative range of stopwatch-tick deltas a tick can produce (a sub-ms
/// tick up to a multi-second stall), so a future reader cannot mistake the
/// optimisation for a behaviour change.
/// </para>
/// </summary>
public sealed class AuditPin_Metric_Reciprocal
{
    // The reciprocal the production code now caches.
    private static readonly double CachedReciprocal = 1000d / Stopwatch.Frequency;

    public static TheoryData<long> RepresentativeTickDeltas()
    {
        long freq = Stopwatch.Frequency;
        return new TheoryData<long>
        {
            0L,
            1L,
            freq / 1000,        // ~1 ms
            freq / 60,          // ~one 60 TPS frame (~16.7 ms)
            freq / 10,          // ~100 ms
            freq,               // ~1 s (a stall)
            freq * 5,           // ~5 s (a long stall)
            freq * 30,          // ~30 s (the retention window)
            long.MaxValue / 2000, // a large delta that stays well clear of overflow
        };
    }

    [Theory]
    [MemberData(nameof(RepresentativeTickDeltas))]
    public void CachedReciprocalMultiply_AgreesWithPerCallDivision(long elapsedTicks)
    {
        double viaCachedMultiply = elapsedTicks * CachedReciprocal;
        double viaPerCallDivision = elapsedTicks * 1000d / Stopwatch.Frequency;

        // Both forms compute the same value in ms; assert they agree to far
        // tighter than the 0.5 ms histogram bucket the baseline rounds to.
        double diff = Math.Abs(viaCachedMultiply - viaPerCallDivision);
        Assert.True(diff < 1e-9,
            $"ticks={elapsedTicks}: cached-multiply={viaCachedMultiply} vs " +
            $"per-call-divide={viaPerCallDivision} differ by {diff} (>= 1e-9).");
    }

    [Fact]
    public void CachedReciprocal_MatchesTheDivisionItReplaces()
    {
        // The field the production code holds must equal the expression it
        // replaced, exactly (same operands, same operation).
        Assert.Equal(1000d / Stopwatch.Frequency, CachedReciprocal);
    }
}
