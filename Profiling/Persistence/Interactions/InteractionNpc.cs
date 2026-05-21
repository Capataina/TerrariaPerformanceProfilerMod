#nullable enable

using PerformanceProfiler.Profiling.Persistence.Records;
using PerformanceProfiler.Profiling.Pools;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
namespace PerformanceProfiler.Profiling.Persistence.Interactions;

/// <summary>
/// Captures every NPC spawn via <see cref="GlobalNPC.OnSpawn"/>. The
/// <see cref="IEntitySource"/> subclass name is the universal "where did
/// this come from" hint — CheatSheet, HEROsMod, vanilla all surface the
/// same way (Invariant 5). The mod that owns the spawned NPC's type is
/// resolved dynamically from the loaded mod registry.
/// </summary>
internal sealed class InteractionNpc : GlobalNPC
{
    public override void OnSpawn(NPC npc, IEntitySource source)
    {
        var system = ModContent.GetInstance<ProfilerSystem>();
        var recorder = system?.LiveRecorder;
        if (recorder == null) return;

        // v0.6: ModOwnerCache.FromEntitySource strips the EntitySource_
        // prefix from the runtime type name. LangNameCache.Npc and
        // ModOwnerCache.ForNpc replace the inline lookup + dictionary
        // walk per cross-allocations §3.2 / §3.5.
        string sourceCategory = ModOwnerCache.FromEntitySource(source);
        string sourceContext = source?.Context ?? "";
        string owningMod = ModOwnerCache.ForNpc(npc.type);

        // v0.6.1: pooled row.
        var row = RowPool<NpcSpawnRow>.Rent();
        row.Tick = (long)Main.GameUpdateCount;
        row.UnixMs = Time.UnixMsNow();
        row.NpcType = npc.type;
        row.NpcName = LangNameCache.Npc(npc.type);
        row.OwningMod = owningMod;
        row.SourceCategory = sourceCategory;
        row.SourceContext = sourceContext;
        row.TileX = npc.position.X / 16f;
        row.TileY = npc.position.Y / 16f;
        row.IsBoss = npc.boss || (npc.type >= NPCID.None && npc.type < NPCID.Sets.ShouldBeCountedAsBoss.Length && NPCID.Sets.ShouldBeCountedAsBoss[npc.type]);
        recorder.OnNpcSpawn(row);

        // F2: per-mod usage counter — modded NPCs only.
        if (npc.type >= NPCID.Count)
        {
            PerModUsageAggregator.Live?
                .IncrementNpcSpawned(owningMod);
        }
    }

    /// <summary>F2 kill counter only. The kill is observed read-only; no
    /// row is enqueued (death attribution lives in the death-replay stream).
    /// Generic OnKill surface — Invariant 5 clean, fires for every mod's
    /// NPCs without naming any of them.</summary>
    public override void OnKill(NPC npc)
    {
        if (npc.type < NPCID.Count) return; // vanilla — no mod attribution
        string owningMod = ModOwnerCache.ForNpc(npc.type);
        PerModUsageAggregator.Live?
            .IncrementNpcKilled(owningMod);
    }
}
