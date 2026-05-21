#nullable enable

using LiteDB;
using PerformanceProfiler.Profiling.Persistence.Records;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// Centralised short-name mapping for every record property that's
/// stored verbatim in BSON. v0.5 paid the cost of full C# property
/// names (~12 chars average × ~12 fields/row) inline in every document —
/// per the cross-storage-ram dossier §4.1 that's ~135 bytes/row of
/// field-name overhead, ~475 KB/min at combat-scale event rates.
///
/// <para>
/// v0.6 uses LiteDB's fluent <see cref="BsonMapper"/> API to remap
/// every property to a short ASCII name. The mapping is concentrated
/// here rather than scattered as <c>[BsonField("...")]</c> attributes
/// across 24 record files. Same effect on the wire, cleaner change.
/// </para>
///
/// <para>
/// <b>Schema break warning</b>: v0.5 DBs cannot be read with the v0.6
/// mapper — the old field names ("SessionId") aren't in the v0.6
/// mapping. Per cross-storage-ram §6.5 this is a forward-only break;
/// old session data simply doesn't appear in v0.6 queries. The DB
/// schema version bump (USER_VERSION → 7, future delta phase) will
/// formalise this; for v0.6 the break is documented.
/// </para>
/// </summary>
internal static class BsonShortNames
{
    /// <summary>
    /// Apply the short-name mapping to a <see cref="BsonMapper"/>.
    /// Call once at <see cref="ProfilerDatabase"/> construction before
    /// the first read or write happens. Idempotent.
    /// </summary>
    public static void Apply(BsonMapper mapper)
    {
        // Common fields (appear on most rows) — keep their short names
        // consistent so a future cross-collection viewer doesn't need
        // per-collection field-name dictionaries.

        mapper.Entity<DamageTakenRow>()
            .Field(x => x.SessionId, "s")
            .Field(x => x.Tick, "t")
            .Field(x => x.UnixMs, "u")
            .Field(x => x.SourceKind, "sk")
            .Field(x => x.SourceId, "si")
            .Field(x => x.SourceName, "sn")
            .Field(x => x.DamageRaw, "dr")
            .Field(x => x.DamageDealt, "dd")
            .Field(x => x.Pvp, "p")
            .Field(x => x.Crit, "c")
            .Field(x => x.HpBefore, "hb")
            .Field(x => x.HpAfter, "ha")
            .Field(x => x.MaxHp, "mh")
            .Field(x => x.ActiveBuffs, "ab");

        mapper.Entity<DamageDealtRow>()
            .Field(x => x.SessionId, "s")
            .Field(x => x.Tick, "t")
            .Field(x => x.UnixMs, "u")
            .Field(x => x.Path, "pt")
            .Field(x => x.ItemId, "ii")
            .Field(x => x.ProjectileId, "pi")
            .Field(x => x.NpcType, "nt")
            .Field(x => x.NpcName, "nn")
            .Field(x => x.DamageDealt, "dd")
            .Field(x => x.Crit, "c")
            .Field(x => x.LoadoutFingerprint, "lf");

        mapper.Entity<BuffEventRow>()
            .Field(x => x.SessionId, "s")
            .Field(x => x.Tick, "t")
            .Field(x => x.UnixMs, "u")
            .Field(x => x.Edge, "e")
            .Field(x => x.BuffType, "bt")
            .Field(x => x.BuffName, "bn")
            .Field(x => x.OwningMod, "om")
            .Field(x => x.DurationTicks, "dt");

        mapper.Entity<LoadoutSnapshotRow>()
            .Field(x => x.SessionId, "s")
            .Field(x => x.Tick, "t")
            .Field(x => x.UnixMs, "u")
            .Field(x => x.Reason, "r")
            .Field(x => x.HeldItemType, "hi")
            .Field(x => x.HeldItemName, "hn")
            .Field(x => x.Slots, "sl")
            .Field(x => x.Fingerprint, "fp");

        mapper.Entity<NpcSpawnRow>()
            .Field(x => x.SessionId, "s")
            .Field(x => x.Tick, "t")
            .Field(x => x.UnixMs, "u")
            .Field(x => x.NpcType, "nt")
            .Field(x => x.NpcName, "nn")
            .Field(x => x.OwningMod, "om")
            .Field(x => x.SourceCategory, "sc")
            .Field(x => x.SourceContext, "sx")
            .Field(x => x.TileX, "tx")
            .Field(x => x.TileY, "ty")
            .Field(x => x.IsBoss, "ib");

        mapper.Entity<ItemCreatedRow>()
            .Field(x => x.SessionId, "s")
            .Field(x => x.Tick, "t")
            .Field(x => x.UnixMs, "u")
            .Field(x => x.ItemType, "it")
            .Field(x => x.ItemName, "in")
            .Field(x => x.OwningMod, "om")
            .Field(x => x.SourceContext, "sc")
            .Field(x => x.ContextCategory, "cc")
            .Field(x => x.Stack, "st");

        mapper.Entity<WorldSnapshotRow>()
            .Field(x => x.SessionId, "s")
            .Field(x => x.Tick, "t")
            .Field(x => x.UnixMs, "u")
            .Field(x => x.TileX, "tx")
            .Field(x => x.TileY, "ty")
            .Field(x => x.Hp, "hp")
            .Field(x => x.MaxHp, "mh")
            .Field(x => x.Mana, "mn")
            .Field(x => x.MaxMana, "mxm")
            .Field(x => x.PrimaryBiome, "pb")
            .Field(x => x.IsHardmode, "ih")
            .Field(x => x.GameMode, "gm")
            .Field(x => x.TimeOfDayBand, "tod")
            .Field(x => x.IsDayTime, "id")
            .Field(x => x.NpcCount, "nc")
            .Field(x => x.ProjectileCount, "pc")
            .Field(x => x.DustCount, "dc")
            .Field(x => x.ItemCount, "ic")
            .Field(x => x.PrimaryBoss, "pbs");

        mapper.Entity<PlayerDeathRow>()
            .Field(x => x.SessionId, "s")
            .Field(x => x.Tick, "t")
            .Field(x => x.UnixMs, "u")
            .Field(x => x.TileX, "tx")
            .Field(x => x.TileY, "ty")
            .Field(x => x.LastHp, "lh")
            .Field(x => x.MaxHp, "mh")
            .Field(x => x.ActiveBossNpcTypes, "ab")
            .Field(x => x.PrimaryBoss, "pb")
            .Field(x => x.DamageWeighting, "dw")
            .Field(x => x.DamageAttributionWindowSeconds, "dws")
            .Field(x => x.Summary, "sy");

        mapper.Entity<DeathDamageContributor>()
            .Field(x => x.SourceKind, "sk")
            .Field(x => x.SourceId, "si")
            .Field(x => x.SourceName, "sn")
            .Field(x => x.TotalDamage, "td")
            .Field(x => x.HitCount, "hc")
            .Field(x => x.Fraction, "f");

        mapper.Entity<StallEventRow>()
            .Field(x => x.SessionId, "s")
            .Field(x => x.TickIndex, "ti")
            .Field(x => x.UnixMs, "u")
            .Field(x => x.DurationMs, "dm")
            .Field(x => x.BaselineTickMs, "btm")
            .Field(x => x.Cause, "c")
            .Field(x => x.Severity, "sv")
            .Field(x => x.GcPauseDurationMs, "gpd")
            .Field(x => x.Gen0Collections, "g0")
            .Field(x => x.Gen1Collections, "g1")
            .Field(x => x.Gen2Collections, "g2")
            .Field(x => x.HeapDeltaBytes, "hd")
            .Field(x => x.CpuTimeDeltaMs, "ctd")
            .Field(x => x.Warming, "w")
            .Field(x => x.ClusterId, "ci")
            .Field(x => x.TopContributors, "tc");

        mapper.Entity<StallClusterRow>()
            .Field(x => x.SessionId, "s")
            .Field(x => x.StartTick, "st")
            .Field(x => x.EndTick, "et")
            .Field(x => x.StartUnixMs, "su")
            .Field(x => x.EndUnixMs, "eu")
            .Field(x => x.StallCount, "n")
            .Field(x => x.TotalDurationMs, "td")
            .Field(x => x.WorstDurationMs, "wd")
            .Field(x => x.SpanMs, "sp")
            .Field(x => x.DominantCause, "c")
            .Field(x => x.DominantContributorModId, "cm")
            .Field(x => x.DominantContributorName, "cn");

        mapper.Entity<SpikeWindowRow>()
            .Field(x => x.SessionId, "s")
            .Field(x => x.StartTick, "st")
            .Field(x => x.EndTick, "et")
            .Field(x => x.WorstTick, "wt")
            .Field(x => x.WorstFrameMs, "wf")
            .Field(x => x.BaselineMs, "bm")
            .Field(x => x.MadMs, "mm")
            .Field(x => x.Warming, "w")
            .Field(x => x.Context, "ctx")
            .Field(x => x.TopContributors, "tc")
            .Field(x => x.PerModCatMs, "pmc")
            .Field(x => x.PerModCatBytes, "pmb");

        mapper.Entity<ContextTransitionRow>()
            .Field(x => x.SessionId, "s")
            .Field(x => x.Tick, "t")
            .Field(x => x.Type, "ty")
            .Field(x => x.From, "f")
            .Field(x => x.To, "to")
            .Field(x => x.TickFrameMs, "tfm");

        mapper.Entity<SegmentRow>()
            .Field(x => x.SessionId, "s")
            .Field(x => x.Family, "fa")
            .Field(x => x.Key, "k")
            .Field(x => x.Name, "n")
            .Field(x => x.StartTick, "st")
            .Field(x => x.EndTick, "et")
            .Field(x => x.StartUnixMs, "su")
            .Field(x => x.EndUnixMs, "eu")
            .Field(x => x.DurationMs, "d")
            .Field(x => x.Ticks, "tc")
            .Field(x => x.TotalFrameMs, "tf")
            .Field(x => x.SpikeCount, "sp")
            .Field(x => x.StallCount, "sl")
            .Field(x => x.DeathCount, "dh")
            .Field(x => x.BossKillCount, "bk")
            .Field(x => x.Promoted, "pr")
            .Field(x => x.PromotionReason, "pn")
            .Field(x => x.ModIds, "mi")
            .Field(x => x.ModMs, "mm");

        // Tick aggregates / session row / modlist row / world row / metadata /
        // mod row keep their existing shape — they're low-volume (one per
        // session or one per ~1 Hz/min). The savings concentrate on the
        // high-volume event streams above.
    }
}
