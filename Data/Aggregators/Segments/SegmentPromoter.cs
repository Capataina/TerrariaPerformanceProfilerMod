#nullable enable

using PerformanceProfiler.Profiling.Events;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data.Aggregators.Segments;

/// <summary>
/// Promotion decision for a freshly-closed segment. Returns
/// <see cref="PromotionResult.NoCard"/> when the segment should land
/// silently in the Timeline tab without a pop-up. Otherwise returns the
/// reason string the retrospective card shows in the corner.
///
/// <para>
/// Pure logic; no game-state access. Lifetime-stats lookups happen in the
/// caller and pass any required context in. Keeping this isolated makes it
/// trivially unit-testable.
/// </para>
/// </summary>
internal static class SegmentPromoter
{
    public readonly struct PromotionResult
    {
        public readonly bool Promote;
        public readonly string Reason;
        public PromotionResult(bool promote, string reason) { Promote = promote; Reason = reason; }
        public static readonly PromotionResult NoCard = new PromotionResult(false, string.Empty);
    }

    /// <summary>
    /// Decide whether <paramref name="seg"/> deserves a retrospective toast.
    /// </summary>
    /// <param name="seg">The freshly-closed segment.</param>
    /// <param name="lifetimeAvgMsPerTick">
    /// Lifetime average frame-ms-per-tick across all segments of the same
    /// (family, key). Pass 0 if no history yet — the outlier check skips
    /// gracefully.
    /// </param>
    /// <param name="lifetimeSampleCount">
    /// How many segments of this (family, key) exist in lifetime data
    /// (including this one). Used to gate the outlier test — single-sample
    /// outliers aren't statistically interesting.
    /// </param>
    public static PromotionResult Decide(Segment seg, double lifetimeAvgMsPerTick, int lifetimeSampleCount)
    {
        // Always card these — they're inherently rare and the player will want the breakdown.
        if (seg.Family == SegmentFamily.UserBookmark) return new PromotionResult(true, "bookmark");
        if (seg.Family == SegmentFamily.Boss && seg.BossKillCount > 0) return new PromotionResult(true, "boss-kill");
        if (seg.Family == SegmentFamily.Invasion) return new PromotionResult(true, "rare");
        if (seg.Family == SegmentFamily.Hardmode) return new PromotionResult(true, "rare");
        if (seg.Family == SegmentFamily.Subworld) return new PromotionResult(true, "rare");

        // Inherently-rare weather events: blood moon, eclipse, pumpkin/frost moon, lantern night.
        if (seg.Family == SegmentFamily.Weather)
        {
            WeatherFlags w = (WeatherFlags)seg.Key;
            if (w == WeatherFlags.BloodMoon || w == WeatherFlags.Eclipse ||
                w == WeatherFlags.PumpkinMoon || w == WeatherFlags.SnowMoon ||
                w == WeatherFlags.LanternNight)
            {
                return new PromotionResult(true, "rare");
            }
        }

        // Drama signals (spike/stall/death) only promote for "interesting"
        // families — Boss, Combat, DeathBracket. Biome / Weather / Hardmode
        // segments are noisy: a player who dies once in Forest+Purity+Rain
        // would otherwise fire four near-identical death toasts. The death
        // already gets its own DeathBracket retrospective, which IS the
        // right surface for "what cost you that life".
        bool dramaEligible = seg.Family is SegmentFamily.Boss
            or SegmentFamily.Combat
            or SegmentFamily.DeathBracket;
        if (dramaEligible)
        {
            if (seg.DeathCount > 0) return new PromotionResult(true, "death");
            if (seg.StallCount > 0) return new PromotionResult(true, "stall");
            if (seg.SpikeCount > 0) return new PromotionResult(true, "spike");
        }

        // Outlier check — needs at least 5 prior samples of this segment type.
        if (lifetimeSampleCount >= 5 && lifetimeAvgMsPerTick > 0d)
        {
            double avg = seg.AvgFrameMs;
            if (avg > lifetimeAvgMsPerTick * 1.5d)
            {
                return new PromotionResult(true, "outlier");
            }
        }

        return PromotionResult.NoCard;
    }
}
