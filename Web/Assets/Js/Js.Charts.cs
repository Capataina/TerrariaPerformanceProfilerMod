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
      // A datum carrying an id becomes a click target (data-mod); .slice.hit gives
      // it the pointer affordance + hover feedback. Callers wire the click.
      const hit = d.id != null ? ` data-mod='${d.id}' class='slice hit'` : ` class='slice'`;
      paths += `<path${hit} d='${_ring(cx, cy, rO, rI, ang, ang + Math.max(0.4, sw))}' fill='${d.color}'><title>${escapeHtml((d.label || '') + (d.valueLabel != null ? ' · ' + d.valueLabel : ''))}</title></path>`;
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
  // For a half gauge leave room below the diameter for the centre value + sub
  // (the sub sits at cy + 0.12*size, so the box must clear that).
  const vbH = sweep <= 180 ? Math.round(size / 2 + size * 0.2) : size;
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
  // Optional reference tick across the band (e.g. a budget threshold), so the
  // arc is read against a target instead of in the abstract.
  if (o.ref != null && isFinite(o.ref)) {
    const ra = start + sweep * Math.max(0, Math.min(1, o.ref));
    const i = _polar(cx, cy, r - stroke * 0.8, ra), oo = _polar(cx, cy, r + stroke * 0.8, ra);
    svg += `<line class='gauge-ref' x1='${i[0].toFixed(2)}' y1='${i[1].toFixed(2)}' x2='${oo[0].toFixed(2)}' y2='${oo[1].toFixed(2)}'></line>`;
  }
  if (o.centerValue != null) svg += `<text class='gauge-text' x='${cx}' y='${cy}' font-size='${(size * 0.17).toFixed(0)}'>${escapeHtml(String(o.centerValue))}</text>`;
  if (o.centerSub != null) svg += `<text class='gauge-sub' x='${cx}' y='${(cy + size * 0.12).toFixed(0)}' font-size='${(size * 0.075).toFixed(0)}'>${escapeHtml(String(o.centerSub))}</text>`;
  return `<svg viewBox='0 0 ${size} ${vbH}' class='chart-gauge' width='${size}' height='${vbH}'>${svg}</svg>`;
}

// ---- Scatter / bubble: x vs y relationship, optional radius -----------
// The encoding for a relationship (two shared-axis quantities), with an optional
// third dimension as bubble area and an optional y=x reference line. opts:
//   { points:[{x,y,r?,color?,label?,id?}], w, h, xMax, yMax, rMax,
//     xLabel, yLabel, fmt, diag?:bool }
// A datum with an id becomes a click target (data-mod / .slice.hit), matching donut.
function scatter(o) {
  o = o || {};
  const w = o.w || 420, h = o.h || 300;
  const padL = 40, padR = 14, padT = 12, padB = 30;
  const pw = w - padL - padR, ph = h - padT - padB;
  const pts = (o.points || []).filter(p => p && isFinite(p.x) && isFinite(p.y));
  const xMax = o.xMax || Math.max(1e-9, ...pts.map(p => p.x));
  const yMax = o.yMax || Math.max(1e-9, ...pts.map(p => p.y));
  const rMax = o.rMax || Math.max(1e-9, ...pts.map(p => p.r || 0));
  const X = v => padL + (v / xMax) * pw;
  const Y = v => padT + ph - (v / yMax) * ph;
  const R = v => rMax > 0 ? 3 + Math.sqrt(Math.max(0, v) / rMax) * 12 : 4;
  const fmt = o.fmt || (v => v.toFixed(0));

  // Frame: left + bottom axes, faint gridlines at the mid + max.
  let g = `<line class='sc-axis' x1='${padL}' y1='${padT}' x2='${padL}' y2='${padT + ph}'></line>` +
          `<line class='sc-axis' x1='${padL}' y1='${padT + ph}' x2='${padL + pw}' y2='${padT + ph}'></line>` +
          `<line class='sc-grid' x1='${padL}' y1='${(padT + ph / 2).toFixed(1)}' x2='${padL + pw}' y2='${(padT + ph / 2).toFixed(1)}'></line>` +
          `<line class='sc-grid' x1='${(padL + pw / 2).toFixed(1)}' y1='${padT}' x2='${(padL + pw / 2).toFixed(1)}' y2='${padT + ph}'></line>`;
  // Optional y=x balance reference (only meaningful when axes share a scale).
  if (o.diag) {
    const m = Math.min(xMax, yMax);
    g += `<line class='sc-diag' x1='${X(0).toFixed(1)}' y1='${Y(0).toFixed(1)}' x2='${X(m).toFixed(1)}' y2='${Y(m).toFixed(1)}'></line>`;
  }
  // Bubbles, largest first so small ones stay clickable on top.
  const ordered = pts.slice().sort((a, b) => (b.r || 0) - (a.r || 0));
  for (const p of ordered) {
    const hit = p.id != null ? ` data-mod='${p.id}' class='sc-dot hit'` : ` class='sc-dot'`;
    const tip = escapeHtml((p.label || '') + ' · ' + fmt(p.x) + ' / ' + fmt(p.y) + (p.r != null ? ' · ' + fmtInt(p.r) : ''));
    g += `<circle${hit} cx='${X(p.x).toFixed(1)}' cy='${Y(p.y).toFixed(1)}' r='${R(p.r || 0).toFixed(1)}' fill='${p.color || 'var(--cpu)'}'><title>${tip}</title></circle>`;
  }
  // Axis labels + max ticks.
  const xl = o.xLabel ? `<text class='sc-lbl' x='${(padL + pw / 2).toFixed(0)}' y='${h - 4}' text-anchor='middle'>${escapeHtml(o.xLabel)}</text>` : '';
  const yl = o.yLabel ? `<text class='sc-lbl' x='${-(padT + ph / 2).toFixed(0)}' y='11' text-anchor='middle' transform='rotate(-90)'>${escapeHtml(o.yLabel)}</text>` : '';
  const ticks = `<text class='sc-tick' x='${padL + pw}' y='${padT + ph + 12}' text-anchor='end'>${escapeHtml(fmt(xMax))}</text>` +
                `<text class='sc-tick' x='${padL - 4}' y='${padT + 8}' text-anchor='end'>${escapeHtml(fmt(yMax))}</text>` +
                `<text class='sc-tick' x='${padL - 4}' y='${padT + ph}' text-anchor='end'>0</text>`;
  return `<svg viewBox='0 0 ${w} ${h}' class='chart-scatter' preserveAspectRatio='xMidYMid meet'>${g}${xl}${yl}${ticks}</svg>`;
}

// ---- Waffle: unit grid, cells coloured by category --------------------
// Part-to-whole as a grid of unit squares (one cell per unit), so composition
// reads as countable area, not just a number. opts:
//   { cells:[{count,color,label}], cols?, total? }
function waffle(o) {
  o = o || {};
  const cells = (o.cells || []).filter(c => c && c.count > 0);
  const total = o.total || cells.reduce((s, c) => s + c.count, 0);
  if (total <= 0) return emptyState('no composition data');
  const cols = o.cols || Math.min(20, Math.max(8, Math.ceil(Math.sqrt(total))));
  // Flatten the category counts into a per-cell colour list.
  const seq = [];
  for (const c of cells) for (let i = 0; i < c.count; i++) seq.push(c.color);
  let squares = '';
  for (let i = 0; i < seq.length; i++) {
    squares += `<span class='wf-cell' style='background:${seq[i]}'></span>`;
  }
  // The caller renders the key via legend(); this returns just the cell grid.
  return `<div class='chart-waffle' style='grid-template-columns:repeat(${cols},1fr)'>${squares}</div>`;
}

// ---- Sankey: two-layer flow (source category -> target mod) -----------
// Where a quantity flows: left nodes -> right nodes, ribbon width = flow value.
// The asked-for new capability (cost flowing category -> mod). opts:
//   { left:[{label,value,color?}], right:[{label,value,color?}],
//     links:[{l:leftIdx, r:rightIdx, value}], w, h, fmt }
function sankey(o) {
  o = o || {};
  const w = o.w || 460, h = o.h || 320;
  const nodeW = 10, padT = 6, padB = 6, gap = 6;
  const colX = { l: 2, r: w - nodeW - 2 };
  const left = o.left || [], right = o.right || [], links = (o.links || []).filter(k => k && k.value > 0);
  const fmt = o.fmt || (v => v.toFixed(2));
  if (!left.length || !right.length || !links.length) return emptyState('no flow data');

  // Node heights proportional to value, stacked top->bottom with gaps.
  function layout(nodes, x) {
    const tot = nodes.reduce((s, n) => s + (n.value || 0), 0) || 1;
    const usable = h - padT - padB - gap * (nodes.length - 1);
    let y = padT;
    return nodes.map(n => {
      const nh = Math.max(2, (n.value || 0) / tot * usable);
      const box = { x, y, h: nh, cy: y + nh / 2, value: n.value, label: n.label, color: n.color, off: 0 };
      y += nh + gap;
      return box;
    });
  }
  const L = layout(left, colX.l), Rr = layout(right, colX.r);

  // Track a running offset on each node so stacked ribbons don't overlap.
  const Loff = L.map(() => 0), Roff = Rr.map(() => 0);
  // Draw widest links first (under), thin on top.
  const sorted = links.slice().sort((a, b) => b.value - a.value);
  let ribbons = '';
  for (const k of sorted) {
    const a = L[k.l], b = Rr[k.r];
    if (!a || !b) continue;
    const aTot = left[k.l].value || 1, bTot = right[k.r].value || 1;
    const wA = a.h * (k.value / aTot), wB = b.h * (k.value / bTot);
    const y0 = a.y + Loff[k.l], y1 = b.y + Roff[k.r];
    Loff[k.l] += wA; Roff[k.r] += wB;
    const x0 = colX.l + nodeW, x1 = colX.r, xm = (x0 + x1) / 2;
    // Filled ribbon: top edge curves x0->x1, bottom edge curves back.
    const d = `M ${x0} ${y0.toFixed(1)} C ${xm} ${y0.toFixed(1)}, ${xm} ${y1.toFixed(1)}, ${x1} ${y1.toFixed(1)} ` +
              `L ${x1} ${(y1 + wB).toFixed(1)} C ${xm} ${(y1 + wB).toFixed(1)}, ${xm} ${(y0 + wA).toFixed(1)}, ${x0} ${(y0 + wA).toFixed(1)} Z`;
    ribbons += `<path class='sk-link' d='${d}' fill='${a.color || 'var(--cpu)'}'><title>${escapeHtml(a.label + ' → ' + b.label + ' · ' + fmt(k.value))}</title></path>`;
  }
  // Node rectangles + labels (left labels right-anchored inside, right labels left).
  function nodes(boxes, side) {
    return boxes.map(n => {
      const lblX = side === 'l' ? n.x + nodeW + 4 : n.x - 4;
      const anchor = side === 'l' ? 'start' : 'end';
      return `<rect class='sk-node' x='${n.x}' y='${n.y.toFixed(1)}' width='${nodeW}' height='${n.h.toFixed(1)}' fill='${n.color || 'var(--muted)'}'></rect>` +
        (n.h > 9 ? `<text class='sk-lbl' x='${lblX}' y='${(n.cy + 3).toFixed(1)}' text-anchor='${anchor}'>${escapeHtml(n.label)}</text>` : '');
    }).join('');
  }
  return `<svg viewBox='0 0 ${w} ${h}' class='chart-sankey' preserveAspectRatio='xMidYMid meet'>${ribbons}${nodes(L, 'l')}${nodes(Rr, 'r')}</svg>`;
}

// ---- Smooth-curve helpers (Catmull-Rom -> cubic bezier) -------------
// Turn a list of [x,y] points into smooth bezier segments (no leading M), so
// stacked-area bands and trend lines flow as soft curves instead of jagged
// polylines. The tension is the classic 1/6 Catmull-Rom.
function _catmullSegs(pts) {
  let d = '';
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[i - 1] || pts[i], p1 = pts[i], p2 = pts[i + 1], p3 = pts[i + 2] || p2;
    const c1x = p1[0] + (p2[0] - p0[0]) / 6, c1y = p1[1] + (p2[1] - p0[1]) / 6;
    const c2x = p2[0] - (p3[0] - p1[0]) / 6, c2y = p2[1] - (p3[1] - p1[1]) / 6;
    d += ` C ${c1x.toFixed(1)} ${c1y.toFixed(1)}, ${c2x.toFixed(1)} ${c2y.toFixed(1)}, ${p2[0].toFixed(1)} ${p2[1].toFixed(1)}`;
  }
  return d;
}

// ---- Stacked-area stream: every series stacked over a rolling x window
// A streamgraph of per-series magnitude over time: each series is a smooth
// filled band, stacked, so the TOTAL height is the combined magnitude and each
// band is one series' contribution. The x axis is normalised to fill the full
// width regardless of sample count (3 samples stretch across; 50 pack in), and
// the y axis auto-scales to the tallest stacked total so it never overflows.
// opts: { series:[{label, values:[], color}], w, h, line?:bool, markers?:bool, maxY? }
// Series are drawn in the order given (first = bottom of the stack).
function streamArea(o) {
  o = o || {};
  const w = o.w || 1200, h = o.h || 160;
  const padX = 1, padT = 10, padB = 1;
  const pw = w - padX * 2, ph = h - padT - padB;
  let series = (o.series || []).filter(s => s && s.values && s.values.length);
  if (!series.length) return emptyState('building the cost stream over time…');
  let n = Math.max(...series.map(s => s.values.length));
  // Left-pad each series with zeros so all bands share the same x samples (a
  // mod that appeared late contributed nothing before it existed).
  series = series.map(s => { const v = s.values.slice(); while (v.length < n) v.unshift(0); return { label: s.label, color: s.color, v }; });
  // A single sample can't draw a curve — duplicate it into a flat band.
  if (n < 2) { n = 2; series = series.map(s => ({ label: s.label, color: s.color, v: [s.v[0] || 0, s.v[0] || 0] })); }

  const totals = [];
  for (let i = 0; i < n; i++) { let t = 0; for (const s of series) t += s.v[i] || 0; totals.push(t); }
  const maxY = o.maxY || Math.max(1e-9, ...totals) * 1.08;
  const X = i => padX + (n > 1 ? i / (n - 1) : 0.5) * pw;
  const Y = v => padT + ph - (v / maxY) * ph;

  const cum = new Array(n).fill(0);
  let bands = '';
  for (const s of series) {
    const bottom = [], top = [];
    for (let i = 0; i < n; i++) { const y0 = cum[i], y1 = cum[i] + (s.v[i] || 0); bottom.push([X(i), Y(y0)]); top.push([X(i), Y(y1)]); cum[i] = y1; }
    const bRev = bottom.slice().reverse();
    const d = `M ${top[0][0].toFixed(1)} ${top[0][1].toFixed(1)}` + _catmullSegs(top) +
              ` L ${bRev[0][0].toFixed(1)} ${bRev[0][1].toFixed(1)}` + _catmullSegs(bRev) + ' Z';
    bands += `<path class='st-band' d='${d}' fill='${s.color}'><title>${escapeHtml(s.label)}</title></path>`;
  }

  let line = '';
  if (o.line) {
    const pts = totals.map((t, i) => [X(i), Y(t)]);
    line = `<path class='st-line' d='M ${pts[0][0].toFixed(1)} ${pts[0][1].toFixed(1)}${_catmullSegs(pts)}' fill='none' vector-effect='non-scaling-stroke'></path>`;
    // Subsample the envelope markers to ~14 max so a 50-sample window stays
    // readable instead of a dotted wall.
    if (o.markers) {
      const stride = Math.max(1, Math.ceil(n / 14));
      for (let i = 0; i < n; i += stride) {
        line += `<circle class='st-dot' cx='${pts[i][0].toFixed(1)}' cy='${pts[i][1].toFixed(1)}' r='2.4' vector-effect='non-scaling-stroke'></circle>`;
      }
      if ((n - 1) % stride !== 0) line += `<circle class='st-dot' cx='${pts[n - 1][0].toFixed(1)}' cy='${pts[n - 1][1].toFixed(1)}' r='2.4' vector-effect='non-scaling-stroke'></circle>`;
    }
  }
  return `<svg viewBox='0 0 ${w} ${h}' class='chart-stream' preserveAspectRatio='none'>${bands}${line}</svg>`;
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

// ---- Chord: circular pairwise-relationship diagram ------------------
// The textbook encoding for symmetric pairwise links between entities (here:
// mod-pair cost correlation). Each node is an arc sized by its total coupling;
// each link is a ribbon whose ends occupy a slice of both arcs proportional to
// the link magnitude, curved through the centre. Ribbon colour encodes the
// caller's sign/category; node arcs use a supplied colour or modColor. opts:
//   { nodes:[{label,color?}], links:[{a,b,value,color?,tip?}], w? }
// a/b are indices into nodes. Returns the SVG; pair it with a compact table for
// the exact figures — the chord shows the coupling STRUCTURE, not precise r.
function chord(o) {
  o = o || {};
  const nodes = o.nodes || [], links = (o.links || []).filter(k => k && k.value > 0);
  if (nodes.length < 2 || links.length === 0) return emptyState('no coupling data');
  const size = o.w || 300, cx = size / 2, cy = size / 2;
  const rO = size / 2 - 2, rI = rO - Math.max(7, size * 0.04);
  const P = (r, deg) => { const p = _polar(cx, cy, r, deg); return p[0].toFixed(1) + ' ' + p[1].toFixed(1); };

  // Node weight = sum of the magnitudes of the links touching it.
  const tot = nodes.map(() => 0);
  for (const k of links) { tot[k.a] += k.value; tot[k.b] += k.value; }
  const grand = tot.reduce((s, v) => s + v, 0) || 1;
  const gapDeg = Math.min(7, 200 / nodes.length);
  const drawSpan = 360 - gapDeg * nodes.length;
  // Arc extent + a running cursor (consumed as ribbons attach) per node.
  const arc = []; let ang = 0;
  for (let i = 0; i < nodes.length; i++) {
    const w = drawSpan * (tot[i] / grand);
    arc.push({ a0: ang, a1: ang + w, cur: ang });
    ang += w + gapDeg;
  }
  // Node ring band.
  let band = '';
  for (let i = 0; i < nodes.length; i++) {
    band += `<path class='cd-arc' d='${_ring(cx, cy, rO, rI, arc[i].a0, arc[i].a1)}' fill='${nodes[i].color || modColor(i)}'><title>${escapeHtml(nodes[i].label)}</title></path>`;
  }
  // Ribbons (widest first so thin ones stay legible on top). Each end takes an
  // angular slice = drawSpan·value/grand on both nodes (symmetric), curved
  // through the centre. The cursors fill each arc exactly (Σ slices = arc width).
  let ribbons = '';
  links.slice().sort((a, b) => b.value - a.value).forEach(k => {
    const w = drawSpan * (k.value / grand);
    const A = arc[k.a], B = arc[k.b];
    const a0 = A.cur, a1 = A.cur + w; A.cur = a1;
    const b0 = B.cur, b1 = B.cur + w; B.cur = b1;
    const d = `M ${P(rI, a0)} A ${rI} ${rI} 0 0 1 ${P(rI, a1)} Q ${cx} ${cy} ${P(rI, b0)} A ${rI} ${rI} 0 0 1 ${P(rI, b1)} Q ${cx} ${cy} ${P(rI, a0)} Z`;
    ribbons += `<path class='cd-link' d='${d}' fill='${k.color || 'var(--cpu)'}'><title>${escapeHtml(k.tip || (nodes[k.a].label + ' × ' + nodes[k.b].label))}</title></path>`;
  });
  return `<svg viewBox='0 0 ${size} ${size}' class='chart-chord' preserveAspectRatio='xMidYMid meet'>${ribbons}${band}</svg>`;
}
";
}
