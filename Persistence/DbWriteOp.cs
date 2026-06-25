#nullable enable

using System.Collections.Generic;
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
/// Discriminator for <see cref="DbWriteOp"/>. The writer thread switches on
/// this to dispatch the payload to the correct collection.
/// </summary>
public enum DbOpKind : byte
{
    SessionStart,
    SessionEnd,
    Spike,
    Stall,
    ContextTransition,
    WarmAggregate,
    ColdAggregate,
    ArchiveAggregate,
    PerSessionModAggregateBatch,
    PerSessionHookAggregateBatch,
    Insight,
    UpsertWorld,
    UpsertModlist,
    UpsertMod,
    StallCluster,
    PlayerDeath,
    WorldSnapshot,
    DamageTaken,
    DamageDealt,
    NpcSpawn,
    ItemCreated,
    LoadoutSnapshot,
    BuffEvent,
    Segment,
}

/// <summary>
/// One unit of durable work, enqueued by the game thread, applied by the
/// writer thread. <see cref="Payload"/> is the typed record matching
/// <see cref="Kind"/>; mismatched kind/payload combinations are a writer
/// bug and surface as <c>Logger.Error</c>.
///
/// The struct is intentionally heap-light — the only allocation per op is
/// the payload object the caller constructs (a record class or a small
/// list). Channel.Writer.TryWrite then takes a single ref. There is no
/// boxing of the discriminator because the struct is the message type
/// itself.
/// </summary>
public readonly struct DbWriteOp
{
    public readonly DbOpKind Kind;
    public readonly ObjectId SessionId;
    public readonly object Payload;
    public readonly string EndReason;       // session-end only
    public readonly long DurationMs;        // session-end only
    public readonly long TicksObserved;     // session-end only

    private DbWriteOp(DbOpKind kind, ObjectId sessionId, object payload,
                      string endReason = "", long durationMs = 0L, long ticksObserved = 0L)
    {
        Kind = kind;
        SessionId = sessionId;
        Payload = payload;
        EndReason = endReason;
        DurationMs = durationMs;
        TicksObserved = ticksObserved;
    }

    public static DbWriteOp SessionStart(SessionRow row)
        => new DbWriteOp(DbOpKind.SessionStart, row.Id, row);

    public static DbWriteOp SessionEnd(ObjectId sessionId, string endReason, long durationMs, long ticksObserved)
        => new DbWriteOp(DbOpKind.SessionEnd, sessionId, new object(), endReason, durationMs, ticksObserved);

    public static DbWriteOp Spike(SpikeWindowRow row)
        => new DbWriteOp(DbOpKind.Spike, row.SessionId, row);

    public static DbWriteOp Stall(StallEventRow row)
        => new DbWriteOp(DbOpKind.Stall, row.SessionId, row);

    public static DbWriteOp ContextTransition(ContextTransitionRow row)
        => new DbWriteOp(DbOpKind.ContextTransition, row.SessionId, row);

    public static DbWriteOp WarmAggregate(TickAggregateWarm row)
        => new DbWriteOp(DbOpKind.WarmAggregate, row.SessionId, row);

    public static DbWriteOp ColdAggregate(TickAggregateCold row)
        => new DbWriteOp(DbOpKind.ColdAggregate, row.SessionId, row);

    public static DbWriteOp ArchiveAggregate(TickAggregateArchive row)
        => new DbWriteOp(DbOpKind.ArchiveAggregate, row.SessionId, row);

    public static DbWriteOp ModAggregateBatch(ObjectId sessionId, List<PerSessionModAggregate> rows)
        => new DbWriteOp(DbOpKind.PerSessionModAggregateBatch, sessionId, rows);

    public static DbWriteOp HookAggregateBatch(ObjectId sessionId, List<PerSessionHookAggregate> rows)
        => new DbWriteOp(DbOpKind.PerSessionHookAggregateBatch, sessionId, rows);

    public static DbWriteOp Insight(InsightRow row)
        => new DbWriteOp(DbOpKind.Insight, row.SessionId, row);

    public static DbWriteOp UpsertWorld(WorldRow row)
        => new DbWriteOp(DbOpKind.UpsertWorld, ObjectId.Empty, row);

    public static DbWriteOp UpsertModlist(ModlistRow row)
        => new DbWriteOp(DbOpKind.UpsertModlist, ObjectId.Empty, row);

    public static DbWriteOp UpsertMod(ModRow row)
        => new DbWriteOp(DbOpKind.UpsertMod, ObjectId.Empty, row);

    public static DbWriteOp StallCluster(StallClusterRow row)
        => new DbWriteOp(DbOpKind.StallCluster, row.SessionId, row);

    public static DbWriteOp PlayerDeath(PlayerDeathRow row)
        => new DbWriteOp(DbOpKind.PlayerDeath, row.SessionId, row);

    public static DbWriteOp WorldSnapshot(WorldSnapshotRow row)
        => new DbWriteOp(DbOpKind.WorldSnapshot, row.SessionId, row);

    public static DbWriteOp DamageTaken(DamageTakenRow row)
        => new DbWriteOp(DbOpKind.DamageTaken, row.SessionId, row);

    public static DbWriteOp DamageDealt(DamageDealtRow row)
        => new DbWriteOp(DbOpKind.DamageDealt, row.SessionId, row);

    public static DbWriteOp NpcSpawn(NpcSpawnRow row)
        => new DbWriteOp(DbOpKind.NpcSpawn, row.SessionId, row);

    public static DbWriteOp ItemCreated(ItemCreatedRow row)
        => new DbWriteOp(DbOpKind.ItemCreated, row.SessionId, row);

    public static DbWriteOp LoadoutSnapshot(LoadoutSnapshotRow row)
        => new DbWriteOp(DbOpKind.LoadoutSnapshot, row.SessionId, row);

    public static DbWriteOp BuffEvent(BuffEventRow row)
        => new DbWriteOp(DbOpKind.BuffEvent, row.SessionId, row);

    public static DbWriteOp Segment(SegmentRow row)
        => new DbWriteOp(DbOpKind.Segment, row.SessionId, row);
}
