#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string JsConfig = @"
// ====== Config =======================================================
const POLL_NOW_MS    = 500;
const POLL_DETAIL_MS = 1500;
const POLL_HOOKS_MS  = 2500;
const POLL_SELF_MS   = 5000;
const DISCONNECT_MS  = 4000;

// ====== State ========================================================
let activeTab = 'summary';
let lastNow = null, lastFrames = null, lastMods = null, lastHooks = null;
let lastSegments = null, lastSpikes = null, lastStalls = null;
let lastInsights = null, lastSelf = null, lastHeatmap = null, lastEvents = null;
let lastSuccessAt = Date.now();
let modSort = 'composite';
let modFilter = '';
let timelineFilter = 'all';
let frameChartMode = 'ms';  // 'ms' (frame time) or 'fps'
const expandedMods = new Set();      // modId -> open
const expandedCategories = new Set(); // modId|catId -> open
const expandedSpikes = new Set();
const expandedStalls = new Set();
const expandedSegments = new Set();
const modSparkHistory = new Map();   // modId -> [last N cpu values] for inline mini-spark
";
}
