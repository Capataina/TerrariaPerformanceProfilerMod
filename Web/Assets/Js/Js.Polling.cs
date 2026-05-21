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
    private const string JsPolling = @"
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
";
}
