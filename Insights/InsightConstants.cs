#nullable enable

namespace PerformanceProfiler.Insights;

/// <summary>
/// Constants shared across the insight detectors. Kept in one place so the
/// detectors agree on values that would otherwise drift if each carried its own
/// copy.
/// </summary>
public static class InsightConstants
{
    /// <summary>
    /// The profiler's own mod internal name (matches <c>build.txt</c>'s mod name and
    /// the entry written into <c>HookInterceptor.ProfiledModNames</c>). The profiler is
    /// instrumented like any other mod, so without this it appears in its own rankings:
    /// it runs every tick and has zero engagement by construction, so it always trips the
    /// "costly but unused", "top spike contributor", and "dominant allocator" detectors.
    /// Self-identification by the profiler's OWN name is legitimate (Invariant 5 forbids
    /// hard-coding OTHER mods' identifiers, not recognising oneself); the detectors use it
    /// to drop the profiler from findings that are about the modlist it is measuring.
    /// </summary>
    public const string SelfModInternalName = "PerformanceProfiler";
}
