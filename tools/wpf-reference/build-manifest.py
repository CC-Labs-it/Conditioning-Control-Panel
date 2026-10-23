"""Builds docs/wpf-reference/ from a WPF reference render (Tests/ConditioningControlPanel.Tests/WpfReferenceRender.cs).

Copies each rendered type to docs/wpf-reference/<row-id>.png when a docs/avalonia-parity.md row's WPF column names
that type's .xaml file, keeps the rest under docs/wpf-reference/auto/<TypeName>.png, lists the live captures under
docs/wpf-reference/live/, and writes docs/wpf-reference/manifest.md.

Usage: python tools/wpf-reference/build-manifest.py <render-dir> [--version 7.1.14] [--scale 100%]
"""
import argparse
import json
import re
import shutil
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "docs" / "wpf-reference"
PREFIXES = ("win-", "dialogs-", "views-", "ctrl-", "overlay-", "shell-", "theme-", "game-", "lab-", "chaos-")


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    w, h = struct.unpack(">II", head[16:24])
    return f"{w}x{h}"


def ledger_rows():
    """row id -> list of WPF .xaml class names named in its WPF column (first copy of each id wins)."""
    rows = {}
    for line in (ROOT / "docs" / "avalonia-parity.md").read_text(encoding="utf-8").splitlines():
        cells = [c.strip() for c in line.split("|")]
        if len(cells) < 6 or not cells[1] or cells[1] == "id" or set(cells[1]) <= {"-", " "}:
            continue
        row_id = cells[1].strip("` ")
        if not row_id.startswith(PREFIXES) or row_id in rows:
            continue
        xaml = re.findall(r"([A-Za-z0-9_]+)\.xaml\b", cells[2])
        if xaml:
            rows[row_id] = list(dict.fromkeys(xaml))
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("render_dir")
    ap.add_argument("--version", default="7.1.14")
    ap.add_argument("--scale", default="100%")
    a = ap.parse_args()
    src = Path(a.render_dir)
    index = json.loads((src / "_index.json").read_text(encoding="utf-8"))

    (OUT / "auto").mkdir(parents=True, exist_ok=True)
    used, lines, skipped, nomatch = set(), [], [], []
    for row_id, classes in sorted(ledger_rows().items()):
        hit = next((c for c in classes if index.get(c, {}).get("ok")), None)
        if hit:
            dst = OUT / f"{row_id}.png"
            shutil.copyfile(src / index[hit]["file"], dst)
            used.add(hit)
            lines.append(f"{row_id} | {dst.name} | auto | {png_size(dst)} | {hit}.xaml")
            continue
        err = next((index[c]["error"] for c in classes if c in index and not index[c].get("ok")), None)
        (skipped if err else nomatch).append((row_id, ", ".join(c + ".xaml" for c in classes), err))

    extra = []
    for name, entry in sorted(index.items()):
        if entry.get("ok") and name not in used:
            dst = OUT / "auto" / entry["file"]
            shutil.copyfile(src / entry["file"], dst)
            extra.append(f"(no row) | auto/{dst.name} | auto | {png_size(dst)} | {name}")

    live = [f"{p.stem} | live/{p.name} | live | {png_size(p)} | sandbox profile, offline"
            for p in sorted((OUT / "live").glob("*.png"))]

    md = [
        "# WPF reference pictures",
        "",
        f"WPF app version: {a.version} (Release build of port/7.2 be09ff13f, net10, Windows 11). Windows scaling: {a.scale}.",
        "Auto = offscreen RenderTargetBitmap at 96 dpi (WpfReferenceRender, no services, fresh profile).",
        "Live = PrintWindow capture of the running app on a throwaway CCP_USERDATA_DIR, offline, never signed in.",
        f"Counts: {len(lines)} rows auto-rendered, {len(live)} live captures, {len(extra)} extra auto renders with no row,"
        f" {len(skipped)} rows skipped (type threw), {len(nomatch)} rows whose .xaml type was not rendered.",
        "",
        "## Rows",
        "",
        "row-id | file | auto/live | size | notes",
        "---|---|---|---|---",
        *lines,
        "",
        "## Live captures",
        "",
        "Names: `shell-door-<door>` (rail door), `shell-tab-<pill>` (nav pill), `dialog-from-<pill>` (window that pill opens),",
        "`settings-page-NN` (Settings page scrolled top to bottom), `release-content-mod-manager-rows` (Mod Manager, built-in",
        "CCP Default selected). The launcher has no live capture (PrintWindow returns black for it); use auto/LauncherWindow.png.",
        "",
        "name | file | live | size | notes",
        "---|---|---|---|---",
        *live,
        "",
        "## Extra auto renders (type has no ledger row)",
        "",
        "name | file | auto | size | type",
        "---|---|---|---|---",
        *extra,
        "",
        "## Skipped rows (the type threw offscreen)",
        "",
        *[f"- {r} ({x}): {e}" for r, x, e in skipped],
        "",
        "## Rows with no render (the .xaml type is not a Window/UserControl, or was not found)",
        "",
        *[f"- {r} ({x})" for r, x, _ in nomatch],
        "",
    ]
    (OUT / "manifest.md").write_text("\n".join(md), encoding="utf-8", newline="\n")
    print(f"rows auto {len(lines)}, live {len(live)}, extra {len(extra)}, skipped {len(skipped)}, no render {len(nomatch)}")


if __name__ == "__main__":
    main()
