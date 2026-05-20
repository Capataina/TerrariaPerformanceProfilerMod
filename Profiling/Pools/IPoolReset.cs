#nullable enable

namespace PerformanceProfiler.Profiling.Pools;

/// <summary>
/// Implemented by every record / row type that <see cref="RowPool{T}"/>
/// can pool. Called by the pool when a row is returned, so the next
/// renter starts from a clean default state. Keep the implementation
/// branch-free and allocation-free.
/// </summary>
public interface IPoolReset
{
    /// <summary>Reset every mutable field back to its default-constructed value.</summary>
    void Reset();
}
