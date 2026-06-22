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
// stat lines) instead of bespoke metaphor charts.
// Surfaces: KPI ring strip, dormant-content table (I2), mod card list
// with a composition split bar + a tabular detail pane (I1+I3+I4), a
// cross-cutting signal-class section list (I5), an engagement-vs-cost
// table (I6), and a mod-pair correlation table (I7).
//
// Poll-stable scroll: every scrollable surface (dormant table, observatory
// card list, detail pane, engagement table, correlation table) keeps a
// stable inner scroll container across polls; its header stays static and
// only the inner content is replaced via setHTML(), so scroll survives a
// 3s poll instead of snapping to the top.
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

// Build (once) the static header + stable scroll container inside a
// section root, returning the scroll element. The scroll element keeps a
// stable identity across polls so setHTML() can preserve its scrollTop —
// only its inner content is replaced each tick, never the wrapper itself.
// hClass is the header element class, sClass the scroll container class.
function ensureScrollSection(root, hClass, sClass) {
  let scroll = root.querySelector('.' + sClass);
  if (!scroll) {
    root.innerHTML = `<div class='${hClass}'></div><div class='${sClass}'></div>`;
    scroll = root.querySelector('.' + sClass);
  }
  return { head: root.querySelector('.' + hClass), scroll };
}

// ----- I2 dormant surface --------------------------------------------
// Sortable perf-tinted table. Each row is a mod with a usage split bar
// (engaged green vs unused surface) carrying the % text, a used/roster
// count, and the dominant unused category as a chip. The caption carries
// the two headline totals. Pure measurement — no judgement on any row.
// The .dor-scroll container is stable across polls (setHTML preserves its
// scroll position); only the table inside it is rebuilt each tick.
function renderDormantSurface() {
  const root = document.getElementById('ins-dormant');
  if (!root) return;
  const { head, scroll } = ensureScrollSection(root, 'dor-h', 'dor-scroll');
  const dor = lastDormant;
  if (!dor || !dor.worldLoaded) {
    head.innerHTML = `<span class='label'>dormant content</span><span>—</span>`;
    setHTML(scroll, '');
    return;
  }
  const entries = (dor.entries || []).slice();
  const headSummary = `${fmtInt(dor.modsWithZeroUsage)} mods at zero usage · ${fmtInt(dor.modsBelowFivePercentUsage)} under 5%`;
  head.innerHTML = `<span class='label'>dormant content</span><span>${headSummary}</span>`;

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

  setHTML(scroll, `
    <table class='dtable'>
      <thead>${headRow}</thead>
      <tbody>${rows}</tbody>
    </table>`);
}

// ----- I1 observatory list -------------------------------------------
// Ranked card list, ordered by cpu share. Each card carries the mod
// name, a cost cellBar, readable usage micro-stats, and a roster
// composition split bar (replacing the unreadable DNA strand) whose
// legend appears in the detail pane. Click selects -> fills detail pane.
function renderObservatoryList() {
  const root = document.getElementById('ins-obs-list');
  if (!root) return;
  // Stable inner scroll container: the .ins-obs-list wrapper persists, the
  // .obs-scroll element keeps its identity across polls so setHTML preserves
  // the card list's scroll position instead of snapping back to the top.
  let scroll = root.querySelector('.obs-scroll');
  if (!scroll) {
    root.innerHTML = `<div class='obs-scroll'></div>`;
    scroll = root.querySelector('.obs-scroll');
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

  // Roster composition split bar: each segment is a category, fraction =
  // that category's count / roster total. Rendered as a secondary signal
  // (thin + quieted via .comp) so it shows the roster mix without competing
  // with the card's primary cpu/cost signals — every card's bar is full
  // width regardless of mod size, so it must not lead. Empty roster ->
  // a muted 'no content'.
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

  setHTML(scroll, cards.map((c, i) => {
    const sel = c.modId === selectedObservatoryModId ? 'selected' : '';
    const costFrac = c.cpuSharePct / maxCpu;
    const micro = `${fmtInt(c.usage.itemsCreated)} items · ${fmtInt(c.usage.npcsSpawned)} npcs · ${fmtInt(c.usage.buffsApplied)} buffs · ${c.cpuSharePct.toFixed(1)}% cpu · ${c.usageSharePct.toFixed(1)}% usage`;
    return `<div class='ins-obs-card ${sel}' data-mod='${c.modId}'>
      <span class='rank'>${i + 1}</span>
      <div class='body'>
        <div class='nm'>${escapeHtml(c.modName)}</div>
        <div class='micro'>${micro}</div>
        <div class='cost'>${cellBar(costFrac, 'var(--cpu)')}</div>
        <div class='comp'>${compositionBar(c.roster)}</div>
      </div>
      <span class='ms'>${fmtMs(c.smoothedMsThisTick)}<span class='u'>ms</span></span>
    </div>`;
  }).join(''));

  scroll.querySelectorAll('.ins-obs-card').forEach(el => {
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
  // Stable inner scroll container: the .ins-detail aside persists, .det-scroll
  // keeps identity across polls so setHTML preserves scroll position while a
  // selected mod's long detail is open. Selecting a new mod replaces content
  // (and naturally scrolls to top via the new shorter/longer body).
  let scroll = root.querySelector('.det-scroll');
  if (!scroll) {
    root.innerHTML = `<div class='det-scroll'></div>`;
    scroll = root.querySelector('.det-scroll');
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
    <div>
      <div class='det-title'>${escapeHtml(card.modName)}</div>
      <div style='font-family:var(--mono);font-size:0.74rem;color:var(--muted)'>roster total ${fmtInt(totalRoster)} entries</div>
    </div>
    <div>
      <h4>roster composition</h4>
      ${legendHtml}
    </div>
    <div class='det-stats'>
      <div class='statline'><span class='k'>cpu share</span><span class='v'>${dash(card.cpuSharePct, v => v.toFixed(2) + '%')}</span></div>
      <div class='statline'><span class='k'>smoothed ms this tick</span><span class='v'>${dash(card.smoothedMsThisTick, v => fmtMs(v) + ' ms')}</span></div>
      <div class='statline'><span class='k'>average ms</span><span class='v'>${dash(card.averageMs, v => fmtMs(v) + ' ms')}</span></div>
      <div class='statline'><span class='k'>usage share</span><span class='v'>${dash(card.usageSharePct, v => v.toFixed(2) + '%')}</span></div>
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
  `);
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
    root.innerHTML = `<div class='cc-h'>cross-cutting signals</div>` +
      emptyState('no cross-cutting signals recorded yet');
    return;
  }
  const groups = cc.groups.filter(g => g.leaders && g.leaders.length > 0);
  if (groups.length === 0) {
    root.innerHTML = `<div class='cc-h'>cross-cutting signals</div>` +
      emptyState('signals recorded but no leaders yet');
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
  // Static header + stable scroll container so a poll preserves scroll position.
  const { head, scroll } = ensureScrollSection(root, 'sc-h', 'sc-scroll');
  const ec = lastEngagementCost;
  if (!ec || !ec.worldLoaded || !ec.dots || ec.dots.length === 0) {
    head.innerHTML = `engagement vs cost`;
    setHTML(scroll, emptyState('no engagement vs cost data yet'));
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

  head.innerHTML = `engagement vs cost — ${ec.dots.length} mods`;
  setHTML(scroll, `
    <table class='dtable'>
      <thead>${headRow}</thead>
      <tbody>${rows}</tbody>
    </table>`);
}

// ----- I7 mod-pair cost correlation ----------------------------------
// Readable top-coupled-pairs table only. The full NxN Pearson grid was
// cut: it read as dead space and was twice misunderstood. The pairs table
// carries the same signal in plain rows (mod A × mod B, r, magnitude bar,
// samples) under a plain-English caption. Positive r tints green, negative
// red; magnitude drives the bar. Descriptive only — no verdict on a pair.
function renderModInteractionMatrix() {
  const root = document.getElementById('ins-matrix');
  if (!root) return;
  // Static header + stable scroll container so a poll preserves scroll position.
  const { head, scroll } = ensureScrollSection(root, 'mx-h', 'mx-scroll');
  const mi = lastModInteraction;
  if (!mi || !mi.worldLoaded || !mi.modIds || mi.modIds.length === 0) {
    head.innerHTML = `mod-pair cost correlation`;
    setHTML(scroll, emptyState('no mod interaction data yet (needs ≥2 active mods over time)'));
    return;
  }
  const N = mi.modIds.length;
  if (N < 2 || !mi.topCoupled || mi.topCoupled.length === 0) {
    head.innerHTML = `mod-pair cost correlation — ${N} mods`;
    setHTML(scroll, emptyState('no coupled pairs ranked yet'));
    return;
  }

  head.innerHTML = `mod-pair cost correlation — ${N} mods (Pearson r)`;

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

  setHTML(scroll, `
    <div class='mx-caption'>mods whose per-tick CPU rises and falls together — a high r means they tend to get busy at the same moments</div>
    <table class='dtable'>
      <thead><tr><th class='dim'>#</th><th class='l'>mod A</th><th class='l'>mod B</th><th>r</th><th class='l'>magnitude</th><th>samples</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>`);
}
";
}
