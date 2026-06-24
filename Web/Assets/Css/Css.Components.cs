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
/* Sticky header sits at the very top of the scroll region. z-index 3 + an opaque
   surface (and a matching cover on thead) so rows scrolling underneath can never
   bleed through or above it. A box-shadow seam hides any sub-pixel gap at the top. */
.dtable thead { background: var(--header); }
.dtable thead th { position: sticky; top: 0; z-index: 3; background: var(--header);
  font-family: var(--ui); font-size: 0.67rem; font-weight: 500; text-transform: uppercase;
  letter-spacing: 0.07em; color: var(--muted); text-align: right;
  padding: 0.4rem 0.55rem; border-bottom: 1px solid var(--border); white-space: nowrap;
  box-shadow: 0 -1px 0 var(--header); }
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

/* ===== Panel body: THE single padded slot ========================== */
/* Every panel child insets identically here, so nothing sits flush to the
   border and no pane invents its own gutter maths (the strip-leak class). */
.panel-body { padding: 0.7rem 0.9rem; min-height: 0; }
.panel-body.flush { padding: 0; }
.panel-body.tight { padding: 0.4rem 0.6rem; }
/* A panel whose body must fill + scroll: the body is the flex child that grows
   and the ScrollRegion inside it owns the overflow. */
.panel.fill { flex: 1 1 auto; }

/* ===== Scroll region: bounded overflow + poll-stable scroll ======== */
/* The ONE scroll primitive. Callers re-render its innerHTML through setHTML()
   so the poll never snaps it to the top. Bound it either by .fill (grows to a
   height-bounded flex parent) or an inline max-height. */
.scroll-region { min-height: 0; overflow: hidden auto; overscroll-behavior: contain; }
.scroll-region.fill { flex: 1 1 auto; }

/* ===== Section header: sub-headers INSIDE a panel body ============= */
/* The canonical small uppercase label every sub-section used to reinvent
   (.dor-h / .cc-h / .lag-section-h / .mem-sect-h / .tl-detail h4 …). */
.section-h { display: flex; align-items: baseline; justify-content: space-between;
  gap: 0.6rem; font-family: var(--ui); font-size: 0.7rem; font-weight: 600;
  text-transform: uppercase; letter-spacing: 0.07em; color: var(--muted);
  margin: 0 0 0.4rem; }
.section-h .section-sub { font-family: var(--mono); font-weight: 400;
  text-transform: none; letter-spacing: 0; color: var(--dim); }
.section-h + .section-h, .section-block + .section-block { margin-top: 0.8rem; }

/* ===== Row / RowList: one clickable-list model ===================== */
/* One hover + selection treatment everywhere: subtle bg + a reserved 2px left
   bar tinted to the accent. No box-shadow edges, no per-pane .selected colour.
   The grid template is passed inline by the renderer (columns vary per list);
   everything else is shared. */
.rowlist { display: flex; flex-direction: column; }
.row { display: grid; align-items: center; gap: 0.5rem; padding: 0.28rem 0.6rem;
  border-left: 2px solid transparent; border-bottom: 1px solid var(--border-soft);
  font-family: var(--mono); font-size: 0.84rem; }
.rowlist .row:last-child { border-bottom: 0; }
.row.clickable { cursor: pointer; user-select: none;
  transition: background 0.12s, border-color 0.12s; }
.row.clickable:hover { background: var(--hover); border-left-color: var(--accent); }
.row.sel { background: var(--accent-soft); border-left-color: var(--accent); }
.row .nm { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--text); }
.row .rk { color: var(--dim); text-align: right; font-size: 0.78rem; }
/* Outlier marker: gold at rest, resolves to the accent on hover (no second bar). */
.row.outlier { border-left-color: var(--amber); }
.row.outlier:hover { border-left-color: var(--accent); }

/* ===== Twirl: the one expand/collapse chevron ===================== */
.twirl { color: var(--muted); font-size: 0.8em; width: 0.8em; flex: none;
  text-align: center; transition: transform 0.15s, color 0.12s; }
.twirl.open, .open > .twirl, .open > * > .twirl { transform: rotate(90deg); }
.clickable:hover > .twirl, .clickable:hover > * > .twirl { color: var(--accent); }

/* ===== Stat tile / grid: label-over-value card (optional bar) ====== */
/* Consolidates .mem-card / .mc-stat / .hero-stat into one tile, and the
   severity tint (.good/.warn/.bad) into ONE definition. */
/* auto-fit (not auto-fill): collapse the empty tracks so a few tiles stretch to
   fill the row instead of clustering left with dead space to the right. */
.stat-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
  gap: 0.5rem; }
.stat-tile { display: flex; flex-direction: column; gap: 0.15rem;
  background: var(--secondary); border: 1px solid var(--border-soft);
  border-radius: 4px; padding: 0.5rem 0.6rem; min-width: 0; }
.stat-tile .k { font-family: var(--ui); font-size: 0.7rem; color: var(--muted);
  text-transform: uppercase; letter-spacing: 0.06em; }
.stat-tile .v { font-family: var(--mono); font-size: 1.15rem; color: var(--text-bright);
  font-variant-numeric: tabular-nums; line-height: 1.15; }
.stat-tile .v.big { font-size: 1.5rem; }
.stat-tile .sub { font-family: var(--mono); font-size: 0.7rem; color: var(--dim); }
.stat-tile .stat-bar { margin-top: 0.3rem; }
/* The ONE severity tint, shared by stat tiles, stat lines and kpi values. */
.v.good, .stat-tile .v.good, .statline .v.good { color: var(--good); }
.v.warn, .stat-tile .v.warn, .statline .v.warn { color: var(--amber); }
.v.bad,  .stat-tile .v.bad,  .statline .v.bad  { color: var(--danger); }
.v.accent { color: var(--accent); }

/* ===== Drawer: the one slide-in detail overlay ==================== */
/* Generalises .modcard. A fixed right-side panel that slides in; its body
   scrolls and is built from section blocks. */
.drawer { position: fixed; top: 0; right: 0; bottom: 0; width: 26rem; max-width: 92vw;
  z-index: 40; display: flex; flex-direction: column;
  background: var(--popover); border-left: 1px solid var(--border);
  box-shadow: -8px 0 24px rgba(0,0,0,0.45);
  transform: translateX(0); transition: transform 0.18s ease-out; }
.drawer.hidden { transform: translateX(100%); pointer-events: none; display: flex !important; }
.drawer-h { display: grid; grid-template-columns: auto 1fr auto; align-items: center;
  gap: 0.6rem; padding: 0.7rem 0.9rem; border-bottom: 1px solid var(--border-soft); }
.drawer-h .dr-close { cursor: pointer; color: var(--muted); font-size: 1.1rem;
  line-height: 1; padding: 0 0.2rem; }
.drawer-h .dr-close:hover { color: var(--text-bright); }
.drawer-h .dr-title { font-family: var(--ui); font-weight: 600; color: var(--text-bright);
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.drawer-h .dr-meta { font-family: var(--mono); font-size: 0.78rem; color: var(--muted); }
.drawer-body { flex: 1 1 auto; min-height: 0; overflow: hidden auto;
  padding: 0.8rem 0.9rem; }
.section-block { margin-bottom: 0.9rem; }

/* ===== Callout: bordered info / warn / danger box ================= */
.callout { border: 1px solid var(--accent-line); background: var(--accent-soft);
  border-radius: 4px; padding: 0.5rem 0.7rem; font-family: var(--ui);
  font-size: 0.82rem; color: var(--text); line-height: 1.4; }
.callout strong { color: var(--accent); }
.callout.warn { border-color: rgba(184,138,37,0.4); background: rgba(184,138,37,0.08); }
.callout.warn strong { color: var(--amber); }
.callout.bad { border-color: rgba(185,78,88,0.4); background: rgba(185,78,88,0.08); }
.callout.bad strong { color: var(--danger); }

/* ===== Legend variants (one legend, two layouts) ================== */
/* .bar-legend (above) is the inline default; .stack is the vertical scrollable
   form the donut used, .inline a wider-gap row the heatmap used. */
.bar-legend.stack { flex-direction: column; flex-wrap: nowrap; gap: 0.25rem; }
.bar-legend.inline { gap: 0.4rem 1.2rem; }

/* ===== Sentence-case rule for descriptive text ===================== */
/* Eyebrow / labels keep their uppercase treatment (they carry acronyms like
   CPU / GC / MS); this lifts the lowercase PROSE — empty states, callouts,
   captions, stat sub-captions — to sentence case so the UI no longer reads as
   a wall of lowercase. */
.empty::first-letter, .callout::first-letter, .ins-caption::first-letter,
.comp-empty::first-letter, .stat-tile .sub::first-letter,
.bar-row .lbl::first-letter { text-transform: uppercase; }
";
}
