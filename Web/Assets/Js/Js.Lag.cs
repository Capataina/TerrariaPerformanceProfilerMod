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
    private const string JsLag = @"
// ====== LAG TAB =======================================================
let lagFilter = 'all';
const lagFilterEl = document.getElementById('lag-filter');
if (lagFilterEl) {
  lagFilterEl.addEventListener('click', e => {
    const b = e.target.closest('button');
    if (!b) return;
    lagFilter = b.dataset.lagFilter;
    document.querySelectorAll('#lag-filter button').forEach(x => x.classList.toggle('active', x === b));
    if (activeTab === 'lag') renderLag();
  });
}

function renderLag() {
  const root = document.getElementById('lagfeed');
  const sub = document.getElementById('lag-sub');
  if (!root) return;

  // Merge spikes + stalls into a single chronological feed, newest first.
  const items = [];
  if ((lagFilter === 'all' || lagFilter === 'spikes') && lastSpikes && lastSpikes.spikes) {
    for (const s of lastSpikes.spikes) {
      const unix = lastNow ? lastNow.unixMs - Math.max(0, (lastNow.tickIndex - s.worstTick)) * 1000 / 60 : Date.now();
      items.push({ kind: 'spike', data: s, unix });
    }
  }
  if ((lagFilter === 'all' || lagFilter === 'stalls') && lastStalls && lastStalls.stalls) {
    for (const s of lastStalls.stalls) {
      const unix = lastNow ? lastNow.unixMs - Math.max(0, (lastNow.tickIndex - s.startTick)) * 1000 / 60 : Date.now();
      items.push({ kind: 'stall', data: s, unix });
    }
  }
  items.sort((a, b) => b.unix - a.unix);

  if (items.length === 0) {
    root.innerHTML = '<div class=""empty-line"">no spikes or stalls observed in the last 30s</div>';
    sub.textContent = '0 events';
    return;
  }

  const spikeCount = items.filter(i => i.kind === 'spike').length;
  const stallCount = items.filter(i => i.kind === 'stall').length;
  sub.textContent = items.length + ' events · ' + spikeCount + ' spikes · ' + stallCount + ' stalls';

  root.innerHTML = items.map(it => {
    if (it.kind === 'spike') return renderSpikeCard(it.data, it.unix);
    return renderStallCard(it.data, it.unix);
  }).join('');

  // Wire expansion clicks.
  root.querySelectorAll('.lag-card').forEach(el => {
    el.addEventListener('click', () => {
      const k = el.dataset.key;
      const set = el.dataset.kind === 'spike' ? expandedSpikes : expandedStalls;
      if (set.has(k)) set.delete(k); else set.add(k);
      renderLag();
    });
  });
}

function renderSpikeCard(s, unix) {
  const k = 's_' + s.worstTick;
  const isOpen = expandedSpikes.has(k);
  const contribs = (s.contributors || []).map(c =>
    `<div class='lag-c'><span class='nm'>${escapeHtml(c.name)}</span><span class='vl'>${fmtMs(c.ms)} ms</span></div>`
  ).join('');
  return `<div class='lag-card spike ${s.warming ? 'warming' : ''}' data-key='${k}' data-kind='spike'>
    <div class='lag-head'>
      <span class='lag-glyph'>⚡</span>
      <div class='lag-main'>
        <div class='lag-title'><span class='lag-kind'>SPIKE</span>${fmtMs(s.worstFrameMs)} ms at tick #${fmtInt(s.worstTick)}${s.warming ? ' · warmup' : ''}</div>
        <div class='lag-sub'>baseline ${fmtMs(s.baselineMs)}ms · ${fmtAgo(unix)}</div>
      </div>
      <span class='lag-chevron'>${isOpen ? '▾' : '▸'}</span>
    </div>
    <div class='lag-detail ${isOpen ? '' : 'hidden'}'>
      <div class='lag-detail-h'>top contributors at worst tick</div>
      <div class='lag-contribs'>${contribs || '<span class=muted>(no per-mod snapshot)</span>'}</div>
    </div>
  </div>`;
}

function renderStallCard(s, unix) {
  const k = 't_' + s.startTick;
  const isOpen = expandedStalls.has(k);
  return `<div class='lag-card stall ${s.warming ? 'warming' : ''}' data-key='${k}' data-kind='stall'>
    <div class='lag-head'>
      <span class='lag-glyph'>⏸</span>
      <div class='lag-main'>
        <div class='lag-title'><span class='lag-kind danger'>STALL</span>${fmtMs(s.durationMs)} ms · ${s.cause}</div>
        <div class='lag-sub'>tick #${fmtInt(s.startTick)} · ${s.severity} · ${fmtAgo(unix)}</div>
      </div>
      <span class='lag-chevron'>${isOpen ? '▾' : '▸'}</span>
    </div>
    <div class='lag-detail ${isOpen ? '' : 'hidden'}'>
      <div class='lag-contribs'>
        <div class='lag-c'><span class='nm'>excess over baseline</span><span class='vl'>${fmtMs(s.excessMs)} ms</span></div>
        <div class='lag-c'><span class='nm'>gc pause</span><span class='vl'>${fmtMs(s.gcPauseMs)} ms</span></div>
        <div class='lag-c'><span class='nm'>gen 0 / 1 / 2 collections</span><span class='vl'>${s.gen0} · ${s.gen1} · ${s.gen2}</span></div>
        <div class='lag-c'><span class='nm'>severity</span><span class='vl'>${s.severity}</span></div>
      </div>
    </div>
  </div>`;
}

";
}
