#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    /// <summary>
    /// Dashboard JS. Single-file SPA controller: tab routing, polling
    /// loop, SVG chart drawing, per-tab renderers, mod card slide-in,
    /// tooltip engine, keyboard nav, mod-tree expansion.
    /// No bundler, no framework — vanilla ES2020+.
    /// </summary>
    public const string Js = @"
'use strict';

// ====== Config =======================================================
const POLL_NOW_MS    = 500;
const POLL_DETAIL_MS = 1500;
const POLL_HOOKS_MS  = 2500;
const POLL_SELF_MS   = 5000;
const DISCONNECT_MS  = 4000;

// ====== State ========================================================
let activeTab = 'summary';
let lastNow = null, lastFrames = null, lastMods = null, lastHooks = null;
let lastSegments = null, lastSpikes = null, lastStalls = null;
let lastInsights = null, lastSelf = null, lastHeatmap = null, lastEvents = null;
let lastSuccessAt = Date.now();
let modSort = 'composite';
let modFilter = '';
let timelineFilter = 'all';
let frameChartMode = 'ms';  // 'ms' (frame time) or 'fps'
const expandedMods = new Set();      // modId -> open
const expandedCategories = new Set(); // modId|catId -> open
const expandedSpikes = new Set();
const expandedStalls = new Set();
const expandedSegments = new Set();
const modSparkHistory = new Map();   // modId -> [last N cpu values] for inline mini-spark

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

// ====== Polling loops ================================================
async function fetchJson(path) {
  try {
    const r = await fetch(path, { cache: 'no-store' });
    if (!r.ok) throw new Error('HTTP ' + r.status);
    lastSuccessAt = Date.now();
    return await r.json();
  } catch (e) { return null; }
}

async function pollNow() {
  const [now, frames, segs] = await Promise.all([
    fetchJson('/api/now'), fetchJson('/api/frames'), fetchJson('/api/segments'),
  ]);
  if (now) lastNow = now;
  if (frames) lastFrames = frames;
  if (segs) lastSegments = segs;
  updateConnection();
  renderTopbar(); renderFooter(); updateOverlays();
  if (activeTab === 'summary' || activeTab === 'timeline') renderAll();
}

async function pollDetail() {
  const [mods, spikes, stalls, ins, events] = await Promise.all([
    fetchJson('/api/mods'), fetchJson('/api/spikes'), fetchJson('/api/stalls'),
    fetchJson('/api/insights'), fetchJson('/api/events'),
  ]);
  if (mods) { lastMods = mods; foldModSparkHistory(mods); }
  if (spikes) lastSpikes = spikes;
  if (stalls) lastStalls = stalls;
  if (ins) lastInsights = ins;
  if (events) lastEvents = events;
  if (activeTab === 'summary' || activeTab === 'lag' || activeTab === 'insights') renderAll();
}

async function pollHooks() {
  // Fetch only when summary is active AND at least one mod row is expanded.
  if (activeTab !== 'summary' || expandedMods.size === 0) return;
  const hooks = await fetchJson('/api/hooks');
  if (hooks) lastHooks = hooks;
  if (activeTab === 'summary') renderSummaryMods();
}

async function pollSelf() {
  const self = await fetchJson('/api/self');
  if (self) lastSelf = self;
  if (activeTab === 'self') renderSelf();
}

async function pollHeatmap() {
  const hm = await fetchJson('/api/heatmap');
  if (hm) lastHeatmap = hm;
  if (activeTab === 'summary') renderHeatmap();
}

// Track the last tick we recorded so we only advance time-series state
// when the game actually produced new data. Without this guard the
// dashboard's polling timer races ahead of the game (Terraria pauses
// when unfocused; our poll keeps firing), producing flat segments and
// fake spark-history entries that don't correspond to real ticks.
let lastSeenTick = -1;

function foldModSparkHistory(modsResp) {
  if (!modsResp || !modsResp.mods) return;
  // Skip when the underlying tick hasn't advanced — keeps the spark
  // history honest about the game's progress, not the browser's.
  const tickNow = lastNow ? lastNow.tickIndex : null;
  if (tickNow != null && tickNow === lastSeenTick) return;
  if (tickNow != null) lastSeenTick = tickNow;

  const N = 30;
  for (const m of modsResp.mods) {
    let arr = modSparkHistory.get(m.id);
    if (!arr) { arr = []; modSparkHistory.set(m.id, arr); }
    arr.push(m.cpuMs);
    if (arr.length > N) arr.shift();
  }
}

// Track when the game's tick last advanced. If polling keeps succeeding
// but the tick number is stuck, the game is paused (window unfocused).
let tickAdvancedAt = Date.now();
let pausedLastTick = -1;
function updateConnection() {
  const now = Date.now();
  const ok = (now - lastSuccessAt) < DISCONNECT_MS;
  const dot = document.getElementById('live-dot');
  const txt = document.getElementById('live-text');

  // Detect paused (server responding, but ticks not advancing).
  if (lastNow && lastNow.worldLoaded) {
    if (lastNow.tickIndex !== pausedLastTick) {
      pausedLastTick = lastNow.tickIndex;
      tickAdvancedAt = now;
    }
  }
  const paused = ok && lastNow && lastNow.worldLoaded && (now - tickAdvancedAt) > 1500;

  if (!ok) {
    dot.className = 'live-dot err';
    txt.textContent = 'connection lost · retrying';
  } else if (paused) {
    dot.className = 'live-dot paused';
    txt.textContent = 'game paused (window unfocused)';
  } else if (lastNow && lastNow.worldLoaded) {
    dot.className = 'live-dot ok';
    txt.textContent = 'live · world loaded';
  } else {
    dot.className = 'live-dot idle';
    txt.textContent = 'live · no world';
  }
}

function updateOverlays() {
  const disconnected = (Date.now() - lastSuccessAt) >= DISCONNECT_MS && lastSuccessAt > 0;
  document.getElementById('disconnected').classList.toggle('hidden', !disconnected);
  if (disconnected) {
    document.getElementById('empty').classList.add('hidden');
    return;
  }
  const loaded = lastNow && lastNow.worldLoaded;
  document.getElementById('empty').classList.toggle('hidden', !!loaded);
}

setInterval(pollNow, POLL_NOW_MS);
setInterval(pollDetail, POLL_DETAIL_MS);
setInterval(pollHooks, POLL_HOOKS_MS);
setInterval(pollSelf, POLL_SELF_MS);
setInterval(pollHeatmap, 3000);
setInterval(updateConnection, 1000);
pollNow(); pollDetail(); pollSelf(); pollHeatmap();

// ====== Helpers ======================================================
function fmtMs(v) {
  if (v == null || !isFinite(v)) return '—';
  if (v < 0.005) return '0.00';
  if (v < 10) return v.toFixed(2);
  if (v < 100) return v.toFixed(1);
  return v.toFixed(0);
}
function fmtInt(v) { return v == null ? '—' : v.toLocaleString(); }
function fmtBytes(v) {
  if (v == null || !isFinite(v) || v <= 0) return '—';
  if (v < 1024) return v.toFixed(0) + ' B';
  if (v < 1024*1024) return (v/1024).toFixed(1) + ' KB';
  return (v/(1024*1024)).toFixed(1) + ' MB';
}
function fmtDuration(ms) {
  if (ms == null) return '—';
  if (ms < 1000) return ms + 'ms';
  const s = Math.floor(ms / 1000);
  if (s < 60) return s + 's';
  const m = Math.floor(s / 60);
  if (m < 60) return m + 'm ' + String(s%60).padStart(2,'0') + 's';
  return Math.floor(m/60) + 'h ' + String(m%60).padStart(2,'0') + 'm';
}
function fmtAgo(unixMs) {
  if (!unixMs || !lastNow) return '';
  const dt = lastNow.unixMs - unixMs;
  if (dt < 1000) return 'just now';
  if (dt < 60000) return Math.floor(dt/1000) + 's ago';
  if (dt < 3600000) return Math.floor(dt/60000) + 'm ago';
  return Math.floor(dt/3600000) + 'h ago';
}
function truncate(s, n) { return s && s.length > n ? s.substring(0, n-1) + '…' : (s || ''); }
function escapeHtml(s) {
  if (s == null) return '';
  return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
                  .replace(/'/g, '&#39;').replace(/""/g, '&quot;');
}

// Consistent mod color: hash modId into the visible-pleasant range.
// Desaturated palette so 18 simultaneous slices in the impact donut
// don't fight the rest of the UI. Each is recognisable but muted.
const MOD_COLORS = ['#5f8db3', '#7d6d9c', '#4f9d6a', '#7e9477', '#a07852', '#8a6db8', '#a05b6a', '#4ab8c2', '#6aa3a8', '#b88a25', '#8d7e5a', '#5b6cb0'];
function modColor(id) { return MOD_COLORS[(id * 7 + 3) % MOD_COLORS.length]; }

// ====== Tooltips =====================================================
const TOOLTIPS = {
  'composite': {
    title: 'Composite sort',
    body: 'Composite = <code>cpuMs × 0.7 + avgCpuMs × 0.3</code>.<br/>Weights live cost heavier than session average so mods that spiked recently rise to the top, while still surfacing consistently expensive ones over momentary calm.'
  },
  'tick-frame': {
    title: 'Frame time (this tick)',
    body: 'Wall-clock ms the game took to process the most recent tick. At 60 fps the budget is ~16.6 ms per tick.'
  },
  'tick-avg': {
    title: 'Average · last 30 s',
    body: 'Rolling mean frame time over the most recent ~1800 ticks. Less jittery than the live value — what your average performance actually feels like.'
  },
  'tick-gc': {
    title: 'GC time this tick',
    body: 'Time the .NET garbage collector blocked during this tick. Large values here usually correspond to spikes and stalls.'
  },
  'backend': {
    title: 'Attribution backend',
    body: 'Which MonoMod method we use to intercept per-mod calls. <code>ilhook</code> is the IL-rewriting fast path; <code>delegate</code> uses tModLoader\'s delegate-pair detours. <code>parallel</code> runs both for comparison during dev.'
  },
  'alloc': {
    title: 'Allocation bytes',
    body: 'Managed-heap bytes attributable to each mod\'s hooks this tick. Shown as 0 when allocation tracking is disabled (Lite mode).'
  },
  'sparklines': {
    title: 'Session-trend sparklines',
    body: 'Three rolling 30-second series. <strong>frame</strong> is per-tick frame ms; <strong>alloc</strong> is process-wide allocation rate; <strong>spikes</strong> shows tick markers where spike threshold was crossed.'
  },
  'spike': {
    title: 'What counts as a spike',
    body: 'A spike fires when a frame exceeds <code>2× the 30-second median</code>. Adjacent over-threshold ticks coalesce into one window. Click a spike to see which mod owned the worst tick in that window.'
  },
  'stall': {
    title: 'What counts as a stall',
    body: 'A stall is a sustained main-thread freeze — usually 3× baseline or more. The cause is classified into <strong>MainThreadFreeze</strong>, <strong>GcDominated</strong>, or <strong>Suspended</strong> (game paused by OS). Stalls correlate with GC pressure; the per-window <code>gen0/1/2</code> counts tell you which.'
  },
  'insights': {
    title: 'Insights',
    body: 'Heuristic pattern records computed from your session and lifetime data. Examples: <em>mod X has been #1 in 9 of 10 Blood Moons</em>, or <em>this Jungle visit ran 63% above your lifetime average</em>. Strictly descriptive — never prescriptive.'
  },
  'self-severity': {
    title: 'Profiler severity',
    body: 'Severity is the <strong>ratio of bytes-per-hook to a measured baseline</strong> (36 KB/hook in v0.9). Healthy &lt; 1.5×, Concerning 1.5×–2.5×, Severe ≥ 2.5×. Tracks our regressions cleanly — independent of how big your modlist is.'
  },
  'install-delta': {
    title: 'Install delta',
    body: 'Managed-heap memory the profiler added during its hook install pass. Scales linearly with modlist size: ~36 KB per installed hook.'
  },
  'process-context': {
    title: 'Process context',
    body: 'How tModLoader\'s total memory breaks down. <strong>Managed heap</strong> is .NET-tracked memory (us + every mod + the runtime). <strong>Working set</strong> is total RAM the OS sees the process using, including native code and textures.'
  },
  'spark-frame': {
    title: 'Frame time sparkline',
    body: 'Per-tick frame duration over the last ~30 s. Spikes upward = slow frames; flat = smooth.'
  },
  'spark-gc': {
    title: 'GC time per tick',
    body: 'How much of each frame was lost to .NET garbage collection over the last ~30 s. Big bumps here usually coincide with stalls — too much memory was being allocated and the runtime had to pause to clean up.'
  },
  'spark-spike': {
    title: 'Spike markers',
    body: 'One vertical tick per detected spike window in the last ~30 s. A spike = a frame ≥ 2× the session median.'
  },
  'kpi-fps': {
    title: 'Avg FPS',
    body: 'Average frames-per-second over the last 30 s, derived from <code>1000 / avg-frame-ms</code> and clamped to 60 (Terraria\'s tick ceiling).'
  },
  'kpi-worst': {
    title: 'Worst frame',
    body: 'The single slowest frame in the last 30 s. Above ~33 ms is felt as a hitch; above 100 ms is a visible stutter.'
  },
  'kpi-spikes': {
    title: 'Lag spikes (>50 ms)',
    body: 'Count of frames in the last 30 s that exceeded 50 ms — the perceptual threshold where players actually notice a hitch.'
  },
  'kpi-stalls': {
    title: 'Stalls · session total',
    body: 'Sustained main-thread freezes (multi-second pauses) detected this session. Usually correlates with GC pressure or window-focus loss.'
  },
  'heatmap': {
    title: 'Session timeframe',
    body: 'One cell per minute of play. Cell colour = average frame time for that minute (green smooth → red painful). Cells with a red halo had a boss fight during that minute.'
  },
};

const tipEl = document.getElementById('tooltip');
function showTooltip(target, key) {
  const t = TOOLTIPS[key];
  if (!t) return;
  tipEl.innerHTML = '<span class=""tip-title"">' + t.title + '</span>' + t.body;
  tipEl.classList.remove('hidden');
  const rect = target.getBoundingClientRect();
  const tipRect = tipEl.getBoundingClientRect();
  let left = rect.left + rect.width / 2 - tipRect.width / 2;
  let top = rect.bottom + 6;
  if (left < 8) left = 8;
  if (left + tipRect.width > window.innerWidth - 8) left = window.innerWidth - tipRect.width - 8;
  if (top + tipRect.height > window.innerHeight - 8) top = rect.top - tipRect.height - 6;
  tipEl.style.left = left + 'px';
  tipEl.style.top = top + 'px';
}
function hideTooltip() { tipEl.classList.add('hidden'); }
document.addEventListener('mouseover', e => {
  const t = e.target.closest('[data-explain]');
  if (t) showTooltip(t, t.dataset.explain);
});
document.addEventListener('mouseout', e => {
  if (e.target.closest('[data-explain]')) hideTooltip();
});

// ====== Topbar / footer ==============================================
const previousValues = {};
function renderTopbar() {
  if (!lastNow || !lastNow.worldLoaded) {
    ['ts-tick','ts-frame','ts-avg','ts-gc','ts-backend'].forEach(id => document.getElementById(id).textContent = '—');
    return;
  }
  setTopstat('ts-tick',    '#' + fmtInt(lastNow.tickIndex));
  setTopstat('ts-frame',   fmtMs(lastNow.frameMs) + 'ms');
  setTopstat('ts-avg',     fmtMs(lastNow.avg30sMs) + 'ms');
  setTopstat('ts-gc',      fmtMs(lastNow.gcMs) + 'ms');
  setTopstat('ts-backend', lastNow.backend || '—');
}
function setTopstat(id, v) {
  const el = document.getElementById(id);
  if (previousValues[id] !== v) {
    el.classList.remove('flash'); void el.offsetWidth; el.classList.add('flash');
    previousValues[id] = v;
  }
  el.textContent = v;
}
function renderFooter() {
  document.getElementById('foot-clock').textContent = new Date().toLocaleTimeString();
  document.getElementById('foot-mode').textContent = lastNow && lastNow.worldLoaded
    ? lastNow.npcCount + ' npc · ' + lastNow.projectileCount + ' proj · ' + lastNow.dustCount + ' dust'
    : 'idle';
}

// ====== Master render dispatcher =====================================
function renderAll() {
  switch (activeTab) {
    case 'summary':  renderSummary();  break;
    case 'timeline': renderTimeline(); break;
    case 'lag':      renderLag();      break;
    case 'insights': renderInsights(); break;
    case 'self':     renderSelf();     break;
  }
}

// ====== SUMMARY =======================================================
function renderSummary() {
  renderKpiStrip();
  renderFrameChart();
  renderDonut();
  renderTrendSparklines();
  renderHeatmap();
  renderNowPlaying();
  renderNowEvents();
  renderSummaryMods();
}

// ====== KPI strip =====================================================
// KPI values are computed server-side (KpiCalculator.Compute) and
// delivered in /api/now's `kpi` block. The dashboard picks color
// bands + sub-rows + draws the spark. Each card has a hero number,
// a status tag, two or three sub-stats, and a sparkline.
function renderKpiStrip() {
  if (!lastNow || !lastNow.worldLoaded || !lastNow.kpi || lastNow.kpi.sampleN === 0) {
    setKpiEmpty('fps'); setKpiEmpty('worst');
    setKpiEmpty('spikes'); setKpiEmpty('stalls');
    return;
  }
  const k = lastNow.kpi;
  const ms = (lastFrames && lastFrames.frameMs) || [];

  // ---------- avg fps ----------
  const fpsClass = k.avgFps >= 55 ? 'good' : k.avgFps >= 30 ? 'warn' : 'bad';
  const fpsTag = k.avgFps < 30 ? 'rough' : k.avgFps < 55 ? 'okay' : 'smooth';
  setKpi('fps', {
    value: k.avgFps.toFixed(0),
    valueClass: fpsClass,
    tag: fpsTag, tagClass: fpsClass,
    subs: [
      { k: 'median', v: fmtMs(k.medianFrameMs) + 'ms' },
      { k: 'best',   v: fmtMs(k.bestFrameMs) + 'ms' },
      { k: 'samples', v: fmtInt(k.sampleN) },
    ],
    sparkVals: ms.length > 1 ? ms.map(v => v > 0 ? 1000 / Math.max(1, v) : 0) : null,
    sparkClass: fpsClass,
  });

  // ---------- worst frame ----------
  const worstClass = k.worstFrameMs > 100 ? 'bad' : k.worstFrameMs > 50 ? 'orange' : k.worstFrameMs > 33 ? 'warn' : 'good';
  const worstTag = k.worstFrameMs > 100 ? 'stutter' : k.worstFrameMs > 50 ? 'hitch' : k.worstFrameMs > 33 ? 'felt' : 'smooth';
  setKpi('worst', {
    value: fmtMs(k.worstFrameMs),
    valueClass: worstClass,
    tag: worstTag, tagClass: worstClass,
    subs: [
      { k: 'avg 30s', v: fmtMs(lastNow.avg30sMs) + 'ms' },
      { k: 'median', v: fmtMs(k.medianFrameMs) + 'ms' },
      { k: 'best',   v: fmtMs(k.bestFrameMs) + 'ms' },
    ],
    sparkVals: ms, sparkClass: worstClass,
  });

  // ---------- lag spikes (>50ms in last 30s) ----------
  const spClass = k.lagSpikeCount >= 5 ? 'bad' : k.lagSpikeCount >= 1 ? 'orange' : 'good';
  const spTag = k.lagSpikeCount === 0 ? 'clean' : k.lagSpikeCount >= 5 ? 'noisy' : 'occasional';
  setKpi('spikes', {
    value: String(k.lagSpikeCount),
    valueClass: spClass,
    tag: spTag, tagClass: spClass,
    subs: [
      { k: 'session', v: fmtInt(k.spikeCount) },
      { k: 'lag total', v: fmtMs(k.totalLagMs) + 'ms' },
      { k: 'threshold', v: '>50ms' },
    ],
    sparkVals: null,
    sparkClass: spClass,
  });

  // ---------- stalls (session-cumulative) ----------
  const stClass = k.stallCount > 0 ? (k.stallCount > 5 ? 'bad' : 'orange') : 'good';
  const stTag = k.stallCount === 0 ? 'clean' : k.stallCount >= 5 ? 'rough' : 'sporadic';
  setKpi('stalls', {
    value: String(k.stallCount),
    valueClass: stClass,
    tag: stTag, tagClass: stClass,
    subs: [
      { k: 'biggest', v: fmtMs(k.worstStallMs) + 'ms' },
      { k: 'average', v: fmtMs(k.avgStallMs) + 'ms' },
      { k: 'in 30s',  v: 'see chart' },
    ],
    sparkVals: null,
    sparkClass: stClass,
  });
}

function setKpiEmpty(name) {
  setKpi(name, {
    value: '—', valueClass: '',
    tag: '', tagClass: '',
    subs: [
      { k: '—', v: '—' }, { k: '—', v: '—' },
    ],
    sparkVals: null, sparkClass: '',
  });
}

function setKpi(name, opts) {
  const v = document.getElementById('kpi-' + name + '-v');
  const tag = document.getElementById('kpi-' + name + '-tag');
  const subs = document.getElementById('kpi-' + name + '-subs');
  const spark = document.getElementById('kpi-' + name + '-spark');

  v.textContent = opts.value;
  v.className = 'v ' + (opts.valueClass || '');
  tag.textContent = opts.tag || '';
  tag.className = 'kpi-tag ' + (opts.tagClass || '');

  subs.innerHTML = (opts.subs || []).map(s =>
    `<div class='kpi-sub'><span class='k'>${escapeHtml(s.k)}</span><span class='v'>${escapeHtml(s.v)}</span></div>`
  ).join('');

  if (spark) {
    if (!opts.sparkVals || opts.sparkVals.length < 2) { spark.innerHTML = ''; }
    else {
      const vals = opts.sparkVals;
      const max = Math.max(0.0001, Math.max(...vals));
      const min = Math.min(...vals);
      const range = Math.max(0.0001, max - min);
      let d = '';
      for (let i = 0; i < vals.length; i++) {
        const x = (i / Math.max(1, vals.length - 1)) * 100;
        const y = 15 - ((vals[i] - min) / range) * 13;
        d += (i === 0 ? 'M' : 'L') + x.toFixed(2) + ',' + y.toFixed(2) + ' ';
      }
      const c = opts.sparkClass === 'bad' ? 'var(--danger)' : opts.sparkClass === 'orange' ? 'var(--orange)' : opts.sparkClass === 'warn' ? 'var(--amber)' : 'var(--good)';
      spark.innerHTML = `<path d='${d}' fill='none' stroke='${c}' stroke-width='0.6'/>`;
    }
  }
}

// ====== Heatmap =======================================================
function renderHeatmap() {
  const root = document.getElementById('heatmap-grid');
  const sub = document.getElementById('heatmap-sub');
  if (!lastHeatmap || !lastHeatmap.worldLoaded || !lastHeatmap.buckets) {
    root.innerHTML = '';
    sub.textContent = 'waiting for data…';
    return;
  }
  const buckets = lastHeatmap.buckets;
  if (buckets.length === 0) {
    root.innerHTML = '<div class=""empty-line"">no minutes recorded yet</div>';
    sub.textContent = '0 minutes';
    return;
  }

  // For each bucket, determine which boss-segment (if any) overlaps it.
  function bossLabelFor(b) {
    if (!lastHeatmap.bossOverlays) return null;
    const end = b.startUnixMs + (lastHeatmap.bucketMs || 60000);
    for (const o of lastHeatmap.bossOverlays) {
      if (o.endUnixMs >= b.startUnixMs && o.startUnixMs <= end) return o;
    }
    return null;
  }

  function bandClass(avgMs) {
    if (avgMs <= 17)  return 'p0';
    if (avgMs <= 25)  return 'p1';
    if (avgMs <= 40)  return 'p2';
    if (avgMs <= 60)  return 'p3';
    return 'p4';
  }

  sub.textContent = buckets.length + ' minute(s) · ' + (lastHeatmap.bossOverlays?.length || 0) + ' boss segment(s)';
  root.innerHTML = buckets.map(b => {
    const cls = bandClass(b.avgMs);
    const boss = bossLabelFor(b);
    const time = new Date(b.startUnixMs).toLocaleTimeString();
    const tip = (boss ? boss.name + ' · ' : '') + time + ' · avg ' + fmtMs(b.avgMs) + 'ms · worst ' + fmtMs(b.worstMs) + 'ms';
    return `<div class='hm-cell ${cls} ${boss ? 'boss' : ''}' data-tip='${escapeHtml(tip)}'></div>`;
  }).join('');
}

function renderFrameChart() {
  const svg = document.getElementById('frame-chart');
  const sub = document.getElementById('chart-sub');
  const title = document.getElementById('chart-title');
  if (!lastFrames || !lastFrames.worldLoaded || !lastFrames.frameMs || lastFrames.frameMs.length === 0) {
    svg.innerHTML = ''; sub.textContent = '—'; return;
  }
  const ms = lastFrames.frameMs;
  const n = ms.length;

  // Map series + axis depending on toggle.
  const showFps = frameChartMode === 'fps';
  title.textContent = showFps ? 'fps · last 30s' : 'frame time · last 30s';
  const series = showFps ? ms.map(v => v > 0 ? Math.min(120, 1000 / Math.max(0.5, v)) : 0) : ms;
  const sortedMs = ms.slice().sort((a, b) => a - b);
  const medianMs = sortedMs[Math.floor(n / 2)];
  const thresholdMs = medianMs * 2;
  const sortedSeries = series.slice().sort((a, b) => a - b);
  const medianS = sortedSeries[Math.floor(n / 2)];
  const max = Math.max(showFps ? 60 : 2, Math.max(...series) * 1.08);
  // For FPS, low values are bad. For ms, high values are bad. Adjust accordingly.
  const thresholdS = showFps ? 1000 / Math.max(0.5, thresholdMs) : thresholdMs;

  sub.textContent = showFps
    ? n + ' frames · median ' + medianS.toFixed(0) + ' fps · target 60'
    : n + ' frames · median ' + fmtMs(medianMs) + 'ms · spike ≥ ' + fmtMs(thresholdMs) + 'ms';

  const w = 100, h = 28;
  let pathD = '', areaD = '';
  for (let i = 0; i < n; i++) {
    const x = (i / Math.max(1, n - 1)) * w;
    const v = showFps ? series[i] : Math.min(series[i], max);
    const y = h - (v / max) * h;
    pathD += (i === 0 ? 'M' : 'L') + x.toFixed(2) + ',' + y.toFixed(2) + ' ';
    areaD += (i === 0 ? 'M' : 'L') + x.toFixed(2) + ',' + y.toFixed(2) + ' ';
  }
  areaD += 'L' + w + ',' + h + ' L0,' + h + ' Z';

  const thresholdY = h - (Math.min(thresholdS, max) / max) * h;
  const medianY = h - (medianS / max) * h;

  // Spike markers within window. Color red because they're bad.
  const firstTick = lastFrames.firstTick, lastTick = lastFrames.lastTick;
  let marks = '';
  if (lastFrames.spikeMarks && firstTick != null) {
    for (const m of lastFrames.spikeMarks) {
      const x = ((m.tick - firstTick) / Math.max(1, lastTick - firstTick)) * w;
      const v = showFps ? Math.min(120, 1000 / Math.max(0.5, m.ms)) : m.ms;
      const y = h - (Math.min(v, max) / max) * h;
      marks += '<circle cx=""' + x.toFixed(2) + '"" cy=""' + y.toFixed(2) + '"" r=""0.8"" fill=""#b94e58"" stroke=""#0a0d12"" stroke-width=""0.15""/>';
    }
  }

  // Series color: signature electric blue for ms, cool cyan for fps.
  const seriesColor = showFps ? '#4ab8c2' : '#4a9eff';
  svg.innerHTML = `
    <defs>
      <linearGradient id='g-area' x1='0' y1='0' x2='0' y2='1'>
        <stop offset='0%' stop-color='${seriesColor}' stop-opacity='0.45'/>
        <stop offset='100%' stop-color='${seriesColor}' stop-opacity='0.02'/>
      </linearGradient>
    </defs>
    <line x1='0' y1='${thresholdY}' x2='${w}' y2='${thresholdY}' stroke='#ff9e64' stroke-width='0.25' stroke-dasharray='0.8,0.8'/>
    <line x1='0' y1='${medianY}' x2='${w}' y2='${medianY}' stroke='#565f89' stroke-width='0.2' stroke-dasharray='0.5,0.8'/>
    <path d='${areaD}' fill='url(#g-area)'/>
    <path d='${pathD}' fill='none' stroke='${seriesColor}' stroke-width='0.5' stroke-linejoin='round' stroke-linecap='round'/>
    ${marks}
  `;
}

// Frame-chart mode toggle.
const chartModeEl = document.getElementById('chart-mode');
if (chartModeEl) {
  chartModeEl.addEventListener('click', e => {
    const b = e.target.closest('button');
    if (!b) return;
    frameChartMode = b.dataset.mode;
    document.querySelectorAll('#chart-mode button').forEach(x => x.classList.toggle('active', x === b));
    renderFrameChart();
  });
}

function renderDonut() {
  const svg = document.getElementById('donut-svg');
  const legend = document.getElementById('donut-legend');
  const pctEl = document.getElementById('donut-pct');
  const nameEl = document.getElementById('donut-name');
  const msEl = document.getElementById('donut-ms');
  const sub = document.getElementById('donut-sub');

  if (!lastMods || !lastMods.worldLoaded || !lastMods.mods) {
    svg.innerHTML = ''; legend.innerHTML = '';
    pctEl.textContent = '—'; nameEl.textContent = '—'; msEl.textContent = '';
    sub.textContent = '—';
    return;
  }
  const sorted = lastMods.mods.slice().filter(m => m.cpuMs > 0.001).sort((a, b) => b.cpuMs - a.cpuMs);
  const total = sorted.reduce((s, m) => s + m.cpuMs, 0);
  if (total <= 0) { svg.innerHTML = ''; legend.innerHTML = ''; sub.textContent = 'idle'; return; }
  sub.textContent = sorted.length + ' active · ' + fmtMs(total) + ' ms/t';

  const top = sorted.slice(0, 6);
  const rest = sorted.slice(6);
  const restSum = rest.reduce((s, m) => s + m.cpuMs, 0);

  let acc = 0;
  let paths = '';
  for (const m of top) {
    const frac = m.cpuMs / total;
    paths += donutSlice(acc, acc + frac, modColor(m.id));
    acc += frac;
  }
  if (restSum > 0) paths += donutSlice(acc, 1, '#3a3f4a');
  svg.innerHTML = paths;

  const headliner = top[0];
  pctEl.textContent = (headliner.cpuMs / total * 100).toFixed(0) + '%';
  nameEl.textContent = truncate(headliner.name, 18);
  msEl.textContent = fmtMs(headliner.cpuMs) + ' ms/t';

  legend.innerHTML = top.map(m => `
    <div class='leg'>
      <span class='sw' style='background:${modColor(m.id)}'></span>
      <span class='nm'>${escapeHtml(m.name)}</span>
      <span class='pc'>${(m.cpuMs / total * 100).toFixed(1)}%</span>
    </div>
  `).join('') + (rest.length > 0 ? `<div class='leg'><span class='sw' style='background:#3a3f4a'></span><span class='nm'>+ ${rest.length} more</span><span class='pc'>${(restSum/total*100).toFixed(1)}%</span></div>` : '');
}

function donutSlice(from, to, color) {
  // Outer radius 50, inner 32; convert fraction (0-1) to angle (-PI/2 start, clockwise).
  const startA = -Math.PI / 2 + from * Math.PI * 2;
  const endA   = -Math.PI / 2 + to   * Math.PI * 2;
  const r1 = 50, r2 = 32;
  const x1 = Math.cos(startA) * r1, y1 = Math.sin(startA) * r1;
  const x2 = Math.cos(endA)   * r1, y2 = Math.sin(endA)   * r1;
  const x3 = Math.cos(endA)   * r2, y3 = Math.sin(endA)   * r2;
  const x4 = Math.cos(startA) * r2, y4 = Math.sin(startA) * r2;
  const largeArc = (to - from) > 0.5 ? 1 : 0;
  return `<path d='M ${x1} ${y1} A ${r1} ${r1} 0 ${largeArc} 1 ${x2} ${y2} L ${x3} ${y3} A ${r2} ${r2} 0 ${largeArc} 0 ${x4} ${y4} Z' fill='${color}'/>`;
}

function renderTrendSparklines() {
  if (!lastFrames || !lastFrames.frameMs || lastFrames.frameMs.length === 0) {
    document.getElementById('spark-frame').innerHTML = '';
    document.getElementById('spark-alloc').innerHTML = '';
    document.getElementById('spark-spike').innerHTML = '';
    return;
  }
  drawSpark('spark-frame', lastFrames.frameMs, '#4a9eff');
  // alloc: derive a rough proxy from gc time (no per-tick alloc series). Substitute zero series otherwise.
  drawSpark('spark-alloc', lastFrames.gcMs || [], '#c39ad8');
  // spike density: counts within a sliding window. For simplicity show a marker per spike.
  drawSpikeMarkers('spark-spike', lastFrames);
}

function drawSpark(id, vals, color) {
  const svg = document.getElementById(id);
  if (!vals || vals.length === 0) { svg.innerHTML = ''; return; }
  const max = Math.max(0.5, Math.max(...vals));
  const n = vals.length;
  let d = '';
  for (let i = 0; i < n; i++) {
    const x = (i / Math.max(1, n - 1)) * 100;
    const y = 16 - (vals[i] / max) * 14;
    d += (i === 0 ? 'M' : 'L') + x.toFixed(2) + ',' + y.toFixed(2) + ' ';
  }
  svg.innerHTML = `<path d='${d}' fill='none' stroke='${color}' stroke-width='0.5'/>`;
}

function drawSpikeMarkers(id, frames) {
  const svg = document.getElementById(id);
  if (!frames.spikeMarks || frames.spikeMarks.length === 0 || frames.firstTick == null) {
    svg.innerHTML = '<line x1=""0"" y1=""15"" x2=""100"" y2=""15"" stroke=""#3a3f4a"" stroke-width=""0.2""/>';
    return;
  }
  let marks = '';
  const span = Math.max(1, frames.lastTick - frames.firstTick);
  for (const m of frames.spikeMarks) {
    const x = ((m.tick - frames.firstTick) / span) * 100;
    marks += `<line x1='${x.toFixed(2)}' y1='2' x2='${x.toFixed(2)}' y2='14' stroke='#c97f3c' stroke-width='0.4'/>`;
  }
  svg.innerHTML = '<line x1=""0"" y1=""15"" x2=""100"" y2=""15"" stroke=""#3a3f4a"" stroke-width=""0.2""/>' + marks;
}

function renderNowPlaying() {
  const root = document.getElementById('nowlist');
  const sub = document.getElementById('now-sub');
  if (!lastSegments || !lastSegments.open || lastSegments.open.length === 0) {
    root.innerHTML = '<div class=""empty-line"">no open segments — wander into a biome, fight a boss, wait for weather</div>';
    sub.textContent = '0 open';
    return;
  }
  sub.textContent = lastSegments.open.length + ' open';
  root.innerHTML = lastSegments.open
    .slice().sort((a, b) => familyWeight(a.family) - familyWeight(b.family))
    .map(s => {
      const top = s.topModName
        ? `<span class='mod'>${truncate(s.topModName, 16)}</span> · ${fmtMs(s.topModMsPerTick)}ms/t`
        : '<span class=""muted"">—</span>';
      return `<div class='now-seg rich' data-family='${s.family}'>
        <span class='swatch'></span>
        <span class='name-block'>
          <span class='top'><span class='family-tag'>${escapeHtml(s.family)}</span>${escapeHtml(s.name)}</span>
          <span class='sub'>${fmtDuration(s.elapsedMs)} · ${fmtInt(s.ticks)} ticks${s.spikeCount > 0 ? ' · ⚡' + s.spikeCount : ''}${s.deathCount > 0 ? ' · ☠' + s.deathCount : ''}</span>
        </span>
        <span class='meta'>${top}</span>
      </div>`;
    }).join('');
}
function familyWeight(f) {
  const order = ['Boss', 'Invasion', 'UserBookmark', 'Weather', 'Subworld', 'Combat', 'Hardmode', 'DeathBracket', 'Biome'];
  const i = order.indexOf(f); return i < 0 ? 99 : i;
}

function renderNowEvents() {
  const root = document.getElementById('nowevents');
  // /api/events delivers a pre-merged, pre-sorted, capped feed —
  // the JS just renders. Replaces the previous client-side merge
  // across segments + spikes (which had no access to stalls and
  // got stall+segment interleaving wrong).
  if (!lastEvents || !lastEvents.events || lastEvents.events.length === 0) {
    root.innerHTML = '<div class=""empty-line"">nothing yet — events appear as segments close + spikes fire</div>';
    return;
  }
  root.innerHTML = lastEvents.events.map(e =>
    `<div class='event' data-kind='${e.kind}'>
      <span class='glyph'>${glyphFor(e.kind)}</span>
      <span class='what'>${escapeHtml(e.text)}</span>
      <span class='when'>${fmtAgo(e.unixMs)}</span>
    </div>`
  ).join('');
}
function glyphFor(kind) {
  return ({ 'boss-kill':'✓', 'death':'☠', 'spike':'⚡', 'stall':'⏸', 'segment':'↺' })[kind] || '·';
}

// ====== Summary: Mod tree =============================================
function renderSummaryMods() {
  const root = document.getElementById('modtable');
  if (!lastMods || !lastMods.worldLoaded || !lastMods.mods) {
    root.innerHTML = '<div class=""empty-line"">no data yet</div>';
    return;
  }

  // Toggle alloc visibility in header.
  const allocHeader = document.getElementById('mh-alloc');
  const sortAllocBtn = document.getElementById('sort-alloc');
  if (lastMods.tracksAllocations) {
    allocHeader.style.opacity = ''; sortAllocBtn.style.display = '';
  } else {
    allocHeader.style.opacity = '0.4'; sortAllocBtn.style.opacity = '0.4';
  }

  // Filter + sort.
  const q = modFilter.trim().toLowerCase();
  let mods = lastMods.mods.filter(m => (m.cpuMs > 0 || m.avgCpuMs > 0) && (q === '' || m.name.toLowerCase().includes(q)));
  let getter;
  switch (modSort) {
    case 'cpu':    getter = m => m.cpuMs; break;
    case 'avg':    getter = m => m.avgCpuMs; break;
    case 'alloc':  getter = m => m.allocBytes || 0; break;
    default:       getter = m => m.cpuMs * 0.7 + m.avgCpuMs * 0.3; break;
  }
  mods.sort((a, b) => getter(b) - getter(a));
  const max = mods.length > 0 ? getter(mods[0]) : 1;
  const median = mods.length > 0 ? getter(mods[Math.floor(mods.length / 2)]) : 0;
  const outlierCut = median * 2.5;

  // Color-grade bars by per-mod ratio against the median: bar color
  // shifts from green (calm) → yellow → orange → red as a mod climbs.
  // Provides at-a-glance answer to ""is this mod actually expensive
  // for this session"" beyond just the bar length.
  function barColor(value) {
    if (median <= 0) return 'var(--perf-0)';
    const r = value / median;
    if (r < 1.5) return 'var(--perf-0)';
    if (r < 3)   return 'var(--perf-1)';
    if (r < 6)   return 'var(--perf-2)';
    if (r < 12)  return 'var(--perf-3)';
    return 'var(--perf-4)';
  }

  let html = '';
  for (let i = 0; i < mods.length; i++) {
    const m = mods[i];
    const v = getter(m);
    const isTop = i < 3;
    const isOutlier = v > outlierCut && i < 3;
    const isOpen = expandedMods.has(m.id);
    const sparkSvg = renderModSparkInline(m.id);
    const color = barColor(v);
    html += `<div class='modrow ${isTop ? 'is-top' : ''} ${isOutlier ? 'outlier' : ''}' data-mod='${m.id}'>
      <span class='rank'>${i + 1}</span>
      <span class='name'>
        <span class='twirl' data-role='twirl'>▶</span>
        <span class='modname' data-role='name'>${escapeHtml(m.name)}</span>
      </span>
      <span class='bar'><span style='width: ${(v / max * 100).toFixed(1)}%; background: ${color}'></span></span>
      <span class='spark'>${sparkSvg}</span>
      <span class='ms'>${fmtMs(m.cpuMs)}<span class='u'>ms</span></span>
      <span class='ms'>${fmtMs(m.avgCpuMs)}<span class='u'>avg</span></span>
      <span class='alloc'>${lastMods.tracksAllocations ? fmtBytes(m.allocBytes) : '—'}</span>
    </div>`;
    if (isOpen) html += renderModTree(m);
  }
  root.innerHTML = html;

  // Toggle open class on opens.
  for (const id of expandedMods) {
    const r = root.querySelector('.modrow[data-mod=""' + id + '""]');
    if (r) r.classList.add('open');
  }

  // Wire row clicks.
  root.querySelectorAll('.modrow').forEach(row => {
    const modId = parseInt(row.dataset.mod, 10);
    row.querySelector('[data-role=twirl]').addEventListener('click', e => {
      e.stopPropagation();
      toggleExpandMod(modId);
    });
    row.querySelector('[data-role=name]').addEventListener('click', e => {
      e.stopPropagation();
      openModCard(modId);
    });
    row.addEventListener('click', () => toggleExpandMod(modId));
  });
}

function renderModSparkInline(modId) {
  const arr = modSparkHistory.get(modId);
  if (!arr || arr.length < 2) return '';
  const max = Math.max(0.005, Math.max(...arr));
  let d = '';
  for (let i = 0; i < arr.length; i++) {
    const x = (i / Math.max(1, arr.length - 1)) * 100;
    const y = 16 - (arr[i] / max) * 14;
    d += (i === 0 ? 'M' : 'L') + x.toFixed(2) + ',' + y.toFixed(2) + ' ';
  }
  return `<svg viewBox='0 0 100 16' preserveAspectRatio='none'><path d='${d}' fill='none' stroke='#6ec07e' stroke-width='0.7'/></svg>`;
}

function renderModTree(mod) {
  // Group hook records by mod, then by category.
  if (!lastHooks || !lastHooks.hooks) {
    return `<div class='mod-tree'><div class='cat-row'><span class='twirl'></span><span class='name muted'>loading…</span></div></div>`;
  }
  const cats = lastMods.categories || [];
  // Build categoryId → hooks[] for this mod.
  const buckets = new Map();
  for (const h of lastHooks.hooks) {
    if (h.modId !== mod.id) continue;
    let arr = buckets.get(h.categoryId);
    if (!arr) { arr = []; buckets.set(h.categoryId, arr); }
    arr.push(h);
  }
  if (buckets.size === 0) {
    return `<div class='mod-tree'><div class='hook-row'><span></span><span class='name muted'>no active hooks for this mod (yet)</span><span></span><span></span><span></span><span></span><span></span></div></div>`;
  }
  // Sort each category by cpuMs desc, sort categories by total cpuMs.
  const catOrder = [...buckets.entries()].map(([catId, arr]) => {
    arr.sort((a, b) => b.cpuMs - a.cpuMs);
    const total = arr.reduce((s, h) => s + h.cpuMs, 0);
    return { catId, total, hooks: arr };
  }).sort((a, b) => b.total - a.total);

  const max = catOrder[0].total || 1;

  let html = '<div class=""mod-tree"">';
  for (const c of catOrder) {
    const catName = cats[c.catId] || ('cat:' + c.catId);
    const catKey = mod.id + '|' + c.catId;
    const catOpen = expandedCategories.has(catKey);
    html += `<div class='cat-row ${catOpen ? 'open' : ''}' data-cat='${catKey}'>
      <span class='twirl'>▶</span>
      <span class='name'>${escapeHtml(catName)}</span>
      <span class='bar'><span style='width: ${(c.total / max * 100).toFixed(1)}%; background: #4978a8'></span></span>
      <span></span>
      <span class='ms'>${fmtMs(c.total)}<span class='u'>ms</span></span>
      <span class='muted' style='text-align:right'>${c.hooks.length} hooks</span>
      <span></span>
    </div>`;
    if (catOpen) {
      const hookMax = c.hooks[0].cpuMs || 1;
      for (const h of c.hooks.slice(0, 20)) {
        html += `<div class='hook-row'>
          <span></span>
          <span></span>
          <span class='name'>${escapeHtml(truncate(h.display, 60))}</span>
          <span class='bar'><span style='width: ${(h.cpuMs / hookMax * 100).toFixed(1)}%'></span></span>
          <span class='ms'>${fmtMs(h.cpuMs)}<span class='u'>ms</span></span>
          <span class='ms'>${fmtMs(h.avgCpuMs)}<span class='u'>avg</span></span>
          <span class='alloc'>${lastHooks.tracksAllocations ? fmtBytes(h.allocBytes) : '—'}</span>
        </div>`;
      }
      if (c.hooks.length > 20) {
        html += `<div class='hook-row'><span></span><span></span><span class='name muted'>+ ${c.hooks.length - 20} quieter hooks</span><span></span><span></span><span></span><span></span></div>`;
      }
    }
  }
  html += '</div>';
  // Bind category twirls — done after render via event delegation in toggleExpandMod's caller
  setTimeout(() => {
    document.querySelectorAll('.cat-row').forEach(r => {
      const key = r.dataset.cat;
      if (!r.dataset.bound) {
        r.dataset.bound = '1';
        r.addEventListener('click', e => {
          e.stopPropagation();
          if (expandedCategories.has(key)) expandedCategories.delete(key);
          else expandedCategories.add(key);
          renderSummaryMods();
        });
      }
    });
  }, 0);
  return html;
}

function toggleExpandMod(modId) {
  if (expandedMods.has(modId)) expandedMods.delete(modId);
  else { expandedMods.add(modId); pollHooks(); }
  renderSummaryMods();
}

// Mod-tree sort + filter wiring.
document.getElementById('mods-sort').addEventListener('click', e => {
  const b = e.target.closest('button');
  if (!b) return;
  modSort = b.dataset.sort;
  document.querySelectorAll('#mods-sort button').forEach(x => x.classList.toggle('active', x === b));
  if (activeTab === 'summary') renderSummaryMods();
});
document.getElementById('mod-filter').addEventListener('input', e => {
  modFilter = e.target.value;
  if (activeTab === 'summary') renderSummaryMods();
});

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
  const catTotal = mod.categories.reduce((s, v) => s + v, 0);
  const catMax = Math.max(...mod.categories);
  const catRows = mod.categories.map((v, i) => {
    if (v <= 0.001) return '';
    return `<div class='mc-cat-row'>
      <span class='nm'>${escapeHtml(cats[i] || 'cat:' + i)}</span>
      <span class='br'><span style='width: ${(v / catMax * 100).toFixed(1)}%'></span></span>
      <span class='vl'>${fmtMs(v)} ms</span>
    </div>`;
  }).filter(Boolean).join('');

  body.innerHTML = `
    <div class='mc-section'>
      <h3>cost · live</h3>
      <div class='mc-stat-grid'>
        <div class='mc-stat'><span class='k'>cpu now</span><span class='v big'>${fmtMs(mod.cpuMs)} ms/t</span><span class='sub'>this tick</span></div>
        <div class='mc-stat'><span class='k'>cpu avg</span><span class='v big'>${fmtMs(mod.avgCpuMs)} ms/t</span><span class='sub'>session</span></div>
        <div class='mc-stat'><span class='k'>alloc/t</span><span class='v'>${lastMods.tracksAllocations ? fmtBytes(mod.allocBytes) : '—'}</span><span class='sub'>${lastMods.tracksAllocations ? 'tracked' : 'off'}</span></div>
        <div class='mc-stat'><span class='k'>share</span><span class='v accent'>${(mod.cpuMs / (sorted.reduce((s,m)=>s+m.cpuMs,0) || 1) * 100).toFixed(1)}%</span><span class='sub'>of all mods</span></div>
      </div>
    </div>

    <div class='mc-section'>
      <h3>hypothetical · without this mod</h3>
      <div class='mc-callout'>
        if this mod were removed, your frame would be roughly <strong>${fmtMs(withoutAvg)} ms</strong>
        (vs <strong>${fmtMs(totalAvg)} ms</strong> now). predicted fps: <strong>${fpsWithoutAvg.toFixed(0)}</strong>
        (vs <strong>${fpsAvg.toFixed(0)}</strong>).
        <br/><span class='muted'>caveat: this assumes the other mods' behavior wouldn't change. some mods reduce their own work when sibling content isn't present, so this is an upper bound on the gain.</span>
      </div>
    </div>

    <div class='mc-section'>
      <h3>category breakdown</h3>
      <div class='mc-catlist'>${catRows || '<span class=""muted"">no per-category activity yet</span>'}</div>
    </div>

    <div class='mc-section'>
      <h3>next steps</h3>
      <div class='mc-callout'>
        click the row on the Summary tab to expand the cascading tree — drill into which specific hooks inside this mod are doing the work.
      </div>
    </div>
  `;
  card.classList.remove('hidden');
}

function closeModCard() {
  document.getElementById('modcard').classList.add('hidden');
}
document.getElementById('mc-close').addEventListener('click', closeModCard);

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

// ====== LAG TAB =======================================================
let lagFilter = 'all';
const lagFilterEl = document.getElementById('lag-filter');
if (lagFilterEl) {
  lagFilterEl.addEventListener('click', e => {
    const b = e.target.closest('button');
    if (!b) return;
    lagFilter = b.dataset.lagFilter;
    document.querySelectorAll('#lag-filter button').forEach(x => x.classList.toggle('active', x === b));
    if (activeTab === 'lag') renderLag();
  });
}

function renderLag() {
  const root = document.getElementById('lagfeed');
  const sub = document.getElementById('lag-sub');
  if (!root) return;

  // Merge spikes + stalls into a single chronological feed, newest first.
  const items = [];
  if ((lagFilter === 'all' || lagFilter === 'spikes') && lastSpikes && lastSpikes.spikes) {
    for (const s of lastSpikes.spikes) {
      const unix = lastNow ? lastNow.unixMs - Math.max(0, (lastNow.tickIndex - s.worstTick)) * 1000 / 60 : Date.now();
      items.push({ kind: 'spike', data: s, unix });
    }
  }
  if ((lagFilter === 'all' || lagFilter === 'stalls') && lastStalls && lastStalls.stalls) {
    for (const s of lastStalls.stalls) {
      const unix = lastNow ? lastNow.unixMs - Math.max(0, (lastNow.tickIndex - s.startTick)) * 1000 / 60 : Date.now();
      items.push({ kind: 'stall', data: s, unix });
    }
  }
  items.sort((a, b) => b.unix - a.unix);

  if (items.length === 0) {
    root.innerHTML = '<div class=""empty-line"">no lag events yet — clean session</div>';
    sub.textContent = '0 events';
    return;
  }

  const spikeCount = items.filter(i => i.kind === 'spike').length;
  const stallCount = items.filter(i => i.kind === 'stall').length;
  sub.textContent = items.length + ' events · ' + spikeCount + ' spikes · ' + stallCount + ' stalls';

  root.innerHTML = items.map(it => {
    if (it.kind === 'spike') return renderSpikeCard(it.data, it.unix);
    return renderStallCard(it.data, it.unix);
  }).join('');

  // Wire expansion clicks.
  root.querySelectorAll('.lag-card').forEach(el => {
    el.addEventListener('click', () => {
      const k = el.dataset.key;
      const set = el.dataset.kind === 'spike' ? expandedSpikes : expandedStalls;
      if (set.has(k)) set.delete(k); else set.add(k);
      renderLag();
    });
  });
}

function renderSpikeCard(s, unix) {
  const k = 's_' + s.worstTick;
  const isOpen = expandedSpikes.has(k);
  const contribs = (s.contributors || []).map(c =>
    `<div class='lag-c'><span class='nm'>${escapeHtml(c.name)}</span><span class='vl'>${fmtMs(c.ms)} ms</span></div>`
  ).join('');
  return `<div class='lag-card spike ${s.warming ? 'warming' : ''}' data-key='${k}' data-kind='spike'>
    <div class='lag-head'>
      <span class='lag-glyph'>⚡</span>
      <div class='lag-main'>
        <div class='lag-title'><span class='lag-kind'>SPIKE</span>${fmtMs(s.worstFrameMs)} ms at tick #${fmtInt(s.worstTick)}${s.warming ? ' · warmup' : ''}</div>
        <div class='lag-sub'>baseline ${fmtMs(s.baselineMs)}ms · ${fmtAgo(unix)}</div>
      </div>
      <span class='lag-chevron'>${isOpen ? '▾' : '▸'}</span>
    </div>
    <div class='lag-detail ${isOpen ? '' : 'hidden'}'>
      <div class='lag-detail-h'>top contributors at worst tick</div>
      <div class='lag-contribs'>${contribs || '<span class=muted>(no per-mod snapshot)</span>'}</div>
    </div>
  </div>`;
}

function renderStallCard(s, unix) {
  const k = 't_' + s.startTick;
  const isOpen = expandedStalls.has(k);
  return `<div class='lag-card stall ${s.warming ? 'warming' : ''}' data-key='${k}' data-kind='stall'>
    <div class='lag-head'>
      <span class='lag-glyph'>⏸</span>
      <div class='lag-main'>
        <div class='lag-title'><span class='lag-kind danger'>STALL</span>${fmtMs(s.durationMs)} ms · ${s.cause}</div>
        <div class='lag-sub'>tick #${fmtInt(s.startTick)} · ${s.severity} · ${fmtAgo(unix)}</div>
      </div>
      <span class='lag-chevron'>${isOpen ? '▾' : '▸'}</span>
    </div>
    <div class='lag-detail ${isOpen ? '' : 'hidden'}'>
      <div class='lag-contribs'>
        <div class='lag-c'><span class='nm'>excess over baseline</span><span class='vl'>${fmtMs(s.excessMs)} ms</span></div>
        <div class='lag-c'><span class='nm'>gc pause</span><span class='vl'>${fmtMs(s.gcPauseMs)} ms</span></div>
        <div class='lag-c'><span class='nm'>gen 0 / 1 / 2 collections</span><span class='vl'>${s.gen0} · ${s.gen1} · ${s.gen2}</span></div>
        <div class='lag-c'><span class='nm'>severity</span><span class='vl'>${s.severity}</span></div>
      </div>
    </div>
  </div>`;
}


// ====== INSIGHTS ======================================================
// Pattern names like 'HotHookDominance' don't mean anything to a player.
// This map turns each detector's PatternKey into an 'observation'-style
// label that says what it MEANS, not what it's called internally.
// The full proper rework is a separate session — this is the holdover.
const INSIGHT_LABEL_MAP = {
  'HotHookDominance': 'cost concentration',
  'AllocationBurst': 'allocation pressure',
  'FreeRemovalCandidate': 'possibly removable',
  'PeakContributorToSpike': 'top spike contributor',
  'ContextCorrelatedSpike': 'context-linked spike',
  'ContextConditionalCost': 'context-conditional cost',
  'SustainedCostShift': 'cost shift',
  'NewContributor': 'new contributor',
  'GcPauseCulprit': 'gc pause source',
  'HookFrequencyTail': 'long-tail hook frequency',
  'LoadoutCorrelatedCost': 'loadout-linked cost',
  'EventConditionalCost': 'event-conditional cost',
  'LoadoutCombinationCost': 'loadout combination cost',
  'SegmentOutlier': 'segment outlier',
  'SegmentTopMod': 'consistent top mod',
  'SegmentDeathCorrelation': 'deaths correlate with cost',
};

function renderInsights() {
  const root = document.getElementById('insightslist');
  const sub = document.getElementById('insights-sub');
  if (!lastInsights || !lastInsights.records || lastInsights.records.length === 0) {
    root.innerHTML = `<div class='insight-empty'>
      <h3>no observations yet</h3>
      <p>insights surface patterns we've measured across your session and prior sessions —
         things like 'this mod is consistently the top contributor in boss fights' or
         'this segment ran above your lifetime average'.</p>
      <p class='muted'>they need a few sessions of data on the same modlist before most
         of them can fire. play a bit longer.</p>
    </div>`;
    sub.textContent = '0';
    return;
  }
  const recs = lastInsights.records;
  sub.textContent = recs.length + ' observation' + (recs.length === 1 ? '' : 's');
  root.innerHTML = recs.map(r => {
    const flavour = r.confidence === 'High' ? '' : r.confidence === 'Low' ? 'warn' : '';
    const label = INSIGHT_LABEL_MAP[r.pattern] || r.pattern.replace(/([A-Z])/g, ' $1').trim().toLowerCase();
    return `<div class='insight ${flavour}'>
      <div class='head'>
        <span class='pattern'>${escapeHtml(label)}</span>
        <span class='conf'>${r.confidence} confidence · ${r.scope}</span>
      </div>
      <div class='body'>${escapeHtml(r.mediumText || r.shortText)}</div>
      <div class='footer'>seen ${r.confirmationCount}× · ticks ${fmtInt(r.firstSeenTick)}–${fmtInt(r.lastSeenTick)}</div>
    </div>`;
  }).join('');
}

// ====== SELF TAB ======================================================
function renderSelf() {
  if (!lastSelf) return;
  const install = document.getElementById('self-install');
  const proc = document.getElementById('self-process');
  const back = document.getElementById('self-backend');
  const sevClass = lastSelf.severity === 'Severe' ? 'bad' : lastSelf.severity === 'Concerning' ? 'warn' : 'good';

  // Hero gauge
  renderSelfGauge(lastSelf);
  document.getElementById('hero-sev').textContent = lastSelf.severity.toLowerCase();
  document.getElementById('hero-sev').className = 'v ' + sevClass;
  document.getElementById('hero-bph').textContent = lastSelf.bytesPerHookKb.toFixed(1) + ' KB';
  const ratio = lastSelf.bytesPerHook / (36 * 1024);
  document.getElementById('hero-ratio').textContent = ratio.toFixed(2) + '× baseline';
  document.getElementById('hero-ratio').className = 'v ' + sevClass;
  document.getElementById('hero-hooks').textContent = fmtInt(lastSelf.installedHookCount);

  install.innerHTML = `
    <div class='self-row'><span class='k'>install delta</span><span class='v'>${lastSelf.installDeltaMb.toFixed(0)} MB</span></div>
    <div class='self-row'><span class='k'>bytes per hook</span><span class='v ${sevClass}'>${lastSelf.bytesPerHookKb.toFixed(1)} KB</span></div>
    <div class='self-row'><span class='k'>hook count</span><span class='v'>${fmtInt(lastSelf.installedHookCount)}</span></div>
    <div class='self-row'><span class='k'>vs 36KB baseline</span><span class='v ${sevClass}'>${ratio.toFixed(2)}× </span></div>
  `;
  // Footprint bar: visualize ratio against bands. Healthy 0-1.5x, Concerning 1.5-2.5x, Severe ≥2.5x.
  const r = Math.min(3.5, ratio);
  const fp = document.getElementById('footprint-bar');
  fp.innerHTML = `
    <span style='width: ${(Math.min(r, 1.5) / 3.5 * 100).toFixed(1)}%; background: var(--good);'></span>
    <span style='width: ${(Math.max(0, Math.min(r, 2.5) - 1.5) / 3.5 * 100).toFixed(1)}%; background: var(--amber);'></span>
    <span style='width: ${(Math.max(0, r - 2.5) / 3.5 * 100).toFixed(1)}%; background: var(--danger);'></span>
  `;

  proc.innerHTML = `
    <div class='self-row'><span class='k'>working set</span><span class='v'>${lastSelf.processWorkingSetMb.toFixed(0)} MB</span></div>
    <div class='self-row'><span class='k'>managed heap</span><span class='v'>${lastSelf.processManagedHeapMb.toFixed(0)} MB</span></div>
    <div class='self-row'><span class='k'>managed share</span><span class='v'>${(lastSelf.managedFractionOfWorkingSet * 100).toFixed(0)}%</span></div>
  `;
  // Split bar: managed / native portion of working set.
  const ws = lastSelf.processWorkingSetMb || 1;
  const managed = lastSelf.processManagedHeapMb || 0;
  const native = Math.max(0, ws - managed);
  const split = document.getElementById('self-split');
  split.innerHTML = `
    <span style='width: ${(managed / ws * 100).toFixed(1)}%; background: var(--accent);'></span>
    <span style='width: ${(native / ws * 100).toFixed(1)}%; background: var(--surface-2);'></span>
  `;

  back.innerHTML = `
    <div class='self-row'><span class='k'>backend</span><span class='v'>${lastSelf.backend}</span></div>
    <div class='self-row'><span class='k'>installed</span><span class='v ${lastSelf.installed ? 'good' : 'warn'}'>${lastSelf.installed ? 'yes' : 'pending'}</span></div>
  `;

  // Hook distribution per mod (uses /api/hooks if available, otherwise /api/mods category sums).
  renderHookDistribution();
}

function renderSelfGauge(self) {
  const g = document.querySelector('#self-gauge svg');
  if (!g) return;
  // Half-circle gauge from -PI to 0, mapped from 0..3.5 ratio.
  const ratio = self.bytesPerHook / (36 * 1024);
  const r = Math.min(3.5, ratio);
  const angle = -Math.PI + (r / 3.5) * Math.PI;
  const x = 50 + Math.cos(angle) * 40;
  const y = 50 + Math.sin(angle) * 40;
  const sevColor = self.severity === 'Severe' ? '#b94e58' : self.severity === 'Concerning' ? '#c97f3c' : '#4f9d6a';
  // Three colored arcs: green 0-1.5, amber 1.5-2.5, red 2.5-3.5
  const arc = (from, to, color) => {
    const a1 = -Math.PI + (from / 3.5) * Math.PI;
    const a2 = -Math.PI + (to / 3.5) * Math.PI;
    const x1 = 50 + Math.cos(a1) * 40, y1 = 50 + Math.sin(a1) * 40;
    const x2 = 50 + Math.cos(a2) * 40, y2 = 50 + Math.sin(a2) * 40;
    return `<path d='M ${x1} ${y1} A 40 40 0 0 1 ${x2} ${y2}' stroke='${color}' stroke-width='6' fill='none' stroke-linecap='round'/>`;
  };
  g.innerHTML = `
    ${arc(0, 1.5, '#4f9d6a')}
    ${arc(1.5, 2.5, '#c97f3c')}
    ${arc(2.5, 3.5, '#b94e58')}
    <circle cx='${x}' cy='${y}' r='4' fill='${sevColor}' stroke='#d6dae0' stroke-width='1'/>
    <text x='50' y='40' text-anchor='middle' fill='#d6dae0' font-family='Inter, sans-serif' font-size='8' font-weight='600'>${self.severity}</text>
    <text x='50' y='52' text-anchor='middle' fill='#6a727f' font-family='JetBrains Mono, monospace' font-size='5'>${ratio.toFixed(2)}× baseline</text>
  `;
}

function renderHookDistribution() {
  const root = document.getElementById('self-hookdist');
  const sub = document.getElementById('hookdist-sub');
  if (!lastHooks || !lastHooks.hooks || lastHooks.hooks.length === 0) {
    // Fall back: derive a rough hook count per mod from /api/mods if available.
    root.innerHTML = '<div class=""empty-line"">expand a mod on Summary to load /api/hooks — full hook list will appear here</div>';
    sub.textContent = 'not yet loaded';
    return;
  }
  const perMod = new Map();
  for (const h of lastHooks.hooks) {
    if (!perMod.has(h.modId)) perMod.set(h.modId, { id: h.modId, name: h.modName, count: 0, ms: 0 });
    const e = perMod.get(h.modId);
    e.count++;
    e.ms += h.cpuMs;
  }
  const sorted = [...perMod.values()].sort((a, b) => b.count - a.count).slice(0, 12);
  const max = sorted.length > 0 ? sorted[0].count : 1;
  sub.textContent = perMod.size + ' mods · ' + lastHooks.hooks.length + ' active hooks shown';
  root.innerHTML = sorted.map((m, i) => `
    <div class='hd-row'>
      <span class='rk'>${i + 1}</span>
      <span class='nm'>${escapeHtml(m.name)}</span>
      <span class='bar'><span style='width: ${(m.count / max * 100).toFixed(1)}%'></span></span>
      <span class='n'>${fmtInt(m.count)} hooks</span>
      <span class='mb'>${fmtMs(m.ms)} ms</span>
    </div>
  `).join('');
}
";
}
