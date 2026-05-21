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
    private const string JsSelf = @"
// ====== SELF TAB ======================================================
function renderSelf() {
  if (!lastSelf) return;
  const install = document.getElementById('self-install');
  const proc = document.getElementById('self-process');
  const back = document.getElementById('self-backend');
  const sevClass = lastSelf.severity === 'Severe' ? 'bad' : lastSelf.severity === 'Concerning' ? 'warn' : 'good';

  // Hero gauge
  renderSelfGauge(lastSelf);
  document.getElementById('hero-sev').textContent = lastSelf.severity.toLowerCase();
  document.getElementById('hero-sev').className = 'v ' + sevClass;
  document.getElementById('hero-bph').textContent = lastSelf.bytesPerHookKb.toFixed(1) + ' KB';
  const ratio = lastSelf.bytesPerHook / (36 * 1024);
  document.getElementById('hero-ratio').textContent = ratio.toFixed(2) + '× baseline';
  document.getElementById('hero-ratio').className = 'v ' + sevClass;
  document.getElementById('hero-hooks').textContent = fmtInt(lastSelf.installedHookCount);

  install.innerHTML = `
    <div class='self-row'><span class='k'>install delta</span><span class='v'>${lastSelf.installDeltaMb.toFixed(0)} MB</span></div>
    <div class='self-row'><span class='k'>bytes per hook</span><span class='v ${sevClass}'>${lastSelf.bytesPerHookKb.toFixed(1)} KB</span></div>
    <div class='self-row'><span class='k'>hook count</span><span class='v'>${fmtInt(lastSelf.installedHookCount)}</span></div>
    <div class='self-row'><span class='k'>vs 36KB baseline</span><span class='v ${sevClass}'>${ratio.toFixed(2)}× </span></div>
  `;
  // Footprint bar: visualize ratio against bands. Healthy 0-1.5x, Concerning 1.5-2.5x, Severe ≥2.5x.
  const r = Math.min(3.5, ratio);
  const fp = document.getElementById('footprint-bar');
  fp.innerHTML = `
    <span style='width: ${(Math.min(r, 1.5) / 3.5 * 100).toFixed(1)}%; background: var(--good);'></span>
    <span style='width: ${(Math.max(0, Math.min(r, 2.5) - 1.5) / 3.5 * 100).toFixed(1)}%; background: var(--amber);'></span>
    <span style='width: ${(Math.max(0, r - 2.5) / 3.5 * 100).toFixed(1)}%; background: var(--danger);'></span>
  `;

  proc.innerHTML = `
    <div class='self-row'><span class='k'>working set</span><span class='v'>${lastSelf.processWorkingSetMb.toFixed(0)} MB</span></div>
    <div class='self-row'><span class='k'>managed heap</span><span class='v'>${lastSelf.processManagedHeapMb.toFixed(0)} MB</span></div>
    <div class='self-row'><span class='k'>managed share</span><span class='v'>${(lastSelf.managedFractionOfWorkingSet * 100).toFixed(0)}%</span></div>
  `;
  // Split bar: managed / native portion of working set.
  const ws = lastSelf.processWorkingSetMb || 1;
  const managed = lastSelf.processManagedHeapMb || 0;
  const native = Math.max(0, ws - managed);
  const split = document.getElementById('self-split');
  split.innerHTML = `
    <span style='width: ${(managed / ws * 100).toFixed(1)}%; background: var(--accent);'></span>
    <span style='width: ${(native / ws * 100).toFixed(1)}%; background: var(--surface-2);'></span>
  `;

  back.innerHTML = `
    <div class='self-row'><span class='k'>backend</span><span class='v'>${lastSelf.backend}</span></div>
    <div class='self-row'><span class='k'>installed</span><span class='v ${lastSelf.installed ? 'good' : 'warn'}'>${lastSelf.installed ? 'yes' : 'pending'}</span></div>
  `;

  // Hook distribution per mod (uses /api/hooks if available, otherwise /api/mods category sums).
  renderHookDistribution();
}

function renderSelfGauge(self) {
  const g = document.querySelector('#self-gauge svg');
  if (!g) return;
  // Half-circle gauge from -PI to 0, mapped from 0..3.5 ratio.
  const ratio = self.bytesPerHook / (36 * 1024);
  const r = Math.min(3.5, ratio);
  const angle = -Math.PI + (r / 3.5) * Math.PI;
  const x = 50 + Math.cos(angle) * 40;
  const y = 50 + Math.sin(angle) * 40;
  const sevColor = self.severity === 'Severe' ? '#b94e58' : self.severity === 'Concerning' ? '#c97f3c' : '#4f9d6a';
  // Three colored arcs: green 0-1.5, amber 1.5-2.5, red 2.5-3.5
  const arc = (from, to, color) => {
    const a1 = -Math.PI + (from / 3.5) * Math.PI;
    const a2 = -Math.PI + (to / 3.5) * Math.PI;
    const x1 = 50 + Math.cos(a1) * 40, y1 = 50 + Math.sin(a1) * 40;
    const x2 = 50 + Math.cos(a2) * 40, y2 = 50 + Math.sin(a2) * 40;
    return `<path d='M ${x1} ${y1} A 40 40 0 0 1 ${x2} ${y2}' stroke='${color}' stroke-width='6' fill='none' stroke-linecap='round'/>`;
  };
  g.innerHTML = `
    ${arc(0, 1.5, '#4f9d6a')}
    ${arc(1.5, 2.5, '#c97f3c')}
    ${arc(2.5, 3.5, '#b94e58')}
    <circle cx='${x}' cy='${y}' r='4' fill='${sevColor}' stroke='#d6dae0' stroke-width='1'/>
    <text x='50' y='40' text-anchor='middle' fill='#d6dae0' font-family='Inter, sans-serif' font-size='8' font-weight='600'>${self.severity}</text>
    <text x='50' y='52' text-anchor='middle' fill='#6a727f' font-family='JetBrains Mono, monospace' font-size='5'>${ratio.toFixed(2)}× baseline</text>
  `;
}

function renderHookDistribution() {
  const root = document.getElementById('self-hookdist');
  const sub = document.getElementById('hookdist-sub');
  if (!lastHooks || !lastHooks.hooks || lastHooks.hooks.length === 0) {
    // Fall back: derive a rough hook count per mod from /api/mods if available.
    root.innerHTML = '<div class=""empty-line"">expand a mod on Summary to load /api/hooks — full hook list will appear here</div>';
    sub.textContent = 'not yet loaded';
    return;
  }
  const perMod = new Map();
  for (const h of lastHooks.hooks) {
    if (!perMod.has(h.modId)) perMod.set(h.modId, { id: h.modId, name: h.modName, count: 0, ms: 0 });
    const e = perMod.get(h.modId);
    e.count++;
    e.ms += h.cpuMs;
  }
  const sorted = [...perMod.values()].sort((a, b) => b.count - a.count).slice(0, 12);
  const max = sorted.length > 0 ? sorted[0].count : 1;
  sub.textContent = perMod.size + ' mods · ' + lastHooks.hooks.length + ' active hooks shown';
  root.innerHTML = sorted.map((m, i) => `
    <div class='hd-row'>
      <span class='rk'>${i + 1}</span>
      <span class='nm'>${escapeHtml(m.name)}</span>
      <span class='bar'><span style='width: ${(m.count / max * 100).toFixed(1)}%'></span></span>
      <span class='n'>${fmtInt(m.count)} hooks</span>
      <span class='mb'>${fmtMs(m.ms)} ms</span>
    </div>
  `).join('');
}
";
}
