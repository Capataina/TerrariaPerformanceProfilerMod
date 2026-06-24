#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;

namespace PerformanceProfiler.Data.Aggregators;

/// <summary>
/// F2 — folds the mod's event streams (item created, NPC spawn / kill,
/// damage, buff edge, loadout snapshot) plus per-tick context attendance
/// (biome, invasion, boss, accessory) into per-mod session counters
/// consumed by the Insights tab.
///
/// <para>
/// Counters are int-id-indexed against the same mod table
/// <see cref="HookInterceptor.ProfiledModNames"/> uses, so every other
/// per-mod surface (CPU attribution, roster, observatory) shares one
/// modId space. Increment APIs come in both name (for the Interaction*
/// hook sites that already resolve via <see cref="ModOwnerCache"/>) and
/// id (for the per-tick context-attendance fold) forms.
/// </para>
///
/// <para>
/// Invariant 2: the per-tick callback never allocates. Counter storage
/// is a single struct-array sized once at <see cref="Initialise"/>.
/// Increment hot paths use <see cref="Interlocked.Increment(ref long)"/>
/// since Interaction* hooks fire from the game thread but the registry's
/// per-tick driver may run from a different invocation surface; defensive
/// interlocked is cheap (~1 ns) and removes the race class entirely.
/// </para>
///
/// <para>
/// Invariant 5: the per-tick fold reads generic surfaces only —
/// <see cref="BiomeRegistry"/> for biome ownership, the
/// <see cref="EventContext.VanillaInvasion"/> enum for vanilla invasion
/// edges (1.4.4 has no ModInvasion API; modded invasions stay
/// unattributed by design), <see cref="NPCLoader.GetNPC"/> for boss
/// ownership, and <see cref="ModOwnerCache.ForItem"/> for the equipped
/// accessory slots. No mod-name strings appear in the per-tick code.
/// </para>
/// </summary>
public sealed class PerModUsageAggregator : IDataAggregator<ModUsageSnapshot>, IHasPerTickCallback
{
    /// <summary>Live singleton exposed to the Interaction* hook sites so they can
    /// post counter increments without going through ProfilerSystem / the registry.</summary>
    internal static PerModUsageAggregator? Live { get; private set; }

    private struct Counters
    {
        public long ItemsCreated;
        public long NpcsSpawned;
        public long NpcsKilled;
        public long BossesFought;
        public long BuffsApplied;
        public long BiomeTicks;
        public long InvasionsFought;
        public long AccessoryEquippedTicks;
        // Active-use tick counters (Wave 4): one credit per tick the mod's
        // content is wielded or worn, the real "is this in use" signal the
        // creation counts above could not give.
        public long ItemsHeldTicks;
        public long ArmorEquippedTicks;
    }

    private Counters[] _counters = Array.Empty<Counters>();
    private Dictionary<string, int> _modIdByName = new(StringComparer.Ordinal);

    // Per-tick edge-state for invasion + boss attribution.
    private InvasionId _lastInvasion;
    private readonly HashSet<int> _lastBossTypes = new();
    private readonly HashSet<int> _curBossTypes = new();

    public string Name => RolloutStreamNames.PerModUsage;
    public DataStreamCadence Cadence => DataStreamCadence.PerTick;
    public DataStage Stage => DataStage.Aggregator;

    // Static delegate so the call site is non-virtual and we don't allocate
    // a delegate per tick.
    private static readonly TickCapture _tickCapture = Capture;
    public TickCapture? PerTickCallback => _tickCapture;

    public void Initialise(SessionContext _)
    {
        int modCount = PerModAttribution.ModCount;
        _counters = new Counters[modCount];

        // Build name → modId reverse map from the canonical ProfiledModNames table.
        _modIdByName = new Dictionary<string, int>(modCount, StringComparer.Ordinal);
        string[] names = HookInterceptor.ProfiledModNames;
        for (int i = 0; i < names.Length && i < modCount; i++)
        {
            // Last writer wins on the (vanishingly rare) duplicate name.
            _modIdByName[names[i]] = i;
        }

        _lastInvasion = InvasionId.None;
        _lastBossTypes.Clear();
        _curBossTypes.Clear();
        Live = this;
    }

    public void Reset()
    {
        if (_counters.Length > 0) Array.Clear(_counters, 0, _counters.Length);
        _modIdByName.Clear();
        _lastInvasion = InvasionId.None;
        _lastBossTypes.Clear();
        _curBossTypes.Clear();
        Live = null;
    }

    public void Dispose() { /* counters cleared on Reset */ }

    // ----- Increment API: name overloads (called from Interaction* hooks) ----

    /// <summary>Resolve a mod name to a modId, or -1 if unknown / vanilla-only.</summary>
    public int ModIdForName(string modName)
    {
        if (string.IsNullOrEmpty(modName)) return -1;
        return _modIdByName.TryGetValue(modName, out int id) ? id : -1;
    }

    public void IncrementItemCreated(string modName) => IncrementItemCreated(ModIdForName(modName));
    public void IncrementNpcSpawned(string modName)  => IncrementNpcSpawned(ModIdForName(modName));
    public void IncrementNpcKilled(string modName)   => IncrementNpcKilled(ModIdForName(modName));
    public void IncrementBuffApplied(string modName) => IncrementBuffApplied(ModIdForName(modName));

    // ----- Increment API: int-id overloads (used internally and by callers
    //       that already hold the modId) ----------------------------------

    public void IncrementItemCreated(int modId)
    {
        if ((uint)modId < (uint)_counters.Length)
            Interlocked.Increment(ref _counters[modId].ItemsCreated);
    }

    public void IncrementNpcSpawned(int modId)
    {
        if ((uint)modId < (uint)_counters.Length)
            Interlocked.Increment(ref _counters[modId].NpcsSpawned);
    }

    public void IncrementNpcKilled(int modId)
    {
        if ((uint)modId < (uint)_counters.Length)
            Interlocked.Increment(ref _counters[modId].NpcsKilled);
    }

    public void IncrementBuffApplied(int modId)
    {
        if ((uint)modId < (uint)_counters.Length)
            Interlocked.Increment(ref _counters[modId].BuffsApplied);
    }

    // ----- Per-tick context fold --------------------------------------------

    private static void Capture(in TickContext ctx)
    {
        PerModUsageAggregator? live = Live;
        if (live == null) return;
        live.CaptureInstance(in ctx);
    }

    private void CaptureInstance(in TickContext ctx)
    {
        Counters[] counters = _counters;
        if (counters.Length == 0) return;

        ref readonly EventContext ec = ref ctx.Context;

        // ---- Biome attendance: one tick credit per active modded biome.
        // Vanilla biome bits (id < VanillaCount) have no owning mod and
        // are skipped.
        IReadOnlyList<BiomeDescriptor> biomes = BiomeRegistry.Biomes;
        int vanillaCount = BiomeRegistry.VanillaCount;
        int n = biomes.Count;
        for (int b = vanillaCount; b < n; b++)
        {
            if (!ec.Biomes.IsSet(b)) continue;
            string? owner = biomes[b].ModName;
            if (owner == null) continue;
            int modId = ModIdForName(owner);
            if ((uint)modId < (uint)counters.Length)
            {
                counters[modId].BiomeTicks++;
            }
        }

        // ---- Invasion edge: count one InvasionsFought when state moves
        // from None → any. tML 1.4.4 has no ModInvasion API so the
        // vanilla invasions stay unattributed (no owning mod). The edge
        // is still tracked so the schema-future ModInvasion path drops
        // straight in here.
        InvasionId now = ec.VanillaInvasion;
        if (now != _lastInvasion)
        {
            // Edge detected — nothing to attribute on the vanilla path
            // (see Invariant 5 note in the class-doc).
            _lastInvasion = now;
        }

        // ---- Boss appearance edges: each new boss type this tick that
        // wasn't there last tick counts as one BossesFought for its
        // owning mod. Vanilla bosses have no owning mod and are skipped.
        _curBossTypes.Clear();
        for (int i = 0; i < ec.Bosses.Count; i++)
        {
            int type = ec.Bosses[i];
            if (type <= 0) continue;
            _curBossTypes.Add(type);
            if (_lastBossTypes.Contains(type)) continue;

            // New boss this tick.
            if (type >= NPCID.Count)
            {
                var mn = NPCLoader.GetNPC(type)?.Mod?.Name;
                if (mn != null)
                {
                    int modId = ModIdForName(mn);
                    if ((uint)modId < (uint)counters.Length)
                    {
                        counters[modId].BossesFought++;
                    }
                }
            }
        }
        // Swap last ↔ cur without allocating.
        _lastBossTypes.Clear();
        foreach (int t in _curBossTypes) _lastBossTypes.Add(t);

        // ---- Worn equipment attendance: armor[0..2] are the head/chest/legs
        // armour slots, armor[3..9] the accessory slots. One tick credit per
        // non-air, modded item to its owning mod, split by slot kind so armour
        // and accessory usage stay distinguishable. Invariant 5: ownership comes
        // from the generic ModOwnerCache, never a mod-name match.
        Player? lp = Main.LocalPlayer;
        if (lp != null && lp.armor != null)
        {
            Item[] armor = lp.armor;
            int end = armor.Length < 10 ? armor.Length : 10;
            for (int i = 0; i < end; i++)
            {
                Item it = armor[i];
                if (it == null || it.type == ItemID.None) continue;
                if (it.type < ItemID.Count) continue; // vanilla — no mod attribution
                string mn = ModOwnerCache.ForItem(it.type);
                if (mn == "Terraria") continue;
                int modId = ModIdForName(mn);
                if ((uint)modId >= (uint)counters.Length) continue;
                if (i < 3) counters[modId].ArmorEquippedTicks++;
                else counters[modId].AccessoryEquippedTicks++;
            }
        }

        // ---- Held-item attendance: one tick credit to the owning mod of the
        // wielded item (Player.HeldItem, a generic surface). This is the fix for
        // the Flute-reads-zero bug: holding or swinging an already-owned weapon
        // fires no creation hook, so without this a wielded-but-not-crafted item
        // never registered as used. Reading HeldItem each tick is a single field
        // access + cached owner lookup — no allocation (Invariant 2).
        if (lp != null)
        {
            Item held = lp.HeldItem;
            if (held != null && held.type != ItemID.None && held.type >= ItemID.Count)
            {
                string mn = ModOwnerCache.ForItem(held.type);
                if (mn != "Terraria")
                {
                    int modId = ModIdForName(mn);
                    if ((uint)modId < (uint)counters.Length)
                    {
                        counters[modId].ItemsHeldTicks++;
                    }
                }
            }
        }
    }

    // ----- Snapshot ---------------------------------------------------------

    public ModUsageSnapshot CurrentSnapshot()
    {
        Counters[] counters = _counters;
        if (counters.Length == 0) return ModUsageSnapshot.Empty;

        // First pass — count non-empty so the result list is sized exactly.
        int nonEmpty = 0;
        for (int i = 0; i < counters.Length; i++)
        {
            if (IsNonEmpty(in counters[i])) nonEmpty++;
        }
        if (nonEmpty == 0)
        {
            return new ModUsageSnapshot(worldLoaded: true, Array.Empty<ModUsageEntry>());
        }

        var entries = new List<ModUsageEntry>(nonEmpty);
        for (int i = 0; i < counters.Length; i++)
        {
            ref readonly Counters c = ref counters[i];
            if (!IsNonEmpty(in c)) continue;
            entries.Add(new ModUsageEntry(
                ModId: i,
                ItemsCreated: c.ItemsCreated,
                NpcsSpawned: c.NpcsSpawned,
                NpcsKilled: c.NpcsKilled,
                BossesFought: c.BossesFought,
                BuffsApplied: c.BuffsApplied,
                TicksInOwnedBiomes: c.BiomeTicks,
                InvasionsFought: c.InvasionsFought,
                AccessoryEquippedTicks: c.AccessoryEquippedTicks,
                ItemsHeldTicks: c.ItemsHeldTicks,
                ArmorEquippedTicks: c.ArmorEquippedTicks));
        }
        return new ModUsageSnapshot(worldLoaded: true, entries);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();

    private static bool IsNonEmpty(in Counters c)
    {
        return (c.ItemsCreated | c.NpcsSpawned | c.NpcsKilled | c.BossesFought
              | c.BuffsApplied | c.BiomeTicks | c.InvasionsFought
              | c.AccessoryEquippedTicks | c.ItemsHeldTicks | c.ArmorEquippedTicks) != 0L;
    }
}
