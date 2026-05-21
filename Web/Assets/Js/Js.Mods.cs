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

function renderSummaryMods() {
  if (modtableHovered) { modtableRenderPending = true; return; }
  renderSummaryModsForced();
}

function renderSummaryModsForced() {
  modtableRenderPending = false;
  const root = document.getElementById('modtable');
  if (!lastMods || !lastMods.worldLoaded || !lastMods.mods) {
    root.innerHTML = '<div class=""empty-line"">no data yet</div>';
    return;
  }

  // Toggle alloc visibility in header.
  const allocHeader = document.getElementById('mh-alloc');
  const sortAllocBtn = document.getElementById('sort-alloc');
  if (lastMods.tracksAllocations) {
    allocHeader.style.opacity = ''; sortAllocBtn.style.display = '';
  } else {
    allocHeader.style.opacity = '0.4'; sortAllocBtn.style.opacity = '0.4';
  }

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

  // Color-grade bars by per-mod ratio against the median: bar color
  // shifts from green (calm) → yellow → orange → red as a mod climbs.
  // Provides at-a-glance answer to ""is this mod actually expensive
  // for this session"" beyond just the bar length.
  function barColor(value) {
    if (median <= 0) return 'var(--perf-0)';
    const r = value / median;
    if (r < 1.5) return 'var(--perf-0)';
    if (r < 3)   return 'var(--perf-1)';
    if (r < 6)   return 'var(--perf-2)';
    if (r < 12)  return 'var(--perf-3)';
    return 'var(--perf-4)';
  }

  let html = '';
  for (let i = 0; i < mods.length; i++) {
    const m = mods[i];
    const v = getter(m);
    const isTop = i < 3;
    const isOutlier = v > outlierCut && i < 3;
    const isOpen = expandedMods.has(m.id);
    const sparkSvg = renderModSparkInline(m.id);
    const color = barColor(v);
    html += `<div class='modrow ${isTop ? 'is-top' : ''} ${isOutlier ? 'outlier' : ''}' data-mod='${m.id}'>
      <span class='rank'>${i + 1}</span>
      <span class='name'>
        <span class='twirl' data-role='twirl'>▶</span>
        <span class='modname' data-role='name'>${escapeHtml(m.name)}</span>
      </span>
      <span class='bar'><span style='width: ${(v / max * 100).toFixed(1)}%; background: ${color}'></span></span>
      <span class='spark'>${sparkSvg}</span>
      <span class='ms'>${fmtMs(m.cpuMs)}<span class='u'>ms</span></span>
      <span class='ms'>${fmtMs(m.avgCpuMs)}<span class='u'>avg</span></span>
      <span class='alloc'>${lastMods.tracksAllocations ? fmtBytes(m.allocBytes) : '—'}</span>
    </div>`;
    if (isOpen) html += renderModTree(m);
  }
  root.innerHTML = html;

  // Toggle open class on opens.
  for (const id of expandedMods) {
    const r = root.querySelector('.modrow[data-mod=""' + id + '""]');
    if (r) r.classList.add('open');
  }

  // Wire row clicks.
  root.querySelectorAll('.modrow').forEach(row => {
    const modId = parseInt(row.dataset.mod, 10);
    row.querySelector('[data-role=twirl]').addEventListener('click', e => {
      e.stopPropagation();
      toggleExpandMod(modId);
    });
    row.querySelector('[data-role=name]').addEventListener('click', e => {
      e.stopPropagation();
      openModCard(modId);
    });
    row.addEventListener('click', () => toggleExpandMod(modId));
  });
}

function renderModSparkInline(modId) {
  const arr = modSparkHistory.get(modId);
  if (!arr || arr.length < 2) return '';
  const max = Math.max(0.005, Math.max(...arr));
  let d = '';
  for (let i = 0; i < arr.length; i++) {
    const x = (i / Math.max(1, arr.length - 1)) * 100;
    const y = 16 - (arr[i] / max) * 14;
    d += (i === 0 ? 'M' : 'L') + x.toFixed(2) + ',' + y.toFixed(2) + ' ';
  }
  return `<svg viewBox='0 0 100 16' preserveAspectRatio='none'><path d='${d}' fill='none' stroke='#6ec07e' stroke-width='0.7'/></svg>`;
}

function renderModTree(mod) {
  // Group hook records by mod, then by category.
  if (!lastHooks || !lastHooks.hooks) {
    return `<div class='mod-tree'><div class='cat-row'><span class='twirl'></span><span class='name muted'>loading…</span></div></div>`;
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
    return `<div class='mod-tree'><div class='hook-row'><span></span><span class='name muted'>no active hooks for this mod (yet)</span><span></span><span></span><span></span><span></span><span></span></div></div>`;
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
    html += `<div class='cat-row ${catOpen ? 'open' : ''}' data-cat='${catKey}'>
      <span class='twirl'>▶</span>
      <span class='name'>${escapeHtml(catName)}</span>
      <span class='bar'><span style='width: ${(c.total / max * 100).toFixed(1)}%; background: #4978a8'></span></span>
      <span></span>
      <span class='ms'>${fmtMs(c.total)}<span class='u'>ms</span></span>
      <span class='muted' style='text-align:right'>${c.hooks.length} hooks</span>
      <span></span>
    </div>`;
    if (catOpen) {
      const hookMax = c.hooks[0].cpuMs || 1;
      for (const h of c.hooks.slice(0, 20)) {
        html += `<div class='hook-row'>
          <span></span>
          <span></span>
          <span class='name'>${escapeHtml(truncate(h.display, 60))}</span>
          <span class='bar'><span style='width: ${(h.cpuMs / hookMax * 100).toFixed(1)}%'></span></span>
          <span class='ms'>${fmtMs(h.cpuMs)}<span class='u'>ms</span></span>
          <span class='ms'>${fmtMs(h.avgCpuMs)}<span class='u'>avg</span></span>
          <span class='alloc'>${lastHooks.tracksAllocations ? fmtBytes(h.allocBytes) : '—'}</span>
        </div>`;
      }
      if (c.hooks.length > 20) {
        html += `<div class='hook-row'><span></span><span></span><span class='name muted'>+ ${c.hooks.length - 20} quieter hooks</span><span></span><span></span><span></span><span></span></div>`;
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

// Mod-tree sort + filter wiring.
document.getElementById('mods-sort').addEventListener('click', e => {
  const b = e.target.closest('button');
  if (!b) return;
  modSort = b.dataset.sort;
  document.querySelectorAll('#mods-sort button').forEach(x => x.classList.toggle('active', x === b));
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
