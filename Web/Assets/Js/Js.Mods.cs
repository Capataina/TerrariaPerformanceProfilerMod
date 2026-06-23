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
    private const string JsMods = @"
// ====== Summary: Mod tree =============================================
// Hover-suppression flag for the mod tree. The poll loop calls
// renderSummaryMods() at ~1.5 s cadence, which wipes #modtable's
// innerHTML and rebuilds it; doing that while the cursor is over a
// row causes a visible flicker as the row briefly leaves the DOM
// and rejoins it. We track hover state on #modtable; if a render is
// requested while hovering, we mark it pending and run it on the
// next mouseleave. User actions (twirl click, sort change, filter,
// collapse-all) bypass the suppression via renderSummaryModsForced.
let modtableHovered = false;
let modtableRenderPending = false;

// Shared grid template for every level of the tree (headline / category / hook)
// so columns line up with the static .modtable-head. rank | name | cost bar |
// 30s spark | now | avg | alloc.
const MODTABLE_COLS = '2em minmax(0, 1.4fr) minmax(0, 2.2fr) 4.5em 4.5em 4.5em 5em';

// Render the composite/cpu/avg/alloc sort control via the shared segmented().
// The alloc option is disabled (not removed) when alloc isn't tracked.
function renderModSortControl() {
  const host = document.getElementById('mods-sort');
  if (!host) return;
  const tracks = lastMods && lastMods.tracksAllocations;
  host.innerHTML = segmented({
    id: 'mods-sort-seg', attr: 'data-sort', active: modSort,
    options: [
      { value: 'composite', label: 'composite' },
      { value: 'cpu', label: 'cpu' },
      { value: 'avg', label: 'avg' },
      { value: 'alloc', label: 'alloc', disabled: !tracks, title: tracks ? '' : 'allocation tracking is off' },
    ],
  });
}

function renderSummaryMods() {
  if (modtableHovered) { modtableRenderPending = true; return; }
  renderSummaryModsForced();
}

function renderSummaryModsForced() {
  modtableRenderPending = false;
  const root = document.getElementById('modtable');
  if (!lastMods || !lastMods.worldLoaded || !lastMods.mods) {
    root.innerHTML = emptyState('no data yet');
    return;
  }

  renderModSortControl();
  // Toggle alloc visibility in the static header.
  const allocHeader = document.getElementById('mh-alloc');
  if (allocHeader) allocHeader.style.opacity = lastMods.tracksAllocations ? '' : '0.4';

  // Filter + sort.
  const q = modFilter.trim().toLowerCase();
  let mods = lastMods.mods.filter(m => (m.cpuMs > 0 || m.avgCpuMs > 0) && (q === '' || m.name.toLowerCase().includes(q)));
  let getter;
  switch (modSort) {
    case 'cpu':    getter = m => m.cpuMs; break;
    case 'avg':    getter = m => m.avgCpuMs; break;
    case 'alloc':  getter = m => m.allocBytes || 0; break;
    default:       getter = m => m.cpuMs * 0.7 + m.avgCpuMs * 0.3; break;
  }
  mods.sort((a, b) => getter(b) - getter(a));
  const max = mods.length > 0 ? getter(mods[0]) : 1;
  const median = mods.length > 0 ? getter(mods[Math.floor(mods.length / 2)]) : 0;
  const outlierCut = median * 2.5;

  let html = '';
  for (let i = 0; i < mods.length; i++) {
    const m = mods[i];
    const v = getter(m);
    const isOutlier = v > outlierCut && i < 3;
    const isOpen = expandedMods.has(m.id);
    // Bar colour grades by ratio against the median (perfColor): calm green ->
    // red as a mod climbs. Outliers read amber, the top-3 climbers red.
    const ratio = median > 0 ? v / median : 0;
    const barCol = isOutlier ? 'var(--amber)' : (i < 3 && ratio >= 12 ? 'var(--danger)' : perfColor(ratio));
    html += row({
      cols: MODTABLE_COLS, clickable: true, outlier: isOutlier,
      cls: 'modrow' + (isOpen ? ' open' : ''),
      attrs: `data-mod='${m.id}'`,
      cells: [
        `<span class='rk'>${i + 1}</span>`,
        `<span class='nm-cell'>${twirl(isOpen)}<span class='modname' data-role='name'>${escapeHtml(m.name)}</span></span>`,
        cellBar(v / max, barCol),
        `<span class='mt-spark'>${renderModSparkInline(m.id)}</span>`,
        `<span class='mt-num'>${fmtMs(m.cpuMs)}<span class='u'>ms</span></span>`,
        `<span class='mt-num'>${fmtMs(m.avgCpuMs)}<span class='u'>avg</span></span>`,
        `<span class='mt-num'>${lastMods.tracksAllocations ? fmtBytes(m.allocBytes) : '—'}</span>`,
      ],
    });
    if (isOpen) html += renderModTree(m);
  }
  root.innerHTML = html;

  // Wire row clicks: twirl + whole row toggle the tree, the name opens the card.
  root.querySelectorAll('.modrow').forEach(rowEl => {
    const modId = parseInt(rowEl.dataset.mod, 10);
    rowEl.querySelector('.twirl').addEventListener('click', e => {
      e.stopPropagation();
      toggleExpandMod(modId);
    });
    rowEl.querySelector('[data-role=name]').addEventListener('click', e => {
      e.stopPropagation();
      openModCard(modId);
    });
    rowEl.addEventListener('click', () => toggleExpandMod(modId));
  });
}

function renderModSparkInline(modId) {
  const arr = modSparkHistory.get(modId);
  if (!arr || arr.length < 2) return '';
  return sparkline(arr, { color: 'var(--cpu)', strokeW: 1 });
}

function renderModTree(mod) {
  const blank = `<span></span>`;
  // Group hook records by mod, then by category.
  if (!lastHooks || !lastHooks.hooks) {
    return `<div class='mod-tree'>` + row({
      cols: MODTABLE_COLS, cls: 'cat-row',
      cells: [blank, `<span class='nm muted'>loading…</span>`, blank, blank, blank, blank, blank],
    }) + `</div>`;
  }
  const cats = lastMods.categories || [];
  // Build categoryId → hooks[] for this mod.
  const buckets = new Map();
  for (const h of lastHooks.hooks) {
    if (h.modId !== mod.id) continue;
    let arr = buckets.get(h.categoryId);
    if (!arr) { arr = []; buckets.set(h.categoryId, arr); }
    arr.push(h);
  }
  if (buckets.size === 0) {
    return `<div class='mod-tree'>` + row({
      cols: MODTABLE_COLS, cls: 'hook-row',
      cells: [blank, `<span class='nm muted'>no active hooks for this mod (yet)</span>`, blank, blank, blank, blank, blank],
    }) + `</div>`;
  }
  // Sort each category by cpuMs desc, sort categories by total cpuMs.
  const catOrder = [...buckets.entries()].map(([catId, arr]) => {
    arr.sort((a, b) => b.cpuMs - a.cpuMs);
    const total = arr.reduce((s, h) => s + h.cpuMs, 0);
    return { catId, total, hooks: arr };
  }).sort((a, b) => b.total - a.total);

  const max = catOrder[0].total || 1;

  let html = '<div class=""mod-tree"">';
  for (const c of catOrder) {
    const catName = cats[c.catId] || ('cat:' + c.catId);
    const catKey = mod.id + '|' + c.catId;
    const catOpen = expandedCategories.has(catKey);
    html += row({
      cols: MODTABLE_COLS, clickable: true, cls: 'cat-row' + (catOpen ? ' open' : ''),
      attrs: `data-cat='${catKey}'`,
      cells: [
        twirl(catOpen),
        `<span class='nm'>${escapeHtml(catName)}</span>`,
        cellBar(c.total / max, 'var(--accent)'),
        blank,
        `<span class='mt-num'>${fmtMs(c.total)}<span class='u'>ms</span></span>`,
        `<span class='mt-num muted'>${c.hooks.length} hooks</span>`,
        blank,
      ],
    });
    if (catOpen) {
      const hookMax = c.hooks[0].cpuMs || 1;
      for (const h of c.hooks.slice(0, 20)) {
        html += row({
          cols: MODTABLE_COLS, cls: 'hook-row',
          cells: [
            blank, blank,
            `<span class='nm'>${escapeHtml(truncate(h.display, 60))}</span>`,
            cellBar(h.cpuMs / hookMax, 'var(--accent)'),
            `<span class='mt-num'>${fmtMs(h.cpuMs)}<span class='u'>ms</span></span>`,
            `<span class='mt-num'>${fmtMs(h.avgCpuMs)}<span class='u'>avg</span></span>`,
            `<span class='mt-num'>${lastHooks.tracksAllocations ? fmtBytes(h.allocBytes) : '—'}</span>`,
          ],
        });
      }
      if (c.hooks.length > 20) {
        html += row({
          cols: MODTABLE_COLS, cls: 'hook-row',
          cells: [blank, blank, `<span class='nm muted'>+ ${c.hooks.length - 20} quieter hooks</span>`, blank, blank, blank, blank],
        });
      }
    }
  }
  html += '</div>';
  // Bind category twirls — done after render via event delegation in toggleExpandMod's caller
  setTimeout(() => {
    document.querySelectorAll('.cat-row').forEach(r => {
      const key = r.dataset.cat;
      if (!r.dataset.bound) {
        r.dataset.bound = '1';
        r.addEventListener('click', e => {
          e.stopPropagation();
          if (expandedCategories.has(key)) expandedCategories.delete(key);
          else expandedCategories.add(key);
          // User-driven — force past the hover-suppression gate.
          renderSummaryModsForced();
        });
      }
    });
  }, 0);
  return html;
}

function toggleExpandMod(modId) {
  if (expandedMods.has(modId)) expandedMods.delete(modId);
  else { expandedMods.add(modId); pollHooks(); }
  // User-driven render — force, bypassing the hover-suppression gate.
  renderSummaryModsForced();
}

// Mod-tree sort + filter wiring. The sort control is the shared segmented()
// rendered into #mods-sort; clicks are delegated on that persistent host and
// renderModSortControl() repaints the active state from modSort on re-render.
document.getElementById('mods-sort').addEventListener('click', e => {
  const b = e.target.closest('button');
  if (!b) return;
  modSort = b.dataset.sort;
  if (activeTab === 'summary') renderSummaryModsForced();
});
document.getElementById('mod-filter').addEventListener('input', e => {
  modFilter = e.target.value;
  if (activeTab === 'summary') renderSummaryModsForced();
});
document.getElementById('mods-collapse-all').addEventListener('click', () => {
  expandedMods.clear();
  expandedCategories.clear();
  if (activeTab === 'summary') renderSummaryModsForced();
});

// Pause polling-induced re-renders while the cursor is over the mod tree
// — prevents the hover-flicker that happens when innerHTML is wiped and
// rebuilt every 1.5 s. Pending renders fire on mouseleave.
(function bindModtableHoverGate() {
  const root = document.getElementById('modtable');
  if (!root) return;
  root.addEventListener('mouseenter', () => { modtableHovered = true; });
  root.addEventListener('mouseleave', () => {
    modtableHovered = false;
    if (modtableRenderPending) renderSummaryModsForced();
  });
})();
";
}
