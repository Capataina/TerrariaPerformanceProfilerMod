#nullable enable

using System;
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
/// Severity bucket for the profiler's own resource budget. Drives overlay
/// colour and surfaces in the session JSON so the player and the agent can
/// both see when we've become heavy.
/// </summary>
public enum SelfHealthSeverity : byte
{
    /// <summary>Within budget; nothing to flag.</summary>
    Healthy = 0,
    /// <summary>Above the comfortable band but not dangerous yet.</summary>
    Concerning = 1,
    /// <summary>Profiler is a meaningful share of the game's memory footprint.</summary>
    Severe = 2,
}

/// <summary>
/// Measures the profiler's own resource cost so it can never be silently
/// expensive. The mod is supposed to be a feature-rich observer with a
/// near-invisible footprint; the only way to keep ourselves honest is to
/// measure and surface our own cost continuously, in-game and in JSON, with
/// the same rigour we apply to any other mod.
///
/// <para>
/// <b>What we can measure honestly.</b> We DON'T claim to know our exact
/// per-mod RAM share — that requires per-<c>AssemblyLoadContext</c> memory
/// accounting we'd have to implement against tModLoader's private internals.
/// Instead we expose three measurements anyone can verify:
/// </para>
/// <list type="number">
///   <item><b>Install-delta</b> — managed heap size before vs after our hook
///   install pass. The dominant cost (Mono.Cecil method-body cache + MonoMod
///   trampolines for every ILHook). Captured once at install time.</item>
///   <item><b>Process working set</b> and <b>managed heap total</b> — refreshed
///   periodically. The process numbers cover everything (game, every mod,
///   the runtime), but they're the denominator for "how big a share of the
///   game are we".</item>
///   <item><b>Bytes per hook</b> — install-delta divided by installed hook
///   count. The number that explodes when bigger content mods bring their
///   own thousands of hooks; the single strongest signal for the eventual
///   memory-burn mitigation work.</item>
/// </list>
///
/// <para>
/// <b>Why one-shot install measurement plus a 1 Hz refresh.</b> Heap deltas
/// from the install pass are the single largest cost we incur. Per-tick
/// retained allocation (ring buffers, smoothing scratch) is dwarfed by it.
/// Refreshing process state at 1 Hz catches drift between sessions and
/// late-arriving heap pressure without paying the <c>proc_pidinfo</c> cost
/// every frame.
/// </para>
/// </summary>
public sealed class ProfilerSelfHealth
{
    /// <summary>Refresh cadence for live process state, in ticks (~1 s at 60 Hz).</summary>
    public const int RefreshIntervalTicks = 60;

    // v0.7.3: budget thresholds expressed as ratios over a measured baseline.
    // The OLD signal (fraction of process working set) was technically
    // "relative" but had a pathology: on a small modlist the tML process is
    // small, so install delta dominates the ratio and we trip Severe even
    // though per-hook cost is fine. On a large modlist both numerator and
    // denominator scale together, so the ratio stays roughly fixed. The
    // signal failed to distinguish "we regressed" from "the modlist is small."
    //
    // Bytes-per-hook is modlist-invariant by construction (10× hooks → 10×
    // delta, per-hook flat). Measured baselines across releases:
    //   v0.5   38.0 KB/hook
    //   v0.6.1 35.0 KB/hook
    //   v0.7.x 36.8 KB/hook
    //   v0.13  ~30   KB/hook (post-scaffolding-trim; install-ram.md exec log)
    // BaselineBytesPerHook below pins the "healthy normal" we measure
    // against. Bump it when an intentional install-path improvement lands
    // and we want the new floor to be the comparison point; leave it alone
    // if a per-hook regression slips in — then Severity surfaces it.
    //
    // Bands are ratios so the cutoffs scale with the baseline: a future
    // release that improves install to 20 KB/hook would update Baseline
    // to 20, and Concerning would automatically become 30 KB/hook
    // (1.5×) rather than the stale 55 KB constant the previous design used.
    //
    // NOTE: the constant is still pinned at the v0.7.x 36 KB normal, NOT the
    // v0.13 ~30 KB measured post-trim floor. Re-pinning to 30 KB tightens the
    // amber/red bands (Concerning would drop 54→45 KB), which shifts a
    // player- and agent-visible Severity badge — a tuning decision held for
    // engineer sign-off, not a free comment update.
    private const long BaselineBytesPerHook = 36L * 1024L;     // v0.7.x measured normal (retune to ~30 KB deferred)
    private const double ConcerningRatio = 1.5;                // 1.5× baseline → amber
    private const double SevereRatio     = 2.5;                // 2.5× baseline → red

    private readonly Process _self;
    private long _lastRefreshTickIndex;
    // Tracked explicitly instead of via "_lastRefreshTickIndex = long.MinValue":
    // the cadence-guard subtraction (currentTickIndex - _lastRefreshTickIndex)
    // signed-overflows on the first call when the sentinel is MinValue,
    // producing a negative result that's always < the cadence interval. The
    // first refresh then never fires and ProcessWorkingSetBytes stays at 0
    // for the whole session — the exact bug that left the v5 selfHealth
    // block reading severity=Healthy at a 527 MB install delta.
    private bool _hasEverRefreshed;

    // Wall-clock throttle shared by the tick-driven and dashboard-driven refresh
    // paths, so process metrics stay live at the menu (no ticks) without
    // double-sampling during play.
    private DateTime _lastSampleUtc = DateTime.MinValue;
    private static readonly TimeSpan WallRefreshInterval = TimeSpan.FromSeconds(1d);

    /// <summary>Captured at install start; the heap baseline we measure against.</summary>
    public long ManagedHeapAtInstallStartBytes { get; private set; }

    /// <summary>Captured immediately after hook install completes.</summary>
    public long ManagedHeapAtInstallEndBytes { get; private set; }

    /// <summary>The managed heap delta attributable to our install pass.</summary>
    public long InstallDeltaBytes => ManagedHeapAtInstallEndBytes - ManagedHeapAtInstallStartBytes;

    /// <summary>The number of hooks the active backend(s) installed.</summary>
    public int InstalledHookCount { get; private set; }

    /// <summary>
    /// Mean managed bytes attributable to one installed hook. The number that
    /// scales linearly with modlist size and drives the eventual "profiler is
    /// heavier than Calamity" problem.
    /// </summary>
    public long BytesPerHook => InstalledHookCount == 0 ? 0L : InstallDeltaBytes / InstalledHookCount;

    /// <summary>Process resident working set, refreshed at <see cref="RefreshIntervalTicks"/> cadence.</summary>
    public long ProcessWorkingSetBytes { get; private set; }

    /// <summary>Total managed heap across the whole process (game + every mod + runtime).</summary>
    public long ProcessManagedHeapBytes { get; private set; }

    /// <summary>How much of the process working set is GC-managed memory.</summary>
    public double ManagedFractionOfWorkingSet { get; private set; }

    /// <summary>
    /// Our install-delta as a fraction of process working set. This is the
    /// "how big a share of the game are we" number. Not perfectly accurate
    /// (process working set includes native code, image caches, etc.) but
    /// stable and conservative — if anything we under-estimate our share.
    /// </summary>
    public double InstallDeltaFractionOfProcess { get; private set; }

    /// <summary>Budget severity bucket derived from <see cref="BytesPerHook"/>. See <see cref="ClassifySeverity"/>.</summary>
    public SelfHealthSeverity Severity { get; private set; }

    // ---- Per-tick self-overhead (A2 + A3) -----------------------------------
    // The CPU cost the profiler adds to every frame that FrameTimeMs (the
    // update-window metric) structurally cannot see: EndTick captures its end
    // timestamp BEFORE running its own ~6-array harvest, so the profiler's
    // central per-tick bookkeeping never counted toward its own frame number.
    // Fed each tick by MetricCollector.RecordTickOverhead.

    private const double SelfOverheadSmoothing = 0.06d; // ~1 s settle @ 60 Hz, matches PerModSmoothing

    /// <summary>
    /// EMA of the profiler's own per-tick harvest cost, in milliseconds: the time
    /// <see cref="MetricCollector.EndTick"/> spends AFTER its end-timestamp
    /// snapshot — walking the per-mod and per-hook arrays, smoothing, recomputing
    /// the baseline, and running the spike/stall passes. This work runs outside
    /// the <c>FrameTimeMs</c> window, so surfacing it here is the only way the
    /// profiler's own central cost becomes visible to itself (A2).
    /// </summary>
    public double HarvestMsEma { get; private set; }

    /// <summary>
    /// EMA of the number of instrumented method calls (ProbeStack entries) per
    /// tick — a proxy for observer-effect magnitude (A3). The profiler pays two
    /// Stopwatch reads plus an attribution write on each, and that dispatch cost
    /// falls between the timed windows so it cannot be measured directly; the
    /// COUNT is the honest stand-in and scales with entity/projectile density.
    /// </summary>
    public double ProbeCallsPerTickEma { get; private set; }

    /// <summary>
    /// EMA of the DRAW-PHASE share of <see cref="ProbeCallsPerTickEma"/> (S01):
    /// probes that entered outside the update window. The live capture that
    /// motivated the phase split read 24,227 probe calls/tick while the game
    /// was PAUSED — all draw traffic, previously indistinguishable.
    /// </summary>
    public double ProbeCallsDrawPerTickEma { get; private set; }

    /// <summary>
    /// Folds this tick's measured harvest cost and probe-call counts into the
    /// self-overhead EMAs. Called once per tick from
    /// <see cref="MetricCollector.EndTick"/>; three multiply-adds, no allocation.
    /// </summary>
    public void RecordTickOverhead(double harvestMs, long probeCalls, long probeCallsDraw = 0L)
    {
        HarvestMsEma += SelfOverheadSmoothing * (harvestMs - HarvestMsEma);
        ProbeCallsPerTickEma += SelfOverheadSmoothing * (probeCalls - ProbeCallsPerTickEma);
        ProbeCallsDrawPerTickEma += SelfOverheadSmoothing * (probeCallsDraw - ProbeCallsDrawPerTickEma);
    }

    /// <summary>True once <see cref="MarkInstallEnd"/> has run; refresh() is a no-op before that.</summary>
    public bool IsInstalled { get; private set; }

    public ProfilerSelfHealth()
    {
        // Cache the Process reference; calling Process.GetCurrentProcess()
        // allocates a fresh Process object each invocation, which violates
        // the per-tick zero-allocation discipline.
        _self = Process.GetCurrentProcess();
    }

    /// <summary>
    /// Records the managed heap baseline immediately before
    /// <see cref="HookInterceptor.Install"/> runs. Pair with
    /// <see cref="MarkInstallEnd"/> after both backends finish.
    /// </summary>
    public void MarkInstallStart()
    {
        // Force a Gen2 BEFORE sampling. Pre-install we're between content
        // loading and our own work; the heap is full of transient setup
        // junk that should not count against us. A single Gen2 here costs
        // ~50-150 ms once and gives us a clean baseline forever after.
        GC.Collect(generation: 2, mode: GCCollectionMode.Forced, blocking: true);
        ManagedHeapAtInstallStartBytes = GC.GetTotalMemory(forceFullCollection: false);
    }

    /// <summary>
    /// Records the managed heap snapshot after both <see cref="HookInterceptor.Install"/>
    /// and <see cref="ILHookInterceptor.Install"/> have finished, and the
    /// final installed hook count.
    /// </summary>
    public void MarkInstallEnd(int installedHookCount)
    {
        // Force a Gen2 before sampling, symmetric with MarkInstallStart, so the
        // delta measures RETAINED state rather than whatever transient install
        // scratch the GC happened not to have collected at the sampling instant.
        // Without this the reported install-delta / bytes-per-hook swung ~25%
        // between identical 152k-hook loads (9.0 GB vs 7.2 GB) on GC timing
        // alone. The bulk is genuinely retained regardless (decompiled MonoMod
        // keeps a per-hook SourceCloneIl + read-only LastContext Cecil graph
        // until unload); this only makes the number honest and repeatable.
        GC.Collect(generation: 2, mode: GCCollectionMode.Forced, blocking: true);
        ManagedHeapAtInstallEndBytes = GC.GetTotalMemory(forceFullCollection: false);
        InstalledHookCount = installedHookCount;
        IsInstalled = true;
    }

    /// <summary>
    /// Refreshes live process state if at least <see cref="RefreshIntervalTicks"/>
    /// ticks have passed since the last refresh. Called every tick by
    /// <see cref="MetricCollector.EndTick"/>; the cadence guard keeps the
    /// <c>proc_pidinfo</c> cost to ~1 Hz instead of 60 Hz.
    /// </summary>
    public void Refresh(long currentTickIndex)
    {
        if (!IsInstalled) return;
        // Cadence guard via explicit "have we ever refreshed" flag — see the
        // signed-overflow note on _hasEverRefreshed for why a sentinel value
        // was wrong here.
        if (_hasEverRefreshed && currentTickIndex - _lastRefreshTickIndex < RefreshIntervalTicks) return;
        _hasEverRefreshed = true;
        _lastRefreshTickIndex = currentTickIndex;
        SampleProcessState();
    }

    /// <summary>
    /// Wall-clock refresh of live process state, throttled to ~1 Hz, callable
    /// when no world is live (no ticks) — e.g. from the dashboard's self-health
    /// snapshot read at the menu, so working-set / managed-heap don't read zero
    /// just because the tick loop isn't running. Shares <see cref="_lastSampleUtc"/>
    /// with the tick path so the two never double-sample: during play the tick
    /// refresh keeps it fresh and this no-ops; at the menu this is the only sampler.
    /// </summary>
    public void RefreshIfStale()
    {
        if (!IsInstalled) return;
        if (DateTime.UtcNow - _lastSampleUtc < WallRefreshInterval) return;
        SampleProcessState();
    }

    private void SampleProcessState()
    {
        try
        {
            _self.Refresh();
            ProcessWorkingSetBytes = _self.WorkingSet64;
            ProcessManagedHeapBytes = GC.GetTotalMemory(forceFullCollection: false);

            ManagedFractionOfWorkingSet = ProcessWorkingSetBytes > 0L
                ? (double)ProcessManagedHeapBytes / ProcessWorkingSetBytes
                : 0d;

            InstallDeltaFractionOfProcess = ProcessWorkingSetBytes > 0L
                ? (double)InstallDeltaBytes / ProcessWorkingSetBytes
                : 0d;

            // Memory-guard trend push (S04): ride the existing sample cadence,
            // throttled to the configured interval. The ring + verdict maths
            // live in Data.Stats.MemoryTrend (pure); the trend spans the
            // PROCESS lifetime, not one world — exactly what makes reload
            // stacking visible as a staircase.
            if (MemoryGuardEnabled &&
                (DateTime.UtcNow - _lastTrendPushUtc).TotalSeconds >= MemorySampleSeconds)
            {
                _lastTrendPushUtc = DateTime.UtcNow;
                _memoryTrend.Push(Time.UnixMsNow(), ProcessWorkingSetBytes, ProcessManagedHeapBytes);
                RecordMemoryTrend(_memoryTrend.Snapshot());
            }

            // Severity now keyed on per-hook cost (modlist-size-invariant),
            // ESCALATED by the memory-trend growth axis (H3, 2026-07-07): the
            // install-delta axis judges a snapshot and was structurally blind
            // to the 4.2 → 10.4 GB session walk the live capture recorded.
            // Whichever axis is worse wins; the Self gauge subtitle names it.
            SelfHealthSeverity installAxis = ClassifySeverity(BytesPerHook);
            Severity = (SelfHealthSeverity)Math.Max((byte)installAxis, (byte)GrowthSeverity);
            _lastSampleUtc = DateTime.UtcNow;
        }
        catch
        {
            // Process.Refresh on some platforms can fail transiently when the
            // OS denies a stat call. We never let self-health monitoring crash
            // the profiler — the worst case is one stale reading.
        }
    }

    private static SelfHealthSeverity ClassifySeverity(long bytesPerHook)
    {
        if (bytesPerHook <= 0L) return SelfHealthSeverity.Healthy;
        double ratio = (double)bytesPerHook / BaselineBytesPerHook;
        if (ratio >= SevereRatio) return SelfHealthSeverity.Severe;
        if (ratio >= ConcerningRatio) return SelfHealthSeverity.Concerning;
        return SelfHealthSeverity.Healthy;
    }

    // ---- Memory-trend growth axis (S04 guard, closes H3) --------------------

    private readonly Data.Stats.MemoryTrend _memoryTrend = new Data.Stats.MemoryTrend();
    private DateTime _lastTrendPushUtc = DateTime.MinValue;

    /// <summary>The process-lifetime trend ring; the Memory tab's strip reads it.</summary>
    public Data.Stats.MemoryTrend MemoryTrendRing => _memoryTrend;

    /// <summary>S23 gate: MemoryGuard config toggle. Off ⇒ no pushes, phase stays Warming.</summary>
    public bool MemoryGuardEnabled { get; set; } = true;

    /// <summary>S23: seconds between trend pushes (MemorySampleSeconds slider).</summary>
    public int MemorySampleSeconds { get; set; } = 5;

    /// <summary>Latest 10-minute growth slope from the trend sampler, MB/min.</summary>
    public double GrowthMbPerMin10 { get; private set; }

    /// <summary>Latest trend phase (warming/flat/growing/climbing/reclaimed).</summary>
    public Data.Stats.MemoryTrendPhase MemoryPhase { get; private set; }

    /// <summary>The growth axis' own severity contribution, folded into <see cref="Severity"/> on the next Refresh.</summary>
    public SelfHealthSeverity GrowthSeverity { get; private set; }

    /// <summary>
    /// Fed by the memory-guard sampler on its slow cadence. Sustained growth
    /// escalates severity: Growing ⇒ Concerning, Climbing ⇒ Severe. The
    /// warming gate lives in <see cref="Data.Stats.MemoryTrend"/> — a phase of
    /// Warming always reads Healthy here.
    /// </summary>
    public void RecordMemoryTrend(in Data.Stats.MemoryTrendSnapshot trend)
    {
        GrowthMbPerMin10 = trend.GrowthMbPerMin10;
        MemoryPhase = trend.Phase;
        GrowthSeverity = trend.Phase switch
        {
            Data.Stats.MemoryTrendPhase.Climbing => SelfHealthSeverity.Severe,
            Data.Stats.MemoryTrendPhase.Growing => SelfHealthSeverity.Concerning,
            _ => SelfHealthSeverity.Healthy,
        };
    }
}
