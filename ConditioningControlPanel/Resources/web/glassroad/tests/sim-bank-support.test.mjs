import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildMainframe } from '../dev/gen-mainframe.mjs';
import { loadTrack } from '../track/load.js';
import { S, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { createContact } from '../sim/contact.js';
import { limitBankHop } from '../sim/hop.js';
import { qlook } from '../core/math.js';
import { makeFeel, acts, recorder } from './sim-fake.mjs';
import { fixtureJson } from './sim-fixture-p6.mjs';

const track = loadTrack(buildMainframe().track);
const section = track.sections.find(s => s.id === 'coolant-canal');
function bankState(s, x, heading, speed = 60) {
  const f = track.surfaceAt(s, x), st = createState();
  const a = heading * Math.PI / 180, dir = f.t.map((v, k) => v * Math.cos(a) + f.b[k] * Math.sin(a));
  setPos(st, f.p); setQuat(st, qlook([0, 0, 0, 1], dir, f.n)); setVel(st, dir.map(v => v * speed));
  st[S.seg] = st[S.latchSeg] = f.seg; st[S.s] = s; st[S.lateral] = x;
  st[S.grounded] = 1; st[S.hopT] = 1000; st[S.engineV] = speed;
  return st;
}

for (const side of [-1, 1]) test('fast grounded kart follows the curved bank through its rail and slope, side ' + side, () => {
  const feel = makeFeel(), input = acts({ throttle: 1 }), ev = recorder();
  for (const s of [section.fromS + 18, section.fromS + 55, section.toS - 35]) {
    const st = bankState(s, side * 17.8, side * 55);
    for (let i = 0; i < 90; i++) {
      step(st, input, track, feel, 1 / 60, ev);
      const q = track.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg]);
      assert.equal(st[S.grounded], 1, 'lost supported road at ' + q.s + ', height ' + q.height);
      assert.ok(Math.abs(q.height) < .025, 'feet must remain on the bank, height ' + q.height);
    }
  }
});

for (const side of [-1, 1]) test('side hop keeps normal jump lift without launching with bank climbing velocity, side ' + side, () => {
  const feel = makeFeel(), st = bankState(section.fromS + 55, side * 17, side * 65, 60);
  const f = track.frameAt(st[S.s]), before = [st[S.velX], st[S.velY], st[S.velZ]];
  const along = before.reduce((v, x, k) => v + x * f.t[k], 0);
  step(st, acts({ throttle: 1, hopPressed: true }), track, feel, 1 / 120, recorder());
  assert.equal(st[S.airborne], 1);
  assert.ok(st[S.velY] <= Math.max(0, along * f.t[1]) + feel.get('hop.upMps') + .5, 'bank climb added excessive vertical lift: ' + st[S.velY]);
  const ev = recorder();
  for (let i = 0; i < 180 && !ev.first('land'); i++) step(st, acts({ throttle: 1 }), track, feel, 1 / 120, ev);
  assert.ok(ev.first('land'), 'side hop returns to the curved road');
});

for (const x of [0, 4, 8]) test('canal lane ' + x + ' keeps grounded feet on the sloping floor', () => {
  const st = bankState(section.fromS + 2, x, 0, 45), feel = makeFeel(), ev = recorder();
  for (let i = 0; i < 480; i++) {
    step(st, acts({ throttle: 1 }), track, feel, 1 / 120, ev);
    const q = track.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg]);
    assert.equal(st[S.grounded], 1);
    assert.ok(Math.abs(q.height) < .025, 'lane support error ' + q.height);
  }
});

test('ordinary kart contact leaves both banked karts on the road', () => {
  const feel = makeFeel(), ev = recorder(), contact = createContact(track, feel, ev);
  const racers = [16.8, 17.4].map((x, i) => ({ i, st: bankState(section.fromS + 55, x, 0, 20), feel, fxv: {}, events: ev }));
  contact.update(1 / 60, racers, { phase: 'racing', timeS: 10 });
  assert.notEqual(racers[0].st[S.lateral], 16.8, 'overlap was resolved');
  for (const r of racers) {
    const q = track.query(r.st[S.posX], r.st[S.posY], r.st[S.posZ], r.st[S.seg]);
    assert.ok(Math.abs(q.height) < .025, 'contact embedded the kart: ' + q.height);
    assert.ok(q.edgeDist > .5);
  }
});

test('bank hop limiting preserves genuine along-road ramp lift and downhill velocity', () => {
  const tangent = [0, .6, -.8], normal = [0, .8, .6], ramp = [0, 24, -32];
  limitBankHop(ramp, normal, tangent, 6.8);
  assert.deepEqual(ramp, [0, 24, -32]);
  const downhill = [0, -12, -24];
  limitBankHop(downhill, [Math.SQRT1_2, Math.SQRT1_2, 0], [0, 0, -1], 6.8);
  assert.deepEqual(downhill, [0, -12, -24]);
});

for (const vy of [-4, 12]) test('a kart beneath an upper deck is never attached through its underside, vertical speed ' + vy, () => {
  const stacked = loadTrack(fixtureJson()), upper = stacked.query(110, 14, -16), st = createState(), ev = recorder();
  setPos(st, [110, 13.8, -16]); setVel(st, [0, vy, 0]);
  st[S.seg] = st[S.latchSeg] = upper.seg; st[S.s] = upper.s; st[S.airborne] = 1; st[S.hopT] = 1000;
  step(st, acts(), stacked, makeFeel(), 1 / 120, ev);
  assert.equal(st[S.grounded], 0); assert.equal(ev.all('land').length, 0);
});
