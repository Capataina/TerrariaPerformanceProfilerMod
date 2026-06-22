#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Shared readable-vocabulary components: the small, disciplined set of
    // shapes every surface composes from instead of inventing a bespoke chart
    // per panel. Colour-coded split bars, dense perf-tinted tables, rectangular
    // heatmaps, chips, and stat lines. Defined once so every tab speaks the same
    // visual language. Loaded after the shell (palette + layout vars exist) and
    // before the per-tab CSS (tabs can override locally).
    private const string CssComponents = @"
/* ===== Split bar: colour-coded stacked composition =================== */
.split-bar { display: flex; width: 100%; height: 0.7rem; background: var(--surface);
  border-radius: 2px; overflow: hidden; }
.split-bar > span { display: block; height: 100%; min-width: 1px; transition: width 0.3s ease-out; }
.split-bar.tall { height: 1.5rem; }
.split-bar.thin { height: 0.4rem; }
.bar-legend { display: flex; flex-wrap: wrap; gap: 0.2rem 0.9rem; margin-top: 0.45rem;
  font-family: var(--mono); font-size: 0.74rem; color: var(--muted); }
.bar-legend .lg { display: inline-flex; align-items: center; gap: 0.35rem; }
.bar-legend .sw { width: 0.6rem; height: 0.6rem; border-radius: 1px; flex: none; }
.bar-legend .lg-v { color: var(--text); font-variant-numeric: tabular-nums; }

/* ===== Data table: dense, sortable, perf-tinted ===================== */
.dtable { width: 100%; border-collapse: collapse; font-family: var(--mono); font-size: 0.82rem; }
.dtable thead th { position: sticky; top: 0; z-index: 1; background: var(--header);
  font-family: var(--ui); font-size: 0.67rem; font-weight: 500; text-transform: uppercase;
  letter-spacing: 0.07em; color: var(--muted); text-align: right;
  padding: 0.4rem 0.55rem; border-bottom: 1px solid var(--border); white-space: nowrap; }
.dtable thead th.l { text-align: left; }
.dtable thead th.sortable { cursor: pointer; user-select: none; }
.dtable thead th.sortable:hover { color: var(--text); }
.dtable thead th.sorted { color: var(--accent); }
.dtable tbody td { padding: 0.32rem 0.55rem; text-align: right; color: var(--text);
  border-bottom: 1px solid var(--border-soft); white-space: nowrap;
  font-variant-numeric: tabular-nums; }
.dtable tbody td.l { text-align: left; }
.dtable tbody tr.clickable { cursor: pointer; }
.dtable tbody tr:hover { background: var(--hover); }
.dtable tbody tr.sel { background: var(--accent-soft); box-shadow: inset 2px 0 0 var(--accent); }
.dtable td.muted, .dtable .muted { color: var(--muted); }
.dtable td.dim, .dtable .dim { color: var(--dim); }

/* Perf tint for a value (good -> bad). */
.t0 { color: var(--perf-0); } .t1 { color: var(--perf-1); } .t2 { color: var(--perf-2); }
.t3 { color: var(--perf-3); } .t4 { color: var(--perf-4); }

/* Inline cell bar (width set inline by the renderer). */
.cellbar { display: inline-block; vertical-align: middle; width: 100%; min-width: 2.5rem;
  height: 0.45rem; background: var(--surface); border-radius: 1px; overflow: hidden; }
.cellbar > span { display: block; height: 100%; background: var(--cpu); }

/* ===== Rectangular heatmap: 2D categorical grid ===================== */
.rheat { display: grid; gap: 2px; font-family: var(--mono); }
.rheat .rh-corner { background: transparent; }
.rheat .rh-col { color: var(--muted); font-size: 0.68rem; text-align: center;
  padding: 0.25rem 0.2rem; align-self: end; }
.rheat .rh-row { color: var(--muted); font-size: 0.72rem; text-align: right;
  padding: 0.2rem 0.4rem; white-space: nowrap; align-self: center; }
.rheat .rh-cell { display: flex; align-items: center; justify-content: center;
  min-height: 1.8rem; min-width: 2.2rem; border-radius: 2px; color: var(--text-bright);
  font-size: 0.78rem; background: var(--surface); font-variant-numeric: tabular-nums; }
.rheat .rh-cell.zero { color: var(--dim); }

/* ===== Chip / tag: small labelled token ============================= */
.chip { display: inline-flex; align-items: center; gap: 0.3rem; font-family: var(--mono);
  font-size: 0.71rem; padding: 0.08rem 0.42rem; border-radius: 3px; background: var(--surface-2);
  color: var(--text); border: 1px solid var(--border-soft); white-space: nowrap; }
.chip .dot { width: 0.5rem; height: 0.5rem; border-radius: 50%; flex: none; }
.chip.good { color: var(--good); border-color: rgba(79,157,106,0.35); }
.chip.warn { color: var(--amber); border-color: rgba(184,138,37,0.35); }
.chip.bad  { color: var(--danger); border-color: rgba(185,78,88,0.35); }
.chip.cool { color: var(--accent); border-color: var(--accent-line); }

/* ===== Stat line: label + value pair ================================ */
.statline { display: flex; justify-content: space-between; align-items: baseline; gap: 1rem;
  font-family: var(--mono); font-size: 0.82rem; padding: 0.22rem 0;
  border-bottom: 1px solid var(--border-soft); }
.statline:last-child { border-bottom: 0; }
.statline .k { color: var(--muted); font-size: 0.76rem; }
.statline .v { color: var(--text); font-variant-numeric: tabular-nums; }
";
}
