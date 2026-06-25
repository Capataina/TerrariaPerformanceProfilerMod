#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data.Aggregators;

/// <summary>
/// Stable identifiers for the six dimensions the Events tab aggregates over.
/// Used as the integer prefix on bucket keys exposed to the UI so a single
/// flat row list can carry rows from every dimension without an extra wrapper.
/// </summary>
internal enum EventDimension
{
    Biome    = 0,
    Weather  = 1,
    Boss     = 2,
    Invasion = 3,
    Difficulty = 4,
    Subworld = 5,
}

/// <summary>
/// One row's worth of fully-resolved bucket data ready for rendering:
/// dimension label, display name, stats, and the int key used for stable
/// lookups inside the dimension. Pure data; produced on demand by
/// <see cref="EventAggregator.SnapshotRows"/>.
/// </summary>
internal readonly struct EventBucketRow
{
    public readonly EventDimension Dimension;
    public readonly int Key;
    public readonly string DisplayName;
    public readonly BucketStats Stats;
    public readonly bool Active;

    public EventBucketRow(EventDimension dim, int key, string displayName, BucketStats stats, bool active)
    {
        Dimension = dim;
        Key = key;
        DisplayName = displayName;
        Stats = stats;
        Active = active;
    }
}

/// <summary>
/// Per-dimension bucket aggregator. A tick contributes its frame-time to
/// every currently-active bucket in every dimension in parallel — biomes,
/// weather bits, bosses, invasion, difficulty, subworld. Cross-dimensional
/// queries (cost in Jungle AND Blood Moon) are computed on demand by
/// re-walking the ring buffer; never stored.
///
/// <para>
/// Per plan §8: per-dimension dictionaries, no Cartesian product. Spike
/// classification uses a session running mean (incrementally updated)
/// against the 2× threshold; matches the project's existing spike posture
/// while staying free of any dependency on <see cref="SpikeDetector"/>.
/// </para>
/// </summary>
internal sealed class EventAggregator
{
    private readonly Dictionary<int, BucketStats>[] _byDim;
    private readonly HashSet<int>[] _activeKeysLastTick;

    private double _runningMeanMs;
    private long _totalTicks;

    /// <summary>Internal copy of the most recent context, used to flag "active now" rows in snapshots.</summary>
    private EventContext _lastContext;
    private bool _hasContext;

    public EventAggregator()
    {
        int dimCount = System.Enum.GetValues<EventDimension>().Length;
        _byDim = new Dictionary<int, BucketStats>[dimCount];
        _activeKeysLastTick = new HashSet<int>[dimCount];
        for (int i = 0; i < dimCount; i++)
        {
            _byDim[i] = new Dictionary<int, BucketStats>();
            _activeKeysLastTick[i] = new HashSet<int>();
        }
    }

    /// <summary>Total frames recorded across the session. Useful for the now-active header readout.</summary>
    public long TotalTicks => _totalTicks;

    /// <summary>Session running mean frame time, in milliseconds. Used to colour-grade bucket rows.</summary>
    public double SessionAverageMs => _runningMeanMs;

    /// <summary>The most recent context snapshot, or default if none yet. Read-only; tabs only consume.</summary>
    public ref readonly EventContext Latest => ref _lastContext;

    /// <summary>True once at least one tick has been recorded.</summary>
    public bool HasContext => _hasContext;

    /// <summary>
    /// PER-TICK HOT PATH (Invariant 2) — no allocation, no interface dispatch
    /// in steady state. Driven directly each tick rather than through the
    /// registry's Cadence-gated loop, so this banner is the contract.
    /// Accumulates one tick's frame time into every dimension's active
    /// buckets. Allocation only fires the first time a new bucket appears
    /// (dictionary growth + one <see cref="BucketStats"/>); steady state is
    /// allocation-free.
    /// </summary>
    public void Accumulate(in EventContext ctx, double frameMs)
    {
        _totalTicks++;
        // Running-mean update: equivalent to streaming average.
        _runningMeanMs += (frameMs - _runningMeanMs) / _totalTicks;
        bool isSpike = _totalTicks > 60 && frameMs > _runningMeanMs * 2d;

        // Track per-dimension active key sets for the "active now" badge.
        for (int d = 0; d < _activeKeysLastTick.Length; d++) _activeKeysLastTick[d].Clear();

        // ---- Biomes -------------------------------------------------------
        int biomeCount = BiomeRegistry.Count;
        for (int i = 0; i < biomeCount; i++)
        {
            if (!ctx.Biomes.IsSet(i)) continue;
            BumpBucket(EventDimension.Biome, i, ctx.TickIndex, frameMs, isSpike);
            _activeKeysLastTick[(int)EventDimension.Biome].Add(i);
        }

        // ---- Weather flags ------------------------------------------------
        WeatherFlags w = ctx.Weather;
        foreach (var pair in WeatherSources.All)
        {
            if ((w & pair.Flag) == 0) continue;
            int key = (int)pair.Flag;
            BumpBucket(EventDimension.Weather, key, ctx.TickIndex, frameMs, isSpike);
            _activeKeysLastTick[(int)EventDimension.Weather].Add(key);
        }

        // ---- Bosses -------------------------------------------------------
        BossSlotArray bosses = ctx.Bosses;
        int bossCount = bosses.Count;
        for (int i = 0; i < bossCount; i++)
        {
            int type = bosses[i];
            if (type == 0) continue;
            BumpBucket(EventDimension.Boss, type, ctx.TickIndex, frameMs, isSpike);
            _activeKeysLastTick[(int)EventDimension.Boss].Add(type);
        }

        // ---- Invasion -----------------------------------------------------
        if (ctx.VanillaInvasion != InvasionId.None)
        {
            int key = (int)ctx.VanillaInvasion;
            BumpBucket(EventDimension.Invasion, key, ctx.TickIndex, frameMs, isSpike);
            _activeKeysLastTick[(int)EventDimension.Invasion].Add(key);
        }

        // ---- Difficulty / Hardmode ----------------------------------------
        // One row per active difficulty, plus an implicit Hardmode/Pre-Hardmode
        // row so a session that crosses the threshold splits cleanly.
        int modeKey = (int)ctx.Mode;
        BumpBucket(EventDimension.Difficulty, modeKey, ctx.TickIndex, frameMs, isSpike);
        _activeKeysLastTick[(int)EventDimension.Difficulty].Add(modeKey);
        int hardKey = ctx.Hardmode ? 100 : 101;
        BumpBucket(EventDimension.Difficulty, hardKey, ctx.TickIndex, frameMs, isSpike);
        _activeKeysLastTick[(int)EventDimension.Difficulty].Add(hardKey);

        // ---- Subworld -----------------------------------------------------
        if (ctx.SubworldKey > 0)
        {
            BumpBucket(EventDimension.Subworld, ctx.SubworldKey, ctx.TickIndex, frameMs, isSpike);
            _activeKeysLastTick[(int)EventDimension.Subworld].Add(ctx.SubworldKey);
        }

        _lastContext = ctx;
        _hasContext = true;
    }

    private void BumpBucket(EventDimension dim, int key, long tickIndex, double frameMs, bool isSpike)
    {
        Dictionary<int, BucketStats> map = _byDim[(int)dim];
        if (!map.TryGetValue(key, out BucketStats? stats))
        {
            stats = new BucketStats();
            map[key] = stats;
        }
        stats.Add(tickIndex, frameMs, isSpike);
    }

    /// <summary>True if the dimension's bucket for <paramref name="key"/> was active on the last tick.</summary>
    public bool IsActiveNow(EventDimension dim, int key)
    {
        return _activeKeysLastTick[(int)dim].Contains(key);
    }

    /// <summary>The unsorted bucket dictionary for one dimension. UI sorts on top.</summary>
    public IReadOnlyDictionary<int, BucketStats> BucketsFor(EventDimension dim) => _byDim[(int)dim];

    /// <summary>
    /// Materialises every bucket above the minimum-dwell threshold as a
    /// renderable row. The caller (the tab) sorts and slices; this method
    /// only resolves keys to display strings. Allocates a new list each
    /// call — runs on tab refresh, not per-tick, so the allocation is fine.
    /// </summary>
    public List<EventBucketRow> SnapshotRows(int minDwellTicks)
    {
        var rows = new List<EventBucketRow>();
        foreach (var (key, stats) in _byDim[(int)EventDimension.Biome])
        {
            if (stats.Ticks < minDwellTicks) continue;
            string name = key < BiomeRegistry.Biomes.Count ? BiomeRegistry.Biomes[key].DisplayName : "biome:" + key;
            rows.Add(new EventBucketRow(EventDimension.Biome, key, name, stats, IsActiveNow(EventDimension.Biome, key)));
        }
        foreach (var (key, stats) in _byDim[(int)EventDimension.Weather])
        {
            if (stats.Ticks < minDwellTicks) continue;
            rows.Add(new EventBucketRow(EventDimension.Weather, key, WeatherSources.DisplayName((WeatherFlags)key), stats, IsActiveNow(EventDimension.Weather, key)));
        }
        foreach (var (key, stats) in _byDim[(int)EventDimension.Boss])
        {
            if (stats.Ticks < minDwellTicks) continue;
            rows.Add(new EventBucketRow(EventDimension.Boss, key, BossSampler.DisplayName(key), stats, IsActiveNow(EventDimension.Boss, key)));
        }
        foreach (var (key, stats) in _byDim[(int)EventDimension.Invasion])
        {
            if (stats.Ticks < minDwellTicks) continue;
            rows.Add(new EventBucketRow(EventDimension.Invasion, key, InvasionDisplay((InvasionId)key), stats, IsActiveNow(EventDimension.Invasion, key)));
        }
        foreach (var (key, stats) in _byDim[(int)EventDimension.Difficulty])
        {
            if (stats.Ticks < minDwellTicks) continue;
            rows.Add(new EventBucketRow(EventDimension.Difficulty, key, DifficultyDisplay(key), stats, IsActiveNow(EventDimension.Difficulty, key)));
        }
        foreach (var (key, stats) in _byDim[(int)EventDimension.Subworld])
        {
            if (stats.Ticks < minDwellTicks) continue;
            rows.Add(new EventBucketRow(EventDimension.Subworld, key, SubworldProbe.DisplayName(key), stats, IsActiveNow(EventDimension.Subworld, key)));
        }
        return rows;
    }

    private static string InvasionDisplay(InvasionId id) => id switch
    {
        InvasionId.Goblins     => "Goblin Army",
        InvasionId.FrostLegion => "Frost Legion",
        InvasionId.Pirates     => "Pirate Invasion",
        InvasionId.Martians    => "Martian Madness",
        InvasionId.OldOnesArmy => "Old Ones Army",
        _ => id.ToString(),
    };

    private static string DifficultyDisplay(int key) => key switch
    {
        (int)GameMode.Classic  => "Classic",
        (int)GameMode.Expert   => "Expert",
        (int)GameMode.Master   => "Master",
        (int)GameMode.Journey  => "Journey",
        100 => "Hardmode",
        101 => "Pre-Hardmode",
        _ => "Mode:" + key,
    };
}
