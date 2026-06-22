#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Terraria.ModLoader;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// Read-only, guarded reflection into tModLoader's own per-mod memory estimates
/// (<c>Terraria.ModLoader.Core.MemoryTracking.modMemoryUsageEstimates</c>, a
/// <c>Dictionary&lt;string, ModMemoryUsage&gt;</c> keyed by mod name). tModLoader
/// captures these during load and surfaces them as the "Mods using the most RAM"
/// log line; we read them so the Memory tab can show each mod's own footprint
/// broken into code / textures / sounds / managed, alongside our own measured
/// hook-scaffolding estimate.
///
/// <para><b>Invariant 1 (read-only):</b> only reads; nothing is written into tML
/// state. <b>Invariant 4 (abort-clean on host drift):</b> the type/field lookups
/// are resolved once and cached; if the tML shape no longer matches (an update
/// renamed or moved a member) <see cref="TryRead"/> returns false and the Memory
/// tab simply omits the tML-context numbers — it never throws into the request
/// path. The reflection target is tML's, not MonoMod's, so it is independent of
/// the hook-install internals the ILHook backend depends on.</para>
/// </summary>
public static class ModRamReader
{
    /// <summary>One mod's tModLoader-estimated footprint, in bytes.</summary>
    public readonly struct ModRam
    {
        public readonly long Total;
        public readonly long Managed;
        public readonly long Code;
        public readonly long Textures;
        public readonly long Sounds;

        public ModRam(long managed, long code, long textures, long sounds)
        {
            Managed = managed;
            Code = code;
            Textures = textures;
            Sounds = sounds;
            Total = managed + code + textures + sounds;
        }
    }

    // Resolution state: -1 unresolved, 0 unavailable (shape mismatch), 1 ready.
    private static int _state = -1;
    private static FieldInfo? _dictField;                       // MemoryTracking.modMemoryUsageEstimates (static)
    private static FieldInfo? _managedF, _codeF, _texF, _sndF;  // ModMemoryUsage instance fields

    private static bool Resolve()
    {
        if (_state >= 0)
        {
            return _state == 1;
        }

        try
        {
            Assembly tml = typeof(Mod).Assembly;
            Type? tracking = tml.GetType("Terraria.ModLoader.Core.MemoryTracking");
            Type? usage = tml.GetType("Terraria.ModLoader.Core.ModMemoryUsage");
            if (tracking == null || usage == null)
            {
                _state = 0;
                return false;
            }

            _dictField = tracking.GetField("modMemoryUsageEstimates", BindingFlags.NonPublic | BindingFlags.Static);
            _managedF = usage.GetField("managed", BindingFlags.NonPublic | BindingFlags.Instance);
            _codeF = usage.GetField("code", BindingFlags.NonPublic | BindingFlags.Instance);
            _texF = usage.GetField("textures", BindingFlags.NonPublic | BindingFlags.Instance);
            _sndF = usage.GetField("sounds", BindingFlags.NonPublic | BindingFlags.Instance);

            if (_dictField == null || _managedF == null || _codeF == null || _texF == null || _sndF == null)
            {
                _state = 0;
                return false;
            }

            _state = 1;
            return true;
        }
        catch
        {
            // Abort-clean: any reflection failure marks the reader permanently
            // unavailable for the process; the Memory tab degrades to our own
            // measured numbers without the tML context.
            _state = 0;
            return false;
        }
    }

    /// <summary>
    /// Try to read tModLoader's per-mod memory estimates, keyed by mod name.
    /// Returns false (and an empty dictionary) if the host shape is unrecognised
    /// or any read fails — callers degrade to profiler-measured numbers only.
    /// </summary>
    public static bool TryRead(out Dictionary<string, ModRam> byName)
    {
        byName = new Dictionary<string, ModRam>();
        if (!Resolve())
        {
            return false;
        }

        try
        {
            if (_dictField!.GetValue(null) is not IDictionary dict)
            {
                return false;
            }

            foreach (DictionaryEntry entry in dict)
            {
                if (entry.Key is not string name || entry.Value is null)
                {
                    continue;
                }

                long managed = Convert.ToInt64(_managedF!.GetValue(entry.Value));
                long code = Convert.ToInt64(_codeF!.GetValue(entry.Value));
                long textures = Convert.ToInt64(_texF!.GetValue(entry.Value));
                long sounds = Convert.ToInt64(_sndF!.GetValue(entry.Value));
                byName[name] = new ModRam(managed, code, textures, sounds);
            }

            return byName.Count > 0;
        }
        catch
        {
            return false;
        }
    }
}
