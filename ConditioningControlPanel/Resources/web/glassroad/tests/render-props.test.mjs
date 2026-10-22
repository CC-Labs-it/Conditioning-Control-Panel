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

// ---- polish 4: the hero set (owner "I still cannot see any 3d model") ----
import { placeHeroes, heroCount, heroBasis, heroAvoidHit, bodyClear, capsulesApart, isWarmHue, heroCasters, shadeRanges } from '../render/colossiMath.js';
import { LOOK } from '../render/look.js';
const heroKinds = W.heroKinds.filter((k) => rowOf(k.id));
const heroes = (tier, seed = 7) => placeHeroes(tr, heroKinds, rowOf, seed, { want: heroCount(tier, W), avoid: placed, W, samples, sectionOf });
const KINDS = ['king', 'queen', 'knight', 'rook', 'bishop', 'pawn', 'slot', 'card', 'watch', 'disc'];
const kindOf = (id) => KINDS.find((k) => id.includes(k));

test('heroes: every tier fills its count and the high tier shows every prop kind', () => {
  // COLOSSAL (owner 21:40): fewer but 4 to 5 times bigger
  assert.deepEqual([heroCount('high', W), heroCount('mid', W), heroCount('phone', W)], [16, 10, 6]);
  for (const tier of ['high', 'mid', 'phone']) for (const seed of [1, 7, 4242]) {
    const h = heroes(tier, seed);
    assert.ok(h.length >= heroCount(tier, W) - 1, tier + ' seed ' + seed + ' placed ' + h.length);
  }
  for (const seed of [1, 7, 4242]) {
    const seen = new Set(heroes('high', seed).map((x) => kindOf(x.k.id)));
    for (const k of KINDS) assert.ok(seen.has(k), 'seed ' + seed + ' shows a ' + k);
  }
  const phone = new Set(heroes('phone', 7).map((x) => kindOf(x.k.id)));
  assert.ok(phone.size >= 5, 'phone kinds ' + phone.size);
});

test('heroes: colossal (30 to 55 m tall, 15 to 90 m off the edge, rising past the road, a few leaning toward it)', () => {
  for (const seed of [1, 7, 4242]) {
    const h = heroes('high', seed);
    let leaning = 0;
    for (const x of h) {
      const f = tr.frameAt(x.s), q = localOf(x.p, f);
      const gap = Math.abs(q.l) - f.w / 2 - x.plinth.r;
      assert.ok(gap >= 15 - 1e-6 && gap <= 90 + 1e-6, x.k.id + ' gap ' + gap.toFixed(1));
      assert.ok(q.h >= W.heroLowUpM[0] - 1e-6 && q.h <= W.heroUpM[1] + 1e-6, x.k.id + ' base ' + q.h.toFixed(1));
      assert.ok(x.h >= 30 && x.h <= 55, x.k.id + ' height ' + x.h.toFixed(1));
      assert.ok(Math.abs(x.h - rowOf(x.k.id).sizeM[1] * x.scale) < 1e-6);
      // it towers: the top stands at least 20 m over the road
      const top = localOf(V3.add(x.p, V3.mul(x.Y, x.h)), f);
      assert.ok(top.h >= 20, x.k.id + ' top ' + top.h.toFixed(1));
      const lean = Math.acos(Math.min(1, V3.dot(x.Y, x.Y0))) * 180 / Math.PI;
      assert.ok(lean <= W.heroLeanDeg[1] + 1e-6, x.k.id + ' lean ' + lean.toFixed(1));
      if (lean > 10) {
        leaning++;
        assert.ok(x.bare, 'a leaning hero rises bare, without a plinth');
        // it leans toward the road: seen from above, the top sits nearer the road than the base (a banked
        // frame's own lateral axis tilts, so this is measured level)
        const hd = (P) => Math.hypot(P[0] - f.p[0], P[2] - f.p[2]);
        assert.ok(hd(V3.add(x.p, V3.mul(x.Y, x.h))) < hd(x.p) - 3, x.k.id + ' leans toward the road');
      }
    }
    assert.ok(leaning >= 2, 'seed ' + seed + ' leaning ' + leaning);
    assert.ok(h.filter((x) => x.up < -5).length >= 2, 'some stand far below the road');
  }
  // the ink outline keeps its pixel width out to 900 m (world width = distance times heroOutlineK)
  assert.ok(W.heroOutlineM[1] / W.heroOutlineK >= 900 && W.heroOutlineM[0] / W.heroOutlineK <= 20);
});

test('heroes: the nearest few whose shadow reaches the road cast it (high tier, at most heroCastN)', () => {
  let any = 0;
  for (const seed of [1, 7, 4242]) {
    const h = heroes('high', seed), c = heroCasters(h, LOOK.lightDir, samples, W);
    assert.ok(c.length <= W.heroCastN);
    any += c.length;
    for (let j = 0; j < c.length; j++) {
      const x = h[c[j]];
      const r = shadeRanges({ B: x.p, top: V3.add(x.p, V3.mul(x.Y, x.h)), r: x.rLat * W.heroCastR }, LOOK.lightDir, samples);
      assert.ok(r.reduce((a, [p, q]) => a + q - p, 0) >= W.heroCastMinM, x.k.id + ' shades the road');
      if (j > 0) assert.ok(h[c[j - 1]].gap <= x.gap, 'nearest first');
    }
  }
  assert.ok(any >= 3, 'casters over three seeds ' + any);
});

test('heroes: clear of every stretch, the corridors, the billboards, the drop shortcut, colossi, each other and the scatter', () => {
  const fine = sampleTrack(tr, 2);
  const Wc = { ...W, stepM: 2, axisStepM: 2, sideM: W.heroMarginM, underM: W.heroMarginM, clearM: W.heroAirM };
  for (const tier of ['high', 'phone']) for (const seed of [1, 7, 4242]) {
    const h = heroes(tier, seed);
    for (const x of h) {
      assert.ok(bodyClear(x.body, fine, Wc).ok, x.k.id + ' at s ' + x.s.toFixed(0) + ' clips a road stretch');
      assert.equal(corridorHit(x.s, L, W), null, x.k.id + ' in a corridor at s ' + x.s.toFixed(0));
      assert.ok(!heroAvoidHit(x.s, L, W), x.k.id + ' in an avoid window at s ' + x.s.toFixed(0));
      // a near hero keeps its whole reach off the gates, the media tunnel and the shortcut windows
      if (x.gap < W.heroFarGapM) {
        assert.ok(['board', null].includes(corridorHit(x.s, L, W, x.plinth.r + 2)), x.k.id + ' reach in a corridor at s ' + x.s.toFixed(0));
        assert.ok(!heroAvoidHit(x.s, L, W, x.plinth.r + 2), x.k.id + ' reach in an avoid window');
      }
      for (const b of W.corridorBoards) {
        const d = Math.abs(((x.s - b * L) % L + L * 1.5) % L - L / 2);
        assert.ok(d >= W.corridorBoardHalfM + W.heroBoardPadM, x.k.id + ' too near the billboard at f ' + b);
      }
      for (const c of placed) assert.ok(capsulesApart(x.body, c, W.heroMarginM), x.k.id + ' touches ' + c.id);
    }
    for (let i = 0; i < h.length; i++) for (let j = i + 1; j < h.length; j++) assert.ok(capsulesApart(h[i].body, h[j].body, W.heroGapBetweenM));
    // the scatter keeps clear of the heroes (buildScatter passes their bodies as avoid)
    const sc = placeScatter(tr, kinds, rowOf, seed, { want: scatterCount(tier, W), avoid: [...placed, ...h.map((x) => x.body)], W, samples, sectionOf });
    for (const s of sc) for (const x of h) assert.ok(capsulesApart(s.body, x.body, W.scatterMarginM - 1e-6), s.k.id + ' over a hero');
    assert.ok(sc.length >= Math.floor(scatterCount(tier, W) * 0.85), 'scatter still fills: ' + sc.length);
  }
  // the drop shortcut take-off (helix top) and landing (lower straight) stay open
  for (const s of [520, 560, 600, 820, 860, 900]) assert.ok(heroAvoidHit(s, L, W), 'avoid ' + s);
  assert.ok(heroAvoidHit(L - 5, L, W) && heroAvoidHit(5, L, W), 'the finish arch');
});

test('heroes: chess in pink and white silicone, chrome or violet (never warm flesh); upright and facing poses', () => {
  const flesh = (hex) => { const c = hueSat(hex); return c.h >= 10 && c.h <= 50 && c.s >= 0.12 && c.s <= 0.7; };
  for (const sk of W.heroChessSkins) { assert.ok(!flesh(sk.body), sk.body); assert.ok(!flesh(sk.rim), sk.rim); }
  const chess = heroes('high', 7).filter((x) => x.k.look === 'chess');
  const pw = chess.filter((x) => ['#f3eeff', '#ff3fb8'].includes(x.look.body)).length;
  assert.ok(pw / chess.length >= 0.6, 'pink and white lead: ' + pw + ' of ' + chess.length);
  for (const x of chess) assert.ok(!isWarmHue(x.look.body, W) || x.look.body === '#ff3fb8' || hueSat(x.look.body).s < 0.25, x.look.body);
  assert.ok(!W.heroChessSkins.some((sk) => sk.chrome), 'no chrome skin (it mirrored the warm sky into a peach)');
  const near = (a, b, e = 1e-6) => Math.abs(a - b) < e;
  for (const x of heroes('high', 7)) {
    const f = tr.frameAt(x.s);
    assert.ok(x.Y0[1] > Math.cos(15 * Math.PI / 180), x.k.id + ' plinth stands upright, at most gently tilted');
    if (!x.bare) assert.ok(V3.dot(x.Y, x.Y0) > 1 - 1e-9, x.k.id + ' on a plinth stands straight on it');
    for (const ang of [0, 1.1, 3]) {
      const b = heroBasis(x, ang, W);
      assert.ok(near(V3.len(b.X), 1) && near(V3.dot(b.X, b.Y), 0) && near(V3.dot(b.Y, b.Z), 0));
      assert.ok(V3.dot(V3.cross(b.X, b.Y), b.Z) > 0.999);
    }
    if (x.pose === 'face') {
      const cf = tr.frameAt(((x.s - W.heroFaceLeadM) % L + L) % L);
      const to = V3.norm(V3.sub(V3.add(cf.p, V3.mul(cf.n, W.heroEyeM)), V3.add(x.p, V3.mul(x.Y, x.h / 2))));
      assert.ok(V3.dot(V3.mul(heroBasis(x, 0, W).Z, -1), to) > 0.8, x.k.id + ' faces the oncoming kart');
    }
  }
});

test('sky: the clean gradient has no muddy band (no red, orange, yellow or green stop)', async () => {
  const src = readFileSync(new URL('../render/materials/sky.js', import.meta.url), 'utf8');
  const m = src.match(/cleanStops: Object\.freeze\(\[([^\]]+)\]\)/);
  assert.ok(m, 'cleanStops');
  const stops = m[1].match(/#[0-9a-fA-F]{6}/g);
  assert.ok(stops.length >= 4);
  for (const s of stops) {
    const c = hueSat(s.toLowerCase());
    assert.ok(c.h >= 180 && c.h <= 340, s + ' hue ' + c.h.toFixed(0));
    assert.ok(c.s >= 0.6 && c.v >= 0.8, s + ' clean and bright');
  }
  assert.ok(!/ramp\(nx\)\.mul\(cloud\.mul\(0\.6\)/.test(src), 'the old full-spectrum blotch field is gone');
});
