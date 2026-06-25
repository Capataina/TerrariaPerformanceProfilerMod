#nullable enable

using System;

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
namespace PerformanceProfiler.Profiling.Util;

/// <summary>
/// Fixed-size bool[] wrapper for O(1) membership tests on a small id
/// space. Drop-in replacement for <see cref="System.Array.IndexOf{T}"/>
/// on small int arrays used as set-membership probes in the buff-diff
/// hot path.
///
/// <para>
/// v0.5 buff diff did
/// <c>Array.IndexOf(_prevBuffTypes, t, 0, _prevBuffCount) &lt; 0</c> for
/// every active buff every tick — O(active × prev) on the game thread.
/// A <see cref="BoolIndex"/> backed by a 1-bit-per-id array makes each
/// membership test a single load.
/// </para>
///
/// <para>
/// Sized at construction. Reset via <see cref="Clear"/>. Not thread-safe
/// — owned by a single ModPlayer instance per local player.
/// </para>
/// </summary>
public sealed class BoolIndex
{
    private bool[] _bits;

    public BoolIndex(int capacity)
    {
        _bits = new bool[capacity];
    }

    public int Capacity => _bits.Length;

    public bool Contains(int id)
    {
        if ((uint)id >= (uint)_bits.Length) return false;
        return _bits[id];
    }

    public void Add(int id)
    {
        EnsureCapacity(id + 1);
        _bits[id] = true;
    }

    public void Remove(int id)
    {
        if ((uint)id < (uint)_bits.Length) _bits[id] = false;
    }

    public void Clear()
    {
        Array.Clear(_bits, 0, _bits.Length);
    }

    public void EnsureCapacity(int required)
    {
        if (required <= _bits.Length) return;
        // Floor the doubling base at 1 so a zero-capacity BoolIndex
        // (e.g. constructed with `new BoolIndex(0)` as a placeholder)
        // does not loop forever on `0 *= 2`.
        int newCap = _bits.Length == 0 ? 1 : _bits.Length;
        while (newCap < required) newCap *= 2;
        var bigger = new bool[newCap];
        Array.Copy(_bits, bigger, _bits.Length);
        _bits = bigger;
    }
}
