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

// Heat-cell background from intensity 0..1 (faint -> accent). For .rh-cell.
function heatFill(intensity) {
  const a = Math.max(0, Math.min(1, intensity || 0));
  return `rgba(74,158,255,${(0.05 + a*0.5).toFixed(3)})`;
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
";
}
