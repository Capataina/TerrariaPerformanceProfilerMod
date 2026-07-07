#nullable enable

using System.Collections.Generic;

namespace PerformanceProfiler.Tests.Simulation;

/// <summary>
/// The canonical scenario library (e2e plan Ring 1). Each scenario is a
/// scripted session shaped like a real failure class from this project's
/// history; the UI fixture generator mirrors these names so the C# pins and
/// the Playwright harness exercise the same shapes.
/// </summary>
internal static class Scenarios
{
    /// <summary>The 60 UPS tick: compute light, loop at the vsync budget.</summary>
    public const double HealthyComputeMs = 4.2;
    public const double HealthyRealMs = 16.67;

    /// <summary>The 2026-07-07 live slow-motion session: compute looked tiny, the loop ran at 2× budget.</summary>
    public const double SlowmoComputeMs = 8.3;
    public const double SlowmoRealMs = 33.3;

    /// <summary>Healthy hour-of-play sample: 3600 ticks ≈ 60 s of game time.</summary>
    public static List<ScriptTick> Healthy60(int ticks = 3600)
    {
        var s = new List<ScriptTick>(ticks);
        for (int i = 0; i < ticks; i++) s.Add(new ScriptTick(HealthyComputeMs, HealthyRealMs));
        return s;
    }

    /// <summary>
    /// The X1 class: uniform slow-motion. Compute-window time reads healthy
    /// (8.3 ms), the real loop runs at 33.3 ms — the exact live capture that
    /// produced "you sustain 60 fps with 8.4 ms free" during 31-fps slow-mo.
    /// </summary>
    public static List<ScriptTick> Slowmo30(int ticks = 3600)
    {
        var s = new List<ScriptTick>(ticks);
        for (int i = 0; i < ticks; i++) s.Add(new ScriptTick(SlowmoComputeMs, SlowmoRealMs));
        return s;
    }

    /// <summary>
    /// The X3 class: a healthy session with three long alt-tab suspends
    /// (25 s / 41 s / 45 s — the durations from the live client.log). The
    /// stall headline must exclude them; the speed metric must not crater.
    /// </summary>
    public static List<ScriptTick> AltTabbed()
    {
        var s = Healthy60(1200);
        s[300] = new ScriptTick(HealthyComputeMs, HealthyRealMs, suspendGapMs: 25_000d);
        s[700] = new ScriptTick(HealthyComputeMs, HealthyRealMs, suspendGapMs: 41_000d);
        s[1100] = new ScriptTick(HealthyComputeMs, HealthyRealMs, suspendGapMs: 45_000d);
        return s;
    }

    /// <summary>
    /// Variance, not level: healthy cadence with a hard 120 ms hitch every
    /// 200 ticks. Spikes must fire; sustained slowness must NOT (brief dips
    /// never accumulate 30 s below threshold).
    /// </summary>
    public static List<ScriptTick> Spiky(int ticks = 2400)
    {
        var s = new List<ScriptTick>(ticks);
        for (int i = 0; i < ticks; i++)
        {
            bool hitch = i > 0 && i % 200 == 0;
            s.Add(hitch
                ? new ScriptTick(90d, 120d)
                : new ScriptTick(HealthyComputeMs, HealthyRealMs));
        }
        return s;
    }

    /// <summary>Session younger than the calibration window: nothing may fire confidently.</summary>
    public static List<ScriptTick> Warming(int ticks = 45)
    {
        return Healthy60(ticks);
    }
}
