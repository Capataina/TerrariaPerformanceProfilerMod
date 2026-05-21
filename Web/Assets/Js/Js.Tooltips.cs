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
    private const string JsTooltips = @"
// ====== Tooltips =====================================================
const TOOLTIPS = {
  'composite': {
    title: 'Composite sort',
    body: 'Composite = <code>cpuMs × 0.7 + avgCpuMs × 0.3</code>.<br/>Weights live cost heavier than session average so mods that spiked recently rise to the top, while still surfacing consistently expensive ones over momentary calm.'
  },
  'tick-frame': {
    title: 'Frame time (this tick)',
    body: 'Wall-clock ms the game took to process the most recent tick. At 60 fps the budget is ~16.6 ms per tick.'
  },
  'tick-avg': {
    title: 'Average · last 30 s',
    body: 'Rolling mean frame time over the most recent ~1800 ticks. Less jittery than the live value — what your average performance actually feels like.'
  },
  'tick-gc': {
    title: 'GC time this tick',
    body: 'Time the .NET garbage collector blocked during this tick. Large values here usually correspond to spikes and stalls.'
  },
  'backend': {
    title: 'Attribution backend',
    body: 'Which MonoMod method we use to intercept per-mod calls. <code>ilhook</code> is the IL-rewriting fast path; <code>delegate</code> uses tModLoader\'s delegate-pair detours. <code>parallel</code> runs both for comparison during dev.'
  },
  'alloc': {
    title: 'Allocation bytes',
    body: 'Managed-heap bytes attributable to each mod\'s hooks this tick. Shown as 0 when allocation tracking is disabled (Lite mode).'
  },
  'sparklines': {
    title: 'Session-trend sparklines',
    body: 'Three rolling 30-second series. <strong>frame</strong> is per-tick frame ms; <strong>alloc</strong> is process-wide allocation rate; <strong>spikes</strong> shows tick markers where spike threshold was crossed.'
  },
  'spike': {
    title: 'What counts as a spike',
    body: 'A spike fires when a frame exceeds <code>2× the 30-second median</code>. Adjacent over-threshold ticks coalesce into one window. Click a spike to see which mod owned the worst tick in that window.'
  },
  'stall': {
    title: 'What counts as a stall',
    body: 'A stall is a sustained main-thread freeze — usually 3× baseline or more. The cause is classified into <strong>MainThreadFreeze</strong>, <strong>GcDominated</strong>, or <strong>Suspended</strong> (game paused by OS). Stalls correlate with GC pressure; the per-window <code>gen0/1/2</code> counts tell you which.'
  },
  'insights': {
    title: 'Insights',
    body: 'Heuristic pattern records computed from your session and lifetime data. Examples: <em>mod X has been #1 in 9 of 10 Blood Moons</em>, or <em>this Jungle visit ran 63% above your lifetime average</em>. Strictly descriptive — never prescriptive.'
  },
  'self-severity': {
    title: 'Profiler severity',
    body: 'Severity is the <strong>ratio of bytes-per-hook to a measured baseline</strong> (36 KB/hook in v0.9). Healthy &lt; 1.5×, Concerning 1.5×–2.5×, Severe ≥ 2.5×. Tracks our regressions cleanly — independent of how big your modlist is.'
  },
  'install-delta': {
    title: 'Install delta',
    body: 'Managed-heap memory the profiler added during its hook install pass. Scales linearly with modlist size: ~36 KB per installed hook.'
  },
  'process-context': {
    title: 'Process context',
    body: 'How tModLoader\'s total memory breaks down. <strong>Managed heap</strong> is .NET-tracked memory (us + every mod + the runtime). <strong>Working set</strong> is total RAM the OS sees the process using, including native code and textures.'
  },
  'spark-frame': {
    title: 'Frame time sparkline',
    body: 'Per-tick frame duration over the last ~30 s. Spikes upward = slow frames; flat = smooth.'
  },
  'spark-gc': {
    title: 'GC time per tick',
    body: 'How much of each frame was lost to .NET garbage collection over the last ~30 s. Big bumps here usually coincide with stalls — too much memory was being allocated and the runtime had to pause to clean up.'
  },
  'spark-spike': {
    title: 'Spike markers',
    body: 'One vertical tick per detected spike window in the last ~30 s. A spike = a frame ≥ 2× the session median.'
  },
  'kpi-fps': {
    title: 'Avg FPS',
    body: 'Average frames-per-second over the last 30 s, derived from <code>1000 / avg-frame-ms</code> and clamped to 60 (Terraria\'s tick ceiling).'
  },
  'kpi-worst': {
    title: 'Worst frame',
    body: 'The single slowest frame in the last 30 s. Above ~33 ms is felt as a hitch; above 100 ms is a visible stutter.'
  },
  'kpi-spikes': {
    title: 'Lag spikes (>50 ms)',
    body: 'Count of frames in the last 30 s that exceeded 50 ms — the perceptual threshold where players actually notice a hitch.'
  },
  'kpi-stalls': {
    title: 'Stalls · session total',
    body: 'Sustained main-thread freezes (multi-second pauses) detected this session. Usually correlates with GC pressure or window-focus loss.'
  },
  'heatmap': {
    title: 'Session timeframe',
    body: 'One cell per minute of play. Cell colour = average frame time for that minute (green smooth → red painful). Cells with a red halo had a boss fight during that minute.'
  },
};

const tipEl = document.getElementById('tooltip');
function showTooltip(target, key) {
  const t = TOOLTIPS[key];
  if (!t) return;
  tipEl.innerHTML = '<span class=""tip-title"">' + t.title + '</span>' + t.body;
  tipEl.classList.remove('hidden');
  const rect = target.getBoundingClientRect();
  const tipRect = tipEl.getBoundingClientRect();
  let left = rect.left + rect.width / 2 - tipRect.width / 2;
  let top = rect.bottom + 6;
  if (left < 8) left = 8;
  if (left + tipRect.width > window.innerWidth - 8) left = window.innerWidth - tipRect.width - 8;
  if (top + tipRect.height > window.innerHeight - 8) top = rect.top - tipRect.height - 6;
  tipEl.style.left = left + 'px';
  tipEl.style.top = top + 'px';
}
function hideTooltip() { tipEl.classList.add('hidden'); }
document.addEventListener('mouseover', e => {
  const t = e.target.closest('[data-explain]');
  if (t) showTooltip(t, t.dataset.explain);
});
document.addEventListener('mouseout', e => {
  if (e.target.closest('[data-explain]')) hideTooltip();
});
";
}
