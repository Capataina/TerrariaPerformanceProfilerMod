#nullable enable

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
    // Default: Parallel so the new ILHook backend lights up immediately and we
    // can compare it 1:1 against the delegate baseline. Drop to Delegate to
    // disable ILHook, or to ILHook to disable the delegate path.
    private static HookBackendMode _mode = HookBackendMode.Parallel;

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
