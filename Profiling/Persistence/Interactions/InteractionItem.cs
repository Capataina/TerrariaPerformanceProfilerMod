#nullable enable

using PerformanceProfiler.Profiling.Persistence.Records;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace PerformanceProfiler.Profiling.Persistence.Interactions;

/// <summary>
/// Captures every item creation via <see cref="GlobalItem.OnCreated"/>.
/// The <see cref="ItemCreationContext"/> subclass name distinguishes
/// recipe-craft vs init-spawn vs buy vs the
/// <c>InitializationItemCreationContext</c> CheatSheet uses (Invariant 5
/// — the source category is a vanilla tML surface, not a mod-name
/// match).
/// </summary>
internal sealed class InteractionItem : GlobalItem
{
    public override void OnCreated(Item item, ItemCreationContext context)
    {
        var system = ModContent.GetInstance<ProfilerSystem>();
        var recorder = system?.LiveRecorder;
        if (recorder == null) return;

        string ctxCategory = context?.GetType().Name ?? "unknown";
        if (ctxCategory.EndsWith("ItemCreationContext"))
            ctxCategory = ctxCategory.Substring(0, ctxCategory.Length - "ItemCreationContext".Length);

        string owningMod = "Terraria";
        if (item.type >= ItemID.Count)
        {
            var modItem = ItemLoader.GetItem(item.type);
            if (modItem != null) owningMod = modItem.Mod?.Name ?? "Terraria";
        }

        recorder.OnItemCreated(new ItemCreatedRow
        {
            Tick = (long)Main.GameUpdateCount,
            UnixMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ItemType = item.type,
            ItemName = item.Name ?? "",
            OwningMod = owningMod,
            ContextCategory = ctxCategory,
            Stack = item.stack,
        });
    }
}
