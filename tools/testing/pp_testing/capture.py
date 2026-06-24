"""L8 capture — screenshot every surface in every state into a clean-slate dir.

Drives the booted dashboard, discovers each tab and each panel within it, and shoots:
  * the whole tab at the real viewport (layout / overflow context),
  * each panel cropped on its own (default state),
  * each scrollable panel scrolled to its extreme (sticky-header / bottom rows),
  * each panel with an interactive target in its HOVER and SELECTED states,
  * the whole tab after clicking the master panel's first target (master -> detail,
    so a click in one panel that fills a detail in another is captured),
  * the mod-card drawer once per tab that offers one.

Interactivity is discovered generically (anything with a pointer cursor or a known
interactive selector — rows, swimlane blocks, donut slices, segmented buttons), so a
bespoke click target is driven without being named. Nothing here is tab-specific: add a
tab and it is swept; add a panel and it is cropped and interacted with.

It writes a manifest.json describing what was shot, the per-page doc to update, and the
rubric/design-bar the review agents apply. The directory is wiped and remade each run.
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

        panels = dash.panels(key)
        icounts = dash.interactive_counts(key)
        panes = []
        seen_slug = {}
        for p in panels:
            i = p["index"]
            base = _slug(p["title"])
            seen_slug[base] = seen_slug.get(base, -1) + 1
            if seen_slug[base]:
                base = "%s-%d" % (base, seen_slug[base])
            shots = {}

            def crop(state, suffix=""):
                png = os.path.join(tdir, "%02d-%s%s.png" % (i, base, suffix))
                if dash.shoot_panel(key, i, png):
                    shots[state] = png

            crop("default")

            if p["hasScroll"] and dash.scroll_panel_to_bottom(key, i):
                crop("scrolled", "--scrolled")

            interactive = i < len(icounts) and icounts[i] > 0
            if interactive:
                if dash.hover_interactive(key, i, 0):
                    crop("hover", "--hover")
                    dash.reset_mouse()
                if dash.click_interactive(key, i, 0) is not None:
                    crop("selected", "--selected")
                    dash.close_drawer()

            panes.append({
                "index": i,
                "title": p["title"],
                "sub": p.get("sub", ""),
                "interactive_targets": icounts[i] if i < len(icounts) else 0,
                "flags": {k: p[k] for k in ("hasScroll", "hasRows", "hasEmpty", "hasChart")},
                "shots": {st: os.path.relpath(pth, out_root) for st, pth in shots.items()},
            })

        # Non-panel sections (the Timeline swimlane / heatstrip / transition track and
        # the like) carry real interactive content with no .panel chrome. Crop each
        # that has a stable selector, and drive its first target's hover + selected
        # state, so a bespoke widget is audited exactly like a panel.
        sections = []
        for s in dash.sections(key):
            shots = {}
            slug = _slug(s["label"])
            if s["sel"]:
                spng = os.path.join(tdir, "sec-%s.png" % slug)
                if dash.shoot_selector(s["sel"], spng):
                    shots["default"] = spng
            if s["targets"] > 0:
                if dash.hover_section(key, s["childIndex"], 0) and s["sel"]:
                    hpng = os.path.join(tdir, "sec-%s--hover.png" % slug)
                    if dash.shoot_selector(s["sel"], hpng):
                        shots["hover"] = hpng
                    dash.reset_mouse()
                if dash.click_section(key, s["childIndex"], 0) is not None and s["sel"]:
                    cpng = os.path.join(tdir, "sec-%s--selected.png" % slug)
                    if dash.shoot_selector(s["sel"], cpng):
                        shots["selected"] = cpng
                    dash.close_drawer()
            sections.append({
                "label": s["label"],
                "selector": s["sel"],
                "interactive_targets": s["targets"],
                "shots": {st: os.path.relpath(pth, out_root) for st, pth in shots.items()},
            })

        # Master -> detail: click the first real data element ANYWHERE in the pane
        # (panel row or bespoke section block) and reshoot the WHOLE tab, so a detail
        # it fills in another panel/section is captured.
        after_click_rel = None
        master_label = None
        dash.focus(key)  # reset any prior selection state on this tab
        clicked = dash.click_master(key)
        if clicked is not None:
            master_label = clicked
            acpng = os.path.join(tdir, "_after-click.png")
            dash.shoot_viewport(acpng)
            after_click_rel = os.path.relpath(acpng, out_root)
            dash.close_drawer()

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
            "after_click": after_click_rel,
            "master_clicked": master_label,
            "drawer": drawer_rel,
            "panes": panes,
            "sections": sections,
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
