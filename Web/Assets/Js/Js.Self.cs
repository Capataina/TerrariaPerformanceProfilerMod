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
    // Self tab renderer, composed entirely from the component library:
    // gauge() for the health dial, statTile/statGrid for the hero metrics,
    // statLine for the panel rows, splitBar for the footprint + managed/native
    // bars, row()/rowList() for the hook distribution. No bespoke markup.
    private const string JsSelf = @"
// ====== SELF TAB ======================================================
function renderSelf() {
  if (!lastSelf) return;
  const s = lastSelf;
  const sevClass = s.severity === 'Severe' ? 'bad' : s.severity === 'Concerning' ? 'warn' : 'good';
  const ratio = s.bytesPerHook / (36 * 1024);   // vs the 36 KB/hook baseline

  // ---- hero gauge (tokenised bands; replaces the hardcoded-hex SVG) ----
  const gEl = document.getElementById('self-gauge');
  if (gEl) gEl.innerHTML = gauge({
    ratio: Math.min(1, ratio / 3.5), sweep: 180, w: 200, stroke: 16,
    bands: [
      { to: 1.5 / 3.5, color: 'var(--good)' },
      { to: 2.5 / 3.5, color: 'var(--orange)' },
      { to: 1, color: 'var(--danger)' },
    ],
    // Reference tick at the healthy->concerning boundary (1.5x baseline on the
    // 0..3.5 scale), so the fill is read as 'how far into budget' against the
    // same threshold the bands already use, not a free-floating arc.
    ref: 1.5 / 3.5,
    color: s.severity === 'Severe' ? 'var(--danger)' : s.severity === 'Concerning' ? 'var(--orange)' : 'var(--good)',
    centerValue: ratio.toFixed(2) + '×', centerSub: s.severity.toLowerCase(),
  });

  // ---- hero stat tiles ----
  const hs = document.getElementById('self-herostats');
  if (hs) hs.innerHTML = statGrid([
    statTile({ k: 'severity', v: s.severity.toLowerCase(), vClass: sevClass }),
    statTile({ k: 'bytes / hook', v: dash(s.bytesPerHookKb, v => v.toFixed(1) + ' KB') }),
    statTile({ k: 'vs baseline', v: ratio.toFixed(2) + '×', vClass: sevClass }),
    statTile({ k: 'hooks installed', v: dash(s.installedHookCount, fmtInt) }),
  ]);

  // ---- install footprint ----
  document.getElementById('self-install').innerHTML =
    statLine('install delta', dash(s.installDeltaMb, v => v.toFixed(0) + ' MB')) +
    statLine('bytes per hook', dash(s.bytesPerHookKb, v => v.toFixed(1) + ' KB'), sevClass) +
    statLine('hook count', dash(s.installedHookCount, fmtInt)) +
    statLine('vs 36 KB baseline', ratio.toFixed(2) + '×', sevClass);
  const r = Math.min(3.5, ratio);
  document.getElementById('footprint-bar').innerHTML = splitBar([
    { frac: Math.min(r, 1.5) / 3.5, color: 'var(--good)', label: 'healthy' },
    { frac: Math.max(0, Math.min(r, 2.5) - 1.5) / 3.5, color: 'var(--amber)', label: 'concerning' },
    { frac: Math.max(0, r - 2.5) / 3.5, color: 'var(--danger)', label: 'severe' },
  ], { tall: true });

  // ---- process context (managed / native split) ----
  // The managed share is the panel's headline answer ('managed heap vs total
  // working set'), so it leads as a hero stat tile; the two raw byte figures
  // read as its supporting detail below, not as three equal rows.
  document.getElementById('self-process').innerHTML =
    `<div class='self-share-hero'>` +
    statTile({ k: 'managed share', v: dash(s.managedFractionOfWorkingSet, v => (v * 100).toFixed(0) + '%'),
               big: true, sub: 'of total working set' }) +
    `</div>` +
    statLine('working set', dash(s.processWorkingSetMb, v => v.toFixed(0) + ' MB')) +
    statLine('managed heap', dash(s.processManagedHeapMb, v => v.toFixed(0) + ' MB'));
  const ws = s.processWorkingSetMb || 1, managed = s.processManagedHeapMb || 0, native = Math.max(0, ws - managed);
  // managed = near-white accent fill; native = a clear mid-grey (--muted), not
  // the near-panel --surface-2 that vanishes against the dark panel and reads as
  // 'one bright swatch' next to managed. Two distinct neutrals on the mono ramp.
  document.getElementById('self-split').innerHTML = splitBar([
    { frac: managed / ws, color: 'var(--accent)', label: 'managed', value: fmtInt(Math.round(managed)) + ' MB' },
    { frac: native / ws, color: 'var(--muted)', label: 'native', value: fmtInt(Math.round(native)) + ' MB' },
  ], { tall: true }) + splitLegend([
    { color: 'var(--accent)', label: 'managed' }, { color: 'var(--muted)', label: 'native' },
  ]);

  // ---- backend ----
  document.getElementById('self-backend').innerHTML =
    statLine('backend', s.backend) +
    statLine('installed', s.installed ? 'yes' : 'pending', s.installed ? 'good' : 'warn');

  renderHookDistribution();
}

function renderHookDistribution() {
  const root = document.getElementById('self-hookdist');
  const sub = document.getElementById('hookdist-sub');
  // Per-mod hook COUNTS come straight from /api/memory (m.hookCount), which is
  // polled regardless of the active tab — so this populates the moment the Self
  // tab opens, with no click and no Summary mod-expand. It used to aggregate the
  // heavy per-hook /api/hooks payload (gated to Summary+expand), which is why it
  // sat in a 'not yet loaded' state until you went and triggered it elsewhere.
  // The full per-hook list still loads lazily on the Summary mod tree, where the
  // hook-level detail belongs; this panel only needs the top mods by count.
  const mods = lastMemory && lastMemory.mods;
  if (!mods || mods.length === 0) {
    root.innerHTML = emptyState('loading hook distribution…');
    sub.textContent = 'loading';
    return;
  }
  // Join per-mod cost (ms) from /api/mods when present, so the secondary bar shows
  // where hook count and cost diverge; absent that, the panel still stands on the
  // hook-count bar alone.
  const cpuById = new Map();
  if (lastMods && lastMods.mods) for (const m of lastMods.mods) cpuById.set(m.id, m.cpuMs || 0);

  const entries = mods
    .filter(m => (m.hookCount || 0) > 0)
    .map(m => ({ id: m.id, name: m.name, count: m.hookCount || 0, ms: cpuById.get(m.id) || 0 }))
    .sort((a, b) => b.count - a.count)
    .slice(0, 12);
  if (entries.length === 0) {
    root.innerHTML = emptyState('no installed hooks recorded yet');
    sub.textContent = '0 mods';
    return;
  }
  const max = entries[0].count || 1;
  const maxMs = entries.reduce((m, e) => Math.max(m, e.ms), 0);
  const hasMs = maxMs > 0;
  sub.textContent = mods.length + ' mods · top ' + entries.length + ' by hook count';
  // The count bar is sorted-by (the primary metric); ms gets its own faint
  // secondary cellBar on an independent scale so where cost and hook count
  // diverge is visible instead of two bare number columns parsed by eye.
  const cols = hasMs
    ? '2em minmax(0,1.4fr) minmax(0,2.4fr) 6em minmax(0,1fr) 5.5em'
    : '2em minmax(0,1.6fr) minmax(0,3fr) 6em';
  root.innerHTML = rowList(entries.map((m, i) => row({
    cols,
    cells: hasMs ? [
      `<span class='rk'>${i + 1}</span>`,
      `<span class='nm'>${escapeHtml(m.name)}</span>`,
      cellBar(m.count / max, modColor(m.id)),
      `<span style='text-align:right'>${fmtInt(m.count)} hooks</span>`,
      cellBar(m.ms / maxMs, modColor(m.id)),
      `<span style='text-align:right;color:var(--muted)'>${fmtMs(m.ms)} ms</span>`,
    ] : [
      `<span class='rk'>${i + 1}</span>`,
      `<span class='nm'>${escapeHtml(m.name)}</span>`,
      cellBar(m.count / max, modColor(m.id)),
      `<span style='text-align:right'>${fmtInt(m.count)} hooks</span>`,
    ],
  })));
}
";
}
