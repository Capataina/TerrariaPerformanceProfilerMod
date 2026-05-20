#nullable enable

using LiteDB;

namespace PerformanceProfiler.Profiling.Persistence.Records;

/// <summary>
/// One row per item creation detected via
/// <see cref="Terraria.ModLoader.GlobalItem.OnCreated"/>. The
/// <c>ItemCreationContext</c> subclass name (RecipeItemCreationContext,
/// InitializationItemCreationContext, etc.) is recorded verbatim — the
/// vanilla surface every item-creating mod uses, per Invariant 5.
/// </summary>
public sealed class ItemCreatedRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long Tick { get; set; }
    public long UnixMs { get; set; }

    public int ItemType { get; set; }
    public string ItemName { get; set; } = "";
    public string OwningMod { get; set; } = "";

    /// <summary>Short name of the ItemCreationContext subclass (without the suffix).</summary>
    public string ContextCategory { get; set; } = "unknown";

    public int Stack { get; set; }
}
