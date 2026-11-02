// Feel wave 2026-10-26, lane C (render world): render/worldFeel.js rules and the feel.json groups.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  WORLD_FEEL, feelVal, motionMul, skidBudget, debrisAlpha, settleY, createFinalLap, finalLapMods, topSpeedShare,
  easeOutBack, cascadePlan, cascadeFrames, cascadeIn, createFaceGlance,
} from '../render/worldFeel.js';
import { FACE_IDS } from '../render/faces.js';
import { P4_LOOK } from '../render/atlas.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const feel = { get(p) { const [g, k] = p.split('.'); const v = feelJson[g]?.[k]; if (v === undefined) throw new Error('missing ' + p); return v; } };

test('feel.json groups mirror the WORLD_FEEL defaults', () => {
  for (const g of ['permanence', 'speedSense', 'cascade', 'faces']) assert.deepEqual(feelJson[g], JSON.parse(JSON.stringify(WORLD_FEEL[g])), g);
  assert.equal(feelVal({ get() { throw new Error('missing'); } }, 'permanence.skidCapMul', 7), 7);
  assert.equal(feelVal(null, 'x.y', 2), 2);
});

test('motion share: full 1, less half, off 0', () => {
  assert.equal(motionMul('full'), 1); assert.equal(motionMul('less'), 0.5); assert.equal(motionMul('off'), 0); assert.equal(motionMul('??'), 1);
});

test('skids: desktop rings x3 and race-long, phone unchanged', () => {
  const hi = skidBudget(P4_LOOK, 'high', feel), mid = skidBudget(P4_LOOK, 'mid', feel), ph = skidBudget(P4_LOOK, 'phone', feel);
  assert.equal(hi.cap, P4_LOOK.skidCap.high * 3); assert.equal(mid.cap, P4_LOOK.skidCap.mid * 3);
  assert.equal(ph.cap, P4_LOOK.skidCap.phone); assert.equal(ph.lifeS, P4_LOOK.skidLifeS); assert.equal(ph.holdS, P4_LOOK.skidHoldS);
  assert.equal(hi.lifeS, 600);
  // the ease out keeps its length: the fade still runs over the same seconds, only later
  assert.ok(Math.abs((hi.lifeS - hi.holdS) - (P4_LOOK.skidLifeS - P4_LOOK.skidHoldS)) < 1e-9);
});

test('debris settles, stays 20 s, then eases out (no snap)', () => {
  assert.equal(debrisAlpha(0), 1); assert.equal(debrisAlpha(19.9), 1); assert.equal(debrisAlpha(20), 1);
  const a = debrisAlpha(20.5), b = debrisAlpha(21);
  assert.ok(a < 1 && b < a && b > 0); assert.equal(debrisAlpha(21.5), 0); assert.equal(debrisAlpha(-1), 0);
  assert.deepEqual(settleY(-0.2, -3, 0), { y: 0, rest: true });
  assert.deepEqual(settleY(-0.2, 2, 0), { y: -0.2, rest: false });
  assert.deepEqual(settleY(1, -3, 0), { y: 1, rest: false });
});

test('final lap wake: one variable tweened in over 1.5 s, frozen while paused, gone under off', () => {
  const w = createFinalLap(feel);
  w.lap({ final: false }); w.step(1); assert.equal(w.value, 0);
  w.lap({ final: true });
  w.step(0.75); assert.ok(w.value > 0.4 && w.value < 0.6);
  w.step(5, true); assert.ok(w.raw < 0.51);
  w.step(0.75); assert.equal(w.value, 1);
  w.setMotion('less'); assert.equal(w.value, 0.5);
  w.setMotion('off'); assert.equal(w.value, 0);
  w.reset(); w.setMotion('full'); assert.equal(w.value, 0);
  const m = finalLapMods(1, feel), z = finalLapMods(0, feel);
  assert.equal(m.particleMul, 1.5); assert.equal(z.particleMul, 1); assert.ok(m.fieldShift > 0 && z.fieldShift === 0); assert.ok(m.reactiveMul > 1);
});

test('top-speed streaks only near the top, scaled by motion', () => {
  assert.equal(topSpeedShare(0.5, feel), 0); assert.equal(topSpeedShare(0.85, feel), 0);
  assert.equal(topSpeedShare(1, feel), 1); assert.equal(topSpeedShare(1, feel, 'less'), 0.5); assert.equal(topSpeedShare(1, feel, 'off'), 0);
});

test('cascade: 40-90 ms random stagger, easeOutBack, less reduced, off instant', () => {
  assert.ok(Math.abs(easeOutBack(0)) < 1e-9); assert.ok(Math.abs(easeOutBack(1) - 1) < 1e-9);
  assert.ok(Math.max(...Array.from({ length: 50 }, (_, i) => easeOutBack(i / 49))) > 1);   // it overshoots
  let r = 0; const rnd = () => (r = (r + 0.37) % 1);
  const full = cascadePlan(6, 'full', rnd, feel);
  assert.equal(full.delays[0], 0);
  for (let i = 1; i < 6; i++) { const gap = full.delays[i] - full.delays[i - 1]; assert.ok(gap >= 39 && gap <= 91, String(gap)); }
  const less = cascadePlan(6, 'less', () => 0.5, feel), fl = cascadePlan(6, 'full', () => 0.5, feel);
  assert.ok(less.delays[5] < fl.delays[5] && less.riseY < fl.riseY && less.overshoot === 0);
  const off = cascadePlan(6, 'off', rnd, feel);
  assert.equal(off.durMs, 0); assert.ok(off.delays.every((d) => d === 0));
  const frames = cascadeFrames(fl);
  assert.equal(frames[0].opacity, 0); assert.equal(frames.at(-1).opacity, 1); assert.equal(frames.at(-1).transform, 'translateY(0.00px)');
  // DOM: animations end with no fill, so hover / selected CSS takes over again; off runs nothing
  const calls = []; const el = () => ({ animate(f, o) { calls.push(o); } });
  assert.equal(cascadeIn([el(), el(), {}], 'full', { feel }), 2);
  assert.ok(calls.every((o) => o.fill === 'backwards'));
  assert.equal(cascadeIn([el()], 'off', { feel }), 0);
  assert.equal(cascadeIn(null), 0);
});

test('rival faces: glance / grin 0.8 s, one change per racer per 2 s', () => {
  assert.ok(FACE_IDS.includes(WORLD_FEEL.faces.passer) && FACE_IDS.includes(WORLD_FEEL.faces.passed));
  const g = createFaceGlance(feel);
  assert.equal(g.overtake({ racer: 2, passed: 3 }, 10), 2);
  assert.equal(g.face(2, 10.5), 'grin'); assert.equal(g.face(3, 10.5), 'determined');
  assert.equal(g.face(2, 10.81), null);
  assert.equal(g.overtake({ racer: 3, passed: 2 }, 11), 0);         // inside the cooldown: no change
  assert.equal(g.face(3, 11.1), null);
  assert.equal(g.overtake({ racer: 3, passed: 2 }, 12.01), 2);
  assert.equal(g.face(3, 12.2), 'grin');
  g.reset(); assert.equal(g.face(3, 12.2), null);
});
