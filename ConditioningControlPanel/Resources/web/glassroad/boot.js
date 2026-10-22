/* boot.js - Glassroad page entry: wires the lanes in the CONTRACT.md "Frame loop" order.
 *
 * Url flags: ?renderer=webgl2|webgpu, ?quality=high|mid|phone, ?gentle=1, ?feel=1 (live feel sheet),
 * ?bot=1 (autopilot for smoke tests and attract mode: follows the centreline, holds throttle, restarts
 * after the finish), &botFall=1 (the bot steers off the first open corner once, to show a rescue).
 * Escape only pauses; nothing here ever leaves.
 */
import { createBridge } from './host/bridge.js';
import { createHostEscape } from './host/hostEscape.js';
import { loadFeel } from './core/feel.js';
import { createBus, createQueue } from './core/events.js';
import { createClock } from './core/clock.js';
import { createActions, createActionMap, createHopLatch } from './core/actions.js';
import { clamp, v3, qrotate } from './core/math.js';
import { createKeyboard } from './input/keyboard.js';
import { createGamepad } from './input/gamepad.js';
import { createTouch } from './input/touch.js';
import { S, RESPAWN, createState, copyState, lerpState } from './sim/state.js';
import { spawn, step } from './sim/kart.js';
import { updateRespawn } from './sim/respawn.js';
import { createRace } from './sim/race.js';
import { createProgress } from './sim/progress.js';
import { loadTrack } from './track/load.js';
import { createRenderer } from './render/renderer.js';
import { buildWorld } from './render/world.js';
import { loadArt } from './render/art.js';
import { createChaseCamera } from './render/camera.js';
import { createGentle } from './fx/gentle.js';
import { createPhotosafe } from './fx/photosafe.js';
import { createFx } from './fx/reactions.js';
import { createAudio } from './audio/bus.js';
import { createHud } from './hud/hud.js';
import { openFeelSheet } from './hud/feelSheet.js';

const qs = new URLSearchParams(location.search);
const BOT = qs.get('bot') === '1', BOT_FALL = BOT && qs.get('botFall') === '1';
const canvas = document.getElementById('view');
const cardEl = document.getElementById('card');
const note = document.getElementById('card-note');
/** Read-only status for the boot check (dev/boot-check.mjs) and the console. */
const debug = { phase: 'boot', drops: 0, rescues: 0, laps: 0, fps: 0, events: {} };
window.__glassroad = debug;

function fail(e) {
  console.error('[glassroad] boot failed', e);
  if (note) note.textContent = 'Could not start: ' + (e && e.message ? e.message : e);
  debug.phase = 'failed';
}

/** Autopilot InputSource: steers at a centreline point ahead, full throttle. */
function createBot(track, cur, feel) {
  const fwd = v3(), right = v3(), q = [0, 0, 0, 1], F = [0, 0, -1], R = [1, 0, 0];
  const f = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
  let fallFrom = -1, fallTo = -1, fell = !BOT_FALL;
  if (BOT_FALL) {
    for (let i = 0; i < track.nodeCount; i++) {
      const open = track.nodeFlags(i).includes('open');
      if (open && fallFrom < 0) fallFrom = track.nodeS[i];
      if (!open && fallFrom >= 0) { fallTo = track.nodeS[i]; break; }
    }
  }
  return {
    poll(into) {
      into.throttle = 1;
      if (cur[S.respawnPhase] !== RESPAWN.NONE) { fell = true; return; }
      const s = cur[S.s], speed = Math.max(0, cur[S.speedPct]) * feel.get('speed.topMps');
      track.frameAt(s + Math.max(8, speed * 0.55), f);
      const lat = !fell && fallFrom >= 0 && s > fallFrom + 10 && s < fallTo ? -f.w : 0;   // outside of the bank
      q[0] = cur[S.quatX]; q[1] = cur[S.quatY]; q[2] = cur[S.quatZ]; q[3] = cur[S.quatW];
      qrotate(fwd, q, F); qrotate(right, q, R);
      const d = [f.p[0] + f.b[0] * lat - cur[S.posX], f.p[1] + f.b[1] * lat - cur[S.posY], f.p[2] + f.b[2] * lat - cur[S.posZ]];
      const x = d[0] * right[0] + d[1] * right[1] + d[2] * right[2];
      const z = d[0] * fwd[0] + d[1] * fwd[1] + d[2] * fwd[2];
      into.steer = clamp(Math.atan2(x, Math.max(z, 0.1)) * 2.2, -1, 1);
    },
    dispose() {},
  };
}

async function boot() {
  const bridge = createBridge();
  const hostEscape = createHostEscape();          // before the key listeners: its capture hears Escape first
  const init = await bridge.init;
  const [feel, trackJson] = await Promise.all([
    loadFeel(),
    fetch(new URL('./tracks/threshold.json', import.meta.url), { cache: 'no-store' }).then((r) => {
      if (!r.ok) throw new Error('track ' + r.status);
      return r.json();
    }),
  ]);
  const gentle = createGentle(feel);
  gentle.set(init.gentle);
  if (qs.get('feel') === '1') openFeelSheet(feel, document.body);
  if (!(trackJson.laps > 0)) trackJson.laps = 3;
  const track = loadTrack(trackJson);
  const laps = track.laps;

  const bus = createBus(), queue = createQueue();
  const photosafe = createPhotosafe(feel);
  const fx = createFx(bus, feel, { gentle, photosafe });
  const audio = createAudio(feel, bus, { gentle });
  const hud = createHud({ root: document.getElementById('hud'), card: cardEl }, feel, bus);
  bus.on('*', (p, name) => { debug.events[name] = (debug.events[name] || 0) + 1; });
  hud.setReady(false, 'Warming up');
  let cardKind = null, ready = false;
  const setCard = (k, data) => { cardKind = k || null; hud.setCard(k, data); };
  setCard('start', { title: (track.name && track.name.en) || 'Threshold' });

  const renderer = await createRenderer(canvas, { forceRenderer: init.caps.forceRenderer });
  // wave 1 art (never blocks: a missing asset keeps its greybox); ?art=0 races the greybox
  const art = qs.get('art') === '0' ? null : await loadArt(renderer);
  if (art && art.failed.length) debug.artFailed = art.failed;
  const seed = +qs.get('seed') || ((Math.random() * 2147483646) | 0) + 1;
  debug.seed = seed;
  const world = buildWorld(track, feel, { art, bus, seed });
  const cam = createChaseCamera(feel);
  debug.backend = renderer.backend;

  const cur = createState(), prev = createState(), view = createState();
  const COAST = createActions();
  const clock = createClock({ hz: feel.get('clock.hz'), maxSubsteps: feel.get('clock.maxSubsteps'), bpm: feel.get('clock.bpm') });
  const race = createRace(track, feel, queue);
  const progress = createProgress(track, queue);
  const gamepad = createGamepad();
  // the pad's own edges, so a pad-only player can work the cards (gamepads fire no DOM events)
  const pad = { pause: false, hop: false, go: false, wasThrottle: false };
  const padSource = {
    poll(into) {
      gamepad.poll(into);
      const th = into.throttle > 0.5;
      pad.pause = !!into.pause; pad.hop = !!into.hopPressed; pad.go = th && !pad.wasThrottle; pad.wasThrottle = th;
    },
    dispose() { gamepad.dispose(); },
  };
  const sources = [createKeyboard({ target: window }), padSource, createTouch({ root: document.body })];
  if (BOT) sources.push(createBot(track, cur, feel));
  const actions = createActionMap(sources);

  let paused = false, lapTimes = [], finishAt = 0, rumbleT = 0, rumbleSent = 0;
  const hopLatch = createHopLatch();

  function place() {
    spawn(cur, track, feel, 0);
    copyState(prev, cur); copyState(view, cur);
    progress.reset(cur);
    fx.reset();
    const sec = cur[S.sectionIdx];
    audio.reset(sec);                         // held edge hum / rescue tone never carry into a new run
    fx.state.paletteMix = { from: sec, to: sec, t: 1 };
    queue.clear();
    cam.snap(view, track, renderer.aspect);
  }
  place();

  function setPaused(on) {
    on = !!on;
    if (on === paused) return;
    if (on && race.phase !== 'countdown' && race.phase !== 'racing') return;
    paused = on;
    clock.pause(on);
    audio.setPaused(on);
    bus.emit('pause', { on });
    setCard(on ? 'pause' : null);
    if (!on) canvas.focus({ preventScroll: true });
  }

  function begin() {
    setCard(null);
    canvas.focus({ preventScroll: true });
    race.start();
    queue.flush(bus);
    bridge.runStarted({ track: track.id, laps });
  }
  function restart() {
    if (paused) { paused = false; clock.pause(false); audio.setPaused(false); bus.emit('pause', { on: false }); }
    race.reset(); lapTimes = []; finishAt = 0;
    place();
    begin();
  }

  const startRun = () => { audio.start(); begin(); };
  hud.onStart(startRun);
  hud.onResume(() => setPaused(false));
  hud.onRestart(restart);
  bus.on('lap', ({ lapTimeS }) => { lapTimes.push(lapTimeS); debug.laps++; });
  bus.on('drop', () => { debug.drops++; });
  bus.on('rescue-release', () => { debug.rescues++; });
  bus.on('finish', ({ timeS }) => {
    finishAt = performance.now() + feel.get('juice.finishSwellS') * 1000;
    debug.finishTimeS = timeS;
    bridge.runEnded({ timeS, laps });
  });

  bridge.on('pause', (m) => setPaused(m.on));
  bridge.on('kept-escape', () => hostEscape.kept());
  bridge.on('gentle', (m) => gentle.set(m));
  bridge.on('focus', () => canvas.focus({ preventScroll: true }));
  if (!BOT) {
    window.addEventListener('blur', () => setPaused(true));
    document.addEventListener('visibilitychange', () => { if (document.hidden) setPaused(true); });
  }

  // warm every material and the post chain behind the start card
  await renderer.compile(world.scene, cam.camera);
  hud.setReady(true);
  ready = true;
  bridge.ready();
  debug.phase = 'ready';
  if (BOT) { audio.start(); begin(); }

  let fpsN = 0, fpsT = performance.now(), telT = 0;
  function frame(now) {
    requestAnimationFrame(frame);
    const { steps, alpha, frameDt } = clock.frame(now);
    const a = actions.sample();                                   // 1. input
    // cards: the pad's edges work them (keyboard and pointer go through the hud's own listeners);
    // Escape and the pad's Start only ever pause from play, the pad's Start or A resumes
    if (cardKind === 'pause') { if (pad.pause || pad.hop) { setPaused(false); hopLatch.clear(); a.hopPressed = false; } }
    else if (cardKind === 'start') { if (ready && (pad.hop || pad.go)) { startRun(); hopLatch.clear(); a.hopPressed = false; } }
    else if (cardKind === 'finish') { if (pad.hop || pad.go) { restart(); hopLatch.clear(); a.hopPressed = false; } }
    else if (a.pause && !paused) setPaused(true);
    if (paused) hopLatch.clear(); else hopLatch.hold(a);         // a hop edge waits for the next step
    if (!paused) for (let i = 0; i < steps; i++) {                // 2. sim
      copyState(prev, cur);
      race.update(clock.dt, a, cur);
      if (race.phase === 'racing' || race.phase === 'finished') step(cur, race.phase === 'finished' ? COAST : a, track, feel, clock.dt, queue);
      updateRespawn(cur, track, feel, clock.dt, queue);
      progress.update(cur, race);
      hopLatch.spent(a);
    }
    queue.flush(bus);                                             // 3. events
    fx.update(frameDt, cur, race);                                // 4. fx
    const fs = fx.state;
    audio.update(cur[S.speedPct], {                               // 5. audio
      boosting: cur[S.boostKind] > 0, drifting: cur[S.driftDir] !== 0, driftTier: cur[S.driftStage],
      airborne: cur[S.airborne] > 0, onRoad: cur[S.grounded] > 0, edge: fs.edgeGlow > 0, paused, duck: fs.duck,
    });
    // rumble follows fs.rumble (zero under Gentle): re-sent every juice.rumbleMs at its current level,
    // and at once when it steps up (drift tiers 2 and 3), so the ladder ramps and a landing fades
    const rm = paused ? 0 : fs.rumble || 0;
    if (rm > 0.05) {
      rumbleT -= frameDt;
      if (rumbleT <= 0 || rm > rumbleSent + 0.1) {
        const ms = feel.get('juice.rumbleMs');
        gamepad.rumble(rm, rm, ms * 1.5);                         // overlaps the next send: no gaps
        rumbleSent = rm; rumbleT = ms / 1000;
      }
    } else { rumbleT = 0; rumbleSent = 0; }
    lerpState(view, prev, cur, alpha);                            // 6. camera
    cam.update(view, fs, track, frameDt, renderer.aspect);
    world.update({ kart: view, fx: fs, timeS: clock.simTime, beat: clock.beat, sectionIdx: cur[S.sectionIdx] });
    renderer.render({ scene: world.scene, camera: cam.camera, fx: fs });   // 7. render
    hud.update({ lap: cur[S.lap], laps, timeS: race.timeS, speedPct: cur[S.speedPct], countdown: null,
      sectionIdx: cur[S.sectionIdx], paused });                   // 8. hud

    if (finishAt && now >= finishAt) {
      finishAt = 0;
      setCard('finish', { timeS: race.timeS, best: lapTimes.length ? Math.min(...lapTimes) : 0 });
      if (BOT) setTimeout(restart, 3000);
    }
    debug.phase = paused ? 'paused' : race.phase;
    debug.lap = cur[S.lap]; debug.s = cur[S.s]; debug.timeS = race.timeS; debug.speedPct = cur[S.speedPct];
    fpsN++;
    if (now - fpsT >= 1000) {
      debug.fps = Math.round(fpsN * 1000 / (now - fpsT)); fpsN = 0; fpsT = now;
      Object.assign(debug, renderer.stats());
      if (bridge.hosted && now - telT >= 5000) { telT = now; bridge.telemetry({ fps: debug.fps, ...renderer.stats() }); }
    }
  }
  requestAnimationFrame(frame);
}

boot().catch(fail);
