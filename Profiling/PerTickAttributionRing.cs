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

    // Two cooperating counters. _writeCount is the ring's own monotonic counter
    // (0, 1, 2, ...) used for slot arithmetic and the warmup test "have we even
    // written N rows yet". _lastGameTick is the most recent game-tick index
    // (Main.GameUpdateCount) we were handed at Push time, so lookups by game
    // tick can validate whether the requested tick falls in our retention
    // window. They are unrelated magnitudes -- _writeCount counts from zero,
    // _lastGameTick can be in the millions on a long-running world. Keeping
    // both is the only correct way to support both "give me the most recent
    // row" (uses _writeCount) and "give me row at game tick T" (uses
    // _lastGameTick + slot modulo).
    private long _writeCount;
    private long _lastGameTick = -1L;

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

    /// <summary>The game-tick index of the most recently written row, or -1 if empty.</summary>
    public long LastGameTick => _lastGameTick;

    /// <summary>True if the ring is sized to hold allocation columns too.</summary>
    public bool TracksAllocations => _perModBytes != null;

    /// <summary>
    /// Writes one tick's row from a harvest of per-mod-category ms (and optional
    /// bytes). Called once per tick from <see cref="MetricCollector.EndTick"/>
    /// after the smoothing pass; allocation-free. <paramref name="gameTick"/>
    /// is the game's tick index (Main.GameUpdateCount) for this row; lookups
    /// by game tick validate against it.
    /// </summary>
    /// <param name="gameTick">Game-tick index for this row.</param>
    /// <param name="perModCatMs">[modId * <see cref="PerModAttribution.CategoryCount"/> + catId] ms values for this tick.</param>
    /// <param name="perModCatBytes">Parallel byte values; pass null if allocation tracking is off.</param>
    public void Push(long gameTick, double[] perModCatMs, double[]? perModCatBytes)
    {
        int catCount = PerModAttribution.CategoryCount;
        // Slot from the ring's own monotonic counter so wrap-around behaves
        // regardless of how the game's tick counter is sourced. The game tick
        // is stored alongside for lookup validation, not used for slot math.
        int tickSlot = (int)(_writeCount % _historyTicks);
        int catTickSlot = (int)(_writeCount % _categorySnapshotTicks);

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

        _lastGameTick = gameTick;
        _writeCount++;
    }

    /// <summary>
    /// Returns the per-mod ms total at <paramref name="gameTick"/>, or 0 if
    /// the tick is outside the retained window or before the ring was populated.
    /// </summary>
    public float GetPerModMs(long gameTick, int modId)
    {
        long ago = _lastGameTick - gameTick;
        if (_lastGameTick < 0 || ago < 0 || ago >= _historyTicks) return 0f;
        if ((uint)modId >= (uint)_modCount) return 0f;
        // Walk back from the most recently written slot; the ring is decoupled
        // from game-tick magnitude so we use the ring's own write counter.
        long latestSlot = (_writeCount - 1) % _historyTicks;
        long slot = (latestSlot - ago + _historyTicks) % _historyTicks;
        return _perModMs[(int)slot * _modCount + modId];
    }

    /// <summary>
    /// Returns the per-mod allocation bytes total at <paramref name="gameTick"/>,
    /// or 0 if the tick is outside the retained window or tracking is off.
    /// </summary>
    public float GetPerModBytes(long gameTick, int modId)
    {
        if (_perModBytes == null) return 0f;
        long ago = _lastGameTick - gameTick;
        if (_lastGameTick < 0 || ago < 0 || ago >= _historyTicks) return 0f;
        if ((uint)modId >= (uint)_modCount) return 0f;
        long latestSlot = (_writeCount - 1) % _historyTicks;
        long slot = (latestSlot - ago + _historyTicks) % _historyTicks;
        return _perModBytes[(int)slot * _modCount + modId];
    }

    /// <summary>
    /// Copies the MOST RECENTLY WRITTEN per-mod-per-category row into the
    /// destination spans. Decouples the snapshot from any game-tick-vs-ring
    /// counter ambiguity. The spike detector calls this immediately after
    /// the corresponding <see cref="Push"/>, so "most recently written" is
    /// always the data for the tick the detector is reasoning about.
    /// </summary>
    public void CopyLatestCategorySnapshot(Span<float> destinationMs, Span<float> destinationBytes)
    {
        if (_writeCount == 0) return;
        int catCount = PerModAttribution.CategoryCount;
        int latestSlot = (int)((_writeCount - 1) % _categorySnapshotTicks);
        int baseIdx = latestSlot * _modCount * catCount;
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
    }

    /// <summary>
    /// Copies the per-mod-per-category snapshot for <paramref name="gameTick"/>
    /// into the destination spans. Returns false if the tick is outside the
    /// category snapshot window.
    /// </summary>
    /// <remarks>
    /// The category snapshot window is smaller than the full history window --
    /// a spike from 25 seconds ago still has its per-mod totals via
    /// <see cref="GetPerModMs"/>, but the per-category breakdown is only kept
    /// for the last <c>categorySnapshotTicks</c> ticks (~2s default). The
    /// detector captures the snapshot at spike-detection time via
    /// <see cref="CopyLatestCategorySnapshot"/> so it's frozen before the
    /// window can age out.
    /// </remarks>
    public bool TryGetCategorySnapshot(long gameTick, Span<float> destinationMs, Span<float> destinationBytes)
    {
        long ago = _lastGameTick - gameTick;
        if (_lastGameTick < 0 || ago < 0 || ago >= _categorySnapshotTicks) return false;

        int catCount = PerModAttribution.CategoryCount;
        long latestSlot = (_writeCount - 1) % _categorySnapshotTicks;
        long slot = (latestSlot - ago + _categorySnapshotTicks) % _categorySnapshotTicks;
        int baseIdx = (int)slot * _modCount * catCount;
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
