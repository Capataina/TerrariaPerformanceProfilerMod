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
  return (v/(1024*1024)).toFixed(1) + ' MB';
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

// Consistent mod color: hash modId into the visible-pleasant range.
// Desaturated palette so 18 simultaneous slices in the impact donut
// don't fight the rest of the UI. Each is recognisable but muted.
const MOD_COLORS = ['#5f8db3', '#7d6d9c', '#4f9d6a', '#7e9477', '#a07852', '#8a6db8', '#a05b6a', '#4ab8c2', '#6aa3a8', '#b88a25', '#8d7e5a', '#5b6cb0'];
function modColor(id) { return MOD_COLORS[(id * 7 + 3) % MOD_COLORS.length]; }
";
}
