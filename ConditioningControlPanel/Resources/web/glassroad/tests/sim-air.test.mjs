import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S, RESPAWN } from '../sim/state.js';
import { anchorNode } from '../sim/respawn.js';
import { landTier } from '../sim/air.js';
import { qrotate, v3dot, v3sub, v3norm, v3reject, v3dist } from '../core/math.js';
import { DT, makeFeel, fakeTrack, place, recorder, acts, run } from './sim-fake.mjs';

const near = (a, b, e, msg = '') => assert.ok(Math.abs(a - b) <= e, `${msg} ${a} vs ${b} (+-${e})`);
const pos = (st) => [st[S.posX], st[S.posY], st[S.posZ]];
const fwd = (st) => qrotate([0, 0, 0], [st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]], [0, 0, -1]);

test('landing tiers by airtime', () => {
  const feel = makeFeel(), g = feel.get('hop.gravityMps2');
  for (const [t, tier] of [[0.2, 0], [0.59, 1], [0.8, 2], [1.2, 3]]) {
    assert.equal(landTier(t, feel), tier);
    const tr = fakeTrack({ w: 40 }), rec = recorder();
    const st = place(tr, { speed: 10, height: 0.5 * g * t * t, airborne: true });
    run(st, tr, feel, acts({ throttle: 1 }), t + 0.1, rec, () => !!rec.first('land'));
    const land = rec.first('land');
    assert.equal(land.payload.tier, tier, 'tier for ' + t + ' s');
    near(land.payload.airTime, t, 2 * DT);
  }
});

test('medium and heavy landings bounce; a light one does not', () => {
  const feel = makeFeel(), g = feel.get('hop.gravityMps2');
  const tr = fakeTrack({ w: 40 }), rec = recorder();
  const st = place(tr, { speed: 10, height: 0.5 * g * 1.2 * 1.2, airborne: true });
  run(st, tr, feel, acts({ throttle: 1 }), 2, rec);
  const lands = rec.all('land').map((e) => e.payload.tier);
  assert.equal(lands[0], 3);
  assert.ok(lands.length >= 2 && lands[1] < 3, 'bounced then settled: ' + lands);
  assert.equal(st[S.grounded], 1);
});

test('drop past an open edge, rescue at the TAKE-OFF node facing ahead', () => {
  const feel = makeFeel(), tr = fakeTrack({ flags: () => [] }), rec = recorder();
  const st = place(tr, { s: 300, lateral: 4, headingDeg: 30, speed: 20 });
  let latch = -1;
  run(st, tr, feel, acts({ throttle: 1 }), 3, rec, (s) => {
    if (latch < 0 && s[S.airborne]) latch = s[S.latchSeg];
    return !!rec.first('land');
  });
  assert.deepEqual(rec.names().filter((n) => !n.startsWith('boost')),
    ['airborne', 'edge-enter', 'edge-exit', 'drop', 'rescue-pull', 'rescue-hover', 'rescue-release', 'land']);
  assert.equal(rec.first('airborne').payload.cause, 'edge');
  assert.deepEqual(rec.first('edge-exit').payload, { to: 'drop' });
  assert.equal(rec.first('drop').payload.cause, 'edge');
  // drop timing: the 1.2 m grace distance, then 0.8 s
  const dropT = rec.first('drop').t - rec.first('edge-enter').t;
  assert.ok(dropT > feel.get('drop.edgeGraceS') && dropT < feel.get('drop.edgeGraceS') + 0.3, 'drop after ' + dropT);
  near(rec.first('land').t - rec.first('drop').t,
    feel.get('respawn.pullS') + feel.get('respawn.hoverS') + feel.get('respawn.releaseS'), 3 * DT, 'rescue length');
  assert.deepEqual(rec.first('land').payload, { tier: 1, airTime: 0 });
  // at the latched node, facing aheadNodes on
  assert.ok(latch >= 30 && latch <= 32, 'latch ' + latch);
  const a = tr.frameAt(tr.nodeS[latch]), b = tr.frameAt(tr.nodeS[latch + feel.get('respawn.aheadNodes')]);
  assert.ok(v3dist(pos(st), a.p) < 1e-3, 'at the anchor');
  const want = v3norm([0, 0, 0], v3reject([0, 0, 0], v3sub([0, 0, 0], b.p, a.p), a.n));
  assert.ok(v3dot(fwd(st), want) > 0.9999, 'facing ahead');
  near(st[S.speedPct], feel.get('respawn.releasePct'), 0.01);
  assert.equal(st[S.respawnPhase], RESPAWN.GRACE);
  // grace: drives again, then ends
  run(st, tr, feel, acts({ throttle: 1 }), feel.get('respawn.graceS') + 0.05, rec);
  assert.equal(st[S.respawnPhase], RESPAWN.NONE);
  assert.equal(st[S.grounded], 1);
  assert.ok(st[S.s] > tr.nodeS[latch] + 10, 'drove on');
});

test('fallY drops at once', () => {
  const feel = makeFeel(), tr = fakeTrack({ fallY: -5 }), rec = recorder();
  const st = place(tr, { speed: 10, height: -4, airborne: true });
  st[S.posY] = -5.5;
  run(st, tr, feel, acts(), 0.1, rec, () => !!rec.first('drop'));
  assert.equal(rec.first('drop').payload.cause, 'fallY');
  near(rec.first('drop').t, DT, 1e-9);
});

test('respawn anchor: a lip or a gap node moves to the first landing node', () => {
  const tr = fakeTrack({ jumps: [{ node: 50, land: 52, airLen: 20, height: 4 }] });
  const st = place(tr, {});
  for (const [latch, want] of [[49, 49], [50, 52], [51, 52], [52, 52], [60, 60]]) {
    st[S.latchSeg] = latch;
    assert.equal(anchorNode(st, tr), want, 'latch ' + latch);
  }
});
