#nullable enable

namespace PerformanceProfiler.Profiling;

/// <summary>
/// Enum-to-string lookup tables for the five enums that get rendered
/// (UI + log lines + insight bodies) repeatedly: <see cref="StallCause"/>,
/// <see cref="StallSeverity"/>, plus three semi-enums encoded as strings
/// on record types (LoadoutReason, BuffEdge, PatternKey).
///
/// <para>
/// Replaces <c>enumValue.ToString()</c> calls in the overlay draw path,
/// the inline-narration log path, and the insight renderer. Each
/// <c>Enum.ToString()</c> is a hashtable lookup + a string allocation
/// in the standard library; the cached array makes it a single indexer
/// access.
/// </para>
/// </summary>
public static class EnumStringTable
{
    // Static-init populated once at JIT time; no thread-safety needed.

    public static readonly string[] Cause =
    {
        "Unknown",          // 0
        "MajorGc",          // 1
        "MinorGc",          // 2
        "ProcessSuspended", // 3
        "LongFrame",        // 4
        "UiOverlayBlocking",// 5
        "WorldLoad",        // 6
        "MainThreadFreeze", // 7
    };

    public static readonly string[] Severity =
    {
        "Minor",        // 0
        "Noticeable",   // 1
        "Disruptive",   // 2
        "Freeze",       // 3
    };

    /// <summary>
    /// Loadout-snapshot reason field. The string values are
    /// "change" (loadout actually changed this tick) or "periodic"
    /// (30-second anchor with unchanged loadout).
    /// </summary>
    public const string LoadoutReasonChange = "change";
    public const string LoadoutReasonPeriodic = "periodic";

    /// <summary>Buff-event edge field: "on" or "off".</summary>
    public const string BuffEdgeOn = "on";
    public const string BuffEdgeOff = "off";

    public static string CauseName(StallCause c)
    {
        int idx = (int)c;
        if ((uint)idx < (uint)Cause.Length) return Cause[idx];
        return "Cause-" + idx;
    }

    public static string SeverityName(StallSeverity s)
    {
        int idx = (int)s;
        if ((uint)idx < (uint)Severity.Length) return Severity[idx];
        return "Severity-" + idx;
    }
}
