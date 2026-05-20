#nullable enable

using PerformanceProfiler.Profiling.Persistence.Records;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

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

        recorder.OnNpcSpawn(new NpcSpawnRow
        {
            Tick = (long)Main.GameUpdateCount,
            UnixMs = Time.UnixMsNow(),
            NpcType = npc.type,
            NpcName = LangNameCache.Npc(npc.type),
            OwningMod = owningMod,
            SourceCategory = sourceCategory,
            SourceContext = sourceContext,
            TileX = npc.position.X / 16f,
            TileY = npc.position.Y / 16f,
            IsBoss = npc.boss || (npc.type >= 0 && npc.type < NPCID.Sets.ShouldBeCountedAsBoss.Length && NPCID.Sets.ShouldBeCountedAsBoss[npc.type]),
        });
    }
}
