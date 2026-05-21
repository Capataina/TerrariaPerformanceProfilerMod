#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using PerformanceProfiler.Profiling.Persistence.Records;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Collectors;
namespace PerformanceProfiler.Data.Streams;

/// <summary>
/// Per-world recorder. Lives one-to-one with a world load (constructed at
/// <c>OnWorldLoad</c>, ended at <c>OnWorldUnload</c>). Owns the session id,
/// the per-tick downsampler, and the cursor state for incremental spike +
/// stall + context-transition reads.
///
/// The game thread is the only caller; every recorder method either
/// returns immediately or enqueues onto the writer thread's queue. Nothing
/// here blocks on disk.
/// </summary>
/// <summary>
/// Internal struct backing <see cref="SessionRecorder._recentDamageRing"/>.
/// Mutable on purpose — entries are overwritten in place by the ring.
/// </summary>
internal struct RecentDamageEntry
{
    public long UnixMs;
    public string SourceKind;
    public int SourceId;
    public string SourceName;
    public int DamageDealt;
}

public sealed class SessionRecorder
{
    private readonly ProfilerDatabase _db;
    private readonly ObjectId _sessionId;
    private readonly TickDownsampler _downsampler;
    private readonly DateTime _startedUtc;
    private readonly string _profilerVersion;
    private readonly string _modlistFingerprint;
    private readonly bool _tracksAllocations;

    private int _spikeCursor;
    private int _stallCursor;
    private long _ticksObserved;
    private double _maxFrameSeen;
    private double _gcSeen;

    /// <summary>
    /// Live cluster being built from consecutive stalls. <c>null</c> when no
    /// cluster is currently open. Flushed when no new stall has arrived
    /// within <see cref="ClusterIdleMs"/> of the cluster's last event.
    /// </summary>
    private StallClusterRow? _liveCluster;

    /// <summary>Sum of TopContributor RecentMs across the cluster, keyed by ModId. Resolves "dominant contributor".</summary>
    private readonly Dictionary<int, double> _clusterContribCost = new Dictionary<int, double>();

    /// <summary>Count of each StallCause seen in the live cluster. Resolves "dominant cause".</summary>
    private readonly Dictionary<string, int> _clusterCauseCounts = new Dictionary<string, int>();

    /// <summary>Stall events with unix-ms gaps larger than this break the cluster (start a new one).</summary>
    private const long ClusterIdleMs = 2000;

    /// <summary>
    /// Fixed-size ring of recent damage taken events, populated alongside
    /// <see cref="OnDamageTaken"/>. Used by <see cref="AggregateRecentDamage"/>
    /// at the death edge to compute the damage-weighted killer over a window,
    /// replacing the v0.5 last-hit-credit LiteDB query (which was the only
    /// game-thread DB read in the hot lifecycle path).
    ///
    /// Sized at 64 because a 10-second window at 60 FPS rarely sees more
    /// than ~30 distinct damage events even in heavy combat; 64 gives
    /// headroom without paying for it. Entries past the window cutoff are
    /// ignored by the aggregator rather than evicted.
    /// </summary>
    private readonly RecentDamageEntry[] _recentDamageRing = new RecentDamageEntry[64];
    private int _recentDamageNext;

    /// <summary>Window over which damage is aggregated for killer attribution.</summary>
    public const int DamageAttributionWindowSeconds = 10;

    public ObjectId SessionId => _sessionId;
    public DateTime StartedUtc => _startedUtc;
    public long TicksObserved => _ticksObserved;
    public TickDownsampler Downsampler => _downsampler;

    public SessionRecorder(
        ProfilerDatabase db,
        string profilerVersion,
        string tmlVersion,
        string mode,
        bool tracksAllocations,
        string modlistFingerprint,
        ObjectId? worldId)
    {
        _db = db;
        _sessionId = ObjectId.NewObjectId();
        _downsampler = new TickDownsampler();
        _startedUtc = DateTime.UtcNow;
        _profilerVersion = profilerVersion;
        _modlistFingerprint = modlistFingerprint;
        _tracksAllocations = tracksAllocations;

        var row = new SessionRow
        {
            Id = _sessionId,
            StartedUtc = _startedUtc,
            ProfilerVersion = profilerVersion,
            TmlVersion = tmlVersion,
            WorldId = worldId,
            ModlistFingerprint = modlistFingerprint,
            HookCoverageVersion = HookInterceptor.HookCoverageVersion,
            Mode = mode,
            TracksAllocations = tracksAllocations,
            EndReason = "clean",
            Incomplete = true,
        };
        _db.Writer.Enqueue(DbWriteOp.SessionStart(row));
    }

    /// <summary>
    /// Per-tick entry point. Drives the downsampler, drains any new spike
    /// windows and stall events, and bumps the local tick counter.
    /// </summary>
    public void OnTick(TickFrame frame, MetricCollector collector)
    {
        _ticksObserved++;
        if (frame.FrameTimeMs > _maxFrameSeen) _maxFrameSeen = frame.FrameTimeMs;
        _gcSeen += frame.GcTimeMs;

        _downsampler.OnTickCommitted(frame, collector, _db.Writer, _sessionId);

        DrainSpikes(collector);
        DrainStalls(collector);
    }

    /// <summary>
    /// Records a context transition. Caller resolves the type/from/to text
    /// (we don't reach into the ContextTagger here to keep the recorder
    /// tModLoader-runtime-agnostic — the recorder is testable without a
    /// running game).
    /// </summary>
    public void OnContextTransition(string type, string from, string to, long tick, double tickFrameMs)
    {
        var row = new ContextTransitionRow
        {
            SessionId = _sessionId,
            Tick = tick,
            Type = type,
            From = from,
            To = to,
            TickFrameMs = tickFrameMs,
        };
        _db.Writer.Enqueue(DbWriteOp.ContextTransition(row));

        if (PerformanceProfiler.LoggerOrNull != null && IsHeadline(type))
        {
            PerformanceProfiler.LoggerOrNull.Info($"context  tick={tick}  {type}: {from} -> {to}");
        }
    }

    /// <summary>Records a player death event. Called from <c>ContextTransitionWatcher</c> on the dead-edge.</summary>
    public void OnPlayerDeath(PlayerDeathRow row)
    {
        row.SessionId = _sessionId;
        _db.Writer.Enqueue(DbWriteOp.PlayerDeath(row));
        if (PerformanceProfiler.LoggerOrNull != null)
        {
            PerformanceProfiler.LoggerOrNull.Info(
                $"player-death  tick={row.Tick}  at=({row.TileX:F0},{row.TileY:F0})  hp={row.LastHp}/{row.MaxHp}  boss={row.PrimaryBoss}");
        }
    }

    /// <summary>Records a periodic world-state snapshot.</summary>
    public void OnWorldSnapshot(WorldSnapshotRow row)
    {
        row.SessionId = _sessionId;
        _db.Writer.Enqueue(DbWriteOp.WorldSnapshot(row));
    }

    public void OnDamageTaken(DamageTakenRow row)
    {
        row.SessionId = _sessionId;
        _db.Writer.Enqueue(DbWriteOp.DamageTaken(row));

        // Mirror into the in-RAM ring for damage-weighted death attribution.
        // The ring is local to this recorder (same lifecycle), so we don't
        // need an explicit clear on world unload — the recorder is replaced.
        ref var slot = ref _recentDamageRing[_recentDamageNext];
        slot.UnixMs = row.UnixMs;
        slot.SourceKind = row.SourceKind;
        slot.SourceId = row.SourceId;
        slot.SourceName = row.SourceName;
        slot.DamageDealt = row.DamageDealt;
        _recentDamageNext = (_recentDamageNext + 1) & 63;   // 64-slot mask
    }

    /// <summary>
    /// Aggregate damage taken in the last <paramref name="windowSeconds"/>
    /// seconds (relative to <paramref name="referenceUnixMs"/>) into a
    /// damage-weighted contributor list, sorted descending by total damage.
    /// Returns an empty list if no damage in the window. Allocates the
    /// returned list and its contributors — called once per death edge, so
    /// the alloc cost is event-frequency not per-tick.
    /// </summary>
    public List<DeathDamageContributor> AggregateRecentDamage(long referenceUnixMs, int windowSeconds = DamageAttributionWindowSeconds)
    {
        long cutoff = referenceUnixMs - (long)windowSeconds * 1000L;
        // Walk the whole 64-slot ring; uninitialised slots have UnixMs = 0
        // which is unconditionally below cutoff. No filtering branch needed
        // for "empty" slots beyond the cutoff check.
        var byKey = new Dictionary<(string, int), DeathDamageContributor>();
        int totalDamage = 0;
        for (int i = 0; i < _recentDamageRing.Length; i++)
        {
            ref readonly var e = ref _recentDamageRing[i];
            if (e.UnixMs < cutoff) continue;
            var key = (e.SourceKind, e.SourceId);
            if (!byKey.TryGetValue(key, out var c))
            {
                c = new DeathDamageContributor
                {
                    SourceKind = e.SourceKind,
                    SourceId = e.SourceId,
                    SourceName = e.SourceName,
                };
                byKey[key] = c;
            }
            c.TotalDamage += e.DamageDealt;
            c.HitCount++;
            totalDamage += e.DamageDealt;
        }

        if (byKey.Count == 0) return new List<DeathDamageContributor>();

        var list = new List<DeathDamageContributor>(byKey.Values);
        list.Sort((a, b) => b.TotalDamage.CompareTo(a.TotalDamage));
        if (totalDamage > 0)
        {
            float inv = 1f / totalDamage;
            for (int i = 0; i < list.Count; i++) list[i].Fraction = list[i].TotalDamage * inv;
        }
        return list;
    }

    public void OnDamageDealt(DamageDealtRow row)
    {
        row.SessionId = _sessionId;
        _db.Writer.Enqueue(DbWriteOp.DamageDealt(row));
    }

    public void OnNpcSpawn(NpcSpawnRow row)
    {
        row.SessionId = _sessionId;
        _db.Writer.Enqueue(DbWriteOp.NpcSpawn(row));
    }

    public void OnItemCreated(ItemCreatedRow row)
    {
        row.SessionId = _sessionId;
        _db.Writer.Enqueue(DbWriteOp.ItemCreated(row));
    }

    public void OnLoadoutSnapshot(LoadoutSnapshotRow row)
    {
        row.SessionId = _sessionId;
        _db.Writer.Enqueue(DbWriteOp.LoadoutSnapshot(row));
    }

    public void OnBuffEvent(BuffEventRow row)
    {
        row.SessionId = _sessionId;
        _db.Writer.Enqueue(DbWriteOp.BuffEvent(row));
    }

    /// <summary>Transitions worth narrating to client.log (vs the high-frequency biome-bit ones).</summary>
    private static bool IsHeadline(string type)
        => type == "bossStart" || type == "bossEnd" || type == "bossSwap"
        || type == "hardmode"  || type == "invasion"
        || type == "subworld"  || type == "session";

    /// <summary>
    /// World-unload path: build the per-session aggregates, archive row,
    /// and session-end op. The whole set is enqueued; the writer thread
    /// drains everything during <see cref="ProfilerDatabase.Dispose"/>.
    /// </summary>
    public void End(MetricCollector collector, string endReason = "clean")
    {
        // Flush any final spike/stall windows that arrived after the last OnTick.
        DrainSpikes(collector);
        DrainStalls(collector);
        FlushCluster();

        var modAggs = BuildModAggregates(collector);
        var hookAggs = BuildHookAggregates(collector);
        var archive = BuildArchive(collector, modAggs);

        if (modAggs.Count > 0)
        {
            _db.Writer.Enqueue(DbWriteOp.ModAggregateBatch(_sessionId, modAggs));
        }
        if (hookAggs.Count > 0)
        {
            _db.Writer.Enqueue(DbWriteOp.HookAggregateBatch(_sessionId, hookAggs));
        }
        _db.Writer.Enqueue(DbWriteOp.ArchiveAggregate(archive));

        long durationMs = (long)(DateTime.UtcNow - _startedUtc).TotalMilliseconds;
        _db.Writer.Enqueue(DbWriteOp.SessionEnd(_sessionId, endReason, durationMs, _ticksObserved));
    }

    // ---- private accumulation paths --------------------------------------

    private void DrainSpikes(MetricCollector collector)
    {
        IReadOnlyList<SpikeWindow> spikes = collector.Spikes;
        while (_spikeCursor < spikes.Count)
        {
            SpikeWindow w = spikes[_spikeCursor++];
            var row = new SpikeWindowRow
            {
                SessionId = _sessionId,
                StartTick = w.StartTick,
                EndTick = w.EndTick,
                WorstTick = w.WorstTick,
                WorstFrameMs = w.WorstFrameMs,
                BaselineMs = w.BaselineMs,
                MadMs = w.MadMs,
                Warming = w.Warming,
                Context = w.ContextSummary,
                PerModCatMs = ToList(w.PerModCatMs),
                PerModCatBytes = w.PerModCatBytes != null ? ToList(w.PerModCatBytes) : null,
                TopContributors = BuildSpikeTopContributors(w),
            };
            _db.Writer.Enqueue(DbWriteOp.Spike(row));

            // Inline narration to client.log for spikes that are at least
            // SpikeLogMultiplier × the captured baseline — what the player
            // perceives as a hitch relative to their *own* normal frame.
            // A 30 fps modded player and a 120 fps player both get the
            // right log volume without retuning constants. Skip
            // warming-window noise.
            const double SpikeLogMultiplier = 6.0;
            if (w.WorstFrameMs >= w.BaselineMs * SpikeLogMultiplier && !w.Warming && PerformanceProfiler.LoggerOrNull != null)
            {
                string topName = row.TopContributors.Count > 0 ? row.TopContributors[0].Name : "(no attribution)";
                double topMs = row.TopContributors.Count > 0 ? row.TopContributors[0].Ms : 0d;
                PerformanceProfiler.LoggerOrNull.Info(
                    $"spike  tick={w.WorstTick}  worst={w.WorstFrameMs:F0}ms  baseline={w.BaselineMs:F1}ms  top={topName} ({topMs:F1}ms)");
            }
        }
    }

    private void DrainStalls(MetricCollector collector)
    {
        IReadOnlyList<StallEvent> stalls = collector.Stalls;
        string[] modNames = HookInterceptor.ProfiledModNames;
        while (_stallCursor < stalls.Count)
        {
            StallEvent s = stalls[_stallCursor++];

            // Decide whether this stall extends the open cluster or starts a
            // new one. ClusterIdleMs (2s) is the gap that breaks runs apart.
            if (_liveCluster != null && s.StartTimestampUnixMs - _liveCluster.EndUnixMs > ClusterIdleMs)
            {
                FlushCluster();
            }

            if (_liveCluster == null)
            {
                _liveCluster = new StallClusterRow
                {
                    SessionId = _sessionId,
                    StartTick = s.StartTickIndex,
                    StartUnixMs = s.StartTimestampUnixMs,
                };
                _clusterContribCost.Clear();
                _clusterCauseCounts.Clear();
            }

            // Update cluster aggregates.
            // v0.6 (stall-detection §5.L): EndUnixMs is the moment the stall
            // ENDED, not the moment it started. Including the stall's own
            // duration in the cluster span is what makes "span" mean "from
            // first stall start to last stall end". v0.5 used start-only,
            // understating cluster span by ~TickPeriodMs per stall (e.g.
            // ~80 ms on a 40-stall cluster).
            _liveCluster.EndTick = s.EndTickIndex;
            _liveCluster.EndUnixMs = s.StartTimestampUnixMs + (long)s.TickPeriodMs;
            _liveCluster.StallCount++;
            _liveCluster.TotalDurationMs += s.TickPeriodMs;
            if (s.TickPeriodMs > _liveCluster.WorstDurationMs)
                _liveCluster.WorstDurationMs = s.TickPeriodMs;
            _liveCluster.SpanMs = _liveCluster.EndUnixMs - _liveCluster.StartUnixMs;

            string causeKey = EnumStringTable.CauseName(s.Cause);
            _clusterCauseCounts[causeKey] = (_clusterCauseCounts.TryGetValue(causeKey, out int cc) ? cc : 0) + 1;

            AccumulateContribCost(s.C0);
            AccumulateContribCost(s.C1);
            AccumulateContribCost(s.C2);
            AccumulateContribCost(s.C3);
            AccumulateContribCost(s.C4);

            var topContribs = new List<StallContributorEntry>(5);
            AddContrib(topContribs, s.C0, modNames);
            AddContrib(topContribs, s.C1, modNames);
            AddContrib(topContribs, s.C2, modNames);
            AddContrib(topContribs, s.C3, modNames);
            AddContrib(topContribs, s.C4, modNames);

            var row = new StallEventRow
            {
                SessionId = _sessionId,
                TickIndex = s.StartTickIndex,
                UnixMs = s.StartTimestampUnixMs,
                DurationMs = s.TickPeriodMs,
                BaselineTickMs = s.BaselineMs,
                Cause = EnumStringTable.CauseName(s.Cause),
                Severity = EnumStringTable.SeverityName(s.Severity),
                GcPauseDurationMs = s.GcPauseDurationMs,
                Gen0Collections = s.Gen0Collections,
                Gen1Collections = s.Gen1Collections,
                Gen2Collections = s.Gen2Collections,
                HeapDeltaBytes = s.HeapSizeAfterBytes - s.HeapSizeBeforeBytes,
                CpuTimeDeltaMs = s.ProcessCpuTimeDeltaMs,
                Warming = s.Warming,
                ClusterId = _liveCluster.Id,
                TopContributors = topContribs,
            };
            _db.Writer.Enqueue(DbWriteOp.Stall(row));

            // Inline narration to client.log so a log-only inspection later
            // (the workflow we just had to do manually) doesn't need the DB.
            // Threshold = StallDetector.SeverityFreezeMultiplier × baseline,
            // i.e. only the "the game stopped" tier gets a log line. A 30
            // fps modded player needs higher absolute ms than a 120 fps
            // player to hit this; using baseline as the divisor makes the
            // threshold honest for both.
            if (s.TickPeriodMs >= s.BaselineMs * StallDetector.SeverityFreezeMultiplier && PerformanceProfiler.LoggerOrNull != null)
            {
                string top = topContribs.Count > 0 ? topContribs[0].Name : "(no attribution)";
                PerformanceProfiler.LoggerOrNull.Info(
                    $"stall  tick={s.StartTickIndex}  dur={s.TickPeriodMs:F0}ms  cause={causeKey}  top={top} ({(topContribs.Count > 0 ? topContribs[0].RecentMs : 0d):F2}ms recent)");
            }
        }
    }

    /// <summary>Finalise the open cluster + emit it. Called on idle-gap and on session end.</summary>
    internal void FlushCluster()
    {
        if (_liveCluster == null) return;
        if (_liveCluster.StallCount <= 0) { _liveCluster = null; return; }

        // Pick dominant cause = most-frequent across the cluster's events.
        string domCause = "Unknown";
        int domCauseCount = -1;
        foreach (var kv in _clusterCauseCounts)
        {
            if (kv.Value > domCauseCount) { domCauseCount = kv.Value; domCause = kv.Key; }
        }
        _liveCluster.DominantCause = domCause;

        // Pick dominant contributor = mod with the highest cumulative RecentMs
        // across the cluster's per-event top-5 snapshots.
        int domModId = -1;
        double domVal = -1d;
        foreach (var kv in _clusterContribCost)
        {
            if (kv.Value > domVal) { domVal = kv.Value; domModId = kv.Key; }
        }
        _liveCluster.DominantContributorModId = domModId;
        string[] names = HookInterceptor.ProfiledModNames;
        _liveCluster.DominantContributorName =
            (domModId >= 0 && domModId < names.Length) ? names[domModId] : "(no attribution)";

        _db.Writer.Enqueue(DbWriteOp.StallCluster(_liveCluster));

        // Inline narration: every cluster is a "the player saw this freeze".
        if (PerformanceProfiler.LoggerOrNull != null)
        {
            PerformanceProfiler.LoggerOrNull.Info(
                $"stall-cluster  {_liveCluster.StallCount} stalls over {_liveCluster.SpanMs:F0}ms  total={_liveCluster.TotalDurationMs:F0}ms  worst={_liveCluster.WorstDurationMs:F0}ms  cause={domCause}  contributor={_liveCluster.DominantContributorName}");
        }

        _liveCluster = null;
        _clusterContribCost.Clear();
        _clusterCauseCounts.Clear();
    }

    private void AccumulateContribCost(StallContributor c)
    {
        if (c.ModId < 0 || c.RecentMs <= 0d) return;
        _clusterContribCost[c.ModId] = (_clusterContribCost.TryGetValue(c.ModId, out double v) ? v : 0d) + c.RecentMs;
    }

    private static void AddContrib(List<StallContributorEntry> list, StallContributor c, string[] modNames)
    {
        if (c.ModId < 0 || c.RecentMs <= 0d) return;
        list.Add(new StallContributorEntry
        {
            ModId = c.ModId,
            Name = (c.ModId < modNames.Length) ? modNames[c.ModId] : ("mod-" + c.ModId),
            RecentMs = c.RecentMs,
        });
    }

    private List<PerSessionModAggregate> BuildModAggregates(MetricCollector collector)
    {
        int modCount = PerModAttribution.ModCount;
        int categoryCount = PerModAttribution.CategoryCount;
        var result = new List<PerSessionModAggregate>(modCount);

        IReadOnlyList<double> avgMs = collector.PerModCategoryAverageMs;
        IReadOnlyList<double>? avgBytes = collector.PerModCategoryAverageBytes;
        IReadOnlyList<int> measured = HookInterceptor.MeasuredHookCounts;
        IReadOnlyList<int> total = HookInterceptor.TotalHookCounts;
        string[] names = HookInterceptor.ProfiledModNames;

        long durationMs = Math.Max(1L, _ticksObserved); // avoid div-by-zero

        for (int modId = 0; modId < modCount; modId++)
        {
            double totalCategoryMs = 0d;
            double peakCategoryMs = 0d;
            double totalBytes = 0d;
            double peakBytes = 0d;
            int offset = modId * categoryCount;
            var categorySlice = new List<double>(categoryCount);
            for (int cat = 0; cat < categoryCount; cat++)
            {
                int idx = offset + cat;
                double ms = idx < avgMs.Count ? avgMs[idx] : 0d;
                totalCategoryMs += ms;
                if (ms > peakCategoryMs) peakCategoryMs = ms;
                categorySlice.Add(ms);
                if (avgBytes != null && idx < avgBytes.Count)
                {
                    totalBytes += avgBytes[idx];
                    if (avgBytes[idx] > peakBytes) peakBytes = avgBytes[idx];
                }
            }

            int measuredHooks = modId < measured.Count ? measured[modId] : 0;
            int totalHooks = modId < total.Count ? total[modId] : 0;
            string badge = totalHooks == 0
                ? "n/a"
                : (measuredHooks >= totalHooks ? "full" : (measuredHooks * 2 >= totalHooks ? "partial" : "limited"));

            result.Add(new PerSessionModAggregate
            {
                SessionId = _sessionId,
                ModId = modId,
                ModInternalName = modId < names.Length ? names[modId] : ("mod-" + modId),
                AvgMs = totalCategoryMs,
                PeakMs = peakCategoryMs,
                TotalMs = totalCategoryMs * _ticksObserved,
                P95Ms = peakCategoryMs,           // placeholder until per-tick p95 lands
                AvgBytes = totalBytes,
                PeakBytes = peakBytes,
                TotalBytes = totalBytes * _ticksObserved,
                CategoryMs = categorySlice,
                Coverage = new ModCoverage
                {
                    Measured = measuredHooks,
                    Total = totalHooks,
                    Badge = badge,
                },
                TopHooks = BuildTopHooks(collector, modId),
            });
        }

        return result;
    }

    private List<TopHookEntry> BuildTopHooks(MetricCollector collector, int modId)
    {
        var hooks = PerModAttribution.Hooks;
        IReadOnlyList<double> hookAvg = collector.PerHookAverageMs;
        var entries = new List<TopHookEntry>();
        for (int hookId = 0; hookId < hooks.Count; hookId++)
        {
            if (hooks[hookId].ModId != modId) continue;
            double ms = hookId < hookAvg.Count ? hookAvg[hookId] : 0d;
            if (ms <= 0d) continue;
            entries.Add(new TopHookEntry
            {
                HookId = hookId,
                DisplayName = hooks[hookId].DisplayName,
                AvgMs = ms,
            });
        }
        entries.Sort((a, b) => b.AvgMs.CompareTo(a.AvgMs));
        if (entries.Count > 5) entries.RemoveRange(5, entries.Count - 5);
        return entries;
    }

    private List<PerSessionHookAggregate> BuildHookAggregates(MetricCollector collector)
    {
        var hooks = PerModAttribution.Hooks;
        IReadOnlyList<double> hookAvg = collector.PerHookAverageMs;
        IReadOnlyList<double>? hookAvgBytes = collector.PerHookAverageBytes;
        var result = new List<PerSessionHookAggregate>(hooks.Count);
        for (int hookId = 0; hookId < hooks.Count; hookId++)
        {
            HookDescriptor desc = hooks[hookId];
            double avgMs = hookId < hookAvg.Count ? hookAvg[hookId] : 0d;
            double avgBytes = (hookAvgBytes != null && hookId < hookAvgBytes.Count) ? hookAvgBytes[hookId] : 0d;

            // Skip silent hooks regardless of allocation tracking — writing
            // a row per zero-ms zero-byte hook is what blew the DB up to
            // 5.8 MB in a 3-minute session (10,250 hooks × ~200 B row).
            // With this gate the per-session-hook batch carries only the
            // hooks that actually fired.
            if (avgMs <= 0d && avgBytes <= 0d) continue;

            result.Add(new PerSessionHookAggregate
            {
                SessionId = _sessionId,
                HookId = hookId,
                ModId = desc.ModId,
                CategoryId = desc.CategoryId,
                DisplayName = desc.DisplayName,
                AvgMs = avgMs,
                PeakMs = avgMs,
                TotalMs = avgMs * _ticksObserved,
                AvgBytes = avgBytes,
                CallCount = 0,
            });
        }
        return result;
    }

    private TickAggregateArchive BuildArchive(MetricCollector collector, List<PerSessionModAggregate> modAggs)
    {
        var perMod = new List<ArchivePerMod>(modAggs.Count);
        foreach (var m in modAggs)
        {
            perMod.Add(new ArchivePerMod
            {
                ModId = m.ModId,
                Name = m.ModInternalName,
                AvgMs = m.AvgMs,
                TotalMs = m.TotalMs,
                PeakMs = m.PeakMs,
                TotalBytes = m.TotalBytes,
            });
        }

        // Frame distribution from history. The RingBuffer's enumerator is
        // oldest-first; we only need a one-pass average + max for the
        // archive — p95/p99 will be lifted out of warm-tier data by future
        // queries.
        double totalFrame = 0d;
        double max = _maxFrameSeen;
        int n = collector.History.Count;
        for (int i = 0; i < n; i++) totalFrame += collector.History[i].FrameTimeMs;
        double avg = n > 0 ? totalFrame / n : 0d;

        return new TickAggregateArchive
        {
            SessionId = _sessionId,
            AvgFrameMs = avg,
            MedianFrameMs = avg,
            P95FrameMs = max,        // placeholder; warm-tier P95 is the precise source
            P99FrameMs = max,
            MaxFrameMs = max,
            TotalGcMs = _gcSeen,
            TicksObserved = _ticksObserved,
            SpikeCount = collector.Spikes.Count,
            StallCount = collector.Stalls.Count,
            PerMod = perMod,
        };
    }

    private List<SpikeContributor> BuildSpikeTopContributors(SpikeWindow w)
    {
        int modCount = PerModAttribution.ModCount;
        int categoryCount = PerModAttribution.CategoryCount;
        string[] names = HookInterceptor.ProfiledModNames;
        var contributors = new List<SpikeContributor>();
        for (int modId = 0; modId < modCount; modId++)
        {
            double ms = 0d;
            double bytes = 0d;
            int offset = modId * categoryCount;
            for (int cat = 0; cat < categoryCount; cat++)
            {
                int idx = offset + cat;
                if (idx < w.PerModCatMs.Length) ms += w.PerModCatMs[idx];
                if (w.PerModCatBytes != null && idx < w.PerModCatBytes.Length) bytes += w.PerModCatBytes[idx];
            }
            if (ms > 0d)
            {
                contributors.Add(new SpikeContributor
                {
                    ModId = modId,
                    Name = modId < names.Length ? names[modId] : ("mod-" + modId),
                    Ms = ms,
                    Bytes = bytes,
                });
            }
        }
        contributors.Sort((a, b) => b.Ms.CompareTo(a.Ms));
        if (contributors.Count > 5) contributors.RemoveRange(5, contributors.Count - 5);
        return contributors;
    }

    private static List<double> ToList(float[] arr)
    {
        var list = new List<double>(arr.Length);
        for (int i = 0; i < arr.Length; i++) list.Add(arr[i]);
        return list;
    }
}
