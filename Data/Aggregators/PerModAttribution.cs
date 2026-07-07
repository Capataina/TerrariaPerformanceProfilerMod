#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Data.Aggregators;

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

    // Stopwatch-ticks → milliseconds. Stopwatch.Frequency is fixed at process
    // boot, so caching the reciprocal turns the per-harvest division (run 2-4×
    // per tick) into a multiply, matching the Time.cs:46 convention.
    private static readonly double TicksToMs = 1000d / Stopwatch.Frequency;

    /// <summary>Number of hook categories.</summary>
    public static int CategoryCount => CategoryNames.Length;

    // Accumulated Stopwatch ticks for the current game tick, per backend.
    // _ticksByBackend[backendId][modId * CategoryCount + categoryId].
    private static long[][] _ticksByBackend = Array.Empty<long[]>();

    // Accumulated Stopwatch ticks per installed hook for the current game tick, per backend.
    // _hookTicksByBackend[backendId][hookId].
    private static long[][] _hookTicksByBackend = Array.Empty<long[]>();

    // Accumulated allocated bytes for the current game tick, per backend. Parallel
    // structure to _ticksByBackend / _hookTicksByBackend. Sized only when allocation
    // tracking is on, so Lite-style modes pay zero memory cost here.
    private static long[][] _bytesByBackend = Array.Empty<long[]>();
    private static long[][] _hookBytesByBackend = Array.Empty<long[]>();

    // ---- Loop-anatomy phase lanes (atlas S01, 2026-07-07) -------------------
    // Draw-phase mirrors of the tick accumulators. A probe that STARTED outside
    // the update window (draw + present + off-tick — everything between EndTick
    // and the next BeginTick) credits these instead of the primary arrays, so
    // per-mod cost splits into update-ms vs draw-ms. Update-phase writes keep
    // hitting the original arrays untouched — with the lanes disabled the
    // behaviour is bit-identical to pre-S01.
    // Cost: mirrors are modCount×CategoryCount longs (~1.6 KB at 29 mods) plus
    // hookCount longs (~0.5 MB at 62k hooks) per backend; one static bool read
    // + branch per Add. Invariant 2: measured in the phase-lane diagnostics test.
    private static long[][] _drawTicksByBackend = Array.Empty<long[]>();
    private static long[][] _hookDrawTicksByBackend = Array.Empty<long[]>();

    /// <summary>
    /// True while the game's update window is open (set by
    /// <see cref="Profiling.MetricCollector"/> BeginTick/EndTick). Terraria's
    /// loop is single-threaded, so a plain static bool is sufficient; the rare
    /// draw-thread probe that races a boundary mis-lanes ONE sample by one
    /// phase — a magnitude-irrelevant error, same tolerance as the existing
    /// race-on-counter note above.
    /// </summary>
    public static bool CurrentPhaseIsUpdate;

    /// <summary>True when the draw-lane mirrors are sized (PhaseSplitAttribution on).</summary>
    public static bool PhaseLanesEnabled => _drawTicksByBackend.Length > 0;

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

    /// <summary>Sizes the accumulator for <paramref name="modCount"/> mods with one backend, CPU only.</summary>
    public static void Configure(int modCount)
    {
        Configure(modCount, backendCount: 1, trackAllocations: false);
    }

    /// <summary>Sizes the accumulator for <paramref name="modCount"/> mods across backends, CPU only.</summary>
    public static void Configure(int modCount, int backendCount)
    {
        Configure(modCount, backendCount, trackAllocations: false);
    }

    /// <summary>
    /// Sizes the accumulator for <paramref name="modCount"/> mods across
    /// <paramref name="backendCount"/> backends, optionally allocating parallel
    /// byte arrays for allocation tracking. Called once at setup.
    /// </summary>
    public static void Configure(int modCount, int backendCount, bool trackAllocations)
        => Configure(modCount, backendCount, trackAllocations, phaseLanes: true);

    /// <summary>
    /// Full overload: <paramref name="phaseLanes"/> sizes the draw-phase
    /// mirrors (atlas S01). Off ⇒ every sample lands in the primary lane and
    /// behaviour is bit-identical to pre-S01.
    /// </summary>
    public static void Configure(int modCount, int backendCount, bool trackAllocations, bool phaseLanes)
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

        if (phaseLanes)
        {
            _drawTicksByBackend = new long[backendCount][];
            _hookDrawTicksByBackend = new long[backendCount][];
            for (int b = 0; b < backendCount; b++)
            {
                _drawTicksByBackend[b] = new long[cells];
                _hookDrawTicksByBackend[b] = Array.Empty<long>();
            }
        }
        else
        {
            _drawTicksByBackend = Array.Empty<long[]>();
            _hookDrawTicksByBackend = Array.Empty<long[]>();
        }
        CurrentPhaseIsUpdate = false;

        if (trackAllocations)
        {
            _bytesByBackend = new long[backendCount][];
            _hookBytesByBackend = new long[backendCount][];
            for (int b = 0; b < backendCount; b++)
            {
                _bytesByBackend[b] = new long[cells];
                _hookBytesByBackend[b] = Array.Empty<long>();
            }
        }
        else
        {
            _bytesByBackend = Array.Empty<long[]>();
            _hookBytesByBackend = Array.Empty<long[]>();
        }

        _hooks.Clear();
    }

    /// <summary>True if Configure was called with allocation tracking on.</summary>
    public static bool TracksAllocations => _bytesByBackend.Length > 0;

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
        for (int b = 0; b < _hookBytesByBackend.Length; b++)
        {
            Array.Resize(ref _hookBytesByBackend[b], _hooks.Count);
        }
        for (int b = 0; b < _hookDrawTicksByBackend.Length; b++)
        {
            Array.Resize(ref _hookDrawTicksByBackend[b], _hooks.Count);
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
        for (int b = 0; b < _bytesByBackend.Length; b++)
        {
            Array.Clear(_bytesByBackend[b], 0, _bytesByBackend[b].Length);
            Array.Clear(_hookBytesByBackend[b], 0, _hookBytesByBackend[b].Length);
        }
        for (int b = 0; b < _drawTicksByBackend.Length; b++)
        {
            Array.Clear(_drawTicksByBackend[b], 0, _drawTicksByBackend[b].Length);
            Array.Clear(_hookDrawTicksByBackend[b], 0, _hookDrawTicksByBackend[b].Length);
        }
    }

    /// <summary>
    /// Three-id overload used by the delegate-path HookProbes: credits backend 0
    /// (the delegate path's slot). Equivalent to the four-arg form with
    /// <c>backendId: 0</c>; kept as a separate entry so the hot path's call site
    /// is short.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        // Phase lane (S01): the primary arrays above carry the TOTAL exactly as
        // before (every existing consumer bit-identical); a draw-phase sample
        // ADDITIONALLY credits the draw mirror, so update = total − draw at
        // harvest. Phase is read at credit time; a probe spanning the tick
        // boundary mis-lanes one sample by one phase (documented tolerance).
        if (!CurrentPhaseIsUpdate && (uint)backendId < (uint)_drawTicksByBackend.Length)
        {
            long[] drawTicks = _drawTicksByBackend[backendId];
            if ((uint)index < (uint)drawTicks.Length)
            {
                drawTicks[index] += elapsedStopwatchTicks;
            }
            long[] hookDraw = _hookDrawTicksByBackend[backendId];
            if ((uint)hookId < (uint)hookDraw.Length)
            {
                hookDraw[hookId] += elapsedStopwatchTicks;
            }
        }
    }

    /// <summary>
    /// Adds elapsed Stopwatch ticks AND allocated bytes to one mod/category
    /// total in <paramref name="backendId"/>'s slot. Used by the allocation-
    /// tracking ILHook path (<see cref="ProbeStack.LeaveCpuAlloc"/>) so the
    /// hot path makes one call instead of two. The byte credit is a no-op
    /// if Configure was called without trackAllocations.
    /// </summary>
    public static void Add(int backendId, int modId, int categoryId, int hookId,
        long elapsedStopwatchTicks, long allocatedBytes)
    {
        if ((uint)backendId >= (uint)_ticksByBackend.Length) return;
        if ((uint)categoryId >= (uint)CategoryCount) return;

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

        // Phase lane (S01) — see the CPU-only overload for the contract.
        if (!CurrentPhaseIsUpdate && (uint)backendId < (uint)_drawTicksByBackend.Length)
        {
            long[] drawTicks = _drawTicksByBackend[backendId];
            if ((uint)index < (uint)drawTicks.Length)
            {
                drawTicks[index] += elapsedStopwatchTicks;
            }
            long[] hookDraw = _hookDrawTicksByBackend[backendId];
            if ((uint)hookId < (uint)hookDraw.Length)
            {
                hookDraw[hookId] += elapsedStopwatchTicks;
            }
        }

        // Allocation credit (only if Configure(trackAllocations: true) was called).
        if ((uint)backendId < (uint)_bytesByBackend.Length)
        {
            long[] bytes = _bytesByBackend[backendId];
            if ((uint)index < (uint)bytes.Length)
            {
                bytes[index] += allocatedBytes;
            }
            long[] hookBytes = _hookBytesByBackend[backendId];
            if ((uint)hookId < (uint)hookBytes.Length)
            {
                hookBytes[hookId] += allocatedBytes;
            }
        }
    }

    /// <summary>
    /// Writes one backend's DRAW-PHASE per-mod/category totals for the tick,
    /// in milliseconds, into <paramref name="destination"/> (S01). Zero-filled
    /// when the phase lanes are disabled. Same layout and caller-owned-buffer
    /// discipline as <see cref="HarvestInto(double[], int)"/>.
    /// </summary>
    public static void HarvestDrawInto(double[] destination, int backendId)
    {
        if ((uint)backendId >= (uint)_drawTicksByBackend.Length)
        {
            Array.Clear(destination, 0, destination.Length);
            return;
        }

        long[] ticks = _drawTicksByBackend[backendId];
        int n = ticks.Length < destination.Length ? ticks.Length : destination.Length;
        for (int i = 0; i < n; i++)
        {
            destination[i] = ticks[i] * TicksToMs;
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
        int n = ticks.Length < destination.Length ? ticks.Length : destination.Length;
        for (int i = 0; i < n; i++)
        {
            destination[i] = ticks[i] * TicksToMs;
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
        int n = hookTicks.Length < destination.Length ? hookTicks.Length : destination.Length;
        for (int i = 0; i < n; i++)
        {
            destination[i] = hookTicks[i] * TicksToMs;
        }
    }

    /// <summary>
    /// Writes one backend's per-mod/category allocation totals for the tick,
    /// in raw bytes, into <paramref name="destination"/>. No unit conversion --
    /// callers format bytes / KB / MB depending on magnitude. Destination is
    /// zero-filled if allocation tracking is off.
    /// </summary>
    public static void HarvestAllocationsInto(double[] destination, int backendId)
    {
        if ((uint)backendId >= (uint)_bytesByBackend.Length)
        {
            Array.Clear(destination, 0, destination.Length);
            return;
        }

        long[] bytes = _bytesByBackend[backendId];
        int n = bytes.Length < destination.Length ? bytes.Length : destination.Length;
        for (int i = 0; i < n; i++)
        {
            destination[i] = bytes[i];
        }
    }

    /// <summary>
    /// Writes one backend's per-hook allocation totals for the tick, in raw
    /// bytes, into <paramref name="destination"/>. Destination is zero-filled
    /// if allocation tracking is off.
    /// </summary>
    public static void HarvestHookAllocationsInto(double[] destination, int backendId)
    {
        if ((uint)backendId >= (uint)_hookBytesByBackend.Length)
        {
            Array.Clear(destination, 0, destination.Length);
            return;
        }

        long[] hookBytes = _hookBytesByBackend[backendId];
        int n = hookBytes.Length < destination.Length ? hookBytes.Length : destination.Length;
        for (int i = 0; i < n; i++)
        {
            destination[i] = hookBytes[i];
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
