#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// The per-tick CPU accumulator, broken down by mod and by hook category. The
/// timing detours installed by <see cref="HookInterceptor"/> (delegate backend)
/// and <see cref="ILHookInterceptor"/> (ILHook backend) call <see cref="Add"/>
/// from inside each mod's hook code; the profiler resets it at the start of
/// every tick and harvests it at the end.
///
/// <para>
/// Per <see cref="HookBackend"/>, the storage is sized for one or two backends
/// running in parallel. Each backend writes into its own contiguous slice; the
/// collector harvests each slice separately. In single-backend modes (Delegate
/// or ILHook), backendCount = 1 and there is no overhead. In Parallel mode the
/// storage doubles -- still tiny: an 18-mod / 7-category install costs 126
/// longs per backend = 2 KB total.
/// </para>
///
/// <para>
/// Single-threaded by design on each backend's hot path: tModLoader's hook
/// dispatch runs on the game's update thread, the profiler's tick boundaries
/// run there too, so no locking is needed for accumulation. Draw-thread hooks
/// also reach Add but in practice never overlap a tick boundary with the
/// update thread (BeginTick/HarvestInto are called from PreUpdateEntities /
/// PostUpdateEverything). The races that exist are race-on-counter, not
/// race-on-structure, and produce at most a one-tick mis-attribution -- not
/// a correctness bug.
/// </para>
///
/// <para>
/// Storage is one fixed array per backend, sized once, so the per-tick calls
/// never allocate (Invariant 2).
/// </para>
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

    // Accumulated Stopwatch ticks for the current game tick, per backend.
    // _ticksByBackend[backendId][modId * CategoryCount + categoryId].
    private static long[][] _ticksByBackend = Array.Empty<long[]>();

    // Accumulated Stopwatch ticks per installed hook for the current game tick, per backend.
    // _hookTicksByBackend[backendId][hookId].
    private static long[][] _hookTicksByBackend = Array.Empty<long[]>();

    // Registered during install only; read by the collector/UI after setup.
    // Hooks are shared across backends -- a delegate detour and an ILHook detour
    // for the same method share the same hookId. Backends differ only by where
    // they write the elapsed ticks, not by what they identify.
    private static readonly List<HookDescriptor> _hooks = new List<HookDescriptor>();

    /// <summary>Number of mods being attributed; 0 until <see cref="Configure"/> runs.</summary>
    public static int ModCount =>
        _ticksByBackend.Length == 0 ? 0 : _ticksByBackend[0].Length / CategoryCount;

    /// <summary>Number of attribution backends currently sized (1 or 2).</summary>
    public static int BackendCount => _ticksByBackend.Length;

    /// <summary>Number of individually timed hooks registered at setup.</summary>
    public static int HookCount => _hooks.Count;

    /// <summary>Metadata for each timed hook, indexed by hookId.</summary>
    public static IReadOnlyList<HookDescriptor> Hooks => _hooks;

    /// <summary>Sizes the accumulator for <paramref name="modCount"/> mods with one backend.</summary>
    public static void Configure(int modCount)
    {
        Configure(modCount, backendCount: 1);
    }

    /// <summary>
    /// Sizes the accumulator for <paramref name="modCount"/> mods across
    /// <paramref name="backendCount"/> backends. Called once at setup.
    /// </summary>
    public static void Configure(int modCount, int backendCount)
    {
        if (modCount < 0) modCount = 0;
        if (backendCount < 1) backendCount = 1;

        int cells = modCount * CategoryCount;
        _ticksByBackend = new long[backendCount][];
        _hookTicksByBackend = new long[backendCount][];
        for (int b = 0; b < backendCount; b++)
        {
            _ticksByBackend[b] = new long[cells];
            _hookTicksByBackend[b] = Array.Empty<long>();
        }
        _hooks.Clear();
    }

    /// <summary>
    /// Registers one hook timing row. Returns its hookId. Called during setup,
    /// never from the hot path. Both backends share the registration so that
    /// in parallel mode a hookId resolves to the same descriptor regardless of
    /// which backend produced the measurement.
    /// </summary>
    public static int RegisterHook(int modId, int categoryId, string displayName)
    {
        int hookId = _hooks.Count;
        _hooks.Add(new HookDescriptor(modId, categoryId, displayName));
        for (int b = 0; b < _hookTicksByBackend.Length; b++)
        {
            Array.Resize(ref _hookTicksByBackend[b], _hooks.Count);
        }
        return hookId;
    }

    /// <summary>
    /// Registers a hook idempotently using a stable (modId, categoryId, displayName)
    /// key; if a hook with the same key is already registered, returns its hookId.
    /// Lets the ILHook and delegate backends in Parallel mode share hookIds without
    /// duplicating rows for the same target method.
    /// </summary>
    public static int RegisterOrReuseHook(int modId, int categoryId, string displayName)
    {
        for (int i = 0; i < _hooks.Count; i++)
        {
            HookDescriptor existing = _hooks[i];
            if (existing.ModId == modId && existing.CategoryId == categoryId &&
                existing.DisplayName == displayName)
            {
                return i;
            }
        }

        return RegisterHook(modId, categoryId, displayName);
    }

    /// <summary>Clears every running total. Called at the start of each tick.</summary>
    public static void BeginTick()
    {
        for (int b = 0; b < _ticksByBackend.Length; b++)
        {
            Array.Clear(_ticksByBackend[b], 0, _ticksByBackend[b].Length);
            Array.Clear(_hookTicksByBackend[b], 0, _hookTicksByBackend[b].Length);
        }
    }

    /// <summary>
    /// Legacy two-arg overload: credits backend 0 with no hook breakdown.
    /// </summary>
    public static void Add(int modId, int categoryId, long elapsedStopwatchTicks)
    {
        Add(0, modId, categoryId, hookId: -1, elapsedStopwatchTicks);
    }

    /// <summary>
    /// Legacy three-id overload: credits backend 0 (the delegate path's slot).
    /// </summary>
    public static void Add(int modId, int categoryId, int hookId, long elapsedStopwatchTicks)
    {
        Add(0, modId, categoryId, hookId, elapsedStopwatchTicks);
    }

    /// <summary>
    /// Adds elapsed Stopwatch ticks to one mod/category total in the slot owned
    /// by <paramref name="backendId"/>. Called from timing detours on either
    /// backend, so it stays allocation-free and trivially cheap. Out-of-range
    /// indices are ignored rather than throwing, since this runs inside other
    /// mods' code and must never disrupt them (Invariant 1).
    /// </summary>
    public static void Add(int backendId, int modId, int categoryId, int hookId, long elapsedStopwatchTicks)
    {
        if ((uint)backendId >= (uint)_ticksByBackend.Length)
        {
            return;
        }

        if ((uint)categoryId >= (uint)CategoryCount)
        {
            return;
        }

        long[] ticks = _ticksByBackend[backendId];
        int index = modId * CategoryCount + categoryId;
        if ((uint)index < (uint)ticks.Length)
        {
            ticks[index] += elapsedStopwatchTicks;
        }

        long[] hookTicks = _hookTicksByBackend[backendId];
        if ((uint)hookId < (uint)hookTicks.Length)
        {
            hookTicks[hookId] += elapsedStopwatchTicks;
        }
    }

    /// <summary>
    /// Writes backend 0's per-mod/category totals for the tick, in milliseconds,
    /// into <paramref name="destination"/>. Provided for callers that don't care
    /// about backend separation.
    /// </summary>
    public static void HarvestInto(double[] destination)
    {
        HarvestInto(destination, backendId: 0);
    }

    /// <summary>
    /// Writes one backend's per-mod/category totals for the tick, in milliseconds,
    /// into <paramref name="destination"/>. Same [modId * CategoryCount + categoryId]
    /// layout. The destination buffer is supplied by the caller so harvesting
    /// never allocates.
    /// </summary>
    public static void HarvestInto(double[] destination, int backendId)
    {
        if ((uint)backendId >= (uint)_ticksByBackend.Length)
        {
            Array.Clear(destination, 0, destination.Length);
            return;
        }

        long[] ticks = _ticksByBackend[backendId];
        double ticksToMs = 1000d / Stopwatch.Frequency;
        int n = ticks.Length < destination.Length ? ticks.Length : destination.Length;
        for (int i = 0; i < n; i++)
        {
            destination[i] = ticks[i] * ticksToMs;
        }
    }

    /// <summary>
    /// Writes backend 0's per-hook totals for the tick, in milliseconds, into
    /// <paramref name="destination"/>.
    /// </summary>
    public static void HarvestHooksInto(double[] destination)
    {
        HarvestHooksInto(destination, backendId: 0);
    }

    /// <summary>
    /// Writes one backend's per-hook totals for the tick, in milliseconds, into
    /// <paramref name="destination"/>. The destination buffer is caller-owned.
    /// </summary>
    public static void HarvestHooksInto(double[] destination, int backendId)
    {
        if ((uint)backendId >= (uint)_hookTicksByBackend.Length)
        {
            Array.Clear(destination, 0, destination.Length);
            return;
        }

        long[] hookTicks = _hookTicksByBackend[backendId];
        double ticksToMs = 1000d / Stopwatch.Frequency;
        int n = hookTicks.Length < destination.Length ? hookTicks.Length : destination.Length;
        for (int i = 0; i < n; i++)
        {
            destination[i] = hookTicks[i] * ticksToMs;
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
