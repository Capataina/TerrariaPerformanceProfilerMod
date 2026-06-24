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
    // Timeline renderers — composed from the shared component library.
    //
    // Every surface now draws with the shared vocabulary instead of bespoke
    // markup. No data field was dropped — each was reshaped onto a component:
    //   heat strip   barChart() vertical strip (value = avgFrameMs,
    //                colour = perfColor(avgFrameMs/16.6), spike/stall marks)
    //   transitions  time-placed .chip tokens — KEPT bespoke (time-domain)
    //   swimlanes    absolute gantt — KEPT bespoke; waterfall via splitBar()
    //   detail       panel() body: statLine() rows + a .dtable of per-mod ms
    //   attendance   panel() body: statLine totals + splitBar/splitLegend + .dtable
    //   deaths       per-death cards: head statline-ish + a .chip event row
    //   chronicle    scrollRegion + setHTML, a tokenised block list
    //   filter bar   segmented() control (gates which swimlanes render)
    //
    // The family filter (timelineFilter, declared in JsConfig) gates which
    // swimlanes + transitions + attendance render; the segmented control in
    // #tl-filterbar drives it via setTimelineFilter.
    //
    // Caching: every renderer keys its last input by a cheap signature so a
    // poll that returns identical data doesn't re-build the DOM. DOM churn
    // matters here since the F9 overlay shares browser resources with the game.
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
let chronicleFilter     = 'all';  // 'all' or a chronicle kind group (see CR_KINDS)

// Render-signature cache — skip rebuild when input didn't change.
const _tlSig = {
  heatstrip: '', transitions: '', swimlanes: '', detail: '',
  attendance: '', deaths: '', chronicle: '', filterbar: ''
};

const TL_FAMILIES = ['Biome','Weather','Boss','Invasion','Subworld'];

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

// Swimlane-only time domain — spans just the captured segments (first segment
// start to last segment end), NOT the full session window. timelineWindow()
// stretches to `now` and to the last activity-strip minute, which leaves every
// segment clustered in the far-left sliver of each lane with dead space out to
// the right edge. Scaling the gantt to the range that actually contains
// segments spreads the blocks across the lane width while keeping their
// RELATIVE positions/proportions exact. A small margin on each side keeps the
// first/last blocks off the very edges. Every lane is laid against this one
// window, so the family lanes stay axis-aligned with each other.
function swimlaneWindow() {
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
      if (nowMs > e) e = nowMs;   // an open segment legitimately runs to now
    }
  }
  if (!isFinite(s) || !isFinite(e) || e <= s) {
    // No segments yet — fall back to the broad window so the lanes still scale
    // to something sane rather than collapsing to a degenerate span.
    return timelineWindow();
  }
  // Pad by a small fraction of the captured span so the first block doesn't
  // touch the left edge and the last doesn't touch the right.
  const margin = Math.max(1, (e - s) * 0.03);
  s -= margin; e += margin;
  return { startMs: s, endMs: e, spanMs: Math.max(1, e - s) };
}

// ---- Family filter --------------------------------------------------
// timelineFilter (JsConfig) is 'all' or one of TL_FAMILIES. Selecting a
// family narrows the swimlanes + transitions + attendance to it; 'all' shows
// everything. State is reflected in the segmented control.
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

// Family filter rendered as the shared segmented() control. The options are
// 'all' + each family; the active option mirrors timelineFilter.
function renderTimelineFilterBar() {
  const root = document.getElementById('tl-filterbar');
  if (!root) return;
  const sig = timelineFilter;
  if (sig === _tlSig.filterbar) return;
  _tlSig.filterbar = sig;
  const options = [{ value: 'all', label: 'all' }].concat(
    TL_FAMILIES.map(f => ({ value: f, label: f.toLowerCase() })));
  root.innerHTML = segmented({
    id: 'tl-filter-seg', attr: 'data-tlfilter', active: timelineFilter, options
  });
  root.querySelectorAll('[data-tlfilter]').forEach(b => {
    b.addEventListener('click', () => setTimelineFilter(b.getAttribute('data-tlfilter')));
  });
}

function familyVisible(f) {
  return timelineFilter === 'all' || timelineFilter === f;
}

// ---- T4: heat strip — barChart vertical strip -----------------------
// One vertical bar per minute bucket inside a height-bounded shell. Bar value
// is avgFrameMs (scaled to the busiest minute so a flat-but-busy session still
// reads as variation), colour is the perf ramp of avgFrameMs vs the 16.6 ms
// (60 fps) reference. Spike/stall presence shows as marks above the bar via
// barChart's marks:[]. The native tip carries the full per-minute summary.
function renderTimelineHeatstrip() {
  const root = document.getElementById('tl-heatstrip');
  if (!root) return;
  const buckets = (lastActivityStrip && lastActivityStrip.minutes) || [];
  const sig = buckets.length + ':' + (buckets.length ? (buckets[0].minuteIndex + '|' + buckets[buckets.length-1].minuteIndex + '|' + buckets[buckets.length-1].avgFrameMs.toFixed(2)) : '');
  if (sig === _tlSig.heatstrip) return;
  _tlSig.heatstrip = sig;

  if (buckets.length === 0) {
    root.innerHTML = emptyState('no activity buckets yet');
    return;
  }

  // Scale bars to the busiest minute so the strip reads as variation, not a
  // wall; barChart floors each fill at a small visible stub on its own.
  const maxFrame = buckets.reduce((m, b) => Math.max(m, b.avgFrameMs || 0), 0) || 1;
  const bars = buckets.map(b => {
    const marks = [];
    if (b.stallCount > 0)      marks.push('stall');
    else if (b.spikeCount > 0) marks.push('spike');
    return {
      value: b.avgFrameMs,
      color: perfColor(b.avgFrameMs / 16.6),   // 16.6 ms = 60 fps reference
      marks,
      tip: 'min ' + b.minuteIndex + ': ' + fmtMs(b.avgFrameMs) + ' ms/t, ' +
           b.segmentCount + ' segs, ' + b.spikeCount + ' spikes, ' + b.stallCount + ' stalls'
    };
  });
  // colWidth is a floor only — the local .tl-heatstrip CSS lets the columns
  // flex-grow to fill the panel so a short session spans the strip instead of
  // stranding it in the left corner; scrollx kicks in only once the floor is hit.
  const strip = barChart({ bars, max: maxFrame, colWidth: 6, scrollx: true });

  // Legend: the bar fill is the perf ramp (healthy -> busy) keyed to the busiest
  // minute, and the marker dots flag spike/stall minutes. Both were unlabelled,
  // so a reader saw 'red dots' with no key and a colour ramp with no reference.
  // The legend names the marks and anchors the ramp to its min..max ms/t.
  const minFrame = buckets.reduce((m, b) => Math.min(m, b.avgFrameMs || 0), maxFrame);
  const legend =
    `<div class='hs-legend'>` +
      `<span class='hs-ramp'>` +
        `<span class='hs-ramp-label'>${fmtMs(minFrame)}</span>` +
        `<span class='hs-ramp-bar'></span>` +
        `<span class='hs-ramp-label'>${fmtMs(maxFrame)} ms/t</span>` +
      `</span>` +
      `<span class='hs-key'><span class='bar-mark spike'></span>spike min</span>` +
      `<span class='hs-key'><span class='bar-mark stall'></span>stall min</span>` +
    `</div>`;
  root.innerHTML = `<div class='hs-strip'>${strip}</div>${legend}`;
}

// ---- T3: transitions track — time-placed labelled chips -------------
// type strings look like 'weather:BloodMoon' / 'biome:Jungle' / 'hardmode' /
// 'invasion:Goblin' / 'subworld:...'. We split on ':' to word the label in full.
// Genuine time-domain layout: chips are absolutely placed at their tick fraction
// (KEPT bespoke). Transitions carry NO colour encoding — a transition is a
// time-domain event, not an ordered magnitude or a perf reading. Tinting the
// chip green/amber/red collided with the perf ramp (green = within budget), so
// these chips stay neutral chrome; the perf-ramp hues are reserved for cost.
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
    root.innerHTML = emptyState('no transitions' + (timelineFilter !== 'all' ? ' for ' + timelineFilter.toLowerCase() : '') + ' yet');
    return;
  }
  // The track is a fixed-width time domain; too many chips collide and clip. Cap
  // the count to the most-recent TL_TX_CAP and surface the rest as a '+N' token
  // anchored to the left edge so nothing is silently dropped. Two stagger bands
  // hold the cap; a lower cap keeps each band sparse enough that adjacent chips
  // on the same band don't overprint.
  const TL_TX_CAP = 10;
  const ordered = list.slice().sort((a, b) => a.unixMs - b.unixMs);
  const hidden = Math.max(0, ordered.length - TL_TX_CAP);
  const shown = hidden > 0 ? ordered.slice(ordered.length - TL_TX_CAP) : ordered;

  const chips = shown.map((t, i) => {
    const leftPct = Math.max(0, Math.min(100, pctOf(t.unixMs, win)));
    // Edge-anchor: chips near the left/right edge anchor to that edge so the
    // (clipped + inset) track never cuts the label; the rest centre on their
    // tick. Wider edge bands than before so a chip whose tick sits at the very
    // right anchors inward rather than overhanging the clip boundary.
    const tx = leftPct < 18 ? '0' : leftPct > 82 ? '-100%' : '-50%';
    // Vertical stagger: alternate chips onto two stacked bands so two closely-
    // timed transitions sit above/below each other instead of overprinting.
    const band = i % 2 === 0 ? 'lo' : 'hi';
    const word = transitionKindWord(t.type);
    const arrow = (t.from || t.to)
      ? `${escapeHtml(t.from || '?')} → ${escapeHtml(t.to || '?')}`
      : escapeHtml(t.type || word);
    const tip = `${word}: ${t.from || '?'} → ${t.to || '?'} (tick ${t.tickIndex})`;
    return `<span class='chip tx-chip tx-${band}' style='left:${leftPct.toFixed(2)}%;transform:translateX(${tx})' title='${escapeHtml(tip)}'>
      <span class='tx-kind'>${escapeHtml(word)}</span> ${arrow}
    </span>`;
  }).join('');
  const overflow = hidden > 0
    ? `<span class='chip tx-chip tx-more' title='${hidden} earlier transition${hidden === 1 ? '' : 's'} not shown'>+${hidden}</span>`
    : '';
  root.innerHTML = `<div class='tx-track'><span class='tx-rail'></span>${overflow}${chips}</div>`;
}

// ---- T1+T2: swimlanes ------------------------------------------------
// Absolute time-scaled gantt bars per family (KEPT bespoke — no generic
// component does absolute time placement). Each closed segment carries a
// top-4-mod waterfall rendered with the shared splitBar() (coloured by
// modColor), a lifetime-delta badge, and accent outlining when the segment is
// a lifetime outlier. Filter gates which lanes render.
function renderTimelineSwimlanes() {
  // Scale the gantt to the segment-containing span, not the full session
  // window — see swimlaneWindow(). This one window drives every lane's block
  // placement so the family lanes stay axis-aligned with each other.
  const win = swimlaneWindow();
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
    if (arr.length === 0) {
      // Legitimately empty lane: a muted idle line, so a blank Boss/Invasion/
      // Subworld row reads as 'nothing happened' rather than 'unfinished'.
      lane.innerHTML = `<span class='tl-lane-empty'>none this session</span>`;
      continue;
    }
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
      // Fill = cost intensity: perf ramp of the segment's avg frame time vs the
      // 16.6 ms (60 fps) reference. Open/unmeasured segments (avgFrameMs 0) fall
      // through perfColor to the healthy end; the inline --seg-fill drives .bg.
      const fill = (s.avgFrameMs > 0)
        ? perfColor(s.avgFrameMs / 16.6)
        : 'var(--surface-2)';
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
        style='left:${left.toFixed(2)}%;width:${width.toFixed(2)}%;--seg-fill:${fill}' title='${escapeHtml(tip)}'>
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
// Tabular drill of the selected segment, composed from statLine() rows plus a
// per-mod contribution .dtable with cellBar bars. The panel chrome is static
// in the markup; this fills its scroll body via setHTML so a poll-driven
// rebuild (e.g. lifetime sample count ticking up) keeps the scroll position.
function renderTimelineDetail() {
  const root = document.getElementById('tl-detail-body');
  if (!root) return;
  const sig = selectedSegmentKey || '(none)';
  // Always rebuild when selection changes; cheap.
  if (!selectedSegmentKey) {
    if (_tlSig.detail === '(none)') return;
    _tlSig.detail = '(none)';
    setHTML(root, emptyState('select a segment block above'));
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
    setHTML(root, emptyState('selected segment no longer in the window'));
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
    const maxMs = modSource.reduce((a,b)=>Math.max(a, b.ms), 0) || 1;
    const sorted = modSource.slice().sort((a,b)=>b.ms-a.ms);
    const modRows = sorted.map(m => {
      const id = m[idKey];
      const name = m.modName != null ? m.modName : m.name;
      const c = modColor(id);
      return `<tr>
        <td class='l' style='color:${c}'>${escapeHtml(name)}</td>
        <td>${cellBar(m.ms / maxMs, c)}</td>
        <td>${fmtMs(m.ms)} ms</td>
      </tr>`;
    }).join('');
    modsHtml = `<table class='dtable' style='margin-top:0.5rem'>
      <thead><tr><th class='l'>mod</th><th></th><th>ms</th></tr></thead>
      <tbody>${modRows}</tbody>
    </table>`;
  }

  const body = rows.map(r => statLine(r[0], String(r[1]))).join('') + modsHtml;
  // setHTML preserves the scroll body's position across a poll rebuild.
  setHTML(root, body);
}

// ---- T5: attendance — split bar + supporting table -----------------
// A summary band of the four totals (statLine), a colour-coded split bar
// (vanilla vs per-mod modded biome ticks) with its legend, and a .dtable
// enumerating every mod's attendance counts. The panel chrome is static in
// the markup; this fills its scroll body via setHTML.
function renderTimelineAttendance() {
  const root = document.getElementById('tl-attendance-body');
  if (!root) return;
  if (!lastAttendance || !lastAttendance.byMod || lastAttendance.byMod.length === 0) {
    if (_tlSig.attendance === 'empty') return;
    _tlSig.attendance = 'empty';
    setHTML(root, emptyState('no attendance data yet'));
    return;
  }
  const a = lastAttendance;
  // All-zero readout (no biome-tick attribution yet) reads as broken under a
  // populated-looking table shell. Show an explicit idle state instead of the
  // zero-grid + headerless table.
  const totalTicks = a.totalBiomeTicks || 0;
  if (totalTicks <= 0) {
    if (_tlSig.attendance === 'idle') return;
    _tlSig.attendance = 'idle';
    setHTML(root, emptyState('no biome-tick attribution captured this session'));
    return;
  }
  const sig = totalTicks + ':' + (a.moddedBiomeTicks||0) + ':' + a.byMod.length + ':' + (a.byMod.length ? a.byMod[0].biomeTicks : 0);
  if (sig === _tlSig.attendance) return;
  _tlSig.attendance = sig;

  const total = Math.max(1, totalTicks);
  const modded = Math.max(0, Math.min(total, a.moddedBiomeTicks || 0));
  const vanilla = Math.max(0, total - modded);

  const totalsBand = `<div class='tm-totals'>` +
    statLine('biome ticks', fmtInt(a.totalBiomeTicks)) +
    statLine('modded', fmtInt(a.moddedBiomeTicks)) +
    statLine('invasions', fmtInt(a.totalInvasions)) +
    statLine('boss segments', fmtInt(a.totalBossSegments)) +
    `</div>`;

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

  // The bar only encodes proportion once there are >=2 series to split; a single
  // flat fill (vanilla-only) is decoration, so in that degenerate case drop the
  // bar and keep just the legend line stating the share.
  let barHtml = '';
  if (segsArr.length >= 2) {
    const legendSegs = segsArr.slice(0, 9).map(s => ({ color: s.color, label: s.label, value: s.value }));
    barHtml = `<div class='tm-bar'>${splitBar(segsArr, { tall: true })}${splitLegend(legendSegs)}</div>`;
  } else if (segsArr.length === 1) {
    barHtml = `<div class='tm-bar'>${splitLegend(segsArr.map(s => ({ color: s.color, label: s.label, value: s.value })))}</div>`;
  }

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

  // Only render the per-mod table when there are contributing mods — a header
  // row over an empty body reads as a broken table, so replace it with a muted
  // idle line in the vanilla-only case.
  const tableHtml = sorted.length > 0
    ? `<table class='dtable tm-table'>
    <thead><tr>
      <th class='l'>mod</th><th>biome ticks</th><th>share</th><th>invasions</th><th>boss segs</th>
    </tr></thead>
    <tbody>${rows}</tbody>
  </table>`
    : `<div class='tm-idle'>no per-mod biome attribution this session</div>`;

  setHTML(root, totalsBand + barHtml + tableHtml);
}

// ---- T6: death replay — compact cards + labelled event chips --------
// Per death, a card header (when, biome, boss, final damage) and a horizontal
// row of .chip tokens for the pre-death events — text label from
// event.label/kind, native title= carrying kind + mod + offset.
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
    root.innerHTML = emptyState('no deaths this session');
    return;
  }
  root.innerHTML = deaths.map(d => {
    const when = d.deathUnixMs ? fmtAgo(d.deathUnixMs) : '—';
    const dmgMod = d.finalDamageModName ? ' (' + escapeHtml(d.finalDamageModName) + ')' : '';
    const events = (d.events || []).slice().sort((a,b)=>a.offsetMs-b.offsetMs);
    const chips = events.length === 0
      ? emptyState('no recorded events')
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

// ---- T7: chronicle — chronological block list ----------------------
// A tokenised block list inside a bounded scrollRegion; each line is a
// timestamp + kind + text block, left-accented by kind via the .chip colour
// classes. setHTML keeps the reader's scroll position across the poll.
// Colour the kind badge by SEVERITY only — death is the alarm (red), spikes and
// boss bounds are warnings (amber). The non-severity event types (transition,
// weather, summary, join) stay neutral so the perf-ramp green keeps its single
// meaning ('within budget') and one hue never means two things across panes.
function chronicleKindClass(kind) {
  switch (kind) {
    case 'death':       return 'bad';
    case 'spike':       return 'warn';
    case 'boss-start':
    case 'boss-end':    return 'warn';
    default:            return '';
  }
}

// Collapse the raw kinds onto the filter groups the chip row offers, so e.g.
// boss-start and boss-end both fall under 'boss'. 'all' matches everything.
function chronicleKindGroup(kind) {
  switch (kind) {
    case 'boss-start':
    case 'boss-end':   return 'boss';
    default:           return kind || 'other';
  }
}

const CR_KINDS = ['death','spike','boss','weather','transition','summary'];

// Filter setter wired by the sticky chip row; resets the cache so the next
// render rebuilds the filtered list.
function setChronicleFilter(f) {
  const next = (f === chronicleFilter && f !== 'all') ? 'all' : f;
  if (next === chronicleFilter) return;
  chronicleFilter = next;
  _tlSig.chronicle = '';
  renderTimelineChronicle();
}

function renderTimelineChronicle() {
  const scroll = document.getElementById('tl-chronicle-scroll');
  if (!scroll) return;
  const lines = (lastChronicle && lastChronicle.lines) || [];
  // Which filter groups actually occur — so the chip row only offers live ones.
  const present = new Set(lines.map(l => chronicleKindGroup(l.kind)));
  const sig = lines.length + ':' + chronicleFilter + ':' +
    (lines.length ? (lines[0].unixMs + ',' + lines[lines.length-1].unixMs) : '');
  if (sig === _tlSig.chronicle) return;
  _tlSig.chronicle = sig;

  if (lines.length === 0) {
    setHTML(scroll, emptyState('no chronicle lines yet'));
    return;
  }

  // Sticky type filter at the top of the scroll body — a segmented() control of
  // 'all' + each kind group that occurs this session. It stays pinned while the
  // list scrolls, giving the long log a per-type filter consistent with the
  // swimlane family filter above.
  const opts = [{ value: 'all', label: 'all' }].concat(
    CR_KINDS.filter(k => present.has(k)).map(k => ({ value: k, label: k })));
  const filterBar = `<div class='cr-filter'>` +
    segmented({ id: 'cr-filter-seg', attr: 'data-crfilter', active: chronicleFilter, options: opts }) +
    `</div>`;

  // Oldest -> newest so the column reads top-to-bottom in session order. Each
  // line is a single-column row() (so it inherits the shared hover + left-bar)
  // whose cell lays the kind chip, timestamp and text on ONE dense baseline so a
  // long session shows more of its arc without heavy scrolling.
  let sorted = lines.slice().sort((a,b) => a.unixMs - b.unixMs);
  if (chronicleFilter !== 'all') {
    sorted = sorted.filter(l => chronicleKindGroup(l.kind) === chronicleFilter);
  }
  if (sorted.length === 0) {
    setHTML(scroll, filterBar + emptyState('no ' + chronicleFilter + ' events this session'));
    wireChronicleFilter(scroll);
    return;
  }
  const blocks = sorted.map(l => {
    const t = l.unixMs ? new Date(l.unixMs).toLocaleTimeString() : '—';
    const cls = chronicleKindClass(l.kind);
    const cell =
      `<div class='cr-cell'>` +
        `<span class='chip ${cls}'>${escapeHtml(l.kind)}</span>` +
        `<span class='cr-time muted'>${escapeHtml(t)}</span>` +
        `<span class='cr-text'>${escapeHtml(l.text)}</span>` +
      `</div>`;
    return row({
      cols: '1fr',
      cells: [cell],
      cls: 'cr-block',
      attrs: `title='${escapeHtml(t + ' — ' + l.kind + ': ' + l.text)}'`
    });
  });
  // tl-chronicle-scroll is the overflow:auto container; setHTML keeps scroll.
  setHTML(scroll, filterBar + rowList(blocks));
  wireChronicleFilter(scroll);
}

function wireChronicleFilter(scroll) {
  scroll.querySelectorAll('[data-crfilter]').forEach(b => {
    b.addEventListener('click', () => setChronicleFilter(b.getAttribute('data-crfilter')));
  });
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
