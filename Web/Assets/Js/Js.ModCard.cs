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
    private const string JsModCard = @"
// ====== Mod card slide-in =============================================
function openModCard(modId) {
  if (!lastMods || !lastMods.mods) return;
  const mod = lastMods.mods.find(m => m.id === modId);
  if (!mod) return;
  const card = document.getElementById('modcard');
  const body = document.getElementById('mc-body');
  document.getElementById('mc-name').textContent = mod.name;

  // Rank.
  const sorted = lastMods.mods.slice().sort((a, b) => b.cpuMs - a.cpuMs);
  const rank = sorted.findIndex(m => m.id === modId) + 1;
  document.getElementById('mc-rank').textContent = '#' + rank + ' of ' + sorted.length;

  // Predicted-FPS-without-this-mod math.
  const totalNow = lastNow && lastNow.frameMs ? lastNow.frameMs : 0;
  const totalAvg = lastNow && lastNow.avg30sMs ? lastNow.avg30sMs : 0;
  const withoutNow = Math.max(0.1, totalNow - mod.cpuMs);
  const withoutAvg = Math.max(0.1, totalAvg - mod.avgCpuMs);
  const fpsNow = totalNow > 0 ? 1000 / Math.max(1000/60, totalNow) : 0;
  const fpsWithoutNow = totalNow > 0 ? 1000 / Math.max(1000/60, withoutNow) : 0;
  const fpsAvg = totalAvg > 0 ? 1000 / Math.max(1000/60, totalAvg) : 0;
  const fpsWithoutAvg = totalAvg > 0 ? 1000 / Math.max(1000/60, withoutAvg) : 0;

  // Category breakdown.
  const cats = lastMods.categories || [];
  const catMax = Math.max(...mod.categories);
  const catRows = mod.categories.map((v, i) => {
    if (v <= 0.001) return '';
    return row({
      cols: 'minmax(0, 1fr) minmax(0, 2fr) 5em',
      cells: [
        `<span class='nm'>${escapeHtml(cats[i] || 'cat:' + i)}</span>`,
        cellBar(v / catMax, 'var(--cpu)'),
        `<span class='mc-catv'>${fmtMs(v)} ms</span>`,
      ],
    });
  }).filter(Boolean);

  const sharePct = (mod.cpuMs / (sorted.reduce((s, m) => s + m.cpuMs, 0) || 1) * 100).toFixed(1);

  body.innerHTML =
    sectionBlock('cost · live', statGrid([
      statTile({ k: 'cpu now', v: fmtMs(mod.cpuMs) + ' ms/t', big: true, sub: 'this tick' }),
      statTile({ k: 'cpu avg', v: fmtMs(mod.avgCpuMs) + ' ms/t', big: true, sub: 'session' }),
      statTile({ k: 'alloc/t', v: lastMods.tracksAllocations ? fmtBytes(mod.allocBytes) : '—', sub: lastMods.tracksAllocations ? 'tracked' : 'off' }),
      statTile({ k: 'share', v: sharePct + '%', vClass: 'accent', sub: 'of all mods' }),
    ], { cols: '1fr 1fr' })) +

    sectionBlock('frame impact · marginal contribution', callout(
      `this mod adds <strong>${fmtMs(mod.avgCpuMs)} ms</strong> to the average frame ` +
      `(<strong>${fmtMs(mod.cpuMs)} ms</strong> right now). at your current frame time that is the ` +
      `difference between <strong>${fpsWithoutAvg.toFixed(0)} fps</strong> and ` +
      `<strong>${fpsAvg.toFixed(0)} fps</strong> on average, ` +
      `and <strong>${fpsWithoutNow.toFixed(0)}</strong> vs <strong>${fpsNow.toFixed(0)}</strong> live.` +
      `<br/><span class='muted'>caveat: a marginal upper bound. sibling mods may do less work when this mod's content isn't present, so the real-world delta is usually smaller. this describes measured cost, not a recommendation.</span>`)) +

    sectionBlock('category breakdown',
      catRows.length ? rowList(catRows) : `<span class='muted'>no per-category activity yet</span>`) +

    sectionBlock('next steps', callout(
      `click the row on the Summary tab to expand the cascading tree — drill into which specific hooks inside this mod are doing the work.`));
  card.classList.remove('hidden');
}

function closeModCard() {
  document.getElementById('modcard').classList.add('hidden');
}
document.getElementById('mc-close').addEventListener('click', closeModCard);
";
}
