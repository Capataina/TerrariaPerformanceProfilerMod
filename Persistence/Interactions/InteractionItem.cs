#nullable enable

using PerformanceProfiler.Persistence.Records;
using PerformanceProfiler.Profiling.Pools;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
namespace PerformanceProfiler.Persistence.Interactions;

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

    private static SessionRecorder? ResolveRecorder()
    {
        var system = ModContent.GetInstance<ProfilerSystem>();
        return system?.LiveRecorder;
    }

    private static void Emit(SessionRecorder recorder, Item item, string sourceContext, string contextCategory)
    {
        // v0.6.1: pooled row.
        var row = RowPool<ItemCreatedRow>.Rent();
        row.Tick = (long)Main.GameUpdateCount;
        row.UnixMs = Time.UnixMsNow();
        row.ItemType = item.type;
        row.ItemName = LangNameCache.Item(item.type);
        row.OwningMod = ModOwnerCache.ForItem(item.type);
        row.SourceContext = sourceContext;
        row.ContextCategory = contextCategory;
        row.Stack = item.stack;
        recorder.OnItemCreated(row);

        // F2: per-mod usage counter — modded items only (vanilla = no
        // attribution). Name-keyed because OwningMod is already resolved here.
        if (item.type >= ItemID.Count)
        {
            PerModUsageAggregator.Live?
                .IncrementItemCreated(row.OwningMod);
        }
    }
}
