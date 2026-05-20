// SLOT FILLING ONLY -- DO NOT INTRODUCE LLM.
// Every rendered insight is a deterministic interpolation of fields on the
// InsightRecord. Free-form string composition is forbidden by plan §8 and
// by Invariant 3 (the honesty contract). Banned vocabulary: "caused by",
// "must remove", "core mod", "removable", "bad mod". See plan §8.3.

#nullable enable

using System.Globalization;

namespace PerformanceProfiler.Profiling.Insights;

/// <summary>
/// Two render densities. Short = overlay row, Medium = overlay click-through
/// + report body, Long = modder export only. Each (Pattern, Audience,
/// Density) triple has at most one template; missing combinations fall
/// back to the Player+Short template.
/// </summary>
public enum Density : byte { Short = 0, Medium = 1, Long = 2 }

/// <summary>
/// Renders an <see cref="InsightRecord"/> into a string. Templates are
/// hardcoded here (no external file, no DSL parser) so the banned-vocab
/// rule is enforced at the call site by inspection rather than a regex.
///
/// <para>
/// Number formatting is centralised in this file to keep the same value
/// shape across patterns (e.g. ms always renders with F2 below 1, F1
/// above). The "compared to {baseline}" clause is filled from
/// <see cref="Evidence.Baseline"/> via <see cref="BaselineClause"/>.
/// </para>
/// </summary>
public static class InsightRenderer
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// Renders <paramref name="rec"/> for the given audience and density.
    /// Returns the cached value on the record when possible; falls back to
    /// rebuilding if the cache slot is empty (e.g. after rank-driven
    /// invalidation). Never allocates per-frame for the same record.
    /// </summary>
    public static string Render(InsightRecord rec, Audience audience, Density density)
    {
        if (audience == Audience.Player && density == Density.Short && rec.CachedShortPlayer != null)
            return rec.CachedShortPlayer;
        if (audience == Audience.Player && density == Density.Medium && rec.CachedMediumPlayer != null)
            return rec.CachedMediumPlayer;
        if (audience == Audience.Modder && density == Density.Long && rec.CachedLongModder != null)
            return rec.CachedLongModder;

        string s = Build(rec, audience, density);
        if (audience == Audience.Player && density == Density.Short) rec.CachedShortPlayer = s;
        else if (audience == Audience.Player && density == Density.Medium) rec.CachedMediumPlayer = s;
        else if (audience == Audience.Modder && density == Density.Long) rec.CachedLongModder = s;
        return s;
    }

    private static string Build(InsightRecord rec, Audience audience, Density density)
    {
        return rec.Pattern switch
        {
            PatternKey.HotHookDominance => RenderHotHook(rec, audience, density),
            PatternKey.AllocationBurst => RenderAllocBurst(rec, audience, density),
            PatternKey.FreeRemovalCandidate => RenderFreeRemoval(rec, audience, density),
            PatternKey.PeakContributorToSpike => RenderPeakContributor(rec, audience, density),
            _ => RenderUnsupported(rec),
        };
    }

    // ---- Per-pattern templates -----------------------------------------------

    private static string RenderHotHook(InsightRecord rec, Audience audience, Density density)
    {
        string mod = ModName(rec.Subject.ModId);
        string hook = HookName(rec.Subject.HookId);
        string share = Pct(rec.Magnitude.RatioOrDelta);
        string hookMs = Ms(rec.Magnitude.ObservedMs);
        string modMs = Ms(rec.Magnitude.BaselineMs);

        if (density == Density.Short)
            return $"{hook} accounts for {share} of {mod}'s session cost.";
        if (density == Density.Medium)
            return $"{hook} is {share} of {mod}'s smoothed session cost ({hookMs} of {modMs} ms/tick) — measured across the live ring's smoothed window. {BaselineClause(rec.Evidence.Baseline)}.";
        return $"[HOT_HOOK_DOMINANCE] {mod} (modId={rec.Subject.ModId})\n" +
               $"  Hook: {hook} (hookId={rec.Subject.HookId})\n" +
               $"  Share of mod cost: {share} ({hookMs} of {modMs} ms/tick).\n" +
               $"  Sample (smoothed ring frames): n={rec.Evidence.SampleN}.\n" +
               $"  Confidence: {rec.Confidence}. {BaselineClause(rec.Evidence.Baseline)}.";
    }

    private static string RenderAllocBurst(InsightRecord rec, Audience audience, Density density)
    {
        string mod = ModName(rec.Subject.ModId);
        string share = Pct(rec.Magnitude.RatioOrDelta);
        string bytes = Bytes(rec.Magnitude.AllocBytes);

        if (density == Density.Short)
            return $"{mod} allocates {bytes}/tick, {share} of session allocations.";
        if (density == Density.Medium)
            return $"{mod} accounts for {share} of all allocations measured this session, averaging {bytes} per tick. {BaselineClause(rec.Evidence.Baseline)}.";
        return $"[ALLOCATION_BURST] {mod} (modId={rec.Subject.ModId})\n" +
               $"  Smoothed alloc rate: {bytes}/tick.\n" +
               $"  Share of session total: {share}.\n" +
               $"  Sample (smoothed ring frames): n={rec.Evidence.SampleN}.\n" +
               $"  Confidence: {rec.Confidence}. {BaselineClause(rec.Evidence.Baseline)}.";
    }

    private static string RenderFreeRemoval(InsightRecord rec, Audience audience, Density density)
    {
        string mod = ModName(rec.Subject.ModId);
        string cost = Ms(rec.Magnitude.ObservedMs);

        if (density == Density.Short)
            return $"{mod} cost {cost} ms/tick this session (engagement signal not measured yet).";
        return $"{mod} averaged {cost} ms/tick across the observed window this session. " +
               $"Engagement signal (items used, NPCs killed, biome touched) is not yet measured by the profiler, so this is a one-sided observation: cost is low, engagement is unknown.";
    }

    private static string RenderPeakContributor(InsightRecord rec, Audience audience, Density density)
    {
        string mod = ModName(rec.Subject.ModId);
        string share = Pct(rec.Magnitude.RatioOrDelta);
        string peak = Ms(rec.Magnitude.ObservedMs);
        string baseline = Ms(rec.Magnitude.BaselineMs);

        if (density == Density.Short)
            return $"Spike of {peak} ms was {share} {mod}.";
        if (density == Density.Medium)
            return $"A {peak} ms frame (baseline {baseline} ms) was largely {mod}: {share} of the per-mod attribution snapshot at the spike.";
        return $"[PEAK_CONTRIBUTOR_TO_SPIKE] spike ticks {rec.Evidence.FirstTickIndex}..{rec.Evidence.LastTickIndex}\n" +
               $"  Worst frame: {peak} ms (baseline {baseline} ms).\n" +
               $"  Top contributor: {mod} ({share} of snapshot).\n" +
               $"  Confidence: {rec.Confidence}. {BaselineClause(rec.Evidence.Baseline)}.";
    }

    private static string RenderUnsupported(InsightRecord rec) =>
        $"[{rec.Pattern}] no template registered (gated detector should not emit records).";

    // ---- Slot helpers --------------------------------------------------------

    private static string ModName(int modId)
    {
        string[] names = HookInterceptor.ProfiledModNames;
        if (modId < 0 || modId >= names.Length) return "unknown mod";
        return names[modId];
    }

    private static string HookName(int hookId)
    {
        var hooks = PerModAttribution.Hooks;
        if (hookId < 0 || hookId >= hooks.Count) return "unknown hook";
        return hooks[hookId].DisplayName;
    }

    private static string Ms(double ms)
    {
        if (ms >= 1d) return ms.ToString("F1", Invariant);
        if (ms >= 0.1d) return ms.ToString("F2", Invariant);
        return ms.ToString("F3", Invariant);
    }

    private static string Pct(double fraction) =>
        fraction >= 0.10d
            ? (fraction * 100d).ToString("F0", Invariant) + " %"
            : (fraction * 100d).ToString("F1", Invariant) + " %";

    private static string Bytes(long bytes)
    {
        if (bytes >= 1024L * 1024L) return (bytes / (1024d * 1024d)).ToString("F1", Invariant) + " MB";
        if (bytes >= 1024L) return (bytes / 1024d).ToString("F1", Invariant) + " KB";
        return bytes.ToString(Invariant) + " B";
    }

    private static string BaselineClause(BaselineKind kind) => kind switch
    {
        BaselineKind.SessionMean => "compared to this session's average",
        BaselineKind.RollingFiveMinute => "compared to the last 5 minutes",
        BaselineKind.PreContext => "compared to the moments before the transition",
        BaselineKind.ComparableContexts => "compared to other comparable contexts",
        BaselineKind.SessionFirstHalf => "compared to the first half of the session",
        BaselineKind.PerModRollingMean => "compared to this mod's rolling mean",
        _ => "no baseline comparison",
    };
}
