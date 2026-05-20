#nullable enable

using LiteDB;
using PerformanceProfiler.Profiling.Pools;

namespace PerformanceProfiler.Profiling.Persistence.Records;

/// <summary>
/// One row per item edge captured via <see cref="Terraria.ModLoader.GlobalItem"/>:
/// <c>OnCreated</c> (recipe / init / buy / journey-duplicate), <c>OnSpawn</c>
/// (world-drops, NPC loot, chest reveal, debug command, …), and <c>OnPickup</c>
/// (every pickup by the local player). The <c>SourceContext</c> field
/// distinguishes which surface fired; <c>ContextCategory</c> carries the
/// vanilla type-name encoding of the source (the <c>ItemCreationContext</c>
/// or <c>IEntitySource</c> subclass), per Invariant 5.
///
/// v0.6 schema: bumped from 1 to 2 to add <c>SourceContext</c>. Old rows
/// (schema = 1) default to <c>SourceContext = "Create"</c> in migration —
/// the only surface that fired in v0.5.
/// </summary>
public sealed class ItemCreatedRow : IPoolReset
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 2;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long Tick { get; set; }
    public long UnixMs { get; set; }

    public int ItemType { get; set; }
    public string ItemName { get; set; } = "";
    public string OwningMod { get; set; } = "";

    /// <summary>
    /// Which surface fired: <c>Create</c> (OnCreated), <c>WorldDrop</c>
    /// (OnSpawn), or <c>Pickup</c> (OnPickup).
    /// </summary>
    public string SourceContext { get; set; } = "Create";

    /// <summary>
    /// For <c>Create</c>: the <c>ItemCreationContext</c> subclass name
    /// (Recipe, Initialization, Buy, JourneyDuplication).
    /// For <c>WorldDrop</c>: the <c>IEntitySource</c> subclass name
    /// (e.g. <c>NpcDeath</c>, <c>TileBreak</c>, <c>DebugCommand_SpawnItem</c>).
    /// For <c>Pickup</c>: the literal string <c>Pickup</c>.
    /// </summary>
    public string ContextCategory { get; set; } = "unknown";

    public int Stack { get; set; }

    public void Reset()
    {
        Id = ObjectId.NewObjectId();
        Schema = 2;
        SessionId = ObjectId.Empty;
        Tick = 0; UnixMs = 0;
        ItemType = 0; ItemName = ""; OwningMod = "";
        SourceContext = "Create"; ContextCategory = "unknown";
        Stack = 0;
    }
}
