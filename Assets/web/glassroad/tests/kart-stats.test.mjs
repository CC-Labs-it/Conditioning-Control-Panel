// Kart bodies (owner 2026-10-31): sim/kartStats.js, the feel.json table and every multiplier's seat in the sim.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { BAR_LEVELS, kartStats, statBars, bodyOfVehicle, isKartBody, maxTopMul, STAT_KEYS, TYPE_LABELS, NEUTRAL_KART } from '../sim/kartStats.js';
import { KARTS, raceLook, DEFAULT } from '../hud/garageState.js';
import { createRacers, createFeelOverlay } from '../sim/racers.js';
import { createContact } from '../sim/contact.js';
import { gripYaw } from '../sim/steer.js';
import { S, getQuat, getVel, setVel } from '../sim/state.js';
import { q4, qrotate, v3dot } from '../core/math.js';
import { makeFeel, fakeTrack, place, run, recorder, acts, DT } from './sim-fake.mjs';

const RAW = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const feel = makeFeel();
const IDS = Object.keys(RAW.karts.bodies);
/** A feel overlay driving one body, the way a racer's does. */
const on = (id, f = feel) => { const o = createFeelOverlay(f); o.kart = kartStats(f, id); return o; };

test('the table: one row per garage kart, in the fixed shape, Lovebomb is the default and all ones', () => {
  assert.deepEqual([...IDS].sort(), KARTS.map(([id]) => id).sort());
  assert.equal(RAW.karts.default, 'lovebomb');
  const { type, ...mults } = RAW.karts.bodies.lovebomb;
  assert.equal(type, 'allround');
  assert.deepEqual(Object.values(mults), [1, 1, 1, 1, 1, 1]);
  for (const id of IDS) {
    assert.deepEqual(Object.keys(RAW.karts.bodies[id]), Object.keys(NEUTRAL_KART), id);
    assert.ok(TYPE_LABELS[RAW.karts.bodies[id].type], id + ' type');
  }
});

test('the table: top speed stays within +3% / -2.5%, and maxTopMul is the highest row', () => {
  for (const id of IDS) {
    const t = RAW.karts.bodies[id].topMul;
    assert.ok(t <= 1.03 && t >= 0.975, id + ' topMul ' + t);
  }
  assert.equal(RAW.karts.maxTopMul, Math.max(...IDS.map((id) => RAW.karts.bodies[id].topMul)));
  assert.equal(maxTopMul(feel), RAW.karts.maxTopMul);
  assert.equal(maxTopMul(null), 1);
});

test('the table: trade-offs only, no kart beats another on every stat', () => {
  // higher is better for top, accel, drift; lower is better for grip (looser), wall and bump
  const better = (a, b) => [a.topMul - b.topMul, a.accelMul - b.accelMul, b.gripMul - a.gripMul, a.driftMul - b.driftMul, b.wallMul - a.wallMul, b.bumpMul - a.bumpMul];
  for (const x of IDS) for (const y of IDS) {
    if (x === y) continue;
    const d = better(RAW.karts.bodies[x], RAW.karts.bodies[y]);
    assert.ok(d.some((v) => v < 0), x + ' beats ' + y + ' on every stat');
  }
});

test('kartStats: the row, the default row when unknown, never throws', () => {
  assert.equal(kartStats(feel, 'undertow').type, 'fast');
  assert.equal(kartStats(RAW, 'undertow').topMul, RAW.karts.bodies.undertow.topMul);
  for (const id of ['nope', null, undefined, 7, 'constructor', '__proto__']) assert.deepEqual(kartStats(feel, id), RAW.karts.bodies.lovebomb, String(id));
  for (const f of [null, undefined, {}, { get() { throw new Error('no key'); } }]) assert.deepEqual(kartStats(f, 'undertow'), NEUTRAL_KART);
  assert.equal(isKartBody(feel, 'petal'), true);
  assert.equal(isKartBody(feel, 'constructor'), false);
});

test('statBars: integers 1..7, Lovebomb 4 4 4 4, unknown reads as Lovebomb, the bars tell the types apart', () => {
  assert.deepEqual(STAT_KEYS, ['speed', 'accel', 'handling', 'stability']);
  assert.deepEqual(statBars(feel, 'lovebomb'), { speed: 4, accel: 4, handling: 4, stability: 4, type: 'allround' });
  assert.deepEqual(statBars(feel, 'nope'), statBars(feel, 'lovebomb'));
  for (const id of IDS) {
    const b = statBars(feel, id);
    for (const k of STAT_KEYS) assert.ok(Number.isInteger(b[k]) && b[k] >= 1 && b[k] <= BAR_LEVELS && !Object.is(b[k], -0), id + ' ' + k);
  }
  const { speed: us, accel: ua, handling: uh, stability: ub } = statBars(feel, 'undertow');
  assert.ok(us > 4 && ua < 4 && uh < 4 && ub < 4, 'fast: speed for everything else');
  const pe = statBars(feel, 'petal');
  assert.ok(pe.speed < 4 && pe.accel > 4 && pe.handling > 4 && pe.stability > 4, 'easy: the reverse');
  assert.ok(statBars(feel, 'checkmate').stability > 4 && statBars(feel, 'checkmate').accel < 4, 'heavy');
});

test('no kart reads as beating another on every bar: the shop sells trade-offs, not upgrades', () => {
  for (const a of IDS) for (const b of IDS) {
    if (a === b) continue;
    const x = statBars(feel, a), y = statBars(feel, b);
    assert.ok(STAT_KEYS.some((k) => x[k] < y[k]), a + ' reads as at least as good as ' + b + ' on every bar');
  }
});

test('the vehicle a look names is the body; anything else reads as none', () => {
  assert.equal(bodyOfVehicle('emi-vehicle-house-edge'), 'house-edge');
  assert.equal(bodyOfVehicle(raceLook(DEFAULT).vehicle), DEFAULT.kart);
  for (const v of ['emi-teacup-kart', null, undefined, 3]) assert.equal(bodyOfVehicle(v), null);
});

test('racers: a person takes the seat look, a bot and an old vehicle drive the default, solo by setBody, a restart keeps it', () => {
  const track = fakeTrack(), q = { emit() {} }, look = (id) => ({ outfit: null, vehicle: id });
  const net = [{ seat: 0, kind: 'human', name: 'a', local: true, look: look('emi-vehicle-undertow') },
    { seat: 1, kind: 'human', name: 'b', look: look('emi-vehicle-checkmate') },
    { seat: 2, kind: 'bot', name: 'c', local: true, look: look('emi-vehicle-undertow') },
    { seat: 3, kind: 'human', name: 'd', look: look('emi-teacup-kart') }];
  const on = createRacers(track, feel, q, { net, seed: 1 }).list.map((r) => r.feel.kart);
  assert.deepEqual(on.map((k) => k.topMul), [RAW.karts.bodies.undertow.topMul, RAW.karts.bodies.checkmate.topMul, 1, 1]);
  const solo = createRacers(track, feel, q, { count: 2, seed: 1 });
  assert.equal(solo.player.feel.kart.topMul, 1);
  solo.setBody(0, 'petal');
  solo.place();
  assert.equal(solo.player.feel.kart.type, 'easy');
  assert.equal(solo.list[1].feel.kart.topMul, 1);
  solo.setBody(0, 'gone');
  assert.equal(solo.player.feel.kart.type, 'allround');
});

test('lovebomb is exactly the plain kart: the same state, tick for tick', () => {
  const tr = fakeTrack(), go = acts({ throttle: 1, steer: 0.4 });
  const a = place(tr), b = place(tr);
  const script = (st, f) => {
    run(st, tr, f, go, 6, recorder());
    run(st, tr, f, acts({ throttle: 1, hop: true, steer: 0.8 }), 3, recorder());
    run(st, tr, f, go, 2, recorder());
  };
  script(a, feel); script(b, on('lovebomb'));
  assert.deepEqual([...a], [...b]);
});

test('topMul: the engine settles at the body cap, while speed.topMps and speedPct keep the class number', () => {
  const tr = fakeTrack(), top = feel.get('speed.topMps'), settle = (id) => {
    const f = on(id), st = place(tr);
    run(st, tr, f, acts({ throttle: 1 }), 12, recorder());
    assert.equal(f.get('speed.topMps'), top);
    return st;
  };
  const near = (a, b, m) => assert.ok(Math.abs(a - b) < 1e-3 * top, m + ' ' + a + ' vs ' + b);
  near(settle('lovebomb')[S.engineV], top, 'lovebomb');
  const un = settle('undertow'), pe = settle('petal');
  near(un[S.engineV], top * RAW.karts.bodies.undertow.topMul, 'undertow');
  near(pe[S.engineV], top * RAW.karts.bodies.petal.topMul, 'petal');
  near(un[S.speedPct], RAW.karts.bodies.undertow.topMul, 'speedPct against the class top');
});

test('accelMul: a quicker body reaches a set speed sooner, by the gain', () => {
  const tr = fakeTrack(), mark = feel.get('speed.topMps') * 0.5, t = (id) => {
    const st = place(tr);
    let n = 0;
    run(st, tr, on(id), acts({ throttle: 1 }), 20, recorder(), (s) => { n++; return s[S.engineV] >= mark; });
    return n * DT;
  };
  const base = t('lovebomb');
  assert.ok(t('petal') < base * 0.9, 'petal quicker');
  assert.ok(t('checkmate') > base * 1.1, 'checkmate slower');
});

test('gripMul: a looser body keeps more of a sideways slide after a tick', () => {
  const tr = fakeTrack({ nodes: 40000 }), side = (id) => {
    const st = place(tr, { speed: 20, engine: 20, headingDeg: 10 });   // the nose 10 deg off a velocity along the road
    const t = tr.frameAt(100, { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] }).t;
    setVel(st, [t[0] * 20, t[1] * 20, t[2] * 20]);
    run(st, tr, on(id), acts({ throttle: 1 }), DT, recorder());
    const nose = qrotate([0, 0, 0], getQuat(q4(), st), [0, 0, -1]), v = getVel([0, 0, 0], st), along = v3dot(v, nose);
    return Math.hypot(v[0] - nose[0] * along, v[1] - nose[1] * along, v[2] - nose[2] * along);
  };
  const base = side('lovebomb');
  assert.ok(base > 0);
  assert.ok(side('undertow') > base, 'undertow looser');
  assert.ok(side('petal') < base, 'petal tighter');
});

test('driftMul: tiers charge sooner by the mul, the drift turns by half of it', () => {
  const tr = fakeTrack(), first = (id) => {
    const st = place(tr, { speed: 30, engine: 30 }), rec = recorder();
    run(st, tr, on(id), acts({ throttle: 1, hop: true, hopPressed: true, steer: 1 }), 0.1, rec);
    run(st, tr, on(id), acts({ throttle: 1, hop: true, steer: 1 }), 3, rec, () => !!rec.first('drift-tier'));
    return rec.first('drift-tier').t;
  };
  const base = first('lovebomb'), quick = first('feedback');
  assert.ok(quick < base * 0.95, 'feedback charges sooner ' + quick + ' vs ' + base);
  assert.ok(first('checkmate') > base * 1.03, 'checkmate charges later');
  const yawRate = (id) => {
    const st = place(tr, { speed: 30, engine: 30 });
    run(st, tr, on(id), acts({ throttle: 1, hop: true, hopPressed: true, steer: 1 }), 0.1, recorder());
    run(st, tr, on(id), acts({ throttle: 1, hop: true, steer: 1 }), 0.5, recorder());
    return Math.abs(st[S.yawRate]);
  };
  const y0 = yawRate('lovebomb'), yf = yawRate('feedback');
  assert.ok(Math.abs(yf / y0 - (1 + (RAW.karts.bodies.feedback.driftMul - 1) / 2)) < 0.02, 'yaw ratio ' + yf / y0);
});

test('wallMul: a rail takes the body share of the engine', () => {
  const tr = fakeTrack(), hit = (id) => {
    const st = place(tr, { lateral: 5, headingDeg: 20, speed: 30, engine: 30 }), rec = recorder();
    let pre = 0;
    run(st, tr, on(id), acts({ throttle: 1 }), 1, rec, (s) => { if (rec.first('wall')) return true; pre = s[S.engineV]; return false; });
    return 1 - st[S.engineV] / pre;
  };
  const base = hit('lovebomb');
  assert.ok(hit('petal') < base * 0.8, 'petal shrugs it off');
  assert.ok(hit('undertow') > base * 1.2, 'undertow pays');
});

test('bumpMul: a body takes its share of a push, the other kart its own', () => {
  const tr = fakeTrack(), push = (idA, idB) => {
    const racers = createRacers(tr, feel, { emit() {} }, { count: 2, seed: 1 });
    racers.setBody(0, idA); racers.setBody(1, idB);
    const [a, b] = racers.list;
    const f = tr.frameAt(100), put = (r, x) => {
      r.st[S.posX] = f.p[0] + f.b[0] * x; r.st[S.posY] = f.p[1] + f.b[1] * x; r.st[S.posZ] = f.p[2] + f.b[2] * x;
      r.st[S.s] = 100; r.st[S.lateral] = x; r.st[S.grounded] = 1; r.st[S.seg] = Math.floor(100 / 10);
    };
    put(a, -0.2); put(b, 0.2);
    createContact(tr, feel, { emit() {} }).update(DT, racers.list, { phase: 'racing', timeS: 10 });
    const side = (r) => r.st[S.velX] * f.b[0] + r.st[S.velY] * f.b[1] + r.st[S.velZ] * f.b[2];
    return [Math.abs(side(a)), Math.abs(side(b))];
  };
  const [a0, b0] = push('lovebomb', 'lovebomb');
  assert.ok(a0 > 0 && Math.abs(a0 - b0) < 1e-3);
  const [a1, b1] = push('checkmate', 'undertow');
  assert.ok(Math.abs(a1 / a0 - RAW.karts.bodies.checkmate.bumpMul) < 1e-3, 'checkmate ' + a1 / a0);
  assert.ok(Math.abs(b1 / b0 - RAW.karts.bodies.undertow.bumpMul) < 1e-3, 'undertow ' + b1 / b0);
});

test('steering at speed is the same on every body: no body touches steer or assist', () => {
  for (const id of IDS) assert.equal(gripYaw(feel.get('speed.topMps'), on(id)), gripYaw(feel.get('speed.topMps'), feel), id);
  for (const file of ['../sim/steer.js', '../sim/assist.js']) {
    assert.ok(!/kartStats|\.kart\b/.test(readFileSync(new URL(file, import.meta.url), 'utf8')), file);
  }
});
