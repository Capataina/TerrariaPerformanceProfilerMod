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
    private const string CssShell = @"
/* ============================================================== APP SHELL */
.app {
  display: grid;
  grid-template-rows: auto auto 1fr auto;
  height: 100vh;
  width: 100vw;
  /* The grid is exactly the viewport and only the content row scrolls; clip the
     app itself so it can never become a second scroll source that drags the
     fixed top bar / footer rows out of place. */
  overflow: hidden;
  background: linear-gradient(180deg, var(--bg-page) 0%, var(--bg-deep) 70%);
}

/* ============================================================== TOP BAR */
.topbar {
  display: grid;
  /* Four tracks for the four children (brand · live · topstats · controls).
     The old three-track template dropped the reset button onto an implicit
     second row (audit X8/H1) — a column count bug, not a width issue. */
  grid-template-columns: auto auto 1fr auto;
  gap: 1.4rem;
  align-items: center;
  padding: 0.7rem 1.2rem;
  background: linear-gradient(180deg, var(--header) 0%, var(--panel) 100%);
  border-bottom: 1px solid var(--border);
}
.brand { display: flex; align-items: center; gap: 0.7rem; min-width: 0; }
.brand-mark {
  width: 0.7rem; height: 1.2rem;
  background: linear-gradient(180deg, var(--accent) 0%, transparent 100%);
  display: inline-block;
}
.brand-name {
  font-family: var(--ui); font-weight: 700; font-size: 1.05rem;
  color: var(--text-bright); letter-spacing: 0.02em;
}
.brand-version {
  font-family: var(--mono); font-size: 0.75rem;
  color: var(--muted); padding: 0.05em 0.45em;
  background: var(--surface); border: 1px solid var(--border-soft);
  border-radius: 3px;
}

.live {
  display: flex; align-items: center; gap: 0.5em;
  font-family: var(--mono); font-size: 0.85rem;
}
.live-dot {
  width: 0.55em; height: 0.55em; border-radius: 50%;
  background: var(--muted);
  box-shadow: 0 0 0 0 transparent;
  transition: background 0.2s;
}
.live-dot.ok {
  background: var(--good);
  box-shadow: 0 0 8px rgba(149, 212, 163, 0.6);
  animation: pulse 1.6s ease-in-out infinite;
}
.live-dot.err {
  background: var(--danger);
  box-shadow: 0 0 8px rgba(247, 118, 142, 0.55);
}
.live-dot.paused {
  background: var(--amber);
  box-shadow: 0 0 8px rgba(224, 175, 104, 0.55);
}
.live-dot.idle {
  background: var(--dim);
}
.live-dot.db {
  background: var(--magenta);
  box-shadow: 0 0 8px rgba(131, 103, 163, 0.55);
}
@keyframes pulse {
  0%, 100% { box-shadow: 0 0 8px rgba(79, 157, 106, 0.45); }
  50%      { box-shadow: 0 0 14px rgba(79, 157, 106, 0.85); }
}
.live-text { color: var(--muted); }

.topstats {
  display: flex; justify-content: flex-end;
  gap: 1.6rem; flex-wrap: wrap;
}
.topstat { display: flex; flex-direction: column; align-items: flex-end; line-height: 1.1; cursor: help; }
.topstat .k { font-family: var(--ui); font-size: 0.7rem; color: var(--muted); text-transform: uppercase; letter-spacing: 0.07em; }
.topstat .v { font-family: var(--mono); font-size: 1.05rem; color: var(--text-bright); font-weight: 500; transition: color 0.2s; }
.topstat .v.flash { animation: flash 0.4s ease-out; }
@keyframes flash {
  0%   { color: var(--accent); }
  100% { color: var(--text-bright); }
}

/* ============================================================== TABS */
.tabs {
  display: flex; gap: 0; padding: 0 1.2rem;
  background: var(--panel); border-bottom: 1px solid var(--border);
}
.tab {
  font-family: var(--ui); font-size: 0.85rem; font-weight: 500;
  background: transparent; border: 0; color: var(--muted);
  padding: 0.75rem 1.2rem 0.7rem;
  cursor: pointer; border-bottom: 2px solid transparent;
  letter-spacing: 0.03em; white-space: nowrap;
  display: flex; align-items: center; gap: 0.5em;
  transition: color 0.15s, border-color 0.15s, background 0.15s;
}
.tab:hover { color: var(--text); background: rgba(255, 255, 255, 0.02); }
.tab.active { color: var(--accent); border-bottom-color: var(--accent); }
.tab .ki {
  font-family: var(--mono); font-size: 0.65rem;
  padding: 0.1em 0.4em; border-radius: 2px;
  background: var(--surface); color: var(--dim);
  border: 1px solid var(--border-soft);
}
.tab.active .ki { color: var(--accent); border-color: var(--accent-line); background: var(--accent-soft); }

/* ============================================================== CONTENT */
/* overflow: hidden auto — y scrolls, x is clipped. Setting only overflow-y:auto
   makes the x-axis compute to auto too, which produced a few-px phantom
   horizontal scroll whenever any child sat a hair over the width. */
.content { min-height: 0; overflow: hidden auto; overscroll-behavior: contain; padding: 1rem 1.2rem 4rem; }
.tab-pane { display: none; }
.tab-pane.active { display: block; }

/* ============================================================== OVERLAYS */
.overlay-state {
  position: fixed; inset: 0;
  display: flex; align-items: center; justify-content: center;
  background: rgba(7, 9, 14, 0.92);
  z-index: 50; padding: 2rem;
}
.overlay-inner {
  max-width: 38rem;
  background: var(--panel);
  border: 1px dashed var(--border);
  border-radius: 6px;
  padding: 2rem 2.4rem; text-align: center;
}
.overlay-inner h2 {
  font-weight: 600; font-size: 1.3rem; margin: 0 0 0.5rem; color: var(--text-bright);
}
.overlay-inner p { color: var(--muted); margin: 0.6em 0; }
.overlay-inner p.hint {
  font-size: 0.85em; background: var(--surface); border: 1px solid var(--border-soft);
  border-radius: 3px; padding: 0.6em 0.9em; margin-top: 1.2em; color: var(--text);
}
#disconnected .overlay-inner { border-color: var(--danger); }
";
}
