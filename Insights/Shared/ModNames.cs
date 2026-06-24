#nullable enable

using System.Collections.Generic;

namespace PerformanceProfiler.Insights.Shared;

/// <summary>
/// Safe mod-name resolution. Looking up <c>names[modId]</c> with a bounds guard
/// and a synthetic fallback recurred at several interpretation sites with two
/// fallback styles (<c>"mod-" + id</c> and a literal placeholder); this is their
/// single home.
/// </summary>
public static class ModNames
{
    /// <summary>
    /// Returns <c>names[modId]</c> when the id is in range, else
    /// <c>"mod-" + modId</c>. The <c>(uint)</c> cast folds the negative-index
    /// case into the same out-of-range branch.
    /// </summary>
    public static string SafeName(int modId, IReadOnlyList<string> names) =>
        (uint)modId < (uint)names.Count ? names[modId] : "mod-" + modId;

    /// <summary>
    /// As <see cref="SafeName(int,IReadOnlyList{string})"/> but with a
    /// caller-supplied placeholder for the out-of-range case (e.g. an em-dash
    /// where a synthetic name would read worse than a blank).
    /// </summary>
    public static string SafeName(int modId, IReadOnlyList<string> names, string outOfRange) =>
        (uint)modId < (uint)names.Count ? names[modId] : outOfRange;
}
