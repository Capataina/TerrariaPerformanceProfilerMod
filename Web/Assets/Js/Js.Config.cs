#nullable enable

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
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
let lastMemory = null;
let lastDataHealth = null;
let lastSuccessAt = Date.now();
let modSort = 'composite';
let modFilter = '';
let timelineFilter = 'all';
let frameChartMode = 'ms';  // 'ms' (frame time) or 'fps'
let streamModCount = 5;     // cost-stream: top-N mods shown (3 / 5 / 10)
let streamWindow = 25;      // cost-stream: time entries shown (10 / 25 / 50)
const expandedMods = new Set();      // modId -> open
const expandedCategories = new Set(); // modId|catId -> open
const expandedSpikes = new Set();
const expandedStalls = new Set();
const expandedSegments = new Set();
const modSparkHistory = new Map();   // modId -> [last N cpu values] for inline mini-spark
const modStreamHistory = new Map();  // modId -> [last 50 composite samples] for the cost-stream area (sampled ~5s)
";
}
