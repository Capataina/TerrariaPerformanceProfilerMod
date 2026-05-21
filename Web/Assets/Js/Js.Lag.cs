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
  renderLagGalaxy();
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

// ---------------------------------------------------------------- Heatmap (HEX grid)
// Visualisation: cause × context hexagonal tile grid.
// Each combination becomes a flat-top hex whose fill intensity encodes
// event count. Hover shows count + total ms.
function renderLagHeatmap() {
  const root = document.getElementById('lag-heatmap');
  if (!root) return;
  const cells = (lastLagClusters && lastLagClusters.causeContext) || [];

  if (cells.length === 0) {
    root.innerHTML = `<div class='lag-section-h'>cause × context · hex grid</div>
      <div class='lag-empty'>no events observed</div>`;
    return;
  }

  const causes = [];
  const contexts = [];
  const causeSet = new Set(), ctxSet = new Set();
  for (const c of cells) {
    if (!causeSet.has(c.causeClass)) { causeSet.add(c.causeClass); causes.push(c.causeClass); }
    if (!ctxSet.has(c.context))      { ctxSet.add(c.context);      contexts.push(c.context); }
  }
  const key = (a, b) => a + '\x1f' + b;
  const map = new Map();
  let maxCount = 0;
  for (const c of cells) {
    map.set(key(c.causeClass, c.context), c);
    if (c.eventCount > maxCount) maxCount = c.eventCount;
  }

  // Hex geometry — flat-topped. R = circumradius.
  const R = 22;
  const W = 2 * R;                 // hex width (flat-top)
  const H = Math.sqrt(3) * R;      // hex height
  const colStep = 1.5 * R;         // horizontal stride for flat-top
  const rowStep = H;
  const padX = 80, padY = 38;
  const width  = padX + colStep * contexts.length + R + 8;
  const height = padY + rowStep * causes.length + H/2 + 8;

  let svg = `<div class='lag-section-h'>cause × context · hex grid</div>
    <svg class='lag-hexgrid' viewBox='0 0 ${width} ${height}' preserveAspectRatio='xMinYMin meet'>`;

  // Column headers (contexts)
  for (let cx = 0; cx < contexts.length; cx++) {
    const px = padX + cx * colStep + R;
    svg += `<text x='${px}' y='${padY - 12}' class='hex-head' text-anchor='middle'>${escapeHtml(contexts[cx])}</text>`;
  }
  // Row labels (causes) + hex bodies
  for (let ry = 0; ry < causes.length; ry++) {
    const cy = padY + ry * rowStep + H/2;
    svg += `<text x='${padX - 10}' y='${cy + 4}' class='hex-row' text-anchor='end'>${escapeHtml(causes[ry])}</text>`;
    for (let cx = 0; cx < contexts.length; cx++) {
      const cell = map.get(key(causes[ry], contexts[cx]));
      // Stagger every other column down by H/2 for flat-top tiling
      const ox = padX + cx * colStep + R;
      const oy = cy + (cx % 2 === 1 ? H/2 : 0);
      const pts = hexPoints(ox, oy, R);
      if (!cell || cell.eventCount === 0) {
        svg += `<polygon points='${pts}' class='hex-empty'/>`;
      } else {
        const t = maxCount > 0 ? cell.eventCount / maxCount : 0;
        const fill = `rgba(74, 158, 255, ${(0.10 + 0.55 * t).toFixed(3)})`;
        const stroke = `rgba(120, 180, 255, ${(0.35 + 0.55 * t).toFixed(3)})`;
        svg += `<polygon points='${pts}' fill='${fill}' stroke='${stroke}' stroke-width='1'>
          <title>${escapeHtml(causes[ry])} · ${escapeHtml(contexts[cx])}
${cell.eventCount} events · ${fmtMs(cell.totalMs)} ms</title></polygon>`;
        svg += `<text x='${ox}' y='${oy + 2}' class='hex-count' text-anchor='middle'>${cell.eventCount}</text>`;
      }
    }
  }
  svg += `</svg>`;
  root.innerHTML = svg;
}
function hexPoints(cx, cy, r) {
  // Flat-top: angles 0,60,120,180,240,300
  const pts = [];
  for (let i = 0; i < 6; i++) {
    const a = Math.PI / 3 * i;
    pts.push((cx + r * Math.cos(a)).toFixed(2) + ',' + (cy + r * Math.sin(a)).toFixed(2));
  }
  return pts.join(' ');
}

// ---------------------------------------------------------------- Galaxy
// Visualisation: fingerprint clusters as circles in a 2D space.
// X axis = cause class (categorical band).
// Y axis = signature hash of biome+weather+hardmode (continuous-ish).
// Size  = event count (sqrt-scaled).
// Color = top mod.
// Click expands a details strip below.
let lagGalaxySelected = null;
function renderLagGalaxy() {
  const root = document.getElementById('lag-clusters');
  if (!root) return;
  const clusters = (lastLagClusters && lastLagClusters.clusters) || [];

  if (clusters.length === 0) {
    root.innerHTML = `<div class='lag-section-h'>fingerprint galaxy</div>
      <div class='lag-empty'>no clusters yet</div>`;
    return;
  }

  // Build cause-class column index.
  const causes = [];
  const causeIndex = new Map();
  for (const c of clusters) {
    const k = c.causeClass || '—';
    if (!causeIndex.has(k)) { causeIndex.set(k, causes.length); causes.push(k); }
  }
  const W = 460, H = 260;
  const padL = 14, padR = 14, padT = 10, padB = 22;
  const innerW = W - padL - padR;
  const innerH = H - padT - padB;
  const colW = causes.length > 0 ? innerW / causes.length : innerW;

  // Y position derived from a stable hash of context signature.
  function ysig(c) {
    const s = (c.primaryBiome || '') + '|' + (c.weatherFlags || '') + '|' + (c.hardmode ? 'H' : '');
    let h = 2166136261 >>> 0;
    for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); }
    return ((h >>> 0) % 1000) / 1000;
  }

  let maxEvents = 0;
  for (const c of clusters) if ((c.eventCount || 0) > maxEvents) maxEvents = c.eventCount;
  const rMin = 6, rMax = 22;

  // Pre-compute placements, then nudge to reduce overlap within a column.
  const placed = clusters.map((c, i) => {
    const col = causeIndex.get(c.causeClass || '—') || 0;
    const cx  = padL + col * colW + colW / 2;
    const cy  = padT + ysig(c) * innerH;
    const r   = rMin + (maxEvents > 0 ? Math.sqrt(c.eventCount / maxEvents) : 0) * (rMax - rMin);
    return { c, i, cx, cy, r };
  });
  // Simple overlap-relax: vertical jitter pass.
  for (let pass = 0; pass < 6; pass++) {
    for (let i = 0; i < placed.length; i++) {
      for (let j = i+1; j < placed.length; j++) {
        const a = placed[i], b = placed[j];
        const dx = a.cx - b.cx, dy = a.cy - b.cy;
        const d  = Math.sqrt(dx*dx + dy*dy);
        const want = a.r + b.r + 2;
        if (d < want && d > 0.0001) {
          const push = (want - d) / 2;
          const uy = dy / d;
          a.cy = Math.max(padT + a.r, Math.min(padT + innerH - a.r, a.cy + uy * push));
          b.cy = Math.max(padT + b.r, Math.min(padT + innerH - b.r, b.cy - uy * push));
        }
      }
    }
  }

  // Column band guides.
  let svg = `<div class='lag-section-h'>fingerprint galaxy</div>
    <svg class='lag-galaxy' viewBox='0 0 ${W} ${H}' preserveAspectRatio='xMidYMid meet'>`;
  for (let i = 0; i < causes.length; i++) {
    const x0 = padL + i * colW;
    if (i % 2 === 1) {
      svg += `<rect x='${x0.toFixed(1)}' y='${padT}' width='${colW.toFixed(1)}' height='${innerH}' class='galaxy-band'/>`;
    }
    svg += `<text x='${(x0 + colW/2).toFixed(1)}' y='${H - 6}' class='galaxy-axis' text-anchor='middle'>${escapeHtml(causes[i])}</text>`;
  }
  // Y-axis label (rotated).
  svg += `<text x='8' y='${(padT + innerH/2).toFixed(1)}' class='galaxy-axis-y' text-anchor='middle' transform='rotate(-90 8 ${(padT + innerH/2).toFixed(1)})'>biome · weather · hm signature</text>`;

  // Circles.
  for (const p of placed) {
    const colour = modColor(p.c.topModId || 0);
    const selected = lagGalaxySelected === p.c.fingerprintId;
    const cls = 'galaxy-node' + (selected ? ' sel' : '');
    svg += `<circle class='${cls}' cx='${p.cx.toFixed(1)}' cy='${p.cy.toFixed(1)}' r='${p.r.toFixed(1)}' fill='${colour}' data-fp='${escapeHtml(p.c.fingerprintId || '')}' onclick='lagGalaxyPick(this.getAttribute(""data-fp""))'>
      <title>${escapeHtml(p.c.fingerprintId || '')}
${escapeHtml(p.c.causeClass || '')} · ${escapeHtml(p.c.primaryBiome || '')} ${escapeHtml(p.c.weatherFlags || '')}
${fmtInt(p.c.eventCount)} events · p95 ${fmtMs(p.c.p95Ms)}ms · total ${fmtMs(p.c.totalMs)}ms
top mod: ${escapeHtml(p.c.topModName || '—')} (${((p.c.topModShare||0)*100).toFixed(0)}%)</title></circle>`;
    // Top-mod share ring overlay (inner arc).
    const share = Math.max(0, Math.min(1, p.c.topModShare || 0));
    if (share > 0 && share < 1) {
      const ringR = p.r + 2;
      const a0 = -Math.PI/2, a1 = a0 + share * Math.PI * 2;
      const lx0 = p.cx + ringR * Math.cos(a0), ly0 = p.cy + ringR * Math.sin(a0);
      const lx1 = p.cx + ringR * Math.cos(a1), ly1 = p.cy + ringR * Math.sin(a1);
      const large = share > 0.5 ? 1 : 0;
      svg += `<path d='M ${lx0.toFixed(1)} ${ly0.toFixed(1)} A ${ringR.toFixed(1)} ${ringR.toFixed(1)} 0 ${large} 1 ${lx1.toFixed(1)} ${ly1.toFixed(1)}' class='galaxy-ring' stroke='${colour}'/>`;
    }
  }
  svg += `</svg>`;

  // Details strip for the selected node.
  let details = '';
  const pick = clusters.find(c => c.fingerprintId === lagGalaxySelected);
  if (pick) {
    const subBits = [];
    if (pick.primaryBiome) subBits.push(escapeHtml(pick.primaryBiome));
    if (pick.weatherFlags) subBits.push(escapeHtml(pick.weatherFlags));
    if (pick.hardmode) subBits.push('hardmode');
    details = `<div class='galaxy-details'>
      <div class='gd-top'>
        <span class='gd-cause'>${escapeHtml(pick.causeClass || '—')}</span>
        <span class='gd-fp'>${escapeHtml(pick.fingerprintId || '')}</span>
        <span class='gd-sub'>${subBits.join(' · ') || '—'}</span>
      </div>
      <div class='gd-stats'>
        <span><b>${fmtInt(pick.eventCount)}</b> events</span>
        <span>p95 <b>${fmtMs(pick.p95Ms)}</b> ms</span>
        <span>total <b>${fmtMs(pick.totalMs)}</b> ms</span>
        <span>top mod <b style='color:${modColor(pick.topModId||0)}'>${escapeHtml(pick.topModName || '—')}</b> ${((pick.topModShare||0)*100).toFixed(0)}%</span>
      </div>
    </div>`;
  } else {
    details = `<div class='galaxy-hint'>click a node to inspect · size = events · ring = top-mod share</div>`;
  }

  root.innerHTML = svg + details;
}
function lagGalaxyPick(fp) {
  if (!fp) return;
  lagGalaxySelected = (lagGalaxySelected === fp) ? null : fp;
  renderLagGalaxy();
}

// ---------------------------------------------------------------- Density (BEESWARM)
// Visualisation: each segment becomes a row of jittered dots.
// One dot per spike/stall event. Stalls render in stall color, spikes in spike color.
// Outlier segments visually crowded; quiet segments visually sparse.
function renderLagDensity() {
  const root = document.getElementById('lag-density');
  if (!root) return;
  const snap = lastSegmentLagDensity;
  const entries = (snap && snap.entries) || [];

  if (entries.length === 0) {
    root.innerHTML = `<div class='lag-section-h'>per-segment lag swarm</div>
      <div class='lag-empty'>no segments closed yet</div>`;
    return;
  }

  const baseline = (snap && snap.sessionBaselinePerMin) || 0;

  // Hash for jitter stability so re-renders don't shuffle dots.
  function jitter(seed, i) {
    let h = (seed * 2654435761) ^ (i * 2246822519);
    h = (h ^ (h >>> 13)) >>> 0;
    return ((h % 1000) / 1000);
  }

  // Per-row geometry.
  const rowH = 28;
  const swarmW = 320;
  const labelW = 130;
  const valW   = 150;
  const W = labelW + swarmW + valW + 18;
  const H = rowH * entries.length + 8;

  // For ms-axis: events have no per-event ms in this snapshot, so dot x
  // is deterministic-jittered along the row. The *density* is the story.
  let svg = `<div class='lag-section-h'>per-segment lag swarm</div>
    <div class='lag-density-baseline'>session baseline · ${baseline.toFixed(2)} events/min · each dot = one observed lag event</div>
    <svg class='lag-swarm' viewBox='0 0 ${W} ${H}' preserveAspectRatio='xMinYMin meet'>`;

  for (let i = 0; i < entries.length; i++) {
    const e = entries[i];
    const yMid = i * rowH + rowH/2 + 2;
    const spikes = e.spikeCount || 0;
    const stalls = e.stallCount || 0;
    const total  = spikes + stalls;
    const hot    = (e.vsBaseline || 0) >= 2.0;

    // Row backdrop.
    svg += `<rect x='${labelW}' y='${i*rowH + 4}' width='${swarmW}' height='${rowH - 4}' class='swarm-bg ${hot ? 'hot' : ''}'/>`;
    // Row name.
    const nm = e.name || '—';
    svg += `<text x='${labelW - 8}' y='${yMid + 4}' class='swarm-label' text-anchor='end'>${escapeHtml(nm)}</text>`;

    // Dots — spikes first then stalls so stalls draw on top.
    const dotsTotal = Math.min(total, 220); // cap visual crowding
    const stallShown = Math.round(dotsTotal * (total > 0 ? stalls / total : 0));
    const spikeShown = dotsTotal - stallShown;
    for (let d = 0; d < spikeShown; d++) {
      const jx = jitter(i * 31 + 1, d);
      const jy = jitter(i * 31 + 2, d);
      const px = labelW + 4 + jx * (swarmW - 8);
      const py = (i * rowH + 6) + jy * (rowH - 10);
      svg += `<circle cx='${px.toFixed(1)}' cy='${py.toFixed(1)}' r='1.8' class='swarm-dot spike'/>`;
    }
    for (let d = 0; d < stallShown; d++) {
      const jx = jitter(i * 31 + 3, d);
      const jy = jitter(i * 31 + 4, d);
      const px = labelW + 4 + jx * (swarmW - 8);
      const py = (i * rowH + 6) + jy * (rowH - 10);
      svg += `<circle cx='${px.toFixed(1)}' cy='${py.toFixed(1)}' r='2.3' class='swarm-dot stall'/>`;
    }

    // Value column.
    const baseNote = isFinite(e.vsBaseline) && e.vsBaseline > 0
      ? `${e.vsBaseline.toFixed(2)}× base`
      : '— base';
    svg += `<text x='${labelW + swarmW + 8}' y='${yMid + 4}' class='swarm-val'>${(e.eventsPerMin||0).toFixed(1)}/min · ${escapeHtml(baseNote)}</text>`;
  }

  svg += `</svg>
    <div class='swarm-legend'><span class='dot spike'></span>spike <span class='dot stall'></span>stall</div>`;
  root.innerHTML = svg;
}

// ---------------------------------------------------------------- GC TIDE
// Visualisation: managed-heap MB over session rendered as a tidal curve.
// Filled area with smoothed top, baseline 0. The 'tide' is the heap.
// Wave breaks (downward triangles) drawn proportionally to gen2 totals.
function renderLagGcPressure() {
  const root = document.getElementById('lag-gc');
  if (!root) return;
  const gc = lastGcPressure;
  if (!gc || !gc.worldLoaded) {
    root.innerHTML = `<div class='lag-section-h'>gc tide</div>
      <div class='lag-empty'>no gc data yet</div>`;
    return;
  }

  const series = gc.heapMbSeries || [];
  const n = series.length;
  const W = 540, H = 110;
  const padX = 8, padTop = 14, padBot = 18;
  const innerW = W - padX*2;
  const innerH = H - padTop - padBot;

  let maxHeap = 0;
  for (const v of series) if (v > maxHeap) maxHeap = v;
  if (maxHeap <= 0) maxHeap = 1;

  // Build smoothed path.
  function pt(i) {
    const x = padX + (n > 1 ? (i / (n - 1)) * innerW : innerW/2);
    const y = padTop + innerH - (series[i] / maxHeap) * innerH;
    return [x, y];
  }
  let path = '';
  if (n > 0) {
    const [x0, y0] = pt(0);
    path = `M ${x0.toFixed(1)} ${(padTop + innerH).toFixed(1)} L ${x0.toFixed(1)} ${y0.toFixed(1)}`;
    for (let i = 1; i < n; i++) {
      const [px, py] = pt(i - 1);
      const [cx, cy] = pt(i);
      // Cubic-ish bezier with horizontal control points for a wave look.
      const mx = (px + cx) / 2;
      path += ` C ${mx.toFixed(1)} ${py.toFixed(1)} ${mx.toFixed(1)} ${cy.toFixed(1)} ${cx.toFixed(1)} ${cy.toFixed(1)}`;
    }
    const [xN, yN] = pt(n - 1);
    path += ` L ${xN.toFixed(1)} ${(padTop + innerH).toFixed(1)} Z`;
  }

  // Wave-break markers: gen2 collections — distribute total across series as
  // visual ticks above the curve. Exact times are not in the snapshot;
  // approximate uniform spacing reflects 'these happened during the session'.
  const gen2 = Math.min(gc.gen2Total || 0, 12);  // visual cap
  let breaks = '';
  for (let k = 0; k < gen2; k++) {
    const frac = gen2 > 1 ? (k + 0.5) / gen2 : 0.5;
    const idx = Math.floor(frac * Math.max(0, n - 1));
    const [bx, by] = n > 0 ? pt(idx) : [padX + innerW/2, padTop + innerH];
    const top = by - 8;
    breaks += `<polygon points='${(bx-4).toFixed(1)},${top.toFixed(1)} ${(bx+4).toFixed(1)},${top.toFixed(1)} ${bx.toFixed(1)},${by.toFixed(1)}' class='gc-break'/>`;
  }

  // Generation 'wind' arrows — rate strength as arrow length.
  function arrow(label, rate, y, color) {
    const len = Math.min(60, 8 + rate * 8);
    return `<g class='gc-wind' transform='translate(${(W - 80).toFixed(1)}, ${y})'>
      <text x='-6' y='3' class='gc-wind-l' text-anchor='end'>${label}</text>
      <line x1='0' y1='0' x2='${len.toFixed(1)}' y2='0' stroke='${color}' stroke-width='1.4'/>
      <polygon points='${len.toFixed(1)},-3 ${(len+5).toFixed(1)},0 ${len.toFixed(1)},3' fill='${color}'/>
      <text x='${(len+9).toFixed(1)}' y='3' class='gc-wind-v'>${rate.toFixed(2)}/min</text>
    </g>`;
  }

  // Counters cluster (compact).
  const counters = [
    ['paused', fmtMs(gc.totalPausedMs) + ' ms'],
    ['freed in pauses', fmtInt(gc.freedMbDuringPauses) + ' MB'],
    ['current heap', (gc.currentManagedMb || 0).toFixed(1) + ' MB'],
    ['peak heap', maxHeap.toFixed(1) + ' MB'],
  ];
  const cellsHtml = counters.map(([k, v]) =>
    `<span class='k'>${escapeHtml(k)}</span><span class='v'>${escapeHtml(String(v))}</span>`
  ).join('');

  const svg = `<svg class='gc-tide' viewBox='0 0 ${W} ${H}' preserveAspectRatio='xMidYMid meet'>
    <defs>
      <linearGradient id='gc-tide-grad' x1='0' x2='0' y1='0' y2='1'>
        <stop offset='0%' stop-color='rgba(110,93,150,0.55)'/>
        <stop offset='100%' stop-color='rgba(110,93,150,0.08)'/>
      </linearGradient>
    </defs>
    <line x1='${padX}' y1='${padTop + innerH}' x2='${W - padX}' y2='${padTop + innerH}' class='gc-baseline'/>
    <path d='${path}' fill='url(#gc-tide-grad)' stroke='#9b8acb' stroke-width='1.2'/>
    ${breaks}
    ${arrow('gen0', gc.gen0PerMin || 0, padTop + 6,  '#5f8db3')}
    ${arrow('gen1', gc.gen1PerMin || 0, padTop + 22, '#7d6d9c')}
    ${arrow('gen2', gc.gen2PerMin || 0, padTop + 38, '#a05b6a')}
    <text x='${padX}' y='${padTop - 4}' class='gc-axis'>heap MB · 1Hz · peak ${maxHeap.toFixed(1)} MB</text>
    <text x='${W - padX}' y='${H - 4}' class='gc-axis' text-anchor='end'>▼ = gen-2 collection</text>
  </svg>`;

  root.innerHTML = `<div class='lag-section-h'>gc tide</div>
    <div class='lag-gc-grid'>
      <div class='lag-gc-counters compact'>${cellsHtml}
        <span class='k'>gen0 total</span><span class='v'>${fmtInt(gc.gen0Total)}</span>
        <span class='k'>gen1 total</span><span class='v'>${fmtInt(gc.gen1Total)}</span>
        <span class='k'>gen2 total</span><span class='v'>${fmtInt(gc.gen2Total)}</span>
      </div>
      <div>${svg}</div>
    </div>`;
}

// ---------------------------------------------------------------- Causality (SANKEY)
// Visualisation: each gc-stall chain rendered as a tiny inline Sankey.
// Left side: mod contributor bands (sized by bytes).
// Centre: a GC 'node' bar.
// Right side: freed bytes band.
function renderLagCausality() {
  const root = document.getElementById('lag-causality');
  if (!root) return;
  const chains = (lastAllocCausality && lastAllocCausality.chains) || [];

  if (chains.length === 0) {
    root.innerHTML = `<div class='lag-section-h'>allocation → gc → freed sankeys</div>
      <div class='lag-empty'>no gc stalls observed</div>`;
    return;
  }

  const cards = chains.map(ch => {
    const contribs = (ch.topContributors || []);
    const totalIn = ch.totalBytesInWindow || contribs.reduce((s, c) => s + (c.bytesInWindow || 0), 0) || 1;
    const freed   = ch.freedBytes || 0;
    const W = 460, H = Math.max(72, 18 + contribs.length * 12);
    const padY = 6;
    const colLeftX = 8,  colLeftW  = 110;
    const colMidX  = 220, colMidW  = 24;
    const colRightX = 380, colRightW = 60;
    const usableH = H - padY * 2;

    // Layout: contributors stacked on left, gc bar centre, freed bar right.
    let leftCursor = padY;
    let pathHtml = '';
    let leftLabels = '';
    let leftBands = '';
    for (const c of contribs) {
      const h = Math.max(2, ((c.bytesInWindow || 0) / totalIn) * usableH);
      const colour = modColor(c.modId);
      leftBands += `<rect x='${colLeftX}' y='${leftCursor.toFixed(1)}' width='${colLeftW}' height='${h.toFixed(1)}' fill='${colour}' opacity='0.85'>
        <title>${escapeHtml(c.modName || '—')} · ${fmtBytes(c.bytesInWindow)} · ${(c.sharePct||0).toFixed(1)}%</title></rect>`;
      // Label inside if tall enough, else right of band.
      if (h >= 11) {
        leftLabels += `<text x='${(colLeftX + 4)}' y='${(leftCursor + h/2 + 3).toFixed(1)}' class='sk-label-in'>${escapeHtml(truncate(c.modName || '—', 14))} ${(c.sharePct||0).toFixed(0)}%</text>`;
      }
      // Flow path from right edge of band → top portion of GC bar.
      const y0 = leftCursor, y1 = leftCursor + h;
      const ymid = (y0 + y1) / 2;
      const xa = colLeftX + colLeftW;
      const xb = colMidX;
      const targetTop = padY + ((leftCursor - padY) / usableH) * usableH;
      const targetBot = targetTop + h;
      // Smooth ribbon.
      const cx1 = xa + (xb - xa) * 0.4;
      const cx2 = xa + (xb - xa) * 0.6;
      pathHtml += `<path d='M ${xa} ${y0.toFixed(1)} C ${cx1} ${y0.toFixed(1)}, ${cx2} ${targetTop.toFixed(1)}, ${xb} ${targetTop.toFixed(1)}
        L ${xb} ${targetBot.toFixed(1)} C ${cx2} ${targetBot.toFixed(1)}, ${cx1} ${y1.toFixed(1)}, ${xa} ${y1.toFixed(1)} Z'
        fill='${colour}' opacity='0.22'/>`;
      leftCursor += h;
    }

    // GC centre bar.
    const gcBar = `<rect x='${colMidX}' y='${padY}' width='${colMidW}' height='${usableH}' class='sk-gc-bar'/>`;
    const gcLabel = `<text x='${(colMidX + colMidW/2)}' y='${padY - 1}' class='sk-gc-label' text-anchor='middle'>${escapeHtml(ch.gcKind || 'gc')}</text>
      <text x='${(colMidX + colMidW/2)}' y='${(H - 1)}' class='sk-gc-label' text-anchor='middle'>${fmtMs(ch.pauseMs)}ms</text>`;

    // Freed bar — right column, scaled relative to totalIn so the user
    // can read freed-vs-incoming visually.
    const freedFrac = Math.min(1, freed / Math.max(1, totalIn));
    const freedH = Math.max(2, freedFrac * usableH);
    const freedY = padY + (usableH - freedH) / 2;
    const freedBar = `<rect x='${colRightX}' y='${freedY.toFixed(1)}' width='${colRightW}' height='${freedH.toFixed(1)}' class='sk-freed-bar'>
      <title>freed ${fmtBytes(freed)}</title></rect>`;
    const freedFlow = `<path d='M ${colMidX + colMidW} ${(padY + (usableH - freedH)/2).toFixed(1)}
      C ${(colMidX + colMidW + 40)} ${(padY + (usableH - freedH)/2).toFixed(1)},
        ${(colRightX - 40)} ${freedY.toFixed(1)},
        ${colRightX} ${freedY.toFixed(1)}
      L ${colRightX} ${(freedY + freedH).toFixed(1)}
      C ${(colRightX - 40)} ${(freedY + freedH).toFixed(1)},
        ${(colMidX + colMidW + 40)} ${(padY + (usableH + freedH)/2).toFixed(1)},
        ${colMidX + colMidW} ${(padY + (usableH + freedH)/2).toFixed(1)} Z'
      class='sk-freed-flow'/>`;
    const freedLbl = `<text x='${(colRightX + colRightW + 6)}' y='${(freedY + freedH/2 + 3).toFixed(1)}' class='sk-side-l'>freed ${fmtBytes(freed)}</text>`;
    const inLbl = `<text x='${colLeftX}' y='${padY - 2}' class='sk-side-l'>${fmtBytes(totalIn)} in ${(ch.windowMs/1000).toFixed(1)}s</text>`;

    return `<div class='lag-chain-sankey'>
      <svg viewBox='0 0 ${W} ${H}' preserveAspectRatio='xMinYMin meet'>
        ${pathHtml}
        ${leftBands}
        ${leftLabels}
        ${gcBar}
        ${freedFlow}
        ${freedBar}
        ${inLbl}
        ${gcLabel}
        ${freedLbl}
      </svg>
    </div>`;
  }).join('');

  root.innerHTML = `<div class='lag-section-h'>allocation → gc → freed sankeys</div>
    <div class='lag-causality-list'>${cards}</div>`;
}

// ---------------------------------------------------------------- Rhythm (POLAR)
// Visualisation: inter-event interval histogram in polar coordinates.
// Angle: interval value (axisMin..axisMax mapped to 0..2π).
// Radius: count.
// Rhythm clusters drawn as highlighted wedges.
function renderLagRhythm() {
  const root = document.getElementById('lag-rhythm');
  if (!root) return;
  const snap = lastLagRhythm;
  const hist = (snap && snap.histogram) || [];
  const clusters = (snap && snap.clusters) || [];

  if (hist.length === 0 && clusters.length === 0) {
    root.innerHTML = `<div class='lag-section-h'>lag rhythm · polar</div>
      <div class='lag-empty'>not enough events for periodicity</div>`;
    return;
  }

  const W = 320, H = 320;
  const cx = W/2, cy = H/2;
  const rMax = Math.min(W, H) / 2 - 24;
  const rMin = 30;

  // Interval range for angle mapping.
  let lo = Infinity, hi = -Infinity;
  for (const b of hist) {
    if (b.intervalSeconds < lo) lo = b.intervalSeconds;
    if (b.intervalSeconds > hi) hi = b.intervalSeconds;
  }
  if (!isFinite(lo)) { lo = 0; hi = 1; }
  if (hi <= lo) hi = lo + 1;
  const angleOf = (sec) => ((sec - lo) / (hi - lo)) * Math.PI * 2 - Math.PI/2;

  let maxCount = 0;
  for (const b of hist) if (b.count > maxCount) maxCount = b.count;
  if (maxCount <= 0) maxCount = 1;

  // Concentric ring grid.
  let grid = '';
  for (let k = 1; k <= 3; k++) {
    const rr = rMin + ((rMax - rMin) * k / 3);
    grid += `<circle cx='${cx}' cy='${cy}' r='${rr.toFixed(1)}' class='polar-ring'/>`;
  }
  // Centre core.
  grid += `<circle cx='${cx}' cy='${cy}' r='${rMin}' class='polar-core'/>`;

  // Angle labels — show min, ¼, ½, ¾, max.
  let labels = '';
  for (let k = 0; k <= 4; k++) {
    const frac = k / 4;
    const sec = lo + (hi - lo) * frac;
    const a = angleOf(sec);
    const lx = cx + (rMax + 12) * Math.cos(a);
    const ly = cy + (rMax + 12) * Math.sin(a);
    labels += `<text x='${lx.toFixed(1)}' y='${ly.toFixed(1)}' class='polar-tick' text-anchor='middle' dominant-baseline='middle'>${sec.toFixed(1)}s</text>`;
  }

  // Cluster wedges (drawn behind the bars).
  let wedges = '';
  for (const c of clusters) {
    const a0 = angleOf(Math.max(lo, c.centreSeconds - c.widthSeconds/2));
    const a1 = angleOf(Math.min(hi, c.centreSeconds + c.widthSeconds/2));
    if (!isFinite(a0) || !isFinite(a1)) continue;
    const colour = modColor(c.topModId || 0);
    const x0 = cx + rMin * Math.cos(a0), y0 = cy + rMin * Math.sin(a0);
    const x1 = cx + rMax * Math.cos(a0), y1 = cy + rMax * Math.sin(a0);
    const x2 = cx + rMax * Math.cos(a1), y2 = cy + rMax * Math.sin(a1);
    const x3 = cx + rMin * Math.cos(a1), y3 = cy + rMin * Math.sin(a1);
    const large = (a1 - a0) > Math.PI ? 1 : 0;
    wedges += `<path d='M ${x0.toFixed(1)} ${y0.toFixed(1)}
      L ${x1.toFixed(1)} ${y1.toFixed(1)}
      A ${rMax} ${rMax} 0 ${large} 1 ${x2.toFixed(1)} ${y2.toFixed(1)}
      L ${x3.toFixed(1)} ${y3.toFixed(1)}
      A ${rMin} ${rMin} 0 ${large} 0 ${x0.toFixed(1)} ${y0.toFixed(1)} Z'
      fill='${colour}' opacity='0.18'/>`;
    // Centre marker line.
    const ac = angleOf(c.centreSeconds);
    const mx = cx + (rMax + 6) * Math.cos(ac), my = cy + (rMax + 6) * Math.sin(ac);
    const ix = cx + rMin * Math.cos(ac), iy = cy + rMin * Math.sin(ac);
    wedges += `<line x1='${ix.toFixed(1)}' y1='${iy.toFixed(1)}' x2='${mx.toFixed(1)}' y2='${my.toFixed(1)}' stroke='${colour}' stroke-width='1.4' opacity='0.8'/>`;
  }

  // Bars — one per histogram bucket. Drawn as thin radial wedges.
  let bars = '';
  // Compute bin half-width in angle.
  const halfA = hist.length > 1 ? (Math.PI * 2) / (hist.length * 2.4) : 0.05;
  for (const b of hist) {
    const a = angleOf(b.intervalSeconds);
    const r = rMin + (b.count / maxCount) * (rMax - rMin);
    const a0 = a - halfA, a1 = a + halfA;
    const x0 = cx + rMin * Math.cos(a0), y0 = cy + rMin * Math.sin(a0);
    const x1 = cx + r * Math.cos(a0),    y1 = cy + r * Math.sin(a0);
    const x2 = cx + r * Math.cos(a1),    y2 = cy + r * Math.sin(a1);
    const x3 = cx + rMin * Math.cos(a1), y3 = cy + rMin * Math.sin(a1);
    bars += `<path d='M ${x0.toFixed(1)} ${y0.toFixed(1)}
      L ${x1.toFixed(1)} ${y1.toFixed(1)}
      A ${r.toFixed(1)} ${r.toFixed(1)} 0 0 1 ${x2.toFixed(1)} ${y2.toFixed(1)}
      L ${x3.toFixed(1)} ${y3.toFixed(1)}
      A ${rMin} ${rMin} 0 0 0 ${x0.toFixed(1)} ${y0.toFixed(1)} Z'
      class='polar-bar'>
      <title>${b.intervalSeconds.toFixed(2)}s · ${b.count} events</title></path>`;
  }

  // Centre readout.
  let centreText = `<text x='${cx}' y='${cy - 4}' class='polar-c1' text-anchor='middle'>${hist.length}</text>
    <text x='${cx}' y='${cy + 10}' class='polar-c2' text-anchor='middle'>buckets</text>`;
  if (clusters.length > 0) {
    centreText = `<text x='${cx}' y='${cy - 4}' class='polar-c1' text-anchor='middle'>${clusters.length}</text>
      <text x='${cx}' y='${cy + 10}' class='polar-c2' text-anchor='middle'>rhythm${clusters.length===1?'':'s'}</text>`;
  }

  const svg = `<svg class='lag-polar' viewBox='0 0 ${W} ${H}' preserveAspectRatio='xMidYMid meet'>
    ${grid}
    ${wedges}
    ${bars}
    ${labels}
    ${centreText}
  </svg>`;

  const clusterRows = clusters.map(c => {
    const share = Math.max(0, Math.min(1, c.shareOfSession || 0));
    const colour = modColor(c.topModId || 0);
    return `<div class='lag-rhythm-cluster'>
      <span class='swatch' style='background:${colour}'></span>
      <span class='centre'>${c.centreSeconds.toFixed(2)}s ± ${c.widthSeconds.toFixed(2)}s</span>
      <span class='note'>${fmtInt(c.eventCount)} events · top mod ${escapeHtml(c.topModName || '—')} (${((c.topModShare||0)*100).toFixed(0)}%)</span>
      <span class='share'>${(share * 100).toFixed(1)}% of session</span>
    </div>`;
  }).join('');

  root.innerHTML = `<div class='lag-section-h'>lag rhythm · polar</div>
    <div class='lag-polar-shell'>
      ${svg}
      <div class='lag-rhythm-clusters'>${clusterRows || `<div class='lag-empty'>no rhythm clusters detected</div>`}</div>
    </div>`;
}
";
}
