#nullable enable

namespace PerformanceProfiler.Profiling;

/// <summary>
/// One mod's CPU and allocation cost for a single game tick.
///
/// Pure data with no tModLoader dependency, so it is unit-testable without a
/// running game (see the testability standard in CLAUDE.md). The Metric
/// Collector fills a pre-allocated array of these once per tick, so the struct
/// is kept small and blittable: the array never grows and the values never box
/// (Invariant 2: the per-tick hot path must not allocate).
/// </summary>
public struct PerModSample
{
    /// <summary>
    /// Stable per-session mod id, assigned once at world-load. Doubles as the
    /// index of this sample within a tick's <see cref="TickFrame.ModSamples"/>
    /// array, so attribution is an array index rather than a lookup.
    /// </summary>
    public int ModId;

    /// <summary>CPU time attributed to this mod for the tick, in milliseconds.</summary>
    public double CpuMs;

    /// <summary>Managed bytes allocated by this mod during the tick.</summary>
    public long AllocatedBytes;

    /// <summary>Number of hook invocations attributed to this mod during the tick.</summary>
    public int HookCalls;

    /// <summary>
    /// Zeroes the per-tick measurements so a pooled sample can be reused for the
    /// next tick without reallocation. <see cref="ModId"/> is left intact: it
    /// identifies the slot for the whole session and is set once at world-load.
    /// </summary>
    public void ResetMeasurements()
    {
        CpuMs = 0d;
        AllocatedBytes = 0L;
        HookCalls = 0;
    }
}
