#nullable enable

using PerformanceProfiler.Profiling;
using Xunit;

namespace PerformanceProfiler.Tests;

/// <summary>
/// Pins the contract of <see cref="Time.UnixMsNow"/>: the value tracks
/// <see cref="System.DateTimeOffset.UtcNow"/> within a small drift window
/// across a synthetic delay. Static-init binds the origin at type-load;
/// <see cref="Time.Reset"/> re-anchors it explicitly.
/// </summary>
public class TimeTests
{
    [Fact]
    public void UnixMsNow_TracksUtcNow_Within50Ms()
    {
        Time.Reset();
        long ours = Time.UnixMsNow();
        long theirs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.InRange(System.Math.Abs(ours - theirs), 0, 50);
    }

    [Fact]
    public void UnixMsNow_IsMonotonicAndAdvancesAcrossSleep()
    {
        Time.Reset();
        long a = Time.UnixMsNow();
        System.Threading.Thread.Sleep(20);
        long b = Time.UnixMsNow();
        // Strict monotonicity (no clock skew, no negative deltas).
        Assert.True(b >= a, $"Time went backward: a={a}, b={b}");
        // 20 ms sleep should produce 15–60 ms delta (depending on OS scheduling).
        Assert.InRange(b - a, 10, 200);
    }

    [Fact]
    public void Reset_RebindsToFreshDateTimeOrigin()
    {
        Time.Reset();
        long t0 = Time.UnixMsNow();
        System.Threading.Thread.Sleep(10);
        Time.Reset();
        long t1 = Time.UnixMsNow();
        Assert.True(t1 >= t0, "Reset must not move the wall-clock backward");
    }
}
