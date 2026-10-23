/* tests/sim-routes.test.mjs - the drop shortcut (polish 4 ROUTES): the open edge over the lower straight,
 * landing there, the lap credit, the exploit guard, the kerb that needs a hop and the rescue on a miss. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { validate } from '../track/schema.js';
import { S, RESPAWN, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { createProgress, STEP_MAX_M } from '../sim/progress.js';
import { registeredSkip, edgeAt } from '../sim/routes.js';
import { createActions } from '../core/actions.js';
import { qfromBasis, wrap } from '../core/math.js';
import { makeFeel, DT } from './sim-fake.mjs';
import { makeRace } from './sim-racers-harness.mjs';

const JSON_TRACK = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const track = loadTrack(JSON_TRACK);
const R0 = track.routes[0];
const clone = () => JSON.parse(JSON.stringify(JSON_TRACK));

/** A kart on the road at s, lateral lat, at speed v, nose yawDeg left of the road. */
function kartAt(s, lat, v = 30, yawDeg = 0) {
  const st = createState(), f = track.frameAt(s), c = Math.cos(yawDeg * Math.PI / 180), sn = Math.sin(yawDeg * Math.PI / 180);
  const t = [0, 1, 2].map((k) => f.t[k] * c - f.b[k] * sn), b = [0, 1, 2].map((k) => f.b[k] * c + f.t[k] * sn);
  setPos(st, [f.p[0] + f.b[0] * lat, f.p[1] + f.b[1] * lat, f.p[2] + f.b[2] * lat]);
  setQuat(st, qfromBasis([0, 0, 0, 1], b, f.n, [-t[0], -t[1], -t[2]]));
  setVel(st, [t[0] * v, t[1] * v, t[2] * v]);
  st[S.engineV] = v; st[S.grounded] = 1; st[S.seg] = f.seg; st[S.latchSeg] = f.seg; st[S.s] = s; st[S.lateral] = lat; st[S.hopT] = 1e3;
  return st;
}

/**
 * Drive from s0: pursue the straight line through the landing pad, `lineM` to the open side, and hop the
 * kerb `hopEdgeM` from the edge, from hopLeadM before the opening (the bots' recipe). Returns events, the state and a progress tracker's view.
 */
function drive({ s0 = 470, lat0 = 0, lineM = 2.8, hopEdgeM = 2.2, hopLeadM = 3, hop = true, maxS = 8, race = null } = {}) {
  const feel = makeFeel(), st = kartAt(s0, lat0), a = createActions(), ev = [];
  const sink = { emit: (name, p = {}) => ev.push({ name, p, s: st[S.s] }) };
  const prog = createProgress(track, sink);
  prog.reset(st);
  st[S.checkpoint] = 1;                              // s0 is past checkpoint 1
  const pt = R0.pad.t, pp = R0.pad.p, pn = R0.pad.n;
  const b = [pt[1] * pn[2] - pt[2] * pn[1], pt[2] * pn[0] - pt[0] * pn[2], pt[0] * pn[1] - pt[1] * pn[0]];
  const off = R0.side === 'L' ? -lineM : lineM;
  for (let i = 0; i < maxS / DT; i++) {
    a.throttle = 1; a.hop = false; a.hopPressed = false;
    if (st[S.respawnPhase] === RESPAWN.NONE) {
      const along = (st[S.posX] - pp[0]) * pt[0] + (st[S.posY] - pp[1]) * pt[1] + (st[S.posZ] - pp[2]) * pt[2] + 25;
      const tx = pp[0] + pt[0] * along + b[0] * off - st[S.posX], tz = pp[2] + pt[2] * along + b[2] * off - st[S.posZ];
      const qx = st[S.quatX], qy = st[S.quatY], qz = st[S.quatZ], qw = st[S.quatW];
      const rx = 1 - 2 * (qy * qy + qz * qz), rz = 2 * (qx * qz - qw * qy), fx = -(2 * (qx * qz + qw * qy)), fz = -(1 - 2 * (qx * qx + qy * qy));
      a.steer = Math.max(-1, Math.min(1, Math.atan2(tx * rx + tz * rz, tx * fx + tz * fz) * 3));
      const into = wrap(st[S.s] - (R0.openS0 - hopLeadM), track.lengthM);
      if (hop && st[S.grounded] > 0 && into < R0.openS1 - R0.openS0 + hopLeadM && -st[S.lateral] > 6.5 - hopEdgeM) {
        a.hop = true; a.hopPressed = true; a.steer = Math.max(-0.15, Math.min(0.15, a.steer));
      }
    }
    step(st, a, track, feel, DT, sink);
    updateRespawn(st, track, feel, DT, sink);
    if (race) { race.timeS += DT; prog.update(st, race); }
    if (st[S.respawnPhase] === RESPAWN.GRACE) break;
    if (st[S.s] > 940 && st[S.s] < 1100 && st[S.grounded] > 0) break;
  }
  return { ev, st, names: ev.map((e) => e.name) };
}
const fakeRace = () => ({ phase: 'racing', timeS: 0, lapStartS: 0, finish() { this.phase = 'finished'; } });

test('routes: threshold carries one drop shortcut, left open over the lower straight', () => {
  assert.equal(validate(JSON_TRACK).ok, true);
  assert.equal(track.routes.length, 1);
  const r = R0;
  assert.equal(r.side, 'L');
  for (let i = r.from; i < r.to; i++) {
    const fl = JSON_TRACK.nodes[i].flags;
    assert.ok(fl.includes('railR') && !fl.includes('rail') && !fl.includes('railL'), 'node ' + i + ' ' + fl);
  }
  assert.ok(JSON_TRACK.nodes[r.to].flags.includes('rail') && JSON_TRACK.nodes[r.from - 1].flags.includes('rail'));
  const drop = JSON_TRACK.nodes[r.from].p[1] - JSON_TRACK.nodes[r.land.from].p[1];
  assert.ok(drop >= 10 && drop <= 16, 'drop ' + drop);
  assert.ok(r.openS1 - r.openS0 >= 7 && r.openS1 - r.openS0 <= 9, 'the opening is about 8 m (EDGES lane): ' + (r.openS1 - r.openS0).toFixed(1));
  assert.ok(r.openS0 >= r.fromS && r.openS1 <= r.toS);
  assert.ok(r.pad.s >= r.land.fromS && r.pad.s <= r.land.toS);
});

test('routes: the schema refuses a railed gap and a landing behind the gap', () => {
  const a = clone();
  a.nodes[a.routes[0].from].flags = ['rail'];
  assert.match(validate(a).errors.join(';'), /must leave the L side open/);
  const b = clone();
  b.routes[0].land = { from: 10, to: 12 };
  b.routes[0].pad.node = 11;
  assert.match(validate(b).errors.join(';'), /ahead of the gap/);
  const c = clone();
  delete c.routes;
  assert.equal(validate(c).ok, true, 'routes are optional');
});

/** A real race with bot `bi` sent down the shortcut on its first lap (the bots' own recipe). */
function forcedBot(bi = 1, seed = 1) {
  const R = makeRace({ seed });
  R.tick();
  const b = R.ai.botOf(bi);
  b.route = R.track.routes[0]; b.routeLap = 0;
  R.run(60, () => R.racers.list[bi].lapTimes.length >= 1);
  const ev = R.log.filter((e) => e.name === 'racer-event' && e.payload.racer === bi).map((e) => ({ name: e.payload.name, p: e.payload.p, t: e.t }));
  return { R, ev, names: ev.map((e) => e.name), r: R.racers.list[bi] };
}

test('routes: a hop through the opening lands on the lower straight, s jumps forward, the catch holds', () => {
  const { ev, names } = forcedBot(1);
  const land = ev.find((e) => e.name === 'route-land');
  assert.ok(land, names.filter((n) => /route|drop|wall|hop/.test(n)).join(','));
  assert.ok(names.indexOf('route-drop') < names.indexOf('route-land'));
  assert.ok(land.p.skipM > 280 && land.p.skipM < 380, 'skip ' + land.p.skipM);
  assert.ok(land.p.s >= R0.land.fromS && land.p.s <= R0.land.toS, 'landed at s ' + land.p.s);
  assert.equal(land.p.clean, true, 'the bot line through the pad is clean');
  const i = names.indexOf('route-land'), j = names.indexOf('route-boost');
  assert.ok(j > i && ev[j].t - ev[i].t >= 1.5, 'the boost waits out the catch');
});

test('routes: the skip credits the jumped checkpoint once, and the lap still counts', () => {
  const { ev, r } = forcedBot(1);
  const skip = ev.findIndex((e) => e.name === 'route-skip');
  assert.ok(skip > 0, 'skip registered');
  const cps = ev.filter((e) => e.name === 'checkpoint').map((e) => e.p.idx);
  assert.deepEqual(cps, [1, 2, 3, 0], 'every checkpoint once, in order');
  assert.equal(ev.findIndex((e) => e.name === 'checkpoint' && e.p.idx === 2), skip + 1, 'checkpoint 2 comes with the skip');
  assert.equal(r.lapTimes.length, 1);
  assert.ok(r.lapTimes[0] < 42, 'a shortcut lap ' + r.lapTimes[0].toFixed(1) + ' s');
});

test('routes: the opening is a kicker lip, the rest of the open side a rail (EDGES lane: no kerb, no hop needed)', () => {
  const q = (s) => { const f = track.frameAt(s); return track.query(f.p[0] - f.b[0] * 6, f.p[1] - f.b[1] * 6, f.p[2] - f.b[2] * 6, f.seg); };
  assert.equal(edgeAt(track, q((R0.openS0 + R0.openS1) / 2), -6), 'kicker');
  assert.equal(edgeAt(track, q(R0.openS0 - 3), -6), 'rail');
  assert.equal(edgeAt(track, q(R0.openS1 + 1.5), -6), 'rail');
  assert.equal(edgeAt(track, q(R0.openS0 - 0.5), -6, 0.7), 'kicker', 'a body over the opening never scrapes the rail end');
  assert.equal(edgeAt(track, q((R0.openS0 + R0.openS1) / 2), 6), null, 'the far side is a plain rail');
  const { names } = drive({ hop: false, lineM: 6, maxS: 6 });
  assert.ok(names.includes('route-land'), 'driven through, no hop: ' + names.filter((n) => /route|drop|edge|wall/.test(n)).join(','));
});

test('routes: an unregistered or backwards jump never credits a checkpoint', () => {
  const evs = [], sink = { emit: (n, p) => evs.push([n, p]) };
  const prog = createProgress(track, sink), race = fakeRace();
  const st = kartAt(300, 0);
  prog.reset(st); st[S.checkpoint] = 0;
  // a teleport forward across checkpoint 1 (s 334), on the ground: refused
  st[S.s] = 300 + 200; prog.update(st, race);
  assert.equal(st[S.checkpoint], 0);
  assert.deepEqual(evs.filter(([n]) => n === 'checkpoint'), []);
  // a route-sized jump that does not start at the open edge: refused
  st[S.s] = 400; prog.update(st, race);
  st[S.s] = 900; prog.update(st, race);
  assert.deepEqual(evs.filter(([n]) => n === 'checkpoint' || n === 'route-skip'), []);
  // backwards from the landing stretch to the open edge: nothing
  st[S.checkpoint] = 2; st[S.s] = 905; prog.update(st, race);
  st[S.s] = 560; prog.update(st, race);
  assert.equal(st[S.checkpoint], 2);
  assert.equal(registeredSkip(track, 905, 560), null);
  assert.equal(registeredSkip(track, 400, 900), null);
  assert.ok(registeredSkip(track, 570, 900));
  assert.ok(STEP_MAX_M >= 20 && STEP_MAX_M < 100);
});

test('routes: a jump held in the air waits for touchdown; a rescue back never credits', () => {
  const evs = [], sink = { emit: (n, p) => evs.push([n, p]) };
  const prog = createProgress(track, sink), race = fakeRace();
  const st = kartAt(570, -6);
  prog.reset(st); st[S.checkpoint] = 1;
  st[S.grounded] = 0; st[S.airborne] = 1;
  st[S.s] = 905; prog.update(st, race);                 // the probe snapped while falling: not judged yet
  assert.equal(st[S.checkpoint], 1);
  st[S.respawnPhase] = RESPAWN.PULL; st[S.s] = track.nodeS[R0.from]; prog.update(st, race);   // missed: rescued back
  st[S.respawnPhase] = RESPAWN.NONE; st[S.grounded] = 1;
  st[S.s] += 1; prog.update(st, race);
  assert.equal(st[S.checkpoint], 1, 'no credit for a miss');
});

test('routes: a wild exit misses the lower road and takes the normal rescue at the upper road', () => {
  const feel = makeFeel(), st = kartAt(R0.openS0 + 0.5, -5.2, 30, 35), a = createActions(), names = [];
  const sink = { emit: (n) => names.push(n) };
  a.throttle = 1; a.hop = true; a.hopPressed = true;
  let lowest = Infinity;
  for (let i = 0; i < 5 / DT && st[S.respawnPhase] !== RESPAWN.GRACE; i++) {
    step(st, a, track, feel, DT, sink); updateRespawn(st, track, feel, DT, sink);
    a.hopPressed = false; a.hop = false;
    lowest = Math.min(lowest, st[S.posY]);
  }
  assert.ok(!names.includes('route-land'), 'did not land');
  assert.ok(names.includes('drop') && names.includes('rescue-pull'), names.filter((n) => /route|drop|rescue|edge/.test(n)).join(','));
  assert.ok(st[S.s] >= R0.fromS - 1 && st[S.s] < 620, 'rescued onto the upper road at s ' + st[S.s].toFixed(0));
});

test('routes: bots take the shortcut sometimes, land it and still finish', () => {
  let landed = 0, drops = 0, finished = 0, bots = 0;
  for (const seed of [1, 4]) {
    const R = makeRace({ seed });
    R.run(200, () => R.racers.list.every((r) => r.finished));
    landed += R.botEvents('route-land').length;
    drops += R.botEvents('drop').length;
    finished += R.racers.list.filter((r) => r.finished).length;
    bots += R.racers.list.length;
  }
  assert.ok(landed >= 2, 'route landings ' + landed);
  assert.ok(landed <= 0.5 * (bots - 2) * 3, 'not every lap: ' + landed);
  assert.equal(finished, bots, 'every racer finishes');
  assert.ok(drops <= 3, 'drops ' + drops);
});
