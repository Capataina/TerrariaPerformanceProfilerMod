#nullable enable

using System;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Collectors;

/// <summary>
/// F1 — One-shot scanner that walks every tModLoader content loader at
/// <c>PostSetupContent</c> and tallies, per loaded mod, the inventory of
/// content the mod added (items, NPCs, buffs, projectiles, mounts,
/// accessories, biomes, invasions, bosses).
///
/// <para>
/// The scan runs once per process. Snapshot consumers (the per-mod
/// observatory cards in the Insights tab) pull <see cref="CurrentSnapshot"/>
/// on demand; until <see cref="Scan"/> has run, the snapshot is
/// <see cref="ModRosterSnapshot.Empty"/>.
/// </para>
///
/// <para>
/// Read-only by construction (Invariant 1): for the per-item / per-NPC
/// accessory + boss tallies we instantiate a fresh local
/// <see cref="Item"/> / <see cref="NPC"/> and call <c>SetDefaults</c> on
/// the local instance. That instance is never registered with the game;
/// it does not touch <c>Main.item</c>, <c>Main.npc</c>, world state, or
/// any other mod's state.
/// </para>
///
/// <para>
/// Abort-clean (Invariant 4): every loader walk is wrapped in
/// <c>try</c>/<c>catch</c>. A loader API that is missing or throws on
/// this tModLoader version causes that category to be skipped for the
/// affected mod (count stays 0) with a single <c>Logger.Warn</c>; the
/// rest of the scan continues.
/// </para>
///
/// <para>
/// No mod-specific code (Invariant 5): mods are identified only by
/// enumeration order through <see cref="ModLoader.Mods"/>, with the
/// synthetic <c>"ModLoader"</c> placeholder skipped. Names are read
/// straight off the <see cref="Mod"/> instance, never matched against.
/// </para>
/// </summary>
public sealed class ModRosterScanner : IDataCollector<ModRosterSnapshot>
{
    public string Name => RolloutStreamNames.ModRoster;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Collector;
    public TickCapture? PerTickCallback => null;

    private ModRosterSnapshot _snapshot = ModRosterSnapshot.Empty;
    private bool _scanned;
    private readonly object _gate = new();

    public void Initialise(SessionContext session) { /* install-time data; no per-session work */ }
    public void Reset() { /* roster does not change across worlds within a process */ }
    public void Dispose() { }

    public ModRosterSnapshot CurrentSnapshot() => _snapshot;
    public object CurrentSnapshotBoxed() => _snapshot;

    /// <summary>
    /// Performs the one-shot per-loader walk. Idempotent: subsequent calls
    /// are no-ops. Intended call site: <c>ProfilerSystem.PostSetupContent</c>,
    /// after the host has finished loading content but before the first
    /// world is entered.
    /// </summary>
    public void Scan()
    {
        lock (_gate)
        {
            if (_scanned) return;

            List<Mod> profiled = new List<Mod>();
            foreach (Mod mod in ModLoader.Mods)
            {
                // Skip the synthetic placeholder. Terraria-as-a-mod is not
                // present in ModLoader.Mods; vanilla content is excluded
                // implicitly by the modded-range walks below.
                if (mod.Name == "ModLoader") continue;
                profiled.Add(mod);
            }

            // Pre-build a name -> index map so per-content owner lookups
            // (which return a Mod reference) can be bucketed in O(1).
            Dictionary<string, int> indexByName = new Dictionary<string, int>(profiled.Count);
            int[] items = new int[profiled.Count];
            int[] npcs = new int[profiled.Count];
            int[] buffs = new int[profiled.Count];
            int[] projectiles = new int[profiled.Count];
            int[] mounts = new int[profiled.Count];
            int[] accessories = new int[profiled.Count];
            int[] biomes = new int[profiled.Count];
            int[] invasions = new int[profiled.Count];
            int[] bosses = new int[profiled.Count];
            for (int i = 0; i < profiled.Count; i++) indexByName[profiled[i].Name] = i;

            // ----- Items + accessory tally + per-mod ownership bucket -----
            try
            {
                int from = ItemID.Count;
                int to = ItemLoader.ItemCount;
                Item templ = new Item();
                for (int type = from; type < to; type++)
                {
                    ModItem? mi;
                    try { mi = ItemLoader.GetItem(type); }
                    catch { continue; }
                    if (mi == null) continue;
                    if (!indexByName.TryGetValue(mi.Mod.Name, out int idx)) continue;
                    items[idx]++;

                    // Local templating only. SetDefaults on a fresh Item does
                    // not touch the game's Main.item array; the resulting
                    // accessory flag is read off the local instance.
                    try
                    {
                        templ.SetDefaults(type, noMatCheck: true);
                        if (templ.accessory) accessories[idx]++;
                    }
                    catch { /* one bad item shouldn't halt the walk */ }
                }
            }
            catch (Exception ex)
            {
                PerformanceProfiler.LoggerOrNull?.Warn(
                    $"ModRosterScanner: item walk failed ({ex.GetType().Name}: {ex.Message}); item/accessory counts may be incomplete.");
            }

            // ----- NPCs + boss tally -----
            try
            {
                int from = NPCID.Count;
                int to = NPCLoader.NPCCount;
                NPC templ = new NPC();
                for (int type = from; type < to; type++)
                {
                    ModNPC? mn;
                    try { mn = NPCLoader.GetNPC(type); }
                    catch { continue; }
                    if (mn == null) continue;
                    if (!indexByName.TryGetValue(mn.Mod.Name, out int idx)) continue;
                    npcs[idx]++;

                    try
                    {
                        templ.SetDefaults(type);
                        if (templ.boss) bosses[idx]++;
                    }
                    catch { /* skip the bad npc, keep going */ }
                }
            }
            catch (Exception ex)
            {
                PerformanceProfiler.LoggerOrNull?.Warn(
                    $"ModRosterScanner: NPC walk failed ({ex.GetType().Name}: {ex.Message}); NPC/boss counts may be incomplete.");
            }

            // ----- Buffs -----
            try
            {
                int from = BuffID.Count;
                int to = BuffLoader.BuffCount;
                for (int type = from; type < to; type++)
                {
                    ModBuff? mb;
                    try { mb = BuffLoader.GetBuff(type); }
                    catch { continue; }
                    if (mb == null) continue;
                    if (indexByName.TryGetValue(mb.Mod.Name, out int idx)) buffs[idx]++;
                }
            }
            catch (Exception ex)
            {
                PerformanceProfiler.LoggerOrNull?.Warn(
                    $"ModRosterScanner: buff walk failed ({ex.GetType().Name}: {ex.Message}); buff counts may be incomplete.");
            }

            // ----- Projectiles -----
            try
            {
                int from = ProjectileID.Count;
                int to = ProjectileLoader.ProjectileCount;
                for (int type = from; type < to; type++)
                {
                    ModProjectile? mp;
                    try { mp = ProjectileLoader.GetProjectile(type); }
                    catch { continue; }
                    if (mp == null) continue;
                    if (indexByName.TryGetValue(mp.Mod.Name, out int idx)) projectiles[idx]++;
                }
            }
            catch (Exception ex)
            {
                PerformanceProfiler.LoggerOrNull?.Warn(
                    $"ModRosterScanner: projectile walk failed ({ex.GetType().Name}: {ex.Message}); projectile counts may be incomplete.");
            }

            // ----- Mounts -----
            try
            {
                int from = MountID.Count;
                int to = MountLoader.MountCount;
                for (int type = from; type < to; type++)
                {
                    ModMount? mm;
                    try { mm = MountLoader.GetMount(type); }
                    catch { continue; }
                    if (mm == null) continue;
                    if (indexByName.TryGetValue(mm.Mod.Name, out int idx)) mounts[idx]++;
                }
            }
            catch (Exception ex)
            {
                PerformanceProfiler.LoggerOrNull?.Warn(
                    $"ModRosterScanner: mount walk failed ({ex.GetType().Name}: {ex.Message}); mount counts may be incomplete.");
            }

            // ----- Biomes -----
            // ModContent.GetContent<ModBiome>() returns every ModBiome
            // registered across every loaded mod; group by .Mod.Name.
            try
            {
                foreach (ModBiome biome in ModContent.GetContent<ModBiome>())
                {
                    if (biome == null) continue;
                    if (indexByName.TryGetValue(biome.Mod.Name, out int idx)) biomes[idx]++;
                }
            }
            catch (Exception ex)
            {
                PerformanceProfiler.LoggerOrNull?.Warn(
                    $"ModRosterScanner: biome walk failed ({ex.GetType().Name}: {ex.Message}); biome counts may be incomplete.");
            }

            // ----- Invasions -----
            // tModLoader does not currently expose a stable public
            // InvasionLoader / ModInvasion content type the way it does
            // for the other categories. Leave the count at 0 rather than
            // reflect into internals — F2 PerModUsageAggregator owns the
            // session-side InvasionsFought tally, which is the engagement
            // signal the observatory cards actually need.
            _ = invasions;

            // ----- Materialise the snapshot -----
            ModRosterEntry[] entries = new ModRosterEntry[profiled.Count];
            for (int i = 0; i < profiled.Count; i++)
            {
                entries[i] = new ModRosterEntry(
                    ModId: i,
                    ModName: profiled[i].Name,
                    Items: items[i],
                    NPCs: npcs[i],
                    Buffs: buffs[i],
                    Projectiles: projectiles[i],
                    Mounts: mounts[i],
                    Accessories: accessories[i],
                    Biomes: biomes[i],
                    Invasions: invasions[i],
                    Bosses: bosses[i]);
            }

            _snapshot = new ModRosterSnapshot(scanned: true, mods: entries);
            _scanned = true;

            var log = PerformanceProfiler.LoggerOrNull;
            if (log != null)
            {
                log.Info($"ModRosterScanner: scanned {entries.Length} mods.");
                for (int i = 0; i < entries.Length; i++)
                {
                    ModRosterEntry e = entries[i];
                    log.Info(
                        $"  [{e.ModId,3}] {e.ModName}: items={e.Items} npcs={e.NPCs} buffs={e.Buffs} " +
                        $"proj={e.Projectiles} mounts={e.Mounts} acc={e.Accessories} " +
                        $"biomes={e.Biomes} bosses={e.Bosses}");
                }
            }
        }
    }
}
