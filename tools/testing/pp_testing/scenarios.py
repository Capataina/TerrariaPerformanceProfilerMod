"""L6 — generative fixtures + contract drift, all generic over the contract.

The "contract" is the committed fixture set (tools/preview/fixtures/*.json), which is
a real captured session that already renders every pane. A scenario is a *generic
transform* applied to whatever endpoints are discovered — never a per-endpoint or
per-field hand-written blob. So:

  * a new endpoint the JS starts fetching is filled the moment a fixture is captured
    for it (render.py --live), with no edit here;
  * a new field inside an existing endpoint flows through every transform untouched.

Scenarios:
  full              the realistic dense session (the committed contract verbatim) —
                    the "fill everything" baseline the audit screenshots.
  edge-long-names   every name/label-ish string replaced with an over-long value —
                    stresses truncation / ellipsis / horizontal overflow.
  edge-extremes     numeric leaves pushed to huge / tiny / zero / negative —
                    stresses number formatting and bar/axis layout.
  empty             every array emptied — stresses every empty state.

To add a scenario you add one transform function to SCENARIOS; you never touch the
endpoint list, because there isn't one to touch.
"""
import json
import os

from . import harness

# Name/label-ish keys whose string values are display text (truncation targets).
_NAME_KEY = ("name", "label", "title", "source", "biome", "boss", "text", "mod",
             "kind", "category", "class", "pattern", "session")

_LONG = ("Antidisestablishmentarian Calamity Overhaul & Thorium Rework Expansion "
         "Pack Deluxe Edition (Community Patch)")
_LONG_UNI = "幻想的に長いモッド名前テストデモンストレーション・オーバーフロー検証用文字列ABCDEFG"


def load_contract_fixtures():
    """{endpoint: parsed-json} from the committed contract snapshot."""
    out = {}
    if not os.path.isdir(harness.CONTRACT_FIXTURES):
        return out
    for fn in os.listdir(harness.CONTRACT_FIXTURES):
        if fn.endswith(".json"):
            ep = fn[:-5]
            try:
                out[ep] = json.load(open(os.path.join(harness.CONTRACT_FIXTURES, fn),
                                         encoding="utf-8"))
            except Exception:
                out[ep] = {}
    return out


# ---- generic schema + drift -------------------------------------------------

def schema_of(obj):
    """A compact, hashable type descriptor (keys + leaf types), ignoring values."""
    if isinstance(obj, dict):
        return {k: schema_of(v) for k, v in sorted(obj.items())}
    if isinstance(obj, list):
        return ["[]", schema_of(obj[0])] if obj else ["[]"]
    if isinstance(obj, bool):
        return "bool"
    if isinstance(obj, (int, float)):
        return "num"
    if obj is None:
        return "null"
    return "str"


def contract_report(endpoints, fixtures):
    """What the frontend fetches vs what we have a fixture (shape) for."""
    have = set(fixtures)
    fetched = set(endpoints)
    return {
        "fetched_no_fixture": sorted(fetched - have),   # need a render.py --live capture
        "fixture_no_fetch": sorted(have - fetched),     # orphan fixture (endpoint removed?)
        "covered": sorted(fetched & have),
        "schema": {ep: schema_of(fixtures[ep]) for ep in sorted(have)},
    }


# ---- generic recursive transforms ------------------------------------------

def _is_name_key(k):
    kl = str(k).lower()
    return any(t in kl for t in _NAME_KEY)


def _walk_strings(obj, key, fn):
    """Recursively replace string leaves; fn(key, value) -> new value."""
    if isinstance(obj, dict):
        return {k: _walk_strings(v, k, fn) for k, v in obj.items()}
    if isinstance(obj, list):
        return [_walk_strings(v, key, fn) for v in obj]
    if isinstance(obj, str):
        return fn(key, obj)
    return obj


def _walk_numbers(obj, fn, counter):
    if isinstance(obj, dict):
        return {k: _walk_numbers(v, fn, counter) for k, v in obj.items()}
    if isinstance(obj, list):
        return [_walk_numbers(v, fn, counter) for v in obj]
    if isinstance(obj, bool):
        return obj
    if isinstance(obj, (int, float)):
        counter[0] += 1
        return fn(counter[0], obj)
    return obj


def _empty_arrays(obj):
    if isinstance(obj, dict):
        return {k: _empty_arrays(v) for k, v in obj.items()}
    if isinstance(obj, list):
        return []
    return obj


# ---- scenario builders ------------------------------------------------------

def _full(fixtures):
    return {ep: obj for ep, obj in fixtures.items()}


def _long_names(fixtures):
    def fn(key, val):
        if _is_name_key(key) and val and len(val) < 40:
            # Vary so we exercise both Latin truncation and CJK wrapping.
            return _LONG_UNI if (len(val) % 5 == 0) else _LONG
        return val
    return {ep: _walk_strings(obj, None, fn) for ep, obj in fixtures.items()}


def _extremes(fixtures):
    picks = [1e12, 1e-9, 0, -4242.4242, 9.999999999, 1234567.89]

    def fn(i, val):
        # Deterministically push ~1 in 3 numeric leaves to an extreme.
        if i % 3 == 0:
            return picks[i % len(picks)]
        return val
    return {ep: _walk_numbers(obj, fn, [0]) for ep, obj in fixtures.items()}


def _empty(fixtures):
    return {ep: _empty_arrays(obj) for ep, obj in fixtures.items()}


SCENARIOS = {
    "full": _full,
    "edge-long-names": _long_names,
    "edge-extremes": _extremes,
    "empty": _empty,
}


def build_scenario(name, endpoints, fixtures):
    """{endpoint: obj} for `name`, covering every discovered endpoint.

    Endpoints with no committed fixture get `{}` (the dashboard shows their empty
    state) and are reported by contract_report so the gap is visible, not silent.
    """
    if name not in SCENARIOS:
        raise ValueError("unknown scenario %r (have: %s)" % (name, ", ".join(SCENARIOS)))
    transformed = SCENARIOS[name](fixtures)
    # Guarantee an entry for every endpoint the frontend will fetch.
    return {ep: transformed.get(ep, {}) for ep in endpoints} \
        if endpoints else transformed
