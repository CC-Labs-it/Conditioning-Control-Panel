# Glassroad CONTRACT (Phase A)

THE seam between lanes. Code to it exactly. If something is wrong or missing, do the smallest thing
that works inside your own files and report it as a "contract gap"; never edit another lane's files.
Plan: `C:/wt-glassroad-run/PLAN.md` sections 2, 3.1, 3.2, 3.4, 6. Brief: `C:/wt-glassroad-run/BRIEF.md`.

## 1. Units, axes, rules

- Metres, seconds, m/s, radians in code. Degrees appear ONLY in `feel.json` (keys ending `Deg` /
  `Degps`, read with `feel.rad(path)`) and in track JSON (`roll`, converted by `loadTrack`).
- World: right-handed, +Y up. Kart and camera local frame: forward -Z, right +X, up +Y (three.js camera frame).
- Track frame at arc length s: `t` unit tangent (travel direction), `n` unit road normal (after roll),
  `b = t x n` unit RIGHT. `lateral` + = right of travel. Positive `roll` rotates n toward b, so the
  right edge sits lower (banks a right turn). Steer +1 = right, `yawRate` + = turning right.
- Vectors are plain `[x, y, z]`, quaternions `[x, y, z, w]` (`core/math.js`). Only `render/**` and
  `boot.js` import three; sim, track (except mesh upload, which lives in render), core, fx, audio, hud,
  host run under bare `node --test`.
- Feel numbers only through `core/feel.js`, read each tick (`feel.get('group.key')`); never cache a
  tuning number at module load. A key you need that is missing: a local default in your own file with
  a `// feel gap` comment, reported under contract gaps.
- Events: sim modules emit into a queue (`events` argument, `{ emit(name, payload) }`); boot flushes
  it into the bus once per frame. Nothing in sim calls fx, audio or hud directly.
- Tests: `tests/<lane>-*.test.mjs` (`sim-`, `track-`, `render-`, `fx-`, `audio-`, `hud-`, `host-`).
  Run all: `cd G && node --test tests/*.test.mjs`.

## 2. File ownership

| Owner | Files |
|---|---|
| CONTRACTS (frozen) | `core/math.js`, `core/events.js`, `core/feel.js`, `sim/state.js`, `track/schema.js`, `feel.json`, `FEEL.md`, `CONTRACT.md`, `tracks/threshold.json`, `dev/gen-threshold.mjs`, `vendor/three/**`, `tests/schema.test.mjs`, `tests/threshold.test.mjs`, `tests/contracts.test.mjs` |
| SIM | `core/clock.js`, `core/actions.js`, `input/*`, `sim/*` except `sim/state.js` |
| TRACK | `track/*` except `track/schema.js` |
| RENDER | `render/**` |
| FXAUDIO | `fx/*`, `audio/*`, `hud/*`, `host/*`, `styles.css` |
| integrator | `boot.js`, `index.html` |

Contract files stay frozen unless the integrator must change them. Vendor note: the r186 webgpu
build imports `./three.core.js`, so `vendor/three/build/three.core.js` is vendored next to it
(jsDelivr-minified r186). Addons: `three/addons/tsl/display/BloomNode.js` (imports resolve through the map).

## 3. Shared types

- `KartState`: `Float32Array(STATE_SIZE)` from `sim/state.js`; fields by `S.*` index; enums
  `BOOST {NONE, DRIFT1, DRIFT2, DRIFT3, PAD, LAUNCH}`, `RESPAWN {NONE, PULL, HOVER, RELEASE, GRACE}`.
  Helpers `createState, copyState, cloneState, lerpState(out, a, b, alpha), getPos/getQuat/getVel, setPos/setQuat/setVel`.
  `speedPct = forwardSpeed / feel speed.topMps`, clamped 0..`speed.maxPct`.
- `Actions` (`core/actions.js`): `{ steer: -1..1, throttle: 0..1, brake: 0..1, hop: boolean (held),
  hopPressed: boolean (edge), pause: boolean (edge) }`.
- `Feel` (`core/feel.js`): `createFeel(obj)`, `loadFeel(url?)`, `{ get, rad, set, subscribe, toJSON, reset }`.
- Bus / queue (`core/events.js`): `createBus() -> { on(name, fn) -> off, off, emit }` (`'*'` hears all),
  `createQueue() -> { emit, flush(bus), clear, length }`.

## 4. SIM

```js
// core/clock.js
/** @returns {Clock} */ export function createClock({ hz = 60, maxSubsteps = 2, bpm = 96 } = {})
// Clock: { dt: number /* 1/hz s */, tick: number, simTime: number, beat: { bpm, phase /* 0..1 */, index },
//   frame(nowMs) -> { steps /* 0..maxSubsteps */, alpha /* 0..1 render blend */, frameDt /* real s, <= 0.1 */, beats /* beat edges crossed */ },
//   setBpm(bpm), pause(on) /* paused: steps 0, beat holds, no catch-up on resume */, reset(nowMs) }
// Accumulator on real time: one step per 1/60 s; a frame over 20 ms may run 2 steps; time past 2 steps is dropped.
// The beat clock advances with sim time (it holds while paused). It is the ONE beat clock (countdown, bands, HUD pulse).

// core/actions.js
export function createActions() /* -> Actions, zeroed */
export function createActionMap(sources /* InputSource[] */) /* -> { sample(): Actions, dispose() } */
// sample(): once per FRAME; merges sources (steer = largest |steer|, throttle/brake max, hop OR, edges OR).
// Edges are latched in the source until the next sample(). Returns the same object each call.
// InputSource: { poll(into: Actions): void, dispose(): void }

// input/keyboard.js   W/Up throttle, S/Down brake (reverse when stopped), A/D or Left/Right steer, Shift or Space hop/drift, Escape pause
export function createKeyboard({ target = window } = {}) /* -> InputSource */
// input/gamepad.js    left stick steer, RT throttle, LT brake, A hop/drift, Start pause (standard mapping)
export function createGamepad() /* -> InputSource & { rumble(strong 0..1, weak 0..1, ms): void } */  // caller skips rumble under Gentle

// sim/kart.js
export function spawn(state, track, feel, slot = 0) /* -> state; at rest on track.spawnPose(slot), latchSeg = seg */
export function step(state, actions, track, feel, dt, events) /* -> void; one fixed tick */
// Writes every kinematic field, s, seg, segT, lateral, height, grounded, airborne, airTime, speedPct, yawRate.
// Emits drift-*, boost-*, hop, airborne, land, wall, pad, edge-enter, edge-exit, drop.
// On a drop it sets respawnPhase = PULL, respawnT = 0 and emits drop; while respawnPhase is PULL, HOVER
// or RELEASE it leaves motion alone (respawn owns the kart). During GRACE it never drops or wall-cancels.
// Walls: a side is railed per the track; with feel gentle.on the open sides act as rails too (soft rails).
// Pads: grounded and inside a boost rectangle (s in [pad.s, pad.s + len], |lateral - x| <= w / 2) starts PAD once per entry.
// Stones (wave 2, sim/stones.js): on the ground, any step from supported ground (the previous ground query was
// onRoad: the deck before the field or a stone) to a query whose hole has `lip: true` kicks the kart up by
// stones.kickUpMps along the road normal and takes off as 'lip' (the deck hands over to a void on the staggered
// lanes; the road's own edges and court holes never kick). Over a lip void the kart stays "on its own road" while within kart.snapM of the deck (no fall probe, no hole
// latch, no edge glow); below that band the hole-fall latch runs as today (drop cause 'hole', or the fall onto the road
// beneath through sim/falls.js when the track offers one). Emits stone { id, field, s, x, p } when the kart lands on or first
// drives onto a stone it was not on before (a hop that lands on the same stone is silent) and void { s, x } once per
// flight when it first drops below that band over a void (before the drop or the fall).
export function applyBoost(state, kind /* BOOST.* */, tier /* 1..3, 0 for PAD */, feel, events) /* emits boost-start */

// sim/respawn.js
export function updateRespawn(state, track, feel, dt, events) /* -> void; call every tick after step */
// PULL (pullS) -> HOVER (hoverS) -> RELEASE (releaseS, leaves at releasePct * top) -> GRACE (graceS) -> NONE.
// Anchor: node latchSeg (latched at TAKE-OFF), facing node latchSeg + respawn.aheadNodes. If the anchor is a
// jump lip or inside a gap, use the first landing node after the gap. Never over a court hole (court-paths.js
// courtRescueNode) and never inside a stone field: the last node before it (stones.js fieldRescueNode).
// Emits rescue-pull, rescue-hover, rescue-release, then land { tier: 1, airTime: 0 } when the kart touches down.

// sim/race.js
export function createRace(track, feel, events) /* -> Race */
// Race: { phase: 'ready'|'countdown'|'racing'|'finished', timeS /* since go */, lapStartS,
//   start() /* ready -> countdown */, update(dt, actions, state) /* per tick */, finish(), reset() }
// Countdown: feel juice.countdownPulses pulses, one per beat (60 / clock.bpm s): countdown {n} for n = 3,2,1, then go.
// A throttle press that starts inside the last boost.launchWindowS before go: at go, applyBoost(LAUNCH,
// boost.launchTier) and emit launch-boost. Holding throttle from before the window does not count.

// sim/progress.js
export function createProgress(track, events) /* -> { update(state, race): void, reset(state): void } */
// Per tick after respawn. Checkpoints count forward only, in order; crossing checkpoint 0 after all the
// others completes a lap. Sets state checkpoint, lap, sectionIdx. Emits checkpoint, lap, section, and
// finish (also calls race.finish()) when lap reaches track.laps.
```

## 5. TRACK

```js
// track/load.js
export function loadTrack(json, { validate = true } = {}) /* -> Track; throws Error listing schema errors */
```
Track:
```js
{ id, name, laps, loop: true, lengthM, nodeCount,
  nodeS,                 // Float64Array(nodeCount + 1), arc length at each node; nodeS[nodeCount] = lengthM
  sections,              // [{ id, act, palette, sky, bandSpeed, from, to, fromS, toS }]
  sectionAt(s) -> index,
  checklines,            // [{ idx, node, s, p, t, n, b, w }], one per checkpoints entry, in order
  boosts,                // [{ node, x, w, len, s }]  pad covers s..s+len along, x +- w/2 across
  jumps,                 // [{ node, airLen, height, s, landS }]  no road from s (lip) to landS
  limits,                // { fallY, edgeGraceM, edgeGraceS } (the sim reads only fallY; drop.* in feel.json owns the grace)
  spawnPose(slot) -> { p, q, seg, s, lateral },   // grid[slot] = [x right, z along (negative = behind)]; q faces t, up n
  nodeFlags(i) -> string[],
  frameAt(s, out?) -> Frame,       // s wraps into [0, lengthM)
  query(px, py, pz, hintSeg = -1, out?) -> Query,
  meshes: { road, rails, pads, jumps } }        // MeshData each, built on first access
```
- `Frame`: `{ p, t, n, b, roll /* rad */, w, flags, surface, seg, segT }`.
- `Query`: `{ seg, segT, s, lateral, height, normal, onRoad, edgeDist, surface, flags, railed, p }`.
  `height` is along the road normal above the surface (negative below). `edgeDist = w/2 - |lateral|`
  (+ inside, - past the edge). `onRoad` = inside the width AND the segment has road. `railed` = the
  nearest side has a rail. `p` = closest surface point. With `hintSeg >= 0` it searches a window of
  +-4 segments around the hint, else all segments; a kart far off the track still gets the nearest segment.
- Segment i runs node i -> node i+1 (wrapping). Its flags and surface are node i's; `w` and `roll`
  interpolate along it. Rails: `rail` = both sides, `railL` / `railR` = one side, none = open side.
  A segment whose start node carries `jump` is a gap: no road, no rails.
- Spline: centripetal Catmull-Rom through node `p`, closed. At a gap the curve breaks: the segment into a
  lip uses a phantom next point `lip + (lip - prev)`, the landing node a phantom previous point
  `land - (next - land)`, so the kicker keeps its pitch. Frames: parallel transport with the loop's
  closing twist spread evenly, then roll about t.
- `MeshData`: `{ positions: Float32Array, normals: Float32Array, uvs: Float32Array, indices: Uint32Array }`.
  Road: top surface, `u` = lateral metres (+ right), `v` = s metres. Rails: vertical ribbons on railed
  sides, `u` = metres up, `v` = s. Pads: one quad per boost 2 cm above the road, `u` across, `v` along
  from 0. Jumps: lip marker quads on the last metres before each lip, same uv rule.

## 6. RENDER

```js
// render/renderer.js
export async function createRenderer(canvas, { forceRenderer = null /* 'webgpu'|'webgl2'|null */, maxDpr = 2 } = {}) /* -> Renderer */
// Renderer: { backend: 'webgpu'|'webgl2', three /* WebGPURenderer */,
//   compile(scene, camera): Promise<void> /* compileAsync, behind the start card */,
//   resize(): void /* canvas client size, devicePixelRatio capped at maxDpr */,
//   render({ scene, camera, fx }): void /* scene + half-res bloom (bloomBoost) + composite */, dispose() }
// WebGPURenderer with automatic WebGL2 fallback; forceRenderer 'webgl2' sets forceWebGL. Every material TSL/node.

// render/world.js
export function buildWorld(track, feel) /* -> World */
// World: { scene, kart /* Object3D, saucer + cup greybox */, update(view): void, dispose() }
// view: { kart: KartState (interpolated), fx: FxState, timeS, beat: { phase, index }, sectionIdx }
// Builds road, rails, pads and jump markers from track.meshes; sky spiral; section palettes by fx.paletteMix.

// render/camera.js
export function createChaseCamera(feel) /* -> { camera: PerspectiveCamera, update(kart, fx, track, dt, aspect): void, snap(kart, track): void } */
// feel camera.* and fov.*; vertical fov through the lifted viewport law (vFovForAspect at 16:9 base) plus fx.fovKick;
// adds fx.shake (camera-local), fx.camPush, fx.orbit; yaw frozen while state stalled > 0.
```

## 7. FXAUDIO

```js
// fx/reactions.js (registry pattern: one reaction per event, never a switch)
export function createFx(bus, feel, { gentle, photosafe }) /* -> { state: FxState, update(dt, kart, race): void, reset(): void } */
// fx/photosafe.js
export function createPhotosafe(feel) /* -> { request(kind: 'bloom'|'band'|'palette'|'flash'|'red'): boolean, update(dt): void, used(): number } */
// rolling photosafe.windowS window, at most photosafe.maxTransitions granted; 'red' has its own budget.
// fx/gentle.js
export function createGentle(feel) /* -> Gentle */
// Gentle: { on, fadeMs, riseMs, set({ on, fadeMs, riseMs, speed }): void /* also feel.set('gentle.on', on) */,
//   rise(s): number /* max(s, riseMs/1000) when on */, fade(s): number, kick(v): number /* 0 when on */,
//   contrast(v): number, scroll(v): number /* x gentle.contrastMul / scrollMul when on */ }
// fx/shake.js
export function createShake(feel) /* -> { kick(kind: 'hit'|'boost'): void, update(dt): number[3] } */  // [0,0,0] under Gentle
```
`FxState` (numbers unless noted; rest values in brackets):
`shake` [0,0,0] camera-local metres; `fovKick` [0] degrees; `camPush` [0] metres toward the kart;
`bloomBoost` [0] 0..1; `bandSpeedMul` [1]; `bandBright` [0] 0..1; `paletteMix` [{from:0,to:0,t:1}] (+ `base` {sectionIdx: weight} when a gate came mid-fade, the blend to start from, and `waitS`, the one beat the old palette lingers);
`sparkTier` [0] 0..3; `sparkSide` [0] -1|1; `sparkBurst` [0] 0..1; `squash` [0] + flatter / - stretched;
`dust` [0] 0..1; `padRing` [0] 0..1 progress (ring scale); `padGlow` [0] 0..1 pad and ring brightness; `speedLines` [0] 0..1; `edgeGlow` [0] 0..1; `edgeSide` [0];
`rescueGlow` [0] 0..1; `tendril` [0] 0..1; `whiteFade` [0] 0..`respawn.pullWhite`; `flashLevel` [0] 0..1;
`orbit` [0] 0..1 of `juice.finishOrbitDeg` (halved under Gentle); `rumble` [0] 0..1 gamepad rumble; `duck` [0] 0..1 audio duck. Every luminance change asks photosafe first;
Gentle zeroes fovKick, shake, bloom pulses and rumble, and applies the rise floor to every IN.
The road's beat breath (render LOOK.beatBreath, 6%, soft rise, no step) is NOT a photosafe transition:
owner call whether a sub-10% swell should count.

```js
// audio/  (bus.js engine.js cues.js; one key, one clock, one variable: speedPct)
export function createAudio(feel, bus, { gentle }) /* -> Audio */   // in audio/bus.js
// Audio: { start(): Promise<void> /* from the Start click */, update(speedPct, flags): void, cue(name, { volume = 1, pitch = 1 } = {}): void,
//   reset(sectionIdx) /* Restart: release held tones, pad to that section's chord */, setPaused(on): void, dispose() }
// flags: { boosting, drifting, driftTier, airborne, onRoad, edge, paused, duck }. Subscribes to the bus for cues itself.
// WebAudio synthesis only; NO speechSynthesis or TTS.

// hud/hud.js
export function createHud({ root, card }, feel, bus) /* -> Hud */
// Hud: { setCard(kind: 'start'|'pause'|'finish'|null, data?): void, setReady(ready, note?): void,
//   onStart(fn), onResume(fn), onRestart(fn), update({ lap, laps, timeS, speedPct, countdown, sectionIdx, paused }): void }
// hud/feelSheet.js
export function openFeelSheet(feel, root) /* ?feel=1: one row per feel.json key, edits through feel.set */

// host/bridge.js  protocol v2 (plan 3.4)
export function createBridge({ win = window } = {}) /* -> Bridge */
// Bridge: { hosted /* window.chrome.webview present */, protocol: 2, init: Promise<Init>,
//   on(type, fn) -> off /* buffers early messages per type */, send(msg), ready() /* posts { type: 'ready', protocol: 2 } */,
//   log(level, msg) }
// Answers ping with pong and sends heartbeat itself while hosted.
// host/hostEscape.js  lifted from dtrh/race/hostEscape.js: createHostEscape({ win, doc }) -> { kept(): boolean, dispose() }
```
`Init`: `{ v: 2, gentle: { on, fadeMs, riseMs, speed }, caps: { platform: 'desktop'|'web'|'phone', net,
relayBase, texturableRemote, gamepad, touch, forceRenderer: 'webgpu'|'webgl2'|null, hostSfx, trackPick,
canSurface, mediaControls, songs }, identity: { unifiedId, displayName, tier } | null }`.
`caps.songs`: same-origin race song chart urls, each a plain path to a .json file (`host/bridge.js` SONG_URL, no
`..`, at most 8), tried in order until one loads. Web sends `['/glassroad-audio/pink-road/chart.json']`, desktop `[]`
(the private song never ships in the public repo or the installer). Absent or empty = no song. `?chart=<url>` /
`?chart=0` and the dev host (8790, its `/private-audio/` list) win over it (`songCharts`).
When nothing named a song and the page has a lobby link (`caps.net`), the page asks the lobby once for signed song
urls (`GET /v2/race/songs`, NET.md section 14) and plays them straight from the room origin (`host/bridge.js`
`lobbySongCharts`, `ROOM_ORIGIN`): a host never relays song bytes, and a desktop or phone host simply keeps sending
`songs: []`. Such a host's page must be allowed to fetch `https://glassroad-room.cclabs-race.workers.dev` (CORS `*`).
Browser defaults (unhosted): `gentle.on` from `?gentle=1`, fadeMs 600, riseMs 300, speed 0.5;
caps platform 'web', net false, relayBase null, texturableRemote false, gamepad true, touch from
`(pointer: coarse)`, forceRenderer from `?renderer=webgl2|webgpu` else null, songs `[]`, the rest false; identity null.
Host -> page used in A: `init`, `pause {on}`, `kept-escape`, `gentle {...}`, `focus`, `ping`.
Page -> host in A: `ready`, `heartbeat`, `pong`, `run-started`, `run-ended { timeS, laps }`, `telemetry`.
Escape only pauses; the page never sends `exit` on Escape.

## 8. Events (the 10 juice beats)

| Event | Payload | Emits | Consumes |
|---|---|---|---|
| `countdown` | `{ n: 3\|2\|1 }` | race | fx (bandBright, bloom step), audio, hud |
| `go` | `{}` | race | fx (bloom release), audio, hud |
| `launch-boost` | `{ tier }` | race | fx (camPush), audio |
| `drift-start` | `{ dir: -1\|1 }` | kart | fx (sparks), audio (drift tone) |
| `drift-tier` | `{ tier: 1..3, dir }` | kart | fx (sparkTier, rumble), audio (pitch step) |
| `drift-release` | `{ tier: 0..3 }` (0 = no boost) | kart | fx (burst), audio (glide down) |
| `drift-cancel` | `{ reason: 'slip'\|'slow'\|'wall'\|'stall' }` | kart | fx, audio |
| `boost-start` | `{ kind: 'drift'\|'pad'\|'launch', tier, amount, holdS }` | kart (applyBoost) | fx (fovKick, speedLines, bandSpeedMul, shake boost), audio (low-pass opens) |
| `boost-end` | `{ kind }` | kart | fx, audio (ease back over boost.tailTcS) |
| `hop` | `{ speed }` | kart | fx (squash then stretch), audio |
| `airborne` | `{ cause: 'lip'\|'edge' }` | kart | fx (stretch) |
| `land` | `{ tier: 0..3, airTime }` (0 = under air.lightS) | kart, respawn | fx (squash, dust, shake on 3), audio (cue on 2 and 3) |
| `edge-enter` | `{ side: -1\|1 }` | kart | fx (edgeGlow), audio (hum) |
| `edge-exit` | `{ to: 'road'\|'drop' }` | kart | fx (glow fade or hand over), audio |
| `drop` | `{ seg, cause: 'edge'\|'fallY' }` | kart | fx, audio (duck to one tone) |
| `rescue-pull` | `{}` | respawn | fx (tendril, rescueGlow, whiteFade), audio |
| `rescue-hover` | `{}` | respawn | fx |
| `rescue-release` | `{}` | respawn | fx (release pulse), audio (duck lifts over juice.duckLiftS) |
| `pad` | `{ index }` | kart | fx (padRing, bandBright via photosafe), audio |
| `phrase-gate` | `{ id, course, index, gentle }` (local forward crossing only) | phrase gate visual, same frame as pulse/text start | themed pentatonic note on existing SFX bus |
| `press-thud` | `{ id, gain, gentle }` (nearest visible press contact within 48 m) | physical press descent already supplies the motion | quiet low impact on existing SFX bus, no replay after seeks |
| `section` | `{ from, to }` (section indices) | progress | fx (paletteMix over juice.paletteFadeS), audio (chord), hud |
| `checkpoint` | `{ idx }` | progress | hud |
| `wall` | `{ dv, side }` (dv = normal speed lost, m/s) | kart | fx (hit shake when large), audio (volume dv / wall.cueFullDvMps) |
| `lap` | `{ lap, lapTimeS, timeS }` | progress | hud, audio |
| `finish` | `{ timeS }` | progress | fx (flashLevel swell, orbit), audio, hud, boot (finish card) |
| `pause` | `{ on }` | boot | audio, hud, fx |
| `surface` | `{ kind, from }` (ground surface kind changed: glass, grip, haze, stream, pool) | kart | fx (fov lift entering stream) |
| `splash` | `{ s, x, speed, down }` (landing onto a pool, down = vertical m/s, positive down) | kart | fx (fov dip, white ring), audio |
| `chute` | `{ s }` (first drive onto a stream node steeper than water.chuteGradeDeg) | kart | fx (fov lean, lip spray) |
| `stone` | `{ id, field, s, x, p:[worldX,worldY,worldZ] }` (distinct grounded platform contact; same-platform hops are silent; player emits bare, bots use racer-event wrapper) | kart | platform highlight, landing sparkles and local ascending note; existing soft bob |
| `void` | `{ s, x }` (once per flight: the kart first drops below the deck over a field's void, before the drop or the fall) | kart | fx (fov dip, short darkening) |

## 9. Frame loop (boot.js, every requestAnimationFrame)

```js
const { steps, alpha, frameDt } = clock.frame(now);
const a = actionMap.sample();                   // 1. input, once per frame
// cards: the pad's own edges (Start / A / RT press) resume, start and restart; Escape and the pad's
// Start only ever PAUSE from play (also window blur, host pause {on}, kept-escape)
if (card) padEdgesWorkTheCard(); else if (a.pause && !paused) setPaused(true);
if (paused) hopLatch.clear(); else hopLatch.hold(a);   // core/actions.js createHopLatch: an edge seen on a
                                                // frame with steps 0 (most frames above 60 Hz) waits
if (!paused) for (let i = 0; i < steps; i++) {  // 2. sim steps
  copyState(prev, cur);
  race.update(clock.dt, a, cur);
  if (race.phase === 'racing' || race.phase === 'finished') step(cur, race.phase === 'finished' ? COAST : a, track, feel, clock.dt, queue);
  updateRespawn(cur, track, feel, clock.dt, queue);
  progress.update(cur, race);
  hopLatch.spent(a);                            //    an edge is consumed by the first step that runs
}
queue.flush(bus);                               // 3. events -> fx, audio, hud handlers
fx.update(frameDt, cur, race);                  // 4. fx
audio.update(cur[S.speedPct], flags);           // 5. audio
lerpState(view, prev, cur, alpha);              // 6. camera
camera.update(view, fx.state, track, frameDt, aspect);
world.update({ kart: view, fx: fx.state, timeS: clock.simTime, beat: clock.beat, sectionIdx: cur[S.sectionIdx] });
renderer.render({ scene: world.scene, camera: camera.camera, fx: fx.state });   // 7. render
hud.update({ lap, laps, timeS, speedPct, countdown, sectionIdx, paused });      // 8. hud
```
Pause calls `clock.pause(on)`, `audio.setPaused(on)`, `hud.setCard('pause')` and emits `pause`. The
start card stays up until `renderer.compile` resolves; Start runs `audio.start()` then `race.start()`.
