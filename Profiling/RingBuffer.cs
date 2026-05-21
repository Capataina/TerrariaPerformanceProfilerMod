#nullable enable

using System;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Profiling;

/// <summary>
/// Fixed-capacity circular buffer of value-type records.
///
/// Allocated once (in practice at world-load) and never grown. <see cref="Push"/>
/// is a single array store plus index arithmetic: no allocation, no boxing
/// (Invariant 2: the per-tick hot path must not allocate). Once the buffer is
/// full, each <see cref="Push"/> overwrites the oldest record.
///
/// Pure logic with no tModLoader dependency, unit-testable without a running
/// game. The buffer is single-writer by design: one producer (the game thread
/// committing ticks). It is intentionally not synchronised; a lock-free
/// cross-thread read path for the UI worker is a later concern owned by the
/// Metric Collector, not by this type.
/// </summary>
/// <typeparam name="T">
/// A value type. Reference types are rejected so the backing store is one flat,
/// pre-allocated block with no per-record indirection.
/// </typeparam>
public sealed class RingBuffer<T> where T : struct
{
    private readonly T[] _items;

    // Physical index the next Push writes to.
    private int _head;

    // Number of live records, saturating at Capacity once the buffer wraps.
    private int _count;

    /// <summary>Creates a buffer that holds exactly <paramref name="capacity"/> records.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity), capacity, "Ring buffer capacity must be positive.");
        }

        _items = new T[capacity];
    }

    /// <summary>The maximum number of records the buffer holds.</summary>
    public int Capacity => _items.Length;

    /// <summary>The number of live records, between 0 and <see cref="Capacity"/>.</summary>
    public int Count => _count;

    /// <summary>True once the buffer has filled and every <see cref="Push"/> overwrites the oldest record.</summary>
    public bool IsFull => _count == _items.Length;

    /// <summary>
    /// Appends a record, overwriting the oldest one if the buffer is full. Zero
    /// allocation: one array store and a little index arithmetic. The record is
    /// taken by <c>in</c> to avoid copying a multi-field struct into the call.
    /// </summary>
    public void Push(in T item)
    {
        _items[_head] = item;
        _head = _head + 1 == _items.Length ? 0 : _head + 1;

        if (_count < _items.Length)
        {
            _count++;
        }
    }

    /// <summary>
    /// Records in chronological order: index 0 is the oldest live record and
    /// index <see cref="Count"/> - 1 is the most recent.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> is outside [0, <see cref="Count"/>).</exception>
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new IndexOutOfRangeException(
                    $"Index {index} is outside the live range [0, {_count}).");
            }

            // The oldest live record sits Count slots behind the head, modulo
            // Capacity. A single correction is enough: the raw value is always
            // within (-Capacity, Capacity).
            int physical = _head - _count + index;
            if (physical < 0)
            {
                physical += _items.Length;
            }

            return _items[physical];
        }
    }

    /// <summary>The most recently pushed record.</summary>
    /// <exception cref="InvalidOperationException">The buffer is empty.</exception>
    public T Newest =>
        _count > 0 ? this[_count - 1] : throw new InvalidOperationException("Ring buffer is empty.");

    /// <summary>The oldest live record.</summary>
    /// <exception cref="InvalidOperationException">The buffer is empty.</exception>
    public T Oldest =>
        _count > 0 ? this[0] : throw new InvalidOperationException("Ring buffer is empty.");

    /// <summary>
    /// Drops every record without releasing the backing store, so the same
    /// buffer can be reused for the next world without reallocating.
    /// </summary>
    public void Clear()
    {
        _head = 0;
        _count = 0;
    }
}
