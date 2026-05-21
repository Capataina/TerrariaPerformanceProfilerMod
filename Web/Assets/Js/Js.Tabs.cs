#nullable enable

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

// Keyboard 1-5 switches tabs.
document.addEventListener('keydown', e => {
  if (e.target.tagName === 'INPUT') return;
  const map = { '1': 'summary', '2': 'timeline', '3': 'lag', '4': 'insights', '5': 'self' };
  if (map[e.key]) switchTab(map[e.key]);
  if (e.key === 'Escape') closeModCard();
});
";
}
