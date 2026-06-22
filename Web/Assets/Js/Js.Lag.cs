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
    // Lag tab — readable-vocabulary rebuild.
    //
    // The earlier ""adventurous"" shapes (hex grid, fingerprint galaxy,
    // beeswarm, gradient tide, inline sankeys, polar histogram) were
    // rejected as unreadable. Every surface is reshaped onto the shared
    // component vocabulary (Css.Components / Js.Components): rectangular
    // heatmap (.rheat), sortable perf-tinted tables (.dtable), colour-coded
    // split bars (splitBar / splitLegend), chips, stat lines, horizontal
    // bars. No metaphor shapes; no data dropped — every field the endpoints
    // expose is still rendered, just in a quieter, scannable form.
    //
    // Tooltips use the native title= attribute via escapeHtml, matching the
    // backtick-single-quote templating the rest of the dashboard uses.
    private const string JsLag = @"
// ====== LAG TAB =======================================================
let lastLagClusters = null, lastGcPressure = null, lastSegmentLagDensity = null,
    lastAllocCausality = null, lastLagRhythm = null;

// Selection + sort state for the reshaped surfaces.
let lagGalaxySelected = null;          // selected fingerprintId in the cluster table
let lagClusterSort = { key: 'eventCount', dir: -1 };
let lagDensitySort = { key: 'vsBaseline', dir: -1 };

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

// Sortable-header helper: emits a <th> that toggles a {key,dir} sort state
// and re-runs the given render fn. Matches the shared .dtable th contract
// (.l for left-align, .sortable, .sorted when active).
function lagSortTh(state, key, label, leftAlign, onSort) {
  const active = state.key === key;
  const arrow = active ? (state.dir < 0 ? ' ▾' : ' ▴') : '';
  const cls = (leftAlign ? 'l ' : '') + 'sortable' + (active ? ' sorted' : '');
  return `<th class='${cls}' onclick='${onSort}(""${key}"")'>${escapeHtml(label)}${arrow}</th>`;
}

function lagApplySort(rows, state) {
  const k = state.key, dir = state.dir;
  return rows.slice().sort((a, b) => {
    const va = a[k], vb = b[k];
    if (typeof va === 'string' || typeof vb === 'string') {
      return dir * String(va == null ? '' : va).localeCompare(String(vb == null ? '' : vb));
    }
    return dir * (((va || 0)) - ((vb || 0)));
  });
}

// ---------------------------------------------------------------- KPI strip
// Standard KPI cells (kept). events / session lag ms / worst p95 ms /
// load factor x — derived exactly as before from clusters + density.
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

// ---------------------------------------------------------------- Heatmap (rectangular)
// OLD: cause × context flat-top hex grid.
// NEW: shared .rheat rectangular heatmap — rows = causeClass, cols = context.
// Each cell shows eventCount, background = heatFill(eventCount/maxCount);
// zero cells get .rh-cell.zero. Native title= carries the full breakdown.
//
// Degeneration guard: when every cluster shares one context (or context is
// empty / '-'), a 1-column heatmap is pointless — so we fall back to a ranked
// horizontal bar list, one row per cause (bar width = eventCount/maxCount via
// cellBar). The genuine multi-context case (2+ distinct contexts) still gets
// the real .rheat grid. The section header is static in IndexHtml.Lag.cs; this
// renderer only owns #lag-heatmap-body.
function renderLagHeatmap() {
  const root = document.getElementById('lag-heatmap-body');
  if (!root) return;
  const cells = (lastLagClusters && lastLagClusters.causeContext) || [];

  if (cells.length === 0) {
    root.innerHTML = `<div class='lag-empty'>no events observed</div>`;
    return;
  }

  // Build ordered row (cause) + column (context) axes preserving first-seen order.
  const causes = [], contexts = [];
  const causeSet = new Set(), ctxSet = new Set();
  for (const c of cells) {
    if (!causeSet.has(c.causeClass)) { causeSet.add(c.causeClass); causes.push(c.causeClass); }
    if (!ctxSet.has(c.context))      { ctxSet.add(c.context);      contexts.push(c.context); }
  }

  // Distinct, meaningful contexts: drop empty / '-' placeholders. If 0 or 1
  // remain, the grid would degenerate to a single column, so render a bar list.
  const realContexts = contexts.filter(c => c != null && c !== '' && c !== '-');
  if (realContexts.length <= 1) {
    // Ranked horizontal bar list: aggregate eventCount + totalMs per cause
    // across whatever (single / placeholder) context each cluster carried.
    const agg = new Map();
    for (const c of cells) {
      const a = agg.get(c.causeClass) || { eventCount: 0, totalMs: 0 };
      a.eventCount += c.eventCount || 0;
      a.totalMs += c.totalMs || 0;
      agg.set(c.causeClass, a);
    }
    const rows = causes.map(cause => ({ cause, ...(agg.get(cause) || { eventCount: 0, totalMs: 0 }) }))
                       .sort((a, b) => b.eventCount - a.eventCount);
    let maxCount = 0;
    for (const r of rows) if (r.eventCount > maxCount) maxCount = r.eventCount;
    if (maxCount <= 0) maxCount = 1;
    const ctxNote = realContexts.length === 1 ? ` · ${escapeHtml(realContexts[0])}` : '';
    root.innerHTML = `<div class='lag-caption'>single context${ctxNote} · ranked by events</div>
      <div class='lag-causebars'>` + rows.map(r => {
        const frac = r.eventCount / maxCount;
        const tip = `${r.cause} · ${fmtInt(r.eventCount)} events · ${fmtMs(r.totalMs)} ms total`;
        return `<div class='lag-causebar-row' title='${escapeHtml(tip)}'>
          <span class='lbl'>${escapeHtml(r.cause || '—')}</span>
          <span class='bar'>${cellBar(frac, 'var(--accent)')}</span>
          <span class='val'>${fmtInt(r.eventCount)}</span>
        </div>`;
      }).join('') + `</div>`;
    return;
  }

  const key = (a, b) => a + '\x1f' + b;
  const map = new Map();
  let maxCount = 0;
  for (const c of cells) {
    map.set(key(c.causeClass, c.context), c);
    if ((c.eventCount || 0) > maxCount) maxCount = c.eventCount;
  }

  // Grid: 1 row-label column + one column per real context.
  const cols = `minmax(7rem, auto) repeat(${realContexts.length}, minmax(2.4rem, 1fr))`;
  let html = `<div class='rheat lag-rheat' style='grid-template-columns:${cols}'>`;

  // Header row: empty corner + context labels.
  html += `<div class='rh-corner'></div>`;
  for (const ctx of realContexts) {
    html += `<div class='rh-col' title='${escapeHtml(ctx)}'>${escapeHtml(ctx)}</div>`;
  }
  // Body rows.
  for (const cause of causes) {
    html += `<div class='rh-row' title='${escapeHtml(cause)}'>${escapeHtml(cause)}</div>`;
    for (const ctx of realContexts) {
      const cell = map.get(key(cause, ctx));
      const count = cell ? (cell.eventCount || 0) : 0;
      const ms = cell ? (cell.totalMs || 0) : 0;
      if (count === 0) {
        html += `<div class='rh-cell zero' title='${escapeHtml(cause)} / ${escapeHtml(ctx)} · 0 events'>·</div>`;
      } else {
        const t = maxCount > 0 ? count / maxCount : 0;
        const tip = `${cause} / ${ctx} · ${fmtInt(count)} events · ${fmtMs(ms)} ms total`;
        html += `<div class='rh-cell' style='background:${heatFill(t)}' title='${escapeHtml(tip)}'>${fmtInt(count)}</div>`;
      }
    }
  }
  html += `</div>`;
  root.innerHTML = html;
}

// ---------------------------------------------------------------- Cluster table
// OLD: fingerprint galaxy (circles in 2D space, size = events, ring = share).
// NEW: shared sortable .dtable. Columns: cause + context chips, events, p95
// (perf-tinted), total ms, top mod + share (inline split bar coloured by
// modColor). Click a row to select it (tr.sel) and fill the detail strip below.
//
// The cryptic raw fingerprintId (e.g. 'Spike|m6|-||h0') is no longer a visible
// cell — the leading column leads with the human-readable causeClass and the
// context chips; the raw id moves into a title= tooltip on the cause cell (and
// a dimmed secondary line in the detail strip). Selection is still keyed on
// fingerprintId internally.
//
// Scroll-stability: the section header lives in IndexHtml.Lag.cs; the table is
// written into the STABLE scroll container #lag-clusters-body via setHTML(),
// which preserves scrollTop so a 3s poll doesn't snap the list to the top. The
// detail strip is a separate static node (#lag-clusters-detail) replaced
// outside the scroll container.
function lagGalaxySort(key) {
  if (lagClusterSort.key === key) lagClusterSort.dir *= -1;
  else lagClusterSort = { key, dir: -1 };
  renderLagGalaxy();
}

function renderLagGalaxy() {
  const root = document.getElementById('lag-clusters-body');
  const detailRoot = document.getElementById('lag-clusters-detail');
  if (!root) return;
  const clusters = (lastLagClusters && lastLagClusters.clusters) || [];

  if (clusters.length === 0) {
    setHTML(root, `<div class='lag-empty'>no clusters yet</div>`);
    if (detailRoot) detailRoot.innerHTML = '';
    return;
  }

  const sorted = lagApplySort(clusters, lagClusterSort);
  // perf tint for p95 is a ratio against the median p95 across clusters, so
  // tintClass spreads typical → hot the way it does on the other tabs (1 =
  // typical). Median is robust to a single dominant outlier.
  const p95s = clusters.map(c => c.p95Ms || 0).sort((a, b) => a - b);
  const medianP95 = p95s.length > 0 ? p95s[Math.floor(p95s.length / 2)] : 0;

  const head = `<thead><tr>
    ${lagSortTh(lagClusterSort, 'causeClass', 'cause', true, 'lagGalaxySort')}
    <th class='l'>context</th>
    ${lagSortTh(lagClusterSort, 'eventCount', 'events', false, 'lagGalaxySort')}
    ${lagSortTh(lagClusterSort, 'p95Ms', 'p95', false, 'lagGalaxySort')}
    ${lagSortTh(lagClusterSort, 'totalMs', 'total', false, 'lagGalaxySort')}
    ${lagSortTh(lagClusterSort, 'topModShare', 'top mod', true, 'lagGalaxySort')}
  </tr></thead>`;

  const body = sorted.map(c => {
    const sel = c.fingerprintId === lagGalaxySelected ? ' sel' : '';
    // Context chips: biome, each weather flag, hardmode.
    let ctxChips = '';
    if (c.primaryBiome) ctxChips += `<span class='chip'>${escapeHtml(c.primaryBiome)}</span>`;
    if (c.weatherFlags) ctxChips += `<span class='chip cool'>${escapeHtml(c.weatherFlags)}</span>`;
    if (c.hardmode) ctxChips += `<span class='chip warn'>hardmode</span>`;
    if (!ctxChips) ctxChips = `<span class='muted'>—</span>`;

    const tint = tintClass(medianP95 > 0 ? (c.p95Ms || 0) / medianP95 : 0);
    const colour = modColor(c.topModId || 0);
    const share = Math.max(0, Math.min(1, c.topModShare || 0));
    const sharePct = (share * 100).toFixed(0) + '%';
    const topModName = c.topModName || '—';
    // Inline split bar: top mod's share vs the rest, coloured by modColor.
    const modBar = splitBar([
      { frac: share, color: colour, label: topModName, value: sharePct },
      { frac: 1 - share, color: 'var(--surface-2)' },
    ], { thin: true });
    const modCell = `<div class='lag-topmod' title='${escapeHtml(topModName + ' · ' + sharePct)}'>
        <span class='nm' style='color:${colour}'>${escapeHtml(truncate(topModName, 16))}</span>
        <span class='sh'>${sharePct}</span>
        ${modBar}
      </div>`;

    // Cause cell: human-readable causeClass; raw fingerprintId in the tooltip.
    const fpId = c.fingerprintId || '';
    const causeTip = fpId ? (c.causeClass || '—') + ' · id ' + fpId : (c.causeClass || '—');
    return `<tr class='clickable${sel}' onclick='lagGalaxyPick(""${escapeHtml(fpId)}"")'>
      <td class='l' title='${escapeHtml(causeTip)}'>${escapeHtml(c.causeClass || '—')}</td>
      <td class='l'>${ctxChips}</td>
      <td>${fmtInt(c.eventCount)}</td>
      <td class='${tint}'>${fmtMs(c.p95Ms)}</td>
      <td>${fmtMs(c.totalMs)}</td>
      <td class='l'>${modCell}</td>
    </tr>`;
  }).join('');

  // Detail strip for the selected fingerprint — same per-fingerprint fields.
  let details = '';
  const pick = clusters.find(c => c.fingerprintId === lagGalaxySelected);
  if (pick) {
    const subBits = [];
    if (pick.primaryBiome) subBits.push(escapeHtml(pick.primaryBiome));
    if (pick.weatherFlags) subBits.push(escapeHtml(pick.weatherFlags));
    if (pick.hardmode) subBits.push('hardmode');
    details = `<div class='lag-detail'>
      <div class='ld-top'>
        <span class='ld-cause'>${escapeHtml(pick.causeClass || '—')}</span>
        <span class='ld-sub'>${subBits.join(' · ') || '—'}</span>
        <span class='ld-fp' title='fingerprint id'>${escapeHtml(pick.fingerprintId || '')}</span>
      </div>
      <div class='ld-stats'>
        <span><b>${fmtInt(pick.eventCount)}</b> events</span>
        <span>p95 <b>${fmtMs(pick.p95Ms)}</b> ms</span>
        <span>total <b>${fmtMs(pick.totalMs)}</b> ms</span>
        <span>top mod <b style='color:${modColor(pick.topModId||0)}'>${escapeHtml(pick.topModName || '—')}</b> ${((pick.topModShare||0)*100).toFixed(0)}%</span>
      </div>
    </div>`;
  } else {
    details = `<div class='lag-hint'>click a row to inspect a fingerprint cluster</div>`;
  }

  setHTML(root, `<table class='dtable'>${head}<tbody>${body}</tbody></table>`);
  if (detailRoot) detailRoot.innerHTML = details;
}
function lagGalaxyPick(fp) {
  if (!fp) return;
  lagGalaxySelected = (lagGalaxySelected === fp) ? null : fp;
  renderLagGalaxy();
}

// ---------------------------------------------------------------- Density table
// OLD: per-segment beeswarm of jittered dots.
// NEW: shared sortable .dtable. Columns: segment, spikes/stalls (per-row
// split bar spike=var(--spike) / stall=var(--stall) plus the two counts),
// events/min, vsBaseline x (perf-tinted via tintClass). Caption shows the
// session baseline.
function lagDensitySortBy(key) {
  if (lagDensitySort.key === key) lagDensitySort.dir *= -1;
  else lagDensitySort = { key, dir: -1 };
  renderLagDensity();
}

function renderLagDensity() {
  // Section header is static in IndexHtml.Lag.cs. The table is written into the
  // STABLE scroll container #lag-density-body via setHTML() so a 3s poll
  // preserves scrollTop; caption + legend are separate static nodes.
  const root = document.getElementById('lag-density-body');
  const capRoot = document.getElementById('lag-density-caption');
  const legendRoot = document.getElementById('lag-density-legend');
  if (!root) return;
  const snap = lastSegmentLagDensity;
  const entries = (snap && snap.entries) || [];

  if (entries.length === 0) {
    setHTML(root, `<div class='lag-empty'>no segments closed yet</div>`);
    if (capRoot) capRoot.innerHTML = '';
    if (legendRoot) legendRoot.innerHTML = '';
    return;
  }

  const baseline = (snap && snap.sessionBaselinePerMin) || 0;
  const sorted = lagApplySort(entries, lagDensitySort);

  const head = `<thead><tr>
    ${lagSortTh(lagDensitySort, 'name', 'segment', true, 'lagDensitySortBy')}
    <th class='l'>spikes / stalls</th>
    ${lagSortTh(lagDensitySort, 'eventsPerMin', 'events/min', false, 'lagDensitySortBy')}
    ${lagSortTh(lagDensitySort, 'vsBaseline', 'vs base', false, 'lagDensitySortBy')}
  </tr></thead>`;

  const body = sorted.map(e => {
    const spikes = e.spikeCount || 0;
    const stalls = e.stallCount || 0;
    const total = spikes + stalls;
    const spikeFrac = total > 0 ? spikes / total : 0;
    const stallFrac = total > 0 ? stalls / total : 0;
    const tip = `${e.name || '—'} · ${fmtInt(spikes)} spikes · ${fmtInt(stalls)} stalls`;
    const bar = splitBar([
      { frac: spikeFrac, color: 'var(--spike)', label: 'spikes', value: fmtInt(spikes) },
      { frac: stallFrac, color: 'var(--stall)', label: 'stalls', value: fmtInt(stalls) },
    ], { thin: true });
    const countsCell = `<div class='lag-spikestall' title='${escapeHtml(tip)}'>
        ${bar}
        <span class='cnt'><span class='spk'>${fmtInt(spikes)}</span> / <span class='stl'>${fmtInt(stalls)}</span></span>
      </div>`;
    const vsTint = tintClass(e.vsBaseline);
    const vsTxt = (isFinite(e.vsBaseline) && e.vsBaseline > 0) ? e.vsBaseline.toFixed(2) + '×' : '—';
    return `<tr>
      <td class='l' title='${escapeHtml((e.family || '') + ' · ' + fmtDuration(e.durationMs))}'>${escapeHtml(e.name || '—')}</td>
      <td class='l'>${countsCell}</td>
      <td>${(e.eventsPerMin || 0).toFixed(1)}</td>
      <td class='${vsTint}'>${vsTxt}</td>
    </tr>`;
  }).join('');

  if (capRoot) capRoot.innerHTML = `<div class='lag-caption'>session baseline · ${baseline.toFixed(2)} events/min</div>`;
  setHTML(root, `<table class='dtable'>${head}<tbody>${body}</tbody></table>`);
  if (legendRoot) legendRoot.innerHTML = `<div class='lag-bar-legend'>${splitLegend([
      { color: 'var(--spike)', label: 'spike' },
      { color: 'var(--stall)', label: 'stall' },
    ])}</div>`;
}

// ---------------------------------------------------------------- GC pressure
// OLD: gradient ""tide"" waveform with wave-break triangles + wind arrows.
// NEW: a normal heap line (SVG area path) with the peak labelled, plus a
// .statline stat block for every GC counter. Generation rates are three
// plain labelled stat rows (no wind arrows). Respects worldLoaded.
//
// Y-axis is AUTO-SCALED to the heap's own min..max via seriesPaths/niceScale.
// The managed heap sits at a near-constant ~7.6-8.0 GB band, so a 0-based axis
// pinned the line to the very top of the box and hid all variation. seriesPaths
// rescales to the data's own padded range; both the scaled baseline (min MB)
// and peak (max MB) are labelled so the band the line lives in is explicit.
// Section header is static in IndexHtml.Lag.cs; this renderer owns #lag-gc-body.
function renderLagGcPressure() {
  const root = document.getElementById('lag-gc-body');
  if (!root) return;
  const gc = lastGcPressure;
  if (!gc || !gc.worldLoaded) {
    root.innerHTML = `<div class='lag-empty'>no gc data yet</div>`;
    return;
  }

  const series = gc.heapMbSeries || [];
  const n = series.length;
  let maxHeap = 0;
  for (const v of series) if (v > maxHeap) maxHeap = v;
  if (maxHeap <= 0) maxHeap = 1;

  // Quiet area chart: auto-scaled filled path under a thin accent line, peak
  // marked. seriesPaths handles the niceScale + path math; the y-axis runs the
  // heap's own padded min..max instead of 0..max so variation is visible.
  const W = 540, H = 120, padX = 8, padTop = 16, padBot = 18;
  const innerW = W - padX * 2;
  const innerH = H - padTop - padBot;
  const sp = seriesPaths(series, { w: W, h: H, padX, padTop, padBot });
  const scale = sp.scale; // { min, max } of the padded heap band
  const span = (scale.max - scale.min) || 1;
  function ptX(i) { return padX + (n > 1 ? (i / (n - 1)) * innerW : innerW / 2); }
  function ptY(v) { return padTop + innerH - ((v - scale.min) / span) * innerH; }

  let peakMark = '';
  if (n > 0) {
    // Peak marker at the max sample, placed on the same auto-scaled axis.
    let peakI = 0; for (let i = 1; i < n; i++) if (series[i] > series[peakI]) peakI = i;
    const px = ptX(peakI), py = ptY(series[peakI]);
    peakMark = `<circle cx='${px.toFixed(1)}' cy='${py.toFixed(1)}' r='2.5' class='gc-peak'/>
      <text x='${px.toFixed(1)}' y='${(py - 5).toFixed(1)}' class='gc-peak-l' text-anchor='middle'>${maxHeap.toFixed(1)} MB</text>`;
  }
  // Axis labels: the scaled band's floor (left, on the baseline) and peak (top).
  const chart = `<svg class='gc-heap' viewBox='0 0 ${W} ${H}' preserveAspectRatio='none'>
      <line x1='${padX}' y1='${padTop + innerH}' x2='${W - padX}' y2='${padTop + innerH}' class='gc-baseline'/>
      ${sp.area ? `<path d='${sp.area}' class='gc-area'/>` : ''}
      ${sp.line ? `<path d='${sp.line}' class='gc-line'/>` : ''}
      ${peakMark}
      <text x='${padX}' y='${(padTop - 5).toFixed(1)}' class='gc-axis'>managed heap MB · 1Hz</text>
      <text x='${W - padX}' y='${(padTop - 5).toFixed(1)}' class='gc-axis' text-anchor='end'>${scale.max.toFixed(0)} MB peak</text>
      <text x='${padX}' y='${(padTop + innerH + 12).toFixed(1)}' class='gc-axis'>${scale.min.toFixed(0)} MB baseline</text>
    </svg>`;

  // Stat block: every GC counter as a .statline row.
  const stats = [
    ['total paused', fmtMs(gc.totalPausedMs) + ' ms'],
    ['freed in pauses', fmtInt(gc.freedMbDuringPauses) + ' MB'],
    ['current heap', (gc.currentManagedMb || 0).toFixed(1) + ' MB'],
    ['peak heap', maxHeap.toFixed(1) + ' MB'],
    ['gen0 total', fmtInt(gc.gen0Total)],
    ['gen1 total', fmtInt(gc.gen1Total)],
    ['gen2 total', fmtInt(gc.gen2Total)],
    ['gen0 / min', (gc.gen0PerMin || 0).toFixed(2)],
    ['gen1 / min', (gc.gen1PerMin || 0).toFixed(2)],
    ['gen2 / min', (gc.gen2PerMin || 0).toFixed(2)],
  ];
  const statBlock = stats.map(([k, v]) =>
    `<div class='statline'><span class='k'>${escapeHtml(k)}</span><span class='v'>${escapeHtml(String(v))}</span></div>`
  ).join('');

  root.innerHTML = `<div class='lag-gc-grid'>
      <div class='lag-gc-stats'>${statBlock}</div>
      <div class='lag-gc-chart'>${chart}</div>
    </div>`;
}

// ---------------------------------------------------------------- Causality cards
// OLD: per-chain inline Sankey (left mod bands → GC node → freed band).
// NEW: per-chain card. A split bar of topContributors[] (frac =
// bytesInWindow / totalBytesInWindow, colour = modColor, label = modName,
// value = bytes + share%), a splitLegend, and a .statline row for gcKind,
// pauseMs, total bytes in window, freed bytes, windowMs. Keeps the empty
// state.
function renderLagCausality() {
  // Section header is static in IndexHtml.Lag.cs; .lag-causality-list is the
  // STABLE scroll container, so cards are written via setHTML() to preserve
  // scrollTop across the 3s poll.
  const root = document.getElementById('lag-causality-body');
  if (!root) return;
  const chains = (lastAllocCausality && lastAllocCausality.chains) || [];

  if (chains.length === 0) {
    setHTML(root, `<div class='lag-empty'>no gc stalls observed</div>`);
    return;
  }

  const cards = chains.map(ch => {
    const contribs = ch.topContributors || [];
    const totalIn = ch.totalBytesInWindow || contribs.reduce((s, c) => s + (c.bytesInWindow || 0), 0) || 1;

    const segs = contribs.map(c => {
      const frac = (c.bytesInWindow || 0) / totalIn;
      const colour = modColor(c.modId);
      const value = fmtBytes(c.bytesInWindow) + ' · ' + (c.sharePct || 0).toFixed(1) + '%';
      return { frac, color: colour, label: c.modName || '—', value };
    });

    const bar = segs.length > 0 ? splitBar(segs, { tall: true }) : `<div class='lag-empty'>no contributors recorded</div>`;
    const legend = segs.length > 0 ? splitLegend(segs) : '';

    const stats = [
      ['gc kind', ch.gcKind || '—'],
      ['pause', fmtMs(ch.pauseMs) + ' ms'],
      ['bytes in window', fmtBytes(ch.totalBytesInWindow)],
      ['freed', fmtBytes(ch.freedBytes)],
      ['window', (ch.windowMs != null ? (ch.windowMs / 1000).toFixed(1) + ' s' : '—')],
    ];
    const statRow = `<div class='lag-chain-stats'>` + stats.map(([k, v]) =>
      `<span class='st'><span class='k'>${escapeHtml(k)}</span><span class='v'>${escapeHtml(String(v))}</span></span>`
    ).join('') + `</div>`;

    return `<div class='lag-chain'>
      ${bar}
      ${legend}
      ${statRow}
    </div>`;
  }).join('');

  setHTML(root, cards);
}

// ---------------------------------------------------------------- Rhythm
// OLD: polar interval histogram + wedges.
// NEW: horizontal interval histogram (one bar per bucket, width =
// count/maxCount, label = interval seconds, value = count) plus a .dtable
// of rhythm clusters (centre ± width, events, top mod + share, share of
// session).
function renderLagRhythm() {
  // Section header + the two-column grid are static in IndexHtml.Lag.cs. The
  // histogram (#lag-rhythm-hist) and the cluster table (#lag-rhythm-clusters,
  // a .lag-table-wrap scroll container) are filled via setHTML() so a 3s poll
  // preserves scrollTop on whichever side the user scrolled.
  const histRoot = document.getElementById('lag-rhythm-hist');
  const clusterRoot = document.getElementById('lag-rhythm-clusters');
  if (!histRoot || !clusterRoot) return;
  const snap = lastLagRhythm;
  const hist = (snap && snap.histogram) || [];
  const clusters = (snap && snap.clusters) || [];

  if (hist.length === 0 && clusters.length === 0) {
    setHTML(histRoot, `<div class='lag-empty'>not enough events for periodicity</div>`);
    setHTML(clusterRoot, '');
    return;
  }

  // Horizontal histogram: interval bucket label + bar + count.
  let maxCount = 0;
  for (const b of hist) if ((b.count || 0) > maxCount) maxCount = b.count;
  if (maxCount <= 0) maxCount = 1;

  const histHtml = hist.length === 0
    ? `<div class='lag-empty'>no interval histogram yet</div>`
    : hist.map(b => {
        const frac = (b.count || 0) / maxCount;
        const tip = `${b.intervalSeconds.toFixed(2)}s interval · ${fmtInt(b.count)} events`;
        return `<div class='lag-hist-row' title='${escapeHtml(tip)}'>
          <span class='lbl'>${b.intervalSeconds.toFixed(2)}s</span>
          <span class='bar'>${cellBar(frac, 'var(--accent)')}</span>
          <span class='val'>${fmtInt(b.count)}</span>
        </div>`;
      }).join('');

  // Cluster table.
  let clusterTable;
  if (clusters.length === 0) {
    clusterTable = `<div class='lag-empty'>no rhythm clusters detected</div>`;
  } else {
    const rows = clusters.map(c => {
      const colour = modColor(c.topModId || 0);
      const share = Math.max(0, Math.min(1, c.shareOfSession || 0));
      const topModName = c.topModName || '—';
      const topShare = ((c.topModShare || 0) * 100).toFixed(0) + '%';
      return `<tr>
        <td class='l'>${c.centreSeconds.toFixed(2)}s ± ${c.widthSeconds.toFixed(2)}s</td>
        <td>${fmtInt(c.eventCount)}</td>
        <td class='l'><span class='nm' style='color:${colour}'>${escapeHtml(truncate(topModName, 16))}</span> <span class='muted'>${topShare}</span></td>
        <td>${(share * 100).toFixed(1)}%</td>
      </tr>`;
    }).join('');
    clusterTable = `<table class='dtable'>
      <thead><tr><th class='l'>interval</th><th>events</th><th class='l'>top mod</th><th>of session</th></tr></thead>
      <tbody>${rows}</tbody></table>`;
  }

  setHTML(histRoot, histHtml);
  setHTML(clusterRoot, clusterTable);
}
";
}
