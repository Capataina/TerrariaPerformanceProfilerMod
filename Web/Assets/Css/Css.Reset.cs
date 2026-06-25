#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // The reset control (DB rework wave 3): a discreet top-bar button + a centred confirm
    // dialog with two scoped destructive options. Monochrome chrome (the shadcn-neutral
    // tokens); the "everything" option carries a danger tint so the heavier action reads
    // as heavier without shouting.
    private const string CssReset = @"
/* =================================================== RESET CONTROL */
.topbar-reset {
  margin-left: auto; align-self: center;
  font-family: var(--ui); font-size: 0.7rem; font-weight: 600;
  letter-spacing: 0.04em; text-transform: lowercase;
  color: var(--dim); background: transparent;
  border: 1px solid var(--border-soft); border-radius: 6px;
  padding: 0.3rem 0.6rem; cursor: pointer;
  transition: color 0.12s, border-color 0.12s, background 0.12s;
}
.topbar-reset:hover { color: var(--text); border-color: var(--border); background: var(--hover); }

.reset-backdrop {
  position: fixed; inset: 0; z-index: 50;
  display: flex; align-items: center; justify-content: center;
  background: rgba(0,0,0,0.55); backdrop-filter: blur(2px);
}
.reset-backdrop.hidden { display: none; }

.reset-modal {
  width: min(30rem, calc(100vw - 2rem));
  background: var(--card); border: 1px solid var(--border);
  border-radius: 10px; padding: 1.1rem 1.2rem 0.9rem;
  box-shadow: 0 12px 40px rgba(0,0,0,0.5);
}
.reset-modal h2 {
  margin: 0 0 0.3rem; font-family: var(--ui); font-size: 1rem; font-weight: 700; color: var(--text);
}
.reset-sub { margin: 0 0 0.9rem; font-family: var(--ui); font-size: 0.78rem; line-height: 1.45; color: var(--muted); }

.reset-opts { display: flex; flex-direction: column; gap: 0.55rem; }
.reset-opt {
  display: flex; flex-direction: column; gap: 0.2rem; text-align: left;
  background: var(--panel-2); border: 1px solid var(--border-soft);
  border-left: 3px solid var(--muted); border-radius: 6px;
  padding: 0.6rem 0.7rem; cursor: pointer;
  transition: background 0.12s, border-color 0.12s, transform 0.1s ease-out;
}
.reset-opt:hover { background: var(--hover); border-color: var(--border); transform: translateY(-1px); }
.reset-opt:disabled { opacity: 0.5; cursor: default; transform: none; }
.reset-opt.danger { border-left-color: var(--danger); }
.reset-opt.danger:hover { border-color: var(--danger); }
.reset-opt-t { font-family: var(--ui); font-size: 0.85rem; font-weight: 600; color: var(--text); }
.reset-opt.danger .reset-opt-t { color: var(--danger); }
.reset-opt-d { font-family: var(--ui); font-size: 0.72rem; line-height: 1.4; color: var(--dim); }

.reset-foot { display: flex; align-items: center; gap: 0.7rem; margin-top: 0.9rem; }
.reset-cancel {
  font-family: var(--ui); font-size: 0.76rem; color: var(--muted);
  background: transparent; border: 1px solid var(--border-soft); border-radius: 6px;
  padding: 0.35rem 0.8rem; cursor: pointer; transition: color 0.12s, border-color 0.12s;
}
.reset-cancel:hover { color: var(--text); border-color: var(--border); }
.reset-status { font-family: var(--mono); font-size: 0.72rem; color: var(--muted); }
";
}
