#nullable enable

using System;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Profiling.Events;

/// <summary>
/// Variable-length bitset of "is biome N active this tick?" backed by a
/// single <see cref="ulong"/> array. One bit per registered biome (vanilla +
/// modded), allocated once at install when <see cref="BiomeRegistry.Count"/>
/// becomes known and copied per-tick into <see cref="EventContext"/> via a
/// caller-provided destination — no per-tick allocation.
///
/// <para>
/// The shape mirrors <c>Player.modBiomeFlags</c> (a <c>BitArray</c>) so the
/// modded-biome read path is a near-direct copy; see plan §3.2 and §3.5.
/// </para>
/// </summary>
internal struct BiomeBitset : IEquatable<BiomeBitset>
{
    private ulong[] _words;
    private int _bitLength;

    /// <summary>Allocates a bitset wide enough for <paramref name="bitLength"/> bits.</summary>
    public BiomeBitset(int bitLength)
    {
        _bitLength = bitLength;
        _words = bitLength <= 0 ? Array.Empty<ulong>() : new ulong[(bitLength + 63) >> 6];
    }

    public int BitLength => _bitLength;
    public int WordCount => _words.Length;

    /// <summary>Discards the existing storage and reallocates for the new bit length. Used at install only.</summary>
    public void ResizeAndClear(int bitLength)
    {
        _bitLength = bitLength;
        _words = bitLength <= 0 ? Array.Empty<ulong>() : new ulong[(bitLength + 63) >> 6];
    }

    /// <summary>True if <paramref name="bit"/> is set. Bounds-checked; out-of-range returns false (defensive against registry drift).</summary>
    public readonly bool IsSet(int bit)
    {
        if ((uint)bit >= (uint)_bitLength) return false;
        return (_words[bit >> 6] & (1UL << (bit & 63))) != 0UL;
    }

    public void Set(int bit)
    {
        if ((uint)bit >= (uint)_bitLength) return;
        _words[bit >> 6] |= 1UL << (bit & 63);
    }

    public void Clear(int bit)
    {
        if ((uint)bit >= (uint)_bitLength) return;
        _words[bit >> 6] &= ~(1UL << (bit & 63));
    }

    /// <summary>Zeroes every word. Allocation-free.</summary>
    public void ClearAll()
    {
        for (int i = 0; i < _words.Length; i++) _words[i] = 0UL;
    }

    /// <summary>
    /// Word-level accessor for diff hot paths. Used by
    /// <c>ContextTransitionWatcher.DiffBiomeBits</c> to compute XOR deltas
    /// in 64-bit chunks instead of bit-by-bit, per events-and-context R1.
    /// Out-of-range index returns zero (defensive against shape drift).
    /// </summary>
    public readonly ulong WordAt(int wordIndex)
    {
        if ((uint)wordIndex >= (uint)_words.Length) return 0UL;
        return _words[wordIndex];
    }

    /// <summary>Copies <paramref name="source"/> into this bitset. Both must share the same bit length; mismatches reallocate.</summary>
    public void CopyFrom(in BiomeBitset source)
    {
        if (_bitLength != source._bitLength)
        {
            _bitLength = source._bitLength;
            _words = source._words.Length == 0 ? Array.Empty<ulong>() : new ulong[source._words.Length];
        }
        for (int i = 0; i < _words.Length; i++) _words[i] = source._words[i];
    }

    public readonly bool Equals(BiomeBitset other)
    {
        if (_bitLength != other._bitLength) return false;
        for (int i = 0; i < _words.Length; i++)
            if (_words[i] != other._words[i]) return false;
        return true;
    }

    public readonly override bool Equals(object? obj) => obj is BiomeBitset bs && Equals(bs);

    public readonly override int GetHashCode()
    {
        int h = _bitLength;
        for (int i = 0; i < _words.Length; i++) h = unchecked(h * 31 + _words[i].GetHashCode());
        return h;
    }

    /// <summary>Enumerator-free iteration: callers walk indices and call <see cref="IsSet"/>.</summary>
    /// <summary>Index of the lowest-numbered set bit, or -1 if none are set.</summary>
    public readonly int PrimaryBitIndex()
    {
        for (int w = 0; w < _words.Length; w++)
        {
            ulong bits = _words[w];
            if (bits == 0UL) continue;
            return (w << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits);
        }
        return -1;
    }

    public readonly int PopCount()
    {
        int count = 0;
        for (int i = 0; i < _words.Length; i++) count += System.Numerics.BitOperations.PopCount(_words[i]);
        return count;
    }
}
