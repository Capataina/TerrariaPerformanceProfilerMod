#nullable enable

using PerformanceProfiler.Profiling;
using Xunit;

namespace PerformanceProfiler.Tests;

/// <summary>
/// Pins the wrap-around behaviour of the ring buffer that backs both the
/// 30s tick history and the 50-window spike retainer. A regression here
/// silently corrupts the per-tick history readers (the spike snapshot and
/// the session JSON timeline).
/// </summary>
public class RingBufferTests
{
    [Fact]
    public void NewBuffer_IsEmpty()
    {
        RingBuffer<int> r = new RingBuffer<int>(4);
        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void Push_BelowCapacity_RetainsChronologicalOrder()
    {
        RingBuffer<int> r = new RingBuffer<int>(4);
        for (int i = 1; i <= 3; i++)
        {
            int v = i;
            r.Push(in v);
        }

        Assert.Equal(3, r.Count);
        Assert.Equal(1, r[0]);
        Assert.Equal(2, r[1]);
        Assert.Equal(3, r[2]);
    }

    [Fact]
    public void Push_BeyondCapacity_WrapsAndDropsOldest()
    {
        RingBuffer<int> r = new RingBuffer<int>(4);
        for (int i = 1; i <= 7; i++)
        {
            int v = i;
            r.Push(in v);
        }

        Assert.Equal(4, r.Count);
        // Oldest three should have rolled off; expect [4,5,6,7] in order.
        Assert.Equal(4, r[0]);
        Assert.Equal(5, r[1]);
        Assert.Equal(6, r[2]);
        Assert.Equal(7, r[3]);
    }

    [Fact]
    public void Newest_ReturnsMostRecentPush()
    {
        RingBuffer<int> r = new RingBuffer<int>(4);
        for (int i = 1; i <= 10; i++)
        {
            int v = i;
            r.Push(in v);
        }

        Assert.Equal(10, r.Newest);
    }
}
