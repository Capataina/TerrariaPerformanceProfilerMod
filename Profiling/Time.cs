#nullable enable

using System;
using System.Diagnostics;

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
/// Project-wide Unix-millisecond clock backed by a single
/// <see cref="Stopwatch"/> read. Every row emitter in the persistence
/// layer used to call <c>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()</c>
/// on the game thread, which boxes a <c>DateTimeOffset</c> struct, runs
/// internal offset math, and was repeated in 28 sites across the
/// codebase per the cross-allocations dossier §3.1.
///
/// <para>
/// This helper captures a single wall-clock origin at mod load
/// (<see cref="Reset"/>), then computes every subsequent timestamp as
/// <c>origin + delta_from_stopwatch</c>. <see cref="UnixMsNow"/> is a
/// pair of long reads + a multiplication — no allocation, no boxing.
/// </para>
///
/// <para>
/// The drift profile across a multi-hour session is bounded by the
/// monotonicity of <see cref="Stopwatch"/> on the platforms tModLoader
/// runs on (macOS / Windows / Linux on x86_64 / arm64); cross-process
/// suspends / sleeps will skew the reading vs system time. For our
/// use (row timestamps relative to each other within a session) the
/// monotonic stopwatch is the right reference. <see cref="Reset"/> can
/// be called periodically to re-anchor if needed.
/// </para>
/// </summary>
public static class Time
{
    private static long _originUnixMs;
    private static long _originTimestamp;
    private static double _ticksToMs = 1000.0 / Stopwatch.Frequency;

    static Time()
    {
        Reset();
    }

    /// <summary>
    /// Capture a fresh wall-clock origin. Called at mod load and any time
    /// the wall-clock and stopwatch should be re-aligned (e.g. after a
    /// known process suspend).
    /// </summary>
    public static void Reset()
    {
        _originUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _originTimestamp = Stopwatch.GetTimestamp();
        _ticksToMs = 1000.0 / Stopwatch.Frequency;
    }

    /// <summary>
    /// Returns the current Unix time in milliseconds, computed from the
    /// origin captured at <see cref="Reset"/> plus the elapsed Stopwatch
    /// delta. Zero allocation. Approximately 5–10 ns per call on Apple
    /// Silicon and recent Intel/AMD; vs ~150–250 ns for
    /// <c>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()</c>.
    /// </summary>
    public static long UnixMsNow()
    {
        long delta = Stopwatch.GetTimestamp() - _originTimestamp;
        return _originUnixMs + (long)(delta * _ticksToMs);
    }
}
