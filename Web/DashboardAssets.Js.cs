#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    /// <summary>
    /// Dashboard JS. Single-file SPA controller: tab routing, polling
    /// loop, SVG chart drawing, per-tab renderers. No bundler, no
    /// framework — vanilla ES2020+ runs natively in every modern browser.
    /// Hand-rolled for full control over the polling rhythm and zero
    /// build-pipeline complexity.
    /// </summary>
    public const string Js = @"
'use strict';

// ====== Config =======================================================
const POLL_NOW_MS      = 500;   // /api/now + /api/frames + /api/segments
const POLL_DETAIL_MS   = 1500;  // /api/mods + /api/spikes + /api/insights
const POLL_SELF_MS     = 5000;  // /api/self (low-frequency)

// ====== State ========================================================
let activeTab = 'now';
let lastNow = null;
let lastFrames = null;
let lastMods = null;
let lastSegments = null;
let lastSpikes = null;
let lastInsights = null;
let lastSelf = null;
let modSort = 'composite';
let connOk = false;

// ====== Tab routing ==================================================
document.querySelectorAll('.tab').forEach(t => {
  t.addEventListener('click', () => {
    const next = t.dataset.tab;
    if (next === activeTab) return;
    activeTab = next;
    document.querySelectorAll('.tab').forEach(x => x.classList.toggle('active', x.dataset.tab === next));
    document.querySelectorAll('.tab-pane').forEach(p => p.classList.toggle('active', p.dataset.pane === next));
    renderAll();
  });
});

// ====== Polling loops ================================================
async function fetchJson(path) {
  try {
    const r = await fetch(path, { cache: 'no-store' });
    if (!r.ok) throw new Error('HTTP ' + r.status);
    return await r.json();
  } catch (e) {
    return null;
  }
}

async function pollNow() {
  const [now, frames, segs] = await Promise.all([
    fetchJson('/api/now'),
    fetchJson('/api/frames'),
    fetchJson('/api/segments'),
  ]);
  if (now)    lastNow = now;
  if (frames) lastFrames = frames;
  if (segs)   lastSegments = segs;
  setConnection(!!now);
  renderTopbar();
  renderFooter();
  renderEmptyState();
  if (activeTab === 'now' || activeTab === 'timeline') renderAll();
}

async function pollDetail() {
  const [mods, spikes, insights] = await Promise.all([
    fetchJson('/api/mods'),
    fetchJson('/api/spikes'),
    fetchJson('/api/insights'),
  ]);
  if (mods)     lastMods = mods;
  if (spikes)   lastSpikes = spikes;
  if (insights) lastInsights = insights;
  if (activeTab === 'mods' || activeTab === 'spikes' || activeTab === 'insights' || activeTab === 'now') renderAll();
}

async function pollSelf() {
  const self = await fetchJson('/api/self');
  if (self) lastSelf = self;
  if (activeTab === 'self') renderSelf();
}

function setConnection(ok) {
  connOk = ok;
  const dot = document.getElementById('live-dot');
  const txt = document.getElementById('live-text');
  if (ok) {
    dot.className = 'live-dot ok';
    txt.textContent = lastNow && lastNow.worldLoaded ? 'live · world loaded' : 'live · no world';
  } else {
    dot.className = 'live-dot err';
    txt.textContent = 'connection lost · retrying';
  }
}

setInterval(pollNow, POLL_NOW_MS);
setInterval(pollDetail, POLL_DETAIL_MS);
setInterval(pollSelf, POLL_SELF_MS);
pollNow();
pollDetail();
pollSelf();

// ====== Helpers ======================================================
function fmtMs(v) {
  if (v == null) return '—';
  if (v < 0.005) return '0.00';
  if (v < 10) return v.toFixed(2);
  if (v < 100) return v.toFixed(1);
  return v.toFixed(0);
}
function fmtInt(v) {
  if (v == null) return '—';
  return v.toLocaleString();
}
function fmtDuration(ms) {
  if (ms == null) return '—';
  if (ms < 1000) return ms + 'ms';
  const s = Math.floor(ms / 1000);
  if (s < 60) return s + 's';
  const m = Math.floor(s / 60);
  const ss = s % 60;
  if (m < 60) return m + 'm ' + String(ss).padStart(2, '0') + 's';
  const h = Math.floor(m / 60);
  return h + 'h ' + String(m % 60).padStart(2, '0') + 'm';
}
function fmtAgo(unixMs) {
  if (!unixMs || !lastNow) return '';
  const dt = lastNow.unixMs - unixMs;
  if (dt < 1000) return 'just now';
  if (dt < 60000) return Math.floor(dt/1000) + 's ago';
  if (dt < 3600000) return Math.floor(dt/60000) + 'm ago';
  return Math.floor(dt/3600000) + 'h ago';
}
function truncate(s, n) {
  if (!s) return '';
  return s.length > n ? s.substring(0, n - 1) + '…' : s;
}
function clamp(x, lo, hi) { return Math.max(lo, Math.min(hi, x)); }

// ====== Topbar / footer ==============================================
function renderTopbar() {
  if (!lastNow || !lastNow.worldLoaded) {
    document.getElementById('ts-tick').textContent = '—';
    document.getElementById('ts-frame').textContent = '—';
    document.getElementById('ts-avg').textContent = '—';
    document.getElementById('ts-gc').textContent = '—';
    document.getElementById('ts-backend').textContent = lastNow ? (lastNow.backend || '—') : '—';
    return;
  }
  document.getElementById('ts-tick').textContent = '#' + fmtInt(lastNow.tickIndex);
  document.getElementById('ts-frame').textContent = fmtMs(lastNow.frameMs) + 'ms';
  document.getElementById('ts-avg').textContent = fmtMs(lastNow.avg30sMs) + 'ms';
  document.getElementById('ts-gc').textContent = fmtMs(lastNow.gcMs) + 'ms';
  document.getElementById('ts-backend').textContent = lastNow.backend || '—';
}

function renderFooter() {
  const now = new Date();
  const t = now.toLocaleTimeString();
  document.getElementById('foot-clock').textContent = t;
  document.getElementById('foot-mode').textContent = lastNow && lastNow.worldLoaded
    ? `${lastNow.npcCount} npc · ${lastNow.projectileCount} proj · ${lastNow.dustCount} dust`
    : 'idle';
}

function renderEmptyState() {
  const loaded = lastNow && lastNow.worldLoaded;
  document.getElementById('empty').classList.toggle('hidden', loaded);
  document.getElementById('content').style.visibility = loaded ? 'visible' : 'hidden';
}

// ====== Master render dispatcher =====================================
function renderAll() {
  switch (activeTab) {
    case 'now':      renderNow();      break;
    case 'mods':     renderMods();     break;
    case 'timeline': renderTimeline(); break;
    case 'spikes':   renderSpikes();   break;
    case 'insights': renderInsights(); break;
    case 'self':     renderSelf();     break;
  }
}

// ====== NOW ===========================================================
function renderNow() {
  renderFrameChart();
  renderNowPlaying();
  renderNowMods();
  renderNowEvents();
}

function renderFrameChart() {
  const svg = document.getElementById('frame-chart');
  const sub = document.getElementById('chart-sub');
  if (!lastFrames || !lastFrames.worldLoaded || !lastFrames.frameMs || lastFrames.frameMs.length === 0) {
    svg.innerHTML = '';
    sub.textContent = '—';
    return;
  }
  const ms = lastFrames.frameMs;
  const n = ms.length;
  const max = Math.max(2, Math.max(...ms) * 1.1);
  const median = ms.slice().sort((a, b) => a - b)[Math.floor(n / 2)];
  const threshold = median * 2;
  sub.textContent = `${n} frames · median ${fmtMs(median)} ms · spike ≥ ${fmtMs(threshold)} ms`;

  const w = 100, h = 28;
  let pathD = '';
  let areaD = '';
  for (let i = 0; i < n; i++) {
    const x = (i / (n - 1)) * w;
    const y = h - (ms[i] / max) * h;
    pathD += (i === 0 ? 'M' : 'L') + x.toFixed(2) + ',' + y.toFixed(2) + ' ';
    areaD += (i === 0 ? 'M' : 'L') + x.toFixed(2) + ',' + y.toFixed(2) + ' ';
  }
  areaD += `L${w},${h} L0,${h} Z`;

  const thresholdY = h - (threshold / max) * h;
  const medianY = h - (median / max) * h;

  svg.innerHTML = `
    <defs>
      <linearGradient id='g-area' x1='0' y1='0' x2='0' y2='1'>
        <stop offset='0%' stop-color='#79c0ff' stop-opacity='0.5'/>
        <stop offset='100%' stop-color='#79c0ff' stop-opacity='0.02'/>
      </linearGradient>
    </defs>
    <line x1='0' y1='${thresholdY}' x2='${w}' y2='${thresholdY}' stroke='#f5b342' stroke-width='0.25' stroke-dasharray='0.8,0.8'/>
    <line x1='0' y1='${medianY}' x2='${w}' y2='${medianY}' stroke='#6e7480' stroke-width='0.2' stroke-dasharray='0.5,0.8'/>
    <path d='${areaD}' fill='url(#g-area)'/>
    <path d='${pathD}' fill='none' stroke='#79c0ff' stroke-width='0.5' stroke-linejoin='round' stroke-linecap='round'/>
  `;
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
    .slice()
    .sort((a, b) => familyWeight(a.family) - familyWeight(b.family))
    .map(s => {
      const top = s.topModName
        ? `<span class='mod'>${truncate(s.topModName, 14)}</span><br/>${fmtMs(s.topModMsPerTick)}ms/t`
        : '—';
      return `<div class='now-seg' data-family='${s.family}'>
        <span class='swatch'></span>
        <span class='name'>${escapeHtml(s.name)} <span class='muted'>· ${fmtDuration(s.elapsedMs)}</span></span>
        <span class='meta'>${top}</span>
      </div>`;
    }).join('');
}

function familyWeight(f) {
  const order = ['Boss', 'Invasion', 'UserBookmark', 'Weather', 'Subworld', 'Combat', 'Hardmode', 'DeathBracket', 'Biome'];
  const i = order.indexOf(f);
  return i < 0 ? 99 : i;
}

function renderNowMods() {
  const root = document.getElementById('nowmods');
  const sub = document.getElementById('mods-sub');
  if (!lastMods || !lastMods.worldLoaded || !lastMods.mods) {
    root.innerHTML = '<div class=""empty-line"">—</div>';
    sub.textContent = '—';
    return;
  }
  const sorted = lastMods.mods.slice().sort((a, b) => b.cpuMs - a.cpuMs);
  const top = sorted.filter(m => m.cpuMs > 0.001).slice(0, 12);
  const total = top.reduce((a, m) => a + m.cpuMs, 0);
  const max = top.length > 0 ? top[0].cpuMs : 1;
  sub.textContent = `${sorted.length} mods · ${fmtMs(total)} ms/t total`;
  root.innerHTML = top.map((m, i) => `
    <div class='modrow'>
      <span class='rank'>${i + 1}</span>
      <span class='name'>${escapeHtml(m.name)}</span>
      <span class='bars'><span class='b cpu' style='width: ${(m.cpuMs / max * 100).toFixed(1)}%'></span></span>
      <span class='ms'>${fmtMs(m.cpuMs)}<span class='u'>ms</span></span>
    </div>
  `).join('');
}

function renderNowEvents() {
  const root = document.getElementById('nowevents');
  const items = [];

  if (lastSegments && lastSegments.recent) {
    for (const s of lastSegments.recent) {
      let kind = 'segment';
      let what = `${s.name} ended · ${fmtDuration(s.durationMs)}`;
      if (s.deathCount > 0) { kind = 'death'; what = `died in ${s.name}`; }
      else if (s.bossKillCount > 0) { kind = 'boss-kill'; what = `${s.name} · victory · ${fmtDuration(s.durationMs)}`; }
      else if (s.spikeCount > 0) { kind = 'spike'; what = `${s.name} closed with ${s.spikeCount} spike(s)`; }
      items.push({ unix: s.endUnixMs, kind, what, glyph: glyphFor(kind) });
    }
  }
  if (lastSpikes && lastSpikes.spikes) {
    for (const s of lastSpikes.spikes) {
      const top = s.contributors && s.contributors.length > 0
        ? `${s.contributors[0].name} ${fmtMs(s.contributors[0].ms)} ms`
        : '(unattributed)';
      items.push({
        unix: lastNow ? lastNow.unixMs - (lastNow.tickIndex - s.worstTick) * 16 : Date.now(),
        kind: 'spike',
        what: `spike ${fmtMs(s.worstFrameMs)} ms · top ${top}`,
        glyph: '⚡',
      });
    }
  }
  items.sort((a, b) => b.unix - a.unix);
  const trimmed = items.slice(0, 12);

  root.innerHTML = trimmed.length === 0
    ? '<div class=""empty-line"">nothing yet — events appear as segments close, spikes fire, etc.</div>'
    : trimmed.map(e => `
        <div class='event' data-kind='${e.kind}'>
          <span class='glyph'>${e.glyph}</span>
          <span class='what'>${escapeHtml(e.what)}</span>
          <span class='when'>${fmtAgo(e.unix)}</span>
        </div>`).join('');
}

function glyphFor(kind) {
  switch (kind) {
    case 'boss-kill': return '✓';
    case 'death':     return '☠';
    case 'spike':     return '⚡';
    case 'stall':     return '⏸';
    case 'segment':   return '↺';
    default:          return '·';
  }
}

// ====== MODS TAB =====================================================
document.getElementById('mods-sort').addEventListener('click', e => {
  const b = e.target.closest('button');
  if (!b) return;
  modSort = b.dataset.sort;
  document.querySelectorAll('#mods-sort button').forEach(x => x.classList.toggle('active', x === b));
  if (activeTab === 'mods') renderMods();
});

function renderMods() {
  const root = document.getElementById('modtable');
  if (!lastMods || !lastMods.worldLoaded || !lastMods.mods) {
    root.innerHTML = '<div class=""empty-line"">no data yet</div>';
    return;
  }
  const mods = lastMods.mods.filter(m => m.cpuMs > 0 || m.avgCpuMs > 0);
  let getter;
  switch (modSort) {
    case 'cpu':    getter = m => m.cpuMs; break;
    case 'avg':    getter = m => m.avgCpuMs; break;
    default:       getter = m => m.cpuMs * 0.7 + m.avgCpuMs * 0.3; break;
  }
  mods.sort((a, b) => getter(b) - getter(a));
  const max = mods.length > 0 ? getter(mods[0]) : 1;
  const median = mods.length > 0 ? getter(mods[Math.floor(mods.length / 2)]) : 0;
  const outlierCut = median * 2.5;
  root.innerHTML = mods.map((m, i) => {
    const v = getter(m);
    const isOutlier = v > outlierCut && i < 3;
    const isTop = i < 3;
    return `<div class='modrow big ${isOutlier ? 'outlier' : ''} ${isTop ? 'top' : ''}'>
      <span class='rank'>${i + 1}</span>
      <span class='name'>${i < 3 ? '<strong>' + escapeHtml(m.name) + '</strong>' : escapeHtml(m.name)}</span>
      <span class='bars'><span class='b cpu' style='width: ${(v / max * 100).toFixed(1)}%'></span></span>
      <span class='ms'>${fmtMs(m.cpuMs)}<span class='u'>now</span></span>
      <span class='ms'>${fmtMs(m.avgCpuMs)}<span class='u'>avg</span></span>
    </div>`;
  }).join('');
}

// ====== TIMELINE TAB =================================================
function renderTimeline() {
  const root = document.getElementById('timelinelist');
  const sub = document.getElementById('timeline-sub');
  if (!lastSegments || !lastSegments.recent || lastSegments.recent.length === 0) {
    root.innerHTML = '<div class=""empty-line"">no segments closed yet</div>';
    sub.textContent = '—';
    return;
  }
  const segs = lastSegments.recent;
  sub.textContent = `${segs.length} recent · newest first`;
  root.innerHTML = segs.map(s => {
    const chips = [];
    if (s.deathCount > 0)    chips.push(`<span class='chip death'>☠ ${s.deathCount}</span>`);
    if (s.spikeCount > 0)    chips.push(`<span class='chip spike'>⚡ ${s.spikeCount}</span>`);
    if (s.stallCount > 0)    chips.push(`<span class='chip stall'>⏸ ${s.stallCount}</span>`);
    if (s.bossKillCount > 0) chips.push(`<span class='chip boss'>✓ ${s.bossKillCount}</span>`);
    const topMods = (s.topMods || []).map(m => `<span>${escapeHtml(m.name)} <span class='muted'>${fmtMs(m.ms)}ms</span></span>`).join('');
    return `<div class='tl-seg ${s.promoted ? 'promoted' : ''}' data-family='${s.family}'>
      <span class='name'>${escapeHtml(s.name)}</span>
      <span class='dur'>${fmtDuration(s.durationMs)}</span>
      <span class='mspt'>${fmtMs(s.avgFrameMs)} ms/t</span>
      <span class='badge'>${fmtInt(s.ticks)} ticks</span>
      <span class='chips'>${chips.join('')}</span>
      <span class='topmods'>${topMods}</span>
    </div>`;
  }).join('');
}

// ====== SPIKES TAB ===================================================
function renderSpikes() {
  const root = document.getElementById('spikeslist');
  const sub = document.getElementById('spikes-sub');
  if (!lastSpikes || !lastSpikes.worldLoaded || !lastSpikes.spikes || lastSpikes.spikes.length === 0) {
    root.innerHTML = '<div class=""empty-line"">no spikes yet — clean session</div>';
    sub.textContent = '0';
    return;
  }
  const spikes = lastSpikes.spikes.slice().reverse();
  sub.textContent = spikes.length + ' total';
  root.innerHTML = spikes.map(s => {
    const contribs = (s.contributors || []).map(c =>
      `<div class='c'><span class='name'>${escapeHtml(c.name)}</span><span class='ms'>${fmtMs(c.ms)} ms</span></div>`
    ).join('');
    return `<div class='spike-row ${s.warming ? 'warming' : ''}'>
      <div class='head'>
        <span><span class='worst'>${fmtMs(s.worstFrameMs)} ms</span> at tick #${fmtInt(s.worstTick)} ${s.warming ? '· warmup' : ''}</span>
        <span class='baseline'>baseline ${fmtMs(s.baselineMs)} ms · mad ${fmtMs(s.madMs)} ms</span>
      </div>
      <div class='contribs'>${contribs || '<span class=muted>(no per-mod snapshot)</span>'}</div>
    </div>`;
  }).join('');
}

// ====== INSIGHTS TAB =================================================
function renderInsights() {
  const root = document.getElementById('insightslist');
  const sub = document.getElementById('insights-sub');
  if (!lastInsights || !lastInsights.worldLoaded || !lastInsights.records || lastInsights.records.length === 0) {
    root.innerHTML = '<div class=""empty-line"">no insights yet — lifetime detectors need a few sessions of data to fire</div>';
    sub.textContent = '0';
    return;
  }
  const recs = lastInsights.records;
  sub.textContent = recs.length + ' live';
  root.innerHTML = recs.map(r => {
    const flavour = r.confidence === 'High' ? '' : r.confidence === 'Low' ? 'warn' : '';
    return `<div class='insight ${flavour}'>
      <div class='head'>
        <span class='pattern'>${escapeHtml(r.pattern.replace(/([A-Z])/g, ' $1').trim())}</span>
        <span class='conf'>${r.confidence} · ${r.scope}</span>
      </div>
      <div class='body'>${escapeHtml(r.mediumText || r.shortText)}</div>
      <div class='footer'>seen ${r.confirmationCount}× · ticks ${fmtInt(r.firstSeenTick)}–${fmtInt(r.lastSeenTick)}</div>
    </div>`;
  }).join('');
}

// ====== SELF TAB =====================================================
function renderSelf() {
  if (!lastSelf) return;
  const install = document.getElementById('self-install');
  const proc = document.getElementById('self-process');
  const back = document.getElementById('self-backend');
  const sevClass = lastSelf.severity === 'Severe' ? 'bad' : lastSelf.severity === 'Concerning' ? 'warn' : 'good';
  install.innerHTML = `
    <div class='self-row'><span class='k'>install delta</span><span class='v'>${lastSelf.installDeltaMb.toFixed(0)} MB</span></div>
    <div class='self-row'><span class='k'>bytes per hook</span><span class='v ${sevClass}'>${lastSelf.bytesPerHookKb.toFixed(1)} KB</span></div>
    <div class='self-row'><span class='k'>hook count</span><span class='v'>${fmtInt(lastSelf.installedHookCount)}</span></div>
    <div class='self-row'><span class='k'>severity</span><span class='v ${sevClass}'>${lastSelf.severity}</span></div>
  `;
  proc.innerHTML = `
    <div class='self-row'><span class='k'>working set</span><span class='v'>${lastSelf.processWorkingSetMb.toFixed(0)} MB</span></div>
    <div class='self-row'><span class='k'>managed heap</span><span class='v'>${lastSelf.processManagedHeapMb.toFixed(0)} MB</span></div>
    <div class='self-row'><span class='k'>managed share</span><span class='v'>${(lastSelf.managedFractionOfWorkingSet * 100).toFixed(0)}%</span></div>
  `;
  back.innerHTML = `
    <div class='self-row'><span class='k'>backend</span><span class='v'>${lastSelf.backend}</span></div>
    <div class='self-row'><span class='k'>installed</span><span class='v ${lastSelf.installed ? 'good' : 'warn'}'>${lastSelf.installed ? 'yes' : 'pending'}</span></div>
  `;
}

// ====== Util =========================================================
function escapeHtml(s) {
  if (s == null) return '';
  return String(s)
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/'/g, '&#39;').replace(/""/g, '&quot;');
}
";
}
