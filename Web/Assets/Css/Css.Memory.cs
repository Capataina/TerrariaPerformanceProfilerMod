#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Memory tab: only the genuinely-bespoke layout the components don't cover —
    // the page column grid and the two table-column treatments (the RAM bar+figure
    // unit and the slim footprint column). Everything else (summary band, hero
    // strip, legend, table chrome, breakdown drawer) is the shared component
    // library: statGrid/statTile, splitBar/legend, .dtable, cellBar, sectionBlock.
    // The retired bespoke classes (.mem-summary/.mem-stat, .mem-strip/.mem-slice,
    // .mem-table-wrap, .mem-val*, .mem-bd*, .mem-drawer*, .mem-sect*, .mem-card*)
    // moved onto those components.
    private const string CssMemory = @"
/* ===== Memory tab ==================================================== */
.mem-layout { display: flex; flex-direction: column; gap: 1rem; }

/* RAM / scaffold column LEADS the row. The magnitude bar (share of the visible
   total) and its figure sit on one baseline as a single unit — the bar filling
   the space to the left, the number flush right — so bar length tracks RAM size
   as the eye scans the column. Header right-aligns over the figure (.dtable th
   default). Reserve enough width that the bar has room to vary. */
.mem-col-val { white-space: nowrap; min-width: 11rem; }
.mem-val-cell { display: flex; align-items: center; gap: 0.55rem; }
.mem-val-cell .cellbar { flex: 1; min-width: 4rem; height: 0.62rem; }
.mem-val-cell .n { flex: none; font-variant-numeric: tabular-nums; }

/* Footprint composition is SECONDARY: a thin split confined to a slim column so
   it reads as a quiet hint, never the headline. The full breakdown is in the
   drawer on click. Header + cell share the cap so the column stays narrow. */
.mem-col-fp { width: 7rem; }
.mem-col-fp .split-bar { max-width: 6rem; }
";
}
