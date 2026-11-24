"""Build the downloadable Expo from the game assets and licensed radio staging."""
import argparse
import hashlib
import json
import re
import shutil
import statistics
import tempfile
import time
import zipfile
from pathlib import Path

GAME = Path(__file__).resolve().parents[1]
REPO = next(folder for folder in GAME.parents if (folder / ".git").exists())
ASSETS = GAME / "assets" / "expo"
RADIO = ("breathe-in.mp3", "idle-cycle.mp3", "sealed.mp3")


def collect(radio):
    tracks = {f"audio/radio/{name}": radio / name for name in RADIO}
    missing = [str(path) for path in tracks.values() if not path.is_file() or not path.stat().st_size]
    if missing:
        raise ValueError("Missing licensed radio staging files: " + ", ".join(missing))
    if not (ASSETS / "zones" / "zones.json").is_file():
        raise ValueError(f"Missing Expo zone manifest in {ASSETS}")
    files = {path.relative_to(ASSETS).as_posix(): path for path in ASSETS.rglob("*")
             if path.is_file() and path.name != ".gitignore"}
    files.update(tracks)
    for name, path in files.items():
        if path.stat().st_size < 1024:
            with path.open("rb") as source:
                if source.readline().rstrip() == b"version https://git-lfs.github.com/spec/v1":
                    raise ValueError(f"Missing Git LFS content for {name}. Restore it before packing.")
    return sorted(files.items())


def archive(target, files, method):
    with zipfile.ZipFile(target, "w") as dest:
        for name, path in files:
            entry = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
            entry.create_system = 3
            entry.external_attr = 0o100644 << 16
            entry.compress_type = method
            with path.open("rb") as source, dest.open(entry, "w") as out:
                shutil.copyfileobj(source, out, 1024 * 1024)


def compare(archives, work):
    times = {key: [] for key in archives}
    for run in range(3):
        order = list(archives) if run % 2 == 0 else list(reversed(archives))
        for key in order:
            with tempfile.TemporaryDirectory(dir=work) as dest:
                start = time.perf_counter()
                with zipfile.ZipFile(archives[key]) as source:
                    source.extractall(dest)
                times[key].append(round(1000 * (time.perf_counter() - start)))
    return {key: {"bytes": path.stat().st_size, "unpackMs": times[key],
                  "medianUnpackMs": statistics.median(times[key])} for key, path in archives.items()}


def build(args):
    output = args.out.resolve()
    if output.is_relative_to(REPO):
        raise ValueError("Choose an outputs directory outside the repository")
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]{0,63}", args.version):
        raise ValueError("Use a pack version of at most 64 letters, digits, dots, underscores or hyphens")
    files = collect(args.radio.resolve())
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output) as temp:
        work = Path(temp)
        packed = work / "deflated.zip"
        archive(packed, files, zipfile.ZIP_DEFLATED)
        with packed.open("rb") as source:
            sha = hashlib.file_digest(source, "sha256").hexdigest()
        file = f"expo-{args.version}-{sha[:16]}.zip"
        manifest = {"v": 1, "file": file, "bytes": packed.stat().st_size, "sha256": sha,
                    "version": args.version, "minApp": "7.4.0"}
        report = {"files": len(files), "manifest": manifest}
        if args.compare:
            stored = work / "stored.zip"
            archive(stored, files, zipfile.ZIP_STORED)
            report["formats"] = compare({"stored": stored, "deflated": packed}, work)
        staged = work / "expo-manifest.json"
        staged.write_bytes((json.dumps(manifest, indent=2) + "\n").encode("utf-8"))
        packed.replace(output / file)
        staged.replace(output / staged.name)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True, help="Outputs directory outside the repository")
    parser.add_argument("--version", required=True, help="Pack version for the manifest")
    parser.add_argument("--radio", type=Path, default=GAME / "assets/lofi/audio", help="Licensed radio staging directory")
    parser.add_argument("--compare", action="store_true", help="Time full extraction of both ZIP methods three times")
    try:
        build(parser.parse_args())
    except (OSError, ValueError, zipfile.BadZipFile) as error:
        parser.exit(1, f"Expo pack build failed: {error}\n")
