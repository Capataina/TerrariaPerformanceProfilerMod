#!/usr/bin/env python3
"""Build a single, self-contained, OFFLINE preview of the dashboard.

Output: design/dashboard-preview.html  — open it directly in any browser (no
build, no game, no server) to see the *latest* UI driven by synthetic data.
Fully interactive: real CSS + real JS (the actual component library), with a
fetch() shim that returns embedded fixture JSON instead of hitting /api/*.

Why this exists: render.py screenshots the dashboard, but you cannot click it.
This produces a live, clickable replica so a CSS/JS change can be eyeballed
instantly. Edit the Web/Assets/*.cs, re-run this, refresh the file.

Pipeline:
  Web/Assets/*.cs  --extract-->  css + js + html (verbatim, reflects un-built edits)
  fixtures/*.json  --embed---->  window.fetch shim returns these for /api/*
  everything       --inline--->  design/dashboard-preview.html (one file)

Data source for fixtures, in order of preference:
  1. tools/preview/fixtures/*.json   (durable, committed snapshot)
  2. /tmp/pp-preview/site/api/*.json  (a prior `render.py --live` capture)
On first run it snapshots (2) -> (1), trimming the huge hooks payload, so later
runs are durable even after /tmp is flushed. Refresh the data any time with
`render.py --live` (game open) then delete tools/preview/fixtures/ and re-run.

Usage:  python3 tools/preview/build_preview_html.py
"""
import os, re, json, sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import render  # reuse the proven .cs verbatim-string extractor

REPO = render.REPO
ASSETS = os.path.join(REPO, "Web", "Assets")
FIX = os.path.join(HERE, "fixtures")            # durable, committed
TMP_API = "/tmp/pp-preview/site/api"            # a prior --live capture
OUT = os.path.join(REPO, "design", "dashboard-preview.html")
HOOK_CAP = 600                                  # keep the top-N hooks; full set is multi-MB


def extract_assets():
    """css, js, html exactly as DashboardAssets/IndexHtml concatenate them."""
    bodies = render.collect_consts(ASSETS)
    da = render.read(os.path.join(ASSETS, "DashboardAssets.cs"))
    ih = render.read(os.path.join(ASSETS, "IndexHtml.cs"))
    css = "".join(bodies.get(n, "") for n in render.concat_order(da, "Css"))
    js = "".join(bodies.get(n, "") for n in render.concat_order(da, "Js"))
    html = "".join(bodies.get(n, "") for n in render.concat_order(ih, "IndexHtml"))
    return css, js, html


def trim_hooks(obj):
    """The /api/hooks payload is one row per installed hook (multi-MB on a
    kitchen-sink modlist). Keep the costliest HOOK_CAP so the preview stays
    small but the Summary hook-tree and Self hook-distribution still populate."""
    if not isinstance(obj, dict):
        return obj
    for k, v in obj.items():
        if isinstance(v, list) and len(v) > HOOK_CAP:
            def cost(x):
                if isinstance(x, dict):
                    return x.get("hookCount") or x.get("cpuMs") or x.get("avgCpuMs") or 0
                return 0
            v.sort(key=cost, reverse=True)
            obj[k] = v[:HOOK_CAP]
    return obj


def load_fixtures():
    """Return {endpoint: parsed-json}. Snapshot /tmp -> repo on first run."""
    os.makedirs(FIX, exist_ok=True)
    have_repo = any(os.path.exists(os.path.join(FIX, ep + ".json")) for ep in render.API)
    src = FIX if have_repo else TMP_API
    data, missing = {}, []
    for ep in render.API:
        p = os.path.join(src, ep + ".json")
        if os.path.exists(p):
            try:
                obj = json.load(open(p, encoding="utf-8"))
            except Exception:
                obj = {}
            if ep == "hooks":
                obj = trim_hooks(obj)
            data[ep] = obj
            # Persist the (trimmed) fixture into the repo for durability.
            if src != FIX:
                json.dump(obj, open(os.path.join(FIX, ep + ".json"), "w", encoding="utf-8"))
        else:
            data[ep] = {}
            missing.append(ep)
    return data, missing, src


def safe_for_script(s):
    """Neutralise </script> so an inlined blob can't close its own tag."""
    return re.sub(r"</(script)", r"<\\/\1", s, flags=re.I)


SHIM = """<script>
/* OFFLINE PREVIEW SHIM — intercept /api/* fetches and serve embedded fixtures.
   This is the ONLY thing that differs from the in-game dashboard; the CSS/JS
   below are the real assets verbatim. */
(function () {
  var orig = window.fetch ? window.fetch.bind(window) : null;
  window.fetch = function (input, init) {
    var FIX = window.__PP_FIXTURES__ || {};   // read at call time, not setup time
    var url = (typeof input === 'string') ? input : (input && input.url) || '';
    var m = url.match(/\\/api\\/([a-z0-9-]+)/i);
    if (m) {
      var body = JSON.stringify(m[1] in FIX ? FIX[m[1]] : {});
      return Promise.resolve(new Response(body, { status: 200, headers: { 'Content-Type': 'application/json' } }));
    }
    if (orig) return orig(input, init);
    return Promise.reject(new Error('offline preview: ' + url));
  };
})();
</script>"""

BOOT = """<script>
/* OFFLINE PREVIEW BOOT — synthetic data has no server, so suppress the
   disconnected / no-world overlays, then warm every tab's cache (each tab's
   gated poll only fires while it is active) so panels are populated before the
   first click. Real tab switching / hover / drawers all work normally after. */
window.addEventListener('load', function () {
  setTimeout(function () {
    ['disconnected', 'empty'].forEach(function (id) {
      var el = document.getElementById(id); if (el) el.classList.add('hidden');
    });
    var pollFns = Object.keys(window).filter(function (k) {
      return /^poll/.test(k) && typeof window[k] === 'function';
    });
    var tabs = ['summary', 'timeline', 'lag', 'insights', 'self', 'memory'];
    tabs.forEach(function (t) {
      try { if (typeof switchTab === 'function') switchTab(t); } catch (e) {}
      pollFns.forEach(function (p) { try { window[p](); } catch (e) {} });
    });
    try { if (typeof switchTab === 'function') switchTab('summary'); } catch (e) {}
    var ov = document.getElementById('empty'); if (ov) ov.classList.add('hidden');
  }, 150);
});
</script>"""


def build():
    css, js, html = extract_assets()
    data, missing, src = load_fixtures()

    fixtures_js = json.dumps(data, separators=(",", ":"))
    fixtures_js = re.sub(r"</(script)", r"<\\/\1", fixtures_js, flags=re.I)
    data_block = "<script>window.__PP_FIXTURES__=" + fixtures_js + ";</script>"

    style_block = "<style>\n" + css + "\n</style>"
    js_block = "<script>\n" + safe_for_script(js) + "\n</script>"

    # Inline the stylesheet where the <link> was (fallback: before </head>).
    # Replacements are passed as functions so backslashes in the CSS/JS/JSON
    # (e.g. \u, \/) are treated literally, not as regex group references.
    html, n_css = re.subn(r'<link[^>]*dashboard\.css[^>]*>', lambda m: style_block, html, flags=re.I)
    if n_css == 0:
        html = re.sub(r'</head>', lambda m: style_block + "\n</head>", html, count=1, flags=re.I)

    # Replace the external <script src=dashboard.js> with shim+data+js+boot
    # (fallback: before </body>).
    bundle = "\n".join([data_block, SHIM, js_block, BOOT])
    html, n_js = re.subn(r'<script[^>]*dashboard\.js[^>]*>\s*</script>', lambda m: bundle, html, flags=re.I)
    if n_js == 0:
        html = re.sub(r'</body>', lambda m: bundle + "\n</body>", html, count=1, flags=re.I)

    banner = ("<!-- GENERATED by tools/preview/build_preview_html.py — do not hand-edit.\n"
              "     Offline synthetic-data replica of the live dashboard. Regenerate after\n"
              "     changing any Web/Assets/*.cs. -->\n")
    html = banner + html

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    open(OUT, "w", encoding="utf-8").write(html)

    print("fixtures from: %s%s" % (src, "" if src == FIX else "  (snapshotted -> %s)" % FIX))
    if missing:
        print("missing fixtures (empty {}): %s" % ", ".join(missing))
    print("css=%dB js=%dB html(in)=%dB data=%dB" % (len(css), len(js), len(html), len(fixtures_js)))
    print("css link inlined=%d, js script replaced=%d" % (n_css, n_js))
    print("wrote %s (%d KB)" % (OUT, os.path.getsize(OUT) // 1024))


if __name__ == "__main__":
    build()
