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
// The Insights tab is the per-mod observatory: KPI strip, mod card list
// with a per-mod detail pane (I1+I3+I4), a dormant-content surface (I2),
// a cross-cutting signal class strip (I5), an engagement vs cost scatter
// (I6), and a mod-pair correlation matrix (I7).
//
// Invariant 3: every string is descriptive. No 'should remove', no
// 'junk'; only measurements like 'X items used of Y in roster'.
let lastModObservatory = null;
let lastDormant = null;
let lastCrossCutting = null;
let lastEngagementCost = null;
let lastModInteraction = null;

let selectedObservatoryModId = -1;
let dormantOpen = false;

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

// ----- KPI strip ------------------------------------------------------
function renderInsightsKpiStrip() {
  const root = document.getElementById('ins-kpi');
  if (!root) return;
  const obs = lastModObservatory || {};
  const dor = lastDormant || {};
  const active = obs.activeCount != null ? obs.activeCount : 0;
  const dormant = obs.dormantCount != null ? obs.dormantCount : (dor.modsWithZeroUsage || 0);
  const loaded = dor.modsLoaded != null ? dor.modsLoaded : (active + dormant);
  const lowUse = dor.modsBelowFivePercentUsage != null ? dor.modsBelowFivePercentUsage : 0;

  root.innerHTML = `
    <div class='tile'><span class='lbl'>mods loaded</span><span class='val'>${fmtInt(loaded)}</span><span class='sub'>profiled this session</span></div>
    <div class='tile'><span class='lbl'>active</span><span class='val'>${fmtInt(active)}</span><span class='sub'>recorded usage</span></div>
    <div class='tile'><span class='lbl'>dormant</span><span class='val'>${fmtInt(dormant)}</span><span class='sub'>zero usage events</span></div>
    <div class='tile'><span class='lbl'>under 5% usage</span><span class='val'>${fmtInt(lowUse)}</span><span class='sub'>roster engagement</span></div>
  `;
}

// ----- I2 dormant strip ----------------------------------------------
function renderDormantSurface() {
  const root = document.getElementById('ins-dormant');
  if (!root) return;
  const dor = lastDormant;
  if (!dor || !dor.worldLoaded) {
    root.innerHTML = `<div class='dor-h'><span class='label'>dormant content</span><span>—</span></div>`;
    return;
  }
  const entries = dor.entries || [];
  const headSummary = `${fmtInt(dor.modsWithZeroUsage)} mods at zero usage · ${fmtInt(dor.modsBelowFivePercentUsage)} under 5%`;
  let body = '';
  if (entries.length === 0) {
    body = `<div class='dor-row'><span class='nm'>no dormant entries recorded this session</span></div>`;
  } else {
    // Show every dormant entry sorted ascending by usage ratio.
    const sorted = entries.slice().sort((a,b) => a.usageRatio - b.usageRatio);
    body = sorted.map(e => {
      const pct = (e.usageRatio * 100).toFixed(1);
      return `<div class='dor-row'>
        <span class='nm'>${escapeHtml(e.modName)}</span>
        <span class='v'>${fmtInt(e.usedCount)}/${fmtInt(e.rosterSize)} used</span>
        <span class='v'>${pct}% engaged</span>
        <span class='v'>${escapeHtml(e.dominantUnusedCategory || '—')}</span>
      </div>`;
    }).join('');
  }
  root.classList.toggle('open', dormantOpen);
  root.innerHTML = `
    <div class='dor-h' data-role='toggle'>
      <span class='label'>${dormantOpen ? '▾' : '▸'} dormant content surface</span>
      <span>${headSummary}</span>
    </div>
    <div class='dor-body'>${body}</div>
  `;
  const head = root.querySelector('[data-role=toggle]');
  if (head) head.addEventListener('click', () => { dormantOpen = !dormantOpen; renderDormantSurface(); });
}

// ----- I1 observatory list -------------------------------------------
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

  root.innerHTML = cards.map((c, i) => {
    const sel = c.modId === selectedObservatoryModId ? 'selected' : '';
    const barW = ((c.cpuSharePct / maxCpu) * 100).toFixed(1);
    const micro = `${fmtInt(c.usage.itemsCreated)} items · ${fmtInt(c.usage.npcsSpawned)} npcs · ${fmtInt(c.usage.buffsApplied)} buffs`;
    return `<div class='ins-obs-card ${sel}' data-mod='${c.modId}'>
      <span class='rank'>${i + 1}</span>
      <div class='body'>
        <div class='nm'>${escapeHtml(c.modName)}</div>
        <div class='micro'>${micro} · ${c.cpuSharePct.toFixed(1)}% cpu · ${c.usageSharePct.toFixed(1)}% usage share</div>
        <div class='bar'><span style='width:${barW}%'></span></div>
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

  // Roster table
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
    const usedCell = used == null ? `<td class='muted'>—</td>` : `<td class='num'>${fmtInt(used)}</td>`;
    return `<tr><td>${k}</td><td class='num'>${fmtInt(ros)}</td>${usedCell}</tr>`;
  }).join('');

  // I3 biome attendance
  const biome = (card.biomeAttendance || []).slice(0, 12);
  const biomeHtml = biome.length === 0
    ? `<div style='color:var(--dim);font-size:0.78rem'>no biome attendance recorded</div>`
    : `<table class='det-table'>
        <thead><tr><th>biome</th><th class='num'>ticks</th><th class='num'>share</th></tr></thead>
        <tbody>${biome.map(b => `<tr>
          <td>${escapeHtml(b.biomeName)}</td>
          <td class='num'>${fmtInt(b.ticks)}</td>
          <td class='num'>${b.sharePct.toFixed(1)}%</td>
        </tr>`).join('')}</tbody></table>`;

  // I4 loadout influence
  const li = (card.topLoadoutItems || []).slice(0, 10);
  const liHtml = li.length === 0
    ? `<div style='color:var(--dim);font-size:0.78rem'>no loadout influence recorded</div>`
    : `<table class='det-table'>
        <thead><tr><th>item</th><th>slot</th><th class='num'>ticks equipped</th></tr></thead>
        <tbody>${li.map(it => `<tr>
          <td>${escapeHtml(it.itemName)}</td>
          <td class='muted'>${escapeHtml(it.slotKind || '')}</td>
          <td class='num'>${fmtInt(it.equippedTicks)}</td>
        </tr>`).join('')}</tbody></table>`;

  root.innerHTML = `
    <div>
      <div class='det-title'>${escapeHtml(card.modName)}</div>
      <div style='font-family:var(--mono);font-size:0.74rem;color:var(--muted)'>roster total ${fmtInt(totalRoster)} entries</div>
    </div>
    <div class='det-stat-grid'>
      <span class='lbl'>cpu share</span><span class='val'>${card.cpuSharePct.toFixed(2)}%</span>
      <span class='lbl'>smoothed ms this tick</span><span class='val'>${fmtMs(card.smoothedMsThisTick)} ms</span>
      <span class='lbl'>average ms</span><span class='val'>${fmtMs(card.averageMs)} ms</span>
      <span class='lbl'>usage share</span><span class='val'>${card.usageSharePct.toFixed(2)}%</span>
    </div>
    <div>
      <h4>roster vs usage</h4>
      <table class='det-table'>
        <thead><tr><th>category</th><th class='num'>roster</th><th class='num'>used / counted</th></tr></thead>
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
function renderCrossCutting() {
  const root = document.getElementById('ins-cross');
  if (!root) return;
  const cc = lastCrossCutting;
  if (!cc || !cc.worldLoaded || !cc.groups || cc.groups.length === 0) {
    root.innerHTML = `<div class='cc-h'>cross-cutting signals</div>
      <div style='color:var(--dim);font-size:0.82rem;padding:0.4rem 0'>no cross-cutting signals recorded yet</div>`;
    return;
  }
  const rows = cc.groups.map(g => {
    const leaders = (g.leaders || []).slice(0, 6).map(l =>
      `<span class='ldr'><span class='nm'>${escapeHtml(l.modName)}</span><span class='cnt'>${fmtInt(l.appearances)}×</span></span>`
    ).join('');
    return `<div class='cc-row'>
      <span class='cls'>${escapeHtml(g.signalClass)}</span>
      <span class='leaders'>${leaders || '<span style=color:var(--dim)>no leaders</span>'}</span>
    </div>`;
  }).join('');
  root.innerHTML = `<div class='cc-h'>cross-cutting signals</div>${rows}`;
}

// ----- I6 engagement vs cost scatter ---------------------------------
function renderEngagementScatter() {
  const root = document.getElementById('ins-scatter');
  if (!root) return;
  const ec = lastEngagementCost;
  if (!ec || !ec.worldLoaded || !ec.dots || ec.dots.length === 0) {
    root.innerHTML = `<div class='sc-h'>engagement vs cost</div>
      <div style='color:var(--dim);font-size:0.82rem;padding:0.4rem 0'>no engagement vs cost dots yet</div>`;
    return;
  }

  const W = 720, H = 320;
  const padL = 50, padR = 16, padT = 16, padB = 36;
  const plotW = W - padL - padR;
  const plotH = H - padT - padB;
  // Both axes are 0..1 shares. Use linear; clamp.
  function x(v) { return padL + Math.max(0, Math.min(1, v)) * plotW; }
  function y(v) { return padT + (1 - Math.max(0, Math.min(1, v))) * plotH; }

  const maxRoster = Math.max(1, ...ec.dots.map(d => d.rosterSize || 1));
  function r(rosterSize) {
    const t = Math.sqrt((rosterSize || 1) / maxRoster);
    return 2.5 + t * 7.5;
  }

  // Grid + quadrant lines at 0.5
  const gridTicks = [0, 0.25, 0.5, 0.75, 1];
  const gridX = gridTicks.map(t =>
    `<line class='${t === 0.5 ? 'axis' : 'gridline'}' x1='${x(t)}' y1='${padT}' x2='${x(t)}' y2='${padT+plotH}' />`
  ).join('');
  const gridY = gridTicks.map(t =>
    `<line class='${t === 0.5 ? 'axis' : 'gridline'}' x1='${padL}' y1='${y(t)}' x2='${padL+plotW}' y2='${y(t)}' />`
  ).join('');

  const tickX = gridTicks.map(t =>
    `<text class='tick-label' x='${x(t)}' y='${padT+plotH+14}' text-anchor='middle'>${(t*100).toFixed(0)}%</text>`
  ).join('');
  const tickY = gridTicks.map(t =>
    `<text class='tick-label' x='${padL-6}' y='${y(t)+3}' text-anchor='end'>${(t*100).toFixed(0)}%</text>`
  ).join('');

  const quadrantLabels = `
    <text class='quadrant-label' x='${x(0.75)}' y='${y(0.95)}' text-anchor='middle'>high cost · high engagement</text>
    <text class='quadrant-label' x='${x(0.25)}' y='${y(0.95)}' text-anchor='middle'>high cost · low engagement</text>
    <text class='quadrant-label' x='${x(0.75)}' y='${y(0.05)+8}' text-anchor='middle'>low cost · high engagement</text>
    <text class='quadrant-label' x='${x(0.25)}' y='${y(0.05)+8}' text-anchor='middle'>low cost · low engagement</text>
  `;

  // Dots — sort descending by rosterSize so big dots sit behind.
  const dots = ec.dots.slice().sort((a,b) => (b.rosterSize||0) - (a.rosterSize||0));
  const dotsSvg = dots.map(d => {
    const cx = x(d.usageShare), cy = y(d.cpuShare), rad = r(d.rosterSize);
    return `<circle class='dot' cx='${cx.toFixed(1)}' cy='${cy.toFixed(1)}' r='${rad.toFixed(1)}'>
      <title>${escapeHtml(d.modName)} — usage ${(d.usageShare*100).toFixed(1)}% · cpu ${(d.cpuShare*100).toFixed(1)}% · roster ${fmtInt(d.rosterSize)}</title>
    </circle>`;
  }).join('');

  // Label only the top-cpu 6 dots to avoid clutter.
  const labelled = ec.dots.slice().sort((a,b) => b.cpuShare - a.cpuShare).slice(0, 6);
  const labelSvg = labelled.map(d => {
    const cx = x(d.usageShare), cy = y(d.cpuShare);
    return `<text class='dot-label' x='${(cx+r(d.rosterSize)+2).toFixed(1)}' y='${(cy+3).toFixed(1)}'>${escapeHtml(d.modName)}</text>`;
  }).join('');

  root.innerHTML = `
    <div class='sc-h'>engagement vs cost — ${ec.dots.length} mods plotted</div>
    <svg viewBox='0 0 ${W} ${H}' preserveAspectRatio='xMidYMid meet'>
      ${gridY}${gridX}
      <line class='axis' x1='${padL}' y1='${padT+plotH}' x2='${padL+plotW}' y2='${padT+plotH}' />
      <line class='axis' x1='${padL}' y1='${padT}' x2='${padL}' y2='${padT+plotH}' />
      ${tickX}${tickY}
      <text class='axis-label' x='${padL+plotW/2}' y='${H-6}' text-anchor='middle'>usage share →</text>
      <text class='axis-label' transform='rotate(-90 14 ${padT+plotH/2})' x='14' y='${padT+plotH/2}' text-anchor='middle'>cpu share →</text>
      ${quadrantLabels}
      ${dotsSvg}
      ${labelSvg}
    </svg>
  `;
}

// ----- I7 mod interaction matrix -------------------------------------
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

  function cellColor(r) {
    if (!isFinite(r)) return 'var(--panel)';
    const a = Math.min(1, Math.abs(r));
    if (r >= 0) return `rgba(110, 192, 126, ${0.08 + a * 0.85})`;   // green positive
    return        `rgba(202, 100, 100, ${0.08 + a * 0.85})`;        // red negative
  }

  // Build grid: 1 column for row-labels + N data columns; first row is column labels.
  const labelColW = 120;
  const cellW = 16;
  const styleGrid = `grid-template-columns: ${labelColW}px repeat(${N}, ${cellW}px);`;

  let html = `<div class='mx-h'>mod-pair cost correlation — ${N}×${N} (Pearson r)</div>`;
  html += `<div class='mx-grid' style='${styleGrid}'>`;

  // Header row: empty corner + column labels (vertical).
  html += `<div class='lbl-row'></div>`;
  for (let j = 0; j < N; j++) {
    html += `<div class='lbl-col'>${escapeHtml(names[j] || ('mod:' + ids[j]))}</div>`;
  }

  // Data rows.
  for (let i = 0; i < N; i++) {
    html += `<div class='lbl-row'>${escapeHtml(names[i] || ('mod:' + ids[i]))}</div>`;
    for (let j = 0; j < N; j++) {
      const r = matrix[i * N + j];
      const color = cellColor(r);
      const isfin = isFinite(r);
      const title = isfin
        ? `${escapeHtml(names[i] || '?')} × ${escapeHtml(names[j] || '?')} — r = ${r.toFixed(3)}`
        : 'no samples';
      html += `<div class='cell' style='background:${color}' title=""${title}""></div>`;
    }
  }
  html += `</div>`;

  // Legend
  html += `<div class='mx-legend'>
    <span class='swatch' style='background:rgba(202,100,100,0.75)'></span><span>−1</span>
    <span class='swatch' style='background:rgba(202,100,100,0.25)'></span><span>−0.3</span>
    <span class='swatch' style='background:var(--panel)'></span><span>0</span>
    <span class='swatch' style='background:rgba(110,192,126,0.25)'></span><span>+0.3</span>
    <span class='swatch' style='background:rgba(110,192,126,0.75)'></span><span>+1</span>
  </div>`;

  // Top coupled pairs (descriptive list).
  if (mi.topCoupled && mi.topCoupled.length > 0) {
    const pairs = mi.topCoupled.slice(0, 6).map(p =>
      `<div style='font-family:var(--mono);font-size:0.76rem;color:var(--muted);padding:0.15rem 0'>
        <span style='color:var(--text)'>${escapeHtml(p.modNameA)}</span>
        × <span style='color:var(--text)'>${escapeHtml(p.modNameB)}</span>
        — r = ${p.pearson.toFixed(3)} (n=${fmtInt(p.samplesUsed)})
      </div>`
    ).join('');
    html += `<div style='margin-top:0.5rem'>
      <div style='font-family:var(--mono);font-size:0.7rem;color:var(--muted);letter-spacing:0.08em;text-transform:uppercase;margin-bottom:0.2rem'>top coupled pairs</div>
      ${pairs}
    </div>`;
  }

  root.innerHTML = html;
}
";
}
