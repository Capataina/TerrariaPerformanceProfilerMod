#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string JsInsights = @"
// ====== INSIGHTS ======================================================
// Pattern names like 'HotHookDominance' don't mean anything to a player.
// This map turns each detector's PatternKey into an 'observation'-style
// label that says what it MEANS, not what it's called internally.
// The full proper rework is a separate session — this is the holdover.
const INSIGHT_LABEL_MAP = {
  'HotHookDominance': 'cost concentration',
  'AllocationBurst': 'allocation pressure',
  'FreeRemovalCandidate': 'idle most of session',
  'PeakContributorToSpike': 'top spike contributor',
  'ContextCorrelatedSpike': 'context-linked spike',
  'ContextConditionalCost': 'context-conditional cost',
  'SustainedCostShift': 'cost shift',
  'NewContributor': 'new contributor',
  'GcPauseCulprit': 'gc pause source',
  'HookFrequencyTail': 'long-tail hook frequency',
  'LoadoutCorrelatedCost': 'loadout-linked cost',
  'EventConditionalCost': 'event-conditional cost',
  'LoadoutCombinationCost': 'loadout combination cost',
  'SegmentOutlier': 'segment outlier',
  'SegmentTopMod': 'consistent top mod',
  'SegmentDeathCorrelation': 'deaths correlate with cost',
};

function renderInsights() {
  const root = document.getElementById('insightslist');
  const sub = document.getElementById('insights-sub');
  if (!lastInsights || !lastInsights.records || lastInsights.records.length === 0) {
    root.innerHTML = `<div class='insight-empty'>
      <h3>no observations yet</h3>
      <p>insights surface patterns we've measured across your session and prior sessions —
         things like 'this mod is consistently the top contributor in boss fights' or
         'this segment ran above your lifetime average'.</p>
      <p class='muted'>they need a few sessions of data on the same modlist before most
         of them can fire. play a bit longer.</p>
    </div>`;
    sub.textContent = '0';
    return;
  }
  const recs = lastInsights.records;
  sub.textContent = recs.length + ' observation' + (recs.length === 1 ? '' : 's');
  root.innerHTML = recs.map(r => {
    const flavour = r.confidence === 'High' ? '' : r.confidence === 'Low' ? 'warn' : '';
    const label = INSIGHT_LABEL_MAP[r.pattern] || r.pattern.replace(/([A-Z])/g, ' $1').trim().toLowerCase();
    return `<div class='insight ${flavour}'>
      <div class='head'>
        <span class='pattern'>${escapeHtml(label)}</span>
        <span class='conf'>${r.confidence} confidence · ${r.scope}</span>
      </div>
      <div class='body'>${escapeHtml(r.mediumText || r.shortText)}</div>
      <div class='footer'>seen ${r.confirmationCount}× · ticks ${fmtInt(r.firstSeenTick)}–${fmtInt(r.lastSeenTick)}</div>
    </div>`;
  }).join('');
}
";
}
