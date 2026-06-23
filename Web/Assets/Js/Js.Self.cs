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
  document.getElementById('self-process').innerHTML =
    statLine('working set', dash(s.processWorkingSetMb, v => v.toFixed(0) + ' MB')) +
    statLine('managed heap', dash(s.processManagedHeapMb, v => v.toFixed(0) + ' MB')) +
    statLine('managed share', dash(s.managedFractionOfWorkingSet, v => (v * 100).toFixed(0) + '%'));
  const ws = s.processWorkingSetMb || 1, managed = s.processManagedHeapMb || 0, native = Math.max(0, ws - managed);
  document.getElementById('self-split').innerHTML = splitBar([
    { frac: managed / ws, color: 'var(--accent)', label: 'managed', value: fmtInt(Math.round(managed)) + ' MB' },
    { frac: native / ws, color: 'var(--surface-2)', label: 'native', value: fmtInt(Math.round(native)) + ' MB' },
  ], { tall: true }) + splitLegend([
    { color: 'var(--accent)', label: 'managed' }, { color: 'var(--surface-2)', label: 'native' },
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
  if (!lastHooks || !lastHooks.hooks || lastHooks.hooks.length === 0) {
    root.innerHTML = emptyState('expand a mod on Summary to load /api/hooks — the full hook list appears here');
    sub.textContent = 'not yet loaded';
    return;
  }
  const perMod = new Map();
  for (const h of lastHooks.hooks) {
    if (!perMod.has(h.modId)) perMod.set(h.modId, { id: h.modId, name: h.modName, count: 0, ms: 0 });
    const e = perMod.get(h.modId); e.count++; e.ms += h.cpuMs;
  }
  const sorted = [...perMod.values()].sort((a, b) => b.count - a.count).slice(0, 12);
  const max = sorted.length ? sorted[0].count : 1;
  sub.textContent = perMod.size + ' mods · ' + lastHooks.hooks.length + ' active hooks shown';
  const cols = '2em minmax(0,1.4fr) minmax(0,3fr) 7em 6em';
  root.innerHTML = rowList(sorted.map((m, i) => row({
    cols,
    cells: [
      `<span class='rk'>${i + 1}</span>`,
      `<span class='nm'>${escapeHtml(m.name)}</span>`,
      cellBar(m.count / max, 'var(--accent)'),
      `<span style='text-align:right'>${fmtInt(m.count)} hooks</span>`,
      `<span style='text-align:right;color:var(--muted)'>${fmtMs(m.ms)} ms</span>`,
    ],
  })));
}
";
}
