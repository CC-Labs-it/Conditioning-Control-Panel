/* sim/racers.js - N karts through the unchanged Phase A sim (CONTRACT-B1.md sections 2 and 3).
 *
 * Racer 0 is the local player, 1..N-1 are bots. Each racer owns a KartState, a progress tracker and an
 * event sink: the player's events go to the frame queue exactly as in Phase A, a bot's are re-emitted as
 * racer-event { racer, name, p }. Both sinks record lap and finish on the racer first. Item effects reach
 * the sim without a kart.js edit: `act` is the input after mods, and each racer reads feel through its own
 * overlay (grip, slide, top speed, soft rails for the steering assist). No three.js.
 * Online (B2, `net` option, NET.md): one racer per seat at that seat's grid slot, racer 0 = this client's seat.
 * A racer of kind 'remote' runs no sim: racers.remote(r, dt) (net/remote.js) writes its state each tick. The
 * bot seats this client keeps are kind 'local' bots; setKind moves a seat between the two (bot keeper).
 */
import { S, BOOST, RESPAWN, createState, copyState, lerpState } from './state.js';
import { spawn, step as kartStep } from './kart.js';
import { updateRespawn } from './respawn.js';
import { createProgress } from './progress.js';
import { createActions } from '../core/actions.js';
import { clamp } from '../core/math.js';
import { gap } from './gaps.js';

const DEG = Math.PI / 180;
const has = Object.prototype.hasOwnProperty;

/** A per-racer view of feel: get(path) returns over[path] when set, else the base. */
export function createFeelOverlay(feel) {
  const over = {};
  const get = (path) => (has.call(over, path) ? over[path] : feel.get(path));
  const rad = (path) => { const v = get(path); return Array.isArray(v) ? v.map((d) => d * DEG) : v * DEG; };
  return {
    over, get, rad,
    set: (p, v) => feel.set(p, v), subscribe: (fn) => feel.subscribe(fn),
    reset: () => feel.reset(), toJSON: () => feel.toJSON(),
  };
}

export const identityMods = (m = {}) => Object.assign(m, { steerMul: 1, steerAdd: 0, throttleMul: 1, gripMul: 1, slideMul: 1, capMul: 1, shoveMul: 1, blind: 0 });

function newActions() { return Object.assign(createActions(), { item: false, back: false }); }

export function createRacers(track, feel, queue, { count = 8, seed = 1, net = null } = {}) {
  const list = [];
  if (net) count = net.length;
  const grid = () => {
    const ps = clamp(feel.get('ai.playerSlot') | 0, 0, count - 1), slots = [ps];
    for (let k = 0; slots.length < count; k++) if (k !== ps) slots.push(k);
    return slots;
  };
  const slots = net ? net.map((x) => x.seat) : grid();
  for (let i = 0; i < count; i++) list.push(makeRacer(i, slots[i]));
  const racers = { list, player: list[0], clockS: 0, seed, place, step, interpolate, setAssist, nearby, setKind, remote: null };

  function makeRacer(i, slot) {
    const n = net && net[i], isBot = n ? n.kind === 'bot' : i > 0;
    const r = {
      i, kind: n && i > 0 && !n.local ? 'remote' : 'local',
      meta: n ? { i, id: 'seat' + n.seat, name: i === 0 ? 'You' : n.name, isBot, tint: n.seat, slot, seat: n.seat, look: n.look, left: false, paused: false }
        : { i, id: isBot ? 'bot' + i : 'you', name: isBot ? 'EMI bot' : 'You', isBot, tint: i, slot },
      st: createState(), prev: createState(), view: createState(),
      input: newActions(), act: newActions(), feel: createFeelOverlay(feel), mods: identityMods(),
      item: { id: null, rolling: false, rollT: 0, heldT: 0 },
      fxv: { spinRad: 0, shield: null, echo: 0, effect: null, ghost: false, slip: 0, place: i + 1 },
      assist: false, raceView: null, lapTimes: [], bestLapS: 0, finished: false, finishS: 0,
      progressM: 0, place: i + 1, rubber: 1, falls: 0,
      events: null, progress: null,
    };
    // sinks: record lap / finish / drop on the racer, then forward (player as is, bots as racer-event)
    r.events = {
      emit(name, p = {}) {
        if (name === 'lap') {
          r.lapTimes.push(p.lapTimeS);
          r.bestLapS = r.bestLapS > 0 ? Math.min(r.bestLapS, p.lapTimeS) : p.lapTimeS;
        } else if (name === 'finish') { r.finished = true; r.finishS = p.timeS; }
        else if (name === 'drop') r.falls++;
        if (i > 0) queue.emit('racer-event', { racer: i, name, p });   // online, a remote person is not the player either
        else queue.emit(name, p);
      },
    };
    r.progress = createProgress(track, r.events);
    if (isBot) r.raceView = botRace();
    return r;
  }

  /** A bot's race: its own phase, the shared clock, its own lap start. */
  function botRace() {
    const v = { phase: 'ready', timeS: 0, lapStartS: 0, finish() { if (v.phase === 'racing') v.phase = 'finished'; } };
    return v;
  }

  function place() {
    racers.clockS = 0;
    for (const r of list) {
      spawn(r.st, track, feel, r.meta.slot);
      copyState(r.prev, r.st); copyState(r.view, r.st);
      Object.assign(r.input, newActions()); Object.assign(r.act, newActions());
      identityMods(r.mods);
      for (const k of Object.keys(r.feel.over)) delete r.feel.over[k];
      Object.assign(r.item, { id: null, rolling: false, rollT: 0, heldT: 0 });
      Object.assign(r.fxv, { spinRad: 0, shield: null, echo: 0, effect: null, ghost: false, slip: 0, place: r.i + 1 });
      r.lapTimes.length = 0; r.bestLapS = 0; r.finished = false; r.finishS = 0;
      r.progressM = 0; r.place = r.i + 1; r.rubber = 1; r.falls = 0;
      if (r.meta.isBot) Object.assign(r.raceView, { phase: 'ready', timeS: 0, lapStartS: 0 });
      r.progress.reset(r.st);
    }
  }

  function step(dt, a, race, ctx = {}) {
    const { ai = null, items = null, standings = null } = ctx;
    const live = race.phase === 'racing' || race.phase === 'finished';
    if (race.phase === 'racing') racers.clockS = race.timeS;
    else if (race.phase === 'finished') racers.clockS += dt;
    const aiCtx = { racers: list, items, standings, race };
    const base = basePace();
    for (const r of list) {
      copyState(r.prev, r.st);
      if (r.kind === 'remote') { if (racers.remote) racers.remote(r, dt); continue; }
      if (r.meta.isBot) {
        const v = r.raceView;
        if (v.phase !== 'finished') v.phase = live ? 'racing' : race.phase;
        v.timeS = racers.clockS;
      } else r.raceView = race;

      // intent
      const inp = r.input;
      if (!r.meta.isBot && !(r.finished && ai)) {
        inp.item = false; inp.back = false;
        Object.assign(inp, a);
      } else if (ai) ai.drive(r, aiCtx, dt, inp);
      else Object.assign(inp, newActions());

      // act = input after mods
      const m = r.mods, act = r.act;
      Object.assign(act, inp);
      act.steer = clamp(inp.steer * m.steerMul + m.steerAdd, -1, 1);
      act.throttle = inp.throttle * m.throttleMul;

      // per-racer feel overlay
      const o = r.feel.over;
      const pace = r.meta.isBot ? base[Math.min(r.i - 1, base.length - 1)] * (r.rubber || 1) : 1;
      o['kart.gripTcS'] = gap(feel, 'kart.gripTcS') * m.gripMul;
      o['drift.slideTcS'] = gap(feel, 'drift.slideTcS') * m.slideMul;
      if (r.meta.isBot) o['steer.userMul'] = 1;        // steering sensitivity is the player's option only
      o['speed.topMps'] = feel.get('speed.topMps') * m.capMul * pace;
      if (r.assist) o['gentle.on'] = true; else delete o['gentle.on'];

      if (live) kartStep(r.st, act, track, r.feel, dt, r.events);
      updateRespawn(r.st, track, r.feel, dt, r.events);
      r.progress.update(r.st, r.raceView);
    }
  }

  /** Bot pace multipliers by bot index (bot k -> [k - 1]): personality pace x difficulty. */
  function basePace() {
    const pace = feel.get('ai.paceMul'), muls = feel.get('ai.difficultyMul');
    const d = muls[clamp(feel.get('ai.difficulty') | 0, 0, muls.length - 1)];
    return pace.map((p) => p * d);
  }

  /** Online: 'remote' (state written by net) or 'local' (a kept bot simulated here). */
  function setKind(i, kind) { list[i].kind = kind === 'remote' ? 'remote' : 'local'; }

  function interpolate(alpha) { for (const r of list) lerpState(r.view, r.prev, r.st, alpha); }

  function setAssist(on) { racers.player.assist = !!on; }

  /** The n nearest rivals within maxM of the player: distance, pan (+ right of the player), speed, boosting. */
  function nearby(n, maxM) {
    const me = racers.player.st, out = [];
    const qx = me[S.quatX], qy = me[S.quatY], qz = me[S.quatZ], qw = me[S.quatW];
    // the player's right axis: quaternion applied to +X
    const rx = 1 - 2 * (qy * qy + qz * qz), ry = 2 * (qx * qy + qw * qz), rz = 2 * (qx * qz - qw * qy);
    for (const r of list) {
      if (r === racers.player) continue;
      const dx = r.st[S.posX] - me[S.posX], dy = r.st[S.posY] - me[S.posY], dz = r.st[S.posZ] - me[S.posZ];
      const distM = Math.hypot(dx, dy, dz);
      if (distM > maxM) continue;
      const pan = distM > 1e-3 ? clamp((dx * rx + dy * ry + dz * rz) / distM, -1, 1) : 0;
      out.push({ i: r.i, distM, pan, speedPct: r.st[S.speedPct], boosting: r.st[S.boostKind] !== BOOST.NONE });
    }
    out.sort((p, q) => p.distM - q.distM);
    if (out.length > n) out.length = Math.max(0, n);
    return out;
  }

  place();
  return racers;
}

/** True while respawn owns the kart (PULL, HOVER, RELEASE). */
export const rescuing = (st) => { const p = st[S.respawnPhase]; return p === RESPAWN.PULL || p === RESPAWN.HOVER || p === RESPAWN.RELEASE; };
