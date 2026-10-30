#!/usr/bin/env bash
# Presence gate only: a human must still judge the quality of the WPF comparison.
set -euo pipefail
cd "$(dirname "$0")/../.."
PYTHONDONTWRITEBYTECODE=1 python3 - "${1:-docs/avalonia-parity.html}" "${2:-docs/avalonia-test-plan.html}" <<'PY'
import re
import sys
from pathlib import Path

sys.path.insert(0, ".github/scripts")
import avalonia_docs  # noqa: E402

ledger, plan = (Path(p).read_text(encoding="utf-8") for p in sys.argv[1:])
# Duplicate entries represent the same tester row.
required = {r["id"] for r in avalonia_docs.rows(ledger, "id") if r.get("status") in {"wired", "stub", "improved", "verified"}}
if not required:
    sys.exit("No testable ledger rows found; check the ledger table format.")
covered = set()
ids = re.compile(r"<code>([a-z][a-z0-9-]+)</code>")
for line in plan.splitlines():
    # Inventories, wildcard IDs and prose mentions do not supply tester steps.
    if line.startswith("<li>") and " WPF <code>" in line and " Rows: " in line:
        covered.update(ids.findall(line.split(" Rows: ", 1)[1]))
    elif line.startswith("<li>Render-only: "):
        covered.update(ids.findall(line.split("</code>. ", 1)[0] + "</code>"))  # only the ids before the first sentence
missing = sorted(required - covered)
if missing:
    print(f"Missing tester steps/render-only notes ({len(missing)}):")
    print("\n".join(missing))
    sys.exit(1)
print(f"Test plan coverage: {len(required)} distinct ledger IDs have steps or render-only notes.")
PY
