// tests/render-particles4.test.mjs - polish 4 PARTICLES lane: the depth speed field rules, skid marks
// (fade, ring buffer, wheel steps, the skid flag), tier colours and the burst numbers.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createState, S, BOOST, RESPAWN } from '../sim/state.js';
import {
  P4_LOOK as P, p4Look, tierColour, hueOf, fieldBirth, fieldPoint, fieldDensity, streakLen, boostTarget, skidOn, skidFade,
  createSkidBuffer, stepWheel, trickKey,
} from '../render/atlas.js';

const src = (f) => readFileSync(new URL('../render/' + f, import.meta.url), 'utf8');

// ---- fixer: the lens rule (render/lensMath.js) ----
import { LENS, lensCapSize, lensAlphaAt } from '../render/lensMath.js';
import { AURA_LOOK } from '../render/items/look.js';

test('lens: a sprite within 8 m spans at most about 5.5 percent of the view; rivals 10 to 40 m out stay big', () => {
  const vh = 2 * Math.tan(35 * Math.PI / 180);   // a 70 degree view
  for (const d of [0.5, 2, 4, 6, 8]) assert.ok(lensCapSize(50, d, vh) / (d * vh) <= LENS.nearFrac + 1e-9, 'd ' + d);
  for (const d of [16, 25, 40]) assert.ok(lensCapSize(50, d, vh) / (d * vh) >= 0.15, 'rival at ' + d);
  assert.equal(lensCapSize(0.3, 30, vh), 0.3, 'a small sprite far out keeps its size');
  assert.equal(lensAlphaAt(0.5), 0, 'gone at the lens');
  assert.ok(lensAlphaAt(3) < 0.75 && lensAlphaAt(10) === 1);
  assert.ok(LENS.playerBurstMul <= 0.5, 'the player burst is at most half');
});

test('lens: the player aura is a see-through shell; speed streaks are short dashes', () => {
  assert.ok(AURA_LOOK.playerAlpha <= 0.35 && AURA_LOOK.alpha < 1);
  assert.ok(streakLen(1, 1, 1) <= 2.5, 'longest streak ' + streakLen(1, 1, 1));
  assert.ok(P.fieldShare.streak < P.fieldShare.glitter + P.fieldShare.orb);
  for (const f of ['particles.js', 'items/materials.js']) assert.ok(/lensScale\(/.test(src(f)) && /lensAlpha\(/.test(src(f)), f + ' applies the lens rule');
});
const tanV = Math.tan((62 / 2) * Math.PI / 180), tanH = tanV * 16 / 9;

test('p4: drift tiers wear the HUD pip fills in the kit order, pink, white, gold; a slide with no tier is pale', () => {
  const kit = readFileSync(new URL('../hud/kit.js', import.meta.url), 'utf8').match(/pips: \[([^\]]+)\]/)[1].match(/drift-pip-\w+/g);
  const pip = (id) => readFileSync(new URL('../art/ui/' + id + '.svg', import.meta.url), 'utf8').match(/fill="(#[0-9a-f]{6})"/i)[1].toLowerCase();
  assert.equal(kit.length, 3);
  assert.deepEqual([1, 2, 3].map((t) => tierColour(t).toLowerCase()), kit.map(pip), 'sparks match the pip each tier lights');
  const [p, g] = [1, 3].map((t) => hueOf(tierColour(t)));
  assert.ok(p > 300 && p < 345, 'pink ' + p);
  assert.ok(g > 30 && g < 55, 'gold ' + g);
  assert.equal(tierColour(0), P.slideColour);
  assert.equal(tierColour(7), tierColour(3));
  assert.equal(p4Look({ p4: { slideColour: '#000000' } }).slideColour, '#000000');
});

test('p4 field: born 8 to 40 m ahead and never inside the centre third, whatever the curve', () => {
  for (let i = 0; i < 400; i++) {
    const e = fieldBirth(Math.random(), Math.random(), Math.random(), i & 1 ? 1 : -1);
    assert.ok(e.depth >= 8 && e.depth <= 40 && Math.abs(e.nx) >= 1 / 3);
    for (const curve of [-0.05, -0.01, 0, 0.02, 0.05]) {
      for (const t of [0, 0.3, 0.7, 1]) for (const back of [0, 5]) {
        const q = fieldPoint(e, t, back, curve, tanH, tanV);
        const ndc = q.x / (-q.z * tanH);
        assert.ok(Math.abs(ndc) >= 1 / 3 - 1e-9, 'centre third ' + ndc);
        assert.ok(Math.sign(ndc) === Math.sign(e.nx), 'keeps its side');
        assert.ok(-q.z >= P.fieldEndM - 1e-9, 'never nearer than the end depth');
      }
    }
  }
});

test('p4 field: elements only move outward on screen as they stream past (parallax, no crossing)', () => {
  const e = fieldBirth(0.1, 0.5, 0.8, -1);
  let last = 0;
  for (let t = 0; t <= 1; t += 0.05) {
    const q = fieldPoint(e, t, 0, 0, tanH, tanV), ndc = Math.abs(q.x / (-q.z * tanH));
    assert.ok(ndc >= last - 1e-9, 'outward');
    last = ndc;
  }
  assert.ok(last > 1, 'it has left the screen by the end of its run');
  // a near element crosses the screen faster than a far one: parallax
  const near = fieldBirth(0.5, 0.5, 0, 1), far = fieldBirth(0.5, 0.5, 1, 1);
  const sweep = (x) => { const a = fieldPoint(x, 0, 0, 0, tanH, tanV), b = fieldPoint(x, 0, 2, 0, tanH, tanV); return Math.abs(a.x / a.z - b.x / b.z); };
  assert.ok(sweep(near) > sweep(far) * 3);
});

test('p4 field: density and streak length grow with speed and boost; tier boosts climb', () => {
  assert.equal(fieldDensity(0.2, 0), 0);
  assert.ok(fieldDensity(0.7, 0) > fieldDensity(0.55, 0));
  assert.equal(fieldDensity(1, 0), P.fieldCruise, 'cruising keeps room for the boost');
  assert.equal(fieldDensity(1, 1), 1);
  assert.ok(fieldDensity(0.2, 0.6) >= 0.6, 'a boost shows the field at any speed');
  assert.ok(streakLen(0.5, 1, 0) > streakLen(0.5, 0.5, 0));
  assert.ok(streakLen(0.5, 1, 1) > streakLen(0.5, 1, 0) * 1.5);
  assert.equal(boostTarget(BOOST.NONE, BOOST), 0);
  const d = [BOOST.DRIFT1, BOOST.DRIFT2, BOOST.DRIFT3].map((k) => boostTarget(k, BOOST));
  assert.ok(d[0] < d[1] && d[1] < d[2] && d[2] === 1);
  assert.equal(boostTarget(BOOST.PAD, BOOST), 1);
});

test('p4 skids: the flag reads the sim field when it exists, else a grounded drift', () => {
  const st = createState();
  st[S.driftDir] = 1; st[S.grounded] = 1;
  assert.equal(skidOn(st, S), true);
  st[S.airborne] = 1; assert.equal(skidOn(st, S), false, 'not in the air');
  st[S.airborne] = 0; st[S.respawnPhase] = RESPAWN.PULL; assert.equal(skidOn(st, S), false, 'not in a rescue');
  st[S.respawnPhase] = 0; st[S.driftDir] = 0; assert.equal(skidOn(st, S), false);
  const S2 = { ...S, skid: S.boostFromV + 1 }, st2 = new Float32Array(S2.skid + 1);
  st2[S2.skid] = 1; assert.equal(skidOn(st2, S2), true, 'the sim flag wins');
  st2[S2.skid] = 0; st2[S2.driftDir] = 1; st2[S2.grounded] = 1; assert.equal(skidOn(st2, S2), false);
});

test('p4 skids: marks rise fast, hold, then ease out by 3 to 4 s', () => {
  assert.equal(skidFade(0), 0);
  assert.equal(skidFade(-1), 0);
  assert.equal(skidFade(P.skidHoldS * 0.5), 1);
  assert.ok(P.skidLifeS >= 3 && P.skidLifeS <= 4);
  assert.equal(skidFade(P.skidLifeS), 0);
  let last = 1;
  for (let a = P.skidHoldS; a < P.skidLifeS; a += 0.1) { const f = skidFade(a); assert.ok(f <= last + 1e-9); last = f; }
  assert.ok(P.skidRiseS >= 0.03, 'no one-frame pop');
});

test('p4 skids: the ring buffer lays quads and reuses the oldest; clear hides everything', () => {
  const b = createSkidBuffer(3);
  const i = b.lay([0, 0, 0], [0, 0, 1], [1, 0, 0], [1, 0, 0], 1, 2, 0.1, [1, 0.5, 0]);
  assert.equal(i, 0);
  assert.deepEqual(Array.from(b.pos.slice(0, 12)).map((x) => Math.round(x * 100) / 100), [-0.1, 0, 0, 0.1, 0, 0, -0.1, 0, 1, 0.1, 0, 1]);
  assert.deepEqual([b.meta[0], b.meta[2], b.meta[4], b.meta[6]], [1, 1, 2, 2]);
  assert.deepEqual([b.meta[1], b.meta[3]], [-1, 1]);
  b.lay([0, 0, 0], [0, 0, 1], [1, 0, 0], [1, 0, 0], 3, 4, 0.1, [1, 1, 1]);
  b.lay([0, 0, 0], [0, 0, 1], [1, 0, 0], [1, 0, 0], 5, 6, 0.1, [1, 1, 1]);
  assert.equal(b.lay([0, 0, 0], [0, 0, 1], [1, 0, 0], [1, 0, 0], 7, 8, 0.1, [1, 1, 1]), 0, 'wraps to the oldest');
  assert.equal(b.meta[0], 7);
  assert.equal(b.index.length, 18);
  b.clear();
  assert.ok(b.meta[0] < -1e5 && b.laid === 0);
  assert.ok(P.skidCap.phone <= P.skidCap.high / 2 && P.skidCap.mid < P.skidCap.high);
});

test('p4 skids: a wheel lays a segment every step, breaks when off or after a jump', () => {
  const w = {}, R = [1, 0, 0];
  assert.equal(stepWheel(w, [0, 0, 0], R, true, 0), null, 'first point only remembers');
  assert.equal(stepWheel(w, [0, 0, P.skidStepM * 0.5], R, true, 0.01), null, 'too short');
  const s = stepWheel(w, [0, 0, P.skidStepM * 1.2], R, true, 0.02);
  assert.ok(s && s.a[2] === 0 && s.ta === 0);
  assert.equal(stepWheel(w, [0, 0, 5], R, false, 0.1), null);
  assert.equal(w.has, false);
  stepWheel(w, [0, 0, 5], R, true, 0.2);
  assert.equal(stepWheel(w, [0, 0, 5 + P.skidBreakM + 1], R, true, 0.3), null, 'a teleport starts fresh');
  assert.ok(stepWheel(w, [0, 0, 5 + P.skidBreakM + 2], R, true, 0.4));
});

test('p4 bursts: tier sparks, the mini-turbo and tricks grow; great beats good', () => {
  for (const k of ['sparkTierSize', 'sparkTierRate', 'turboCount', 'turboSpeedMps', 'turboRingM']) {
    const a = P[k]; assert.ok(a[0] <= a[1] && a[1] < a[2], k);
  }
  assert.ok(P.sparkTierStars[0] === 0 && P.sparkTierStars[2] > 0);
  assert.ok(P.trickCount.great > P.trickCount.good && P.trickLandCount.great > P.trickLandCount.good && P.trickRingM.great > P.trickRingM.good);
  assert.equal(trickKey('great'), 'great'); assert.equal(trickKey('ok'), 'good'); assert.equal(trickKey(undefined), 'good');
});

test('p4 sources: the field is camera space with the centre clamp; particles take the FEEL events by name', () => {
  const sl = src('speedlines.js');
  assert.match(sl, /cameraWorldMatrix/);
  assert.match(sl, /fieldCentre/);
  assert.doesNotMatch(sl, /lineRadiusM/, 'no cylinder round the kart any more');
  const pa = src('particles.js');
  for (const name of ['drift-start', 'drift-tier', 'drift-boost', 'trick', 'trick-land']) assert.ok(pa.includes("'" + name + "'"), name);
  assert.match(pa, /evBoost/); assert.match(pa, /evTier/);
  const sk = src('skids.js');
  assert.match(sk, /createSkidBuffer/); assert.match(sk, /skidOn/);
  for (const f of ['speedlines.js', 'particles.js', 'skids.js', 'wakes.js']) {
    const s = src(f);
    assert.ok(!/[–—]/.test(s), f + ' has no en or em dashes');
  }
});
