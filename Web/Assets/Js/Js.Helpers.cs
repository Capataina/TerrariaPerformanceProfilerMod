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
    private const string JsHelpers = @"
// ====== Helpers ======================================================
function fmtMs(v) {
  if (v == null || !isFinite(v)) return '—';
  if (v < 0.005) return '0.00';
  if (v < 10) return v.toFixed(2);
  if (v < 100) return v.toFixed(1);
  return v.toFixed(0);
}
function fmtInt(v) { return v == null ? '—' : v.toLocaleString(); }
function fmtBytes(v) {
  if (v == null || !isFinite(v) || v <= 0) return '—';
  if (v < 1024) return v.toFixed(0) + ' B';
  if (v < 1024*1024) return (v/1024).toFixed(1) + ' KB';
  if (v < 1024*1024*1024) return (v/(1024*1024)).toFixed(1) + ' MB';
  return (v/(1024*1024*1024)).toFixed(2) + ' GB';
}
function fmtDuration(ms) {
  if (ms == null) return '—';
  if (ms < 1000) return ms + 'ms';
  const s = Math.floor(ms / 1000);
  if (s < 60) return s + 's';
  const m = Math.floor(s / 60);
  if (m < 60) return m + 'm ' + String(s%60).padStart(2,'0') + 's';
  return Math.floor(m/60) + 'h ' + String(m%60).padStart(2,'0') + 'm';
}
function fmtAgo(unixMs) {
  if (!unixMs || !lastNow) return '';
  const dt = lastNow.unixMs - unixMs;
  if (dt < 1000) return 'just now';
  if (dt < 60000) return Math.floor(dt/1000) + 's ago';
  if (dt < 3600000) return Math.floor(dt/60000) + 'm ago';
  return Math.floor(dt/3600000) + 'h ago';
}
function truncate(s, n) { return s && s.length > n ? s.substring(0, n-1) + '…' : (s || ''); }
function escapeHtml(s) {
  if (s == null) return '';
  return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
                  .replace(/'/g, '&#39;').replace(/""/g, '&quot;');
}

// Consistent per-mod colour: a systematic OKLCH categorical palette (12 hues at
// a locked lightness 0.72 and chroma 0.11, stepped evenly around the wheel). The
// equal L/C makes it read as one cohesive, muted family on the neutral-grey
// surface rather than a clashing rainbow, while even hue keeps slices
// distinguishable. The id*7+3 hash spreads consecutive mod ids onto non-adjacent
// hues so neighbouring rows don't get near-identical colours.
const MOD_COLORS = [
  'oklch(0.72 0.11 20)',  'oklch(0.72 0.11 50)',  'oklch(0.72 0.11 80)',  'oklch(0.72 0.11 110)',
  'oklch(0.72 0.11 140)', 'oklch(0.72 0.11 170)', 'oklch(0.72 0.11 200)', 'oklch(0.72 0.11 230)',
  'oklch(0.72 0.11 260)', 'oklch(0.72 0.11 290)', 'oklch(0.72 0.11 320)', 'oklch(0.72 0.11 350)'];
function modColor(id) { return MOD_COLORS[(id * 7 + 3) % MOD_COLORS.length]; }
";
}
