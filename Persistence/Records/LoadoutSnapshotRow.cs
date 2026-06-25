#nullable enable

using System.Collections.Generic;
using LiteDB;
using PerformanceProfiler.Profiling.Pools;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
namespace PerformanceProfiler.Persistence.Records;

/// <summary>
/// One row per loadout-change edge plus a periodic 30s anchor snapshot.
/// Captures every Player.armor[] slot (vanilla 3 + tML accessories +
/// vanity + dye) and the held item so a future insight detector can
/// correlate cost shifts with "what the player just equipped".
///
/// <para>
/// Storage shape is a list of (slot kind, slot index, item type) tuples
/// rather than a fixed schema, because mods like FargosMutant add
/// accessory slots dynamically via ModAccessorySlot. Reading those
/// generically is the only Invariant-5-compliant path.
/// </para>
/// </summary>
public sealed class LoadoutSnapshotRow : IPoolReset
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public long Tick { get; set; }
    public long UnixMs { get; set; }

    /// <summary>"change" or "periodic".</summary>
    public string Reason { get; set; } = "change";

    public int HeldItemType { get; set; }
    public string HeldItemName { get; set; } = "";

    public List<EquipmentSlotEntry> Slots { get; set; } = new();

    /// <summary>Stable fingerprint of (held item + every slot's item type) — used as a key for change detection + insight correlation.</summary>
    public string Fingerprint { get; set; } = "";

    public void Reset()
    {
        Id = ObjectId.NewObjectId();
        Schema = 1;
        SessionId = ObjectId.Empty;
        Tick = 0; UnixMs = 0;
        Reason = "change";
        HeldItemType = 0; HeldItemName = "";
        Slots.Clear();      // preserves capacity for next renter
        Fingerprint = "";
    }
}

/// <summary>One occupied loadout slot.</summary>
public sealed class EquipmentSlotEntry
{
    public string Kind { get; set; } = ""; // "armor" | "accessory" | "vanity" | "dye" | "modSlot"
    public int Index { get; set; }
    public int ItemType { get; set; }
    public string ItemName { get; set; } = "";
}
