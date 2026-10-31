# Racing Thoughts 3D on Linux - measurement kit (for Mich)

Hi Mich. We need real Linux numbers before deciding how Racing Thoughts 3D runs in the 7.2 Linux preview.
This folder measures the game in every web engine your box has, the same way in each. It builds nothing in
the app and touches none of your branches.

## Run it

```bash
bash run.sh
```

About 25 minutes. Windows open and close on their own; please leave the machine alone meanwhile (a window
behind another one runs slower). If you have a gamepad, plug it in and press a button whenever a race window is
open. At the end: open `rt-linux-spike-<host>-<date>/report.md`, fill the short "By hand" part at the bottom,
re-run the `tar` line the script prints, and send the `.tar.gz` back to the owner.

## What it needs

- `git`, `curl`, `python3` with GObject + the WebKit2 4.1 typelib (Debian/Ubuntu `gir1.2-webkit2-4.1`,
  Fedora `webkit2gtk4.1`, Arch `webkit2gtk-4.1`). The app already needs the library; the typelib is tiny.
- No sudo. Node 22 and Electron are downloaded into `~/.cache/ccp-rt-spike` (checksums verified) if missing.
- Optional: `flatpak` (it installs `org.gnome.Platform//50` for your user, a few hundred MB, the runtime our
  Flatpak uses; `NO_FLATPAK=1` skips), Chrome/Chromium, Firefox.

## What it measures

Engines: WebKitGTK as the app uses it today; WebKitGTK inside the Flatpak GNOME 50 runtime; Electron (our own
Chromium, WebGL2 and WebGPU); system Chrome/Chromium and Firefox with their defaults (the browser fallback).
Courses: Threshold and GOONWORKS, full desktop look pinned before boot (quality high, render scale 100%), a bot
driving. Per run: boot to garage, boot to fully warm, fps mean / 1% low / worst second, frames over 50 and
100 ms (shader stalls), CPU per frame, memory (PSS of the whole engine process tree), audio clock drift and
output latency, the online course hash vs Node, WebGPU / WebGL2 adapter, local picture and video decode
("My files"), gamepads seen. On the first WebKitGTK run it also checks the app's Scrolller block filter compiles
and blocks.

Knobs: `RACE_SECONDS=45 SIZE=1920x1080 COURSES="threshold goonworks" NO_ELECTRON=1 NO_BROWSERS=1 GLASSROAD_DIR=...`.

Files: `run.sh` (driver), `bench-server.mjs` (serves the game from the public repo, collects results),
`spike.js` (in-page probe), `wk-bench.py` (WebKitGTK window), `electron-main.js` (Electron window),
`make-report.mjs` (report), `run-windows.ps1` (the same runs on Windows, for our reference rows).
