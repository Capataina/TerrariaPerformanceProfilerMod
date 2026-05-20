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

        // IEntitySource is a class hierarchy; the runtime type name is what
        // distinguishes CheatSheet's debug-spawn from a natural spawn, a
        // boss-spawn-item, a SpawnNPC call, etc. Strip the EntitySource_
        // prefix for compactness; record the rest verbatim.
        string sourceCategory = source?.GetType().Name ?? "unknown";
        if (sourceCategory.StartsWith("EntitySource_"))
            sourceCategory = sourceCategory.Substring("EntitySource_".Length);

        string sourceContext = source?.Context ?? "";

        // Owning mod of the NPC type — vanilla types resolve to "Terraria",
        // modded types to their mod's internal name. Read from tML's
        // dynamic registry, never from a hardcoded list.
        string owningMod = "Terraria";
        if (npc.type >= NPCID.Count)
        {
            var modNpc = NPCLoader.GetNPC(npc.type);
            if (modNpc != null) owningMod = modNpc.Mod?.Name ?? "Terraria";
        }

        recorder.OnNpcSpawn(new NpcSpawnRow
        {
            Tick = (long)Main.GameUpdateCount,
            UnixMs = Time.UnixMsNow(),
            NpcType = npc.type,
            NpcName = npc.TypeName ?? "",
            OwningMod = owningMod,
            SourceCategory = sourceCategory,
            SourceContext = sourceContext,
            TileX = npc.position.X / 16f,
            TileY = npc.position.Y / 16f,
            IsBoss = npc.boss || (npc.type >= 0 && npc.type < NPCID.Sets.ShouldBeCountedAsBoss.Length && NPCID.Sets.ShouldBeCountedAsBoss[npc.type]),
        });
    }
}
