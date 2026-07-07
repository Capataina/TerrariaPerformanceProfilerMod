#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // Popup cards (S18) + panel states (S20). New primitives from the
    // 2026-07-07 ui-overhaul wave; both reuse the existing token vocabulary
    // (panel/border/dim) so they read as native chrome, not a bolt-on.
    private const string CssCards = @"
/* =================================================== POPUP CARDS (S18) */
.card-backdrop {
  position: fixed; inset: 0; z-index: 60;
  display: flex; align-items: center; justify-content: center;
  background: rgba(0,0,0,0.55); backdrop-filter: blur(2px);
  animation: card-in 0.12s ease-out;
}
@keyframes card-in { from { opacity: 0; } to { opacity: 1; } }
.popup-card {
  background: var(--panel); border: 1px solid var(--border);
  border-radius: 10px; min-width: 22rem; max-width: 34rem;
  max-height: 80vh; overflow-y: auto;
  box-shadow: 0 12px 40px rgba(0,0,0,0.5);
  animation: card-pop 0.14s ease-out;
}
@keyframes card-pop { from { transform: scale(0.97); } to { transform: scale(1); } }
.popup-card .card-h {
  display: flex; align-items: baseline; gap: 0.6rem;
  padding: 0.8rem 1rem; border-bottom: 1px solid var(--border);
}
.popup-card .card-title { font-weight: 700; font-size: 1rem; }
.popup-card .card-meta { color: var(--dim); font-size: 0.75rem; flex: 1; }
.popup-card .card-close {
  cursor: pointer; color: var(--dim); font-size: 1.1rem; line-height: 1;
}
.popup-card .card-close:hover { color: var(--text); }
.popup-card .card-body { padding: 0.8rem 1rem 1rem; }
.popup-card .card-sect {
  margin: 0.9rem 0 0.4rem; font-size: 0.7rem; letter-spacing: 0.06em;
  text-transform: uppercase; color: var(--dim);
  border-bottom: 1px solid var(--border-soft); padding-bottom: 0.25rem;
}
.popup-card .card-modrow {
  display: flex; align-items: center; gap: 0.5rem;
  font-family: var(--mono); font-size: 0.8rem; padding: 0.2rem 0;
}
.popup-card .card-modrow .nm { flex: 1; }
.popup-card .card-modrow .val { color: var(--dim); }

/* ================================================ PANEL STATES (S20) */
.pstate { display: flex; flex-direction: column; gap: 0.3rem; align-items: center; }
.pstate-tag {
  font-size: 0.65rem; letter-spacing: 0.08em; text-transform: uppercase;
  border: 1px solid var(--border-soft); border-radius: 4px;
  padding: 0 0.5em; color: var(--muted);
}
.pstate-warming .pstate-tag { color: var(--amber); border-color: var(--amber); opacity: 0.7; }
.pstate-detail { font-size: 0.75rem; color: var(--muted); }

/* S04 memory-guard strips on the Self tab. */
.self-trend-spark { height: 1.4rem; margin: 0.3rem 0; }
.self-trend-spark .spark-svg { height: 100%; width: 100%; }
.self-arms { margin-top: 0.4rem; border-top: 1px dashed var(--border-soft); padding-top: 0.3rem; }

/* Scroll-edge affordance (audit I3/M2): a fade at the trailing edge of a
   scrollable region signals 'there is more' without a visible scrollbar. */
.scroll-fade-x { position: relative; }
.scroll-fade-x::after {
  content: ''; position: absolute; top: 0; right: 0; bottom: 0; width: 2.5rem;
  background: linear-gradient(90deg, transparent, var(--bg)); pointer-events: none;
}
";
}
