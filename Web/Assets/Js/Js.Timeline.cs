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
    // Timeline renderers — readable-vocabulary layer.
    //
    // The earlier metaphor-heavy renderers (seismograph waveform, per-kind
    // glyph track, nested treemap, abstract death icons) were rejected as
    // unreadable. They are replaced here with shapes from the shared
    // component vocabulary (split bars, dtables, chips, perf tints) so every
    // surface reads at a glance. No data field was dropped — each was
    // reshaped onto a quieter primitive:
    //   tl-heatstrip    per-minute vertical bar strip (height = avgFrameMs,
    //                   colour = perf tint; spike/stall as small markers)
    //   tl-transitions  time-placed labelled .chip tokens (words, not glyphs)
    //   tl-gantt lanes  kept — time-scaled bars, top-mod waterfall via splitBar
    //   tl-detail       kept — restyled with .statline + cellBar bars
    //   tl-attendance   split bar (vanilla vs per-mod) + supporting .dtable
    //   tl-deaths       death cards + labelled .chip event row (words)
    //   tl-chronicle    kept — chronological blocks, minor restyle
    //
    // The family filter (timelineFilter, declared in JsConfig) gates which
    // swimlanes render; '.tl-filter' buttons in the HTML drive it.
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

const TL_FAMILIES = ['Biome','Weather','Boss','Invasion','Subworld'];

function segKey(family, key, startTick) {
  return (family || '') + '|' + (key != null ? key : '') + '|' + (startTick != null ? startTick : '');
}

// Per-family swimlane bar colour, matched to the CSS lane accents.
const TL_FAMILY_COLOR = {
  Biome: 'var(--good)', Weather: 'var(--amber)', Boss: 'var(--danger)',
  Invasion: 'var(--orange)', Subworld: 'var(--purple)'
};

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

// ---- Family filter --------------------------------------------------
// timelineFilter (JsConfig) is 'all' or one of TL_FAMILIES. Clicking a
// filter button narrows the swimlanes + transitions + attendance to that
// family; 'all' shows everything. Filter is reflected in the button row.
function setTimelineFilter(f) {
  const next = (f === timelineFilter && f !== 'all') ? 'all' : f;
  if (next === timelineFilter) return;
  timelineFilter = next;
  // Selection may belong to a now-hidden lane — drop it if so.
  if (selectedSegmentKey && timelineFilter !== 'all') {
    const fam = selectedSegmentKey.split('|')[0];
    if (fam !== timelineFilter) selectedSegmentKey = null;
  }
  _tlSig.transitions = ''; _tlSig.swimlanes = ''; _tlSig.detail = '';
  renderTimelineFilterBar();
  renderTimeline();
}

function renderTimelineFilterBar() {
  const root = document.getElementById('tl-filterbar');
  if (!root) return;
  const opts = ['all'].concat(TL_FAMILIES);
  root.innerHTML = opts.map(o => {
    const active = (o === timelineFilter) ? ' active' : '';
    const label = (o === 'all') ? 'all' : o.toLowerCase();
    return `<button type='button' class='tl-filter-btn${active}' data-tlfilter='${o}'>${escapeHtml(label)}</button>`;
  }).join('');
  root.querySelectorAll('.tl-filter-btn').forEach(b => {
    b.addEventListener('click', () => setTimelineFilter(b.dataset.tlfilter));
  });
}

function familyVisible(f) {
  return timelineFilter === 'all' || timelineFilter === f;
}

// ---- T4: heat strip — per-minute vertical bar strip -----------------
// One thin fixed-width vertical bar per minute bucket inside a modest
// fixed-height container. Bar height is avgFrameMs auto-scaled across the
// session's minutes via niceScale (so a flat-but-busy session still shows
// its variation rather than every bar pinning to the top), colour is the
// perf tint of avgFrameMs vs a 16.6 ms (60 fps) reference. Spike/stall
// presence shows as a small marker above the bar, never by inflating the
// bar. Native title= carries the full per-minute summary.
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

  // Auto-scale heights across the minutes so the strip reads as variation,
  // not a wall. Floor each bar at a small visible stub so a near-zero minute
  // still registers as a tick rather than vanishing.
  const scale = niceScale(buckets.map(b => b.avgFrameMs), 0.05);
  const span = (scale.max - scale.min) || 1;

  const bars = buckets.map(b => {
    const norm = (b.avgFrameMs - scale.min) / span;
    const hPct = Math.max(6, Math.min(100, norm * 100));
    const tint = tintClass(b.avgFrameMs / 16.6);   // 16.6 ms = 60 fps reference
    const tip = `min ${b.minuteIndex}: ${fmtMs(b.avgFrameMs)} ms/t, ${b.segmentCount} segs, ${b.spikeCount} spikes, ${b.stallCount} stalls`;
    let mark = '';
    if (b.stallCount > 0)      mark = `<span class='hs-mark stall' title='${escapeHtml(b.stallCount + ' stalls')}'></span>`;
    else if (b.spikeCount > 0) mark = `<span class='hs-mark spike' title='${escapeHtml(b.spikeCount + ' spikes')}'></span>`;
    return `<span class='hs-col' title='${escapeHtml(tip)}'>
      ${mark}
      <span class='hs-bar ${tint}' style='height:${hPct.toFixed(1)}%'></span>
    </span>`;
  }).join('');

  root.innerHTML = `<div class='hs-strip'>${bars}</div>`;
}

// ---- T3: transitions track — time-placed labelled chips -------------
// type strings look like 'weather:BloodMoon' / 'biome:Jungle' /
// 'hardmode' / 'invasion:Goblin' / 'subworld:...'. We split on ':' to
// route the chip colour-class by the head and word the label in full.
function transitionChipClass(type) {
  const head = (type || '').split(':')[0].toLowerCase();
  switch (head) {
    case 'weather':  return 'warn';     // amber
    case 'biome':    return 'good';      // green
    case 'hardmode': return 'bad';       // danger
    case 'invasion': return 'warn';      // amber/orange family
    case 'subworld': return 'cool';      // accent
    default:         return 'cool';
  }
}
function transitionKindWord(type) {
  const head = (type || '').split(':')[0].toLowerCase();
  switch (head) {
    case 'weather':  return 'weather';
    case 'biome':    return 'biome';
    case 'hardmode': return 'hardmode';
    case 'invasion': return 'invasion';
    case 'subworld': return 'subworld';
    default:         return 'change';
  }
}

function renderTimelineTransitions() {
  const root = document.getElementById('tl-transitions');
  if (!root) return;
  let list = (lastTransitions && lastTransitions.transitions) || [];
  if (timelineFilter !== 'all') {
    const want = timelineFilter.toLowerCase();
    list = list.filter(t => transitionKindWord(t.type) === want);
  }
  const win = timelineWindow();
  const sig = list.length + ':' + win.startMs + ':' + win.endMs + ':' + timelineFilter + ':' + (list.length ? list[list.length-1].tickIndex : '');
  if (sig === _tlSig.transitions) return;
  _tlSig.transitions = sig;

  if (list.length === 0) {
    root.innerHTML = `<div class='tl-empty'>no transitions${timelineFilter !== 'all' ? ' for ' + escapeHtml(timelineFilter.toLowerCase()) : ''} yet</div>`;
    return;
  }
  const chips = list.map(t => {
    const leftPct = Math.max(0, Math.min(98, pctOf(t.unixMs, win)));
    const cls = transitionChipClass(t.type);
    const word = transitionKindWord(t.type);
    const arrow = (t.from || t.to)
      ? `${escapeHtml(t.from || '?')} → ${escapeHtml(t.to || '?')}`
      : escapeHtml(t.type || word);
    const tip = `${word}: ${t.from || '?'} → ${t.to || '?'} (tick ${t.tickIndex})`;
    return `<span class='chip ${cls} tx-chip' style='left:${leftPct.toFixed(2)}%' title='${escapeHtml(tip)}'>
      <span class='tx-kind'>${escapeHtml(word)}</span> ${arrow}
    </span>`;
  }).join('');
  root.innerHTML = `<div class='tx-track'><span class='tx-rail'></span>${chips}</div>`;
}

// ---- T1+T2: swimlanes ------------------------------------------------
// Time-scaled bars per family. Each closed segment carries a top-4-mod
// waterfall rendered with the shared splitBar primitive (coloured by
// modColor), a lifetime-delta badge, and perf-tint outlining when the
// segment is a lifetime outlier. Filter gates which lanes render.
function renderTimelineSwimlanes() {
  const win = timelineWindow();
  const byFamily = {};
  for (const f of TL_FAMILIES) byFamily[f] = [];

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
  let sig = win.startMs + ':' + win.endMs + ':' + timelineFilter + ':' + (selectedSegmentKey || '');
  for (const f of TL_FAMILIES) sig += ':' + f + '=' + byFamily[f].length + ',' + (byFamily[f].length ? byFamily[f][byFamily[f].length-1].startTick : '');
  if (sig === _tlSig.swimlanes) return;
  _tlSig.swimlanes = sig;

  for (const f of TL_FAMILIES) {
    const laneRow = document.getElementById('tl-laneRow-' + f.toLowerCase());
    const lane = document.getElementById('tl-lane-' + f.toLowerCase());
    if (laneRow) laneRow.style.display = familyVisible(f) ? '' : 'none';
    if (!lane) continue;
    if (!familyVisible(f)) { lane.innerHTML = ''; continue; }
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
          const segsArr = top.map(m => ({
            frac: m.ms / total, color: modColor(m.modId),
            label: m.modName, value: fmtMs(m.ms) + ' ms'
          }));
          waterfall = `<div class='tl-waterfall'>${splitBar(segsArr, { thin: true })}</div>`;
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
// Tabular drill of the selected segment, restyled onto .statline rows
// plus a per-mod contribution table built with cellBar bars.
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
  let modSource = null, idKey = null;
  if (attr && attr.perMod && attr.perMod.length > 0) { modSource = attr.perMod; idKey = 'modId'; }
  else if (s.topMods && s.topMods.length > 0)        { modSource = s.topMods; idKey = 'id'; }
  if (modSource) {
    const total = modSource.reduce((a,b)=>a+b.ms, 0) || 1;
    const maxMs = modSource.reduce((a,b)=>Math.max(a, b.ms), 0) || 1;
    const sorted = modSource.slice().sort((a,b)=>b.ms-a.ms);
    modsHtml = `<div class='det-mods'>` + sorted.map(m => {
      const id = m[idKey];
      const name = m.modName != null ? m.modName : m.name;
      const c = modColor(id);
      return `<div class='row'>
        <span class='name' style='color:${c}'>${escapeHtml(name)}</span>
        <span class='bar'>${cellBar(m.ms / maxMs, c)}</span>
        <span class='ms'>${fmtMs(m.ms)}</span>
      </div>`;
    }).join('') + `</div>`;
  }

  // tl-detail is an overflow:auto pane; setHTML preserves scroll when the
  // pane rebuilds on a poll (e.g. lifetime sample count ticking up).
  setHTML(root,
    `<h4>segment detail</h4>` +
    rows.map(r => `<div class='statline'><span class='k'>${escapeHtml(r[0])}</span><span class='v'>${escapeHtml(String(r[1]))}</span></div>`).join('') +
    modsHtml);
}

// ---- T5: attendance — split bar + supporting table -----------------
// A summary band of the four totals, a colour-coded split bar (vanilla
// vs per-mod modded biome ticks, each mod coloured by modColor), its
// legend, and a .dtable enumerating every mod's attendance counts.
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

  const totalsBand = `<div class='tm-totals'>
    <span class='statline'><span class='k'>biome ticks</span><span class='v'>${fmtInt(a.totalBiomeTicks)}</span></span>
    <span class='statline'><span class='k'>modded</span><span class='v'>${fmtInt(a.moddedBiomeTicks)}</span></span>
    <span class='statline'><span class='k'>invasions</span><span class='v'>${fmtInt(a.totalInvasions)}</span></span>
    <span class='statline'><span class='k'>boss segments</span><span class='v'>${fmtInt(a.totalBossSegments)}</span></span>
  </div>`;

  // Split bar: vanilla first, then each contributing mod by biome ticks.
  const sorted = a.byMod.slice().filter(m => m.biomeTicks > 0).sort((x,y)=>y.biomeTicks-x.biomeTicks);
  const segsArr = [];
  if (vanilla > 0) {
    segsArr.push({ frac: vanilla / total, color: 'var(--dim)', label: 'vanilla',
      value: fmtInt(vanilla) + ' (' + ((vanilla/total)*100).toFixed(1) + '%)' });
  }
  for (const m of sorted) {
    segsArr.push({ frac: m.biomeTicks / total, color: modColor(m.modId), label: m.modName,
      value: fmtInt(m.biomeTicks) + ' (' + ((m.biomeTicks/total)*100).toFixed(1) + '%)' });
  }

  // Legend: vanilla + the top contributors (cap to keep it readable).
  const legendSegs = segsArr.slice(0, 9).map(s => ({ color: s.color, label: s.label, value: s.value }));
  const barHtml = splitBar(segsArr, { tall: true }) + splitLegend(legendSegs);

  const rows = sorted.map(m => {
    const share = (m.biomeTicks / total) * 100;
    return `<tr>
      <td class='l'><span class='dot' style='background:${modColor(m.modId)}'></span>${escapeHtml(m.modName)}</td>
      <td>${fmtInt(m.biomeTicks)}</td>
      <td>${share.toFixed(1)}%</td>
      <td>${fmtInt(m.invasionCount)}</td>
      <td>${fmtInt(m.bossSegmentCount)}</td>
    </tr>`;
  }).join('');

  const tableHtml = `<table class='dtable tm-table'>
    <thead><tr>
      <th class='l'>mod</th><th>biome ticks</th><th>share</th><th>invasions</th><th>boss segs</th>
    </tr></thead>
    <tbody>${rows}</tbody>
  </table>`;

  // tl-attendance is an overflow:auto pane; setHTML preserves scroll when the
  // table rebuilds on a poll instead of snapping the reader to the top.
  setHTML(root, `<h4>attendance</h4>${totalsBand}<div class='tm-bar'>${barHtml}</div>${tableHtml}`);
}

// ---- T6: death replay — compact cards + labelled event chips --------
// Per death, a card header (when, biome, boss, final damage) and a
// horizontal row of .chip tokens for the pre-death events — text label
// from event.label/kind, native title= carrying kind + mod + offset.
function deathEventChipClass(kind) {
  switch (kind) {
    case 'death':       return 'bad';
    case 'damage':      return 'bad';
    case 'npc-spawn':   return 'warn';
    case 'item-created': return 'cool';
    case 'buff-on':     return 'good';
    case 'buff-off':    return '';
    default:            return '';
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
    const when = d.deathUnixMs ? fmtAgo(d.deathUnixMs) : '—';
    const dmgMod = d.finalDamageModName ? ' (' + escapeHtml(d.finalDamageModName) + ')' : '';
    const events = (d.events || []).slice().sort((a,b)=>a.offsetMs-b.offsetMs);
    const chips = events.length === 0
      ? `<span class='tl-empty'>no recorded events</span>`
      : events.map(e => {
          const cls = deathEventChipClass(e.kind);
          const label = e.label || e.kind || 'event';
          const offS = (e.offsetMs / 1000).toFixed(1) + 's';
          const tip = `${e.kind}: ${e.label || ''}${e.modName ? ' (' + e.modName + ')' : ''} @ ${e.offsetMs}ms`;
          return `<span class='chip ${cls} ev-chip' title='${escapeHtml(tip)}'>
            <span class='ev-off'>${escapeHtml(offS)}</span>${escapeHtml(label)}${e.modName ? `<span class='ev-mod'>${escapeHtml(e.modName)}</span>` : ''}
          </span>`;
        }).join('');
    return `<div class='tl-death'>
      <div class='head'>
        <span><span class='k'>when</span>${escapeHtml(when)}</span>
        <span><span class='k'>biome</span>${escapeHtml(d.primaryBiome || '—')}</span>
        <span><span class='k'>boss</span>${escapeHtml(d.primaryBoss || '—')}</span>
        <span><span class='k'>final</span>${escapeHtml(d.finalDamageSource || '—')}${dmgMod} · ${fmtInt(d.finalDamageAmount)}</span>
      </div>
      <div class='ev-row'>${chips}</div>
    </div>`;
  }).join('');
}

// ---- T7: chronicle — chronological blocks --------------------------
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
  // Oldest -> newest so the column reads top-to-bottom in session order.
  const sorted = lines.slice().sort((a,b) => a.unixMs - b.unixMs);
  const blocks = sorted.map(l => {
    const t = l.unixMs ? new Date(l.unixMs).toLocaleTimeString() : '—';
    return `<div class='cr-block' data-kind='${escapeHtml(l.kind)}' title='${escapeHtml(t + ' — ' + l.kind + ': ' + l.text)}'>
              <div class='cr-meta'><span class='cr-time'>${escapeHtml(t)}</span><span class='cr-kind'>${escapeHtml(l.kind)}</span></div>
              <div class='cr-text'>${escapeHtml(l.text)}</div>
            </div>`;
  }).join('');
  // tl-chronicle is itself the overflow:auto scroll container; setHTML keeps
  // the reader's scroll position across the 2.5 s poll instead of snapping
  // them back to the top whenever a new chronicle line lands.
  setHTML(root, `<div class='cr-list'>${blocks}</div>`);
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
renderTimelineFilterBar();
";
}
