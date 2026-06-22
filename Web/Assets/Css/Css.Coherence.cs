#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Loaded LAST among the content stylesheets (just before the scrollbar
    // override). The single source of truth for cross-cutting consistency.
    //
    // Each tab was rebuilt independently and invented its own empty-state class
    // with its own colour / size / italic / alignment (.tl-empty, .lag-empty,
    // .comp-empty, .nowlist .empty-line, ...), so "no data" messages looked
    // different on every tab and two could sit side by side looking unrelated.
    // This layer unifies them all onto one canonical treatment, overriding the
    // per-tab definitions by source order (matched specificity, no !important).
    //
    // New surfaces should use the canonical `.empty` class (emptyState() in JS);
    // the legacy names are aliased here so existing markup is coherent until it
    // is migrated. This is the modular fix: empty-state styling now lives in one
    // place, so it cannot drift per-tab again.
    private const string CssCoherence = @"
/* ===== Canonical panel-level empty / placeholder state ============== */
.empty,
.empty-line, .tl-empty, .lag-empty, .lag-hint, .sc-empty, .mx-empty,
.dor-empty, .cc-empty,
.nowlist .empty-line, .ins-detail .empty {
  color: var(--muted); font-family: var(--mono); font-size: 0.82rem;
  font-style: normal; text-align: center; padding: 1.1rem 1rem; line-height: 1.5;
}

/* Inline placeholder (inside a card / table row): muted + de-italicised, but
   kept left-aligned and tight so it reads as part of the row, not a panel. */
.comp-empty { color: var(--muted); font-style: normal; font-size: 0.78rem; text-align: left; }

/* ===== Container discipline ========================================= */
/* Keep the memory strip + any colour-coded legend inside the panel bound at
   every width (the strip's thin trailing slices used to spill past the edge). */
.mem-strip, .bar-legend, .split-bar { max-width: 100%; box-sizing: border-box; }

/* ===== Canonical selected-row accent =============================== */
/* Selection across the list / table surfaces reads with the one signature
   accent (the blue), not a different colour per surface. The observatory
   cards shipped with a green selection bar that did not match the dtable's
   blue .sel rule sitting right beside it; unify them here so 'selected'
   means the same colour everywhere. (The memory strip keeps its inset ring
   — a bar segment needs an all-sides outline, not a left bar.) */
.ins-obs-card.selected { box-shadow: inset 3px 0 0 var(--accent); }
";
}
