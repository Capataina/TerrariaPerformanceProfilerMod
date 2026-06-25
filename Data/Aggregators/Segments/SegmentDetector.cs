#nullable enable

using System;
using System.Collections.Generic;
using LiteDB;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
namespace PerformanceProfiler.Data.Aggregators.Segments;

/// <summary>
/// The segment engine. Driven once per tick from
/// <c>ProfilerSystem.PostUpdateEverything</c> after the
/// <see cref="EventAggregator"/> has accumulated the frame.
///
/// <para>
/// <b>What it does.</b> Maintains a set of in-flight <see cref="OpenSegment"/>
/// instances keyed by <c>(family, key)</c>. Each tick:
/// </para>
/// <list type="number">
///   <item>Compute the new active <c>(family, key)</c> set from
///         <see cref="EventContext"/>.</item>
///   <item>Open segments for every active key that wasn't active last tick.</item>
///   <item>Close segments for every key that was active last tick but isn't now.</item>
///   <item>Fold this tick's frame-ms and per-mod-ms into every still-open segment.</item>
/// </list>
///
/// <para>
/// <b>Spike / stall / death side-channel.</b> Other detectors call
/// <see cref="OnSpike"/> / <see cref="OnStall"/> / <see cref="OnDeath"/>
/// to increment counters on every currently-open segment plus push a fresh
/// death-bracket open. These side-channel events are rare (a handful per
/// session each), so the linear scan over open segments is fine.
/// </para>
///
/// <para>
/// <b>Allocation contract.</b> Per-tick path is allocation-free once the
/// modlist + open-segment dictionary have warmed up. Closing a segment
/// allocates one <see cref="SegmentRow"/> + one <see cref="Segment"/> (the
/// retrospective record), which is rare (~tens per session) and is the
/// price of crossing the persistence boundary.
/// </para>
/// </summary>
internal sealed class SegmentDetector
{
    private const int MinDwellTicks = 6;          // ~0.1 s
    private const int CombatIdleCloseTicks = 300; // 5 s of no damage closes a combat window

    private readonly ObjectId _sessionId;
    private readonly ISegmentSink _sink;
    private readonly Dictionary<long, OpenSegment> _open = new();
    private readonly Stack<OpenSegment> _pool = new();

    // Scratch sets reused across ticks for the active-key sweep. Allocated once
    // per family at first use; cleared each tick. Keeps the per-tick path
    // allocation-free.
    private readonly HashSet<int> _activeBiome = new();
    private readonly HashSet<int> _activeWeather = new();
    private readonly HashSet<int> _activeBoss = new();
    private readonly HashSet<int> _activeBossPrev = new();
    private readonly HashSet<int> _toClose = new();

    // Per-family "previous tick" snapshots so the edge detector can spot a key
    // that disappeared this tick. Boss closures specifically need to know
    // which boss died (was active last tick, not active this tick) so we can
    // bump that segment's BossKillCount before close.
    private InvasionId _prevInvasion = InvasionId.None;
    private int _prevSubworld;
    private bool _prevHardmode;
    private long _prevTick = -1L;
    private int _deathIndex;
    private int _bookmarkIndex;
    private long _lastCombatHitTick = -1L;

    // Composite biome name resolved each tick from the active bitset; read
    // by OpenIfAbsent when it allocates the (single) biome composite segment.
    private string _biomeCompositeName = string.Empty;

    // Memoisation for ComputeBiomeComposite — the biome bitset rarely
    // changes between ticks but the function was rebuilding a fresh
    // StringBuilder + final string every tick. Cache the previous
    // input's key + name and short-circuit when the bitset is identical.
    // Invariant 2 (zero per-tick allocation) compliance.
    private int _cachedCompositeKey;
    private string _cachedCompositeName = string.Empty;
    private BiomeBitset _cachedCompositeBitset;
    private bool _cachedCompositeValid;

    // Reusable buffer for the rare path where the composite genuinely
    // changes; cleared and re-filled in place. Single-threaded ownership
    // (game thread) so a single instance field is safe.
    private readonly System.Text.StringBuilder _compositeScratch = new System.Text.StringBuilder(64);

    public SegmentDetector(ObjectId sessionId, ISegmentSink sink)
    {
        _sessionId = sessionId;
        _sink = sink;
    }

    /// <summary>Currently-open segments, exposed for the live "Now playing" panel and chat commands.</summary>
    public IReadOnlyCollection<OpenSegment> OpenSegments => _open.Values;

    /// <summary>
    /// Per-tick edge sweep + accumulation.
    /// </summary>
    /// <param name="tickIndex">Game tick index (Main.GameUpdateCount).</param>
    /// <param name="unixMs">Wall-clock time at this tick.</param>
    /// <param name="ctx">Captured EventContext for this tick.</param>
    /// <param name="frameMs">Frame time this tick, in ms.</param>
    /// <param name="perModCategoryRawMs">
    /// Per-mod / per-category raw ms for this tick. The concrete
    /// <c>double[]</c> (via <see cref="MetricCollector.PerModCategoryRawMsArray"/>)
    /// so the per-segment fold below indexes without interface dispatch.
    /// Layout: <c>[modId * CategoryCount + categoryId]</c>.
    /// </param>
    public void OnTick(long tickIndex, long unixMs, in EventContext ctx, double frameMs,
        double[] perModCategoryRawMs)
    {
        int modCount = PerModAttribution.ModCount;
        int categoryCount = PerModAttribution.CategoryCount;

        // --- 1. Compute new active key sets from EventContext -----------------
        // v0.7.2: biomes collapse to ONE composite segment per tick. The
        // active set ("Forest" + "Purity") becomes a single key/name; when
        // the set changes (e.g. enter Corruption while leaving Purity)
        // the composite closes and a new composite opens. One transition,
        // one Timeline row, regardless of how many bits flipped.
        //
        // _activeBiome holds the composite-key as a single entry; the
        // scratch sweep code reuses the same HashSet machinery without
        // a per-bit ceremony.
        _activeBiome.Clear();
        int compositeKey = ComputeBiomeComposite(in ctx.Biomes, out string compositeName);
        if (compositeKey != 0)
        {
            _activeBiome.Add(compositeKey);
            _biomeCompositeName = compositeName;
        }
        else
        {
            _biomeCompositeName = string.Empty;
        }

        _activeWeather.Clear();
        WeatherFlags w = ctx.Weather;
        foreach (var pair in WeatherSources.All)
        {
            if ((w & pair.Flag) != 0) _activeWeather.Add((int)pair.Flag);
        }

        _activeBossPrev.Clear();
        foreach (int prev in _activeBoss) _activeBossPrev.Add(prev);
        _activeBoss.Clear();
        BossSlotArray bosses = ctx.Bosses;
        int bossSlotCount = bosses.Count;
        for (int i = 0; i < bossSlotCount; i++)
        {
            int type = bosses[i];
            if (type != 0) _activeBoss.Add(type);
        }

        // --- 2. Open new segments where a predicate just turned on ------------
        SweepOpen(SegmentFamily.Biome, _activeBiome, tickIndex, unixMs, modCount);
        SweepOpen(SegmentFamily.Weather, _activeWeather, tickIndex, unixMs, modCount);
        SweepOpen(SegmentFamily.Boss, _activeBoss, tickIndex, unixMs, modCount);

        if (ctx.VanillaInvasion != InvasionId.None && _prevInvasion == InvasionId.None)
        {
            OpenIfAbsent(SegmentFamily.Invasion, (int)ctx.VanillaInvasion, tickIndex, unixMs, modCount);
        }
        if (ctx.SubworldKey != 0 && _prevSubworld == 0)
        {
            OpenIfAbsent(SegmentFamily.Subworld, ctx.SubworldKey, tickIndex, unixMs, modCount);
        }
        if (ctx.Hardmode && !_prevHardmode)
        {
            OpenIfAbsent(SegmentFamily.Hardmode, 1, tickIndex, unixMs, modCount);
        }

        // --- 3. Close segments where a predicate just turned off -------------
        SweepClose(SegmentFamily.Biome, _activeBiome, tickIndex, unixMs);
        SweepClose(SegmentFamily.Weather, _activeWeather, tickIndex, unixMs);

        // Boss closures need to credit a kill: any boss that was active last
        // tick but isn't this tick (and was not just promoted to head) counts
        // as either killed or despawned; we credit it as a kill on close.
        _toClose.Clear();
        foreach (int prev in _activeBossPrev)
        {
            if (!_activeBoss.Contains(prev)) _toClose.Add(prev);
        }
        foreach (int prevKey in _toClose)
        {
            long composite = Compose(SegmentFamily.Boss, prevKey);
            if (_open.TryGetValue(composite, out OpenSegment? seg))
            {
                seg.BossKillCount++;
                CloseAndPublish(composite, seg, tickIndex, unixMs);
            }
        }

        if (ctx.VanillaInvasion == InvasionId.None && _prevInvasion != InvasionId.None)
        {
            long composite = Compose(SegmentFamily.Invasion, (int)_prevInvasion);
            if (_open.TryGetValue(composite, out OpenSegment? seg))
            {
                CloseAndPublish(composite, seg, tickIndex, unixMs);
            }
        }
        if (ctx.SubworldKey == 0 && _prevSubworld != 0)
        {
            long composite = Compose(SegmentFamily.Subworld, _prevSubworld);
            if (_open.TryGetValue(composite, out OpenSegment? seg))
            {
                CloseAndPublish(composite, seg, tickIndex, unixMs);
            }
        }
        // Hardmode never closes mid-session — flushed in CloseAllOnShutdown.

        // Combat-idle close: a combat window that's seen no damage for
        // CombatIdleCloseTicks closes itself.
        if (_lastCombatHitTick >= 0L && tickIndex - _lastCombatHitTick >= CombatIdleCloseTicks)
        {
            long composite = Compose(SegmentFamily.Combat, 1);
            if (_open.TryGetValue(composite, out OpenSegment? seg))
            {
                CloseAndPublish(composite, seg, tickIndex, unixMs);
                _lastCombatHitTick = -1L;
            }
        }

        // --- 4. Fold this tick's frame-ms + per-mod into every still-open ----
        if (_open.Count > 0)
        {
            foreach (var seg in _open.Values)
            {
                seg.Ticks++;
                seg.TotalFrameMs += frameMs;
                double[] dest = seg.PerModMs;
                if (dest.Length != modCount) continue;
                // Sum across categories for each mod -- the segment view is
                // per-mod, not per-mod-per-category. (Per-mod-per-category
                // would 7× the storage for negligible UI value.)
                for (int m = 0; m < modCount; m++)
                {
                    int baseIdx = m * categoryCount;
                    double rowSum = 0d;
                    for (int c = 0; c < categoryCount; c++)
                    {
                        rowSum += perModCategoryRawMs[baseIdx + c];
                    }
                    dest[m] += rowSum;
                }
            }
        }

        _prevInvasion = ctx.VanillaInvasion;
        _prevSubworld = ctx.SubworldKey;
        _prevHardmode = ctx.Hardmode;
        _prevTick = tickIndex;
    }

    /// <summary>
    /// Records a spike on every currently-open segment. Called by the spike
    /// detector once it commits a window.
    /// </summary>
    public void OnSpike()
    {
        foreach (var seg in _open.Values) seg.SpikeCount++;
    }

    /// <summary>Records a stall on every currently-open segment.</summary>
    public void OnStall()
    {
        foreach (var seg in _open.Values) seg.StallCount++;
    }

    /// <summary>
    /// Records a death on every currently-open segment, closes the current
    /// death-bracket (if any), and opens a new one for the next run.
    /// </summary>
    public void OnDeath(long tickIndex, long unixMs)
    {
        foreach (var seg in _open.Values) seg.DeathCount++;

        long currentBracket = Compose(SegmentFamily.DeathBracket, _deathIndex);
        if (_open.TryGetValue(currentBracket, out OpenSegment? prev))
        {
            CloseAndPublish(currentBracket, prev, tickIndex, unixMs);
        }
        _deathIndex++;
        int modCount = PerModAttribution.ModCount;
        OpenIfAbsent(SegmentFamily.DeathBracket, _deathIndex, tickIndex, unixMs, modCount);
    }

    /// <summary>Marks that a damage event landed this tick; (re)opens the combat window.</summary>
    public void OnCombatHit(long tickIndex, long unixMs)
    {
        _lastCombatHitTick = tickIndex;
        int modCount = PerModAttribution.ModCount;
        OpenIfAbsent(SegmentFamily.Combat, 1, tickIndex, unixMs, modCount);
    }

    /// <summary>
    /// Opens a user bookmark. Returns the bookmark id (the Key field on the
    /// resulting segment); the caller can echo it back in chat so the user
    /// can later end the bookmark by id.
    /// </summary>
    public int OpenBookmark(long tickIndex, long unixMs, string? label)
    {
        _bookmarkIndex++;
        int modCount = PerModAttribution.ModCount;
        OpenSegment seg = Rent(modCount);
        seg.Family = SegmentFamily.UserBookmark;
        seg.Key = _bookmarkIndex;
        seg.Name = string.IsNullOrEmpty(label) ? "Bookmark #" + _bookmarkIndex : label;
        seg.StartTick = tickIndex;
        seg.StartUnixMs = unixMs;
        _open[Compose(SegmentFamily.UserBookmark, _bookmarkIndex)] = seg;
        return _bookmarkIndex;
    }

    /// <summary>Closes a user bookmark by id (the one OpenBookmark returned). Returns true if closed.</summary>
    public bool CloseBookmark(int bookmarkId, long tickIndex, long unixMs)
    {
        long composite = Compose(SegmentFamily.UserBookmark, bookmarkId);
        if (!_open.TryGetValue(composite, out OpenSegment? seg)) return false;
        CloseAndPublish(composite, seg, tickIndex, unixMs);
        return true;
    }

    /// <summary>Flushes every open segment as if it were closing right now. Called at world unload / session end.</summary>
    public void CloseAllOnShutdown(long tickIndex, long unixMs)
    {
        if (_open.Count == 0) return;
        // Materialise keys first so we can mutate _open during iteration.
        var keys = new long[_open.Count];
        int i = 0;
        foreach (long k in _open.Keys) keys[i++] = k;
        for (int j = 0; j < keys.Length; j++)
        {
            if (_open.TryGetValue(keys[j], out OpenSegment? seg))
            {
                CloseAndPublish(keys[j], seg, tickIndex, unixMs);
            }
        }
        _open.Clear();
    }

    // ---- Internal helpers ------------------------------------------------

    private void SweepOpen(SegmentFamily family, HashSet<int> activeNow, long tickIndex, long unixMs, int modCount)
    {
        foreach (int key in activeNow)
        {
            OpenIfAbsent(family, key, tickIndex, unixMs, modCount);
        }
    }

    private void SweepClose(SegmentFamily family, HashSet<int> activeNow, long tickIndex, long unixMs)
    {
        _toClose.Clear();
        foreach (var kv in _open)
        {
            long composite = kv.Key;
            SegmentFamily f = (SegmentFamily)(byte)(composite >> 56);
            if (f != family) continue;
            int key = unchecked((int)composite);
            if (!activeNow.Contains(key)) _toClose.Add(key);
        }
        foreach (int key in _toClose)
        {
            long composite = Compose(family, key);
            if (_open.TryGetValue(composite, out OpenSegment? seg))
            {
                CloseAndPublish(composite, seg, tickIndex, unixMs);
            }
        }
    }

    private void OpenIfAbsent(SegmentFamily family, int key, long tickIndex, long unixMs, int modCount)
    {
        long composite = Compose(family, key);
        if (_open.ContainsKey(composite)) return;
        OpenSegment seg = Rent(modCount);
        seg.Family = family;
        seg.Key = key;
        // Biome composites store their human-readable name on the side
        // because the key is a hash and SegmentNameTable can't reverse it.
        seg.Name = family == SegmentFamily.Biome && !string.IsNullOrEmpty(_biomeCompositeName)
            ? _biomeCompositeName + " visit"
            : SegmentNameTable.For(family, key);
        seg.StartTick = tickIndex;
        seg.StartUnixMs = unixMs;
        _open[composite] = seg;
    }

    private void CloseAndPublish(long composite, OpenSegment seg, long tickIndex, long unixMs)
    {
        _open.Remove(composite);
        // Re-resolve boss name at close so the row spells the localised name
        // even if BossSampler's cache only saw it once.
        if (seg.Family == SegmentFamily.Boss)
        {
            seg.Name = SegmentNameTable.For(seg.Family, seg.Key);
        }

        // Drop segments below the dwell threshold without persisting — these
        // are the chip-on-biome-edge or boss-segment-collapse false starts.
        if (seg.Ticks < MinDwellTicks)
        {
            Return(seg);
            return;
        }

        // Build the row + retrospective Segment.
        Segment closed = BuildSegment(seg, tickIndex, unixMs);
        _sink.OnSegmentClosed(closed, seg);
        Return(seg);
    }

    private Segment BuildSegment(OpenSegment seg, long endTick, long endUnixMs)
    {
        long durationMs = endUnixMs - seg.StartUnixMs;
        if (durationMs < 0) durationMs = 0;

        // Pack the per-mod arrays — strip zeros so the row stays small. A
        // typical Blood Moon has 18 mods loaded but only 8-10 actually
        // accumulate non-zero cost during the segment.
        int nonZero = 0;
        for (int i = 0; i < seg.PerModMs.Length; i++)
        {
            if (seg.PerModMs[i] > 0d) nonZero++;
        }
        int[] modIds = nonZero == 0 ? Array.Empty<int>() : new int[nonZero];
        double[] modMs = nonZero == 0 ? Array.Empty<double>() : new double[nonZero];
        int dst = 0;
        for (int i = 0; i < seg.PerModMs.Length; i++)
        {
            double v = seg.PerModMs[i];
            if (v <= 0d) continue;
            modIds[dst] = i;
            modMs[dst] = v;
            dst++;
        }

        return new Segment
        {
            Id = ObjectId.NewObjectId(),
            SessionId = _sessionId,
            Family = seg.Family,
            Key = seg.Key,
            Name = seg.Name,
            StartTick = seg.StartTick,
            EndTick = endTick,
            StartUnixMs = seg.StartUnixMs,
            EndUnixMs = endUnixMs,
            DurationMs = durationMs,
            Ticks = seg.Ticks,
            TotalFrameMs = seg.TotalFrameMs,
            SpikeCount = seg.SpikeCount,
            StallCount = seg.StallCount,
            DeathCount = seg.DeathCount,
            BossKillCount = seg.BossKillCount,
            ModIds = modIds,
            ModMs = modMs,
        };
    }

    private OpenSegment Rent(int modCount)
    {
        OpenSegment seg = _pool.Count > 0 ? _pool.Pop() : new OpenSegment();
        seg.Reset(modCount);
        return seg;
    }

    private void Return(OpenSegment seg)
    {
        if (_pool.Count < 64) _pool.Push(seg);
    }

    /// <summary>Compose (family, key) into a single long for dictionary keying. 8 bits family + 32 bits key in low 40.</summary>
    private static long Compose(SegmentFamily family, int key)
    {
        return ((long)(byte)family << 56) | (uint)key;
    }

    /// <summary>
    /// Folds the full active biome bitset into a single composite (key, name)
    /// pair. Key is an FNV-1a hash of the sorted bit ids — stable across
    /// ticks for the same set, distinct for different sets, fits in an int.
    /// Name is the corresponding biome display names joined with spaces
    /// (sorted by id), e.g. "Forest Purity" or "Desert Corruption Underground".
    /// Returns 0 / empty when no biome is active.
    /// </summary>
    private int ComputeBiomeComposite(in BiomeBitset bitset, out string compositeName)
    {
        // Hot-path memoisation: the biome composite changes only when
        // the player crosses a biome boundary, which is rare relative
        // to the 60-tick poll rate. Hash the bitset first; if it
        // matches the previous tick's hash AND the bits are equal,
        // return the cached name without allocating.
        int count = BiomeRegistry.Count;
        uint hash = 2166136261u; // FNV-1a offset basis
        bool any = false;
        for (int i = 0; i < count; i++)
        {
            if (!bitset.IsSet(i)) continue;
            hash = (hash ^ (uint)i) * 16777619u;
            any = true;
        }
        int key = !any ? 0 : unchecked((int)(hash & 0x7FFFFFFFu));
        if (any && key == 0) key = 1; // 0 reserved for "no biome".

        if (_cachedCompositeValid && _cachedCompositeKey == key && _cachedCompositeBitset.Equals(bitset))
        {
            compositeName = _cachedCompositeName;
            return key;
        }

        // Composite changed — rebuild the display name into the reusable
        // scratch buffer. One allocation here per *change*, not per tick.
        // Index the concrete list (not the IReadOnlyList view) so each lookup
        // is devirtualised, matching the per-tick folds.
        List<BiomeDescriptor> biomes = BiomeRegistry.BiomesList;
        _compositeScratch.Clear();
        bool first = true;
        for (int i = 0; i < count; i++)
        {
            if (!bitset.IsSet(i)) continue;
            if (!first) _compositeScratch.Append(' ');
            _compositeScratch.Append(biomes[i].DisplayName);
            first = false;
        }
        compositeName = !any ? string.Empty : _compositeScratch.ToString();
        _cachedCompositeKey = key;
        _cachedCompositeName = compositeName;
        _cachedCompositeBitset.CopyFrom(bitset);
        _cachedCompositeValid = true;
        return key;
    }
}

/// <summary>Where closed segments go. ProfilerSystem implements this against the SegmentStore + DB writer.</summary>
internal interface ISegmentSink
{
    void OnSegmentClosed(Segment segment, OpenSegment source);
}
