#nullable enable

using System;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// Raw per-tick per-mod attribution, retained for spike drill-down.
///
/// <para>
/// <see cref="MetricCollector"/> smooths and 30-second-averages the live signal
/// so the overlay tree is stable. That's correct for normal viewing, useless for
/// "what did Mod X look like at the exact tick of the 32 ms spike?" — by the
/// time the user clicks into the spike, the smoothed values have already moved
/// past it. This ring keeps the **unsmoothed** per-mod totals tick-by-tick so
/// the drill-down can read straight from the moment of the spike.
/// </para>
///
/// <para>
/// Two retention windows of different size:
/// </para>
/// <list type="bullet">
///   <item><b>Per-mod totals</b> at every tick for the full 30-second history
///         (1800 ticks @ 60 tps). Cheap (one float per mod per tick).</item>
///   <item><b>Per-mod-per-category snapshots</b> only for the last ~2 seconds.
///         A spike drill-down needs the category breakdown for the worst tick;
///         we never need 30 s of category data, so the larger array stays small.</item>
/// </list>
///
/// <para>
/// Memory budget at default sizing (18 mods, 1800 ticks, 120 cat-snapshot ticks,
/// CPU + alloc tracked): ~860 KB. At a 200-mod nightmare modlist the budget tops
/// at ~4.2 MB which is still inside the hard ceiling. The category snapshot window
/// is the tunable knob if pressure shows up.
/// </para>
///
/// <para>
/// Invariant 2 (overhead budget): <see cref="Push"/> is a tight loop with no
/// allocation. Float conversion costs nanoseconds; we accept that loss vs. the
/// memory savings of float over double (50% reduction on the dominant array).
/// </para>
/// </summary>
public sealed class PerTickAttributionRing
{
    private readonly float[] _perModMs;           // [tickSlot * modCount + modId]
    private readonly float[]? _perModBytes;       // null when allocation tracking is off

    private readonly float[] _perModCatMs;        // [catTickSlot * (modCount * catCount) + modId*catCount + catId]
    private readonly float[]? _perModCatBytes;    // null when allocation tracking is off

    private readonly int _modCount;
    private readonly int _historyTicks;
    private readonly int _categorySnapshotTicks;

    // Monotonic tick counter. Used directly (mod _historyTicks) when writing.
    // Reads expose the most recently written tick via CurrentTickIndex.
    private long _writeTick;

    /// <summary>
    /// Builds a ring sized for the given mod count and retention windows.
    /// <paramref name="trackAllocations"/> sizes (or skips) the parallel byte arrays.
    /// </summary>
    public PerTickAttributionRing(int modCount, int historyTicks, int categorySnapshotTicks, bool trackAllocations)
    {
        _modCount = modCount;
        _historyTicks = historyTicks;
        _categorySnapshotTicks = categorySnapshotTicks;

        _perModMs = new float[modCount * historyTicks];
        int catCount = PerModAttribution.CategoryCount;
        _perModCatMs = new float[modCount * catCount * categorySnapshotTicks];

        if (trackAllocations)
        {
            _perModBytes = new float[modCount * historyTicks];
            _perModCatBytes = new float[modCount * catCount * categorySnapshotTicks];
        }
    }

    /// <summary>The monotonic tick index of the most recently written row, or -1 if empty.</summary>
    public long CurrentTickIndex => _writeTick - 1;

    /// <summary>True if the ring is sized to hold allocation columns too.</summary>
    public bool TracksAllocations => _perModBytes != null;

    /// <summary>
    /// Writes one tick's row from a harvest of per-mod-category ms (and optional
    /// bytes). Called once per tick from <see cref="MetricCollector.EndTick"/>
    /// after the smoothing pass; allocation-free.
    /// </summary>
    /// <param name="perModCatMs">[modId * <see cref="PerModAttribution.CategoryCount"/> + catId] ms values for this tick.</param>
    /// <param name="perModCatBytes">Parallel byte values; pass null if allocation tracking is off.</param>
    public void Push(double[] perModCatMs, double[]? perModCatBytes)
    {
        int catCount = PerModAttribution.CategoryCount;
        int tickSlot = (int)(_writeTick % _historyTicks);
        int catTickSlot = (int)(_writeTick % _categorySnapshotTicks);

        int byTickBase = tickSlot * _modCount;
        int byCatTickBase = catTickSlot * _modCount * catCount;
        bool trackBytes = _perModBytes != null && perModCatBytes != null;

        for (int mod = 0; mod < _modCount; mod++)
        {
            float modTotalMs = 0f;
            float modTotalBytes = 0f;
            int catBase = mod * catCount;

            for (int c = 0; c < catCount; c++)
            {
                int cell = catBase + c;
                float ms = (float)perModCatMs[cell];
                _perModCatMs[byCatTickBase + cell] = ms;
                modTotalMs += ms;

                if (trackBytes)
                {
                    float b = (float)perModCatBytes![cell];
                    _perModCatBytes![byCatTickBase + cell] = b;
                    modTotalBytes += b;
                }
            }

            _perModMs[byTickBase + mod] = modTotalMs;
            if (trackBytes)
            {
                _perModBytes![byTickBase + mod] = modTotalBytes;
            }
        }

        _writeTick++;
    }

    /// <summary>
    /// Returns the per-mod ms total at <paramref name="tickIndex"/>, or 0 if the
    /// tick is outside the retained window or before the ring was populated.
    /// </summary>
    public float GetPerModMs(long tickIndex, int modId)
    {
        long ago = _writeTick - 1 - tickIndex;
        if (ago < 0 || ago >= _historyTicks) return 0f;
        if ((uint)modId >= (uint)_modCount) return 0f;
        int slot = (int)(tickIndex % _historyTicks);
        return _perModMs[slot * _modCount + modId];
    }

    /// <summary>
    /// Returns the per-mod allocation bytes total at <paramref name="tickIndex"/>,
    /// or 0 if the tick is outside the retained window or tracking is off.
    /// </summary>
    public float GetPerModBytes(long tickIndex, int modId)
    {
        if (_perModBytes == null) return 0f;
        long ago = _writeTick - 1 - tickIndex;
        if (ago < 0 || ago >= _historyTicks) return 0f;
        if ((uint)modId >= (uint)_modCount) return 0f;
        int slot = (int)(tickIndex % _historyTicks);
        return _perModBytes[slot * _modCount + modId];
    }

    /// <summary>
    /// Copies the per-mod-per-category snapshot for <paramref name="tickIndex"/>
    /// into <paramref name="destinationMs"/> and (when allocation tracking is on)
    /// <paramref name="destinationBytes"/>. Returns false if the tick is outside
    /// the category snapshot window.
    /// </summary>
    /// <remarks>
    /// The category snapshot window is smaller than the full history window —
    /// a spike from 25 seconds ago will still have a per-mod-total in
    /// <see cref="GetPerModMs"/>, but the per-category breakdown is only kept
    /// for the last <c>categorySnapshotTicks</c> ticks (default ~2s). The
    /// detector captures the snapshot at spike-detection time so we never lose it.
    /// </remarks>
    public bool TryGetCategorySnapshot(long tickIndex, Span<float> destinationMs, Span<float> destinationBytes)
    {
        long ago = _writeTick - 1 - tickIndex;
        if (ago < 0 || ago >= _categorySnapshotTicks) return false;

        int catCount = PerModAttribution.CategoryCount;
        int slot = (int)(tickIndex % _categorySnapshotTicks);
        int baseIdx = slot * _modCount * catCount;
        int n = _modCount * catCount;

        int copyMs = n < destinationMs.Length ? n : destinationMs.Length;
        for (int i = 0; i < copyMs; i++)
        {
            destinationMs[i] = _perModCatMs[baseIdx + i];
        }

        if (_perModCatBytes != null && destinationBytes.Length > 0)
        {
            int copyBytes = n < destinationBytes.Length ? n : destinationBytes.Length;
            for (int i = 0; i < copyBytes; i++)
            {
                destinationBytes[i] = _perModCatBytes[baseIdx + i];
            }
        }
        return true;
    }
}
