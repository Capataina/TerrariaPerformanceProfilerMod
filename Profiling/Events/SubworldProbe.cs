#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Profiling.Events;

/// <summary>
/// Reflection probe for <c>SubworldLibrary.SubworldSystem</c>. SubworldLibrary
/// is an optional dependency — never imported, never added to
/// <c>build.txt</c>. When SWL is loaded the static <c>AnyActive()</c> +
/// <c>Current</c> surface is bound once at install; when it isn't loaded
/// every read returns 0 with no overhead.
///
/// <para>
/// Each subworld's full name is mapped to a small positive int (1, 2, 3 …)
/// at first observation; that int is what <see cref="EventContext.SubworldKey"/>
/// stores. 0 reserves "no subworld".
/// </para>
/// </summary>
internal static class SubworldProbe
{
    private static MethodInfo? _anyActive;
    private static PropertyInfo? _currentProp;
    private static PropertyInfo? _fullNameProp;

    private static readonly Dictionary<string, int> _keys = new();
    private static readonly List<string> _names = new();

    public static bool Available => _anyActive != null && _currentProp != null && _fullNameProp != null;

    /// <summary>Names indexed by key − 1; key 0 is reserved and has no entry here.</summary>
    public static IReadOnlyList<string> Names => _names;

    /// <summary>Friendly label for a given key, or <c>"None"</c> for 0 / unknown.</summary>
    public static string DisplayName(int key)
    {
        if (key <= 0 || key > _names.Count) return "None";
        return _names[key - 1];
    }

    /// <summary>
    /// Binds SubworldLibrary's static reflection surface. Idempotent; safe to
    /// call multiple times. When SWL is absent every reference stays null and
    /// <see cref="Sample"/> returns 0.
    /// </summary>
    public static void Initialise()
    {
        _anyActive = null;
        _currentProp = null;
        _fullNameProp = null;

        try
        {
            Type? swSystem = Type.GetType("SubworldLibrary.SubworldSystem, SubworldLibrary", throwOnError: false);
            if (swSystem == null) return;

            _anyActive = swSystem.GetMethod("AnyActive", BindingFlags.Public | BindingFlags.Static, binder: null, types: Type.EmptyTypes, modifiers: null);
            _currentProp = swSystem.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);

            Type? subworld = Type.GetType("SubworldLibrary.Subworld, SubworldLibrary", throwOnError: false);
            _fullNameProp = subworld?.GetProperty("FullName", BindingFlags.Public | BindingFlags.Instance);
        }
        catch
        {
            _anyActive = null;
            _currentProp = null;
            _fullNameProp = null;
        }
    }

    /// <summary>Clears the key dictionary on world unload; the static bindings stay (they are cheap and SWL doesn't unload mid-process).</summary>
    public static void Clear()
    {
        _keys.Clear();
        _names.Clear();
    }

    /// <summary>Returns the current subworld key (0 = none / SWL not loaded). Allocation-free unless a new subworld is first seen.</summary>
    public static int Sample()
    {
        if (!Available) return 0;

        try
        {
            object? anyBoxed = _anyActive!.Invoke(null, null);
            if (anyBoxed is not bool any || !any) return 0;

            object? current = _currentProp!.GetValue(null);
            if (current == null) return 0;

            if (_fullNameProp!.GetValue(current) is not string full || full.Length == 0) return 0;

            if (_keys.TryGetValue(full, out int existing)) return existing;

            int key = _keys.Count + 1;
            _keys[full] = key;
            _names.Add(full);
            return key;
        }
        catch
        {
            return 0;
        }
    }
}
