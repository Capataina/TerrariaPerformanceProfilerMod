#nullable enable

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
/// Selects which instrumentation backend is active.
///
/// <para><b>Delegate</b> — the original, signature-matching On-hook system in
/// <see cref="HookInterceptor"/>. Battle-tested but limited to ~71.6% coverage
/// because every signature shape needs a hand-written delegate pair.</para>
///
/// <para><b>ILHook</b> — the new, signature-agnostic IL-injection system in
/// <see cref="ILHookInterceptor"/>. Targets every hook override regardless of
/// signature, expected coverage ~100%.</para>
///
/// <para><b>Parallel</b> — both systems run simultaneously. Each writes to its
/// own attribution slot (backendId 0 for delegate, 1 for ILHook). The collector
/// computes per-tick divergence between the two; the overlay still displays the
/// delegate numbers (the proven baseline) while client.log captures the diff.
/// This is the validation mode -- when the divergence stays small in real
/// gameplay, we know the ILHook system is accurate.</para>
/// </summary>
public enum HookBackendMode
{
    /// <summary>Only the delegate-pair backend runs; ILHook dormant.</summary>
    Delegate = 0,
    /// <summary>Only the ILHook backend runs; delegate path dormant.</summary>
    ILHook = 1,
    /// <summary>Both backends run; per-tick divergence logged for comparison.</summary>
    Parallel = 2,
}

/// <summary>
/// Process-wide configuration for which hook backend(s) are active.
///
/// Read once at <see cref="HookInterceptor.Install"/> and
/// <see cref="ILHookInterceptor.Install"/>; reads after install have no effect
/// on already-installed detours. Changing the mode requires a mod reload.
/// </summary>
public static class HookBackend
{
    // Default: ILHook. Validated against the delegate baseline in Parallel mode
    // and confirmed to give a cleaner measurement -- the delegate path's
    // On-hook wrapper adds ~30-100ns of trampoline overhead per call that the
    // delegate path attributes to "hook cost", inflating quiet-tick numbers by
    // ~30%. The IL-injection path captures only the body's actual time.
    //
    // The delegate code stays in the assembly as an archived fallback: flip to
    // Delegate or Parallel here and rebuild to bring it back without code
    // changes (useful if a tModLoader update breaks something IL-specific).
    private static HookBackendMode _mode = HookBackendMode.ILHook;

    /// <summary>
    /// Whether the IL-emitted hook prologue also captures
    /// <see cref="System.GC.GetAllocatedBytesForCurrentThread"/> alongside
    /// <c>Stopwatch.GetTimestamp</c>, so per-mod allocation bytes can be
    /// attributed in parallel to per-mod CPU time.
    ///
    /// <para>
    /// Default: true. A throwaway micro-benchmark (not retained in the tree) measured the
    /// alloc API at ~3.2 ns/call vs Stopwatch at ~17.2 ns -- 5× cheaper --
    /// so the per-call cost of carrying alloc tracking on top of timing is
    /// marginal at our current modlist scale. This is the "Deep" mode the
    /// README names; future Lite mode will set this false to drop the extra
    /// call pair, plus the byte arrays in PerModAttribution / MetricCollector.
    /// </para>
    ///
    /// <para>
    /// Like <see cref="Mode"/>, this is read once at install time. Flipping
    /// it at runtime has no effect on already-installed detours -- a mod
    /// reload is required.
    /// </para>
    /// </summary>
    public static bool AllocationTracking { get; set; } = true;

    /// <summary>The active backend mode. Changes take effect on the next mod reload.</summary>
    public static HookBackendMode Mode
    {
        get => _mode;
        set => _mode = value;
    }

    /// <summary>Number of backends contributing measurements (1 in single-mode, 2 in parallel).</summary>
    public static int BackendCount => _mode == HookBackendMode.Parallel ? 2 : 1;

    /// <summary>The backendId for the delegate path's writes into PerModAttribution.</summary>
    public const int DelegateBackendId = 0;

    /// <summary>
    /// The backendId for the ILHook path's writes into PerModAttribution.
    /// In single-ILHook mode, the ILHook path also writes to slot 0 so the rest
    /// of the pipeline (collector, UI) doesn't need to know which backend produced
    /// the numbers. In Parallel mode, ILHook writes to slot 1 to keep the two
    /// streams separated for comparison.
    /// </summary>
    public static int ILHookBackendId => _mode == HookBackendMode.Parallel ? 1 : 0;

    /// <summary>The "primary" backendId the UI and session log read from.</summary>
    public static int PrimaryBackendId => 0;

    /// <summary>True if the delegate-pair system should install its detours.</summary>
    public static bool DelegateActive => _mode != HookBackendMode.ILHook;

    /// <summary>True if the ILHook system should install its detours.</summary>
    public static bool ILHookActive => _mode != HookBackendMode.Delegate;
}
