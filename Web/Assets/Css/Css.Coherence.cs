#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Loaded LAST among the content stylesheets (just before the scrollbar
    // override). Originally this layer aliased nine per-tab empty-state classes
    // onto one canonical `.empty`. After the component-library migration every
    // surface emits the canonical `.empty` (via emptyState()) directly, so the
    // legacy aliases are gone; what remains is the canonical empty state, the
    // one inline-placeholder variant still emitted by the observatory, and the
    // container-bound discipline for the colour-coded bars/legends.
    private const string CssCoherence = @"
/* ===== Canonical panel-level empty / placeholder state ============== */
.empty {
  display: flex; align-items: center; justify-content: center;
  min-height: 4.5rem;   /* breathing room so a short panel's empty state isn't crushed */
  color: var(--muted); font-family: var(--mono); font-size: 0.82rem;
  font-style: normal; text-align: center; padding: 1.1rem 1rem; line-height: 1.5;
}

/* Inline placeholder (inside a card / table row): muted + de-italicised, but
   kept left-aligned and tight so it reads as part of the row, not a panel.
   Still emitted by the Insights composition bar. */
.comp-empty { color: var(--muted); font-style: normal; font-size: 0.78rem; text-align: left; }

/* ===== Container discipline ========================================= */
/* Keep any colour-coded split bar / legend inside the panel bound at every
   width (the strip's thin trailing slices used to spill past the edge). */
.bar-legend, .split-bar { max-width: 100%; box-sizing: border-box; }
";
}
