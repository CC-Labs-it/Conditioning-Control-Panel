/* tests/sim-routes.test.mjs - the shortcut route (polish 6: "dare", a gap jump off the OUTSIDE of the coil's first turn
 * onto its last, which runs round outside it 2.5 m off and 4-5 m lower, the same way round): the open window, the
 * kicker lip and rails, a sharp cut that lands on the last turn and is credited, the exploit guard, a forgiving
 * shallow cut, the rescue on a wild miss and the bots. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { validate } from '../track/schema.js';
import { S, RESPAWN, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { createProgress, STEP_MAX_M } from '../sim/progress.js';
import { registeredSkip, edgeAt, airCatch } from '../sim/routes.js';
import { createActions } from '../core/actions.js';
import { qfromBasis, wrap } from '../core/math.js';
import { makeFeel, DT } from './sim-fake.mjs';
import { makeRace } from './sim-racers-harness.mjs';

const JSON_TRACK = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const track = loadTrack(JSON_TRACK);
const R0 = track.routes[0];
const SIDE = R0.side === 'L' ? -1 : 1;
const L = track.lengthM;
const clone = () => JSON.parse(JSON.stringify(JSON_TRACK));

/** A kart on the road at s, lateral lat, at speed v, nose yawDeg toward the open side. */
function kartAt(s, lat, v = 30, yawDeg = 0) {
  const st = createState(), f = track.frameAt(s), c = Math.cos(yawDeg * Math.PI / 180), sn = Math.sin(yawDeg * Math.PI / 180) * SIDE;
  const t = [0, 1, 2].map((k) => f.t[k] * c + f.b[k] * sn), b = [0, 1, 2].map((k) => f.b[k] * c - f.t[k] * sn);
  setPos(st, [f.p[0] + f.b[0] * lat, f.p[1] + f.b[1] * lat, f.p[2] + f.b[2] * lat]);
  setQuat(st, qfromBasis([0, 0, 0, 1], b, f.n, [-t[0], -t[1], -t[2]]));
  setVel(st, [t[0] * v, t[1] * v, t[2] * v]);
  st[S.engineV] = v; st[S.grounded] = 1; st[S.seg] = f.seg; st[S.latchSeg] = f.seg; st[S.s] = s; st[S.lateral] = lat; st[S.hopT] = 1e3;
  return st;
}
const fakeRace = () => ({ phase: 'racing', timeS: 0, lapStartS: 0, finish() { this.phase = 'finished'; } });

/** The cut: from ds into the opening, lat 3 toward the open side, v, nose yawDeg out; throttle and a little steer. */
function cut({ ds = 4, v = 30, yawDeg = 55, hop = false, maxS = 4, followS = 0, brakeOnLand = false } = {}) {
  const feel = makeFeel(), st = kartAt(R0.openS0 + ds, 3 * SIDE, v, yawDeg), a = createActions(), ev = [];
  const rollout = []; let landedAt = -1;
  const sink = { emit: (name, p = {}) => ev.push({ name, p, s: st[S.s] }) };
  const prog = createProgress(track, sink), race = fakeRace();
  prog.reset(st); st[S.checkpoint] = JSON_TRACK.checkpoints.filter((n) => track.nodeS[n] <= st[S.s]).length - 1;
  for (let i = 0; i < maxS / DT; i++) {
    const braking = brakeOnLand && landedAt >= 0;
    a.throttle = braking ? 0 : 1; a.brake = braking ? 1 : 0;
    a.steer = landedAt >= 0 && followS ? 0 : 0.3 * SIDE; a.hop = a.hopPressed = hop && i === 0;
    const previous = [st[S.posX], st[S.posY], st[S.posZ]];
    step(st, a, track, feel, DT, sink);
    updateRespawn(st, track, feel, DT, sink);
    race.timeS += DT; prog.update(st, race);
    if (landedAt < 0 && ev.some(e => e.name === 'route-land')) landedAt = i;
    if (landedAt >= 0 && followS) {
      const f = track.frameAt(st[S.s]);
      rollout.push({ grounded: st[S.grounded], engine: st[S.engineV],
        forward: [st[S.velX], st[S.velY], st[S.velZ]].reduce((sum, v, k) => sum + v * f.t[k], 0),
        moved: [st[S.posX], st[S.posY], st[S.posZ]].reduce((sum, p, k) => sum + (p - previous[k]) * f.t[k], 0) / DT });
    }
    if (ev.some(e => e.name === 'drop') || (followS ? landedAt >= 0 && (i - landedAt) * DT >= followS : ev.some(e => e.name === 'route-skip'))) break;
  }
  return { ev, st, rollout, names: ev.map((e) => e.name) };
}

test('shortcut momentum flows through touchdown and a full second of driving without a hop', () => {
  for (const [v, yawDeg] of [[25, 40], [25, 60], [30, 40]]) {
    const { names, rollout } = cut({ v, yawDeg, followS: 1 });
    assert.ok(names.includes('route-skip'));
    assert.ok(!names.includes('drop') && !names.includes('wall'));
    assert.equal(names.filter(n => n === 'land').length, 1, 'no secondary traction-delaying bounce');
    assert.ok(rollout.length >= 60);
    for (const r of rollout) {
      assert.equal(r.grounded, 1, 'touchdown restores grip immediately');
      assert.ok(r.forward >= v * .8, `forward ${r.forward} after ${v} m/s approach at ${yawDeg} degrees`);
      assert.ok(r.moved >= v * .75, `actual travel ${r.moved} must continue through the contact correction`);
    }
  }
});

test('shortcut rollout still obeys the brake without a forced speed floor', () => {
  const { st, rollout, names } = cut({ v: 25, yawDeg: 40, followS: 1, brakeOnLand: true });
  assert.ok(names.includes('route-land') && !names.includes('drop'));
  assert.ok(st[S.engineV] < rollout[0].engine * .5, 'braking must slow the engine');
  assert.ok(rollout.at(-1).forward < rollout[0].forward * .5, 'braking must slow actual travel');
});

test('shortcut air steering conserves surface momentum and leaves falling speed alone', () => {
  const feel = makeFeel(), f = track.frameAt(R0.pad.s);
  for (const [bank, slope] of [[0, 0], [.8, .4], [1.2, -.4]]) for (const along of [-20, 0, 20]) for (const lateral of [-16, 0, 16]) {
    const normal = f.n.map((v, k) => v - f.b[k] * bank - f.t[k] * slope);
    const length = Math.hypot(...normal);
    for (let k = 0; k < 3; k++) normal[k] /= length;
    const V = f.t.map((v, k) => v * along + f.b[k] * lateral - f.n[k] * 24);
    const energy = V.reduce((sum, v) => sum + v * v, 0);
    const down = V.reduce((sum, v, k) => sum + v * normal[k], 0);
    const q = { s: R0.pad.s, lateral: 4, normal };
    airCatch(track, q, V, feel, DT);
    assert.ok(Math.abs(V.reduce((sum, v) => sum + v * v, 0) - energy) < 1e-6);
    assert.ok(Math.abs(V.reduce((sum, v, k) => sum + v * normal[k], 0) - down) < 1e-6);
    if (along < 0 && !bank && !slope) assert.ok(V.reduce((sum, v, k) => sum + v * f.t[k], 0) < 0, 'no forced forward launch');
  }
});

test('routes: threshold carries the dare, a gap route off the outside of the first coil turn onto its last', () => {
  assert.equal(validate(JSON_TRACK).ok, true);
  assert.equal(track.routes.length, 1);
  const r = R0;
  assert.equal(r.kind, 'gap');
  const shut = r.side === 'L' ? 'railL' : 'railR';
  for (let i = r.from; i < r.to; i++) {
    const fl = JSON_TRACK.nodes[i].flags;
    assert.ok(!fl.includes('rail') && !fl.includes(shut), 'node ' + i + ' ' + fl);
  }
  const drop = JSON_TRACK.nodes[r.from].p[1] - JSON_TRACK.nodes[r.land.from].p[1];
  assert.ok(drop >= 6 && drop <= 14, 'drop ' + drop);
  assert.ok(r.openS1 - r.openS0 >= 8 && r.openS1 - r.openS0 <= 30, 'the opening ' + (r.openS1 - r.openS0).toFixed(1));
  assert.ok(r.openS0 >= r.fromS && r.openS1 <= r.toS);
  assert.ok(r.pad.s >= r.land.fromS && r.pad.s <= r.land.toS);
  assert.ok(wrap(r.land.fromS - r.toS, L) < L / 2, 'the landing lies ahead, within half a lap');
});

test('routes: the schema refuses a railed gap and a landing behind the gap', () => {
  const a = clone();
  a.nodes[a.routes[0].from].flags = ['rail'];
  assert.match(validate(a).errors.join(';'), new RegExp('must leave the ' + R0.side + ' side open'));
  const b = clone();
  const back = b.routes[0].from - 12;                  // a landing behind the open stretch
  b.routes[0].land = { from: back, to: back + 2 };
  b.routes[0].pad.node = back + 1;
  assert.match(validate(b).errors.join(';'), /ahead of the gap/);
  const c = clone();
  delete c.routes;
  assert.equal(validate(c).ok, true, 'routes are optional');
});

test('routes: a sharp cut across the opening lands on the outer turn, s jumps forward, credited, speed kept', () => {
  for (const [v, yawDeg, hop] of [[25, 40, false], [25, 30, true], [30, 40, false]]) {
    const { ev, names, st } = cut({ v, yawDeg, hop });
    const land = ev.find((e) => e.name === 'route-land');
    assert.ok(land, v + '/' + yawDeg + ': ' + names.filter((n) => /route|drop|fall|wall|edge/.test(n)).join(','));
    assert.ok(names.indexOf('route-drop') < names.indexOf('route-land'));
    assert.ok(land.p.s >= R0.land.fromS - 1 && land.p.s <= R0.land.toS + 1, 'landed at s ' + land.p.s);
    assert.ok(land.p.skipM > 120 && land.p.skipM < L / 2, 'skip ' + land.p.skipM);
    assert.ok(names.includes('route-skip'), 'progress credits the route');
    assert.ok(!names.includes('drop'));
    assert.ok(st[S.engineV] > 15, 'a gap route keeps its speed: ' + st[S.engineV].toFixed(1));
  }
});

test('routes: the whole marked opening is a kicker lip with real rails either side', () => {
  const q = (s) => { const f = track.frameAt(s); return track.query(f.p[0] + f.b[0] * 6 * SIDE, f.p[1] + f.b[1] * 6 * SIDE, f.p[2] + f.b[2] * 6 * SIDE, f.seg); };
  assert.equal(edgeAt(track, q((R0.openS0 + R0.openS1) / 2), 6 * SIDE), 'kicker');
  assert.ok(q(R0.openS0 - 3).railed);
  assert.ok(q(R0.openS1 + 1.5).railed);
  assert.equal(edgeAt(track, q(R0.openS0 - 0.5), 6 * SIDE, 0.7), 'kicker', 'a body over the opening never scrapes the rail end');
  assert.equal(edgeAt(track, q((R0.openS0 + R0.openS1) / 2), -6 * SIDE), null, 'the far side is a plain rail');
});

test('routes: an unregistered or backwards jump never credits a checkpoint', () => {
  const evs = [], sink = { emit: (n, p) => evs.push([n, p]) };
  const prog = createProgress(track, sink), race = fakeRace();
  const cp1 = track.checklines[1].s;
  const st = kartAt(cp1 - 30, 0);
  prog.reset(st); st[S.checkpoint] = 0;
  // a teleport forward across checkpoint 1, on the ground: refused
  st[S.s] = cp1 + 170; prog.update(st, race);
  assert.equal(st[S.checkpoint], 0);
  assert.deepEqual(evs.filter(([n]) => n === 'checkpoint'), []);
  // a route-sized jump that does not start at the open edge: refused
  const far = wrap(R0.fromS - 300, L);
  st[S.s] = far; prog.update(st, race);
  st[S.s] = wrap(far + 200, L); prog.update(st, race);
  assert.deepEqual(evs.filter(([n]) => n === 'route-skip'), []);
  // backwards from the landing stretch to the open edge: nothing
  const mid = (R0.land.fromS + R0.land.toS) / 2;
  assert.equal(registeredSkip(track, mid, R0.fromS + 5), null);
  assert.equal(registeredSkip(track, far, mid), null);
  assert.ok(registeredSkip(track, R0.fromS + 10, mid));
  assert.ok(STEP_MAX_M >= 20 && STEP_MAX_M < 100);
});

test('routes: a jump held in the air waits for touchdown; a rescue back never credits', () => {
  const evs = [], sink = { emit: (n, p) => evs.push([n, p]) };
  const prog = createProgress(track, sink), race = fakeRace();
  const st = kartAt(R0.fromS + 10, 6 * SIDE);
  prog.reset(st); st[S.checkpoint] = 2;
  st[S.grounded] = 0; st[S.airborne] = 1;
  st[S.s] = (R0.land.fromS + R0.land.toS) / 2; prog.update(st, race);   // the probe snapped while falling: not judged yet
  assert.deepEqual(evs.filter(([n]) => n === 'route-skip'), []);
  st[S.respawnPhase] = RESPAWN.PULL; st[S.s] = track.nodeS[R0.from]; prog.update(st, race);   // missed: rescued back
  st[S.respawnPhase] = RESPAWN.NONE; st[S.grounded] = 1;
  st[S.s] += 1; prog.update(st, race);
  assert.equal(st[S.checkpoint], 2, 'no credit for a miss');
});

test('routes: a shallow late cut now lands; a true overshoot still rescues without skip credit', () => {
  const timid = cut({ v: 20, yawDeg: 10, ds: 8, maxS: 3 });
  const why = timid.names.filter((n) => /route|drop|rescue|edge|fall|wall/.test(n)).join(',');
  assert.ok(timid.names.includes('route-land') && timid.names.includes('route-skip'), 'timid: ' + why);
  assert.ok(!timid.names.includes('drop'), 'timid: ' + why);
  const wild = cut({ v: 50, yawDeg: 80, hop: true, maxS: 6 });
  assert.ok(!wild.names.includes('route-land') && !wild.names.includes('route-skip'), 'wild: ' + wild.names.join(','));
  assert.ok(wild.names.includes('drop'), 'wild: ' + wild.names.filter((n) => /route|drop|rescue|edge|fall/.test(n)).join(','));
});

test('routes: the bots leave the dare alone, finish, and seldom fall', () => {
  let landed = 0, drops = 0, finished = 0, bots = 0;
  for (const seed of [1, 4]) {
    const R = makeRace({ seed });
    R.run(900, () => R.racers.list.every((r) => r.finished));
    landed += R.botEvents('route-land').length;
    drops += R.botEvents('drop').length;
    finished += R.racers.list.filter((r) => r.finished).length;
    bots += R.racers.list.length;
  }
  assert.equal(landed, 0, 'route landings ' + landed);
  assert.equal(finished, bots, 'every racer finishes');
  assert.ok(drops <= 8, 'drops ' + drops);
});
