#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using PerformanceProfiler.Persistence.Records;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
namespace PerformanceProfiler.Persistence;

/// <summary>
/// Facade over the open <see cref="LiteDatabase"/>, the append-only event
/// journal, and the writer thread. One instance per running mod, constructed
/// at <c>Mod.Load</c> and disposed at <c>Mod.Unload</c>.
///
/// <para>
/// Per-collection write/read logic does <b>not</b> live here. Every collection
/// is owned by an <see cref="IPersistenceStream"/> implementation under
/// <c>Streams/</c>; this facade wires the registry, dispatches ops on the
/// writer thread, and owns the cross-cutting concerns (open + recovery,
/// schema versioning, journal replay, backups, compact, lifecycle).
/// </para>
///
/// <para>
/// To add a new collection later: create a record class under
/// <c>Records/</c>, a stream implementation under <c>Streams/</c>, and add
/// the new <see cref="DbOpKind"/>(s) to <see cref="DbWriteOp"/>. Register
/// the stream in <see cref="StreamRegistry.Default"/>. No edit to this file
/// is required.
/// </para>
/// </summary>
public sealed class ProfilerDatabase : IDisposable
{
    /// <summary>Schema version this profiler writes. Bumped on whole-DB-shape changes.</summary>
    public const int CurrentUserVersion = 1;

    /// <summary>Number of rotating backup files kept on disk.</summary>
    public const int BackupKeep = 3;

    /// <summary>Warm-tier retention from creation.</summary>
    public static readonly TimeSpan WarmRetention = TimeSpan.FromHours(24);

    private readonly string _root;
    private readonly LiteDatabase _db;
    private readonly EventJournal _journal;
    private readonly DbWriterThread _writer;
    private readonly StreamRegistry _registry;
    private readonly Action<string, Exception?> _log;
    private bool _disposed;

    public string Root => _root;
    public LiteDatabase RawDb => _db;
    public EventJournal Journal => _journal;
    public DbWriterThread Writer => _writer;
    public StreamRegistry Registry => _registry;

    // Typed collection accessors. Cheap — LiteDatabase caches.
    public ILiteCollection<SessionRow>              Sessions              => _db.GetCollection<SessionRow>("sessions");
    public ILiteCollection<ModlistRow>              Modlists              => _db.GetCollection<ModlistRow>("modlists");
    public ILiteCollection<ModRow>                  Mods                  => _db.GetCollection<ModRow>("mods");
    public ILiteCollection<WorldRow>                Worlds                => _db.GetCollection<WorldRow>("worlds");
    public ILiteCollection<PerSessionModAggregate>  PerSessionMods        => _db.GetCollection<PerSessionModAggregate>("perSessionModAggregates");
    public ILiteCollection<PerSessionHookAggregate> PerSessionHooks       => _db.GetCollection<PerSessionHookAggregate>("perSessionHookAggregates");
    public ILiteCollection<SpikeWindowRow>          SpikeWindows          => _db.GetCollection<SpikeWindowRow>("spikeWindows");
    public ILiteCollection<StallEventRow>           Stalls                => _db.GetCollection<StallEventRow>("stallEvents");
    public ILiteCollection<ContextTransitionRow>    ContextTransitions    => _db.GetCollection<ContextTransitionRow>("contextTransitions");
    public ILiteCollection<TickAggregateWarm>       TickAggregatesWarm    => _db.GetCollection<TickAggregateWarm>("tickAggregatesWarm");
    public ILiteCollection<TickAggregateCold>       TickAggregatesCold    => _db.GetCollection<TickAggregateCold>("tickAggregatesCold");
    public ILiteCollection<TickAggregateArchive>    TickAggregatesArchive => _db.GetCollection<TickAggregateArchive>("tickAggregatesArchive");
    public ILiteCollection<InsightRow>              Insights              => _db.GetCollection<InsightRow>("insights");
    public ILiteCollection<MetadataRow>             Metadata              => _db.GetCollection<MetadataRow>("metadata");
    public ILiteCollection<StallClusterRow>         StallClusters         => _db.GetCollection<StallClusterRow>("stallClusters");
    public ILiteCollection<PlayerDeathRow>          PlayerDeaths          => _db.GetCollection<PlayerDeathRow>("playerDeaths");
    public ILiteCollection<WorldSnapshotRow>        WorldSnapshots        => _db.GetCollection<WorldSnapshotRow>("worldSnapshots");
    public ILiteCollection<DamageTakenRow>          DamageTaken           => _db.GetCollection<DamageTakenRow>("damageTakenEvents");
    public ILiteCollection<DamageDealtRow>          DamageDealt           => _db.GetCollection<DamageDealtRow>("damageDealtEvents");
    public ILiteCollection<NpcSpawnRow>             NpcSpawns             => _db.GetCollection<NpcSpawnRow>("npcSpawnEvents");
    public ILiteCollection<ItemCreatedRow>          ItemCreations         => _db.GetCollection<ItemCreatedRow>("itemCreatedEvents");
    public ILiteCollection<LoadoutSnapshotRow>      LoadoutSnapshots      => _db.GetCollection<LoadoutSnapshotRow>("loadoutSnapshots");
    public ILiteCollection<BuffEventRow>            BuffEvents            => _db.GetCollection<BuffEventRow>("buffEvents");
    public ILiteCollection<SegmentRow>              Segments              => _db.GetCollection<SegmentRow>("segments");
    // Wave 6: cross-session per-context per-mod cost baselines, keyed by fingerprint.
    // An additive collection (LiteDB creates it on first use), so it needs no schema
    // migration — old DBs simply have none until a session writes one.
    public ILiteCollection<ContextBaselineRow>      ContextBaselines      => _db.GetCollection<ContextBaselineRow>("contextBaselines");

    // The cross-session history rollup (DB rework wave 1). Two levels: one row per mod
    // (global lifetime, keyed InternalName) and one per (mod, modlist). Both additive —
    // LiteDB creates them on first fold; old DBs have none until the backfill or the
    // first session-end fold writes one. Permanent + tiny, so retention never trims them.
    public ILiteCollection<ModLifetimeRollupRow>    ModLifetimeRollups    => _db.GetCollection<ModLifetimeRollupRow>("modLifetimeRollups");
    public ILiteCollection<ModModlistRollupRow>     ModModlistRollups     => _db.GetCollection<ModModlistRollupRow>("modModlistRollups");

    public ProfilerDatabase(string root, Action<string, Exception?>? log = null, string profilerVersion = "")
        : this(root, StreamRegistry.Default(), log, profilerVersion) { }

    /// <summary>
    /// Open (or recover) the DB at <paramref name="root"/> with an explicit
    /// stream registry. Tests inject a subset to exercise specific streams
    /// in isolation; production callers use the parameterless ctor.
    /// </summary>
    public ProfilerDatabase(string root, StreamRegistry registry,
                            Action<string, Exception?>? log = null, string profilerVersion = "")
    {
        _root = root;
        _registry = registry;
        _log = log ?? ((_, _) => { });
        Directory.CreateDirectory(_root);

        RecoverIfNeeded();

        // v0.6 Phase δ3: apply short BSON field names to every record before
        // the first read or write happens. Centralised in BsonShortNames.cs
        // rather than scattered as [BsonField] attributes across 24 record
        // files (cross-storage-ram §4.1 + persistence §5.1).
        BsonShortNames.Apply(BsonMapper.Global);

        string connStr = $"Filename={Path.Combine(_root, PersistenceFileNames.Db)};Upgrade=true;Connection=direct";
        _db = new LiteDatabase(connStr);
        _db.Pragma("UTC_DATE", false);
        _db.Pragma("CHECKPOINT", 1000);

        EnsureSchemaVersion();
        EnsureAllIndexes();
        PreWarmCollections();

        _journal = new EventJournal(Path.Combine(_root, PersistenceFileNames.Journal));
        ReplayJournalIfNeeded();
        MarkCrashDetectedSessions();
        SweepExpiredWarmTier();
        TouchMetadata(profilerVersion);

        try { _db.Checkpoint(); }
        catch (Exception ex) { _log("ProfilerDatabase: initial checkpoint failed", ex); }

        _writer = new DbWriterThread(this, _journal, _log);

        StartRollupBackfillIfNeeded();
    }

    /// <summary>
    /// One-time rebuild of the cross-session rollup from existing sessions (DB rework
    /// wave 1b). Runs on a background task so it never blocks mod load, and only when the
    /// run-once marker is unset AND the rollup is still empty (a fresh upgrade) — so it
    /// never double-folds or clobbers live data. Completes before any world loads, so it
    /// does not contend with the writer thread on the rollup collections.
    /// </summary>
    private void StartRollupBackfillIfNeeded()
    {
        try
        {
            MetadataRow? meta = Metadata.FindById("metadata");
            if (meta?.RollupBackfillUtc != null) return; // already backfilled

            bool rollupEmpty = ModLifetimeRollups.Count() == 0;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    // Defensive: only rebuild from history when the rollup is genuinely
                    // empty. A non-empty rollup with an unset marker (e.g. a crash mid-
                    // backfill) is left as-is rather than risking a double-fold of sessions
                    // that have already aged out of the dedup ring.
                    if (rollupEmpty) History.RollupBackfill.Run(this, _log);
                    MarkRollupBackfillDone();
                }
                catch (Exception ex) { _log("ProfilerDatabase: rollup backfill task failed", ex); }
            });
        }
        catch (Exception ex) { _log("ProfilerDatabase: rollup backfill trigger failed", ex); }
    }

    private void MarkRollupBackfillDone()
    {
        try
        {
            MetadataRow? meta = Metadata.FindById("metadata");
            if (meta != null)
            {
                meta.RollupBackfillUtc = DateTime.UtcNow;
                Metadata.Update(meta);
            }
        }
        catch (Exception ex) { _log("ProfilerDatabase: rollup backfill marker write failed", ex); }
    }

    /// <summary>
    /// Dispatch a batch of ops to the right streams. Invoked by the writer
    /// thread. Each op routes through <see cref="StreamRegistry"/> to its
    /// owning stream; the stream applies it idempotently. Unknown kinds
    /// (defensive — a stream registration miss) log and skip.
    /// </summary>
    public void ApplyBatch(IReadOnlyList<DbWriteOp> batch)
    {
        if (batch == null || batch.Count == 0) return;
        for (int i = 0; i < batch.Count; i++)
        {
            DbWriteOp op = batch[i];
            IPersistenceStream? stream = _registry.Lookup(op.Kind);
            if (stream == null)
            {
                _log($"ProfilerDatabase: no stream handles op kind {op.Kind}", null);
                continue;
            }
            try
            {
                stream.Apply(in op, this);
            }
            catch (Exception ex)
            {
                _log($"ProfilerDatabase: stream '{stream.Name}' apply failed (kind {op.Kind})", ex);
            }
        }
    }

    public void Checkpoint() => _db.Checkpoint();

    /// <summary>
    /// Drain the writer queue, checkpoint the DB, and truncate the journal.
    /// Called at <c>OnWorldUnload</c> in addition to <see cref="Dispose"/>,
    /// because tModLoader doesn't always fire <c>Mod.Unload</c> on
    /// quit-to-desktop (especially on macOS); world-unload is the most
    /// reliable signal that a session ended cleanly.
    ///
    /// Safe to call multiple times; the writer simply re-flushes whatever
    /// has arrived since the last drain. The writer thread itself stays
    /// alive — only the queue contents drain and the journal truncates.
    /// </summary>
    public void DrainAndTruncateJournalForSessionEnd()
    {
        try
        {
            // Best-effort wait for the writer thread to drain whatever the
            // recorder enqueued at session-end.
            int spins = 0;
            while (_writer.ApproxQueueDepth > 0 && spins < 100)
            {
                System.Threading.Thread.Sleep(20);
                spins++;
            }
            _db.Checkpoint();
            _journal.TruncateOnCleanShutdown();
        }
        catch (Exception ex)
        {
            _log("ProfilerDatabase: drain-and-truncate at session-end failed", ex);
        }
    }

    /// <summary>
    /// Compact the DB: <c>Checkpoint()</c> first (per LiteDB issue #2152),
    /// then <c>Rebuild()</c>. Caller must guarantee no live world.
    /// </summary>
    public long Compact()
    {
        _db.Checkpoint();
        return _db.Rebuild();
    }

    /// <summary>File size of the main DB, in bytes.</summary>
    public long DbFileSize
    {
        get
        {
            string p = Path.Combine(_root, PersistenceFileNames.Db);
            return File.Exists(p) ? new FileInfo(p).Length : 0L;
        }
    }

    /// <summary>Rotate the bounded backup ring. Called on clean session-end.</summary>
    public void RotateBackups()
    {
        try
        {
            string mainFile = Path.Combine(_root, PersistenceFileNames.Db);
            if (!File.Exists(mainFile)) return;

            string oldest = Path.Combine(_root, PersistenceFileNames.BackupPrefix + (BackupKeep).ToString());
            if (File.Exists(oldest)) File.Delete(oldest);
            for (int n = BackupKeep - 1; n >= 1; n--)
            {
                string src = Path.Combine(_root, PersistenceFileNames.BackupPrefix + (n).ToString());
                if (File.Exists(src)) File.Move(src, Path.Combine(_root, PersistenceFileNames.BackupPrefix + (n + 1).ToString()));
            }
            File.Copy(mainFile, Path.Combine(_root, PersistenceFileNames.BackupPrefix + (1).ToString()), overwrite: true);
        }
        catch (Exception ex)
        {
            _log("ProfilerDatabase: backup rotation failed", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            _writer.Dispose();
            RotateBackups();
            _db.Dispose();
            _journal.TruncateOnCleanShutdown();
            _journal.Dispose();
        }
        catch (Exception ex)
        {
            _log("ProfilerDatabase: dispose failed", ex);
        }
        finally
        {
            _disposed = true;
        }
    }

    // ---- private bring-up paths -------------------------------------------

    private void RecoverIfNeeded()
    {
        string mainFile = Path.Combine(_root, PersistenceFileNames.Db);
        if (!File.Exists(mainFile)) return;

        try
        {
            using var probe = new LiteDatabase(
                $"Filename={mainFile};ReadOnly=true;Connection=direct");
            return;
        }
        catch (Exception openEx)
        {
            _log("ProfilerDatabase: main file failed to open; attempting backup recovery", openEx);
        }

        for (int n = 1; n <= BackupKeep; n++)
        {
            string bak = Path.Combine(_root, PersistenceFileNames.BackupPrefix + (n).ToString());
            if (!File.Exists(bak)) continue;
            try
            {
                using var probe = new LiteDatabase(
                    $"Filename={bak};ReadOnly=true;Connection=direct");
            }
            catch
            {
                continue;
            }

            string brokenPath = Path.Combine(_root,
                PersistenceFileNames.BrokenPrefix + DateTime.UtcNow.ToString("yyyyMMddTHHmmss"));
            try
            {
                File.Move(mainFile, brokenPath);
                File.Copy(bak, mainFile, overwrite: false);
                _log($"ProfilerDatabase: recovered from {Path.GetFileName(bak)}; broken file at {Path.GetFileName(brokenPath)}", null);
                return;
            }
            catch (Exception ex)
            {
                _log("ProfilerDatabase: backup promotion failed", ex);
            }
        }

        string ts = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");
        string quarantine = Path.Combine(_root, PersistenceFileNames.BrokenPrefix + ts);
        try
        {
            File.Move(mainFile, quarantine);
            _log($"ProfilerDatabase: no usable backup; quarantined main to {Path.GetFileName(quarantine)} and starting fresh", null);
        }
        catch (Exception ex)
        {
            _log("ProfilerDatabase: quarantine failed", ex);
        }
    }

    private void EnsureSchemaVersion()
    {
        // LiteDB returns BsonValue for Pragma; USER_VERSION is stored as
        // Int32. Defensive read: AsInt32 throws on Null/Type-mismatch,
        // so on a torn or oddly-built DB we fall through to version 0
        // (which then forces the migration path) rather than aborting
        // mod load.
        var pragma = _db.Pragma("USER_VERSION");
        int v = pragma != null && pragma.IsInt32 ? pragma.AsInt32 : 0;
        if (v == 0)
        {
            _db.Pragma("USER_VERSION", CurrentUserVersion);
            return;
        }
        if (v > CurrentUserVersion)
        {
            throw new InvalidOperationException(
                $"Profiler DB at user-version {v} is newer than this profiler (knows up to {CurrentUserVersion}); refusing to write.");
        }
        if (v < CurrentUserVersion)
        {
            Migrations.Apply(_db, v, CurrentUserVersion, _log);
            _db.Pragma("USER_VERSION", CurrentUserVersion);
        }
    }

    /// <summary>
    /// Walks every registered stream and lets it declare its own indexes.
    /// Adding a new stream's indexes is a stream-local change; the facade
    /// has no per-collection index list of its own.
    /// </summary>
    private void EnsureAllIndexes()
    {
        foreach (var stream in _registry.Streams)
        {
            try
            {
                stream.EnsureIndexes(this);
            }
            catch (Exception ex)
            {
                _log($"ProfilerDatabase: stream '{stream.Name}' EnsureIndexes failed", ex);
            }
        }
    }

    /// <summary>
    /// Inserts and immediately deletes a sentinel row in <c>sessions</c> so
    /// the DB file has grown past zero pages before the first real burst.
    /// Mitigates LiteDB #2401.
    /// </summary>
    private void PreWarmCollections()
    {
        if (Sessions.Count() > 0) return;
        try
        {
            var sentinel = new SessionRow
            {
                Id = ObjectId.NewObjectId(),
                StartedUtc = DateTime.UtcNow,
                ProfilerVersion = "sentinel",
                ModlistFingerprint = "sentinel",
                Mode = "sentinel",
                EndReason = "sentinel",
                Incomplete = false,
            };
            Sessions.Insert(sentinel);
            Sessions.Delete(sentinel.Id);
        }
        catch (Exception ex)
        {
            _log("ProfilerDatabase: pre-warm sentinel failed", ex);
        }
    }

    private void ReplayJournalIfNeeded()
    {
        if (_journal.FileSize == 0L) return;
        int replayed = 0;
        foreach (JournalLine line in _journal.Replay())
        {
            if (string.IsNullOrEmpty(line.Kind)) continue;
            if (!Enum.TryParse(line.Kind, out DbOpKind kind)) continue;

            IPersistenceStream? stream = _registry.Lookup(kind);
            if (stream == null) continue;

            try
            {
                DbWriteOp? op = stream.Reconstruct(line);
                if (op.HasValue)
                {
                    DbWriteOp value = op.Value;
                    stream.Apply(in value, this);
                    replayed++;
                }
            }
            catch (Exception ex)
            {
                _log($"ProfilerDatabase: journal replay skipped (kind={line.Kind})", ex);
            }
        }
        _journal.TruncateOnCleanShutdown();
        _log($"ProfilerDatabase: replayed {replayed} ops from journal", null);
    }

    private void MarkCrashDetectedSessions()
    {
        var orphans = Sessions.Find(x => x.Incomplete && x.EndedUtc == null).ToList();
        foreach (SessionRow row in orphans)
        {
            row.EndReason = "crash-detected";
            row.Incomplete = false;
            Sessions.Update(row);
        }
        if (orphans.Count > 0)
        {
            _log($"ProfilerDatabase: marked {orphans.Count} crash-detected session(s)", null);
        }
    }

    private void SweepExpiredWarmTier()
    {
        try
        {
            int removed = TickAggregatesWarm.DeleteMany(x => x.ExpireAtUtc < DateTime.UtcNow);
            if (removed > 0) _log($"ProfilerDatabase: swept {removed} expired warm row(s)", null);
        }
        catch (Exception ex)
        {
            _log("ProfilerDatabase: warm-tier sweep failed", ex);
        }
    }

    private void TouchMetadata(string profilerVersion)
    {
        try
        {
            var existing = Metadata.FindById("metadata");
            DateTime now = DateTime.UtcNow;
            if (existing == null)
            {
                Metadata.Insert(new MetadataRow
                {
                    Id = "metadata",
                    DbCreatedUtc = now,
                    LastOpenedUtc = now,
                    ProfilerVersionSeen = string.IsNullOrEmpty(profilerVersion)
                        ? new List<string>()
                        : new List<string> { profilerVersion },
                    SessionCount = 0,
                });
            }
            else
            {
                existing.LastOpenedUtc = now;
                if (!string.IsNullOrEmpty(profilerVersion) && !existing.ProfilerVersionSeen.Contains(profilerVersion))
                {
                    existing.ProfilerVersionSeen.Add(profilerVersion);
                }
                Metadata.Update(existing);
            }
        }
        catch (Exception ex)
        {
            _log("ProfilerDatabase: metadata touch failed", ex);
        }
    }
}

