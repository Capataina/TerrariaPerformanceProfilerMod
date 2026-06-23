#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Chart render functions: one implementation per chart shape, built on the
    // niceScale / seriesPaths scale core. Every pane composes these instead of
    // hand-rolling SVG. Loaded after Js.Components (cellBar / heatFill / escape).
    private const string JsCharts = @"
// ---- shared geometry --------------------------------------------------
function _polar(cx, cy, r, deg) { const a = (deg - 90) * Math.PI / 180; return [cx + r * Math.cos(a), cy + r * Math.sin(a)]; }
// Filled ring/pie sector from a0..a1 degrees (clockwise from 12 o'clock).
function _ring(cx, cy, rO, rI, a0, a1) {
  if (a1 - a0 >= 359.999) a1 = a0 + 359.999;
  const large = (a1 - a0) > 180 ? 1 : 0;
  const o0 = _polar(cx, cy, rO, a0), o1 = _polar(cx, cy, rO, a1);
  if (rI <= 0) {
    return `M ${cx.toFixed(2)} ${cy.toFixed(2)} L ${o0[0].toFixed(2)} ${o0[1].toFixed(2)} A ${rO} ${rO} 0 ${large} 1 ${o1[0].toFixed(2)} ${o1[1].toFixed(2)} Z`;
  }
  const i1 = _polar(cx, cy, rI, a1), i0 = _polar(cx, cy, rI, a0);
  return `M ${o0[0].toFixed(2)} ${o0[1].toFixed(2)} A ${rO} ${rO} 0 ${large} 1 ${o1[0].toFixed(2)} ${o1[1].toFixed(2)} L ${i1[0].toFixed(2)} ${i1[1].toFixed(2)} A ${rI} ${rI} 0 ${large} 0 ${i0[0].toFixed(2)} ${i0[1].toFixed(2)} Z`;
}
// Open (stroked) arc, for gauges.
function _arcStroke(cx, cy, r, a0, a1) {
  if (a1 - a0 >= 360) a1 = a0 + 359.999;
  const large = (a1 - a0) > 180 ? 1 : 0;
  const p0 = _polar(cx, cy, r, a0), p1 = _polar(cx, cy, r, a1);
  return `M ${p0[0].toFixed(2)} ${p0[1].toFixed(2)} A ${r} ${r} 0 ${large} 1 ${p1[0].toFixed(2)} ${p1[1].toFixed(2)}`;
}
function _bandColor(bands, ratio) { for (const b of bands) { if (ratio <= b.to) return b.color; } return bands.length ? bands[bands.length - 1].color : 'var(--accent)'; }

// Perf colour (data ramp) from a ratio against reference, mirrors tintClass.
function perfColor(ratio) { const t = tintClass(ratio); return t ? `var(--perf-${t.slice(1)})` : 'var(--perf-0)'; }

// ---- Sparkline: mini line (+ optional vertical markers) --------------
// opts: { w, h, color, strokeW, markers:[idx], markColor }
function sparkline(values, o) {
  o = o || {};
  values = (values || []).filter(v => isFinite(v));
  const w = o.w || 100, h = o.h || 16;
  let path = '';
  if (values.length >= 2) {
    const sc = niceScale(values, 0.1), span = (sc.max - sc.min) || 1;
    let d = '';
    for (let i = 0; i < values.length; i++) {
      const x = (i / (values.length - 1)) * w;
      const y = h - ((values[i] - sc.min) / span) * (h - 2) - 1;
      d += (i ? 'L' : 'M') + x.toFixed(1) + ',' + y.toFixed(1) + ' ';
    }
    path = `<path d='${d}' fill='none' stroke='${o.color || 'var(--accent)'}' stroke-width='${o.strokeW || 1}'></path>`;
  }
  let marks = '';
  if (o.markers && o.markers.length) {
    const n = o.markN || (values.length || 2);
    for (const mi of o.markers) {
      const x = (mi / Math.max(1, n - 1)) * w;
      marks += `<line x1='${x.toFixed(1)}' y1='0' x2='${x.toFixed(1)}' y2='${h}' stroke='${o.markColor || 'var(--spike)'}' stroke-width='0.7' opacity='0.7'></line>`;
    }
  }
  if (!path && !marks) return '';
  return `<svg viewBox='0 0 ${w} ${h}' class='spark-svg' preserveAspectRatio='none'>${path}${marks}</svg>`;
}

// ---- Line / area chart ----------------------------------------------
// opts: { series:[{values, color, area?}], w, h, rules:[{value,color,label}],
//         markers:[{at:0..1 | index, color}], axis?, fmt }
function lineChart(o) {
  o = o || {};
  const w = o.w || 540, h = o.h || 120;
  const padX = o.padX != null ? o.padX : 8, padTop = o.padTop != null ? o.padTop : 14, padBot = o.padBot != null ? o.padBot : 14;
  const iw = w - padX * 2, ih = h - padTop - padBot;
  const series = (o.series || []).filter(s => s && s.values && s.values.length);
  if (!series.length) return '';
  let all = [];
  for (const s of series) all = all.concat(s.values.filter(isFinite));
  if (o.rules) for (const r of o.rules) if (r && isFinite(r.value)) all.push(r.value);
  if (!all.length) return '';
  const sc = niceScale(all, o.pad), span = (sc.max - sc.min) || 1;
  const Y = v => padTop + ih - ((v - sc.min) / span) * ih;
  let body = '';
  for (const s of series) {
    const n = s.values.length, X = i => padX + (n > 1 ? (i / (n - 1)) * iw : iw / 2);
    let line = '';
    for (let i = 0; i < n; i++) line += (i ? ' L ' : 'M ') + X(i).toFixed(1) + ' ' + Y(s.values[i]).toFixed(1);
    if (s.area) {
      const area = 'M ' + X(0).toFixed(1) + ' ' + (padTop + ih).toFixed(1) + ' L ' + line.slice(2) + ' L ' + X(n - 1).toFixed(1) + ' ' + (padTop + ih).toFixed(1) + ' Z';
      body += `<path class='chart-area' d='${area}' fill='${s.color}'></path>`;
    }
    body += `<path class='chart-line' d='${line}' stroke='${s.color}'></path>`;
  }
  if (o.rules) for (const r of o.rules) {
    if (!isFinite(r.value)) continue;
    const y = Y(r.value).toFixed(1);
    body += `<line class='chart-rule' x1='${padX}' y1='${y}' x2='${w - padX}' y2='${y}' stroke='${r.color || 'var(--muted)'}'></line>`;
    if (r.label) body += `<text x='${w - padX}' y='${(+y - 3).toFixed(1)}' text-anchor='end' font-size='9' fill='${r.color || 'var(--muted)'}' font-family='var(--mono)'>${escapeHtml(r.label)}</text>`;
  }
  if (o.markers) {
    const n0 = series[0].values.length;
    for (const m of o.markers) {
      const fx = (m.at != null) ? m.at : (m.index != null ? m.index / Math.max(1, n0 - 1) : 0);
      const x = (padX + Math.max(0, Math.min(1, fx)) * iw).toFixed(1);
      body += `<line class='chart-mark' x1='${x}' y1='${padTop}' x2='${x}' y2='${padTop + ih}' stroke='${m.color || 'var(--spike)'}'></line>`;
    }
  }
  const svg = `<svg viewBox='0 0 ${w} ${h}' class='chart-svg' preserveAspectRatio='none'>${body}</svg>`;
  if (o.axis) { const fmt = o.fmt || (v => v.toFixed(0)); return svg + `<div class='chart-axis'><span class='lo'>${escapeHtml(fmt(sc.min))}</span><span class='hi'>${escapeHtml(fmt(sc.max))}</span></div>`; }
  return svg;
}

// ---- Bar chart: vertical column strip OR horizontal rows (histogram) --
// vertical opts: { bars:[{value,color,marks:['spike'],tip}], max, colWidth, scrollx }
// horizontal opts: { horizontal:true, bars:[{value,label,valueLabel,color}], max }
function barChart(o) {
  o = o || {};
  const bars = o.bars || [];
  const max = o.max || Math.max(1e-6, ...bars.map(b => b.value || 0));
  if (o.horizontal) {
    let h = `<div class='bar-rows'>`;
    for (const b of bars) {
      const frac = (b.value || 0) / max;
      h += `<div class='bar-row'><span class='lbl'>${escapeHtml(b.label != null ? String(b.label) : '')}</span>` +
        cellBar(frac, b.color || 'var(--accent)') +
        `<span class='val'>${escapeHtml(b.valueLabel != null ? String(b.valueLabel) : String(b.value))}</span></div>`;
    }
    return h + `</div>`;
  }
  const cw = o.colWidth || 5;
  let h = `<div class='bar-strip${o.scrollx ? ' scrollx' : ''}'>`;
  for (const b of bars) {
    const frac = Math.max(0, Math.min(1, (b.value || 0) / max));
    let marks = '';
    if (b.marks) for (const m of b.marks) marks += `<span class='bar-mark ${m}'></span>`;
    const bg = b.color ? `background:${b.color};` : '';
    h += `<div class='bar-col' style='width:${cw}px' title='${escapeHtml(b.tip || '')}'>${marks}<span class='bar-col-fill' style='height:${(frac * 100).toFixed(1)}%;${bg}'></span></div>`;
  }
  return h + `</div>`;
}

// ---- Donut / pie / stacked-pie --------------------------------------
// opts: { data:[{value,label,color,valueLabel}] , inner:0..1 (0=pie),
//         rings:[ data[], data[] ] (nested/stacked pie), w, ringGap,
//         center:{top,mid,bot} }
function donut(o) {
  o = o || {};
  const size = o.w || 160, cx = size / 2, cy = size / 2, rOmax = size / 2 - 1;
  const ringsData = o.rings || [o.data || []];
  const innerFrac = o.inner != null ? o.inner : 0.62;
  const bandTotal = rOmax * (1 - innerFrac), gap = o.ringGap != null ? o.ringGap : 2;
  let paths = '';
  for (let ri = 0; ri < ringsData.length; ri++) {
    const data = (ringsData[ri] || []).filter(d => d && d.value > 0);
    const total = data.reduce((s, d) => s + d.value, 0) || 1;
    const band = bandTotal / ringsData.length;
    const rO = rOmax - ri * band;
    const rI = ringsData.length > 1 ? Math.max(0, rO - band + gap) : innerFrac * rOmax;
    let ang = 0;
    for (const d of data) {
      const sw = d.value / total * 360;
      paths += `<path d='${_ring(cx, cy, rO, rI, ang, ang + Math.max(0.4, sw))}' fill='${d.color}'><title>${escapeHtml((d.label || '') + (d.valueLabel != null ? ' · ' + d.valueLabel : ''))}</title></path>`;
      ang += sw;
    }
  }
  let center = '';
  if (o.center) {
    center = `<div class='chart-center'>` +
      (o.center.top != null ? `<span class='cc-top'>${escapeHtml(String(o.center.top))}</span>` : '') +
      (o.center.mid != null ? `<span class='cc-mid'>${escapeHtml(String(o.center.mid))}</span>` : '') +
      (o.center.bot != null ? `<span class='cc-bot'>${escapeHtml(String(o.center.bot))}</span>` : '') + `</div>`;
  }
  return `<div class='chart-donut-wrap'><svg viewBox='0 0 ${size} ${size}' class='chart-donut' width='${size}' height='${size}'>${paths}</svg>${center}</div>`;
}

// ---- Gauge: radial ratio with optional coloured bands ---------------
// opts: { ratio:0..1 | value+max, bands:[{to,color}], sweep:180|270, w, stroke,
//         color, centerValue, centerSub }
function gauge(o) {
  o = o || {};
  const ratio = Math.max(0, Math.min(1, o.ratio != null ? o.ratio : ((o.value || 0) / (o.max || 1))));
  const size = o.w || 170, sweep = o.sweep || 180, stroke = o.stroke || 12;
  const cx = size / 2, r = size / 2 - stroke / 2 - 1, cy = size / 2;
  const vbH = sweep <= 180 ? (size / 2 + stroke + 4) : size;
  const start = 270, end = 270 + sweep;
  let svg = `<path class='gauge-track' d='${_arcStroke(cx, cy, r, start, end)}' stroke-width='${stroke}' stroke-linecap='round'></path>`;
  if (o.bands) {
    let from = 0;
    for (const b of o.bands) {
      const a0 = start + sweep * from, a1 = start + sweep * Math.min(1, b.to);
      svg += `<path d='${_arcStroke(cx, cy, r, a0, a1)}' fill='none' stroke='${b.color}' stroke-width='${(stroke * 0.45).toFixed(1)}' opacity='0.45'></path>`;
      from = b.to;
    }
  }
  const va = start + sweep * ratio;
  const valColor = o.color || (o.bands ? _bandColor(o.bands, ratio) : 'var(--accent)');
  if (ratio > 0) svg += `<path class='gauge-val' d='${_arcStroke(cx, cy, r, start, va)}' stroke='${valColor}' stroke-width='${stroke}' stroke-linecap='round'></path>`;
  if (o.centerValue != null) svg += `<text class='gauge-text' x='${cx}' y='${cy}' font-size='${(size * 0.17).toFixed(0)}'>${escapeHtml(String(o.centerValue))}</text>`;
  if (o.centerSub != null) svg += `<text class='gauge-sub' x='${cx}' y='${(cy + size * 0.12).toFixed(0)}' font-size='${(size * 0.075).toFixed(0)}'>${escapeHtml(String(o.centerSub))}</text>`;
  return `<svg viewBox='0 0 ${size} ${vbH}' class='chart-gauge' width='${size}' height='${vbH}'>${svg}</svg>`;
}

// ---- Heatmap: 2D categorical matrix (.rheat) ------------------------
// opts: { rows:[label], cols:[label], cellAt:(r,c)=>{value,tip}, max, fmt }
function heatmapMatrix(o) {
  o = o || {};
  const rows = o.rows || [], cols = o.cols || [], max = o.max || 1;
  let h = `<div class='rheat' style='grid-template-columns:minmax(4rem,auto) repeat(${cols.length},1fr)'>`;
  h += `<div class='rh-corner'></div>`;
  for (const c of cols) h += `<div class='rh-col'>${escapeHtml(String(c))}</div>`;
  for (let r = 0; r < rows.length; r++) {
    h += `<div class='rh-row'>${escapeHtml(String(rows[r]))}</div>`;
    for (let c = 0; c < cols.length; c++) {
      const cell = o.cellAt ? o.cellAt(r, c) : null;
      const v = cell ? (cell.value || 0) : 0;
      h += `<div class='rh-cell${v <= 0 ? ' zero' : ''}' style='background:${heatFill(v / max)}' title='${escapeHtml((cell && cell.tip) || '')}'>${v > 0 ? escapeHtml(o.fmt ? o.fmt(v) : String(v)) : ''}</div>`;
    }
  }
  return h + `</div>`;
}
";
}
