#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Insights.Shared;

namespace PerformanceProfiler.Insights.Detectors;

/// <summary>
/// COST_CONCENTRATION — Family E (structure across signals). "3 of your 47 mods
/// account for 71% of measured mod cost." A descriptive structural fact about the
/// whole system, not a verdict on any mod: it tells the player where the cost is
/// concentrated (a lever), never that the concentrated mods should go.
///
/// <para>
/// Only emits when the concentration is actually notable — a small set of mods
/// (≤ <see cref="MaxLeverMods"/>, and a real minority of the roster) carrying the
/// majority of cost. When cost is spread evenly there is no lever and nothing to
/// say. Subject is the session; one record, refreshed each pass.
/// </para>
/// </summary>
public sealed class CostConcentrationDetector : IInsightDetector
{
    /// <summary>The cost share the lever set must reach to be worth surfacing.</summary>
    private const double ConcentrationThreshold = 0.70d;

    /// <summary>A lever is only interesting if it is a handful of mods, not most of them.</summary>
    private const int MaxLeverMods = 5;

    public PatternKey Pattern => PatternKey.CostConcentration;
    public Audience DefaultAudience => Audience.Player;
    public bool IsGated => false;
    public string? GatedOn => null;

    public bool IsAvailable(MetricCollector collector) =>
        PerModAttribution.ModCount > 0 && PerModAttribution.CategoryCount > 0;

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        int modCount = PerModAttribution.ModCount;
        int catCount = PerModAttribution.CategoryCount;

        ConcentrationResult? result = CostConcentrationCore.Compute(
            collector.PerModCategoryAverageMs, modCount, catCount, ConcentrationThreshold, MaxLeverMods);
        if (result == null) return;
        ConcentrationResult res = result.Value;

        emit.Add(new Insight
        {
            Pattern = PatternKey.CostConcentration,
            Subject = SubjectRef.ForSession(),
            Magnitude = new Magnitude
            {
                Shape = MagnitudeShape.Share,
                RatioOrDelta = res.Share,          // fraction of cost the lever carries
                Count = res.LeverCount,            // how many mods the lever is
                LoadedCount = res.LoadedCount,     // every mod loaded this session (incl. idle)
                ObservedMs = res.LeverCostMs,
                BaselineMs = res.TotalMs,
            },
            Evidence = new Evidence
            {
                SampleN = res.ContributingCount,   // mods with measurable cost (the "active" denominator)
                BaselineN = collector.History.Count,
                PValue = 1d,
                PValueAdjusted = 1d,
                Baseline = BaselineKind.None,
            },
            // Name the lever mods so the card reads "3 of 26 active … ImproveGame, Calamity …"
            // rather than a bare count that looks wrong once the roster has idle mods.
            Contributors = res.Contributors,
            Confidence = Confidence.Preliminary,
            Audience = Audience.Player,
            Scope = EvidenceScope.ThisSession,
            ConfirmationCount = 1,
        });
    }
}
