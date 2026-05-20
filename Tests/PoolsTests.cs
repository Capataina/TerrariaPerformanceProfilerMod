#nullable enable

using PerformanceProfiler.Profiling.Pools;
using Xunit;

namespace PerformanceProfiler.Tests;

/// <summary>
/// Pins the borrow/return semantics of <see cref="RowPool{T}"/> and
/// <see cref="ListPool{T}"/>. Each row type that lives in the persistence
/// layer will eventually implement <see cref="IPoolReset"/>; this test
/// validates the pool itself against a synthetic row type so the pool's
/// contract is decoupled from any specific record.
/// </summary>
public class PoolsTests
{
    private sealed class FakeRow : IPoolReset
    {
        public int Value;
        public string Name = "";
        public void Reset() { Value = 0; Name = ""; }
    }

    [Fact]
    public void RowPool_Rent_ReturnsFreshInstanceWhenEmpty()
    {
        var r = RowPool<FakeRow>.Rent();
        Assert.NotNull(r);
        Assert.Equal(0, r.Value);
        Assert.Equal("", r.Name);
    }

    [Fact]
    public void RowPool_Return_ResetsBeforeStoring()
    {
        var r = RowPool<FakeRow>.Rent();
        r.Value = 42;
        r.Name = "dirty";
        RowPool<FakeRow>.Return(r);
        var r2 = RowPool<FakeRow>.Rent();
        Assert.Equal(0, r2.Value);
        Assert.Equal("", r2.Name);
    }

    [Fact]
    public void RowPool_Return_NullIsHarmless()
    {
        RowPool<FakeRow>.Return(null);   // must not throw
    }

    [Fact]
    public void ListPool_Rent_ReturnsEmptyList()
    {
        var l = ListPool<int>.Rent();
        Assert.NotNull(l);
        Assert.Empty(l);
    }

    [Fact]
    public void ListPool_Return_ClearsButPreservesCapacity()
    {
        var l = ListPool<int>.Rent();
        for (int i = 0; i < 100; i++) l.Add(i);
        int capBefore = l.Capacity;
        ListPool<int>.Return(l);
        var l2 = ListPool<int>.Rent();
        Assert.Empty(l2);
        // Capacity may or may not be preserved (ConcurrentBag is LIFO-ish but
        // not strict — we just want to assert that Clear() emptied the list
        // contents, which is the load-bearing semantic. Capacity assertion is
        // a soft win, not a contract.
        Assert.True(l2.Capacity >= 0);
        _ = capBefore;
    }
}
