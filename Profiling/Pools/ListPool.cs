#nullable enable

using System.Collections.Concurrent;
using System.Collections.Generic;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Profiling.Pools;

/// <summary>
/// Thread-safe pool of <see cref="List{T}"/> instances. v0.5 allocated a
/// fresh <c>List&lt;EquipmentSlotEntry&gt;</c> every loadout change and a
/// fresh <c>List&lt;int&gt;</c> every damage event for the
/// <c>SnapshotActiveBuffTypes</c> snapshot. Both fire often enough to
/// matter on the game thread. The pool turns those allocations into a
/// take/clear/return cycle.
///
/// <para>
/// <see cref="Return"/> clears the list (preserves capacity) so the next
/// renter sees an empty list backed by the previously-grown array.
/// </para>
/// </summary>
public static class ListPool<T>
{
    private static readonly ConcurrentBag<List<T>> _bag = new();

    public static List<T> Rent()
    {
        if (_bag.TryTake(out var list)) return list;
        return new List<T>();
    }

    /// <summary>
    /// Return a list to the pool. Clears the list first (preserves the
    /// backing array's capacity for the next renter). Null-safe.
    /// </summary>
    public static void Return(List<T>? list)
    {
        if (list == null) return;
        list.Clear();
        _bag.Add(list);
    }
}
