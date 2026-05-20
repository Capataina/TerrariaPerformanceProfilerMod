#nullable enable

using System;
using System.Collections.Generic;

namespace PerformanceProfiler.Profiling;

/// <summary>How the impact leaderboard is currently sorted.</summary>
internal enum ImpactSortMode { Composite = 0, Cpu = 1, Spike = 2, Alloc = 3 }

/// <summary>Absolute pain band a mod sits in, against the same thresholds for every modlist.</summary>
internal enum ImpactBand { Green = 0, Amber = 1, Red = 2 }

/// <summary>
/// One mod's row in the Overview leaderboard: the composite ms-equivalent
/// total alongside its three constituent components. The values are absolute
/// (ms units throughout), so a row stays meaningful when |mods| = 1 and bands
/// are graded against the same global thresholds for every modlist.
///
/// <para>
/// The composite is intentionally additive: <c>Composite = CpuMs + SpikeMs +
/// AllocMsEq</c>. No weights are applied at v1 — each component arrives in
/// ms-equivalent already, and the design contract is that the user always
/// sees all three sub-bars beside the composite so a misleading rank cannot
/// hide the underlying numbers (see overview-tab-plan.md §4.2).
/// </para>
/// </summary>
internal readonly struct ModImpact
{
    public readonly int ModId;
    public readonly double Composite;
    public readonly double CpuMs;
    public readonly double SpikeMs;
    public readonly double AllocMsEq;
    public readonly double AllocBytesPerTick;
    public readonly double ShareOfTotal;
    public readonly ImpactBand Band;

    public ModImpact(int modId, double cpuMs, double spikeMs, double allocMsEq,
        double allocBytesPerTick, double shareOfTotal)
    {
        ModId = modId;
        CpuMs = cpuMs;
        SpikeMs = spikeMs;
        AllocMsEq = allocMsEq;
        AllocBytesPerTick = allocBytesPerTick;
        Composite = cpuMs + spikeMs + allocMsEq;
        ShareOfTotal = shareOfTotal;
        Band = Composite < ImpactBands.GreenMaxMs ? ImpactBand.Green
             : Composite < ImpactBands.AmberMaxMs ? ImpactBand.Amber
             : ImpactBand.Red;
    }
}

/// <summary>Absolute composite-band thresholds. See overview-tab-plan.md §8.3.</summary>
internal static class ImpactBands
{
    /// <summary>Below this a row reads "objectively fine" — invisible at 60 fps.</summary>
    public const double GreenMaxMs = 1.0d;

    /// <summary>Below this, "noticeable but not painful". Above, the row turns red.</summary>
    public const double AmberMaxMs = 4.0d;

    /// <summary>The "concerning mods only" filter threshold; rows below this collapse into a summary row.</summary>
    public const double MinInterestingMs = 0.5d;
}

/// <summary>
/// Reads <see cref="MetricCollector"/>'s already-smoothed per-mod cost vectors,
/// reduces each mod to a single ms-equivalent composite via the formula in
/// overview-tab-plan.md §4, and sorts the results. Pure logic with no UI or
/// tModLoader dependency — unit-testable per CLAUDE.md.
///
/// <para>
/// <b>Cadence.</b> <see cref="Recompute"/> is called from the Overview tab's
/// per-frame <c>Tick</c>, but the actual recompute is gated to 1 Hz (60 ticks)
/// so the leaderboard does not reshuffle every frame. Inputs are already
/// rolling-averaged across 30 s, so 1 Hz is plenty.
/// </para>
///
/// <para>
/// <b>Allocations.</b> Both result buffers (<c>_impactsByMod</c> and the sorted
/// view) are pre-allocated, sized to the maximum mod count seen so far. A
/// recompute writes into those buffers in-place, so steady-state operation is
/// allocation-free (Invariant 2). The buffers grow only when a new mod
/// appears, which never happens after the first tick of a session.
/// </para>
///
/// <para>
/// <b>Calibration.</b> The allocation-to-ms conversion is the only piece of
/// numerical heuristic in the scorer. It is derived from the player's own
/// session: <c>gcMsPerByte = Σ GcTimeMs / Σ allocBytes</c> over the rolling
/// window. If the window holds no GC events (a quiet session), the scorer
/// falls back to <see cref="FallbackGcMsPerByte"/>. The live value is exposed
/// via <see cref="GcMsPerByte"/> so the UI can render it in the calibration
/// footer (overview-tab-plan.md §5.3, §5.4).
/// </para>
/// </summary>
internal sealed class ModImpactScorer
{
    /// <summary>
    /// Fallback ms-equivalent cost per allocated byte, used when the rolling
    /// window contains no observed GC events. Derived from the Microsoft
    /// background-GC blog post: a small Gen0 collection on a modern CPU is
    /// ~0.5 ms and clears ~5 MB, giving ~1e-7 ms/byte. See
    /// overview-tab-plan.md §5.3.
    /// </summary>
    public const double FallbackGcMsPerByte = 1e-7d;

    /// <summary>
    /// Minimum number of history ticks before allocation contribution is
    /// surfaced at all. Below this the smoothing has not converged and the
    /// alloc column stays greyed out; the row's composite collapses to CPU
    /// (+ spike, when that lands).
    /// </summary>
    public const int MinCalibrationTicks = 300; // ~5 s at 60 tps

    /// <summary>Recompute cadence, in ticks. 60 ticks = 1 Hz on Terraria's main loop.</summary>
    public const long RecomputeIntervalTicks = 60L;

    private ModImpact[] _impactsByMod = Array.Empty<ModImpact>();
    private ModImpact[] _sorted = Array.Empty<ModImpact>();
    private int _modCount;
    private double _gcMsPerByte = FallbackGcMsPerByte;
    private bool _gcCalibratedFromHistory;
    private int _calibrationSamples;
    private long _lastComputeTick = -RecomputeIntervalTicks;
    private bool _dirty = true;
    private ImpactSortMode _lastSortMode = ImpactSortMode.Composite;

    /// <summary>The sorted view of the most recent <see cref="Recompute"/>.</summary>
    public IReadOnlyList<ModImpact> Sorted => new ArraySegment<ModImpact>(_sorted, 0, _modCount);

    /// <summary>Number of mods currently in the leaderboard.</summary>
    public int Count => _modCount;

    /// <summary>The live alloc-to-ms conversion factor, in ms per byte.</summary>
    public double GcMsPerByte => _gcMsPerByte;

    /// <summary>True if <see cref="GcMsPerByte"/> was derived from observed history, false if it is the fallback constant.</summary>
    public bool GcCalibratedFromHistory => _gcCalibratedFromHistory;

    /// <summary>Number of history ticks that contributed to the latest calibration.</summary>
    public int CalibrationSamples => _calibrationSamples;

    /// <summary>True once the rolling window holds enough samples to surface allocation contribution.</summary>
    public bool IsCalibrated => _calibrationSamples >= MinCalibrationTicks;

    /// <summary>Forces the next <see cref="Recompute"/> call to do real work, even if the cadence guard would skip.</summary>
    public void MarkDirty() => _dirty = true;

    /// <summary>
    /// Refreshes the leaderboard from the collector's smoothed vectors. The
    /// 1 Hz cadence guard short-circuits the call when <paramref name="currentTick"/>
    /// has not advanced enough, unless <see cref="MarkDirty"/> or a sort-mode
    /// change forces a recompute. Safe to call every frame.
    /// </summary>
    public void Recompute(MetricCollector collector, ImpactSortMode sortMode, bool descending, long currentTick)
    {
        bool sortChanged = sortMode != _lastSortMode;
        if (!_dirty && !sortChanged && currentTick - _lastComputeTick < RecomputeIntervalTicks)
        {
            return;
        }

        _lastComputeTick = currentTick;
        _lastSortMode = sortMode;
        _dirty = false;

        UpdateCalibration(collector);

        IReadOnlyList<double> cpuView = collector.PerModCategoryAverageMs;
        IReadOnlyList<double>? bytesView = collector.PerModCategoryAverageBytes;
        ComputeImpacts(cpuView, bytesView);
        Sort(sortMode, descending);
    }

    /// <summary>
    /// Pure functional core: builds <see cref="ModImpact"/> rows from the
    /// supplied vectors using the current <see cref="GcMsPerByte"/>. Exposed
    /// so unit tests can drive the scorer without a live <see cref="MetricCollector"/>.
    /// </summary>
    public void ComputeImpacts(IReadOnlyList<double> perModCategoryMs, IReadOnlyList<double>? perModCategoryBytes)
    {
        int catCount = PerModAttribution.CategoryCount;
        if (catCount <= 0) { _modCount = 0; return; }

        int modCount = perModCategoryMs.Count / catCount;
        if (modCount <= 0) { _modCount = 0; return; }

        EnsureCapacity(modCount);

        double allocMsActive = IsCalibrated ? _gcMsPerByte : 0d;

        double compositeTotal = 0d;
        for (int mod = 0; mod < modCount; mod++)
        {
            double cpuMs = 0d;
            double allocBytesPerTick = 0d;
            int baseIdx = mod * catCount;
            for (int c = 0; c < catCount; c++)
            {
                int cell = baseIdx + c;
                if (cell < perModCategoryMs.Count)
                {
                    double v = perModCategoryMs[cell];
                    if (!double.IsFinite(v) || v < 0d) v = 0d;
                    cpuMs += v;
                }
                if (perModCategoryBytes != null && cell < perModCategoryBytes.Count)
                {
                    double b = perModCategoryBytes[cell];
                    if (!double.IsFinite(b) || b < 0d) b = 0d;
                    allocBytesPerTick += b;
                }
            }

            double allocMsEq = allocBytesPerTick * allocMsActive;
            // Spike contribution is 0 until the sibling SpikeTracker lands; the
            // composite collapses cleanly per overview-tab-plan.md §13.
            double spikeMs = 0d;

            _impactsByMod[mod] = new ModImpact(mod, cpuMs, spikeMs, allocMsEq, allocBytesPerTick, 0d);
            compositeTotal += cpuMs + spikeMs + allocMsEq;
        }

        // Second pass to record share-of-total now that the denominator is known.
        if (compositeTotal > 0d)
        {
            for (int mod = 0; mod < modCount; mod++)
            {
                ref ModImpact src = ref _impactsByMod[mod];
                _impactsByMod[mod] = new ModImpact(
                    src.ModId, src.CpuMs, src.SpikeMs, src.AllocMsEq, src.AllocBytesPerTick,
                    src.Composite / compositeTotal);
            }
        }

        _modCount = modCount;
    }

    private void Sort(ImpactSortMode mode, bool descending)
    {
        // Copy into the sorted view, then in-place sort that view. Insertion
        // sort is fine: |mods| is small (typically 10-100) and 1 Hz cadence
        // makes the constant factor irrelevant.
        for (int i = 0; i < _modCount; i++) _sorted[i] = _impactsByMod[i];

        for (int i = 1; i < _modCount; i++)
        {
            ModImpact key = _sorted[i];
            double keyV = ValueFor(in key, mode);
            int j = i - 1;
            while (j >= 0)
            {
                double otherV = ValueFor(in _sorted[j], mode);
                bool swap = descending ? otherV < keyV : otherV > keyV;
                if (!swap) break;
                _sorted[j + 1] = _sorted[j];
                j--;
            }
            _sorted[j + 1] = key;
        }
    }

    private static double ValueFor(in ModImpact m, ImpactSortMode mode) => mode switch
    {
        ImpactSortMode.Cpu => m.CpuMs,
        ImpactSortMode.Spike => m.SpikeMs,
        ImpactSortMode.Alloc => m.AllocMsEq,
        _ => m.Composite,
    };

    private void EnsureCapacity(int modCount)
    {
        if (_impactsByMod.Length < modCount)
        {
            _impactsByMod = new ModImpact[modCount];
            _sorted = new ModImpact[modCount];
        }
    }

    private void UpdateCalibration(MetricCollector collector)
    {
        RingBuffer<TickFrame> history = collector.History;
        IReadOnlyList<double>? bytesView = collector.PerModCategoryAverageBytes;
        _calibrationSamples = history.Count;

        if (bytesView == null || history.Count == 0)
        {
            _gcMsPerByte = FallbackGcMsPerByte;
            _gcCalibratedFromHistory = false;
            return;
        }

        double totalGcMs = 0d;
        for (int i = 0; i < history.Count; i++) totalGcMs += history[i].GcTimeMs;

        // Σ allocBytes across the same window = Σ_mods (avg bytes/tick) * windowTicks.
        // The average view is the same one ComputeImpacts uses, so the calibration
        // and the per-mod attribution stay consistent.
        double totalBytesPerTick = 0d;
        for (int i = 0; i < bytesView.Count; i++)
        {
            double v = bytesView[i];
            if (!double.IsFinite(v) || v < 0d) continue;
            totalBytesPerTick += v;
        }
        double totalBytes = totalBytesPerTick * history.Count;

        if (totalBytes <= 0d || totalGcMs <= 0d)
        {
            _gcMsPerByte = FallbackGcMsPerByte;
            _gcCalibratedFromHistory = false;
            return;
        }

        _gcMsPerByte = totalGcMs / totalBytes;
        _gcCalibratedFromHistory = true;
    }
}
