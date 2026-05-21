#nullable enable

namespace PerformanceProfiler.Web;

/// <summary>
/// The dashboard's HTML/CSS/JS, inlined as a C# const for the prototype.
///
/// <para>
/// Inlining keeps the prototype self-contained — no asset pipeline work
/// needed to ship a working proof-of-concept. Once the loop is verified
/// end-to-end and the design lands, this moves out to
/// <c>Web/Assets/index.html</c> and gets served via tML's asset stream
/// (or read off disk relative to the mod root during dev).
/// </para>
///
/// <para>
/// <b>Prototype scope.</b> The page polls <c>/api/now</c> every 250 ms
/// and renders the current tick + frame ms + a few other live fields.
/// Visually deliberate minimum so the verification focus stays on
/// "does the seamless loop actually work" rather than "does the design
/// look right."
/// </para>
/// </summary>
internal static class DashboardHtml
{
    public const string Page = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<title>Performance Profiler</title>
<style>
  :root {
    --bg: #0d1117;
    --surface: #161b22;
    --border: #1f2329;
    --text: #c5c8ce;
    --muted: #6e7480;
    --good: #95d4a3;
    --amber: #f5b342;
    --danger: #f47174;
    --accent: #79c0ff;
  }
  * { box-sizing: border-box; }
  html, body {
    margin: 0; padding: 0; background: var(--bg); color: var(--text);
    font-family: -apple-system, system-ui, 'Segoe UI', sans-serif;
    font-size: 16px;
  }
  .shell { max-width: 960px; margin: 0 auto; padding: 3rem 1.5rem; }
  h1 { color: #fff; font-size: 1.8rem; margin: 0 0 0.4rem; }
  .subtitle { color: var(--muted); margin-bottom: 2rem; }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
    gap: 0.75rem;
  }
  .card {
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: 4px;
    padding: 0.9rem 1rem;
    display: flex; flex-direction: column; gap: 0.2rem;
  }
  .label {
    font-size: 0.78rem;
    color: var(--muted);
    text-transform: uppercase;
    letter-spacing: 0.06em;
  }
  .value { font-size: 1.6rem; color: var(--good); line-height: 1; }
  .value.bright { color: #fff; }
  .value.amber { color: var(--amber); }
  .value.danger { color: var(--danger); }
  .value.muted { color: var(--muted); }
  .footer { font-size: 0.82rem; color: var(--muted); }

  .status {
    margin-top: 2rem;
    padding: 1rem 1.2rem;
    background: var(--surface);
    border: 1px solid var(--border);
    border-radius: 4px;
  }
  .status .row { display: flex; gap: 1rem; align-items: baseline; font-size: 0.9rem; }
  .status .row .k { color: var(--muted); min-width: 9rem; }
  .status .row .v { color: var(--text); font-family: 'SFMono-Regular', 'Menlo', monospace; }
  .dot { display: inline-block; width: 0.6em; height: 0.6em; border-radius: 50%; vertical-align: middle; margin-right: 0.4em; }
  .dot.good { background: var(--good); box-shadow: 0 0 6px rgba(149,212,163,0.5); }
  .dot.bad { background: var(--danger); }

  #notLoaded {
    background: var(--surface);
    border: 1px dashed var(--border);
    border-radius: 4px;
    padding: 2rem;
    text-align: center;
    color: var(--muted);
    margin-top: 1rem;
  }

  .hidden { display: none !important; }
</style>
</head>
<body>
<div class=""shell"">
  <h1>Performance Profiler</h1>
  <div class=""subtitle""><span class=""dot good"" id=""connDot""></span><span id=""connText"">connecting…</span></div>

  <div class=""grid"" id=""liveCards"">
    <div class=""card""><div class=""label"">frame</div><div class=""value"" id=""frameMs"">—</div><div class=""footer"">this tick</div></div>
    <div class=""card""><div class=""label"">tick</div><div class=""value bright"" id=""tick"">—</div><div class=""footer"" id=""ents"">— npc · — proj · — dust</div></div>
    <div class=""card""><div class=""label"">open segments</div><div class=""value"" id=""segs"">—</div><div class=""footer"">biome / weather / boss / etc</div></div>
    <div class=""card""><div class=""label"">profiler health</div><div class=""value"" id=""sev"">—</div><div class=""footer"" id=""kbPerHook"">— KB/hook</div></div>
  </div>

  <div id=""notLoaded"" class=""hidden"">no world loaded — open a save and walk around. dashboard will populate automatically.</div>

  <div class=""status"">
    <div class=""row""><span class=""k"">poll cadence</span><span class=""v"">250 ms</span></div>
    <div class=""row""><span class=""k"">endpoint</span><span class=""v"">/api/now</span></div>
    <div class=""row""><span class=""k"">last successful poll</span><span class=""v"" id=""lastPoll"">—</span></div>
    <div class=""row""><span class=""k"">install delta</span><span class=""v"" id=""installMb"">—</span></div>
    <div class=""row""><span class=""k"">hook count</span><span class=""v"" id=""hookCount"">—</span></div>
  </div>
</div>

<script>
let lastSuccessAt = 0;
async function poll() {
  try {
    const r = await fetch('/api/now', { cache: 'no-store' });
    if (!r.ok) throw new Error('HTTP ' + r.status);
    const j = await r.json();
    apply(j);
    lastSuccessAt = Date.now();
    setConn(true);
  } catch (e) {
    setConn(false);
  }
}

function apply(j) {
  const live = document.getElementById('liveCards');
  const empty = document.getElementById('notLoaded');
  if (!j.worldLoaded) {
    live.classList.add('hidden');
    empty.classList.remove('hidden');
    return;
  }
  live.classList.remove('hidden');
  empty.classList.add('hidden');

  const frame = document.getElementById('frameMs');
  frame.textContent = j.frameMs.toFixed(2) + ' ms';
  frame.className = 'value ' + (j.frameMs > 4 ? 'amber' : 'value');

  document.getElementById('tick').textContent = '#' + j.tickIndex.toLocaleString();
  document.getElementById('ents').textContent =
    j.npcCount + ' npc · ' + j.projectileCount + ' proj · ' + j.dustCount + ' dust';

  document.getElementById('segs').textContent = j.openSegmentCount;

  const sev = document.getElementById('sev');
  sev.textContent = j.severity.toLowerCase();
  sev.className = 'value ' + (
    j.severity === 'Severe' ? 'danger' :
    j.severity === 'Concerning' ? 'amber' : ''
  );
  document.getElementById('kbPerHook').textContent = j.bytesPerHookKb.toFixed(1) + ' KB/hook';

  document.getElementById('lastPoll').textContent =
    new Date().toLocaleTimeString();
  document.getElementById('installMb').textContent = j.installDeltaMb.toFixed(0) + ' MB';
  document.getElementById('hookCount').textContent = j.hookCount.toLocaleString();
}

function setConn(ok) {
  document.getElementById('connDot').className = 'dot ' + (ok ? 'good' : 'bad');
  document.getElementById('connText').textContent = ok
    ? 'connected · live'
    : 'lost connection · retrying';
}

setInterval(poll, 250);
poll();
</script>
</body>
</html>
";
}
