#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string JsMemory = @"
// ====== Memory tab ===================================================
// memBasis: 'modlist' = each mod's own RAM (tModLoader estimate);
//           'overhead' = the profiler's hook-scaffolding we hold per mod.
let memBasis = 'modlist';
let memSelected = null;   // selected modId for the breakdown drawer

function memValue(m) { return memBasis === 'overhead' ? (m.hookBytes || 0) : (m.tmlTotal || 0); }

function memStat(k, v) {
  return `<div class='mem-stat'><span class='k'>${escapeHtml(k)}</span><span class='v'>${escapeHtml(String(v))}</span></div>`;
}

// tML code/textures/sounds/managed breakdown as a thin split bar (table cell).
function memBreakdownBar(m) {
  const t = m.tmlTotal || 0;
  if (t <= 0) return `<span class='dim'>—</span>`;
  return splitBar([
    { frac: m.tmlCode/t,     color: 'var(--cpu)',   label: 'code',     value: fmtBytes(m.tmlCode) },
    { frac: m.tmlTextures/t, color: 'var(--spike)', label: 'textures', value: fmtBytes(m.tmlTextures) },
    { frac: m.tmlSounds/t,   color: 'var(--cyan)',  label: 'sounds',   value: fmtBytes(m.tmlSounds) },
    { frac: m.tmlManaged/t,  color: 'var(--gc)',    label: 'managed',  value: fmtBytes(m.tmlManaged) },
  ], { thin: true });
}

function renderMemory() {
  const mem = lastMemory;
  const stripEl  = document.getElementById('mem-strip');
  const legendEl = document.getElementById('mem-legend');
  const tableEl  = document.getElementById('mem-table');
  const drawerEl = document.getElementById('mem-drawer');
  const sumEl    = document.getElementById('mem-summary');
  if (!stripEl) return;

  if (!mem || !mem.mods || mem.mods.length === 0) {
    stripEl.innerHTML = '';
    legendEl.innerHTML = '';
    sumEl.innerHTML = '';
    tableEl.innerHTML = `<div class='empty-line'>no data yet</div>`;
    drawerEl.innerHTML = `<div class='empty-line'>select a mod for its breakdown</div>`;
    return;
  }

  // Basis toggle state: 'modlist' needs the tML estimate; fall back to 'overhead'.
  if (memBasis === 'modlist' && !mem.tmlAvailable) memBasis = 'overhead';
  document.querySelectorAll('#mem-basis button').forEach(b => {
    const disabled = b.dataset.basis === 'modlist' && !mem.tmlAvailable;
    b.style.opacity = disabled ? '0.35' : '';
    b.classList.toggle('active', b.dataset.basis === memBasis);
  });

  // Summary band.
  const parts = [];
  parts.push(memStat('process RSS', fmtBytes(mem.processRssBytes)));
  if (mem.tmlAvailable) parts.push(memStat('mods · tML est', fmtBytes(mem.tmlTotalBytes)));
  if (mem.scaffoldAvailable) {
    parts.push(memStat('profiler scaffolding', fmtBytes(mem.scaffoldBytes)));
    parts.push(memStat('per hook', (mem.bytesPerHook/1024).toFixed(0) + ' KB'));
    parts.push(memStat('hooks', fmtInt(mem.installedHookCount)));
  }
  sumEl.innerHTML = parts.join('');

  // Rows for the current basis, sorted desc.
  const rows = mem.mods.map(m => ({ m, v: memValue(m) })).filter(r => r.v > 0).sort((a, b) => b.v - a.v);
  if (rows.length === 0) {
    stripEl.innerHTML = '';
    legendEl.innerHTML = '';
    tableEl.innerHTML = `<div class='empty-line'>${memBasis === 'overhead' ? 'profiler not installed yet' : 'no per-mod memory estimate yet'}</div>`;
    drawerEl.innerHTML = `<div class='empty-line'>select a mod for its breakdown</div>`;
    return;
  }
  const total = rows.reduce((s, r) => s + r.v, 0) || 1;

  // Strip — clickable slices.
  let sh = '';
  for (const r of rows) {
    const w = (r.v / total * 100).toFixed(2);
    const sel = memSelected === r.m.id ? ' sel' : '';
    sh += `<span class='mem-slice${sel}' data-mod='${r.m.id}' style='width:${w}%;background:${modColor(r.m.id)}' title='${escapeHtml(r.m.name + ' · ' + fmtBytes(r.v))}'></span>`;
  }
  stripEl.innerHTML = sh;

  // Legend — top 8 + rest, clickable.
  const top = rows.slice(0, 8);
  let lh = '';
  for (const r of top) {
    lh += `<span class='lg' data-mod='${r.m.id}'><span class='sw' style='background:${modColor(r.m.id)}'></span>${escapeHtml(truncate(r.m.name, 18))} <span class='lg-v'>${fmtBytes(r.v)}</span></span>`;
  }
  if (rows.length > top.length) {
    const restSum = rows.slice(8).reduce((s, r) => s + r.v, 0);
    lh += `<span class='lg'><span class='sw' style='background:var(--dim)'></span>+${rows.length - top.length} more <span class='lg-v'>${fmtBytes(restSum)}</span></span>`;
  }
  legendEl.innerHTML = lh;

  // Table — same order as the strip.
  let th = `<table class='dtable'><thead><tr>`
    + `<th class='l'>mod</th><th>${memBasis === 'overhead' ? 'scaffold' : 'RAM'}</th>`
    + `<th class='l'>footprint · code / tex / snd / managed</th><th>hooks</th><th>alloc/s</th>`
    + `</tr></thead><tbody>`;
  for (const r of rows) {
    const m = r.m;
    const sel = memSelected === m.id ? ' sel' : '';
    th += `<tr class='clickable${sel}' data-mod='${m.id}'>`
      + `<td class='l'>${escapeHtml(truncate(m.name, 24))}</td>`
      + `<td class='mem-val'><span class='n'>${fmtBytes(r.v)}</span>`
        + cellBar(r.v / total, modColor(m.id)) + `</td>`
      + `<td class='l'>${memBreakdownBar(m)}</td>`
      + `<td class='muted'>${fmtInt(m.hookCount)}</td>`
      + `<td class='muted'>${mem.tracksAllocations ? fmtBytes(m.allocBytes) : '—'}</td>`
      + `</tr>`;
  }
  th += `</tbody></table>`;
  setHTML(tableEl, th);   // preserve scroll on poll-driven re-render

  renderMemoryDrawer();
}

// One compact instrumentation card: label, value, optional proportion bar
// (frac of the mod's own total footprint, so the number gains a visual scale).
function memCard(k, v, frac, color) {
  let bar = '';
  if (frac != null && isFinite(frac)) bar = `<span class='mem-card-bar'>${cellBar(frac, color)}</span>`;
  return `<div class='mem-card'><span class='k'>${escapeHtml(k)}</span>`
    + `<span class='v'>${escapeHtml(String(v))}</span>${bar}</div>`;
}

function renderMemoryDrawer() {
  const drawerEl = document.getElementById('mem-drawer');
  const mem = lastMemory;
  if (!drawerEl) return;
  if (memSelected == null || !mem) {
    drawerEl.innerHTML = `<div class='empty-line'>select a mod slice or row for its breakdown</div>`;
    return;
  }
  const m = mem.mods.find(x => x.id === memSelected);
  if (!m) { drawerEl.innerHTML = `<div class='empty-line'>select a mod for its breakdown</div>`; return; }

  let h = `<div class='mem-drawer-head'><span class='mem-drawer-name'>${escapeHtml(m.name)}</span>`;
  if (m.tmlTotal > 0) h += `<span class='mem-drawer-total'>${fmtBytes(m.tmlTotal)}</span>`;
  h += `</div>`;

  if (m.tmlTotal > 0) {
    const t = m.tmlTotal;
    const segs = [
      { frac: m.tmlCode/t,     color: 'var(--cpu)',   label: 'code',     value: fmtBytes(m.tmlCode) },
      { frac: m.tmlTextures/t, color: 'var(--spike)', label: 'textures', value: fmtBytes(m.tmlTextures) },
      { frac: m.tmlSounds/t,   color: 'var(--cyan)',  label: 'sounds',   value: fmtBytes(m.tmlSounds) },
      { frac: m.tmlManaged/t,  color: 'var(--gc)',    label: 'managed',  value: fmtBytes(m.tmlManaged) },
    ];
    h += `<div class='mem-sect'><div class='mem-sect-h'>mod footprint · tModLoader estimate</div>`
      + splitBar(segs, { tall: true }) + splitLegend(segs) + `</div>`;
  } else {
    h += `<div class='mem-sect'><div class='mem-sect-h'>mod footprint</div>`
      + `<div class='empty-line'>tModLoader estimate unavailable</div></div>`;
  }

  // Instrumentation — compact stat cards, capped width, not full-width rows.
  // The scaffolding card carries a proportion bar (its share of the mod's own
  // footprint) so the number reads as a quantity, not a bare figure.
  const t = m.tmlTotal || 0;
  const scaffoldFrac = t > 0 ? (m.hookBytes || 0) / t : null;
  h += `<div class='mem-sect'><div class='mem-sect-h'>profiler instrumentation</div>`
    + `<div class='mem-card-grid'>`
    + memCard('hook scaffolding · est', fmtBytes(m.hookBytes), scaffoldFrac, 'var(--accent)')
    + memCard('installed hooks', fmtInt(m.hookCount))
    + memCard('allocation rate', mem.tracksAllocations ? fmtBytes(m.allocBytes) + '/s' : '—')
    + `</div></div>`;

  drawerEl.innerHTML = h;
}

// Delegated interactions — bound once to the stable containers.
(function bindMemory() {
  ['mem-strip', 'mem-legend', 'mem-table'].forEach(id => {
    const el = document.getElementById(id);
    if (!el) return;
    el.addEventListener('click', e => {
      const t = e.target.closest('[data-mod]');
      if (!t) return;
      const mid = parseInt(t.dataset.mod, 10);
      memSelected = (memSelected === mid) ? null : mid;
      if (activeTab === 'memory') renderMemory();
    });
  });
  const basis = document.getElementById('mem-basis');
  if (basis) basis.addEventListener('click', e => {
    const b = e.target.closest('button');
    if (!b) return;
    if (b.dataset.basis === 'modlist' && lastMemory && !lastMemory.tmlAvailable) return;
    memBasis = b.dataset.basis;
    if (activeTab === 'memory') renderMemory();
  });
})();
";
}
