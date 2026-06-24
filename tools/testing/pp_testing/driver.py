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

    # ---- generic interaction (catches bespoke click targets, not just rows) -

    # Find the LEAF-most interactive elements in a panel: a union of known
    # interactive selectors plus anything the stylesheet gives a pointer cursor
    # (so swimlane blocks, donut slices, legend hits, segmented buttons are all
    # found without being named), keeping only visible leaves in reading order.
    _CANDS_JS = r"""
      const SEL = 'a,button,[role=button],[data-mod],[data-family],[data-key],[data-seg],[data-sort],.clickable,tr.clickable,.slice.hit,.lg.hit,.tl-segment,.twirl';
      const panel = (key, i) => { const p = document.querySelector('.tab-pane[data-pane="' + key + '"]'); return p && p.querySelectorAll('.panel')[i]; };
      function cands(p) {
        if (!p) return [];
        const set = new Set();
        p.querySelectorAll(SEL).forEach(e => set.add(e));
        p.querySelectorAll('*').forEach(e => { try { if (getComputedStyle(e).cursor === 'pointer') set.add(e); } catch (_) {} });
        let arr = [...set].filter(e => { const r = e.getBoundingClientRect(); return r.width > 4 && r.height > 4 && e.offsetParent !== null; });
        // Prefer leaves: drop any candidate that contains another candidate.
        arr = arr.filter(e => !arr.some(o => o !== e && e.contains(o)));
        arr.sort((a, b) => { const ra = a.getBoundingClientRect(), rb = b.getBoundingClientRect(); return (ra.top - rb.top) || (ra.left - rb.left); });
        return arr;
      }
    """

    def interactive_counts(self, key):
        """Per-panel count of interactive targets — used to pick the 'master' panel."""
        return self.page.evaluate(
            self._CANDS_JS + r"""
            (key) => { const pane = document.querySelector('.tab-pane[data-pane="' + key + '"]');
              if (!pane) return [];
              return [...pane.querySelectorAll('.panel')].map((_, i) => cands(panel(key, i)).length); }
            """, key)

    def click_interactive(self, key, index, n=0):
        """Click the nth interactive target in a panel; returns a short label or None.

        Uses a synthetic, bubbling click event rather than el.click() so it works on
        SVG targets too (donut slices, chart marks), which have no .click() method.
        """
        try:
            label = self.page.evaluate(
                self._CANDS_JS + r"""
                ([key, i, n]) => { const c = cands(panel(key, i)); if (c.length <= n) return null;
                  const el = c[n]; try { el.scrollIntoView({block:'nearest'}); } catch (_) {}
                  const t = ((el.textContent || el.getAttribute('data-key') || el.tagName || '') + '').trim().slice(0, 40);
                  if (typeof el.click === 'function') el.click();
                  else el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window }));
                  return t || (el.tagName || 'el'); }
                """, [key, index, n])
        except Exception:
            return None
        if label is not None:
            self.page.wait_for_timeout(220)
        return label

    def hover_interactive(self, key, index, n=0):
        """Hover the nth interactive target; returns True if a hoverable target was found."""
        try:
            box = self.page.evaluate(
                self._CANDS_JS + r"""
                ([key, i, n]) => { const c = cands(panel(key, i)); if (c.length <= n) return null;
                  const el = c[n]; el.scrollIntoView({block:'nearest'});
                  const r = el.getBoundingClientRect();
                  return { x: r.left + r.width / 2, y: r.top + Math.min(r.height / 2, 12) }; }
                """, [key, index, n])
            if not box:
                return False
            self.page.mouse.move(box["x"], box["y"])
            self.page.wait_for_timeout(180)
            return True
        except Exception:
            return False

    def reset_mouse(self):
        try:
            self.page.mouse.move(2, 2)
        except Exception:
            pass

    # ---- non-panel sections (bespoke blocks outside .panel, e.g. the swimlane) -

    # Many panes mix .panel components with bespoke sections directly in the pane
    # (the Timeline swimlane / heatstrip / transition track). Those carry real,
    # interactive content but no .panel chrome, so panel-scoped discovery misses
    # them entirely. This finds them generically: the non-panel content blocks of
    # the pane's content root, labelled and given a stable selector when possible.
    _SECTIONS_JS = r"""(key) => {
      const pane = document.querySelector('.tab-pane[data-pane="' + key + '"]');
      if (!pane) return [];
      let root = pane;
      const kids = [...pane.children].filter(c => c.getBoundingClientRect().height > 0);
      if (kids.length === 1 && !kids[0].matches('.panel')) root = kids[0];  // unwrap a shell
      const ctrl = '.segctl button,[data-seg],[data-sort],[data-tlfilter],.twirl';
      const DATA = 'a,button,[data-mod],[data-family],[data-key],.clickable,.tl-segment,.slice.hit,.lg.hit';
      const out = [];
      [...root.children].forEach((c, idx) => {
        if (c.matches('.panel') || c.querySelector('.panel')) return;     // panels handled elsewhere
        const r = c.getBoundingClientRect();
        if (r.height < 24 || r.width < 60) return;                        // skip slivers / control bars
        let label = (c.querySelector('.section-h, h2, h3')?.textContent || '').trim();
        if (!label) label = ((c.id || (c.className || '').split(/\s+/)[0] || 'section') + '')
                              .replace(/^tl-/, '').replace(/[-_]/g, ' ').trim();
        let sel = '';
        if (c.id) sel = '#' + CSS.escape(c.id);
        else { const cl = (c.className || '').split(/\s+/)[0];
               if (cl && document.querySelectorAll('.' + CSS.escape(cl)).length === 1) sel = '.' + CSS.escape(cl); }
        let targets = 0;
        c.querySelectorAll(DATA).forEach(e => { if (!e.closest(ctrl)) { const rr = e.getBoundingClientRect();
          if (rr.width > 4 && rr.height > 4 && e.offsetParent !== null) targets++; } });
        out.push({ childIndex: idx, id: c.id || '', label, sel,
                   hasScroll: !!c.querySelector('.scroll-region'), targets });
      });
      return out;
    }"""

    def sections(self, key):
        return self.page.evaluate(self._SECTIONS_JS, key)

    def shoot_selector(self, sel, path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        try:
            self.page.locator(sel).first.screenshot(path=path, timeout=4000)
            return True
        except Exception:
            return False

    # Find the first data target inside the section at root.children[sidx], then
    # either return its hover point (mode 'box') or click it (mode 'click').
    _SECTION_TGT_JS = r"""([key, sidx, n, mode]) => {
      const pane = document.querySelector('.tab-pane[data-pane="' + key + '"]');
      if (!pane) return null;
      let root = pane;
      const kids = [...pane.children].filter(c => c.getBoundingClientRect().height > 0);
      if (kids.length === 1 && !kids[0].matches('.panel')) root = kids[0];
      const tgt = root.children[sidx];
      if (!tgt) return null;
      const ctrl = '.segctl button,[data-seg],[data-sort],[data-tlfilter],.twirl';
      const DATA = 'a,button,[data-mod],[data-family],[data-key],.clickable,.tl-segment,.slice.hit,.lg.hit';
      const cands = [...tgt.querySelectorAll(DATA)].filter(e => !e.closest(ctrl) && e.offsetParent !== null
        && e.getBoundingClientRect().width > 4 && e.getBoundingClientRect().height > 4);
      if (cands.length <= n) return null;
      const el = cands[n];
      try { el.scrollIntoView({ block: 'nearest' }); } catch (_) {}
      const r = el.getBoundingClientRect();
      if (mode === 'box') return { x: r.left + r.width / 2, y: r.top + Math.min(r.height / 2, 12) };
      if (typeof el.click === 'function') el.click();
      else el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window }));
      return ((el.textContent || el.getAttribute('data-key') || el.tagName || '') + '').trim().slice(0, 40) || 'el';
    }"""

    def click_section(self, key, child_index, n=0):
        try:
            label = self.page.evaluate(self._SECTION_TGT_JS, [key, child_index, n, "click"])
        except Exception:
            return None
        if label is not None:
            self.page.wait_for_timeout(220)
        return label

    def hover_section(self, key, child_index, n=0):
        try:
            box = self.page.evaluate(self._SECTION_TGT_JS, [key, child_index, n, "box"])
            if not box:
                return False
            self.page.mouse.move(box["x"], box["y"])
            self.page.wait_for_timeout(180)
            return True
        except Exception:
            return False

    def click_master(self, key):
        """Click the first real data element anywhere in the pane (skipping controls),
        so a master->detail interaction fires regardless of whether the master is a
        panel row or a bespoke section block. Returns a short label or None."""
        try:
            return self.page.evaluate(r"""(key) => {
              const pane = document.querySelector('.tab-pane[data-pane="' + key + '"]');
              if (!pane) return null;
              const SEL = '.tl-segment,.row.clickable,.dtable tr.clickable,[data-mod],.slice.hit';
              const ctrl = '.segctl button,[data-seg],[data-sort],[data-tlfilter]';
              const el = [...pane.querySelectorAll(SEL)].find(e => !e.closest(ctrl)
                && e.offsetParent !== null && e.getBoundingClientRect().width > 4);
              if (!el) return null;
              try { el.scrollIntoView({ block: 'nearest' }); } catch (_) {}
              const t = ((el.textContent || el.getAttribute('data-key') || el.tagName || '') + '').trim().slice(0, 40);
              if (typeof el.click === 'function') el.click();
              else el.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, view: window }));
              return t || 'el';
            }""", key)
        except Exception:
            return None

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
