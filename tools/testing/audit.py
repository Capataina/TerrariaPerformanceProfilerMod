#!/usr/bin/env python3
"""Performance Profiler dashboard testing suite — CLI.

Run with the suite's own venv python so Playwright is on the path:

  tools/testing/.venv/bin/python tools/testing/audit.py <command>

Commands
  doctor                 self-check: deps, browser, asset extraction, discovery.
  contract               print the discovered /api contract + fixture coverage/drift.
  gen      [--scenario]  write a scenario's fixtures to a dir (inspect L6 output).
  assert   [--scenario]  L4: drive the dashboard and check layout/interaction invariants.
  capture  [--scenario]  L8: clean-slate screenshot sweep + manifest for the agent audit.
  synthesize             fold per-tab agent findings into the evolving page dossiers.

Scenarios (L6, all generic over the contract): full, edge-long-names, edge-extremes, empty.
See tools/testing/README.md for the full L8 agent-fan-out protocol.
"""
import argparse
import glob
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

from pp_testing import harness, scenarios, site as site_mod, pages  # noqa: E402

SHOTS_ROOT = "/tmp/pp-audit/shots"
FINDINGS_DIR = "/tmp/pp-audit/findings"
RUBRIC_REL = "tools/testing/rubric.md"
DESIGN_BAR_REL = "tools/testing/design-bar.md"


def _contract():
    css, js, html = harness.extract_assets()
    eps = harness.discover_endpoints(js)
    fx = scenarios.load_contract_fixtures()
    return eps, fx, scenarios.contract_report(eps, fx)


def cmd_doctor(a):
    print("== doctor ==")
    ok = True
    try:
        import playwright  # noqa
        print("[ok] playwright importable:", playwright.__version__ if hasattr(playwright, "__version__") else "?")
    except Exception as e:
        ok = False
        print("[FAIL] playwright not importable:", e)
    bp = os.environ.get("PLAYWRIGHT_BROWSERS_PATH",
                        os.path.join(HERE, ".venv", "ms-playwright"))
    has_browser = os.path.isdir(bp) and any("chromium" in d for d in os.listdir(bp)) if os.path.isdir(bp) else False
    print("[%s] chromium under %s" % ("ok" if has_browser else "FAIL", bp))
    ok = ok and has_browser
    try:
        eps, fx, rep = _contract()
        print("[ok] assets extracted; %d endpoints discovered, %d covered by fixtures"
              % (len(eps), len(rep["covered"])))
        if rep["fetched_no_fixture"]:
            print("     note: no fixture for:", ", ".join(rep["fetched_no_fixture"]))
    except Exception as e:
        ok = False
        print("[FAIL] extraction/discovery:", e)
    print("== %s ==" % ("all good" if ok else "problems found"))
    return 0 if ok else 1


def cmd_contract(a):
    eps, fx, rep = _contract()
    print("discovered endpoints (%d): %s" % (len(eps), ", ".join(eps)))
    print("covered by a committed fixture: %d" % len(rep["covered"]))
    if rep["fetched_no_fixture"]:
        print("FETCHED, NO FIXTURE (capture with `render.py --live`): %s"
              % ", ".join(rep["fetched_no_fixture"]))
    if rep["fixture_no_fetch"]:
        print("FIXTURE, NO FETCH (orphan?): %s" % ", ".join(rep["fixture_no_fetch"]))
    print("scenarios available: %s" % ", ".join(scenarios.SCENARIOS))
    return 0


def cmd_gen(a):
    eps, fx, rep = _contract()
    data = scenarios.build_scenario(a.scenario, eps, fx)
    out = a.out or os.path.join("/tmp/pp-audit/gen", a.scenario)
    os.makedirs(out, exist_ok=True)
    for ep, obj in data.items():
        json.dump(obj, open(os.path.join(out, ep + ".json"), "w", encoding="utf-8"))
    print("scenario %r -> %d endpoint fixtures in %s" % (a.scenario, len(data), out))
    return 0


def cmd_assert(a):
    from pp_testing import driver, layout
    httpd, url, site_dir, report, eps = site_mod.stand_up(a.scenario, port=a.port)
    try:
        with driver.Dashboard(url, width=a.width, height=a.height) as dash:
            result = layout.run(dash)
    finally:
        httpd.shutdown()
    print("== L4 layout/interaction assert (scenario: %s) ==" % a.scenario)
    print("tabs: %s" % ", ".join(result["tabs"]))
    print("panels checked: %d" % result["panels_checked"])
    if result["passed"]:
        print("PASS — no invariant violations.")
        return 0
    print("FAIL — %d violation(s):" % len(result["violations"]))
    for v in result["violations"]:
        print("  [%s] %s%s — %s" % (v.get("invariant"), v.get("tab", "-"),
              ("/" + v["panel"]) if v.get("panel") else "", v.get("detail", "")))
    return 1


def cmd_capture(a):
    from pp_testing import driver, capture
    httpd, url, site_dir, report, eps = site_mod.stand_up(a.scenario, port=a.port)
    try:
        with driver.Dashboard(url, width=a.width, height=a.height) as dash:
            manifest = capture.capture(dash, a.scenario, SHOTS_ROOT, pages.PAGES_DIR,
                                       RUBRIC_REL, DESIGN_BAR_REL, report)
    finally:
        httpd.shutdown()
    n_shots = sum(len(p["shots"]) for t in manifest["tabs"] for p in t["panes"]) \
        + sum(1 for t in manifest["tabs"]) + sum(1 for t in manifest["tabs"] if t["drawer"])
    print("== L8 capture (scenario: %s) ==" % a.scenario)
    print("tabs discovered: %d  panes: %d  screenshots: ~%d"
          % (len(manifest["tabs"]), sum(len(t["panes"]) for t in manifest["tabs"]), n_shots))
    print("manifest: %s" % os.path.join(SHOTS_ROOT, "manifest.json"))
    print("\nFan out one review agent per tab; each reads %s + %s + its shots, writes\n"
          "%s/<tab>.json as {\"findings\":[{severity,category,panel,state,title,what,fix}]}.\n"
          "Then run: audit.py synthesize" % (RUBRIC_REL, DESIGN_BAR_REL, FINDINGS_DIR))
    for t in manifest["tabs"]:
        print("  - %-9s shots: %s/%s   doc: %s" % (t["key"], SHOTS_ROOT, t["key"], t["doc"]))
    return 0


def cmd_synthesize(a):
    mpath = a.manifest or os.path.join(SHOTS_ROOT, "manifest.json")
    if not os.path.exists(mpath):
        print("no manifest at %s — run `capture` first." % mpath)
        return 1
    manifest = json.load(open(mpath, encoding="utf-8"))
    fdir = a.findings or FINDINGS_DIR
    findings_by_tab = {}
    for t in manifest["tabs"]:
        fp = os.path.join(fdir, t["key"] + ".json")
        if os.path.exists(fp):
            try:
                findings_by_tab[t["key"]] = json.load(open(fp, encoding="utf-8")).get("findings", [])
            except Exception as e:
                print("warn: bad findings json for %s: %s" % (t["key"], e))
    written = pages.synthesize(manifest, findings_by_tab, pages.PAGES_DIR)
    print("== synthesize -> %s ==" % pages.PAGES_DIR)
    for key, label, doc, opened, npanes in written:
        print("  %-9s %d panes, %d open findings -> %s" % (key, npanes, opened, doc))
    print("index: %s" % os.path.join(pages.PAGES_DIR, "_index.md"))
    return 0


def main():
    ap = argparse.ArgumentParser(description="Performance Profiler dashboard testing suite")
    sub = ap.add_subparsers(dest="cmd", required=True)

    def add_common(p):
        p.add_argument("--scenario", default="full", choices=list(scenarios.SCENARIOS))
        p.add_argument("--port", type=int, default=27299)
        p.add_argument("--width", type=int, default=1500)
        p.add_argument("--height", type=int, default=950)

    sub.add_parser("doctor").set_defaults(fn=cmd_doctor)
    sub.add_parser("contract").set_defaults(fn=cmd_contract)
    g = sub.add_parser("gen"); g.add_argument("--scenario", default="full", choices=list(scenarios.SCENARIOS))
    g.add_argument("--out", default=None); g.set_defaults(fn=cmd_gen)
    aa = sub.add_parser("assert"); add_common(aa); aa.set_defaults(fn=cmd_assert)
    cc = sub.add_parser("capture"); add_common(cc); cc.set_defaults(fn=cmd_capture)
    sy = sub.add_parser("synthesize"); sy.add_argument("--manifest", default=None)
    sy.add_argument("--findings", default=None); sy.set_defaults(fn=cmd_synthesize)

    a = ap.parse_args()
    sys.exit(a.fn(a))


if __name__ == "__main__":
    main()
