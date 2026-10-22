// tests/render-props.test.mjs - polish 3 PROPS lane: the floating world reads from the kart camera.
// Scatter counts per tier, the placement corridors (spiral gates, media tunnel, billboards), the
// corridor the camera sees, sizes, poses, saturated colours, the haze, and colossi that show their tops.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import {
  worldLook, sampleTrack, placeColossi, placeScatter, scatterCount, corridorHit, freeSpans, scatterBasis,
  propColour, hexOf, hueSat, localOf, V3,
} from '../render/colossiMath.js';

const rd = (p) => JSON.parse(readFileSync(new URL(p, import.meta.url), 'utf8'));
const tr = loadTrack(rd('../tracks/threshold.json'));
const layout = rd('../tracks/threshold.colossi.json');
const manifest = rd('../art/manifest.json');
const rowOf = (id) => manifest.find((r) => r.id === id) || null;
const W = worldLook(), L = tr.lengthM;
const samples = sampleTrack(tr, W.stepM);
const placed = placeColossi(layout.colossi, tr, rowOf, W, samples);
const kinds = W.scatterKinds.filter((k) => rowOf(k.id));
const sectionOf = (s) => tr.sections[tr.sectionAt(s)].palette;
const lay = (tier, seed = 7) => placeScatter(tr, kinds, rowOf, seed, { want: scatterCount(tier, W), avoid: placed, W, samples, sectionOf });

test('props: scatter counts per tier (about 70 high, 45 mid, 25 phone) and every tier fills', () => {
  assert.equal(scatterCount('high', W), 70);
  assert.equal(scatterCount('mid', W), 45);
  assert.equal(scatterCount('phone', W), 25);
  assert.equal(scatterCount('nope', W), 70);
  for (const tier of ['high', 'mid', 'phone']) for (const seed of [1, 7, 4242]) {
    const n = lay(tier, seed).length, want = scatterCount(tier, W);
    assert.ok(n >= Math.floor(want * 0.9) && n <= want, tier + ' seed ' + seed + ' placed ' + n + ' of ' + want);
  }
});

test('props: the corridor windows match the brief and nothing sits in one', () => {
  const f = (x) => x * L;
  for (const g of [0.14, 0.31, 0.47, 0.69, 0.86]) {
    assert.equal(corridorHit(f(g), L, W), 'gate');
    assert.equal(corridorHit(f(g) + 19, L, W), 'gate');
  }
  assert.equal(corridorHit(f(0.58), L, W), 'tunnel');
  assert.equal(corridorHit(f(0.55) - 15, L, W), 'tunnel');
  for (const b of [0.06, 0.2, 0.25, 0.38, 0.42, 0.52, 0.64, 0.76, 0.8, 0.92]) assert.equal(corridorHit(f(b) + 7, L, W), 'board');
  assert.equal(corridorHit(f(0.1), L, W), null);
  // windows wrap round the line
  assert.equal(corridorHit(L - 1, L, { ...W, corridorGates: [0.005] }), 'gate');
  assert.equal(corridorHit(3, L, { ...W, corridorGates: [0.999] }), 'gate');
  const free = freeSpans(L, W);
  assert.ok(free.length > 5 && free.every(([a, b]) => b > a));
  for (const tier of ['high', 'mid', 'phone']) for (const seed of [1, 7, 4242]) {
    for (const x of lay(tier, seed)) assert.equal(corridorHit(x.s, L, W), null, tier + ' ' + x.k.id + ' at s ' + x.s.toFixed(0));
  }
  for (const c of layout.colossi) assert.equal(corridorHit(c.s, L, W), null, c.id + ' at s ' + c.s);
});

test('props: the scatter hangs where the chase camera looks, never empty for long', () => {
  for (const seed of [1, 7, 4242]) {
    const sc = lay('high', seed);
    const over = sc.filter((x) => x.over), side = sc.filter((x) => !x.over);
    assert.ok(over.length >= 4, 'some cross overhead: ' + over.length);
    for (const x of side) {
      const f = tr.frameAt(x.s), q = localOf(x.p, f);
      const gap = Math.abs(q.l) - f.w / 2 - x.body.r;
      assert.ok(gap >= W.scatterSideM[0] - 1e-6 && gap <= W.scatterSideM[1] + 1e-6, x.k.id + ' gap ' + gap.toFixed(1));
      assert.ok(q.h >= W.scatterUpM[0] - 1e-6 && q.h <= W.scatterUpM[1] + 1e-6, x.k.id + ' height ' + q.h.toFixed(1));
    }
    for (const x of over) {
      const q = localOf(x.p, tr.frameAt(x.s));
      assert.ok(q.h > W.scatterAirM && Math.abs(q.l) <= tr.frameAt(x.s).w, x.k.id + ' over the road');
    }
    // inside the free road, any 100 m holds a piece (the corridors carry the spirals and screens)
    const ss = sc.map((x) => x.s).sort((a, b) => a - b);
    for (const [a, b] of freeSpans(L, W)) {
      for (let s = a; s + 100 <= b; s += 20) assert.ok(ss.some((v) => v >= s && v < s + 100), 'empty stretch from s ' + s.toFixed(0));
    }
  }
});

test('props: sizes read from the road (cards 10 to 20 m, chess 12 to 30 m) and kinds mix', () => {
  const sc = lay('high', 7);
  for (const x of sc) {
    if (x.k.id.startsWith('card-')) assert.ok(x.h >= 10 && x.h <= 20, x.k.id + ' ' + x.h);
    if (x.k.id.startsWith('chess-') && !x.k.id.includes('pawn')) assert.ok(x.h >= 12 && x.h <= 30, x.k.id + ' ' + x.h);
    assert.ok(Math.abs(x.h - rowOf(x.k.id).sizeM[1] * x.scale) < 1e-6, 'scale gives the height');
  }
  const ids = new Set(sc.map((x) => x.k.id.split('-')[0]));
  for (const p of ['card', 'chess', 'pocket', 'spiral']) assert.ok(ids.has(p), 'has ' + p);
  assert.ok(sc.filter((x) => x.k.id.startsWith('chess-')).length >= 10, 'plenty of chess');
  assert.ok(sc.filter((x) => x.k.id.startsWith('card-')).length >= 12, 'plenty of cards');
});

test('props: poses show faces and tops: basis is orthonormal, cards face the oncoming camera, chess stays upright', () => {
  const near = (a, b, e = 1e-6) => Math.abs(a - b) < e;
  for (const x of lay('high', 7)) {
    if (x.pose === 'tumble') { assert.equal(scatterBasis(x, 0, W), null); continue; }
    for (const ang of [0, 1.3, 4]) {
      const b = scatterBasis(x, ang, W);
      assert.ok(near(V3.len(b.X), 1) && near(V3.len(b.Y), 1) && near(V3.len(b.Z), 1));
      assert.ok(near(V3.dot(b.X, b.Y), 0) && near(V3.dot(b.Y, b.Z), 0));
      assert.ok(V3.dot(V3.cross(b.X, b.Y), b.Z) > 0.999, 'right handed');
      // the axis never moves with the turn, so the clearance capsule holds
      assert.ok(V3.dot(b.Y, x.body.D) > 0.9999);
    }
    const f = tr.frameAt(x.s), cf = tr.frameAt(((x.s - W.scatterFaceLeadM) % L + L) % L);
    const eye = V3.add(cf.p, V3.mul(cf.n, W.scatterEyeM));
    const toCam = V3.norm(V3.sub(eye, V3.add(x.p, V3.mul(f.n, x.h / 2))));
    if (x.pose === 'face') {
      // the model front (-Z) faces the camera at rest and never turns past the swing
      const front = V3.mul(scatterBasis(x, 0, W).Z, -1);
      assert.ok(V3.dot(front, toCam) > 0.75, x.k.id + ' faces the camera ' + V3.dot(front, toCam).toFixed(2));
      const worst = V3.mul(scatterBasis(x, Math.PI / 2, W).Z, -1);
      assert.ok(V3.dot(worst, toCam) > 0.5, x.k.id + ' swings too far');
    } else {
      // upright: within 35 degrees of the road's up, leaning toward the camera, never away
      assert.ok(V3.dot(x.Y, f.n) > Math.cos(35 * Math.PI / 180), x.k.id + ' upright');
      assert.ok(V3.dot(V3.sub(x.Y, V3.mul(f.n, V3.dot(x.Y, f.n))), toCam) >= -1e-9, x.k.id + ' top leans toward the camera');
    }
  }
});

test('props: saturated colours across the wheel, each prop its own', () => {
  for (const tier of ['high', 'phone']) {
    const sc = lay(tier, 7);
    const hues = new Set();
    for (const x of sc) {
      const c = hueSat(x.colour);
      assert.ok(c.s >= 0.45 && c.v >= 0.45, x.colour + ' saturated and bright');
      hues.add(Math.floor(c.h / 45));
    }
    assert.ok(hues.size >= 4, tier + ' hue buckets ' + hues.size);
  }
  assert.equal(hexOf('#ABC'), '#aabbcc');
  assert.equal(hexOf([1, 0, 0.5]), '#ff0080');
  assert.equal(hexOf({ r: 0, g: 255, b: 0 }), '#00ff00');
  assert.equal(hexOf(0x123456), '#123456');
  assert.equal(hexOf('nope'), null);
  assert.ok(hexOf(propColour(3, 'deep')));
});

test('props: no haze inside 300 m and no low haze swallowing the props', () => {
  assert.ok(W.hazeNearM >= 300 && W.hazeFarM > W.hazeNearM);
  assert.equal(W.hazeLowMax, 0);
  assert.ok(W.hazeMax <= 0.45);
});

test('props: colossi wear distinct saturated colours and show their tops from the road', () => {
  const tints = layout.colossi.map((c) => hexOf(c.tint));
  assert.equal(new Set(tints).size, tints.length, 'distinct');
  for (const t of tints) assert.ok(hueSat(t).s >= 0.6, t);
  for (const c of layout.colossi) assert.ok(hexOf(c.seam) && hexOf(c.seam) !== hexOf(c.tint), c.id + ' seam');
  const at = (id) => placed.find((p) => p.id === id);
  // the king stands upright beside the road (polish 3 review), its crown in the sky above the eye
  const k = at('chess-king-lod0'), kf = tr.frameAt(k.spec.s), kt = localOf(k.top, kf), kb = localOf(k.B, kf);
  assert.equal(k.spec.kind, 'tower');
  assert.ok(kt.h > kb.h && kt.h > W.clearM && kt.h < 150 && Math.abs(kt.l) < 120, 'king crown ' + JSON.stringify(kt));
  assert.ok(V3.dot(k.D, kf.n) > 0.95, 'upright');
  // the knight's head rises beside the road near eye height
  const n = at('chess-knight-lod0'), nt = localOf(n.top, tr.frameAt(n.spec.s));
  assert.ok(nt.h > 10 && nt.h < 70 && Math.abs(nt.l) < 120, 'knight head ' + JSON.stringify(nt));
});

test('props: chess pieces never read as skin (no warm hues, full tint, no pawn or bishop shafts)', async () => {
  const { isWarmHue, chessColour } = await import('../render/colossiMath.js');
  assert.ok(!W.scatterKinds.some((k) => /pawn|bishop/.test(k.id)), 'no pawn or bishop in the scatter');
  assert.equal(W.tintMix, 1);
  for (const tier of ['high', 'mid', 'phone']) {
    for (const x of lay(tier, 7)) if (x.k.id.startsWith('chess-')) assert.ok(!isWarmHue(x.colour, W), x.k.id + ' ' + x.colour);
  }
  for (const c of layout.colossi) if (c.id.startsWith('chess-')) assert.ok(!isWarmHue(c.tint, W), c.id + ' ' + c.tint);
  for (const sec of ['dawn', 'deep', 'afterglow']) for (let i = 0; i < 40; i++) assert.ok(!isWarmHue(chessColour(i, sec, W), W));
  for (const warm of ['#ff7a1a', '#ff3040', '#ffc928', '#ff1a8c', '#d2a679']) assert.ok(isWarmHue(warm, W), warm);
  for (const cool of ['#19e3ff', '#2f6bff', '#8a3dff', '#ff1ff0', '#7dff3a']) assert.ok(!isWarmHue(cool, W), cool);
});
