import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { validate } from '../track/schema.js';

const track = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const P = track.nodes.map((n) => n.p);
const N = P.length;
const dist = (a, b) => Math.hypot(b[0] - a[0], b[1] - a[1], b[2] - a[2]);

test('threshold validates with no warnings', () => {
  const r = validate(track);
  assert.equal(r.ok, true, r.errors.join('; '));
  assert.deepEqual(r.warnings, []);
});

test('threshold is a closed loop', () => {
  assert.equal(track.loop, true);
  assert.ok(dist(P[N - 1], P[0]) <= 15);
  assert.ok(track.nodes[0].flags.includes('start'));
});

test('threshold length is 1.15 to 1.69 km (polish 6: the stacked course, +-25% of the old ring)', () => {
  let L = 0;
  for (let i = 0; i < N; i++) L += dist(P[i], P[(i + 1) % N]);
  assert.ok(L >= 1150 && L <= 1690, 'length ' + L);
});

test('threshold carries the Phase A features', () => {
  assert.deepEqual(track.sections.map((s) => s.act), ['induction', 'deepening', 'release']);
  assert.deepEqual(track.sections.map((s) => s.palette), ['dawn', 'deep', 'afterglow']);
  assert.ok(track.boosts.length >= 16, 'dense acceleration runs retain the original authored pads');
  assert.equal(track.jumps.length, 1);
  assert.ok(track.nodes[track.jumps[0].node].flags.includes('jump'));
  assert.ok(track.nodes.some((n) => n.flags.includes('open')));
  const rolls = track.nodes.map((n) => Math.abs(n.roll));
  assert.ok(Math.max(...rolls) >= 20 && Math.max(...rolls) <= 35);
  assert.equal(track.spawn.grid.length, 8);
  assert.equal(track.checkpoints.length, 4);
  assert.equal(track.beat.bpm, null);
});

test('only the spiral passes over itself, at least 10 m apart', () => {
  let worst = Infinity;
  for (let i = 0; i < N; i++) for (let j = i + 1; j < N; j++) {
    const di = Math.min(j - i, N - (j - i));
    if (di < 6) continue;
    const plan = Math.hypot(P[i][0] - P[j][0], P[i][2] - P[j][2]);
    if (plan < (track.nodes[i].w + track.nodes[j].w) / 2 + 2) worst = Math.min(worst, Math.abs(P[i][1] - P[j][1]));
  }
  assert.ok(worst >= 10, 'closest pass ' + worst);
});
