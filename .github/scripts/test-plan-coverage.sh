#!/usr/bin/env bash
# Presence gate only; tester reports are still required for verification.
set -euo pipefail
cd "$(dirname "$0")/../.."
PYTHONDONTWRITEBYTECODE=1 python3 .github/scripts/test_plan_coverage.py "$@"
