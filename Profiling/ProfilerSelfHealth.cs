#nullable enable

using System;
using System.Diagnostics;

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

    // Budget thresholds expressed as fractions of process working set. These
    // are relative because the absolute number depends entirely on the user's
    // modlist size — a 200 MB install-delta on a 4-mod player is catastrophic;
    // on a 60-mod kitchen-sink session it might be unavoidable. The ratio is
    // what we can defend.
    private const double SevereFraction = 0.20;     // >= 20% of process is us → red
    private const double ConcerningFraction = 0.10; // 10-20% → amber

    private readonly Process _self;
    private long _lastRefreshTickIndex = long.MinValue;

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

    /// <summary>Budget severity bucket derived from <see cref="InstallDeltaFractionOfProcess"/>.</summary>
    public SelfHealthSeverity Severity { get; private set; }

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
        // No forced GC here: the delta should reflect what we ACTUALLY hold
        // after install, including any pinning effects. Forcing here would
        // under-report by collecting transient install scratch we genuinely
        // released.
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
        if (currentTickIndex - _lastRefreshTickIndex < RefreshIntervalTicks) return;
        _lastRefreshTickIndex = currentTickIndex;

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

            Severity = ClassifySeverity(InstallDeltaFractionOfProcess);
        }
        catch
        {
            // Process.Refresh on some platforms can fail transiently when the
            // OS denies a stat call. We never let self-health monitoring crash
            // the profiler — the worst case is one stale reading.
        }
    }

    /// <summary>Resets the measurement so a fresh session starts clean.</summary>
    public void Reset()
    {
        _lastRefreshTickIndex = long.MinValue;
        ManagedHeapAtInstallStartBytes = 0L;
        ManagedHeapAtInstallEndBytes = 0L;
        InstalledHookCount = 0;
        ProcessWorkingSetBytes = 0L;
        ProcessManagedHeapBytes = 0L;
        ManagedFractionOfWorkingSet = 0d;
        InstallDeltaFractionOfProcess = 0d;
        Severity = SelfHealthSeverity.Healthy;
        IsInstalled = false;
    }

    private static SelfHealthSeverity ClassifySeverity(double fraction)
    {
        if (fraction >= SevereFraction) return SelfHealthSeverity.Severe;
        if (fraction >= ConcerningFraction) return SelfHealthSeverity.Concerning;
        return SelfHealthSeverity.Healthy;
    }
}
