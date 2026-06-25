#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string JsTabs = @"
// ====== Tab routing ==================================================
function switchTab(name) {
  if (name === activeTab) return;
  if (!document.querySelector('.tab[data-tab=""' + name + '""]')) return;
  activeTab = name;
  document.querySelectorAll('.tab').forEach(x => x.classList.toggle('active', x.dataset.tab === name));
  document.querySelectorAll('.tab-pane').forEach(p => p.classList.toggle('active', p.dataset.pane === name));
  renderAll();
}
document.querySelectorAll('.tab').forEach(t => t.addEventListener('click', () => switchTab(t.dataset.tab)));

// Keyboard 1-6 switches tabs.
document.addEventListener('keydown', e => {
  if (e.target.tagName === 'INPUT') return;
  const map = { '1': 'summary', '2': 'timeline', '3': 'lag', '4': 'observatory', '5': 'insights', '6': 'self', '7': 'memory' };
  if (map[e.key]) switchTab(map[e.key]);
  if (e.key === 'Escape') closeModCard();
});
";
}
