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
    private const string CssModCard = @"
/* =================================================== MOD CARD (slide-in) */
.modcard {
  position: fixed; top: 0; right: 0; bottom: 0;
  width: 28rem; max-width: 95vw;
  background: linear-gradient(180deg, var(--panel) 0%, var(--bg-page) 100%);
  border-left: 1px solid var(--border);
  z-index: 40; display: flex; flex-direction: column;
  transform: translateX(0); transition: transform 0.2s ease-out;
  box-shadow: -8px 0 24px rgba(0, 0, 0, 0.4);
}
.modcard.hidden { transform: translateX(100%); display: flex !important; pointer-events: none; }
.mc-h {
  display: grid; grid-template-columns: auto 1fr auto;
  gap: 0.8rem; align-items: center;
  padding: 0.9rem 1.1rem; border-bottom: 1px solid var(--border);
}
.mc-back {
  cursor: pointer; color: var(--muted); font-size: 1.5rem;
  width: 1.2em; height: 1.2em; display: flex; align-items: center; justify-content: center;
  border-radius: 3px; line-height: 1;
}
.mc-back:hover { color: var(--text); background: var(--hover); }
.mc-name { font-family: var(--ui); font-weight: 600; font-size: 1.1rem; color: var(--text-bright); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.mc-rank { font-family: var(--mono); font-size: 0.78rem; color: var(--muted); }
.mc-body { flex: 1; overflow-y: auto; padding: 1rem 1.1rem 2rem; }
.mc-section { margin-bottom: 1.2rem; }
.mc-section h3 {
  font-family: var(--ui); font-size: 0.75rem; font-weight: 600;
  color: var(--muted); text-transform: uppercase; letter-spacing: 0.07em;
  margin: 0 0 0.4rem;
}
.mc-stat-grid {
  display: grid; grid-template-columns: 1fr 1fr; gap: 0.5rem;
}
.mc-stat {
  background: var(--surface); border: 1px solid var(--border-soft);
  border-radius: 3px; padding: 0.55rem 0.7rem;
  display: flex; flex-direction: column; gap: 0.15rem;
}
.mc-stat .k { font-size: 0.7rem; color: var(--muted); text-transform: uppercase; letter-spacing: 0.06em; }
.mc-stat .v { font-family: var(--mono); font-size: 1.15rem; color: var(--text); }
.mc-stat .v.big { font-size: 1.5rem; color: var(--text-bright); }
.mc-stat .v.accent { color: var(--accent); }
.mc-stat .v.good { color: var(--good); }
.mc-stat .v.warn { color: var(--amber); }
.mc-stat .sub { font-size: 0.7rem; color: var(--dim); }

.mc-callout {
  background: rgba(121, 192, 255, 0.06);
  border: 1px solid var(--accent-line);
  border-radius: 3px;
  padding: 0.7rem 0.85rem; margin: 0.6rem 0;
  font-size: 0.88rem;
}
.mc-callout strong { color: var(--accent); }

.mc-catlist { display: flex; flex-direction: column; gap: 0.2rem; }
.mc-cat-row {
  display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 2fr) 5em;
  gap: 0.5rem; align-items: center;
  font-family: var(--mono); font-size: 0.85rem;
}
.mc-cat-row .nm { color: var(--text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.mc-cat-row .br { height: 0.55rem; background: var(--surface); border-radius: 1px; overflow: hidden; }
.mc-cat-row .br > span { display: block; height: 100%; background: var(--cpu); }
.mc-cat-row .vl { color: var(--muted); text-align: right; }
";
}
