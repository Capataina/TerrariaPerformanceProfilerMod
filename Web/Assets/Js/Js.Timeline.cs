#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string JsTimeline = @"
// ====== TIMELINE ======================================================
document.getElementById('timeline-filter').addEventListener('click', e => {
  const b = e.target.closest('button');
  if (!b) return;
  timelineFilter = b.dataset.filter;
  document.querySelectorAll('#timeline-filter button').forEach(x => x.classList.toggle('active', x === b));
  renderTimeline();
});

function renderTimeline() {
  const root = document.getElementById('timelinelist');
  const sub = document.getElementById('timeline-sub');
  if (!lastSegments || !lastSegments.recent || lastSegments.recent.length === 0) {
    root.innerHTML = '<div class=""empty-line"">no segments closed yet</div>';
    sub.textContent = '—';
    return;
  }
  let segs = lastSegments.recent.slice();
  if (timelineFilter === 'boss')    segs = segs.filter(s => s.family === 'Boss');
  if (timelineFilter === 'biome')   segs = segs.filter(s => s.family === 'Biome');
  if (timelineFilter === 'weather') segs = segs.filter(s => s.family === 'Weather');
  if (timelineFilter === 'drama')   segs = segs.filter(s => s.deathCount > 0 || s.spikeCount > 0 || s.stallCount > 0);
  sub.textContent = segs.length + ' shown · newest first';

  root.innerHTML = segs.map((s, i) => {
    const segKey = s.startUnixMs + '_' + s.name;
    const isOpen = expandedSegments.has(segKey);
    const chips = [];
    if (s.deathCount > 0)    chips.push(`<span class='chip death'>☠ ${s.deathCount}</span>`);
    if (s.spikeCount > 0)    chips.push(`<span class='chip spike'>⚡ ${s.spikeCount}</span>`);
    if (s.stallCount > 0)    chips.push(`<span class='chip stall'>⏸ ${s.stallCount}</span>`);
    if (s.bossKillCount > 0) chips.push(`<span class='chip boss'>✓ ${s.bossKillCount}</span>`);
    const topMods = (s.topMods || []).map(m => `<span>${escapeHtml(m.name)} <span class='muted'>${fmtMs(m.ms)}ms</span></span>`).join('');
    return `<div class='tl-seg ${s.promoted ? 'promoted' : ''}' data-family='${s.family}' data-key='${escapeHtml(segKey)}'>
      <div class='tl-seg-main'>
        <span class='name'>${escapeHtml(s.name)}</span>
        <span class='dur'>${fmtDuration(s.durationMs)}</span>
        <span class='mspt'>${fmtMs(s.avgFrameMs)} ms/t</span>
        <span class='badge'>${fmtInt(s.ticks)} ticks</span>
        <span class='chips'>${chips.join('')}</span>
        <span class='topmods'>${topMods}</span>
      </div>
      <div class='tl-seg-detail ${isOpen ? '' : 'hidden'}'>
        <div class='det-row'><span>started</span><span class='v'>${new Date(s.startUnixMs).toLocaleTimeString()}</span></div>
        <div class='det-row'><span>ended</span><span class='v'>${new Date(s.endUnixMs).toLocaleTimeString()}</span></div>
        <div class='det-row'><span>family</span><span class='v'>${s.family}</span></div>
        <div class='det-row'><span>promoted</span><span class='v'>${s.promoted ? 'yes · ' + s.promotionReason : 'no'}</span></div>
        <div class='det-row'><span>frame total</span><span class='v'>${fmtMs(s.avgFrameMs * s.ticks)} ms</span></div>
        ${(s.topMods || []).map(m => `<div class='det-row'><span>${escapeHtml(m.name)}</span><span class='v'>${fmtMs(m.ms)} ms · ${(m.share*100).toFixed(1)}%</span></div>`).join('')}
      </div>
    </div>`;
  }).join('');

  root.querySelectorAll('.tl-seg').forEach(el => {
    el.addEventListener('click', () => {
      const k = el.dataset.key;
      if (expandedSegments.has(k)) expandedSegments.delete(k);
      else expandedSegments.add(k);
      renderTimeline();
    });
  });
}
";
}
