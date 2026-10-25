import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { loadTrack } from '../track/load.js';
import { KART_VIEW, scaledRadius, chaseFraming, nearCuePlan, basisMatrix, NEAR_LOOK } from '../render/viewMath.js';
import { onLoop } from '../render/colossiMath.js';
import { RACER_FRAMING } from '../render/framing.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
const feel = createFeel(feelJson);
const cam = feelJson.camera;
const BEFORE = { eyeUpM: 1.5, eyeBackM: 4.1, lookAheadM: 9, lookUpM: 0.3 };   // P6 preview camera
const BEFORE_SCALE = 0.68;
const ASPECTS = { desk: 16 / 9, portrait: 390 / 844, landscape: 844 / 390 };

test('view: the reference framing opens road-word space while retaining a close chase', () => {
  assert.ok(cam.eyeBackM / 3.4 >= 1.1 && cam.eyeBackM / 3.4 <= 1.2, 'modestly farther than the accepted close camera');
  assert.ok(cam.eyeUpM >= 1.65 && cam.eyeUpM <= 2, 'eye high enough to see the road over the racer');
  assert.ok(cam.eyeBackM > KART_VIEW.modelRadiusM * KART_VIEW.scale * 4, 'eye stays well behind the vessel');
  assert.equal(cam.lookAheadM, BEFORE.lookAheadM, 'keep the same road focus');
  assert.equal(cam.lookUpM, BEFORE.lookUpM);
  assert.equal(feel.get('fov.baseVDeg'), 60, 'size comes from framing, not a narrower FOV');
});

test('view: EMI is more readable, with the complete rest model visible at desktop and phone ratios', () => {
  const now = chaseFraming(cam, feel.get('fov.baseVDeg'), ASPECTS.desk);
  assert.ok(KART_VIEW.scale / BEFORE_SCALE >= 1.1 && KART_VIEW.scale / BEFORE_SCALE <= 1.2, 'modest whole-racer enlargement');
  assert.ok(now.kartW >= 0.13 && now.kartW <= 0.18, 'readable screen width ' + now.kartW);
  assert.ok(Math.abs(now.kartY - 0.62) < 1e-6, 'player centre leaves a little more road above the racer');
  assert.ok(now.horizonY < now.kartY - 0.17, 'road ahead between horizon and kart');

  // This shipped R4 mesh has identity mesh ancestors and float POSITION bounds. Project all eight
  // rest-box corners, including the handle and antenna; the saucer-centre metric alone is smaller.
  const bytes = readFileSync(new URL('../art/models/emi-teacup-kart.glb', import.meta.url));
  const gltf = JSON.parse(bytes.subarray(20, 20 + bytes.readUInt32LE(12)).toString('utf8'));
  const positions = gltf.meshes.flatMap((m) => m.primitives.map((p) => gltf.accessors[p.attributes.POSITION]));
  positions.forEach((a) => assert.ok(a.componentType === 5126 && !a.normalized, 'use decoded float bounds'));
  const lo = [0, 1, 2].map((i) => Math.min(...positions.map((a) => a.min[i])));
  const hi = [0, 1, 2].map((i) => Math.max(...positions.map((a) => a.max[i])));
  for (const [name, a] of Object.entries({ ...ASPECTS, wide: 21 / 9 })) {
    const th = Math.tan(chaseFraming(cam, 60, a).vFov * Math.PI / 360);
    const pitch = Math.atan2(cam.eyeUpM - KART_VIEW.modelUpM * KART_VIEW.scale, cam.eyeBackM)
      - Math.atan((RACER_FRAMING.screenY * 2 - 1) * th);
    for (const x of [lo[0], hi[0]]) for (const y of [lo[1], hi[1]]) for (const z of [lo[2], hi[2]]) {
      const dy = y * KART_VIEW.scale - cam.eyeUpM, dz = cam.eyeBackM - z * KART_VIEW.scale;
      const depth = dz * Math.cos(pitch) - dy * Math.sin(pitch);
      const up = dy * Math.cos(pitch) + dz * Math.sin(pitch);
      const sx = 0.5 + x * KART_VIEW.scale / depth / th / a / 2, sy = 0.5 - up / depth / th / 2;
      assert.ok(depth > 2, name + ' clear of the near plane');
      assert.ok(sx > 0.25 && sx < 0.75, name + ' room beside the whole kart');
      assert.ok(sy > 0.35 && sy < 0.85, name + ' whole kart visible with lower-frame margin: ' + sy);
    }
  }
});

test('view: phones keep the kart readable and inside the frame', () => {
  for (const [name, a] of Object.entries(ASPECTS)) {
    const f = chaseFraming(cam, feel.get('fov.baseVDeg'), a);
    assert.ok(f.kartW > 0.07 && f.kartW < 0.3, name + ' width ' + f.kartW);
    assert.ok(f.kartBottomY < 0.95, name + ' kart bottom inside the frame ' + f.kartBottomY);
    assert.ok(f.horizonY > 0.15 && f.horizonY < 0.5, name + ' horizon ' + f.horizonY);
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
