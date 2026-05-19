#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// The per-tick CPU accumulator, broken down by mod and by hook category. The
/// timing detours installed by <see cref="HookInterceptor"/> call <see cref="Add"/>
/// from inside each mod's hook code; the profiler resets it at the start of
/// every tick and harvests it at the end.
///
/// The per-category split is what lets the overlay's tree fold a mod row open
/// into a Systems / Players / NPCs / Projectiles breakdown.
///
/// Single-threaded by design: tModLoader's hook dispatch and the profiler's
/// tick boundaries all run on the game's update thread, so no locking is
/// needed. Storage is one fixed array sized once, so the per-tick calls never
/// allocate (Invariant 2).
/// </summary>
public static class PerModAttribution
{
    /// <summary>Hook categories a mod's cost is split across. The index is the categoryId.</summary>
    public static readonly string[] CategoryNames =
    {
        "Systems", "Players", "NPCs", "Projectiles", "Items", "World", "Buffs",
    };

    /// <summary>Number of hook categories.</summary>
    public static int CategoryCount => CategoryNames.Length;

    // Accumulated Stopwatch ticks for the current game tick.
    // Indexed [modId * CategoryCount + categoryId].
    private static long[] _ticks = Array.Empty<long>();

    // Accumulated Stopwatch ticks per installed hook for the current game tick.
    private static long[] _hookTicks = Array.Empty<long>();

    // Registered during HookInterceptor.Install only; read by the collector/UI after setup.
    private static readonly List<HookDescriptor> _hooks = new List<HookDescriptor>();

    /// <summary>Number of mods being attributed; 0 until <see cref="Configure"/> runs.</summary>
    public static int ModCount => _ticks.Length / CategoryCount;

    /// <summary>Number of individually timed hooks registered at setup.</summary>
    public static int HookCount => _hooks.Count;

    /// <summary>Metadata for each timed hook, indexed by hookId.</summary>
    public static IReadOnlyList<HookDescriptor> Hooks => _hooks;

    /// <summary>Sizes the accumulator for <paramref name="modCount"/> mods. Called once at setup.</summary>
    public static void Configure(int modCount)
    {
        _ticks = new long[(modCount < 0 ? 0 : modCount) * CategoryCount];
        _hookTicks = Array.Empty<long>();
        _hooks.Clear();
    }

    /// <summary>
    /// Registers one hook timing row. Called during setup, never from the hot path.
    /// </summary>
    public static int RegisterHook(int modId, int categoryId, string displayName)
    {
        int hookId = _hooks.Count;
        _hooks.Add(new HookDescriptor(modId, categoryId, displayName));
        Array.Resize(ref _hookTicks, _hooks.Count);
        return hookId;
    }

    /// <summary>Clears every running total. Called at the start of each tick.</summary>
    public static void BeginTick()
    {
        Array.Clear(_ticks, 0, _ticks.Length);
        Array.Clear(_hookTicks, 0, _hookTicks.Length);
    }

    /// <summary>
    /// Adds elapsed Stopwatch ticks to one mod-and-category total. Called from
    /// the timing detours, so it stays allocation-free and trivially cheap. An
    /// out-of-range index is ignored rather than throwing, since this runs
    /// inside other mods' code and must never disrupt them (Invariant 1).
    /// </summary>
    public static void Add(int modId, int categoryId, long elapsedStopwatchTicks)
    {
        Add(modId, categoryId, hookId: -1, elapsedStopwatchTicks);
    }

    /// <summary>
    /// Adds elapsed Stopwatch ticks to one mod/category total and, when supplied,
    /// the exact installed-hook total. Called from timing detours only.
    /// </summary>
    public static void Add(int modId, int categoryId, int hookId, long elapsedStopwatchTicks)
    {
        if ((uint)categoryId >= (uint)CategoryCount)
        {
            return;
        }

        int index = modId * CategoryCount + categoryId;
        if ((uint)index < (uint)_ticks.Length)
        {
            _ticks[index] += elapsedStopwatchTicks;
        }

        if ((uint)hookId < (uint)_hookTicks.Length)
        {
            _hookTicks[hookId] += elapsedStopwatchTicks;
        }
    }

    /// <summary>
    /// Writes every mod-and-category total for the tick, in milliseconds, into
    /// <paramref name="destination"/> (same [modId * CategoryCount + categoryId]
    /// layout). The destination buffer is supplied by the caller so harvesting
    /// never allocates.
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

    /// <summary>
    /// Writes every registered-hook total for the tick, in milliseconds, into
    /// <paramref name="destination"/>. The destination buffer is caller-owned.
    /// </summary>
    public static void HarvestHooksInto(double[] destination)
    {
        double ticksToMs = 1000d / Stopwatch.Frequency;
        int n = _hookTicks.Length < destination.Length ? _hookTicks.Length : destination.Length;
        for (int i = 0; i < n; i++)
        {
            destination[i] = _hookTicks[i] * ticksToMs;
        }
    }
}

/// <summary>Stable metadata for one installed timing hook.</summary>
public readonly struct HookDescriptor
{
    public readonly int ModId;
    public readonly int CategoryId;
    public readonly string DisplayName;

    public HookDescriptor(int modId, int categoryId, string displayName)
    {
        ModId = modId;
        CategoryId = categoryId;
        DisplayName = displayName;
    }
}
