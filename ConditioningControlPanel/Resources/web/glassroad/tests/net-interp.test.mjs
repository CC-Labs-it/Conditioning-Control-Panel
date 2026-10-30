/* tests/net-interp.test.mjs - remote karts 100 ms in the past, dead reckoning up to 250 ms, 150 ms correction
 * blend, rescue snap, held seats, pulses counted once, a record that agrees moves nothing on screen (NET.md section
 * 3, client render rule). */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createInterp } from '../net/interp.js';
import { createKart, PULSE } from '../net/protocol.js';
import { S, createState } from '../sim/state.js';
import { threshold } from './sim-racers-harness.mjs';

const track = threshold();
const rec = (seq, s, x = 0, more = {}) => Object.assign(createKart(), { seat: 2, seq, s, x, tick: seq, q: [0, 0, 0, 1], v: [0, 0, 20] }, more);
const near = (a, b, eps = 1e-3) => Math.abs(a - b) <= eps;

test('draws 100 ms in the past between the two records around that time', () => {
  const ip = createInterp(track), out = createState();
  ip.push(rec(1, 100), 1000);
  ip.push(rec(2, 102), 1050);
  ip.push(rec(3, 104), 1100);
  const k = ip.sample(2, 1125, out);       // drawn at 1025: halfway 100 -> 102
  assert.ok(k);
  assert.ok(near(out[S.s], 101));
  const f = track.frameAt(101);
  assert.ok(near(out[S.posX], f.p[0], 1e-2) && near(out[S.posZ], f.p[2], 1e-2));
});

test('dead reckons past the newest record for at most 250 ms, then holds', () => {
  const ip = createInterp(track), out = createState();
  ip.push(rec(1, 100), 1000);
  ip.push(rec(2, 102), 1050);              // 40 m/s along s
  ip.sample(2, 1150, out);                 // drawn at 1050: on the newest record
  assert.ok(near(out[S.s], 102));
  ip.sample(2, 1250, out);                 // 100 ms ahead: +4 m (blend settles nothing: no new record)
  assert.ok(near(out[S.s], 106, 1e-2));
  ip.sample(2, 1600, out);                 // 450 ms ahead: capped at 250 ms = +10 m
  assert.ok(near(out[S.s], 112, 1e-2));
});

test('a held (idle) seat does not dead reckon', () => {
  const ip = createInterp(track), out = createState();
  ip.push(rec(1, 100), 1000); ip.push(rec(2, 102), 1050);
  ip.hold(2, true);
  ip.sample(2, 1400, out);
  assert.ok(near(out[S.s], 102));
});

test('a correction is blended out over 150 ms', () => {
  const ip = createInterp(track), out = createState();
  ip.push(rec(1, 100), 1000); ip.push(rec(2, 102), 1050);
  ip.sample(2, 1250, out);                 // extrapolated to 106
  ip.push(rec(3, 103, 0), 1100);           // the kart slowed: the target at 1150 dead-reckons 103 + 1 = 104
  ip.sample(2, 1250, out);                 // first frame with it: still drawn at 106 (error 2 m)
  assert.ok(near(out[S.s], 106, 1e-2));
  ip.sample(2, 1250 + 75, out);            // halfway through the blend: target 104 + 1.5, error 1
  assert.ok(near(out[S.s], 106.5, 2e-2), String(out[S.s]));
  ip.sample(2, 1250 + 150, out);           // blend over: on target 103 + 20 m/s * 0.2 s
  assert.ok(near(out[S.s], 107, 2e-2), String(out[S.s]));
});

test('a rescue teleport snaps instead of blending or interpolating', () => {
  const ip = createInterp(track), out = createState();
  ip.push(rec(1, 500), 1000); ip.push(rec(2, 502), 1050);
  ip.sample(2, 1150, out);
  ip.push(rec(3, 430, 0, { respawn: 1 }), 1100);
  ip.sample(2, 1200, out);
  assert.ok(near(out[S.s], 430));
  assert.equal(out[S.respawnPhase], 1);
});

test('older or repeated seqs are dropped; seq wraps', () => {
  const ip = createInterp(track);
  assert.equal(ip.push(rec(65535, 100), 1000), true);
  assert.equal(ip.push(rec(0, 101), 1050), true);
  assert.equal(ip.push(rec(65535, 99), 1100), false);
  assert.equal(ip.newest(2).k.seq, 0);
});

test('pulses of records behind the drawn time are taken once', () => {
  const ip = createInterp(track);
  ip.push(rec(1, 100, 0, { pulses: PULSE.HOP }), 1000);
  ip.push(rec(2, 101, 0, { pulses: PULSE.LAND }), 1050);
  assert.equal(ip.takePulses(2, 1110), PULSE.HOP);
  assert.equal(ip.takePulses(2, 1110), 0);
  assert.equal(ip.takePulses(2, 1200), PULSE.LAND);
});

test('seed writes the newest record as a full state (keeper hand-off)', () => {
  const ip = createInterp(track), out = createState();
  ip.push(rec(7, 250, 1.5, { lap: 1, checkpoint: 2, speedPct: 0.9 }), 1000);
  ip.seed(2, out);
  assert.ok(near(out[S.s], 250) && near(out[S.lateral], 1.5));
  assert.equal(out[S.lap], 1);
  assert.equal(out[S.checkpoint], 2);
  assert.ok(near(out[S.speedPct], 0.9));
});

test('a record that agrees with the kart moves nothing on screen, frame after frame', () => {
  const ip = createInterp(track), out = createState(), V = 40;    // 40 m/s, a record every 50 ms, drawn at 60 fps
  let next = 0, seq = 0, prev = null;
  for (let t = 0; t < 2000; t += 1000 / 60) {
    while (next <= t) { ip.push(rec(++seq, 100 + V * next / 1000), 1000 + next); next += 50; }
    ip.sample(2, 1100 + t, out);
    if (t > 1000) assert.ok(near(out[S.s] - prev, V / 60), `frame at ${t}: moved ${out[S.s] - prev}`);
    prev = out[S.s];
  }
});

test('a late record that corrects the kart eases in, with no step on screen', () => {
  const ip = createInterp(track), out = createState();
  ip.push(rec(1, 100), 1000); ip.push(rec(2, 102), 1050);        // 40 m/s
  ip.sample(2, 1200, out);                                       // dead reckoning at 1100: s 104
  const before = out[S.s];
  ip.push(rec(3, 103), 1100);                                    // really 20 m/s from here: s 103 at 1100
  ip.sample(2, 1200, out);
  assert.ok(near(out[S.s], before), 'same drawn time, same place');
  ip.sample(2, 1350, out);                                       // past the blend: on the corrected path
  assert.ok(near(out[S.s], 103 + 20 * 0.15, 1e-6));
});

test('whichever sample meets a new record first (sim tick, drawn frame), the blend is the same', () => {
  const a = createInterp(track), b = createInterp(track), oa = createState(), ob = createState(), op = createState();
  for (const ip of [a, b]) { ip.push(rec(1, 100), 1000); ip.push(rec(2, 102), 1050); }
  a.sample(2, 1200, oa); b.sample(2, 1200, ob);
  for (const ip of [a, b]) ip.push(rec(3, 103), 1100);
  a.sample(2, 1200, op); b.sample(2, 1200, op); b.sample(2, 1200, op);
  a.sample(2, 1290, oa); b.sample(2, 1290, ob);
  assert.equal(oa[S.s], ob[S.s]);
});
