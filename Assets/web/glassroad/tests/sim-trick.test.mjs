// tests/sim-trick.test.mjs - polish 4 FEEL lane: the trick boost (sim/trick.js). A hop timed at a lip, a crest
// or a boost pad pays a boost on landing; a press slightly early or late gets nothing.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { S, BOOST, RESPAWN, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { BOOST_TRICK } from '../sim/boost.js';
import { lipAhead, trickOnHop, noteTakeoff, trickOf } from '../sim/trick.js';
import { loadTrack } from '../track/load.js';
import { qlook } from '../core/math.js';
import { DT, makeFeel, fakeTrack, place, recorder, acts, run } from './sim-fake.mjs';

const near = (a, b, e, msg = '') => assert.ok(Math.abs(a - b) <= e, `${msg} ${a} vs ${b} (+-${e})`);

/** Airborne kart over a flat road that just left a lip; press hop after pressS (or never), run to the landing. */
function lateRun(pressS, heightM = 2.5) {
  const feel = makeFeel(), tr = fakeTrack({ w: 400 }), rec = recorder();
  const st = place(tr, { speed: 25, height: heightM, airborne: true });
  noteTakeoff(st, 'lip');
  const a = acts({ throttle: 1 });
  for (let i = 0; i < 120 && !rec.first('land'); i++) {
    if (pressS >= 0 && Math.abs((i + 1) * DT - pressS) < DT / 2) a.hopPressed = true;
    run(st, tr, feel, a, DT, rec);
  }
  return { feel, st, rec };
}

test('late side: a press just after a lip is great, a little later good, past lateS nothing', () => {
  const f = makeFeel();
  const great = lateRun(DT), good = lateRun(f.get('trick.lateS') - DT), late = lateRun(f.get('trick.lateS') + 3 * DT), none = lateRun(-1);
  assert.deepEqual([great, good].map((r) => r.rec.first('trick').payload.grade), ['great', 'good']);
  assert.equal(great.rec.first('trick').payload.at, 'lip');
  assert.ok(good.rec.first('trick').payload.airS > great.rec.first('trick').payload.airS, 'airS = air so far');
  for (const r of [late, none]) { assert.ok(!r.rec.first('trick'), 'no trick'); assert.ok(!r.rec.first('trick-land')); }
  assert.ok(!late.rec.first('hop'), 'an air press never hops');
});

test('landing pays the trick: kind trick, bigger for great, longer for bigger air', () => {
  const f = makeFeel(), great = lateRun(DT), good = lateRun(0.15), big = lateRun(DT, 6);
  for (const r of [great, good, big]) {
    const tl = r.rec.first('trick-land'), bs = r.rec.all('boost-start').pop().payload;
    assert.ok(tl, 'trick-land');
    assert.equal(bs.kind, 'trick'); assert.equal(r.st[S.boostKind], BOOST_TRICK);
    near(tl.payload.airS, r.rec.first('land').payload.airTime, 1e-9);
    assert.ok(tl.t >= r.rec.first('land').t, 'after the land event');
  }
  near(great.rec.first('trick-land').payload.amount, f.get('trick.amount')[1], 1e-9);
  near(good.rec.first('trick-land').payload.amount, f.get('trick.amount')[0], 1e-9);
  assert.ok(big.rec.first('trick-land').payload.holdS > great.rec.first('trick-land').payload.holdS, 'bigger air, longer hold');
  assert.ok(big.rec.first('trick-land').payload.holdS <= f.get('trick.holdMaxS') + 1e-9);
  assert.equal(trickOf(great.st), '', 'paid once');
});

test('pad: a hop just after entering a pad is a pad trick that never pays less than the pad', () => {
  const f = makeFeel(), mk = (pressAfterS) => {
    const tr = fakeTrack({ w: 400, boosts: [{ node: 12, x: 0, w: 4, len: 6 }] }), rec = recorder();
    const st = place(tr, { s: 110, speed: 25 }), a = acts({ throttle: 1 });
    let padT = -1;
    for (let i = 0; i < 150; i++) {
      if (padT >= 0 && Math.abs(rec.t - padT - pressAfterS) < DT / 2) a.hopPressed = true;
      run(st, tr, f, a, DT, rec);
      if (padT < 0 && rec.first('pad')) padT = rec.first('pad').t;
    }
    return rec;
  };
  const hit = mk(DT), miss = mk(f.get('trick.padLateS') + 0.1);
  assert.equal(hit.first('trick').payload.at, 'pad');
  assert.equal(hit.first('trick').payload.grade, 'great');
  const tl = hit.first('trick-land').payload;
  assert.ok(tl.amount >= f.get('pad.amount') + f.get('trick.stackAdd') - 1e-9, 'stacks over the pad');
  assert.ok(miss.first('hop') && !miss.first('trick'), 'a late hop is a plain hop');
});

test('early side (track passed to hop): a jump or pad just ahead counts, further ahead does not', () => {
  const f = makeFeel(), tr = fakeTrack({ w: 400, jumps: [{ node: 30, land: 32 }], boosts: [{ node: 50, x: 3, w: 4, len: 6 }] });
  const v = 30, reach = v * f.get('trick.earlyS');
  assert.deepEqual(lipAhead(tr, 300 - 1, 0, reach, f), { at: 'lip', d: 1 });
  assert.equal(lipAhead(tr, 300 - reach - 1, 0, reach, f), null, 'too early');
  assert.equal(lipAhead(tr, 300 + 1, 0, reach, f), null, 'behind');
  assert.equal(lipAhead(tr, 500 - 2, 0, reach, f), null, 'a pad off to the side');
  assert.equal(lipAhead(tr, 500 - 2, 3, reach, f).at, 'pad');
  const near1 = place(tr, { s: 299, speed: v }), far = place(tr, { s: 300 - reach * 0.8, speed: v }), r1 = recorder(), r2 = recorder();
  trickOnHop(near1, f, r1, v, tr); trickOnHop(far, f, r2, v, tr);
  assert.equal(r1.first('trick').payload.grade, 'great');
  assert.equal(r2.first('trick').payload.grade, 'good');
  const no = place(tr, { s: 200, speed: v }), r3 = recorder();
  trickOnHop(no, f, r3, v, tr); trickOnHop(no, f, r3, v, null);
  assert.ok(!r3.first('trick'));
});

test('a rescue loses an armed trick: no trick-land after the drop', () => {
  const f = makeFeel(), tr = fakeTrack({ w: 400 }), rec = recorder();
  const st = place(tr, { speed: 25, height: 2.5, airborne: true });
  noteTakeoff(st, 'lip');
  run(st, tr, f, acts({ throttle: 1, hopPressed: true }), DT, rec);
  assert.equal(trickOf(st), 'great');
  st[S.respawnPhase] = RESPAWN.GRACE;
  run(st, tr, f, acts({ throttle: 1 }), DT, rec);
  assert.equal(trickOf(st), '');
});

test('Threshold jump: drive off the real lip, press a beat after take-off, land a great trick', () => {
  const tr = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
  const f = makeFeel(), j = tr.jumps[0];
  const fr = tr.frameAt(j.s - 60, { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] });
  const st = createState();
  setPos(st, fr.p); setQuat(st, qlook([0, 0, 0, 1], fr.t, fr.n)); setVel(st, fr.t.map((x) => x * 30));
  st[S.engineV] = 30; st[S.s] = j.s - 60; st[S.seg] = fr.seg; st[S.latchSeg] = fr.seg; st[S.hopT] = 1e3; st[S.grounded] = 1;
  const rec = recorder(), a = acts({ throttle: 1 });
  let off = -1;
  for (let i = 0; i < 300 && !rec.first('trick-land'); i++) {
    rec.t += DT;
    if (off >= 0 && rec.t - off < DT * 1.5 && rec.t > off) a.hopPressed = true;
    step(st, a, tr, f, DT, rec); updateRespawn(st, tr, f, DT, rec);
    a.hopPressed = false;
    const air = rec.all('airborne').find((e) => e.payload.cause === 'lip');
    if (off < 0 && air) off = air.t;
  }
  assert.ok(off > 0, 'took off at the lip');
  assert.equal(rec.first('trick').payload.grade, 'great');
  const tl = rec.first('trick-land');
  assert.ok(tl && tl.payload.airS > 0.4, 'real air: ' + (tl && tl.payload.airS));
  assert.ok(!rec.first('drop'), 'landed on the road');
});
