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
";
}
