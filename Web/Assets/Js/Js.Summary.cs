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
    root.innerHTML = emptyState('no minutes recorded yet');
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

  // Band edges shared by bandClass + the within-band shade. Each minute tile gets
  // its categorical band (p0..p4) AND a 0..1 position within that band's ms range,
  // so even an all-smooth session (every tile p0) still shows minute-to-minute
  // texture — a faster minute reads lighter, a slower one darker, keeping the tile
  // an honest magnitude encoding rather than a flat status block.
  const bandEdges = [0, 17, 25, 40, 60];
  function bandClass(avgMs) {
    if (avgMs <= 17)  return 'p0';
    if (avgMs <= 25)  return 'p1';
    if (avgMs <= 40)  return 'p2';
    if (avgMs <= 60)  return 'p3';
    return 'p4';
  }
  // 0 = at the cool edge of the band, 1 = at the hot edge. The top band (>60ms)
  // has no upper bound, so we span it across the next 40ms and clamp.
  function bandShade(avgMs) {
    let lo = 0, hi = 60 + 40;
    for (let i = 0; i < bandEdges.length; i++) {
      if (avgMs <= bandEdges[i] || i === bandEdges.length - 1) {
        lo = bandEdges[i - 1] != null ? bandEdges[i - 1] : 0;
        hi = bandEdges[i] != null && avgMs <= bandEdges[i] ? bandEdges[i] : 60 + 40;
        break;
      }
    }
    const span = (hi - lo) || 1;
    return Math.max(0, Math.min(1, (avgMs - lo) / span));
  }

  sub.textContent = buckets.length + ' minute(s) · ' + (lastHeatmap.bossOverlays?.length || 0) + ' boss segment(s)';
  root.innerHTML = buckets.map(b => {
    const cls = bandClass(b.avgMs);
    const boss = bossLabelFor(b);
    const time = new Date(b.startUnixMs).toLocaleTimeString();
    const tip = (boss ? boss.name + ' · ' : '') + time + ' · avg ' + fmtMs(b.avgMs) + 'ms · worst ' + fmtMs(b.worstMs) + 'ms';
    return `<div class='hm-cell ${cls} ${boss ? 'boss' : ''}' style='--hm-t:${bandShade(b.avgMs).toFixed(3)}' data-tip='${escapeHtml(tip)}'></div>`;
  }).join('');
}

// Render (or re-render) the ms/fps segmented toggle into the chart header.
function renderFrameModeToggle() {
  const host = document.getElementById('chart-mode');
  if (!host) return;
  host.innerHTML = segmented({
    id: 'frame-mode', attr: 'data-mode', active: frameChartMode,
    options: [{ value: 'ms', label: 'ms' }, { value: 'fps', label: 'fps' }],
  });
}

function renderFrameChart() {
  const chart = document.getElementById('frame-chart');
  const sub = document.getElementById('chart-sub');
  const title = document.getElementById('chart-title');
  const empty = document.getElementById('chart-empty');
  if (!document.getElementById('frame-mode')) renderFrameModeToggle();
  const dbMode = lastNow && lastNow.source === 'db';
  const showFps = frameChartMode === 'fps';
  if (!lastFrames || !lastFrames.worldLoaded || !lastFrames.frameMs || lastFrames.frameMs.length === 0) {
    // No live trace. In db mode that's expected (the live per-tick trace
    // isn't persisted), so we relabel to a last-session framing and show a
    // note inside the panel instead of a blank chart. Outside db mode this
    // is the genuine 'no data yet' case.
    chart.innerHTML = '';
    title.textContent = dbMode
      ? (showFps ? 'fps · last session' : 'frame time · last session')
      : (showFps ? 'fps · last 30s' : 'frame time · last 30s');
    if (dbMode && empty) {
      empty.innerHTML = emptyState(""live trace isn't stored — showing last session summary"");
      empty.classList.remove('hidden');
      sub.textContent = lastNow.sessionLabel || 'last session';
    } else {
      if (empty) empty.classList.add('hidden');
      sub.textContent = '—';
    }
    return;
  }
  // Live trace present — restore the live chrome.
  if (empty) empty.classList.add('hidden');
  const ms = lastFrames.frameMs;
  const n = ms.length;

  // Map series + axis depending on toggle.
  title.textContent = showFps ? 'fps · last 30s' : 'frame time · last 30s';
  // In fps mode each frame-time maps to an instantaneous fps (1000/ms).
  const series = showFps ? ms.map(v => v > 0 ? 1000 / Math.max(1, v) : 0) : ms;
  const sortedMs = ms.slice().sort((a, b) => a - b);
  const medianMs = sortedMs[Math.floor(n / 2)];
  const thresholdMs = medianMs * 2;
  const sortedSeries = series.slice().sort((a, b) => a - b);
  const medianS = sortedSeries[Math.floor(n / 2)];
  // Reference rules: 60fps line + the per-session median, expressed in the
  // active units so the rule sits where the eye expects it.
  const sixtyVal = showFps ? 60 : 1000 / 60;
  const medianVal = medianS;

  sub.textContent = showFps
    ? n + ' frames · median ' + medianS.toFixed(0) + ' fps · target 60'
    : n + ' frames · median ' + fmtMs(medianMs) + 'ms · spike ≥ ' + fmtMs(thresholdMs) + 'ms';

  // Spike markers, positioned by tick within the window (0..1 along the axis).
  let marks = [];
  const firstTick = lastFrames.firstTick, lastTick = lastFrames.lastTick;
  if (lastFrames.spikeMarks && firstTick != null) {
    const span = Math.max(1, lastTick - firstTick);
    for (const m of lastFrames.spikeMarks) {
      marks.push({ at: Math.max(0, Math.min(1, (m.tick - firstTick) / span)), color: 'var(--spike)' });
    }
  }

  // Rules carry NO inline label: lineChart draws rule labels as SVG text at the
  // right edge, on top of the trace + spike bars, so they collide with the data.
  // We draw the dashed lines unlabelled and annotate them with a compact inset
  // key in the top-right gutter (.chart-keys) instead, clear of the plot.
  const fmtRule = showFps ? (v => v.toFixed(0) + ' fps') : (v => fmtMs(v) + 'ms');
  const keys =
    `<div class='chart-keys'>` +
      `<span class='chart-key'><span class='ck-rule' style='border-color:var(--muted)'></span>60 fps · ${fmtRule(sixtyVal)}</span>` +
      `<span class='chart-key'><span class='ck-rule' style='border-color:var(--dim)'></span>median · ${fmtRule(medianVal)}</span>` +
    `</div>`;
  chart.innerHTML = lineChart({
    series: [{ values: series, color: 'var(--accent)', area: true }],
    rules: [
      { value: sixtyVal, color: 'var(--muted)' },
      { value: medianVal, color: 'var(--dim)' },
    ],
    markers: marks,
    axis: true,
    fmt: showFps ? (v => v.toFixed(0)) : fmtMs,
  }) + keys;
}

// Frame-chart mode toggle (delegated; the segmented control is re-rendered).
const chartModeEl = document.getElementById('chart-mode');
if (chartModeEl) {
  chartModeEl.addEventListener('click', e => {
    const b = e.target.closest('button');
    if (!b) return;
    frameChartMode = b.dataset.mode;
    renderFrameModeToggle();
    renderFrameChart();
  });
}

function renderDonut() {
  const chart = document.getElementById('donut-svg');
  const legendEl = document.getElementById('donut-legend');
  const sub = document.getElementById('donut-sub');

  if (!lastMods || !lastMods.worldLoaded || !lastMods.mods) {
    chart.innerHTML = ''; legendEl.innerHTML = '';
    sub.textContent = '—';
    return;
  }
  const sorted = lastMods.mods.slice().filter(m => m.cpuMs > 0.001).sort((a, b) => b.cpuMs - a.cpuMs);
  const total = sorted.reduce((s, m) => s + m.cpuMs, 0);
  if (total <= 0) { chart.innerHTML = ''; legendEl.innerHTML = ''; sub.textContent = 'idle'; return; }
  sub.textContent = sorted.length + ' active · ' + fmtMs(total) + ' ms/t';

  const top = sorted.slice(0, 6);
  const rest = sorted.slice(6);
  const restSum = rest.reduce((s, m) => s + m.cpuMs, 0);

  // Top-6 mod slices + one aggregated 'rest' slice, coloured per-mod.
  const data = top.map(m => ({
    id: m.id, value: m.cpuMs, label: m.name, color: modColor(m.id),
    valueLabel: (m.cpuMs / total * 100).toFixed(1) + '%',
  }));
  if (restSum > 0) data.push({ value: restSum, label: '+ ' + rest.length + ' more', color: 'var(--surface-2)', valueLabel: (restSum / total * 100).toFixed(1) + '%' });

  const headliner = top[0];
  chart.innerHTML = donut({
    data, inner: 0.6, w: 170,
    center: {
      top: (headliner.cpuMs / total * 100).toFixed(0) + '%',
      mid: truncate(headliner.name, 18),
      bot: fmtMs(headliner.cpuMs) + ' ms/t',
    },
  });

  legendEl.innerHTML = legend(data.map(d => ({ color: d.color, label: d.label, value: d.valueLabel, id: d.id })), { stack: true });
}

// Donut interactivity: clicking a slice or a legend row opens that mod's card
// (the same drawer the Summary mod tree opens). Bound once on the stable
// containers; renderDonut only swaps their innerHTML, so the delegation holds.
(function bindDonutInteractivity() {
  function pick(e) {
    const el = e.target.closest('[data-mod]');
    if (el) openModCard(parseInt(el.dataset.mod, 10));
  }
  const chart = document.getElementById('donut-svg');
  const leg = document.getElementById('donut-legend');
  if (chart) chart.addEventListener('click', pick);
  if (leg) leg.addEventListener('click', pick);
})();

function renderTrendSparklines() {
  const title = document.getElementById('trends-title');
  const rows = document.getElementById('trend-rows');
  const empty = document.getElementById('trends-empty');
  const dbMode = lastNow && lastNow.source === 'db';
  if (!lastFrames || !lastFrames.frameMs || lastFrames.frameMs.length === 0) {
    document.getElementById('spark-frame').innerHTML = '';
    document.getElementById('spark-alloc').innerHTML = '';
    document.getElementById('spark-spike').innerHTML = '';
    // In db mode the per-series live trace isn't stored, so the labelled rows
    // would sit empty (looks unfinished). Hide them and show one note instead,
    // and relabel the panel to its last-session framing. Outside db mode the
    // rows simply stay blank as the genuine no-data state.
    if (dbMode) {
      if (title) title.textContent = 'session trend · last session';
      if (rows) rows.classList.add('hidden');
      if (empty) {
        empty.innerHTML = emptyState(""live trace isn't stored — showing last session summary"");
        empty.classList.remove('hidden');
      }
    } else {
      if (title) title.textContent = 'session trend · last 30s';
      if (rows) rows.classList.remove('hidden');
      if (empty) empty.classList.add('hidden');
    }
    return;
  }
  // Live trace present — restore the rows + live framing.
  if (title) title.textContent = 'session trend · last 30s';
  if (rows) rows.classList.remove('hidden');
  if (empty) empty.classList.add('hidden');
  drawSpark('spark-frame', lastFrames.frameMs, 'var(--accent)');
  // alloc: derive a rough proxy from gc time (no per-tick alloc series). Substitute zero series otherwise.
  drawSpark('spark-alloc', lastFrames.gcMs || [], 'var(--alloc)');
  // spike density: a marker per spike, positioned by tick within the window.
  drawSpikeMarkers('spark-spike', lastFrames);
}

function drawSpark(id, vals, color) {
  const el = document.getElementById(id);
  if (!el) return;
  el.innerHTML = sparkline(vals, { color, strokeW: 1 });
}

function drawSpikeMarkers(id, frames) {
  const el = document.getElementById(id);
  if (!el) return;
  // Place a marker per spike, indexed along the tick window. sparkline() draws
  // markers as vertical lines; with no series, it renders the markers alone.
  if (!frames.spikeMarks || frames.spikeMarks.length === 0 || frames.firstTick == null) {
    el.innerHTML = sparkline([], {});
    return;
  }
  const span = Math.max(1, frames.lastTick - frames.firstTick);
  const n = 100;
  const markers = frames.spikeMarks.map(m =>
    Math.round(Math.max(0, Math.min(1, (m.tick - frames.firstTick) / span)) * (n - 1)));
  el.innerHTML = sparkline([], { markers, markN: n, markColor: 'var(--spike)' });
}

function renderNowPlaying() {
  const root = document.getElementById('nowlist');
  const sub = document.getElementById('now-sub');
  if (!lastSegments || !lastSegments.open || lastSegments.open.length === 0) {
    root.innerHTML = emptyState('no open segments — wander into a biome, fight a boss, wait for weather');
    sub.textContent = '0 open';
    return;
  }
  const open = lastSegments.open.slice().sort((a, b) => familyWeight(a.family) - familyWeight(b.family));
  sub.textContent = open.length + ' open';
  const cols = '0.32rem minmax(0, 1.4fr) auto';
  const rowsHtml = rowList(open
    .map(s => {
      const meta = s.topModName
        ? `<span class='now-mod'>${escapeHtml(truncate(s.topModName, 16))}</span> · ${fmtMs(s.topModMsPerTick)}ms/t`
        : `<span class='muted'>—</span>`;
      return row({
        cols,
        cells: [
          `<span class='now-swatch' style='background:${familyColor(s.family)}'></span>`,
          `<span class='now-name'>` +
            `<span class='now-top'><span class='chip'>${escapeHtml(s.family)}</span>${escapeHtml(s.name)}</span>` +
            `<span class='now-sub'>${fmtDuration(s.elapsedMs)} · ${fmtInt(s.ticks)} ticks${s.spikeCount > 0 ? ' · ⚡' + s.spikeCount : ''}${s.deathCount > 0 ? ' · ☠' + s.deathCount : ''}</span>` +
          `</span>`,
          `<span class='now-meta'>${meta}</span>`,
        ],
      });
    }));
  // Muted footer so a short open-segment list earns its panel height rather than
  // leaving a tall void: a quiet 'nothing else open' line closes the list off.
  const foot = `<div class='now-foot'>no further encounters open · new segments appear as you enter biomes, bosses, weather</div>`;
  root.innerHTML = rowsHtml + foot;
}
function familyWeight(f) {
  const order = ['Boss', 'Invasion', 'UserBookmark', 'Weather', 'Subworld', 'Combat', 'Hardmode', 'DeathBracket', 'Biome'];
  const i = order.indexOf(f); return i < 0 ? 99 : i;
}
// Family -> data colour for the now-playing swatch (was driven by [data-family]
// attribute selectors). Boss/Invasion read as danger, the world-state families
// as amber, combat as the spike hue, bookmarks as the accent.
function familyColor(f) {
  return ({
    Boss: 'var(--danger)', Invasion: 'var(--danger)',
    Weather: 'var(--amber)', Hardmode: 'var(--amber)', Subworld: 'var(--amber)',
    Combat: 'var(--spike)', DeathBracket: 'var(--muted)', UserBookmark: 'var(--accent)',
  })[f] || 'var(--good)';
}

function renderNowEvents() {
  const root = document.getElementById('nowevents');
  // /api/events delivers a pre-merged, pre-sorted, capped feed —
  // the JS just renders. Replaces the previous client-side merge
  // across segments + spikes (which had no access to stalls and
  // got stall+segment interleaving wrong).
  if (!lastEvents || !lastEvents.events || lastEvents.events.length === 0) {
    root.innerHTML = emptyState('nothing yet — events appear as segments close + spikes fire');
    return;
  }
  const cols = '1.4em minmax(0, 1fr) auto';
  root.innerHTML = rowList(lastEvents.events.map(e => row({
    cols, cls: 'ev-row',
    attrs: `data-kind='${e.kind}'`,
    cells: [
      `<span class='ev-glyph'>${glyphFor(e.kind)}</span>`,
      `<span class='nm'>${escapeHtml(e.text)}</span>`,
      `<span class='ev-when'>${fmtAgo(e.unixMs)}</span>`,
    ],
  })));
}
function glyphFor(kind) {
  return ({ 'boss-kill':'✓', 'death':'☠', 'spike':'⚡', 'stall':'⏸', 'segment':'↺' })[kind] || '·';
}
";
}
