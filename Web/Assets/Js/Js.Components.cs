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
";
}
