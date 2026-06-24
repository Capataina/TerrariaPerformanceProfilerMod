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

/* Control bar above the per-mod table: a search filter (left) and the footprint
   key (right). It is the first child of the #mem-table scroll region and sticks
   to its top so it stays in view while the 29-row roster scrolls under it. The
   sticky offset (-1px) + opaque surface keep rows from bleeding above it, the
   same trick the .dtable sticky header uses. */
.mem-controls { position: sticky; top: 0; z-index: 4; display: flex; align-items: center;
  justify-content: space-between; gap: 0.8rem; flex-wrap: wrap;
  padding: 0.45rem 0.55rem; background: var(--header); border-bottom: 1px solid var(--border); }
.mem-controls .filter-input { max-width: none; width: 11rem; }
/* Footprint key: the small uppercase label + the role-colour swatches, reusing
   the shared .bar-legend swatch markup. margin-top:0 because it sits inline here,
   not stacked under a bar. */
.mem-fp-key { display: inline-flex; align-items: center; gap: 0.6rem; }
.mem-fp-key .mem-fp-key-lbl { font-family: var(--ui); font-size: 0.67rem; font-weight: 500;
  text-transform: uppercase; letter-spacing: 0.07em; color: var(--muted); }
.mem-fp-key .bar-legend { margin-top: 0; }

/* RAM / scaffold column LEADS the row. The magnitude bar (share of the visible
   total) and its figure sit on one baseline as a single unit — the bar filling
   the space to the left, the number flush right — so bar length tracks RAM size
   as the eye scans the column. Header right-aligns over the figure (.dtable th
   default). Reserve enough width that the bar has room to vary. */
.mem-col-val { white-space: nowrap; min-width: 11rem; }
.mem-val-cell { display: flex; align-items: center; gap: 0.55rem; }
.mem-val-cell .cellbar { flex: 1; min-width: 4rem; height: 0.62rem; }
.mem-val-cell .n { flex: none; font-variant-numeric: tabular-nums; }
/* Floor the filled bar to a visible sliver: below ~10 MB the magnitude fraction
   collapses to 1-2px and reads as no bar at all, losing the encoding exactly
   where the long tail of small mods lives. A 3px minimum keeps a nonzero value
   visible without distorting the larger bars (they are far past 3px already). */
.mem-val-cell .cellbar > span { min-width: 3px; }

/* Footprint composition is SECONDARY: a thin split confined to a slim column so
   it reads as a quiet hint, never the headline. The full breakdown is in the
   drawer on click. Header + cell share the cap so the column stays narrow. */
.mem-col-fp { width: 7rem; }
.mem-col-fp .split-bar { max-width: 6rem; }

/* Strip slice selection: the shared .split-bar carries no selection hook, so the
   renderer tags the active slice with .sel. A bright inset ring + a touch of
   lift marks it without an extra colour (the slice keeps its per-mod hue); the
   bar clips overflow, so the ring reads as a crisp edge inside the strip. The
   per-slice hover-brighten + pointer signals each slice is independently
   clickable, matching the 'click one for its breakdown' affordance in copy. */
#mem-strip .split-bar > span { cursor: pointer; transition: filter 0.12s, box-shadow 0.12s; }
#mem-strip .split-bar > span:hover { filter: brightness(1.12); }
#mem-strip .split-bar > span.sel { box-shadow: inset 0 0 0 2px var(--text-bright); filter: brightness(1.18); }

/* Breakdown empty-state: primes the slot instead of reading as dead space. The
   prompt names the action; the hint names what a selection reveals. Centred,
   compact, muted — honest about being empty without looking unfinished. */
.mem-bd-empty { display: flex; flex-direction: column; align-items: center; justify-content: center;
  gap: 0.3rem; text-align: center; padding: 1.4rem 1rem; color: var(--muted); }
.mem-bd-empty .mem-bd-prompt { font-family: var(--ui); font-size: 0.82rem; color: var(--text); }
.mem-bd-empty .mem-bd-prompt::first-letter { text-transform: uppercase; }
.mem-bd-empty .mem-bd-hint { font-family: var(--ui); font-size: 0.74rem; color: var(--dim);
  max-width: 18rem; line-height: 1.4; }
.mem-bd-empty .mem-bd-hint::first-letter { text-transform: uppercase; }
";
}
