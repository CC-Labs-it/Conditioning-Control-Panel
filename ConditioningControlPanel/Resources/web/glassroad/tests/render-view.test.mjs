import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { loadTrack } from '../track/load.js';
import { KART_VIEW, scaledRadius, chaseFraming, nearCuePlan, basisMatrix, NEAR_LOOK } from '../render/viewMath.js';
import { onLoop } from '../render/colossiMath.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const feel = createFeel(feelJson);
const cam = feelJson.camera;
const BEFORE = { eyeUpM: 2.2, eyeBackM: 6.5, lookAheadM: 8, lookUpM: 0.6 };   // polish 5 camera
const ASPECTS = { desk: 16 / 9, portrait: 390 / 844, landscape: 844 / 390 };

test('view: the chase camera sits closer and lower than polish 5', () => {
  assert.ok(cam.eyeBackM < BEFORE.eyeBackM - 1, 'closer');
  assert.ok(cam.eyeUpM < BEFORE.eyeUpM - 0.3, 'lower');
  assert.ok(cam.eyeUpM > 1.2 && cam.eyeBackM > 3.4, 'still a chase view, never inside the kart');
});

test('view: the kart is smaller against the world and holds its size on screen', () => {
  const was = chaseFraming(BEFORE, 60, ASPECTS.desk, { scale: 1 });
  const now = chaseFraming(cam, feel.get('fov.baseVDeg'), ASPECTS.desk);
  // world scale: the kart shrank more than the camera came in, relative to road and props
  const worldGain = BEFORE.eyeBackM / cam.eyeBackM;
  assert.ok(KART_VIEW.scale < 0.85 && KART_VIEW.scale >= 0.6, 'a clear shrink that keeps the rider readable');
  assert.ok(KART_VIEW.scale / worldGain < 0.6, 'kart against the world');
  // on screen: about the same width (never more than 15% larger), lower in frame, under the horizon
  assert.ok(now.kartW <= was.kartW * 1.15 && now.kartW >= was.kartW * 0.8, 'width ' + now.kartW + ' vs ' + was.kartW);
  assert.ok(now.kartY > was.kartY, 'lower in frame');
  assert.ok(now.horizonY < now.kartY - 0.15, 'road ahead between horizon and kart');
});

test('view: phones keep the kart readable and inside the frame', () => {
  for (const [name, a] of Object.entries(ASPECTS)) {
    const f = chaseFraming(cam, feel.get('fov.baseVDeg'), a);
    assert.ok(f.kartW > 0.07 && f.kartW < 0.3, name + ' width ' + f.kartW);
    assert.ok(f.kartBottomY < 0.95, name + ' kart bottom inside the frame ' + f.kartBottomY);
    assert.ok(f.horizonY > 0.25 && f.horizonY < 0.5, name + ' horizon ' + f.horizonY);
  }
});

test('view: collision sizes follow the kart scale', () => {
  const r = KART_VIEW.modelRadiusM * KART_VIEW.scale;
  assert.ok(Math.abs(feel.get('contact.radiusM') - r) < 0.1, 'contact radius ' + feel.get('contact.radiusM') + ' vs visual ' + r);
  assert.equal(scaledRadius(0.8, 0.68), 0.54);
  assert.ok(feel.get('contact.maxHeightM') <= 1.5 * KART_VIEW.scale + 0.05, 'contact height follows the kart height');
});

const plain = JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8'));
const track = loadTrack(plain, { validate: false });

test('view: near cues stand only beside a railed, upright, closed edge', () => {
  const plan = nearCuePlan(track);
  assert.ok(plan.length > 40, 'posts along the lap: ' + plan.length);
  for (const c of plan) {
    const f = track.frameAt(c.s);
    const flags = f.flags || [];
    assert.ok(flags.includes('rail') || flags.includes(c.side > 0 ? 'railR' : 'railL'), 'railed at ' + c.s);
    for (const x of NEAR_LOOK.skipFlags) assert.ok(!flags.includes(x), x + ' at ' + c.s);
    assert.ok(f.n[1] >= NEAR_LOOK.minUpY, 'upright at ' + c.s);
    // the foot sits just outside the road edge, on the right side
    const d = [c.p[0] - f.p[0], c.p[1] - f.p[1], c.p[2] - f.p[2]];
    const lat = d[0] * f.b[0] + d[1] * f.b[1] + d[2] * f.b[2];
    assert.ok(Math.abs(Math.abs(lat) - (f.w / 2 + NEAR_LOOK.outM)) < 1e-6 && Math.sign(lat) === c.side);
    // x points back at the road, the basis is right handed (no mirrored instance)
    assert.ok(c.x[0] * d[0] + c.x[1] * d[1] + c.x[2] * d[2] < 0);
    const z = [c.x[1] * c.y[2] - c.x[2] * c.y[1], c.x[2] * c.y[0] - c.x[0] * c.y[2], c.x[0] * c.y[1] - c.x[1] * c.y[0]];
    assert.ok(Math.abs(z[0] - c.z[0]) + Math.abs(z[1] - c.z[1]) + Math.abs(z[2] - c.z[2]) < 1e-9);
    assert.match(c.hex, /^#[0-9a-f]{6}$/i);
  }
  for (const r of track.routes || []) assert.ok(!plan.some((c) => c.s > r.openS0 && c.s < r.openS1), 'none over a route opening');
  // polish 6: none round a vertical loop (the wheel stands over its entry and exit straights)
  assert.ok(track.nodeFlags && [...Array(track.nodeCount).keys()].some((i) => track.nodeFlags(i).includes('magnet')), 'threshold has a loop');
  for (const c of plan) assert.ok(!onLoop(track, c.s, NEAR_LOOK.loopPadM), 'a post by the loop at ' + c.s.toFixed(0));
});

test('view: near cues thin on the phone tier, banners on every other post', () => {
  const hi = nearCuePlan(track), ph = nearCuePlan(track, { phone: true });
  assert.ok(ph.length < hi.length * 0.6 && ph.length > 0);
  const b = hi.filter((c) => c.banner).length;
  assert.ok(b >= hi.length / 2 - 2 && b <= hi.length / 2 + 2);
});

test('view: near cues stay clear of the chase eye', () => {
  const L = NEAR_LOOK;
  // a drift bias swings the eye sideways by eyeBack * sin(bias); the pole stands further out than that past the rail
  const swing = cam.eyeBackM * Math.sin(cam.driftBiasOutDeg * Math.PI / 180);
  assert.ok(L.outM > swing, 'pole ' + L.outM + ' vs eye swing ' + swing);
  // whatever hangs over the road (banner, lamp) has its lower edge well above the eye
  assert.ok(L.poleM - L.bannerDropM - L.bannerH > cam.eyeUpM + 0.6, 'banner bottom');
  assert.ok(L.poleM - 0.08 - L.lampR * 1.6 > cam.eyeUpM + 0.6, 'lamp bottom');
});

test('view: basisMatrix writes a column major transform', () => {
  const m = basisMatrix(new Array(16), [1, 0, 0], [0, 1, 0], [0, 0, 1], [3, 4, 5]);
  assert.deepEqual(m, [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 3, 4, 5, 1]);
});
