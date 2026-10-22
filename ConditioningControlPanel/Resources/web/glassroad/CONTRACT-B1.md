# Glassroad CONTRACT-B1 (the full solo race)

THE seam between the six B1 lanes. `CONTRACT.md` (Phase A) still holds wherever this file is silent.
Code to it exactly. If it is wrong or missing something, do the smallest thing that works inside your own
files and report it under "contract gaps"; never edit another lane's files. Brief: `C:/wt-glassroad-run/BRIEF-B1.md`.
Design: the board (loop, contact, power-ups, race screen, menu, pause, results); the board wins over PLAN.md.

## 1. File ownership

| Lane | Owns (new files allowed inside) | Tests (new files only) |
|---|---|---|
| RACE | `sim/racers.js`, `sim/ai.js`, `sim/contact.js`, `sim/standings.js`, new `sim/race-*.js` helpers | `tests/sim-racers*`, `sim-ai*`, `sim-contact*`, `sim-standings*` |
| ITEMS | `sim/items.js`, `sim/itemTable.js`, `fx/effects.js`, new `sim/items-*.js` helpers | `tests/sim-items*`, `fx-effects*` |
| JUICE | `render/**` except `render/screenfx.js` and `render/items/**`; `fx/cues.js`, `fx/reactions.js`, `fx/shake.js` (new registry keys and FxState fields only) | `tests/render-*` (pure helpers), `fx-juice*` |
| SCREENFX | `render/screenfx.js`, `render/items/**` | `tests/render-screenfx*`, `render-items*` (pure helpers) |
| HUD | `hud/**`, `styles.css`, everything it injects into `#hud` and `#card` | `tests/hud-*` |
| AUDIO | `audio/**` (incl. new `audio/music.js`, `audio/sfx/*`, `audio/SFX.md`) | `tests/audio-*` |
| integrator | `boot.js`, `index.html`, `dev/**`, `core/actions.js`, `input/*`, `README.md` | `tests/boot-*` |
| contracts (frozen, done) | `CONTRACT-B1.md`, `feel.json`, `FEEL.md`, `sim/grant.js`, `track/schema.js`, `track/load.js` (items only), `tracks/threshold.json`, `dev/gen-threshold.mjs`, `art/fx/*`, `art/ART.md`, `tests/contracts-b1.test.mjs` | |

Every other Phase A file is frozen except by the integrator. A lane may READ and import anything. Never
delete or weaken a Phase A test; if your own change breaks one in your own area, fix the test minimally and
report it. A feel key you need that is missing: a local default with a `// feel gap` comment, reported.
`sim/`, `track/`, `core/`, `fx/`, `hud/`, `audio/` never import three.

## 2. The multi-kart model

- `KartState` and `sim/state.js` do NOT grow in B1 (no new index constants, STATE_SIZE unchanged). Per-racer
  extras live on the `Racer` object below. `sim/kart.js`, `respawn.js`, `race.js`, `progress.js` are called
  per racer unchanged.
- Racer 0 is the local player. Racers 1..7 are bots. Grid slot of the player = `ai.playerSlot`; bots fill
  the other `track.spawnPose(slot)` slots in order.
- Per-racer modifiers reach the unchanged sim two ways (no kart.js edit):
  1. `act` (what `kart.step` gets) = `input` with `steer = clamp(input.steer * mods.steerMul + mods.steerAdd, -1, 1)`,
     `throttle = input.throttle * mods.throttleMul`, the rest copied.
  2. A per-racer feel OVERLAY (`createFeelOverlay`): `get(path)` returns `over[path]` when set, else the base
     feel. Each tick before `step` racers set `kart.gripTcS = gap(base) * mods.gripMul`, `drift.slideTcS = gap(base) * mods.slideMul`,
     `speed.topMps = base * mods.capMul * paceMul` (bots: paceMul = `ai.paceMul[k-1] * ai.difficultyMul[ai.difficulty]` times the
     rubber band; player 1), and `gentle.on = true` when `racer.assist` (soft rails for the player only). `set`, `subscribe`,
     `reset`, `toJSON` pass to the base. Gaps (`sim/gaps.js`) work through it unchanged.
- Spin (Shatter): ITEMS writes the kart directly at the hit: `st[S.stalled] = items.shatterS` (kart.js already freezes yaw,
  clears steering, thrust x `stall.thrustMul`), `st[S.engineV] *= items.shatterSpeedKeep`, `cancelDrift` + `endBoost`
  (import from `sim/drift.js`, `sim/boost.js`). The visual spin is `fxv.spinRad` (render only).
- Event routing: racer 0 emits into the frame queue exactly as in Phase A (every Phase A consumer keeps
  working). A bot's sink re-emits each of its events as `racer-event { racer, name, p }`. RACE's sinks also
  record `lap` and `finish` per racer before forwarding.
- Boosts from B1 sources go through `sim/grant.js` `grantBoost(st, amount, holdS, kind, feel, events)`
  (kind `'slip'|'surge'|'bubble'`; runs as BOOST.PAD; boost-end still says `'pad'`; a smaller grant never cuts a bigger boost).

```js
/** @typedef {{ i: number, id: string /* 'you' | 'bot1'..'bot7' */, name: string /* 'You' | 'EMI bot' */, isBot: boolean,
 *   tint: number /* 0 = player, 1..7 */, slot: number }} RacerMeta */
/** @typedef {{ steerMul: number, steerAdd: number, throttleMul: number, gripMul: number, slideMul: number,
 *   capMul: number, shoveMul: number, blind: number /* 0..1, bots: view lost */ }} Mods   // identity: 1,0,1,1,1,1,1,0 */
/** @typedef {{ spinRad: number, shield: null|'glass'|'golden', echo: number /* copies left */, effect: null|string /* item id */,
 *   ghost: boolean, slip: number /* 0..1 wake charge */, place: number }} RacerFx
 *   writers: spinRad, shield, echo, effect = ITEMS; ghost, slip = RACE contact; place = RACE standings. Render reads only. */
/** @typedef {{ i: number, meta: RacerMeta,
 *   st: Float32Array, prev: Float32Array, view: Float32Array,   // view = lerpState(prev, st, alpha)
 *   input: Actions,      // intent: player = sampled actions; bot = ai.drive output; incl. item (edge) and back (held)
 *   act: Actions,        // after mods, fed to kart.step
 *   feel: Feel,          // the overlay
 *   mods: Mods,          // ITEMS resets to identity and rewrites every tick; RACE and contact read
 *   item: { id: string|null, rolling: boolean, rollT: number, heldT: number },   // ITEMS only writes
 *   fxv: RacerFx, assist: boolean,
 *   raceView: { phase, timeS, lapStartS, finish() },   // player: the real race; bots: RACE's proxy on racers.clockS
 *   lapTimes: number[], bestLapS: number, finished: boolean, finishS: number,
 *   progressM: number, place: number }} Racer */
```

`Actions` (integrator extends `core/actions.js`, OR-merged like hop): `item: boolean` (edge, latched like the
hop edge until a step runs), `back: boolean` (held: keyboard S / Down, gamepad stick down past 0.5, touch: none).
Keyboard E = item, gamepad X = item, touch: tap the item slot.

## 3. Signatures

```js
// sim/racers.js (RACE)
export function createFeelOverlay(feel) /* -> Feel & { over: Object<string, any> } */
export function createRacers(track, feel, queue, { count = 8, seed = 1 } = {}) /* -> Racers */
// Racers: { list: Racer[], player: Racer, clockS /* s since go, keeps running after the player finishes */,
//   place(): void            /* spawn all on the grid; reset progress, item, mods, fxv, laps; clockS = 0 */,
//   step(dt, a, race, ctx /* { ai, items, standings } */): void
//        /* per racer: copyState(prev, st); input (player: copy of a, or ai.drive autopilot once finished; bot: ai.drive);
//           act from mods; overlay; step only while race.phase is 'racing' or 'finished'; updateRespawn; progress.update */,
//   interpolate(alpha): void, setAssist(on): void,
//   nearby(n, maxM): Array<{ i, distM, pan /* -1..1, + right of the player kart */, speedPct, boosting }> }

// sim/ai.js (RACE)
export function createAi(track, feel, { seed = 1 } = {}) /* -> { drive(r, ctx, dt, out): void, reset(): void } */
// ctx: { racers: Racer[], items, standings, race }. Writes out.steer/throttle/brake/hop/hopPressed/item/back.
// Line: own lateral (lineSpreadM) + wander + corner cut, clear of open edges by openEdgeMarginM, around karts and
// traps within avoidM; hop drifts on bends (driftCurveDegPerM, held >= driftMinS); launch at go with launchChance;
// rubber band on top speed vs the player's progress; mods.blind adds steer noise and lifts throttle;
// items: holds >= itemHoldMinS, then itemUseChancePerS, cooldown itemCooldownS, throws only with a target within
// itemRangeM, front bots drop throws behind with itemDropChance. Bots use items less often than people.

// sim/contact.js (RACE)
export function createContact(track, feel, events) /* -> { update(dt, racers, race): void, reset(): void } */
// Pairs closer than 2 * contact.radiusM in 3D and within maxHeightM along the road normal push apart along the
// line between them, projected on the road plane: positions separate, sideways speed min(maxPushMps, pushMps +
// pushPerDv * closing) split by weight (1 + speed / speedRefMps, x boostShoveMul when boosting, / stalledShoveMul
// when stalled, x mods.shoveMul), engineV x (1 - engineLoss). No spin, no stall. A push may carry a kart past an
// open edge (the Phase A drop rule takes it) EXCEPT when feel gentle.on or the kart's assist: then lateral is
// clamped inside the road. Ghosts (no contact): clockS < ghostS after go, respawnPhase != NONE (incl. the 1.5 s grace).
// Slipstream: within slipMinM..slipMaxM behind a kart (wrap-aware s), |dLateral| < slipWidthM, both speedPct >
// slipMinPct, both grounded: charge; after slipChargeS grantBoost(slipAmount, slipHoldS, 'slip'), cooldown slipCooldownS.

// sim/standings.js (RACE)
export function createStandings(track, feel, events) /* -> Standings */
// Standings: { order: number[] /* racer idx by place */, update(racers, race, dt): void, placeOf(i): number /* 1..N */,
//   progressOf(i): number, gapToLeader(i): number /* m >= 0 */, result: Result|null, reset(): void }
// progressM = lap * lengthM + d, d = wrap(s - startS) with d -= lengthM while checkpoint === 0 and d > lengthM / 2
// (karts behind the line). Finished racers rank by finishS ahead of every unfinished one.
// Result (final once the player finished AND (all finished OR ai.resultsWaitS passed); unfinished get
// finishS = clockS + left / max(their mean speed, 5) and finished: false):
// { track, laps, rows: [{ place, i, name, isBot, me, timeS, bestLapS, finished }], me: { place, timeS, bestLapS } }

// sim/itemTable.js (ITEMS)
export const ITEM_IDS = ['flash', 'spiral', 'brainDrain', 'freeze', 'blackout', 'pink', 'melt', 'shatter', 'surge', 'glass', 'echo', 'golden'];
export const ITEMS /* id -> { use: 'throw'|'lob'|'wide'|'trap'|'self', view: 'full'|'mild'|'none'|null, bot: 'blind'|'steer'|'ice'|'spin'|null, icon } */;
//   flash throw full blind | spiral throw full steer | brainDrain throw full blind | freeze throw mild steer |
//   blackout lob full blind | pink wide mild blind | melt trap full ice | shatter throw none spin |
//   surge, glass, echo, golden: self, view null, bot null.   icon = 'art/fx/<file>.png' per art/ART.md "fx".
export const BUCKETS = ['front', 'middle', 'back'];
export function bucketOf(feel, place, gapToLeaderM) /* place 1 or gap <= frontGapM: front; gap >= backGapM: back; else middle */
export function weightsFor(feel, bucket) /* -> [id, weight][] from items.<id>Weights, weight > 0 only */
export function rollItem(feel, bucket, rand /* () => 0..1 */) /* -> id */
export function durationOf(feel, id) /* -> items.<id>S; glass -> items.glassMaxS */

// sim/items.js (ITEMS)
export function createItems(track, feel, events, { seed = 1 } = {}) /* -> Items */
// Items: { update(dt, racers, standings, race): void, view(): ItemsView /* same object each call */, reset(): void,
//   nearOpenEdge(s, m): boolean }
// update runs only while race.phase is 'racing' or 'finished' (the player stops using items once finished).
```

**Item rules (sim/items.js).** Boxes and bubbles come from `track.items` (`{ kind: 'box'|'bubble', node, x, ds, s }`).
- Pickup: grounded or below `pickHeightM`, within `boxRadiusM` along and across of a live box, slot empty and not rolling:
  box gone for `boxRespawnS`; draw now with a seeded rand from `bucketOf(place, gapToLeader)`; slot rolls `rollS`
  (`item-box`), then shows (`item-roll`). Bubbles: within `bubbleRadiusM`: `grantBoost(bubbleAmount, bubbleHoldS, 'bubble')`, gone `bubbleRespawnS` (`bubble-pop`).
- Use (`input.item` edge, item shown): `back` held drops any throw or lob item as a trap `dropBehindM` behind (same kind);
  melt is always a trap behind; otherwise by `use`:
  throw = projectile along the driveline at user speed + `throwExtraMps`, `throwHeightM` up, locks on the kart directly
  ahead in progress within `throwRangeM` (lateral eases to it, `homeTcS`), hits within `hitRadiusM`, else pops at `throwLifeS`
  (`fizzle`) or on any kart it meets. lob (blackout) = arc (`lobHeightM`) that reaches the leader (place 2 when the user leads)
  in `lobFlightS`. wide (pink) = instant hit on every kart ahead within `pinkRangeM` of progress, plus a wave in the view.
  self: surge `grantBoost(surgeAmount[bucket], surgeS, 'surge')`; glass shield until one hit or `glassMaxS`; echo `echoCopies`
  for `echoS`; golden for `goldenS`. Traps: live `trapLifeS`, cannot hit their layer for `trapArmS`, hit within `trapRadiusM`
  (`meltRadiusM` for melt), at most `maxTraps` (oldest pops).
- Shatter never fires, lands or is laid within `shatterEdgeSafeM` of an open segment (`nearOpenEdge`): a press there
  keeps the item and emits `item-refused { racer, id, reason: 'edge' }`; a Shatter throw closing on a victim near an open
  edge waits behind them until clear or its life ends.
- Hit verdict, in order: victim golden -> `golden` (nothing); glass -> `glass` (shield pops); throw or lob at an Echo
  kart with copies and rand < `echoDecoyChance` -> `decoy` (a copy goes); `view` full or mild and (victim's last screen
  effect ended < `mercyS` ago, or the rolling `viewWindowS` ledger has no room: the hit is trimmed to what is left of
  `viewBudgetS`, mild kinds count `mildBlockMul`, under `minHitS` -> `mercy`); else `hit` with `durationS`.
  A screen effect occupies a victim for durationS + `effectOutS`; one at a time per victim, a new hit replaces it.
- On `hit`: mods for its duration: spiral `steerAdd = spiralSteerAmp * sin(2 pi spiralSteerHz t)`; freeze `steerMul =
  freezeSteerMul`; melt `gripMul = meltGripMul`, `slideMul = meltSlideMul`; shatter: the spin (section 2); bots also get
  `blind = 1` for blind kinds. golden: `shoveMul = goldenShoveMul`. fxv.effect = kind while it runs.
- Bot victims play no screen effect; the player (racer 0) gets one through `fx/effects.js`.

```js
/** ItemsView (sim coordinates, world metres; p = road surface point, n = road normal; render adds hover heights)
 * { boxes: [{ i, p, n, t, b, alive, k /* 0..1 grow-in after respawn */ }],
 *   bubbles: [{ i, p, n, alive, k }],
 *   held: [{ racer, id, rolling, rollT }],                  // one per racer with an item or a roll
 *   projectiles: [{ id, kind, mode: 'throw'|'lob', p /* incl. height */, t, target }],
 *   traps: [{ id, kind, p, n, t, armed }],
 *   waves: [{ id, kind: 'pink', from, p, t, rangeM }],
 *   shields: [{ racer, kind: 'glass'|'golden'|'echo', t, k /* 0..1 left */, copies }],
 *   pops: [{ kind, at: 'box'|'bubble'|'hit'|'glass'|'golden'|'decoy'|'trap'|'fizzle', p, t }] }   // kept popShowS */

// fx/effects.js (ITEMS): the LOCAL player's screen effect
export function createEffects(bus, feel, { gentle, photosafe }) /* -> { screen: ScreenState, update(dt): void, reset(): void, dispose(): void } */
// Listens to item-hit with victim 0, verdict 'hit' and ITEMS[kind].view (full or mild) or kind 'shatter'.
// One at a time: a new hit replaces (effect-end { kind, replaced: true }, then effect-start); the old one becomes
// prevKind / prevK and fades over items.effectInS (no hard cut). IN = items.effectInS (gentle.rise floor), held to
// durationS, OUT = gentle.fade(items.effectOutS). Every start asks photosafe ('flash' for flash and blackout,
// 'palette' for the rest); refused -> IN = max(IN, items.photosafeInS). Mercy is decided in sim/items.js only.
// Gentle (the player's): flash -> params.corner, scale items.gentleFlashScale; spiral -> params.dimOnly, strength
// items.gentleSpiralDim; params.blurMul = items.gentleBlurMul; params.flicker = false. Emits effect-start
// { kind, durationS, by } and effect-end { kind, replaced } on the bus. Boot sets fx.state.screen = effects.screen.
/** ScreenState { kind: 'none'|'flash'|'spiral'|'brainDrain'|'freeze'|'blackout'|'pink'|'melt'|'shatter',
 *   t /* s since start */, durS, k /* 0..1 envelope */, strength /* 0..1 */, prevKind, prevK,
 *   params: { gentle, corner, dimOnly, blurMul, flicker, cover /* items.flashCover */, seed, by } } */
```

```js
// render/screenfx.js (SCREENFX)
export function createScreenFx(three /* WebGPURenderer */) /* -> { node(sceneTex): Node /* vec3 */, update(screen, dt, aspect): void, dispose(): void } */
// node() takes the scene pass texture node and returns the colour that replaces `col.rgb` in render/post.js;
// it may sample sceneTex at offset uvs (blur, run-down). Loads its own textures (art/fx PNGs via TextureLoader; the
// Flash picture in B1 is a spiral card). NEVER fully blind: post adds bloom AFTER this node, so rails and the
// racing line glow through every effect; Blackout keeps a circle; Melt has no free centre but stays readable.
// Look numbers in render/items/look.js. kind 'none' with prevK 0 returns sceneTex.rgb (no cost beyond one sample).
// render/items/visuals.js (SCREENFX)
export function createItemVisuals(scene, art, feel) /* -> { update(itemsView, dt, karts): void, dispose(): void } */
// Glass sphere + effect icon moving inside + a ring of drifting motes (lift dtrh game/bubbleLife.js, chaosField.js):
// boxes = spinning DtRH cards that turn into a bubble (art 'card-*' models when present), held bubble bobbing over
// each kart, throws in an arc, lobs high, traps (melt: a melting spiral puddle with wobbly dripping arms), pink wave,
// shields (glass bubble, gold, echo copies: no flicker under Gentle), pops. Phone tier: about half the motes.
// JUICE wiring: buildWorld creates it and calls update(view.items, dt, view.karts); post.js uses createScreenFx.

// render/world.js (JUICE)
export function buildWorld(track, feel, { art, bus, seed, count = 8 } = {}) /* -> { scene, kart, karts: Object3D[], update(view), dispose() } */
// view: { kart /* = karts[0].st, Phase A compat */, karts: [{ st /* interpolated */, meta, fxv }], fx /* FxState + screen */,
//   timeS, beat, sectionIdx, items: ItemsView, mode: 'menu'|'race'|'results' }
// Rival karts tinted by meta.tint (LOOK.kartTints), shatter spin from fxv.spinRad, ghost look, slipstream wake from
// fxv.slip, name tags over rivals within 40 m only, bot drift sparks and boosts read from their states. Menu: the
// kart turns in the background under the live spiral sky. Camera: createChaseCamera keeps its API;
// update(kart, fx, track, dt, aspect, mode = 'race') ('menu' = slow attract orbit).
// renderer.render({ scene, camera, fx, dt }) passes dt to post.update(fx, dt).
```

```js
// hud/hud.js (HUD): Phase A API kept (setCard, setReady, update, onStart, onResume, onRestart, credit, dispose, formatTime)
// Hud adds: on(name, fn) -> off; names: 'race' | 'online' | 'options' | 'controls' | 'back' | 'resume' | 'restart' |
//   'quit' | 'again' | 'menu' | 'option' ({ key, value, options })
// setCard(kind: 'menu'|'pause'|'options'|'controls'|'results'|null, data)  ('start' and 'finish' stay for Phase A)
//   menu { title, track, laps } two doors Race / Online ("soon"), links Options, Controls, Back
//   pause { track, lap, laps, place, count } Resume, Restart, Options, Quit to menu
//   options { from: 'menu'|'pause', hostGentle } Gentle preview (locked on when hostGentle), steering assist, volumes
//   results { track, rows, me } (standings Result) Again / Menu
// nav(dir: 'up'|'down'|'left'|'right'|'ok'|'back')  pad navigation on any card (keyboard handled by the hud itself;
//   Escape on options or controls = back, on any other card it does nothing; it never leaves)
// setRacers(meta: RacerMeta[]); setTrack({ xz: Float32Array /* closed loop, world x,z pairs */ })
// options: { gentlePreview: false, assist: false, volMaster: 0.8, volMusic: 0.7, volSfx: 0.9 } (localStorage
//   'glassroad.options.v1', every access in try/catch)
// update(v) adds: place, count, item: { id, rolling, rollT, rollS } | null, map: Float32Array (x, z per racer), finished
// Corners: lap and time top left, item top centre, minimap top right, place bottom left, speed bottom right; drift pips
// under the kart; phone: place top right. Toasts rare and short (Slipstream, Lap, Final lap, who threw / who got hit).

// audio/bus.js (AUDIO): Phase A API kept, plus
//   update(speedPct, flags, rivals = [])  rivals = racers.nearby(ai.audibleKarts, 80): quiet AI engines, panned
//   setVolumes({ master, music, sfx })    0..1
//   music: Music | null                   made on start()
// audio/music.js
export function createMusic(ctx, out, feel) /* -> Music */
// Music: { playChart(chartUrl): Promise<boolean>, playFile(file: File, { bpm = 96 } = {}): Promise<boolean>, stop(): void,
//   readonly playing: boolean, readonly chart: Chart|null, readonly beat: { bpm, phase /* 0..1 */, index } | null, setVolume(v) }
// Chart = handoff-1022/RACE-AUDIO-BRIEF.md chart.json: { id, title, mod, durationS, bpm, downbeatS, sections: [{ id, startS }],
//   gates: [{ w, startS, endS }], words: [...], credits: {...} } plus our optional `audio` (url relative to the chart;
//   default: the chart's basename with .mp3). Words and gates are unused in B1. No chart ships in B1 (private masters).
// Cues: item-box, item-roll, item-fire, item-hit by verdict (the DtRH pop; glass chime; gold ping), item-refused,
//   bubble-pop, effect-start / effect-end per kind (a bed under the effect, never louder than the engine),
//   bump (volume by dv), slipstream, overtake, place-change, race-result, boost-start kinds slip / surge / bubble.
//   Only the player's own events, or ones near the player (payload p). Recorded DtRH sfx may be copied into audio/sfx
//   (list them in audio/SFX.md). No speech of any kind.
```

## 4. Events

Queue = emitted in sim steps, flushed once per frame. Bus = emitted synchronously during flush or fx.

| Event | Payload | Emits | Consumes |
|---|---|---|---|
| `racer-event` | `{ racer, name, p }` (a bot's Phase A event) | racers (queue) | render, audio (near ones) |
| `item-box` | `{ racer, box, p }` | items | hud (slot spins, racer 0), audio, render |
| `item-roll` | `{ racer, id }` | items | hud, audio |
| `item-fire` | `{ racer, id, mode: 'throw'\|'lob'\|'wide'\|'drop'\|'self', target, p }` | items | audio, hud, fx |
| `item-refused` | `{ racer, id, reason: 'edge' }` | items | hud (racer 0), audio |
| `item-hit` | `{ victim, by, kind, verdict: 'hit'\|'glass'\|'golden'\|'mercy'\|'decoy', durationS, p }` | items | effects (victim 0), fx, audio, hud (name tag) |
| `bubble-pop` | `{ racer, i, p }` | items | audio, fx |
| `effect-start` | `{ kind, durationS, by }` | effects (bus) | audio, hud |
| `effect-end` | `{ kind, replaced }` | effects (bus) | audio, hud |
| `bump` | `{ a, b, dv, p }` | contact | fx (shake when a or b is 0), audio |
| `slipstream` | `{ racer, state: 'in'\|'fire'\|'out' }` | contact | hud (toast on fire, racer 0), audio, fx |
| `place-change` | `{ racer, from, to }` | standings | hud, audio (racer 0) |
| `overtake` | `{ racer, passed }` (racer 0 is one of them) | standings | audio, fx |
| `race-standings` | `{ order }` (on a change) | standings | hud |
| `race-result` | `Result` (once) | standings | boot (results card, host message), audio, hud |
| `boost-start` | adds kinds `'slip'\|'surge'\|'bubble'` (tier 0) | grant.js | fx, audio |

Phase A events stay racer-0 only. Host message (integrator): `{ type: 'race-result', v: 1, track, laps, place, of,
timeS, bestLapS, finished, items: true, xp: null }` (the desktop adds XP later); `run-ended` stays.

## 5. B1 frame loop (boot.js)

```js
const { steps, alpha, frameDt } = clock.frame(now);
menu.tick(pad);                                     // 0. state: menu | countdown | racing | finished | results; paused overlay
const a = actions.sample();                         // 1. input (hop and item edges latched until a step runs)
if (!paused && inRace) for (let i = 0; i < steps; i++) {
  race.update(clock.dt, a, racers.player.st);       //    countdown, go, player launch (Phase A)
  racers.step(clock.dt, a, race, { ai, items, standings });   // 2. player + 7 bots
  contact.update(clock.dt, racers.list, race);      // 3. bumps, ghosts, slipstream
  standings.update(racers.list, race, clock.dt);    //    places, race-result
  items.update(clock.dt, racers.list, standings, race);       // 4. pickups, use, flights, traps, hits, mods for next tick
  latches.spent(a);
}
queue.flush(bus);                                   // 5. events
fx.update(frameDt, racers.player.st, race);         // 6. fx (Phase A + JUICE keys)
effects.update(frameDt);                            // 7. local screen effect (fx.state.screen)
audio.update(racers.player.st[S.speedPct], flags, racers.nearby(feel.get('ai.audibleKarts'), 80));   // 8. audio
racers.interpolate(alpha);                          // 9. camera
cam.update(racers.player.view, fx.state, track, frameDt, aspect, mode);
world.update({ kart: racers.player.view, karts, fx: fx.state, timeS, beat, sectionIdx, items: items.view(), mode });
renderer.render({ scene: world.scene, camera: cam.camera, fx: fx.state, dt: frameDt });   // 10. render
hud.update({ ...phaseA, place, count, item, map, finished });                            // 11. hud
```
Escape and window blur pause from countdown or racing (never leave); results card after the finish swell once
`race-result` came. Restart = `race.reset(); racers.place(); contact.reset(); standings.reset(); items.reset();
effects.reset(); ai.reset(); fx.reset()`. Options: gentle preview -> `gentle.set({ on })` unless the host forced it
on; assist -> `racers.setAssist(on)`; volumes -> `audio.setVolumes`. `?bot=1` drives the player with `ai.drive`.

## 6. Feel

New groups `items.*`, `ai.*`, `contact.*` in `feel.json`, one line each in `FEEL.md` (weights are
`[front, middle, back]`). Read every number each tick through `feel.get`; render art numbers go in the render
lanes' look tables (`render/look.js` JUICE, `render/items/look.js` SCREENFX).

## 7. Safety and copy (every lane)

One screen effect at a time; mercy and the blocked-view budget in sim/items.js; never fully blind; Gentle belongs
to the player hit (corner Flash, Spiral a soft dim, blur halved, IN >= 300 ms, no flicker); every effect start asks
photosafe; no strobe; Shatter never near an open edge. Copy dry and short, no em or en dashes, no exclamation
marks, our item names only, no Nintendo names or lookalike items. No synthetic speech.

## 8. Tests and checks

Whole suite before returning: `cd G && node --test --test-reporter=tap tests/*.test.mjs`. Fakes for sim tests:
`tests/sim-fake.mjs` (makeFeel, fakeTrack, place, recorder, acts) and the real `tracks/threshold.json` through
`loadTrack`. A `.js` module is checked by copying it to a `.mjs` temp name for `node --check`.

## 9. Track items (contracts, done)

`tracks/threshold.json` `items`: 5 box rows (18 boxes: 4, 3, 4, 3, 4 across) on the start straight, the S bend,
the deepening straight, before the open corner (55 m clear) and the last sweeper; 3 bubble clusters of 5 on inside
lines (sweep A, the spiral, L2). Entry: `{ node, x, kind: 'box'|'bubble', ds? /* m past the node, 0..40 */ }`;
schema warns on an unknown kind and errors off the road or over a gap. `loadTrack` adds `track.items`:
`[{ kind, node, x, ds, s }]`. Generator checks: boxes 40 m clear of the jump, 8 m of pads; everything 30 m
clear of the open corner.
