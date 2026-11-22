"""Render approved-voice Expo takes and export their browser catalog. Dry runs never load credentials."""
import argparse
from array import array
import json
import shutil
import sys
import subprocess
import tempfile
import wave
from pathlib import Path

from expo_script_v4 import LINES

HERE = Path(__file__).resolve().parent
GAME = HERE.parents[1] / "ConditioningControlPanel/Resources/web/fuzz"
AUDIO = GAME / "assets/expo/narration/hall"


def treat_line(pipeline, name, clean, treated):
    if name != "v4_unplug":
        pipeline.hall(clean, treated, chime=False)
        return

    # On the approved take, the final vowel is still voiced at 3.43 s.
    # Apply the power loss to the hall sound too, so its echo cuts with the speaker.
    with tempfile.TemporaryDirectory() as directory:
        hall = Path(directory) / "speaker.wav"
        pipeline.hall(clean, hall, chime=False)
        with wave.open(str(hall), "rb") as source:
            if source.getsampwidth() != 2 or source.getnchannels() != 1:
                raise ValueError("Unplug treatment expects the mono PCM hall master")
            rate = source.getframerate()
            samples = array("h", source.readframes(source.getnframes()))
    if sys.byteorder != "little":
        samples.byteswap()
    start, cut, end_speed = 2.50 * rate, 3.43 * rate, 0.42
    if len(samples) <= cut + 1:
        raise ValueError("Unplug master ends before the final vowel")
    length = round(2 * (cut - start) / (1 + end_speed))
    result = samples[:round(start)]
    for step in range(length):
        # Integrate a steadily falling playback rate so timing and pitch drop together.
        position = start + step * (1 - (1 - end_speed) * step / (2 * length))
        i = int(position)
        value = samples[i] + (position - i) * (samples[i + 1] - samples[i])
        declick = min(1, (length - 1 - step) / (rate * 0.002))
        result.append(round(value * declick))
    if sys.byteorder != "little":
        result.byteswap()
    subprocess.run(pipeline.FF + ["-f", "s16le", "-ar", str(rate), "-ac", "1",
                   "-i", "pipe:0", "-b:a", "128k", str(treated)], input=result.tobytes(), check=True)


def main():
    args = argparse.ArgumentParser()
    args.add_argument("--dry", action="store_true")
    args.add_argument("--catalog", action="store_true")
    args.add_argument("--treat-unplug", action="store_true", help="Rebuild only the unplug effect from its existing clean take")
    args.add_argument("--limit", type=int)
    args.add_argument("--round", type=int, choices=(4, 5), default=4)
    args.add_argument("--pipeline-dir", type=Path, help="Workshop containing gen_expo_audio.py and its original script")
    options = args.parse_args()
    rows = []
    if options.round == 5:
        from expo_script_v5 import LINES as script
    else:
        script = {name: ("Renewal visit", line) for name, line in LINES.items()}
    if options.treat_unplug and options.round != 4:
        args.error("--treat-unplug belongs to round 4")
    for name, (where, text) in script.items():
        key = ("r5_" if options.round == 5 else "v4_") + name
        rows.append(dict(id=key, set="exhibit", where=where, subtitle=text,
                         seconds=max(2.4, round(len(text.split()) / 2.5, 1)), audio=None, slides=[]))
    if options.dry:
        for row in [r for r in rows if not (AUDIO / (r["id"] + ".mp3")).is_file()][:options.limit]:
            print(row["id"], row["subtitle"])
        return
    if options.treat_unplug or not options.catalog:
        if options.pipeline_dir:
            sys.path.insert(0, str(options.pipeline_dir.resolve()))
        import gen_expo_audio as pipeline
        # Private workshop keeps the reusable clean takes. Only the treated recording ships.
        pending = [r for r in rows if not (AUDIO / (r["id"] + ".mp3")).exists()][:options.limit]
        if options.treat_unplug:
            pending = [r for r in rows if r["id"] == "v4_unplug"]
        for row in pending:
            name = row["id"]
            clean = pipeline.CLEAN / (name + ".mp3")
            if options.treat_unplug and not clean.is_file():
                raise FileNotFoundError(clean)
            if not clean.exists() and not pipeline.render(name, row["subtitle"]):
                raise SystemExit("No clean take for " + name)
            treated = pipeline.HALL / (name + ".mp3")
            treat_line(pipeline, name, clean, treated)
            shutil.copyfile(treated, AUDIO / treated.name)
            print(name, "ready", flush=True)
    for row in rows:
        if (AUDIO / (row["id"] + ".mp3")).is_file():
            row["audio"] = "hall/" + row["id"] + ".mp3"
            duration = subprocess.check_output(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", str(AUDIO / (row["id"] + ".mp3"))], text=True)
            row["seconds"] = round(float(duration.strip()), 2)
    name, binding = ("trail-lines.js", "trailLines") if options.round == 5 else ("renewal-lines.js", "renewalLines")
    (GAME / "ui/expo" / name).write_text(
        "export const " + binding + " = " + json.dumps(rows, indent=2, ensure_ascii=False) + ";\n",
        encoding="utf-8")
    if options.round == 5:
        review = ["# Round 5 narrator lines", "", "TC Narrator (Original A), museum speaker mix.", "",
                  "| Cue | Where it plays | Spoken line | Duration |", "| --- | --- | --- | ---: |"]
        for row in rows:
            duration = f"{row['seconds']:.2f} s" if row['audio'] else "Caption only"
            review.append(f"| `{row['id']}` | {row['where']} | {row['subtitle']} | {duration} |")
        (HERE.parent / "expo-review/ROUND-5-LINES.md").write_text("\n".join(review) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
