#nullable enable

using System.Diagnostics;
using Terraria.ModLoader;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// TEMPORARY: one-shot benchmark for the allocation-tracking API cost.
///
/// <para>
/// Step 3 of the spikes-and-allocations plan: we need to know how expensive
/// <c>GC.GetAllocatedBytesForCurrentThread()</c> is per call before deciding
/// whether to inject it unconditionally (Option A) or only on a sampled
/// fraction of hook calls (Option B). The plan's design assumed Option A
/// based on .NET source-reading; this benchmark validates that assumption
/// against the actual runtime we're shipping on.
/// </para>
///
/// <para>
/// Runs once on the first world load, writes its findings to
/// <c>client.log</c>, then unhooks itself. Comparable cost: a similarly-tight
/// loop of <see cref="Stopwatch.GetTimestamp"/> so we have a known-cheap
/// baseline to compare against (we already pay two of those per hook call,
/// so the alloc API has to be in the same league for Option A to be viable).
/// </para>
///
/// <para>
/// <b>To remove:</b> delete this file once the design path is locked. Cited
/// in plan step 9 as the cleanup target.
/// </para>
/// </summary>
public sealed class _TempAllocBench : ModSystem
{
    private const int Iterations = 1_000_000;
    private bool _hasRun;

    public override void OnWorldLoad()
    {
        if (_hasRun) return;
        _hasRun = true;
        RunBenchmark();
    }

    private void RunBenchmark()
    {
        // Warm up the JIT so we don't measure tiering compilation.
        long warmStopwatch = 0L;
        long warmAlloc = 0L;
        for (int i = 0; i < 10_000; i++)
        {
            warmStopwatch += Stopwatch.GetTimestamp();
            warmAlloc += System.GC.GetAllocatedBytesForCurrentThread();
        }
        // Reference the warmup sums so the JIT can't elide them entirely.
        if (warmStopwatch == long.MinValue) Mod.Logger.Debug("warmup sentinel");
        if (warmAlloc == long.MinValue) Mod.Logger.Debug("warmup sentinel");

        // Bench Stopwatch.GetTimestamp() — the known baseline.
        long stopwatchAccumulator = 0L;
        long startSw = Stopwatch.GetTimestamp();
        for (int i = 0; i < Iterations; i++)
        {
            stopwatchAccumulator += Stopwatch.GetTimestamp();
        }
        long endSw = Stopwatch.GetTimestamp();
        double swTotalNs = (endSw - startSw) * 1_000_000_000d / Stopwatch.Frequency;
        double swPerCallNs = swTotalNs / Iterations;

        // Bench GC.GetAllocatedBytesForCurrentThread().
        long allocAccumulator = 0L;
        long startAlloc = Stopwatch.GetTimestamp();
        for (int i = 0; i < Iterations; i++)
        {
            allocAccumulator += System.GC.GetAllocatedBytesForCurrentThread();
        }
        long endAlloc = Stopwatch.GetTimestamp();
        double allocTotalNs = (endAlloc - startAlloc) * 1_000_000_000d / Stopwatch.Frequency;
        double allocPerCallNs = allocTotalNs / Iterations;

        // Reference the accumulators so the JIT can't elide the loops.
        if (stopwatchAccumulator == long.MinValue) Mod.Logger.Debug("accumulator sentinel");
        if (allocAccumulator == long.MinValue) Mod.Logger.Debug("accumulator sentinel");

        double ratio = swPerCallNs > 0d ? allocPerCallNs / swPerCallNs : 0d;
        string verdict = allocPerCallNs < 30d ? "OPTION A (per-call)"
                       : allocPerCallNs < 60d ? "OPTION A (tighter budget)"
                       : "OPTION B (sampled)";

        Mod.Logger.Info(
            $"[alloc-bench] Stopwatch.GetTimestamp = {swPerCallNs:F1} ns/call; " +
            $"GC.GetAllocatedBytesForCurrentThread = {allocPerCallNs:F1} ns/call; " +
            $"ratio = {ratio:F2}×; verdict = {verdict}");
    }
}
