import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createState, S, BOOST } from '../sim/state.js';
import { LOOK } from '../render/look.js';
import { TIERS } from '../render/quality.js';
import { createPose, stepPose, leanTarget, kickWobble, wobbleAt, kickSquash, tintRgb, tagAlpha, sparkProfile, moteDensity, bumpCount } from '../render/kartPose.js';

const run = (pose, st, s, opts = {}) => { for (let t = 0; t < s; t += 1 / 60) stepPose(pose, st, 1 / 60, LOOK, opts); return pose; };

test('render: karts lean into the turn, clamped, more in a drift, none when stopped', () => {
  assert.ok(leanTarget(2, 1, 0, LOOK) < 0, 'right turn rolls the right side down');
  assert.ok(leanTarget(-2, 1, 0, LOOK) > 0);
  assert.equal(leanTarget(50, 1, 0, LOOK), -LOOK.leanMaxRad);
  assert.equal(Math.abs(leanTarget(2, 0, 0, LOOK)), 0);
  assert.ok(leanTarget(2, 1, 1, LOOK) < leanTarget(2, 1, 0, LOOK));
  const st = createState(); st[S.yawRate] = 2; st[S.speedPct] = 1;
  const p = run(createPose(), st, 1);
  assert.ok(Math.abs(p.roll - leanTarget(2, 1, 0, LOOK)) < 1e-3);
});

test('render: a hit wobble sways and dies; Gentle makes it smaller and slower', () => {
  const p = createPose(), g = createPose();
  kickWobble(p, LOOK, false); kickWobble(g, LOOK, true);
  assert.ok(g.wobAmp < p.wobAmp && g.wobHz < p.wobHz);
  let peak = 0;
  const st = createState();
  for (let t = 0; t < 3; t += 1 / 60) { stepPose(p, st, 1 / 60, LOOK); peak = Math.max(peak, Math.abs(p.roll)); }
  assert.ok(peak > 0.05 && peak <= LOOK.wobbleRad);
  assert.equal(p.roll, 0); assert.equal(p.wobT, -1);
  assert.equal(wobbleAt(-1, 1, 3, 0.3), 0);
});

test('render: a rival squash peaks at once and is gone in botSquashGoneS; the player follows fx.squash', () => {
  const p = createPose(), st = createState();
  kickSquash(p, 0.08);
  stepPose(p, st, 1 / 60, LOOK);
  assert.ok(p.sq > 0.06);
  run(p, st, LOOK.botSquashGoneS + 0.05);
  assert.equal(p.sq, 0);
  stepPose(p, st, 1 / 60, LOOK, { squash: -0.04 });
  assert.equal(p.sq, -0.04);
});

test('render: arms go up while a boost runs; Shatter spin reaches the body yaw', () => {
  const st = createState(); st[S.boostKind] = BOOST.PAD; st[S.grounded] = 1;
  const p = run(createPose(), st, 1.5, { spinRad: 1.2 });
  assert.ok(p.arms > LOOK.armsUpRad * 0.95 && p.pitch > 0);
  assert.ok(Math.abs(p.yawAdd - 1.2) < 1e-9);
});

test('render: tints keep the player white, rivals distinct; tags fade by 40 m', () => {
  assert.deepEqual(tintRgb(0, LOOK), [1, 1, 1]);
  const all = Array.from({ length: 8 }, (_, i) => tintRgb(i, LOOK).map((v) => v.toFixed(3)).join());
  assert.equal(new Set(all).size, 8);
  for (const c of all.flatMap((x) => x.split(',')).map(Number)) assert.ok(c >= 0 && c <= 1);
  assert.equal(tagAlpha(5, LOOK), 1);
  assert.equal(tagAlpha(LOOK.tagNearM + 1, LOOK), 0);
  assert.ok(tagAlpha(LOOK.tagNearM - 2, LOOK) > 0 && tagAlpha(LOOK.tagNearM - 2, LOOK) < 1);
});

test('render: spark ladder climbs per tier; motes thicken in the deep, thin under Gentle; bump sparks by dv', () => {
  const [a, b, c] = [1, 2, 3].map((t) => sparkProfile(t, LOOK));
  assert.ok(a.rate < b.rate && b.rate < c.rate && a.size < c.size && a.petal <= c.petal);
  assert.equal(sparkProfile(9, LOOK).rate, c.rate);
  assert.equal(sparkProfile(2, LOOK, 0.5).rate, b.rate / 2);
  assert.ok(moteDensity(1, LOOK, false) > moteDensity(0, LOOK, false));
  assert.ok(moteDensity(1, LOOK, true) < moteDensity(1, LOOK, false));
  assert.ok(moteDensity(1, LOOK, false) <= 1);
  assert.ok(bumpCount(0, LOOK) === LOOK.bumpSparks[0] && bumpCount(99, LOOK) === LOOK.bumpSparks[1]);
});

test('render: per-tier budgets: phone about half of mid, high the most', () => {
  for (const k of ['particles', 'motes', 'trailPoints', 'kartLodM']) {
    assert.ok(TIERS.high[k] >= TIERS.mid[k] && TIERS.mid[k] > TIERS.phone[k], k);
  }
  assert.ok(TIERS.phone.particles <= TIERS.mid.particles * 0.5 && TIERS.phone.motes <= TIERS.mid.motes * 0.55);
});
