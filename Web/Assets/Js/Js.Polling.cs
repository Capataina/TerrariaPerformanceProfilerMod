#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
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
  // /api/insights + /api/cross-cutting are fetched by the Insights tab's own
  // poll (pollInsights); /api/mod-observatory etc. by the Observatory tab's
  // (pollObservatory). pollDetail feeds the summary / lag surfaces.
  const [mods, spikes, stalls, events] = await Promise.all([
    fetchJson('/api/mods'), fetchJson('/api/spikes'), fetchJson('/api/stalls'),
    fetchJson('/api/events'),
  ]);
  if (mods) { lastMods = mods; foldModSparkHistory(mods); }
  if (spikes) lastSpikes = spikes;
  if (stalls) lastStalls = stalls;
  if (events) lastEvents = events;
  if (activeTab === 'summary' || activeTab === 'lag') renderAll();
}

async function pollHooks() {
  // Fetch only when summary is active AND at least one mod row is expanded.
  if (activeTab !== 'summary' || expandedMods.size === 0) return;
  const hooks = await fetchJson('/api/hooks');
  if (hooks) lastHooks = hooks;
  if (activeTab === 'summary') renderSummaryMods();
}

async function pollSelf() {
  const [self, health] = await Promise.all([
    fetchJson('/api/self'), fetchJson('/api/data-health'),
  ]);
  if (self) lastSelf = self;
  if (health) lastDataHealth = health;
  if (activeTab === 'self') renderSelf();
}

async function pollMemory() {
  const mem = await fetchJson('/api/memory');
  if (mem) lastMemory = mem;
  if (activeTab === 'memory') renderMemory();
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
  } else if (lastNow && lastNow.source === 'db') {
    // No live world; serving the last persisted session. Checked before the
    // paused branch because the tick index is static in db mode and would
    // otherwise read as 'game paused'.
    dot.className = 'live-dot db';
    txt.textContent = 'reading from db · ' + (lastNow.sessionLabel || 'last session');
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

// ---- poll scheduling (S23: PollMs config) ----------------------------
// The base cadence comes from ProfilerConfig.PollMs via /api/now's pollMs
// field; the heavier endpoints scale proportionally off the base (same
// ratios the old fixed constants encoded). When the player changes the
// slider, the next pollNow notices and re-arms every timer — no reload.
let _pollBaseMs = POLL_NOW_MS;
let _pollTimers = [];
function armPolls(baseMs) {
  _pollTimers.forEach(clearInterval);
  const scale = baseMs / POLL_NOW_MS; // ratios preserved vs the tuned defaults
  _pollTimers = [
    setInterval(pollNow, baseMs),
    setInterval(pollDetail, Math.round(POLL_DETAIL_MS * scale)),
    setInterval(pollHooks, Math.round(POLL_HOOKS_MS * scale)),
    setInterval(pollSelf, Math.round(POLL_SELF_MS * scale)),
    setInterval(pollMemory, Math.round(POLL_SELF_MS * scale)),
    setInterval(pollHeatmap, Math.round(3000 * scale)),
  ];
  _pollBaseMs = baseMs;
  const foot = document.getElementById('foot-cadence');
  if (foot) foot.textContent = `polling /api · ${baseMs} ms · 1-7 to switch tabs`;
}
function maybeRearmPolls() {
  const want = lastNow && lastNow.pollMs > 0 ? lastNow.pollMs : _pollBaseMs;
  if (want !== _pollBaseMs) armPolls(want);
}
armPolls(POLL_NOW_MS);
setInterval(updateConnection, 1000);
setInterval(maybeRearmPolls, 2000);
pollNow(); pollDetail(); pollSelf(); pollHeatmap(); pollMemory();
";
}
