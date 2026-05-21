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
    private const string JsSummary = @"
// ====== Heatmap =======================================================
function renderHeatmap() {
  const root = document.getElementById('heatmap-grid');
  const sub = document.getElementById('heatmap-sub');
  if (!lastHeatmap || !lastHeatmap.worldLoaded || !lastHeatmap.buckets) {
    root.innerHTML = '';
    sub.textContent = 'waiting for data…';
    return;
  }
  const buckets = lastHeatmap.buckets;
  if (buckets.length === 0) {
    root.innerHTML = '<div class=""empty-line"">no minutes recorded yet</div>';
    sub.textContent = '0 minutes';
    return;
  }

  // For each bucket, determine which boss-segment (if any) overlaps it.
  function bossLabelFor(b) {
    if (!lastHeatmap.bossOverlays) return null;
    const end = b.startUnixMs + (lastHeatmap.bucketMs || 60000);
    for (const o of lastHeatmap.bossOverlays) {
      if (o.endUnixMs >= b.startUnixMs && o.startUnixMs <= end) return o;
    }
    return null;
  }

  function bandClass(avgMs) {
    if (avgMs <= 17)  return 'p0';
    if (avgMs <= 25)  return 'p1';
    if (avgMs <= 40)  return 'p2';
    if (avgMs <= 60)  return 'p3';
    return 'p4';
  }

  sub.textContent = buckets.length + ' minute(s) · ' + (lastHeatmap.bossOverlays?.length || 0) + ' boss segment(s)';
  root.innerHTML = buckets.map(b => {
    const cls = bandClass(b.avgMs);
    const boss = bossLabelFor(b);
    const time = new Date(b.startUnixMs).toLocaleTimeString();
    const tip = (boss ? boss.name + ' · ' : '') + time + ' · avg ' + fmtMs(b.avgMs) + 'ms · worst ' + fmtMs(b.worstMs) + 'ms';
    return `<div class='hm-cell ${cls} ${boss ? 'boss' : ''}' data-tip='${escapeHtml(tip)}'></div>`;
  }).join('');
}

function renderFrameChart() {
  const svg = document.getElementById('frame-chart');
  const sub = document.getElementById('chart-sub');
  const title = document.getElementById('chart-title');
  if (!lastFrames || !lastFrames.worldLoaded || !lastFrames.frameMs || lastFrames.frameMs.length === 0) {
    svg.innerHTML = ''; sub.textContent = '—'; return;
  }
  const ms = lastFrames.frameMs;
  const n = ms.length;

  // Map series + axis depending on toggle.
  const showFps = frameChartMode === 'fps';
  title.textContent = showFps ? 'fps · last 30s' : 'frame time · last 30s';
  const series = showFps ? ms.map(v => v > 0 ? Math.min(120, 1000 / Math.max(0.5, v)) : 0) : ms;
  const sortedMs = ms.slice().sort((a, b) => a - b);
  const medianMs = sortedMs[Math.floor(n / 2)];
  const thresholdMs = medianMs * 2;
  const sortedSeries = series.slice().sort((a, b) => a - b);
  const medianS = sortedSeries[Math.floor(n / 2)];
  const max = Math.max(showFps ? 60 : 2, Math.max(...series) * 1.08);
  // For FPS, low values are bad. For ms, high values are bad. Adjust accordingly.
  const thresholdS = showFps ? 1000 / Math.max(0.5, thresholdMs) : thresholdMs;

  sub.textContent = showFps
    ? n + ' frames · median ' + medianS.toFixed(0) + ' fps · target 60'
    : n + ' frames · median ' + fmtMs(medianMs) + 'ms · spike ≥ ' + fmtMs(thresholdMs) + 'ms';

  const w = 100, h = 28;
  let pathD = '', areaD = '';
  for (let i = 0; i < n; i++) {
    const x = (i / Math.max(1, n - 1)) * w;
    const v = showFps ? series[i] : Math.min(series[i], max);
    const y = h - (v / max) * h;
    pathD += (i === 0 ? 'M' : 'L') + x.toFixed(2) + ',' + y.toFixed(2) + ' ';
    areaD += (i === 0 ? 'M' : 'L') + x.toFixed(2) + ',' + y.toFixed(2) + ' ';
  }
  areaD += 'L' + w + ',' + h + ' L0,' + h + ' Z';

  const thresholdY = h - (Math.min(thresholdS, max) / max) * h;
  const medianY = h - (medianS / max) * h;

  // Spike markers within window. Color red because they're bad.
  const firstTick = lastFrames.firstTick, lastTick = lastFrames.lastTick;
  let marks = '';
  if (lastFrames.spikeMarks && firstTick != null) {
    for (const m of lastFrames.spikeMarks) {
      const x = ((m.tick - firstTick) / Math.max(1, lastTick - firstTick)) * w;
      const v = showFps ? Math.min(120, 1000 / Math.max(0.5, m.ms)) : m.ms;
      const y = h - (Math.min(v, max) / max) * h;
      marks += '<circle cx=""' + x.toFixed(2) + '"" cy=""' + y.toFixed(2) + '"" r=""0.8"" fill=""#b94e58"" stroke=""#0a0d12"" stroke-width=""0.15""/>';
    }
  }

  // Series color: signature electric blue for ms, cool cyan for fps.
  const seriesColor = showFps ? '#4ab8c2' : '#4a9eff';
  svg.innerHTML = `
    <defs>
      <linearGradient id='g-area' x1='0' y1='0' x2='0' y2='1'>
        <stop offset='0%' stop-color='${seriesColor}' stop-opacity='0.45'/>
        <stop offset='100%' stop-color='${seriesColor}' stop-opacity='0.02'/>
      </linearGradient>
    </defs>
    <line x1='0' y1='${thresholdY}' x2='${w}' y2='${thresholdY}' stroke='#ff9e64' stroke-width='0.25' stroke-dasharray='0.8,0.8'/>
    <line x1='0' y1='${medianY}' x2='${w}' y2='${medianY}' stroke='#565f89' stroke-width='0.2' stroke-dasharray='0.5,0.8'/>
    <path d='${areaD}' fill='url(#g-area)'/>
    <path d='${pathD}' fill='none' stroke='${seriesColor}' stroke-width='0.5' stroke-linejoin='round' stroke-linecap='round'/>
    ${marks}
  `;
}

// Frame-chart mode toggle.
const chartModeEl = document.getElementById('chart-mode');
if (chartModeEl) {
  chartModeEl.addEventListener('click', e => {
    const b = e.target.closest('button');
    if (!b) return;
    frameChartMode = b.dataset.mode;
    document.querySelectorAll('#chart-mode button').forEach(x => x.classList.toggle('active', x === b));
    renderFrameChart();
  });
}

function renderDonut() {
  const svg = document.getElementById('donut-svg');
  const legend = document.getElementById('donut-legend');
  const pctEl = document.getElementById('donut-pct');
  const nameEl = document.getElementById('donut-name');
  const msEl = document.getElementById('donut-ms');
  const sub = document.getElementById('donut-sub');

  if (!lastMods || !lastMods.worldLoaded || !lastMods.mods) {
    svg.innerHTML = ''; legend.innerHTML = '';
    pctEl.textContent = '—'; nameEl.textContent = '—'; msEl.textContent = '';
    sub.textContent = '—';
    return;
  }
  const sorted = lastMods.mods.slice().filter(m => m.cpuMs > 0.001).sort((a, b) => b.cpuMs - a.cpuMs);
  const total = sorted.reduce((s, m) => s + m.cpuMs, 0);
  if (total <= 0) { svg.innerHTML = ''; legend.innerHTML = ''; sub.textContent = 'idle'; return; }
  sub.textContent = sorted.length + ' active · ' + fmtMs(total) + ' ms/t';

  const top = sorted.slice(0, 6);
  const rest = sorted.slice(6);
  const restSum = rest.reduce((s, m) => s + m.cpuMs, 0);

  let acc = 0;
  let paths = '';
  for (const m of top) {
    const frac = m.cpuMs / total;
    paths += donutSlice(acc, acc + frac, modColor(m.id));
    acc += frac;
  }
  if (restSum > 0) paths += donutSlice(acc, 1, '#3a3f4a');
  svg.innerHTML = paths;

  const headliner = top[0];
  pctEl.textContent = (headliner.cpuMs / total * 100).toFixed(0) + '%';
  nameEl.textContent = truncate(headliner.name, 18);
  msEl.textContent = fmtMs(headliner.cpuMs) + ' ms/t';

  legend.innerHTML = top.map(m => `
    <div class='leg'>
      <span class='sw' style='background:${modColor(m.id)}'></span>
      <span class='nm'>${escapeHtml(m.name)}</span>
      <span class='pc'>${(m.cpuMs / total * 100).toFixed(1)}%</span>
    </div>
  `).join('') + (rest.length > 0 ? `<div class='leg'><span class='sw' style='background:#3a3f4a'></span><span class='nm'>+ ${rest.length} more</span><span class='pc'>${(restSum/total*100).toFixed(1)}%</span></div>` : '');
}

function donutSlice(from, to, color) {
  // Outer radius 50, inner 32; convert fraction (0-1) to angle (-PI/2 start, clockwise).
  const startA = -Math.PI / 2 + from * Math.PI * 2;
  const endA   = -Math.PI / 2 + to   * Math.PI * 2;
  const r1 = 50, r2 = 32;
  const x1 = Math.cos(startA) * r1, y1 = Math.sin(startA) * r1;
  const x2 = Math.cos(endA)   * r1, y2 = Math.sin(endA)   * r1;
  const x3 = Math.cos(endA)   * r2, y3 = Math.sin(endA)   * r2;
  const x4 = Math.cos(startA) * r2, y4 = Math.sin(startA) * r2;
  const largeArc = (to - from) > 0.5 ? 1 : 0;
  return `<path d='M ${x1} ${y1} A ${r1} ${r1} 0 ${largeArc} 1 ${x2} ${y2} L ${x3} ${y3} A ${r2} ${r2} 0 ${largeArc} 0 ${x4} ${y4} Z' fill='${color}'/>`;
}

function renderTrendSparklines() {
  if (!lastFrames || !lastFrames.frameMs || lastFrames.frameMs.length === 0) {
    document.getElementById('spark-frame').innerHTML = '';
    document.getElementById('spark-alloc').innerHTML = '';
    document.getElementById('spark-spike').innerHTML = '';
    return;
  }
  drawSpark('spark-frame', lastFrames.frameMs, '#4a9eff');
  // alloc: derive a rough proxy from gc time (no per-tick alloc series). Substitute zero series otherwise.
  drawSpark('spark-alloc', lastFrames.gcMs || [], '#c39ad8');
  // spike density: counts within a sliding window. For simplicity show a marker per spike.
  drawSpikeMarkers('spark-spike', lastFrames);
}

function drawSpark(id, vals, color) {
  const svg = document.getElementById(id);
  if (!vals || vals.length === 0) { svg.innerHTML = ''; return; }
  const max = Math.max(0.5, Math.max(...vals));
  const n = vals.length;
  let d = '';
  for (let i = 0; i < n; i++) {
    const x = (i / Math.max(1, n - 1)) * 100;
    const y = 16 - (vals[i] / max) * 14;
    d += (i === 0 ? 'M' : 'L') + x.toFixed(2) + ',' + y.toFixed(2) + ' ';
  }
  svg.innerHTML = `<path d='${d}' fill='none' stroke='${color}' stroke-width='0.5'/>`;
}

function drawSpikeMarkers(id, frames) {
  const svg = document.getElementById(id);
  if (!frames.spikeMarks || frames.spikeMarks.length === 0 || frames.firstTick == null) {
    svg.innerHTML = '<line x1=""0"" y1=""15"" x2=""100"" y2=""15"" stroke=""#3a3f4a"" stroke-width=""0.2""/>';
    return;
  }
  let marks = '';
  const span = Math.max(1, frames.lastTick - frames.firstTick);
  for (const m of frames.spikeMarks) {
    const x = ((m.tick - frames.firstTick) / span) * 100;
    marks += `<line x1='${x.toFixed(2)}' y1='2' x2='${x.toFixed(2)}' y2='14' stroke='#c97f3c' stroke-width='0.4'/>`;
  }
  svg.innerHTML = '<line x1=""0"" y1=""15"" x2=""100"" y2=""15"" stroke=""#3a3f4a"" stroke-width=""0.2""/>' + marks;
}

function renderNowPlaying() {
  const root = document.getElementById('nowlist');
  const sub = document.getElementById('now-sub');
  if (!lastSegments || !lastSegments.open || lastSegments.open.length === 0) {
    root.innerHTML = '<div class=""empty-line"">no open segments — wander into a biome, fight a boss, wait for weather</div>';
    sub.textContent = '0 open';
    return;
  }
  sub.textContent = lastSegments.open.length + ' open';
  root.innerHTML = lastSegments.open
    .slice().sort((a, b) => familyWeight(a.family) - familyWeight(b.family))
    .map(s => {
      const top = s.topModName
        ? `<span class='mod'>${truncate(s.topModName, 16)}</span> · ${fmtMs(s.topModMsPerTick)}ms/t`
        : '<span class=""muted"">—</span>';
      return `<div class='now-seg rich' data-family='${s.family}'>
        <span class='swatch'></span>
        <span class='name-block'>
          <span class='top'><span class='family-tag'>${escapeHtml(s.family)}</span>${escapeHtml(s.name)}</span>
          <span class='sub'>${fmtDuration(s.elapsedMs)} · ${fmtInt(s.ticks)} ticks${s.spikeCount > 0 ? ' · ⚡' + s.spikeCount : ''}${s.deathCount > 0 ? ' · ☠' + s.deathCount : ''}</span>
        </span>
        <span class='meta'>${top}</span>
      </div>`;
    }).join('');
}
function familyWeight(f) {
  const order = ['Boss', 'Invasion', 'UserBookmark', 'Weather', 'Subworld', 'Combat', 'Hardmode', 'DeathBracket', 'Biome'];
  const i = order.indexOf(f); return i < 0 ? 99 : i;
}

function renderNowEvents() {
  const root = document.getElementById('nowevents');
  // /api/events delivers a pre-merged, pre-sorted, capped feed —
  // the JS just renders. Replaces the previous client-side merge
  // across segments + spikes (which had no access to stalls and
  // got stall+segment interleaving wrong).
  if (!lastEvents || !lastEvents.events || lastEvents.events.length === 0) {
    root.innerHTML = '<div class=""empty-line"">nothing yet — events appear as segments close + spikes fire</div>';
    return;
  }
  root.innerHTML = lastEvents.events.map(e =>
    `<div class='event' data-kind='${e.kind}'>
      <span class='glyph'>${glyphFor(e.kind)}</span>
      <span class='what'>${escapeHtml(e.text)}</span>
      <span class='when'>${fmtAgo(e.unixMs)}</span>
    </div>`
  ).join('');
}
function glyphFor(kind) {
  return ({ 'boss-kill':'✓', 'death':'☠', 'spike':'⚡', 'stall':'⏸', 'segment':'↺' })[kind] || '·';
}
";
}
