#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// Cause classification for a stall — the diagnostic story attached to each
/// event so the player and the agent can both reason about why the game
/// froze, not just that it did.
/// </summary>
public enum StallCause : byte
{
    /// <summary>None of the signals matched cleanly — likely an exotic case worth investigating manually.</summary>
    Unknown = 0,
    /// <summary>Gen2 GC collected during the stall and GC pause dominates the gap. The 200 MB heap problem in action.</summary>
    MajorGc = 1,
    /// <summary>Gen0 or Gen1 GC collected and GC pause dominates; no Gen2. Usually brief and recoverable.</summary>
    MinorGc = 2,
    /// <summary>Wall clock advanced but the process CPU didn't — the OS suspended us (background, app switcher, sleep).</summary>
    ProcessSuspended = 3,
    /// <summary>Wall and CPU both advanced, GC didn't — code stall. Lock contention, sync I/O, JIT compile, draw-thread hook blocking.</summary>
    LongFrame = 4,
}

/// <summary>
/// Perceptual-severity ladder for stall badging. Used by the overlay and
/// session JSON to communicate "how bad the freeze actually looked" to the
/// player, independent of whether it was a real outlier relative to their
/// session baseline (the trigger uses the relative comparison; this is purely
/// for display).
///
/// <para>
/// The ladder values are absolute wall time of the stall. The eye doesn't
/// know about FPS — a 500 ms freeze looks like a 500 ms freeze whether the
/// player normally runs at 25 fps or 120 fps. macOS's beach ball cursor
/// appears at roughly the <see cref="Freeze"/> threshold; that's the band
/// the player will actually report as "the game stopped".
/// </para>
/// </summary>
public enum StallSeverity : byte
{
    /// <summary>Below 100 ms of total tick period. A perceptible hitch on a high-refresh display, barely noticeable on a slower one.</summary>
    Minor = 0,
    /// <summary>100 to 250 ms. The player saw a frame stutter.</summary>
    Noticeable = 1,
    /// <summary>250 to 500 ms. Disruptive — most players notice and remember.</summary>
    Disruptive = 2,
    /// <summary>500 ms or more. A visible freeze — macOS shows the spinning beach ball at roughly this threshold.</summary>
    Freeze = 3,
}

/// <summary>
/// One detected stall — a wall-clock gap between two consecutive
/// <c>BeginTick</c> calls that exceeded the user's session baseline by the
/// configured multiplier. Captured at detection time and frozen thereafter
/// so a sustained pressure window doesn't keep mutating already-emitted
/// records.
/// </summary>
public struct StallEvent
{
    /// <summary>Tick index of the last successful tick before the stall.</summary>
    public long StartTickIndex;

    /// <summary>Tick index of the first tick after recovery.</summary>
    public long EndTickIndex;

    /// <summary>Unix-ms wall time at <see cref="StartTickIndex"/>; useful for cross-referencing with <c>client.log</c>.</summary>
    public long StartTimestampUnixMs;

    /// <summary>Wall-clock duration between the two ticks' <c>BeginTick</c> stamps.</summary>
    public double TickPeriodMs;

    /// <summary>Baseline tick period at the moment of detection — the value the trigger compared against.</summary>
    public double BaselineMs;

    /// <summary>How much longer than baseline the period was. Equivalent to TickPeriodMs - BaselineMs.</summary>
    public double ExcessOverBaselineMs;

    /// <summary><c>GC.GetTotalPauseDuration</c> delta over the stall window.</summary>
    public double GcPauseDurationMs;

    /// <summary>Process CPU time delta over the stall window. Much less than <see cref="TickPeriodMs"/> means we were suspended.</summary>
    public double ProcessCpuTimeDeltaMs;

    /// <summary>Gen0 collections that fired during the stall.</summary>
    public int Gen0Collections;

    /// <summary>Gen1 collections during the stall.</summary>
    public int Gen1Collections;

    /// <summary>Gen2 collections during the stall — the expensive ones.</summary>
    public int Gen2Collections;

    /// <summary>Managed heap size at the previous tick's start. Pair with <see cref="HeapSizeAfterBytes"/> to see compaction.</summary>
    public long HeapSizeBeforeBytes;

    /// <summary>Managed heap size at the recovery tick's start.</summary>
    public long HeapSizeAfterBytes;

    /// <summary>Cause bucket from <see cref="StallDetector.ClassifyCause"/>.</summary>
    public StallCause Cause;

    /// <summary>Perceptual severity from <see cref="StallDetector.ClassifySeverity"/>.</summary>
    public StallSeverity Severity;

    /// <summary>True if the stall fired in the first ~10 s of the session (JIT warmup territory).</summary>
    public bool Warming;
}

/// <summary>
/// Detects stalls — wall-clock gaps between consecutive <c>BeginTick</c>
/// calls that exceed the user's session baseline tick period by the
/// configured multiplier. The trigger is purely relative so the same
/// detector behaves correctly on a 120 fps player and a 25 fps player; the
/// perceptual severity badge is absolute because that's how the eye works.
///
/// <para>
/// <b>Why this matters separately from <see cref="SpikeDetector"/>.</b> Spike
/// detection times the work inside a tick — <c>BeginTick → EndTick</c>. A
/// stall is the work that DIDN'T happen between two ticks — the gap where
/// the game loop stalled, the CPU got stolen, or the GC took the world.
/// Both feel like lag to the player ("lag spikes" in casual usage) but they
/// have different causes and need different attribution.
/// </para>
///
/// <para>
/// <b>Cause classification.</b> Every stall is tagged with one of:
/// <c>MajorGc</c> (Gen2 collected, GC pause dominates), <c>MinorGc</c>
/// (Gen0/1 + GC pause), <c>ProcessSuspended</c> (wall advanced but CPU
/// didn't — OS background / sleep / app switcher), or <c>LongFrame</c> (wall
/// + CPU both advanced, GC didn't — code stall, likely lock or sync I/O
/// from a draw-thread hook). The classifier is testable and lives as a
/// public static method.
/// </para>
/// </summary>
public sealed class StallDetector
{
    /// <summary>Default relative trigger: stall when tick period ≥ multiplier × baseline median.</summary>
    public const double DefaultThresholdMultiplier = 3.0;

    /// <summary>Number of ticks at session start where stalls are badged "warming" (JIT compile, content load follow-up).</summary>
    public const int WarmupTicks = 600;

    /// <summary>Severity threshold (ms) for <see cref="StallSeverity.Noticeable"/>.</summary>
    public const double SeverityNoticeableMs = 100;

    /// <summary>Severity threshold (ms) for <see cref="StallSeverity.Disruptive"/>.</summary>
    public const double SeverityDisruptiveMs = 250;

    /// <summary>Severity threshold (ms) for <see cref="StallSeverity.Freeze"/>; matches the macOS beach-ball window.</summary>
    public const double SeverityFreezeMs = 500;

    private readonly RingBuffer<StallEvent> _events = new RingBuffer<StallEvent>(50);
    private readonly StallEventsView _view;
    private readonly Process? _self;

    // Per-tick state snapshot. Updated at every BeginTick so the next BeginTick
    // can compute deltas against it if a stall fires.
    private long _prevBeginStamp;
    private double _prevGcPauseMs;
    private int _prevGen0;
    private int _prevGen1;
    private int _prevGen2;
    private long _prevHeapBytes;
    private TimeSpan _prevCpuTime;
    private bool _hasBaselineSample;
    private int _ticksSeen;

    public StallDetector()
    {
        _view = new StallEventsView(_events);
        // Cached at construction so the per-tick path doesn't allocate a new
        // Process object per call. Wrapped in a try because Process is sandbox-
        // restricted in some test/CI environments — null falls back gracefully.
        try { _self = Process.GetCurrentProcess(); }
        catch { _self = null; }
    }

    /// <summary>Relative trigger multiplier. Defaults to <see cref="DefaultThresholdMultiplier"/>.</summary>
    public double ThresholdMultiplier { get; set; } = DefaultThresholdMultiplier;

    /// <summary>Captured stall events in chronological order, oldest first.</summary>
    public IReadOnlyList<StallEvent> Events => _view;

    /// <summary>Number of stalls captured this session (capped at the ring's 50 entries).</summary>
    public int Count => _events.Count;

    /// <summary>
    /// Called by <see cref="MetricCollector.BeginTick"/> after the tick start
    /// timestamp has been captured. Detects a stall by comparing the wall
    /// period since the previous <c>BeginTick</c> against the shared baseline.
    /// </summary>
    /// <param name="beginStamp"><see cref="Stopwatch.GetTimestamp"/> at this tick's start.</param>
    /// <param name="tickIndex">The game's tick index for this tick.</param>
    /// <param name="tickStartUnixMs">Wall-clock time at this tick's start (for log cross-reference).</param>
    /// <param name="baseline">Shared baseline service for the relative threshold.</param>
    public void OnBeginTick(long beginStamp, long tickIndex, long tickStartUnixMs, Baseline baseline)
    {
        _ticksSeen++;

        // First-tick path: no previous sample to compare against. Capture the
        // baseline snapshot and bail.
        if (!_hasBaselineSample || !baseline.IsCalibrated)
        {
            CaptureBaseline(beginStamp);
            _hasBaselineSample = true;
            return;
        }

        long stopwatchFreq = Stopwatch.Frequency;
        double tickPeriodMs = (beginStamp - _prevBeginStamp) * 1000d / stopwatchFreq;
        double baselineMs = baseline.TickPeriodMsMedian;

        if (tickPeriodMs < baselineMs * ThresholdMultiplier)
        {
            // No stall. Slide the baseline sample forward and exit.
            CaptureBaseline(beginStamp);
            return;
        }

        // Stall detected. Read all the diagnostic deltas while they're fresh.
        // GC and heap reads are CLR-internal counters — sub-microsecond. The
        // Process CPU read does an OS stat; bounded to the moment-of-stall
        // path so we don't pay it every tick.
        double gcPauseNow = SafeGcPauseMs();
        int gen0Now = GC.CollectionCount(0);
        int gen1Now = GC.CollectionCount(1);
        int gen2Now = GC.CollectionCount(2);
        long heapNow = GC.GetTotalMemory(forceFullCollection: false);

        TimeSpan cpuNow = _prevCpuTime;
        try { _self?.Refresh(); if (_self != null) cpuNow = _self.TotalProcessorTime; }
        catch { /* keep previous reading */ }

        double gcDelta = gcPauseNow - _prevGcPauseMs; if (gcDelta < 0d) gcDelta = 0d;
        int g0 = gen0Now - _prevGen0;
        int g1 = gen1Now - _prevGen1;
        int g2 = gen2Now - _prevGen2;
        double cpuDelta = (cpuNow - _prevCpuTime).TotalMilliseconds;
        if (cpuDelta < 0d) cpuDelta = 0d;

        double excess = tickPeriodMs - baselineMs;

        StallEvent ev = new StallEvent
        {
            StartTickIndex = tickIndex - 1,
            EndTickIndex = tickIndex,
            StartTimestampUnixMs = tickStartUnixMs,
            TickPeriodMs = tickPeriodMs,
            BaselineMs = baselineMs,
            ExcessOverBaselineMs = excess,
            GcPauseDurationMs = gcDelta,
            ProcessCpuTimeDeltaMs = cpuDelta,
            Gen0Collections = g0,
            Gen1Collections = g1,
            Gen2Collections = g2,
            HeapSizeBeforeBytes = _prevHeapBytes,
            HeapSizeAfterBytes = heapNow,
            Cause = ClassifyCause(tickPeriodMs, gcDelta, g2, cpuDelta),
            Severity = ClassifySeverity(tickPeriodMs),
            Warming = _ticksSeen <= WarmupTicks,
        };
        _events.Push(in ev);

        CaptureBaseline(beginStamp);
    }

    /// <summary>
    /// Maps the four signals (wall delta, GC pause delta, Gen2 count delta,
    /// CPU delta) onto a <see cref="StallCause"/>. Pure function; tested in
    /// isolation against a truth table.
    /// </summary>
    public static StallCause ClassifyCause(double wallMs, double gcMs, int gen2Delta, double cpuMs)
    {
        // Defensive: a zero-wall stall is degenerate; report unknown.
        if (wallMs <= 0d) return StallCause.Unknown;

        // CPU much less than wall → the OS wasn't running us. App switcher,
        // background, sleep, or another process eating all the cores.
        if (cpuMs < wallMs * 0.2d) return StallCause.ProcessSuspended;

        // GC pause is at least half the stall — the heap was the bottleneck.
        // Gen2 deciding factor between Major/Minor; Gen2 is the slow one.
        if (gcMs > wallMs * 0.5d)
            return gen2Delta > 0 ? StallCause.MajorGc : StallCause.MinorGc;

        // Wall and CPU both advanced, GC didn't. Some code path took the time
        // — most often a draw-thread hook doing sync I/O or hitting a lock.
        return StallCause.LongFrame;
    }

    /// <summary>
    /// Maps total tick period to a perceptual severity bucket. Absolute
    /// thresholds because the player's eye doesn't know about FPS.
    /// </summary>
    public static StallSeverity ClassifySeverity(double tickPeriodMs)
    {
        if (tickPeriodMs >= SeverityFreezeMs) return StallSeverity.Freeze;
        if (tickPeriodMs >= SeverityDisruptiveMs) return StallSeverity.Disruptive;
        if (tickPeriodMs >= SeverityNoticeableMs) return StallSeverity.Noticeable;
        return StallSeverity.Minor;
    }

    /// <summary>Resets the detector. Called from <see cref="MetricCollector"/> at world unload via spike-flush analogue.</summary>
    public void Reset()
    {
        _events.Clear();
        _prevBeginStamp = 0;
        _prevGcPauseMs = 0;
        _prevGen0 = 0;
        _prevGen1 = 0;
        _prevGen2 = 0;
        _prevHeapBytes = 0;
        _prevCpuTime = TimeSpan.Zero;
        _hasBaselineSample = false;
        _ticksSeen = 0;
    }

    private void CaptureBaseline(long beginStamp)
    {
        _prevBeginStamp = beginStamp;
        _prevGcPauseMs = SafeGcPauseMs();
        _prevGen0 = GC.CollectionCount(0);
        _prevGen1 = GC.CollectionCount(1);
        _prevGen2 = GC.CollectionCount(2);
        _prevHeapBytes = GC.GetTotalMemory(forceFullCollection: false);
        try { _self?.Refresh(); if (_self != null) _prevCpuTime = _self.TotalProcessorTime; }
        catch { /* keep previous; the stall path will produce a 0 cpuDelta which classifies as ProcessSuspended */ }
    }

    private static double SafeGcPauseMs()
    {
        try { return GC.GetTotalPauseDuration().TotalMilliseconds; }
        catch { return 0d; } // GetTotalPauseDuration is .NET 7+; defensive for older runtimes
    }

    /// <summary>
    /// Allocation-free <see cref="IReadOnlyList{T}"/> wrapper over the ring,
    /// matching the pattern from <see cref="SpikeDetector"/>.
    /// </summary>
    private sealed class StallEventsView : IReadOnlyList<StallEvent>
    {
        private readonly RingBuffer<StallEvent> _source;
        public StallEventsView(RingBuffer<StallEvent> source) => _source = source;
        public int Count => _source.Count;
        public StallEvent this[int index] => _source[index];

        public IEnumerator<StallEvent> GetEnumerator()
        {
            int n = _source.Count;
            for (int i = 0; i < n; i++) yield return _source[i];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
