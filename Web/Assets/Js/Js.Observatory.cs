#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // ====== OBSERVATORY TAB ==============================================
    // The descriptive half of the old Insights tab: per-mod ATTRIBUTION, the
    // measurement layer. Answers 'what is each mod, and what does it cost / earn'
    // — composition, dormant content, the per-mod master-detail, the
    // engagement-vs-cost relationship, and the cost-correlation chord. The
    // interpretive half (the insight FEED — what the engine concludes) lives on
    // the sibling Insights tab. Split because the two are different mental modes
    // and a single page carrying both was becoming unreadably tall.
    //
    // Composed entirely from the shared component + chart library: waffle() /
    // statGrid() up top, row()/rowList() + statLine()/.dtable for the
    // master-detail, scatter() and chord() for the analysis pair. Invariant 3:
    // every string is descriptive measurement, never a verdict.
    private const string JsObservatory = @"
let lastModObservatory = null;
let lastDormant = null;
let lastEngagementCost = null;
let lastModInteraction = null;
let lastModlistHistory = null;

let selectedObservatoryModId = -1;

// Sort state for the dormant table. {key, dir}; dir 1 (asc) / -1 (desc).
let dormantSort = { key: 'usageRatio', dir: 1 };       // least-engaged first

// Observatory list controls (persist across the 3s poll).
let obsSort = 'cpu', obsFilter = '';

// Roster composition categories: [field, label, colour]. Shared by the
// observatory list split bar, its legend, and the detail pane so colour ==
// meaning across all three. A fixed content-TYPE role set on a monochrome
// luminance ramp (brightest->dimmest), orthogonal to the per-mod hues — see the
// long-form rationale that shipped with this ramp: nine categorical hues
// collided near-white at the top, so a fixed grey ramp (0.84 -> 0.42) keeps every
// category a distinct grey that never competes with the chrome accent.
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

async function pollObservatory() {
  if (activeTab !== 'observatory') return;
  try {
    const [mo, dr, ec, mi, mh] = await Promise.all([
      fetch('/api/mod-observatory', { cache: 'no-store' }).then(r => r.json()),
      fetch('/api/dormant',         { cache: 'no-store' }).then(r => r.json()),
      fetch('/api/engagement-cost', { cache: 'no-store' }).then(r => r.json()),
      fetch('/api/mod-interaction', { cache: 'no-store' }).then(r => r.json()),
      fetch('/api/modlist-history', { cache: 'no-store' }).then(r => r.json()),
    ]);
    lastModObservatory = mo;
    lastDormant = dr;
    lastEngagementCost = ec;
    lastModInteraction = mi;
    lastModlistHistory = mh;
    renderObservatory();
  } catch (e) { /* swallow — next tick will retry */ }
}
setInterval(pollObservatory, 3000);

function renderObservatory() {
  renderObservatoryComposition();
  renderObservatoryStats();
  renderObservatoryList();
  renderObservatoryDetail();
  renderEngagementScatter();
  renderModCorrelation();
  renderRosterMatrix();
  renderDormantSurface();
}

// ----- modlist composition (waffle) ----------------------------------
// The loaded modlist as a unit grid: one cell per mod, coloured by engagement
// bucket (active >= 5% usage / under 5% / dormant at zero), so composition reads
// as countable area. Descriptive — the buckets are measured usage thresholds.
function renderObservatoryComposition() {
  const root = document.getElementById('obs-composition');
  if (!root) return;
  const obs = lastModObservatory || {};
  const dor = lastDormant || {};
  const loaded = dor.modsLoaded != null ? dor.modsLoaded
    : ((obs.activeCount || 0) + (obs.dormantCount || 0));
  const dormant = dor.modsWithZeroUsage != null ? dor.modsWithZeroUsage : (obs.dormantCount || 0);
  const below5 = dor.modsBelowFivePercentUsage != null ? dor.modsBelowFivePercentUsage : dormant;
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
  // O3 warm gate: usage shares are judgement-toned; a young session frames
  // them as an early read, not a verdict (audit X4 pattern).
  const warmNote = sessionMinutes() < 10 ? ` · early read (${Math.max(1, Math.round(sessionMinutes()))}m of data)` : '';
  const sub = `${fmtInt(loaded)} mods · ${(100 * active / denom).toFixed(0)}% active · ${(100 * dormant / denom).toFixed(0)}% dormant${warmNote}`;

  let body = root.querySelector('#wf-body');
  if (!body) {
    root.innerHTML = panel({ title: 'modlist composition', sub, body: `<div id='wf-body'></div>` });
    body = root.querySelector('#wf-body');
  }
  const subEl = root.querySelector('.panel-sub');
  if (subEl) subEl.textContent = sub;
  setHTML(body, `<div class='wf-wrap'>${grid}</div><div class='wf-key'>${key}</div>`);
}

// ----- headline stat tiles -------------------------------------------
// The four numbers worth reading at a glance, as shared stat tiles: loaded
// count, active share, dormant share, and the single heaviest mod by cpu.
function renderObservatoryStats() {
  const root = document.getElementById('obs-stats');
  if (!root) return;
  const obs = lastModObservatory || {};
  const dor = lastDormant || {};
  const loaded = dor.modsLoaded != null ? dor.modsLoaded : ((obs.activeCount || 0) + (obs.dormantCount || 0));
  const dormant = dor.modsWithZeroUsage != null ? dor.modsWithZeroUsage : (obs.dormantCount || 0);
  const below5 = dor.modsBelowFivePercentUsage != null ? dor.modsBelowFivePercentUsage : dormant;
  const active = Math.max(0, loaded - below5);
  const denom = Math.max(1, loaded);

  // Heaviest mod by cpu share, from the observatory cards.
  let top = null;
  if (obs.cards && obs.cards.length) {
    for (const c of obs.cards) if (!top || c.cpuSharePct > top.cpuSharePct) top = c;
  }

  const tiles = statGrid([
    statTile({ k: 'mods loaded', v: fmtInt(loaded), big: true }),
    statTile({ k: 'active', v: (100 * active / denom).toFixed(0) + '%', vClass: 'good', sub: fmtInt(active) + ' mods', frac: active / denom, color: 'var(--good-bar)' }),
    statTile({ k: 'dormant', v: (100 * dormant / denom).toFixed(0) + '%', sub: fmtInt(dormant) + ' at zero', frac: dormant / denom, color: 'var(--muted)' }),
    statTile({ k: 'heaviest mod', v: top ? (top.cpuSharePct * 100).toFixed(1) + '%' : '—', sub: top ? truncate(top.modName, 16) : 'no data', vClass: 'accent' }),
  ], { cols: 'repeat(2, minmax(0, 1fr))' });

  let body = root.querySelector('#obs-stats-body');
  if (!body) {
    root.innerHTML = panel({ title: 'at a glance', body: `<div id='obs-stats-body'></div>` });
    body = root.querySelector('#obs-stats-body');
  }
  setHTML(body, tiles);
}

// ----- per-mod observatory list (master) -----------------------------
// Ranked card list (row()/rowList()), ordered by cpu share. Each card: rank,
// name + usage micro-stats + a cpu cost cellBar + a roster composition split
// bar, and the smoothed ms. Click selects and fills the detail pane.
function renderObservatoryList() {
  const root = document.getElementById('obs-list');
  if (!root) return;

  let scroll = root.querySelector('#obs-scroll');
  if (!scroll) {
    root.innerHTML = panel({
      title: 'per-mod observatory',
      actions: `<input class='filter-input' id='obs-search' placeholder='search mods…' style='width:9rem' value='${escapeHtml(obsFilter)}'>` +
        segmented({ id: 'obs-sort', attr: 'data-osort', active: obsSort, options: [
          { value: 'cpu', label: 'cpu' }, { value: 'usage', label: 'usage' }, { value: 'name', label: 'name' }] }),
      body: scrollRegion('obs-scroll', '', { maxH: '34rem' }),
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
    renderIfChanged('obsList', 'empty', () => setHTML(scroll, emptyState('no per-mod observatory data yet')));
    return;
  }
  const maxCpu = Math.max(0.0001, ...obs.cards.map(c => c.cpuSharePct));
  let cards = obs.cards.slice();
  if (obsFilter) { const q = obsFilter.toLowerCase(); cards = cards.filter(c => c.modName.toLowerCase().includes(q)); }
  cards.sort((a, b) =>
    obsSort === 'name' ? a.modName.localeCompare(b.modName) :
    obsSort === 'usage' ? (b.usageSharePct - a.usageSharePct) || a.modName.localeCompare(b.modName) :
    (b.cpuSharePct - a.cpuSharePct) || a.modName.localeCompare(b.modName));
  if (cards.length === 0) {
    renderIfChanged('obsList', 'nomatch:' + obsFilter, () => setHTML(scroll, emptyState('no mods match the search')));
    return;
  }

  if (selectedObservatoryModId < 0 || !cards.some(c => c.modId === selectedObservatoryModId)) {
    selectedObservatoryModId = cards[0].modId;
  }

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

  const obsSig = obsFilter + '|' + obsSort + '|' + selectedObservatoryModId + '|' + maxCpu.toFixed(4) + '|' +
    cards.map(c => c.modId + ':' + (c.cpuSharePct || 0).toFixed(4) + ':' + (c.usageSharePct || 0).toFixed(4) + ':' + (c.smoothedMsThisTick || 0).toFixed(3)).join(',');
  if (_renderSig['obsList'] === obsSig) return;
  _renderSig['obsList'] = obsSig;

  const _obsModsById = new Map(((lastMods && lastMods.mods) || []).map(m => [m.id, m]));
  const cols = '2.2em minmax(0,1fr) 5em';
  setHTML(scroll, rowList(cards.map((c, i) => {
    const costFrac = c.cpuSharePct / maxCpu;
    // cpuSharePct / usageSharePct are FRACTIONS (0..1), ×100 to read as percent.
    // O1: these are USED counts (items created, npcs spawned…), not roster
    // sizes — say so, and drop the noise until anything has been used.
    // O2: the usage share is a share of the usage OBSERVED SO FAR; while the
    // session warms it reads as an early number, not a verdict.
    const anyUse = (c.usage.itemsCreated + c.usage.npcsSpawned + c.usage.buffsApplied) > 0;
    const useBits = anyUse
      ? `${fmtInt(c.usage.itemsCreated)} items used · ${fmtInt(c.usage.npcsSpawned)} npcs spawned · ${fmtInt(c.usage.buffsApplied)} buffs applied · `
      : '';
    const warmTag = sessionMinutes() < 10 ? ' (early)' : '';
    const micro = `${useBits}${(c.cpuSharePct * 100).toFixed(1)}% cpu · ${(c.usageSharePct * 100).toFixed(1)}% usage${warmTag}`;
    // S01 split bar: /api/mods carries per-mod drawMs; when the phase lanes
    // are live the cost bar splits update (bright) vs draw (dimmer) so a
    // draw-bound mod is visible in the ranking itself.
    let costHtml = cellBar(costFrac, 'var(--cpu)');
    const lm = _obsModsById && _obsModsById.get(c.modId);
    if (lm && lastMods && lastMods.phaseSplit && lm.cpuMs > 0) {
      const drawFrac = Math.max(0, Math.min(1, lm.drawMs / lm.cpuMs));
      costHtml = splitBar([
        { frac: (1 - drawFrac) * costFrac, color: 'var(--cpu)', label: 'update', value: fmtMs(lm.cpuMs - lm.drawMs) + ' ms' },
        { frac: drawFrac * costFrac, color: 'var(--muted)', label: 'draw', value: fmtMs(lm.drawMs) + ' ms' },
        { frac: 1 - costFrac, color: 'transparent' },
      ], { thin: true });
    }
    const bodyCell = `<div class='obs-body'>` +
      `<div class='nm'>${escapeHtml(c.modName)}</div>` +
      `<div class='obs-micro'>${micro}</div>` +
      `<div class='obs-cost'>${costHtml}</div>` +
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

// ----- mod detail (detail) -------------------------------------------
// The master-detail right side: roster composition legend (the key to the
// list's split bars), headline stats, the roster-vs-usage table, biome
// attendance, and top loadout influence. Tabular and readable.
function renderObservatoryDetail() {
  const root = document.getElementById('obs-detail');
  if (!root) return;

  let scroll = root.querySelector('#det-scroll');
  if (!scroll) {
    root.innerHTML = panel({
      title: 'mod detail',
      body: scrollRegion('det-scroll', '', { maxH: '36rem' }),
      pad: 'flush',
    });
    scroll = root.querySelector('#det-scroll');
  }
  const obs = lastModObservatory;
  if (!obs || !obs.cards || obs.cards.length === 0) {
    renderIfChanged('obsDetail', 'empty', () => setHTML(scroll, emptyState('select a mod from the list to see its observatory detail')));
    return;
  }
  const card = obs.cards.find(c => c.modId === selectedObservatoryModId) || obs.cards[0];
  if (!card) {
    renderIfChanged('obsDetail', 'nocard', () => setHTML(scroll, emptyState('no card selected')));
    return;
  }

  const detSig = card.modId + '|' + (card.cpuSharePct || 0).toFixed(4) + ':' + (card.usageSharePct || 0).toFixed(4) +
    ':' + (card.smoothedMsThisTick || 0).toFixed(3) + ':' + (card.averageMs || 0).toFixed(3) +
    '|' + (card.biomeAttendance ? card.biomeAttendance.length : 0) + ':' + (card.topLoadoutItems ? card.topLoadoutItems.length : 0);
  if (_renderSig['obsDetail'] === detSig) return;
  _renderSig['obsDetail'] = detSig;

  const r = card.roster, u = card.usage;
  const totalRoster = ROSTER_CATS.reduce((sum, [f]) => sum + (r[f] || 0), 0);

  const legendSegs = ROSTER_CATS
    .map(([f, label, color]) => ({ frac: r[f] || 0, color, label, value: fmtInt(r[f] || 0) }))
    .filter(s => s.frac > 0);
  const legendHtml = legendSegs.length > 0
    ? splitLegend(legendSegs)
    : `<div class='comp-empty'>no content registered (library-shaped mod)</div>`;

  const statsHtml =
    statLine('cpu share', dash(card.cpuSharePct, v => (v * 100).toFixed(2) + '%')) +
    statLine('smoothed ms this tick', dash(card.smoothedMsThisTick, v => fmtMs(v) + ' ms')) +
    statLine('average ms', dash(card.averageMs, v => fmtMs(v) + ' ms')) +
    statLine('usage share', dash(card.usageSharePct, v => (v * 100).toFixed(2) + '%'));

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
    // X5 null-vs-zero convention: '—' means NO USAGE COUNTER EXISTS for the
    // category (not instrumented), a real 0 means instrumented-and-unused.
    // The tooltip carries the distinction so a mixed column reads as designed
    // rather than as broken data.
    const usedCell = used == null
      ? `<td class='dim' title='no usage counter exists for this category yet'>—</td>`
      : `<td>${fmtInt(used)}</td>`;
    return `<tr><td class='l'>${k}</td><td>${fmtInt(ros)}</td>${usedCell}</tr>`;
  }).join('');

  const biome = (card.biomeAttendance || []).slice(0, 12);
  const biomeHtml = biome.length === 0
    ? emptyState('no biome attendance recorded')
    : dtable(`<tr><th class='l'>biome</th><th>ticks</th><th>share</th></tr>`,
        biome.map(b => `<tr>
          <td class='l'>${escapeHtml(b.biomeName)}</td>
          <td>${dash(b.ticks, fmtInt)}</td>
          <td>${dash(b.sharePct, v => v.toFixed(1) + '%')}</td>
        </tr>`).join(''));

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

// ----- engagement vs cost (bubble scatter) ---------------------------
// usage share (x) vs cpu share (y), bubble area = roster size, with a y=x
// balance reference. Above the line = cost-heavy; below = usage-heavy; on it =
// balanced. Colour encodes the tilt; clicking a bubble opens the mod card.
function renderEngagementScatter() {
  const root = document.getElementById('obs-scatter');
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
    renderIfChanged('obsScatter', 'empty', () => setHTML(body, emptyState('no engagement vs cost data yet')));
    return;
  }
  if (subEl) subEl.textContent = `${ec.dots.length} mods · bubble = roster size`;

  const scatSig = ec.dots.map(d => d.modId + ':' + (d.usageShare || 0).toFixed(4) + ':' + (d.cpuShare || 0).toFixed(4) + ':' + (d.rosterSize || 0)).join(',');
  if (_renderSig['obsScatter'] === scatSig) return;
  _renderSig['obsScatter'] = scatSig;

  const COST = 'var(--spike)', USE = 'var(--good-bar)', BAL = 'var(--muted)';
  function tiltColor(d) {
    const sum = (d.usageShare || 0) + (d.cpuShare || 0);
    if (sum < 1e-9) return BAL;
    const t = ((d.cpuShare || 0) - (d.usageShare || 0)) / sum;
    return t > 0.15 ? COST : t < -0.15 ? USE : BAL;
  }

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

  body.querySelectorAll('.sc-dot.hit').forEach(el => {
    el.addEventListener('click', () => { if (typeof openModCard === 'function') openModCard(parseInt(el.dataset.mod, 10)); });
  });
}

// ----- mod-pair cost correlation (chord + ranked table) --------------
// A chord diagram of the strongest coupled pairs: each mod is an arc, each pair
// a ribbon whose width is |r| and whose colour is its sign (green together / red
// trade-off). The chord shows the coupling STRUCTURE at a glance; the compact
// table beneath carries the exact r. Descriptive only — no verdict on a pair.
function renderModCorrelation() {
  const root = document.getElementById('obs-corr');
  if (!root) return;
  const mi = lastModInteraction;

  let body = root.querySelector('#corr-body');
  if (!body) {
    root.innerHTML = panel({
      title: 'mod-pair cost correlation', sub: '—',
      body: `<div class='ins-caption'>mods whose per-tick CPU rises and falls together — a thick ribbon means they get busy at the same moments (green) or trade off (red)</div>` +
        `<div id='corr-body'></div>`,
      pad: 'tight',
    });
    body = root.querySelector('#corr-body');
  }
  const subEl = root.querySelector('.panel-sub');

  if (!mi || !mi.worldLoaded || !mi.modIds || mi.modIds.length === 0) {
    if (subEl) subEl.textContent = '—';
    renderIfChanged('obsCorr', 'none', () => setHTML(body, emptyState('no mod interaction data yet (needs ≥2 active mods over time)')));
    return;
  }
  const N = mi.modIds.length;
  if (N < 2 || !mi.topCoupled || mi.topCoupled.length === 0) {
    if (subEl) subEl.textContent = `${N} mods`;
    renderIfChanged('obsCorr', 'nopairs:' + N, () => setHTML(body, emptyState('no coupled pairs ranked yet')));
    return;
  }

  const pairs = mi.topCoupled.slice(0, 10);
  if (subEl) subEl.textContent = `${N} mods · ${pairs.length} strongest pairs`;

  const corrSig = pairs.map(p => p.modNameA + '×' + p.modNameB + ':' + (p.pearson || 0).toFixed(4)).join(',');
  if (_renderSig['obsCorr'] === corrSig) return;
  _renderSig['obsCorr'] = corrSig;

  // Chord nodes = the distinct mods across the top pairs; links = the pairs.
  const idx = new Map();
  const nodes = [];
  function nodeFor(name, modId) {
    if (!idx.has(name)) { idx.set(name, nodes.length); nodes.push({ label: name, color: modColor(modId) }); }
    return idx.get(name);
  }
  const links = pairs.map(p => {
    const a = nodeFor(p.modNameA, p.modIdA != null ? p.modIdA : 0);
    const b = nodeFor(p.modNameB, p.modIdB != null ? p.modIdB : 1);
    const r = p.pearson || 0;
    return {
      a, b, value: Math.max(0.01, Math.abs(r)),
      color: r >= 0 ? 'var(--good)' : 'var(--danger)',
      tip: p.modNameA + ' × ' + p.modNameB + ' · r = ' + r.toFixed(3) + ' (n=' + fmtInt(p.samplesUsed) + ')',
    };
  });
  const chordSvg = chord({ nodes, links, w: 300 });

  const rows = pairs.map((p, i) => {
    const r = p.pearson || 0, tint = r >= 0 ? 'var(--good)' : 'var(--danger)';
    return `<tr title='${escapeHtml(p.modNameA + ' × ' + p.modNameB + ' — r = ' + r.toFixed(3) + ' (n=' + fmtInt(p.samplesUsed) + ')')}'>
      <td class='dim'>${i + 1}</td>
      <td class='l'>${escapeHtml(p.modNameA)}</td>
      <td class='l'>${escapeHtml(p.modNameB)}</td>
      <td style='color:${tint}'>${r.toFixed(3)}</td>
    </tr>`;
  }).join('');

  setHTML(body, `<div class='corr-chord'>${chordSvg}</div>` +
    dtable(`<tr><th class='dim'>#</th><th class='l'>mod A</th><th class='l'>mod B</th><th>r</th></tr>`, rows));
}

// ----- roster evolution matrix ---------------------------------------
// Every distinct modlist the player has run is one column (oldest -> newest); the
// union of every mod ever seen is the rows. A cell carries the version the mod ran
// at in that roster: a monochrome fill = present, an empty cell = absent, and a
// single accent = the version changed from the prior roster the mod appeared in.
// The presence blocks read adds/removes at a glance; the accent reads version
// bumps. Pure measurement of what was loaded — no verdict on any roster or mod.
function renderRosterMatrix() {
  const root = document.getElementById('obs-roster');
  if (!root) return;

  let scroll = root.querySelector('#roster-scroll');
  if (!scroll) {
    root.innerHTML = panel({
      title: 'roster evolution', sub: '—',
      body: `<div class='ins-caption'>every distinct modlist you have run, oldest to newest — a filled cell is the version that roster ran, an accent marks a version that changed from the prior roster, an empty cell means the mod was not in that roster</div>` +
        scrollRegion('roster-scroll', '', { maxH: '30rem' }),
      pad: 'flush',
    });
    scroll = root.querySelector('#roster-scroll');
  }
  const subEl = root.querySelector('.panel-sub');
  const mh = lastModlistHistory;

  if (!mh || !mh.available || !mh.modlists || mh.modlists.length === 0) {
    if (subEl) subEl.textContent = '—';
    renderIfChanged('obsRoster', 'empty', () => setHTML(scroll, emptyState('no roster history persisted yet (needs ≥1 ended session)')));
    return;
  }

  const lists = mh.modlists, mods = mh.mods || [], versions = mh.versions || [];
  if (subEl) subEl.textContent = `${fmtInt(mods.length)} mods · ${fmtInt(lists.length)} rosters`;

  // Rebuild only when the persisted matrix actually changes (the 3s poll
  // otherwise churns the whole grid every tick).
  const sig = lists.map(l => l.fingerprint + ':' + l.sessionCount).join(',') + '|' +
    mods.map((m, i) => m.name + ':' + (versions[i] || []).join('/')).join(',');
  if (_renderSig['obsRoster'] === sig) return;
  _renderSig['obsRoster'] = sig;

  // Column headers carry the timeline cue without colour: a stack index, the
  // first-seen date, and the short fingerprint; the title= holds the full weight.
  const head = lists.map((l, c) => {
    const d = l.firstSeenUtc ? new Date(l.firstSeenUtc) : null;
    const date = d && !isNaN(d) ? d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' }) : '—';
    const tip = `roster ${c + 1} · ${l.shortFp || ''} · ${fmtInt(l.modCount)} mods · ${fmtInt(l.sessionCount)} sessions · first seen ${date}`;
    return `<div class='rmx-col' title='${escapeHtml(tip)}'>` +
      `<span class='rmx-cn'>stack ${c + 1}</span>` +
      `<span class='rmx-cd'>${escapeHtml(date)}</span>` +
      `<span class='rmx-cf'>${escapeHtml(l.shortFp || '')}</span></div>`;
  }).join('');

  const PRESENT = heatFill(0.55);
  const body = mods.map((m, r) => {
    const vrow = versions[r] || [];
    let prev = '';   // last non-empty version walking left -> right (held across gaps)
    const cells = lists.map((l, c) => {
      const v = vrow[c] || '';
      if (!v) return `<div class='rmx-cell absent' title='${escapeHtml(m.name + ' · not in stack ' + (c + 1))}'></div>`;
      const changed = prev !== '' && prev !== v;
      const tip = changed
        ? m.name + ' · stack ' + (c + 1) + ' · v' + prev + ' → v' + v
        : m.name + ' · stack ' + (c + 1) + ' · v' + v;
      prev = v;
      return `<div class='rmx-cell present${changed ? ' changed' : ''}' style='background:${PRESENT}' title='${escapeHtml(tip)}'>${escapeHtml(v)}</div>`;
    }).join('');
    const nameTip = m.name + ' · in ' + fmtInt(m.presentCount) + ' of ' + fmtInt(lists.length) + ' rosters';
    return `<div class='rmx-name' title='${escapeHtml(nameTip)}'>${escapeHtml(m.name)}</div>${cells}`;
  }).join('');

  const cols = `minmax(7rem, 12rem) repeat(${lists.length}, minmax(3.4rem, 1fr))`;
  const key = legend([
    { color: PRESENT, label: 'present (version shown)' },
    { color: 'var(--accent)', label: 'version changed' },
    { color: 'var(--surface)', label: 'absent' },
  ], { inline: true });

  setHTML(scroll, `<div class='rmx' style='grid-template-columns:${cols}'>` +
    `<div class='rmx-corner'>mod</div>${head}${body}</div>` +
    `<div class='rmx-key'>${key}</div>`);
}

// ----- dormant content (reference table) -----------------------------
// Sortable .dtable: each row is a mod with a usage-intensity split bar (engaged
// vs headroom), a tick-credit active-use count, its roster size, and the
// dominant unused category. Pure measurement — no judgement on any row.
function renderDormantSurface() {
  const root = document.getElementById('obs-dormant');
  if (!root) return;
  const dor = lastDormant;

  let scroll = root.querySelector('#dormant-scroll');
  if (!scroll) {
    root.innerHTML = panel({
      title: 'dormant content', sub: '—',
      body: scrollRegion('dormant-scroll', '', { maxH: '260px' }),
      pad: 'flush',
    });
    scroll = root.querySelector('#dormant-scroll');
  }
  const subEl = root.querySelector('.panel-sub');

  if (!dor || !dor.worldLoaded) {
    if (subEl) subEl.textContent = '—';
    renderIfChanged('obsDormant', 'unloaded', () => setHTML(scroll, ''));
    return;
  }
  if (subEl) subEl.textContent = `${fmtInt(dor.modsWithZeroUsage)} mods at zero usage · ${fmtInt(dor.modsBelowFivePercentUsage)} under 5%`;

  const entries = (dor.entries || []).slice();
  if (entries.length === 0) {
    renderIfChanged('obsDormant', 'empty', () => setHTML(scroll, emptyState('no dormant entries recorded this session')));
    return;
  }

  const obsDormantSig = dormantSort.key + dormantSort.dir + '|' +
    entries.map(e => (e.modName || '') + ':' + (e.usageRatio || 0) + ':' + (e.usedCount || 0) + ':' + (e.rosterSize || 0)).join(',');
  if (_renderSig['obsDormant'] === obsDormantSig) return;
  _renderSig['obsDormant'] = obsDormantSig;

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
  const headRow = sortableHead(cols, dormantSort, renderDormantSurface, 'obs-dormant')
    .replace('</tr>', `<th class='l'>dominant unused</th></tr>`);

  const rows = entries.map(e => {
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
      <td class='l'><div class='obs-usage'>${bar}<span class='obs-pct'>${pct}%</span></div></td>
      <td>${fmtInt(e.usedCount)}</td>
      <td>${fmtInt(e.rosterSize)}</td>
      <td class='l'>${cat}</td>
    </tr>`;
  }).join('');

  setHTML(scroll, dtable(headRow, rows));
}
";
}
