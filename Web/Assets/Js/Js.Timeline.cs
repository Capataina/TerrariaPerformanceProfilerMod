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
    // Timeline renderers — Wave 4 visualisation layer.
    //
    // Wave 4 replaces the plain Wave-3 renderers with richer shapes:
    //   tl-heatstrip    SVG seismograph waveform (path + peak triangles)
    //   tl-transitions  per-kind glyphs (moon, triangle, square, portal, gem)
    //   tl-gantt lanes  reused — already creative enough at Wave 3
    //   tl-attendance   nested treemap (modded vs vanilla → per-mod cells)
    //   tl-deaths       inline SVG icon events (skull / sword / bolt / potion / arrow)
    //   tl-chronicle    horizontal film-strip narrative ribbon
    //
    // Caching: every renderer keys its last input by a cheap signature so a
    // poll that returns identical data doesn't re-build the DOM. Hot-path
    // overhead is irrelevant here (browser side) but DOM churn is, since
    // the F9 overlay shares browser resources with the game.
    private const string JsTimeline = @"
// ====== TIMELINE ======================================================
let lastSegmentLifetime = null;
let lastSegmentModAttr  = null;
let lastTransitions     = null;
let lastActivityStrip   = null;
let lastAttendance      = null;
let lastDeaths          = null;
let lastChronicle       = null;
let selectedSegmentKey  = null;   // 'family|key|startTick'

// Render-signature cache — skip rebuild when input didn't change.
const _tlSig = {
  heatstrip: '', transitions: '', swimlanes: '', detail: '',
  attendance: '', deaths: '', chronicle: ''
};

function segKey(family, key, startTick) {
  return (family || '') + '|' + (key != null ? key : '') + '|' + (startTick != null ? startTick : '');
}

function timelineWindow() {
  let s = Infinity, e = -Infinity;
  if (lastSegments) {
    for (const sg of (lastSegments.recent || [])) {
      if (sg.startUnixMs < s) s = sg.startUnixMs;
      if (sg.endUnixMs   > e) e = sg.endUnixMs;
    }
    for (const sg of (lastSegments.open || [])) {
      if (sg.startUnixMs < s) s = sg.startUnixMs;
      const nowMs = lastNow ? lastNow.unixMs : Date.now();
      const endMs = sg.startUnixMs + (sg.elapsedMs || 0);
      if (endMs > e) e = endMs;
      if (nowMs > e) e = nowMs;
    }
  }
  if (lastTransitions) {
    for (const t of (lastTransitions.transitions || [])) {
      if (t.unixMs < s) s = t.unixMs;
      if (t.unixMs > e) e = t.unixMs;
    }
  }
  if (lastActivityStrip) {
    for (const m of (lastActivityStrip.minutes || [])) {
      if (m.unixMs < s) s = m.unixMs;
      if (m.unixMs + 60000 > e) e = m.unixMs + 60000;
    }
  }
  if (!isFinite(s) || !isFinite(e) || e <= s) {
    const now = lastNow ? lastNow.unixMs : Date.now();
    s = now - 10 * 60 * 1000;
    e = now;
  }
  return { startMs: s, endMs: e, spanMs: Math.max(1, e - s) };
}

function pctOf(ms, win) {
  return ((ms - win.startMs) / win.spanMs) * 100;
}

// ---- T4: heat strip — SVG seismograph waveform ----------------------
function renderTimelineHeatstrip() {
  const root = document.getElementById('tl-heatstrip');
  if (!root) return;
  const buckets = (lastActivityStrip && lastActivityStrip.minutes) || [];
  const sig = buckets.length + ':' + (buckets.length ? (buckets[0].minuteIndex + '|' + buckets[buckets.length-1].minuteIndex + '|' + buckets[buckets.length-1].avgFrameMs.toFixed(2)) : '');
  if (sig === _tlSig.heatstrip) return;
  _tlSig.heatstrip = sig;

  if (buckets.length === 0) {
    root.innerHTML = `<div class='tl-empty'>no activity buckets yet</div>`;
    return;
  }

  // Build a path that runs left to right, mirrored vertically into a
  // tape-like waveform. avgFrameMs is normalised; peaks pushed above as
  // small triangle markers when a bucket is locally maximal.
  let maxMs = 0;
  for (const b of buckets) if (b.avgFrameMs > maxMs) maxMs = b.avgFrameMs;
  if (maxMs <= 0) maxMs = 1;

  const W = 1000, H = 60;     // viewBox; SVG scales to container width via preserveAspectRatio
  const mid = H / 2;
  const usable = (H / 2) - 4;
  const n = buckets.length;

  // Upper / lower halves of the waveform.
  let topPts = '', botPts = '';
  const xs = [];
  const ys = [];
  for (let i = 0; i < n; i++) {
    const t = Math.min(1, buckets[i].avgFrameMs / maxMs);
    const x = (n === 1) ? (W / 2) : (i * (W / (n - 1)));
    const y = t * usable;
    xs.push(x); ys.push(y);
    topPts += `${x.toFixed(1)},${(mid - y).toFixed(1)} `;
    botPts += `${x.toFixed(1)},${(mid + y).toFixed(1)} `;
  }
  // Filled tape: top edge L→R, bottom edge R→L
  let tape = `M ${xs[0]},${(mid - ys[0]).toFixed(1)} `;
  for (let i = 1; i < n; i++) tape += `L ${xs[i].toFixed(1)},${(mid - ys[i]).toFixed(1)} `;
  for (let i = n - 1; i >= 0; i--) tape += `L ${xs[i].toFixed(1)},${(mid + ys[i]).toFixed(1)} `;
  tape += 'Z';

  // Peak markers: local maxima with t >= 0.6 of session max.
  let peaks = '';
  for (let i = 1; i < n - 1; i++) {
    const t = buckets[i].avgFrameMs / maxMs;
    if (t < 0.6) continue;
    if (buckets[i].avgFrameMs >= buckets[i-1].avgFrameMs && buckets[i].avgFrameMs >= buckets[i+1].avgFrameMs) {
      const x = xs[i], y = (mid - ys[i]) - 2;
      peaks += `<polygon points='${x},${y - 5} ${x - 4},${y} ${x + 4},${y}' class='peak'/>`;
    }
  }

  // Spike / stall counts shown as tiny dots along the bottom axis.
  let dots = '';
  for (let i = 0; i < n; i++) {
    const b = buckets[i];
    if (b.spikeCount > 0) {
      dots += `<circle cx='${xs[i]}' cy='${H - 3}' r='${Math.min(3, 1 + b.spikeCount * 0.4)}' class='spike-dot'/>`;
    }
    if (b.stallCount > 0) {
      dots += `<circle cx='${xs[i]}' cy='${H - 3}' r='${Math.min(4, 1.5 + b.stallCount * 0.6)}' class='stall-dot'/>`;
    }
  }

  // Tooltips: invisible rects per bucket carry the per-minute summary.
  let hots = '';
  const cellW = W / Math.max(1, n);
  for (let i = 0; i < n; i++) {
    const b = buckets[i];
    const tip = `min ${b.minuteIndex}: ${fmtMs(b.avgFrameMs)} ms/t, ${b.segmentCount} segs, ${b.spikeCount} spikes, ${b.stallCount} stalls`;
    hots += `<rect x='${(xs[i] - cellW/2).toFixed(1)}' y='0' width='${cellW.toFixed(1)}' height='${H}' class='hot'><title>${escapeHtml(tip)}</title></rect>`;
  }

  root.innerHTML =
    `<svg viewBox='0 0 ${W} ${H}' preserveAspectRatio='none' class='tl-seismo'>
       <line x1='0' y1='${mid}' x2='${W}' y2='${mid}' class='axis-mid'/>
       <path d='${tape}' class='tape'/>
       <polyline points='${topPts}' class='edge'/>
       <polyline points='${botPts}' class='edge'/>
       ${peaks}
       ${dots}
       ${hots}
     </svg>`;
}

// ---- T3: transitions track — per-kind SVG glyphs --------------------
function transitionGlyph(type) {
  // Pick a shape family per transition kind. type strings look like
  // 'weather:BloodMoon' / 'biome:Jungle' / 'hardmode' / 'invasion:Goblin' / 'subworld:...'
  // We split on ':' and route by the head; tail is purely descriptive.
  const head = (type || '').split(':')[0].toLowerCase();
  switch (head) {
    case 'weather':
      // half-moon glyph — celestial-shift metaphor
      return `<path d='M -4 0 A 4 4 0 1 0 4 0 A 3 3 0 1 1 -4 0 Z' class='gx weather'/>`;
    case 'biome':
      // hexagon — biome-cell metaphor
      return `<polygon points='-4,-2.3 0,-4.6 4,-2.3 4,2.3 0,4.6 -4,2.3' class='gx biome'/>`;
    case 'hardmode':
      // upward triangle — escalation
      return `<polygon points='0,-5 4.5,3 -4.5,3' class='gx hardmode'/>`;
    case 'invasion':
      // small square rotated 45 (axes-aligned diamond, distinct from weather moon)
      return `<rect x='-3.5' y='-3.5' width='7' height='7' transform='rotate(45)' class='gx invasion'/>`;
    case 'subworld':
      // portal arc — concentric arcs
      return `<g class='gx subworld'><circle cx='0' cy='0' r='4.5' class='ring outer'/><circle cx='0' cy='0' r='2.5' class='ring inner'/></g>`;
    default:
      // generic — small chevron
      return `<polyline points='-4,2 0,-2 4,2' class='gx generic'/>`;
  }
}

function renderTimelineTransitions() {
  const root = document.getElementById('tl-transitions');
  if (!root) return;
  const list = (lastTransitions && lastTransitions.transitions) || [];
  const win = timelineWindow();
  const sig = list.length + ':' + win.startMs + ':' + win.endMs + ':' + (list.length ? list[list.length-1].tickIndex : '');
  if (sig === _tlSig.transitions) return;
  _tlSig.transitions = sig;

  if (list.length === 0) {
    root.innerHTML = `<div class='tl-empty'>no transitions yet</div>`;
    return;
  }
  // Inline SVG track scaled to container; viewBox uses pct-derived xs.
  const W = 1000, H = 24;
  let body = `<line x1='0' y1='${H/2}' x2='${W}' y2='${H/2}' class='tx-rail'/>`;
  for (const t of list) {
    const leftPct = Math.max(0, Math.min(100, pctOf(t.unixMs, win)));
    const x = (leftPct / 100) * W;
    const tip = `${t.type}: ${t.from} → ${t.to} (tick ${t.tickIndex})`;
    body += `<g transform='translate(${x.toFixed(1)} ${H/2})' data-type='${escapeHtml(t.type)}'>${transitionGlyph(t.type)}<title>${escapeHtml(tip)}</title></g>`;
  }
  root.innerHTML = `<svg viewBox='0 0 ${W} ${H}' preserveAspectRatio='none' class='tl-tx-svg'>${body}</svg>`;
}

// ---- T1+T2: swimlanes ------------------------------------------------
function renderTimelineSwimlanes() {
  const win = timelineWindow();
  const families = ['Biome','Weather','Boss','Invasion','Subworld'];
  const byFamily = {};
  for (const f of families) byFamily[f] = [];

  const lifetimeIx = new Map();
  if (lastSegmentLifetime && lastSegmentLifetime.entries) {
    for (const e of lastSegmentLifetime.entries) {
      lifetimeIx.set(segKey(e.family, e.key, e.segmentStartTick), e);
    }
  }
  const attrIx = new Map();
  if (lastSegmentModAttr && lastSegmentModAttr.entries) {
    for (const e of lastSegmentModAttr.entries) {
      attrIx.set(segKey(e.family, e.key, e.segmentStartTick), e);
    }
  }

  const segs = (lastSegments && lastSegments.recent) || [];
  for (const s of segs) {
    if (!byFamily[s.family]) continue;
    byFamily[s.family].push(Object.assign({}, s, { _open: false }));
  }
  const nowMs = lastNow ? lastNow.unixMs : Date.now();
  for (const s of ((lastSegments && lastSegments.open) || [])) {
    if (!byFamily[s.family]) continue;
    byFamily[s.family].push({
      family: s.family, key: s.key, name: s.name,
      startUnixMs: s.startUnixMs,
      endUnixMs: nowMs,
      startTick: s.startTick,
      durationMs: nowMs - s.startUnixMs,
      ticks: s.ticks,
      avgFrameMs: 0,
      spikeCount: s.spikeCount, stallCount: s.stallCount, deathCount: s.deathCount,
      bossKillCount: 0, promoted: false,
      topMods: s.topModName ? [{ id: s.topModId, name: s.topModName, ms: 0, share: 0 }] : [],
      _open: true,
    });
  }

  // signature
  let sig = win.startMs + ':' + win.endMs + ':' + (selectedSegmentKey || '');
  for (const f of families) sig += ':' + f + '=' + byFamily[f].length + ',' + (byFamily[f].length ? byFamily[f][byFamily[f].length-1].startTick : '');
  if (sig === _tlSig.swimlanes) return;
  _tlSig.swimlanes = sig;

  for (const f of families) {
    const lane = document.getElementById('tl-lane-' + f.toLowerCase());
    if (!lane) continue;
    const arr = byFamily[f];
    if (arr.length === 0) { lane.innerHTML = ''; continue; }
    lane.innerHTML = arr.map(s => {
      const left = pctOf(s.startUnixMs, win);
      const right = pctOf(s.endUnixMs, win);
      const width = Math.max(0.2, right - left);
      const k = segKey(s.family, s.key, s.startTick);
      const lifetime = lifetimeIx.get(k);
      const attr = attrIx.get(k);
      const delta = lifetime ? lifetime.deltaFraction : null;
      const outlier = (delta != null && Math.abs(delta) > 0.25) ? 'outlier' : '';
      const selected = (k === selectedSegmentKey) ? 'selected' : '';
      let waterfall = '';
      if (attr && attr.perMod && attr.perMod.length > 0) {
        let total = 0;
        for (const m of attr.perMod) total += m.ms;
        if (total > 0) {
          const top = attr.perMod.slice().sort((a,b)=>b.ms-a.ms).slice(0, 4);
          waterfall = `<div class='waterfall'>` + top.map(m => {
            const w = (m.ms / total) * 100;
            return `<span style='width:${w.toFixed(2)}%;background:${modColor(m.modId)}' title='${escapeHtml(m.modName)} ${fmtMs(m.ms)}ms'></span>`;
          }).join('') + `</div>`;
        }
      }
      let badge = '';
      if (delta != null) {
        const sign = delta >= 0 ? '+' : '';
        badge = `<span class='badge'>${sign}${(delta*100).toFixed(0)}%</span>`;
      }
      const tip = `${s.name} — ${fmtDuration(s.durationMs)} · ${fmtMs(s.avgFrameMs)} ms/t`;
      return `<div class='tl-segment ${outlier} ${selected}' data-family='${s.family}' data-key='${escapeHtml(k)}'
        style='left:${left.toFixed(2)}%;width:${width.toFixed(2)}%' title='${escapeHtml(tip)}'>
        <span class='lbl'>${escapeHtml(s.name)}</span>
        ${waterfall}
        ${badge}
      </div>`;
    }).join('');
    lane.querySelectorAll('.tl-segment').forEach(el => {
      el.addEventListener('click', () => {
        const k = el.dataset.key;
        selectedSegmentKey = (selectedSegmentKey === k) ? null : k;
        _tlSig.swimlanes = ''; _tlSig.detail = '';
        renderTimeline();
      });
    });
  }
}

// ---- Detail pane: selected segment drill ----------------------------
function renderTimelineDetail() {
  const root = document.getElementById('tl-detail');
  if (!root) return;
  const sig = selectedSegmentKey || '(none)';
  // Always rebuild when selection changes; cheap.
  if (!selectedSegmentKey) {
    if (_tlSig.detail === '(none)') return;
    _tlSig.detail = '(none)';
    root.innerHTML = `<h4>segment detail</h4><div class='tl-empty'>select a segment block above</div>`;
    return;
  }
  _tlSig.detail = sig + ':' + (lastSegmentLifetime ? (lastSegmentLifetime.entries||[]).length : 0);

  const recent = (lastSegments && lastSegments.recent) || [];
  let s = null;
  for (const x of recent) {
    if (segKey(x.family, x.key, x.startTick) === selectedSegmentKey) { s = x; break; }
  }
  if (!s) {
    const opens = (lastSegments && lastSegments.open) || [];
    for (const x of opens) {
      if (segKey(x.family, x.key, x.startTick) === selectedSegmentKey) { s = x; break; }
    }
  }
  if (!s) {
    root.innerHTML = `<h4>segment detail</h4><div class='tl-empty'>selected segment no longer in the window</div>`;
    return;
  }
  let lifetime = null;
  if (lastSegmentLifetime) {
    for (const e of (lastSegmentLifetime.entries || [])) {
      if (segKey(e.family, e.key, e.segmentStartTick) === selectedSegmentKey) { lifetime = e; break; }
    }
  }
  let attr = null;
  if (lastSegmentModAttr) {
    for (const e of (lastSegmentModAttr.entries || [])) {
      if (segKey(e.family, e.key, e.segmentStartTick) === selectedSegmentKey) { attr = e; break; }
    }
  }
  const rows = [];
  rows.push(['family', s.family]);
  rows.push(['name', s.name]);
  rows.push(['started', s.startUnixMs ? new Date(s.startUnixMs).toLocaleTimeString() : '—']);
  rows.push(['ended', s.endUnixMs ? new Date(s.endUnixMs).toLocaleTimeString() : 'open']);
  rows.push(['duration', fmtDuration(s.durationMs)]);
  rows.push(['ticks', fmtInt(s.ticks)]);
  if (s.avgFrameMs != null) rows.push(['avg frame', fmtMs(s.avgFrameMs) + ' ms/t']);
  if (s.spikeCount  != null) rows.push(['spikes', fmtInt(s.spikeCount)]);
  if (s.stallCount  != null) rows.push(['stalls', fmtInt(s.stallCount)]);
  if (s.deathCount  != null) rows.push(['deaths', fmtInt(s.deathCount)]);
  if (s.bossKillCount) rows.push(['boss kills', fmtInt(s.bossKillCount)]);
  if (s.promoted) rows.push(['promoted', s.promotionReason || 'yes']);
  if (lifetime) {
    rows.push(['lifetime avg', fmtMs(lifetime.lifetimeAvgMs) + ' ms/t']);
    rows.push(['this segment', fmtMs(lifetime.thisSegmentAvgMs) + ' ms/t']);
    rows.push(['samples', fmtInt(lifetime.lifetimeSampleCount)]);
    const sign = lifetime.deltaFraction >= 0 ? '+' : '';
    rows.push(['delta vs lifetime', sign + (lifetime.deltaFraction * 100).toFixed(1) + '%']);
  }

  let modsHtml = '';
  if (attr && attr.perMod && attr.perMod.length > 0) {
    const total = attr.perMod.reduce((a,b)=>a+b.ms, 0) || 1;
    const sorted = attr.perMod.slice().sort((a,b)=>b.ms-a.ms);
    modsHtml = `<div class='det-mods'>` + sorted.map(m => {
      const share = (m.ms / total) * 100;
      return `<div class='row'>
        <span class='name' style='color:${modColor(m.modId)}'>${escapeHtml(m.modName)}</span>
        <span class='bar'><span style='width:${share.toFixed(2)}%;background:${modColor(m.modId)}'></span></span>
        <span class='ms'>${fmtMs(m.ms)}</span>
      </div>`;
    }).join('') + `</div>`;
  } else if (s.topMods && s.topMods.length > 0) {
    const total = s.topMods.reduce((a,b)=>a+b.ms, 0) || 1;
    modsHtml = `<div class='det-mods'>` + s.topMods.map(m => {
      const share = (m.ms / total) * 100;
      return `<div class='row'>
        <span class='name' style='color:${modColor(m.id)}'>${escapeHtml(m.name)}</span>
        <span class='bar'><span style='width:${share.toFixed(2)}%;background:${modColor(m.id)}'></span></span>
        <span class='ms'>${fmtMs(m.ms)}</span>
      </div>`;
    }).join('') + `</div>`;
  }

  root.innerHTML =
    `<h4>segment detail</h4>` +
    rows.map(r => `<div class='det-row'><span class='k'>${escapeHtml(r[0])}</span><span class='v'>${escapeHtml(String(r[1]))}</span></div>`).join('') +
    modsHtml;
}

// ---- T5: attendance — nested treemap --------------------------------
// Two-level treemap. Outer split: vanilla vs modded biome ticks. Inner
// (within modded): one cell per mod sized by its biome ticks. Cells are
// laid out with a simple slice-and-dice algorithm — alternating
// horizontal/vertical splits — which gives stable proportions without
// the complexity of squarified treemaps and renders fine at this scale.
function sliceDice(items, x, y, w, h, horizontal, out) {
  // items: [{ value, ...payload }] sorted desc by value
  let total = 0;
  for (const it of items) total += it.value;
  if (total <= 0 || items.length === 0) return;
  if (items.length === 1) {
    out.push({ x, y, w, h, item: items[0] });
    return;
  }
  // Greedy: take the largest item first, give it a proportional slice,
  // recurse on the rest in the orthogonal direction.
  const head = items[0];
  const rest = items.slice(1);
  const frac = head.value / total;
  if (horizontal) {
    const cw = w * frac;
    out.push({ x, y, w: cw, h, item: head });
    sliceDice(rest, x + cw, y, w - cw, h, false, out);
  } else {
    const ch = h * frac;
    out.push({ x, y, w, h: ch, item: head });
    sliceDice(rest, x, y + ch, w, h - ch, true, out);
  }
}

function renderTimelineAttendance() {
  const root = document.getElementById('tl-attendance');
  if (!root) return;
  if (!lastAttendance || !lastAttendance.byMod || lastAttendance.byMod.length === 0) {
    if (_tlSig.attendance === 'empty') return;
    _tlSig.attendance = 'empty';
    root.innerHTML = `<h4>attendance</h4><div class='tl-empty'>no attendance data yet</div>`;
    return;
  }
  const a = lastAttendance;
  const sig = (a.totalBiomeTicks||0) + ':' + (a.moddedBiomeTicks||0) + ':' + a.byMod.length + ':' + (a.byMod.length ? a.byMod[0].biomeTicks : 0);
  if (sig === _tlSig.attendance) return;
  _tlSig.attendance = sig;

  const total = Math.max(1, a.totalBiomeTicks || 0);
  const modded = Math.max(0, Math.min(total, a.moddedBiomeTicks || 0));
  const vanilla = Math.max(0, total - modded);

  // Treemap viewBox; let SVG scale to container.
  const W = 1000, H = 220, pad = 4;
  const moddedFrac = modded / total;
  // Outer split horizontal — vanilla on the right, modded on the left.
  const moddedW = (W - pad * 3) * moddedFrac;
  const vanillaW = (W - pad * 3) * (1 - moddedFrac);
  const innerY = pad, innerH = H - pad * 2;

  // Inner layout for modded mods.
  const sorted = a.byMod.slice().filter(m => m.biomeTicks > 0).sort((a,b)=>b.biomeTicks-a.biomeTicks);
  const items = sorted.map(m => ({ value: m.biomeTicks, mod: m }));
  const tiles = [];
  if (items.length > 0 && moddedW > 1) {
    sliceDice(items, pad, innerY, moddedW, innerH, true, tiles);
  }

  let modCells = '';
  for (const t of tiles) {
    if (t.w < 0.5 || t.h < 0.5) continue;
    const m = t.item.mod;
    const c = modColor(m.modId);
    const share = (m.biomeTicks / total) * 100;
    const tip = `${m.modName} — ${fmtInt(m.biomeTicks)} biome ticks (${share.toFixed(1)}%), ${m.invasionCount} invasions, ${m.bossSegmentCount} boss segs`;
    // Pick a readable label only if cell large enough.
    const showLabel = (t.w > 70 && t.h > 22);
    const showShare = (t.w > 50 && t.h > 36);
    modCells += `<g class='tm-cell'>
      <rect x='${t.x.toFixed(1)}' y='${t.y.toFixed(1)}' width='${t.w.toFixed(1)}' height='${t.h.toFixed(1)}' fill='${c}' fill-opacity='0.55' stroke='${c}' stroke-width='1'/>
      ${showLabel ? `<text x='${(t.x+4).toFixed(1)}' y='${(t.y+14).toFixed(1)}' class='tm-lbl'>${escapeHtml(m.modName)}</text>` : ''}
      ${showShare ? `<text x='${(t.x+4).toFixed(1)}' y='${(t.y+28).toFixed(1)}' class='tm-sub'>${share.toFixed(1)}%</text>` : ''}
      <title>${escapeHtml(tip)}</title>
    </g>`;
  }

  // Vanilla cell — on the right of the treemap.
  const vx = pad * 2 + moddedW;
  const vanillaPct = (vanilla / total) * 100;
  const vanillaCell = `<g class='tm-vanilla'>
    <rect x='${vx.toFixed(1)}' y='${innerY}' width='${vanillaW.toFixed(1)}' height='${innerH}' class='tm-vanilla-rect'/>
    ${vanillaW > 80 ? `<text x='${(vx+8).toFixed(1)}' y='${(innerY+16).toFixed(1)}' class='tm-lbl'>vanilla</text>` : ''}
    ${vanillaW > 60 ? `<text x='${(vx+8).toFixed(1)}' y='${(innerY+30).toFixed(1)}' class='tm-sub'>${vanillaPct.toFixed(1)}%</text>` : ''}
    <title>vanilla biome ticks — ${fmtInt(vanilla)} (${vanillaPct.toFixed(1)}%)</title>
  </g>`;

  // Outer frame label band
  const totalsBand = `<div class='tm-totals'>
    <span>biome ticks <span class='v'>${fmtInt(a.totalBiomeTicks)}</span></span>
    <span>modded <span class='v'>${fmtInt(a.moddedBiomeTicks)}</span></span>
    <span>invasions <span class='v'>${fmtInt(a.totalInvasions)}</span></span>
    <span>boss segments <span class='v'>${fmtInt(a.totalBossSegments)}</span></span>
  </div>`;

  root.innerHTML =
    `<h4>attendance</h4>${totalsBand}
     <svg viewBox='0 0 ${W} ${H}' preserveAspectRatio='none' class='tl-treemap'>
       ${modCells}
       ${vanillaCell}
     </svg>`;
}

// ---- T6: death replay — inline SVG icon events ----------------------
function deathEventIcon(kind) {
  // Each icon is a tiny SVG drawn into a 12x12 box centred on (0,0)
  // (translated via parent <g>). Kept abstract — geometric, not literal —
  // to stay descriptive rather than judgemental.
  switch (kind) {
    case 'death':
      // skull-ish: rounded square head + two eye holes
      return `<g class='ic-death'>
        <rect x='-4' y='-5' width='8' height='8' rx='2' ry='2'/>
        <circle cx='-1.5' cy='-2' r='0.9' class='hole'/>
        <circle cx='1.5'  cy='-2' r='0.9' class='hole'/>
        <rect x='-2.5' y='3' width='5' height='1.5' class='jaw'/>
      </g>`;
    case 'damage':
      // crossed sword strokes
      return `<g class='ic-damage'>
        <line x1='-4' y1='-4' x2='4' y2='4'/>
        <line x1='4'  y1='-4' x2='-4' y2='4'/>
      </g>`;
    case 'npc-spawn':
      // lightning bolt
      return `<polygon class='ic-spawn' points='1,-5 -3,1 0,1 -1,5 3,-1 0,-1'/>`;
    case 'item-created':
      // potion: bottle + cap
      return `<g class='ic-item'>
        <rect x='-1.5' y='-5' width='3' height='2' class='cap'/>
        <path d='M -3 -3 L 3 -3 L 3.5 4 A 3.5 3.5 0 0 1 -3.5 4 L -3 -3 Z' class='bottle'/>
      </g>`;
    case 'buff-on':
      // upward arrow
      return `<g class='ic-buff-on'>
        <line x1='0' y1='-5' x2='0' y2='4'/>
        <polyline points='-3,-2 0,-5 3,-2' fill='none'/>
      </g>`;
    case 'buff-off':
      // downward arrow
      return `<g class='ic-buff-off'>
        <line x1='0' y1='-4' x2='0' y2='5'/>
        <polyline points='-3,2 0,5 3,2' fill='none'/>
      </g>`;
    default:
      return `<circle r='2' class='ic-generic'/>`;
  }
}

function renderTimelineDeaths() {
  const root = document.getElementById('tl-deaths');
  if (!root) return;
  const deaths = (lastDeaths && lastDeaths.deaths) || [];
  const sig = deaths.length + ':' + (deaths.length ? (deaths[0].deathTickIndex + ',' + deaths[deaths.length-1].deathTickIndex) : '');
  if (sig === _tlSig.deaths) return;
  _tlSig.deaths = sig;

  if (deaths.length === 0) {
    root.innerHTML = `<div class='tl-empty'>no deaths this session</div>`;
    return;
  }
  root.innerHTML = deaths.map(d => {
    let minOff = -30000, maxOff = 0;
    for (const e of (d.events || [])) {
      if (e.offsetMs < minOff) minOff = e.offsetMs;
      if (e.offsetMs > maxOff) maxOff = e.offsetMs;
    }
    const span = Math.max(1, maxOff - minOff);
    const W = 1000, H = 26;
    // Axis ticks every 10s.
    let axisTicks = '';
    const tenS = 10000;
    for (let t = Math.ceil(minOff / tenS) * tenS; t <= maxOff; t += tenS) {
      const x = ((t - minOff) / span) * W;
      const lab = (t / 1000).toFixed(0) + 's';
      axisTicks += `<line x1='${x.toFixed(1)}' y1='${H-6}' x2='${x.toFixed(1)}' y2='${H-2}' class='ax-tick'/>
                    <text x='${x.toFixed(1)}' y='${H-8}' class='ax-lbl' text-anchor='middle'>${lab}</text>`;
    }
    const evs = (d.events || []).map(e => {
      const x = ((e.offsetMs - minOff) / span) * W;
      const y = H / 2 - 1;
      const tip = `${e.kind} · ${e.label}${e.modName ? ' (' + e.modName + ')' : ''} @ ${e.offsetMs}ms`;
      return `<g transform='translate(${x.toFixed(1)} ${y})' class='ev' data-kind='${escapeHtml(e.kind)}'>
                ${deathEventIcon(e.kind)}<title>${escapeHtml(tip)}</title>
              </g>`;
    }).join('');
    const when = d.deathUnixMs ? new Date(d.deathUnixMs).toLocaleTimeString() : '—';
    const dmgMod = d.finalDamageModName ? ' (' + escapeHtml(d.finalDamageModName) + ')' : '';
    return `<div class='tl-death'>
      <div class='head'>
        <span><span class='k'>when</span>${escapeHtml(when)}</span>
        <span><span class='k'>biome</span>${escapeHtml(d.primaryBiome || '—')}</span>
        <span><span class='k'>boss</span>${escapeHtml(d.primaryBoss || '—')}</span>
        <span><span class='k'>final</span>${escapeHtml(d.finalDamageSource || '—')}${dmgMod} · ${fmtInt(d.finalDamageAmount)}</span>
      </div>
      <svg viewBox='0 0 ${W} ${H}' preserveAspectRatio='none' class='tl-death-svg'>
        <line x1='0' y1='${H/2}' x2='${W}' y2='${H/2}' class='ax-rail'/>
        ${axisTicks}
        ${evs}
      </svg>
    </div>`;
  }).join('');
}

// ---- T7: chronicle — horizontal narrative film-strip ribbon ---------
function renderTimelineChronicle() {
  const root = document.getElementById('tl-chronicle');
  if (!root) return;
  const lines = (lastChronicle && lastChronicle.lines) || [];
  const sig = lines.length + ':' + (lines.length ? (lines[0].unixMs + ',' + lines[lines.length-1].unixMs) : '');
  if (sig === _tlSig.chronicle) return;
  _tlSig.chronicle = sig;

  if (lines.length === 0) {
    root.innerHTML = `<div class='tl-empty'>no chronicle lines yet</div>`;
    return;
  }
  // Sort oldest → newest so the ribbon reads left-to-right like a film
  // strip running across session time.
  const sorted = lines.slice().sort((a,b) => a.unixMs - b.unixMs);
  // Each cell width is uniform — the ribbon is a sequenced narrative,
  // not a time-scaled axis (the swimlanes carry the time axis).
  const blocks = sorted.map(l => {
    const t = l.unixMs ? new Date(l.unixMs).toLocaleTimeString() : '—';
    return `<div class='cr-block' data-kind='${escapeHtml(l.kind)}' title='${escapeHtml(t + ' — ' + l.kind + ': ' + l.text)}'>
              <div class='cr-time'>${escapeHtml(t)}</div>
              <div class='cr-kind'>${escapeHtml(l.kind)}</div>
              <div class='cr-text'>${escapeHtml(l.text)}</div>
            </div>`;
  }).join('<div class=""cr-sep""></div>');
  root.innerHTML = `<div class='cr-ribbon'>${blocks}</div>`;
}

// ---- Top-level dispatch --------------------------------------------
function renderTimeline() {
  renderTimelineHeatstrip();
  renderTimelineTransitions();
  renderTimelineSwimlanes();
  renderTimelineDetail();
  renderTimelineAttendance();
  renderTimelineDeaths();
  renderTimelineChronicle();
}

async function pollTimelineData() {
  if (activeTab !== 'timeline') return;
  try {
    const [sl, sma, tr, as, at, dd, ch] = await Promise.all([
      fetch('/api/segment-lifetime',        { cache: 'no-store' }).then(r => r.ok ? r.json() : null).catch(() => null),
      fetch('/api/segment-mod-attribution', { cache: 'no-store' }).then(r => r.ok ? r.json() : null).catch(() => null),
      fetch('/api/transitions',             { cache: 'no-store' }).then(r => r.ok ? r.json() : null).catch(() => null),
      fetch('/api/activity-strip',          { cache: 'no-store' }).then(r => r.ok ? r.json() : null).catch(() => null),
      fetch('/api/attendance',              { cache: 'no-store' }).then(r => r.ok ? r.json() : null).catch(() => null),
      fetch('/api/deaths',                  { cache: 'no-store' }).then(r => r.ok ? r.json() : null).catch(() => null),
      fetch('/api/chronicle',               { cache: 'no-store' }).then(r => r.ok ? r.json() : null).catch(() => null),
    ]);
    if (sl)  lastSegmentLifetime = sl;
    if (sma) lastSegmentModAttr  = sma;
    if (tr)  lastTransitions     = tr;
    if (as)  lastActivityStrip   = as;
    if (at)  lastAttendance      = at;
    if (dd)  lastDeaths          = dd;
    if (ch)  lastChronicle       = ch;
    renderTimeline();
  } catch (e) { /* polling is best-effort; surface via /api/self if needed */ }
}
setInterval(pollTimelineData, 2500);
";
}
