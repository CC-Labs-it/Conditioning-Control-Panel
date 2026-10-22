/* boot.js - Glassroad page entry: wires the lanes in the CONTRACT-B1.md section 5 frame loop order.
 *
 * States: menu -> race (countdown, racing, finished) -> results -> again or menu; the pause card lies
 * over countdown and racing only. Eight racers: you plus seven EMI bots, items, contact, standings,
 * the local screen effect (fx/effects.js into render/screenfx.js), the item visuals, HUD and audio.
 * Url flags: ?renderer=webgl2|webgpu, ?quality=high|mid|phone, ?gentle=1, ?feel=1 (live feel sheet),
 * ?seed=N (item rolls and the bots), ?bot=1 (the AI drives the player too, for smoke tests and attract
 * mode: the menu starts the race on its own after a short look, and results go round again),
 * &botFall=1 (the player steers off the first open corner once, to show a rescue).
 * Dev (?bot=1 only): window.__glassroad.peek = { s, backM, upM, latM, aheadM, liftM } holds the camera at a
 * fixed spot on the track for screenshots (dev/shots.mjs); null hands it back to the chase rig.
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
import { createWordRun } from './sim/words.js';
import { createWordCount } from './hud/words.js';
import { openFeelSheet } from './hud/feelSheet.js';
import { createSlotFx } from './render/items/slotfx.js';
import { createGateScreen } from './render/gateScreen.js';
import { loadFonts } from './render/fonts.js';
import { createAssist } from './sim/assist.js';

const qs = new URLSearchParams(location.search);
const BOT = qs.get('bot') === '1', BOT_FALL = BOT && qs.get('botFall') === '1';
// the race song (handoff-1022/MUSIC-ROAD-BRIEF.md): ?chart=<url> plays that chart, ?chart=0 none; the dev
// server (port 8790) tries the private song folder it mounts at /private-audio/. Nothing ships a chart yet.
const DEV_HOST = location.port === '8790' && (location.hostname === '127.0.0.1' || location.hostname === 'localhost');
const CHARTS = qs.get('chart') === '0' ? [] : qs.get('chart') ? [qs.get('chart')]
  : DEV_HOST ? ['/private-audio/pink-road/chart.json', '/private-audio/pink-road/chart.v0.json'] : [];
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

/** Dev peek camera: eye backM behind s, upM over the road, latM to the right, looking aheadM down the road. */
const peekF = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] }, peekG = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
function peekCamera(camera, track, pk, aspect) {
  const L = track.lengthM, s = (((+pk.s || 0) % L) + L) % L;
  const back = pk.backM ?? 12, up = pk.upM ?? 3, lat = pk.latM ?? 0, ahead = pk.aheadM ?? 40, lift = pk.liftM ?? 2;
  track.frameAt(s - back, peekF); track.frameAt(s + ahead - back, peekG);
  const f = peekF, g = peekG;
  camera.position.set(f.p[0] + f.n[0] * up + f.b[0] * lat, f.p[1] + f.n[1] * up + f.b[1] * lat, f.p[2] + f.n[2] * up + f.b[2] * lat);
  camera.up.set(f.n[0], f.n[1], f.n[2]);
  camera.lookAt(g.p[0] + g.n[0] * lift + g.b[0] * (pk.lookLatM ?? 0), g.p[1] + g.n[1] * lift + g.b[1] * (pk.lookLatM ?? 0), g.p[2] + g.n[2] * lift + g.b[2] * (pk.lookLatM ?? 0));
  if (pk.fov && camera.fov !== pk.fov) { camera.fov = pk.fov; camera.updateProjectionMatrix(); }
  camera.aspect = aspect; camera.updateProjectionMatrix();
  camera.updateMatrixWorld();
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
  // the diegetic faces (render/fonts.js) load beside the feel: road markings and bubble text measure with them
  const [feel, trackJson] = await Promise.all([
    loadFeel(),
    fetch(new URL('./tracks/threshold.json', import.meta.url), { cache: 'no-store' }).then((r) => {
      if (!r.ok) throw new Error('track ' + r.status);
      return r.json();
    }),
    loadFonts(),   // never rejects; a face that fails keeps its system fallback
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
  // the player's own word bubbles (sim/words.js): built from the song's chart, never sent anywhere
  const wordCount = createWordCount(document.getElementById('hud'));
  const NO_WORDS = [];
  let words = null;
  hud.setReady(false, 'Starting the renderer');
  hud.setHostGentle(hostGentle);
  hud.setCard('start', { title: trackName });
  // the warming card names each stage and its bar fills with the real progress (pass 3)
  const paintGap = () => new Promise((r) => requestAnimationFrame(() => setTimeout(r, 0)));
  hud.setLoading('Starting the renderer', 0.04);

  // the saved display options (frame cap, render scale, quality, fps readout) reach the renderer at boot
  const renderer = await createRenderer(canvas, { forceRenderer: init.caps.forceRenderer, options: hud.options });
  // wave 1 art (never blocks: a missing asset keeps its greybox); ?art=0 races the greybox
  hud.setLoading('Loading art', 0.08);
  const art = qs.get('art') === '0' ? null
    : await loadArt(renderer, { onProgress: (d, n) => hud.setLoading('Loading art ' + d + '/' + n, 0.08 + 0.4 * d / n) });
  hud.setLoading('Building the road', 0.5);
  await paintGap();
  if (art && art.failed.length) debug.artFailed = art.failed;
  debug.art = art ? { loaded: art.loaded, wanted: art.wanted, failed: art.failed.slice(0, 4) } : null;
  renderer.setArt(art);   // the fps readout shows art loaded / wanted and the first failed id
  const seed = +qs.get('seed') || ((Math.random() * 2147483646) | 0) + 1;
  debug.seed = seed;
  // ?shadows=0 (dev) drops the shadow map whatever the option says; the map is always set up otherwise
  // and the Shadows option turns it on or off in applyOptions
  const world = buildWorld(track, feel, { art, bus, seed, count: COUNT, renderer, photosafe, shadowsOn: qs.get('shadows') !== '0',
    spiralLayout: qs.get('spirals') === '0' ? null : undefined });   // ?spirals=0 (dev): no spiral gates
  // passing through a spiral gate plays that gate's own Loom over the view (photosafe, Gentle halves it)
  const gateScreen = createGateScreen(document.body, { feel, photosafe, tier: renderer.tier });
  bus.on('gate-pass', (g) => { if (state === 'race' && !paused) gateScreen.play(g.params, 1); });
  const cam = createChaseCamera(feel, { bus });   // the kick layer hears the player's bumps and wall hits
  debug.backend = renderer.backend;
  debug.gpu = renderer.gpu;
  // the HUD item slot's orbit and roll shuffle (FXPOLISH lane)
  const slotFx = createSlotFx(hud.item, hud.item && hud.item.querySelector('img'), { feel, phone: renderer.tier === 'phone' });
  // the arches that lean over the road, for the boot check's colossus shot
  // polish 3 layers for the shot harness: gate and media slot positions along the lap
  debug.gates = world.spirals ? world.spirals.gates.map((g) => ({ id: g.id, s: Math.round(g.s) })) : [];
  debug.mediaSlots = world.media ? world.media.slots.length : 0;
  debug.lengthM = track.lengthM;
  // dev (?bot=1): the shot harness pokes at the world; not enumerable, so the status JSON stays small
  if (BOT) Object.defineProperty(debug, 'world', { value: world, enumerable: false });
  debug.sections = track.sections.map((x) => ({ id: x.id, palette: x.palette, fromS: Math.round(x.fromS), toS: Math.round(x.toS) }));
  debug.colossi = world.colossi ? world.colossi.placed.map((x) => ({ id: x.id, s: x.spec && x.spec.s, kind: x.spec && x.spec.kind })) : [];
  const arches = world.colossi ? world.colossi.placed.filter((x) => x.spec && x.spec.kind === 'arch').map((x) => ({ id: x.id, s: x.spec.s })) : [];

  const clock = createClock({ hz: feel.get('clock.hz'), maxSubsteps: feel.get('clock.maxSubsteps'), bpm: feel.get('clock.bpm') });
  const race = createRace(track, feel, queue);
  const racers = createRacers(track, feel, queue, { count: COUNT, seed });
  const ai = createAi(track, feel, { seed });
  const assist = createAssist(track, feel);       // the player's Steer assist (Options), never the bots'
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
  // dev (?bot=1): the racers and the track for scripted runs (dev drive scripts); not enumerable
  // debug.hands = { steer, throttle, brake, hop, hopPressed } overrides the AI's hands on the player (hopPressed
  // lasts one step); null hands the player back to the AI
  if (BOT) for (const [k, v] of [['racers', racers], ['track', track], ['feel', feel], ['ai', ai]]) Object.defineProperty(debug, k, { value: v, enumerable: false });

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
  const actions = createActionMap([createKeyboard({ target: window }), padSource, createTouch({ root: document.body }), tapSource], { items: true, feel });
  const hopLatch = createEdgeLatch('hopPressed'), itemLatch = createEdgeLatch('item');

  let state = 'boot', paused = false, finishAt = 0, result = null, rumbleT = 0, rumbleSent = 0, botT = 0;

  // ---- options: Gentle preview (never below the host's switch), steering assist and sensitivity,
  //      volumes, and the display set (frame cap, render scale, quality, fps readout, shadows) ----
  let opts = hud.options;
  function applyOptions(o) {
    opts = o;
    gentle.set({ on: hostGentle || !!o.gentlePreview });
    racers.setAssist(!!o.assist);
    assist.setMode(o.steerAssist);
    audio.setVolumes({ master: o.volMaster, music: o.volMusic, sfx: o.volSfx });
    if (Number.isFinite(o.steerSens)) feel.set('steer.userMul', o.steerSens);   // FEEL.md: grip yaw, player only
    renderer.setOptions(o);
    if (world.shadows) world.shadows.setEnabled(qs.get('shadows') !== '0' && o.shadows !== false);
  }
  applyOptions(opts);
  hud.on('option', ({ options }) => applyOptions(options || hud.options));

  function resetAll() {
    race.reset(); racers.place(); contact.reset(); standings.reset(); items.reset();
    effects.reset(); ai.reset(); fx.reset(); assist.reset();
    if (world.resetReactions) world.resetReactions();   // last race's cheer, sulk and faces never carry over
    fx.state.screen = effects.screen;
    const sec = P.st[S.sectionIdx];
    audio.reset(sec);                         // held tones never carry into a new run
    fx.state.paletteMix = { from: sec, to: sec, t: 1 };
    queue.clear(); hopLatch.clear(); itemLatch.clear();
    racers.interpolate(1);
    cam.snap(P.view, track, renderer.aspect);
    finishAt = 0; result = null; debug.result = null; debug.place = 0;
    armWords();
  }
  /** A word run for the playing song (a new song builds one, the same song resets it); none without words. */
  function armWords() {
    const song = audio.music && audio.music.song;
    if (!song || !song.words.length) { words = null; wordCount.show(false); return; }
    if (!words || words.song !== song) {
      words = createWordRun(song, track, { seed }); words.song = song;
      words.onPop((e) => { bus.emit('word-pop', e); wordCount.pop(!!feel.get('gentle.on'), e); });
    } else words.reset(seed);
    wordCount.show(true);
  }
  /** Start the race song from the top: the first chart in CHARTS that loads. */
  let songToken = 0;
  async function playSong() {
    const my = ++songToken;
    for (const url of CHARTS) {
      if (my !== songToken || !audio.music) return;
      if (await audio.music.playChart(url)) {
        if (my !== songToken) return;
        debug.chart = url;
        armWords();
        return;
      }
    }
  }
  function stopSong() { songToken++; if (audio.music) audio.music.stop(); words = null; wordCount.show(false); }
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
  function startRace() {
    const started = audio.start(); unpause(); resetAll(); begin();
    if (CHARTS.length) Promise.resolve(started).then(playSong).catch((e) => console.warn('[glassroad] song', e));
  }
  function toMenu() {
    stopSong(); unpause(); resetAll();
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

  // dev status for the boot check's reaction and item shots (times are performance.now())
  bus.on('bump', ({ a, b, dv = 0 }) => { if (a === 0 || b === 0) { debug.bumpMeAt = performance.now(); debug.bumpDv = Math.round(dv * 10) / 10; } });
  bus.on('item-fire', ({ racer, id, mode }) => { if (mode === 'throw' || mode === 'lob') { debug.fireAt = performance.now(); debug.fire = { racer, id, mode }; } });
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

  // warm every material and the post chain behind the warming card, a group at a time (the bar moves)
  hud.setLoading('Preparing shaders', 0.55);
  await paintGap();
  const warm = await renderer.compile(world.scene, cam.camera, undefined,
    { onProgress: (k, n) => hud.setLoading('Preparing shaders ' + k + '/' + n, 0.55 + 0.4 * k / n) });
  debug.warm = warm;
  // and the gate screen's own context and Loom program (its first build froze the race at the first gate)
  hud.setLoading('Last touches', 0.97);
  await paintGap();
  gateScreen.warm(world.spirals && world.spirals.gates[0] ? world.spirals.gates[0].params : null);
  hud.setLoading('Ready', 1);
  hud.setReady(true);
  bridge.ready();
  toMenu();
  if (BOT) botT = performance.now() + 2500;      // attract: a short look at the menu, then race

  let fpsN = 0, fpsT = performance.now(), telT = 0;

  const DIAG = (location.port === '8790' || location.port === '8792') && (location.hostname === '127.0.0.1' || location.hostname === 'localhost');

  let diagT = 0, cardThrottle = false;
  function frame(now) {
    requestAnimationFrame(frame);
    if (!renderer.frameDue(now)) return;                          // the Frame cap option skips this one
    const { steps, alpha, frameDt, renderTime } = clock.frame(now);
    const a = actions.sample();                                   // 1. input
    const nav = gamepad.takeNav();
    const card = hud.card;
    if (card) {                                                   // 0. cards take the pad; Escape only ever pauses
      if (card === 'pause' && pad.pause) setPaused(false);
      else if (nav) hud.nav(nav);
      else if (a.throttle >= 0.5 && !cardThrottle) hud.nav('throttle');   // the how-to card leaves on a throttle press
      hopLatch.clear(); itemLatch.clear(); a.hopPressed = false; a.item = false;
    } else if (a.pause && !paused) setPaused(true);
    cardThrottle = a.throttle >= 0.5;
    if (BOT && botT && now >= botT) {
      botT = 0;
      if (state === 'menu') startRace();
      else if (state === 'results') startRace();
    }
    if (paused) { hopLatch.clear(); itemLatch.clear(); } else { hopLatch.hold(a); itemLatch.hold(a); }
    const inRace = state === 'race' || state === 'results';
    if (!paused && inRace) for (let i = 0; i < steps; i++) {        // 2. sim
      if (BOT && !P.finished) {
        ai.drive(P, aiCtx, clock.dt, a); if (fall) fall.apply(a);
        if (debug.hands) {                                      // dev drive scripts (steerAdd leans on the AI's own steer)
          const h = debug.hands, aiSteer = a.steer;
          Object.assign(a, h);
          if (h.steerAdd) a.steer = clamp(aiSteer + h.steerAdd, -1, 1);
          h.hopPressed = false;
        }
      }
      // the Steer assist reshapes the player's hands only (sim/assist.js); a ?bot=1 run opts in with debug.assistOn
      const pa = BOT && !debug.assistOn ? a : assist.apply(a, P.st, clock.dt, race.phase);
      race.update(clock.dt, pa, P.st);
      racers.step(clock.dt, pa, race, sctx);
      contact.update(clock.dt, racers.list, race);                //    bumps, ghosts, slipstream
      standings.update(racers.list, race, clock.dt);              //    places, race-result
      items.update(clock.dt, racers.list, standings, race);       //    pickups, use, flights, hits, mods
      hopLatch.spent(a); itemLatch.spent(a);
    }
    queue.flush(bus);                                             // 3. events
    const fxDt = paused ? 0 : frameDt;                            //    the pause card freezes every effect too
    fx.update(fxDt, P.st, race);                                  // 4. fx
    effects.update(fxDt);                                         //    the local screen effect
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
    if (BOT && debug.peek) peekCamera(cam.camera, track, debug.peek, renderer.aspect);
    const iv = items.view();
    // the song position the player hears (frozen while paused: the context is suspended)
    const music = audio.music, song = inRace && music ? music.song : null, songS = song ? music.songS : NaN;
    const pv = P.view;
    const wordLive = words && song ? words.update(songS,
      { s: pv[S.s], lat: pv[S.lateral], speedMps: Math.max(0, pv[S.speedPct]) * feel.get('speed.topMps') }) : NO_WORDS;
    if (words) wordCount.set(words.pops, words.total, words.streak);
    // renderTime (sim time + alpha x dt), not simTime: whole sim steps would judder the world on a
    // display that is not 60 Hz
    world.update({ kart: P.view, karts: kartViews, fx: fs, timeS: renderTime, beat: clock.beat,
      sectionIdx: pst[S.sectionIdx], items: iv, mode, camera: cam.camera, paused, song, songS, words: wordLive });
    if (BOT && song && world.musicRoad) {                         //    dev only: the boot check reads the music road
      const m = world.musicRoad.frame;
      debug.song = { t: Math.round(songS * 100) / 100, section: m.sectionId, calm: +m.calm.toFixed(2), helix: +m.helix.toFixed(2),
        big: +m.big.toFixed(2), shatter: +m.shatter.toFixed(2), wave: +m.waveAmp.toFixed(2), words: words ? words.pops : 0, wordsOf: words ? words.total : 0 };
    }
    if (BOT) debug.boxAheadM = boxAhead(P.view, iv.boxes);        //    dev only: the boot check's item box shot
    if (BOT && world.roadWords) debug.roadWords = world.roadWords.stats();   // dev only: the painted road words
    renderer.setHold(state === 'race');                           //    no tier step (a relink) mid-race or under the pause card
    renderer.render({ scene: world.scene, camera: cam.camera, fx: fs, dt: paused ? 0 : frameDt });   // 7. render

    const place = inRace && standings.order.length ? standings.placeOf(0) : 0;   // 8. hud
    for (let i = 0; i < COUNT; i++) { const s = racers.list[i].view; mapXz[i * 2] = s[S.posX]; mapXz[i * 2 + 1] = s[S.posZ]; }
    const it = P.item, holding = inRace && (it.id || it.rolling);
    if (holding) { hudItem.id = it.id; hudItem.rolling = it.rolling; hudItem.rollT = it.rollT; hudItem.rollS = feel.get('items.rollS'); }
    hud.update({ lap: pst[S.lap], laps, timeS: race.timeS, speedPct: pst[S.speedPct], countdown: null,
      sectionIdx: pst[S.sectionIdx], paused, place, count: COUNT, item: holding ? hudItem : null,
      map: mapXz, finished: P.finished, bestLapS: P.bestLapS });
    gateScreen.update(frameDt, paused);
    slotFx.update(paused ? 0 : frameDt, holding && !P.finished ? hudItem : null, !!feel.get('gentle.on'));

    if (state === 'race' && finishAt && now >= finishAt && result) {   // results after the finish swell
      finishAt = 0;
      state = 'results';
      hud.setCard('results', result);
      if (BOT) botT = now + 5000;
    }
    debug.phase = paused ? 'paused' : state === 'race' ? race.phase : state;
    debug.lap = pst[S.lap]; debug.s = pst[S.s]; debug.timeS = race.timeS; debug.speedPct = pst[S.speedPct];
    debug.place = place; debug.item = it.id;
    debug.boost = pst[S.boostKind] || 0;
    debug.air = pst[S.airborne] > 0.5 && pst[S.respawnPhase] === RESPAWN.NONE;
    debug.archAheadM = -1;
    for (const x of arches) {
      let d = x.s - pst[S.s];
      if (d < 0) d += track.lengthM;
      if (debug.archAheadM < 0 || d < debug.archAheadM) { debug.archAheadM = Math.round(d); debug.arch = x.id; }
    }
    fpsN++;
    if (now - fpsT >= 1000) {
      debug.fps = Math.round(fpsN * 1000 / (now - fpsT)); fpsN = 0; fpsT = now;
      Object.assign(debug, renderer.stats());
      debug.sfx = audio.sfx;
      debug.shadowMode = world.shadows ? world.shadows.mode : 'none';
      if (bridge.hosted && now - telT >= 5000) { telT = now; bridge.telemetry({ fps: debug.fps, ...renderer.stats() }); }
      // dev server only (node dev/serve.mjs prints it): what the tester's own browser actually runs
      if (DIAG && now - diagT >= 10000) { diagT = now; try { navigator.sendBeacon('/__diag', JSON.stringify({ fps: debug.fps, backend: debug.backend, gpu: debug.gpu, tier: renderer.tier, art: debug.art, song: audio.music ? { chart: debug.chart || null, playing: audio.music.playing, has: !!audio.music.song, t: Math.round(audio.music.songS), ctx: audio.state } : 'no-music', words: words ? words.pops + '/' + words.total : null, colossi: debug.colossi.length, lap: debug.lap, s: Math.round(debug.s || 0), stats: renderer.stats(), w: innerWidth, h: innerHeight, dpr: devicePixelRatio, ua: navigator.userAgent })); } catch { /* best effort */ } }
    }
  }
  requestAnimationFrame(frame);
}

boot().catch(fail);
