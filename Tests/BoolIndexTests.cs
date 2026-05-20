#nullable enable

using PerformanceProfiler.Profiling.Util;
using Xunit;

namespace PerformanceProfiler.Tests;

/// <summary>
/// Pins the membership-test semantics of <see cref="BoolIndex"/>. Used by
/// <c>InteractionPlayer.PostUpdateBuffs</c> to replace the v0.5
/// <c>Array.IndexOf</c> per-tick scan with an O(1) bit-membership test.
/// </summary>
public class BoolIndexTests
{
    [Fact]
    public void Empty_ContainsNothing()
    {
        var b = new BoolIndex(32);
        for (int i = 0; i < 32; i++) Assert.False(b.Contains(i));
    }

    [Fact]
    public void AddRemove_RoundTrip()
    {
        var b = new BoolIndex(16);
        b.Add(3); b.Add(7); b.Add(15);
        Assert.True(b.Contains(3));
        Assert.True(b.Contains(7));
        Assert.True(b.Contains(15));
        Assert.False(b.Contains(0));
        Assert.False(b.Contains(8));
        b.Remove(7);
        Assert.False(b.Contains(7));
        Assert.True(b.Contains(3));
        Assert.True(b.Contains(15));
    }

    [Fact]
    public void Contains_OutOfRange_ReturnsFalse_NoThrow()
    {
        var b = new BoolIndex(8);
        Assert.False(b.Contains(-1));
        Assert.False(b.Contains(8));
        Assert.False(b.Contains(int.MaxValue));
    }

    [Fact]
    public void Add_BeyondCapacity_GrowsTransparently()
    {
        var b = new BoolIndex(4);
        b.Add(10);
        Assert.True(b.Contains(10));
        Assert.True(b.Capacity > 4);
    }

    [Fact]
    public void Clear_EmptiesEverything()
    {
        var b = new BoolIndex(64);
        for (int i = 0; i < 64; i += 3) b.Add(i);
        b.Clear();
        for (int i = 0; i < 64; i++) Assert.False(b.Contains(i));
    }
}
