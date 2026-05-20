#nullable enable

using PerformanceProfiler.Profiling.Persistence.Records;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

namespace PerformanceProfiler.Profiling.Persistence.Interactions;

/// <summary>
/// Captures item creation, world-spawn, and pickup edges. Three generic
/// vanilla / tML surfaces, all Invariant-5 clean (the source category is
/// always the runtime type of the context object, never a mod-name match):
///
/// <list type="bullet">
///   <item><description><see cref="OnCreated"/> — recipe craft, init, buy, journey-duplicate.
///         <c>ItemCreationContext</c> subclass name encodes the source.</description></item>
///   <item><description><see cref="OnSpawn"/> — every item-in-the-world spawn: NPC loot,
///         chest reveal, world gen, treasure-bag, debug command. The
///         <c>IEntitySource</c> subclass name encodes the source.</description></item>
///   <item><description><see cref="OnPickup"/> — every player pickup. Always returns
///         <c>true</c> (read-only contract, Invariant 1).</description></item>
/// </list>
///
/// v0.5 only wired <c>OnCreated</c> — pickups and world-drops were silently
/// dropped (the 4.5-min playtest captured zero rows). v0.6 lands the missing
/// surfaces. The <c>SourceContext</c> field on <c>ItemCreatedRow</c>
/// disambiguates which surface fired: <c>Recipe</c> / <c>Initialization</c> /
/// <c>Buy</c> / <c>JourneyDuplication</c> from OnCreated, plus
/// <c>WorldDrop</c> from OnSpawn (with the <c>IEntitySource</c> subclass name
/// captured in <c>ContextCategory</c>) and <c>Pickup</c> from OnPickup.
/// </summary>
internal sealed class InteractionItem : GlobalItem
{
    public override void OnCreated(Item item, ItemCreationContext context)
    {
        var recorder = ResolveRecorder();
        if (recorder == null) return;

        string ctxCategory = context?.GetType().Name ?? "unknown";
        if (ctxCategory.EndsWith("ItemCreationContext"))
            ctxCategory = ctxCategory.Substring(0, ctxCategory.Length - "ItemCreationContext".Length);

        Emit(recorder, item, sourceContext: "Create", contextCategory: ctxCategory);
    }

    public override void OnSpawn(Item item, IEntitySource source)
    {
        var recorder = ResolveRecorder();
        if (recorder == null) return;

        string srcCategory = source?.GetType().Name ?? "unknown";
        if (srcCategory.StartsWith("EntitySource_"))
            srcCategory = srcCategory.Substring("EntitySource_".Length);

        Emit(recorder, item, sourceContext: "WorldDrop", contextCategory: srcCategory);
    }

    public override bool OnPickup(Item item, Player player)
    {
        // Read-only contract (Invariant 1) — we never deny the pickup.
        if (player.whoAmI != Main.myPlayer) return true;

        var recorder = ResolveRecorder();
        if (recorder == null) return true;

        Emit(recorder, item, sourceContext: "Pickup", contextCategory: "Pickup");
        return true;
    }

    // ---- helpers --------------------------------------------------------

    private static Persistence.SessionRecorder? ResolveRecorder()
    {
        var system = ModContent.GetInstance<ProfilerSystem>();
        return system?.LiveRecorder;
    }

    private static void Emit(Persistence.SessionRecorder recorder, Item item, string sourceContext, string contextCategory)
    {
        string owningMod = "Terraria";
        if (item.type >= ItemID.Count)
        {
            var modItem = ItemLoader.GetItem(item.type);
            if (modItem != null) owningMod = modItem.Mod?.Name ?? "Terraria";
        }

        recorder.OnItemCreated(new ItemCreatedRow
        {
            Tick = (long)Main.GameUpdateCount,
            UnixMs = Time.UnixMsNow(),
            ItemType = item.type,
            ItemName = item.Name ?? "",
            OwningMod = owningMod,
            SourceContext = sourceContext,
            ContextCategory = contextCategory,
            Stack = item.stack,
        });
    }
}
