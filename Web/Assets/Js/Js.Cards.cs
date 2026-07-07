#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Popup card system (atlas S18, ui-overhaul P-C). Drawers stay for
    // list-adjacent browsing (Observatory, Insights); popups serve
    // MOMENT-shaped things — a boss fight, a minute, a stall cluster — where
    // a centred card the eye can screenshot beats a side panel. One card at a
    // time; esc / backdrop / × all close it.
    private const string JsCards = @"
// ====== Popup cards (S18) =============================================
function openCard(title, meta, bodyHtml) {
  closeCard();
  const backdrop = document.createElement('div');
  backdrop.className = 'card-backdrop';
  backdrop.id = 'popup-card';
  backdrop.innerHTML = `
    <div class='popup-card' role='dialog' aria-modal='true'>
      <header class='card-h'>
        <span class='card-title'>${escapeHtml(title)}</span>
        <span class='card-meta'>${escapeHtml(meta || '')}</span>
        <span class='card-close' title='close'>&times;</span>
      </header>
      <div class='card-body'>${bodyHtml}</div>
    </div>`;
  backdrop.addEventListener('click', e => { if (e.target === backdrop) closeCard(); });
  backdrop.querySelector('.card-close').addEventListener('click', closeCard);
  document.body.appendChild(backdrop);
  document.addEventListener('keydown', _cardEsc);
}
function _cardEsc(e) { if (e.key === 'Escape') closeCard(); }
function closeCard() {
  const el = document.getElementById('popup-card');
  if (el) el.remove();
  document.removeEventListener('keydown', _cardEsc);
}

// ---- The boss report card (flagship) ---------------------------------
// Opened from a Boss swimlane block or a heatmap boss overlay. Pulls the
// segment + its per-mod attribution from the already-polled payloads — no
// extra endpoint round-trip.
function openBossCard(family, key, startTick) {
  const k = segKey(family, key, startTick);
  let seg = null;
  for (const s of ((lastSegments && lastSegments.recent) || [])) {
    if (segKey(s.family, s.key, s.startTick) === k) { seg = s; break; }
  }
  if (!seg) {
    for (const s of ((lastSegments && lastSegments.open) || [])) {
      if (segKey(s.family, s.key, s.startTick) === k) {
        seg = Object.assign({}, s, { endUnixMs: 0, durationMs: (lastNow ? lastNow.unixMs : 0) - s.startUnixMs, _open: true });
        break;
      }
    }
  }
  if (!seg) return;

  let attr = null;
  for (const e of ((lastSegmentModAttr && lastSegmentModAttr.entries) || [])) {
    if (segKey(e.family, e.key, e.segmentStartTick) === k) { attr = e; break; }
  }

  const avg = seg.ticks > 0 && seg.totalFrameMs > 0 ? seg.totalFrameMs / seg.ticks : (seg.avgFrameMs || 0);
  const verdictCls = avg > 33 ? 'bad' : avg > 20 ? 'warn' : 'good';

  let statsHtml =
    statLine('duration', fmtDuration(seg.durationMs) + (seg._open ? ' (ongoing)' : '')) +
    statLine('avg frame during', avg > 0 ? fmtMs(avg) + ' ms' : '—', verdictCls) +
    statLine('spikes during', fmtInt(seg.spikeCount || 0)) +
    statLine('stalls during', fmtInt(seg.stallCount || 0)) +
    (seg.deathCount > 0 ? statLine('deaths', fmtInt(seg.deathCount), 'bad') : '') +
    (seg.bossKillCount > 0 ? statLine('outcome', 'defeated', 'good') : (seg._open ? '' : statLine('outcome', 'survived you', 'warn')));

  let modsHtml = '';
  if (attr && attr.perMod && attr.perMod.length > 0) {
    let total = 0;
    for (const m of attr.perMod) total += m.ms;
    const top = attr.perMod.slice().sort((a, b) => b.ms - a.ms).slice(0, 5);
    modsHtml = `<div class='card-sect'>costliest mods during the fight</div>` +
      top.map(m => {
        const share = total > 0 ? (m.ms / total * 100).toFixed(0) : '0';
        return `<div class='card-modrow'>
          <span class='dot' style='background:${modColor(m.modId)}'></span>
          <span class='nm'>${escapeHtml(m.modName)}</span>
          <span class='val'>${fmtMs(m.ms)} ms · ${share}%</span>
        </div>`;
      }).join('');
  }

  openCard(seg.name, (seg.family || '') + ' segment', statsHtml + modsHtml);
}
";
}
