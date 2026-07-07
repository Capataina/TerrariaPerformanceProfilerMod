#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Data.Detectors;

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
    /// <summary>Wall clock advanced but the process CPU didn't, and this was an isolated event — the OS suspended us (background, app switcher, sleep).</summary>
    ProcessSuspended = 3,
    /// <summary>Wall and CPU both advanced, GC didn't — code stall. Lock contention, sync I/O, JIT compile, draw-thread hook blocking.</summary>
    LongFrame = 4,
    /// <summary>Sustained cluster of medium-duration stalls (5+ within ~5 seconds, each 80–500 ms). Signature of an open mod UI that blocks the main thread on vsync / heavy per-frame draw, even though per-frame CPU may read low because the swap is the wait.</summary>
    UiOverlayBlocking = 5,
    /// <summary>Stall fired during a known world-load tick window (entering or leaving a world). JIT + asset bind dominate; not a real gameplay stall.</summary>
    WorldLoad = 6,
    /// <summary>Wall advanced, CPU did not, but the game retained input focus throughout. The OS wasn't suspending us — something inside the main thread blocked (lock, sync I/O, render-thread wait). Distinct from <see cref="ProcessSuspended"/>, which requires focus loss.</summary>
    MainThreadFreeze = 7,
}

/// <summary>
/// Severity ladder for stall badging, expressed as multiples of the
/// player's session baseline tick period. Used by the overlay and the
/// session report to communicate "how bad relative to *this player's*
/// normal frame" the stall was.
///
/// <para>
/// Absolute ms thresholds are wrong here: a 100 ms hitch on a 120 fps
/// player (12× their normal 8.3 ms frame) is appalling; the same 100 ms
/// hitch on a 30 fps modded player (3× their normal 33 ms frame) is
/// barely noticeable. The same goes for every other detector / log
/// threshold in this codebase — see <see cref="StallDetector.SeverityFor"/>
/// for the band mapping and <c>context/notes/decisions.md</c> for the
/// "everything relative" decision.
/// </para>
/// </summary>
public enum StallSeverity : byte
{
    /// <summary>Below ~3× baseline. A perceptible hitch on a high-refresh display, lost in noise on a heavy modlist.</summary>
    Minor = 0,
    /// <summary>~3–8× baseline. The player saw a frame stutter relative to their own normal.</summary>
    Noticeable = 1,
    /// <summary>~8–20× baseline. Disruptive — the player will remember this freeze.</summary>
    Disruptive = 2,
    /// <summary>≥ 20× baseline. A visible freeze the player will report as "the game stopped".</summary>
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

    /// <summary>
    /// Top contributors at the moment the stall fired — top 5 mods ranked by
    /// recent (rolling 30s average) ms cost. Captured at stall detection time
    /// so the row remains queryable even after the smoothed values move on.
    /// Fixed-length 5-slot array; unused slots have ModId = -1.
    /// </summary>
    public StallContributor C0, C1, C2, C3, C4;
}

/// <summary>One row of stall attribution: which mod, how much it was costing at stall time.</summary>
public struct StallContributor
{
    public int ModId;
    public double RecentMs;

    public static StallContributor Empty => new StallContributor { ModId = -1, RecentMs = 0d };
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

    // ---- Severity multipliers ------------------------------------------------
    // All severity thresholds are multiples of the player's session baseline
    // tick period. Bands chosen so that, at a typical 60 fps baseline
    // (~16.7 ms), they map to roughly the old absolute thresholds (50 / 130 /
    // 330 ms) — close to "what the eye perceives at 60fps" — but on a 30 fps
    // modded player they correctly scale up. See enum doc + decisions.md.

    /// <summary>Multiplier on baseline tick period at which a stall becomes <see cref="StallSeverity.Noticeable"/>.</summary>
    public const double SeverityNoticeableMultiplier = 3.0;

    /// <summary>Multiplier on baseline tick period at which a stall becomes <see cref="StallSeverity.Disruptive"/>.</summary>
    public const double SeverityDisruptiveMultiplier = 8.0;

    /// <summary>Multiplier on baseline tick period at which a stall becomes <see cref="StallSeverity.Freeze"/>.</summary>
    public const double SeverityFreezeMultiplier = 20.0;

    // ---- Classifier multipliers ---------------------------------------------
    // Boundaries between cause buckets, all relative to baseline.

    /// <summary>Cluster check rejects stalls longer than this × baseline — beyond this they're real freezes, not menu blocking.</summary>
    public const double UiOverlayClusterMaxMultiplier = 30.0;

    /// <summary>Lone CPU-starved stalls longer than this × baseline classify as unambiguous OS suspends.</summary>
    public const double LongSuspendMultiplier = 50.0;

    /// <summary>
    /// Absolute wall-clock ceiling, in ms, above which a gap is an OS suspend /
    /// pause regardless of focus. Unlike the baseline-relative multipliers above
    /// (which model perceptual severity), this one is genuinely absolute: no real
    /// in-app main-thread freeze runs for multiple seconds — the OS would mark the
    /// process "not responding". A multi-second-to-minutes gap is the game paused
    /// (window unfocused, so Terraria stops ticking), the OS sleeping, or a
    /// debugger break. Because the game stops ticking while unfocused, the
    /// focus-loss happens between ticks and the focus-sampled signal can't observe
    /// it, so without this ceiling such gaps misclassify as MainThreadFreeze and
    /// pollute the lag metrics with minute-long "freezes".
    /// </summary>
    public const double SuspendCeilingMs = 5000.0;

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

    /// <summary>
    /// True if every tick since the previous BeginTick had focus. Reset to
    /// the current-tick focus state after each successful tick. A stall
    /// gap that includes any focus-lost frame flips this false → indicates
    /// the OS gave focus to another app during the gap, which is the
    /// real ProcessSuspended signature.
    /// </summary>
    private bool _focusHeldAcrossGap = true;

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
        => OnBeginTick(beginStamp, tickIndex, tickStartUnixMs, baseline, null, hadFocusThisTick: true);

    public void OnBeginTick(long beginStamp, long tickIndex, long tickStartUnixMs, Baseline baseline,
        IReadOnlyList<double>? perModSmoothedMs)
        => OnBeginTick(beginStamp, tickIndex, tickStartUnixMs, baseline, perModSmoothedMs, hadFocusThisTick: true);

    /// <summary>
    /// Full overload. Captures per-mod attribution at stall time AND the
    /// focus-retention signal so the classifier can distinguish a real OS
    /// suspend (focus lost during the gap) from a main-thread freeze
    /// (focus retained, game just didn't progress).
    /// </summary>
    public void OnBeginTick(long beginStamp, long tickIndex, long tickStartUnixMs, Baseline baseline,
        IReadOnlyList<double>? perModSmoothedMs, bool hadFocusThisTick)
    {
        _ticksSeen++;
        // Track focus-retention across the current gap. If any tick in the
        // gap reported focus lost, the flag stays false until we reset it
        // on a successful (non-stall) tick.
        if (!hadFocusThisTick) _focusHeldAcrossGap = false;

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
            // No stall. Slide the baseline sample forward and reset the
            // gap-focus tracker so the next stall starts with a clean
            // "focus held throughout" assumption.
            CaptureBaseline(beginStamp);
            _focusHeldAcrossGap = hadFocusThisTick;
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

        // Cluster-shape signal: how many stalls have fired in the recent
        // window. A run of medium-duration gaps is the UI-blocking
        // signature; a lone big gap is real OS suspension.
        int recentInLast5s = CountRecentStallsInWindow(tickStartUnixMs, windowMs: 5000);

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
            Cause = ClassifyCause(tickPeriodMs, gcDelta, g2, cpuDelta, recentInLast5s, baselineMs, _focusHeldAcrossGap),
            Severity = ClassifySeverity(tickPeriodMs, baselineMs),
            Warming = _ticksSeen <= WarmupTicks,
        };
        // A ProcessSuspended (alt-tab / OS-sleep) or WorldLoad gap is wall time
        // in which NO mod ran — the process was paused or the world was
        // (un)loading. Crediting the pre-gap smoothed cost to it is meaningless,
        // and it is exactly what made a 114 s alt-tab log as
        // "top=PerformanceProfiler (2.38ms recent)". Only real in-app stalls
        // (freezes, GC, UI-blocking, long frames) carry per-mod contributors;
        // suspends and loads are left unattributed.
        if (ev.Cause != StallCause.ProcessSuspended && ev.Cause != StallCause.WorldLoad)
        {
            CaptureTopContributors(perModSmoothedMs, ref ev);
        }
        else
        {
            ev.C0 = ev.C1 = ev.C2 = ev.C3 = ev.C4 = StallContributor.Empty;
        }
        _events.Push(in ev);

        CaptureBaseline(beginStamp);
        // Reset gap-focus tracker for the next stall's accumulation window.
        _focusHeldAcrossGap = hadFocusThisTick;
    }

    /// <summary>
    /// Walk the rolling event ring and count how many stalls landed within
    /// <paramref name="windowMs"/> of <paramref name="nowUnixMs"/>. Used by
    /// the classifier to detect clustered UI-blocking patterns.
    /// </summary>
    private int CountRecentStallsInWindow(long nowUnixMs, int windowMs)
    {
        int count = 0;
        for (int i = 0; i < _events.Count; i++)
        {
            if (nowUnixMs - _events[i].StartTimestampUnixMs <= windowMs) count++;
        }
        return count;
    }

    /// <summary>
    /// Read <paramref name="perModSmoothedMs"/> (per-mod-per-category) and pick
    /// the top-5 mods by summed cost across categories. Sets ev.C0–C4 with
    /// (ModId, RecentMs). Unused slots get ModId = -1. Allocation-free.
    /// </summary>
    private static void CaptureTopContributors(IReadOnlyList<double>? perModSmoothedMs, ref StallEvent ev)
    {
        ev.C0 = ev.C1 = ev.C2 = ev.C3 = ev.C4 = StallContributor.Empty;
        if (perModSmoothedMs == null || perModSmoothedMs.Count == 0) return;

        int cats = PerModAttribution.CategoryCount;
        if (cats <= 0) return;
        int modCount = perModSmoothedMs.Count / cats;

        // 5-way top-N selection with branch-free insertion. No alloc.
        int b0 = -1, b1 = -1, b2 = -1, b3 = -1, b4 = -1;
        double v0 = 0, v1 = 0, v2 = 0, v3 = 0, v4 = 0;

        for (int modId = 0; modId < modCount; modId++)
        {
            double sum = 0d;
            int offset = modId * cats;
            for (int c = 0; c < cats; c++) sum += perModSmoothedMs[offset + c];
            if (sum <= 0d) continue;

            if (sum > v0)       { v4=v3; b4=b3; v3=v2; b3=b2; v2=v1; b2=b1; v1=v0; b1=b0; v0=sum; b0=modId; }
            else if (sum > v1)  { v4=v3; b4=b3; v3=v2; b3=b2; v2=v1; b2=b1; v1=sum; b1=modId; }
            else if (sum > v2)  { v4=v3; b4=b3; v3=v2; b3=b2; v2=sum; b2=modId; }
            else if (sum > v3)  { v4=v3; b4=b3; v3=sum; b3=modId; }
            else if (sum > v4)  { v4=sum; b4=modId; }
        }

        ev.C0 = new StallContributor { ModId = b0, RecentMs = v0 };
        ev.C1 = new StallContributor { ModId = b1, RecentMs = v1 };
        ev.C2 = new StallContributor { ModId = b2, RecentMs = v2 };
        ev.C3 = new StallContributor { ModId = b3, RecentMs = v3 };
        ev.C4 = new StallContributor { ModId = b4, RecentMs = v4 };
    }

    /// <summary>
    /// Back-compat overload — no cluster context. Treated as a lone stall.
    /// </summary>
    public static StallCause ClassifyCause(double wallMs, double gcMs, int gen2Delta, double cpuMs)
        => ClassifyCause(wallMs, gcMs, gen2Delta, cpuMs, recentStallsInLast5s: 0);

    /// <summary>
    /// Maps the five signals (wall delta, GC pause delta, Gen2 count delta,
    /// CPU delta, recent-stall-count) onto a <see cref="StallCause"/>. Pure
    /// function; tested in isolation against a truth table.
    ///
    /// <para>
    /// The cluster-shape signal (<paramref name="recentStallsInLast5s"/>) is
    /// what distinguishes a true OS suspend from a UI-overlay menu blocking
    /// the main thread on vsync. Both look identical at the per-event level —
    /// wall advanced, CPU didn't (because the swap is the wait), GC didn't —
    /// but the cluster shape is the tell. A real cmd-tab is lone and long; a
    /// CheatSheet NPC-spawn menu produces a run of medium gaps.
    /// </para>
    /// </summary>
    public static StallCause ClassifyCause(double wallMs, double gcMs, int gen2Delta, double cpuMs,
        int recentStallsInLast5s)
        => ClassifyCause(wallMs, gcMs, gen2Delta, cpuMs, recentStallsInLast5s, baselineMs: 16.67d, focusHeldAcrossGap: true);

    public static StallCause ClassifyCause(double wallMs, double gcMs, int gen2Delta, double cpuMs,
        int recentStallsInLast5s, double baselineMs)
        => ClassifyCause(wallMs, gcMs, gen2Delta, cpuMs, recentStallsInLast5s, baselineMs, focusHeldAcrossGap: true);

    /// <summary>
    /// Cluster-shape + baseline-aware + focus-aware classifier. All
    /// thresholds are multiples of <paramref name="baselineMs"/> so a
    /// 30 fps player and a 120 fps player both get correct buckets
    /// without retuning constants.
    ///
    /// <para>
    /// <paramref name="focusHeldAcrossGap"/> is true if Main.hasFocus was
    /// true for every tick in the stall window. A long CPU-starved stall
    /// with focus held is a <see cref="StallCause.MainThreadFreeze"/>
    /// (something inside the game blocked the main thread); the same
    /// signature with focus lost is a real <see cref="StallCause.ProcessSuspended"/>
    /// (OS gave focus to another app). This is what disambiguates a
    /// frozen game from a real alt-tab.
    /// </para>
    /// </summary>
    public static StallCause ClassifyCause(double wallMs, double gcMs, int gen2Delta, double cpuMs,
        int recentStallsInLast5s, double baselineMs, bool focusHeldAcrossGap)
    {
        // Defensive: a zero-wall stall is degenerate; report unknown.
        if (wallMs <= 0d) return StallCause.Unknown;
        if (baselineMs <= 0d) baselineMs = 16.67d;  // safety fallback to 60 fps

        // Absolute pause/suspend ceiling. Caught before the focus split because
        // an unfocus while the game isn't ticking is invisible to the focus
        // sample, so a paused gap would otherwise read as a held-focus
        // MainThreadFreeze. A multi-second gap is never a real in-app freeze.
        if (wallMs >= SuspendCeilingMs) return StallCause.ProcessSuspended;

        bool cpuStarved = cpuMs < wallMs * 0.2d;
        bool isLone = recentStallsInLast5s <= 2;

        // First priority: a long, lone, CPU-starved gap.
        //
        // Two sub-cases, distinguished by whether the game kept input focus:
        //   focus lost during the gap → ProcessSuspended (real OS suspend —
        //     the player switched apps, the OS slept, etc).
        //   focus retained throughout → MainThreadFreeze (the game thread
        //     blocked on a lock / sync wait / render-thread stall, the OS
        //     wasn't suspending us, but the game wasn't advancing either).
        //
        // The v0.4 playtest mis-narrated this: 1864ms stall, focus held the
        // whole time (player wasn't alt-tabbed), classified as
        // ProcessSuspended. v0.5 splits them.
        if (cpuStarved && isLone && wallMs >= baselineMs * LongSuspendMultiplier)
            return focusHeldAcrossGap ? StallCause.MainThreadFreeze : StallCause.ProcessSuspended;

        // GC pause is at least half the stall — the heap was the bottleneck.
        // Gen2 deciding factor between Major/Minor; Gen2 is the slow one.
        if (gcMs > wallMs * 0.5d)
            return gen2Delta > 0 ? StallCause.MajorGc : StallCause.MinorGc;

        // Sustained cluster of medium-duration stalls → UI/main-thread
        // blocking. "Medium" relative to the player's own baseline, not an
        // absolute ms ceiling. CheatSheet NPC-spawn menu playtest: 47
        // consecutive 6-13× baseline gaps over 13 seconds.
        if (recentStallsInLast5s >= 5 && wallMs < baselineMs * UiOverlayClusterMaxMultiplier)
            return StallCause.UiOverlayBlocking;

        // Shorter lone CPU-starved events: same focus split.
        if (cpuStarved && isLone)
            return focusHeldAcrossGap ? StallCause.MainThreadFreeze : StallCause.ProcessSuspended;

        // Wall and CPU both advanced, GC didn't. Some code path took the
        // time — most often a draw-thread hook doing sync I/O or hitting
        // a lock.
        return StallCause.LongFrame;
    }

    /// <summary>
    /// Maps tick period to a severity bucket as a multiple of the player's
    /// session baseline. A 100 ms hitch on a 120 fps player (12× baseline)
    /// is disruptive; the same 100 ms hitch on a 30 fps modded player
    /// (3× baseline) is barely noticeable. Always pass the live baseline.
    /// </summary>
    public static StallSeverity ClassifySeverity(double tickPeriodMs, double baselineMs)
    {
        if (baselineMs <= 0d) baselineMs = 16.67d;
        double mult = tickPeriodMs / baselineMs;
        if (mult >= SeverityFreezeMultiplier) return StallSeverity.Freeze;
        if (mult >= SeverityDisruptiveMultiplier) return StallSeverity.Disruptive;
        if (mult >= SeverityNoticeableMultiplier) return StallSeverity.Noticeable;
        return StallSeverity.Minor;
    }

    /// <summary>Back-compat overload: assumes 60 fps baseline. Prefer the two-arg variant.</summary>
    public static StallSeverity ClassifySeverity(double tickPeriodMs)
        => ClassifySeverity(tickPeriodMs, baselineMs: 16.67d);

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
