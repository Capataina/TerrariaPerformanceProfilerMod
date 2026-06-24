"""Stand up a scenario site on loopback — shared by L4 (assert) and L8 (capture)."""
import os

from . import harness, scenarios


def stand_up(scenario="full", port=27299, site_root="/tmp/pp-audit/site"):
    """Build the dashboard with a scenario's data and serve it on loopback.

    Returns (httpd, url, site_dir, report, endpoints). The caller drives the page
    with driver.Dashboard(url) and is responsible for httpd.shutdown().
    """
    css, js, html = harness.extract_assets()
    endpoints = harness.discover_endpoints(js)
    fixtures = scenarios.load_contract_fixtures()
    report = scenarios.contract_report(endpoints, fixtures)
    data = scenarios.build_scenario(scenario, endpoints, fixtures)

    site_dir = os.path.join(site_root, scenario)
    # The driver does the page boot via evaluate(), so the served boot file is empty.
    harness.assemble_site(site_dir, css, js, html, boot_js="")
    harness.write_api(site_dir, data)
    httpd = harness.serve(site_dir, port)
    return httpd, "http://127.0.0.1:%d/" % port, site_dir, report, endpoints
