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
  // The 1000/60 floor models the vsync cap: when a frame already fits the 16.67ms
  // budget, removing a mod's slice can't raise fps past 60. That makes the two fps
  // figures collapse to '60 vs 60' whenever both frame times sit under the cap, so
  // we only quote an fps clause when the two genuinely differ (see fpsClause below).
  const fpsNow = totalNow > 0 ? 1000 / Math.max(1000/60, totalNow) : 0;
  const fpsWithoutNow = totalNow > 0 ? 1000 / Math.max(1000/60, withoutNow) : 0;
  const fpsAvg = totalAvg > 0 ? 1000 / Math.max(1000/60, totalAvg) : 0;
  const fpsWithoutAvg = totalAvg > 0 ? 1000 / Math.max(1000/60, withoutAvg) : 0;

  // Build an honest fps clause for one with/without pair. When the two fps figures
  // round to the same integer the 'N vs N' template is meaningless, so we drop it
  // and state the cost in the ms delta the line above already carries. Two ways
  // this happens: the frame already fits the 16.67ms budget (the 60fps cap absorbs
  // the saving), or the slice is too small to move a whole frame per second.
  // `full` controls verbosity: the average is the primary sentence and carries the
  // full explanation; the live figure reuses this helper with full=false for a
  // terse secondary clause, so the explanation appears once, not twice.
  function fpsClause(withMod, withoutMod, msDelta, frameMs, full) {
    const a = Math.round(withoutMod), b = Math.round(withMod);
    if (a === b) {
      const capped = frameMs > 0 && frameMs <= 1000 / 60 + 0.001;
      if (!full) return capped ? `no measurable fps change` : `under 1 fps of difference`;
      return capped
        ? `no measurable fps change — the frame already fits the 60 fps budget, so the ${fmtMs(msDelta)} ms shows up as headroom rather than lost fps`
        : `under 1 fps of difference — the ${fmtMs(msDelta)} ms is too small to move the frame rate by a whole frame per second`;
    }
    return `the difference between <strong>${a} fps</strong> and <strong>${b} fps</strong>`;
  }
  // The live figure only earns its own fps note when it lands in a different fps
  // regime from the average — otherwise it would just restate the average sentence's
  // verdict verbatim, so we show the live value alone and let the verdict appear once.
  function fpsDiffers(withMod, withoutMod) { return Math.round(withMod) !== Math.round(withoutMod); }

  // Category breakdown.
  const cats = lastMods.categories || [];
  const catVals = mod.categories.map((v, i) => ({ v, i })).filter(c => c.v > 0.001).sort((a, b) => b.v - a.v);
  const catMax = catVals.length ? catVals[0].v : 1;
  // Median of the active categories — the perf-ramp reference, matching the
  // cascading tree's magnitude bar so cost reads with one colour idiom across
  // both surfaces (calm green at the median, hot toward the outliers).
  const catMedian = catVals.length ? catVals[Math.floor(catVals.length / 2)].v : 0;
  // Ordered by cost, biggest first — the breakdown should read top-down by impact.
  const catRows = catVals.map(({ v, i }) => row({
      cols: 'minmax(0, 1fr) minmax(0, 2fr) 5em',
      cells: [
        `<span class='nm'>${escapeHtml(cats[i] || 'cat:' + i)}</span>`,
        cellBar(v / catMax, perfColor(catMedian > 0 ? v / catMedian : 0)),
        `<span class='mc-catv'>${fmtMs(v)} ms</span>`,
      ],
    }));

  const sharePct = (mod.cpuMs / (sorted.reduce((s, m) => s + m.cpuMs, 0) || 1) * 100).toFixed(1);

  body.innerHTML =
    `<div class='mc-hero'><span class='mc-swatch' style='background:${modColor(mod.id)}'></span>` +
      `<div class='mc-hero-meta'><span class='mc-hero-val'>${sharePct}%</span>` +
      `<span class='mc-hero-lbl'>Of all mod CPU, this tick</span></div></div>` +
    sectionBlock('cost · live', statGrid([
      statTile({ k: 'cpu now', v: fmtMs(mod.cpuMs) + ' ms/t', big: true, sub: 'This tick' }),
      statTile({ k: 'cpu avg', v: fmtMs(mod.avgCpuMs) + ' ms/t', big: true, sub: 'Session' }),
      statTile({ k: 'alloc/t', v: lastMods.tracksAllocations ? fmtBytes(mod.allocBytes) : '—', sub: lastMods.tracksAllocations ? 'Tracked' : 'Off' }),
      statTile({ k: 'share', v: sharePct + '%', vClass: 'accent', sub: 'Of all mods' }),
    ], { cols: '1fr 1fr' })) +

    sectionBlock('frame impact · marginal contribution', callout(
      `This mod adds <strong>${fmtMs(mod.avgCpuMs)} ms</strong> to the average frame, ` +
      `which is ${fpsClause(fpsAvg, fpsWithoutAvg, mod.avgCpuMs, totalAvg, true)}. ` +
      `<span class='muted'>Right now: <strong>${fmtMs(mod.cpuMs)} ms</strong>` +
        `${fpsDiffers(fpsNow, fpsWithoutNow) ? ', ' + fpsClause(fpsNow, fpsWithoutNow, mod.cpuMs, totalNow, false) : ''}.</span>` +
      `<br/><span class='muted'>Caveat: a marginal upper bound. Sibling mods may do less work when this mod's content isn't present, so the real-world delta is usually smaller. This describes measured cost, not a recommendation.</span>`)) +

    sectionBlock('category breakdown',
      catRows.length ? rowList(catRows) : `<span class='muted'>No per-category activity yet</span>`) +

    sectionBlock('next steps', callout(
      `Expand this mod in the cascading tree below to see which specific hooks inside it are doing the work.`));
  card.classList.remove('hidden');
}

function closeModCard() {
  document.getElementById('modcard').classList.add('hidden');
}
document.getElementById('mc-close').addEventListener('click', closeModCard);
";
}
