#nullable enable

using System;

namespace PerformanceProfiler.Insights.Shared;

/// <summary>
/// Online running mean and variance via Welford's algorithm — numerically stable
/// and O(1) per sample, so a reference frame can accumulate a per-(context, mod)
/// cost distribution at 1 Hz across a whole session without storing the samples.
/// A value type: a frame holds an array of these, never a list of points.
/// </summary>
public struct RunningStat
{
    public long Count;
    public double Mean;
    private double _m2; // sum of squared deviations from the running mean

    /// <summary>Folds one sample in (Welford update). Allocation-free.</summary>
    public void Add(double x)
    {
        Count++;
        double delta = x - Mean;
        Mean += delta / Count;
        _m2 += delta * (x - Mean);
    }

    /// <summary>Population variance (M2 / n), or 0 with fewer than 2 samples.</summary>
    public double Variance => Count > 1 ? _m2 / Count : 0d;

    /// <summary>Sample variance (M2 / (n-1)), the unbiased estimator; 0 with fewer than 2 samples.</summary>
    public double SampleVariance => Count > 1 ? _m2 / (Count - 1) : 0d;

    /// <summary>Standard deviation (population).</summary>
    public double StdDev => Math.Sqrt(Variance);

    /// <summary>Raw Welford components — for persistence (the cross-session store reads these back).</summary>
    public double M2 => _m2;

    /// <summary>Rebuilds a RunningStat from persisted components, so a prior session's distribution can resume accumulating.</summary>
    public static RunningStat FromComponents(long count, double mean, double m2)
        => new RunningStat { Count = count < 0 ? 0 : count, Mean = mean, _m2 = m2 < 0d ? 0d : m2 };

    /// <summary>
    /// Combines two independent distributions into one (Chan's parallel algorithm)
    /// — the forward of <see cref="Without"/>. This is how a session's freshly
    /// accumulated stats fold into the lifetime baseline: count, mean, and M2 all
    /// merge exactly, no re-reading the samples.
    /// </summary>
    public RunningStat Merge(in RunningStat other)
    {
        if (Count == 0) return other;
        if (other.Count == 0) return this;
        long n = Count + other.Count;
        double delta = other.Mean - Mean;
        double mean = Mean + delta * other.Count / n;
        double m2 = _m2 + other._m2 + delta * delta * ((double)Count * other.Count) / n;
        return new RunningStat { Count = n, Mean = mean, _m2 = m2 };
    }

    /// <summary>Relative-cancellation threshold for <see cref="Without"/>: a recovered M2
    /// below this fraction of the parent M2 is treated as unrecoverable noise (the reverse
    /// Chan subtraction has lost its significant digits), not as a true near-zero variance.</summary>
    private const double WithoutRelEps = 1e-9;

    /// <summary>
    /// Subtracts a sub-distribution from this one, yielding the complement
    /// (e.g. "out of context" = global − in-context). Returns a RunningStat with
    /// the remaining count, mean, and M2 recovered via the parallel-variance
    /// identity. Returns the zero stat if the subset is not strictly contained.
    /// </summary>
    /// <remarks>
    /// The reverse-Chan M2 recovery subtracts two nearly-equal large doubles when the
    /// subset's spread is close to the parent's — exactly the no-signal case where a mod
    /// costs the same in and out of a context. Catastrophic cancellation then drives the
    /// recovered M2 negative or to a tiny residue of rounding noise. Flooring that to an
    /// exact <c>0</c> would manufacture a degenerate zero-variance complement, which
    /// <see cref="Stats.WelchTTestP"/> reads as "infinitely confident" — a spurious
    /// significant p-value, an honesty-contract violation (Invariant 3). The guard below
    /// detects the cancellation regime (recovered M2 negative, or below
    /// <see cref="WithoutRelEps"/> of the parent M2) and falls back to the parent's
    /// per-sample spread for the complement, the conservative honest estimate when the
    /// exact recovery is numerically unrecoverable, rather than emitting a zero variance.
    /// </remarks>
    public RunningStat Without(in RunningStat subset)
    {
        long n = Count - subset.Count;
        if (n <= 0 || subset.Count <= 0) return new RunningStat { Count = Math.Max(0, n) };
        double mean = (Mean * Count - subset.Mean * subset.Count) / n;
        // Reverse the Chan parallel-merge M2 combination:
        //   M2_all = M2_a + M2_b + delta^2 * (n_a * n_b) / n_all
        double delta = subset.Mean - mean;
        double m2 = _m2 - subset._m2 - delta * delta * ((double)n * subset.Count) / Count;
        if (m2 < WithoutRelEps * _m2)
        {
            // Cancellation regime: the subtraction lost its significant digits. Do not
            // trust the residue as a near-zero variance. Estimate the complement's M2 from
            // the parent's per-sample spread (population variance × n), so the complement
            // keeps a defensible non-degenerate spread and the Welch test cannot read it
            // as infinitely confident. Still floored at 0 for the pathological Count<=1 parent.
            m2 = Count > 1 ? (_m2 / Count) * n : 0d;
        }
        return new RunningStat { Count = n, Mean = mean, _m2 = m2 };
    }
}

/// <summary>
/// Pure statistical tests the detectors share. Two-sample comparisons (does a mod
/// cost more in context X than outside it?) need an effect size AND a p-value so a
/// finding can be honest about both how big and how sure it is — the two axes the
/// confidence badge and the magnitude split apart.
/// </summary>
public static class Stats
{
    /// <summary>
    /// Cohen's d effect size between two distributions, using the pooled standard
    /// deviation. Magnitude-of-difference in standard-deviation units; sign follows
    /// (a − b). Returns 0 when the pooled spread is degenerate.
    /// </summary>
    public static double CohensD(in RunningStat a, in RunningStat b)
    {
        if (a.Count < 2 || b.Count < 2) return 0d;
        double pooledVar = ((a.Count - 1) * a.SampleVariance + (b.Count - 1) * b.SampleVariance)
                         / (a.Count + b.Count - 2);
        double sd = Math.Sqrt(pooledVar);
        return sd > 1e-12 ? (a.Mean - b.Mean) / sd : 0d;
    }

    /// <summary>
    /// Two-sided p-value of Welch's t-test between two distributions (unequal
    /// variances). Uses a normal (z) approximation to the t-distribution and omits
    /// the Welch–Satterthwaite degrees-of-freedom correction; this is approximate
    /// once both samples have a few dozen points — the regime the 1 Hz reference
    /// frames operate in. On right-skewed low-mean cost with unequal n (the actual
    /// shape of per-mod cost) the approximation can be slightly anti-conservative,
    /// partially offset by the detectors' Bonferroni step. Returns 1.0 (no evidence)
    /// when either sample is too small or both variances are zero.
    /// </summary>
    public static double WelchTTestP(in RunningStat a, in RunningStat b)
    {
        if (a.Count < 2 || b.Count < 2) return 1d;
        double va = a.SampleVariance / a.Count;
        double vb = b.SampleVariance / b.Count;
        double denom = va + vb;
        if (denom <= 1e-18) return 1d;
        double t = (a.Mean - b.Mean) / Math.Sqrt(denom);
        // Two-sided: P(|Z| > |t|) under the normal approximation.
        return 2d * (1d - NormalCdf(Math.Abs(t)));
    }

    /// <summary>
    /// Standard-normal CDF Φ(x) via the Abramowitz &amp; Stegun 7.1.26 erf
    /// approximation (|error| &lt; 1.5e-7). Pure; no allocations.
    /// </summary>
    public static double NormalCdf(double x) => 0.5d * (1d + Erf(x / Math.Sqrt(2d)));

    private static double Erf(double x)
    {
        double sign = x < 0d ? -1d : 1d;
        x = Math.Abs(x);
        double t = 1d / (1d + 0.3275911d * x);
        double y = 1d - (((((1.061405429d * t - 1.453152027d) * t) + 1.421413741d) * t
                          - 0.284496736d) * t + 0.254829592d) * t * Math.Exp(-x * x);
        return sign * y;
    }
}
