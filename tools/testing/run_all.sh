#!/usr/bin/env bash
# The whole no-game verification gate in one command (e2e plan S27).
# Exit non-zero on ANY failure. Usage: tools/testing/run_all.sh [--skip-ui]
set -uo pipefail
cd "$(dirname "$0")/../.."

fail=0

echo "── ring 1+2: dotnet test (scenario engine, honesty pins, store round-trips) ──"
if ! dotnet test Tests/PerformanceProfiler.Tests.csproj -nologo -v:q; then fail=1; fi

echo "── compile gate: dotnet msbuild (0 error CS; TML003/MSB3073 loader-lock ignored) ──"
build_out=$(dotnet msbuild PerformanceProfiler.csproj -nologo -v:m 2>&1)
cs_errors=$(printf '%s' "$build_out" | grep -c 'error CS' || true)
if [ "${cs_errors:-0}" -ne 0 ]; then
  printf '%s\n' "$build_out" | grep 'error CS' | head -20
  echo "compile gate FAILED: $cs_errors error CS"
  fail=1
else
  echo "compile gate OK (0 error CS)"
fi

if [ "${1:-}" != "--skip-ui" ]; then
  echo "── ring 3: UI harness (fixtures + layout asserts) ──"
  PY=tools/testing/.venv/bin/python
  if [ -x "$PY" ]; then
    if ! PLAYWRIGHT_BROWSERS_PATH="$PWD/tools/testing/.venv/ms-playwright" "$PY" tools/testing/audit.py assert; then
      fail=1
    fi
  else
    echo "harness venv missing (tools/testing/README.md setup) — ring 3 SKIPPED"
    echo "(skip is reported, not silent: install the venv to close the gate)"
  fi
fi

if [ $fail -ne 0 ]; then
  echo "══ run_all: FAILED ══"
  exit 1
fi
echo "══ run_all: all gates green ══"
