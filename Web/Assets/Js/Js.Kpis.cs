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
    private const string JsKpis = @"
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
  // db mode serves the LAST persisted session, not a live 30s window, so the
  // window-implying sublabels ('avg 30s', 'in 30s', 'in 30s' suffix) are
  // relabelled to a session framing. The numbers are unchanged; only the
  // wording adapts. Live mode keeps its existing window wording verbatim.
  const dbMode = lastNow.source === 'db';
  const msFmt = v => dash(v, fmtMs) + (v == null || !isFinite(v) ? '' : 'ms');

  // The lag-spikes hero suffix is window-implying ('in 30s'); switch to
  // 'session' in db mode so stored counts aren't read as a live window.
  const spSuffix = document.getElementById('kpi-spikes-suffix');
  if (spSuffix) spSuffix.textContent = dbMode ? 'session' : 'in 30s';

  // ---------- avg fps ----------
  // avg fps is UPDATE cadence (is game time running at full speed?);
  // render fps is DRAW cadence (how many frames the eyes get). With
  // frameskip on and the game running behind they diverge: updates hold
  // 60 while draws are dropped. Tag priority: slow-mo (measured sustained
  // sub-real-time speed — the real problem) > skipping (frameskip dropping
  // draws) > the plain fps bands.
  const fpsClass = k.avgFps >= 55 ? 'good' : k.avgFps >= 30 ? 'warn' : 'bad';
  const slowmo = k.realtimeSpeed > 0 && k.realtimeSpeed < 0.9;
  const skipping = !slowmo && k.renderFps > 0 && k.renderFps < k.avgFps - 10;
  const fpsTag = slowmo ? 'slow-mo' : skipping ? 'skipping'
    : k.avgFps < 30 ? 'rough' : k.avgFps < 55 ? 'okay' : 'smooth';
  setKpi('fps', {
    value: dash(k.avgFps, v => v.toFixed(0)),
    valueClass: fpsClass,
    tag: fpsTag, tagClass: slowmo ? 'bad' : skipping ? 'warn' : fpsClass,
    subs: [
      { k: 'median', v: msFmt(k.medianFrameMs) },
      { k: 'best',   v: msFmt(k.bestFrameMs) },
      { k: 'samples', v: dash(k.sampleN, fmtInt) },
      { k: 'render', v: k.renderFps > 0 ? k.renderFps.toFixed(0) + 'fps' : '—' },
    ],
    sparkVals: ms.length > 1 ? ms.map(v => v > 0 ? 1000 / Math.max(1, v) : 0) : null,
    sparkClass: fpsClass,
  });

  // ---------- worst frame ----------
  const worstClass = k.worstFrameMs > 100 ? 'bad' : k.worstFrameMs > 50 ? 'orange' : k.worstFrameMs > 33 ? 'warn' : 'good';
  const worstTag = k.worstFrameMs > 100 ? 'stutter' : k.worstFrameMs > 50 ? 'hitch' : k.worstFrameMs > 33 ? 'felt' : 'smooth';
  setKpi('worst', {
    value: dash(k.worstFrameMs, fmtMs),
    valueClass: worstClass,
    tag: worstTag, tagClass: worstClass,
    subs: [
      { k: dbMode ? 'session avg' : 'avg 30s', v: msFmt(lastNow.avg30sMs) },
      { k: 'median', v: msFmt(k.medianFrameMs) },
      { k: 'best',   v: msFmt(k.bestFrameMs) },
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
      { k: 'session', v: dash(k.spikeCount, fmtInt) },
      { k: 'lag total', v: msFmt(k.totalLagMs) },
      { k: 'threshold', v: '>50ms' },
    ],
    sparkVals: null,
    sparkClass: spClass,
  });

  // ---------- stalls (session-cumulative, real in-app causes only) ----------
  // The headline counts freezes / GC / UI-blocking; alt-tab suspends and
  // world-loads are wall time in which the game was not running, reported
  // separately as 'paused' so a 2-minute alt-tab never reads as the
  // session's biggest stall (X3, 2026-07-07 honesty pass).
  const stClass = k.stallCount > 0 ? (k.stallCount > 5 ? 'bad' : 'orange') : 'good';
  const stTag = k.stallCount === 0 ? 'clean' : k.stallCount >= 5 ? 'rough' : 'sporadic';
  const pausedSub = k.pausedMs > 0
    ? { k: 'paused (excl.)', v: fmtDuration(k.pausedMs) }
    : { k: dbMode ? 'window' : 'in 30s', v: dbMode ? 'last session' : 'see chart' };
  setKpi('stalls', {
    value: String(k.stallCount),
    valueClass: stClass,
    tag: stTag, tagClass: stClass,
    subs: [
      { k: 'biggest', v: msFmt(k.worstStallMs) },
      { k: 'average', v: msFmt(k.avgStallMs) },
      pausedSub,
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
      const c = opts.sparkClass === 'bad' ? 'var(--danger)' : opts.sparkClass === 'orange' ? 'var(--orange)' : opts.sparkClass === 'warn' ? 'var(--amber)' : 'var(--good)';
      spark.innerHTML = sparkline(opts.sparkVals, { color: c, strokeW: 1 });
    }
  }
}
";
}
