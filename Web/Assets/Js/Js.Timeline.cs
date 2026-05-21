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
    // Timeline renderers — Wave 3 functional layer.
    //
    // Each panel reads from its own /api/* feed:
    //   tl-heatstrip    ← lastActivityStrip
    //   tl-transitions  ← lastTransitions
    //   tl-gantt lanes  ← lastSegments.recent (+ open) joined with
    //                     lastSegmentLifetime + lastSegmentModAttr
    //   tl-detail       ← selectedSegmentKey lookup against the joined set
    //   tl-attendance   ← lastAttendance
    //   tl-deaths       ← lastDeaths
    //   tl-chronicle    ← lastChronicle
    //
    // The lane time domain is computed from the union of segment start/end
    // times, transition timestamps, and activity-bucket timestamps so all
    // three rows align over the same window.
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

function segKey(family, key, startTick) {
  return (family || '') + '|' + (key != null ? key : '') + '|' + (startTick != null ? startTick : '');
}

// Compute the [startMs, endMs] window the swimlanes scale across.
// Falls back to last 10 minutes when no segments are present so the
// heat strip and transitions can still render against a stable domain.
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

// ---- T4: heat strip -------------------------------------------------
function renderTimelineHeatstrip() {
  const root = document.getElementById('tl-heatstrip');
  if (!root) return;
  const buckets = (lastActivityStrip && lastActivityStrip.minutes) || [];
  if (buckets.length === 0) {
    root.innerHTML = `<div class='tl-empty'>no activity buckets yet</div>`;
    return;
  }
  // Heat = avgFrameMs normalised to [0..1] vs the strip's own max.
  let maxMs = 0;
  for (const b of buckets) if (b.avgFrameMs > maxMs) maxMs = b.avgFrameMs;
  if (maxMs <= 0) maxMs = 1;
  root.innerHTML = buckets.map(b => {
    const t = Math.min(1, b.avgFrameMs / maxMs);
    // shade from panel toward orange-to-red
    const r = Math.round(20 + 200 * t);
    const g = Math.round(30 +  60 * (1 - t));
    const bl = Math.round(40 +  40 * (1 - t));
    const bg = `rgb(${r},${g},${bl})`;
    const tip = `min ${b.minuteIndex}: ${fmtMs(b.avgFrameMs)} ms/t, ${b.segmentCount} segs, ${b.spikeCount} spikes, ${b.stallCount} stalls`;
    return `<div class='tl-heatcell' style='background:${bg}' title='${escapeHtml(tip)}'></div>`;
  }).join('');
}

// ---- T3: transitions track ------------------------------------------
function renderTimelineTransitions() {
  const root = document.getElementById('tl-transitions');
  if (!root) return;
  const list = (lastTransitions && lastTransitions.transitions) || [];
  if (list.length === 0) {
    root.innerHTML = `<div class='tl-empty'>no transitions yet</div>`;
    return;
  }
  const win = timelineWindow();
  root.innerHTML = list.map(t => {
    const left = Math.max(0, Math.min(100, pctOf(t.unixMs, win)));
    const tip = `${t.type}: ${t.from} → ${t.to} (tick ${t.tickIndex})`;
    return `<div class='tl-tx' data-type='${escapeHtml(t.type)}' style='left:${left}%' title='${escapeHtml(tip)}'></div>`;
  }).join('');
}

// ---- T1+T2: swimlanes ------------------------------------------------
function renderTimelineSwimlanes() {
  const win = timelineWindow();
  const families = ['Biome','Weather','Boss','Invasion','Subworld'];
  const byFamily = {};
  for (const f of families) byFamily[f] = [];

  // index lifetime + mod-attr by 'family|key|startTick' for the join
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
  // open segments — render with an inferred end at now
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
      // waterfall — inline stacked bar of top mods (up to 4)
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
      // delta chip
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
        renderTimeline();
      });
    });
  }
}

// ---- Detail pane: selected segment drill ----------------------------
function renderTimelineDetail() {
  const root = document.getElementById('tl-detail');
  if (!root) return;
  if (!selectedSegmentKey) {
    root.innerHTML = `<h4>segment detail</h4><div class='tl-empty'>select a segment block above</div>`;
    return;
  }
  // locate segment in recent (then open)
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
  // Compose rows
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

// ---- T5: attendance roll-up -----------------------------------------
function renderTimelineAttendance() {
  const root = document.getElementById('tl-attendance');
  if (!root) return;
  if (!lastAttendance || !lastAttendance.byMod || lastAttendance.byMod.length === 0) {
    root.innerHTML = `<h4>attendance</h4><div class='tl-empty'>no attendance data yet</div>`;
    return;
  }
  const total = Math.max(1, lastAttendance.totalBiomeTicks || 0);
  const sorted = lastAttendance.byMod.slice().sort((a,b) => b.biomeTicks - a.biomeTicks);
  const rows = sorted.map(m => {
    const share = (m.biomeTicks / total) * 100;
    return `<div class='att-row'>
      <span class='name'>${escapeHtml(m.modName)}</span>
      <span class='biome-bar'><span style='width:${share.toFixed(2)}%'></span></span>
      <span class='num'>${fmtInt(m.invasionCount)} inv</span>
      <span class='num'>${fmtInt(m.bossSegmentCount)} boss</span>
    </div>`;
  }).join('');
  const totals = `<div class='att-totals'>
    <span>total biome ticks <span class='v'>${fmtInt(lastAttendance.totalBiomeTicks)}</span></span>
    <span>modded ticks <span class='v'>${fmtInt(lastAttendance.moddedBiomeTicks)}</span></span>
    <span>invasions <span class='v'>${fmtInt(lastAttendance.totalInvasions)}</span></span>
    <span>boss segments <span class='v'>${fmtInt(lastAttendance.totalBossSegments)}</span></span>
  </div>`;
  root.innerHTML = `<h4>attendance</h4>${totals}${rows}`;
}

// ---- T6: death replay strips ----------------------------------------
function renderTimelineDeaths() {
  const root = document.getElementById('tl-deaths');
  if (!root) return;
  const deaths = (lastDeaths && lastDeaths.deaths) || [];
  if (deaths.length === 0) {
    root.innerHTML = `<div class='tl-empty'>no deaths this session</div>`;
    return;
  }
  root.innerHTML = deaths.map(d => {
    // Window: typically [-30000, 0] ms relative to death.
    let minOff = -30000, maxOff = 0;
    for (const e of (d.events || [])) {
      if (e.offsetMs < minOff) minOff = e.offsetMs;
      if (e.offsetMs > maxOff) maxOff = e.offsetMs;
    }
    const span = Math.max(1, maxOff - minOff);
    const evs = (d.events || []).map(e => {
      const left = ((e.offsetMs - minOff) / span) * 100;
      const tip = `${e.kind} · ${e.label}${e.modName ? ' (' + e.modName + ')' : ''} @ ${e.offsetMs}ms`;
      return `<div class='ev' data-kind='${escapeHtml(e.kind)}' style='left:${left.toFixed(2)}%' title='${escapeHtml(tip)}'></div>`;
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
      <div class='strip'>${evs}<div class='axis'></div></div>
    </div>`;
  }).join('');
}

// ---- T7: chronicle --------------------------------------------------
function renderTimelineChronicle() {
  const root = document.getElementById('tl-chronicle');
  if (!root) return;
  const lines = (lastChronicle && lastChronicle.lines) || [];
  if (lines.length === 0) {
    root.innerHTML = `<div class='tl-empty'>no chronicle lines yet</div>`;
    return;
  }
  // newest first
  const sorted = lines.slice().sort((a,b) => b.unixMs - a.unixMs);
  root.innerHTML = sorted.map(l => {
    const t = l.unixMs ? new Date(l.unixMs).toLocaleTimeString() : '—';
    return `<div class='cl-row' data-kind='${escapeHtml(l.kind)}'>
      <span class='t'>${escapeHtml(t)}</span>
      <span class='kind'>${escapeHtml(l.kind)}</span>
      <span class='txt'>${escapeHtml(l.text)}</span>
    </div>`;
  }).join('');
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

// ---- Polling for Timeline-specific endpoints ------------------------
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
