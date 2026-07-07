#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;

namespace PerformanceProfiler.Persistence;

/// <summary>
/// Pure fingerprint maths behind <see cref="ModlistFingerprint"/> (the X7 fix,
/// 2026-07-07 honesty pass). Terraria-free so the identity rules are
/// unit-testable — this file is picked up by the test project's
/// <c>Persistence\*.cs</c> link glob.
///
/// <para>
/// <b>Identity rules (v2):</b> a "modlist" is the <b>set of mod internal
/// names, sorted ordinally, excluding the profiler itself</b>. Load order is
/// NOT identity (the v1 digest embedded the load index, so a reorder produced
/// a "different modlist"). Mod versions are NOT identity (v1 embedded them, so
/// every mod auto-update — and every dev rebuild of any mod, including this
/// one — fractured the lifetime baselines: 11 sessions produced 10 "modlists
/// seen" on the live store). The profiler is excluded for the same reason it
/// self-excludes from the cost rollup: its own version moves constantly during
/// development and it measures, it does not participate.
/// </para>
///
/// <para>
/// Versions still matter — they move to session <i>metadata</i>, where the
/// update-regression slot (atlas S10) can compare cost before/after a version
/// change without the identity fracturing.
/// </para>
/// </summary>
internal static class FingerprintCore
{
    /// <summary>The profiler's own internal name, excluded from identity.</summary>
    public const string SelfName = "PerformanceProfiler";

    /// <summary>
    /// Compute the v2 digest from mod internal names. The input array is not
    /// mutated; names are copied, self-filtered, sorted ordinally, and joined
    /// before hashing. Returns the same digest for any permutation of the
    /// same set, with or without the profiler present.
    /// </summary>
    public static string Compute(string[] internalNames)
    {
        var names = new System.Collections.Generic.List<string>(internalNames.Length);
        for (int i = 0; i < internalNames.Length; i++)
        {
            if (!string.Equals(internalNames[i], SelfName, StringComparison.Ordinal))
            {
                names.Add(internalNames[i]);
            }
        }
        names.Sort(StringComparer.Ordinal);

        var builder = new StringBuilder(names.Count * 24);
        for (int i = 0; i < names.Count; i++)
        {
            builder.Append(names[i]).Append(';');
        }
        return Hash(builder.ToString());
    }

    private static string Hash(string text)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }
}
