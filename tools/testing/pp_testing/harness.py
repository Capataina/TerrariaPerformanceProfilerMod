"""Asset extraction, endpoint discovery, and loopback serving.

Reuses the proven verbatim-string extractor from tools/preview/render.py rather
than re-implementing the C# `@"..."` parsing, so the dashboard the suite tests is
byte-identical to the one render.py / build_preview_html.py produce.

The two things this adds over render.py:
  * endpoint discovery from the JS (`/api/<name>`), so a newly-fetched endpoint is
    found automatically instead of living in render.py's hardcoded API list;
  * a serve() bound to an arbitrary site directory, so the suite can stand up
    several scenario sites (full, edge-*, empty) side by side.
"""
import os
import re
import sys
import threading
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler

# Reuse render.py's extractor (the un-double @"" parser + concat-order reader).
_PREVIEW = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "preview",
)
sys.path.insert(0, _PREVIEW)
import render  # noqa: E402  (path-injected sibling tool)

REPO = render.REPO
ASSETS = os.path.join(REPO, "Web", "Assets")
CONTRACT_FIXTURES = os.path.join(_PREVIEW, "fixtures")  # the committed contract snapshot


def extract_assets():
    """(css, js, html) exactly as DashboardAssets / IndexHtml concatenate them.

    Reads the raw .cs verbatim strings, so it reflects un-built edits — the whole
    point of the harness.
    """
    bodies = render.collect_consts(ASSETS)
    da = render.read(os.path.join(ASSETS, "DashboardAssets.cs"))
    ih = render.read(os.path.join(ASSETS, "IndexHtml.cs"))
    css = "".join(bodies.get(n, "") for n in render.concat_order(da, "Css"))
    js = "".join(bodies.get(n, "") for n in render.concat_order(da, "Js"))
    html = "".join(bodies.get(n, "") for n in render.concat_order(ih, "IndexHtml"))
    return css, js, html


def discover_endpoints(js):
    """Every `/api/<name>` the dashboard JS fetches, deduped and sorted.

    This is the *contract* the frontend actually consumes, discovered from source
    rather than maintained by hand. A new endpoint the JS starts fetching shows up
    here with no edit to this suite.
    """
    eps = set(re.findall(r"/api/([a-z0-9][a-z0-9-]*)", js, re.I))
    # `api` would match the literal string "/api/" with no name; guard anyway.
    eps.discard("api")
    return sorted(eps)


def assemble_site(site_dir, css, js, html, boot_js):
    """Write index.html (+ boot hook) / dashboard.css / dashboard.js into site_dir."""
    os.makedirs(site_dir, exist_ok=True)
    boot_tag = '<script src="/preview-boot.js"></script>'
    if "</body>" in html:
        html = html.replace("</body>", boot_tag + "\n</body>", 1)
    else:
        html = html + boot_tag
    with open(os.path.join(site_dir, "index.html"), "w", encoding="utf-8") as f:
        f.write(html)
    with open(os.path.join(site_dir, "dashboard.css"), "w", encoding="utf-8") as f:
        f.write(css)
    with open(os.path.join(site_dir, "dashboard.js"), "w", encoding="utf-8") as f:
        f.write(js)
    with open(os.path.join(site_dir, "preview-boot.js"), "w", encoding="utf-8") as f:
        f.write(boot_js or "")


def write_api(site_dir, scenario_data):
    """Drop {endpoint: obj} as api/<endpoint>.json under site_dir."""
    import json
    apidir = os.path.join(site_dir, "api")
    os.makedirs(apidir, exist_ok=True)
    for ep, obj in scenario_data.items():
        with open(os.path.join(apidir, ep + ".json"), "w", encoding="utf-8") as f:
            json.dump(obj, f)


def _handler_for(site_dir):
    class Handler(SimpleHTTPRequestHandler):
        def translate_path(self, path):
            path = path.split("?")[0]
            if path.startswith("/api/"):
                return os.path.join(site_dir, "api", path[len("/api/"):] + ".json")
            if path == "/":
                return os.path.join(site_dir, "index.html")
            return os.path.join(site_dir, path.lstrip("/"))

        def log_message(self, *a):
            pass

    return Handler


def serve(site_dir, port):
    """Start a loopback server for site_dir on its own thread; return the server."""
    httpd = ThreadingHTTPServer(("127.0.0.1", port), _handler_for(site_dir))
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    return httpd
