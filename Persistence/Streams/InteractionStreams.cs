#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Persistence.Records;
using PerformanceProfiler.Profiling.Pools;

using PerformanceProfiler.Persistence;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
namespace PerformanceProfiler.Persistence.Streams;

/// <summary>
/// One file for the six interaction-tracking streams added in v0.5
/// (damage taken, damage dealt, npc spawn, item created, loadout
/// snapshot, buff event). Each is a tiny <see cref="IPersistenceStream"/>
/// — same pattern as the existing per-collection streams; kept together
/// here because adding six separate one-screen files for a single
/// architectural addition is noise.
///
/// <para>
/// v0.6.1: every Apply now returns the row to <see cref="RowPool{T}"/>
/// after the Upsert completes. Game thread Rents → fills → enqueues →
/// writer thread Applies → Returns. Net: zero per-event row allocation
/// in steady state, since the same handful of pool instances cycle
/// between game thread and writer thread forever.
/// </para>
/// </summary>
internal sealed class DamageTakenStream : IPersistenceStream
{
    public string Name => "damageTakenEvents";
    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.DamageTaken };
    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        var row = (DamageTakenRow)op.Payload;
        db.DamageTaken.Upsert(row);
        RowPool<DamageTakenRow>.Return(row);
    }
    public DbWriteOp? Reconstruct(JournalLine line) => line.Kind == nameof(DbOpKind.DamageTaken)
        ? DbWriteOp.DamageTaken(StreamJson.Deserialize<DamageTakenRow>(line.Payload)) : null;
    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.DamageTaken.EnsureIndex(x => x.SessionId);
        db.DamageTaken.EnsureIndex(x => x.UnixMs);
    }
}

internal sealed class DamageDealtStream : IPersistenceStream
{
    public string Name => "damageDealtEvents";
    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.DamageDealt };
    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        var row = (DamageDealtRow)op.Payload;
        db.DamageDealt.Upsert(row);
        RowPool<DamageDealtRow>.Return(row);
    }
    public DbWriteOp? Reconstruct(JournalLine line) => line.Kind == nameof(DbOpKind.DamageDealt)
        ? DbWriteOp.DamageDealt(StreamJson.Deserialize<DamageDealtRow>(line.Payload)) : null;
    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.DamageDealt.EnsureIndex(x => x.SessionId);
        db.DamageDealt.EnsureIndex(x => x.UnixMs);
        db.DamageDealt.EnsureIndex(x => x.LoadoutFingerprint);
    }
}

internal sealed class NpcSpawnStream : IPersistenceStream
{
    public string Name => "npcSpawnEvents";
    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.NpcSpawn };
    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        var row = (NpcSpawnRow)op.Payload;
        db.NpcSpawns.Upsert(row);
        RowPool<NpcSpawnRow>.Return(row);
    }
    public DbWriteOp? Reconstruct(JournalLine line) => line.Kind == nameof(DbOpKind.NpcSpawn)
        ? DbWriteOp.NpcSpawn(StreamJson.Deserialize<NpcSpawnRow>(line.Payload)) : null;
    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.NpcSpawns.EnsureIndex(x => x.SessionId);
        db.NpcSpawns.EnsureIndex(x => x.SourceCategory);
        db.NpcSpawns.EnsureIndex(x => x.NpcType);
    }
}

internal sealed class ItemCreatedStream : IPersistenceStream
{
    public string Name => "itemCreatedEvents";
    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.ItemCreated };
    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        var row = (ItemCreatedRow)op.Payload;
        db.ItemCreations.Upsert(row);
        RowPool<ItemCreatedRow>.Return(row);
    }
    public DbWriteOp? Reconstruct(JournalLine line) => line.Kind == nameof(DbOpKind.ItemCreated)
        ? DbWriteOp.ItemCreated(StreamJson.Deserialize<ItemCreatedRow>(line.Payload)) : null;
    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.ItemCreations.EnsureIndex(x => x.SessionId);
        db.ItemCreations.EnsureIndex(x => x.ContextCategory);
    }
}

internal sealed class LoadoutSnapshotStream : IPersistenceStream
{
    public string Name => "loadoutSnapshots";
    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.LoadoutSnapshot };
    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        var row = (LoadoutSnapshotRow)op.Payload;
        db.LoadoutSnapshots.Upsert(row);
        RowPool<LoadoutSnapshotRow>.Return(row);
    }
    public DbWriteOp? Reconstruct(JournalLine line) => line.Kind == nameof(DbOpKind.LoadoutSnapshot)
        ? DbWriteOp.LoadoutSnapshot(StreamJson.Deserialize<LoadoutSnapshotRow>(line.Payload)) : null;
    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.LoadoutSnapshots.EnsureIndex(x => x.SessionId);
        db.LoadoutSnapshots.EnsureIndex(x => x.Fingerprint);
    }
}

internal sealed class BuffEventStream : IPersistenceStream
{
    public string Name => "buffEvents";
    public IReadOnlyList<DbOpKind> Kinds { get; } = new[] { DbOpKind.BuffEvent };
    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        var row = (BuffEventRow)op.Payload;
        db.BuffEvents.Upsert(row);
        RowPool<BuffEventRow>.Return(row);
    }
    public DbWriteOp? Reconstruct(JournalLine line) => line.Kind == nameof(DbOpKind.BuffEvent)
        ? DbWriteOp.BuffEvent(StreamJson.Deserialize<BuffEventRow>(line.Payload)) : null;
    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.BuffEvents.EnsureIndex(x => x.SessionId);
        db.BuffEvents.EnsureIndex(x => x.BuffType);
        db.BuffEvents.EnsureIndex(x => x.UnixMs);
    }
}
