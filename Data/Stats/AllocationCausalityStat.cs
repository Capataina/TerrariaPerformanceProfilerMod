#nullable enable

using System;
using System.Collections.Generic;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Insights.Shared;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// L6 — allocation→GC causality. For every GC-cause stall, looks back 5 s
/// (300 ticks at 60 tps) over <see cref="PerTickAttributionRing"/> and ranks
/// the top mods by bytes allocated in that window.
///
/// <para>
/// Returns <see cref="AllocCausalitySnapshot.Empty"/> when allocation
/// tracking is disabled — the byte columns are not populated in that mode so
/// the chain cannot be reconstructed.
/// </para>
/// </summary>
public sealed class AllocationCausalityStat : IDataStat<AllocCausalitySnapshot>
{
    /// <summary>Look-back window in ms (kept at 5 s per the L6 contract).</summary>
    public const long WindowMs = 5000L;

    /// <summary>Look-back window in ticks at 60 tps.</summary>
    public const int WindowTicks = 300;

    /// <summary>Top-N contributors emitted per chain.</summary>
    public const int TopContributors = 5;

    /// <summary>Hard cap on chains returned per snapshot (GC stalls are bounded by the stall ring's 50 entries anyway).</summary>
    public const int MaxChains = 64;

    public string Name => RolloutStreamNames.AllocCausality;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public AllocCausalitySnapshot CurrentSnapshot()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        MetricCollector? c = sys?.Collector;
        if (c == null) return AllocCausalitySnapshot.Empty;
        if (!c.TracksAllocations) return AllocCausalitySnapshot.Empty;

        PerTickAttributionRing ring = c.PerTickRing;
        IReadOnlyList<StallEvent> stalls = c.Stalls;
        if (stalls.Count == 0) return new AllocCausalitySnapshot(worldLoaded: true, Array.Empty<AllocCausalityChain>());

        string[] modNames = HookInterceptor.ProfiledModNames;
        int modCount = modNames.Length;

        List<AllocCausalityChain> chains = new(Math.Min(stalls.Count, MaxChains));

        for (int i = 0; i < stalls.Count && chains.Count < MaxChains; i++)
        {
            StallEvent s = stalls[i];
            if (s.Cause != StallCause.MajorGc && s.Cause != StallCause.MinorGc) continue;

            // Window: [stallTick - WindowTicks, stallTick - 1]. Anchor on
            // StartTickIndex (the last good tick before the GC pause) so
            // the window covers the allocations that PRECEDED the GC.
            long endTick = s.StartTickIndex;
            long startTick = endTick - WindowTicks + 1;

            // Per-mod byte totals across the window. Allocation-free at the
            // sum level (we accumulate into a local long[]), but the result
            // list is allocated per chain — acceptable at OnDemand cadence.
            long[] perModBytes = new long[modCount];
            long totalBytes = 0;
            for (long t = startTick; t <= endTick; t++)
            {
                for (int m = 0; m < modCount; m++)
                {
                    float b = ring.GetPerModBytes(t, m);
                    if (b <= 0f) continue;
                    perModBytes[m] += (long)b;
                    totalBytes += (long)b;
                }
            }

            // Top-N selection. Simple branch: small N (5), small mod count
            // (typically < 200), O(N * modCount) is fine off-hot-path.
            List<AllocCausalityContributor> top = new(TopContributors);
            for (int k = 0; k < TopContributors; k++)
            {
                int bestIdx = -1;
                long bestVal = 0;
                for (int m = 0; m < modCount; m++)
                {
                    if (perModBytes[m] > bestVal)
                    {
                        bestVal = perModBytes[m];
                        bestIdx = m;
                    }
                }
                if (bestIdx < 0) break;
                double share = Shares.Percentage(bestVal, totalBytes);
                string name = ModNames.SafeName(bestIdx, modNames, "—");
                top.Add(new AllocCausalityContributor(
                    ModId: bestIdx,
                    ModName: name,
                    BytesInWindow: bestVal,
                    SharePct: share));
                perModBytes[bestIdx] = 0; // remove from further consideration
            }

            // Guard the subtraction the way the sibling GcPressureStat does
            // (GcPressureStat.cs:88-90): a GC where the heap is larger after
            // than before (allocation inside the collection window, or a
            // sampling race) would otherwise ship a negative byte count to the
            // L6 panel. Same abs handling, so the two stats agree.
            long freedBytes = Math.Abs(s.HeapSizeBeforeBytes - s.HeapSizeAfterBytes);
            chains.Add(new AllocCausalityChain(
                GcStallUnixMs: s.StartTimestampUnixMs,
                GcStallTickIndex: s.StartTickIndex,
                PauseMs: (long)s.TickPeriodMs,
                FreedBytes: freedBytes,
                GcKind: EnumStringTable.CauseName(s.Cause),
                WindowMs: WindowMs,
                TotalBytesInWindow: totalBytes,
                TopContributors: top));
        }

        return new AllocCausalitySnapshot(worldLoaded: true, chains);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
