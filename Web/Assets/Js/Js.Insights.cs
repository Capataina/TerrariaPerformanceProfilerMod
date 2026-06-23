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
// observatory list split bar and its legend so colour == meaning across
// both. Colours come from the palette tokens, never invented.
const ROSTER_CATS = [
  ['items',       'items',       'var(--good)'],
  ['npcs',        'npcs',        'var(--danger)'],
  ['buffs',       'buffs',       'var(--amber)'],
  ['projectiles', 'projectiles', 'var(--cyan)'],
  ['mounts',      'mounts',      'var(--orange)'],
  ['accessories', 'accessories', 'var(--accent)'],
  ['biomes',      'biomes',      'var(--magenta)'],
  ['invasions',   'invasions',   'var(--purple)'],
  ['bosses',      'bosses',      'var(--text-bright)'],
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

function renderInsights() {
  renderInsightsKpiStrip();
  renderDormantSurface();
  renderObservatoryList();
  renderObservatoryDetail();
  renderCrossCutting();
  renderEngagementScatter();
  renderModInteractionMatrix();
}

// Shared sortable-header builder for the .dtable surfaces. cols: [{key, label,
// l (left-align bool), title}]. Clicking a header sorts by that key (toggling
// direction); the active column shows the direction arrow. onSort() is invoked
// after the state is updated. rootId scopes the click binding to one table.
function sortableHead(cols, state, onSort, rootId) {
  const ths = cols.map(c => {
    const sorted = c.key === state.key;
    const cls = (c.l ? 'l ' : '') + 'sortable' + (sorted ? ' sorted' : '');
    const arrow = sorted ? (state.dir === 1 ? ' ▲' : ' ▼') : '';
    const t = c.title ? ` title='${escapeHtml(c.title)}'` : '';
    return `<th class='${cls}' data-key='${c.key}'${t}>${escapeHtml(c.label)}${arrow}</th>`;
  }).join('');
  // Defer binding until the table is in the DOM (caller sets innerHTML next).
  setTimeout(() => {
    const root = document.getElementById(rootId);
    if (!root) return;
    root.querySelectorAll('th.sortable').forEach(th => {
      if (th.dataset.bound) return;
      th.dataset.bound = '1';
      th.addEventListener('click', () => {
        const k = th.dataset.key;
        if (state.key === k) state.dir = -state.dir;
        else { state.key = k; state.dir = -1; }
        onSort();
      });
    });
  }, 0);
  return `<tr>${ths}</tr>`;
}

// ----- KPI strip ------------------------------------------------------
// Four full-ring gauges (sweep 360), one per modlist bucket. Each ring's
// arc-fill encodes that bucket's share of the loaded modlist; the centre
// carries the count. Descriptive only — the gauge shows the measured
// fraction, no thresholds or judgement. Built once into a panel body; the
// counts change rarely so a plain innerHTML rebuild is fine here.
function renderInsightsKpiStrip() {
  const root = document.getElementById('ins-kpi');
  if (!root) return;
  const obs = lastModObservatory || {};
  const dor = lastDormant || {};
  const active = obs.activeCount != null ? obs.activeCount : 0;
  const dormant = obs.dormantCount != null ? obs.dormantCount : (dor.modsWithZeroUsage || 0);
  const loaded = dor.modsLoaded != null ? dor.modsLoaded : (active + dormant);
  const lowUse = dor.modsBelowFivePercentUsage != null ? dor.modsBelowFivePercentUsage : 0;
  const denom = Math.max(1, loaded);

  function kpi(label, value, frac, sub, color) {
    const g = gauge({
      ratio: Math.max(0, Math.min(1, frac)), sweep: 360, w: 64, stroke: 6,
      color, centerValue: value,
    });
    return `<div class='kpi-cell'>${g}<div class='kpi-meta'>` +
      `<span class='kpi-lbl'>${escapeHtml(label)}</span>` +
      `<span class='kpi-sub'>${escapeHtml(sub)}</span></div></div>`;
  }

  const body = `<div class='kpi-grid'>` +
    kpi('mods loaded',    fmtInt(loaded),  1,               'profiled this session',                              'var(--accent)') +
    kpi('active',         fmtInt(active),  active / denom,   (100 * active / denom).toFixed(0) + '% of roster',    'var(--good)') +
    kpi('dormant',        fmtInt(dormant), dormant / denom,  (100 * dormant / denom).toFixed(0) + '% zero usage', 'var(--magenta)') +
    kpi('under 5% usage', fmtInt(lowUse),  lowUse / denom,   (100 * lowUse / denom).toFixed(0) + '% sub-5%',      'var(--amber)') +
    `</div>`;
  root.innerHTML = panel({ body });
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
    setHTML(scroll, '');
    return;
  }
  if (subEl) subEl.textContent = `${fmtInt(dor.modsWithZeroUsage)} mods at zero usage · ${fmtInt(dor.modsBelowFivePercentUsage)} under 5%`;

  const entries = (dor.entries || []).slice();
  if (entries.length === 0) {
    setHTML(scroll, emptyState('no dormant entries recorded this session'));
    return;
  }

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
    { key: 'modName',    label: 'mod',           l: true },
    { key: 'usageRatio', label: 'usage',         title: 'engaged share of this roster' },
    { key: 'usedCount',  label: 'used / roster', title: 'roster entries observed in use this session' },
    { key: 'rosterSize', label: 'roster' },
  ];
  // The dominant-unused-category column is presentational only (not sorted).
  const headRow = sortableHead(cols, dormantSort, renderDormantSurface, 'ins-dormant')
    .replace('</tr>', `<th class='l'>dominant unused</th></tr>`);

  const rows = entries.map(e => {
    const ratio = Math.max(0, Math.min(1, e.usageRatio || 0));
    const pct = (ratio * 100).toFixed(1);
    const bar = splitBar([
      { frac: ratio,     color: 'var(--good)',    label: 'engaged', value: fmtInt(e.usedCount) },
      { frac: 1 - ratio, color: 'var(--surface)', label: 'unused',  value: fmtInt(Math.max(0, (e.rosterSize||0) - (e.usedCount||0))) },
    ], { thin: true });
    const cat = e.dominantUnusedCategory
      ? `<span class='chip'>${escapeHtml(e.dominantUnusedCategory)}</span>`
      : `<span class='dim'>—</span>`;
    return `<tr title='${escapeHtml(e.modName + ' — ' + pct + '% engaged · ' + fmtInt(e.usedCount) + '/' + fmtInt(e.rosterSize) + ' entries used')}'>
      <td class='l'>${escapeHtml(e.modName)}</td>
      <td class='l'><div class='ins-usage'>${bar}<span class='ins-pct'>${pct}%</span></div></td>
      <td>${fmtInt(e.usedCount)} / ${fmtInt(e.rosterSize)}</td>
      <td>${fmtInt(e.rosterSize)}</td>
      <td class='l'>${cat}</td>
    </tr>`;
  }).join('');

  setHTML(scroll, `
    <table class='dtable'>
      <thead>${headRow}</thead>
      <tbody>${rows}</tbody>
    </table>`);
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
    root.innerHTML = panel({
      title: 'per-mod observatory',
      body: scrollRegion('obs-scroll', '', { fill: true }),
      pad: 'flush', fill: true, cls: 'ins-fillcol',
    });
    scroll = root.querySelector('#obs-scroll');
  }
  const obs = lastModObservatory;
  if (!obs || !obs.worldLoaded || !obs.cards || obs.cards.length === 0) {
    setHTML(scroll, emptyState('no per-mod observatory data yet'));
    return;
  }
  // Name tiebreak so the order is deterministic and alphabetical when cpu
  // shares are equal (e.g. idle / db mode where every card reads 0%).
  const cards = obs.cards.slice().sort((a, b) => (b.cpuSharePct - a.cpuSharePct) || a.modName.localeCompare(b.modName));
  const maxCpu = Math.max(0.0001, cards[0].cpuSharePct);

  // If nothing selected, auto-select the top card so the detail pane has content.
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

  const cols = '2.2em minmax(0,1fr) 5em';
  setHTML(scroll, rowList(cards.map((c, i) => {
    const costFrac = c.cpuSharePct / maxCpu;
    const micro = `${fmtInt(c.usage.itemsCreated)} items · ${fmtInt(c.usage.npcsSpawned)} npcs · ${fmtInt(c.usage.buffsApplied)} buffs · ${c.cpuSharePct.toFixed(1)}% cpu · ${c.usageSharePct.toFixed(1)}% usage`;
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
      body: scrollRegion('det-scroll', '', { fill: true }),
      pad: 'flush', fill: true, cls: 'ins-fillcol',
    });
    scroll = root.querySelector('#det-scroll');
  }
  const obs = lastModObservatory;
  if (!obs || !obs.cards || obs.cards.length === 0) {
    setHTML(scroll, emptyState('select a mod from the list to see its observatory detail'));
    return;
  }
  const card = obs.cards.find(c => c.modId === selectedObservatoryModId) || obs.cards[0];
  if (!card) {
    setHTML(scroll, emptyState('no card selected'));
    return;
  }

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
    statLine('cpu share', dash(card.cpuSharePct, v => v.toFixed(2) + '%')) +
    statLine('smoothed ms this tick', dash(card.smoothedMsThisTick, v => fmtMs(v) + ' ms')) +
    statLine('average ms', dash(card.averageMs, v => fmtMs(v) + ' ms')) +
    statLine('usage share', dash(card.usageSharePct, v => v.toFixed(2) + '%'));

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
    : `<table class='dtable'>
        <thead><tr><th class='l'>biome</th><th>ticks</th><th>share</th></tr></thead>
        <tbody>${biome.map(b => `<tr>
          <td class='l'>${escapeHtml(b.biomeName)}</td>
          <td>${dash(b.ticks, fmtInt)}</td>
          <td>${dash(b.sharePct, v => v.toFixed(1) + '%')}</td>
        </tr>`).join('')}</tbody></table>`;

  // I4 loadout influence.
  const li = (card.topLoadoutItems || []).slice(0, 10);
  const liHtml = li.length === 0
    ? emptyState('no loadout influence recorded')
    : `<table class='dtable'>
        <thead><tr><th class='l'>item</th><th class='l'>slot</th><th>ticks equipped</th></tr></thead>
        <tbody>${li.map(it => `<tr>
          <td class='l'>${escapeHtml(it.itemName)}</td>
          <td class='l muted'>${escapeHtml(it.slotKind || '')}</td>
          <td>${dash(it.equippedTicks, fmtInt)}</td>
        </tr>`).join('')}</tbody></table>`;

  setHTML(scroll, `
    <div class='det-pad'>
      <div class='det-head'>
        <div class='det-title'>${escapeHtml(card.modName)}</div>
        <div class='det-roster'>roster total ${fmtInt(totalRoster)} entries</div>
      </div>
      ${sectionBlock('roster composition', legendHtml)}
      ${sectionBlock('headline cost &amp; engagement', statsHtml)}
      ${sectionBlock('roster vs usage', `
        <table class='dtable'>
          <thead><tr><th class='l'>category</th><th>roster</th><th>used / counted</th></tr></thead>
          <tbody>${rosterRows}</tbody>
        </table>`)}
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
    shell(emptyState('no cross-cutting signals recorded yet'), '—');
    return;
  }
  const groups = cc.groups.filter(g => g.leaders && g.leaders.length > 0);
  if (groups.length === 0) {
    shell(emptyState('signals recorded but no leaders yet'), '—');
    return;
  }

  // Count distinct mods across all classes for the header summary.
  const distinct = new Set();
  groups.forEach(g => (g.leaders || []).forEach(l => distinct.add(l.modId)));

  const sections = groups.map(g => {
    const leaders = (g.leaders || []).slice().sort((a, b) => b.appearances - a.appearances);
    const maxApp = Math.max(1, leaders[0] && leaders[0].appearances || 1);
    const rows = leaders.map((l, i) => `<tr title='${escapeHtml(l.modName + ' — ' + fmtInt(l.appearances) + ' appearances in ' + g.signalClass)}'>
      <td class='dim'>${i + 1}</td>
      <td class='l'>${escapeHtml(l.modName)}</td>
      <td>${fmtInt(l.appearances)}</td>
      <td class='l ins-cell'>${cellBar(l.appearances / maxApp, 'var(--accent)')}</td>
    </tr>`).join('');
    return sectionBlock(
      escapeHtml(g.signalClass),
      `<table class='dtable'>
        <thead><tr><th class='dim'>#</th><th class='l'>mod</th><th>appearances</th><th class='l'>share of class</th></tr></thead>
        <tbody>${rows}</tbody>
      </table>`,
      fmtInt(leaders.length) + ' mods');
  }).join('');

  shell(`<div class='cc-sections'>${sections}</div>`,
    `${groups.length} classes · ${distinct.size} mods`);
}

// ----- I6 engagement vs cost -----------------------------------------
// Sortable perf .dtable in a panel. Columns: mod, usage share, cpu share,
// roster size, and a 'tilt' chip describing where the mod sits on the
// usage-vs-cost ratio. cost-heavy (cpu materially above usage) -> .bad;
// usage-heavy (usage materially above cpu) -> .good; otherwise balanced.
// The tilt is a measurement of the share ratio, not a verdict. Scroll region
// stable across polls.
function renderEngagementScatter() {
  const root = document.getElementById('ins-scatter');
  if (!root) return;
  const ec = lastEngagementCost;

  let scroll = root.querySelector('#scatter-scroll');
  if (!scroll) {
    root.innerHTML = panel({
      title: 'engagement vs cost', sub: '—',
      body: scrollRegion('scatter-scroll', '', { maxH: '360px' }),
      pad: 'flush',
    });
    scroll = root.querySelector('#scatter-scroll');
  }
  const subEl = root.querySelector('.panel-sub');

  if (!ec || !ec.worldLoaded || !ec.dots || ec.dots.length === 0) {
    if (subEl) subEl.textContent = '—';
    setHTML(scroll, emptyState('no engagement vs cost data yet'));
    return;
  }
  if (subEl) subEl.textContent = `${ec.dots.length} mods`;

  // Tilt classification from the usage-vs-cost share ratio.
  // Returns {cls, label} where cls maps to a chip variant.
  function tilt(d) {
    const sum = (d.usageShare || 0) + (d.cpuShare || 0);
    if (sum < 1e-6) return { cls: '', label: 'idle' };
    const t = ((d.cpuShare || 0) - (d.usageShare || 0)) / sum;  // -1 pure usage .. +1 pure cost
    if (t > 0.15)  return { cls: 'bad',  label: 'cost-heavy' };
    if (t < -0.15) return { cls: 'good', label: 'usage-heavy' };
    return { cls: 'cool', label: 'balanced' };
  }

  const dots = ec.dots.slice();
  const getters = {
    modName:    d => (d.modName || '').toLowerCase(),
    usageShare: d => d.usageShare,
    cpuShare:   d => d.cpuShare,
    rosterSize: d => d.rosterSize,
  };
  const g = getters[engagementSort.key] || getters.cpuShare;
  dots.sort((a, b) => {
    const va = g(a), vb = g(b);
    if (va < vb) return -1 * engagementSort.dir;
    if (va > vb) return  1 * engagementSort.dir;
    return 0;
  });

  const cols = [
    { key: 'modName',    label: 'mod',         l: true },
    { key: 'usageShare', label: 'usage share', title: 'share of all engagement attributed to this mod' },
    { key: 'cpuShare',   label: 'cpu share',   title: 'share of all measured cpu attributed to this mod' },
    { key: 'rosterSize', label: 'roster' },
  ];
  const headRow = sortableHead(cols, engagementSort, renderEngagementScatter, 'ins-scatter')
    .replace('</tr>', `<th class='l'>tilt</th></tr>`);

  const rows = dots.map(d => {
    const tl = tilt(d);
    const chipCls = tl.cls ? ` ${tl.cls}` : '';
    return `<tr title='${escapeHtml(d.modName + ' — usage ' + ((d.usageShare||0)*100).toFixed(1) + '% · cpu ' + ((d.cpuShare||0)*100).toFixed(1) + '% · roster ' + fmtInt(d.rosterSize))}'>
      <td class='l'>${escapeHtml(d.modName)}</td>
      <td>${((d.usageShare||0)*100).toFixed(1)}%</td>
      <td>${((d.cpuShare||0)*100).toFixed(1)}%</td>
      <td>${fmtInt(d.rosterSize)}</td>
      <td class='l'><span class='chip${chipCls}'>${tl.label}</span></td>
    </tr>`;
  }).join('');

  setHTML(scroll, `
    <table class='dtable'>
      <thead>${headRow}</thead>
      <tbody>${rows}</tbody>
    </table>`);
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
      title: 'mod-pair cost correlation', sub: '—',
      body: scrollRegion('matrix-scroll', '', { maxH: '360px' }),
      pad: 'flush',
    });
    scroll = root.querySelector('#matrix-scroll');
  }
  const subEl = root.querySelector('.panel-sub');

  if (!mi || !mi.worldLoaded || !mi.modIds || mi.modIds.length === 0) {
    if (subEl) subEl.textContent = '—';
    setHTML(scroll, emptyState('no mod interaction data yet (needs ≥2 active mods over time)'));
    return;
  }
  const N = mi.modIds.length;
  if (N < 2 || !mi.topCoupled || mi.topCoupled.length === 0) {
    if (subEl) subEl.textContent = `${N} mods`;
    setHTML(scroll, emptyState('no coupled pairs ranked yet'));
    return;
  }
  if (subEl) subEl.textContent = `${N} mods (Pearson r)`;

  const pairs = mi.topCoupled.slice(0, 12);
  const maxAbs = Math.max(1e-6, ...pairs.map(p => Math.abs(p.pearson || 0)));
  const rows = pairs.map((p, i) => {
    const r = p.pearson || 0;
    const tint = r >= 0 ? 'var(--good)' : 'var(--danger)';
    return `<tr title='${escapeHtml(p.modNameA + ' × ' + p.modNameB + ' — r = ' + r.toFixed(3) + ' (n=' + fmtInt(p.samplesUsed) + ')')}'>
      <td class='dim'>${i + 1}</td>
      <td class='l'>${escapeHtml(p.modNameA)}</td>
      <td class='l'>${escapeHtml(p.modNameB)}</td>
      <td style='color:${tint}'>${r.toFixed(3)}</td>
      <td class='l ins-cell'>${cellBar(Math.abs(r) / maxAbs, tint)}</td>
      <td>${fmtInt(p.samplesUsed)}</td>
    </tr>`;
  }).join('');

  setHTML(scroll, `
    <div class='ins-caption'>mods whose per-tick CPU rises and falls together — a high r means they tend to get busy at the same moments</div>
    <table class='dtable'>
      <thead><tr><th class='dim'>#</th><th class='l'>mod A</th><th class='l'>mod B</th><th>r</th><th class='l'>magnitude</th><th>samples</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>`);
}
";
}
