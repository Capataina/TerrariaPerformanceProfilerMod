#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Profiling;

/// <summary>
/// Times each game tick, stores a rolling history of <see cref="TickFrame"/>s,
/// and harvests the per-mod, per-category CPU attribution accumulated by the
/// timing detours.
///
/// Pure logic with no tModLoader dependency: every game-sourced value (the tick
/// index, the entity counts) is passed in by the caller, so the collector is
/// unit-testable without a running game (CLAUDE.md testability standard). The
/// glue that reads those values from the game lives in the ModSystem that
/// drives this class.
///
/// Usage is a strict pair per tick: <see cref="BeginTick"/> at the start of a
/// tick, <see cref="EndTick"/> at the end. <see cref="EndTick"/> with no open
/// tick is ignored, so a partial-update frame (where the start hook never
/// fired) records nothing rather than a bogus 0 ms tick.
/// </summary>
public sealed class MetricCollector
{
    // How fast the smoothed per-mod costs track the raw per-tick numbers. At
    // 60 ticks/s, 0.06 settles in roughly a second -- enough to kill per-tick
    // jitter without feeling laggy.
    private const double PerModSmoothing = 0.06d;

    // Slow EMA for the per-hook ~30 s average. alpha = 2/(N+1) for an N = 1800-tick
    // (~30 s @ 60 Hz) horizon ≈ 0.00111, so it tracks the same window the old
    // per-hook windowed mean did, without the 1800-sample-per-hook history ring
    // that made that mean cost ~896 MB at 62k hooks (see the _perHookAverageMs
    // field note). Only the per-HOOK average moved to an EMA; the per-mod average
    // stays an exact windowed mean (its history ring is small: modCount*cats*1800).
    private const double PerHookAverageSmoothing = 2d / (1800d + 1d);

    // Below this, an EMA cell is flushed to exactly 0. An exponential average of a
    // cost that goes idle never reaches 0 — it asymptotes through ever-smaller
    // positives and bottoms out as a subnormal double like 2.3e-284, which (a)
    // serialised into the WarmAggregate as visual garbage (the C2 bug) and (b)
    // triggers the CPU's slow subnormal path on x86 every tick for every idle
    // cell. 1e-12 ms (a femtosecond) is far below any real per-tick cost, so
    // snapping cells beneath it to 0 is lossless for the observable and keeps the
    // arrays free of subnormals. All EMA values here are >= 0, so a one-sided
    // compare is sufficient.
    private const double DenormalFloor = 1e-12;

    // Stopwatch-ticks → milliseconds. Stopwatch.Frequency is fixed at process
    // boot on every platform tModLoader runs on, so this is a process-lifetime
    // constant; caching the reciprocal trades a per-tick division for a multiply
    // (the same lever Time.cs:46 already pulls).
    private static readonly double TicksToMs = 1000d / Stopwatch.Frequency;

    private readonly RingBuffer<TickFrame> _history;

    // Per-mod, per-category CPU, [modId * CategoryCount + categoryId]. _raw is
    // this tick's harvest; _smoothed is what the UI displays.
    private readonly double[] _perModRawMs;
    private readonly double[] _perModSmoothedMs;
    private readonly double[] _perModAverageMs;
    private readonly double[] _perModHistoryMs;
    private readonly double[] _perModRollingMs;
    private readonly double[] _perHookRawMs;
    private readonly double[] _perHookSmoothedMs;
    // Per-hook rolling average via a slow EMA, NOT a windowed mean. The windowed
    // mean needed a _perHookHistoryMs ring of HookCount * historyCapacity doubles
    // = 62,203 * 1800 * 8 B ≈ 896 MB allocated at world-entry (plus another
    // ~896 MB for the bytes twin) — the design silently assumed the ~10k-hook
    // bench and exploded on a 62k-hook stack, a prime "ran out of RAM" cause.
    // A ~30 s-time-constant EMA reproduces the same observable (a stable per-hook
    // average that ignores per-tick jitter) at O(HookCount) memory instead of
    // O(HookCount * 1800), and folds in the same pass as the fast smoothing so it
    // also removes two full 62k-element array walks per tick (a B3 win too).
    private readonly double[] _perHookAverageMs;

    // Parallel byte arrays for allocation tracking. Allocated only when
    // HookBackend.AllocationTracking is true at construction; null otherwise
    // so the collector's memory footprint is identical to today in the Lite
    // configuration. Bytes are raw -- callers format B/KB/MB at the UI layer.
    private readonly double[]? _perModRawBytes;
    private readonly double[]? _perModSmoothedBytes;
    private readonly double[]? _perModAverageBytes;
    private readonly double[]? _perModHistoryBytes;
    private readonly double[]? _perModRollingBytes;
    private readonly double[]? _perHookRawBytes;
    private readonly double[]? _perHookSmoothedBytes;
    // Per-hook allocation average: the EMA twin of _perHookAverageMs (see there).
    // Removing its history/rolling rings drops the SECOND ~896 MB world-entry
    // allocation of the pair.
    private readonly double[]? _perHookAverageBytes;
    private readonly bool _tracksAllocations;
    private readonly int _historyCapacity;
    private int _sampleSlot;

    // Stopwatch timestamp captured at BeginTick; -1 means "no tick currently open".
    private long _tickStartTimestamp = -1L;

    // Stopwatch timestamp captured at the PREVIOUS BeginTick; -1 until the
    // second tick of a session. The delta from it to the current BeginTick is
    // the real inter-frame period (Update + Draw + any vsync sleep) — the honest
    // frame time the player experiences, which FrameTimeMs (update-window only)
    // is structurally blind to.
    private long _prevBeginTimestamp = -1L;

    // Real inter-frame period (ms) for the frame currently being closed. Set in
    // BeginTick, read in EndTick. See TickFrame.RealFrameTimeMs.
    private double _realFramePeriodMs;

    // Cumulative GC pause time (ms) read at BeginTick, so EndTick can report the
    // pause time that accrued during this tick alone.
    private double _gcPauseMsAtTickStart;

    // ---- Backend benchmarking (active in HookBackendMode.Parallel) ----------
    // In Parallel mode both backends write to PerModAttribution -- delegate to
    // slot 0, ILHook to slot 1. To benchmark them we keep a second set of raw
    // arrays for backend 1 and a smoothed scalar total per backend.
    private readonly double[]? _perModRawMsBackend1;
    private double _backendTotalSmoothedMs0;
    private double _backendTotalSmoothedMs1;
    private int _divergenceLogCountdown;
    private const int DivergenceLogIntervalTicks = 60 * 10; // every ~10s while in a world

    // ---- Raw per-tick history + spike detection -----------------------------
    // The smoothed averages above are what the live tree displays. The ring
    // below keeps the unsmoothed per-mod-per-category values for the last 30s
    // so a spike drill-down can read "what did Mod X look like at THAT tick"
    // straight from the moment of the spike, before smoothing carried it away.
    // The detector consumes this ring at every tick and emits SpikeWindow
    // records when the frame time crosses the (median × N) threshold.
    private readonly PerTickAttributionRing _perTickRing;
    private readonly SpikeDetector _spikeDetector;
    private readonly StallDetector _stallDetector = new StallDetector();
    private readonly Baseline _baseline = new Baseline();
    // Self-health is owned by ProfilerSystem (a process singleton) because
    // install-time measurement happens before any world exists. The collector
    // just drives its refresh cadence.
    private readonly ProfilerSelfHealth _selfHealth;
    private const int CategorySnapshotTicks = 120; // 2 s @ 60 tps

    /// <summary>
    /// Creates a collector whose history holds <paramref name="historyCapacity"/>
    /// ticks. Uses a freshly-allocated <see cref="ProfilerSelfHealth"/>; the
    /// <see cref="ProfilerSystem"/>-owned singleton is the production path
    /// — this overload is for tests.
    /// </summary>
    public MetricCollector(int historyCapacity)
        : this(historyCapacity, new ProfilerSelfHealth()) { }

    /// <summary>Creates a collector that drives an existing self-health instance.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="historyCapacity"/> is not positive.</exception>
    public MetricCollector(int historyCapacity, ProfilerSelfHealth selfHealth)
    {
        _selfHealth = selfHealth;
        _history = new RingBuffer<TickFrame>(historyCapacity);
        _historyCapacity = historyCapacity;
        int cells = PerModAttribution.ModCount * PerModAttribution.CategoryCount;
        _perModRawMs = new double[cells];
        _perModSmoothedMs = new double[cells];
        _perModAverageMs = new double[cells];
        _perModHistoryMs = new double[cells * historyCapacity];
        _perModRollingMs = new double[cells];
        _perHookRawMs = new double[PerModAttribution.HookCount];
        _perHookSmoothedMs = new double[PerModAttribution.HookCount];
        _perHookAverageMs = new double[PerModAttribution.HookCount];

        // Only allocate the second-backend buffer if we're actually running both.
        if (PerModAttribution.BackendCount > 1)
        {
            _perModRawMsBackend1 = new double[cells];
        }

        // Allocation tracking arrays mirror the CPU shapes. Sized only when
        // HookBackend.AllocationTracking is on, so the Lite configuration pays
        // zero extra memory.
        _tracksAllocations = PerModAttribution.TracksAllocations;
        if (_tracksAllocations)
        {
            _perModRawBytes = new double[cells];
            _perModSmoothedBytes = new double[cells];
            _perModAverageBytes = new double[cells];
            _perModHistoryBytes = new double[cells * historyCapacity];
            _perModRollingBytes = new double[cells];
            _perHookRawBytes = new double[PerModAttribution.HookCount];
            _perHookSmoothedBytes = new double[PerModAttribution.HookCount];
            _perHookAverageBytes = new double[PerModAttribution.HookCount];
        }

        // Raw history ring + detector. Allocation columns now light up when
        // tracking is on so the SPIKES drill-down inherits the bytes data
        // alongside the ms data.
        _perTickRing = new PerTickAttributionRing(
            modCount: PerModAttribution.ModCount,
            historyTicks: historyCapacity,
            categorySnapshotTicks: CategorySnapshotTicks,
            trackAllocations: _tracksAllocations);
        _spikeDetector = new SpikeDetector(
            modCount: PerModAttribution.ModCount,
            tracksAllocations: _tracksAllocations);
    }

    /// <summary>
    /// Smoothed total CPU ms (sum across mods/categories) for the primary
    /// backend's current tick. In Delegate-only or ILHook-only mode this is the
    /// only number; in Parallel mode it's the delegate path's number.
    /// </summary>
    public double BackendTotalMs0 => _backendTotalSmoothedMs0;

    /// <summary>
    /// Smoothed total CPU ms for the secondary backend (ILHook in Parallel
    /// mode). Zero in single-backend modes.
    /// </summary>
    public double BackendTotalMs1 => _backendTotalSmoothedMs1;

    /// <summary>
    /// Divergence ratio between the two backends in Parallel mode:
    /// (ILHook - Delegate) / Delegate. Positive means ILHook reports higher
    /// (typically expected because the delegate path's wrapper adds a tiny
    /// per-call frame, but should be small). 0 in single-backend modes.
    /// </summary>
    public double BackendDivergence
    {
        get
        {
            if (_perModRawMsBackend1 == null) return 0d;
            double baseline = _backendTotalSmoothedMs0;
            if (baseline < 1e-6) return 0d;
            return (_backendTotalSmoothedMs1 - baseline) / baseline;
        }
    }

    /// <summary>The rolling per-tick history, oldest record first. The UI reads this to draw.</summary>
    public RingBuffer<TickFrame> History => _history;

    /// <summary>
    /// Smoothed per-mod, per-category CPU in milliseconds, indexed
    /// [modId * <see cref="PerModAttribution.CategoryCount"/> + categoryId].
    /// This is the stable value the per-mod tree displays; a mod's total is the
    /// sum of its category cells.
    /// </summary>
    public IReadOnlyList<double> PerModCategoryMs => _perModSmoothedMs;

    /// <summary>
    /// Unsmoothed per-mod/category ms for the most-recently-closed tick. Same
    /// <c>[modId * CategoryCount + categoryId]</c> layout. Read-only view onto
    /// the live buffer — never store the reference past the current frame; the
    /// next <c>EndTick</c> overwrites the contents.
    /// </summary>
    public IReadOnlyList<double> PerModCategoryRawMs => _perModRawMs;

    /// <summary>
    /// Same buffer as <see cref="PerModCategoryRawMs"/> exposed as the concrete
    /// <c>double[]</c> for the per-tick fold consumers (<c>SegmentDetector.OnTick</c>,
    /// <c>PerModCostTimeSeriesAggregator.OnTick</c>). Indexing a <c>double[]</c>
    /// is non-virtual and bounds-check-elidable in a counted loop, whereas the
    /// <see cref="IReadOnlyList{T}"/> view forces interface dispatch on every
    /// element — the cost the hottest folds in the pipeline pay otherwise.
    /// Internal so the contract that "the buffer is overwritten each
    /// <c>EndTick</c>" is enforced by the public read-only view; same-assembly
    /// callers that take the array must not store the reference past the frame.
    /// </summary>
    internal double[] PerModCategoryRawMsArray => _perModRawMs;

    /// <summary>
    /// Rolling 30-second per-mod/category average in milliseconds. This is the
    /// stable view for inspecting lag-spike contribution without row churn.
    /// </summary>
    public IReadOnlyList<double> PerModCategoryAverageMs => _perModAverageMs;

    /// <summary>
    /// Smoothed per-hook CPU in milliseconds, indexed by hookId. Hook metadata is
    /// available through <see cref="PerModAttribution.Hooks"/>.
    /// </summary>
    public IReadOnlyList<double> PerHookMs => _perHookSmoothedMs;

    /// <summary>Per-hook ~30-second average in milliseconds (EMA-smoothed, not a
    /// windowed mean — see <c>PerHookAverageSmoothing</c>), indexed by hookId.</summary>
    public IReadOnlyList<double> PerHookAverageMs => _perHookAverageMs;

    /// <summary>
    /// Smoothed per-mod, per-category allocation bytes per tick. Null if
    /// allocation tracking is off. Same indexing as <see cref="PerModCategoryMs"/>.
    /// </summary>
    public IReadOnlyList<double>? PerModCategoryBytes => _perModSmoothedBytes;

    /// <summary>Rolling 30-second per-mod/category allocation average, in bytes. Null if tracking is off.</summary>
    public IReadOnlyList<double>? PerModCategoryAverageBytes => _perModAverageBytes;

    /// <summary>Smoothed per-hook allocation bytes per tick. Null if tracking is off.</summary>
    public IReadOnlyList<double>? PerHookBytes => _perHookSmoothedBytes;

    /// <summary>Per-hook ~30-second allocation average, in bytes (EMA-smoothed, not
    /// a windowed mean). Null if tracking is off.</summary>
    public IReadOnlyList<double>? PerHookAverageBytes => _perHookAverageBytes;

    /// <summary>True when this collector retains allocation columns alongside CPU.</summary>
    public bool TracksAllocations => _tracksAllocations;

    /// <summary>
    /// Raw per-tick per-mod attribution for the last 30 seconds, plus a
    /// per-category snapshot window of ~2 seconds. The spike detector and the
    /// drill-down UI both read this; nothing should write to it from outside.
    /// </summary>
    public PerTickAttributionRing PerTickRing => _perTickRing;

    /// <summary>The coalesced spike windows detected this session, oldest first.</summary>
    public IReadOnlyList<SpikeWindow> Spikes => _spikeDetector.Windows;

    /// <summary>
    /// Stalls detected this session — wall-clock gaps between consecutive
    /// <see cref="BeginTick"/> calls that exceeded the baseline tick period by
    /// the configured multiplier. Distinct from <see cref="Spikes"/>: a spike
    /// is too much work inside one tick, a stall is wall time elapsing without
    /// the game advancing (GC pauses, OS-suspended process, draw-thread
    /// blocking). Both are lag from the player's view; they have different
    /// causes so they're modelled separately.
    /// </summary>
    public IReadOnlyList<StallEvent> Stalls => _stallDetector.Events;

    /// <summary>
    /// Shared baseline (median frame time, median tick period, allocation
    /// rate, calibration state) recomputed once per <see cref="EndTick"/>.
    /// Every detector reads from this instead of carrying its own absolute
    /// floor — the threshold that's a "spike" depends on the user's actual
    /// hardware, not on the developer's assumptions.
    /// </summary>
    public Baseline Baseline => _baseline;

    /// <summary>
    /// Live measurement of the profiler's own footprint: install-delta
    /// managed heap, bytes-per-hook, process working set, severity. Refreshed
    /// at ~1 Hz from <see cref="EndTick"/> so the per-frame path doesn't pay
    /// the <c>proc_pidinfo</c> cost. <see cref="ProfilerSelfHealth.MarkInstallStart"/>
    /// and <see cref="ProfilerSelfHealth.MarkInstallEnd"/> are called by
    /// <see cref="ProfilerSystem"/> around the hook-install pass.
    /// </summary>
    public ProfilerSelfHealth SelfHealth => _selfHealth;

    /// <summary>
    /// Force-close any spike window that's still open. Called by
    /// <c>ProfilerSystem.OnWorldUnload</c> before the final session report is
    /// written so an in-progress spike that ended with the world exit is still
    /// captured. Without this, the window stays in the detector's open-slot
    /// scratch and never reaches the retained ring or the JSON.
    /// </summary>
    public void FlushSpikes() => _spikeDetector.Flush();

    /// <summary>True between a <see cref="BeginTick"/> and its matching <see cref="EndTick"/>.</summary>
    public bool TickOpen => _tickStartTimestamp >= 0L;

    /// <summary>
    /// Marks the start of a tick: captures the wall-clock and GC-pause baselines
    /// the matching <see cref="EndTick"/> measures against, runs the
    /// <see cref="StallDetector"/> against the gap from the previous tick,
    /// and clears the per-mod accumulator so the detours start the tick from
    /// zero.
    /// </summary>
    /// <param name="tickIndex">The game's tick index for this tick. Used by the stall detector for event tagging.</param>
    public void BeginTick(long tickIndex)
    {
        long now = Stopwatch.GetTimestamp();
        // Real inter-frame period: wall time since the previous BeginTick, which
        // spans the whole game loop (Update + Draw + vsync sleep). This is the
        // slow-motion signal FrameTimeMs cannot see; -1 guards the first tick,
        // which has no predecessor and falls back to the update-window in EndTick.
        _realFramePeriodMs = _prevBeginTimestamp >= 0L
            ? (now - _prevBeginTimestamp) * TicksToMs
            : 0d;
        _prevBeginTimestamp = now;
        _tickStartTimestamp = now;
        _gcPauseMsAtTickStart = GcPauseMilliseconds();

        // Stall detection runs HERE. At this point the per-mod accumulator
        // still holds any hook samples that fired between the previous
        // EndTick and now — typically draw-thread hooks during the inter-tick
        // gap. If a stall fires, those samples are the closest thing we have
        // to per-mod attribution for the gap (the gap itself contains no
        // BeginTick/EndTick window we could measure inside).
        long nowUnixMs = Time.UnixMsNow();
        // Pass the smoothed per-mod cost AND the focus state so the
        // detector can attribute the stall to the mods that were costing
        // the most when the gap began (CheatSheet-menu case) and
        // distinguish a real OS suspension (focus lost during the gap)
        // from a main-thread freeze (focus held, game just didn't
        // progress — the v0.4 misdiagnosis).
        bool hadFocus = ProfilerFocusProbe.Read();
        _stallDetector.OnBeginTick(_tickStartTimestamp, tickIndex, nowUnixMs, _baseline, _perModSmoothedMs, hadFocus);

        // Note: PerModAttribution accumulator is NOT cleared here anymore.
        // The clear moved to EndTick (after harvest) so that draw-thread
        // hooks firing in the gap between EndTick and the next BeginTick
        // get their samples included in the next harvest instead of dropped.
        // See the "draw-thread attribution leak" finding from the audit.
    }

    /// <summary>
    /// Back-compat overload used by tests that don't carry a tick index.
    /// The stall detector falls back to using the count of ticks seen as
    /// its index; loses cross-reference fidelity but keeps the detector
    /// behaviour intact.
    /// </summary>
    public void BeginTick() => BeginTick(_history.Count);

    /// <summary>
    /// Marks the end of a tick, builds its <see cref="TickFrame"/>, commits it
    /// to the history, and harvests the per-mod attribution for the tick. Does
    /// nothing if no tick is open: a partial-update frame is "not sampled",
    /// never recorded as a 0 ms tick.
    /// </summary>
    /// <param name="tickIndex">Session-relative tick index (the game's update counter).</param>
    /// <param name="npcCount">Active NPC count at tick close.</param>
    /// <param name="projectileCount">Active projectile count at tick close.</param>
    /// <param name="dustCount">Active dust count at tick close.</param>
    public void EndTick(long tickIndex, int npcCount, int projectileCount, int dustCount)
    {
        if (_tickStartTimestamp < 0L)
        {
            return;
        }

        long endTimestamp = Stopwatch.GetTimestamp();

        double gcTimeMs = GcPauseMilliseconds() - _gcPauseMsAtTickStart;
        if (gcTimeMs < 0d)
        {
            // The pause counter is monotonic; clamp only as a defensive guard.
            gcTimeMs = 0d;
        }

        TickFrame frame = new TickFrame
        {
            TimestampUnixMs = Time.UnixMsNow(),
            TickIndex = tickIndex,
            FrameTimeMs = TimestampDeltaMs(_tickStartTimestamp, endTimestamp),
            RealFrameTimeMs = _realFramePeriodMs > 0d
                ? _realFramePeriodMs
                : TimestampDeltaMs(_tickStartTimestamp, endTimestamp),
            GcTimeMs = gcTimeMs,
            NpcCount = npcCount,
            ProjectileCount = projectileCount,
            DustCount = dustCount,
            ModSamples = null, // Per-frame per-mod arrays are a later memory-tuning step.
        };

        _history.Push(in frame);

        // Harvest this tick's per-mod cost from backend 0 (the primary backend
        // -- delegate in Delegate/Parallel mode, ILHook in ILHook-only mode --
        // since HookBackend.ILHookBackendId folds to 0 when not parallel).
        // Then fold it into the smoothed view.
        PerModAttribution.HarvestInto(_perModRawMs, backendId: 0);
        UpdateRollingAverage(_perModRawMs, _perModHistoryMs, _perModRollingMs, _perModAverageMs, _sampleSlot);
        // Backend-0 total folded into the smoothing pass: this loop already
        // reads every _perModRawMs cell ascending, so accumulating the sum here
        // is bit-identical to a separate SumAll pass (same addition order) and
        // saves a second full sequential walk of the per-mod grid each tick.
        double total0 = 0d;
        for (int i = 0; i < _perModSmoothedMs.Length; i++)
        {
            double raw = _perModRawMs[i];
            total0 += raw;
            double v = _perModSmoothedMs[i] + PerModSmoothing * (raw - _perModSmoothedMs[i]);
            _perModSmoothedMs[i] = v < DenormalFloor ? 0d : v;
        }

        PerModAttribution.HarvestHooksInto(_perHookRawMs, backendId: 0);
        // Fast (display) + slow (~30 s average) EMAs folded in one walk. Replaces
        // the windowed-mean UpdateRollingAverage that required the ~896 MB per-hook
        // history ring: same observable, O(HookCount) memory, one 62k walk instead
        // of three (harvest already walked once; this replaces the rolling-average
        // walk and merges the smoothing walk).
        for (int i = 0; i < _perHookSmoothedMs.Length; i++)
        {
            double raw = _perHookRawMs[i];
            double sm = _perHookSmoothedMs[i] + PerModSmoothing * (raw - _perHookSmoothedMs[i]);
            double av = _perHookAverageMs[i] + PerHookAverageSmoothing * (raw - _perHookAverageMs[i]);
            _perHookSmoothedMs[i] = sm < DenormalFloor ? 0d : sm;
            _perHookAverageMs[i] = av < DenormalFloor ? 0d : av;
        }

        // Allocation tracking: parallel harvest + smoothing for bytes. Same shape
        // as the CPU pass; only runs when the collector was built with tracking on.
        if (_tracksAllocations)
        {
            PerModAttribution.HarvestAllocationsInto(_perModRawBytes!, backendId: 0);
            UpdateRollingAverage(_perModRawBytes!, _perModHistoryBytes!, _perModRollingBytes!, _perModAverageBytes!, _sampleSlot);
            for (int i = 0; i < _perModSmoothedBytes!.Length; i++)
            {
                double v = _perModSmoothedBytes[i] + PerModSmoothing * (_perModRawBytes![i] - _perModSmoothedBytes[i]);
                _perModSmoothedBytes[i] = v < DenormalFloor ? 0d : v;
            }

            PerModAttribution.HarvestHookAllocationsInto(_perHookRawBytes!, backendId: 0);
            // Same EMA fold as the per-hook ms path — drops the second ~896 MB
            // history ring (the bytes twin).
            for (int i = 0; i < _perHookSmoothedBytes!.Length; i++)
            {
                double raw = _perHookRawBytes![i];
                double sm = _perHookSmoothedBytes[i] + PerModSmoothing * (raw - _perHookSmoothedBytes[i]);
                double av = _perHookAverageBytes![i] + PerHookAverageSmoothing * (raw - _perHookAverageBytes[i]);
                _perHookSmoothedBytes[i] = sm < DenormalFloor ? 0d : sm;
                _perHookAverageBytes[i] = av < DenormalFloor ? 0d : av;
            }
        }

        // Track per-backend totals so the UI can show a backend divergence badge
        // and the log can periodically report the comparison. total0 was folded
        // into the smoothing loop above (same ascending order as SumAll).
        _backendTotalSmoothedMs0 += PerModSmoothing * (total0 - _backendTotalSmoothedMs0);

        if (_perModRawMsBackend1 != null)
        {
            PerModAttribution.HarvestInto(_perModRawMsBackend1, backendId: 1);
            double total1 = SumAll(_perModRawMsBackend1);
            _backendTotalSmoothedMs1 += PerModSmoothing * (total1 - _backendTotalSmoothedMs1);
        }

        // Recompute the shared baseline from the just-updated history. Every
        // detector that follows reads its threshold from here instead of
        // carrying a hardcoded ms floor. Allocation total for the tick is the
        // sum of the per-mod-per-category bytes we just harvested.
        double allocBytesThisTick = 0d;
        if (_tracksAllocations && _perModRawBytes != null)
        {
            for (int i = 0; i < _perModRawBytes.Length; i++) allocBytesThisTick += _perModRawBytes[i];
        }
        // Pass the just-closed frame directly so the steady-state path skips
        // re-fetching it through the history ring's indexer (bounds check +
        // wrap arithmetic + full-struct copy). The overload falls back to the
        // history-based rebuild whenever its incremental state has drifted.
        _baseline.Recompute(_history, in frame, _tracksAllocations, allocBytesThisTick);

        // Push this tick's raw per-mod-per-category row into the history ring,
        // then run the spike detector. Order matters: the detector reads from
        // the ring when capturing a worst-tick snapshot, so the ring must be
        // up-to-date first. Both operations are allocation-free in steady
        // state -- the detector only allocates the SpikeWindow's per-mod arrays
        // on an actual new spike, which is a rare event.
        _perTickRing.Push(frame.TickIndex, _perModRawMs, _tracksAllocations ? _perModRawBytes : null);
        _spikeDetector.OnTick(frame, _baseline, _perTickRing);

        // Self-health refresh is cadence-gated to ~1 Hz inside the call; it
        // costs nothing on 59 of every 60 ticks. The 60th tick pays one
        // proc_pidinfo + one GC.GetTotalMemory read.
        _selfHealth.Refresh(frame.TickIndex);

        // Clear the per-mod accumulator AFTER harvest. Anything that writes
        // into it after this (draw-thread hooks during the gap, the next
        // PreUpdateEntities firing late) accumulates into a clean buffer
        // and is included in the next EndTick's harvest. The old design
        // cleared at BeginTick which dropped every gap write on the floor.
        PerModAttribution.BeginTick();

        _sampleSlot++;
        if (_sampleSlot == _historyCapacity)
        {
            _sampleSlot = 0;
        }

        // Self-overhead (A2 + A3): everything since endTimestamp — the frame
        // build, the per-mod + per-hook harvest and smoothing, the baseline
        // recompute, the spike/stall passes, the self-health refresh — is the
        // profiler's own central per-tick cost, and it all ran AFTER the frame's
        // end timestamp was taken, so FrameTimeMs never saw it. Measure it here
        // and fold it (plus the frame's instrumented-call count) into self-health
        // so the profiler's true cost is visible on the Self tab, not hidden.
        double harvestMs = (Stopwatch.GetTimestamp() - endTimestamp) * TicksToMs;
        _selfHealth.RecordTickOverhead(harvestMs, ProbeStack.TakeCallCount());

        _tickStartTimestamp = -1L;
    }

    /// <summary>Returns true once per <see cref="DivergenceLogIntervalTicks"/> ticks, when parallel benchmarking is active.</summary>
    public bool ConsumeDivergenceLogTrigger()
    {
        if (_perModRawMsBackend1 == null)
        {
            return false;
        }

        _divergenceLogCountdown--;
        if (_divergenceLogCountdown > 0)
        {
            return false;
        }

        _divergenceLogCountdown = DivergenceLogIntervalTicks;
        return true;
    }

    private static double SumAll(double[] values)
    {
        double sum = 0d;
        for (int i = 0; i < values.Length; i++)
        {
            sum += values[i];
        }
        return sum;
    }

    /// <summary>
    /// Maintain a rolling sum + rolling average over <see cref="_historyCapacity"/>
    /// samples for one parallel set of arrays. v0.5/v0.6 ran a scalar loop;
    /// v0.6.1 vectorises via <see cref="System.Numerics.Vector{T}"/> when the
    /// array length permits, processing <see cref="System.Numerics.Vector{double}.Count"/>
    /// values per iteration. On Apple Silicon and modern x86, that's typically
    /// 2 or 4 doubles per SIMD lane (= ~2-4× throughput on this hot loop).
    /// Falls back to the scalar loop for any tail that doesn't fit a full
    /// vector. Per metric-collection §4.7 [F] + cross-allocations §6.2 β.
    /// </summary>
    private void UpdateRollingAverage(double[] source, double[] history, double[] rolling, double[] average, int slot)
    {
        int n = source.Length;
        int offset = slot * n;
        int samples = _history.Count < _historyCapacity ? _history.Count + 1 : _historyCapacity;
        double invSamples = 1.0 / samples;

        int vecLen = System.Numerics.Vector<double>.Count;
        int i = 0;
        if (vecLen > 1 && n >= vecLen)
        {
            var sourceSpan = new System.Span<double>(source);
            var historySpan = new System.Span<double>(history, offset, n);
            var rollingSpan = new System.Span<double>(rolling);
            var averageSpan = new System.Span<double>(average);
            var invVec = new System.Numerics.Vector<double>(invSamples);

            for (; i <= n - vecLen; i += vecLen)
            {
                var src = new System.Numerics.Vector<double>(sourceSpan.Slice(i, vecLen));
                var hist = new System.Numerics.Vector<double>(historySpan.Slice(i, vecLen));
                var roll = new System.Numerics.Vector<double>(rollingSpan.Slice(i, vecLen));

                // rolling += source - history
                var newRoll = roll + (src - hist);
                newRoll.CopyTo(rollingSpan.Slice(i, vecLen));
                // history[index] = source
                src.CopyTo(historySpan.Slice(i, vecLen));
                // average = rolling / samples (implemented as * invSamples)
                (newRoll * invVec).CopyTo(averageSpan.Slice(i, vecLen));
            }
        }

        // Scalar tail for the remainder (also the whole loop when n < vecLen).
        for (; i < n; i++)
        {
            int index = offset + i;
            rolling[i] += source[i] - history[index];
            history[index] = source[i];
            average[i] = rolling[i] * invSamples;
        }
    }

    /// <summary>Converts a delta of <see cref="Stopwatch"/> timestamps to milliseconds.</summary>
    private static double TimestampDeltaMs(long startTimestamp, long endTimestamp)
    {
        return (endTimestamp - startTimestamp) * TicksToMs;
    }

    /// <summary>
    /// Cumulative GC pause time since process start, in milliseconds.
    /// <see cref="GC.GetTotalPauseDuration"/> (.NET 8+) is monotonic, so the
    /// difference of two readings is the pause time within the interval.
    /// </summary>
    private static double GcPauseMilliseconds()
    {
        return GC.GetTotalPauseDuration().TotalMilliseconds;
    }
}
