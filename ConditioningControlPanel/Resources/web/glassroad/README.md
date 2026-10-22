# Glassroad (Racing Thoughts 3D), Phase B1: the solo race

A full race on Threshold (3 laps) against 7 EMI bots: a menu (Race, Online "soon", Options, Controls),
effect-bubble items, contact and slipstream, places and a results card, pause with Options, the item
screen effects, juice for every kart and the B1 audio mix. On top of Phase A: drift and boost, hop and
landings, falls and the rescue, the chase camera, the 10 juice beats and the live feel sheet.
The seam between the B1 lanes is `CONTRACT-B1.md`; Phase A's is `CONTRACT.md`.
Plain ES modules and an import map; three.js r186 is vendored in `vendor/three/`. No build step.

## Play locally

```bash
cd ConditioningControlPanel/Resources/web/glassroad
node dev/serve.mjs            # http://127.0.0.1:8790/ (port: first argument or PORT)
```

Open http://127.0.0.1:8790/?feel=1 in Edge or Chrome and press Race.

Keys: W / Up throttle, S / Down brake (reverse when stopped), A / D or Left / Right steer,
Shift or Space hop and drift (hold through a turn, let go for the boost), E item (hold S / Down with E
to drop it behind as a trap), Escape pause. A standard gamepad works too (left stick, RT, LT, A, X item,
stick down + X drops, Start; the d-pad, A and B work the cards). Touch: tap the round item slot.

## Url flags

| Flag | What it does |
|---|---|
| `?feel=1` | live feel sheet: every `feel.json` number, edits apply next tick, kept in this browser, Copy JSON to paste back |
| `?renderer=webgl2` | force the WebGL2 backend (`webgpu` forces WebGPU; default picks WebGPU and falls back) |
| `?quality=high\|mid\|phone` | pin the starting quality tier (the governor only steps down from it, then back up) |
| `?gentle=1` | Gentle visuals: half band and spiral contrast and scroll, no fov kick, shake, rumble or bloom pulses, longer fades |
| `?bot=1` | the AI drives the player too (smoke tests and attract mode): the menu starts the race after 2.5 s, results go round again after 5 s |
| `&botFall=1` | with `bot=1`: the player steers off the first open corner once, to show a drop and the rescue |
| `?seed=N` | seeds the item rolls and the bots (default: random, shown as `__glassroad.seed`) |
| `?touch=1` / `?touch=0` | force the touch layer on or off |

Escape only pauses. Losing focus pauses too (not under `?bot=1`).

## Checks

```bash
node --test tests/*.test.mjs            # the whole suite (the bare directory form fails on Windows)
node dev/boot-check.mjs               # headless Edge over CDP, both backends: menu, Race, about 40 s of racing
node dev/boot-check.mjs --finish      # all 3 laps to the results card (about 3 min per backend)
node dev/boot-check.mjs --fall        # plus &botFall=1 and a rescue shot
node dev/boot-check.mjs --cards       # no bot: menu, Enter on Race, Escape pauses, Enter resumes
```

`boot-check.mjs` writes screenshots to `C:/wt-glassroad-run/shots/b1/` (`--out <dir>` to change), prints a
JSON report (console errors, exceptions, fps, draw calls, triangles, place, item and screen effect events) and exits non-zero on any
error. It starts its own server and browser and kills only those, by PID. Other options: `--only webgpu|webgl2`,
`--flags '&gentle=1'`, `--browser <exe>`.

`window.__glassroad` holds a read-only status (phase: boot, menu, countdown, racing, finished, results or
paused; fps, backend, tier, drawCalls, triangles, lap, place, item, effects seen, result, seed, drops,
rescues, event counts) for the check and the console.

## Map

`CONTRACT.md` and `CONTRACT-B1.md` are the seams between parts. `boot.js` wires them in the B1 frame-loop
order (CONTRACT-B1 section 5): menu state, input, sim steps (race, racers, contact, standings, items), events,
fx and the screen effect, audio, camera, render, hud. The page sends the host `race-result` (place, time, best
lap; XP is the desktop's) and `menu-back` from the menu's Back link. Tuning numbers live only in `feel.json`
(units in `FEEL.md`).
