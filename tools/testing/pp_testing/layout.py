"""L4 — deterministic interaction & layout invariants.

These are the true/false properties a machine can assert, encoding the bug classes
the static screenshot harness kept missing (text leaking past a sticky header, a chip
off-screen, a scroll region that does not cap, selection with no feedback). They run
against a frozen scenario so a failure is a real regression, not a data shift.

Every invariant iterates the *discovered* panes; none names a tab. The split with L8
is the plan's rule: if a finding can be an assertion it lives here; if it needs an eye
it lives in the audit.
"""
TOL = 2.0  # px slack for sub-pixel rounding

# Page-level: nothing should force a horizontal scrollbar on the whole document.
_PAGE_OVERFLOW_JS = r"""(tol) => {
  const d = document.documentElement;
  const over = d.scrollWidth - window.innerWidth;
  return over > tol
    ? [{ invariant: 'no-page-horizontal-scroll', detail: 'document scrollWidth ' +
         d.scrollWidth + 'px exceeds viewport ' + window.innerWidth + 'px by ' +
         Math.round(over) + 'px' }]
    : [];
}"""

# Per-pane: scroll-regions / tables whose content is clipped wider than their box.
_PANE_OVERFLOW_JS = r"""([key, tol]) => {
  const pane = document.querySelector('.tab-pane[data-pane="' + key + '"]');
  if (!pane) return [];
  const out = [];
  pane.querySelectorAll('.scroll-region, .dtable, table, .rowlist').forEach(el => {
    if (el.clientWidth === 0) return;
    const over = el.scrollWidth - el.clientWidth;
    const ox = getComputedStyle(el).overflowX;
    if (over > tol && (ox === 'hidden' || ox === 'clip' || ox === 'auto')) {
      const panel = el.closest('.panel');
      out.push({ invariant: 'no-horizontal-content-overflow',
        panel: (panel?.querySelector('.panel-title')?.textContent || '').trim(),
        detail: (el.className || el.tagName) + ' content ' + el.scrollWidth +
                'px overflows its ' + el.clientWidth + 'px box by ' + Math.round(over) + 'px' });
    }
  });
  return out;
}"""

# Per-pane: scroll a region to the bottom; the sticky header must (a) stay pinned at
# the top of the region and (b) be OPAQUE so rows scrolling under it cannot bleed
# through. The bleed bug (the dormant-leak class) is a *paint* property, not geometry:
# rows sliding under an opaque, stacked header is normal and correct, so we check the
# header's background alpha — the actual cause — not raw box overlap.
_STICKY_JS = r"""([key, tol]) => {
  const pane = document.querySelector('.tab-pane[data-pane="' + key + '"]');
  if (!pane) return [];
  const out = [];
  function alphaOf(c) {
    c = (c || '').trim();
    if (c === 'transparent' || c === '') return 0;
    const rgba = c.match(/rgba?\(([^)]*)\)/i);
    if (rgba) { const p = rgba[1].split(/[,\s/]+/).filter(Boolean); return p.length >= 4 ? parseFloat(p[3]) : 1; }
    const slash = c.match(/\/\s*([0-9.]+%?)\s*\)?$/);   // oklch/lab/hsl ... / alpha
    if (slash) { return slash[1].endsWith('%') ? parseFloat(slash[1]) / 100 : parseFloat(slash[1]); }
    return 1;  // opaque functional colour with no alpha channel
  }
  pane.querySelectorAll('.scroll-region').forEach(sr => {
    const sticky = [...sr.querySelectorAll('*')].find(e => {
      const p = getComputedStyle(e).position; return p === 'sticky' || p === 'fixed';
    });
    if (!sticky || sr.scrollHeight - sr.clientHeight < 8) return;  // not scrollable
    sr.scrollTop = sr.scrollHeight;                                // to the extreme
    const sBox = sticky.getBoundingClientRect();
    const rBox = sr.getBoundingClientRect();
    const panel = (sr.closest('.panel')?.querySelector('.panel-title')?.textContent || '').trim();
    if (sBox.top - rBox.top > 24) {  // a broken sticky scrolls fully out (drift ~ hundreds px)
      out.push({ invariant: 'sticky-header-stays-pinned', panel,
        detail: 'sticky header drifted ' + Math.round(sBox.top - rBox.top) + 'px below the region top after scroll' });
    }
    const a = alphaOf(getComputedStyle(sticky).backgroundColor);
    if (a < 0.99) {  // a translucent header lets the rows under it bleed through
      out.push({ invariant: 'sticky-header-opaque', panel,
        detail: 'sticky header background is ' + Math.round(a * 100) + '% opaque; rows scrolling under it can bleed through' });
    }
    sr.scrollTop = 0;
  });
  const seen = new Set();
  return out.filter(v => { const k = v.invariant + v.panel + v.detail; if (seen.has(k)) return false; seen.add(k); return true; });
}"""

# Within each row-direction grid/flex, side-by-side children must share a top
# edge. Catches a vertical-flow margin (or align bug) leaking into a horizontal
# layout — the recurring "first column sits higher than the rest" class.
_ROW_ALIGN_JS = r"""([key, tol]) => {
  const pane = document.querySelector('.tab-pane[data-pane="' + key + '"]');
  if (!pane) return [];
  const out = [];
  pane.querySelectorAll('*').forEach(c => {
    const cs = getComputedStyle(c);
    const disp = cs.display;
    if (disp !== 'grid' && disp !== 'flex' && disp !== 'inline-flex') return;
    if (disp.indexOf('flex') >= 0 && cs.flexDirection.startsWith('column')) return;  // vertical stack: tops legitimately differ
    // Centred / end / baseline alignment makes differing tops INTENTIONAL (e.g.
    // a .row vertically centres a tall body cell against a single-line value), so
    // only column-style grids that should top-align (start/stretch/normal) qualify.
    const ai = cs.alignItems;
    if (ai === 'center' || ai === 'end' || ai === 'flex-end' || ai === 'baseline') return;
    if (c.matches('.row, tr')) return;  // table/list row cells are aligned by their own model
    const kids = [...c.children].filter(k => { const r = k.getBoundingClientRect();
      return r.width > 4 && r.height > 4 && getComputedStyle(k).position !== 'absolute'; });
    if (kids.length < 2) return;
    const b = kids.map(k => k.getBoundingClientRect());
    for (let i = 0; i < b.length; i++) for (let j = i + 1; j < b.length; j++) {
      const a = b[i], d = b[j];
      const yov = Math.min(a.bottom, d.bottom) - Math.max(a.top, d.top);
      const sideBySide = (a.right <= d.left + 2) || (d.right <= a.left + 2);
      // Skip pairs of very different height — one likely spans multiple rows (a
      // grid item spanning 2 rows legitimately starts above its row-neighbours).
      const hRatio = Math.min(a.height, d.height) / Math.max(a.height, d.height);
      if (hRatio >= 0.6 && yov > Math.min(a.height, d.height) * 0.5 && sideBySide && Math.abs(a.top - d.top) > tol + 4) {
        out.push({ invariant: 'row-items-top-aligned',
          detail: 'two side-by-side items in a ' + disp + ' differ in top by ' +
                  Math.round(Math.abs(a.top - d.top)) + 'px (' + ((c.className || c.tagName) + '').slice(0, 28) + ')' });
      }
    }
  });
  const seen = new Set();
  return out.filter(v => { if (seen.has(v.detail)) return false; seen.add(v.detail); return true; }).slice(0, 8);
}"""

# Visible label/chip/badge elements that are not nested must not overlap — the
# transition-track / time-placed-chip collision class.
_OVERLAP_JS = r"""([key]) => {
  const pane = document.querySelector('.tab-pane[data-pane="' + key + '"]');
  if (!pane) return [];
  const els = [...pane.querySelectorAll('.chip, .tx-chip, .tl-segment .lbl, [class*=badge], .bar-row .lbl')]
    .filter(e => { const r = e.getBoundingClientRect(); return r.width > 4 && r.height > 4 && e.offsetParent; });
  const out = [];
  for (let i = 0; i < els.length; i++) for (let j = i + 1; j < els.length; j++) {
    const a = els[i], d = els[j];
    if (a.contains(d) || d.contains(a)) continue;
    const ra = a.getBoundingClientRect(), rd = d.getBoundingClientRect();
    const ox = Math.min(ra.right, rd.right) - Math.max(ra.left, rd.left);
    const oy = Math.min(ra.bottom, rd.bottom) - Math.max(ra.top, rd.top);
    if (ox > 3 && oy > 3 && ox * oy > Math.min(ra.width * ra.height, rd.width * rd.height) * 0.35) {
      out.push({ invariant: 'no-label-overlap',
        detail: '"' + (a.textContent || '').trim().slice(0, 16) + '" overlaps "' + (d.textContent || '').trim().slice(0, 16) + '"' });
    }
  }
  const seen = new Set();
  return out.filter(v => { if (seen.has(v.detail)) return false; seen.add(v.detail); return true; }).slice(0, 6);
}"""

# Per panel: the rendered content should fill a reasonable fraction of a tall
# panel; a mostly-empty panel is dead space. Conservative (only tall panels, and
# panels without a chart/scroll-region that legitimately reserve height).
_DEADSPACE_JS = r"""([key, minFill]) => {
  const pane = document.querySelector('.tab-pane[data-pane="' + key + '"]');
  if (!pane) return [];
  const out = [];
  pane.querySelectorAll('.panel').forEach(p => {
    const body = p.querySelector('.panel-body, .scroll-region') || p;
    if (p.querySelector('svg, canvas, .chart-svg, .scroll-region, .chart-stream, .chart-sankey')) return;  // legit fillers
    const pb = body.getBoundingClientRect();
    if (pb.height < 220) return;
    let lo = Infinity, hi = -Infinity;
    body.querySelectorAll('*').forEach(e => { const r = e.getBoundingClientRect();
      if (r.height > 0 && r.width > 0 && getComputedStyle(e).position !== 'absolute') { lo = Math.min(lo, r.top); hi = Math.max(hi, r.bottom); } });
    if (!isFinite(lo)) return;
    const fill = (hi - lo) / pb.height;
    if (fill < minFill) {
      out.push({ invariant: 'panel-fills-its-height',
        panel: (p.querySelector('.panel-title')?.textContent || '').trim(),
        detail: 'content fills only ' + Math.round(fill * 100) + '% of the ' + Math.round(pb.height) + 'px panel body (dead space)' });
    }
  });
  return out;
}"""

_DRAWER_CLOSED_JS = r"""() => {
  const c = document.getElementById('modcard');
  if (!c) return [];
  const cls = c.className || '';
  if (cls.includes('hidden')) return [];
  const r = c.getBoundingClientRect();
  const onscreen = r.right > 0 && r.left < window.innerWidth && r.width > 0;
  return onscreen
    ? [{ invariant: 'drawer-hidden-when-closed', detail: 'mod card is on-screen without the hidden class at rest' }]
    : [];
}"""


def _selection_feedback(dash, key, panels):
    """Clicking a row in a clickable list must visibly change it (class or style).

    Targets the first NON-selected row and tracks it BY INDEX across the click:
    auto-selecting panels (Observatory picks its top mod) would otherwise hand
    the rule a pre-selected row whose class cannot change (a false positive
    against correct behaviour, caught live 2026-07-07), and reading back a
    different row than the one clicked compares apples to oranges.
    """
    out = []
    pick_js = r"""([key, i]) => {
      const pane = document.querySelector('.tab-pane[data-pane="' + key + '"]');
      const panel = pane && pane.querySelectorAll('.panel')[i];
      const rows = panel ? panel.querySelectorAll('.row.clickable, .dtable tr.clickable, [data-mod]') : [];
      let idx = -1;
      for (let r = 0; r < rows.length; r++) {
        if (!String(rows[r].className).includes('sel')) { idx = r; break; }
      }
      if (idx < 0 && rows.length > 0) idx = 0;
      if (idx < 0) return null;
      const row = rows[idx];
      return { idx, cls: row.className, bg: getComputedStyle(row).backgroundColor };
    }"""
    read_js = r"""([key, i, idx]) => {
      const pane = document.querySelector('.tab-pane[data-pane="' + key + '"]');
      const panel = pane && pane.querySelectorAll('.panel')[i];
      const rows = panel ? panel.querySelectorAll('.row.clickable, .dtable tr.clickable, [data-mod]') : [];
      if (idx >= rows.length) return null;
      const row = rows[idx];
      return { cls: row.className, bg: getComputedStyle(row).backgroundColor,
               drawer: Array.from(document.querySelectorAll('.drawer')).some(d => !d.className.includes('hidden'))
                       || !!document.getElementById('popup-card') };
    }"""
    click_js = r"""([key, i, idx]) => {
      const pane = document.querySelector('.tab-pane[data-pane="' + key + '"]');
      const panel = pane && pane.querySelectorAll('.panel')[i];
      const rows = panel ? panel.querySelectorAll('.row.clickable, .dtable tr.clickable, [data-mod]') : [];
      if (idx >= rows.length) return false;
      rows[idx].scrollIntoView({ block: 'nearest' });
      // dispatchEvent, not .click(): [data-mod] also matches SVG slices
      // (memory strip), and SVGElement has no click() method.
      rows[idx].dispatchEvent(new MouseEvent('click', { bubbles: true }));
      return true;
    }"""
    for p in panels:
        if not p["hasRows"]:
            continue
        before = dash.evaluate(pick_js, [key, p["index"]])
        if before is None:
            continue
        if not dash.evaluate(click_js, [key, p["index"], before["idx"]]):
            continue
        dash.page.wait_for_timeout(250)
        after = dash.evaluate(read_js, [key, p["index"], before["idx"]])
        if after is None:
            continue
        changed = (after["cls"] != before["cls"]) or (after["bg"] != before["bg"]) or after.get("drawer")
        if not changed:
            out.append({"invariant": "selection-has-feedback", "tab": key,
                        "panel": p["title"],
                        "detail": "clicking a row changed neither its class, background, nor opened a drawer"})
        dash.close_drawer()
    return out


def run(dash):
    """Run every invariant across every discovered tab. Returns a result dict."""
    violations = []
    page_over = dash.evaluate(_PAGE_OVERFLOW_JS, TOL)
    violations += page_over

    tabs = dash.tabs()
    n_panels = 0
    for t in tabs:
        key = t["key"]
        dash.focus(key)
        panels = dash.panels(key)
        n_panels += len(panels)
        for v in dash.evaluate(_PANE_OVERFLOW_JS, [key, TOL]):
            v["tab"] = key
            violations.append(v)
        for v in dash.evaluate(_STICKY_JS, [key, TOL]):
            v["tab"] = key
            violations.append(v)
        # New layout-quality invariants (the recurring classes: misaligned
        # columns, overlapping labels, dead space) — caught automatically so they
        # do not have to be hand-found each time the data shape changes.
        for v in dash.evaluate(_ROW_ALIGN_JS, [key, TOL]):
            v["tab"] = key
            violations.append(v)
        for v in dash.evaluate(_OVERLAP_JS, key):
            v["tab"] = key
            violations.append(v)
        for v in dash.evaluate(_DEADSPACE_JS, [key, 0.42]):
            v["tab"] = key
            violations.append(v)
        violations += _selection_feedback(dash, key, panels)
        # Drawer: open one from this tab if it offers a trigger, then assert it
        # closes off-screen.
        if dash.open_first_drawer(key):
            dash.close_drawer()
        for v in dash.evaluate(_DRAWER_CLOSED_JS):
            v["tab"] = key
            violations.append(v)

    return {
        "tabs": [t["key"] for t in tabs],
        "panels_checked": n_panels,
        "violations": violations,
        "passed": len(violations) == 0,
    }
