#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// Timeline T6 — per-death 30s pre-death event reconstruction.
///
/// <para>
/// For each of the most-recent player deaths in the live session, build a
/// chronological event list covering the 30 s window leading into the
/// death: buff edges (on/off), damage taken events, NPC spawns, and item
/// creations / pickups. Mod attribution per event is resolved through
/// <see cref="ModOwnerCache"/> (NPC type → mod, item type → mod) and then
/// the mod-name → ModId reverse lookup via
/// <see cref="HookInterceptor.ProfiledModNames"/>. ModId = -1 for vanilla
/// or unresolved sources, matching the contract.
/// </para>
///
/// <para>
/// Invariant 1 (read-only): we only query the DB; we never mutate.
/// Invariant 5: no string-matching against any specific mod name; mod
/// ownership is resolved through tModLoader's content-loader surfaces.
/// </para>
/// </summary>
public sealed class DeathReplayStat : IDataStat<DeathReplaySnapshot>
{
    /// <summary>Window before each death we replay, in milliseconds.</summary>
    public const long WindowMs = 30_000L;

    /// <summary>How many recent deaths we surface per snapshot.</summary>
    public const int MaxDeaths = 5;

    /// <summary>Hard cap on events per death (defensive — busy fights can be loud).</summary>
    public const int MaxEventsPerDeath = 300;

    public string Name => RolloutStreamNames.DeathReplay;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public DeathReplaySnapshot CurrentSnapshot()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        ProfilerDatabase? db = PerformanceProfiler.Database;
        ObjectId? sid = sys?.LiveRecorderSessionId;
        if (db == null || sid == null)
        {
            return DeathReplaySnapshot.Empty;
        }

        List<PlayerDeathRow> deaths;
        try
        {
            deaths = db.PlayerDeaths
                .Find(p => p.SessionId == sid)
                .OrderByDescending(p => p.UnixMs)
                .Take(MaxDeaths)
                .ToList();
        }
        catch
        {
            return new DeathReplaySnapshot(worldLoaded: sys?.Collector != null,
                Array.Empty<DeathReplay>());
        }
        if (deaths.Count == 0)
        {
            return new DeathReplaySnapshot(worldLoaded: sys?.Collector != null,
                Array.Empty<DeathReplay>());
        }

        string[] modNames = HookInterceptor.ProfiledModNames;
        List<DeathReplay> result = new(deaths.Count);

        foreach (PlayerDeathRow death in deaths)
        {
            long windowStart = death.UnixMs - WindowMs;
            long windowEnd = death.UnixMs;
            ObjectId deathSid = death.SessionId;

            List<DeathReplayEvent> events = new(64);

            // Buff edges.
            try
            {
                foreach (BuffEventRow b in db.BuffEvents
                             .Find(x => x.SessionId == deathSid
                                  && x.UnixMs >= windowStart && x.UnixMs <= windowEnd))
                {
                    int modId = ResolveModId(b.OwningMod, modNames);
                    string kind = string.Equals(b.Edge, "off", StringComparison.Ordinal)
                        ? "buff-off" : "buff-on";
                    events.Add(new DeathReplayEvent(
                        OffsetMs: b.UnixMs - death.UnixMs,
                        Kind: kind,
                        Label: string.IsNullOrEmpty(b.BuffName) ? ("buff-" + b.BuffType) : b.BuffName,
                        ModId: modId,
                        Magnitude: 0d));
                }
            }
            catch { }

            // NPC spawns.
            try
            {
                foreach (NpcSpawnRow n in db.NpcSpawns
                             .Find(x => x.SessionId == deathSid
                                  && x.UnixMs >= windowStart && x.UnixMs <= windowEnd))
                {
                    int modId = ResolveModIdFromType(ModOwnerCache.Kind.Npc, n.NpcType, n.OwningMod, modNames);
                    events.Add(new DeathReplayEvent(
                        OffsetMs: n.UnixMs - death.UnixMs,
                        Kind: "npc-spawn",
                        Label: string.IsNullOrEmpty(n.NpcName) ? ("npc-" + n.NpcType) : n.NpcName,
                        ModId: modId,
                        Magnitude: 0d));
                }
            }
            catch { }

            // Item creations.
            try
            {
                foreach (ItemCreatedRow it in db.ItemCreations
                             .Find(x => x.SessionId == deathSid
                                  && x.UnixMs >= windowStart && x.UnixMs <= windowEnd))
                {
                    int modId = ResolveModIdFromType(ModOwnerCache.Kind.Item, it.ItemType, it.OwningMod, modNames);
                    events.Add(new DeathReplayEvent(
                        OffsetMs: it.UnixMs - death.UnixMs,
                        Kind: "item-created",
                        Label: string.IsNullOrEmpty(it.ItemName) ? ("item-" + it.ItemType) : it.ItemName,
                        ModId: modId,
                        Magnitude: it.Stack));
                }
            }
            catch { }

            // Damage taken.
            try
            {
                foreach (DamageTakenRow d in db.DamageTaken
                             .Find(x => x.SessionId == deathSid
                                  && x.UnixMs >= windowStart && x.UnixMs <= windowEnd))
                {
                    int modId = ResolveDamageModId(d, modNames);
                    events.Add(new DeathReplayEvent(
                        OffsetMs: d.UnixMs - death.UnixMs,
                        Kind: "damage",
                        Label: string.IsNullOrEmpty(d.SourceName) ? d.SourceKind : d.SourceName,
                        ModId: modId,
                        Magnitude: d.DamageDealt));
                }
            }
            catch { }

            // Order chronologically; cap defensively.
            events.Sort(static (a, b) => a.OffsetMs.CompareTo(b.OffsetMs));
            if (events.Count > MaxEventsPerDeath)
            {
                events = events.GetRange(events.Count - MaxEventsPerDeath, MaxEventsPerDeath);
            }

            // Killer-by-damage from the persisted damage weighting (top entry
            // is the highest-share source over the death window).
            int finalModId = -1;
            string finalSrc = "(unknown)";
            int finalAmt = 0;
            if (death.DamageWeighting != null && death.DamageWeighting.Count > 0)
            {
                DeathDamageContributor top = death.DamageWeighting[0];
                finalSrc = string.IsNullOrEmpty(top.SourceName) ? top.SourceKind : top.SourceName;
                finalAmt = top.TotalDamage;
                finalModId = ResolveDamageModIdFromContributor(top, modNames);
            }

            string primaryBiome = ResolvePrimaryBiome(); // documented seam; always "" today
            result.Add(new DeathReplay(
                DeathUnixMs: death.UnixMs,
                DeathTickIndex: death.Tick,
                PrimaryBiome: primaryBiome,
                PrimaryBoss: death.PrimaryBoss ?? string.Empty,
                FinalDamageModId: finalModId,
                FinalDamageSource: finalSrc,
                FinalDamageAmount: finalAmt,
                Events: events));
        }

        return new DeathReplaySnapshot(worldLoaded: sys?.Collector != null, result);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();

    /// <summary>
    /// Reverse-lookup a mod's internal name in
    /// <see cref="HookInterceptor.ProfiledModNames"/>. Returns -1 for
    /// vanilla / unknown.
    /// </summary>
    private static int ResolveModId(string? owningMod, string[] modNames)
    {
        if (string.IsNullOrEmpty(owningMod) || owningMod == "Terraria") return -1;
        for (int i = 0; i < modNames.Length; i++)
        {
            if (string.Equals(modNames[i], owningMod, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Resolve the owning mod from a content type id via <see cref="ModOwnerCache"/>,
    /// preferring the row-stored <paramref name="rowOwner"/> when present so
    /// reads stay zero-allocation against the cache hot path.
    /// </summary>
    private static int ResolveModIdFromType(ModOwnerCache.Kind kind, int typeId, string? rowOwner, string[] modNames)
    {
        if (!string.IsNullOrEmpty(rowOwner))
        {
            return ResolveModId(rowOwner, modNames);
        }
        string name = kind switch
        {
            ModOwnerCache.Kind.Npc => ModOwnerCache.ForNpc(typeId),
            ModOwnerCache.Kind.Item => ModOwnerCache.ForItem(typeId),
            ModOwnerCache.Kind.Projectile => ModOwnerCache.ForProjectile(typeId),
            ModOwnerCache.Kind.Buff => ModOwnerCache.ForBuff(typeId),
            _ => "Terraria",
        };
        return ResolveModId(name, modNames);
    }

    private static int ResolveDamageModId(DamageTakenRow d, string[] modNames)
    {
        return d.SourceKind switch
        {
            "npc"        => ResolveModIdFromType(ModOwnerCache.Kind.Npc, d.SourceId, null, modNames),
            "projectile" => ResolveModIdFromType(ModOwnerCache.Kind.Projectile, d.SourceId, null, modNames),
            "item"       => ResolveModIdFromType(ModOwnerCache.Kind.Item, d.SourceId, null, modNames),
            _ => -1,
        };
    }

    private static int ResolveDamageModIdFromContributor(DeathDamageContributor top, string[] modNames)
    {
        return top.SourceKind switch
        {
            "npc"        => ResolveModIdFromType(ModOwnerCache.Kind.Npc, top.SourceId, null, modNames),
            "projectile" => ResolveModIdFromType(ModOwnerCache.Kind.Projectile, top.SourceId, null, modNames),
            "item"       => ResolveModIdFromType(ModOwnerCache.Kind.Item, top.SourceId, null, modNames),
            _ => -1,
        };
    }

    /// <summary>
    /// Best-effort primary-biome read at snapshot time. The death row itself
    /// does not persist a biome and there is no cheap name path without
    /// allocating a bitset (PrimaryBitIndex needs one), so this is a documented
    /// seam that always returns empty today; a future refinement could sample
    /// the player's zone state. Kept as a method so the call site and intent
    /// are explicit.
    /// </summary>
    private static string ResolvePrimaryBiome()
    {
        return string.Empty;
    }
}
