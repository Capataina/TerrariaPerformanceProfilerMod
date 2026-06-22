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
  const fpsClass = k.avgFps >= 55 ? 'good' : k.avgFps >= 30 ? 'warn' : 'bad';
  const fpsTag = k.avgFps < 30 ? 'rough' : k.avgFps < 55 ? 'okay' : 'smooth';
  setKpi('fps', {
    value: dash(k.avgFps, v => v.toFixed(0)),
    valueClass: fpsClass,
    tag: fpsTag, tagClass: fpsClass,
    subs: [
      { k: 'median', v: msFmt(k.medianFrameMs) },
      { k: 'best',   v: msFmt(k.bestFrameMs) },
      { k: 'samples', v: dash(k.sampleN, fmtInt) },
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

  // ---------- stalls (session-cumulative) ----------
  const stClass = k.stallCount > 0 ? (k.stallCount > 5 ? 'bad' : 'orange') : 'good';
  const stTag = k.stallCount === 0 ? 'clean' : k.stallCount >= 5 ? 'rough' : 'sporadic';
  setKpi('stalls', {
    value: String(k.stallCount),
    valueClass: stClass,
    tag: stTag, tagClass: stClass,
    subs: [
      { k: 'biggest', v: msFmt(k.worstStallMs) },
      { k: 'average', v: msFmt(k.avgStallMs) },
      { k: dbMode ? 'window' : 'in 30s',  v: dbMode ? 'last session' : 'see chart' },
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
";
}
