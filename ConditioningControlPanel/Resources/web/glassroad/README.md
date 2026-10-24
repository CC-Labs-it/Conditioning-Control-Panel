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

The garage now offers The Keep beside Threshold. Open `?track=keep` for its 2.9 km fortress circuit:
two laps, seven bots, a two-turn tower spiral, four swinging chains, six timed portcullises, a sluice and pool.
Solid walls and roofed halls use the accepted Keep art; close panels alternate moving media and Loom spirals.
The warning footprint brightens before a gate lowers. Pause freezes the gates; restarting resets their cycle.
Local moving media needs the isolated selected-feed preview server and a real Race click. The plain dev server supplies placeholders.
Keep currently supports solo racing only. Online admission still uses Threshold's server track card.

## Url flags

| Flag | What it does |
|---|---|
| `?track=threshold` / `?track=keep` | choose the course (also available in the garage) |
| `?feel=1` | live feel sheet: every `feel.json` number, edits apply next tick, kept in this browser, Copy JSON to paste back |
| `?renderer=webgl2` | force the WebGL2 backend (`webgpu` forces WebGPU; default picks WebGPU and falls back) |
| `?quality=high\|mid\|phone` | pin the starting quality tier (the governor only steps down from it, then back up) |
| `?gentle=1` | Gentle visuals: half band and spiral contrast and scroll, no fov kick, shake, rumble or bloom pulses, longer fades |
| `?bot=1` | the AI drives the player too (smoke tests and attract mode): the menu starts the race after 2.5 s, results go round again after 5 s |
| `&botFall=1` | with `bot=1`: the player steers off the first open corner once, to show a drop and the rescue |
| `?seed=N` | seeds the item rolls and the bots (default: random, shown as `__glassroad.seed`) |
| `?r=<token>` | a friend's race link (NET.md section 13): the lobby card claims it and seats you, waits for the host, or says why not |
| `?touch=1` / `?touch=0` | force the touch layer on or off |
| `?art=0` / `?shadows=0` / `?spirals=0` | dev: race the greybox, drop the shadow map, or drop the spiral gates |

Escape only pauses. Losing focus pauses too (not under `?bot=1`).

### Dev hook (`?dev=1`)

`?dev=1` (with or without `?bot=1`) builds `dev/hook.js` and hangs its scripted drives on `window.__glassroad.dev`
(not enumerable). Without the query the hook is never built and the sim is untouched; online it refuses. Each call
works only while racing (`status.reason` says why not) and returns `dev.status`.

| Call | What the player does |
|---|---|
| `__glassroad.dev.drift()` | put on the outside lane at the next bend ahead, drifting into it at tier 2, tier 3 a few ticks later; hop held about 1 s, then let go: a tier 3 drift boost. `{ holdS }` sets the hold; `{ release: false }` ends the script with the slide still charged |
| `__glassroad.dev.drop()` | the dare (a gap off the outside of the coil's first turn, side L): put 3 m before the opening at 25 m/s, nose 35 deg out, steered at the landing pad: kicker, flight, the catch on the coil's last turn (route-land, then route-skip). `{ leadM, speedPct, yawDeg }` |
| `__glassroad.dev.stop()` | hands back at once |
| `__glassroad.dev.status` | `{ kind, phase (placed, drifting, approach, flight, boost, held, caught, missed, cancel ..., timeout, refused), ok, reason, s, tier, land: { clean, skipM }, events }` |

A placement keeps the lap, takes the last checkpoint before the new spot (no lap credit for the jump) and snaps the
camera. Under `?bot=1` the AI gets the hands back when the script ends.

## Checks

```bash
node --test tests/*.test.mjs            # the whole suite (the bare directory form fails on Windows)
node dev/boot-check.mjs               # headless Edge over CDP, both backends: menu, Race, about 40 s of racing
node dev/boot-check.mjs --finish      # all 3 laps to the results card (about 3 min per backend)
node dev/boot-check.mjs --fall        # plus &botFall=1 and a rescue shot
node dev/boot-check.mjs --cards       # no bot: menu, Enter on Race, Escape pauses, Enter resumes
node dev/shots.mjs                    # look tour: start grid, boost, bump, then a fixed camera on each road section,
                                      # spiral gate, billboard, the sky halo, the tunnel, colossi and props
node dev/shots.mjs --tier --quality phone   # start grid and mid-race on one tier
node dev/perf-check.mjs --size 2560x1440    # frame cost, uncapped, both backends
```

`boot-check.mjs` writes screenshots to `C:/wt-glassroad-run/shots/b1/` (`--out <dir>` to change), prints a
JSON report (console errors, exceptions, fps, draw calls, triangles, place, item and screen effect events) and exits non-zero on any
error. It starts its own server and browser and kills only those, by PID. Other options: `--only webgpu|webgl2`,
`--flags '&gentle=1'`, `--browser <exe>`.

`window.__glassroad` holds a read-only status (phase: boot, menu, countdown, racing, finished, results or
paused; fps, backend, tier, drawCalls, triangles, lap, place, item, effects seen, result, seed, drops,
rescues, event counts, art: loaded / wanted and the first failed ids) for the check and the console. With
`?bot=1`, `__glassroad.peek = { s, backM, upM, latM, aheadM, liftM }` holds the camera at a spot on the lap
(`null` hands it back). The Show fps option also prints the art status (`art 43/43`, or how many failed
and the first failed id), so a machine that cannot load the art says so on screen.

## The Loom beside this tree

The spiral gates (`render/spirals3d.js`) and the media placeholders (`render/mediaPlaceholders.js`) import the
Loom from `../arcademy/engine/loom/` (owner law: every spiral comes from the Loom, never forked). The app serves
`Resources/web` whole, and `dev/serve.mjs` mounts `/arcademy/` beside this tree for the same reason. A phone
pack or any copy of glassroad must ship `arcademy/engine/loom/` beside it; without it the gates draw from their
stored params and the media placeholders use the Loom fallback frame.

## Map

`CONTRACT.md` and `CONTRACT-B1.md` are the seams between parts. `boot.js` wires them in the B1 frame-loop
order (CONTRACT-B1 section 5): menu state, input, sim steps (race, racers, contact, standings, items), events,
fx and the screen effect, audio, camera, render, hud. The page sends the host `race-result` (place, time, best
lap; XP is the desktop's) and `menu-back` from the menu's Back link. Tuning numbers live only in `feel.json`
(units in `FEEL.md`).
