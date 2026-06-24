#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Insights.Shared;

namespace PerformanceProfiler.Insights.ReferenceFrames;

/// <summary>
/// The Family-B (behaviour-over-time) substrate: splits the session into an EARLY
/// window (frozen after the first <see cref="EarlySamples"/> 1 Hz samples) and a
/// LATE window (everything after), accumulating heap, entity count, and per-mod cost
/// in each. A detector compares late against early to see what drifted — and, by
/// carrying the entity-count distribution alongside, can control for workload so a
/// heap rise that merely tracks more entities is read as progression, not a leak
/// (the temporal confound the plan forbids).
///
/// <para>
/// Fed once per engine evaluation (1 Hz, off-thread), so no per-tick cost. Pure logic
/// over declared input — the L1 test axis.
/// </para>
/// </summary>
public sealed class TemporalBaseline
{
    /// <summary>1 Hz samples that form the frozen early baseline (~2 minutes).</summary>
    public const long EarlySamples = 120;

    /// <summary>Minimum samples each side needs before a comparison is a candidate.</summary>
    public const long MinPerSide = 60;

    private readonly int _modCount;
    private long _samples;

    private RunningStat _earlyHeap, _lateHeap;
    private RunningStat _earlyEntities, _lateEntities;
    private readonly RunningStat[] _earlyPerMod;
    private readonly RunningStat[] _latePerMod;

    public TemporalBaseline(int modCount)
    {
        _modCount = modCount < 0 ? 0 : modCount;
        _earlyPerMod = new RunningStat[_modCount];
        _latePerMod = new RunningStat[_modCount];
    }

    public int ModCount => _modCount;
    public long Samples => _samples;

    /// <summary>True once both windows have enough samples to compare.</summary>
    public bool IsReady => _earlyHeap.Count >= MinPerSide && _lateHeap.Count >= MinPerSide;

    public RunningStat EarlyHeap => _earlyHeap;
    public RunningStat LateHeap => _lateHeap;
    public RunningStat EarlyEntities => _earlyEntities;
    public RunningStat LateEntities => _lateEntities;

    /// <summary>Folds one 1 Hz sample into the early or late window by sample index.</summary>
    public void Observe(double heapMB, double entityCount, IReadOnlyList<double> perModCost)
    {
        _samples++;
        int n = _modCount < perModCost.Count ? _modCount : perModCost.Count;
        if (_samples <= EarlySamples)
        {
            _earlyHeap.Add(heapMB);
            _earlyEntities.Add(entityCount);
            for (int m = 0; m < n; m++) _earlyPerMod[m].Add(perModCost[m]);
        }
        else
        {
            _lateHeap.Add(heapMB);
            _lateEntities.Add(entityCount);
            for (int m = 0; m < n; m++) _latePerMod[m].Add(perModCost[m]);
        }
    }

    /// <summary>The early and late cost distributions for a mod, or false when out of range / not ready.</summary>
    public bool TryPerMod(int modId, out RunningStat early, out RunningStat late)
    {
        early = default;
        late = default;
        if ((uint)modId >= (uint)_modCount) return false;
        early = _earlyPerMod[modId];
        late = _latePerMod[modId];
        return early.Count >= MinPerSide && late.Count >= MinPerSide;
    }
}
