/* tests/ramps-system.test.mjs - the ramp system (handoff-1030/GLASSROAD-RAMPS.md, lane R0) on a fixture loop:
 * schema, the wedge in the ground query, the launch rule (speed, direction, width, hop on the wedge), floating item
 * boxes (liftM), the bots over a ramp, the course hash and the wedge MeshData. No course JSON is touched here. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { validate } from '../track/schema.js';
import { loadTrack } from '../track/load.js';
import { rampHeight, RAMP_BACK_M, RAMP_LAUNCH_MIN_MPS } from '../track/ramps.js';
import { buildRampMeshes, RAMP_SHADE } from '../track/rampMesh.js';
import { rampAirS } from '../sim/ramps.js';
import { S, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { createItems } from '../sim/items.js';
import { lipAhead } from '../sim/trick.js';
import { onlineCourseHash } from '../net/courseHash.js';
import { floatingBob } from '../render/rampLook.js';
import { sfxPlan } from '../audio/sfxmap.js';
import { AUDIO_CUES } from '../audio/cues.js';
import { qlook } from '../core/math.js';
import { makeFeel, acts, recorder, DT } from './sim-fake.mjs';
import { makeRace } from './sim-racers-harness.mjs';

const FEEL_JSON = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const RAMP = { id: 'fx-ramp-1', node: 25, x: 0, widthM: 6, lenM: 7, heightM: 1.2, upMps: 14, purpose: 'air', note: 'fixture' };

/** A wide flat railed stadium: two 480 m straights (40 nodes of 12 m) joined by R 95 m bends; one ramp on the first straight. */
function fixture({ ramps = [RAMP], items = [] } = {}) {
  const Ls = 480, R = 95, nodes = [], arcN = 25;
  const push = (x, z) => nodes.push({ p: [x, 0, z], roll: 0, w: 16, flags: nodes.length ? ['rail'] : ['start', 'rail'], speed: 1, surface: 'glass' });
  for (let i = 0; i < 40; i++) push(i * 12, 0);
  for (let i = 0; i < arcN; i++) { const a = (i / arcN) * Math.PI; push(Ls + Math.sin(a) * R, R - Math.cos(a) * R); }
  for (let i = 0; i < 40; i++) push(Ls - i * 12, 2 * R);
  for (let i = 0; i < arcN; i++) { const a = (i / arcN) * Math.PI; push(-Math.sin(a) * R, R + Math.cos(a) * R); }
  const N = nodes.length;
  return {
    format: 'glassroad-track', version: 1, id: 'ramp-fixture', name: { en: 'Ramp fixture' }, loop: true, laps: 3, units: 'm', nodes,
    sections: [{ id: 'a', from: 0, to: 65, act: 'induction', palette: 'dawn', sky: 'spiral-slow', bandSpeed: 0.6 },
      { id: 'b', from: 65, to: N, act: 'release', palette: 'afterglow', sky: 'spiral-wide', bandSpeed: 0.8 }],
    checkpoints: [0, 40, 65, 105], spawn: { node: 0, grid: [[-3, -4], [3, -8], [-3, -12], [3, -16], [-3, -20], [3, -24], [-3, -28], [3, -32]] },
    items, boosts: [], jumps: [], hazards: [], media: [],
    ai: { lines: [], pace: [1] }, beat: { bpm: null, offsetMs: 0, map: [] },
    limits: { fallY: -40, edgeGraceM: 1.2, edgeGraceS: 0.8 },
    ...(ramps ? { ramps: ramps.map((r) => ({ ...r })) } : {}),
  };
}

const T = loadTrack(fixture());
const ramp = T.ramps[0], L = T.lengthM;

/** A grounded kart at s, lateral; the nose along (+1) or against (-1) the road, moving `dir` along it at speed. */
function kart(track, { s, lateral = 0, speed = 50, dir = 1, nose = dir, engine = speed * dir * nose }) {
  const st = createState(), f = track.surfaceAt(s, lateral), q = track.query(f.p[0], f.p[1], f.p[2], f.seg);
  const fwd = f.t.map((v) => v * nose);
  setPos(st, q.p); setQuat(st, qlook([0, 0, 0, 1], fwd, f.n)); setVel(st, f.t.map((v) => v * dir * speed));
  st[S.seg] = st[S.latchSeg] = q.seg; st[S.s] = q.s; st[S.lateral] = q.lateral;
  st[S.grounded] = 1; st[S.hopT] = 1000; st[S.engineV] = engine;
  return st;
}
function drive(track, st, input, ticks, ev = recorder(), each = null) {
  const feel = makeFeel();
  for (let i = 0; i < ticks; i++) { ev.t += DT; step(st, input, track, feel, DT, ev); input.hopPressed = false; each?.(st, i, ev); }
  return ev;
}

test('schema: ramps and liftM are accepted inside the contract and refused outside it', () => {
  const ok = fixture({ items: [{ node: 50, x: 0, kind: 'box', liftM: 3.5 }] });
  assert.deepEqual(validate(ok).errors, []);
  assert.deepEqual(validate(fixture({ ramps: null })).errors, [], 'ramps stay optional');
  const bad = (mut, re) => {
    const t = fixture({ items: [{ node: 50, x: 0, kind: 'box' }] }); mut(t);
    const r = validate(t);
    assert.equal(r.ok, false, 'should refuse: ' + re);
    assert.ok(r.errors.some((e) => re.test(e)), r.errors.join('; '));
  };
  bad((t) => { t.ramps = {}; }, /ramps must be an array/);
  bad((t) => { t.ramps[0].upMps = 20; }, /upMps must be 11\.\.16/);
  bad((t) => { t.ramps[0].widthM = 3; }, /widthM/);
  bad((t) => { t.ramps[0].lenM = 12; }, /lenM/);
  bad((t) => { t.ramps[0].heightM = 2; }, /heightM/);
  bad((t) => { t.ramps[0].purpose = 'fun'; }, /purpose/);
  bad((t) => { t.ramps[0].node = 999; }, /node out of range/);
  bad((t) => { t.ramps[0].x = 7; }, /leaves the road/);
  bad((t) => { t.ramps.push({ ...RAMP }); }, /unique/);
  bad((t) => { t.nodes[RAMP.node].flags = ['jump']; }, /jump gap/);
  bad((t) => { t.items[0].liftM = -1; }, /liftM/);
  bad((t) => { t.items[0].liftM = 'high'; }, /liftM/);
});

test('the ground query stands karts on the wedge: rising over lenM, the lip at heightM, a short back face', () => {
  assert.equal(T.ramps.length, 1);
  assert.equal(ramp.lipS, T.nodeS[RAMP.node]);
  const probe = (d, lat = 0) => { const f = T.surfaceAt(ramp.lipS - d, lat); return T.query(f.p[0], f.p[1] + 3, f.p[2], f.seg); };
  for (const d of [7, 5, 3.5, 1, 0.01, -0.8]) {
    const q = probe(d), want = rampHeight(ramp, ramp.lipS - d, 0, L);
    assert.ok(Math.abs(q.height - (3 - want)) < 1e-3, `d ${d}: height ${q.height} want ${3 - want}`);
    assert.equal(q.ramp ? q.ramp.id : null, want > 0 ? RAMP.id : null);
  }
  assert.ok(Math.abs(rampHeight(ramp, ramp.lipS - 3.5, 0, L) - 0.6) < 1e-9, 'half way up: half height');
  assert.equal(rampHeight(ramp, ramp.lipS + RAMP_BACK_M + 0.1, 0, L), 0);
  assert.equal(probe(3, RAMP.widthM / 2 + 0.2).ramp ?? null, null, 'outside the width: bare road');
  assert.ok(Math.abs(probe(3, 4).height - 3) < 1e-3);
  // no ramps: the query is the bare base query (no ramp fields at all)
  const bare = loadTrack(fixture({ ramps: null }));
  assert.equal(bare.ramps.length, 0);
  const f = bare.surfaceAt(ramp.lipS - 3, 0), q = bare.query(f.p[0], f.p[1] + 3, f.p[2], f.seg);
  assert.ok(Math.abs(q.height - 3) < 1e-3 && q.ramp === undefined);
});

for (const speed of [40, 50, 60, 66.7]) {
  test(`launch at ${speed} m/s: leaves the lip with velY = upMps, keeps its speed, ~2*up/g of air`, () => {
    const st = kart(T, { s: ramp.lipS - 40, speed });
    let launch = null, peak = -Infinity, rode = 0, before = 0;
    const ev = drive(T, st, acts({ throttle: 1 }), 150, recorder(), (s, i, e) => {
      const q = T.query(s[S.posX], s[S.posY], s[S.posZ], s[S.seg]);
      if (s[S.grounded] && q.ramp) rode = Math.max(rode, s[S.posY]);
      if (!launch && !e.first('ramp')) before = Math.hypot(s[S.velX], s[S.velZ]);
      if (!launch && e.first('ramp')) launch = { t: e.t, vy: s[S.velY], h: Math.hypot(s[S.velX], s[S.velZ]), y: s[S.posY] };
      peak = Math.max(peak, s[S.posY]);
    });
    const r = ev.first('ramp');
    assert.ok(r, 'launched');
    assert.equal(r.payload.id, RAMP.id);
    assert.equal(r.payload.purpose, 'air');
    assert.ok(rode > 0.9, 'the kart rode up the wedge before the lip: ' + rode);
    assert.ok(ev.all('airborne').some((e) => e.payload.cause === 'lip'), 'a lip take-off');
    assert.ok(launch.vy > RAMP.upMps - 0.6 && launch.vy <= RAMP.upMps + 0.01, 'velY ' + launch.vy);
    assert.ok(launch.h > before * 0.98, 'horizontal speed kept: ' + launch.h + ' from ' + before);
    const land = ev.list.find((e) => e.name === 'land' && e.t > launch.t);
    assert.ok(land, 'landed');
    const g = FEEL_JSON.hop.gravityMps2, H = RAMP.heightM;
    const want = (RAMP.upMps + Math.sqrt(RAMP.upMps ** 2 + 2 * g * H)) / g;   // from the lip height down to the road
    const air = land.t - launch.t;
    assert.ok(Math.abs(air - want) < 0.07, `air ${air.toFixed(3)} s, ballistic ${want.toFixed(3)} s`);
    assert.ok(Math.abs(rampAirS(RAMP.upMps, g) - 1) < 1e-9, 'upMps 14 -> 1.0 s flat');
    assert.ok(peak - launch.y > RAMP.upMps ** 2 / (2 * g) - 0.4, 'apex about upMps^2 / 2g over the lip');
    assert.equal(ev.all('drop').length, 0);
    assert.equal(st[S.respawnPhase], 0);
  });
}

test('slow crossing (under 8 m/s) drives over the wedge and its back face: no launch, no fall', () => {
  const st = kart(T, { s: ramp.lipS - 9, speed: 6 });
  let maxH = 0;
  const ev = drive(T, st, acts({ throttle: 0 }), 240, recorder(), (s) => {
    s[S.engineV] = 6;                              // cruise control: hold 6 m/s up the slope
    const q = T.query(s[S.posX], s[S.posY], s[S.posZ], s[S.seg]);
    if (!s[S.grounded]) maxH = Math.max(maxH, q.height);
  });
  assert.equal(ev.all('ramp').length, 0);
  assert.ok(maxH < 0.4, 'never more than a bump in the air: ' + maxH);
  assert.ok(st[S.s] > ramp.lipS + 1, 'drove past the lip: ' + (st[S.s] - ramp.lipS));
  assert.equal(st[S.respawnPhase], 0);
  assert.ok(RAMP_LAUNCH_MIN_MPS === 8);
});

test('reverse over the ramp (back face first, then down the slope): no launch, no fall', () => {
  const st = kart(T, { s: ramp.lipS + 4, speed: 7, dir: -1, nose: 1, engine: -7 });
  const ev = drive(T, st, acts({ brake: 1 }), 300);
  assert.equal(ev.all('ramp').length, 0);
  assert.ok(st[S.s] < ramp.lipS - RAMP.lenM, 'reversed off the foot: ' + (ramp.lipS - st[S.s]));
  assert.equal(st[S.respawnPhase], 0);
  assert.equal(ev.all('drop').length, 0);
});

test('fast but driven backwards along the course over the lip area never launches', () => {
  const st = kart(T, { s: ramp.lipS + 10, speed: 30, dir: -1, engine: 30 });   // nose and velocity point back down the course
  const ev = drive(T, st, acts({ throttle: 1 }), 90);
  assert.equal(ev.all('ramp').length, 0);
  assert.equal(st[S.respawnPhase], 0);
});

test('lateral bounds: a fast kart beside the wedge is not launched; inside the edge it is', () => {
  const beside = drive(T, kart(T, { s: ramp.lipS - 30, lateral: RAMP.widthM / 2 + 0.6, speed: 50 }), acts({ throttle: 1 }), 60);
  assert.equal(beside.all('ramp').length, 0);
  const edge = drive(T, kart(T, { s: ramp.lipS - 30, lateral: RAMP.widthM / 2 - 0.3, speed: 50 }), acts({ throttle: 1 }), 60);
  assert.equal(edge.all('ramp').length, 1);
});

test('stepping onto the wedge from the side stands the kart on it (no fall through the wedge)', () => {
  const st = kart(T, { s: ramp.lipS - 3, lateral: RAMP.widthM / 2 + 1.5, speed: 3 });
  // a sideways shove onto the raised part of the wedge
  const f = T.frameAt(ramp.lipS - 3);
  setVel(st, f.t.map((v, k) => v * 3 - f.b[k] * 6));
  const ev = drive(T, st, acts({}), 60);
  assert.equal(st[S.respawnPhase], 0);
  assert.equal(ev.all('drop').length, 0);
  const q = T.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg]);
  assert.ok(q.height > -0.05, 'not inside the wedge or road: ' + q.height);
});

test('a hop on the wedge just before the lip is an early trick and still takes the launch', () => {
  const st = kart(T, { s: ramp.lipS - 3, speed: 50 });
  const ev = drive(T, st, acts({ throttle: 1, hopPressed: true }), 120);
  assert.ok(ev.first('hop'), 'hopped');
  assert.ok(ev.first('ramp'), 'still launched at the lip');
  assert.ok(ev.first('trick'), 'the early window armed a trick');
  assert.ok(ev.first('trick-land'), 'and paid it on landing');
  const lip = lipAhead(T, ramp.lipS - 2, 0, 5, makeFeel());
  assert.equal(lip?.at, 'lip');
  assert.equal(lipAhead(T, ramp.lipS - 2, RAMP.widthM, 5, makeFeel()), null, 'off the wedge line: no lip');
});

test('liftM: a floating box is taken only by a kart flying through it; unlifted boxes unchanged', () => {
  const liftS = 18;   // metres past the lip, high in the arc at 50 m/s (apex 3.5 m at ~0.5 s = 25 m)
  const node = RAMP.node + 1, ds = liftS - T.nodeS[node] + ramp.lipS;
  const json = fixture({ items: [{ node, ds, x: 0, kind: 'box', liftM: 3.2 }, { node: RAMP.node + 8, x: 0, kind: 'box' }] });
  assert.deepEqual(validate(json).errors, []);
  const track = loadTrack(json);
  assert.equal(track.items[0].liftM, 3.2);
  assert.equal(track.items[1].liftM, undefined);
  const run = (opts) => {
    const feel = makeFeel(), ev = recorder(), items = createItems(track, feel, ev, { seed: 3 });
    const r = { i: 0, st: kart(track, opts), input: acts({ throttle: 1, item: false, back: false }), item: { id: null, rolling: false, rollT: 0, heldT: 0 },
      mods: { steerMul: 1, steerAdd: 0, throttleMul: 1, gripMul: 1, slideMul: 1, capMul: 1, shoveMul: 1, blind: 0 },
      fxv: { spinRad: 0, shield: null, echo: 0, effect: null, ghost: false, slip: 0, place: 1 }, finished: false };
    const std = { order: [0], placeOf: () => 1, gapToLeader: () => 0, progressOf: () => 0 };
    const v = items.view();
    assert.ok(v.boxes[0].lift === 3.2 && !v.boxes[1].lift, 'the view carries the lift for render');
    const fr = track.frameAt(track.items[0].s);
    assert.ok(Math.abs((v.boxes[0].p[1] - fr.p[1]) - 3.2) < 1e-6, 'the box point floats liftM over the road');
    const taken = [];
    for (let i = 0; i < 160; i++) {
      ev.t += DT; step(r.st, r.input, track, feel, DT, ev);
      items.update(DT, [r], std, { phase: 'racing' });
      for (const e of ev.all('item-box')) if (!taken.includes(e.payload.box)) taken.push(e.payload.box);
      if (r.item.id && !r.item.rolling) { r.item.id = null; }
      if (r.item.rolling) { r.item.rolling = false; r.item.id = null; }
    }
    return taken;
  };
  // over the ramp at 50 m/s: through the floating box, then the ordinary box on the road
  assert.deepEqual(run({ s: ramp.lipS - 30, speed: 50 }), [0, 1]);
  // beside the ramp (no launch) on the same s: drives under the floating box, still takes the road box
  assert.deepEqual(run({ s: ramp.lipS + 2, speed: 40 }), [1], 'grounded under it: only the road box');
});

test('render math: floating bob holds still under Gentle; a ramp has a soft launch cue on both audio paths', () => {
  assert.equal(floatingBob(1.3, 2, true), 0);
  assert.ok(Math.abs(floatingBob(0.55, 0)) > 0.05 && Math.abs(floatingBob(0.55, 0)) <= 0.22 + 1e-9);
  const plan = sfxPlan('ramp', { id: RAMP.id });
  assert.ok(Array.isArray(plan) && plan.length && plan.every((x) => ['hop', 'pad'].includes(x.id) && x.vol <= 0.6), 'existing bank, soft');
  assert.equal(typeof AUDIO_CUES.ramp, 'function');
});

test('wedge MeshData follows the exact ridden height, one merged draw per layer', () => {
  // a second ramp on the bend (R 95 m) proves the same on a curve
  const TB = loadTrack(fixture({ ramps: [RAMP, { ...RAMP, id: 'fx-ramp-bend', node: 52, x: 2 }] }));
  const m = buildRampMeshes(TB);
  for (const k of ['body', 'chevrons', 'lip']) assert.ok(m[k].indices.length > 0 && m[k].indices.length % 3 === 0, k);
  assert.equal(m.body.shade.length, m.body.positions.length / 3);
  // every lit top vertex sits on the query's wedge surface
  let tops = 0;
  for (let i = 0; i < m.body.shade.length; i++) {
    if (m.body.shade[i] !== RAMP_SHADE.top) continue;
    const p = [m.body.positions[i * 3], m.body.positions[i * 3 + 1], m.body.positions[i * 3 + 2]];
    const q = TB.query(p[0], p[1] + 0.001, p[2]);
    assert.ok(Math.abs(q.height - 0.001) < 2e-3, 'top vertex off the ridden surface: ' + q.height);
    tops++;
  }
  assert.ok(tops >= 2 * 2 * (Math.ceil(RAMP.lenM) + 1) * 2 - 8, 'a row per metre: ' + tops);
  // the chevrons use the pad uv convention: u across the ramp centre, v from the foot to the lip
  const us = [], vs = [];
  for (let i = 0; i < m.chevrons.uvs.length; i += 2) { us.push(m.chevrons.uvs[i]); vs.push(m.chevrons.uvs[i + 1]); }
  assert.equal(Math.min(...us), -RAMP.widthM / 2); assert.equal(Math.max(...us), RAMP.widthM / 2);
  assert.ok(Math.abs(Math.min(...vs)) < 1e-6 && Math.abs(Math.max(...vs) - RAMP.lenM) < 1e-6);
  const bare = buildRampMeshes(loadTrack(fixture({ ramps: null })));
  assert.equal(bare.body.indices.length, 0);
});

test('course hash: ramps, their numbers and lifted boxes all change it', async () => {
  const feel = FEEL_JSON;
  const withRamp = fixture(), without = fixture({ ramps: null });
  const a = await onlineCourseHash(withRamp, feel, loadTrack(withRamp, { moving: true }));
  const b = await onlineCourseHash(without, feel, loadTrack(without, { moving: true }));
  assert.notEqual(a, b);
  const moved = fixture({ ramps: [{ ...RAMP, upMps: 15 }] });
  assert.notEqual(await onlineCourseHash(moved, feel, loadTrack(moved, { moving: true })), a);
  const lifted = fixture({ items: [{ node: 50, x: 0, kind: 'box', liftM: 3 }] }), flat = fixture({ items: [{ node: 50, x: 0, kind: 'box' }] });
  assert.notEqual(await onlineCourseHash(lifted, feel, loadTrack(lifted, { moving: true })), await onlineCourseHash(flat, feel, loadTrack(flat, { moving: true })));
});

test('bots race over a ramp lane: they launch, land and never fall (deterministic)', () => {
  const wide = fixture({ ramps: [{ ...RAMP, widthM: 8 }] });
  const runOnce = () => {
    const race = makeRace({ seed: 5, count: 6, track: loadTrack(wide) });
    race.run(70);
    const launches = race.events('ramp').length + race.botEvents('ramp').length;
    const drops = race.events('drop').length + race.botEvents('drop').length;
    const laps = race.racers.list.map((r) => r.lapTimes.length);
    return { launches, drops, laps, pos: race.racers.list.map((r) => r.st[S.s].toFixed(3)).join(',') };
  };
  const one = runOnce();
  assert.ok(one.launches >= 3, 'bots take the ramp: ' + one.launches);
  assert.equal(one.drops, 0, 'no bot falls over a ramp');
  assert.ok(one.laps.every((n) => n >= 1), 'everyone keeps lapping: ' + one.laps);
  assert.ok(race0BotPayload(), 'bot ramp events carry the racer');
  assert.deepEqual(runOnce(), one, 'the same seed runs the same race');
});

function race0BotPayload() {
  const race = makeRace({ seed: 5, count: 3, track: loadTrack(fixture({ ramps: [{ ...RAMP, widthM: 8 }] })) });
  race.run(40);
  const e = race.botEvents('ramp')[0] || null;
  const p = race.events('ramp')[0] || null;
  return (!e || (e.payload.p.racer === e.payload.racer && e.payload.p.id === RAMP.id)) && (!p || p.payload.racer === 0) && (e || p);
}
