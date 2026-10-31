"""Require concrete tester steps or render-only notes for every non-n/a parity ID."""
import argparse
import re
from pathlib import Path
import avalonia_docs as docs
ROOT = Path(__file__).resolve().parents[2]
ID = re.compile(r"`([a-z][a-z0-9-]+)`")
def coverage(ledger, plan):
    rows = docs.rows(ledger, "id")
    required = {r["id"] for r in rows if r.get("status", "").strip().lower() != "n/a"}
    if not required: raise ValueError("No testable parity rows found")
    if len(rows) != len({r["id"] for r in rows}): raise ValueError("Duplicate parity IDs; use one ledger row per ID")
    covered, mentioned = set(), set()
    for line in plan.splitlines():
        text = " ".join(docs.parse_row("<tr><td>" + line + "</td></tr>")[1])
        mentioned.update(ID.findall(text))
        if line.lstrip().startswith("<li>") and " WPF `" in text and " Rows: " in text:
            covered.update(ID.findall(text.split(" Rows: ", 1)[1]))
        elif text.startswith("Render-only: "):
            covered.update(ID.findall(text.split(". ", 1)[0]))
    return sorted(required - mentioned), sorted(required - covered), len(required)
def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name, filename in (("ledger", "avalonia-parity.html"), ("plan", "avalonia-test-plan.html")):
        parser.add_argument(name, nargs="?", type=Path, default=ROOT / "docs" / filename)
    args = parser.parse_args()
    try: mentions, steps, total = coverage(*(p.read_text(encoding="utf-8") for p in (args.ledger, args.plan)))
    except ValueError as error: parser.exit(1, f"{error}\n")
    for label, ids in ((f"ID mentions ({total} non-n/a parity rows)", mentions), ("tester steps/render-only notes", steps)):
        print(f"{len(ids)} missing {label}")
        for row_id in ids: print(row_id)
    return int(bool(mentions or steps))
if __name__ == "__main__":
    raise SystemExit(main())
