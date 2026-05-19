#nullable enable

using System;
using System.Diagnostics;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// The per-tick, per-mod CPU accumulator. The timing detours installed by
/// <see cref="HookInterceptor"/> call <see cref="Add"/> from inside each mod's
/// hook code; the profiler resets it at the start of every tick and harvests
/// it at the end.
///
/// Single-threaded by design: tModLoader's hook dispatch and the profiler's
/// tick boundaries all run on the game's update thread, so no locking is
/// needed. Storage is one fixed array sized once, so <see cref="Add"/>,
/// <see cref="BeginTick"/> and <see cref="HarvestInto"/> never allocate
/// (Invariant 2).
/// </summary>
public static class PerModAttribution
{
    // Accumulated Stopwatch ticks for the current game tick, indexed by ModId.
    private static long[] _ticks = Array.Empty<long>();

    /// <summary>Number of mods being attributed; 0 until <see cref="Configure"/> runs.</summary>
    public static int ModCount => _ticks.Length;

    /// <summary>Sizes the accumulator for <paramref name="modCount"/> mods. Called once at setup.</summary>
    public static void Configure(int modCount)
    {
        _ticks = new long[modCount < 0 ? 0 : modCount];
    }

    /// <summary>Clears every mod's running total. Called at the start of each tick.</summary>
    public static void BeginTick()
    {
        Array.Clear(_ticks, 0, _ticks.Length);
    }

    /// <summary>
    /// Adds elapsed Stopwatch ticks to a mod's running total. Called from the
    /// timing detours, so it stays allocation-free and trivially cheap. An
    /// out-of-range id is ignored rather than throwing, since this runs inside
    /// other mods' code and must never disrupt them (Invariant 1).
    /// </summary>
    public static void Add(int modId, long elapsedStopwatchTicks)
    {
        if ((uint)modId < (uint)_ticks.Length)
        {
            _ticks[modId] += elapsedStopwatchTicks;
        }
    }

    /// <summary>
    /// Writes each mod's accumulated time for the tick, in milliseconds, into
    /// <paramref name="destination"/> indexed by ModId. The destination buffer
    /// is supplied by the caller so harvesting never allocates.
    /// </summary>
    public static void HarvestInto(double[] destination)
    {
        double ticksToMs = 1000d / Stopwatch.Frequency;
        int n = _ticks.Length < destination.Length ? _ticks.Length : destination.Length;
        for (int i = 0; i < n; i++)
        {
            destination[i] = _ticks[i] * ticksToMs;
        }
    }
}
