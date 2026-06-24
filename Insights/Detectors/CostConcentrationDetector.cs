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

    private readonly List<double> _perMod = new List<double>(64);

    public void Evaluate(MetricCollector collector, long nowTick, long sessionLengthTicks, List<Insight> emit)
    {
        int modCount = PerModAttribution.ModCount;
        int catCount = PerModAttribution.CategoryCount;
        IReadOnlyList<double> avg = collector.PerModCategoryAverageMs;

        _perMod.Clear();
        double total = 0d;
        for (int m = 0; m < modCount; m++)
        {
            double c = ModMetrics.SumModCategories(avg, m, catCount);
            if (c > 0d) { _perMod.Add(c); total += c; }
        }
        // Need enough contributing mods for "concentration" to mean anything.
        if (_perMod.Count <= MaxLeverMods || total <= 0d) return;

        _perMod.Sort((a, b) => b.CompareTo(a)); // descending
        int leverCount = Shares.ParetoCount(_perMod, total, ConcentrationThreshold);
        if (leverCount <= 0 || leverCount > MaxLeverMods) return;

        // The lever set must be a genuine minority of the contributing roster.
        if (leverCount * 4 >= _perMod.Count) return;

        double leverCost = 0d;
        for (int i = 0; i < leverCount; i++) leverCost += _perMod[i];
        double share = leverCost / total;

        emit.Add(new Insight
        {
            Pattern = PatternKey.CostConcentration,
            Subject = SubjectRef.ForSession(),
            Magnitude = new Magnitude
            {
                Shape = MagnitudeShape.Share,
                RatioOrDelta = share,             // fraction of cost the lever carries
                Count = leverCount,               // how many mods the lever is
                ObservedMs = leverCost,
                BaselineMs = total,
            },
            Evidence = new Evidence
            {
                SampleN = _perMod.Count,           // contributing mods
                BaselineN = collector.History.Count,
                PValue = 1d,
                PValueAdjusted = 1d,
                Baseline = BaselineKind.None,
            },
            Confidence = Confidence.Preliminary,
            Audience = Audience.Player,
            Scope = EvidenceScope.ThisSession,
            ConfirmationCount = 1,
        });
    }
}
