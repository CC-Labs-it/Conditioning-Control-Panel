import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { ORBIT, bounce, canCapture, contact, createOrbits, halfSize, orbitStep, snapDiag, turnToward } from '../ui/unlocks/basics/pair-orbit.js';

const body = (x, y, vx, vy, w = 80, h = 40) => ({ x, y, vx, vy, w, h });
const speed = (b) => Math.hypot(b.vx, b.vy);

test('capture needs a small ring and an approach', () => {
  const a = body(0, 0, 200, 0), ring = ORBIT.CAPTURE * (halfSize(a) * 2);
  assert.ok(ORBIT.CAPTURE >= 1.2 && ORBIT.CAPTURE <= 1.6 && ORBIT.INSET < 1);
  assert.equal(canCapture(a, body(ring - 1, 0, -200, 0)), true);
  assert.equal(canCapture(a, body(ring + 1, 0, -200, 0)), false, 'outside the ring');
  assert.equal(canCapture(body(0, 0, -200, 0), body(ring - 1, 0, 200, 0)), false, 'already leaving');
});

test('contact uses the inset box, never the painted one', () => {
  const a = body(0, 0, 0, 0);
  assert.equal(contact(a, body(79, 0, 0, 0)), null, 'painted boxes touch, inset boxes do not');
  const hit = contact(a, body(60, 0, 0, 0));
  assert.equal(hit.axis, 'x');
  assert.ok(Math.abs(hit.depth - 4) < 1e-9);
});

test('an orbit step only turns the velocities', () => {
  const a = body(0, 0, 230, 40), b = body(90, 10, -180, -60);
  const sa = speed(a), sb = speed(b), pair = { age: 0, sense: 1 };
  for (let k = 0; k < 90; k++) { orbitStep(a, b, pair, 1 / 60); pair.age += 1 / 60; a.x += a.vx / 60; a.y += a.vy / 60; b.x += b.vx / 60; b.y += b.vy / 60; }
  assert.ok(Math.abs(speed(a) - sa) < 1e-6 && Math.abs(speed(b) - sb) < 1e-6);
  const [vx, vy] = turnToward(3, 4, 0, 1, 10);
  assert.ok(Math.abs(Math.hypot(vx, vy) - 5) < 1e-9 && Math.abs(vx) < 1e-9);
});

test('the bounce mirrors along the normal, keeps speeds and pushes the boxes apart', () => {
  const a = body(0, 0, 200, 30), b = body(60, 0, -150, 10);
  const sa = speed(a), sb = speed(b);
  const out = bounce(a, b, contact(a, b));
  assert.equal(out.axis, 'x');
  assert.ok(Math.abs(speed(a) - sa) < 1e-9 && Math.abs(speed(b) - sb) < 1e-9);
  assert.ok(a.vx < 0 && b.vx > 0, 'heading apart');
  assert.equal(contact(a, b), null, 'no longer overlapping');
});

test('a converging pair spirals, touches, bounces at its old speeds, then cools down', () => {
  const orbits = createOrbits();
  const bodies = [body(0, 0, 260, 0), body(220, 70, -260, 0)];
  const s0 = bodies.map(speed);
  const seen = [];
  let turned = 0, t = 0;
  for (; t < 6 && !seen.includes('contact'); t += 1 / 60) {
    const before = Math.atan2(bodies[0].vy, bodies[0].vx);
    for (const e of orbits.step(bodies, 1 / 60)) seen.push(e.type);
    let d = Math.atan2(bodies[0].vy, bodies[0].vx) - before; d = Math.atan2(Math.sin(d), Math.cos(d));
    if (!seen.includes('contact')) turned += Math.abs(d);
    for (const b of bodies) { b.x += b.vx / 60; b.y += b.vy / 60; }
  }
  assert.deepEqual(seen, ['capture', 'contact']);
  assert.ok(t <= ORBIT.MAX_S + 0.05, 'resolves within the cap');
  assert.ok(turned > 0.5, 'the path curved: ' + turned);
  bodies.forEach((b, i) => assert.ok(Math.abs(speed(b) - s0[i]) < 1e-6));
  assert.equal(orbits.cooling(0, 1), true);
  bodies[1] = body(bodies[0].x + 70, bodies[0].y, -260, 0);
  assert.ok(!orbits.step(bodies, 1 / 60).some((e) => e.type === 'capture'), 'no re-capture while cooling');
  for (let k = 0; k < ORBIT.COOLDOWN_S * 60 + 2; k++) orbits.step([null, null], 1 / 60);
  assert.equal(orbits.cooling(0, 1), false);
});

test('a slow spiral resolves into the bounce at the cap', () => {
  const orbits = createOrbits({ ...ORBIT, INWARD_0: 0, INWARD_1: 0, TURN: 50 });
  const bodies = [body(0, 0, 0, -200), body(80, 0, 0, 200)];
  let out = [];
  for (let t = 0; t < ORBIT.MAX_S + 0.1 && !out.some((e) => e.type === 'contact'); t += 1 / 60) {
    out = orbits.step(bodies, 1 / 60);
    for (const b of bodies) { b.x += b.vx / 60; b.y += b.vy / 60; }
  }
  assert.ok(out.some((e) => e.type === 'contact'));
});

test('pairs only, and never more than MAX_PAIRS at once', () => {
  const orbits = createOrbits({ ...ORBIT, GAP_S: 0 });
  const cluster = (cx) => [body(cx, 0, 200, 0), body(cx + 100, 0, -200, 0), body(cx + 50, 60, 0, -200)];
  const bodies = [...cluster(0), ...cluster(1000), ...cluster(2000)];
  orbits.step(bodies, 1 / 60);
  const pairs = orbits.pairs;
  assert.equal(pairs.length, ORBIT.MAX_PAIRS);
  const used = pairs.flatMap((p) => [p.i, p.j]);
  assert.equal(new Set(used).size, used.length, 'a copy is in one pair at most');
});

test('at most one capture per GAP_S anywhere on screen (owner 2026-11-08: too busy)', () => {
  const orbits = createOrbits();
  const cluster = (cx) => [body(cx, 0, 200, 0), body(cx + 80, 0, -200, 0)];
  const caps = () => orbits.step([...cluster(0), ...cluster(1000), ...cluster(2000)], 1 / 60).filter((e) => e.type === 'capture').length;
  assert.equal(caps(), 1, 'one capture even with three pairs in range');
  orbits.clear();
  assert.equal(caps(), 1);
  let later = 0;
  for (let k = 0; k < (ORBIT.GAP_S - 1) * 60; k++) later += orbits.step([null, null, null, null, null, null], 1 / 60).filter((e) => e.type === 'capture').length;
  assert.equal(later, 0);
  assert.ok(ORBIT.GAP_S >= 6);
});

test('snapDiag keeps the wall-hit rate and a diagonal lean', () => {
  for (const [dx, dy] of [[2, 0], [0.1, -3], [-1, -1], [0, 0]]) {
    const [x, y] = snapDiag(dx, dy);
    assert.ok(Math.abs(Math.abs(x) + Math.abs(y) - 2) < 1e-9);
    assert.ok(Math.abs(x) >= 0.7 - 1e-9 && Math.abs(x) <= 1.3 + 1e-9);
    if (dx) assert.equal(Math.sign(x), Math.sign(dx));
  }
});

test('the spiral rides Corner Bonus and stays off under Gentle and reduced motion', () => {
  const src = readFileSync(new URL('../ui/unlocks/basics.js', import.meta.url), 'utf8');
  assert.match(src, /!ctx\.has\('corner'\) \|\| ctx\.gentle \|\| ctx\.reduced/);
});

test('overlapping copies pass through, and a copy rests after a spiral before joining another', () => {
  assert.equal(canCapture(body(0, 0, 200, 0), body(20, 0, -200, 0)), false, 'already overlapping');
  const orbits = createOrbits({ ...ORBIT, MAX_S: 0.05, GAP_S: 0 });
  const bodies = [body(0, 0, 200, 0), body(80, 0, -200, 0), body(-500, 0, 0, 0)];
  orbits.step(bodies, 1 / 60);
  for (let k = 0; k < 6; k++) orbits.step(bodies, 1 / 60);
  assert.equal(orbits.pairs.length, 0);
  bodies[2] = body(bodies[0].x - 80, bodies[0].y, 200, 0);
  bodies[1] = body(5000, 0, 0, 0);
  assert.ok(!orbits.step(bodies, 1 / 60).some((e) => e.type === 'capture'), 'copy 0 is resting');
  for (let k = 0; k < ORBIT.REST_S * 60 + 2; k++) orbits.step([null, null, null], 1 / 60);
  bodies[2] = body(bodies[0].x - 80, bodies[0].y, 200, 0);
  assert.ok(orbits.step(bodies, 1 / 60).some((e) => e.type === 'capture'), 'rested');
});
