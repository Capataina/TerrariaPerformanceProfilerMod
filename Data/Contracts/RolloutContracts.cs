#nullable enable

using System;
using System.Collections.Generic;

namespace PerformanceProfiler.Data.Contracts;

// =====================================================================
//   v0.12 ROLLOUT CONTRACTS — frozen snapshot signatures
// =====================================================================
//
// Locked in Wave 0 of the tab-rework rollout so that downstream
// implementing agents (Wave 1 foundations, Wave 2 data layer, Wave 3
// UI build) all read from the same shape. Anything that ends up in a
// /api/* endpoint and is consumed by a renderer goes through one of
// these snapshot types.
//
// Rules of the contract:
//   * Every snapshot is an immutable struct or readonly record class.
//   * Every collection-typed field is IReadOnlyList<T> or
//     IReadOnlyDictionary<TKey,TValue>; never a live mutable
//     reference. Producers copy into a stable shape if the source
//     ring is mutated from another thread.
//   * Every snapshot exposes an Empty default that consumers can
//     fall back to when no world is loaded.
//   * String values that read as user-facing copy must pass the
//     Invariant-3 descriptive-not-normative guard at the producer.
//
// =====================================================================

// ---------------------------------------------------------------------
//   FOUNDATIONS (F1 / F2 / F3)
// ---------------------------------------------------------------------

/// <summary>Per-mod content inventory captured once at PostSetupContent.</summary>
public readonly record struct ModRosterEntry(
    int ModId,
    string ModName,
    int Items,
    int NPCs,
    int Buffs,
    int Projectiles,
    int Mounts,
    int Accessories,
    int Biomes,
    int Invasions,
    int Bosses);

public readonly struct ModRosterSnapshot
{
    public readonly bool Scanned;
    public readonly IReadOnlyList<ModRosterEntry> Mods;
    public ModRosterSnapshot(bool scanned, IReadOnlyList<ModRosterEntry> mods)
        { Scanned = scanned; Mods = mods; }
    public static readonly ModRosterSnapshot Empty = new(false, Array.Empty<ModRosterEntry>());
}

/// <summary>
/// Per-mod session usage counters from F2 PerModUsageAggregator.
///
/// <para>
/// Two distinct axes live here, deliberately not conflated (Wave 4 of the
/// insights rework): the <b>creation / encounter</b> counts (ItemsCreated,
/// NpcsSpawned, NpcsKilled, BossesFought, BuffsApplied, InvasionsFought) record
/// what was made or met this session, while the <b>active-use tick</b> counts
/// (ItemsHeldTicks, AccessoryEquippedTicks, ArmorEquippedTicks, TicksInOwnedBiomes)
/// record what was actually wielded, worn, or stood in. The usage weight
/// (<see cref="PerformanceProfiler.Insights.Shared.ModMetrics.UsageWeight"/>) reads
/// the active-use axis only — a weapon held but not crafted now counts, which the
/// old creation-only proxy missed (the Flute-reads-zero bug).
/// </para>
/// </summary>
public readonly record struct ModUsageEntry(
    int ModId,
    long ItemsCreated,
    long NpcsSpawned,
    long NpcsKilled,
    long BossesFought,
    long BuffsApplied,
    long TicksInOwnedBiomes,
    long InvasionsFought,
    long AccessoryEquippedTicks,
    long ItemsHeldTicks,
    long ArmorEquippedTicks);

public readonly struct ModUsageSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<ModUsageEntry> Entries;
    public ModUsageSnapshot(bool worldLoaded, IReadOnlyList<ModUsageEntry> entries)
        { WorldLoaded = worldLoaded; Entries = entries; }
    public static readonly ModUsageSnapshot Empty = new(false, Array.Empty<ModUsageEntry>());
}

/// <summary>1Hz per-mod cost bucket from F3 PerModCostTimeSeriesAggregator.</summary>
public readonly record struct ModCostBucket(
    long UnixMs,
    long TickIndex,
    IReadOnlyList<double> PerModMs); // index = modId

public readonly struct ModCostTimeSeriesSnapshot
{
    public readonly bool WorldLoaded;
    public readonly int ModCount;
    public readonly IReadOnlyList<ModCostBucket> Buckets; // most-recent last
    public ModCostTimeSeriesSnapshot(bool worldLoaded, int modCount, IReadOnlyList<ModCostBucket> buckets)
        { WorldLoaded = worldLoaded; ModCount = modCount; Buckets = buckets; }
    public static readonly ModCostTimeSeriesSnapshot Empty = new(false, 0, Array.Empty<ModCostBucket>());
}

// ---------------------------------------------------------------------
//   TIMELINE (T1–T7)
// ---------------------------------------------------------------------

/// <summary>Per-segment lifetime baseline + delta vs lifetime avg. T1+T2.</summary>
public readonly record struct SegmentLifetimeEntry(
    long SegmentStartTick,
    int Family,        // matches SegmentFamily enum
    int Key,
    string Name,
    double LifetimeAvgMs,
    int LifetimeSampleCount,
    double ThisSegmentAvgMs,
    double DeltaFraction);  // (this - lifetime) / lifetime

public readonly struct SegmentLifetimeSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<SegmentLifetimeEntry> Recent;
    public SegmentLifetimeSnapshot(bool worldLoaded, IReadOnlyList<SegmentLifetimeEntry> recent)
        { WorldLoaded = worldLoaded; Recent = recent; }
    public static readonly SegmentLifetimeSnapshot Empty = new(false, Array.Empty<SegmentLifetimeEntry>());
}

/// <summary>Per-segment per-mod ms attribution for the waterfall view. T1.</summary>
public readonly record struct SegmentModAttribution(
    long SegmentStartTick,
    int Family,
    int Key,
    IReadOnlyList<int> ModIds,
    IReadOnlyList<double> ModMs);

public readonly struct SegmentModAttributionSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<SegmentModAttribution> Recent;
    public SegmentModAttributionSnapshot(bool worldLoaded, IReadOnlyList<SegmentModAttribution> recent)
        { WorldLoaded = worldLoaded; Recent = recent; }
    public static readonly SegmentModAttributionSnapshot Empty
        = new(false, Array.Empty<SegmentModAttribution>());
}

/// <summary>Timestamped context transitions projected onto the swimlane time domain. T3.</summary>
public readonly record struct TransitionEntry(
    long UnixMs,
    long TickIndex,
    string Type,    // "weather:BloodMoon" / "biome:Jungle" / "hardmode" / etc.
    string From,
    string To);

public readonly struct TransitionTrackSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<TransitionEntry> Transitions;
    public TransitionTrackSnapshot(bool worldLoaded, IReadOnlyList<TransitionEntry> transitions)
        { WorldLoaded = worldLoaded; Transitions = transitions; }
    public static readonly TransitionTrackSnapshot Empty = new(false, Array.Empty<TransitionEntry>());
}

/// <summary>Minute-bucketed session activity heat strip. T4.</summary>
public readonly record struct ActivityBucket(
    long MinuteIndex,
    long UnixMs,
    int SegmentCount,
    int SpikeCount,
    int StallCount,
    double AvgFrameMs);

public readonly struct ActivityHeatStripSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<ActivityBucket> Minutes;
    public ActivityHeatStripSnapshot(bool worldLoaded, IReadOnlyList<ActivityBucket> minutes)
        { WorldLoaded = worldLoaded; Minutes = minutes; }
    public static readonly ActivityHeatStripSnapshot Empty = new(false, Array.Empty<ActivityBucket>());
}

/// <summary>Per-mod context attendance roll-up over the session. T5.</summary>
public readonly record struct AttendanceEntry(
    int ModId,
    string ModName,
    long BiomeTicks,
    int InvasionCount,
    int BossSegmentCount);

public readonly struct AttendanceSnapshot
{
    public readonly bool WorldLoaded;
    public readonly long TotalBiomeTicks;
    public readonly long ModdedBiomeTicks;
    public readonly int TotalInvasions;
    public readonly int TotalBossSegments;
    public readonly IReadOnlyList<AttendanceEntry> ByMod;
    public AttendanceSnapshot(bool worldLoaded, long total, long modded, int invasions, int bossSegs,
        IReadOnlyList<AttendanceEntry> byMod)
    {
        WorldLoaded = worldLoaded; TotalBiomeTicks = total; ModdedBiomeTicks = modded;
        TotalInvasions = invasions; TotalBossSegments = bossSegs; ByMod = byMod;
    }
    public static readonly AttendanceSnapshot Empty = new(false, 0, 0, 0, 0, Array.Empty<AttendanceEntry>());
}

/// <summary>Per-death 30s pre-window reconstruction. T6.</summary>
public readonly record struct DeathReplayEvent(
    long OffsetMs,    // negative = before death
    string Kind,      // "buff-on" / "buff-off" / "damage" / "npc-spawn" / "item-created"
    string Label,
    int ModId,        // -1 if vanilla
    double Magnitude);

public readonly record struct DeathReplay(
    long DeathUnixMs,
    long DeathTickIndex,
    string PrimaryBiome,
    string PrimaryBoss,    // "" if none
    int FinalDamageModId,
    string FinalDamageSource,
    int FinalDamageAmount,
    IReadOnlyList<DeathReplayEvent> Events);

public readonly struct DeathReplaySnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<DeathReplay> Deaths;
    public DeathReplaySnapshot(bool worldLoaded, IReadOnlyList<DeathReplay> deaths)
        { WorldLoaded = worldLoaded; Deaths = deaths; }
    public static readonly DeathReplaySnapshot Empty = new(false, Array.Empty<DeathReplay>());
}

/// <summary>Session chronicle line — single descriptive sentence with timestamp. T7.</summary>
public readonly record struct ChronicleLine(
    long UnixMs,
    long TickIndex,
    string Kind,    // "join" / "spike" / "boss-start" / "boss-end" / "death" / "weather" / "transition" / "summary"
    string Text);   // descriptive sentence — Invariant-3 guarded at producer

public readonly struct SessionChronicleSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<ChronicleLine> Lines;
    public SessionChronicleSnapshot(bool worldLoaded, IReadOnlyList<ChronicleLine> lines)
        { WorldLoaded = worldLoaded; Lines = lines; }
    public static readonly SessionChronicleSnapshot Empty = new(false, Array.Empty<ChronicleLine>());
}

// ---------------------------------------------------------------------
//   LAG (L1, L2, L3, L4, L6, L7)
// ---------------------------------------------------------------------

/// <summary>Lag fingerprint cluster + cause×context cells. L1+L2.</summary>
public readonly record struct LagCluster(
    string FingerprintId,
    string CauseClass,
    string PrimaryBiome,
    string WeatherFlags,
    bool Hardmode,
    int TopModId,
    string TopModName,
    double TopModShare,    // 0..1
    int EventCount,
    double P95Ms,
    double TotalMs);

public readonly record struct CauseContextCell(
    string CauseClass,
    string Context,    // biome / weather / invasion / boss / "none"
    int EventCount,
    double TotalMs);

public readonly struct LagClusterSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<LagCluster> Clusters;
    public readonly IReadOnlyList<CauseContextCell> CauseContext;
    public LagClusterSnapshot(bool worldLoaded, IReadOnlyList<LagCluster> clusters,
        IReadOnlyList<CauseContextCell> causeContext)
        { WorldLoaded = worldLoaded; Clusters = clusters; CauseContext = causeContext; }
    public static readonly LagClusterSnapshot Empty
        = new(false, Array.Empty<LagCluster>(), Array.Empty<CauseContextCell>());
}

/// <summary>GC pressure narrative panel. L3.</summary>
public readonly struct GcPressureSnapshot
{
    public readonly bool WorldLoaded;
    public readonly double Gen0PerMin;
    public readonly double Gen1PerMin;
    public readonly double Gen2PerMin;
    public readonly long Gen0Total;
    public readonly long Gen1Total;
    public readonly long Gen2Total;
    public readonly long TotalPausedMs;
    public readonly long FreedMbDuringPauses;
    public readonly IReadOnlyList<double> HeapMbSeries;   // 1Hz, most-recent last
    public readonly double CurrentManagedMb;
    public GcPressureSnapshot(bool worldLoaded, double g0, double g1, double g2,
        long g0t, long g1t, long g2t, long paused, long freed,
        IReadOnlyList<double> series, double cur)
    {
        WorldLoaded = worldLoaded;
        Gen0PerMin = g0; Gen1PerMin = g1; Gen2PerMin = g2;
        Gen0Total = g0t; Gen1Total = g1t; Gen2Total = g2t;
        TotalPausedMs = paused; FreedMbDuringPauses = freed;
        HeapMbSeries = series; CurrentManagedMb = cur;
    }
    public static readonly GcPressureSnapshot Empty
        = new(false, 0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<double>(), 0);
}

/// <summary>Per-segment lag density. L4.</summary>
public readonly record struct SegmentLagDensityEntry(
    long SegmentStartTick,
    int Family,
    string Name,
    long DurationMs,
    int SpikeCount,
    int StallCount,
    double EventsPerMin,
    double VsBaseline);   // 1.0 = at session baseline

public readonly struct SegmentLagDensitySnapshot
{
    public readonly bool WorldLoaded;
    public readonly double SessionBaselinePerMin;
    public readonly IReadOnlyList<SegmentLagDensityEntry> Entries;
    public SegmentLagDensitySnapshot(bool worldLoaded, double baseline,
        IReadOnlyList<SegmentLagDensityEntry> entries)
        { WorldLoaded = worldLoaded; SessionBaselinePerMin = baseline; Entries = entries; }
    public static readonly SegmentLagDensitySnapshot Empty
        = new(false, 0, Array.Empty<SegmentLagDensityEntry>());
}

/// <summary>Allocation→GC causality chain per GC stall. L6.</summary>
public readonly record struct AllocCausalityContributor(
    int ModId,
    string ModName,
    long BytesInWindow,
    double SharePct);

public readonly record struct AllocCausalityChain(
    long GcStallUnixMs,
    long GcStallTickIndex,
    long PauseMs,
    long FreedBytes,
    string GcKind,    // "MajorGc" / "MinorGc"
    long WindowMs,    // typically 5000
    long TotalBytesInWindow,
    IReadOnlyList<AllocCausalityContributor> TopContributors);

public readonly struct AllocCausalitySnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<AllocCausalityChain> Chains;
    public AllocCausalitySnapshot(bool worldLoaded, IReadOnlyList<AllocCausalityChain> chains)
        { WorldLoaded = worldLoaded; Chains = chains; }
    public static readonly AllocCausalitySnapshot Empty = new(false, Array.Empty<AllocCausalityChain>());
}

/// <summary>Lag rhythm / periodicity. L7.</summary>
public readonly record struct RhythmCluster(
    double CentreSeconds,
    double WidthSeconds,
    int EventCount,
    double ShareOfSession,    // 0..1
    int TopModId,
    string TopModName,
    double TopModShare);

public readonly record struct RhythmHistogramBucket(
    double IntervalSeconds,
    int Count);

public readonly struct LagRhythmSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<RhythmHistogramBucket> Histogram;
    public readonly IReadOnlyList<RhythmCluster> Clusters;
    public LagRhythmSnapshot(bool worldLoaded, IReadOnlyList<RhythmHistogramBucket> hist,
        IReadOnlyList<RhythmCluster> clusters)
        { WorldLoaded = worldLoaded; Histogram = hist; Clusters = clusters; }
    public static readonly LagRhythmSnapshot Empty
        = new(false, Array.Empty<RhythmHistogramBucket>(), Array.Empty<RhythmCluster>());
}

// ---------------------------------------------------------------------
//   INSIGHTS (I1+I3+I4 / I2 / I5 / I6 / I7)
// ---------------------------------------------------------------------

/// <summary>Per-mod biome attendance breakdown for the observatory detail pane. I3.</summary>
public readonly record struct BiomeAttendanceEntry(
    int BiomeBitIndex,
    string BiomeName,
    long Ticks,
    double SharePct);

/// <summary>Per-mod loadout influence — top equipped items + shift count. I4.</summary>
public readonly record struct LoadoutInfluenceItem(
    int ItemType,
    string ItemName,
    string SlotKind,    // "armor" / "accessory" / "dye" / "vanity"
    long EquippedTicks);

/// <summary>One per-mod observatory card. Composes F1 roster + F2 usage + per-mod CPU + I3 + I4. I1.</summary>
public readonly record struct ObservatoryCard(
    int ModId,
    string ModName,
    double CpuSharePct,
    double SmoothedMsThisTick,
    double AverageMs,
    ModRosterEntry Roster,
    ModUsageEntry Usage,
    double UsageSharePct,
    IReadOnlyList<BiomeAttendanceEntry> BiomeAttendance,
    IReadOnlyList<LoadoutInfluenceItem> TopLoadoutItems);

public readonly struct ModObservatorySnapshot
{
    public readonly bool WorldLoaded;
    public readonly int ActiveCount;
    public readonly int DormantCount;
    public readonly IReadOnlyList<ObservatoryCard> Cards;
    public ModObservatorySnapshot(bool worldLoaded, int active, int dormant,
        IReadOnlyList<ObservatoryCard> cards)
        { WorldLoaded = worldLoaded; ActiveCount = active; DormantCount = dormant; Cards = cards; }
    public static readonly ModObservatorySnapshot Empty
        = new(false, 0, 0, Array.Empty<ObservatoryCard>());
}

/// <summary>Dormant content surface. I2.</summary>
public readonly record struct DormantEntry(
    int ModId,
    string ModName,
    int RosterSize,
    int UsedCount,
    double UsageRatio,    // 0..1
    string DominantUnusedCategory);    // "items" / "NPCs" / "buffs" / ...

public readonly struct DormantSurfaceSnapshot
{
    public readonly bool WorldLoaded;
    public readonly int ModsLoaded;
    public readonly int ModsWithZeroUsage;
    public readonly int ModsBelowFivePercentUsage;
    public readonly IReadOnlyList<DormantEntry> Entries;
    public DormantSurfaceSnapshot(bool worldLoaded, int loaded, int zero, int lowUse,
        IReadOnlyList<DormantEntry> entries)
    {
        WorldLoaded = worldLoaded; ModsLoaded = loaded;
        ModsWithZeroUsage = zero; ModsBelowFivePercentUsage = lowUse; Entries = entries;
    }
    public static readonly DormantSurfaceSnapshot Empty
        = new(false, 0, 0, 0, Array.Empty<DormantEntry>());
}

/// <summary>Cross-cutting signal aggregation. I5.</summary>
public readonly record struct CrossCuttingEntry(
    int ModId,
    string ModName,
    int Appearances);

public readonly record struct CrossCuttingGroup(
    string SignalClass,    // "SpikeTopContributor" / "AllocationBurst" / "SegmentOutlier" / ...
    IReadOnlyList<CrossCuttingEntry> Leaders);

public readonly struct CrossCuttingSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<CrossCuttingGroup> Groups;
    public CrossCuttingSnapshot(bool worldLoaded, IReadOnlyList<CrossCuttingGroup> groups)
        { WorldLoaded = worldLoaded; Groups = groups; }
    public static readonly CrossCuttingSnapshot Empty = new(false, Array.Empty<CrossCuttingGroup>());
}

/// <summary>Engagement-vs-cost scatter dot. I6.</summary>
public readonly record struct EngagementCostDot(
    int ModId,
    string ModName,
    double UsageShare,    // x-axis, 0..1
    double CpuShare,    // y-axis, 0..1
    int RosterSize);    // controls dot size

public readonly struct EngagementCostSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<EngagementCostDot> Dots;
    public EngagementCostSnapshot(bool worldLoaded, IReadOnlyList<EngagementCostDot> dots)
        { WorldLoaded = worldLoaded; Dots = dots; }
    public static readonly EngagementCostSnapshot Empty = new(false, Array.Empty<EngagementCostDot>());
}

/// <summary>Mod-pair cost correlation. I7.</summary>
public readonly record struct ModInteractionPair(
    int ModIdA,
    string ModNameA,
    int ModIdB,
    string ModNameB,
    double Pearson,    // -1..+1
    int SamplesUsed);

public readonly struct ModInteractionSnapshot
{
    public readonly bool WorldLoaded;
    public readonly IReadOnlyList<int> ModIds;
    public readonly IReadOnlyList<string> ModNames;
    public readonly IReadOnlyList<double> MatrixRowMajor;    // ModIds.Count^2 length; symmetric
    public readonly IReadOnlyList<ModInteractionPair> TopCoupled;
    public ModInteractionSnapshot(bool worldLoaded, IReadOnlyList<int> ids, IReadOnlyList<string> names,
        IReadOnlyList<double> matrix, IReadOnlyList<ModInteractionPair> top)
    {
        WorldLoaded = worldLoaded; ModIds = ids; ModNames = names;
        MatrixRowMajor = matrix; TopCoupled = top;
    }
    public static readonly ModInteractionSnapshot Empty = new(false,
        Array.Empty<int>(), Array.Empty<string>(), Array.Empty<double>(), Array.Empty<ModInteractionPair>());
}

// =====================================================================
//   STREAM NAMES — the canonical lookup keys for DataRegistry.Shared
// =====================================================================

public static class RolloutStreamNames
{
    // Foundations
    public const string ModRoster              = "modRoster";
    public const string PerModUsage            = "perModUsage";
    public const string PerModCostTimeSeries   = "perModCostTimeSeries";

    // Timeline
    public const string SegmentLifetime        = "segmentLifetime";
    public const string SegmentModAttribution  = "segmentModAttribution";
    public const string TransitionTrack        = "transitionTrack";
    public const string ActivityHeatStrip      = "activityHeatStrip";
    public const string Attendance             = "attendance";
    public const string DeathReplay            = "deathReplay";
    public const string SessionChronicle       = "sessionChronicle";

    // Lag
    public const string LagClusters            = "lagClusters";
    public const string GcPressure             = "gcPressure";
    public const string SegmentLagDensity      = "segmentLagDensity";
    public const string AllocCausality         = "allocCausality";
    public const string LagRhythm              = "lagRhythm";

    // Insights
    public const string ModObservatory         = "modObservatory";
    public const string DormantSurface         = "dormantSurface";
    public const string CrossCutting           = "crossCutting";
    public const string EngagementCost         = "engagementCost";
    public const string ModInteraction         = "modInteraction";
}
