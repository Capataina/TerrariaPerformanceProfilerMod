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
let lastLagClusters = null, lastGcPressure = null, lastSegmentLagDensity = null,
    lastAllocCausality = null, lastLagRhythm = null;

async function pollLagData() {
  if (activeTab !== 'lag') return;
  try {
    const [lc, gc, ld, ac, lr] = await Promise.all([
      fetch('/api/lag-clusters').then(r => r.json()),
      fetch('/api/gc').then(r => r.json()),
      fetch('/api/lag-density').then(r => r.json()),
      fetch('/api/gc-causality').then(r => r.json()),
      fetch('/api/lag-rhythm').then(r => r.json()),
    ]);
    lastLagClusters = lc; lastGcPressure = gc; lastSegmentLagDensity = ld;
    lastAllocCausality = ac; lastLagRhythm = lr;
    renderLag();
  } catch (e) { /* swallow */ }
}
setInterval(pollLagData, 3000);

function renderLag() {
  renderLagKpiStrip();
  renderLagHeatmap();
  renderLagClusters();
  renderLagDensity();
  renderLagGcPressure();
  renderLagCausality();
  renderLagRhythm();
}

// ---------------------------------------------------------------- KPI strip
function renderLagKpiStrip() {
  const root = document.getElementById('lag-kpi');
  if (!root) return;

  let events = 0, sessionLagMs = 0, worstMs = 0;
  if (lastLagClusters && lastLagClusters.clusters) {
    for (const c of lastLagClusters.clusters) {
      events += c.eventCount || 0;
      sessionLagMs += c.totalMs || 0;
      if ((c.p95Ms || 0) > worstMs) worstMs = c.p95Ms;
    }
  }
  let loadFactor = 0;
  if (lastSegmentLagDensity && lastSegmentLagDensity.entries && lastSegmentLagDensity.entries.length > 0) {
    let sum = 0, n = 0;
    for (const e of lastSegmentLagDensity.entries) {
      if (isFinite(e.vsBaseline)) { sum += e.vsBaseline; n++; }
    }
    if (n > 0) loadFactor = sum / n;
  }

  const cells = [
    { label: 'events', value: fmtInt(events), unit: '' },
    { label: 'session lag', value: fmtMs(sessionLagMs), unit: 'ms' },
    { label: 'worst event p95', value: fmtMs(worstMs), unit: 'ms' },
    { label: 'load factor', value: loadFactor > 0 ? loadFactor.toFixed(2) : '—', unit: '×' },
  ];
  root.innerHTML = cells.map(c =>
    `<div class='lag-kpi-cell'>
      <span class='label'>${c.label}</span>
      <span class='value'>${escapeHtml(c.value)}<span class='unit'>${c.unit}</span></span>
    </div>`
  ).join('');
}

// ---------------------------------------------------------------- Heatmap
function renderLagHeatmap() {
  const root = document.getElementById('lag-heatmap');
  if (!root) return;
  const cells = (lastLagClusters && lastLagClusters.causeContext) || [];

  if (cells.length === 0) {
    root.innerHTML = `<div class='lag-section-h'>cause × context</div>
      <div class='lag-empty'>no events observed</div>`;
    return;
  }

  // Build the set of causes (rows) and contexts (cols).
  const causes = [];
  const contexts = [];
  const causeSet = new Set(), ctxSet = new Set();
  for (const c of cells) {
    if (!causeSet.has(c.causeClass)) { causeSet.add(c.causeClass); causes.push(c.causeClass); }
    if (!ctxSet.has(c.context))      { ctxSet.add(c.context);      contexts.push(c.context); }
  }

  // Lookup map (cause, context) → cell.
  const key = (a, b) => a + '\x1f' + b;
  const map = new Map();
  let maxCount = 0;
  for (const c of cells) {
    map.set(key(c.causeClass, c.context), c);
    if (c.eventCount > maxCount) maxCount = c.eventCount;
  }

  // Build grid HTML. First column is the cause label; first row is the
  // context header.
  const cols = contexts.length + 1;
  const colsCss = '8rem' + ' minmax(3rem, 1fr)'.repeat(contexts.length);
  let html = `<div class='lag-section-h'>cause × context</div>
    <div class='lag-heatmap-grid' style='grid-template-columns:${colsCss}'>`;
  // Header row.
  html += `<div class='lag-heatmap-cell head'></div>`;
  for (const ctx of contexts) {
    html += `<div class='lag-heatmap-cell head'>${escapeHtml(ctx)}</div>`;
  }
  // Body rows.
  for (const cause of causes) {
    html += `<div class='lag-heatmap-cell head row-head'>${escapeHtml(cause)}</div>`;
    for (const ctx of contexts) {
      const cell = map.get(key(cause, ctx));
      if (!cell || cell.eventCount === 0) {
        html += `<div class='lag-heatmap-cell'></div>`;
      } else {
        const intensity = maxCount > 0 ? cell.eventCount / maxCount : 0;
        const bg = `rgba(74, 158, 255, ${(0.08 + 0.42 * intensity).toFixed(3)})`;
        html += `<div class='lag-heatmap-cell' style='background:${bg}'>
          <span class='cnt'>${fmtInt(cell.eventCount)}</span>
          <span class='ms'>${fmtMs(cell.totalMs)}ms</span>
        </div>`;
      }
    }
  }
  html += `</div>`;
  root.innerHTML = html;
}

// ---------------------------------------------------------------- Clusters
function renderLagClusters() {
  const root = document.getElementById('lag-clusters');
  if (!root) return;
  const clusters = (lastLagClusters && lastLagClusters.clusters) || [];

  if (clusters.length === 0) {
    root.innerHTML = `<div class='lag-section-h'>fingerprint clusters</div>
      <div class='lag-empty'>no clusters yet</div>`;
    return;
  }

  // Confidence bar segments — for now a simple two-segment bar that shows
  // top-mod share vs the remainder. Later waves layer in richer cluster
  // composition.
  const cards = clusters.map(c => {
    const topShare = Math.max(0, Math.min(1, c.topModShare || 0));
    const otherShare = 1 - topShare;
    const topColor = modColor(c.topModId);
    const subBits = [];
    if (c.primaryBiome) subBits.push(escapeHtml(c.primaryBiome));
    if (c.weatherFlags) subBits.push(escapeHtml(c.weatherFlags));
    if (c.hardmode) subBits.push('hardmode');
    return `<div class='lag-cluster'>
      <div class='title'>
        <span class='cause'>${escapeHtml(c.causeClass || '—')}</span>
        <span>${escapeHtml(c.fingerprintId || '')}</span>
      </div>
      <div class='sub'>${subBits.join(' · ') || '—'}</div>
      <div class='stats'>
        <div><span class='k'>events</span><br><span class='v'>${fmtInt(c.eventCount)}</span></div>
        <div><span class='k'>p95</span><br><span class='v'>${fmtMs(c.p95Ms)} ms</span></div>
        <div><span class='k'>total</span><br><span class='v'>${fmtMs(c.totalMs)} ms</span></div>
      </div>
      <div class='confidence-bar'>
        <span style='width:${(topShare * 100).toFixed(1)}%; background:${topColor}'></span>
        <span style='width:${(otherShare * 100).toFixed(1)}%; background:var(--dim)'></span>
      </div>
      <div class='confidence-legend'>
        <span>${escapeHtml(c.topModName || 'top mod')} · ${(topShare * 100).toFixed(0)}%</span>
        <span>others · ${(otherShare * 100).toFixed(0)}%</span>
      </div>
    </div>`;
  }).join('');

  root.innerHTML = `<div class='lag-section-h'>fingerprint clusters</div>
    <div class='lag-cluster-list'>${cards}</div>`;
}

// ---------------------------------------------------------------- Density
function renderLagDensity() {
  const root = document.getElementById('lag-density');
  if (!root) return;
  const snap = lastSegmentLagDensity;
  const entries = (snap && snap.entries) || [];

  if (entries.length === 0) {
    root.innerHTML = `<div class='lag-section-h'>per-segment lag density</div>
      <div class='lag-empty'>no segments closed yet</div>`;
    return;
  }

  let maxEpm = 0;
  for (const e of entries) if ((e.eventsPerMin || 0) > maxEpm) maxEpm = e.eventsPerMin;
  const denom = maxEpm > 0 ? maxEpm : 1;

  const rows = entries.map(e => {
    const pct = Math.max(0, Math.min(1, (e.eventsPerMin || 0) / denom)) * 100;
    const hot = (e.vsBaseline || 0) >= 2.0;
    const baselineNote = isFinite(e.vsBaseline) && e.vsBaseline > 0
      ? `${e.vsBaseline.toFixed(2)}× baseline`
      : 'baseline —';
    return `<div class='lag-density-row'>
      <span class='nm'>${escapeHtml(e.name || '—')}</span>
      <div class='bar-wrap'><div class='bar ${hot ? 'hot' : ''}' style='width:${pct.toFixed(1)}%'></div></div>
      <span class='vl'>${(e.eventsPerMin || 0).toFixed(1)}/min · ${baselineNote}</span>
    </div>`;
  }).join('');

  const baseline = (snap && snap.sessionBaselinePerMin) || 0;
  root.innerHTML = `<div class='lag-section-h'>per-segment lag density</div>
    <div class='lag-density-baseline'>session baseline · ${baseline.toFixed(2)} events/min</div>
    <div class='lag-density-rows'>${rows}</div>`;
}

// ---------------------------------------------------------------- GC panel
function renderLagGcPressure() {
  const root = document.getElementById('lag-gc');
  if (!root) return;
  const gc = lastGcPressure;
  if (!gc || !gc.worldLoaded) {
    root.innerHTML = `<div class='lag-section-h'>gc pressure</div>
      <div class='lag-empty'>no gc data yet</div>`;
    return;
  }

  const series = gc.heapMbSeries || [];
  let maxHeap = 0;
  for (const v of series) if (v > maxHeap) maxHeap = v;
  const sparkBars = series.map(v => {
    const h = maxHeap > 0 ? (v / maxHeap) * 100 : 0;
    return `<div class='b' style='height:${h.toFixed(1)}%' title='${v.toFixed(1)} MB'></div>`;
  }).join('');

  const counters = [
    ['gen 0 / min', (gc.gen0PerMin || 0).toFixed(2)],
    ['gen 1 / min', (gc.gen1PerMin || 0).toFixed(2)],
    ['gen 2 / min', (gc.gen2PerMin || 0).toFixed(2)],
    ['gen 0 total', fmtInt(gc.gen0Total)],
    ['gen 1 total', fmtInt(gc.gen1Total)],
    ['gen 2 total', fmtInt(gc.gen2Total)],
    ['paused', fmtMs(gc.totalPausedMs) + ' ms'],
    ['freed in pauses', fmtInt(gc.freedMbDuringPauses) + ' MB'],
    ['current heap', (gc.currentManagedMb || 0).toFixed(1) + ' MB'],
  ];
  const cellsHtml = counters.map(([k, v]) =>
    `<span class='k'>${escapeHtml(k)}</span><span class='v'>${escapeHtml(String(v))}</span>`
  ).join('');

  root.innerHTML = `<div class='lag-section-h'>gc pressure</div>
    <div class='lag-gc-grid'>
      <div class='lag-gc-counters'>${cellsHtml}</div>
      <div>
        <div class='lag-gc-spark'>${sparkBars || ''}</div>
        <div class='lag-rhythm-axis'><span>heap (1Hz)</span><span>${maxHeap.toFixed(1)} MB peak</span></div>
      </div>
    </div>`;
}

// ---------------------------------------------------------------- Causality
function renderLagCausality() {
  const root = document.getElementById('lag-causality');
  if (!root) return;
  const chains = (lastAllocCausality && lastAllocCausality.chains) || [];

  if (chains.length === 0) {
    root.innerHTML = `<div class='lag-section-h'>allocation → gc chains</div>
      <div class='lag-empty'>no gc stalls observed</div>`;
    return;
  }

  const cards = chains.map(ch => {
    const contribs = (ch.topContributors || []).map(c => {
      const pct = Math.max(0, Math.min(100, c.sharePct || 0));
      return `<div class='contrib-row'>
        <span class='nm'>${escapeHtml(c.modName || '—')}</span>
        <div class='bar-wrap'><div class='bar' style='width:${pct.toFixed(1)}%; background:${modColor(c.modId)}'></div></div>
        <span class='vl'>${fmtBytes(c.bytesInWindow)} · ${pct.toFixed(1)}%</span>
      </div>`;
    }).join('');
    return `<div class='lag-chain'>
      <div class='head'>
        <span class='kind'>${escapeHtml(ch.gcKind || 'gc')}</span>
        <span>${fmtMs(ch.pauseMs)} ms pause</span>
        <span class='sub'>freed ${fmtBytes(ch.freedBytes)} · window ${(ch.windowMs/1000).toFixed(1)}s · ${fmtBytes(ch.totalBytesInWindow)} total</span>
      </div>
      <div class='contribs'>${contribs || `<div class='lag-empty'>no per-mod contributors</div>`}</div>
    </div>`;
  }).join('');

  root.innerHTML = `<div class='lag-section-h'>allocation → gc chains</div>
    <div class='lag-causality-list'>${cards}</div>`;
}

// ---------------------------------------------------------------- Rhythm
function renderLagRhythm() {
  const root = document.getElementById('lag-rhythm');
  if (!root) return;
  const snap = lastLagRhythm;
  const hist = (snap && snap.histogram) || [];
  const clusters = (snap && snap.clusters) || [];

  if (hist.length === 0 && clusters.length === 0) {
    root.innerHTML = `<div class='lag-section-h'>lag rhythm</div>
      <div class='lag-empty'>not enough events for periodicity</div>`;
    return;
  }

  let maxCount = 0;
  for (const b of hist) if (b.count > maxCount) maxCount = b.count;
  const bars = hist.map(b => {
    const h = maxCount > 0 ? (b.count / maxCount) * 100 : 0;
    return `<div class='b' style='height:${h.toFixed(1)}%' title='${b.intervalSeconds.toFixed(2)}s · ${b.count}'></div>`;
  }).join('');

  let axisMin = '—', axisMax = '—';
  if (hist.length > 0) {
    axisMin = hist[0].intervalSeconds.toFixed(1) + 's';
    axisMax = hist[hist.length - 1].intervalSeconds.toFixed(1) + 's';
  }

  const clusterRows = clusters.map(c => {
    const share = Math.max(0, Math.min(1, c.shareOfSession || 0));
    return `<div class='lag-rhythm-cluster'>
      <span class='centre'>${c.centreSeconds.toFixed(2)}s ± ${c.widthSeconds.toFixed(2)}s</span>
      <span class='note'>${fmtInt(c.eventCount)} events · top mod ${escapeHtml(c.topModName || '—')} (${((c.topModShare||0)*100).toFixed(0)}%)</span>
      <span class='share'>${(share * 100).toFixed(1)}% of session</span>
    </div>`;
  }).join('');

  root.innerHTML = `<div class='lag-section-h'>lag rhythm</div>
    <div class='lag-rhythm-hist'>${bars}</div>
    <div class='lag-rhythm-axis'><span>${axisMin}</span><span>inter-event interval</span><span>${axisMax}</span></div>
    <div class='lag-rhythm-clusters'>${clusterRows}</div>`;
}
";
}
