#nullable enable

using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler;

/// <summary>
/// Player-facing per-feature settings (atlas S23), served through tModLoader's
/// own Mod Config UI — deliberately NO custom settings surface ("reduce churn,
/// not increase it"). Grouped by impact: the heavy-CPU knobs, the heavy-RAM
/// knobs, the dashboard, the detectors. Every default is the HEAVIEST
/// configuration — full instrumentation is the product and the thing we
/// verify; the sliders exist so a player can turn specific costs DOWN, never
/// because a lighter mode is the recommended state.
///
/// <para>
/// Read discipline: the hot path never touches this class. World-arm reads a
/// snapshot (<see cref="ProfilerSystem"/>); load-time gates read once in
/// <see cref="PerformanceProfiler.Load"/>; <see cref="OnChanged"/> pushes the
/// few runtime-safe values (detector sensitivities, poll cadence) to the live
/// systems. <c>[ReloadRequired]</c> marks the install-time gates, so tML's own
/// reload flow handles them — no custom prompts.
/// </para>
/// </summary>
[BackgroundColor(13, 17, 23)]
public sealed class ProfilerConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ClientSide;

    // ---------------------------------------------------------------- CPU

    [Header("MeasurementCpu")]

    /// <summary>ILHook per-hook attribution vs the lighter delegate-level backend.</summary>
    [DefaultValue(true)]
    [ReloadRequired]
    public bool PerHookAttribution;

    /// <summary>Per-mod allocation deltas on every probe (the Deep alloc path).</summary>
    [DefaultValue(true)]
    [ReloadRequired]
    public bool AllocationTracking;

    /// <summary>Split per-mod cost into update-phase vs draw-phase lanes (atlas S01).</summary>
    [DefaultValue(true)]
    public bool PhaseSplitAttribution;

    /// <summary>How often the insights engine evaluates, in ticks.</summary>
    [DefaultValue(60)]
    [Range(30, 600)]
    [Increment(30)]
    [Slider]
    [DrawTicks]
    public int DetectorCadenceTicks;

    // ---------------------------------------------------------------- RAM

    [Header("MeasurementRam")]

    /// <summary>Rolling frame-history window; per-tick RAM scales with it.</summary>
    [DefaultValue(1800)]
    [Range(600, 3600)]
    [Increment(300)]
    [Slider]
    [DrawTicks]
    public int FrameHistoryTicks;

    /// <summary>The session memory-trend sampler (atlas S04 first slice).</summary>
    [DefaultValue(true)]
    public bool MemoryGuard;

    /// <summary>Seconds between memory-trend samples.</summary>
    [DefaultValue(5)]
    [Range(2, 60)]
    [Slider]
    public int MemorySampleSeconds;

    // ---------------------------------------------------------------- Dashboard

    [Header("Dashboard")]

    /// <summary>The local browser-dashboard HTTP server.</summary>
    [DefaultValue(true)]
    [ReloadRequired]
    public bool DashboardServer;

    /// <summary>Dashboard poll cadence in milliseconds.</summary>
    [DefaultValue(500)]
    [Range(250, 2000)]
    [Increment(250)]
    [Slider]
    [DrawTicks]
    public int PollMs;

    /// <summary>Write a self-contained HTML session report at session end (atlas S17).</summary>
    [DefaultValue(false)]
    public bool AutoExportHtmlReport;

    // ---------------------------------------------------------------- Detectors

    [Header("Detectors")]

    /// <summary>
    /// Spike-detection sensitivity multiplier. 1.0 = the tuned default
    /// (a spike is ≥2× your session-median frame); higher = more sensitive
    /// (threshold divides by this).
    /// </summary>
    [DefaultValue(1.0f)]
    [Range(0.5f, 3f)]
    [Increment(0.25f)]
    [Slider]
    public float SpikeSensitivity;

    /// <summary>Stall-detection sensitivity multiplier; same semantics as spikes.</summary>
    [DefaultValue(1.0f)]
    [Range(0.5f, 3f)]
    [Increment(0.25f)]
    [Slider]
    public float StallSensitivity;

    /// <summary>Cross-session lifetime insights (reads the persisted rollup).</summary>
    [DefaultValue(true)]
    public bool CrossSessionInsights;

    // ---------------------------------------------------------------- plumbing

    /// <summary>
    /// Push the runtime-safe values to the live systems. Called by tML after
    /// any change is accepted (and once after load). World-arm values
    /// (history window, phase split) take effect at the next world load;
    /// [ReloadRequired] fields never reach here with a live world.
    /// </summary>
    public override void OnChanged()
    {
        ModContent.GetInstance<ProfilerSystem>()?.ApplyRuntimeConfig(this);
    }
}
