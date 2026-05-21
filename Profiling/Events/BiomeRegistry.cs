#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Terraria;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Profiling.Events;

/// <summary>
/// Discovers every biome whose membership <see cref="ContextTagger"/> will
/// sample each tick. Vanilla biomes are enumerated by reflecting over
/// <see cref="Player"/>'s <c>Zone*</c> boolean properties; modded biomes
/// come from <c>ModContent.GetContent&lt;ModBiome&gt;()</c>. Each entry gets
/// a stable id used as its bit index in <see cref="EventContext.Biomes"/>.
///
/// <para>
/// Populated once per session inside <c>ProfilerSystem.PostSetupContent</c>
/// (after every mod's <see cref="ModBiome"/> has been registered). Cleared by
/// <see cref="Clear"/> on world unload. All getters are cached as compiled
/// delegates so per-tick membership reads are direct virtual calls, not
/// reflective <c>PropertyInfo.GetValue</c> dispatches (see plan §7 R1 and
/// §3.5).
/// </para>
/// </summary>
internal static class BiomeRegistry
{
    private static readonly List<BiomeDescriptor> _biomes = new();
    private static Func<Player, bool>[] _vanillaGetters = Array.Empty<Func<Player, bool>>();
    private static int[] _modBitIndex = Array.Empty<int>();
    private static FieldInfo? _modBiomeFlagsField;

    /// <summary>The 0-based id range below this is vanilla biome bits; ids ≥ this are modded.</summary>
    public static int VanillaCount { get; private set; }

    /// <summary>Total number of registered biomes; equals the bit length of <see cref="EventContext.Biomes"/>.</summary>
    public static int Count => _biomes.Count;

    /// <summary>Every registered biome in id order. Stable for the session.</summary>
    public static IReadOnlyList<BiomeDescriptor> Biomes => _biomes;

    /// <summary>True if the modded-biome direct-read binding succeeded; false means modded biome bits will stay zero.</summary>
    public static bool ModBiomeBindingOk => _modBiomeFlagsField != null;

    /// <summary>
    /// Reflects the vanilla Zone* property surface and enumerates the loaded
    /// <see cref="ModBiome"/>s, caching everything required for an
    /// allocation-free per-tick read. Idempotent; later calls clear and
    /// rebuild.
    /// </summary>
    public static void Populate()
    {
        _biomes.Clear();

        // ---- Vanilla -------------------------------------------------------
        // Reflect over Player's public instance Zone* bool properties; bind a
        // Func<Player, bool> per property so the per-tick read is a direct
        // virtual call. The 38 names observed on tModLoader 1.4.4.9 are
        // listed in plan §0; a future tML release adding Zone* properties is
        // captured for free.
        PropertyInfo[] vanillaProps = typeof(Player)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name.StartsWith("Zone", StringComparison.Ordinal)
                        && p.PropertyType == typeof(bool)
                        && p.GetMethod != null
                        && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

        VanillaCount = vanillaProps.Length;
        _vanillaGetters = new Func<Player, bool>[VanillaCount];
        for (int i = 0; i < VanillaCount; i++)
        {
            PropertyInfo p = vanillaProps[i];
            _vanillaGetters[i] = (Func<Player, bool>)Delegate.CreateDelegate(
                typeof(Func<Player, bool>), p.GetMethod!);
            _biomes.Add(new BiomeDescriptor(
                Id: i,
                DisplayName: Humanise(p.Name),
                FullName: "Vanilla:" + p.Name,
                ModName: null));
        }

        // ---- Modded biomes -------------------------------------------------
        // ModContent.GetContent<ModBiome>() returns every ModBiome registered
        // by every loaded mod. We key the bitset by the ModBiome.Type — the
        // same id tModLoader uses inside Player.modBiomeFlags — so the per
        // tick read is a single BitArray.Get(int) (plan §3.5).
        ModBiome[] modBiomes = ModContent.GetContent<ModBiome>().ToArray();
        _modBitIndex = new int[modBiomes.Length];
        for (int i = 0; i < modBiomes.Length; i++)
        {
            ModBiome mb = modBiomes[i];
            int id = VanillaCount + i;
            _modBitIndex[i] = mb.Type;
            string display = mb.DisplayName?.Value ?? mb.Name;
            _biomes.Add(new BiomeDescriptor(
                Id: id,
                DisplayName: display,
                FullName: mb.FullName,
                ModName: mb.Mod.Name));
        }

        // Bind Player.modBiomeFlags once. Nonpublic instance field on
        // Player; we read its current value per tick via FieldInfo.GetValue
        // — once-per-snapshot reflective fetch is fine at 10 Hz cadence.
        _modBiomeFlagsField = typeof(Player).GetField(
            "modBiomeFlags",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    }

    /// <summary>Drops all registered biomes and the cached delegates. Called on world unload.</summary>
    public static void Clear()
    {
        _biomes.Clear();
        _vanillaGetters = Array.Empty<Func<Player, bool>>();
        _modBitIndex = Array.Empty<int>();
        _modBiomeFlagsField = null;
        VanillaCount = 0;
    }

    /// <summary>Reads the current Zone* + modBiomeFlags state into <paramref name="dest"/>. No allocation.</summary>
    public static void Sample(Player player, ref BiomeBitset dest)
    {
        if (dest.BitLength != _biomes.Count)
        {
            dest.ResizeAndClear(_biomes.Count);
        }
        else
        {
            dest.ClearAll();
        }

        Func<Player, bool>[] vanilla = _vanillaGetters;
        for (int i = 0; i < vanilla.Length; i++)
        {
            if (vanilla[i](player)) dest.Set(i);
        }

        if (_modBiomeFlagsField == null) return;
        if (_modBiomeFlagsField.GetValue(player) is not BitArray flags) return;

        int[] map = _modBitIndex;
        int offset = VanillaCount;
        int len = flags.Length;
        for (int i = 0; i < map.Length; i++)
        {
            int bit = map[i];
            if ((uint)bit < (uint)len && flags[bit])
            {
                dest.Set(offset + i);
            }
        }
    }

    /// <summary>"ZoneUndergroundDesert" → "Underground Desert"; "ZoneJungle" → "Jungle".</summary>
    /// <summary>
    /// Display name for a biome bit index, or <c>(none)</c> when no biome
    /// is active and a bracketed fallback when the index is past the
    /// registered set (defensive — should not happen in practice).
    /// </summary>
    public static string NameOrIndex(int bitIndex)
    {
        if (bitIndex < 0) return "(none)";
        if (bitIndex < _biomes.Count) return _biomes[bitIndex].DisplayName;
        return "biome-" + bitIndex;
    }

    internal static string Humanise(string zoneName)
    {
        string trimmed = zoneName.StartsWith("Zone", StringComparison.Ordinal)
            ? zoneName.Substring("Zone".Length)
            : zoneName;
        return Regex.Replace(trimmed, "(?<=[a-z])(?=[A-Z])", " ");
    }
}
