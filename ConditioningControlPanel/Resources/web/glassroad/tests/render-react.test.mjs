import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createState, S, BOOST, RESPAWN } from '../sim/state.js';
import {
  REACT_DEFAULTS as R, reactNumbers, createReact, springStep, hopSquashAt, landSquash, twistAt, tiltAway, reactionsFor,
  observeKart, reactTo, stepReact, pickFace, blinkNow, moodFace,
} from '../render/kartPose.js';
import { FACE_IDS, faceCell, drawFace } from '../render/faces.js';

const DT = 1 / 60;
const run = (r, st, s, opts = {}) => { let peak = 0, low = 0; for (let t = 0; t < s; t += DT) { const o = stepReact(r, st, DT, R, opts); peak = Math.max(peak, o.sq); low = Math.min(low, o.sq); } return { peak, low, o: r.out }; };
const moving = () => { const st = createState(); st[S.speedPct] = 0.8; st[S.grounded] = 1; return st; };

test('react: hop curve squashes first, stretches on the rise, then rests', () => {
  assert.ok(hopSquashAt(R.hopSquashS / 2, R) > 0.09);
  assert.ok(hopSquashAt(R.hopSquashS + R.hopStretchS / 2, R) < -0.12);
  assert.equal(hopSquashAt(R.hopSquashS + R.hopStretchS + 0.01, R), 0);
  assert.equal(hopSquashAt(-1, R), 0);
});

test('react: landing squash grows with the fall and caps; the spring settles with one overshoot', () => {
  assert.ok(landSquash(10, R) > landSquash(4, R));
  assert.equal(landSquash(1000, R), R.landSqMax);
  assert.equal(landSquash(-3, R), 0);
  const s = { x: 1, v: 0 };
  let low = 1;
  for (let t = 0; t < 3; t += DT) low = Math.min(low, springStep(s, 0, DT, 3, 0.3));
  assert.ok(low < -0.1, 'overshoots into a stretch');
  assert.ok(Math.abs(s.x) < 0.01);
  assert.equal(springStep({ x: 0.5, v: 0 }, 0, 0, 3, 0.3), 0.5);
});

test('react: twists go out and back, a trick turns once; tilts lean away from the hit', () => {
  assert.equal(twistAt(0, 0.5, false), 0); assert.equal(twistAt(1, 0.5, false), 0);
  assert.ok(Math.abs(twistAt(0.5, 0.5, false) - 0.5) < 1e-9);
  assert.ok(twistAt(0.999, Math.PI * 2, true) > 6.2);
  const right = tiltAway(1, 0, 5, R), front = tiltAway(0, -1, 5, R);
  assert.ok(right.roll > 0 && Math.abs(right.pitch) < 1e-9, 'a hit on the right lifts the right side');
  assert.ok(front.pitch > 0, 'a hit on the nose leans it back');
  assert.ok(Math.abs(tiltAway(1, 0, 999, R).roll - R.bumpTiltMaxRad) < 1e-9);
});

test('react: bus events pick their karts; state-borne events are not doubled', () => {
  const b = reactionsFor('bump', { a: 2, b: 5, dv: 4, p: [1, 2, 3] });
  assert.deepEqual(b.map((x) => [x[0], x[1]]), [[2, 'bump'], [5, 'bump']]);
  assert.notEqual(b[0][2], b[1][2], 'each kart gets its own args');
  assert.deepEqual(reactionsFor('racer-event', { racer: 3, name: 'wall', p: { dv: 5, side: -1 } }), [[3, 'wall', { dv: 5, side: -1 }]]);
  assert.deepEqual(reactionsFor('racer-event', { racer: 3, name: 'land', p: { tier: 2 } }), []);
  assert.deepEqual(reactionsFor('land', { tier: 3 }), []);
  assert.deepEqual(reactionsFor('countdown', { n: 2 }), [[-1, 'countdown', { n: 2 }]]);
  assert.deepEqual(reactionsFor('item-hit', { victim: 1, verdict: 'hit', kind: 'freeze' }), []);
  assert.deepEqual(reactionsFor('item-hit', { victim: 1, verdict: 'glass' }), [[1, 'saved', { verdict: 'glass' }]]);
  const rr = reactionsFor('race-result', { rows: [{ i: 0, place: 2 }, { i: 4, place: 1 }] });
  assert.deepEqual(rr[1], [4, 'finish', { place: 1, count: 2 }]);
  assert.deepEqual(reactionsFor('nope', {}), []);
  assert.deepEqual(reactionsFor('bump', null).length, 2);
});

test('react: state edges give hop, land with its fall, boost, places and effects; a rescue lands quietly', () => {
  const r = createReact(1), st = moving(), fxv = { place: 4, effect: null, shield: null };
  assert.deepEqual(observeKart(r, st, fxv), []);
  st[S.airborne] = 1; st[S.hopT] = 0; st[S.velY] = 4;
  assert.deepEqual(observeKart(r, st, fxv).map((e) => e[0]), ['hop']);
  st[S.velY] = -7; observeKart(r, st, fxv);
  st[S.airborne] = 0; st[S.boostKind] = BOOST.DRIFT2; fxv.place = 3; fxv.effect = 'melt';
  const ev = observeKart(r, st, fxv);
  assert.deepEqual(ev.map((e) => e[0]), ['land', 'boost', 'overtake', 'effect']);
  assert.equal(ev[0][1].fall, 7);
  fxv.place = 5;
  assert.equal(observeKart(r, st, fxv)[0][0], 'overtaken');
  st[S.airborne] = 1; st[S.hopT] = 5; observeKart(r, st, fxv);
  st[S.airborne] = 0; st[S.respawnPhase] = RESPAWN.PULL;
  assert.deepEqual(observeKart(r, st, fxv), []);
});

test('react: a hop squashes then stretches and twists; a landing squashes by the fall and settles', () => {
  const r = createReact(0), st = moving();
  reactTo(r, 'hop', {}, R);
  st[S.airborne] = 1; st[S.airTime] = 0.1;
  let peak = 0, low = 0, twist = 0;
  for (let t = 0; t < 0.4; t += DT) { const o = stepReact(r, st, DT, R); peak = Math.max(peak, o.sq); low = Math.min(low, o.sq); twist = Math.max(twist, Math.abs(o.yaw)); }
  assert.ok(peak > 0.05 && low < -0.08 && twist > 0.3);
  st[S.airborne] = 0;
  reactTo(r, 'land', { fall: 10 }, R);
  const a = run(r, st, 0.1).peak;
  const small = createReact(0); reactTo(small, 'land', { fall: 3 }, R);
  assert.ok(a > run(small, st, 0.1).peak);
  assert.ok(Math.abs(run(r, st, 3).o.sq) < 0.01);
});

test('react: a long jump earns one full saucer turn that ends square', () => {
  const r = createReact(0), st = moving();
  reactTo(r, 'airborne', {}, R);
  st[S.airborne] = 1; st[S.airTime] = R.trickAirS + 0.1;
  let most = 0;
  for (let t = 0; t < R.trickSpinS * 0.95; t += DT) most = Math.max(most, Math.abs(stepReact(r, st, DT, R).yaw));
  assert.ok(most > 5.5);
  run(r, st, 0.2);
  assert.equal(r.out.yaw, 0);
});

test('react: a hard bump tilts away, knocks the head, goes dizzy with stars; Gentle halves the shake', () => {
  const peakRoll = (gentle) => {
    const r = createReact(0), st = moving();
    reactTo(r, 'bump', { dv: 4, lx: 1, lz: 0 }, R, gentle);
    let roll = 0, head = 0;
    for (let t = 0; t < 1; t += DT) { const o = stepReact(r, st, DT, R, { gentle }); roll = Math.max(roll, o.roll); head = Math.max(head, Math.abs(o.hRoll)); }
    return [roll, head];
  };
  const [roll, head] = peakRoll(false), [groll] = peakRoll(true);
  assert.ok(roll > 0.1 && head > 0.05);
  assert.ok(Math.abs(groll - roll / 2) < 0.02, 'Gentle halves the tilt');
  const r = createReact(0), st = moving();
  reactTo(r, 'bump', { dv: 0.5, lx: 1 }, R);
  assert.equal(r.tr.v, 0, 'a brush is ignored');
  reactTo(r, 'bump', { dv: R.dizzyMps + 1, lx: -1, lz: 0 }, R);
  assert.deepEqual(r.sprays, [['stars', R.starCount]]);
  assert.equal(stepReact(r, st, DT, R).face, 'dizzy');
  run(r, st, R.dizzyS + 0.5);
  assert.equal(r.out.face, 'neutral');
  const w = createReact(0);
  reactTo(w, 'bump', { dv: R.sweatMps + 0.5, lx: 1 }, R);
  assert.deepEqual(w.sprays, [['sweat', R.sweatCount]]);
  assert.equal(stepReact(w, st, DT, R).face, 'x-eyes');
  const wall = createReact(0); reactTo(wall, 'wall', { dv: 1, side: 1 }, R);
  assert.equal(wall.tr.v, 0);
  reactTo(wall, 'wall', { dv: 6, side: 1 }, R);
  assert.ok(wall.tr.v > 0);
});

test('react: item effects pick their reaction (spin, stiff, droop, wiggle) and face', () => {
  const st = moving(), at = (kind, s = 0.6, gentle = false) => {
    const r = createReact(0); reactTo(r, 'effect', { kind }, R, gentle); run(r, st, s, { gentle }); return r;
  };
  const sp = at('spiral');
  assert.ok(sp.out.yaw > 1 && sp.out.face === 'dizzy');
  reactTo(sp, 'effect', { kind: null }, R);
  run(sp, st, 4);
  assert.equal(sp.out.yaw, 0, 'the spin finishes its turn and stops');
  const fr = at('freeze');
  assert.ok(fr.out.stiff < 0.3 && fr.out.face === 'frozen');
  const me = at('melt', 1.5);
  assert.ok(me.out.hPitch < -0.2 && me.out.sq > 0.05 && me.out.face === 'sleepy');
  assert.ok(me.out.armsK > 0.9 && me.out.arms === 0);
  const go = at('golden', 0.3);
  assert.equal(go.out.face, 'heart');
  assert.ok(go.out.armsK > 0.5);
  const sh = createReact(0); reactTo(sh, 'effect', { kind: 'shatter' }, R);
  assert.equal(sh.sprays[0][0], 'stars');
  assert.equal(stepReact(sh, st, DT, R).face, 'x-eyes');
  assert.equal(moodFace({ effect: 'blackout', dizT: -1, finish: 0 }, st)[0], 'blank');
});

test('react: drift leans in with the head counter-leaning; a boost leans back with a sparkle face', () => {
  const r = createReact(0), st = moving();
  st[S.driftDir] = 1;
  run(r, st, 1, { lean: -0.2 });
  assert.ok(r.out.hRoll > 0.2, 'the head leans against the body');
  assert.equal(r.out.face, 'drift-focus');
  st[S.driftDir] = 0; st[S.boostKind] = BOOST.DRIFT3;
  reactTo(r, 'boost', { kind: BOOST.DRIFT3 }, R);
  run(r, st, 0.5);
  assert.equal(r.out.face, 'sparkle');
  assert.ok(r.out.pitch > 0.05 && r.out.hPitch > 0.1);
});

test('react: overtake is smug with a tiny hop, overtaken is sad; finish cheers the top 3 and sulks last', () => {
  const st = moving(), r = createReact(0);
  reactTo(r, 'overtake', {}, R);
  let lift = 0;
  for (let t = 0; t < R.overtakeHopS; t += DT) lift = Math.max(lift, stepReact(r, st, DT, R).lift);
  assert.ok(lift > R.overtakeHopM * 0.9 && r.out.face === 'smug');
  run(r, st, 0.2);
  assert.equal(r.out.lift, 0);
  const s = createReact(0); reactTo(s, 'overtaken', {}, R); run(s, st, 0.3);
  assert.equal(s.out.face, 'sad');
  const c = createReact(0); reactTo(c, 'finish', { place: 2, count: 8 }, R); run(c, st, 0.5);
  assert.equal(c.out.face, 'cheer'); assert.ok(c.out.armsK > 0.9 && c.out.arms > 0.6);
  const l = createReact(0); reactTo(l, 'finish', { place: 8, count: 8 }, R); run(l, st, 1);
  assert.equal(l.out.face, 'sad'); assert.ok(l.out.hPitch < -0.2);
  const m = createReact(0); reactTo(m, 'finish', { place: 5, count: 8 }, R); run(m, st, 0.5);
  assert.equal(m.out.face, 'happy');
  const n = createReact(0); reactTo(n, 'finish', {}, R); assert.equal(n.finish, 0);
});

test('react: a new countdown clears last race\'s cheer and sulk (Again, Restart, Menu)', () => {
  const st = moving();
  for (const place of [1, 8]) {
    const r = createReact(0);
    const rows = [{ i: 0, place }, { i: 1, place: 9 - place }].concat(Array.from({ length: 6 }, (_, j) => ({ i: j + 2, place: j + 2 })));
    for (const [i, kind, a] of reactionsFor('race-result', { rows })) if (i === 0) reactTo(r, kind, a, R);
    run(r, st, 1);
    assert.equal(r.out.face, place === 1 ? 'cheer' : 'sad');
    for (const [, kind, a] of reactionsFor('countdown', { n: 3 })) reactTo(r, kind, a, R);
    run(r, st, Math.max(R.faceMinS, 0.5) + 0.1);
    assert.equal(r.finish, 0);
    assert.equal(r.out.face, 'neutral');
    assert.equal(r.out.lift, 0);
  }
});

test('react: idle karts breathe and glance about; a countdown pulse makes everyone look up', () => {
  const st = createState(), r = createReact(2);
  let lo = 1, hi = -1, yaw = 0;
  for (let t = 0; t < 6; t += DT) { const o = stepReact(r, st, DT, R); lo = Math.min(lo, o.sq); hi = Math.max(hi, o.sq); yaw = Math.max(yaw, Math.abs(o.hYaw)); }
  assert.ok(hi - lo > R.breathSq, 'a breathing bob');
  assert.ok(yaw > 0.15, 'a glance');
  const c = createReact(0);
  reactTo(c, 'countdown', { n: 1 }, R);
  run(c, st, 0.4);
  assert.ok(c.out.hPitch > 0.05 && c.out.face === 'determined');
  reactTo(c, 'go', {}, R); run(c, st, 0.3);
  assert.equal(c.out.face, 'grin');
});

test('react: faces hold a short beat, timed faces expire, eyes blink now and then (not when shut)', () => {
  const st = moving(), r = createReact(0);
  reactTo(r, 'overtake', {}, R);
  stepReact(r, st, DT, R);
  assert.equal(r.out.face, 'smug');
  reactTo(r, 'overtaken', {}, R);
  stepReact(r, st, DT, R);
  assert.equal(r.out.face, 'smug', 'a change waits faceMinS');
  run(r, st, R.faceMinS);
  assert.equal(r.out.face, 'sad');
  run(r, st, R.faceHoldS + 0.3);
  assert.equal(r.out.face, 'neutral');
  const b = createReact(0);
  let blinks = 0, was = false;
  for (let t = 0; t < 12; t += DT) { const o = stepReact(b, st, DT, R); if (o.blink && !was) blinks++; was = o.blink; }
  assert.ok(blinks >= 2 && blinks <= 6, 'blinks ' + blinks);
  const h = createReact(0); reactTo(h, 'effect', { kind: 'spiral' }, R);
  let any = false;
  for (let t = 0; t < 8; t += DT) any = stepReact(h, st, DT, R).blink || any;
  assert.equal(any, false, 'spiral eyes never blink');
  assert.equal(typeof blinkNow, 'function'); assert.equal(typeof pickFace, 'function');
  assert.equal(reactNumbers({ react: { blinkS: 0.2 } }).blinkS, 0.2);
});

test('faces: the 16 ids match the atlas contract, cells run in order, every face draws', () => {
  assert.deepEqual([...FACE_IDS], ['neutral', 'happy', 'grin', 'wink', 'determined', 'drift-focus', 'dizzy', 'sleepy',
    'frozen', 'heart', 'x-eyes', 'smug', 'sad', 'sparkle', 'cheer', 'blank']);
  assert.deepEqual(faceCell('neutral'), { col: 0, row: 0 });
  assert.deepEqual(faceCell('blank'), { col: 3, row: 3 });
  assert.deepEqual(faceCell('frozen'), { col: 0, row: 2 });
  assert.deepEqual(faceCell('nope'), faceCell('neutral'));
  const calls = [];
  const g = new Proxy({}, { get: (o, k) => (k in o ? o[k] : (...a) => calls.push(k)), set: (o, k, v) => { o[k] = v; return true; } });
  for (const id of FACE_IDS) {
    calls.length = 0;
    assert.equal(drawFace(g, id, { blink: true, t: 0.5 }), id);
    const marks = calls.filter((c) => c === 'fill' || c === 'stroke' || c === 'fillRect' || c === 'strokeRect').length;
    if (id === 'blank') assert.equal(marks, 0); else assert.ok(marks >= 3, id + ' draws eyes and a mouth');
  }
  assert.equal(drawFace(g, 'nope'), 'neutral');
});
