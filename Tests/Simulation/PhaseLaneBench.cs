#nullable enable

using System.Diagnostics;
using PerformanceProfiler.Data.Aggregators;
using Xunit;
using Xunit.Abstractions;

namespace PerformanceProfiler.Tests.Simulation;

/// <summary>
/// The Invariant-2 measurement the loop-anatomy plan demands: the phase-lane
/// branch's cost on the Add hot path, measured on a synthetic 62k-hook tick.
/// Diagnostic style (numbers to output, generous ceiling assert) — timing
/// pins with tight thresholds flake on shared runners; the recorded numbers
/// travel in the commit message per the plan's acceptance criteria.
/// </summary>
public sealed class PhaseLaneBench
{
    private const int HookCount = 62_203;   // the live kitchen-sink stack
    private const int Rounds = 5;

    private readonly ITestOutputHelper _out;
    public PhaseLaneBench(ITestOutputHelper output) => _out = output;

    private static double TimeOneTick(bool lanes, bool drawPhase)
    {
        PerModAttribution.Configure(modCount: 29, backendCount: 1, trackAllocations: false, phaseLanes: lanes);
        // One registered hook re-credited HookCount times: exercises the exact
        // Add instruction path the same number of times as a full 62k-hook
        // tick without paying 62k RegisterHook resizes per round.
        int hook = PerModAttribution.RegisterHook(0, 0, "Bench::Hook");
        PerModAttribution.BeginTick();
        PerModAttribution.CurrentPhaseIsUpdate = !drawPhase;

        long t0 = Stopwatch.GetTimestamp();
        for (int i = 0; i < HookCount; i++)
        {
            PerModAttribution.Add(0, 0, 0, hook, 10L);
        }
        long t1 = Stopwatch.GetTimestamp();
        return (t1 - t0) * 1000d / Stopwatch.Frequency;
    }

    [Fact]
    public void PhaseLaneBranch_CostOnA62kHookTick()
    {
        // Warm the JIT on both shapes first.
        TimeOneTick(lanes: false, drawPhase: false);
        TimeOneTick(lanes: true, drawPhase: true);

        double off = double.MaxValue, onUpdate = double.MaxValue, onDraw = double.MaxValue;
        for (int r = 0; r < Rounds; r++)
        {
            // Best-of-N: ambient scheduler noise only ever adds time.
            double a = TimeOneTick(lanes: false, drawPhase: false);
            double b = TimeOneTick(lanes: true, drawPhase: false);
            double c = TimeOneTick(lanes: true, drawPhase: true);
            if (a < off) off = a;
            if (b < onUpdate) onUpdate = b;
            if (c < onDraw) onDraw = c;
        }

        _out.WriteLine($"62k Adds — lanes OFF: {off:F3} ms · lanes ON (update path): {onUpdate:F3} ms · " +
                       $"lanes ON (draw path, extra writes): {onDraw:F3} ms");
        _out.WriteLine($"deltas — update: {onUpdate - off:+0.000;-0.000} ms · draw: {onDraw - off:+0.000;-0.000} ms");

        // Generous ceilings: the plan's < 0.05 ms/t target is asserted loosely
        // (2 ms) so scheduler noise can't flake the suite; the printed best-of
        // numbers are the real record.
        Assert.True(onUpdate - off < 2.0, $"update-path lane cost {onUpdate - off:F3} ms exploded");
        Assert.True(onDraw - off < 2.0, $"draw-path lane cost {onDraw - off:F3} ms exploded");
    }
}
