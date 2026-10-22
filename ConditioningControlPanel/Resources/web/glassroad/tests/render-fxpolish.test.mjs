import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  breathe, trailPush, trailLook, trapWarn, lobMark, impactPhase, confettiAt, boxGlint, frostThreshold, shatterPhase,
  spiralOpen, pinkHeart, heartSd, shuffleAt, slotOrbit, nearCapR, impactSeen,
} from '../render/items/fxmath.js';

test('impact glow: only a near, on-screen impact asks photosafe (unseen pops never spend the budget)', () => {
  const o = { maxM: L.impactAskM, vFovDeg: 60, aspect: 16 / 9 };
  assert.ok(L.impactAskM > 0);
  assert.equal(impactSeen(0, 0, -20, 0, 0, -1, o), true, 'ahead and near');
  assert.equal(impactSeen(0, 0, 20, 0, 0, -1, o), false, 'behind the camera');
  assert.equal(impactSeen(0, 0, -300, 0, 0, -1, o), false, '300 m away');
  assert.equal(impactSeen(20, 0, 0.5, 0, 0, -1, o), false, 'off to the side');
  assert.equal(impactSeen(8, 0, -20, 0, 0, -1, o), true, 'inside the wide view');
});
import { ITEM_LOOK as L, SCREEN_LOOK as SL, SLOT_LOOK, ITEM_TIERS } from '../render/items/look.js';
import { FAMILIES, CELLS, FX2_LOOK, hitColours, hitFamily } from '../render/atlas.js';
import { ITEM_IDS } from '../sim/itemTable.js';

const span = (f, from, to, step) => { let lo = Infinity, hi = -Infinity; for (let t = from; t <= to; t += step) { const v = f(t); lo = Math.min(lo, v); hi = Math.max(hi, v); } return [lo, hi]; };

test('fxpolish: the icon breathes slowly round 1, Gentle keeps a third', () => {
  const [lo, hi] = span((t) => breathe(t, 3), 0, 10, 0.01);
  assert.ok(Math.abs(hi - 1 - L.breathAmp) < 1e-3 && Math.abs(1 - lo - L.breathAmp) < 1e-3);
  const [glo, ghi] = span((t) => breathe(t, 3, true), 0, 10, 0.01);
  assert.ok(ghi - glo < (hi - lo) / 2.5);
  assert.ok(L.breathHz <= 0.5, 'a breath, not a pulse');
});

test('fxpolish: a throw ribbon keeps its last points a step apart and starts over after a jump', () => {
  const h = { pts: new Float32Array(4 * 3), n: 0 };
  trailPush(h, 0, 0, 0, 4, 1);
  assert.equal(trailPush(h, 0.5, 0, 0, 4, 1), 1, 'too near: no new point');
  for (let x = 1; x <= 6; x++) trailPush(h, x, 0, 0, 4, 1);
  assert.equal(h.n, 4);
  assert.deepEqual([h.pts[0], h.pts[9]], [3, 6], 'oldest dropped, newest last');
  assert.equal(trailPush(h, 100, 0, 0, 4, 1), 1, 'a jump starts over');
  const tail = trailLook(0, 8, 1), headL = trailLook(7, 8, 1);
  assert.ok(tail.size < headL.size && tail.alpha < headL.alpha);
  assert.equal(trailLook(3, 8, 1.7, 0, true).alpha, trailLook(3, 8, 0.2, 0, true).alpha, 'no twinkle under Gentle');
});

test('fxpolish: a trap warns only as a kart comes near; the lob mark tightens as it falls', () => {
  const [far, near] = L.trapWarnM;
  assert.equal(trapWarn(far + 1), 0);
  assert.equal(trapWarn(near), 1);
  assert.ok(trapWarn((far + near) / 2) > 0 && trapWarn((far + near) / 2) < 1);
  assert.equal(trapWarn(Infinity), 0);
  const a = lobMark(0.1, 0), b = lobMark(0.95, 0);
  assert.ok(b.r < a.r && b.alpha > a.alpha && b.ring < a.ring);
  assert.ok(b.alpha <= L.lobShadowAlpha + 1e-9);
  assert.equal(lobMark(0.5, 0.1, true).ringAlpha, lobMark(0.5, 0.7, true).ringAlpha, 'steady under Gentle');
});

test('fxpolish: an impact rings out and its glow goes; refused = no glow; Gentle rises over 300 ms at half', () => {
  const s = impactPhase(0), m = impactPhase(L.impactRingS / 2), e = impactPhase(L.impactRingS * 1.01);
  assert.ok(m.ring > s.ring && m.ringA > 0 && e.ringA === 0);
  assert.equal(impactPhase(0.05, { glow: false }).glowA, 0);
  assert.ok(impactPhase(0.05).glowA > 0 && impactPhase(L.impactGlowS + 0.01).glowA === 0);
  let peak = 0, peakG = 0;
  for (let t = 0; t < 2; t += 0.005) { peak = Math.max(peak, impactPhase(t).glowA); peakG = Math.max(peakG, impactPhase(t, { gentle: true }).glowA); }
  assert.ok(peak <= L.impactGlowAlpha && peakG <= peak / 2 + 1e-9);
  assert.ok(impactPhase(0.15, { gentle: true }).glowA < impactPhase(0.3, { gentle: true }).glowA, 'still rising at 150 ms');
  assert.ok(L.impactGlowFrac <= 0.25, 'the glow never fills the view');
  assert.ok(nearCapR(L.impactGlowM, 1, 60, L.impactGlowFrac) < L.impactGlowM, 'a glow at the lens is capped');
});

test('fxpolish: confetti bursts up and out, falls, fades by its life; box glints sweep and rest', () => {
  const o = [0, 0, 0];
  confettiAt(3, 0, o);
  assert.deepEqual(o.map((x) => Math.abs(x) < 1e-9), [true, true, true]);
  confettiAt(3, 0.25, o);
  assert.ok(o[1] > 0 && Math.hypot(o[0], o[2]) > 0.2, 'up and out');
  const late = confettiAt(3, L.confettiS * 0.99, [0, 0, 0]);
  assert.ok(late.alpha < 0.1 && confettiAt(3, L.confettiS, o).alpha === 0);
  assert.ok(L.confetti.phone <= L.confetti.high / 2 + 1);
  let on = 0, n = 0;
  for (let t = 0; t < L.glintEveryS * 4; t += 0.01, n++) { const g = boxGlint(t, 2); if (g.alpha > 0) { on++; assert.ok(g.u >= -1 && g.u <= 1); } }
  assert.ok(on / n < 0.3 && on > 0, 'a glint now and then');
  assert.ok(boxGlint(L.glintS / 2, 0, true).alpha <= 0.4 + 1e-9);
});

test('fxpolish: screen effect fronts follow the amount (IN and OUT) and stop halfway under Gentle', () => {
  assert.equal(frostThreshold(0), 1, 'no frost at no amount');
  assert.ok(Math.abs(frostThreshold(1) - SL.frostFrontAt) < 1e-9);
  assert.ok(frostThreshold(0.5) > frostThreshold(1));
  const early = shatterPhase(0.05, 1), mid = shatterPhase(SL.shatterCrackS, 1), late = shatterPhase(2, 1);
  assert.ok(early.crack < mid.crack && early.slide === 0, 'cracks first');
  assert.ok(late.slide === 1 && late.crack === 1, 'then the pieces slide');
  assert.equal(shatterPhase(2, 0.3).slide, 0.3, 'the OUT slides them home with the amount');
  assert.equal(spiralOpen(0), 0);
  assert.ok(spiralOpen(1) >= 1, 'the spiral covers the view at full');
  assert.ok(pinkHeart(0) > pinkHeart(1));
  assert.ok(SL.spiralFullAlpha <= 0.5 && SL.irisRimAlpha <= 0.5 && SL.pinkEdgeAlpha <= 0.5 && SL.shatterCrackDark >= 0.3);
});

test('fxpolish: the heart is inside at its middle, outside far away and beside it', () => {
  assert.ok(heartSd(0, 0.5) < 0);
  assert.ok(heartSd(0.3, 0.8) < 0, 'a lobe');
  assert.ok(heartSd(0, 1.2) > 0, 'the notch');
  assert.ok(heartSd(1.5, 0.5) > 0 && heartSd(0, -0.5) > 0);
});

test('fxpolish: the HUD slot shuffle slides in and settles; the orbit passes behind and in front', () => {
  const s0 = shuffleAt(0), s1 = shuffleAt(SLOT_LOOK.shuffleS);
  assert.ok(s0.dx > 0 && s0.rotDeg > 0 && s0.scale < 1);
  assert.deepEqual([s1.dx, s1.rotDeg, s1.scale], [0, 0, 1]);
  assert.ok(shuffleAt(0, true).dx < s0.dx);
  let front = 0, back = 0;
  for (let i = 0; i < SLOT_LOOK.orbitN; i++) {
    const o = slotOrbit(i, SLOT_LOOK.orbitN, 1.3);
    assert.ok(Math.hypot(o.x, o.y) <= SLOT_LOOK.orbitR * 1.07 + 1e-9 && o.alpha > 0 && o.alpha <= 1);
    if (o.front) front++; else back++;
  }
  assert.ok(front > 0 && back > 0);
  assert.ok(SLOT_LOOK.orbitPhoneN < SLOT_LOOK.orbitN);
});

test('fxpolish: every effect sprays real cells in its own tint; extras halve on the phone tier', () => {
  for (const id of ITEM_IDS) {
    if (!['glass', 'golden'].includes(id)) assert.ok(hitFamily(id), id);
    if (FAMILIES[id]) for (const [c] of FAMILIES[id]) assert.ok(CELLS[c], id + ' ' + c);
  }
  assert.ok(FAMILIES.pink.some(([c]) => c === 'heart') && FAMILIES.shatter.some(([c]) => c === 'shard'));
  const pink = hitColours('pink');
  assert.equal(pink.length, 3);
  for (const c of pink) assert.match(c, /^#[0-9a-f]{6}$/);
  assert.equal(hitColours('nope'), null);
  assert.ok(FX2_LOOK.phoneExtraMul <= 0.5 && FX2_LOOK.emberShare <= 0.5);
  assert.ok(ITEM_TIERS.phone.motes <= ITEM_TIERS.high.motes / 2);
});
