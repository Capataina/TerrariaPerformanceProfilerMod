#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Persistence;

/// <summary>
/// Stable identity of the active mod list — a short hex digest over the
/// <b>sorted set of mod internal names, excluding the profiler itself</b>
/// (v2, 2026-07-07; the maths and identity rationale live in
/// <see cref="FingerprintCore"/>, which is Terraria-free and unit-tested).
///
/// <para>
/// v1 hashed <c>(loadIndex, name, version)</c> tuples, which made load order
/// and every mod auto-update part of the identity — 11 dev sessions produced
/// 10 "modlists seen" on the live store and lifetime cross-modpack baselines
/// never accumulated (audit finding X7). The algorithm name below is bumped
/// so stored v1 fingerprints are recognisably foreign; the one-time roster
/// fracture on upgrade is expected, and the rebuild-rollup reset scope covers
/// the rollup side.
/// </para>
///
/// <para>
/// Mod versions are still captured — as session metadata via
/// <see cref="ProfiledModVersions"/> — for future update-regression
/// comparisons (atlas S10); they are just no longer identity.
/// </para>
/// </summary>
internal static class ModlistFingerprint
{
    public const string AlgName = "sha256-of-sorted-names-selfless-v2";

    public static string Compute()
        => FingerprintCore.Compute(HookInterceptor.ProfiledModNames);

    /// <summary>
    /// The (internalName, version) pairs of the profiled mods, for session
    /// metadata. Order matches the interceptor's load order; consumers that
    /// need set semantics sort for themselves.
    /// </summary>
    public static (string Name, string Version)[] ProfiledModVersions()
    {
        string[] names = HookInterceptor.ProfiledModNames;
        string[] versions = HookInterceptor.ProfiledModVersions;
        var pairs = new (string, string)[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            pairs[i] = (names[i], i < versions.Length ? versions[i] : "unknown");
        }
        return pairs;
    }
}
