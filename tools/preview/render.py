#!/usr/bin/env python3
"""Headless preview of the Performance Profiler dashboard for agent visual review.

Pipeline: extract the dashboard assets straight from the C# verbatim-string
constants (so it reflects UN-BUILT edits) -> feed them captured-or-empty /api
data -> serve on loopback -> screenshot the fully-populated page with the
installed Chrome in headless mode. Everything ephemeral lands under /tmp, so it
is flushed on reboot and never tracked.

Why this exists: the only way to see a CSS/JS change used to be build -> reload ->
re-enter the world -> screenshot. This renders the dashboard from the raw .cs
edits with no build and no running game, so visual iteration is a tight loop. The
in-game Build + Reload stays the FINAL check, not every step.

The Chrome-screenshot + static-serve core is project-agnostic; only extract()
knows this project's asset layout (C# string constants concatenated by
DashboardAssets.cs / IndexHtml.cs). For a project whose assets are real .html/
.css/.js files, point Chrome straight at them and skip extract().

Usage (from anywhere):
  python3 tools/preview/render.py            # render current .cs edits, empty /api
  python3 tools/preview/render.py --live     # also curl the running mod's /api/* for real data
  python3 tools/preview/render.py --tabs      # one screenshot per tab instead of one tall printout
Output: /tmp/pp-preview/shots/{full.png | tab-<name>.png}
"""
import os, re, subprocess, threading, argparse, urllib.request
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler

# Repo root = three levels up from this file (<repo>/tools/preview/render.py).
REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = "/tmp/pp-preview"            # ephemeral; flushed on reboot, never tracked
SITE = os.path.join(OUT, "site")
SHOTS = os.path.join(OUT, "shots")
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
LIVE = "http://127.0.0.1:27277"
TABS = ["summary", "timeline", "lag", "insights", "self", "memory"]
API = ["now", "mods", "hooks", "frames", "segments", "spikes", "stalls", "insights",
       "self", "memory", "heatmap", "events", "segment-lifetime",
       "segment-mod-attribution", "transitions", "activity-strip", "attendance",
       "deaths", "chronicle", "lag-clusters", "gc", "lag-density", "gc-causality",
       "lag-rhythm", "mod-observatory", "dormant", "cross-cutting",
       "engagement-cost", "mod-interaction"]

BOOT_FULL = r"""
// Preview-only bootstrap. Reveals every pane and forces all renders so one tall
// screenshot is the whole dashboard; unlocks the fixed-height scroll regions so
// nothing is clipped. Injected by the harness, never shipped in the mod.
(function () {
  function go() {
    document.querySelectorAll('.tab-pane').forEach(function (p) {
      p.classList.add('active'); p.style.display = 'block';
    });
    var s = document.createElement('style');
    s.textContent =
      '.app{height:auto!important}' +
      '.content{overflow:visible!important;height:auto!important;max-height:none!important}' +
      '.modtable,.mem-table-wrap,.lag-table-wrap,.dtable,.events,.nowlist{max-height:none!important;overflow:visible!important}' +
      '.overlay-state{display:none!important}' +
      '.tab-pane{border-top:1px solid #1b2028;padding-top:10px;margin-top:6px}';
    document.head.appendChild(s);
    ['renderSummary', 'renderTimeline', 'renderLag', 'renderInsights', 'renderSelf', 'renderMemory']
      .forEach(function (fn) { try { if (typeof window[fn] === 'function') window[fn](); } catch (e) {} });
    document.title = 'PREVIEW READY';
  }
  setTimeout(go, 1200);
})();
"""

BOOT_TAB = r"""
(function () {
  function go() {
    var t = '%s';
    document.querySelectorAll('.tab-pane').forEach(function (p) {
      var on = p.dataset.pane === t; p.classList.toggle('active', on);
      p.style.display = on ? 'block' : 'none';
    });
    if (typeof window.activeTab !== 'undefined') window.activeTab = t;
    var s = document.createElement('style');
    s.textContent = '.app{height:auto!important}.content{overflow:visible!important;height:auto!important;max-height:none!important}' +
      '.modtable,.mem-table-wrap,.lag-table-wrap,.dtable,.events,.nowlist{max-height:none!important;overflow:visible!important}.overlay-state{display:none!important}';
    document.head.appendChild(s);
    var fn = 'render' + t.charAt(0).toUpperCase() + t.slice(1);
    try { if (typeof window[fn] === 'function') window[fn](); } catch (e) {}
    document.title = 'PREVIEW READY';
  }
  setTimeout(go, 1200);
})();
"""


def read(p):
    with open(p, encoding="utf-8") as f:
        return f.read()


def verbatim_body(src, name):
    """Body of `... string NAME = @"BODY";`, un-doubling "" -> "."""
    m = re.search(r'string\s+' + re.escape(name) + r'\s*=\s*@"', src)
    if not m:
        return None
    i, out = m.end(), []
    while i < len(src):
        c = src[i]
        if c == '"':
            if i + 1 < len(src) and src[i + 1] == '"':
                out.append('"'); i += 2; continue
            break
        out.append(c); i += 1
    return "".join(out)


def collect_consts(assets_dir):
    bodies = {}
    for root, _, files in os.walk(assets_dir):
        for fn in files:
            if fn.endswith(".cs"):
                src = read(os.path.join(root, fn))
                for m in re.finditer(r'private\s+const\s+string\s+(\w+)\s*=\s*@"', src):
                    b = verbatim_body(src, m.group(1))
                    if b is not None:
                        bodies[m.group(1)] = b
    return bodies


def concat_order(src, prop):
    m = re.search(prop + r'\s*=\s*string\.Concat\(([^;]*?)\);', src, re.S)
    if not m:
        return []
    return [n.split("//")[0].strip() for n in m.group(1).split(",") if n.split("//")[0].strip()]


def extract():
    assets = os.path.join(REPO, "Web", "Assets")
    bodies = collect_consts(assets)
    da = read(os.path.join(assets, "DashboardAssets.cs"))
    ih = read(os.path.join(assets, "IndexHtml.cs"))
    css = "".join(bodies.get(n, "") for n in concat_order(da, "Css"))
    js = "".join(bodies.get(n, "") for n in concat_order(da, "Js"))
    html = "".join(bodies.get(n, "") for n in concat_order(ih, "IndexHtml"))
    os.makedirs(SITE, exist_ok=True)
    boot = '<script src="/preview-boot.js"></script>'
    html = html.replace("</body>", boot + "\n</body>", 1) if "</body>" in html else html + boot
    open(os.path.join(SITE, "index.html"), "w", encoding="utf-8").write(html)
    open(os.path.join(SITE, "dashboard.css"), "w", encoding="utf-8").write(css)
    open(os.path.join(SITE, "dashboard.js"), "w", encoding="utf-8").write(js)
    return len(css), len(js), len(html)


def capture_data(live):
    apidir = os.path.join(SITE, "api")
    os.makedirs(apidir, exist_ok=True)
    got = 0
    if live:
        for ep in API:
            try:
                with urllib.request.urlopen(LIVE + "/api/" + ep, timeout=3) as r:
                    open(os.path.join(apidir, ep + ".json"), "wb").write(r.read())
                    got += 1
            except Exception:
                pass
    return got


class Handler(SimpleHTTPRequestHandler):
    def translate_path(self, path):
        path = path.split("?")[0]
        if path.startswith("/api/"):
            return os.path.join(SITE, "api", path[len("/api/"):] + ".json")
        if path == "/":
            return os.path.join(SITE, "index.html")
        return os.path.join(SITE, path.lstrip("/"))

    def log_message(self, *a):
        pass


def serve(port):
    httpd = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    return httpd


def shoot(url, out_png, w, h, budget=4500):
    """Chrome --headless=new writes the PNG then sometimes fails to exit; kill it
    after the screenshot lands and report success on file existence, not exit."""
    os.makedirs(os.path.dirname(out_png), exist_ok=True)
    if os.path.exists(out_png):
        os.remove(out_png)
    try:
        subprocess.run([
            CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars",
            "--force-color-profile=srgb", "--user-data-dir=" + os.path.join(OUT, "chrome-profile"),
            "--virtual-time-budget=%d" % budget, "--window-size=%d,%d" % (w, h),
            "--screenshot=" + out_png, url,
        ], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30)
    except subprocess.TimeoutExpired:
        pass
    return os.path.exists(out_png)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--live", action="store_true", help="curl the running mod for real /api data")
    ap.add_argument("--tabs", action="store_true", help="one screenshot per tab")
    ap.add_argument("--port", type=int, default=27288)
    ap.add_argument("--width", type=int, default=1600)
    ap.add_argument("--height", type=int, default=5200)
    a = ap.parse_args()

    c, j, h = extract()
    print("extracted: css=%dB js=%dB html=%dB" % (c, j, h))
    n = capture_data(a.live)
    print("api fixtures: %d/%d (%s)" % (n, len(API), "live" if a.live else "empty states; pass --live with game open"))
    httpd = serve(a.port)
    base = "http://127.0.0.1:%d/" % a.port
    results = []
    try:
        if a.tabs:
            for t in TABS:
                open(os.path.join(SITE, "preview-boot.js"), "w").write(BOOT_TAB % t)
                results.append((t, shoot(base + "?t=" + t, os.path.join(SHOTS, "tab-%s.png" % t), a.width, a.height)))
        else:
            open(os.path.join(SITE, "preview-boot.js"), "w").write(BOOT_FULL)
            results.append(("full", shoot(base, os.path.join(SHOTS, "full.png"), a.width, a.height)))
    finally:
        httpd.shutdown()
    for name, ok in results:
        png = ("tab-%s.png" % name) if a.tabs else "full.png"
        print(("OK   " if ok else "FAIL ") + os.path.join(SHOTS, png))


if __name__ == "__main__":
    main()
