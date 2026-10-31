"""Check parity IDs have tester steps, using the shared HTML reader (stdlib only).

Presence is not proof of behaviour. Only tester reports can support verification.
Bare inventories and prose references do not count as steps. Retired WPF paths
may instead have an explicit render-only instruction.
"""
import argparse
import re
from pathlib import Path

import avalonia_docs as docs

ROOT = Path(__file__).resolve().parents[2]
ID = re.compile(r"`([a-z][a-z0-9-]+)`")


def coverage(ledger, plan):
    required = [r["id"] for r in docs.rows(ledger, "id")
                if r.get("status", "").strip().lower() != "n/a"]
    if not required:
        raise ValueError("No testable parity rows found")
    all_ids = [r["id"] for r in docs.rows(ledger, "id")]
    if len(all_ids) != len(set(all_ids)):
        raise ValueError("Duplicate parity IDs; use one ledger row per ID")
    covered, mentioned = set(), set()
    for line in plan.splitlines():
        # parse_row decodes HTML entities and preserves code spans as backticks.
        _, cells = docs.parse_row("<tr><td>" + line + "</td></tr>")
        text = " ".join(cells)
        mentioned.update(ID.findall(text))
        if line.lstrip().startswith("<li>") and " WPF `" in text and " Rows: " in text:
            covered.update(ID.findall(text.split(" Rows: ", 1)[1]))
        elif text.startswith("Render-only: "):
            covered.update(ID.findall(text.split(". ", 1)[0]))
    return sorted(set(required) - mentioned), sorted(set(required) - covered), len(required)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("ledger", nargs="?", type=Path, default=ROOT / "docs/avalonia-parity.html")
    parser.add_argument("plan", nargs="?", type=Path, default=ROOT / "docs/avalonia-test-plan.html")
    args = parser.parse_args()
    try:
        mentions, steps, total = coverage(args.ledger.read_text(encoding="utf-8"),
                                          args.plan.read_text(encoding="utf-8"))
    except ValueError as error:
        parser.exit(1, f"{error}\n")
    print(f"{len(mentions)} missing ID mentions ({total} non-n/a parity rows)")
    for row_id in mentions:
        print(row_id)
    print(f"{len(steps)} missing tester steps/render-only notes")
    for row_id in steps:
        print(row_id)
    return int(bool(mentions or steps))


if __name__ == "__main__":
    raise SystemExit(main())
