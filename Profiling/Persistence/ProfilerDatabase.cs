#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using JsonSerializer = System.Text.Json.JsonSerializer;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// Facade owning the open <see cref="LiteDatabase"/>, the event journal, and
/// the writer thread. One instance per running mod, constructed at
/// <c>Mod.Load</c> and disposed at <c>Mod.Unload</c>.
///
/// Lifecycle:
/// <list type="number">
/// <item>ctor — open DB (recover if needed), set pragmas, ensure indexes,
/// replay journal, sweep warm tier, mark crash-detected sessions.</item>
/// <item>game thread enqueues ops via <see cref="Writer"/>.</item>
/// <item><see cref="Dispose"/> — drain queue, checkpoint, rotate backups,
/// truncate journal, close LiteDB.</item>
/// </list>
///
/// All four Project Invariants are upheld here. Invariant 4 (abort-clean on
/// host drift) maps to: if LiteDB construction throws, the mod degrades to
/// no-persistence and continues running — see the <c>try/catch</c> wrap in
/// <see cref="PerformanceProfiler.Load"/>.
/// </summary>
public sealed class ProfilerDatabase : IDisposable
{
    /// <summary>Schema version this profiler writes. Bumped on whole-DB-shape changes.</summary>
    public const int CurrentUserVersion = 1;

    /// <summary>Number of rotating backup files kept on disk.</summary>
    public const int BackupKeep = 3;

    /// <summary>Warm-tier retention from creation. 24h per §3 of the migration plan.</summary>
    public static readonly TimeSpan WarmRetention = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        WriteIndented = false,
        IncludeFields = false,
    };

    private readonly string _root;
    private readonly LiteDatabase _db;
    private readonly EventJournal _journal;
    private readonly DbWriterThread _writer;
    private readonly Action<string, Exception?> _log;
    private bool _disposed;

    public string Root => _root;

    public LiteDatabase RawDb => _db;
    public EventJournal Journal => _journal;
    public DbWriterThread Writer => _writer;

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

    /// <summary>
    /// Open (or recover) the DB at <paramref name="root"/>. Recovery is
    /// best-effort — a corrupt main file is quarantined to
    /// <c>profiler.litedb.broken-&lt;utc&gt;</c> and the most recent backup
    /// is promoted, or a fresh DB is created if all backups are unusable.
    /// </summary>
    public ProfilerDatabase(string root, Action<string, Exception?>? log = null, string profilerVersion = "")
    {
        _root = root;
        _log = log ?? ((_, _) => { });
        ProfilerPaths.EnsureDirectory();

        RecoverIfNeeded();

        string connStr = $"Filename={Path.Combine(_root, ProfilerPaths.DbFileName)};Upgrade=true;Connection=direct";
        _db = new LiteDatabase(connStr);
        _db.Pragma("UTC_DATE", false);
        _db.Pragma("CHECKPOINT", 1000);

        EnsureSchemaVersion();
        EnsureIndexes();
        PreWarmCollections();

        _journal = new EventJournal(Path.Combine(_root, ProfilerPaths.JournalFileName));
        ReplayJournalIfNeeded();
        MarkCrashDetectedSessions();
        SweepExpiredWarmTier();
        TouchMetadata(profilerVersion);

        try { _db.Checkpoint(); }
        catch (Exception ex) { _log("ProfilerDatabase: initial checkpoint failed", ex); }

        _writer = new DbWriterThread(this, _journal, _log);
    }

    /// <summary>
    /// Dispatch a batch of ops to the right collections. Invoked by the
    /// writer thread. Every op is upserted on its natural key so a journal
    /// replay (which re-runs ops that already landed) is idempotent.
    /// </summary>
    public void ApplyBatch(IReadOnlyList<DbWriteOp> batch)
    {
        if (batch == null || batch.Count == 0) return;
        for (int i = 0; i < batch.Count; i++)
        {
            ApplyOne(batch[i]);
        }
    }

    private void ApplyOne(DbWriteOp op)
    {
        switch (op.Kind)
        {
            case DbOpKind.SessionStart:
            {
                var row = (SessionRow)op.Payload;
                Sessions.Upsert(row);
                break;
            }
            case DbOpKind.SessionEnd:
            {
                var existing = Sessions.FindById(op.SessionId);
                if (existing != null)
                {
                    existing.EndedUtc = DateTime.UtcNow;
                    existing.DurationMs = op.DurationMs;
                    existing.TicksObserved = op.TicksObserved;
                    existing.EndReason = string.IsNullOrEmpty(op.EndReason) ? "clean" : op.EndReason;
                    existing.Incomplete = false;
                    Sessions.Update(existing);
                }
                break;
            }
            case DbOpKind.Spike:
                SpikeWindows.Upsert((SpikeWindowRow)op.Payload);
                break;
            case DbOpKind.Stall:
                Stalls.Upsert((StallEventRow)op.Payload);
                break;
            case DbOpKind.ContextTransition:
                ContextTransitions.Upsert((ContextTransitionRow)op.Payload);
                break;
            case DbOpKind.WarmAggregate:
            {
                var row = (TickAggregateWarm)op.Payload;
                // Idempotency: (sessionId, secondIndex) is the natural key.
                // FindOne is safe here — there is at most one match by design.
                var existing = TickAggregatesWarm.FindOne(x =>
                    x.SessionId == row.SessionId && x.SecondIndex == row.SecondIndex);
                if (existing == null) TickAggregatesWarm.Insert(row);
                else { row.Id = existing.Id; TickAggregatesWarm.Update(row); }
                break;
            }
            case DbOpKind.ColdAggregate:
            {
                var row = (TickAggregateCold)op.Payload;
                var existing = TickAggregatesCold.FindOne(x =>
                    x.SessionId == row.SessionId && x.MinuteIndex == row.MinuteIndex);
                if (existing == null) TickAggregatesCold.Insert(row);
                else { row.Id = existing.Id; TickAggregatesCold.Update(row); }
                break;
            }
            case DbOpKind.ArchiveAggregate:
            {
                var row = (TickAggregateArchive)op.Payload;
                var existing = TickAggregatesArchive.FindOne(x => x.SessionId == row.SessionId);
                if (existing == null) TickAggregatesArchive.Insert(row);
                else { row.Id = existing.Id; TickAggregatesArchive.Update(row); }
                break;
            }
            case DbOpKind.PerSessionModAggregateBatch:
            {
                var rows = (List<PerSessionModAggregate>)op.Payload;
                if (rows.Count == 0) break;
                // Wipe-and-insert keyed on sessionId. Cheap because the
                // count is bounded by mod count (~100s, not 1000s).
                PerSessionMods.DeleteMany(x => x.SessionId == op.SessionId);
                PerSessionMods.InsertBulk(rows);
                break;
            }
            case DbOpKind.PerSessionHookAggregateBatch:
            {
                var rows = (List<PerSessionHookAggregate>)op.Payload;
                if (rows.Count == 0) break;
                PerSessionHooks.DeleteMany(x => x.SessionId == op.SessionId);
                PerSessionHooks.InsertBulk(rows);
                break;
            }
            case DbOpKind.Insight:
                Insights.Upsert((InsightRow)op.Payload);
                break;
            case DbOpKind.UpsertWorld:
            {
                var row = (WorldRow)op.Payload;
                var existing = Worlds.FindOne(x => x.Name == row.Name && x.UniqueId == row.UniqueId);
                if (existing == null) Worlds.Insert(row);
                else { row.Id = existing.Id; Worlds.Update(row); }
                break;
            }
            case DbOpKind.UpsertModlist:
            {
                var row = (ModlistRow)op.Payload;
                var existing = Modlists.FindOne(x => x.Fingerprint == row.Fingerprint);
                if (existing == null) Modlists.Insert(row);
                else
                {
                    row.Id = existing.Id;
                    row.SessionCount = existing.SessionCount + 1;
                    if (existing.FirstSeenUtc != default) row.FirstSeenUtc = existing.FirstSeenUtc;
                    Modlists.Update(row);
                }
                break;
            }
            case DbOpKind.UpsertMod:
            {
                var row = (ModRow)op.Payload;
                var existing = Mods.FindOne(x =>
                    x.ModlistFingerprint == row.ModlistFingerprint && x.InternalName == row.InternalName);
                if (existing == null) Mods.Insert(row);
                else
                {
                    row.Id = existing.Id;
                    if (existing.FirstSeenUtc != default) row.FirstSeenUtc = existing.FirstSeenUtc;
                    // Append the new version entry if version changed.
                    if (existing.VersionSeen != row.VersionSeen)
                    {
                        row.VersionHistory = new List<ModVersionEntry>(existing.VersionHistory)
                        {
                            new ModVersionEntry { Version = row.VersionSeen, FirstUtc = DateTime.UtcNow, LastUtc = DateTime.UtcNow }
                        };
                    }
                    else
                    {
                        row.VersionHistory = existing.VersionHistory;
                    }
                    Mods.Update(row);
                }
                break;
            }
        }
    }

    public void Checkpoint() => _db.Checkpoint();

    /// <summary>
    /// Compact the DB: <c>Checkpoint()</c> first (per LiteDB issue #2152),
    /// then <c>Rebuild()</c>. Always at session-end, never during a session;
    /// caller must guarantee no live world.
    /// </summary>
    public long Compact()
    {
        _db.Checkpoint();
        return _db.Rebuild();
    }

    /// <summary>File size of the main DB, in bytes. Diagnostic.</summary>
    public long DbFileSize
    {
        get
        {
            string p = Path.Combine(_root, ProfilerPaths.DbFileName);
            return File.Exists(p) ? new FileInfo(p).Length : 0L;
        }
    }

    /// <summary>
    /// Rotate the bounded backup ring. Called from the writer thread on a
    /// clean session-end; never during a session.
    /// </summary>
    public void RotateBackups()
    {
        try
        {
            string mainFile = Path.Combine(_root, ProfilerPaths.DbFileName);
            if (!File.Exists(mainFile)) return;

            // Shift bak-(N-1) → bak-N, dropping the oldest.
            string oldest = ProfilerPaths.BackupPath(BackupKeep);
            if (File.Exists(oldest)) File.Delete(oldest);
            for (int n = BackupKeep - 1; n >= 1; n--)
            {
                string src = ProfilerPaths.BackupPath(n);
                if (File.Exists(src)) File.Move(src, ProfilerPaths.BackupPath(n + 1));
            }
            File.Copy(mainFile, ProfilerPaths.BackupPath(1), overwrite: true);
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
            // Drain the writer first (it owns final checkpoints).
            _writer.Dispose();
            // Rotate backups while LiteDB still holds an open handle so the
            // file we copy is the post-final-checkpoint state.
            RotateBackups();
            // Now close the DB.
            _db.Dispose();
            // Truncate the journal — every op the writer drained is in the DB,
            // which the freshly-rotated backup also contains.
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
        string mainFile = Path.Combine(_root, ProfilerPaths.DbFileName);
        if (!File.Exists(mainFile)) return;

        // Probe the file by opening read-only; if open works, we're fine.
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

        // Try the bounded backup ring in order (newest first).
        for (int n = 1; n <= BackupKeep; n++)
        {
            string bak = ProfilerPaths.BackupPath(n);
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
                ProfilerPaths.BrokenPrefix + DateTime.UtcNow.ToString("yyyyMMddTHHmmss"));
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

        // All backups failed. Quarantine the main file and start fresh.
        string ts = DateTime.UtcNow.ToString("yyyyMMddTHHmmss");
        string quarantine = Path.Combine(_root, ProfilerPaths.BrokenPrefix + ts);
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
        int v = (int)(long)_db.Pragma("USER_VERSION");
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

    private void EnsureIndexes()
    {
        Sessions.EnsureIndex(x => x.StartedUtc);
        Sessions.EnsureIndex(x => x.ModlistFingerprint);
        Modlists.EnsureIndex(x => x.Fingerprint, unique: true);
        Mods.EnsureIndex(x => x.ModlistFingerprint);
        Mods.EnsureIndex(x => x.InternalName);
        PerSessionMods.EnsureIndex(x => x.SessionId);
        PerSessionMods.EnsureIndex(x => x.ModInternalName);
        PerSessionHooks.EnsureIndex(x => x.SessionId);
        SpikeWindows.EnsureIndex(x => x.SessionId);
        SpikeWindows.EnsureIndex(x => x.WorstFrameMs);
        Stalls.EnsureIndex(x => x.SessionId);
        ContextTransitions.EnsureIndex(x => x.SessionId);
        TickAggregatesWarm.EnsureIndex(x => x.SessionId);
        TickAggregatesWarm.EnsureIndex(x => x.ExpireAtUtc);
        TickAggregatesCold.EnsureIndex(x => x.SessionId);
        TickAggregatesArchive.EnsureIndex(x => x.SessionId, unique: true);
        Insights.EnsureIndex(x => x.SessionId);
        Insights.EnsureIndex(x => x.PatternKey);
    }

    /// <summary>
    /// Inserts and immediately deletes a sentinel doc per collection so the
    /// underlying file is paged before the first real burst arrives.
    /// Mitigates LiteDB issue #2401 (ENSURE-page corruption when growing
    /// from zero pages under heavy bursts).
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
            try
            {
                DbWriteOp? reconstructed = ReconstructOp(line);
                if (reconstructed.HasValue)
                {
                    ApplyOne(reconstructed.Value);
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

    private static DbWriteOp? ReconstructOp(JournalLine line)
    {
        if (!Enum.TryParse(line.Kind, out DbOpKind kind)) return null;
        ObjectId sid = string.IsNullOrEmpty(line.SessionId) ? ObjectId.Empty : new ObjectId(line.SessionId);
        switch (kind)
        {
            case DbOpKind.SessionStart:
                return DbWriteOp.SessionStart(JsonSerializer.Deserialize<SessionRow>(line.Payload, JsonOpts)!);
            case DbOpKind.SessionEnd:
                return DbWriteOp.SessionEnd(sid, line.EndReason, line.DurationMs, line.TicksObserved);
            case DbOpKind.Spike:
                return DbWriteOp.Spike(JsonSerializer.Deserialize<SpikeWindowRow>(line.Payload, JsonOpts)!);
            case DbOpKind.Stall:
                return DbWriteOp.Stall(JsonSerializer.Deserialize<StallEventRow>(line.Payload, JsonOpts)!);
            case DbOpKind.ContextTransition:
                return DbWriteOp.ContextTransition(JsonSerializer.Deserialize<ContextTransitionRow>(line.Payload, JsonOpts)!);
            case DbOpKind.WarmAggregate:
                return DbWriteOp.WarmAggregate(JsonSerializer.Deserialize<TickAggregateWarm>(line.Payload, JsonOpts)!);
            case DbOpKind.ColdAggregate:
                return DbWriteOp.ColdAggregate(JsonSerializer.Deserialize<TickAggregateCold>(line.Payload, JsonOpts)!);
            case DbOpKind.ArchiveAggregate:
                return DbWriteOp.ArchiveAggregate(JsonSerializer.Deserialize<TickAggregateArchive>(line.Payload, JsonOpts)!);
            case DbOpKind.PerSessionModAggregateBatch:
                return DbWriteOp.ModAggregateBatch(sid,
                    JsonSerializer.Deserialize<List<PerSessionModAggregate>>(line.Payload, JsonOpts)!);
            case DbOpKind.PerSessionHookAggregateBatch:
                return DbWriteOp.HookAggregateBatch(sid,
                    JsonSerializer.Deserialize<List<PerSessionHookAggregate>>(line.Payload, JsonOpts)!);
            case DbOpKind.Insight:
                return DbWriteOp.Insight(JsonSerializer.Deserialize<InsightRow>(line.Payload, JsonOpts)!);
            case DbOpKind.UpsertWorld:
                return DbWriteOp.UpsertWorld(JsonSerializer.Deserialize<WorldRow>(line.Payload, JsonOpts)!);
            case DbOpKind.UpsertModlist:
                return DbWriteOp.UpsertModlist(JsonSerializer.Deserialize<ModlistRow>(line.Payload, JsonOpts)!);
            case DbOpKind.UpsertMod:
                return DbWriteOp.UpsertMod(JsonSerializer.Deserialize<ModRow>(line.Payload, JsonOpts)!);
        }
        return null;
    }

    private void MarkCrashDetectedSessions()
    {
        // Any row left with no EndedUtc is from a session that didn't run
        // the clean-end path. Flag it without inventing a duration —
        // the journal replay above is best-effort; if it couldn't recover
        // a clean end, "crash-detected" is the honest label.
        var orphans = Sessions.Find(x => x.Incomplete && x.EndedUtc == null).ToList();
        foreach (SessionRow row in orphans)
        {
            row.EndReason = "crash-detected";
            row.Incomplete = false;
            // We don't fabricate an EndedUtc — leave it null so consumers
            // know the run was crash-cut.
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
