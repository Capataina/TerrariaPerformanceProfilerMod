#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Insights;

namespace PerformanceProfiler.Web;

internal static partial class DashboardRouter
{
    // ----------------------------------------------------------------------
    // /api/insights — live insight records from InsightsEngine.
    // Kept for back-compat; new Insights tab reads the five endpoints below.
    // ----------------------------------------------------------------------
    private static string BuildInsights()
    {
        // Migration step 11 — insights via registry.
        var snap = Data.DataRegistry.Shared
            .Lookup<Insights.Publish.InsightsSnapshot>(Insights.Publish.InsightsStat.StreamName)?
            .CurrentSnapshot() ?? Insights.Publish.InsightsSnapshot.Empty;
        if (!snap.WorldLoaded || snap.Live == null)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, records = Array.Empty<object>() }, JsonOpts);
        }

        string[] modNames = HookInterceptor.ProfiledModNames;
        var records = new List<object>();

        object ToRecord(Insight rec)
        {
            string subjectName = rec.Subject.ModId >= 0 && rec.Subject.ModId < modNames.Length
                ? modNames[rec.Subject.ModId]
                : null!;
            return new
            {
                pattern = rec.Pattern.ToString(),
                confidence = rec.Confidence.ToString(),
                scope = rec.Scope.ToString(),
                audience = rec.Audience.ToString(),
                shortText = InsightRenderer.Render(rec, Audience.Player, Density.Short),
                mediumText = InsightRenderer.Render(rec, Audience.Player, Density.Medium),
                subjectModId = rec.Subject.ModId,
                subjectModName = subjectName,
                observedMs = rec.Magnitude.ObservedMs,
                baselineMs = rec.Magnitude.BaselineMs,
                ratioOrDelta = rec.Magnitude.RatioOrDelta,
                firstSeenTick = rec.FirstSeenTick,
                lastSeenTick = rec.LastSeenTick,
                confirmationCount = rec.ConfirmationCount,
            };
        }

        foreach (var rec in snap.Live) records.Add(ToRecord(rec));
        // Cross-session (LifetimeData) insights live outside the TTL'd live store; merge
        // them into the same kanban feed so the dormant LifetimeData badge finally shows.
        if (snap.CrossSession != null)
        {
            foreach (var rec in snap.CrossSession) records.Add(ToRecord(rec));
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            records,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/mod-observatory — per-mod cards (I1+I3+I4).
    // ----------------------------------------------------------------------
    private static string BuildModObservatory()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<ModObservatorySnapshot>(RolloutStreamNames.ModObservatory)?
            .CurrentSnapshot() ?? ModObservatorySnapshot.Empty;

        var cards = new List<object>(snap.Cards?.Count ?? 0);
        if (snap.Cards != null)
        {
            foreach (var c in snap.Cards)
            {
                var biome = new List<object>(c.BiomeAttendance?.Count ?? 0);
                if (c.BiomeAttendance != null)
                {
                    foreach (var b in c.BiomeAttendance)
                    {
                        biome.Add(new
                        {
                            biomeBitIndex = b.BiomeBitIndex,
                            biomeName = b.BiomeName,
                            ticks = b.Ticks,
                            sharePct = b.SharePct,
                        });
                    }
                }
                var loadout = new List<object>(c.TopLoadoutItems?.Count ?? 0);
                if (c.TopLoadoutItems != null)
                {
                    foreach (var li in c.TopLoadoutItems)
                    {
                        loadout.Add(new
                        {
                            itemType = li.ItemType,
                            itemName = li.ItemName,
                            slotKind = li.SlotKind,
                            equippedTicks = li.EquippedTicks,
                        });
                    }
                }
                cards.Add(new
                {
                    modId = c.ModId,
                    modName = c.ModName,
                    cpuSharePct = c.CpuSharePct,
                    smoothedMsThisTick = c.SmoothedMsThisTick,
                    averageMs = c.AverageMs,
                    roster = new
                    {
                        items = c.Roster.Items,
                        npcs = c.Roster.NPCs,
                        buffs = c.Roster.Buffs,
                        projectiles = c.Roster.Projectiles,
                        mounts = c.Roster.Mounts,
                        accessories = c.Roster.Accessories,
                        biomes = c.Roster.Biomes,
                        invasions = c.Roster.Invasions,
                        bosses = c.Roster.Bosses,
                    },
                    usage = new
                    {
                        itemsCreated = c.Usage.ItemsCreated,
                        npcsSpawned = c.Usage.NpcsSpawned,
                        npcsKilled = c.Usage.NpcsKilled,
                        bossesFought = c.Usage.BossesFought,
                        buffsApplied = c.Usage.BuffsApplied,
                        ticksInOwnedBiomes = c.Usage.TicksInOwnedBiomes,
                        invasionsFought = c.Usage.InvasionsFought,
                        accessoryEquippedTicks = c.Usage.AccessoryEquippedTicks,
                    },
                    usageSharePct = c.UsageSharePct,
                    biomeAttendance = biome,
                    topLoadoutItems = loadout,
                });
            }
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            activeCount = snap.ActiveCount,
            dormantCount = snap.DormantCount,
            cards,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/dormant — dormant content surface (I2).
    // ----------------------------------------------------------------------
    private static string BuildDormantSurface()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<DormantSurfaceSnapshot>(RolloutStreamNames.DormantSurface)?
            .CurrentSnapshot() ?? DormantSurfaceSnapshot.Empty;

        var entries = new List<object>(snap.Entries?.Count ?? 0);
        if (snap.Entries != null)
        {
            foreach (var e in snap.Entries)
            {
                entries.Add(new
                {
                    modId = e.ModId,
                    modName = e.ModName,
                    rosterSize = e.RosterSize,
                    usedCount = e.UsedCount,
                    usageRatio = e.UsageRatio,
                    dominantUnusedCategory = e.DominantUnusedCategory,
                });
            }
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            modsLoaded = snap.ModsLoaded,
            modsWithZeroUsage = snap.ModsWithZeroUsage,
            modsBelowFivePercentUsage = snap.ModsBelowFivePercentUsage,
            entries,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/cross-cutting — grouped cross-cutting signals (I5).
    // ----------------------------------------------------------------------
    private static string BuildCrossCutting()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<CrossCuttingSnapshot>(RolloutStreamNames.CrossCutting)?
            .CurrentSnapshot() ?? CrossCuttingSnapshot.Empty;

        var groups = new List<object>(snap.Groups?.Count ?? 0);
        if (snap.Groups != null)
        {
            foreach (var g in snap.Groups)
            {
                var leaders = new List<object>(g.Leaders?.Count ?? 0);
                if (g.Leaders != null)
                {
                    foreach (var l in g.Leaders)
                    {
                        leaders.Add(new
                        {
                            modId = l.ModId,
                            modName = l.ModName,
                            appearances = l.Appearances,
                        });
                    }
                }
                groups.Add(new
                {
                    signalClass = g.SignalClass,
                    leaders,
                });
            }
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            groups,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/engagement-cost — engagement-vs-cost scatter (I6).
    // ----------------------------------------------------------------------
    private static string BuildEngagementCost()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<EngagementCostSnapshot>(RolloutStreamNames.EngagementCost)?
            .CurrentSnapshot() ?? EngagementCostSnapshot.Empty;

        var dots = new List<object>(snap.Dots?.Count ?? 0);
        if (snap.Dots != null)
        {
            foreach (var d in snap.Dots)
            {
                dots.Add(new
                {
                    modId = d.ModId,
                    modName = d.ModName,
                    usageShare = d.UsageShare,
                    cpuShare = d.CpuShare,
                    rosterSize = d.RosterSize,
                });
            }
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            dots,
        }, JsonOpts);
    }

    // ----------------------------------------------------------------------
    // /api/mod-interaction — mod-pair Pearson correlation matrix (I7).
    // ----------------------------------------------------------------------
    private static string BuildModInteraction()
    {
        var snap = Data.DataRegistry.Shared
            .Lookup<ModInteractionSnapshot>(RolloutStreamNames.ModInteraction)?
            .CurrentSnapshot() ?? ModInteractionSnapshot.Empty;

        var topCoupled = new List<object>(snap.TopCoupled?.Count ?? 0);
        if (snap.TopCoupled != null)
        {
            foreach (var p in snap.TopCoupled)
            {
                topCoupled.Add(new
                {
                    modIdA = p.ModIdA,
                    modNameA = p.ModNameA,
                    modIdB = p.ModIdB,
                    modNameB = p.ModNameB,
                    pearson = p.Pearson,
                    samplesUsed = p.SamplesUsed,
                });
            }
        }
        return JsonSerializer.Serialize(new
        {
            worldLoaded = snap.WorldLoaded,
            modIds = snap.ModIds ?? Array.Empty<int>(),
            modNames = snap.ModNames ?? Array.Empty<string>(),
            matrixRowMajor = snap.MatrixRowMajor ?? Array.Empty<double>(),
            topCoupled,
        }, JsonOpts);
    }
}
