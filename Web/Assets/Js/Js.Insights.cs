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

// Client-side scatter drift history: modId -> [{u, c, t}, ...] last N samples.
// Lets us draw a short trail behind each dot showing where the mod was at
// recent polls. Pure visual memory — never read back into the API.
const scatterHistory = new Map();
const SCATTER_HISTORY_MAX = 20;  // ~60s at the 3s poll cadence

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

    // Append current scatter positions to the per-mod history ring.
    if (ec && ec.worldLoaded && Array.isArray(ec.dots)) {
      const now = Date.now();
      const seen = new Set();
      for (const d of ec.dots) {
        seen.add(d.modId);
        let h = scatterHistory.get(d.modId);
        if (!h) { h = []; scatterHistory.set(d.modId, h); }
        h.push({ u: d.usageShare, c: d.cpuShare, t: now });
        if (h.length > SCATTER_HISTORY_MAX) h.splice(0, h.length - SCATTER_HISTORY_MAX);
      }
      // Drop history for mods no longer plotted.
      for (const k of Array.from(scatterHistory.keys())) {
        if (!seen.has(k)) scatterHistory.delete(k);
      }
    }
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
// Four mini ring gauges. Each ring's arc-fill encodes the share of the
// loaded modlist that falls in this bucket. Descriptive only — the gauge
// shows the measured fraction; no thresholds, no judgement.
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

  // Roster total + usage total are richer signals than a literal count,
  // but stay computable from the observatory cards we already have.
  let rosterTotal = 0, usageTotal = 0;
  const cards = (lastModObservatory && lastModObservatory.cards) || [];
  for (const c of cards) {
    const r = c.roster, u = c.usage;
    rosterTotal += (r.items + r.npcs + r.buffs + r.projectiles + r.mounts + r.accessories + r.biomes + r.invasions + r.bosses);
    usageTotal  += (u.itemsCreated + u.npcsSpawned + u.buffsApplied + u.invasionsFought + u.bossesFought);
  }

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

// ----- I2 dormant strip ----------------------------------------------
// ""Dust shelf"" — each dormant mod sits as a plaque on a wooden shelf,
// with dust accumulated above it. Dust thickness is proportional to
// (1 - usageRatio): pure measurement, not judgement. Stippled overlay
// is generated via SVG noise (a fractal-noise turbulence filter masked
// to a rectangle above each plaque). Hover surfaces the underlying
// counts so the visual never replaces the numbers.
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
    body = `<div class='dust-empty'>no dormant entries recorded this session</div>`;
  } else {
    const sorted = entries.slice().sort((a,b) => a.usageRatio - b.usageRatio);

    // Layout: each plaque is ~110px wide, dust column above it. Wrap to
    // multiple shelves so this stays readable for big modlists.
    const plaqueW = 118, plaqueH = 36, dustMax = 44, gap = 8;
    const perRow = Math.max(4, Math.floor((root.clientWidth || 640) / (plaqueW + gap)) || 8);
    const rows = [];
    for (let i = 0; i < sorted.length; i += perRow) rows.push(sorted.slice(i, i + perRow));

    body = rows.map(row => {
      const W = row.length * (plaqueW + gap) - gap;
      const H = dustMax + plaqueH + 6;
      const plaques = row.map((e, i) => {
        const xL = i * (plaqueW + gap);
        const dustH = (Math.max(0, Math.min(1, 1 - e.usageRatio)) * dustMax).toFixed(1);
        const dustY = (dustMax - dustH).toFixed(1);
        const pct = (e.usageRatio * 100).toFixed(1);
        const title = `${e.modName} — ${fmtInt(e.usedCount)}/${fmtInt(e.rosterSize)} entries used · ${pct}% engaged · dominant unused: ${e.dominantUnusedCategory || '—'}`;
        const label = escapeHtml(e.modName);
        const short = label.length > 14 ? label.slice(0, 13) + '…' : label;
        return `<g class='dust-plaque' transform='translate(${xL},0)'>
          <title>${escapeHtml(title)}</title>
          <rect class='dust' x='4' y='${dustY}' width='${plaqueW-8}' height='${dustH}'
            fill='url(#dustPattern)' opacity='0.85'></rect>
          <rect class='dust-edge' x='4' y='${dustY}' width='${plaqueW-8}' height='1'></rect>
          <rect class='plaque' x='0' y='${dustMax+2}' width='${plaqueW}' height='${plaqueH}' rx='2'></rect>
          <text class='pl-nm' x='${plaqueW/2}' y='${dustMax+2+16}' text-anchor='middle'>${escapeHtml(short)}</text>
          <text class='pl-sub' x='${plaqueW/2}' y='${dustMax+2+28}' text-anchor='middle'>${pct}% engaged · ${fmtInt(e.usedCount)}/${fmtInt(e.rosterSize)}</text>
        </g>`;
      }).join('');
      return `<svg class='dust-shelf' viewBox='0 0 ${W} ${H}' preserveAspectRatio='xMinYMid meet'>
        <defs>
          <pattern id='dustPattern' patternUnits='userSpaceOnUse' width='4' height='4'>
            <rect width='4' height='4' fill='transparent'/>
            <circle cx='1' cy='1' r='0.5' fill='#8a8170' opacity='0.6'/>
            <circle cx='3' cy='2.5' r='0.4' fill='#a09684' opacity='0.45'/>
            <circle cx='2' cy='3.5' r='0.3' fill='#6e6655' opacity='0.55'/>
          </pattern>
        </defs>
        <line class='shelf-line' x1='0' y1='${dustMax+1.5}' x2='${W}' y2='${dustMax+1.5}'></line>
        ${plaques}
      </svg>`;
    }).join('');
  }

  root.classList.toggle('open', dormantOpen);
  root.innerHTML = `
    <div class='dor-h' data-role='toggle'>
      <span class='label'>${dormantOpen ? '▾' : '▸'} dormant content shelf</span>
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

  // DNA strand microvis: per-card horizontal strand of category ticks.
  // Each tick is a category (items / npcs / buffs / projectiles / mounts /
  // accessories / biomes / invasions / bosses), width proportional to that
  // mod's count in that category divided by its roster total. One glance
  // tells you the shape of the mod: mostly-NPCs, mostly-items, balanced,
  // library-shaped (empty strand = no content registered).
  const dnaCats = [
    ['items',       'i', 'var(--good)'],
    ['npcs',        'n', 'var(--danger)'],
    ['buffs',       'b', 'var(--amber)'],
    ['projectiles', 'p', 'var(--cyan)'],
    ['mounts',      'm', 'var(--orange)'],
    ['accessories', 'a', 'var(--accent)'],
    ['biomes',      'B', 'var(--magenta)'],
    ['invasions',   'v', 'var(--purple)'],
    ['bosses',      's', 'var(--text-bright)'],
  ];
  function dnaStrand(roster) {
    const counts = [
      roster.items, roster.npcs, roster.buffs, roster.projectiles, roster.mounts,
      roster.accessories, roster.biomes, roster.invasions, roster.bosses
    ];
    const tot = counts.reduce((a,b) => a+b, 0);
    if (tot === 0) {
      return `<div class='dna empty' title='no content registered (library-shaped mod)'></div>`;
    }
    let cursor = 0;
    const segs = counts.map((cnt, idx) => {
      if (cnt === 0) return '';
      const w = (cnt / tot) * 100;
      const seg = `<span class='dna-tick' style='left:${cursor}%;width:${w}%;background:${dnaCats[idx][2]}'
        title='${dnaCats[idx][0]}: ${fmtInt(cnt)} of ${fmtInt(tot)} (${(w).toFixed(1)}%)'></span>`;
      cursor += w;
      return seg;
    }).join('');
    return `<div class='dna' title='roster composition: ${fmtInt(tot)} registered entries'>${segs}</div>`;
  }

  root.innerHTML = cards.map((c, i) => {
    const sel = c.modId === selectedObservatoryModId ? 'selected' : '';
    const barW = ((c.cpuSharePct / maxCpu) * 100).toFixed(1);
    const micro = `${fmtInt(c.usage.itemsCreated)} items · ${fmtInt(c.usage.npcsSpawned)} npcs · ${fmtInt(c.usage.buffsApplied)} buffs`;
    return `<div class='ins-obs-card ${sel}' data-mod='${c.modId}'>
      <span class='rank'>${i + 1}</span>
      <div class='body'>
        <div class='nm'>${escapeHtml(c.modName)}</div>
        ${dnaStrand(c.roster)}
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
// Constellation: signal-class nodes anchored on a vertical spine, mod
// nodes scattered to the sides, edges drawn for each (signal × mod)
// appearance pair. Edge stroke-width encodes appearance count. Mods
// that show up across multiple signal classes naturally cluster on the
// spine — visually telegraphs ""co-appearing mods"".
//
// Layout is deterministic (hash-based pseudo-random) so the same modlist
// always renders to the same constellation. No physics, no async layout.
function renderCrossCutting() {
  const root = document.getElementById('ins-cross');
  if (!root) return;
  const cc = lastCrossCutting;
  if (!cc || !cc.worldLoaded || !cc.groups || cc.groups.length === 0) {
    root.innerHTML = `<div class='cc-h'>cross-cutting signals</div>
      <div class='cc-empty'>no cross-cutting signals recorded yet</div>`;
    return;
  }

  // Collect unique mods + per-mod (signal -> count).
  const groups = cc.groups.filter(g => g.leaders && g.leaders.length > 0);
  if (groups.length === 0) {
    root.innerHTML = `<div class='cc-h'>cross-cutting signals</div>
      <div class='cc-empty'>signals recorded but no leaders yet</div>`;
    return;
  }
  const modMap = new Map();  // modId -> { name, total, links: [{sigIdx, count}] }
  groups.forEach((g, gi) => {
    (g.leaders || []).forEach(l => {
      let m = modMap.get(l.modId);
      if (!m) { m = { id: l.modId, name: l.modName, total: 0, links: [] }; modMap.set(l.modId, m); }
      m.total += l.appearances;
      m.links.push({ sigIdx: gi, count: l.appearances });
    });
  });
  const mods = Array.from(modMap.values()).sort((a,b) => b.total - a.total);

  // Hash-based deterministic positioning: same name -> same offset.
  function hash(s) { let h = 5381; for (let i = 0; i < s.length; i++) h = ((h << 5) + h + s.charCodeAt(i)) | 0; return Math.abs(h); }

  const W = 720, H = Math.max(260, 50 + groups.length * 56);
  const spineX = W * 0.40;
  const spinePadT = 40, spinePadB = 20;
  const spineH = H - spinePadT - spinePadB;
  const stepY = groups.length > 1 ? spineH / (groups.length - 1) : 0;

  // Signal node positions on the spine.
  const sigPos = groups.map((g, i) => ({
    x: spineX,
    y: spinePadT + i * stepY,
    label: g.signalClass,
  }));

  // Mod positions: alternating sides, slot by total-appearance rank.
  // Big mods near the spine, small mods further out (more ""stars"" outside).
  const maxTot = Math.max(1, mods[0]?.total || 1);
  const modPos = mods.map((m, i) => {
    const side = (i % 2 === 0) ? 1 : -1;
    const seed = hash(m.name + ':' + m.id);
    const rankFrac = i / Math.max(1, mods.length - 1);
    // Radial distance grows with rank (less-prolific mods sit further out).
    const dist = 80 + rankFrac * 230 + (seed % 28);
    const x = spineX + side * dist;
    // y is jittered around the centroid of the signal classes the mod links to.
    const linkYs = m.links.map(l => sigPos[l.sigIdx].y);
    const centroidY = linkYs.reduce((a,b) => a+b, 0) / linkYs.length;
    const yJit = ((seed >> 4) % 60) - 30;
    const y = Math.max(spinePadT - 10, Math.min(H - spinePadB + 10, centroidY + yJit));
    const r = 2.5 + 4.5 * (m.total / maxTot);
    return { mod: m, x, y, r };
  });

  // Edges: thickness scales with appearance count.
  const maxLink = Math.max(1, ...mods.flatMap(m => m.links.map(l => l.count)));
  const edges = modPos.flatMap(mp => mp.mod.links.map(l => {
    const s = sigPos[l.sigIdx];
    const w = 0.4 + 1.8 * (l.count / maxLink);
    const op = 0.25 + 0.55 * (l.count / maxLink);
    return `<line class='cc-edge' x1='${s.x}' y1='${s.y}' x2='${mp.x.toFixed(1)}' y2='${mp.y.toFixed(1)}'
      stroke-width='${w.toFixed(2)}' stroke-opacity='${op.toFixed(2)}'></line>`;
  })).join('');

  // Spine line.
  const spine = `<line class='cc-spine' x1='${spineX}' y1='${spinePadT-8}' x2='${spineX}' y2='${H-spinePadB+8}'></line>`;

  // Signal nodes — labelled on the left of the spine.
  const sigSvg = sigPos.map(s => `
    <g class='cc-sig'>
      <circle cx='${s.x}' cy='${s.y}' r='6'></circle>
      <text class='cc-sig-lbl' x='${s.x - 12}' y='${s.y + 3}' text-anchor='end'>${escapeHtml(s.label)}</text>
    </g>`).join('');

  // Mod stars.
  const modSvg = modPos.map(mp => `
    <g class='cc-star'>
      <title>${escapeHtml(mp.mod.name)} — ${fmtInt(mp.mod.total)} appearances across ${mp.mod.links.length} signal classes</title>
      <circle cx='${mp.x.toFixed(1)}' cy='${mp.y.toFixed(1)}' r='${mp.r.toFixed(1)}'></circle>
      ${mp.r >= 4.5 ? `<text class='cc-star-lbl' x='${(mp.x + (mp.x>spineX?mp.r+3:-mp.r-3)).toFixed(1)}' y='${(mp.y+3).toFixed(1)}'
        text-anchor='${mp.x>spineX?'start':'end'}'>${escapeHtml(mp.mod.name)}</text>` : ''}
    </g>`).join('');

  root.innerHTML = `
    <div class='cc-h'>cross-cutting signals — ${groups.length} classes · ${mods.length} mods</div>
    <svg class='cc-constellation' viewBox='0 0 ${W} ${H}' preserveAspectRatio='xMidYMid meet'>
      ${edges}
      ${spine}
      ${sigSvg}
      ${modSvg}
    </svg>
  `;
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
  // Each dot gets a drift trail: a fading polyline through the last few
  // history points. Trail tells you whether the mod is drifting toward
  // higher cost, higher engagement, both, or neither, during the session.
  //
  // Dot fill encodes ""temperament"" via a usage-vs-cost ratio: cost-heavy
  // dots tilt toward warm (the --danger token), usage-heavy toward cool
  // (--accent), balanced sit near neutral. This is descriptive — a tilt
  // is a measurement of where the mod sits in the share ratio, not a
  // verdict on its quality.
  function temperament(d) {
    const sum = d.usageShare + d.cpuShare;
    if (sum < 1e-6) return 'var(--muted)';
    const tilt = (d.cpuShare - d.usageShare) / sum;   // -1 = pure usage, +1 = pure cost
    if (tilt > 0.25)  return 'var(--danger)';
    if (tilt > 0.05)  return 'var(--orange)';
    if (tilt < -0.25) return 'var(--accent)';
    if (tilt < -0.05) return 'var(--cyan)';
    return 'var(--good)';
  }

  const dots = ec.dots.slice().sort((a,b) => (b.rosterSize||0) - (a.rosterSize||0));
  const trailSvg = dots.map(d => {
    const h = scatterHistory.get(d.modId);
    if (!h || h.length < 2) return '';
    // Polyline through history with fading opacity (older = dimmer).
    // Built as a series of segments so we can vary opacity along it.
    const segs = [];
    for (let i = 1; i < h.length; i++) {
      const p0 = h[i-1], p1 = h[i];
      const op = (i / h.length) * 0.55;
      segs.push(`<line class='dot-trail' x1='${x(p0.u).toFixed(1)}' y1='${y(p0.c).toFixed(1)}'
        x2='${x(p1.u).toFixed(1)}' y2='${y(p1.c).toFixed(1)}'
        stroke-opacity='${op.toFixed(2)}'></line>`);
    }
    return segs.join('');
  }).join('');

  const dotsSvg = dots.map(d => {
    const cx = x(d.usageShare), cy = y(d.cpuShare), rad = r(d.rosterSize);
    const fill = temperament(d);
    return `<circle class='dot' cx='${cx.toFixed(1)}' cy='${cy.toFixed(1)}' r='${rad.toFixed(1)}'
      style='fill:${fill}'>
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
      ${trailSvg}
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
