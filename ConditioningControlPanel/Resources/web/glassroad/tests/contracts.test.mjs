import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as M from '../core/math.js';
import { createBus, createQueue } from '../core/events.js';
import { createFeel } from '../core/feel.js';
import { S, STATE_SIZE, FIELDS, createState, copyState, cloneState, lerpState, BOOST, RESPAWN } from '../sim/state.js';

const near = (a, b, e = 1e-6) => assert.ok(Math.abs(a - b) <= e, a + ' vs ' + b);
const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));

test('math scalars', () => {
  near(M.clamp(5, 0, 1), 1); near(M.lerp(0, 10, 0.25), 2.5);
  near(M.smoothstep(0, 1, 0.5), 0.5); near(M.smoothstep(0, 1, -1), 0);
  near(M.damp(0, 1, 0.1, 0.1), 1 - Math.exp(-1));
  near(M.angleDiff(0.1, -0.1), -0.2); near(Math.abs(M.angleDiff(-3, 3)), 2 * Math.PI - 6, 1e-9);
  near(M.wrap(-1, 10), 9);
});

test('math quaternions: basis, rotate, slerp', () => {
  const q = M.qlook([0, 0, 0, 1], [1, 0, 0], [0, 1, 0]);
  const f = M.qrotate([0, 0, 0], q, [0, 0, -1]);
  near(f[0], 1); near(f[1], 0); near(f[2], 0);
  const r = M.qrotate([0, 0, 0], q, [1, 0, 0]);
  near(r[2], 1); // facing +X with +Y up, right is +Z
  const half = M.qslerp([0, 0, 0, 1], [0, 0, 0, 1], M.qaxis([0, 0, 0, 1], [0, 1, 0], Math.PI / 2), 0.5);
  near(half[1], Math.sin(Math.PI / 8)); near(half[3], Math.cos(Math.PI / 8));
  const m = M.m3fromQuat(new Array(9), q);
  near(m[0], r[0]); near(m[2], r[2]);
});

test('events: bus is synchronous, queue flushes in order', () => {
  const bus = createBus(), seen = [];
  const off = bus.on('hop', (p) => seen.push('hop' + p.n));
  bus.on('*', (_p, name) => seen.push('*' + name));
  bus.emit('hop', { n: 1 });
  const q = createQueue();
  q.emit('hop', { n: 2 }); q.emit('land', { tier: 1 });
  assert.equal(q.length, 2);
  assert.equal(q.flush(bus), 2);
  off(); bus.emit('hop', { n: 3 });
  assert.deepEqual(seen, ['hop1', '*hop', 'hop2', '*hop', '*land', '*hop']);
});

test('feel: get, rad, set, subscribe, reset', () => {
  const feel = createFeel(feelJson), seen = [];
  near(feel.get('speed.topMps'), 37.5);
  near(feel.rad('fov.baseVDeg'), Math.PI / 3);
  const un = feel.subscribe((p, v) => seen.push(p + '=' + v));
  assert.equal(feel.set('speed.topMps', 36), true);
  assert.equal(feel.set('speed.nope', 1), false);
  near(feel.get('speed.topMps'), 36);
  un(); feel.reset();
  near(feel.get('speed.topMps'), 37.5);
  assert.deepEqual(seen, ['speed.topMps=36']);
  assert.throws(() => feel.get('missing.key'));
});

test('feel.json and FEEL.md list the same keys', () => {
  const md = readFileSync(new URL('../FEEL.md', import.meta.url), 'utf8');
  const listed = new Set([...md.matchAll(/^- `([a-zA-Z0-9]+\.[a-zA-Z0-9]+)`/gm)].map((m) => m[1]));
  const leaves = [];
  for (const [g, o] of Object.entries(feelJson)) if (o && typeof o === 'object') for (const k of Object.keys(o)) leaves.push(g + '.' + k);
  for (const k of leaves) assert.ok(listed.has(k), 'FEEL.md misses ' + k);
  for (const k of listed) assert.ok(leaves.includes(k), 'feel.json misses ' + k);
});

test('state layout', () => {
  assert.equal(STATE_SIZE, FIELDS.length);
  assert.equal(S.posX, 0); assert.equal(S.quatW, 6);
  for (const k of ['engineV', 'steerSlew', 'driftStage', 'driftDir', 'driftCharge', 'boostKind', 'boostT', 'airborne', 'airTime',
    'seg', 'segT', 'lateral', 'height', 'lap', 'checkpoint', 'latchSeg', 'respawnPhase', 'respawnT', 'edgeT', 'stalled', 'speedPct'])
    assert.ok(Number.isInteger(S[k]), k);
  assert.equal(BOOST.LAUNCH, 5); assert.equal(RESPAWN.GRACE, 4);
  const a = createState(), b = createState();
  assert.equal(a[S.quatW], 1);
  b[S.posX] = 10; b[S.lap] = 2; b[S.engineV] = 20;
  M.qaxis(M.q4(), [0, 1, 0], 1).forEach((v, i) => { b[S.quatX + i] = v; });
  const o = lerpState(new Float32Array(STATE_SIZE), a, b, 0.5);
  near(o[S.posX], 5, 1e-5); near(o[S.engineV], 10, 1e-5); assert.equal(o[S.lap], 2);
  near(o[S.quatY], Math.sin(0.25), 1e-5);
  b[S.posX] = 100; // teleport snaps
  near(lerpState(o, a, b, 0.5)[S.posX], 100);
  const c = cloneState(b); assert.notEqual(c, b); near(c[S.posX], 100);
  copyState(a, b); near(a[S.posX], 100);
});
