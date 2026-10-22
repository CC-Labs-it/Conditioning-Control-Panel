/* boot.js - Glassroad page entry: wires the lanes in the CONTRACT-B1.md section 5 frame loop order.
 *
 * States: menu -> race (countdown, racing, finished) -> results -> again or menu; the pause card lies
 * over countdown and racing only. Eight racers: you plus seven EMI bots, items, contact, standings,
 * the local screen effect (fx/effects.js into render/screenfx.js), the item visuals, HUD and audio.
 * Url flags: ?renderer=webgl2|webgpu, ?quality=high|mid|phone, ?gentle=1, ?feel=1 (live feel sheet),
 * ?seed=N (item rolls and the bots), ?bot=1 (the AI drives the player too, for smoke tests and attract
 * mode: the menu starts the race on its own after a short look, and results go round again),
 * &botFall=1 (the player steers off the first open corner once, to show a rescue).
 * Escape only pauses; nothing here ever leaves.
 */
import { createBridge } from './host/bridge.js';
import { createHostEscape } from './host/hostEscape.js';
import { loadFeel } from './core/feel.js';
import { createBus, createQueue } from './core/events.js';
import { createClock } from './core/clock.js';
import { createActionMap, createEdgeLatch } from './core/actions.js';
import { clamp, v3, qrotate } from './core/math.js';
import { createKeyboard } from './input/keyboard.js';
import { createGamepad } from './input/gamepad.js';
import { createTouch } from './input/touch.js';
import { S, RESPAWN } from './sim/state.js';
import { createRace } from './sim/race.js';
import { createRacers } from './sim/racers.js';
import { createAi } from './sim/ai.js';
import { createContact } from './sim/contact.js';
import { createStandings } from './sim/standings.js';
import { createItems } from './sim/items.js';
import { loadTrack } from './track/load.js';
import { createRenderer } from './render/renderer.js';
import { buildWorld } from './render/world.js';
import { loadArt } from './render/art.js';
import { createChaseCamera } from './render/camera.js';
import { createGentle } from './fx/gentle.js';
import { createPhotosafe } from './fx/photosafe.js';
import { createFx } from './fx/reactions.js';
import { createEffects } from './fx/effects.js';
import { createAudio } from './audio/bus.js';
import { createHud } from './hud/hud.js';
import { openFeelSheet } from './hud/feelSheet.js';

const qs = new URLSearchParams(location.search);
const BOT = qs.get('bot') === '1', BOT_FALL = BOT && qs.get('botFall') === '1';
const COUNT = 8;
const canvas = document.getElementById('view');
const cardEl = document.getElementById('card');
const note = document.getElementById('card-note');
/** Read-only status for the boot check (dev/boot-check.mjs) and the console. */
const debug = { phase: 'boot', drops: 0, rescues: 0, laps: 0, fps: 0, events: {}, effects: [], place: 0 };
window.__glassroad = debug;

function fail(e) {
  console.error('[glassroad] boot failed', e);
  if (note) note.textContent = 'Could not start: ' + (e && e.message ? e.message : e);
  debug.phase = 'failed';
}

/** ?botFall=1: once, steer the player to the outside of the first open stretch (a rescue to look at). */
function createFallSteer(track, st, feel) {
  const fwd = v3(), right = v3(), q = [0, 0, 0, 1], F = [0, 0, -1], R = [1, 0, 0];
  const f = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
  let from = -1, to = -1, fell = false;
  for (let i = 0; i < track.nodeCount; i++) {
    const open = track.nodeFlags(i).includes('open');
    if (open && from < 0) from = track.nodeS[i];
    if (!open && from >= 0) { to = track.nodeS[i]; break; }
  }
  return {
    apply(into) {
      if (fell || from < 0) return;
      if (st[S.respawnPhase] !== RESPAWN.NONE) { fell = true; return; }
      const s = st[S.s];
      if (!(s > from + 10 && s < to)) return;
      const speed = Math.max(0, st[S.speedPct]) * feel.get('speed.topMps');
      track.frameAt(s + Math.max(8, speed * 0.55), f);
      const lat = -f.w;   // outside of the bank
      q[0] = st[S.quatX]; q[1] = st[S.quatY]; q[2] = st[S.quatZ]; q[3] = st[S.quatW];
      qrotate(fwd, q, F); qrotate(right, q, R);
      const d = [f.p[0] + f.b[0] * lat - st[S.posX], f.p[1] + f.b[1] * lat - st[S.posY], f.p[2] + f.b[2] * lat - st[S.posZ]];
      const x = d[0] * right[0] + d[1] * right[1] + d[2] * right[2];
      const z = d[0] * fwd[0] + d[1] * fwd[1] + d[2] * fwd[2];
      into.steer = clamp(Math.atan2(x, Math.max(z, 0.1)) * 2.2, -1, 1);
      into.throttle = 1; into.brake = 0; into.hop = false; into.hopPressed = false;
    },
  };
}

/** Distance to the nearest live item box in front of the kart (within 25 degrees), or -1. Dev only (?bot=1). */
const boxFwd = v3(), boxQ = [0, 0, 0, 1], BOX_F = [0, 0, -1];
function boxAhead(st, boxes) {
  boxQ[0] = st[S.quatX]; boxQ[1] = st[S.quatY]; boxQ[2] = st[S.quatZ]; boxQ[3] = st[S.quatW];
  qrotate(boxFwd, boxQ, BOX_F);
  let best = -1;
  for (const b of boxes || []) {
    if (!b.alive || !b.p) continue;
    const dx = b.p[0] - st[S.posX], dy = b.p[1] - st[S.posY], dz = b.p[2] - st[S.posZ];
    const d = Math.hypot(dx, dy, dz), along = dx * boxFwd[0] + dy * boxFwd[1] + dz * boxFwd[2];
    if (d > 0 && along > d * 0.9 && (best < 0 || d < best)) best = d;
  }
  return best < 0 ? -1 : Math.round(best * 10) / 10;
}

/** The closed centreline as x, z pairs for the minimap. */
function centreline(track, stepM = 4) {
  const n = Math.max(8, Math.floor(track.lengthM / stepM)), xz = new Float32Array(n * 2);
  const f = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
  for (let i = 0; i < n; i++) { track.frameAt(i * track.lengthM / n, f); xz[i * 2] = f.p[0]; xz[i * 2 + 1] = f.p[2]; }
  return xz;
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
  let hostGentle = !!(init.gentle && init.gentle.on) || !!feel.get('gentle.on');
  if (qs.get('feel') === '1') openFeelSheet(feel, document.body);
  if (!(trackJson.laps > 0)) trackJson.laps = 3;
  const track = loadTrack(trackJson);
  const laps = track.laps, trackName = (track.name && track.name.en) || 'Threshold';

  const bus = createBus(), queue = createQueue();
  const photosafe = createPhotosafe(feel);
  const fx = createFx(bus, feel, { gentle, photosafe });
  const effects = createEffects(bus, feel, { gentle, photosafe });
  fx.state.screen = effects.screen;
  const audio = createAudio(feel, bus, { gentle });
  const hud = createHud({ root: document.getElementById('hud'), card: cardEl }, feel, bus);
  bus.on('*', (p, name) => { debug.events[name] = (debug.events[name] || 0) + 1; });
  bus.on('effect-start', ({ kind }) => { if (!debug.effects.includes(kind)) debug.effects.push(kind); debug.effectNow = kind; });
  bus.on('effect-end', () => { debug.effectNow = null; });
  hud.setReady(false, 'Warming up');
  hud.setHostGentle(hostGentle);
  hud.setCard('start', { title: trackName });

  const renderer = await createRenderer(canvas, { forceRenderer: init.caps.forceRenderer });
  // wave 1 art (never blocks: a missing asset keeps its greybox); ?art=0 races the greybox
  const art = qs.get('art') === '0' ? null : await loadArt(renderer);
  if (art && art.failed.length) debug.artFailed = art.failed;
  const seed = +qs.get('seed') || ((Math.random() * 2147483646) | 0) + 1;
  debug.seed = seed;
  const world = buildWorld(track, feel, { art, bus, seed, count: COUNT });
  const cam = createChaseCamera(feel);
  debug.backend = renderer.backend;

  const clock = createClock({ hz: feel.get('clock.hz'), maxSubsteps: feel.get('clock.maxSubsteps'), bpm: feel.get('clock.bpm') });
  const race = createRace(track, feel, queue);
  const racers = createRacers(track, feel, queue, { count: COUNT, seed });
  const ai = createAi(track, feel, { seed });
  const contact = createContact(track, feel, queue);
  const standings = createStandings(track, feel, queue);
  const items = createItems(track, feel, queue, { seed });
  const P = racers.player;
  const sctx = { ai, items, standings };
  const aiCtx = { racers: racers.list, items, standings, race };
  const kartViews = racers.list.map((r) => ({ st: r.view, meta: r.meta, fxv: r.fxv }));
  const mapXz = new Float32Array(COUNT * 2);
  const hudItem = { id: null, rolling: false, rollT: 0, rollS: 1 };
  hud.setRacers(racers.list.map((r) => r.meta));
  hud.setTrack({ xz: centreline(track) });
  const fall = BOT_FALL ? createFallSteer(track, P.st, feel) : null;

  const gamepad = createGamepad();
  // the pad's own Start edge (gamepads fire no DOM events); card navigation comes from gamepad.takeNav()
  const pad = { pause: false };
  const padSource = {
    poll(into) { gamepad.poll(into); pad.pause = !!into.pause; },
    dispose() { gamepad.dispose(); },
  };
  let tapItem = false;                            // touch: a tap on the HUD item slot
  hud.on('item', () => { tapItem = true; });
  const tapSource = { poll(into) { into.item = tapItem; tapItem = false; }, dispose() {} };
  const actions = createActionMap([createKeyboard({ target: window }), padSource, createTouch({ root: document.body }), tapSource], { items: true });
  const hopLatch = createEdgeLatch('hopPressed'), itemLatch = createEdgeLatch('item');

  let state = 'boot', paused = false, finishAt = 0, result = null, rumbleT = 0, rumbleSent = 0, botT = 0;

  // ---- options: Gentle preview (never below the host's switch), steering assist, volumes ----
  let opts = hud.options;
  function applyOptions(o) {
    opts = o;
    gentle.set({ on: hostGentle || !!o.gentlePreview });
    racers.setAssist(!!o.assist);
    audio.setVolumes({ master: o.volMaster, music: o.volMusic, sfx: o.volSfx });
  }
  applyOptions(opts);
  hud.on('option', ({ options }) => applyOptions(options || hud.options));

  function resetAll() {
    race.reset(); racers.place(); contact.reset(); standings.reset(); items.reset();
    effects.reset(); ai.reset(); fx.reset();
    fx.state.screen = effects.screen;
    const sec = P.st[S.sectionIdx];
    audio.reset(sec);                         // held tones never carry into a new run
    fx.state.paletteMix = { from: sec, to: sec, t: 1 };
    queue.clear(); hopLatch.clear(); itemLatch.clear();
    racers.interpolate(1);
    cam.snap(P.view, track, renderer.aspect);
    finishAt = 0; result = null; debug.result = null; debug.place = 0;
  }
  function unpause() {
    if (!paused) return;
    paused = false; clock.pause(false); audio.setPaused(false); bus.emit('pause', { on: false });
  }
  function setPaused(on) {
    on = !!on;
    if (on === paused) return;
    if (on && (state !== 'race' || (race.phase !== 'countdown' && race.phase !== 'racing'))) return;
    paused = on;
    clock.pause(on);
    audio.setPaused(on);
    bus.emit('pause', { on });
    hud.setCard(on ? 'pause' : null, on ? { track: trackName, lap: P.st[S.lap], laps, place: standings.placeOf(0), count: COUNT } : undefined);
    if (!on) canvas.focus({ preventScroll: true });
  }
  function begin() {
    state = 'race';
    hud.setCard(null);
    canvas.focus({ preventScroll: true });
    race.start();
    queue.flush(bus);
    bridge.runStarted({ track: track.id, laps });
  }
  function startRace() { audio.start(); unpause(); resetAll(); begin(); }
  function toMenu() {
    unpause(); resetAll();
    state = 'menu';
    hud.setCard('menu', { title: 'Racing Thoughts', track: trackName, laps });
  }

  hud.on('race', startRace);
  hud.on('again', startRace);
  hud.on('restart', startRace);
  hud.on('start', startRace);                     // the Phase A start card, should it ever show
  hud.on('resume', () => setPaused(false));
  hud.on('quit', toMenu);
  hud.on('menu', toMenu);
  // the menu's Back link: the host decides (it may close the game); unhosted it does nothing
  hud.on('back', ({ from } = {}) => { if (from === 'menu') bridge.send({ type: 'menu-back' }); });

  bus.on('lap', () => { debug.laps++; });
  bus.on('drop', () => { debug.drops++; });
  bus.on('rescue-release', () => { debug.rescues++; });
  bus.on('finish', ({ timeS }) => {
    finishAt = performance.now() + feel.get('juice.finishSwellS') * 1000;
    debug.finishTimeS = timeS;
    bridge.runEnded({ timeS, laps });
  });
  bus.on('race-result', (res) => {
    result = res;
    const me = (res && res.me) || {}, row = (res.rows || []).find((r) => r.me) || {};
    debug.result = { place: me.place, of: (res.rows || []).length, timeS: me.timeS, bestLapS: me.bestLapS };
    bridge.send({ type: 'race-result', v: 1, track: track.id, laps, place: me.place, of: (res.rows || []).length,
      timeS: me.timeS, bestLapS: me.bestLapS, finished: row.finished !== false, items: true, xp: null });
  });

  bridge.on('pause', (m) => setPaused(m.on));
  bridge.on('kept-escape', () => hostEscape.kept());
  bridge.on('gentle', (m) => {
    hostGentle = !!m.on;
    gentle.set({ ...m, on: hostGentle || !!opts.gentlePreview });
    hud.setHostGentle(hostGentle);
  });
  bridge.on('focus', () => canvas.focus({ preventScroll: true }));
  if (!BOT) {
    window.addEventListener('blur', () => setPaused(true));
    document.addEventListener('visibilitychange', () => { if (document.hidden) setPaused(true); });
  }

  // warm every material and the post chain behind the warming card
  await renderer.compile(world.scene, cam.camera);
  hud.setReady(true);
  bridge.ready();
  toMenu();
  if (BOT) botT = performance.now() + 2500;      // attract: a short look at the menu, then race

  let fpsN = 0, fpsT = performance.now(), telT = 0;
  function frame(now) {
    requestAnimationFrame(frame);
    const { steps, alpha, frameDt } = clock.frame(now);
    const a = actions.sample();                                   // 1. input
    const nav = gamepad.takeNav();
    const card = hud.card;
    if (card) {                                                   // 0. cards take the pad; Escape only ever pauses
      if (card === 'pause' && pad.pause) setPaused(false);
      else if (nav) hud.nav(nav);
      hopLatch.clear(); itemLatch.clear(); a.hopPressed = false; a.item = false;
    } else if (a.pause && !paused) setPaused(true);
    if (BOT && botT && now >= botT) {
      botT = 0;
      if (state === 'menu') startRace();
      else if (state === 'results') startRace();
    }
    if (paused) { hopLatch.clear(); itemLatch.clear(); } else { hopLatch.hold(a); itemLatch.hold(a); }
    const inRace = state === 'race' || state === 'results';
    if (!paused && inRace) for (let i = 0; i < steps; i++) {        // 2. sim
      if (BOT && !P.finished) { ai.drive(P, aiCtx, clock.dt, a); if (fall) fall.apply(a); }
      race.update(clock.dt, a, P.st);
      racers.step(clock.dt, a, race, sctx);
      contact.update(clock.dt, racers.list, race);                //    bumps, ghosts, slipstream
      standings.update(racers.list, race, clock.dt);              //    places, race-result
      items.update(clock.dt, racers.list, standings, race);       //    pickups, use, flights, hits, mods
      hopLatch.spent(a); itemLatch.spent(a);
    }
    queue.flush(bus);                                             // 3. events
    fx.update(frameDt, P.st, race);                               // 4. fx
    effects.update(frameDt);                                      //    the local screen effect
    const fs = fx.state;
    const pst = P.st;
    audio.update(pst[S.speedPct], {                               // 5. audio
      boosting: pst[S.boostKind] > 0, drifting: pst[S.driftDir] !== 0, driftTier: pst[S.driftStage],
      airborne: pst[S.airborne] > 0, onRoad: pst[S.grounded] > 0, edge: fs.edgeGlow > 0, paused, duck: fs.duck,
    }, inRace ? racers.nearby(feel.get('ai.audibleKarts'), 80) : []);
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

    racers.interpolate(alpha);                                    // 6. camera
    const mode = state === 'menu' ? 'menu' : state === 'results' ? 'results' : 'race';
    cam.update(P.view, fs, track, frameDt, renderer.aspect, mode);
    const iv = items.view();
    world.update({ kart: P.view, karts: kartViews, fx: fs, timeS: clock.simTime, beat: clock.beat,
      sectionIdx: pst[S.sectionIdx], items: iv, mode, camera: cam.camera });
    if (BOT) debug.boxAheadM = boxAhead(P.view, iv.boxes);        //    dev only: the boot check's item box shot
    renderer.render({ scene: world.scene, camera: cam.camera, fx: fs, dt: paused ? 0 : frameDt });   // 7. render

    const place = inRace && standings.order.length ? standings.placeOf(0) : 0;   // 8. hud
    for (let i = 0; i < COUNT; i++) { const s = racers.list[i].view; mapXz[i * 2] = s[S.posX]; mapXz[i * 2 + 1] = s[S.posZ]; }
    const it = P.item, holding = inRace && (it.id || it.rolling);
    if (holding) { hudItem.id = it.id; hudItem.rolling = it.rolling; hudItem.rollT = it.rollT; hudItem.rollS = feel.get('items.rollS'); }
    hud.update({ lap: pst[S.lap], laps, timeS: race.timeS, speedPct: pst[S.speedPct], countdown: null,
      sectionIdx: pst[S.sectionIdx], paused, place, count: COUNT, item: holding ? hudItem : null,
      map: mapXz, finished: P.finished, bestLapS: P.bestLapS });

    if (state === 'race' && finishAt && now >= finishAt && result) {   // results after the finish swell
      finishAt = 0;
      state = 'results';
      hud.setCard('results', result);
      if (BOT) botT = now + 5000;
    }
    debug.phase = paused ? 'paused' : state === 'race' ? race.phase : state;
    debug.lap = pst[S.lap]; debug.s = pst[S.s]; debug.timeS = race.timeS; debug.speedPct = pst[S.speedPct];
    debug.place = place; debug.item = it.id;
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
