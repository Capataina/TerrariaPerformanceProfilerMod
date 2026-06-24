// SLOT FILLING ONLY -- DO NOT INTRODUCE LLM.
// Every rendered insight is a deterministic interpolation of fields on the
// Insight. Free-form string composition is forbidden by plan §8 and
// by Invariant 3 (the honesty contract). Banned vocabulary: "caused by",
// "must remove", "core mod", "removable", "bad mod". See plan §8.3.

#nullable enable

using System.Globalization;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Insights;

/// <summary>
/// Two render densities. Short = overlay row, Medium = overlay click-through
/// + report body, Long = modder export only. Each (Pattern, Audience,
/// Density) triple has at most one template; missing combinations fall
/// back to the Player+Short template.
/// </summary>
public enum Density : byte { Short = 0, Medium = 1, Long = 2 }

/// <summary>
/// Renders an <see cref="Insight"/> into a string. Templates are
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
    public static string Render(Insight rec, Audience audience, Density density)
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

    private static string Build(Insight rec, Audience audience, Density density)
    {
        return rec.Pattern switch
        {
            PatternKey.HotHookDominance => RenderHotHook(rec, audience, density),
            PatternKey.AllocationBurst => RenderAllocBurst(rec, audience, density),
            PatternKey.FreeRemovalCandidate => RenderFreeRemoval(rec, audience, density),
            PatternKey.PeakContributorToSpike => RenderPeakContributor(rec, audience, density),
            PatternKey.SegmentOutlier => RenderSegmentOutlier(rec, density),
            PatternKey.SegmentTopMod => RenderSegmentTopMod(rec, density),
            PatternKey.SegmentDeathCorrelation => RenderSegmentDeathCorrelation(rec, density),
            PatternKey.ContextConditionalCost => RenderContextConditionalCost(rec, density),
            PatternKey.FrameHeadroom => RenderFrameHeadroom(rec, density),
            PatternKey.CostConcentration => RenderCostConcentration(rec, density),
            PatternKey.FrameJitter => RenderFrameJitter(rec, density),
            _ => RenderUnsupported(rec),
        };
    }

    private static string RenderSegmentOutlier(Insight rec, Density density)
    {
        string segName = SegmentNameTable.For(
            (SegmentFamily)rec.Subject.ContextDim, rec.Subject.ContextKey);
        string pct = Pct(rec.Magnitude.RatioOrDelta);
        string obs = Ms(rec.Magnitude.ObservedMs);
        string baseMs = Ms(rec.Magnitude.BaselineMs);
        int samples = rec.Evidence.BaselineN;

        if (density == Density.Short)
            return $"a {segName} ran {pct} above your {samples}-segment lifetime average.";
        if (density == Density.Medium)
            return $"a recent {segName} cost {obs} ms/t vs {baseMs} ms/t across {samples} prior segments — a +{pct} deviation. {BaselineClause(rec.Evidence.Baseline)}.";
        return $"[SEGMENT_OUTLIER] {segName}\n" +
               $"  Observed avg ms/t: {obs}\n" +
               $"  Lifetime avg ms/t over {samples} prior: {baseMs}\n" +
               $"  Deviation: +{pct}.\n" +
               $"  Confidence: {rec.Confidence}. {BaselineClause(rec.Evidence.Baseline)}.";
    }

    private static string RenderSegmentTopMod(Insight rec, Density density)
    {
        string mod = ModName(rec.Subject.ModId);
        string segName = SegmentNameTable.For(
            (SegmentFamily)rec.Subject.ContextDim, rec.Subject.ContextKey);
        string share = Pct(rec.Magnitude.RatioOrDelta);
        int wins = rec.Magnitude.Count;
        int n = rec.Evidence.SampleN;

        if (density == Density.Short)
            return $"{mod} is the top mod in {wins} of {n} recent {segName}s.";
        if (density == Density.Medium)
            return $"{mod} ranks #1 by cost in {share} of recent {segName}s ({wins}/{n}). {BaselineClause(rec.Evidence.Baseline)}.";
        return $"[SEGMENT_TOP_MOD] {mod} (modId={rec.Subject.ModId})\n" +
               $"  Segment family: {segName}\n" +
               $"  Top-rank frequency: {wins}/{n} = {share}.\n" +
               $"  Confidence: {rec.Confidence}. {BaselineClause(rec.Evidence.Baseline)}.";
    }

    private static string RenderSegmentDeathCorrelation(Insight rec, Density density)
    {
        string deathMs = Ms(rec.Magnitude.ObservedMs);
        string cleanMs = Ms(rec.Magnitude.BaselineMs);
        string pct = Pct(rec.Magnitude.RatioOrDelta);
        int deathSegs = rec.Evidence.SampleN;
        int cleanSegs = rec.Evidence.BaselineN;

        if (density == Density.Short)
            return $"deaths occurred in segments averaging {deathMs} ms/t vs {cleanMs} ms/t clean.";
        if (density == Density.Medium)
            return $"{deathSegs} death-containing segment(s) averaged {deathMs} ms/t vs {cleanMs} ms/t across {cleanSegs} clean segment(s) — a +{pct} delta. {BaselineClause(rec.Evidence.Baseline)}.";
        return $"[SEGMENT_DEATH_CORRELATION]\n" +
               $"  Death segments (n={deathSegs}): {deathMs} ms/t avg\n" +
               $"  Clean segments  (n={cleanSegs}): {cleanMs} ms/t avg\n" +
               $"  Delta: +{pct}.\n" +
               $"  Confidence: {rec.Confidence}. {BaselineClause(rec.Evidence.Baseline)}.";
    }

    // ---- Per-pattern templates -----------------------------------------------

    private static string RenderHotHook(Insight rec, Audience audience, Density density)
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

    private static string RenderAllocBurst(Insight rec, Audience audience, Density density)
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

    private static string RenderFreeRemoval(Insight rec, Audience audience, Density density)
    {
        string mod = ModName(rec.Subject.ModId);
        string cost = Ms(rec.Magnitude.ObservedMs);

        if (density == Density.Short)
            return $"{mod} cost {cost} ms/tick this session (engagement signal not measured yet).";
        return $"{mod} averaged {cost} ms/tick across the observed window this session. " +
               $"Engagement signal (items used, NPCs killed, biome touched) is not yet measured by the profiler, so this is a one-sided observation: cost is low, engagement is unknown.";
    }

    private static string RenderPeakContributor(Insight rec, Audience audience, Density density)
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

    private static string RenderContextConditionalCost(Insight rec, Density density)
    {
        string mod = ModName(rec.Subject.ModId);
        string ctx = ContextLabel(rec.Subject.ContextDim, rec.Subject.ContextKey);
        string ratio = Multiple(rec.Magnitude.RatioOrDelta);
        string obs = Ms(rec.Magnitude.ObservedMs);
        string baseMs = Ms(rec.Magnitude.BaselineMs);

        if (density == Density.Short)
            return $"{mod} runs {ratio} its usual cost while {ctx}.";
        if (density == Density.Medium)
            return $"{mod} costs {obs} ms/t while {ctx} vs {baseMs} ms/t otherwise — a {ratio} difference across {rec.Evidence.SampleN} in-context samples. {BaselineClause(rec.Evidence.Baseline)}.";
        return $"[CONTEXT_CONDITIONAL_COST] {mod} (modId={rec.Subject.ModId})\n" +
               $"  Context: {ctx}\n" +
               $"  In-context avg ms/t: {obs} (n={rec.Evidence.SampleN})\n" +
               $"  Out-of-context avg ms/t: {baseMs} (n={rec.Evidence.BaselineN})\n" +
               $"  Effect size (Cohen's d): {rec.Evidence.EffectSize.ToString("F2", Invariant)}; p(adj)={rec.Evidence.PValueAdjusted.ToString("G3", Invariant)}.\n" +
               $"  Confidence: {rec.Confidence}. {BaselineClause(rec.Evidence.Baseline)}.";
    }

    private static string RenderFrameHeadroom(Insight rec, Density density)
    {
        double remaining = rec.Magnitude.Remaining;
        string ceiling = Ms(rec.Magnitude.Ceiling);
        string median = Ms(rec.Magnitude.ObservedMs);
        bool over = remaining < 0d;
        string mag = Ms(over ? -remaining : remaining);

        if (density == Density.Short)
            return over
                ? $"your median frame is {mag} ms over the 60 fps budget."
                : $"you sustain 60 fps with {mag} ms of frame budget free.";
        return over
            ? $"your median frame is {median} ms against the {ceiling} ms 60 fps budget — {mag} ms over. Frames are exceeding the budget on a typical tick."
            : $"your median frame is {median} ms against the {ceiling} ms 60 fps budget — {mag} ms of headroom remains on a typical tick.";
    }

    private static string RenderCostConcentration(Insight rec, Density density)
    {
        int count = rec.Magnitude.Count;
        int totalMods = rec.Evidence.SampleN;
        string share = Pct(rec.Magnitude.RatioOrDelta);

        if (density == Density.Short)
            return $"{count} of {totalMods} mods account for {share} of measured mod cost.";
        return $"{count} of {totalMods} cost-contributing mods carry {share} of the measured per-mod cost this session; the rest is spread across the remaining {totalMods - count}.";
    }

    private static string RenderFrameJitter(Insight rec, Density density)
    {
        string median = Ms(rec.Magnitude.P50);
        string spread = Ms(rec.Magnitude.ObservedMs);
        string cv = Pct(rec.Magnitude.RatioOrDelta);

        if (density == Density.Short)
            return $"your frame times swing ±{cv} around {median} ms — frequent small hitches.";
        return $"your median frame is {median} ms but the typical swing is ±{spread} ms ({cv} of the median) — a stutter pattern (many small hitches) rather than a steady-but-slow frame.";
    }

    private static string RenderUnsupported(Insight rec) =>
        $"[{rec.Pattern}] no template registered (gated detector should not emit records).";

    /// <summary>Descriptive label for a context bucket (matches the engine's Dim* ids).</summary>
    private static string ContextLabel(byte dim, int key) => dim switch
    {
        1 => "in hardmode",
        2 => "a boss is alive",
        3 => "a vanilla invasion is active",
        4 => "inside a subworld",
        _ => "a game context is active",
    };

    /// <summary>Formats a ratio as a multiple ("1.8×"); below 1 keeps two places.</summary>
    private static string Multiple(double ratio) =>
        ratio >= 10d
            ? ratio.ToString("F0", Invariant) + "×"
            : ratio.ToString("F1", Invariant) + "×";

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
