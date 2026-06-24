"""L8 capture — screenshot every surface in every state into a clean-slate dir.

Drives the booted dashboard, discovers each tab and each panel within it, and shoots:
  * the whole tab at the real viewport (layout / overflow context),
  * each panel cropped on its own (default state),
  * each scrollable panel scrolled to its extreme (sticky-header / bottom rows),
  * each panel with a clickable list in its selected state (detail panes, drawers),
  * the mod-card drawer once per tab that offers one.

It writes a manifest.json describing what was shot, the per-page doc to update, and
the rubric/design-bar the review agents apply. The directory is wiped and remade each
run so a capture is always the current truth, never a mix of old and new.

Nothing here is tab-specific: add a tab and it is swept; add a panel and it is cropped.
"""
import json
import os
import re
import shutil
from datetime import datetime


def _slug(s):
    s = re.sub(r"[^a-z0-9]+", "-", (s or "").lower()).strip("-")
    return s or "panel"


def capture(dash, scenario, out_root, doc_dir, rubric_rel, design_bar_rel, report):
    """Run the sweep; return the manifest dict (also written to out_root/manifest.json)."""
    if os.path.isdir(out_root):
        shutil.rmtree(out_root)
    os.makedirs(out_root, exist_ok=True)

    tabs = dash.tabs()
    tab_entries = []
    for t in tabs:
        key, label = t["key"], t["label"]
        dash.focus(key)
        tdir = os.path.join(out_root, key)
        os.makedirs(tdir, exist_ok=True)

        whole = os.path.join(tdir, "_whole.png")
        dash.shoot_viewport(whole)

        panes = []
        seen_slug = {}
        for p in dash.panels(key):
            i = p["index"]
            base = _slug(p["title"])
            seen_slug[base] = seen_slug.get(base, -1) + 1
            if seen_slug[base]:
                base = "%s-%d" % (base, seen_slug[base])
            shots = {}

            default_png = os.path.join(tdir, "%02d-%s.png" % (i, base))
            if dash.shoot_panel(key, i, default_png):
                shots["default"] = default_png

            if p["hasScroll"] and dash.scroll_panel_to_bottom(key, i):
                sc_png = os.path.join(tdir, "%02d-%s--scrolled.png" % (i, base))
                if dash.shoot_panel(key, i, sc_png):
                    shots["scrolled"] = sc_png

            if p["hasRows"] and dash.select_first_row(key, i):
                sel_png = os.path.join(tdir, "%02d-%s--selected.png" % (i, base))
                if dash.shoot_panel(key, i, sel_png):
                    shots["selected"] = sel_png
                dash.close_drawer()

            panes.append({
                "index": i,
                "title": p["title"],
                "sub": p.get("sub", ""),
                "flags": {k: p[k] for k in ("hasScroll", "hasRows", "hasEmpty", "hasChart")},
                "shots": {st: os.path.relpath(pth, out_root) for st, pth in shots.items()},
            })

        drawer_rel = None
        if dash.open_first_drawer(key):
            dpng = os.path.join(tdir, "_drawer.png")
            if dash.shoot_drawer(dpng):
                drawer_rel = os.path.relpath(dpng, out_root)
            dash.close_drawer()

        tab_entries.append({
            "key": key,
            "label": label,
            "doc": os.path.join(doc_dir, key + ".md"),
            "whole": os.path.relpath(whole, out_root),
            "drawer": drawer_rel,
            "panes": panes,
        })

    manifest = {
        "run": datetime.now().strftime("%Y-%m-%dT%H:%M:%S"),
        "generated_by": "tools/testing/pp_testing/capture.py",
        "scenario": scenario,
        "viewport": {"width": dash.width, "height": dash.height, "scale": dash.scale},
        "shots_root": out_root,
        "rubric": rubric_rel,
        "design_bar": design_bar_rel,
        "contract": {
            "covered": report["covered"],
            "fetched_no_fixture": report["fetched_no_fixture"],
            "fixture_no_fetch": report["fixture_no_fetch"],
        },
        "tabs": tab_entries,
    }
    with open(os.path.join(out_root, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)
    return manifest
