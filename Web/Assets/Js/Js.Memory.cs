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
let memFilter = '';       // per-mod table search term
// Sort state for the per-mod table (mirrors the Insights sortableHead model).
// Defaults to the value column descending — the costliest-first ordering the
// strip already uses. 'val' tracks whichever metric the basis exposes.
let memSort = { key: 'val', dir: -1 };

function memValue(m) { return memBasis === 'overhead' ? (m.hookBytes || 0) : (m.tmlTotal || 0); }

// Footprint composition encoding. The four tML categories (code / textures /
// sounds / managed) are a fixed ROLE set, not per-mod categories, so they get a
// fixed encoding that never collides with the chromatic per-mod hues (modColor's
// full-wheel L0.72/C0.11 palette). A monochrome luminance ramp does that: it is
// orthogonal to every per-mod hue (no chroma to clash with CalamityMod's lilac),
// it is consistent across every row (segment N is always the same shade), and it
// fits the neutral chrome the way the heatmap's opacity ramp does. Order is fixed
// brightest->dimmest so the legend below the column keys it once for all rows.
// L values are spread wide (0.97 -> 0.32, ~0.22 per step) so the four bands stay
// distinguishable even in the slim row mini-bar; the dimmest (managed) still sits
// clear of the --surface track (L0.235) so a filled segment reads against it.
const MEM_FP_CATS = [
  { key: 'tmlCode',     label: 'code',     color: 'oklch(0.97 0 0)' },
  { key: 'tmlTextures', label: 'textures', color: 'oklch(0.74 0 0)' },
  { key: 'tmlSounds',   label: 'sounds',   color: 'oklch(0.52 0 0)' },
  { key: 'tmlManaged',  label: 'managed',  color: 'oklch(0.32 0 0)' },
];

// Which roles are actually present across the visible roster. splitBar/splitLegend
// both drop zero-fraction segments, so a category that is zero for every mod (e.g.
// sounds, which tModLoader reports as 0 for every mod here) never paints a single
// bar slice. Keying it in the static column legend anyway makes the legend claim a
// role the bars never show — the column key and the bars disagree. So the static
// key is data-driven: it lists only the roles non-zero in at least one mod, which
// is exactly the set the bars can render.
function memFootprintCatsPresent(mods) {
  return MEM_FP_CATS.filter(c => mods.some(m => (m[c.key] || 0) > 0));
}

// tML code/textures/sounds/managed split as a SECONDARY hint (thin bar). Kept
// quiet by design: the full composition lives in the breakdown drawer on click,
// so the table only needs a glance of the proportions, never the headline. The
// RAM-magnitude cellbar leads the row; this trails it.
function memFootprintSegs(m) {
  const t = m.tmlTotal || 0;
  if (t <= 0) return null;
  return MEM_FP_CATS.map(c => ({
    frac: (m[c.key] || 0) / t, color: c.color, label: c.label, value: fmtBytes(m[c.key]),
  }));
}

// Static key for the footprint column: a splitLegend over the role colours of
// the roles actually present (no per-mod data), so the monochrome ramp reads as
// 'code | textures | managed' once for the whole column instead of unexplained
// shades per row — and matches the set the bars can paint (roles zero everywhere
// are dropped from both the bars and this key, so the two agree).
function memFootprintLegend(cats) {
  return splitLegend(cats.map(c => ({ color: c.color, label: c.label })));
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
    drawerEl.innerHTML = memBreakdownPrompt();
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
    drawerEl.innerHTML = memBreakdownPrompt();
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
  // Container-level selection marker: when a slice is active, .has-sel dims the
  // un-selected slices so the picked one stands out by contrast as well as by its
  // own ring. On a wide slice the inset ring alone is a thin border that is easy
  // to miss; dimming the rest makes the selection unmistakable at any slice width.
  stripEl.classList.toggle('has-sel', rows.some(r => memSelected === r.m.id));

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

  // Per-mod table view. Independent of the strip: a 29-mod roster needs to be
  // navigable, so the table carries its own search filter + click-to-sort while
  // the strip stays size-sorted. The magnitude cellbar shares the strip's total
  // so bar lengths read the same in both. tr.sel drives the breakdown drawer.
  const valHead = memBasis === 'overhead' ? 'scaffold' : 'RAM';
  // Sort keys -> comparable scalar. 'name' is alpha; the rest are numeric and
  // tie-break on name so the order is deterministic when figures match (idle/db
  // mode where every alloc reads 0). The footprint column is composition, not a
  // scalar, so it is presentational only (not a sort key).
  const memSortVal = {
    name:  m => null,
    val:   m => memValue(m),
    hooks: m => m.hookCount || 0,
    alloc: m => (mem.tracksAllocations ? (m.allocBytes || 0) : 0),
  };
  const q = memFilter.trim().toLowerCase();
  let view = rows.slice();
  if (q) view = view.filter(r => r.m.name.toLowerCase().includes(q));
  view.sort((a, b) => {
    if (memSort.key === 'name') return memSort.dir * a.m.name.localeCompare(b.m.name);
    const fn = memSortVal[memSort.key] || memSortVal.val;
    return memSort.dir * ((fn(a.m) - fn(b.m)) || a.m.name.localeCompare(b.m.name));
  });

  // Sortable header cell: the shared .dtable th.sortable / .sorted idiom with a
  // direction caret on the active column. data-msort is the delegated click key;
  // extra carries any column-width class (e.g. mem-col-val).
  function memTh(key, label, leftAlign, extra) {
    const on = memSort.key === key;
    const arrow = on ? (memSort.dir === 1 ? ' ▲' : ' ▼') : '';
    const cls = (leftAlign ? 'l ' : '') + (extra ? extra + ' ' : '') + 'sortable' + (on ? ' sorted' : '');
    return `<th class='${cls}' data-msort='${key}'>${escapeHtml(label)}${arrow}</th>`;
  }

  // A search box + a footprint key live in a sticky control bar above the table,
  // so a 29-mod roster is filterable and the monochrome footprint ramp is keyed
  // once. The bar re-emits each poll; bindMemory() restores the input's focus.
  // The key is computed off the FULL roster (not the filtered view) so it stays
  // stable while the user searches, and lists only roles the bars actually paint.
  const fpCats = memFootprintCatsPresent(mem.mods);
  let html = `<div class='mem-controls'>`
    + `<input class='filter-input' id='mem-search' placeholder='search mods…' value='${escapeHtml(memFilter)}'>`
    + `<div class='mem-fp-key'><span class='mem-fp-key-lbl'>footprint</span>${memFootprintLegend(fpCats)}</div>`
    + `</div>`;
  if (view.length === 0) {
    html += emptyState('no mods match “' + memFilter + '”');
    setHTML(tableEl, html);
    memRestoreSearchFocus();
    _renderSig['memTable'] = 'empty:' + memFilter;   // so a later identical view rebuilds
    renderMemoryDrawer();
    return;
  }
  const headRow = `<tr>`
    + memTh('name', 'mod', true)
    + memTh('val', valHead, false, 'mem-col-val')
    + `<th class='l mem-col-fp'>footprint</th>`
    + memTh('hooks', 'hooks', false)
    + memTh('alloc', 'alloc/s', false)
    + `</tr>`;
  let bodyRows = '';
  for (const r of view) {
    const m = r.m;
    const sel = memSelected === m.id ? ' sel' : '';
    const segs = memFootprintSegs(m);
    bodyRows += `<tr class='clickable${sel}' data-mod='${m.id}'>`
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
  html += dtable(headRow, bodyRows);
  // Gate the table rebuild behind a signature so an unchanged poll skips the
  // innerHTML reparse (and so leaves the live search box untouched — focus is
  // only restored when the table is actually rebuilt). Keyed on the control
  // state (basis / sort / filter / selection) + each visible row's value figures.
  const memSig = memBasis + '|' + memSort.key + memSort.dir + '|' + memFilter + '|' + (memSelected == null ? '' : memSelected)
    + '|' + view.map(r => r.m.id + ':' + r.v + ':' + (r.m.hookCount || 0) + ':' + (mem.tracksAllocations ? (r.m.allocBytes || 0) : 0)).join(',');
  renderIfChanged('memTable', memSig, () => { setHTML(tableEl, html); memRestoreSearchFocus(); });

  renderMemoryDrawer();
}

// Breakdown empty-state. A bare centred line read as dead space, so this primes
// the slot: the prompt plus a one-line note of what a selection reveals (the tML
// footprint split + the profiler instrumentation figures). Compact, not a skeleton.
function memBreakdownPrompt() {
  return `<div class='mem-bd-empty'>`
    + `<div class='mem-bd-prompt'>select a mod slice or row</div>`
    + `<div class='mem-bd-hint'>its tModLoader footprint split and profiler instrumentation appear here</div>`
    + `</div>`;
}

function renderMemoryDrawer() {
  const drawerEl = document.getElementById('mem-drawer');
  const mem = lastMemory;
  if (!drawerEl) return;
  if (memSelected == null || !mem) {
    drawerEl.innerHTML = memBreakdownPrompt();
    return;
  }
  const m = mem.mods.find(x => x.id === memSelected);
  if (!m) { drawerEl.innerHTML = memBreakdownPrompt(); return; }

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

// The search input lives inside #mem-table, which setHTML() rewrites every poll,
// so a focused box would lose focus + caret mid-keystroke. We track focus/caret
// as the user types and restore them after each re-render. memSearchActive flips
// false on blur so a re-render never steals focus back once the user clicks away.
let memSearchActive = false, memSearchCaret = 0;
function memRestoreSearchFocus() {
  if (!memSearchActive) return;
  const s = document.getElementById('mem-search');
  if (!s) return;
  s.focus();
  const c = Math.min(memSearchCaret, s.value.length);
  try { s.setSelectionRange(c, c); } catch (e) { /* unsupported */ }
}

// Delegated interactions — bound once to the stable containers.
(function bindMemory() {
  ['mem-strip', 'mem-legend', 'mem-table'].forEach(id => {
    const el = document.getElementById(id);
    if (!el) return;
    el.addEventListener('click', e => {
      // A sort header is not a selection target — handle it first and bail.
      const sortTh = e.target.closest('th[data-msort]');
      if (sortTh) {
        const k = sortTh.dataset.msort;
        // Toggle direction on the active column; a new column starts descending
        // (name starts ascending — A→Z reads as the natural default for a name).
        if (memSort.key === k) memSort.dir = -memSort.dir;
        else { memSort.key = k; memSort.dir = (k === 'name') ? 1 : -1; }
        if (activeTab === 'memory') renderMemory();
        return;
      }
      const t = e.target.closest('[data-mod]');
      if (!t) return;
      const mid = parseInt(t.dataset.mod, 10);
      memSelected = (memSelected === mid) ? null : mid;
      if (activeTab === 'memory') renderMemory();
    });
  });

  // Search box: delegated input/focus/blur on the stable #mem-table container so
  // the listeners survive every poll-driven innerHTML rewrite of the table.
  const table = document.getElementById('mem-table');
  if (table) {
    table.addEventListener('input', e => {
      if (e.target.id !== 'mem-search') return;
      memFilter = e.target.value;
      memSearchCaret = e.target.selectionStart || 0;
      if (activeTab === 'memory') renderMemory();
    });
    table.addEventListener('focusin', e => { if (e.target.id === 'mem-search') memSearchActive = true; });
    table.addEventListener('focusout', e => { if (e.target.id === 'mem-search') memSearchActive = false; });
  }

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
