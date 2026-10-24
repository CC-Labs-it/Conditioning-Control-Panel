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
 * Online (wave B2, NET.md): the menu's Race online door opens hud/lobby.js; its room is a net/room.js session.
 * On the room's start the run is rebuilt from the session (racers per seat, online items, the tapped queue), the
 * lobby card counts down on server time, the race goes at the room's startAt, pause only coasts your own kart
 * (the sim keeps running under the steer assist), and the results come from the room's finish. Lobby transport:
 * desktop host relay (caps.net on desktop), else a direct fetch to caps.relayBase; dev only (127.0.0.1 pages):
 * ?lobby=http://127.0.0.1:8811&uid=<id>&name=<n> talks to a local race-local.mjs lobby with the fake identity
 * (&tier=N also goes to the lobby as x-dev-tier). ?r=<token>: a friend's race link, claimed on the lobby card.
 */
import { createBridge, songCharts, wantLobbySongs, lobbySongCharts, ROOM_ORIGIN } from './host/bridge.js';
import { createHostEscape } from './host/hostEscape.js';
import { createCardMedia } from './host/cardMedia.js';
import { loadFeel } from './core/feel.js';
import { createBus, createQueue } from './core/events.js';
import { createClock } from './core/clock.js';
import { createActionMap, createEdgeLatch } from './core/actions.js';
import { clamp, v3, qrotate } from './core/math.js';
import { createKeyboard } from './input/keyboard.js';
import { createGamepad } from './input/gamepad.js';
import { createTouch } from './input/touch.js';
import { glideActive, resetGlide } from './sim/glide.js';
import { S, RESPAWN } from './sim/state.js';
import { createRace } from './sim/race.js';
import { createRacers } from './sim/racers.js';
import { createAi } from './sim/ai.js';
import { createContact } from './sim/contact.js';
import { createStandings } from './sim/standings.js';
import { createItems } from './sim/items.js';
import { loadTrack } from './track/load.js';
import { selectTrack, allowsOnline, courseSongs } from './track/selection.js';
import { createMainframeScan, scanClock } from './sim/mainframeScan.js';
import { createRenderer } from './render/renderer.js';
import { buildWorld } from './render/world.js';
import { loadArt } from './render/art.js';
import { createChaseCamera } from './render/camera.js';
import { createGentle } from './fx/gentle.js';
import { createPhotosafe } from './fx/photosafe.js';
import { createFx } from './fx/reactions.js';
import { createEffects } from './fx/effects.js';
import { createAudio } from './audio/bus.js';
import { goDownbeat } from './audio/music.js';
import { createHud } from './hud/hud.js';
import { createWordRun } from './sim/words.js';
import { createWordCount } from './hud/words.js';
import { openFeelSheet } from './hud/feelSheet.js';
import { createSlotFx } from './render/items/slotfx.js';
import { createGateScreen } from './render/gateScreen.js';
import { loadFonts } from './render/fonts.js';
import { createAssist } from './sim/assist.js';
import { createRoomSession } from './net/room.js';
import { createLobbyApi, fetchTransport, hostTransport } from './net/lobbyApi.js';
import { configureLobby } from './hud/lobby.js';
import { linkToken, dropLinkParam } from './hud/lobbyMath.js';
import { poseOf } from './hud/presets.js';
import { createDevHook, devAllowed } from './dev/hook.js';
import { chipAlpha, kartPoint } from './hud/countdown.js';
import { createGarage } from './render/garage.js';
import { raceLook } from './hud/garageState.js';
import { COURSES, selectedCourse, courseUrl } from './track/catalog.js';
import { getKeepLayout } from './track/keep-layout.js';
import { getBoudoirLayout } from './track/boudoir-layout.js';
import { createBoudoirPerfume } from './sim/boudoirPerfume.js';
import { createBoudoirHazards } from './sim/boudoirHazards.js';
import { createKeepHazards } from './sim/keepHazards.js';

const qs = new URLSearchParams(location.search);
const course = selectedCourse(location.search);
const TRACK_ID = course.id;
const BOT = qs.get('bot') === '1', BOT_FALL = BOT && qs.get('botFall') === '1';
// ?dev=1: the scripted drives in dev/hook.js (window.__glassroad.dev); refused without the query and online
const DEV = devAllowed(location.search);
// ?hitch=1 (dev/hitch-check.mjs): per-frame section times in window.__hitch (ms), read by the check's rAF probe
const HT = qs.get('hitch') === '1' ? (window.__hitch = { sim: 0, fx: 0, audio: 0, cam: 0, world: 0, render: 0, hud: 0, steps: 0 }) : null;
let hT = 0;
const hLap = (k) => { const t = performance.now(); HT[k] = t - hT; hT = t; };
// the race song (handoff-1022/MUSIC-ROAD-BRIEF.md): ?chart=<url> plays that chart, ?chart=0 none; the dev
// server (port 8790) tries the private song folder it mounts at /private-audio/; a host names its own charts in
// init.caps.songs (host/bridge.js songCharts). Set once the init lands, before any race can start.
const DEV_HOST = location.port === '8790' && (location.hostname === '127.0.0.1' || location.hostname === 'localhost');
let CHARTS = [];
const COUNT = 8;                                   // offline grid (and the kart slots the world builds); online = the room's seats
const LOCAL_URL = /^http:\/\/(127\.0\.0\.1|localhost)(:\d+)?$/;
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

/** The closed centreline for the minimap: x, z pairs and the height per point (polish 6: the decks layer). */
function centreline(track, stepM = 4) {
  const n = Math.max(8, Math.floor(track.lengthM / stepM)), xz = new Float32Array(n * 2), y = new Float32Array(n);
  const f = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
  for (let i = 0; i < n; i++) { track.frameAt(i * track.lengthM / n, f); xz[i * 2] = f.p[0]; xz[i * 2 + 1] = f.p[2]; y[i] = f.p[1]; }
  return { xz, y };
}

async function boot() {
  const bridge = createBridge();
  const hostEscape = createHostEscape();          // before the key listeners: its capture hears Escape first
  const init = await bridge.init;
  CHARTS = songCharts({ search: location.search, devHost: DEV_HOST, songs: init.caps.songs });
  // Mainframe has no recorded Drone song yet. A personally selected chart or audio file remains usable.
  CHARTS = courseSongs(TRACK_ID, location.search, CHARTS);
  // the diegetic faces (render/fonts.js) load beside the feel: road markings and bubble text measure with them
  const [feel, trackJson] = await Promise.all([
    loadFeel(),
    fetch(new URL('./tracks/' + course.id + '.json', import.meta.url), { cache: 'no-store' }).then((r) => {
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
  debug.trackId = track.id;
  const keepLayout = getKeepLayout(track);
  const boudoirLayout = getBoudoirLayout(track);
  const hazards = keepLayout ? createKeepHazards(track, keepLayout) : boudoirLayout ? createBoudoirHazards(track, boudoirLayout) : null;
  const TRACK_LAPS = track.laps, trackName = (track.name && track.name.en) || 'Threshold';
  let laps = TRACK_LAPS;                           // online: the room cfg laps (track.laps follows it for the sim)

  const bus = createBus(), queue = createQueue();
  const photosafe = createPhotosafe(feel);
  const fx = createFx(bus, feel, { gentle, photosafe });
  const effects = createEffects(bus, feel, { gentle, photosafe });
  fx.state.screen = effects.screen;
  const audio = createAudio(feel, bus, { gentle });
  for (const name of ['keep-gate-hit', 'keep-chain-hit', 'boudoir-press-hit', 'boudoir-sweeper-hit', 'boudoir-mirror-hit']) bus.on(name, () => bus.emit('wall', { dv: 12 }));
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
    : await loadArt(renderer, { trackId: track.id, onProgress: (d, n) => hud.setLoading('Loading art ' + d + '/' + n, 0.08 + 0.4 * d / n) });
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
  // A visual review may keep the Loom faces without requesting or displaying private media.
  const cardMedia = qs.get('media') === '0' ? null : createCardMedia(world.media, { bridge });
  if (cardMedia) {
    await cardMedia.ready;
    window.addEventListener('pagehide', () => cardMedia.dispose(), { once: true });
  }
  Object.defineProperty(debug, 'cardMedia', { get: () => world.media?.stats() || null });
  const garage=await createGarage({art,renderer,canvas,track:trackName,laps,trackId:track.id,online:course.online,
    changeTrack:id=>{const url=courseUrl(location.href,id);if(url)location.assign(url);},
    action:name=>hud.menuAction(name),sound:name=>{audio.start();audio.cue(name);}});
  if(garage){hud.setGarageNav(garage.nav);debug.garage=garage;}
  // passing through a spiral gate plays that gate's own Loom over the view (photosafe, Gentle halves it)
  const gateScreen = createGateScreen(document.body, { feel, photosafe, tier: renderer.tier });
  bus.on('gate-pass', (g) => { if (state === 'race' && !paused) gateScreen.play(g.params, 1); });
  const cam = createChaseCamera(feel, { bus });   // the kick layer hears the player's bumps and wall hits
  if (BOT) globalThis.__glassroadCamera = cam.camera;   // dev capture: observe the real chase projection
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
  debug.trackId = track.id;
  if (boudoirLayout) Object.defineProperty(debug, 'perfume', { get: () => perfume.inspect(), enumerable: true });
  if (hazards) Object.defineProperty(debug, boudoirLayout ? 'boudoirHazards' : 'keepGates', { get: () => hazards.inspect(racers.clockS), enumerable: true });
  debug.colossi = world.colossi ? world.colossi.placed.map((x) => ({ id: x.id, s: x.spec && x.spec.s, kind: x.spec && x.spec.kind })) : [];
  debug.terrace = world.terrace ? { count: world.terrace.count, ...world.terrace.diagnostics } : null;
  const arches = world.colossi ? world.colossi.placed.filter((x) => x.spec && x.spec.kind === 'arch').map((x) => ({ id: x.id, s: x.spec.s })) : [];

  const clock = createClock({ hz: feel.get('clock.hz'), maxSubsteps: feel.get('clock.maxSubsteps'), bpm: feel.get('clock.bpm') });
  const assist = createAssist(track, feel);       // the player's Steer assist (Options), never the bots'
  // the run: solo (count COUNT, the page seed) or online (one racer per seat from the room session, its seed, the
  // queue tapped so local cp / lap / finish / drop and pulses go up); rebuilt by buildRun on every switch
  let race, racers, ai, contact, standings, items, scan, perfume, P, sctx, aiCtx, kartViews, mapXz, fall, looksOnline = false;
  let runAssist = false;                          // the Options assist switch (applyOptions), carried into every rebuilt run
  function buildRun(s = null) {
    const q = s ? s.tap(queue) : queue, sd = s ? s.seed : seed;
    race = createRace(track, feel, q);
    racers = createRacers(track, feel, q, s ? { net: s.layout(), seed: sd } : { count: COUNT, seed: sd });
    ai = createAi(track, feel, { seed: s ? sd + s.seat : sd });
    if (hazards) {
      const drive = ai.drive;
      ai.drive = (r, ctx, dt, out) => { drive(r, ctx, dt, out); if (ctx.race.phase === 'racing') hazards.steer(r, out); };
      hazards.reset();
    }
    contact = createContact(track, feel, q);
    scan = createMainframeScan(track, q);
    perfume = createBoudoirPerfume(track, q);
    standings = createStandings(track, feel, q);
    items = s ? s.mount(racers, race, q) : createItems(track, feel, q, { seed: sd });
    P = racers.player;
    sctx = { ai, items, standings, hazards };
    aiCtx = { racers: racers.list, items, standings, race };
    kartViews = racers.list.map((r) => ({ st: r.view, meta: r.meta, fxv: r.fxv }));
    mapXz = new Float32Array(racers.list.length * 2);
    racers.setAssist(runAssist);
    hud.setRacers(racers.list.map((r) => r.meta));
    fall = BOT_FALL && !s ? createFallSteer(track, P.st, feel) : null;
    // each seat's look (Astra's wardrobe and vehicles) on its kart: online the room's ids, back to the seat picks after
    if (world.setLook && (s || looksOnline || garage)) {
      for (let i = 0; i < COUNT; i++) world.setLook(i, s && racers.list[i] ? racers.list[i].meta.look : undefined);
      if(garage&&!s)world.setLook(0,raceLook(garage.state));
      looksOnline = !!s;
    }
  }
  buildRun();
  const hudItem = { id: null, rolling: false, rollT: 0, rollS: 1 };
  hud.setTrack(centreline(track));
  // dev (?bot=1): the racers and the track for scripted runs (dev drive scripts); not enumerable
  // debug.hands = { steer, throttle, brake, hop, hopPressed } overrides the AI's hands on the player (hopPressed
  // lasts one step); null hands the player back to the AI
  // ?dev=1 (dev/hook.js): tier 3 drift on the next bend, the drop shortcut; never built without the query
  const devHook = DEV ? createDevHook({ track, feel, bus, player: () => P, race: () => race, emit: (n, p) => queue.emit(n, p),
    onPlace: () => { racers.interpolate(1); cam.snap(P.view, track, renderer.aspect); }, refuse: () => (online ? 'online' : null) }) : null;
  if (devHook) Object.defineProperty(debug, 'dev', { value: devHook.api, enumerable: false });
  if (BOT) {
    for (const [k, v] of [['track', track], ['feel', feel]]) Object.defineProperty(debug, k, { value: v, enumerable: false });
    Object.defineProperty(debug, 'racers', { get: () => racers, enumerable: false });
    Object.defineProperty(debug, 'ai', { get: () => ai, enumerable: false });
  }

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
  // online: { s (net/room.js session), cfg, go (race.start() done) } or null; lobbyUi = the hud/lobby.js handle
  let online = null, lobbyUi = null;
  let opts = hud.options, assistKey = '';
  /** The Steer assist mode: the Options pick; online it is off on a no lane keeper table and on while paused. */
  function syncAssist() {
    let m = opts.steerAssist;
    if (online && online.go) {
      if (paused) m = !m || m === 'off' ? 'light' : m;
      else if (!(online.cfg && online.cfg.laneKeeper)) m = 'off';
    }
    const key = String(m);
    if (key !== assistKey) { assistKey = key; assist.setMode(m); }
  }
  function applyOptions(o) {
    opts = o;
    gentle.set({ on: hostGentle || !!o.gentlePreview });
    runAssist = !!o.assist;
    racers.setAssist(runAssist);
    assistKey = ''; syncAssist();
    audio.setVolumes({ master: o.volMaster, music: o.volMusic, sfx: o.volSfx });
    if (Number.isFinite(o.steerSens)) feel.set('steer.userMul', o.steerSens);   // FEEL.md: grip yaw, player only
    renderer.setOptions(o);
    if (world.shadows) world.shadows.setEnabled(qs.get('shadows') !== '0' && o.shadows !== false);
  }
  applyOptions(opts);
  hud.on('option', ({ options }) => applyOptions(options || hud.options));

  let glideHintLap = -1;
  function resetAll() {
    glideHintLap = -1;
    for (const r of racers.list) resetGlide(r.st);
    race.reset(); racers.place(); contact.reset(); standings.reset(); items.reset();
    scan.reset(); perfume.reset();
    effects.reset(); ai.reset(); fx.reset(); assist.reset();
    if (hazards) hazards.reset();
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
      words.onPop((e) => {
        bus.emit('word-pop', e); wordCount.pop(!!feel.get('gentle.on'), e);
        if (online && words) online.s.wordPop(words.pops);   // a pulse for the others, the total for the standings
      });
    } else words.reset(seed);
    wordCount.show(true);
  }
  /**
   * Start the race song: the first chart in CHARTS that loads. align(song) -> the song position to hear now, so
   * go lands on a bar downbeat (S3): the song is offset against go, go never moves.
   */
  let songToken = 0;
  // a song the player dropped on the page (solo only): no chart, so the beat shake runs on its analysis
  // (audio/songShape.js analyzeBuffer) and there are no words. Online the room's chart wins.
  let pickedSong = null;
  async function playSong(align = null) {
    const my = ++songToken;
    if (pickedSong && !online && audio.music) {
      if (await audio.music.playFile(pickedSong)) {
        if (my !== songToken) return;
        debug.chart = 'file:' + pickedSong.name;
        armWords();
        return;
      }
      if (my !== songToken) return;
    }
    for (const url of CHARTS) {
      if (my !== songToken || !audio.music) return;
      if (await audio.music.playChart(url, { align })) {
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
    syncAssist();
  }
  function setPaused(on) {
    on = !!on;
    if (on === paused) return;
    if (on && (state !== 'race' || (race.phase !== 'countdown' && race.phase !== 'racing'))) return;
    paused = on;
    // online the room never waits: the sim, the clock and the song keep going and only your kart coasts
    if (!online) { clock.pause(on); audio.setPaused(on); }
    bus.emit('pause', { on });
    syncAssist();
    hud.setCard(on ? 'pause' : null, on ? { track: trackName, lap: P.st[S.lap], laps, place: standings.placeOf(0), count: racers.list.length } : undefined);
    if (!on) canvas.focus({ preventScroll: true });
  }
  function begin() {
    state = 'race';
    garage?.setVisible(false);
    hud.setCard(null);
    canvas.focus({ preventScroll: true });
    race.start();
    queue.flush(bus);
    bridge.runStarted({ track: track.id, laps });
  }
  function startRace() {
    const started = audio.start(); unpause(); resetAll();
    if(garage){world.setLook(0,raceLook(garage.state));debug.playerLook=world.looks()[0];}
    begin();
    // solo: the song would start with the count; it waits (or starts part way in) so go is a downbeat
    const align = (song) => goDownbeat(song, goS) - race.goInS;
    if (pickedSong || CHARTS.length) Promise.resolve(started).then(() => playSong(align)).catch((e) => console.warn('[glassroad] song', e));
  }
  function toMenu() {
    if (online && lobbyUi) lobbyUi.leave();       // quit mid-race online: leave the room (online-left tears down)
    if (online) endOnline();
    stopSong(); unpause(); resetAll();
    state = 'menu';
    hud.setCard('menu', { title: 'Racing Thoughts', track: trackName, laps, online: course.online,
      courses: COURSES.map(c => ({ name: c.name, selected: c.id === track.id, url: courseUrl(location.href, c.id) })) });
  }

  // ---- online (wave B2): lobby transport, the room session behind the lobby UI, the run per room --------------
  /** The lobby transport and identity: dev local lobby, desktop host relay, or a direct fetch on the web. */
  function lobbyLink() {
    if (!course.online) return null;
    const dev = qs.get('lobby');
    if (dev && LOCAL_URL.test(dev) && /^(127\.0\.0\.1|localhost)$/.test(location.hostname)) {
      const uid = qs.get('uid') || 'u_devpage1', name = qs.get('name') || uid;
      return { transport: fetchTransport({ base: dev, headers: { authorization: 'Bearer jwt-' + uid, 'x-dev-name': name, ...(qs.get('tier') ? { 'x-dev-tier': qs.get('tier') } : {}) }, credentials: 'omit' }),
        identity: { unifiedId: uid, displayName: name, tier: Number(qs.get('tier') || 2) } };
    }
    if (!init.caps.net) return null;
    if (init.caps.platform === 'desktop' && bridge.hosted) return { transport: hostTransport(bridge), identity: init.identity };
    return { transport: fetchTransport({ base: init.caps.relayBase || '' }), identity: init.identity };
  }
  const link = lobbyLink();
  const lobbyApi = link ? createLobbyApi(link.transport) : null;
  // race songs from our own storage (NET.md section 14): nothing named a chart and the lobby is there, so ask it once
  // for signed chart urls on the room origin (a local lobby names its room with &room=http://127.0.0.1:<port>). A
  // refusal (preview, dark, offline) leaves no song; a race that starts before the answer lands runs without one.
  if (lobbyApi && wantLobbySongs({ search: location.search, charts: CHARTS })) {
    const devLobby = LOCAL_URL.test(qs.get('lobby') || '') && /^(127\.0\.0\.1|localhost)$/.test(location.hostname);
    const roomOrigin = !devLobby ? ROOM_ORIGIN : (LOCAL_URL.test(qs.get('room') || '') ? qs.get('room') : null);
    lobbyApi.songs().then((r) => {
      const list = lobbySongCharts(r && r.songs, { roomOrigin });
      if (list.length && !CHARTS.length) CHARTS = list;
    }).catch(() => { /* no lobby songs: none */ });
  }
  const platform = init.caps.platform === 'desktop' || init.caps.platform === 'phone' ? init.caps.platform : 'web';
  const goS = feel.get('juice.countdownPulses') * 60 / feel.get('clock.bpm');
  /** The lobby UI's room: a net/room.js session behind the on / send / close / now shape hud/lobby.js codes to. */
  async function connectRoom(join) {
    if (!course.online) throw new Error('This course is for solo races');
    const s = createRoomSession({ track, feel, lobby: lobbyApi, platform, log: (lv, m) => console.warn('[glassroad]', m) });
    const welcome = await s.connect(join, { lookNow: join.look });
    return {
      session: s,
      on(t, fn) {
        if (t === 'close') return s.on('closed', fn);
        const off = s.on(t, fn);
        if (t === 'welcome') Promise.resolve().then(() => fn(welcome));   // connect resolved on it before the UI listened
        return off;
      },
      send(m) {
        if (m.t === 'ready') return s.ready(m.on);
        if (m.t === 'start') return s.start();
        if (m.t === 'look') return s.setLook(m);
        if (m.t === 'emote') return s.emote(m.code, poseOf(m.code));
        if (m.t === 'leave') { s.leave(); return true; }
        return false;
      },
      close() { s.leave(); if (online && online.s === s) endOnline(); },
      now: () => s.serverNow(),
    };
  }
  configureLobby({ api: lobbyApi ? {
    list: () => lobbyApi.list(),
    open: (body) => lobbyApi.open(body),
    join: (roomId, look) => lobbyApi.join({ roomId, look }),
    quick: (o) => lobbyApi.quick(o),
    invite: (friendUid, roomId) => lobbyApi.invite({ friendUid, roomId }),
    leave: (roomId) => lobbyApi.leave({ roomId }),
    makeLink: () => lobbyApi.makeLink(),
    revokeLink: () => lobbyApi.revokeLink(),
    claimLink: (token, look, plat) => lobbyApi.claimLink(token, look, plat),
    access: () => lobbyApi.access(),
  } : null, connect: lobbyApi ? connectRoom : null, identity: () => (link && link.identity) || null, track: track.id, platform,
  garageLook:()=>garage?raceLook(garage.state):null,
  // a race link (?r=<token>, NET.md section 13): the address drops its r after the first claim answer
  linkClaimed: () => { const to = dropLinkParam(location.href); if (to) history.replaceState(history.state, '', to); } });
  hud.on('online', (e) => { lobbyUi = (e && e.lobby) || lobbyUi; });
  // polish 5 (S4): the one hud-to-audio hook: card and lobby sounds (hud/sounds.js names, audio/cues-b1.js UI_CUES)
  hud.on('sound', (e) => { if (e && e.name) audio.cue(e.name); });

  /** The room said start (or we joined mid-race): build the run from the session, the lobby card counts down. */
  function startOnline(ctx) {
    const s = ctx && ctx.room && ctx.room.session;
    if (!s || (online && online.s === s)) return;
    stopSong(); unpause();
    online = { s, cfg: s.cfg, go: false };
    track.laps = (s.cfg && s.cfg.laps) || TRACK_LAPS; laps = track.laps;
    buildRun(s);
    resetAll();
    syncAssist();
    debug.online = { roomId: s.roomId, seat: s.seat, seats: racers.list.length };
    Object.defineProperty(debug.online, 'keep', { get: () => s.keep, enumerable: true });
    if (BOT) Object.defineProperty(debug, 'session', { value: s, configurable: true, enumerable: false });   // dev e2e: room messages
    if (ctx.resume || s.phase === 'racing') goOnline();
  }
  /** countAt: the card goes, the song starts (everyone hears the same moment), go lands on startAt (frame loop). */
  function goOnline() {
    if (!online || state === 'race') return;
    state = 'race';
    hud.setCard(null);
    canvas.focus({ preventScroll: true });
    const started = audio.start(), s = online.s;
    // the song's 0 s is countAt and go is startAt for every seat: the same chart gives everyone the same offset
    // (a late joiner lands part way in)
    const align = (song) => goDownbeat(song, (s.startAt - s.countAt) / 1000) - (s.startAt - s.serverNow()) / 1000;
    if (CHARTS.length) Promise.resolve(started).then(() => playSong(align)).catch((e) => console.warn('[glassroad] song', e));
    bridge.runStarted({ track: track.id, laps });
  }
  /** Back to the solo run (left, closed or aborted). showLobby: put the lobby card back when no card is up. */
  function endOnline(showLobby = false) {
    if (!online) return;
    online = null;
    track.laps = TRACK_LAPS; laps = TRACK_LAPS;
    stopSong(); unpause();
    buildRun();
    resetAll();
    syncAssist();
    debug.online = null;
    if (state === 'race' || state === 'results') state = 'menu';
    if (showLobby && !hud.card) hud.setCard('lobby', {});
  }
  hud.on('online-start', startOnline);
  hud.on('online-race', () => goOnline());
  hud.on('online-finish', (m) => {
    if (!online) return;
    debug.onlineResult = m && m.lines ? m.lines.map((l) => [l.place, l.name, l.time]) : null;
    unpause();
    state = 'results';
    hud.setCard('lobby', { view: 'results' });
  });
  hud.on('online-left', () => endOnline());
  hud.on('online-abort', () => endOnline(true));
  hud.on('online-closed', () => endOnline(true));

  hud.on('race', startRace);
  hud.on('again', startRace);
  hud.on('restart', () => { if (online) setPaused(false); else startRace(); });   // online a restart only resumes
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
  bus.on('go', () => { goAt = performance.now(); });
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
  // drop an audio file anywhere: it is the race song from now on (solo); mid-race it starts at once
  const audioFile = (e) => { const f = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0]; return f && /^audio\//.test(f.type) ? f : null; };
  window.addEventListener('dragover', (e) => { if (!online && e.dataTransfer && [...e.dataTransfer.types].includes('Files')) e.preventDefault(); });
  window.addEventListener('drop', (e) => {
    const f = online ? null : audioFile(e);
    if (!f) return;
    e.preventDefault();
    pickedSong = f;
    if (state === 'race') Promise.resolve(audio.start()).then(() => playSong()).catch((err) => console.warn('[glassroad] song', err));
  });

  // warm every material and the post chain behind the warming card, a group at a time (the bar moves)
  hud.setLoading('Preparing shaders', 0.55);
  await paintGap();
  // Native mirrors skip capture from behind their planes. Warm from inside the hall
  // so its reflection pipelines compile behind the loading card, before the first lap.
  const warmPose = world.mirrorRoom ? cam.camera.clone() : null;
  const warmView = (views) => ({ kart: views[0].st, karts: views, fx: fx.state, timeS: 0, beat: clock.beat,
    raceTimeS: 0, sectionIdx: views[0].st[S.sectionIdx], items: items.view(), mode: 'menu', camera: cam.camera, paused: false,
    scanTimeS: 0, scanReady: scan.ready, perfumeReady: perfume.ready });
  let warm;
  try {
    if (warmPose) {
      const hall = boudoirLayout.landmarks.find(row => row.id === 'drawer-hall');
      peekCamera(cam.camera, track, { s: hall.s0 + 80, backM: 10, upM: 5, aheadM: 40 }, renderer.aspect);
    }
    warm = await renderer.compile(world.scene, cam.camera, undefined,
      { onProgress: (k, n) => hud.setLoading('Preparing shaders ' + k + '/' + n, 0.55 + 0.4 * k / n) });
    if (warmPose) {
      hud.setLoading('Preparing mirrors', .96);
      await paintGap();
      const hall = boudoirLayout.landmarks.find(row => row.id === 'drawer-hall');
      // Temporary view copies put all racers in the capture without changing race state.
      const warmKarts = kartViews.map((r, i) => {
        const st = r.st.slice(), s = hall.s0 + 82 + i * 1.6, f = track.frameAt(s);
        f.p.forEach((value, j) => { st[S.posX + j] = value + f.b[j] * ((i % 3) - 1) * 4; });
        st[S.s] = s;
        return { ...r, st };
      });
      world.update(warmView(warmKarts));
      await world.mirrorRoom.warmCapture(renderer, world.scene, cam.camera);
    }
  } finally {
    if (warmPose) {
      cam.camera.position.copy(warmPose.position); cam.camera.quaternion.copy(warmPose.quaternion); cam.camera.up.copy(warmPose.up);
      cam.camera.updateMatrixWorld(true);
      world.update(warmView(kartViews));
    }
  }
  debug.warm = warm;
  // and the gate screen's own context and Loom program (its first build froze the race at the first gate)
  hud.setLoading('Last touches', 0.97);
  await paintGap();
  gateScreen.warm(world.spirals && world.spirals.gates[0] ? world.spirals.gates[0].params : null);
  hud.setLoading('Ready', 1);
  hud.setReady(true);
  bridge.ready();
  toMenu();
  // ?r=<token>: a friend's race link goes straight to the lobby card, which claims it (seated, waiting or refused)
  const linkTok = course.online ? linkToken(location.search) : null;
  if (linkTok) { hud.setCard('lobby', { claim: linkTok }); lobbyUi = hud.lobby || lobbyUi; }
  if (BOT && !link) botT = performance.now() + 2500;   // attract: a short look at the menu, then race (never with a lobby)

  let fpsN = 0, fpsT = performance.now(), telT = 0;

  const DIAG = (location.port === '8790' || location.port === '8792') && (location.hostname === '127.0.0.1' || location.hostname === 'localhost');

  let diagT = 0, cardThrottle = false;
  // polish 5 (J2): rival name chips hide through the count and fade back after go; the Go burst sits on your kart
  let goAt = -1;
  const kartV = world.kart ? world.kart.position.clone() : null, kartAt = { x: 0.5, y: 0.66 };
  function dimChips(a) {
    for (let i = 1; i < world.karts.length; i++) {
      const root = world.karts[i];
      if (!root) continue;
      for (const o of root.children) {
        // the name tag (render/karts.js tagSprite): a canvas sprite at renderOrder 4; pose() set its opacity this frame
        if (!o.isSprite || o.renderOrder !== 4 || !o.material || !o.material.map || !o.material.map.isCanvasTexture) continue;
        o.material.opacity *= a;
        if (o.material.opacity <= 0.01) o.visible = false;
      }
    }
  }
  function frame(now) {
    requestAnimationFrame(frame);
    if (!renderer.frameDue(now)) return;                          // the Frame cap option skips this one
    if (HT) hT = performance.now();
    const { steps, alpha, frameDt, renderTime } = clock.frame(now);
    if (HT) HT.steps = steps;
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
    // online: go lands on the room's startAt (race.start() runs the local count of goS); the sim never pauses
    if (online && state === 'race' && !online.go && online.s.serverNow() >= online.s.raceStartAt(goS)) { online.go = true; race.start(); syncAssist(); }
    // online the count keeps server time (bells and go on the room's clock, even through a slow frame)
    if (online && online.go && race.phase === 'countdown') race.syncCount((online.s.serverNow() - online.s.raceStartAt(goS)) / 1000 - steps * clock.dt);
    const simOn = inRace && (online ? online.go : !paused);
    if (simOn) for (let i = 0; i < steps; i++) {                    // 2. sim
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
      // paused online: your kart coasts on the assist (neutral hands, cruise and auto) while the race goes on
      const coast = online && paused;
      if (coast) { a.steer = 0; a.throttle = 0; a.brake = 0; a.hop = false; a.hopPressed = false; a.item = false; }
      const pa = BOT && !debug.assistOn && !coast ? a : assist.apply(a, P.st, clock.dt, race.phase);
      if (devHook && !online) devHook.apply(pa, clock.dt);         //    ?dev=1 only: a scripted drive holds the hands
      race.update(clock.dt, pa, P.st);
      racers.step(clock.dt, pa, race, sctx);
      if (race.phase === 'racing') {
        const sc = scanClock(audio.music, race.timeS);
        scan.update(racers.list, sc.timeS, sc.bpm);
        perfume.update(racers.list, sc.timeS, clock.dt);
      }
      contact.update(clock.dt, racers.list, race);                //    bumps, ghosts, slipstream
      standings.update(racers.list, race, clock.dt);              //    places, race-result
      items.update(clock.dt, racers.list, standings, race);       //    pickups, use, flights, hits, mods
      hopLatch.spent(a); itemLatch.spent(a);
    }
    if (HT) hLap('sim');
    if (online) online.s.update({ paused, assist: assist.mode !== 'off' });   //    online: clock slew, pings, uploads
    queue.flush(bus);                                             // 3. events
    const frozen = paused && !online;                             //    online the pause card freezes nothing
    const fxDt = frozen ? 0 : frameDt;                            //    the pause card freezes every effect too
    fx.update(fxDt, P.st, race);                                  // 4. fx
    effects.update(fxDt);                                         //    the local screen effect
    if (HT) hLap('fx');
    const fs = fx.state;
    const pst = P.st;
    audio.update(pst[S.speedPct], {                               // 5. audio
      boosting: pst[S.boostKind] > 0, drifting: pst[S.driftDir] !== 0, driftTier: pst[S.driftStage],
      airborne: pst[S.airborne] > 0, onRoad: pst[S.grounded] > 0, edge: fs.edgeGlow > 0, paused: frozen, duck: fs.duck,
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
    if (HT) hLap('audio');

    racers.interpolate(alpha);                                    // 6. camera
    if (online) online.s.drawRemotes();                           //    remote karts at this frame's server time (100 ms back)
    const mode = state === 'menu' ? 'menu' : state === 'results' ? 'results' : 'race';
    cam.update(P.view, fs, track, frameDt, renderer.aspect, mode);
    if (HT) hLap('cam');
    if (BOT && debug.peek) peekCamera(cam.camera, track, debug.peek, renderer.aspect);
    const iv = items.view();
    // the song position the player hears (frozen while paused: the context is suspended)
    const music = audio.music, song = inRace && music ? music.song : null, songS = song ? music.songS : NaN;
    const sc = scanClock(inRace ? music : null, race.timeS);
    if (BOT) debug.scan = { hits: scan.hits, ready: scan.ready, timeS: sc.timeS };
    const pv = P.view;
    const wordLive = words && song ? words.update(songS,
      { s: pv[S.s], lat: pv[S.lateral], speedMps: Math.max(0, pv[S.speedPct]) * feel.get('speed.topMps') }) : NO_WORDS;
    if (words) wordCount.set(words.pops, words.total, words.streak);
    // renderTime (sim time + alpha x dt), not simTime: whole sim steps would judder the world on a
    // display that is not 60 Hz
    for (let i = 0; i < kartViews.length; i++) kartViews[i].glide = !online && glideActive(racers.list[i].st);
    if ((keepLayout || boudoirLayout) && inRace && !paused && !online && pst[S.lap] !== glideHintLap) {
      const bridge = boudoirLayout?.mirrorJump || keepLayout.drawbridge;
      if (pst[S.s] >= bridge.rampS0 - 65 && pst[S.s] < bridge.rampS1) {
        glideHintLap = pst[S.lap]; hud.toast('Hold jump to fly with the propeller cap');
      }
    }
    if (BOT) debug.gliding = !!kartViews[0]?.glide;
    world.update({ kart: P.view, karts: kartViews, fx: fs, timeS: renderTime, beat: clock.beat,
      raceTimeS: racers.clockS, sectionIdx: pst[S.sectionIdx], items: iv, mode, camera: cam.camera, paused, song, songS, words: wordLive,
      scanTimeS: sc.timeS, scanBpm: sc.bpm, scanReady: scan.ready, perfumeReady: perfume.ready });
    if (BOT && song && world.musicRoad) {                         //    dev only: the boot check reads the music road
      const m = world.musicRoad.frame;
      debug.song = { t: Math.round(songS * 100) / 100, section: m.sectionId, calm: +m.calm.toFixed(2), helix: +m.helix.toFixed(2),
        big: +m.big.toFixed(2), shatter: +m.shatter.toFixed(2), wave: +m.waveAmp.toFixed(2), words: words ? words.pops : 0, wordsOf: words ? words.total : 0 };
    }
    if (BOT) debug.boxAheadM = boxAhead(P.view, iv.boxes);        //    dev only: the boot check's item box shot
    if (BOT && world.roadWords) debug.roadWords = world.roadWords.stats();   // dev only: the painted road words
    const rph = state === 'race' ? race.phase : null;              //    J2: chips off through the count, back after go
    const chipA = rph ? chipAlpha(rph, goAt < 0 ? 0 : (now - goAt) / 1000, feel.get('gentle.on') ? 1.2 : 0.6) : 1;
    if (chipA < 1) dimChips(chipA);
    let goPoint = null;
    if (kartV && rph && (rph === 'countdown' || rph === 'ready' || (goAt > 0 && now - goAt < 1500))) {
      kartV.copy(world.kart.position); kartV.y += 0.6;
      kartV.project(cam.camera);
      const k = kartPoint(kartV.x, kartV.y, kartV.z);
      if (k) { kartAt.x = k.x; kartAt.y = k.y; goPoint = kartAt; }
    }
    if (HT) hLap('world');
    renderer.setHold(state === 'race');                           //    no tier step (a relink) mid-race or under the pause card
    const inGarage=!!garage&&state==='menu'&&hud.card==='menu';
    garage?.setVisible(inGarage);
    if(inGarage)garage.render(frameDt,hostGentle||!!feel.get('gentle.on')||!!hud.options.gentlePreview||matchMedia('(prefers-reduced-motion: reduce)').matches);
    else renderer.render({ scene: world.scene, camera: cam.camera, fx: fs, dt: frozen ? 0 : frameDt });   // 7. render
    if (HT) hLap('render');

    const place = inRace && standings.order.length ? standings.placeOf(0) : 0;   // 8. hud
    for (let i = 0; i < racers.list.length; i++) { const s = racers.list[i].view; mapXz[i * 2] = s[S.posX]; mapXz[i * 2 + 1] = s[S.posZ]; }
    const it = P.item, holding = inRace && (it.id || it.rolling);
    if (holding) { hudItem.id = it.id; hudItem.rolling = it.rolling; hudItem.rollT = it.rollT; hudItem.rollS = feel.get('items.rollS'); }
    hud.update({ lap: pst[S.lap], laps, timeS: race.timeS, speedPct: pst[S.speedPct], countdown: null,
      sectionIdx: pst[S.sectionIdx], paused, place, count: racers.list.length, item: holding ? hudItem : null,
      map: mapXz, finished: P.finished, bestLapS: P.bestLapS, kartAt: goPoint });
    gateScreen.update(frameDt, paused);
    slotFx.update(paused ? 0 : frameDt, holding && !P.finished ? hudItem : null, !!feel.get('gentle.on'));

    if (!online && state === 'race' && finishAt && now >= finishAt && result) {   // results after the finish swell (online: the room's)
      finishAt = 0;
      state = 'results';
      hud.setCard('results', result);
      if (BOT) botT = now + 5000;
    }
    if (HT) hLap('hud');
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
