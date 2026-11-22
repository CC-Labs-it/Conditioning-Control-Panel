"""Render approved-voice Expo takes and export their browser catalog. Dry runs never load credentials."""
import argparse
import json
import shutil
from pathlib import Path

from expo_script_v4 import LINES

HERE = Path(__file__).resolve().parent
GAME = HERE.parents[1] / "ConditioningControlPanel/Resources/web/fuzz"
AUDIO = GAME / "assets/expo/narration/hall"


def main():
    args = argparse.ArgumentParser()
    args.add_argument("--dry", action="store_true")
    args.add_argument("--catalog", action="store_true")
    args.add_argument("--limit", type=int)
    options = args.parse_args()
    rows = []
    for name, text in LINES.items():
        key = "v4_" + name
        rows.append(dict(id=key, set="exhibit", where="Renewal visit", subtitle=text,
                         seconds=max(2.4, round(len(text.split()) / 2.5, 1)), audio=None, slides=[]))
    if options.dry:
        for row in rows[:options.limit]:
            print(row["id"], row["subtitle"])
        return
    if not options.catalog:
        import gen_expo_audio as pipeline
        # Private workshop keeps the reusable clean takes. Only the treated recording ships.
        for row in [r for r in rows if not (AUDIO / (r["id"] + ".mp3")).exists()][:options.limit]:
            name = row["id"]
            clean = pipeline.CLEAN / (name + ".mp3")
            if not clean.exists() and not pipeline.render(name, row["subtitle"]):
                break
            treated = pipeline.HALL / (name + ".mp3")
            pipeline.hall(clean, treated, chime=False)
            shutil.copyfile(treated, AUDIO / treated.name)
            print(name, "ready", flush=True)
    for row in rows:
        if (AUDIO / (row["id"] + ".mp3")).is_file():
            row["audio"] = "hall/" + row["id"] + ".mp3"
    (GAME / "ui/expo/renewal-lines.js").write_text(
        "export const renewalLines = " + json.dumps(rows, indent=2, ensure_ascii=False) + ";\n",
        encoding="utf-8")


if __name__ == "__main__":
    main()
