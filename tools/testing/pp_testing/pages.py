"""Durable, evolving per-page documents — one dossier per discovered tab.

context/pages/<tab>.md accumulates everything known about a page: discovered panes,
open findings (bugs / improvements / observations), and free-form human notes. The
suite owns the findings (each carries a stable id, so a re-run updates it in place
instead of duplicating); the human owns the Notes section (preserved verbatim).

Findings persist as a JSON block at the foot of the file (the source of truth) and are
rendered to markdown above it. A finding the latest run did not re-report is moved to
"Not seen last run" rather than deleted — a fix closes it, the next agent confirms it.

Adding a 7th tab needs no code change: synthesize() writes a dossier for every tab in
the manifest, creating new ones from a skeleton.
"""
import hashlib
import json
import os
import re

PAGES_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))),
    "context", "pages",
)

_BEGIN = "<!-- PP-AUDIT-DATA"
_END = "PP-AUDIT-DATA -->"
_SEV_ORDER = {"P1": 0, "P2": 1, "P3": 2}


def finding_id(tab, category, panel, what):
    norm = "|".join(re.sub(r"\s+", " ", str(x or "")).strip().lower()
                    for x in (tab, category, panel, str(what)[:80]))
    return hashlib.sha1(norm.encode("utf-8")).hexdigest()[:6]


def _parse(path):
    """Return (store, notes). store = {id: finding}; notes = preserved markdown."""
    if not os.path.exists(path):
        return {}, ""
    text = open(path, encoding="utf-8").read()
    store = {}
    m = re.search(re.escape(_BEGIN) + r"(.*?)" + re.escape(_END), text, re.S)
    if m:
        try:
            store = {f["id"]: f for f in json.loads(m.group(1)).get("findings", [])}
        except Exception:
            store = {}
    nm = re.search(r"##\s+Notes\s*\n(.*?)(?:\n<!-- PP-AUDIT-DATA|\Z)", text, re.S)
    notes = nm.group(1).strip() if nm else ""
    return store, notes


def _render(tab, label, run, scenario, panes, store, notes):
    open_f = [f for f in store.values() if f.get("state_seen") == run]
    stale_f = [f for f in store.values() if f.get("state_seen") != run]
    open_f.sort(key=lambda f: (_SEV_ORDER.get(f.get("severity"), 9), f.get("category", "")))
    stale_f.sort(key=lambda f: (_SEV_ORDER.get(f.get("severity"), 9), f.get("category", "")))

    counts = {}
    for f in open_f:
        counts[f.get("severity", "?")] = counts.get(f.get("severity", "?"), 0) + 1
    badge = " · ".join("%s×%d" % (s, counts[s]) for s in ("P1", "P2", "P3") if counts.get(s)) or "none open"

    L = []
    L.append("---")
    L.append("page: %s" % tab)
    L.append("label: %s" % label)
    L.append("last_audit: %s" % run)
    L.append("scenario: %s" % scenario)
    L.append("open_findings: %s" % badge)
    L.append("---\n")
    L.append("# %s — page dossier\n" % label)
    L.append("> Auto-maintained by the dashboard testing suite (`tools/testing`). Findings "
             "accumulate across audit runs and update in place by stable id. The **Notes** "
             "section is hand-owned and preserved across runs.\n")

    L.append("## Page at a glance")
    L.append("- **Panes discovered (last run):** %s" %
             (", ".join("%s" % p["title"] for p in panes) if panes else "_none_"))
    L.append("- **Scenario audited:** `%s`" % scenario)
    L.append("- **Open findings:** %s\n" % badge)

    L.append("## Open findings")
    if open_f:
        for f in open_f:
            L.append(_render_finding(f))
    else:
        L.append("_No open findings from the latest run._\n")

    if stale_f:
        L.append("## Not seen last run")
        L.append("_Reported by an earlier run but not re-flagged latest — fixed, or simply "
                 "not re-surfaced. Confirm before deleting._\n")
        for f in stale_f:
            L.append(_render_finding(f, stale=True))

    L.append("## Notes")
    L.append((notes.strip() + "\n") if notes.strip() else
             "_Hand-written notes about this page live here and survive audit re-runs._\n")

    data = {"tab": tab, "findings": sorted(store.values(),
                                           key=lambda f: (_SEV_ORDER.get(f.get("severity"), 9), f.get("id")))}
    L.append(_BEGIN)
    L.append(json.dumps(data, indent=1))
    L.append(_END)
    return "\n".join(L) + "\n"


def _render_finding(f, stale=False):
    head = "### [%s] %s · `id:%s`" % (f.get("severity", "?"), f.get("title", "(untitled)"), f.get("id"))
    if stale:
        head += "  _(not seen last run)_"
    lines = [head,
             "- **Category:** %s" % f.get("category", "—"),
             "- **Where:** %s%s" % (f.get("panel", "—"),
                                    (" / %s state" % f["state"]) if f.get("state") else "")]
    if f.get("what"):
        lines.append("- **What:** %s" % f["what"])
    if f.get("fix"):
        lines.append("- **Suggested fix:** %s" % f["fix"])
    lines.append("- **First seen:** %s · **Last seen:** %s\n" %
                 (f.get("first_seen", "—"), f.get("state_seen", "—")))
    return "\n".join(lines)


def upsert(doc_path, tab, label, run, scenario, panes, findings):
    """Merge this run's findings into the page dossier at doc_path."""
    store, notes = _parse(doc_path)
    for raw in findings:
        fid = finding_id(tab, raw.get("category", ""), raw.get("panel", ""), raw.get("what") or raw.get("title", ""))
        prior = store.get(fid, {})
        store[fid] = {
            "id": fid,
            "severity": raw.get("severity", "P3"),
            "category": raw.get("category", "uncategorised"),
            "title": raw.get("title") or (str(raw.get("what", ""))[:70]),
            "panel": raw.get("panel", "page"),
            "state": raw.get("state", ""),
            "what": raw.get("what", ""),
            "fix": raw.get("fix", ""),
            "first_seen": prior.get("first_seen", run),
            "state_seen": run,
        }
    os.makedirs(os.path.dirname(doc_path), exist_ok=True)
    with open(doc_path, "w", encoding="utf-8") as f:
        f.write(_render(tab, label, run, scenario, panes, store, notes))
    return store


def synthesize(manifest, findings_by_tab, pages_dir=PAGES_DIR):
    """Write/refresh a dossier for every discovered tab, plus an index."""
    os.makedirs(pages_dir, exist_ok=True)
    run, scenario = manifest["run"], manifest["scenario"]
    written = []
    for t in manifest["tabs"]:
        key = t["key"]
        doc = os.path.join(pages_dir, key + ".md")
        store = upsert(doc, key, t["label"], run, scenario, t["panes"],
                       findings_by_tab.get(key, []))
        opened = sum(1 for f in store.values() if f.get("state_seen") == run)
        written.append((key, t["label"], doc, opened, len(t["panes"])))
    _write_index(manifest, written, pages_dir)
    return written


def _write_index(manifest, written, pages_dir):
    L = ["# Dashboard page dossiers\n",
         "> Auto-maintained index. One dossier per discovered tab; the suite creates a new "
         "one the first time a tab appears. Generated by `tools/testing`.\n",
         "- **Last audit:** %s  · **scenario:** `%s`  · **viewport:** %d×%d@%dx" % (
             manifest["run"], manifest["scenario"], manifest["viewport"]["width"],
             manifest["viewport"]["height"], manifest["viewport"]["scale"]),
         "- **Tabs discovered:** %d\n" % len(written),
         "| Page | Panes | Open findings (latest) | Dossier |",
         "|---|---|---|---|"]
    for key, label, doc, opened, npanes in written:
        L.append("| %s | %d | %d | [`%s.md`](%s.md) |" % (label, npanes, opened, key, key))
    cov = manifest["contract"]
    L.append("\n## Contract coverage")
    L.append("- **Endpoints covered by a fixture:** %d" % len(cov["covered"]))
    if cov["fetched_no_fixture"]:
        L.append("- **Fetched but no fixture (capture with `render.py --live`):** %s"
                 % ", ".join(cov["fetched_no_fixture"]))
    if cov["fixture_no_fetch"]:
        L.append("- **Fixture with no fetch (orphan):** %s" % ", ".join(cov["fixture_no_fetch"]))
    with open(os.path.join(pages_dir, "_index.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(L) + "\n")
