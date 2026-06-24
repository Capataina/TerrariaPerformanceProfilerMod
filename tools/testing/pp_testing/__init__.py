"""Performance Profiler dashboard testing suite (L4 / L6 / L8).

A layered, *self-describing* test harness for the browser dashboard. Nothing here
hardcodes "six tabs" or "29 endpoints": the tab list is discovered from the rendered
DOM, the endpoint list is discovered from the extracted JS, and the per-page audit
docs are created on demand. Add a 7th tab tomorrow and the whole suite audits it with
no code change.

Layers (see context/plans/extensive-testing-infrastructure.md):
  L4  layout.py    deterministic interaction / layout invariants (Playwright)
  L6  scenarios.py generative fill-everything + edge-case fixtures from the contract
  L8  capture.py   clean-slate screenshot sweep + manifest for the agent fan-out
  -   pages.py     durable, evolving per-page documents the audit writes into
  -   harness.py   asset extraction, endpoint discovery, loopback serve
  -   driver.py    Playwright page boot + generic DOM discovery
"""
