#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;

namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// Stable identity of the active mod list. The hash collapses
/// <c>(id, name, version)</c> tuples in load order into a short hex digest
/// — the same modlist always produces the same fingerprint, two different
/// modlists almost never collide.
///
/// Algorithm version is recorded alongside the digest (<c>algName</c>) so a
/// future change can be detected and migrated; this is the same algorithm
/// the legacy <c>SessionLogWriter.ModFingerprint</c> used, lifted out so
/// the new persistence path can compute it without depending on the old
/// writer.
/// </summary>
internal static class ModlistFingerprint
{
    public const string AlgName = "sha256-of-sorted-id-name-version-v1";

    public static string Compute()
    {
        StringBuilder builder = new StringBuilder();
        string[] names = HookInterceptor.ProfiledModNames;
        string[] versions = HookInterceptor.ProfiledModVersions;
        for (int i = 0; i < names.Length; i++)
        {
            builder.Append(i).Append(':').Append(names[i]).Append('@');
            builder.Append(i < versions.Length ? versions[i] : "unknown").Append(';');
        }
        return Hash(builder.ToString());
    }

    private static string Hash(string text)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }
}
