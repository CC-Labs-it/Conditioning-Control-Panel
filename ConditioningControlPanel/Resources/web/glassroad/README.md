# Glassroad (Racing Thoughts 3D), Phase A slice

One kart (EMI on a teacup), one greybox track (Threshold, 3 laps), drift and boost, hop and landings,
falls and the rescue, chase camera, engine audio, the 10 juice beats and the live feel sheet.
Plain ES modules and an import map; three.js r186 is vendored in `vendor/three/`. No build step.

## Play locally

```bash
cd ConditioningControlPanel/Resources/web/glassroad
node dev/serve.mjs            # http://127.0.0.1:8790/ (port: first argument or PORT)
```

Open http://127.0.0.1:8790/?feel=1 in Edge or Chrome. Press any key or Start.

Keys: W / Up throttle, S / Down brake (reverse when stopped), A / D or Left / Right steer,
Shift or Space hop and drift (hold through a turn, let go for the boost), Escape pause.
A standard gamepad works too (left stick, RT, LT, A, Start).

## Url flags

| Flag | What it does |
|---|---|
| `?feel=1` | live feel sheet: every `feel.json` number, edits apply next tick, kept in this browser, Copy JSON to paste back |
| `?renderer=webgl2` | force the WebGL2 backend (`webgpu` forces WebGPU; default picks WebGPU and falls back) |
| `?quality=high\|mid\|phone` | pin the starting quality tier (the governor only steps down from it, then back up) |
| `?gentle=1` | Gentle visuals: half band and spiral contrast and scroll, no fov kick, shake, rumble or bloom pulses, longer fades |
| `?bot=1` | autopilot (smoke tests and attract mode): follows the centreline at full throttle, restarts after the finish |
| `&botFall=1` | with `bot=1`: steers off the first open corner once, to show a drop and the rescue |
| `?touch=1` / `?touch=0` | force the touch layer on or off |

Escape only pauses. Losing focus pauses too (not under `?bot=1`).

## Checks

```bash
node --test tests/*.test.mjs            # the whole suite (the bare directory form fails on Windows)
node dev/boot-check.mjs               # headless Edge over CDP, both backends, screenshots
node dev/boot-check.mjs --cards       # no bot: start card, Enter starts, Escape pauses, a key resumes
node dev/boot-check.mjs --finish --seconds 200   # bot runs all 3 laps to the finish card
```

`boot-check.mjs` writes screenshots to `C:/wt-glassroad-run/shots/` (`--out <dir>` to change), prints a
JSON report (console errors, exceptions, fps, draw calls, triangles, events seen) and exits non-zero on any
error. It starts its own server and browser and kills only those, by PID. Other options: `--only webgpu|webgl2`,
`--flags '&gentle=1'`, `--browser <exe>`.

`window.__glassroad` holds a read-only status (phase, fps, backend, tier, drawCalls, triangles, lap,
drops, rescues, event counts) for the check and the console.

## Map

`CONTRACT.md` is the seam between parts. `boot.js` wires them in the frame-loop order there:
input, sim steps, events, fx, audio, camera, render, hud. Tuning numbers live only in `feel.json`
(units in `FEEL.md`).
