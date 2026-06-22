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
// The Insights tab is the per-mod observatory, rebuilt from the shared
// readable vocabulary (split bars, perf-tinted sortable tables, chips,
// stat lines, a rectangular heatmap) instead of bespoke metaphor charts.
// Surfaces: KPI ring strip, dormant-content table (I2), mod card list
// with a composition split bar + a tabular detail pane (I1+I3+I4), a
// cross-cutting signal-class section list (I5), an engagement-vs-cost
// table (I6), and a mod-pair correlation table + heatmap (I7).
//
// Invariant 3: every string is descriptive. No 'should remove', no
// 'junk'; only measurements like 'X items used of Y in roster'.
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

// Shared sortable-header builder for the readable .dtable surfaces.
// cols: [{key, label, l (left-align bool), title}]. Clicking a header
// sorts by that key (toggling direction); the active column shows the
// direction arrow. onSort(key) is invoked after the state is updated.
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
// Four mini ring gauges. Each ring's arc-fill encodes the share of the
// loaded modlist that falls in this bucket. Descriptive only — the gauge
// shows the measured fraction; no thresholds, no judgement. Acceptable
// flair: small, standard, kept light-touch.
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

  function ring(label, value, frac, sub, hue) {
    const R = 26, C = 2 * Math.PI * R;
    const f = Math.max(0, Math.min(1, frac));
    const dash = (C * f).toFixed(1);
    const gap  = (C - C * f).toFixed(1);
    return `<div class='tile'>
      <div class='ring-wrap'>
        <svg viewBox='0 0 64 64' class='ring'>
          <circle cx='32' cy='32' r='${R}' class='track'></circle>
          <circle cx='32' cy='32' r='${R}' class='arc' stroke='${hue}'
            stroke-dasharray='${dash} ${gap}' transform='rotate(-90 32 32)'></circle>
          <text x='32' y='35' text-anchor='middle' class='ring-val'>${value}</text>
        </svg>
      </div>
      <div class='tile-body'>
        <span class='lbl'>${label}</span>
        <span class='sub'>${sub}</span>
      </div>
    </div>`;
  }

  root.innerHTML = `
    ${ring('mods loaded',   fmtInt(loaded),  1,                       'profiled this session', 'var(--accent)')}
    ${ring('active',        fmtInt(active),  active / denom,          (100 * active / denom).toFixed(0) + '% of roster', 'var(--good)')}
    ${ring('dormant',       fmtInt(dormant), dormant / denom,         (100 * dormant / denom).toFixed(0) + '% zero usage', 'var(--magenta)')}
    ${ring('under 5% usage',fmtInt(lowUse),  lowUse / denom,          (100 * lowUse / denom).toFixed(0) + '% sub-5%',     'var(--amber)')}
  `;
}

// ----- I2 dormant surface --------------------------------------------
// Sortable perf-tinted table. Each row is a mod with a usage split bar
// (engaged green vs unused surface) carrying the % text, a used/roster
// count, and the dominant unused category as a chip. The caption carries
// the two headline totals. Pure measurement — no judgement on any row.
function renderDormantSurface() {
  const root = document.getElementById('ins-dormant');
  if (!root) return;
  const dor = lastDormant;
  if (!dor || !dor.worldLoaded) {
    root.innerHTML = `<div class='dor-h'><span class='label'>dormant content</span><span>—</span></div>`;
    return;
  }
  const entries = (dor.entries || []).slice();
  const headSummary = `${fmtInt(dor.modsWithZeroUsage)} mods at zero usage · ${fmtInt(dor.modsBelowFivePercentUsage)} under 5%`;

  if (entries.length === 0) {
    root.innerHTML = `
      <div class='dor-h'><span class='label'>dormant content</span><span>${headSummary}</span></div>
      <div class='dor-empty'>no dormant entries recorded this session</div>`;
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
    { key: 'modName',    label: 'mod',              l: true },
    { key: 'usageRatio', label: 'usage',            title: 'engaged share of this roster' },
    { key: 'usedCount',  label: 'used / roster',    title: 'roster entries observed in use this session' },
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
      <td class='l'><div class='dor-usage'>${bar}<span class='dor-pct'>${pct}%</span></div></td>
      <td>${fmtInt(e.usedCount)} / ${fmtInt(e.rosterSize)}</td>
      <td>${fmtInt(e.rosterSize)}</td>
      <td class='l'>${cat}</td>
    </tr>`;
  }).join('');

  root.innerHTML = `
    <div class='dor-h'><span class='label'>dormant content</span><span>${headSummary}</span></div>
    <div class='dor-scroll'>
      <table class='dtable'>
        <thead>${headRow}</thead>
        <tbody>${rows}</tbody>
      </table>
    </div>`;
}

// ----- I1 observatory list -------------------------------------------
// Ranked card list, ordered by cpu share. Each card carries the mod
// name, a cost cellBar, readable usage micro-stats, and a roster
// composition split bar (replacing the unreadable DNA strand) whose
// legend appears in the detail pane. Click selects -> fills detail pane.
function renderObservatoryList() {
  const root = document.getElementById('ins-obs-list');
  if (!root) return;
  const obs = lastModObservatory;
  if (!obs || !obs.worldLoaded || !obs.cards || obs.cards.length === 0) {
    root.innerHTML = `<div style='padding:1.5rem;color:var(--dim);text-align:center;font-size:0.85rem'>no per-mod observatory data yet</div>`;
    return;
  }
  const cards = obs.cards.slice().sort((a, b) => b.cpuSharePct - a.cpuSharePct);
  const maxCpu = Math.max(0.0001, cards[0].cpuSharePct);

  // If nothing selected, auto-select the top card so the detail pane has content.
  if (selectedObservatoryModId < 0 || !cards.some(c => c.modId === selectedObservatoryModId)) {
    selectedObservatoryModId = cards[0].modId;
  }

  // Roster composition split bar: each segment is a category, fraction =
  // that category's count / roster total. One labelled, readable bar
  // replacing the 1-2px DNA strand. Empty roster -> a muted 'no content'.
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

  root.innerHTML = cards.map((c, i) => {
    const sel = c.modId === selectedObservatoryModId ? 'selected' : '';
    const costFrac = c.cpuSharePct / maxCpu;
    const micro = `${fmtInt(c.usage.itemsCreated)} items · ${fmtInt(c.usage.npcsSpawned)} npcs · ${fmtInt(c.usage.buffsApplied)} buffs · ${c.cpuSharePct.toFixed(1)}% cpu · ${c.usageSharePct.toFixed(1)}% usage`;
    return `<div class='ins-obs-card ${sel}' data-mod='${c.modId}'>
      <span class='rank'>${i + 1}</span>
      <div class='body'>
        <div class='nm'>${escapeHtml(c.modName)}</div>
        <div class='comp'>${compositionBar(c.roster)}</div>
        <div class='micro'>${micro}</div>
        <div class='cost'>${cellBar(costFrac, 'var(--cpu)')}</div>
      </div>
      <span class='ms'>${fmtMs(c.smoothedMsThisTick)}<span class='u'>ms</span></span>
    </div>`;
  }).join('');

  root.querySelectorAll('.ins-obs-card').forEach(el => {
    el.addEventListener('click', () => {
      selectedObservatoryModId = parseInt(el.dataset.mod, 10);
      renderObservatoryList();
      renderObservatoryDetail();
    });
  });
}

// ----- I1 + I3 + I4 detail pane --------------------------------------
// Restyled with the shared statline + dtable vocabulary. Leads with the
// roster composition legend (the key to the list's split bars), then the
// headline stats, the roster-vs-usage table, biome attendance, and top
// loadout influence. Tabular and readable — no metaphor shapes.
function renderObservatoryDetail() {
  const root = document.getElementById('ins-detail');
  if (!root) return;
  const obs = lastModObservatory;
  if (!obs || !obs.cards || obs.cards.length === 0) {
    root.innerHTML = `<div class='empty'>select a mod from the list to see its observatory detail</div>`;
    return;
  }
  const card = obs.cards.find(c => c.modId === selectedObservatoryModId) || obs.cards[0];
  if (!card) {
    root.innerHTML = `<div class='empty'>no card selected</div>`;
    return;
  }

  const r = card.roster, u = card.usage;
  const totalRoster = (r.items + r.npcs + r.buffs + r.projectiles + r.mounts + r.accessories + r.biomes + r.invasions + r.bosses);

  // Composition legend: the key to the list split bars. Only categories
  // present in this mod's roster are shown, each with its count.
  const legendSegs = ROSTER_CATS
    .map(([f, label, color]) => ({ frac: r[f] || 0, color, label, value: fmtInt(r[f] || 0) }))
    .filter(s => s.frac > 0);
  const legendHtml = legendSegs.length > 0
    ? splitLegend(legendSegs)
    : `<div style='color:var(--dim);font-size:0.78rem'>no content registered (library-shaped mod)</div>`;

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
    ? `<div style='color:var(--dim);font-size:0.78rem'>no biome attendance recorded</div>`
    : `<table class='dtable'>
        <thead><tr><th class='l'>biome</th><th>ticks</th><th>share</th></tr></thead>
        <tbody>${biome.map(b => `<tr>
          <td class='l'>${escapeHtml(b.biomeName)}</td>
          <td>${fmtInt(b.ticks)}</td>
          <td>${b.sharePct.toFixed(1)}%</td>
        </tr>`).join('')}</tbody></table>`;

  // I4 loadout influence.
  const li = (card.topLoadoutItems || []).slice(0, 10);
  const liHtml = li.length === 0
    ? `<div style='color:var(--dim);font-size:0.78rem'>no loadout influence recorded</div>`
    : `<table class='dtable'>
        <thead><tr><th class='l'>item</th><th class='l'>slot</th><th>ticks equipped</th></tr></thead>
        <tbody>${li.map(it => `<tr>
          <td class='l'>${escapeHtml(it.itemName)}</td>
          <td class='l muted'>${escapeHtml(it.slotKind || '')}</td>
          <td>${fmtInt(it.equippedTicks)}</td>
        </tr>`).join('')}</tbody></table>`;

  root.innerHTML = `
    <div>
      <div class='det-title'>${escapeHtml(card.modName)}</div>
      <div style='font-family:var(--mono);font-size:0.74rem;color:var(--muted)'>roster total ${fmtInt(totalRoster)} entries</div>
    </div>
    <div>
      <h4>roster composition</h4>
      ${legendHtml}
    </div>
    <div class='det-stats'>
      <div class='statline'><span class='k'>cpu share</span><span class='v'>${card.cpuSharePct.toFixed(2)}%</span></div>
      <div class='statline'><span class='k'>smoothed ms this tick</span><span class='v'>${fmtMs(card.smoothedMsThisTick)} ms</span></div>
      <div class='statline'><span class='k'>average ms</span><span class='v'>${fmtMs(card.averageMs)} ms</span></div>
      <div class='statline'><span class='k'>usage share</span><span class='v'>${card.usageSharePct.toFixed(2)}%</span></div>
    </div>
    <div>
      <h4>roster vs usage</h4>
      <table class='dtable'>
        <thead><tr><th class='l'>category</th><th>roster</th><th>used / counted</th></tr></thead>
        <tbody>${rosterRows}</tbody>
      </table>
    </div>
    <div>
      <h4>biome attendance</h4>
      ${biomeHtml}
    </div>
    <div>
      <h4>top loadout influence</h4>
      ${liHtml}
    </div>
  `;
}

// ----- I5 cross-cutting ----------------------------------------------
// Grouped ranked tables: one labelled section per signal class, each a
// ranked .dtable of leader mods (mod name + appearances + a cellBar
// scaled to the section's max appearances). Replaces the constellation
// spine/stars with a readable, scannable list. Descriptive only.
function renderCrossCutting() {
  const root = document.getElementById('ins-cross');
  if (!root) return;
  const cc = lastCrossCutting;
  if (!cc || !cc.worldLoaded || !cc.groups || cc.groups.length === 0) {
    root.innerHTML = `<div class='cc-h'>cross-cutting signals</div>
      <div class='cc-empty'>no cross-cutting signals recorded yet</div>`;
    return;
  }
  const groups = cc.groups.filter(g => g.leaders && g.leaders.length > 0);
  if (groups.length === 0) {
    root.innerHTML = `<div class='cc-h'>cross-cutting signals</div>
      <div class='cc-empty'>signals recorded but no leaders yet</div>`;
    return;
  }

  // Count distinct mods across all classes for the header summary.
  const distinct = new Set();
  groups.forEach(g => (g.leaders || []).forEach(l => distinct.add(l.modId)));

  const sections = groups.map(g => {
    const leaders = (g.leaders || []).slice().sort((a, b) => b.appearances - a.appearances);
    const maxApp = Math.max(1, leaders[0]?.appearances || 1);
    const rows = leaders.map((l, i) => `<tr title='${escapeHtml(l.modName + ' — ' + fmtInt(l.appearances) + ' appearances in ' + g.signalClass)}'>
      <td class='dim'>${i + 1}</td>
      <td class='l'>${escapeHtml(l.modName)}</td>
      <td>${fmtInt(l.appearances)}</td>
      <td class='l cc-cell'>${cellBar(l.appearances / maxApp, 'var(--accent)')}</td>
    </tr>`).join('');
    return `<div class='cc-section'>
      <div class='cc-cls'>${escapeHtml(g.signalClass)} <span class='cc-cnt'>${fmtInt(leaders.length)} mods</span></div>
      <table class='dtable'>
        <thead><tr><th class='dim'>#</th><th class='l'>mod</th><th>appearances</th><th class='l'>share of class</th></tr></thead>
        <tbody>${rows}</tbody>
      </table>
    </div>`;
  }).join('');

  root.innerHTML = `
    <div class='cc-h'>cross-cutting signals — ${groups.length} classes · ${distinct.size} mods</div>
    <div class='cc-sections'>${sections}</div>
  `;
}

// ----- I6 engagement vs cost -----------------------------------------
// Sortable perf-tinted table. Columns: mod, usage share, cpu share,
// roster size, and a 'tilt' chip describing where the mod sits in the
// usage-vs-cost ratio. cost-heavy (cpu materially above usage) -> .bad;
// usage-heavy (usage materially above cpu) -> .good; otherwise balanced.
// The tilt is a measurement of the share ratio, not a verdict.
function renderEngagementScatter() {
  const root = document.getElementById('ins-scatter');
  if (!root) return;
  const ec = lastEngagementCost;
  if (!ec || !ec.worldLoaded || !ec.dots || ec.dots.length === 0) {
    root.innerHTML = `<div class='sc-h'>engagement vs cost</div>
      <div class='sc-empty'>no engagement vs cost data yet</div>`;
    return;
  }

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
    { key: 'modName',    label: 'mod',          l: true },
    { key: 'usageShare', label: 'usage share',  title: 'share of all engagement attributed to this mod' },
    { key: 'cpuShare',   label: 'cpu share',    title: 'share of all measured cpu attributed to this mod' },
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

  root.innerHTML = `
    <div class='sc-h'>engagement vs cost — ${ec.dots.length} mods</div>
    <div class='sc-scroll'>
      <table class='dtable'>
        <thead>${headRow}</thead>
        <tbody>${rows}</tbody>
      </table>
    </div>`;
}

// ----- I7 mod interaction matrix -------------------------------------
// Leads with a readable top-pairs table (the primary signal), then keeps
// the full Pearson matrix as a secondary .rheat grid. Both carry the
// +/- semantic colouring: positive r tints green, negative tints red,
// magnitude drives intensity. Replaces the bespoke grid with the shared
// rheat vocabulary.
function renderModInteractionMatrix() {
  const root = document.getElementById('ins-matrix');
  if (!root) return;
  const mi = lastModInteraction;
  if (!mi || !mi.worldLoaded || !mi.modIds || mi.modIds.length === 0) {
    root.innerHTML = `<div class='mx-h'>mod-pair cost correlation</div>
      <div class='mx-empty'>no mod interaction matrix yet (needs ≥2 active mods over time)</div>`;
    return;
  }
  const ids = mi.modIds;
  const names = mi.modNames || [];
  const matrix = mi.matrixRowMajor || [];
  const N = ids.length;
  if (N < 2 || matrix.length < N * N) {
    root.innerHTML = `<div class='mx-h'>mod-pair cost correlation</div>
      <div class='mx-empty'>matrix not ready yet</div>`;
    return;
  }

  // Correlation -> cell background. Positive green, negative red, |r| drives
  // intensity. Kept distinct from heatFill() because that helper is
  // single-hue (accent) and cannot encode sign.
  function corrColor(r) {
    if (!isFinite(r)) return 'var(--surface)';
    const a = Math.min(1, Math.abs(r));
    if (r >= 0) return `rgba(79,157,106,${(0.06 + a * 0.7).toFixed(3)})`;   // --good family, positive
    return        `rgba(185,78,88,${(0.06 + a * 0.7).toFixed(3)})`;          // --danger family, negative
  }
  function corrText(r) {
    if (!isFinite(r)) return 'var(--dim)';
    return Math.abs(r) > 0.45 ? 'var(--text-bright)' : 'var(--muted)';
  }

  let html = `<div class='mx-h'>mod-pair cost correlation — ${N} mods (Pearson r)</div>`;

  // --- Primary: top coupled pairs table ---
  if (mi.topCoupled && mi.topCoupled.length > 0) {
    const pairs = mi.topCoupled.slice(0, 12);
    const maxAbs = Math.max(1e-6, ...pairs.map(p => Math.abs(p.pearson || 0)));
    const rows = pairs.map((p, i) => {
      const r = p.pearson || 0;
      const sign = r >= 0 ? 'pos' : 'neg';
      return `<tr title='${escapeHtml(p.modNameA + ' × ' + p.modNameB + ' — r = ' + r.toFixed(3) + ' (n=' + fmtInt(p.samplesUsed) + ')')}'>
        <td class='dim'>${i + 1}</td>
        <td class='l'>${escapeHtml(p.modNameA)}</td>
        <td class='l'>${escapeHtml(p.modNameB)}</td>
        <td class='r-${sign}'>${r.toFixed(3)}</td>
        <td class='l mx-cell'>${cellBar(Math.abs(r) / maxAbs, r >= 0 ? 'var(--good)' : 'var(--danger)')}</td>
        <td>${fmtInt(p.samplesUsed)}</td>
      </tr>`;
    }).join('');
    html += `<div class='mx-pairs'>
      <table class='dtable'>
        <thead><tr><th class='dim'>#</th><th class='l'>mod A</th><th class='l'>mod B</th><th>r</th><th class='l'>magnitude</th><th>samples</th></tr></thead>
        <tbody>${rows}</tbody>
      </table>
    </div>`;
  }

  // --- Secondary: full correlation heatmap (rheat) ---
  const labelColW = 130, cellW = 30;
  const styleGrid = `grid-template-columns: ${labelColW}px repeat(${N}, ${cellW}px);`;
  let grid = `<div class='rheat mx-rheat' style='${styleGrid}'>`;
  grid += `<div class='rh-corner'></div>`;
  for (let j = 0; j < N; j++) {
    grid += `<div class='rh-col'>${escapeHtml(names[j] || ('mod:' + ids[j]))}</div>`;
  }
  for (let i = 0; i < N; i++) {
    grid += `<div class='rh-row' title='${escapeHtml(names[i] || ('mod:' + ids[i]))}'>${escapeHtml(names[i] || ('mod:' + ids[i]))}</div>`;
    for (let j = 0; j < N; j++) {
      const r = matrix[i * N + j];
      const isfin = isFinite(r);
      const bg = corrColor(r);
      const fg = corrText(r);
      const zero = (!isfin || Math.abs(r) < 1e-9) ? ' zero' : '';
      const txt = isfin ? (i === j ? '·' : r.toFixed(2).replace('0.', '.').replace('-0.', '-.')) : '';
      const title = isfin
        ? `${escapeHtml(names[i] || '?')} × ${escapeHtml(names[j] || '?')} — r = ${r.toFixed(3)}`
        : 'no samples';
      grid += `<div class='rh-cell${zero}' style='background:${bg};color:${fg}' title='${escapeHtml(title)}'>${txt}</div>`;
    }
  }
  grid += `</div>`;

  html += `<div class='mx-grid-h'>full correlation matrix</div>
    <div class='mx-scroll'>${grid}</div>`;

  // Legend for the sign/magnitude colouring.
  html += `<div class='mx-legend'>
    <span class='swatch' style='background:rgba(185,78,88,0.7)'></span><span>−1</span>
    <span class='swatch' style='background:rgba(185,78,88,0.25)'></span><span>−0.3</span>
    <span class='swatch' style='background:var(--surface)'></span><span>0</span>
    <span class='swatch' style='background:rgba(79,157,106,0.25)'></span><span>+0.3</span>
    <span class='swatch' style='background:rgba(79,157,106,0.7)'></span><span>+1</span>
  </div>`;

  root.innerHTML = html;
}
";
}
