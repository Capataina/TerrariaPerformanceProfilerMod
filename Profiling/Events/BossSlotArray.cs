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
/// Fixed-size buffer of active-boss NPC type ids for one tick. Eight slots
/// covers the worst real-world simultaneous boss count we have observed
/// (Twins + the four Lunar Pillars + a worm head + a duo); overflow is
/// truncated and logged once at install boundary by <see cref="ContextTagger"/>.
///
/// <para>
/// Held by-value inside <see cref="EventContext"/>; no allocations per tick.
/// Multi-segment bosses are collapsed to their head NPC's type by
/// <see cref="BossSampler"/> before being written here.
/// </para>
/// </summary>
internal struct BossSlotArray : IEquatable<BossSlotArray>
{
    public const int SlotCount = 8;

    // C# 12 inline-array-style flat fields. Eight shorts; an NPC type fits in
    // a short (vanilla NPCID maxes around 700, modded usually well under
    // 32k). 0 sentinels "no boss in this slot".
    private short _s0, _s1, _s2, _s3, _s4, _s5, _s6, _s7;
    private byte _count;

    public readonly int Count => _count;

    public readonly short this[int i]
    {
        get
        {
            switch (i)
            {
                case 0: return _s0;
                case 1: return _s1;
                case 2: return _s2;
                case 3: return _s3;
                case 4: return _s4;
                case 5: return _s5;
                case 6: return _s6;
                case 7: return _s7;
                default: throw new ArgumentOutOfRangeException(nameof(i));
            }
        }
    }

    public void Clear()
    {
        _s0 = _s1 = _s2 = _s3 = _s4 = _s5 = _s6 = _s7 = 0;
        _count = 0;
    }

    /// <summary>Pushes <paramref name="type"/> into the next free slot. Returns false if the array is already full.</summary>
    public bool TryAdd(short type)
    {
        switch (_count)
        {
            case 0: _s0 = type; _count = 1; return true;
            case 1: _s1 = type; _count = 2; return true;
            case 2: _s2 = type; _count = 3; return true;
            case 3: _s3 = type; _count = 4; return true;
            case 4: _s4 = type; _count = 5; return true;
            case 5: _s5 = type; _count = 6; return true;
            case 6: _s6 = type; _count = 7; return true;
            case 7: _s7 = type; _count = 8; return true;
            default: return false;
        }
    }

    /// <summary>True if <paramref name="type"/> is present in any slot.</summary>
    public readonly bool Contains(short type)
    {
        if (_count == 0) return false;
        if (_s0 == type) return true;
        if (_count > 1 && _s1 == type) return true;
        if (_count > 2 && _s2 == type) return true;
        if (_count > 3 && _s3 == type) return true;
        if (_count > 4 && _s4 == type) return true;
        if (_count > 5 && _s5 == type) return true;
        if (_count > 6 && _s6 == type) return true;
        if (_count > 7 && _s7 == type) return true;
        return false;
    }

    public readonly bool Equals(BossSlotArray other) =>
        _count == other._count
        && _s0 == other._s0 && _s1 == other._s1 && _s2 == other._s2 && _s3 == other._s3
        && _s4 == other._s4 && _s5 == other._s5 && _s6 == other._s6 && _s7 == other._s7;

    public readonly override bool Equals(object? obj) => obj is BossSlotArray b && Equals(b);

    public readonly override int GetHashCode() => unchecked(((((((_s0 * 31 + _s1) * 31 + _s2) * 31 + _s3) * 31 + _s4) * 31 + _s5) * 31 + _s6) * 31 + _s7);
}
