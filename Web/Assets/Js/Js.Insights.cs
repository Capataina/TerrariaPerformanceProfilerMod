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
// ====== INSIGHTS TAB ==================================================
// The per-mod observatory, composed entirely from the shared component
// library: gauge() ring KPIs, panel()/scrollRegion() framing, row()/rowList()
// for the master card list, statLine()/splitLegend()/.dtable for the detail
// pane, and .dtable + cellBar()/splitBar()/chips for the lower analytical
// surfaces. No bespoke per-surface markup.
//
// Poll-stable scroll: each scrollable surface is built once with a stable
// scroll-region container; polls re-render only the container's contents via
// setHTML(), so scroll position survives a 3s poll instead of snapping to top.
//
// Invariant 3: every string is descriptive. Measurements only ('costs X',
// 'used N of M', 'r = ...'); never 'should remove' / 'junk' / a verdict.
let lastModObservatory = null;
let lastDormant = null;
let lastCrossCutting = null;
let lastEngagementCost = null;
let lastModInteraction = null;

let selectedObservatoryModId = -1;

// Sort state for the sortable tables. Each is {key, dir} where dir is
// 1 (ascending) or -1 (descending). Defaults match the most useful
// at-a-glance ordering for each surface.
let dormantSort = { key: 'usageRatio', dir: 1 };       // least-engaged first
let engagementSort = { key: 'cpuShare', dir: -1 };     // costliest first

// Roster composition categories: [field, label, colour]. Shared by the
// observatory list split bar, its legend, and the detail pane so colour ==
// meaning across all three.
//
// These nine are a fixed content-TYPE role set (like the Memory tab's four
// footprint roles), not the chromatic per-mod series, so they ride a fixed
// monochrome luminance ramp rather than nine categorical hues. The prior hue
// set ran out of distinct colours at nine and collided: 'accessories' on
// --accent (oklch 0.922) and 'bosses' on --text-bright (oklch 1.0) both
// rendered near-white, and a near-white DATA swatch breaks the monochrome
// chrome (white is the brightest thing on the dark surface, reserved for
// chrome, never data). The ramp fixes both: every step is a distinct grey, no
// two collide, and grey is orthogonal to the per-mod hues so a category shade
// never clashes with a mod's colour. Order is brightest->dimmest, fixed, so
// the legend keys the column once for every row. Span 0.92->0.40 over nine
// steps (~0.065 L apart) stays perceptibly separable in OKLCH's uniform L, and
// the 0.40 floor matches the Memory footprint ramp's dimmest step so even the
// last category clears the --surface split-bar track (oklch 0.235).
// The ramp top is capped at 0.84 (not near-white): white is the brightest thing
// in the monochrome chrome and reads as a highlight, so a DATA swatch must stay
// clearly below it. The 0.84 -> 0.42 span over nine steps keeps each category a
// distinct grey while the brightest no longer competes with the chrome accent.
const ROSTER_CATS = [
  ['items',       'items',       'oklch(0.840 0 0)'],
  ['npcs',        'npcs',        'oklch(0.788 0 0)'],
  ['buffs',       'buffs',       'oklch(0.735 0 0)'],
  ['projectiles', 'projectiles', 'oklch(0.683 0 0)'],
  ['mounts',      'mounts',      'oklch(0.630 0 0)'],
  ['accessories', 'accessories', 'oklch(0.578 0 0)'],
  ['biomes',      'biomes',      'oklch(0.525 0 0)'],
  ['invasions',   'invasions',   'oklch(0.473 0 0)'],
  ['bosses',      'bosses',      'oklch(0.420 0 0)'],
];

async function pollInsightsData() {
  if (activeTab !== 'insights') return;
  try {
    const [mo, dr, cc, ec, mi] = await Promise.all([
      fetch('/api/mod-observatory', { cache: 'no-store' }).then(r => r.json()),
      fetch('/api/dormant',         { cache: 'no-store' }).then(r => r.json()),
      fetch('/api/cross-cutting',   { cache: 'no-store' }).then(r => r.json()),
      fetch('/api/engagement-cost', { cache: 'no-store' }).then(r => r.json()),
      fetch('/api/mod-interaction', { cache: 'no-store' }).then(r => r.json()),
    ]);
    lastModObservatory = mo;
    lastDormant = dr;
    lastCrossCutting = cc;
    lastEngagementCost = ec;
    lastModInteraction = mi;
    renderInsights();
  } catch (e) { /* swallow — next tick will retry */ }
}
setInterval(pollInsightsData, 3000);

// Observatory list controls (persist across the 3s poll).
let obsSort = 'cpu', obsFilter = '';

function renderInsights() {
  renderInsightsKpiStrip();
  renderDormantSurface();
  renderObservatoryList();
  renderObservatoryDetail();
  renderCrossCutting();
  renderEngagementScatter();
  renderModInteractionMatrix();
}

// sortableHead() — the shared sortable-header builder — lives in Js.Components
// (loaded before this fragment); the dormant + observatory surfaces call it.

// ----- Modlist composition (waffle) ----------------------------------
// The loaded modlist as a unit grid: one cell per mod, coloured by engagement
// bucket (active >= 5% usage / under 5% / dormant at zero usage), so the
// composition reads as countable area. Replaces four separate ring gauges (one
// shape, one read) and is paired with a legend + the headline count. Descriptive
// only — the buckets are measured usage thresholds, not judgements.
function renderInsightsKpiStrip() {
  const root = document.getElementById('ins-kpi');
  if (!root) return;
  const obs = lastModObservatory || {};
  const dor = lastDormant || {};
  const loaded = dor.modsLoaded != null ? dor.modsLoaded
    : ((obs.activeCount || 0) + (obs.dormantCount || 0));
  const dormant = dor.modsWithZeroUsage != null ? dor.modsWithZeroUsage : (obs.dormantCount || 0);
  const below5 = dor.modsBelowFivePercentUsage != null ? dor.modsBelowFivePercentUsage : dormant;
  // Three disjoint buckets that sum to the loaded count.
  const partial = Math.max(0, below5 - dormant);            // nonzero but under 5%
  const active = Math.max(0, loaded - below5);              // >= 5% usage
  const denom = Math.max(1, loaded);

  const ACTIVE = 'var(--good-bar)', PARTIAL = 'var(--amber)', DORMANT = 'var(--muted)';
  const cells = [
    { count: active,  color: ACTIVE,  label: 'active (≥5% usage)' },
    { count: partial, color: PARTIAL, label: 'under 5% usage' },
    { count: dormant, color: DORMANT, label: 'dormant (zero usage)' },
  ];
  const grid = waffle({ cells, total: loaded, cols: Math.min(20, Math.max(8, Math.ceil(Math.sqrt(loaded)))) });
  const key = legend(cells.map(c => ({ color: c.color, label: c.label, value: fmtInt(c.count) })), { inline: true });
  const sub = `${fmtInt(loaded)} mods · ${(100 * active / denom).toFixed(0)}% active · ${(100 * dormant / denom).toFixed(0)}% dormant`;

  let body = root.querySelector('#kpi-waffle');
  if (!body) {
    root.innerHTML = panel({ title: 'modlist composition', sub, body: `<div id='kpi-waffle'></div>` });
    body = root.querySelector('#kpi-waffle');
  }
  const subEl = root.querySelector('.panel-sub');
  if (subEl) subEl.textContent = sub;
  setHTML(body, `<div class='wf-wrap'>${grid}</div><div class='wf-key'>${key}</div>`);
}

// ----- I2 dormant surface --------------------------------------------
// Sortable .dtable inside a panel. Each row is a mod with a usage split bar
// (engaged green vs unused surface) carrying the % text, a used/roster count,
// and the dominant unused category as a chip. The panel sub carries the two
// headline totals. Pure measurement — no judgement on any row. The scroll
// region is stable across polls so setHTML preserves its position; only the
// table inside it is rebuilt each tick.
function renderDormantSurface() {
  const root = document.getElementById('ins-dormant');
  if (!root) return;
  const dor = lastDormant;

  let scroll = root.querySelector('#dormant-scroll');
  if (!scroll) {
    root.innerHTML = panel({
      title: 'dormant content', sub: '—',
      body: scrollRegion('dormant-scroll', '', { maxH: '220px' }),
      pad: 'flush',
    });
    scroll = root.querySelector('#dormant-scroll');
  }
  const subEl = root.querySelector('.panel-sub');

  if (!dor || !dor.worldLoaded) {
    if (subEl) subEl.textContent = '—';
    renderIfChanged('insDormant', 'unloaded', () => setHTML(scroll, ''));
    return;
  }
  if (subEl) subEl.textContent = `${fmtInt(dor.modsWithZeroUsage)} mods at zero usage · ${fmtInt(dor.modsBelowFivePercentUsage)} under 5%`;

  const entries = (dor.entries || []).slice();
  if (entries.length === 0) {
    renderIfChanged('insDormant', 'empty', () => setHTML(scroll, emptyState('no dormant entries recorded this session')));
    return;
  }

  // Signature gate: entry set (mod + the three sortable figures) + sort state.
  // A no-change poll skips the sort, the sortableHead() rebind side effect, and
  // the table reparse. The sub-header above is a cheap sibling, left ungated.
  const insDormantSig = dormantSort.key + dormantSort.dir + '|' +
    entries.map(e => (e.modName || '') + ':' + (e.usageRatio || 0) + ':' + (e.usedCount || 0) + ':' + (e.rosterSize || 0)).join(',');
  if (_renderSig['insDormant'] === insDormantSig) return;
  _renderSig['insDormant'] = insDormantSig;

  const getters = {
    modName:    e => (e.modName || '').toLowerCase(),
    usageRatio: e => e.usageRatio,
    usedCount:  e => e.usedCount,
    rosterSize: e => e.rosterSize,
  };
  const g = getters[dormantSort.key] || getters.usageRatio;
  entries.sort((a, b) => {
    const va = g(a), vb = g(b);
    if (va < vb) return -1 * dormantSort.dir;
    if (va > vb) return  1 * dormantSort.dir;
    return 0;
  });

  const cols = [
    { key: 'modName',    label: 'mod',        l: true },
    { key: 'usageRatio', label: 'engagement', title: 'active-use intensity vs your most-used mod (held / worn / in-biome ticks)' },
    { key: 'usedCount',  label: 'active use', title: 'tick-credits of active use this session' },
    { key: 'rosterSize', label: 'roster',     title: 'count of content this mod registers' },
  ];
  // The dominant-unused-category column is presentational only (not sorted).
  const headRow = sortableHead(cols, dormantSort, renderDormantSurface, 'ins-dormant')
    .replace('</tr>', `<th class='l'>dominant unused</th></tr>`);

  const rows = entries.map(e => {
    // usageRatio is active-use intensity in [0,1] (relative to the most-used mod);
    // the bar reads engagement vs headroom, not a roster fraction.
    const ratio = Math.max(0, Math.min(1, e.usageRatio || 0));
    const pct = (ratio * 100).toFixed(1);
    const bar = splitBar([
      { frac: ratio,     color: 'var(--good)',    label: 'engaged',  value: pct + '%' },
      { frac: 1 - ratio, color: 'var(--surface)', label: 'headroom', value: '' },
    ], { thin: true });
    const cat = e.dominantUnusedCategory
      ? `<span class='chip'>${escapeHtml(e.dominantUnusedCategory)}</span>`
      : `<span class='dim'>—</span>`;
    return `<tr title='${escapeHtml(e.modName + ' — ' + pct + '% active-use intensity · ' + fmtInt(e.usedCount) + ' tick-credits · roster ' + fmtInt(e.rosterSize))}'>
      <td class='l'>${escapeHtml(e.modName)}</td>
      <td class='l'><div class='ins-usage'>${bar}<span class='ins-pct'>${pct}%</span></div></td>
      <td>${fmtInt(e.usedCount)}</td>
      <td>${fmtInt(e.rosterSize)}</td>
      <td class='l'>${cat}</td>
    </tr>`;
  }).join('');

  setHTML(scroll, dtable(headRow, rows));
}

// ----- I1 observatory list -------------------------------------------
// Ranked card list (row()/rowList()), ordered by cpu share. Each card carries
// the rank, name + usage micro-stats + a cpu cost cellBar + a roster
// composition split bar, and the smoothed ms. Click selects (row sel) and
// fills the detail pane. The panel.fill body + scroll-region grow to fill the
// observatory column; scroll survives the poll via setHTML.
function renderObservatoryList() {
  const root = document.getElementById('ins-obs-list');
  if (!root) return;

  let scroll = root.querySelector('#obs-scroll');
  if (!scroll) {
    // Build the shell once (so the search box keeps focus + value across polls).
    // The scroll region is bound to ~10 rows; a search box + sort control live
    // in the header. Header controls are wired once, here.
    root.innerHTML = panel({
      title: 'per-mod observatory',
      actions: `<input class='filter-input' id='obs-search' placeholder='search mods…' style='width:9rem' value='${escapeHtml(obsFilter)}'>` +
        segmented({ id: 'obs-sort', attr: 'data-osort', active: obsSort, options: [
          { value: 'cpu', label: 'cpu' }, { value: 'usage', label: 'usage' }, { value: 'name', label: 'name' }] }),
      body: scrollRegion('obs-scroll', '', { maxH: '32rem' }),
      pad: 'flush',
    });
    scroll = root.querySelector('#obs-scroll');
    const search = root.querySelector('#obs-search');
    if (search) search.addEventListener('input', () => { obsFilter = search.value; renderObservatoryList(); });
    const sortCtl = root.querySelector('#obs-sort');
    if (sortCtl) sortCtl.addEventListener('click', e => {
      const b = e.target.closest('[data-osort]'); if (!b) return;
      obsSort = b.dataset.osort;
      sortCtl.querySelectorAll('button').forEach(x => x.classList.toggle('active', x.dataset.osort === obsSort));
      renderObservatoryList();
    });
  }
  const obs = lastModObservatory;
  if (!obs || !obs.worldLoaded || !obs.cards || obs.cards.length === 0) {
    renderIfChanged('insObsList', 'empty', () => setHTML(scroll, emptyState('no per-mod observatory data yet')));
    return;
  }
  // Cost bars stay comparable against the whole roster (max cpu across all cards,
  // not just the filtered/sorted subset).
  const maxCpu = Math.max(0.0001, ...obs.cards.map(c => c.cpuSharePct));
  let cards = obs.cards.slice();
  if (obsFilter) { const q = obsFilter.toLowerCase(); cards = cards.filter(c => c.modName.toLowerCase().includes(q)); }
  // Sort by the chosen key; name tiebreak keeps it deterministic when shares
  // tie (e.g. idle / db mode where every card reads 0%).
  cards.sort((a, b) =>
    obsSort === 'name' ? a.modName.localeCompare(b.modName) :
    obsSort === 'usage' ? (b.usageSharePct - a.usageSharePct) || a.modName.localeCompare(b.modName) :
    (b.cpuSharePct - a.cpuSharePct) || a.modName.localeCompare(b.modName));
  if (cards.length === 0) {
    renderIfChanged('insObsList', 'nomatch:' + obsFilter, () => setHTML(scroll, emptyState('no mods match the search')));
    return;
  }

  // If nothing selected (or the selection was filtered out), select the top card.
  if (selectedObservatoryModId < 0 || !cards.some(c => c.modId === selectedObservatoryModId)) {
    selectedObservatoryModId = cards[0].modId;
  }

  // Roster composition split bar: each segment is a category, fraction = that
  // category's count / roster total. A secondary signal (thin) showing the
  // roster mix; empty roster -> a muted 'no content' note.
  function compositionBar(roster) {
    const counts = ROSTER_CATS.map(([f]) => roster[f] || 0);
    const tot = counts.reduce((a, b) => a + b, 0);
    if (tot === 0) {
      return `<div class='comp-empty' title='no content registered (library-shaped mod)'>no registered content</div>`;
    }
    const segs = ROSTER_CATS.map(([f, label, color], idx) => ({
      frac: counts[idx] / tot, color, label, value: fmtInt(counts[idx]),
    }));
    return splitBar(segs, { thin: true });
  }

  // Signature gate: filter / sort / selection + each card's displayed figures.
  // Under live load the cost figures move every poll so this rebuilds (correct);
  // on idle / paused / db-mode polls every card reads 0 and the rebuild + rebind
  // is skipped. The search box lives in the static header, so focus is unaffected.
  const obsSig = obsFilter + '|' + obsSort + '|' + selectedObservatoryModId + '|' + maxCpu.toFixed(4) + '|' +
    cards.map(c => c.modId + ':' + (c.cpuSharePct || 0).toFixed(4) + ':' + (c.usageSharePct || 0).toFixed(4) + ':' + (c.smoothedMsThisTick || 0).toFixed(3)).join(',');
  if (_renderSig['insObsList'] === obsSig) return;
  _renderSig['insObsList'] = obsSig;

  const cols = '2.2em minmax(0,1fr) 5em';
  setHTML(scroll, rowList(cards.map((c, i) => {
    const costFrac = c.cpuSharePct / maxCpu;
    // cpuSharePct / usageSharePct are FRACTIONS (0..1, each sums to 1 across the
    // roster), so they must be ×100 to read as percentages — without it a 92.9%
    // share rendered as '0.9%' (100× too small), disagreeing with the scatter and
    // the donut. (The sort/maxCpu uses above keep the raw fraction; only the
    // display scales.)
    const micro = `${fmtInt(c.usage.itemsCreated)} items · ${fmtInt(c.usage.npcsSpawned)} npcs · ${fmtInt(c.usage.buffsApplied)} buffs · ${(c.cpuSharePct * 100).toFixed(1)}% cpu · ${(c.usageSharePct * 100).toFixed(1)}% usage`;
    const bodyCell = `<div class='obs-body'>` +
      `<div class='nm'>${escapeHtml(c.modName)}</div>` +
      `<div class='obs-micro'>${micro}</div>` +
      `<div class='obs-cost'>${cellBar(costFrac, 'var(--cpu)')}</div>` +
      `<div class='obs-comp'>${compositionBar(c.roster)}</div>` +
      `</div>`;
    return row({
      cols,
      clickable: true,
      sel: c.modId === selectedObservatoryModId,
      attrs: `data-mod='${c.modId}'`,
      cells: [
        `<span class='rk'>${i + 1}</span>`,
        bodyCell,
        `<span class='obs-ms'>${fmtMs(c.smoothedMsThisTick)}<span class='u'>ms</span></span>`,
      ],
    });
  })));

  scroll.querySelectorAll('.row.clickable').forEach(el => {
    el.addEventListener('click', () => {
      selectedObservatoryModId = parseInt(el.dataset.mod, 10);
      renderObservatoryList();
      renderObservatoryDetail();
    });
  });
}

// ----- I1 + I3 + I4 detail pane --------------------------------------
// The master-detail right side, built from the shared statLine + splitLegend
// + .dtable vocabulary inside a panel. Leads with the roster composition
// legend (the key to the list's split bars), then the headline stats, the
// roster-vs-usage table, biome attendance (I3), and top loadout influence
// (I4). Tabular and readable — no metaphor shapes.
function renderObservatoryDetail() {
  const root = document.getElementById('ins-detail');
  if (!root) return;

  let scroll = root.querySelector('#det-scroll');
  if (!scroll) {
    root.innerHTML = panel({
      title: 'mod detail',
      body: scrollRegion('det-scroll', '', { maxH: '34rem' }),
      pad: 'flush',
    });
    scroll = root.querySelector('#det-scroll');
  }
  const obs = lastModObservatory;
  if (!obs || !obs.cards || obs.cards.length === 0) {
    renderIfChanged('insDetail', 'empty', () => setHTML(scroll, emptyState('select a mod from the list to see its observatory detail')));
    return;
  }
  const card = obs.cards.find(c => c.modId === selectedObservatoryModId) || obs.cards[0];
  if (!card) {
    renderIfChanged('insDetail', 'nocard', () => setHTML(scroll, emptyState('no card selected')));
    return;
  }

  // Signature gate: the selected card's id + its live figures + the table sizes.
  // A no-change poll (idle / unchanged selection) skips the detail rebuild; a
  // selection change or a moving cost figure rebuilds it.
  const detSig = card.modId + '|' + (card.cpuSharePct || 0).toFixed(4) + ':' + (card.usageSharePct || 0).toFixed(4) +
    ':' + (card.smoothedMsThisTick || 0).toFixed(3) + ':' + (card.averageMs || 0).toFixed(3) +
    '|' + (card.biomeAttendance ? card.biomeAttendance.length : 0) + ':' + (card.topLoadoutItems ? card.topLoadoutItems.length : 0);
  if (_renderSig['insDetail'] === detSig) return;
  _renderSig['insDetail'] = detSig;

  const r = card.roster, u = card.usage;
  const totalRoster = ROSTER_CATS.reduce((sum, [f]) => sum + (r[f] || 0), 0);

  // Composition legend: the key to the list split bars. Only categories
  // present in this mod's roster are shown, each with its count.
  const legendSegs = ROSTER_CATS
    .map(([f, label, color]) => ({ frac: r[f] || 0, color, label, value: fmtInt(r[f] || 0) }))
    .filter(s => s.frac > 0);
  const legendHtml = legendSegs.length > 0
    ? splitLegend(legendSegs)
    : `<div class='comp-empty'>no content registered (library-shaped mod)</div>`;

  // Headline stats as shared stat lines.
  const statsHtml =
    statLine('cpu share', dash(card.cpuSharePct, v => (v * 100).toFixed(2) + '%')) +
    statLine('smoothed ms this tick', dash(card.smoothedMsThisTick, v => fmtMs(v) + ' ms')) +
    statLine('average ms', dash(card.averageMs, v => fmtMs(v) + ' ms')) +
    statLine('usage share', dash(card.usageSharePct, v => (v * 100).toFixed(2) + '%'));

  // Roster vs usage table — perf-vocabulary dtable.
  const rosterRows = [
    ['items', r.items, u.itemsCreated],
    ['npcs', r.npcs, u.npcsSpawned],
    ['buffs', r.buffs, u.buffsApplied],
    ['projectiles', r.projectiles, null],
    ['mounts', r.mounts, null],
    ['accessories', r.accessories, u.accessoryEquippedTicks],
    ['biomes', r.biomes, u.ticksInOwnedBiomes],
    ['invasions', r.invasions, u.invasionsFought],
    ['bosses', r.bosses, u.bossesFought],
  ].map(([k, ros, used]) => {
    const usedCell = used == null ? `<td class='dim'>—</td>` : `<td>${fmtInt(used)}</td>`;
    return `<tr><td class='l'>${k}</td><td>${fmtInt(ros)}</td>${usedCell}</tr>`;
  }).join('');

  // I3 biome attendance.
  const biome = (card.biomeAttendance || []).slice(0, 12);
  const biomeHtml = biome.length === 0
    ? emptyState('no biome attendance recorded')
    : dtable(`<tr><th class='l'>biome</th><th>ticks</th><th>share</th></tr>`,
        biome.map(b => `<tr>
          <td class='l'>${escapeHtml(b.biomeName)}</td>
          <td>${dash(b.ticks, fmtInt)}</td>
          <td>${dash(b.sharePct, v => v.toFixed(1) + '%')}</td>
        </tr>`).join(''));

  // I4 loadout influence.
  const li = (card.topLoadoutItems || []).slice(0, 10);
  const liHtml = li.length === 0
    ? emptyState('no loadout influence recorded')
    : dtable(`<tr><th class='l'>item</th><th class='l'>slot</th><th>ticks equipped</th></tr>`,
        li.map(it => `<tr>
          <td class='l'>${escapeHtml(it.itemName)}</td>
          <td class='l muted'>${escapeHtml(it.slotKind || '')}</td>
          <td>${dash(it.equippedTicks, fmtInt)}</td>
        </tr>`).join(''));

  setHTML(scroll, `
    <div class='det-pad'>
      <div class='det-head'>
        <div class='det-title'>${escapeHtml(card.modName)}</div>
        <div class='det-roster'>roster total ${fmtInt(totalRoster)} entries</div>
      </div>
      ${sectionBlock('roster composition', legendHtml)}
      ${sectionBlock('headline cost & engagement', statsHtml)}
      ${sectionBlock('roster vs usage',
        dtable(`<tr><th class='l'>category</th><th>roster</th><th>used / counted</th></tr>`, rosterRows))}
      ${sectionBlock('biome attendance', biomeHtml)}
      ${sectionBlock('top loadout influence', liHtml)}
    </div>
  `);
}

// ----- I5 cross-cutting ----------------------------------------------
// One ranked .dtable per signal class, packed into an auto-fit grid in a
// panel body. Each row is a leader mod (rank + name + appearances + a cellBar
// scaled to the class max). Descriptive only.
function renderCrossCutting() {
  const root = document.getElementById('ins-cross');
  if (!root) return;
  const cc = lastCrossCutting;

  function shell(body, sub) {
    root.innerHTML = panel({ title: 'cross-cutting signals', sub, body });
  }

  if (!cc || !cc.worldLoaded || !cc.groups || cc.groups.length === 0) {
    renderIfChanged('insCross', 'none', () => shell(emptyState('no cross-cutting signals recorded yet'), '—'));
    return;
  }
  const groups = cc.groups.filter(g => g.leaders && g.leaders.length > 0);
  if (groups.length === 0) {
    renderIfChanged('insCross', 'noleaders', () => shell(emptyState('signals recorded but no leaders yet'), '—'));
    return;
  }

  // Count distinct mods across all classes for the header summary.
  const distinct = new Set();
  groups.forEach(g => (g.leaders || []).forEach(l => distinct.add(l.modId)));

  // Signature gate: each class + its leaders (modId + appearances). A no-change
  // poll skips rebuilding the whole panel (chrome + every class table). The full
  // panel is rebuilt here (not a stable scroll container), so the gate covers it.
  const insCcSig = groups.map(g => g.signalClass + '[' +
    (g.leaders || []).map(l => l.modId + ':' + (l.appearances || 0)).join(',') + ']').join('|');
  if (_renderSig['insCross'] === insCcSig) return;
  _renderSig['insCross'] = insCcSig;

  const sections = groups.map(g => {
    const leaders = (g.leaders || []).slice().sort((a, b) => b.appearances - a.appearances);
    const maxApp = Math.max(1, leaders[0] && leaders[0].appearances || 1);
    // The bar width encodes share-of-class (appearances / class max); its colour
    // is a data token (--cpu, the calm ramp green), not the near-white --accent.
    // Solid white was decorative — the brightest thing in monochrome chrome — and
    // colour here must encode, not decorate.
    const rows = leaders.map((l, i) => `<tr title='${escapeHtml(l.modName + ' — ' + fmtInt(l.appearances) + ' appearances in ' + g.signalClass)}'>
      <td class='dim'>${i + 1}</td>
      <td class='l'>${escapeHtml(l.modName)}</td>
      <td>${fmtInt(l.appearances)}</td>
      <td class='l ins-cell'>${cellBar(l.appearances / maxApp, 'var(--cpu)')}</td>
    </tr>`).join('');
    return sectionBlock(
      humanizeLabel(g.signalClass),
      dtable(`<tr><th class='dim'>#</th><th class='l'>mod</th><th>appearances</th><th class='l'>share of class</th></tr>`, rows),
      fmtInt(leaders.length) + ' mods');
  }).join('');

  shell(`<div class='cc-sections'>${sections}</div>`,
    `${groups.length} classes · ${distinct.size} mods`);
}

// ----- I6 engagement vs cost (bubble scatter) ------------------------
// A real relationship plot: usage share (x) vs cpu share (y), bubble area =
// roster size, with a y=x balance reference. A mod ABOVE the line costs more cpu
// than it earns in engagement (cost-heavy); BELOW the line it earns more
// engagement than it costs (usage-heavy); on the line it is balanced. Colour
// encodes that tilt; position + size carry the rest. Clicking a bubble opens the
// mod card. Descriptive throughout — the plot measures the ratio, names no verdict.
function renderEngagementScatter() {
  const root = document.getElementById('ins-scatter');
  if (!root) return;
  const ec = lastEngagementCost;

  let body = root.querySelector('#scatter-body');
  if (!body) {
    root.innerHTML = panel({
      title: 'engagement vs cost', sub: '—',
      body: `<div id='scatter-body'></div>`,
    });
    body = root.querySelector('#scatter-body');
  }
  const subEl = root.querySelector('.panel-sub');

  if (!ec || !ec.worldLoaded || !ec.dots || ec.dots.length === 0) {
    if (subEl) subEl.textContent = '—';
    renderIfChanged('insScatter', 'empty', () => setHTML(body, emptyState('no engagement vs cost data yet')));
    return;
  }
  if (subEl) subEl.textContent = `${ec.dots.length} mods · bubble = roster size`;

  // Signature gate: each dot's id + its plotted figures. A no-change poll skips
  // the SVG rebuild + dot-click rebind (the existing dots keep their handlers).
  // The sub-header above is a cheap sibling, left ungated.
  const scatSig = ec.dots.map(d => d.modId + ':' + (d.usageShare || 0).toFixed(4) + ':' + (d.cpuShare || 0).toFixed(4) + ':' + (d.rosterSize || 0)).join(',');
  if (_renderSig['insScatter'] === scatSig) return;
  _renderSig['insScatter'] = scatSig;

  // Tilt from the usage-vs-cost share ratio (−1 pure usage .. +1 pure cost).
  const COST = 'var(--spike)', USE = 'var(--good-bar)', BAL = 'var(--muted)';
  function tiltColor(d) {
    const sum = (d.usageShare || 0) + (d.cpuShare || 0);
    if (sum < 1e-9) return BAL;
    const t = ((d.cpuShare || 0) - (d.usageShare || 0)) / sum;
    return t > 0.15 ? COST : t < -0.15 ? USE : BAL;
  }

  // Shared axis scale so the y=x balance line is a true 45°: both axes run
  // 0..max(any usage or cpu share). Percent-formatted ticks.
  const dots = ec.dots;
  const scale = Math.max(1e-9, ...dots.map(d => Math.max(d.usageShare || 0, d.cpuShare || 0)));
  const points = dots.map(d => ({
    x: d.usageShare || 0, y: d.cpuShare || 0, r: d.rosterSize || 0,
    color: tiltColor(d), label: d.modName, id: d.modId,
  }));
  const chart = scatter({
    points, w: 440, h: 300, xMax: scale, yMax: scale, diag: true,
    xLabel: 'usage share →', yLabel: 'cpu share →',
    fmt: v => (v * 100).toFixed(1) + '%',
  });
  const key = legend([
    { color: COST, label: 'cost-heavy (above the line)' },
    { color: USE,  label: 'usage-heavy (below)' },
    { color: BAL,  label: 'balanced' },
  ], { inline: true });
  setHTML(body, chart + `<div class='sc-foot'>${key}</div>`);

  // Bubbles open the mod card, same as the donut / observatory.
  body.querySelectorAll('.sc-dot.hit').forEach(el => {
    el.addEventListener('click', () => { if (typeof openModCard === 'function') openModCard(parseInt(el.dataset.mod, 10)); });
  });
}

// ----- I7 mod-pair cost correlation ----------------------------------
// Readable top-coupled-pairs .dtable in a panel under a plain-English caption.
// Each row: mod A x mod B, signed Pearson r (green positive, red negative),
// a magnitude cellBar, and the sample count. Descriptive only — no verdict on
// a pair. Scroll region stable across polls.
function renderModInteractionMatrix() {
  const root = document.getElementById('ins-matrix');
  if (!root) return;
  const mi = lastModInteraction;

  let scroll = root.querySelector('#matrix-scroll');
  if (!scroll) {
    root.innerHTML = panel({
      // Caption lives in the (non-scrolling) panel body ABOVE the scroll region,
      // so the table's sticky header can never overlap it.
      title: 'mod-pair cost correlation', sub: '—',
      body: `<div class='ins-caption'>mods whose per-tick CPU rises and falls together — a high r means they tend to get busy at the same moments</div>` +
        scrollRegion('matrix-scroll', '', { maxH: '320px' }),
      pad: 'tight',
    });
    scroll = root.querySelector('#matrix-scroll');
  }
  const subEl = root.querySelector('.panel-sub');

  if (!mi || !mi.worldLoaded || !mi.modIds || mi.modIds.length === 0) {
    if (subEl) subEl.textContent = '—';
    renderIfChanged('insMatrix', 'none', () => setHTML(scroll, emptyState('no mod interaction data yet (needs ≥2 active mods over time)')));
    return;
  }
  const N = mi.modIds.length;
  if (N < 2 || !mi.topCoupled || mi.topCoupled.length === 0) {
    if (subEl) subEl.textContent = `${N} mods`;
    renderIfChanged('insMatrix', 'nopairs:' + N, () => setHTML(scroll, emptyState('no coupled pairs ranked yet')));
    return;
  }
  if (subEl) subEl.textContent = `${N} mods (Pearson r)`;

  const pairs = mi.topCoupled.slice(0, 12);
  // Signature gate: the ranked pair list (names + r + samples). A no-change poll
  // skips the bar scaling + table reparse. The sub-header above is left ungated.
  const insMatrixSig = pairs.map(p => p.modNameA + '×' + p.modNameB + ':' + (p.pearson || 0).toFixed(4) + ':' + (p.samplesUsed || 0)).join(',');
  if (_renderSig['insMatrix'] === insMatrixSig) return;
  _renderSig['insMatrix'] = insMatrixSig;
  // Strong-correlation clusters (e.g. every |r| in 0.99..1.0) make a 0..max bar
  // read as uniformly full, so it adds nothing over the r column. Scale the bar to
  // the VISIBLE range instead: baseline at the lowest |r| shown, full at the
  // highest, so small differences between strong correlations stay separable. The
  // numeric r column carries the absolute value; the bar carries the spread.
  const absVals = pairs.map(p => Math.abs(p.pearson || 0));
  const loAbs = Math.min(...absVals), hiAbs = Math.max(...absVals);
  const spanAbs = hiAbs - loAbs;
  const rows = pairs.map((p, i) => {
    const r = p.pearson || 0;
    const tint = r >= 0 ? 'var(--good)' : 'var(--danger)';
    // Map |r| onto [0.08 .. 1] of the visible range so even the weakest shown row
    // keeps a readable stub; a degenerate all-equal set falls back to full.
    const frac = spanAbs > 1e-9 ? 0.08 + 0.92 * ((Math.abs(r) - loAbs) / spanAbs) : 1;
    return `<tr title='${escapeHtml(p.modNameA + ' × ' + p.modNameB + ' — r = ' + r.toFixed(3) + ' (n=' + fmtInt(p.samplesUsed) + ')')}'>
      <td class='dim'>${i + 1}</td>
      <td class='l'>${escapeHtml(p.modNameA)}</td>
      <td class='l'>${escapeHtml(p.modNameB)}</td>
      <td style='color:${tint}'>${r.toFixed(3)}</td>
      <td class='l ins-cell'>${cellBar(frac, tint)}</td>
      <td>${fmtInt(p.samplesUsed)}</td>
    </tr>`;
  }).join('');

  setHTML(scroll, dtable(`<tr><th class='dim'>#</th><th class='l'>mod A</th><th class='l'>mod B</th><th>r</th><th class='l'>magnitude</th><th>samples</th></tr>`, rows));
}
";
}
