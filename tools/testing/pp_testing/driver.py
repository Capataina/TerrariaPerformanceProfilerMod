"""Playwright page boot + generic DOM discovery — the substrate L4 and L8 share.

Everything here discovers structure from the rendered page; nothing is hardcoded:
  * tabs come from `.tab[data-tab]`,
  * panes from `.tab-pane[data-pane]`,
  * panels from `.panel` within the active pane,
  * poll functions from `window` keys matching /^poll/.

So a new tab/pane/panel is driven, screenshotted, and asserted with no change here.
"""
import os

from . import harness

# Point Playwright at the browser installed inside this suite's venv (see README),
# so the suite is self-contained and does not depend on a global install.
os.environ.setdefault(
    "PLAYWRIGHT_BROWSERS_PATH",
    os.path.join(harness.REPO, "tools", "testing", ".venv", "ms-playwright"),
)

# In-page boot: hide the no-data overlays, then warm every discovered tab's data by
# switching to it and awaiting every poll fn. Returns the discovered tab keys.
_BOOT_JS = r"""async () => {
  ['disconnected', 'empty'].forEach(id => {
    const e = document.getElementById(id); if (e) e.classList.add('hidden');
  });
  const polls = Object.keys(window).filter(k => /^poll/.test(k) && typeof window[k] === 'function');
  const tabs = [...document.querySelectorAll('.tab[data-tab]')].map(t => t.dataset.tab);
  for (const t of tabs) {
    try { if (typeof switchTab === 'function') switchTab(t); } catch (e) {}
    await Promise.all(polls.map(p => { try { return Promise.resolve(window[p]()); } catch (e) { return null; } }));
  }
  return tabs;
}"""

# Re-warm + re-render a single tab right before we look at it.
_FOCUS_JS = r"""async (key) => {
  const polls = Object.keys(window).filter(k => /^poll/.test(k) && typeof window[k] === 'function');
  try { if (typeof switchTab === 'function') switchTab(key); } catch (e) {}
  await Promise.all(polls.map(p => { try { return Promise.resolve(window[p]()); } catch (e) { return null; } }));
  const fn = 'render' + key.charAt(0).toUpperCase() + key.slice(1);
  try { if (typeof window[fn] === 'function') window[fn](); } catch (e) {}
  return true;
}"""

_TABS_JS = r"""() => [...document.querySelectorAll('.tab[data-tab]')].map(t => ({
  key: t.dataset.tab,
  label: (t.textContent || '').replace(/^\s*\d+\s*/, '').trim() || t.dataset.tab,
}))"""

# Per-panel feature flags drive which states are worth capturing.
_PANELS_JS = r"""(key) => {
  const pane = document.querySelector('.tab-pane[data-pane="' + key + '"]');
  if (!pane) return [];
  return [...pane.querySelectorAll('.panel')].map((p, i) => {
    const title = (p.querySelector('.panel-title')?.textContent || '').trim();
    const sub = (p.querySelector('.panel-sub')?.textContent || '').trim();
    return {
      index: i,
      title: title || ('panel ' + (i + 1)),
      sub,
      hasScroll: !!p.querySelector('.scroll-region'),
      hasRows: !!p.querySelector('.row.clickable, .dtable tr.clickable, [data-mod]'),
      hasEmpty: !!p.querySelector('.empty'),
      hasChart: !!p.querySelector('svg'),
    };
  });
}"""


class Dashboard:
    """A live, booted dashboard page. Use as a context manager."""

    def __init__(self, url, width=1500, height=950, headless=True, scale=2):
        self.url = url
        self.width = width
        self.height = height
        self.headless = headless
        self.scale = scale
        self._pw = self._browser = self._ctx = self.page = None
        self.tab_keys = []

    def __enter__(self):
        from playwright.sync_api import sync_playwright
        self._pw = sync_playwright().start()
        self._browser = self._pw.chromium.launch(headless=self.headless)
        self._ctx = self._browser.new_context(
            viewport={"width": self.width, "height": self.height},
            device_scale_factor=self.scale,
        )
        self.page = self._ctx.new_page()
        self.page.goto(self.url, wait_until="load")
        self.tab_keys = self.page.evaluate(_BOOT_JS)
        self.page.wait_for_timeout(500)
        return self

    def __exit__(self, *exc):
        for closer in (self._ctx, self._browser):
            try:
                if closer:
                    closer.close()
            except Exception:
                pass
        try:
            if self._pw:
                self._pw.stop()
        except Exception:
            pass

    # ---- discovery ----------------------------------------------------------

    def tabs(self):
        return self.page.evaluate(_TABS_JS)

    def panels(self, key):
        return self.page.evaluate(_PANELS_JS, key)

    def focus(self, key):
        """Switch to a tab and (re)render it with warm data."""
        self.page.evaluate(_FOCUS_JS, key)
        self.page.wait_for_timeout(300)

    # ---- locators -----------------------------------------------------------

    def panel_locator(self, key, index):
        return self.page.locator('.tab-pane[data-pane="%s"] .panel' % key).nth(index)

    # ---- interactions (all best-effort; return whether the state was reached) -

    def scroll_panel_to_bottom(self, key, index):
        return bool(self.page.evaluate(
            r"""([key, i]) => {
              const pane = document.querySelector('.tab-pane[data-pane="' + key + '"]');
              const p = pane && pane.querySelectorAll('.panel')[i];
              const sr = p && p.querySelector('.scroll-region');
              if (!sr) return false;
              sr.scrollTop = sr.scrollHeight; return true;
            }""", [key, index]))

    def select_first_row(self, key, index):
        try:
            panel = self.panel_locator(key, index)
            row = panel.locator('.row.clickable, .dtable tr.clickable, [data-mod]').first
            if row.count() == 0:
                return False
            row.scroll_into_view_if_needed(timeout=1500)
            row.click(timeout=1500)
            self.page.wait_for_timeout(200)
            return True
        except Exception:
            return False

    def open_first_drawer(self, key):
        try:
            trigger = self.page.locator('.tab-pane[data-pane="%s"] [data-mod]' % key).first
            if trigger.count() == 0:
                return False
            trigger.scroll_into_view_if_needed(timeout=1500)
            trigger.click(timeout=1500)
            self.page.wait_for_timeout(300)
            card = self.page.locator("#modcard")
            return card.count() > 0 and "hidden" not in (card.get_attribute("class") or "")
        except Exception:
            return False

    def close_drawer(self):
        try:
            self.page.evaluate("() => { if (typeof closeModCard === 'function') closeModCard(); }")
            self.page.wait_for_timeout(150)
        except Exception:
            pass

    # ---- measurement (L4) ---------------------------------------------------

    def evaluate(self, js, arg=None):
        return self.page.evaluate(js, arg)

    # ---- screenshots --------------------------------------------------------

    def shoot_viewport(self, path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        self.page.screenshot(path=path)

    def shoot_panel(self, key, index, path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        try:
            self.panel_locator(key, index).screenshot(path=path, timeout=4000)
            return True
        except Exception:
            return False

    def shoot_drawer(self, path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        try:
            self.page.locator("#modcard").screenshot(path=path, timeout=4000)
            return True
        except Exception:
            return False
