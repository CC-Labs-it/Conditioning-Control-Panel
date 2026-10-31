"""Check the cosmetics lane against its task-specified post-import base.

The optional base argument supports a negative control with 74ac6ce3d.
Includes staged and tracked working-tree changes so it also runs before commit.
"""
import pathlib
import subprocess
import sys

BASE = "6121afbebda83bbbb3fe81c610c7c84a03c7e05c"
ROOT = pathlib.Path(__file__).resolve().parents[2]


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT, text=True).strip()


base = sys.argv[1] if len(sys.argv) > 1 else BASE
subprocess.run(["git", "merge-base", "--is-ancestor", base, "HEAD"], cwd=ROOT, check=True)
added = removed = 0
errors = []
for row in git("diff", "--no-renames", "--numstat", base, "--").splitlines():
    plus, minus, path = row.split("\t", 2)
    if plus == "-" or minus == "-":
        errors.append(f"binary change: {path}")
    else:
        added += int(plus)
        removed += int(minus)
    if path.startswith(("ConditioningControlPanel/", "Assets/web/")):
        errors.append(f"WPF or web change: {path}")
if added + removed > 600:
    errors.append(f"line limit exceeded: {added + removed} > 600")
print(f"Review base: {base}; +{added}/-{removed}; {added + removed} changed lines")
for error in errors:
    print(error)
sys.exit(1 if errors else 0)
