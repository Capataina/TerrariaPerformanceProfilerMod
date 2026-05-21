#nullable enable

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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
/// Thread-local re-entrant timing state for the ILHook backend.
///
/// Each instrumented hook body is wrapped in IL with
/// <c>ProbeStack.Enter(hookId); try { ...orig body... } finally { ProbeStack.Leave(); }</c>.
/// Enter pushes a (hookId, startTicks) frame onto a per-thread stack; Leave pops the
/// most recent frame, computes elapsed time, and credits it.
///
/// <para>
/// Why a stack? A mod's NPC.AI override may itself trigger another hooked hook on the
/// same thread (e.g. spawning an NPC, which fires GlobalNPC.OnSpawn). A flat
/// <c>static long _start</c> would be overwritten by the inner Enter and the outer
/// Leave would mis-credit a fraction of a tick's runtime. LIFO frames are correct
/// under arbitrary nesting and under exception unwind, because the IL <c>finally</c>
/// guarantees Leave runs once per Enter.
/// </para>
///
/// <para>
/// Allocation discipline: the stack is a pre-grown struct array per thread, sized once
/// on first use, doubled if depth exceeds capacity. After warmup (a few ticks) the
/// hot path never allocates. Per Invariant 2, this matches the per-call cost shape of
/// the delegate-pair <see cref="HookProbe"/> system -- two Stopwatch reads, one static
/// call, no boxing.
/// </para>
///
/// <para>
/// Invariant 1 (read-only): the only state Enter/Leave touch is per-thread profiler
/// memory and <see cref="PerModAttribution"/>. Game state is never read or modified.
/// </para>
/// </summary>
public static class ProbeStack
{
    internal struct Frame
    {
        public int HookId;
        public long StartTicks;
        /// <summary>
        /// Allocated-bytes counter at entry. 0 when allocation tracking is off
        /// (the Lite path never reads or writes this slot). Kept on the same
        /// stack as StartTicks rather than in a parallel structure so the LIFO
        /// discipline that makes CPU attribution correct under nesting also
        /// makes allocation attribution correct -- two stacks could desync;
        /// one stack is one source of truth.
        /// </summary>
        public long StartAllocBytes;
    }

    // tModLoader hooks fire predominantly on the game's update thread, but draw-thread
    // hooks (e.g. PostDrawInterface) and any mod-spawned background tasks also reach
    // hooked methods. ThreadStatic gives each thread its own stack with zero locking.
    [ThreadStatic] private static Frame[]? _stack;
    [ThreadStatic] private static int _depth;

    // Initial capacity: comfortably above the deepest nesting we observe in practice
    // (typically <8). Resize doubles when needed -- amortised O(1) and only at warmup.
    private const int InitialCapacity = 32;

    /// <summary>
    /// IL-prologue entry point. Pushes a frame and starts the Stopwatch.
    /// Called once per hook entry. The hookId is emitted as an inline Ldc_I4
    /// constant by the IL manipulator, so this is effectively a static call
    /// with one int argument -- no allocation, no boxing.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Enter(int hookId)
    {
        Frame[]? s = _stack;
        if (s == null)
        {
            s = new Frame[InitialCapacity];
            _stack = s;
        }
        else if (_depth == s.Length)
        {
            // Doubling growth: rare after warmup. We can swallow the resize cost
            // because it never recurs at steady-state.
            Frame[] grown = new Frame[s.Length * 2];
            Array.Copy(s, grown, s.Length);
            s = grown;
            _stack = grown;
        }

        s[_depth].HookId = hookId;
        s[_depth].StartTicks = Stopwatch.GetTimestamp();
        _depth++;
    }

    /// <summary>
    /// IL-finally exit point. Pops the most recent frame, credits the elapsed
    /// Stopwatch ticks to the hook's mod/category/hook slots via
    /// <see cref="PerModAttribution"/>. Defensive against an underflow: a paired
    /// Leave with no Enter is silently dropped rather than throwing, so a bad
    /// manipulator can never crash the host (Invariant 4).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Leave()
    {
        int d = _depth - 1;
        Frame[]? s = _stack;
        if (s == null || (uint)d >= (uint)s.Length)
        {
            return;
        }

        _depth = d;
        Frame f = s[d];
        long elapsed = Stopwatch.GetTimestamp() - f.StartTicks;

        // Resolve descriptor and credit. Bounds-checked inside Add.
        if ((uint)f.HookId < (uint)PerModAttribution.Hooks.Count)
        {
            HookDescriptor desc = PerModAttribution.Hooks[f.HookId];
            PerModAttribution.Add(HookBackend.ILHookBackendId, desc.ModId, desc.CategoryId, f.HookId, elapsed);
        }
    }

    /// <summary>
    /// IL-prologue entry point for the allocation-tracking path.
    /// <paramref name="allocBytesAtEnter"/> is captured by the IL caller via
    /// <c>GC.GetAllocatedBytesForCurrentThread</c> and pushed on the stack
    /// before this call -- saves one method-call frame compared to reading
    /// the API inside this method, and keeps Enter symmetric with Leave's
    /// reading-it-internally model.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnterCpuAlloc(int hookId, long allocBytesAtEnter)
    {
        Frame[]? s = _stack;
        if (s == null)
        {
            s = new Frame[InitialCapacity];
            _stack = s;
        }
        else if (_depth == s.Length)
        {
            Frame[] grown = new Frame[s.Length * 2];
            Array.Copy(s, grown, s.Length);
            s = grown;
            _stack = grown;
        }

        s[_depth].HookId = hookId;
        s[_depth].StartTicks = Stopwatch.GetTimestamp();
        s[_depth].StartAllocBytes = allocBytesAtEnter;
        _depth++;
    }

    /// <summary>
    /// IL-finally exit point for the allocation-tracking path. Reads BOTH
    /// the Stopwatch and the allocation counter internally so the IL handler
    /// keeps the parameterless <c>call</c> shape used by the Lite path --
    /// preserves the ExceptionHandler IL surface from the original
    /// migration plan.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LeaveCpuAlloc()
    {
        int d = _depth - 1;
        Frame[]? s = _stack;
        if (s == null || (uint)d >= (uint)s.Length) return;

        long endAllocBytes = System.GC.GetAllocatedBytesForCurrentThread();
        long endTicks = Stopwatch.GetTimestamp();

        _depth = d;
        Frame f = s[d];
        long elapsedTicks = endTicks - f.StartTicks;
        long elapsedBytes = endAllocBytes - f.StartAllocBytes;
        // elapsedBytes is non-negative by construction -- the per-thread
        // counter is monotonic. The Stopwatch elapsed can be negative under
        // exotic clock-shift scenarios (not on managed runtimes in practice),
        // so leave that to the consumer.

        if ((uint)f.HookId < (uint)PerModAttribution.Hooks.Count)
        {
            HookDescriptor desc = PerModAttribution.Hooks[f.HookId];
            PerModAttribution.Add(HookBackend.ILHookBackendId, desc.ModId, desc.CategoryId,
                f.HookId, elapsedTicks, elapsedBytes);
        }
    }

    /// <summary>
    /// Diagnostic only: current depth on the calling thread. Used by the
    /// validation logging to surface "probe stack leaking" if Enter is called
    /// without a matching Leave.
    /// </summary>
    public static int CurrentDepth => _depth;
}
