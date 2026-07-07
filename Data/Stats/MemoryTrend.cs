#nullable enable

using System;

namespace PerformanceProfiler.Data.Stats;

/// <summary>Descriptive memory-trend phase. Order matters: higher = more concerning.</summary>
public enum MemoryTrendPhase : byte
{
    /// <summary>Fewer than <see cref="MemoryTrend.MinSamplesForVerdict"/> samples — no verdict yet.</summary>
    Warming = 0,
    /// <summary>|slope| under <see cref="MemoryTrend.FlatMbPerMin"/> MB/min.</summary>
    Flat = 1,
    /// <summary>Negative slope after growth — memory came back.</summary>
    Reclaimed = 2,
    /// <summary>Slope between the flat and climbing thresholds.</summary>
    Growing = 3,
    /// <summary>Slope at or above <see cref="MemoryTrend.ClimbingMbPerMin"/> MB/min.</summary>
    Climbing = 4,
}

/// <summary>One computed view over the trend ring. All sizes in MB, rates in MB/min.</summary>
public readonly struct MemoryTrendSnapshot
{
    public readonly int SampleCount;
    public readonly double CurrentWorkingSetMb;
    public readonly double CurrentManagedMb;
    public readonly double SessionStartWorkingSetMb;
    public readonly double PeakWorkingSetMb;
    /// <summary>Least-squares slope over the trailing 10 minutes, MB/min. 0 while warming.</summary>
    public readonly double GrowthMbPerMin10;
    public readonly MemoryTrendPhase Phase;

    public MemoryTrendSnapshot(int sampleCount, double currentWs, double currentManaged,
        double startWs, double peakWs, double growth10, MemoryTrendPhase phase)
    {
        SampleCount = sampleCount;
        CurrentWorkingSetMb = currentWs;
        CurrentManagedMb = currentManaged;
        SessionStartWorkingSetMb = startWs;
        PeakWorkingSetMb = peakWs;
        GrowthMbPerMin10 = growth10;
        Phase = phase;
    }

    public static readonly MemoryTrendSnapshot Empty =
        new MemoryTrendSnapshot(0, 0d, 0d, 0d, 0d, 0d, MemoryTrendPhase.Warming);
}

/// <summary>
/// The session memory-trend ring + verdict maths (atlas S04 first slice; closes
/// H3's growth-blindness). Pure over pushed samples — the OS reads
/// (Process.Refresh, GC totals) happen in the caller — so the slope and phase
/// rules are unit-testable off-game. The 2026-07-07 live case this instruments:
/// working set walked 4.2 → 10.4 GB across a session while the Self tab read
/// "healthy" throughout, because only the install-time delta was judged.
///
/// <para>
/// Thread contract: <see cref="Push"/> from the sampler task, <see cref="Snapshot"/>
/// from the HTTP worker — both take the internal lock. At a ≥2 s sample cadence
/// and ~0.5 Hz poll rate, contention is nil; correctness beats lock-free
/// cleverness at these frequencies.
/// </para>
/// </summary>
public sealed class MemoryTrend
{
    /// <summary>Ring capacity: 2 h at the default 5 s cadence. ~34 KB of longs — negligible.</summary>
    public const int Capacity = 1440;

    /// <summary>Verdicts need at least this many samples (10 min at 5 s) — the warming gate.</summary>
    public const int MinSamplesForVerdict = 120;

    /// <summary>The trailing window the growth slope is fitted over, in milliseconds.</summary>
    public const long SlopeWindowMs = 10 * 60 * 1000;

    /// <summary>|slope| under this is flat.</summary>
    public const double FlatMbPerMin = 5d;

    /// <summary>Slope at or above this is climbing (the leak-suspect band).</summary>
    public const double ClimbingMbPerMin = 20d;

    private readonly object _gate = new object();
    private readonly long[] _unixMs = new long[Capacity];
    private readonly long[] _wsBytes = new long[Capacity];
    private readonly long[] _managedBytes = new long[Capacity];
    private int _head;
    private int _count;
    private long _peakWsBytes;
    private long _startWsBytes = -1L;
    private bool _sawGrowth;

    /// <summary>Record one sample. Caller supplies the OS/GC reads.</summary>
    public void Push(long unixMs, long workingSetBytes, long managedBytes)
    {
        lock (_gate)
        {
            _unixMs[_head] = unixMs;
            _wsBytes[_head] = workingSetBytes;
            _managedBytes[_head] = managedBytes;
            _head = _head + 1 == Capacity ? 0 : _head + 1;
            if (_count < Capacity) _count++;

            if (workingSetBytes > _peakWsBytes) _peakWsBytes = workingSetBytes;
            if (_startWsBytes < 0L) _startWsBytes = workingSetBytes;
        }
    }

    /// <summary>Compute the current snapshot (slope, phase, peaks) from the ring.</summary>
    public MemoryTrendSnapshot Snapshot()
    {
        lock (_gate)
        {
            if (_count == 0) return MemoryTrendSnapshot.Empty;

            int newest = _head == 0 ? Capacity - 1 : _head - 1;
            double curWs = _wsBytes[newest] / (1024d * 1024d);
            double curManaged = _managedBytes[newest] / (1024d * 1024d);
            double startWs = _startWsBytes / (1024d * 1024d);
            double peakWs = _peakWsBytes / (1024d * 1024d);

            if (_count < MinSamplesForVerdict)
            {
                return new MemoryTrendSnapshot(_count, curWs, curManaged, startWs, peakWs, 0d, MemoryTrendPhase.Warming);
            }

            double slope = SlopeMbPerMin(_unixMs[newest] - SlopeWindowMs);
            MemoryTrendPhase phase = Classify(slope, _sawGrowth);
            if (phase is MemoryTrendPhase.Growing or MemoryTrendPhase.Climbing) _sawGrowth = true;

            return new MemoryTrendSnapshot(_count, curWs, curManaged, startWs, peakWs, slope, phase);
        }
    }

    /// <summary>
    /// Least-squares slope of working set over samples newer than
    /// <paramref name="sinceUnixMs"/>, in MB/min. A regression fit rather than
    /// last-minus-first so a single GC dip or expansion step cannot swing the
    /// verdict (the plan's GC-dip robustness requirement).
    /// </summary>
    private double SlopeMbPerMin(long sinceUnixMs)
    {
        double n = 0, sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (int i = 0; i < _count; i++)
        {
            int idx = _head - 1 - i;
            if (idx < 0) idx += Capacity;
            long t = _unixMs[idx];
            if (t < sinceUnixMs) break; // ring walks newest → oldest
            double x = t / 60000d;                    // minutes
            double y = _wsBytes[idx] / (1024d * 1024d); // MB
            n++; sumX += x; sumY += y; sumXY += x * y; sumXX += x * x;
        }
        if (n < 2) return 0d;
        double denom = n * sumXX - sumX * sumX;
        if (Math.Abs(denom) < 1e-9) return 0d;
        return (n * sumXY - sumX * sumY) / denom;
    }

    /// <summary>
    /// Copy the series (oldest → newest), downsampled by stride to at most
    /// <paramref name="maxPoints"/> points, into fresh arrays for the wire.
    /// Poll-cadence use only (~0.5 Hz); the allocation is the payload's.
    /// </summary>
    public (long[] unixMs, double[] wsMb, double[] managedMb) CopySeries(int maxPoints)
    {
        lock (_gate)
        {
            if (_count == 0 || maxPoints <= 0)
            {
                return (Array.Empty<long>(), Array.Empty<double>(), Array.Empty<double>());
            }
            int stride = _count <= maxPoints ? 1 : (_count + maxPoints - 1) / maxPoints;
            int outCount = (_count + stride - 1) / stride;
            var t = new long[outCount];
            var ws = new double[outCount];
            var mg = new double[outCount];
            int oldest = _count == Capacity ? _head : 0;
            for (int o = 0; o < outCount; o++)
            {
                int i = o * stride;
                int idx = oldest + i;
                if (idx >= Capacity) idx -= Capacity;
                t[o] = _unixMs[idx];
                ws[o] = _wsBytes[idx] / (1024d * 1024d);
                mg[o] = _managedBytes[idx] / (1024d * 1024d);
            }
            return (t, ws, mg);
        }
    }

    /// <summary>The phase table. Pure; pinned directly by the tests.</summary>
    public static MemoryTrendPhase Classify(double slopeMbPerMin, bool sawGrowthBefore)
    {
        if (slopeMbPerMin <= -FlatMbPerMin && sawGrowthBefore) return MemoryTrendPhase.Reclaimed;
        double abs = Math.Abs(slopeMbPerMin);
        if (abs < FlatMbPerMin) return MemoryTrendPhase.Flat;
        if (slopeMbPerMin >= ClimbingMbPerMin) return MemoryTrendPhase.Climbing;
        if (slopeMbPerMin > 0d) return MemoryTrendPhase.Growing;
        return MemoryTrendPhase.Flat; // negative but never grew: treat as flat, not "reclaimed"
    }
}
