#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // The reset control's behaviour (DB rework wave 3, decision E). The top-bar button
    // opens the confirm dialog; a scope choice calls GET /api/reset?scope=... and the
    // continuous poll repaints from the now-cleared store on its next tick. Wired once to
    // the stable dialog element, vanilla-JS to match the rest of the bundle.
    private const string JsReset = @"
// ====== Reset control ================================================
(function bindReset() {
  const btn = document.getElementById('reset-btn');
  const dialog = document.getElementById('reset-dialog');
  const cancel = document.getElementById('reset-cancel');
  const status = document.getElementById('reset-status');
  if (!btn || !dialog) return;

  function open() { status.textContent = ''; setOptsDisabled(false); dialog.classList.remove('hidden'); }
  function close() { dialog.classList.add('hidden'); }
  function setOptsDisabled(v) { dialog.querySelectorAll('.reset-opt').forEach(b => b.disabled = v); }

  function doReset(scope) {
    status.textContent = 'resetting…';
    setOptsDisabled(true);
    fetch('/api/reset?scope=' + encodeURIComponent(scope))
      .then(r => r.json())
      .then(j => {
        if (j && j.ok) {
          status.textContent = scope === 'everything'
            ? 'store reset — everything cleared.'
            : scope === 'rebuild-rollup'
            ? 'lifetime rollup rebuilt from your session history — numbers corrected, nothing deleted.'
            : 'forgot this modlist (' + (j.sessionsCleared || 0) + ' sessions); lifetime history kept.';
          setTimeout(close, 1700);   // the next poll repaints the cleared store
        } else {
          status.textContent = 'reset failed: ' + ((j && j.error) || 'unknown');
          setOptsDisabled(false);
        }
      })
      .catch(err => { status.textContent = 'reset failed: ' + err; setOptsDisabled(false); });
  }

  btn.addEventListener('click', open);
  cancel.addEventListener('click', close);
  document.addEventListener('keydown', e => {
    if (e.key === 'Escape' && !dialog.classList.contains('hidden')) close();
  });
  dialog.addEventListener('click', e => {
    if (e.target === dialog) { close(); return; }       // click the backdrop to dismiss
    const opt = e.target.closest('.reset-opt');
    if (opt && !opt.disabled) doReset(opt.dataset.scope);
  });
})();
";
}
