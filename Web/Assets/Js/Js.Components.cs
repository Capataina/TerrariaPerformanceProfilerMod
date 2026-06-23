#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Render helpers for the shared readable vocabulary. Surfaces compose these
    // instead of hand-rolling bespoke SVG. Loaded after JsHelpers so escapeHtml
    // / fmt* / modColor exist. Dynamic per-element tooltips use native title=;
    // the data-explain path is reserved for the fixed help dictionary.
    private const string JsComponents = @"
// ====== Readable-vocabulary render helpers ===========================

// Perf tint class from a ratio against a reference (1 = at reference).
function tintClass(ratio) {
  if (ratio == null || !isFinite(ratio)) return '';
  if (ratio < 1.5) return 't0';
  if (ratio < 3)   return 't1';
  if (ratio < 6)   return 't2';
  if (ratio < 12)  return 't3';
  return 't4';
}

// Colour-coded split bar. segs: [{frac (0..1), color, label, value}].
// Renders the stacked bar; pair with splitLegend() for the key.
function splitBar(segs, opts) {
  opts = opts || {};
  let cls = 'split-bar';
  if (opts.tall) cls += ' tall';
  if (opts.thin) cls += ' thin';
  let h = `<div class='${cls}'>`;
  for (const s of segs) {
    if (!s.frac || s.frac <= 0) continue;
    const tip = s.label != null ? escapeHtml(s.label + (s.value != null ? ' · ' + s.value : '')) : '';
    h += `<span style='width:${(s.frac*100).toFixed(2)}%;background:${s.color}' title='${tip}'></span>`;
  }
  return h + `</div>`;
}

// Legend row for a split bar. segs: [{color, label, value}].
function splitLegend(segs) {
  let h = `<div class='bar-legend'>`;
  for (const s of segs) {
    if (s.frac != null && s.frac <= 0) continue;
    h += `<span class='lg'><span class='sw' style='background:${s.color}'></span>${escapeHtml(s.label)}`;
    if (s.value != null) h += ` <span class='lg-v'>${escapeHtml(String(s.value))}</span>`;
    h += `</span>`;
  }
  return h + `</div>`;
}

// Inline cost bar for a table cell. frac 0..1, optional bar colour.
function cellBar(frac, color) {
  const w = (Math.max(0, Math.min(1, frac || 0)) * 100).toFixed(1);
  return `<span class='cellbar'><span style='width:${w}%${color ? ';background:'+color : ''}'></span></span>`;
}

// Heat-cell background from intensity 0..1 (faint -> bright). For .rh-cell.
// Monochrome scheme: magnitude reads as white luminance on the dark surface, so
// the heatmap fits the neutral chrome while still encoding the value by opacity.
function heatFill(intensity) {
  const a = Math.max(0, Math.min(1, intensity || 0));
  return `oklch(0.985 0 0 / ${(0.05 + a*0.45).toFixed(3)})`;
}

// Replace innerHTML while preserving the element's own scroll position, so a
// poll-driven re-render of a scrollable list doesn't snap it back to the top.
// The element passed MUST be the scroll container (the one with overflow:auto).
function setHTML(el, html) {
  if (!el) return;
  const t = el.scrollTop, l = el.scrollLeft;
  el.innerHTML = html;
  el.scrollTop = t; el.scrollLeft = l;
}

// Auto-scale a numeric series to its own range (with padding) so a flat-but-high
// band (e.g. heap always ~8 GB) shows its variation instead of hugging the top
// of a 0-based axis. Returns {min, max}.
function niceScale(values, padFrac) {
  let lo = Infinity, hi = -Infinity;
  for (const v of values) { if (!isFinite(v)) continue; if (v < lo) lo = v; if (v > hi) hi = v; }
  if (!isFinite(lo) || !isFinite(hi)) return { min: 0, max: 1 };
  if (hi === lo) { const e = Math.abs(hi) * 0.05 || 1; return { min: lo - e, max: hi + e }; }
  const pad = (hi - lo) * (padFrac == null ? 0.12 : padFrac);
  return { min: lo - pad, max: hi + pad };
}

// SVG line + area path d-strings for a series, auto-scaled by niceScale into a
// box. opts: {w,h,padX,padTop,padBot}. Returns {line, area, scale}.
function seriesPaths(values, opts) {
  opts = opts || {};
  const w = opts.w || 540, h = opts.h || 120;
  const padX = opts.padX != null ? opts.padX : 8;
  const padTop = opts.padTop != null ? opts.padTop : 16;
  const padBot = opts.padBot != null ? opts.padBot : 16;
  const innerW = w - padX * 2, innerH = h - padTop - padBot;
  const n = values.length;
  const s = niceScale(values);
  const span = (s.max - s.min) || 1;
  function pt(i) {
    const x = padX + (n > 1 ? (i / (n - 1)) * innerW : innerW / 2);
    const y = padTop + innerH - ((values[i] - s.min) / span) * innerH;
    return [x, y];
  }
  if (n === 0) return { line: '', area: '', scale: s };
  let line = '';
  for (let i = 0; i < n; i++) { const p = pt(i); line += (i ? ' L ' : 'M ') + p[0].toFixed(1) + ' ' + p[1].toFixed(1); }
  const area = 'M ' + pt(0)[0].toFixed(1) + ' ' + (padTop + innerH).toFixed(1) + ' L ' + line.slice(2) +
               ' L ' + pt(n - 1)[0].toFixed(1) + ' ' + (padTop + innerH).toFixed(1) + ' Z';
  return { line, area, scale: s };
}

// Canonical empty-state markup. Every 'no data yet' / placeholder message should
// go through this so the styling stays consistent (see .empty in the coherence
// layer); stops each surface re-inventing its own empty class.
function emptyState(msg) { return `<div class='empty'>${escapeHtml(msg)}</div>`; }

// Render a value, or an em-dash when it is genuinely absent (null / NaN), so a
// label is never left with nothing after it. fn is an optional formatter; a real
// zero still formats normally (the formatter decides how to show it).
function dash(v, fn) {
  if (v == null || (typeof v === 'number' && !isFinite(v))) return '—';
  return fn ? fn(v) : String(v);
}

// ====== Structural components ========================================
// A 'component' here = one CSS class block + one render fn returning an HTML
// string + an opts contract. No framework. Data is the only variable.

// Panel: border + radius + header (title / sub / actions) + ONE body slot.
// opts: { title?, sub?, actions?:html, body:html, scroll?:bool, scrollId?,
//         fill?:bool, pad?:'flush'|'tight', maxH?, cls? }
function panel(o) {
  o = o || {};
  const actions = o.actions ? `<div class='panel-actions'>${o.actions}</div>` : '';
  const sub = o.sub != null ? `<span class='panel-sub'>${escapeHtml(String(o.sub))}</span>` : '';
  const head = (o.title != null || o.sub != null || o.actions)
    ? `<header class='panel-h'><span class='panel-title'>${escapeHtml(o.title || '')}</span>${sub}${actions}</header>` : '';
  let body;
  if (o.scroll) {
    const st = o.maxH ? ` style='max-height:${o.maxH}'` : '';
    body = `<div class='scroll-region${o.fill ? ' fill' : ''}' id='${o.scrollId || ''}'${st}>${o.body || ''}</div>`;
  } else {
    const pad = o.pad === 'flush' ? ' flush' : o.pad === 'tight' ? ' tight' : '';
    body = `<div class='panel-body${pad}'>${o.body || ''}</div>`;
  }
  return `<div class='panel${o.fill ? ' fill' : ''}${o.cls ? ' ' + o.cls : ''}'>${head}${body}</div>`;
}

// Scroll region markup (bound by .fill or an inline max-height). Re-render its
// contents via setHTML(el, html) so the poll never snaps it to the top.
function scrollRegion(id, html, o) {
  o = o || {};
  const st = o.maxH ? ` style='max-height:${o.maxH}'` : '';
  return `<div class='scroll-region${o.fill ? ' fill' : ''}' id='${id}'${st}>${html || ''}</div>`;
}

// Section header: the small uppercase label inside a panel body / drawer.
function sectionHeader(title, sub) {
  return `<div class='section-h'><span>${escapeHtml(title || '')}</span>` +
    (sub != null ? `<span class='section-sub'>${escapeHtml(String(sub))}</span>` : '') + `</div>`;
}
function sectionBlock(title, body, sub) {
  return `<div class='section-block'>${sectionHeader(title, sub)}${body || ''}</div>`;
}

// The one expand/collapse chevron.
function twirl(open) { return `<span class='twirl${open ? ' open' : ''}'>&#9656;</span>`; }

// Row in a clickable list: one hover + selection model, reserved left bar.
// opts: { cols:gridTemplate, cells:[html], clickable?, sel?, outlier?, attrs?, cls? }
function row(o) {
  o = o || {};
  let cls = 'row';
  if (o.clickable) cls += ' clickable';
  if (o.sel) cls += ' sel';
  if (o.outlier) cls += ' outlier';
  if (o.cls) cls += ' ' + o.cls;
  const st = o.cols ? ` style='grid-template-columns:${o.cols}'` : '';
  return `<div class='${cls}'${st}${o.attrs ? ' ' + o.attrs : ''}>${(o.cells || []).join('')}</div>`;
}
function rowList(rowsHtml) { return `<div class='rowlist'>${(rowsHtml || []).join('')}</div>`; }

// Stat tile: label-over-value card, optional severity tint + proportion bar.
// opts: { k, v, vClass?, big?, sub?, frac?, color? }
function statTile(o) {
  o = o || {};
  let h = `<div class='stat-tile'><span class='k'>${escapeHtml(o.k || '')}</span>` +
    `<span class='v${o.vClass ? ' ' + o.vClass : ''}${o.big ? ' big' : ''}'>${o.v == null ? '—' : escapeHtml(String(o.v))}</span>`;
  if (o.sub != null) h += `<span class='sub'>${escapeHtml(String(o.sub))}</span>`;
  if (o.frac != null && isFinite(o.frac)) h += `<span class='stat-bar'>${cellBar(o.frac, o.color)}</span>`;
  return h + `</div>`;
}
function statGrid(tiles, o) {
  o = o || {};
  const st = o.cols ? ` style='grid-template-columns:${o.cols}'` : '';
  return `<div class='stat-grid'${st}>${(tiles || []).join('')}</div>`;
}

// Bordered callout. kind: '' | 'warn' | 'bad'.
function callout(html, kind) { return `<div class='callout${kind ? ' ' + kind : ''}'>${html || ''}</div>`; }

// Legend (one impl). items: [{color, label, value?}]. opts: {stack?, inline?}.
function legend(items, o) {
  o = o || {};
  let cls = 'bar-legend' + (o.stack ? ' stack' : '') + (o.inline ? ' inline' : '');
  let h = `<div class='${cls}'>`;
  for (const s of items) {
    if (!s) continue;
    h += `<span class='lg'><span class='sw' style='background:${s.color}'></span>${escapeHtml(s.label)}`;
    if (s.value != null) h += ` <span class='lg-v'>${escapeHtml(String(s.value))}</span>`;
    h += `</span>`;
  }
  return h + `</div>`;
}

// Segmented control (unifies .segctl / .chart-toggle). The caller wires clicks
// via the data-attr + id. opts: { id, attr:'data-x', active, options:[{value,label,disabled?,title?}] }
function segmented(o) {
  o = o || {};
  let h = `<span class='segctl' id='${o.id || ''}'>`;
  for (const s of o.options) {
    const on = s.value === o.active ? ' active' : '';
    const dis = s.disabled ? ` style='opacity:0.35;pointer-events:none'` : '';
    const tip = s.title ? ` title='${escapeHtml(s.title)}'` : '';
    h += `<button ${o.attr || 'data-seg'}='${s.value}' class='${on.trim()}'${dis}${tip}>${escapeHtml(s.label)}</button>`;
  }
  return h + `</span>`;
}

// Re-render only when the input signature changed, so the 500ms poll doesn't
// churn the DOM. Generalises the Timeline _tlSig pattern. Returns true if it
// rendered. el may be a scroll container (scroll position is preserved).
const _renderSig = {};
function renderIfChanged(key, sig, el, html) {
  if (_renderSig[key] === sig) return false;
  _renderSig[key] = sig;
  if (typeof el === 'function') { el(); return true; }
  if (el) setHTML(el, html);
  return true;
}
";
}
