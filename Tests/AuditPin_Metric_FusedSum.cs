#nullable enable

using System;
using Xunit;

namespace PerformanceProfiler.Tests;

/// <summary>
/// Code-health audit pin for the SumAll loop-fusion in
/// <see cref="PerformanceProfiler.Profiling.MetricCollector"/>'s <c>EndTick</c>
/// (metric-collection finding #5, Medium severity). The backend-0 total used to
/// be a second full pass <c>SumAll(_perModRawMs)</c> over the per-mod grid right
/// after the smoothing loop already walked it; the fix folds
/// <c>total0 += _perModRawMs[i]</c> into that loop and deletes the second pass.
///
/// <para>
/// Floating-point addition is not associative, so loop fusion is only
/// behaviour-preserving if the addition order is unchanged. The fused
/// accumulator walks ascending index <c>0..n</c>, exactly as the original
/// <c>SumAll</c> did. <c>MetricCollector</c> itself cannot be linked into the
/// test project (it pulls tModLoader-transitive types), so this pin proves the
/// algebraic invariant the fusion depends on — fused ascending accumulation is
/// bit-identical to a separate ascending pass — directly against randomised
/// grids of the sizes the per-mod array takes (18-mod and 200-mod installs).
/// If this holds, the fusion's output equals the pre-fix output bit-for-bit.
/// </para>
/// </summary>
public sealed class AuditPin_Metric_FusedSum
{
    /// <summary>The pre-fix shape: a standalone ascending sum, mirroring the old <c>SumAll</c>.</summary>
    private static double SeparatePass(double[] values)
    {
        double sum = 0d;
        for (int i = 0; i < values.Length; i++)
        {
            sum += values[i];
        }
        return sum;
    }

    /// <summary>The post-fix shape: the sum folded into a pass that also reads every cell.</summary>
    private static double FusedIntoSmoothingLoop(double[] raw, double[] smoothed)
    {
        const double perModSmoothing = 0.06d;
        double total0 = 0d;
        for (int i = 0; i < smoothed.Length; i++)
        {
            total0 += raw[i];
            smoothed[i] += perModSmoothing * (raw[i] - smoothed[i]);
        }
        return total0;
    }

    [Theory]
    [InlineData(126)]   // 18 mods × 7 categories — the default bench install.
    [InlineData(1400)]  // ~200 mods × 7 — the nightmare modlist.
    [InlineData(7)]     // one mod.
    [InlineData(0)]     // degenerate empty grid.
    public void FusedAccumulation_IsBitIdenticalToSeparatePass(int cells)
    {
        var rng = new Random(20260625);
        double[] raw = new double[cells];
        for (int i = 0; i < cells; i++)
        {
            // A spread of magnitudes: most cells sub-ms, a few large, so the
            // running sum spans the regime where addition order would matter.
            raw[i] = rng.NextDouble() * (i % 13 == 0 ? 250d : 0.05d);
        }

        // smoothed must not perturb the total: the fused loop reads raw[i] for
        // both, and the SeparatePass reads the same raw array.
        double[] smoothed = new double[cells];

        double expected = SeparatePass(raw);
        double fused = FusedIntoSmoothingLoop(raw, smoothed);

        // Bit-identical, not merely close: same addends, same ascending order.
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected),
                     BitConverter.DoubleToInt64Bits(fused));
    }
}
