#!/usr/bin/env bash
# Build the linux-x64 tarball the Flatpak manifest and the AUR PKGBUILD consume
# (docs/avalonia-linux-install.html). Local only: never uploads anything.
#   bash packaging/linux/build-tarball.sh [out-dir]   -> prints the tarball path
# Layout: ConditioningControlPanel/{CCP.Avalonia, conditioning-control-panel.sh, LICENSE, THIRD-PARTY-NOTICES.txt, Resources/Models/vosk/<model>,
#         share/{applications,icons/hicolor/*/apps,metainfo}/<app-id>.*}  (share/ maps onto /usr/share or /app/share)
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
out=${1:-$root/packaging/linux/out}
ver=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Version.props")
pub=$root/CCP.Avalonia/bin/publish/linux-x64

rm -rf "$pub"
dotnet publish "$root/CCP.Avalonia/CCP.Avalonia.csproj" -c Release -p:PublishProfile=linux-x64 >&2
test -f "$pub/libvosk.so"
# The small model (~40 MB, sha256-pinned), where SpeechEngine.DefaultModelRoots looks first.
model=$(bash "$root/.github/scripts/fetch-vosk-model.sh")
mkdir -p "$pub/Resources/Models/vosk"
cp -r "$model" "$pub/Resources/Models/vosk/"
cp -r "$root/packaging/linux/share" "$pub/"
cp "$root/LICENSE" "$pub/"
cp "$root/packaging/linux/RT-ENGINE-NOTICES.txt" "$pub/"
py=
for c in python3 python; do "$c" -c 'import tarfile' >/dev/null 2>&1 && { py=$c; break; }; done   # Windows: python3 may be the Store stub
[ -n "$py" ] || { echo "build-tarball: python3 is required to pack the tarball" >&2; exit 1; }
# Notices for the bundled natives: the hand-written part, then the resolved package versions and the exact
# upstream licence files from the restored packages. A package that moves or drops its file fails the build.
"$py" - "$root/CCP.Avalonia/obj/project.assets.json" "$root/packaging/linux/THIRD-PARTY-NOTICES.txt" "$pub/THIRD-PARTY-NOTICES.txt" <<'PY'
import json, os, sys
assets, head, out = sys.argv[1:4]
a = json.load(open(assets, encoding="utf-8"))
roots = list(a["packageFolders"])
pkgs = {k.split("/")[0].lower(): k.split("/")[1] for k in a["libraries"]}
for fw in a["project"]["frameworks"].values():
    for d in fw.get("downloadDependencies", []):
        pkgs[d["name"].lower()] = d["version"].strip("[]").split(",")[0].strip()
# A Linux SDK carries its own linux-x64 apphost pack, so restore lists no download for it (a Windows SDK does).
# Then the apphost comes from <dotnet root>/packs at the runtime pack's version; prove that folder exists.
HOST, RUNTIME = "microsoft.netcore.app.host.linux-x64", "microsoft.netcore.app.runtime.linux-x64"
if HOST not in pkgs and RUNTIME in pkgs:
    import shutil
    exe = shutil.which("dotnet")
    for base in filter(None, [os.environ.get("DOTNET_ROOT"), exe and os.path.dirname(os.path.realpath(exe))]):
        if os.path.isdir(os.path.join(base, "packs", "Microsoft.NETCore.App.Host.linux-x64", pkgs[RUNTIME])):
            pkgs[HOST] = pkgs[RUNTIME] + " (.NET SDK pack)"
            break
LISTED = ["microsoft.netcore.app.runtime.linux-x64", "microsoft.netcore.app.host.linux-x64", "skiasharp",
          "skiasharp.nativeassets.linux", "harfbuzzsharp", "harfbuzzsharp.nativeassets.linux", "microsoft.ml.onnxruntime",
          "opencvsharp4", "opencvsharp4.official.runtime.linux-x64", "vosk", "libvlcsharp", "avalonia",
          "avalonia.controls.webview"]
FILES = [("microsoft.netcore.app.runtime.linux-x64", ["LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"]),
         ("skiasharp.nativeassets.linux", ["LICENSE.txt", "THIRD-PARTY-NOTICES.txt"]),
         ("harfbuzzsharp.nativeassets.linux", ["LICENSE.txt", "THIRD-PARTY-NOTICES.txt"]),
         ("microsoft.ml.onnxruntime", ["LICENSE", "ThirdPartyNotices.txt"])]
def find(pkg, name):
    for r in roots:
        f = os.path.join(r, pkg, pkgs[pkg], name)
        if os.path.isfile(f):
            return f
    sys.exit(f"build-tarball: {pkg} {pkgs.get(pkg)} has no {name} in {roots}")
rule = "=" * 70
with open(out, "w", encoding="utf-8", newline="\n") as o:
    o.write(open(head, encoding="utf-8").read().rstrip() + f"\n\n\n{rule}\nPackage versions in this build\n{rule}\n\n")
    for p in LISTED:
        if p not in pkgs:
            sys.exit(f"build-tarball: {p} is not in {assets}; update packaging/linux/THIRD-PARTY-NOTICES.txt")
        o.write(f"{p} {pkgs[p]}\n")
    seen = {}
    for pkg, names in FILES:
        for n in names:
            text = open(find(pkg, n), encoding="utf-8-sig").read().replace("\r\n", "\n")
            o.write(f"\n\n{rule}\n{pkg} {pkgs[pkg]}: {n}\n{rule}\n\n")
            o.write(f"Identical to {seen[text]} above.\n" if text in seen else text.rstrip() + "\n")
            seen.setdefault(text, f"{pkg} {pkgs[pkg]}: {n}")
PY
cp "$root/packaging/linux/conditioning-control-panel.sh" "$pub/"   # the launcher: OpenSSL 3 hint, then CCP.Avalonia
mkdir -p "$pub/packaging/linux"
cp "$root/packaging/linux/rt-engine-check.sh" "$pub/packaging/linux/"
# A Windows checkout can hand these over with CRLF: a CRLF shebang cannot run, and desktop tools reject CRLF entries.
find "$pub" -type f \( -name '*.sh' -o -name '*.desktop' -o -name '*.metainfo.xml' \) -exec sed -i 's/\r$//' {} +

# A checkout with LFS off publishes the media as pointer files, and the app then plays nothing.
# Refuse them: run `git lfs pull --include "Assets/**"` first (CI does).
pointers=$(find "$pub" -type f -size -1025c -exec grep -l '^version https://git-lfs' {} + || true)
if [ -n "$pointers" ]; then
  echo "build-tarball: LFS pointer files instead of media (git lfs pull --include \"Assets/**\"):" >&2
  echo "$pointers" >&2
  exit 1
fi

mkdir -p "$out"
tar=$out/ConditioningControlPanel-$ver-linux-x64.tar.gz
# Reproducible: sorted names, fixed owner and mtime, no gzip timestamp. Modes are set here, not read from disk:
# a build on NTFS (Git Bash) has no exec bits, and the beta-1 tarball shipped a 0644 CCP.Avalonia
# ("Permission denied"). Executables (apphost, createdump, *.sh) and dirs 0755, everything else 0644.
"$py" - "$pub" "$tar" <<'PY'
import gzip, os, sys, tarfile
pub, out = sys.argv[1], sys.argv[2]
EXEC = {"CCP.Avalonia", "createdump"}
def norm(ti):
    ti.uid = ti.gid = 0; ti.uname = ti.gname = ""; ti.mtime = 0
    base = os.path.basename(ti.name)
    ti.mode = 0o755 if ti.isdir() or base in EXEC or base.endswith(".sh") else 0o644
    return ti
with open(out, "wb") as raw, gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) as gz, \
        tarfile.open(fileobj=gz, mode="w", format=tarfile.GNU_FORMAT) as t:
    t.add(pub, arcname="ConditioningControlPanel", filter=norm)   # add() recurses in sorted order
PY
echo "$tar"
