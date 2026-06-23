#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Memory tab renderer, composed from the component library: segmented() for
    // the basis toggle, statGrid/statTile for the summary band + instrumentation
    // cards, splitBar/legend for the hero strip, .dtable + cellBar + splitBar for
    // the per-mod table, sectionBlock + splitBar/splitLegend for the breakdown
    // drawer. The strip is rendered through splitBar()'s shared .split-bar markup
    // but carries per-slice data-mod + a sel ring (the component's segments have
    // no selection hook), so click-to-select is preserved without bespoke CSS.
    private const string JsMemory = @"
// ====== Memory tab ===================================================
// memBasis: 'modlist' = each mod's own RAM (tModLoader estimate);
//           'overhead' = the profiler's hook-scaffolding we hold per mod.
let memBasis = 'modlist';
let memSelected = null;   // selected modId for the breakdown drawer

function memValue(m) { return memBasis === 'overhead' ? (m.hookBytes || 0) : (m.tmlTotal || 0); }

// tML code/textures/sounds/managed split as a SECONDARY hint (thin bar). Kept
// quiet by design: the full composition lives in the breakdown drawer on click,
// so the table only needs a glance of the proportions, never the headline. The
// RAM-magnitude cellbar leads the row; this trails it.
function memFootprintSegs(m) {
  const t = m.tmlTotal || 0;
  if (t <= 0) return null;
  return [
    { frac: m.tmlCode/t,     color: 'var(--cpu)',   label: 'code',     value: fmtBytes(m.tmlCode) },
    { frac: m.tmlTextures/t, color: 'var(--spike)', label: 'textures', value: fmtBytes(m.tmlTextures) },
    { frac: m.tmlSounds/t,   color: 'var(--cyan)',  label: 'sounds',   value: fmtBytes(m.tmlSounds) },
    { frac: m.tmlManaged/t,  color: 'var(--gc)',    label: 'managed',  value: fmtBytes(m.tmlManaged) },
  ];
}

function renderMemory() {
  const mem = lastMemory;
  const basisEl  = document.getElementById('mem-basis');
  const stripEl  = document.getElementById('mem-strip');
  const legendEl = document.getElementById('mem-legend');
  const tableEl  = document.getElementById('mem-table');
  const drawerEl = document.getElementById('mem-drawer');
  const sumEl    = document.getElementById('mem-summary');
  if (!stripEl) return;

  // Basis toggle state: 'modlist' needs the tML estimate; fall back to 'overhead'.
  const tmlOk = !!(mem && mem.tmlAvailable);
  if (memBasis === 'modlist' && !tmlOk) memBasis = 'overhead';
  // segmented() into the #mem-basis slot; bindMemory()'s delegated listener stays
  // bound to the container, so re-rendering the buttons keeps the click wiring.
  basisEl.innerHTML = segmented({
    id: '', attr: 'data-basis', active: memBasis,
    options: [
      { value: 'modlist',  label: 'modlist RAM',      disabled: !tmlOk, title: tmlOk ? '' : 'tModLoader estimate unavailable' },
      { value: 'overhead', label: 'profiler overhead' },
    ],
  }).replace(`<span class='segctl' id=''>`, '').replace(/<\/span>$/, '');

  if (!mem || !mem.mods || mem.mods.length === 0) {
    stripEl.innerHTML = '';
    legendEl.innerHTML = '';
    sumEl.innerHTML = '';
    tableEl.innerHTML = emptyState('no data yet');
    drawerEl.innerHTML = emptyState('select a mod for its breakdown');
    return;
  }

  // Summary band — statGrid of statTile. dash() guards each metric so an
  // absent/zero figure reads as '—' rather than leaving the label dangling.
  const tiles = [];
  tiles.push(statTile({ k: 'process RSS', v: dash(mem.processRssBytes, fmtBytes) }));
  if (mem.tmlAvailable) tiles.push(statTile({ k: 'mods · tML est', v: dash(mem.tmlTotalBytes, fmtBytes) }));
  if (mem.scaffoldAvailable) {
    tiles.push(statTile({ k: 'profiler scaffolding', v: dash(mem.scaffoldBytes, fmtBytes) }));
    tiles.push(statTile({ k: 'per hook', v: dash(mem.bytesPerHook, v => (v/1024).toFixed(0) + ' KB') }));
    tiles.push(statTile({ k: 'hooks', v: dash(mem.installedHookCount, fmtInt) }));
  }
  sumEl.innerHTML = statGrid(tiles);

  // Rows for the current basis, sorted desc.
  const rows = mem.mods.map(m => ({ m, v: memValue(m) })).filter(r => r.v > 0).sort((a, b) => b.v - a.v);
  if (rows.length === 0) {
    stripEl.innerHTML = '';
    legendEl.innerHTML = '';
    tableEl.innerHTML = emptyState(memBasis === 'overhead' ? 'profiler not installed yet' : 'no per-mod memory estimate yet');
    drawerEl.innerHTML = emptyState('select a mod for its breakdown');
    return;
  }
  const total = rows.reduce((s, r) => s + r.v, 0) || 1;

  // Hero strip — splitBar()'s shared .split-bar.tall markup, one slice per mod
  // coloured by modColor. The component's segments carry no selection hook, so
  // we tag each rendered span in order with data-mod + the .sel ring, keeping
  // click-to-select. rows are pre-filtered to v>0, so splitBar skips none and the
  // span order matches rows 1:1. width/title come straight from splitBar().
  let si = 0;
  stripEl.innerHTML = splitBar(rows.map(r => ({
    frac: r.v / total, color: modColor(r.m.id),
    label: r.m.name, value: fmtBytes(r.v),
  })), { tall: true }).replace(/<span /g, () => {
    const r = rows[si++];
    const sel = memSelected === r.m.id ? ` class='sel'` : '';
    return `<span data-mod='${r.m.id}'${sel} `;
  });

  // Legend — top 8 + rest, clickable (legend() lg rows; we add data-mod so the
  // delegated handler picks them up like the strip + table).
  const top = rows.slice(0, 8);
  const items = top.map(r => ({ color: modColor(r.m.id), label: truncate(r.m.name, 18), value: fmtBytes(r.v) }));
  if (rows.length > top.length) {
    const restSum = rows.slice(8).reduce((s, r) => s + r.v, 0);
    items.push({ color: 'var(--dim)', label: '+' + (rows.length - top.length) + ' more', value: fmtBytes(restSum) });
  }
  let li = 0;
  legendEl.innerHTML = legend(items).replace(/<span class='lg'>/g, () => {
    const r = top[li++];
    return r ? `<span class='lg' data-mod='${r.m.id}'>` : `<span class='lg'>`;
  });

  // Per-mod table — same order as the strip. The RAM/scaffold column LEADS: a
  // wide magnitude bar (share of the visible total) sits beside its figure as
  // one unit, so scanning the column tracks size. The footprint composition is a
  // SECONDARY hint — a thin, narrow split confined to its own slim column; the
  // full breakdown is one click away in the drawer. tr.sel drives the drawer.
  const valHead = memBasis === 'overhead' ? 'scaffold' : 'RAM';
  let th = `<table class='dtable'><thead><tr>`
    + `<th class='l'>mod</th>`
    + `<th class='mem-col-val'>${valHead}</th>`
    + `<th class='l mem-col-fp'>footprint</th><th>hooks</th><th>alloc/s</th>`
    + `</tr></thead><tbody>`;
  for (const r of rows) {
    const m = r.m;
    const sel = memSelected === m.id ? ' sel' : '';
    const segs = memFootprintSegs(m);
    th += `<tr class='clickable${sel}' data-mod='${m.id}'>`
      + `<td class='l'>${escapeHtml(truncate(m.name, 24))}</td>`
      + `<td class='mem-col-val'><span class='mem-val-cell'>`
        + cellBar(r.v / total, modColor(m.id))
        + `<span class='n'>${fmtBytes(r.v)}</span>`
        + `</span></td>`
      + `<td class='l mem-col-fp'>${segs ? splitBar(segs, { thin: true }) : dash(null)}</td>`
      + `<td class='muted'>${fmtInt(m.hookCount)}</td>`
      + `<td class='muted'>${dash(mem.tracksAllocations ? m.allocBytes : null, fmtBytes)}</td>`
      + `</tr>`;
  }
  th += `</tbody></table>`;
  setHTML(tableEl, th);   // preserve scroll on poll-driven re-render

  renderMemoryDrawer();
}

function renderMemoryDrawer() {
  const drawerEl = document.getElementById('mem-drawer');
  const mem = lastMemory;
  if (!drawerEl) return;
  if (memSelected == null || !mem) {
    drawerEl.innerHTML = emptyState('select a mod slice or row for its breakdown');
    return;
  }
  const m = mem.mods.find(x => x.id === memSelected);
  if (!m) { drawerEl.innerHTML = emptyState('select a mod for its breakdown'); return; }

  // Header: name + total (the tML number is an estimate — sub-label keeps that
  // honesty badge in the section header, not a normative judgement).
  let h = `<div class='section-h'><span>${escapeHtml(m.name)}</span>`;
  if (m.tmlTotal > 0) h += `<span class='section-sub'>${escapeHtml(fmtBytes(m.tmlTotal))}</span>`;
  h += `</div>`;

  // mod footprint · tModLoader estimate — tall splitBar + splitLegend.
  const segs = memFootprintSegs(m);
  if (segs) {
    h += sectionBlock('mod footprint · tModLoader estimate',
      splitBar(segs, { tall: true }) + splitLegend(segs));
  } else {
    h += sectionBlock('mod footprint', emptyState('tModLoader estimate unavailable'));
  }

  // profiler instrumentation — statGrid of statTile. The scaffolding tile carries
  // a proportion bar (its share of the mod's own footprint) so the number reads
  // as a quantity, not a bare figure.
  const t = m.tmlTotal || 0;
  const scaffoldFrac = t > 0 ? (m.hookBytes || 0) / t : null;
  h += sectionBlock('profiler instrumentation', statGrid([
    statTile({ k: 'hook scaffolding · est', v: dash(m.hookBytes, fmtBytes), frac: scaffoldFrac, color: 'var(--accent)' }),
    statTile({ k: 'installed hooks', v: dash(m.hookCount, fmtInt) }),
    statTile({ k: 'allocation rate', v: mem.tracksAllocations ? fmtBytes(m.allocBytes) + '/s' : '—' }),
  ]));

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
