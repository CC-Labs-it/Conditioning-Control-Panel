/* tests/sim-hold-p6.test.mjs - polish 6 SIM2: the dare sits on the OUTSIDE of the coil's r 20 first turn. Two
 * pieces keep a kart that is not going for it out of it: the opening holds a grounded kart turning away from it
 * (sim/routes.js turnsAway, sim/kart.js) and the Steer assist eases off for a bend it cannot take (sim/assist.js EASE).
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { S, BOOST, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { turnsAway } from '../sim/routes.js';
import { applyBoost } from '../sim/boost.js';
import { createAssist } from '../sim/assist.js';
import { createActions } from '../core/actions.js';
import { qfromBasis } from '../core/math.js';
import { gap } from '../sim/gaps.js';
import { makeFeel, DT } from './sim-fake.mjs';

// The original r20 knee remains a demanding braking-law regression after the live road broadens.
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')), { courseShape: false });
const R0 = track.routes[0];

function kartAt(s, lat, v) {
  const st = createState(), f = track.frameAt(s);
  setPos(st, [f.p[0] + f.b[0] * lat, f.p[1] + f.b[1] * lat, f.p[2] + f.b[2] * lat]);
  setQuat(st, qfromBasis([0, 0, 0, 1], f.b, f.n, [-f.t[0], -f.t[1], -f.t[2]]));
  setVel(st, [f.t[0] * v, f.t[1] * v, f.t[2] * v]);
  st[S.engineV] = v; st[S.grounded] = 1; st[S.seg] = f.seg; st[S.latchSeg] = f.seg; st[S.s] = s; st[S.lateral] = lat; st[S.hopT] = 1e3;
  return st;
}

test('hold: the opening holds a kart turning away from it, never one steering for it or running straight', () => {
  const feel = makeFeel(), st = createState(), hold = gap(feel, 'route.holdSteer');
  assert.ok(hold > 0 && hold < 0.5, 'a quarter lock or so ' + hold);
  // left side of the road (lat < 0): turning right (+) is away
  st[S.steerSlew] = 1; assert.equal(turnsAway(st, -6, feel), true);
  st[S.steerSlew] = hold; assert.equal(turnsAway(st, -6, feel), true, 'at the threshold');
  st[S.steerSlew] = hold * 0.8; assert.equal(turnsAway(st, -6, feel), false, 'a light touch is not turning away');
  st[S.steerSlew] = 0; assert.equal(turnsAway(st, -6, feel), false, 'straight at it goes through');
  st[S.steerSlew] = -1; assert.equal(turnsAway(st, -6, feel), false, 'steering for it goes through');
  // right side mirrors
  st[S.steerSlew] = -1; assert.equal(turnsAway(st, 6, feel), true);
  st[S.steerSlew] = 1; assert.equal(turnsAway(st, 6, feel), false);
  // a drift's direction wins over the stick (a drift eased toward the outside still turns in)
  st[S.steerSlew] = -0.3; st[S.driftDir] = 1; assert.equal(turnsAway(st, -6, feel), true);
  st[S.steerSlew] = 0.3; st[S.driftDir] = -1; assert.equal(turnsAway(st, -6, feel), false);
});

function ease({ s, v = 30, mode = 'light', boosted = false, brake = 0, steer = 0 }) {
  const feel = makeFeel(), st = kartAt(s, 0, v), a = createActions();
  if (boosted) applyBoost(st, BOOST.PAD, 0, feel, { emit() {} }, { amount: 0.25, holdS: 6 });
  Object.assign(a, { steer, throttle: 1, brake, hop: false, hopPressed: false, item: false, back: false });
  const out = createAssist(track, feel, { mode }).apply(a, st, DT, 'racing');
  return { throttle: out.throttle, brake: out.brake, steer: out.steer };
}

test('ease: throttle held at top speed into the coil, the assist lifts and dabs the brake; Strong dabs harder', () => {
  const s = R0.openS0 - 45;                         // the coil's first turn (r 20) is just ahead
  const light = ease({ s }), strong = ease({ s, mode: 'strong' });
  assert.ok(light.throttle < 0.5, 'light lifts ' + light.throttle.toFixed(2));
  assert.ok(light.brake > 0 && light.brake <= 0.25 + 1e-9, 'light dabs ' + light.brake.toFixed(2));
  assert.ok(strong.brake >= light.brake && strong.brake <= 0.5, 'strong dabs ' + strong.brake.toFixed(2));
  // a slow kart there is under the bend's limit: nothing changes
  const slow = ease({ s, v: 10 });
  assert.equal(slow.throttle, 1); assert.equal(slow.brake, 0);
});

test('ease: never with the assist off, under a boost, while the player brakes, or on the start straight', () => {
  const s = R0.openS0 - 45;
  const off = ease({ s, mode: 'off' });
  assert.equal(off.throttle, 1); assert.equal(off.brake, 0);
  const boosted = ease({ s, boosted: true });
  assert.equal(boosted.throttle, 1, 'a boost cannot be braked out of: no lift');
  const braking = ease({ s, brake: 1 });
  assert.equal(braking.brake, 1, 'the player brakes: theirs as it is');
  const straight = ease({ s: 5, v: 26 });
  assert.equal(straight.throttle, 1); assert.equal(straight.brake, 0);
});

test('ease: a player steering for the dare keeps the full throttle', () => {
  const side = R0.side === 'L' ? -1 : 1;
  const r = ease({ s: R0.openS0 - 20, steer: 0.8 * side });
  assert.equal(r.throttle, 1, 'going for it: no lift');
  assert.equal(r.steer, 0.8 * side);
});
