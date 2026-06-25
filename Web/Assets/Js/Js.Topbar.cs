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
    private const string JsTopbar = @"
// ====== Topbar / footer ==============================================
const previousValues = {};
function renderTopbar() {
  if (!lastNow || !lastNow.worldLoaded) {
    ['ts-tick','ts-frame','ts-avg','ts-gc','ts-backend'].forEach(id => document.getElementById(id).textContent = '—');
    return;
  }
  setTopstat('ts-tick',    '#' + fmtInt(lastNow.tickIndex));
  setTopstat('ts-frame',   fmtMs(lastNow.frameMs) + 'ms');
  setTopstat('ts-avg',     fmtMs(lastNow.avg30sMs) + 'ms');
  setTopstat('ts-gc',      fmtMs(lastNow.gcMs) + 'ms');
  setTopstat('ts-backend', lastNow.backend || '—');
}
function setTopstat(id, v) {
  const el = document.getElementById(id);
  if (previousValues[id] !== v) {
    el.classList.remove('flash'); void el.offsetWidth; el.classList.add('flash');
    previousValues[id] = v;
  }
  el.textContent = v;
}
function renderFooter() {
  document.getElementById('foot-clock').textContent = new Date().toLocaleTimeString();
  document.getElementById('foot-mode').textContent = lastNow && lastNow.worldLoaded
    ? lastNow.npcCount + ' npc · ' + lastNow.projectileCount + ' proj · ' + lastNow.dustCount + ' dust'
    : 'idle';
}

// ====== Master render dispatcher =====================================
function renderAll() {
  switch (activeTab) {
    case 'summary':  renderSummary();  break;
    case 'timeline': renderTimeline(); break;
    case 'lag':      renderLag();      break;
    case 'observatory': renderObservatory(); break;
    case 'insights': renderInsights(); break;
    case 'self':     renderSelf();     break;
    case 'memory':   renderMemory();   break;
  }
}
";
}
