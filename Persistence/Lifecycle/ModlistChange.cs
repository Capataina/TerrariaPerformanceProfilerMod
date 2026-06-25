#nullable enable

using System.Collections.Generic;

namespace PerformanceProfiler.Persistence.Lifecycle;

/// <summary>
/// The diff between the modlist a session is running under and the one the previous
/// session ran under (DB rework wave 3). Pure set arithmetic over internal names, so it
/// is unit-testable and independent of the loader. The mod-membership scope (the
/// HistoryStore) does the actual "cleaning" — removed mods simply drop out of
/// current-stack queries — so this is a SIGNAL surfaced to both examiners (a dashboard
/// badge + a client.log line), never a destructive action.
/// </summary>
public sealed class ModlistChange
{
    public IReadOnlyList<string> Added { get; }
    public IReadOnlyList<string> Removed { get; }

    /// <summary>True when the previous modlist is unknown (first run, or no prior session) — not a change.</summary>
    public bool HadPrior { get; }

    public bool Changed => Added.Count > 0 || Removed.Count > 0;

    private ModlistChange(IReadOnlyList<string> added, IReadOnlyList<string> removed, bool hadPrior)
    {
        Added = added;
        Removed = removed;
        HadPrior = hadPrior;
    }

    public static readonly ModlistChange None = new ModlistChange(System.Array.Empty<string>(), System.Array.Empty<string>(), hadPrior: false);

    /// <summary>
    /// Computes the diff of <paramref name="current"/> against <paramref name="previous"/>.
    /// Added = in current but not previous; Removed = in previous but not current. A null or
    /// empty <paramref name="previous"/> yields <see cref="None"/> (nothing to compare against).
    /// Empty names are ignored.
    /// </summary>
    public static ModlistChange Diff(IReadOnlyList<string>? current, IReadOnlyList<string>? previous)
    {
        if (previous == null || previous.Count == 0) return None;

        var prev = new HashSet<string>();
        foreach (string p in previous) if (!string.IsNullOrEmpty(p)) prev.Add(p);

        var cur = new HashSet<string>();
        var added = new List<string>();
        if (current != null)
        {
            foreach (string c in current)
            {
                if (string.IsNullOrEmpty(c)) continue;
                cur.Add(c);
                if (!prev.Contains(c)) added.Add(c);
            }
        }

        var removed = new List<string>();
        foreach (string p in prev) if (!cur.Contains(p)) removed.Add(p);

        added.Sort(System.StringComparer.Ordinal);
        removed.Sort(System.StringComparer.Ordinal);
        return new ModlistChange(added, removed, hadPrior: true);
    }
}
