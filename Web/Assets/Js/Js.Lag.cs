#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Lag tab renderer, composed from the shared component library:
    // statGrid/statTile for the KPI strip, heatmapMatrix (2D) / barChart
    // (single-context fallback) for cause x context, .dtable for the sortable
    // cluster + density tables, sectionBlock + statLine + chip for the cluster
    // detail strip, lineChart for the GC heap band, splitBar/splitLegend/
    // statGrid for the causality chain cards, barChart + .dtable for rhythm.
    // No bespoke markup; every endpoint field is still rendered. Tooltips use
    // the native title= attribute via escapeHtml. Descriptive copy only.
    private const string JsLag = @"
// ====== LAG TAB =======================================================
let lastLagClusters = null, lastGcPressure = null, lastSegmentLagDensity = null,
    lastAllocCausality = null, lastLagRhythm = null;

// Selection + sort state for the reshaped surfaces.
let lagClustersSelected = null;        // selected fingerprintId in the cluster table
let lagClusterSort = { key: 'eventCount', dir: -1 };
let lagDensitySort = { key: 'vsBaseline', dir: -1 };

// A field counts as real context only if it is a non-empty string that is NOT a
// placeholder sentinel ('—', '-', 'none', 'n/a') — the data ships those for 'no
// context'. Declared once here so the heatmap degeneration guard and the cluster
// table's chip/column logic agree on what counts as context (a weaker case-
// sensitive copy in the heatmap previously let an all-'—' column survive).
function lagRealCtx(s) {
  if (typeof s !== 'string') return false;
  const t = s.trim().toLowerCase();
  return t !== '' && t !== '—' && t !== '-' && t !== 'none' && t !== 'n/a';
}

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

// Sortable-header helper: emits a <th> that toggles a {key,dir} sort state
// and re-runs the given render fn. Matches the shared .dtable th contract
// (.l for left-align, .sortable, .sorted when active).
function lagSortTh(state, key, label, leftAlign, onSort) {
  const active = state.key === key;
  const arrow = active ? (state.dir < 0 ? ' ▾' : ' ▴') : '';
  const cls = (leftAlign ? 'l ' : '') + 'sortable' + (active ? ' sorted' : '');
  return `<th class='${cls}' onclick='${onSort}(""${escapeHtml(key)}"")'>${escapeHtml(label)}${arrow}</th>`;
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

// Dominant-mod inline cell shared by the cluster + rhythm-cluster tables: a
// modColor-tinted truncated name + a muted share%, optionally trailed by a thin
// split bar of share-vs-rest. Collapses the three hand-rolled copies of this
// idiom onto one site (kept Lag-local — these are its only consumers).
function lagTopModCell(modId, modName, share, withBar) {
  const colour = modColor(modId || 0);
  const s = Math.max(0, Math.min(1, share || 0));
  const sharePct = (s * 100).toFixed(0) + '%';
  const name = modName || '—';
  let cell = `<span class='nm' style='color:${colour}'>${escapeHtml(truncate(name, 16))}</span> <span class='muted'>${sharePct}</span>`;
  if (withBar) {
    cell += splitBar([
      { frac: s, color: colour, label: name, value: sharePct },
      { frac: 1 - s, color: 'var(--surface-2)' },
    ], { thin: true });
  }
  return cell;
}

// ---------------------------------------------------------------- KPI strip
// Six tiles. GAME SPEED + SLOW TIME lead (X2, 2026-07-07 honesty pass): the
// event tiles count VARIANCE (spikes, stalls), and a game running uniformly
// at 2× budget produces zero events while sitting at 50% speed — the level
// signal has to headline or the whole tab reads all-clear during the exact
// state it exists to expose. The four original tiles keep their derivations.
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

  const k = (lastNow && lastNow.kpi) || {};
  const speed = k.realtimeSpeed || 0;
  const speedKnown = speed > 0;
  const speedPct = speedKnown ? Math.round(speed * 100) + '%' : '—';
  const slowTime = k.timeBelowThresholdMs > 0 ? fmtDuration(k.timeBelowThresholdMs) : (speedKnown ? 'none' : '—');
  const deficit = k.deficitMsPerSecond > 0 ? ' · losing ' + fmtMs(k.deficitMsPerSecond) + ' ms/s' : '';

  root.innerHTML = statGrid([
    statTile({ k: 'game speed', v: speedPct + (speedKnown && speed < 0.9 ? ' · SLOW-MO' : '') }),
    statTile({ k: 'time below 90%', v: slowTime + deficit }),
    statTile({ k: 'variance events', v: fmtInt(events) }),
    statTile({ k: 'session lag', v: fmtMs(sessionLagMs) + ' ms' }),
    statTile({ k: 'worst event p95', v: fmtMs(worstMs) + ' ms' }),
    statTile({ k: 'load factor', v: loadFactor > 0 ? loadFactor.toFixed(2) + '×' : '—' }),
  ], { cols: 'repeat(6, 1fr)' });
}

// ---------------------------------------------------------------- Heatmap
// cause x context. 2D case -> heatmapMatrix() (rows = causeClass, cols =
// context, cell = eventCount, full breakdown in the native title=). Single-
// context degeneration guard (0 or 1 distinct meaningful context, a '-' /
// empty placeholder dropped) -> a ranked barChart({horizontal:true}), one
// bar per cause. The conditional is kept; the genuine multi-context case
// still gets the real grid.
// Rewrite the outer panel title/sub so the frame never oversells the body:
// the single-context body is a ranked bar list, so the frame says 'lag events
// by cause'; only a real 2D grid earns the 'cause × context' framing.
function setLagHeatmapTitle(title, sub) {
  const t = document.getElementById('lag-heatmap-title');
  const s = document.getElementById('lag-heatmap-sub');
  if (t) t.textContent = title;
  if (s) s.textContent = sub;
}

function renderLagHeatmap() {
  const root = document.getElementById('lag-heatmap-body');
  if (!root) return;
  const cells = (lastLagClusters && lastLagClusters.causeContext) || [];

  // Signature gate: the title + body are fully derived from `cells`, so an
  // unchanged cell set re-derives nothing and rebuilds neither (heatmapMatrix /
  // barChart re-parse the whole SVG/bar subtree otherwise). Keyed on cell count
  // + each (cause, context, eventCount) so any real change still rebuilds.
  const sig = cells.length + '|' +
    cells.map(c => (c.causeClass || '') + ':' + (c.context || '') + ':' + (c.eventCount || 0)).join(',');
  if (_renderSig['lagHeatmap'] === sig) return;
  _renderSig['lagHeatmap'] = sig;

  if (cells.length === 0) {
    setLagHeatmapTitle('lag events by cause', 'ranked by event count');
    root.innerHTML = emptyState('no events observed');
    return;
  }

  // Build ordered cause (row) + context (col) axes preserving first-seen order.
  const causes = [], contexts = [];
  const causeSet = new Set(), ctxSet = new Set();
  for (const c of cells) {
    if (!causeSet.has(c.causeClass)) { causeSet.add(c.causeClass); causes.push(c.causeClass); }
    if (!ctxSet.has(c.context))      { ctxSet.add(c.context);      contexts.push(c.context); }
  }

  // Distinct meaningful contexts: drop empty / placeholder sentinels via the
  // shared lagRealCtx() guard. If 0 or 1 remain the grid degenerates to one
  // column, so render a ranked bar list. (lagRealCtx trims + lowercases and
  // rejects '—'/'none'/'n/a' too, so an all-placeholder set no longer survives
  // here as a junk single-column grid — the cluster table already used it.)
  const realContexts = contexts.filter(lagRealCtx);
  if (realContexts.length <= 1) {
    const agg = new Map();
    for (const c of cells) {
      const a = agg.get(c.causeClass) || { eventCount: 0, totalMs: 0 };
      a.eventCount += c.eventCount || 0;
      a.totalMs += c.totalMs || 0;
      agg.set(c.causeClass, a);
    }
    const rows = causes.map(cause => ({ cause, ...(agg.get(cause) || { eventCount: 0, totalMs: 0 }) }))
                       .sort((a, b) => b.eventCount - a.eventCount);
    // Only one context exists, so the cross-tab degenerates to a single column.
    // Label the panel for what it actually shows — lag events ranked by cause —
    // rather than presenting a 'single context · —' degenerate matrix frame. The
    // sole context (when one is named) goes in the panel sub, and the body is a
    // plain ranked bar list with no redundant inner heading above it.
    const ctxSub = realContexts.length === 1
      ? 'ranked by event count · ' + realContexts[0]
      : 'ranked by event count · one context this session';
    setLagHeatmapTitle('lag events by cause', ctxSub);
    const bars = rows.map(r => ({
      value: r.eventCount,
      label: humanizeLabel(r.cause) || '—',
      valueLabel: fmtInt(r.eventCount),
      color: 'var(--accent)',
    }));
    root.innerHTML = barChart({ horizontal: true, bars });
    return;
  }

  // A real multi-context grid is rendered, so the frame honestly earns the
  // 'cause × context' framing the single-context body could not.
  setLagHeatmapTitle('cause × context', 'events by cause and surrounding context');

  // 2D grid via heatmapMatrix: look the cell up by (cause, context).
  const key = (a, b) => a + '\x1f' + b;
  const map = new Map();
  let maxCount = 0;
  for (const c of cells) {
    map.set(key(c.causeClass, c.context), c);
    if ((c.eventCount || 0) > maxCount) maxCount = c.eventCount;
  }
  if (maxCount <= 0) maxCount = 1;

  root.innerHTML = heatmapMatrix({
    rows: causes,
    cols: realContexts,
    max: maxCount,
    fmt: fmtInt,
    cellAt: (r, c) => {
      const cause = causes[r], ctx = realContexts[c];
      const cell = map.get(key(cause, ctx));
      const count = cell ? (cell.eventCount || 0) : 0;
      const ms = cell ? (cell.totalMs || 0) : 0;
      const tip = count === 0
        ? cause + ' / ' + ctx + ' · 0 events'
        : cause + ' / ' + ctx + ' · ' + fmtInt(count) + ' events · ' + fmtMs(ms) + ' ms total';
      return { value: count, tip };
    },
  });
}

// ---------------------------------------------------------------- Cluster table
// Shared sortable .dtable. Columns: cause (leading, raw fingerprintId in the
// title= tooltip), context chips, events, p95 (perf-tinted vs the median p95),
// total ms, top mod + share (inline split bar coloured by modColor). Click a
// row to select it (tr.sel) and fill the detail strip below (a sectionBlock
// with a cause .chip + statLines). Selection keyed on fingerprintId.
//
// Scroll-stability: the table is written into the STABLE scroll container
// #lag-clusters-body via setHTML(), preserving scrollTop across the 3s poll. A
// renderIfChanged() signature gate keyed on the cluster set + sort + selection
// skips the table rebuild on a no-change poll. The detail strip is a separate
// static node replaced outside that container.
function lagClustersSort(key) {
  if (lagClusterSort.key === key) lagClusterSort.dir *= -1;
  else lagClusterSort = { key, dir: -1 };
  renderLagClusters();
}

function renderLagClusters() {
  const root = document.getElementById('lag-clusters-body');
  const detailRoot = document.getElementById('lag-clusters-detail');
  if (!root) return;
  const clusters = (lastLagClusters && lastLagClusters.clusters) || [];

  if (clusters.length === 0) {
    renderIfChanged('lagClusters', 'empty', () => {
      setHTML(root, emptyState('no clusters yet'));
      if (detailRoot) detailRoot.innerHTML = '';
    });
    return;
  }

  // Signature gate: cluster count + each row's (cause, events, p95) + sort +
  // selection. A no-change poll re-derives nothing and rebuilds no DOM.
  const sig = clusters.length + '|' + lagClusterSort.key + lagClusterSort.dir + '|' + (lagClustersSelected || '') +
    '|' + clusters.map(c => (c.fingerprintId || '') + ':' + (c.eventCount || 0) + ':' + (c.p95Ms || 0)).join(',');
  if (_renderSig['lagClusters'] === sig) return;
  _renderSig['lagClusters'] = sig;

  const sorted = lagApplySort(clusters, lagClusterSort);
  // perf tint for p95 is a ratio against the median p95 across clusters (1 =
  // typical), robust to a single dominant outlier.
  const p95s = clusters.map(c => c.p95Ms || 0).sort((a, b) => a - b);
  const medianP95 = p95s.length > 0 ? p95s[Math.floor(p95s.length / 2)] : 0;

  // The context column is dead width when no cluster carries a real context, so
  // it is dropped from both the header and the rows in that case. The shared
  // lagRealCtx() (declared at the top of the fragment) decides both what counts
  // as a column and whether a chip is drawn below, so the heatmap guard, this
  // column, and the chips all agree on what context is.
  const hasCtx = c => lagRealCtx(c.primaryBiome) || lagRealCtx(c.weatherFlags) || c.hardmode === true;
  const anyContext = clusters.filter(hasCtx).length >= 1;
  const ctxTh = anyContext ? `<th class='l'>context</th>` : '';

  const head = `<tr>
    ${lagSortTh(lagClusterSort, 'causeClass', 'cause', true, 'lagClustersSort')}
    ${ctxTh}
    ${lagSortTh(lagClusterSort, 'eventCount', 'events', false, 'lagClustersSort')}
    ${lagSortTh(lagClusterSort, 'p95Ms', 'p95', false, 'lagClustersSort')}
    ${lagSortTh(lagClusterSort, 'totalMs', 'total', false, 'lagClustersSort')}
    ${lagSortTh(lagClusterSort, 'topModShare', 'top mod', true, 'lagClustersSort')}
  </tr>`;

  const body = sorted.map(c => {
    const sel = c.fingerprintId === lagClustersSelected ? ' sel' : '';
    let ctxCell = '';
    if (anyContext) {
      let ctxChips = '';
      if (lagRealCtx(c.primaryBiome)) ctxChips += `<span class='chip'>${escapeHtml(c.primaryBiome)}</span>`;
      if (lagRealCtx(c.weatherFlags)) ctxChips += `<span class='chip cool'>${escapeHtml(c.weatherFlags)}</span>`;
      if (c.hardmode) ctxChips += `<span class='chip warn'>hardmode</span>`;
      if (!ctxChips) ctxChips = `<span class='muted'>—</span>`;
      ctxCell = `<td class='l'>${ctxChips}</td>`;
    }

    const tint = tintClass(medianP95 > 0 ? (c.p95Ms || 0) / medianP95 : 0);
    // Inline split bar: top mod's share vs the rest, coloured by modColor.
    const modCell = lagTopModCell(c.topModId, c.topModName, c.topModShare, true);

    const fpId = c.fingerprintId || '';
    const causeTip = fpId ? (c.causeClass || '—') + ' · id ' + fpId : (c.causeClass || '—');
    return `<tr class='clickable${sel}' onclick='lagClustersPick(""${escapeHtml(fpId)}"")'>
      <td class='l' title='${escapeHtml(causeTip)}'>${escapeHtml(c.causeClass || '—')}</td>
      ${ctxCell}
      <td>${fmtInt(c.eventCount)}</td>
      <td class='${tint}'>${fmtMs(c.p95Ms)}</td>
      <td>${fmtMs(c.totalMs)}</td>
      <td class='l'>${modCell}</td>
    </tr>`;
  }).join('');

  // Detail strip for the selected fingerprint — same per-fingerprint fields.
  let details;
  const pick = clusters.find(c => c.fingerprintId === lagClustersSelected);
  if (pick) {
    const subBits = [];
    if (lagRealCtx(pick.primaryBiome)) subBits.push(pick.primaryBiome);
    if (lagRealCtx(pick.weatherFlags)) subBits.push(pick.weatherFlags);
    if (pick.hardmode) subBits.push('hardmode');
    const ctxText = subBits.join(' · ') || 'no context';
    // Human-readable headline for the cluster: 'spike · CalamityMod · no context'
    // built from the cause class, the dominant mod, and the surrounding context.
    // The raw fingerprint key ('Spike|m0|—||h0') is kept only as a small dim tag
    // for debugging, never as the prominent label the player reads.
    const headline = [humanizeLabel(pick.causeClass) || '—', pick.topModName || '—', ctxText]
      .filter(Boolean).join(' · ');
    const causeChip = `<span class='chip cool'>${escapeHtml(headline)}</span>`;
    const fpLine = pick.fingerprintId
      ? `<span class='muted' style='font-size:0.7rem' title='internal fingerprint key'>${escapeHtml(pick.fingerprintId)}</span>` : '';
    const detailBody =
      `<div style='display:flex;gap:0.5em;align-items:center;flex-wrap:wrap;margin-bottom:0.4rem'>${causeChip}${fpLine}</div>` +
      statLine('context', ctxText) +
      statLine('events', fmtInt(pick.eventCount)) +
      statLine('p95', fmtMs(pick.p95Ms) + ' ms') +
      statLine('total', fmtMs(pick.totalMs) + ' ms') +
      statLine('top mod', (pick.topModName || '—') + ' · ' + ((pick.topModShare || 0) * 100).toFixed(0) + '%');
    details = sectionBlock('selected cluster', detailBody);
  } else {
    details = emptyState('click a row to inspect a fingerprint cluster');
  }

  setHTML(root, dtable(head, body));
  if (detailRoot) detailRoot.innerHTML = details;
}
function lagClustersPick(fp) {
  if (!fp) return;
  lagClustersSelected = (lagClustersSelected === fp) ? null : fp;
  renderLagClusters();
}

// ---------------------------------------------------------------- Density table
// Shared sortable .dtable. Columns: segment, events (raw count), density (a
// split bar whose TOTAL length scales to the row's events/min relative to the
// busiest segment, with the spike=var(--spike) / stall=var(--stall) split
// living inside that length), vsBaseline x (perf-tinted via tintClass). The
// session baseline goes in the panel sub-header; a splitLegend keys the colours.
//
// The bar length encodes magnitude (which segment is worst reads at a glance);
// the spike/stall split inside it encodes the mix. events/min was previously a
// numeric column too, but it is the un-normalised twin of vs base, so it now
// lives only as the bar length + the caption rather than as a second number.
function lagDensitySortBy(key) {
  if (lagDensitySort.key === key) lagDensitySort.dir *= -1;
  else lagDensitySort = { key, dir: -1 };
  renderLagDensity();
}

function renderLagDensity() {
  const root = document.getElementById('lag-density-body');
  const subRoot = document.getElementById('lag-density-sub');
  const legendRoot = document.getElementById('lag-density-legend');
  if (!root) return;
  const snap = lastSegmentLagDensity;
  const entries = (snap && snap.entries) || [];

  if (entries.length === 0) {
    setHTML(root, emptyState('no segments closed yet'));
    if (subRoot) subRoot.textContent = '—';
    if (legendRoot) legendRoot.innerHTML = '';
    _renderSig['lagDensity'] = 'empty';   // so a later identical non-empty set still rebuilds
    return;
  }

  const baseline = (snap && snap.sessionBaselinePerMin) || 0;
  // The sub-header + legend are cheap static siblings of the gated table, so they
  // stay outside the signature gate (the legend content is constant; the sub is a
  // single textContent write). Only the table body — the heavy rebuild — is gated.
  if (subRoot) subRoot.textContent = 'session baseline · ' + baseline.toFixed(2) + ' events/min';
  if (legendRoot) legendRoot.innerHTML = splitLegend([
    { color: 'var(--spike)', label: 'spike' },
    { color: 'var(--stall)', label: 'stall' },
  ]);

  // Signature gate: entry count + each row's (name, events, eventsPerMin) + sort.
  // A no-change poll skips the sort, the body map, and the table rebuild.
  const sig = entries.length + '|' + lagDensitySort.key + lagDensitySort.dir + '|' +
    entries.map(e => (e.name || '') + ':' + (e.eventCount || 0) + ':' + (e.eventsPerMin || 0)).join(',');
  if (_renderSig['lagDensity'] === sig) return;
  _renderSig['lagDensity'] = sig;

  const sorted = lagApplySort(entries, lagDensitySort);
  // Busiest segment sets the bar's full extent so the lengths read comparably.
  let maxPerMin = 0;
  for (const e of entries) { if ((e.eventsPerMin || 0) > maxPerMin) maxPerMin = e.eventsPerMin; }
  if (maxPerMin <= 0) maxPerMin = 1;

  const head = `<tr>
    ${lagSortTh(lagDensitySort, 'name', 'segment', true, 'lagDensitySortBy')}
    ${lagSortTh(lagDensitySort, 'eventCount', 'events', false, 'lagDensitySortBy')}
    ${lagSortTh(lagDensitySort, 'eventsPerMin', 'density', true, 'lagDensitySortBy')}
    ${lagSortTh(lagDensitySort, 'vsBaseline', 'vs base', false, 'lagDensitySortBy')}
  </tr>`;

  const body = sorted.map(e => {
    const spikes = e.spikeCount || 0;
    const stalls = e.stallCount || 0;
    const total = spikes + stalls;
    const events = e.eventCount != null ? e.eventCount : total;
    // Total bar length = this row's events/min as a fraction of the busiest
    // segment; the spike/stall split divides THAT length, so the bar answers
    // both 'which segment is worst' (length) and 'what kind' (split) at once.
    const lenFrac = Math.max(0, Math.min(1, (e.eventsPerMin || 0) / maxPerMin));
    const spikeFrac = total > 0 ? lenFrac * (spikes / total) : 0;
    const stallFrac = total > 0 ? lenFrac * (stalls / total) : 0;
    const bar = splitBar([
      { frac: spikeFrac, color: 'var(--spike)', label: 'spikes', value: fmtInt(spikes) },
      { frac: stallFrac, color: 'var(--stall)', label: 'stalls', value: fmtInt(stalls) },
    ], { thin: true });
    const perMin = (e.eventsPerMin || 0).toFixed(1);
    const tip = (e.name || '—') + ' · ' + perMin + ' events/min · ' + fmtInt(spikes) + ' spikes · ' + fmtInt(stalls) + ' stalls';
    const densityCell = `<div class='lag-density-cell' title='${escapeHtml(tip)}'>${bar}<span class='cap'><span style='color:var(--spike)'>${fmtInt(spikes)}</span> / <span style='color:var(--stall)'>${fmtInt(stalls)}</span> · ${perMin}/min</span></div>`;
    const vsTint = tintClass(e.vsBaseline);
    const vsTxt = (isFinite(e.vsBaseline) && e.vsBaseline > 0) ? e.vsBaseline.toFixed(2) + '×' : '—';
    return `<tr>
      <td class='l' title='${escapeHtml((e.family || '') + ' · ' + fmtDuration(e.durationMs))}'>${escapeHtml(e.name || '—')}</td>
      <td>${fmtInt(events)}</td>
      <td class='l'>${densityCell}</td>
      <td class='${vsTint}'>${vsTxt}</td>
    </tr>`;
  }).join('');

  setHTML(root, dtable(head, body));
}

// ---------------------------------------------------------------- GC pressure
// A managed-heap line/area chart via lineChart() (auto-scaled to the heap's
// own band, GB-formatted axis), the peak sample marked with a rule, beside a
// statLine block of every GC counter. Respects worldLoaded.
function renderLagGcPressure() {
  const root = document.getElementById('lag-gc-body');
  if (!root) return;
  const gc = lastGcPressure;
  if (!gc || !gc.worldLoaded) {
    root.innerHTML = emptyState('no gc data yet');
    return;
  }

  const series = gc.heapMbSeries || [];
  const n = series.length;
  let maxHeap = 0, peakI = 0;
  for (let i = 0; i < n; i++) { if (series[i] > maxHeap) { maxHeap = series[i]; peakI = i; } }
  if (maxHeap <= 0) maxHeap = 1;

  const gb = v => (v / 1024).toFixed(2) + ' GB';
  // Peak rule (horizontal) labels the peak heap value on the auto-scaled axis;
  // a marker (vertical) pins the sample index where it occurred.
  // padTop + a wider niceScale headroom (pad) push the peak rule down off the
  // plot's top edge so its 'N GB peak' label reads as an annotation on the line
  // rather than colliding with the top-right corner of the frame. The rule label
  // is right-anchored at (w - padX), so the wider padX ends the 'N GB peak' text
  // a comfortable gutter in from the right boundary instead of hugging it.
  const chart = n > 0
    ? lineChart({
        series: [{ values: series, color: 'var(--gc)', area: true }],
        h: 138, padTop: 22, padX: 28, pad: 0.2, axis: true, fmt: gb,
        rules: [{ value: maxHeap, color: 'var(--gc)', label: gb(maxHeap) + ' peak' }],
        markers: n > 1 ? [{ index: peakI, color: 'var(--gc)' }] : [],
      })
    : emptyState('no heap samples yet');

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
  const statBlock = stats.map(([k, v]) => statLine(k, v)).join('');

  root.innerHTML = `<div class='lag-gc-body'>
      <div>${statBlock}</div>
      <div>${chart}</div>
    </div>`;
}

// ---------------------------------------------------------------- Causality cards
// One sectionBlock per chain: a split bar of topContributors[] (frac =
// bytesInWindow / totalBytesInWindow, colour = modColor, label = modName,
// value = bytes + share%), a splitLegend, and a statGrid of the chain stats
// (gcKind, pauseMs, bytes in window, freed bytes, windowMs). Keeps the empty
// state. setHTML() preserves scrollTop across the 3s poll.
function renderLagCausality() {
  const root = document.getElementById('lag-causality-body');
  if (!root) return;
  const chains = (lastAllocCausality && lastAllocCausality.chains) || [];

  if (chains.length === 0) {
    setHTML(root, emptyState('no gc stalls observed'));
    return;
  }

  const cards = chains.map((ch, idx) => {
    const contribs = ch.topContributors || [];
    const totalIn = ch.totalBytesInWindow || contribs.reduce((s, c) => s + (c.bytesInWindow || 0), 0) || 1;

    const segs = contribs.map(c => {
      const frac = (c.bytesInWindow || 0) / totalIn;
      const colour = modColor(c.modId);
      const value = fmtBytes(c.bytesInWindow) + ' · ' + (c.sharePct || 0).toFixed(1) + '%';
      return { frac, color: colour, label: c.modName || '—', value };
    });

    const bar = segs.length > 0 ? splitBar(segs, { tall: true }) : emptyState('no contributors recorded');
    const legend = segs.length > 0 ? splitLegend(segs) : '';

    const statBody = statGrid([
      statTile({ k: 'gc kind', v: ch.gcKind || '—' }),
      statTile({ k: 'pause', v: fmtMs(ch.pauseMs) + ' ms' }),
      statTile({ k: 'bytes in window', v: fmtBytes(ch.totalBytesInWindow) }),
      statTile({ k: 'freed', v: fmtBytes(ch.freedBytes) }),
      statTile({ k: 'window', v: ch.windowMs != null ? (ch.windowMs / 1000).toFixed(1) + ' s' : '—' }),
    ]);

    return sectionBlock('gc stall ' + (idx + 1), bar + legend + statBody, ch.gcKind || '');
  }).join('');

  setHTML(root, cards);
}

// ---------------------------------------------------------------- Rhythm
// Two clearly-headed sub-sections side by side: a coarse interval histogram
// (the raw ~60 micro-buckets are re-binned into a handful of legible, taller
// buckets, the modal one highlighted) and a .dtable of rhythm clusters (centre
// ± width, events, top mod + share, share of session). Each half carries its
// own sectionHeader so the two row systems read as distinct, not one confused
// table, and the coarser histogram closes the height mismatch with the table.

// Re-bin the fine interval histogram into at most `target` equal-width buckets
// spanning the observed interval range, summing counts. Returns
// [{lo, hi, count}] ordered by interval. Fewer than `target` distinct intervals
// pass through unchanged (one bucket each).
function lagRebinHist(hist, target) {
  const pts = hist.filter(b => b && isFinite(b.intervalSeconds));
  if (pts.length === 0) return [];
  let lo = Infinity, hi = -Infinity;
  for (const b of pts) { if (b.intervalSeconds < lo) lo = b.intervalSeconds; if (b.intervalSeconds > hi) hi = b.intervalSeconds; }
  if (pts.length <= target || hi <= lo) {
    return pts.slice().sort((a, b) => a.intervalSeconds - b.intervalSeconds)
              .map(b => ({ lo: b.intervalSeconds, hi: b.intervalSeconds, count: b.count || 0 }));
  }
  const span = hi - lo, n = Math.max(1, target);
  const bins = [];
  for (let i = 0; i < n; i++) bins.push({ lo: lo + span * i / n, hi: lo + span * (i + 1) / n, count: 0 });
  for (const b of pts) {
    let idx = Math.floor((b.intervalSeconds - lo) / span * n);
    if (idx < 0) idx = 0; if (idx >= n) idx = n - 1;
    bins[idx].count += b.count || 0;
  }
  return bins.filter(b => b.count > 0);
}

function renderLagRhythm() {
  const histRoot = document.getElementById('lag-rhythm-hist');
  const clusterRoot = document.getElementById('lag-rhythm-clusters');
  if (!histRoot || !clusterRoot) return;
  const snap = lastLagRhythm;
  const hist = (snap && snap.histogram) || [];
  const clusters = (snap && snap.clusters) || [];

  if (hist.length === 0 && clusters.length === 0) {
    renderIfChanged('lagRhythm', 'empty', () => {
      histRoot.innerHTML = emptyState('not enough events for periodicity');
      setHTML(clusterRoot, '');
    });
    return;
  }

  // Signature gate: both sub-panels are derived from hist + clusters, so an
  // unchanged snapshot rebuilds neither (the re-bin + barChart + cluster table
  // are the heavy work). Keyed on hist length + total count and the per-cluster
  // (centre, events) so any real change still rebuilds.
  const histCount = hist.reduce((s, b) => s + ((b && b.count) || 0), 0);
  const sig = hist.length + ':' + histCount + '|' +
    clusters.map(c => c.centreSeconds + ':' + (c.eventCount || 0) + ':' + (c.topModId || 0)).join(',');
  if (_renderSig['lagRhythm'] === sig) return;
  _renderSig['lagRhythm'] = sig;

  // Coarse, legible histogram: re-bin to ~12 buckets, highlight the modal one.
  const bins = lagRebinHist(hist, 12);
  let modalI = -1, modalC = -1;
  for (let i = 0; i < bins.length; i++) { if (bins[i].count > modalC) { modalC = bins[i].count; modalI = i; } }
  const histInner = bins.length === 0
    ? emptyState('no interval histogram yet')
    : barChart({
        horizontal: true,
        bars: bins.map((b, i) => {
          const label = (b.hi > b.lo)
            ? b.lo.toFixed(2) + '–' + b.hi.toFixed(2) + 's'
            : b.lo.toFixed(2) + 's';
          return {
            value: b.count || 0,
            label,
            valueLabel: fmtInt(b.count),
            // The modal bucket is the periodicity the panel is about, so it reads
            // on the bright accent; the rest sit on the dimmer muted tone. Both
            // are monochrome — magnitude/emphasis, not category.
            color: i === modalI ? 'var(--accent)' : 'var(--muted)',
          };
        }),
      });
  const histHtml = sectionHeader('interval distribution', 'time between repeated events') + histInner;

  // Cluster table.
  let clusterInner;
  if (clusters.length === 0) {
    clusterInner = emptyState('no rhythm clusters detected');
  } else {
    const rows = clusters.map(c => {
      const share = Math.max(0, Math.min(1, c.shareOfSession || 0));
      return `<tr>
        <td class='l'>${c.centreSeconds.toFixed(2)}s ± ${c.widthSeconds.toFixed(2)}s</td>
        <td>${fmtInt(c.eventCount)}</td>
        <td class='l'>${lagTopModCell(c.topModId, c.topModName, c.topModShare, false)}</td>
        <td>${(share * 100).toFixed(1)}%</td>
      </tr>`;
    }).join('');
    clusterInner = dtable(`<tr><th class='l'>interval</th><th>events</th><th class='l'>top mod</th><th>of session</th></tr>`, rows);
  }
  const clusterHtml = sectionHeader('rhythm clusters', 'periodic signatures') + clusterInner;

  histRoot.innerHTML = histHtml;
  setHTML(clusterRoot, clusterHtml);
}
";
}
